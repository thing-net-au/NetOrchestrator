using Orchestrator.Core.Models;
using System.Text.Json;

namespace Orchestrator.Core.Interfaces
{
    public interface IEnvelopeStreamService
    {
        /// <summary>Push _any_ object onto the given topic.</summary>
        void Push<T>(string topic, T payload);

        /// <summary>Subscribe to the raw envelopes for a given topic.</summary>
        IAsyncEnumerable<Envelope> StreamAsync(string topic);
    }

}
