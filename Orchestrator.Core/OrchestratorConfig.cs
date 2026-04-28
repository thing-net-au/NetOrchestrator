using System.Collections.Generic;
using Orchestrator.Core.Models;

namespace Orchestrator.Core
{
    public class OrchestratorConfig
    {
        public Dictionary<string, ServiceConfig> Services { get; set; } = new();
        public GlobalConfig Global { get; set; } = new();
        public SchedulingConfig Scheduling { get; set; } = new();
        public WebConfig Web { get; set; } = new();
    }
}
