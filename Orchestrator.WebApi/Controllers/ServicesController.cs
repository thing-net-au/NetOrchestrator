using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.WebApi.Controllers
{
    [ApiController]
    [Route("api/services")]
    public class ServicesController : ControllerBase
    {
        private readonly IProcessSupervisor _supervisor;
        private readonly IEnumerable<IInternalHealth> _healthProviders;
        private readonly IConfiguration _configuration;
        private readonly OrchestratorConfig _orchestratorConfig;

        public ServicesController(
            IConfiguration configuration,
            IProcessSupervisor supervisor,
            IEnumerable<IInternalHealth> healthProviders)
        {
            _supervisor = supervisor;
            _healthProviders = healthProviders;
            _configuration = configuration;
            _orchestratorConfig = OrchestratorConfig.Current!;
        }

        /// <summary>
        /// GET /api/services
        /// List configured services with their current running/stopped state.
        /// </summary>
        [HttpGet]
        public Task<IEnumerable<ServiceStatus>> GetAll()
        {
                  return _supervisor.ListStatusAsync();
        }   

        /// <summary>
        /// POST /api/services/{name}/start
        /// Start one instance of the named service.
        /// </summary>
        [HttpPost("{name}/start")]
        public Task Start(string name)
            => _supervisor.StartAsync(name);

        /// <summary>
        /// POST /api/services/{name}/stop
        /// Stop one instance of the named service.
        /// </summary>
        [HttpPost("{name}/stop")]
        public Task Stop(string name)
            => _supervisor.StopAsync(name);

        /// <summary>
        /// GET /api/services/internal
        /// Snapshot of all internal health providers.
        /// (For real‐time updates, use SSE on /api/status/stream.)
        /// </summary>
        [HttpGet("internal")]
        public IEnumerable<InternalStatus> GetInternalStatuses()
            => _healthProviders.Select(h => h.GetStatus());
    }
}
