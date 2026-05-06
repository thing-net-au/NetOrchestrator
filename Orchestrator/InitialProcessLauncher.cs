using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;

namespace Orchestrator
{
    /// <summary>
    /// On startup, launches one instance of each configured service in dependency order.
    /// The PolicyScheduler keeps them within their configured min/max bounds.
    /// </summary>
    internal class InitialProcessLauncher : IHostedService
    {
        private readonly IProcessSupervisor _supervisor;
        private readonly IOptions<OrchestratorConfig> _config;
        private readonly ILogger<InitialProcessLauncher> _logger;

        public InitialProcessLauncher(
            IProcessSupervisor supervisor,
            IOptions<OrchestratorConfig> config,
            ILogger<InitialProcessLauncher> logger)
        {
            _supervisor = supervisor;
            _config = config;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            var services = _config.Value.Services;

            // Build dependency graph: service name -> its declared dependencies
            var graph = services.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Dependencies ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<string> order;
            try
            {
                order = TopologicalSort.Sort(graph);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogCritical(ex, "Cannot launch services: {Message}", ex.Message);
                return;
            }

            _logger.LogInformation("Service startup order: {Order}", string.Join(" -> ", order));

            foreach (var name in order)
            {
                if (!services.TryGetValue(name, out var svc)) continue;
                _logger.LogInformation("Launching {Count} instance(s) of service '{Name}'.",
                    svc.MinInstances, name);
                await _supervisor.StartAsync(svc.Name, svc.MinInstances);
            }
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
