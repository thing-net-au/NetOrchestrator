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
    }
}
