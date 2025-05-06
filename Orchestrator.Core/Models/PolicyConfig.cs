using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Orchestrator.Core.Models
{
    public class PolicyConfig
    {
        public string Type { get; set; }      // "steady", "demand", "cron"
        public int? Threshold { get; set; }   // e.g. CPU % for demand
        public string Cron { get; set; }      // e.g. cron expression
    }
}