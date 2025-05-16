using System;
using System.Text.Json.Serialization;

namespace Orchestrator.Core.Models
{
    public class ConsoleLogMessage
    {
        /// <summary>Name of the process</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Process ID</summary>
        [JsonPropertyName("pid")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? PID { get; set; }

        /// <summary>True if it’s currently running/responding</summary>
        [JsonPropertyName("isHealthy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsHealthy { get; set; }

        /// <summary>STDOUT message</summary>
        [JsonPropertyName("details")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Details { get; set; }

        [JsonPropertyName("timestamp")]
              public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
