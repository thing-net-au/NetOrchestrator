// Project: Orchestrator.Scheduler (Class Library)
// References: Orchestrator.Core, Orchestrator.Supervisor, Microsoft.Extensions.Hosting

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Scheduler
{
    /// <summary>
    /// Background service that enforces scheduling policies for all configured services.
    /// </summary>
    public class PolicyScheduler : BackgroundService
    {
        private readonly IProcessSupervisor _supervisor;

        public PolicyScheduler(IProcessSupervisor supervisor)
        {
            _supervisor = supervisor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMilliseconds(OrchestratorConfig.Current.Global.HealthCheckInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Fetch current statuses
                var statuses = (await _supervisor.ListStatusAsync()).ToList();

                foreach (var svcConfig in OrchestratorConfig.Current.Services.Values)
                {
                    var status = statuses.FirstOrDefault(s => s.Name == svcConfig.Name);
                    int running = status?.RunningInstances ?? 0;

                    switch (svcConfig.SchedulePolicy.Type.ToLowerInvariant())
                    {
                        case "steady":
                            // Ensure min <= running <= max
                            if (running < svcConfig.MinInstances)
                                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
                            else if (running > svcConfig.MaxInstances)
                                await _supervisor.StopAsync(svcConfig.Name, running - svcConfig.MaxInstances);
                            break;

                        case "demand":
                            // Example: scale up if CPU% > threshold (stub)
                            int threshold = svcConfig.SchedulePolicy.Threshold ?? OrchestratorConfig.Current.Scheduling.DemandThreshold;
                            // TODO: measure actual CPU usage per service
                            // For now, enforce only min/max
                            if (running < svcConfig.MinInstances)
                                await _supervisor.StartAsync(svcConfig.Name, svcConfig.MinInstances - running);
                            else if (running > svcConfig.MaxInstances)
                                await _supervisor.StopAsync(svcConfig.Name, running - svcConfig.MaxInstances);
                            break;

                        case "cron":
                            // TODO: evaluate CRON expression and schedule accordingly
                            break;

                        default:
                            // Fallback to default policy
                            break;
                    }
                }

                // Wait before next evaluation
                await Task.Delay(interval, stoppingToken);
            }
        }
    }
}
