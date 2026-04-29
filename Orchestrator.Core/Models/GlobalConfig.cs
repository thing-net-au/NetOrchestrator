namespace Orchestrator.Core.Models
{
    public class GlobalConfig
    {
        /// <summary>Default health check interval used as fallback if not configured.</summary>
        public const int DefaultHealthCheckIntervalMs = 10_000;

        public string LoggingLevel { get; set; } = "Info";
        public int IpcTimeout { get; set; } = 5000;
        public int HealthCheckInterval { get; set; } = DefaultHealthCheckIntervalMs;
        /// <summary>Milliseconds to wait before restarting a crashed process.</summary>
        public int RestartBackoffMs { get; set; } = 5000;
        /// <summary>
        /// Scale-down demand threshold expressed as a fraction of the main threshold (default 0.5 = 50%).
        /// A service is scaled down when avgCPU &lt; Threshold × DemandScaleDownRatio.
        /// </summary>
        public double DemandScaleDownRatio { get; set; } = 0.5;
    }
}