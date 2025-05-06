using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Core.Models
{
    public class GlobalConfig
    {
        public string LoggingLevel { get; set; }
        public int IpcTimeout { get; set; }
        public int HealthCheckInterval { get; set; }
    }
}