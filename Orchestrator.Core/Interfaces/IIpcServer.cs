using System.Collections.Generic;
using Orchestrator.Core.Models;
using System.Threading.Tasks;

namespace Orchestrator.Core.Interfaces
{
    /// <summary>
    /// Exposes methods the IPC transport can invoke.
    /// </summary>
    public interface IIpcServer
    {
        /// <summary>Called when a client requests a neighboring service to execute.</summary>
        Task RequestNeighborExecution(string serviceName);

        /// <summary>Called when a worker reports its status over IPC.</summary>
        Task ReportStatus(WorkerStatus status);

        /// <summary>Returns the latest reported statuses for all instances of a service.</summary>
        IEnumerable<WorkerStatus> GetLatestStatuses(string serviceName);

        /// <summary>Returns health status for all internal orchestrator components.</summary>
        IEnumerable<InternalStatus> GetInternalStatuses();
    }
}
