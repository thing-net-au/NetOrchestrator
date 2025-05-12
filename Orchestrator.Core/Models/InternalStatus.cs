
namespace Orchestrator.Core.Models
{
    public class InternalStatus
    {
        /// <summary>Name of the internal component (e.g. “PolicyScheduler”, “IpcBackgroundService”)</summary>
        public string Name { get; set; }

        /// <summary>True if it’s currently running/responding</summary>
        public bool IsHealthy { get; set; }

        /// <summary>Optional details or last run timestamp</summary>
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
