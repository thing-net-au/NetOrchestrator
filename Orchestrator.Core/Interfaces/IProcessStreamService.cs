using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections.Generic;
using System.Text.Json;

namespace Orchestrator.Core.Interfaces
{
    public interface IProcessStreamService
    {
        void Push(string serviceName, string message);
        IAsyncEnumerable<string> StreamAsync(string serviceName);
        // our new generic overload
        async IAsyncEnumerable<T> StreamAsync<T>(string name)
        {
            await foreach (var json in StreamAsync(name))
            {
                yield return JsonSerializer.Deserialize<T>(json)!;
            }
        }
    }
}
