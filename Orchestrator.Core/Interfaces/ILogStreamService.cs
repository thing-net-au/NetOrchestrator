using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections.Generic;

namespace Orchestrator.Core.Interfaces
{
    public interface ILogStreamService
    {
        void Push(string serviceName, string message);
        IAsyncEnumerable<string> StreamAsync(string serviceName);
    }
}
