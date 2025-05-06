using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Core.Models
{
    public class ServiceStatus
    {
        public string Name { get; set; }
        public int RunningInstances { get; set; }
        public State State { get; set; }
        public DateTime? LastReportAt { get; set; }
        public bool? LastHealthy { get; set; }

    }
}
