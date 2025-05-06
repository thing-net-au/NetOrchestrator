using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Orchestrator.Core.Models
{
    public class ServiceConfig
    {
        public string Name { get; set; }
        public string ExecutablePath { get; set; }
        public string Arguments { get; set; }
        public int MinInstances { get; set; }
        public int MaxInstances { get; set; }
        public PolicyConfig SchedulePolicy { get; set; }
        public string[] Dependencies { get; set; }
    }
}