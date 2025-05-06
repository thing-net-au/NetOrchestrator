using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Core.Models
{
    public class WorkerStatus
    {
        public string ServiceName { get; set; }
        public int ProcessId { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Healthy { get; set; }
        public string? Details { get; set; }
    }

}
