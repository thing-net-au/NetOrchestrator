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

    public static class EnvelopeStreamServiceExtensions
    {
        /// <summary>“Unwrap” the envelope, yielding the strongly-typed payload.</summary>
        public static async IAsyncEnumerable<T> StreamAsync<T>(
            this IEnvelopeStreamService svc,
            string topic)
        {
            await foreach (var envelope in svc.StreamAsync(topic))
            {
                yield return envelope.Payload.Deserialize<T>()!;
            }
        }

        /// <summary>Gives you the raw JSON string of the envelope payload.</summary>
        public static async IAsyncEnumerable<string> StreamRawAsync(
            this IEnvelopeStreamService svc,
            string topic)
        {
            await foreach (var envelope in svc.StreamAsync(topic))
            {
                yield return envelope.Payload.GetRawText();
            }
        }
    }
}
