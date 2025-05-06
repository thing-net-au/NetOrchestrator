using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Core
{
    public class OrchestratorConfig : IConfigurationLoader
    {
        public static OrchestratorConfig Current { get; private set; }

        public Dictionary<string, ServiceConfig> Services { get; set; }
        public GlobalConfig Global { get; set; }
        public SchedulingConfig Scheduling { get; set; }
        public WebConfig Web { get; set; }

        public void Load(IConfiguration configuration)
        {
            Current = configuration.Get<OrchestratorConfig>();
        }
    }
}
