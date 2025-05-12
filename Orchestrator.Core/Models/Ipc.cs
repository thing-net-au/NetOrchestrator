using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Orchestrator.Core.Models
{
    public class IpcSettings
    {
        public string Host { get; set; } = "127.0.0.1";
        public int LogPort { get; set; }
        public int StatusPort { get; set; }
        public int HistorySize { get; set; } = 100;
        public int ConnectTimeoutMs { get; set; }
    }

}