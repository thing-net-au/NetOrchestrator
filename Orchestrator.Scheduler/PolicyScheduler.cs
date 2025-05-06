// Project: Orchestrator.Scheduler (Class Library)
// References: Orchestrator.Core, Orchestrator.Supervisor, Microsoft.Extensions.Hosting, System.Text.Json

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Text.Json;

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
        private DateTime _lastRun;

        public InternalStatus GetStatus() => new InternalStatus
        {
            Name = nameof(PolicyScheduler),
            IsHealthy = true,  // you could check if _lastRun is within twice the interval
            Details = $"Last run at {_lastRun:O}"
        };


        public PolicyScheduler(
            IProcessSupervisor supervisor,
            ILogStreamService logStream)
        {
            _supervisor = supervisor;
            _log = logStream;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMilliseconds(OrchestratorConfig.Current.Global.HealthCheckInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Fetch current statuses
                var statuses = (await _supervisor.ListStatusAsync()).ToList();

                // Report each service status via log stream
                foreach (var status in statuses)
                {
                    var payload = JsonSerializer.Serialize(status);
                    _log.Push("ServiceStatus", payload);
                }

                foreach (var svcConfig in OrchestratorConfig.Current.Services.Values)
                {
                    var status = statuses.FirstOrDefault(s => s.Name == svcConfig.Name);
                    int running = status?.RunningInstances ?? 0;

                    switch (svcConfig.SchedulePolicy.Type.ToLowerInvariant())
                    {
                        case "steady":
                            if (running < svcConfig.MinInstances)
                                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
                            else if (running > svcConfig.MaxInstances)
                                await _supervisor.StopAsync(svcConfig.Name, running - svcConfig.MaxInstances);
                            break;

                        case "demand":
                            int threshold = svcConfig.SchedulePolicy.Threshold ?? OrchestratorConfig.Current.Scheduling.DemandThreshold;
                            // TODO: integrate actual metric checks
                            if (running < svcConfig.MinInstances)
                                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
                            else if (running > svcConfig.MaxInstances)
                                await _supervisor.StopAsync(svcConfig.Name, running - svcConfig.MaxInstances);
                            break;

                        case "cron":
                            // TODO: evaluate CRON and schedule accordingly
                            break;

                        default:
                            // fallback
                            break;
                    }
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
    }
}
