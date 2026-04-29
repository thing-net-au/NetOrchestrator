using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Scheduler
{
    /// <summary>
    /// Background service that enforces scheduling policies for all configured services
    /// and reports current statuses via the log stream.
    /// </summary>
    public class PolicyScheduler : BackgroundService, IInternalHealth
    {
        private readonly IProcessSupervisor _supervisor;
        private readonly ILogStreamService _log;
        private readonly IOptions<OrchestratorConfig> _config;
        private readonly ILogger<PolicyScheduler> _logger;

        private DateTime _lastRun = DateTime.MinValue;
        private readonly object _lastRunLock = new();

        // CPU tracking: PID -> (lastCpuTime, lastMeasured)
        private readonly ConcurrentDictionary<int, (TimeSpan CpuTime, DateTime Measured)> _cpuSnapshots = new();
        // Cron last-fire tracking: service name -> last fire time
        private readonly ConcurrentDictionary<string, DateTime> _cronLastFired = new();

        public InternalStatus GetStatus()
        {
            var cfg = _config.Value;
            var staleness = TimeSpan.FromMilliseconds(cfg.Global.HealthCheckInterval * 2);
            DateTime lastRun;
            lock (_lastRunLock) { lastRun = _lastRun; }
            var isHealthy = lastRun == DateTime.MinValue || (DateTime.UtcNow - lastRun) < staleness;
            return new InternalStatus
            {
                Name = nameof(PolicyScheduler),
                IsHealthy = isHealthy,
                Details = $"Last run at {lastRun:O}"
            };
        }

        public PolicyScheduler(
            IProcessSupervisor supervisor,
            ILogStreamService logStream,
            IOptions<OrchestratorConfig> config,
            ILogger<PolicyScheduler> logger)
        {
            _supervisor = supervisor;
            _log = logStream;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMilliseconds(_config.Value.Global.HealthCheckInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var statuses = (await _supervisor.ListStatusAsync()).ToList();

                    // Report each service status via log stream
                    foreach (var status in statuses)
                        _log.Push("ServiceStatus", JsonSerializer.Serialize(status));

                    foreach (var svcConfig in _config.Value.Services.Values)
                    {
                        var status = statuses.FirstOrDefault(s => s.Name == svcConfig.Name);
                        int running = status?.RunningInstances ?? 0;

                        switch (svcConfig.SchedulePolicy.Type.ToLowerInvariant())
                        {
                            case "steady":
                                await ApplySteadyPolicy(svcConfig, running);
                                break;

                            case "demand":
                                await ApplyDemandPolicy(svcConfig, status, running);
                                break;

                            case "cron":
                                await ApplyCronPolicy(svcConfig, running, interval);
                                break;

                            default:
                                _logger.LogWarning("Unknown scheduling policy '{Policy}' for service '{Service}'.",
                                    svcConfig.SchedulePolicy.Type, svcConfig.Name);
                                break;
                        }
                    }

                    lock (_lastRunLock) { _lastRun = DateTime.UtcNow; }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error in PolicyScheduler loop.");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Expected on graceful shutdown — exit the loop cleanly
                    break;
                }
            }
        }

        private async Task ApplySteadyPolicy(ServiceConfig svcConfig, int running)
        {
            if (running < svcConfig.MinInstances)
                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
            else if (running > svcConfig.MaxInstances)
                await _supervisor.StopAsync(svcConfig.Name, running - svcConfig.MaxInstances);
        }

        private async Task ApplyDemandPolicy(ServiceConfig svcConfig, ServiceStatus? status, int running)
        {
            // Ensure minimum instances are running
            if (running < svcConfig.MinInstances)
            {
                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
                return;
            }

            int threshold = svcConfig.SchedulePolicy.Threshold ?? _config.Value.Scheduling.DemandThreshold;
            double avgCpu = ComputeAverageCpuPercent(status?.ProcessIds ?? Array.Empty<int>());

            _logger.LogDebug("Demand policy for {Service}: avgCpu={AvgCpu:F1}%, threshold={Threshold}%, running={Running}.",
                svcConfig.Name, avgCpu, threshold, running);

            // Scale up when avgCPU exceeds threshold
            if (avgCpu > threshold && running < svcConfig.MaxInstances)
                await _supervisor.StartAsync(svcConfig.Name, 1);
            // Scale down when avgCPU drops below threshold × DemandScaleDownRatio (hysteresis band)
            else if (avgCpu < threshold * _config.Value.Global.DemandScaleDownRatio
                     && running > svcConfig.MinInstances)
                await _supervisor.StopAsync(svcConfig.Name, 1);
        }

        private async Task ApplyCronPolicy(ServiceConfig svcConfig, int running, TimeSpan interval)
        {
            if (string.IsNullOrWhiteSpace(svcConfig.SchedulePolicy.Cron))
            {
                _logger.LogWarning("Service '{Service}' uses cron policy but has no Cron expression.", svcConfig.Name);
                return;
            }

            CronExpression expr;
            try { expr = CronExpression.Parse(svcConfig.SchedulePolicy.Cron); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid cron expression '{Cron}' for service '{Service}'.",
                    svcConfig.SchedulePolicy.Cron, svcConfig.Name);
                return;
            }

            var lastFired = _cronLastFired.GetValueOrDefault(svcConfig.Name, DateTime.MinValue);
            var windowStart = lastFired == DateTime.MinValue
                ? DateTime.UtcNow.Subtract(interval)
                : lastFired;

            var next = expr.GetNextOccurrence(windowStart, TimeZoneInfo.Utc);
            if (next.HasValue && next.Value <= DateTime.UtcNow)
            {
                _cronLastFired[svcConfig.Name] = next.Value;
                int target = svcConfig.MinInstances;
                if (running < target)
                    await _supervisor.StartAsync(svcConfig.Name, target - running);
                else if (running > target)
                    await _supervisor.StopAsync(svcConfig.Name, running - target);
            }
        }

        private double ComputeAverageCpuPercent(System.Collections.Generic.IReadOnlyList<int> processIds)
        {
            if (processIds.Count == 0) return 0;

            double total = 0;
            int count = 0;
            var now = DateTime.UtcNow;

            foreach (var pid in processIds)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    var currentCpu = proc.TotalProcessorTime;

                    if (_cpuSnapshots.TryGetValue(pid, out var prev))
                    {
                        var elapsed = (now - prev.Measured).TotalSeconds;
                        if (elapsed > 0)
                        {
                            var cpuFraction = (currentCpu - prev.CpuTime).TotalSeconds
                                             / elapsed
                                             / Environment.ProcessorCount;
                            total += cpuFraction * 100.0;
                            count++;
                        }
                    }

                    _cpuSnapshots[pid] = (currentCpu, now);
                }
                catch { /* process may have exited */ }
            }

            // Clean up snapshots for PIDs no longer in use
            foreach (var key in _cpuSnapshots.Keys.Where(k => !processIds.Contains(k)).ToList())
                _cpuSnapshots.TryRemove(key, out _);

            return count > 0 ? total / count : 0;
        }
    }
}

