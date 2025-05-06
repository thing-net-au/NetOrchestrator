using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orchestrator.Core.Interfaces;       // IIpcServer
using Orchestrator.Supervisor;           // IProcessSupervisor

namespace Orchestrator.IPC
{
    public class IpcServer : IIpcServer
    {
        private readonly IProcessSupervisor _supervisor;

        public IpcServer(IProcessSupervisor supervisor)
            => _supervisor = supervisor;

        public Task RequestNeighborExecution(string serviceName)
            => _supervisor.StartAsync(serviceName);
    }
}
