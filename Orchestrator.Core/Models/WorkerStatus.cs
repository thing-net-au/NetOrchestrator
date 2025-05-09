using System;

namespace Orchestrator.Core.Models
{
    /// <summary>
    /// Represents the status of a worker process, including health and message details.
    /// </summary>
    public class WorkerStatus
    {
        /// <summary>
        /// Logical name of the service emitting this status.
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// Process ID of the worker.
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Timestamp when this status was recorded.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Indicates whether the worker is healthy.
        /// </summary>
        public bool Healthy { get; set; }

        /// <summary>
        /// Arbitrary details or diagnostic information.
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// A human-readable message describing current status or events.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Optional uptime of the worker in seconds.
        /// </summary>
        public double? UptimeSeconds { get; set; }
    }
}