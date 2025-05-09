using Orchestrator.Core.Models;
using System.Threading.Tasks;

namespace Orchestrator.Core.Interfaces
{
    /// <summary>
    /// Exposes methods the IPC transport can invoke.
    /// </summary>
    public interface IIpcServer
    {
        /// <summary>
        /// Called when a client requests a neighboring service to execute.
        /// </summary>
        /// <param name="serviceName">The target service name.</param>
        Task RequestNeighborExecution(string serviceName);
        Task ReportStatus(WorkerStatus status);
        /// <summary>
        /// Return the latest WorkerStatus entries for the given service.
        /// </summary>
        IEnumerable<WorkerStatus> GetLatestStatuses(string serviceName);

        /// <summary>
        /// Return the list of all registered service names.
        /// </summary>
        IReadOnlyCollection<string> GetRegisteredServices();

        /// <summary>
        /// Return the current health reports from all IInternalHealth providers.
        /// </summary>
        IEnumerable<InternalStatus> GetInternalStatuses();
    }
}
