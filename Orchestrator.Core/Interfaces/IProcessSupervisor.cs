using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces
{
    public interface IProcessSupervisor
    {
        Task StartAsync(string serviceName, int count = 1);
        Task StopAsync(string serviceName, int count = 1);
        Task<IEnumerable<ServiceStatus>> ListStatusAsync();
    }
}