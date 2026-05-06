namespace Orchestrator.Core.Models
{
    public class PolicyConfig
    {
        public string Type { get; set; } = "steady";   // "steady", "demand", "cron"
        public int? Threshold { get; set; }             // e.g. CPU % for demand
        public string? Cron { get; set; }               // e.g. "0 * * * *"
    }
}