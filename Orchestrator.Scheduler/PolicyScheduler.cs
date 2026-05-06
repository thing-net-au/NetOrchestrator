using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<PolicyScheduler> _logger;
        private DateTime _lastRun = DateTime.MinValue;

        public InternalStatus GetStatus()
        {
            var intervalMs = OrchestratorConfig.Current.Global.HealthCheckInterval;
            var maxAge = TimeSpan.FromMilliseconds(intervalMs * 2L);
            var age = DateTime.UtcNow - _lastRun;

            return new InternalStatus
            {
                Name = nameof(PolicyScheduler),
                IsHealthy = _lastRun != DateTime.MinValue && age <= maxAge,
                Details = $"Last run at {_lastRun:O}, age={age.TotalSeconds:n1}s"
            };
        }

        public PolicyScheduler(
            IProcessSupervisor supervisor,
            ILogStreamService logStream,
            ILogger<PolicyScheduler> logger)
        {
            _supervisor = supervisor;
            _log = logStream;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMilliseconds(OrchestratorConfig.Current.Global.HealthCheckInterval);
            _logger.LogInformation("Policy scheduler started with interval {IntervalMs}ms.", interval.TotalMilliseconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                _lastRun = DateTime.UtcNow;
                var statuses = (await _supervisor.ListStatusAsync()).ToList();

                foreach (var status in statuses)
                {
                    _log.Push("ServiceStatus", JsonSerializer.Serialize(status));
                }

                foreach (var svcConfig in OrchestratorConfig.Current.Services.Values)
                {
                    var status = statuses.FirstOrDefault(s => s.Name == svcConfig.Name);
                    var running = status?.RunningInstances ?? 0;

                    switch (svcConfig.SchedulePolicy.Type.ToLowerInvariant())
                    {
                        case "steady":
                            if (running < svcConfig.MinInstances)
                                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
                            else if (running > svcConfig.MaxInstances)
                                await _supervisor.StopAsync(svcConfig.Name, running - svcConfig.MaxInstances);
                            break;

                        case "demand":
                            var threshold = svcConfig.SchedulePolicy.Threshold ?? OrchestratorConfig.Current.Scheduling.DemandThreshold;
                            _logger.LogDebug("Demand policy for {ServiceName} using threshold {Threshold}.", svcConfig.Name, threshold);

                            if (running < svcConfig.MinInstances)
                                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
                            else if (running > svcConfig.MaxInstances)
                                await _supervisor.StopAsync(svcConfig.Name, running - svcConfig.MaxInstances);
                            break;

                        case "cron":
                            _logger.LogDebug("Cron policy for {ServiceName} is not implemented yet.", svcConfig.Name);
                            break;

                        default:
                            _logger.LogWarning("Unknown policy type {PolicyType} for service {ServiceName}.", svcConfig.SchedulePolicy.Type, svcConfig.Name);
                            break;
                    }
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
    }
}
