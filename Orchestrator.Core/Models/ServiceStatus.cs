using System;
using System.Text.Json.Serialization;

namespace Orchestrator.Core.Models
{
    public class ServiceStatus
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("runningInstances")]
        public int RunningInstances { get; set; }

        [JsonPropertyName("state")]
        public State State { get; set; } = State.Unknown;

        [JsonPropertyName("lastReportAt")]
        public DateTime? LastReportAt { get; set; }

        [JsonPropertyName("lastHealthy")]
        public bool? LastHealthy { get; set; }
    }
}
