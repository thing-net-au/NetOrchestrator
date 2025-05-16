
namespace Orchestrator.Core.Models
{
    public class ConsoleLogMessage
    {
       /// <summary>
       /// Name of the process
       /// </summary>       
        public string Name { get; set; }
       /// <summary>
       /// Process ID
       /// </summary>
        public int PID { get; set; } = -1;

        /// <summary>True if it’s currently running/responding</summary>
        public bool IsHealthy { get; set; }

        /// <summary>STDOUT message</summary>
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
