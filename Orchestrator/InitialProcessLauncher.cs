using Orchestrator.Core.Interfaces;
using Orchestrator.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator
{
    /// <summary>
    /// On startup, launch one instance of each configured service.
    /// The PolicyScheduler will keep them within their min/max.
    /// </summary>
    internal class InitialProcessLauncher : IHostedService
    {
        private readonly IProcessSupervisor _supervisor;
        private readonly OrchestratorConfig _cfg;

        public InitialProcessLauncher(
            IProcessSupervisor supervisor,
            IConfigurationLoader loader)
        {
            _supervisor = supervisor;
            _cfg = (OrchestratorConfig)loader;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            foreach (var svc in  OrchestratorConfig.Current.Services.Values)
            {
                // start up to MinInstances
                await _supervisor.StartAsync(svc.Name, svc.MinInstances);
            }
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

}
