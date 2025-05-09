using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Text.Json;

namespace Orchestrator.Core
{
    public class OrchestratorConfig : IConfigurationLoader
    {
        // Backing field for the static Current
        private static OrchestratorConfig? _current;

        /// <summary>
        /// The singleton instance of the loaded config.
        /// If it hasn’t been set via Load(IConfiguration), it will be
        /// pulled from “orchestrator.json” in the base directory.
        /// </summary>
        public static OrchestratorConfig Current
        {
            get
            {
                if (_current == null)
                {
                    // Build an IConfiguration that supports comments, trailing commas, etc.
                    var config = new ConfigurationBuilder()
                       .SetBasePath(AppContext.BaseDirectory)
                       .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: false)
                       .Build();

                    // Bind into a fresh instance
                    var oc = new OrchestratorConfig();
                    oc.Load(config);    // this calls configuration.Bind(...) under the covers
                    _current = oc;
                }
                return _current;
            }
            private set => _current = value;
        }

        public Dictionary<string, ServiceConfig> Services { get; set; }
        public GlobalConfig Global { get; set; }
        public SchedulingConfig Scheduling { get; set; }
        public WebConfig Web { get; set; }

        /// <summary>
        /// Called during DI startup to bind IConfiguration into this instance.
        /// This will override the static Current.
        /// </summary>
        public void Load(IConfiguration configuration)
        {
            // Bind all sections (Services, Global, etc.) automatically
            configuration.Bind(this);
            // Update the static singleton
            Current = this;
        }
    }
}
