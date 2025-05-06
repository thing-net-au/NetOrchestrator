using Microsoft.AspNetCore.Mvc;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.WebApi.Controllers
{
    [ApiController]
    [Route("api/services")]
    public class ServicesController : ControllerBase
    {
        private readonly IProcessSupervisor _supervisor;

        public ServicesController(IProcessSupervisor supervisor)
            => _supervisor = supervisor;

        // GET /api/services
        [HttpGet]
        public Task<IEnumerable<ServiceStatus>> GetAll()
            => _supervisor.ListStatusAsync();

        // POST /api/services/{name}/start
        [HttpPost("{name}/start")]
        public Task Start(string name)
            => _supervisor.StartAsync(name);

        // POST /api/services/{name}/stop
        [HttpPost("{name}/stop")]
        public Task Stop(string name)
            => _supervisor.StopAsync(name);
    }
}
