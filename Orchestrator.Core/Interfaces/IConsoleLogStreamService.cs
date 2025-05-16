using Orchestrator.Core.Models;
using System.Text.Json;

namespace Orchestrator.Core.Interfaces
{
    public interface IConsoleLogStreamService
    {
        /// <summary>Push _any_ object onto the given topic.</summary>
        void Push<ConsoleLogMessage>(string facility, ConsoleLogMessage payload);

        /// <summary>Subscribe to the raw envelopes for a given topic.</summary>
        IAsyncEnumerable<ConsoleLogMessage> StreamAsync(string topic);
    }

 
}
