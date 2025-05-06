using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Core.Models
{
    public class SchedulingConfig
    {
        public string DefaultPolicy { get; set; }
        public int DemandThreshold { get; set; }
    }
}