using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orchestrator.Core.Interfaces;       // IIpcServer, IInternalHealth
using Orchestrator.Core.Models;
using Orchestrator.Supervisor;            // IProcessSupervisor
using Microsoft.Extensions.DependencyInjection;
using Orchestrator.Core;
using System.Net.Http;

namespace Orchestrator.IPC
{
    public class IpcServer : IIpcServer
    {
        private readonly IProcessSupervisor _supervisor;
        private readonly IEnumerable<IInternalHealth> _internalHealthProviders;

        // store the last report for each (service, pid)
        private readonly ConcurrentDictionary<(string Service, int Pid), WorkerStatus> _statuses
            = new();

        public IpcServer(
            IProcessSupervisor supervisor,
            IEnumerable<IInternalHealth> internalHealthProviders)
        {
            _supervisor = supervisor;
            _internalHealthProviders = internalHealthProviders;
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
        public IReadOnlyCollection<string> GetRegisteredServices()
            => OrchestratorConfig.Current.Services.Keys.ToList().AsReadOnly();

        /// <inheritdoc/>
        public IEnumerable<InternalStatus> GetInternalStatuses()
        {
            // Simply invoke GetStatus() on each injected IInternalHealth implementation
            return _internalHealthProviders
                .Select(provider => provider.GetStatus());
        }

    }
}
