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
            => _supervisor = supervisor;

        // GET /api/services
        [HttpGet]
        public async Task<IEnumerable<ServiceStatus>> GetAll()
        {
            return await _supervisor.ListStatusAsync();
        }

        // In ServicesController.cs
        [HttpGet("{name}/status")]
        public IEnumerable<WorkerStatus> GetWorkerStatuses(string name)
        {
            var ipc = HttpContext.RequestServices.GetRequiredService<IIpcServer>() as IpcServer;
            return ipc?.GetLatestStatuses(name) ?? Enumerable.Empty<WorkerStatus>();
        }

        // POST /api/services/{name}/start
        [HttpPost("{name}/start")]
        public Task Start(string name)
            => _supervisor.StartAsync(name);

        // POST /api/services/{name}/stop
        [HttpPost("{name}/stop")]
        public Task Stop(string name)
            => _supervisor.StopAsync(name);
        // in ServicesController.cs

        [HttpPost("report")]
        public async Task Report([FromBody] WorkerStatus status)
        {
            var ipc = HttpContext.RequestServices.GetRequiredService<IIpcServer>();
            await ipc.ReportStatus(status);
        }

        // existing code…

        // GET /api/services/internal
        [HttpGet("internal")]
        public IEnumerable<InternalStatus> GetInternalStatuses()
        {
            var ipc = HttpContext.RequestServices.GetRequiredService<IIpcServer>() as IpcServer;
            return ipc?.GetInternalStatuses() ?? Enumerable.Empty<InternalStatus>();

            var all = HttpContext.RequestServices.GetServices<IInternalHealth>();
            return all.Select(s => s.GetStatus());
        }

    }

}
