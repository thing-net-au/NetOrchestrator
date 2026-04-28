using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.IPC
{
    public class IpcServer : IIpcServer
    {
        private readonly IProcessSupervisor _supervisor;
        private readonly IEnumerable<IInternalHealth> _internalHealthProviders;
        private readonly IOptions<OrchestratorConfig> _config;

        // Store the last report for each (service, pid)
        private readonly ConcurrentDictionary<(string Service, int Pid), WorkerStatus> _statuses = new();

        public IpcServer(
            IProcessSupervisor supervisor,
            IEnumerable<IInternalHealth> internalHealthProviders,
            IOptions<OrchestratorConfig> config)
        {
            _supervisor = supervisor;
            _internalHealthProviders = internalHealthProviders;
            _config = config;
        }

        /// <inheritdoc/>
        public Task RequestNeighborExecution(string serviceName)
            => _supervisor.StartAsync(serviceName);

        /// <inheritdoc/>
        public Task ReportStatus(WorkerStatus status)
        {
            _statuses[(status.ServiceName, status.ProcessId)] = status;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public IEnumerable<WorkerStatus> GetLatestStatuses(string serviceName)
            => _statuses
                .Where(kv => kv.Key.Service == serviceName)
                .Select(kv => kv.Value);

        /// <inheritdoc/>
        public IEnumerable<InternalStatus> GetInternalStatuses()
            => _internalHealthProviders.Select(p => p.GetStatus());
    }
}

