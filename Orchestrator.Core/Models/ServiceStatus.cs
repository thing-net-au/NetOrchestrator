using System;
using System.Collections.Generic;

namespace Orchestrator.Core.Models
{
    public class ServiceStatus
    {
        public string Name { get; set; } = string.Empty;
        public int RunningInstances { get; set; }
        public State State { get; set; }
        public DateTime? LastReportAt { get; set; }
        public bool? LastHealthy { get; set; }
        /// <summary>Process IDs of currently running instances, used for CPU/memory metrics.</summary>
        public IReadOnlyList<int> ProcessIds { get; set; } = Array.Empty<int>();
    }
}
