using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.Core.Models
{
    public class WebConfig
    {
        public int UiPort { get; set; }
        public int ApiPort { get; set; }
        public int StreamBufferSize { get; set; }
        public string ApiBaseUrl { get; set; }
    }
}