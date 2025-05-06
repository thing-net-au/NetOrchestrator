using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces
{
    public interface IInternalHealth
    {
        /// <summary>Returns the current health status of this component.</summary>
        InternalStatus GetStatus();

    }
}
