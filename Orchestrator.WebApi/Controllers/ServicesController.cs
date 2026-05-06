using Microsoft.AspNetCore.Mvc;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;

namespace Orchestrator.WebApi.Controllers
{
    [ApiController]
    [Route("api/services")]
    public class ServicesController : ControllerBase
    {
        private readonly IProcessSupervisor _supervisor;

        public ServicesController(IProcessSupervisor supervisor)
        {
            _supervisor = supervisor;
        }

        [HttpGet]
        public async Task<IEnumerable<ServiceStatus>> GetAll()
        {
            return await _supervisor.ListStatusAsync();
        }

        [HttpGet("{name}/status")]
        public IEnumerable<WorkerStatus> GetWorkerStatuses(string name)
        {
            var ipc = HttpContext.RequestServices.GetRequiredService<IIpcServer>() as IpcServer;
            return ipc?.GetLatestStatuses(name) ?? Enumerable.Empty<WorkerStatus>();
        }

        [HttpPost("{name}/start")]
        public async Task<IActionResult> Start(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest("Service name is required.");
            }

            await _supervisor.StartAsync(name);
            return Accepted();
        }

        [HttpPost("{name}/stop")]
        public async Task<IActionResult> Stop(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest("Service name is required.");
            }

            await _supervisor.StopAsync(name);
            return Accepted();
        }

        [HttpPost("report")]
        public async Task<IActionResult> Report([FromBody] WorkerStatus status)
        {
            if (status == null || string.IsNullOrWhiteSpace(status.ServiceName))
            {
                return BadRequest("Valid worker status payload is required.");
            }

            var ipc = HttpContext.RequestServices.GetRequiredService<IIpcServer>();
            await ipc.ReportStatus(status);
            return Accepted();
        }

        [HttpGet("internal")]
        public IEnumerable<InternalStatus> GetInternalStatuses()
        {
            var ipc = HttpContext.RequestServices.GetRequiredService<IIpcServer>() as IpcServer;
            return ipc?.GetInternalStatuses() ?? Enumerable.Empty<InternalStatus>();
        }
    }
}
