using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Collections.Generic;
using System.Text.Json;

namespace Orchestrator.Core.Extensions
{
    /// <summary>Helpers for working with <see cref="IEnvelopeStreamService"/>.</summary>
    public static class EnvelopeStreamServiceExtensions
    {
        /// <summary>Unwrap the envelope, yielding the strongly-typed payload.</summary>
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
