using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly IIpcServer _ipc;

        public ServicesController(IProcessSupervisor supervisor, IIpcServer ipc)
        {
            _supervisor = supervisor;
            _ipc = ipc;
        }

        // GET /api/services
        [HttpGet]
        public async Task<IEnumerable<ServiceStatus>> GetAll()
            => await _supervisor.ListStatusAsync();

        // GET /api/services/{name}/status
        [HttpGet("{name}/status")]
        public IEnumerable<WorkerStatus> GetWorkerStatuses(string name)
            => _ipc.GetLatestStatuses(name);

        // POST /api/services/{name}/start
        [HttpPost("{name}/start")]
        public Task Start(string name)
            => _supervisor.StartAsync(name);

        // POST /api/services/{name}/stop
        [HttpPost("{name}/stop")]
        public Task Stop(string name)
            => _supervisor.StopAsync(name);

        // POST /api/services/report
        [HttpPost("report")]
        public async Task<IActionResult> Report([FromBody] WorkerStatus status)
        {
            if (status == null || string.IsNullOrWhiteSpace(status.ServiceName))
                return BadRequest("Valid worker status payload is required.");

            await _ipc.ReportStatus(status);
            return Accepted();
        }

        // GET /api/services/internal
        [HttpGet("internal")]
        public IEnumerable<InternalStatus> GetInternalStatuses()
            => _ipc.GetInternalStatuses();
    }
}
