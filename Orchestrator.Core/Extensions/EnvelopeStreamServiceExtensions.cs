using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Orchestrator.Core.Extensions
{
    /// <summary>Helpers for working with <see cref="IEnvelopeStreamService"/>.</summary>
    public static class EnvelopeStreamServiceExtensions
    {
        /// <summary>
        /// Unwrap the envelope, yielding the strongly-typed payloads that match T.
        /// Invalid or mismatched payloads are skipped.
        /// </summary>
        public static async IAsyncEnumerable<T> StreamAsync<T>(
            this IEnvelopeStreamService svc,
            string topic,
            ILogger? logger = null)
        {
            string expectedType = typeof(T).FullName!;

            await foreach (var envelope in svc.StreamAsync(topic))
            {
                if (envelope.Payload.ValueKind == JsonValueKind.Undefined ||
                    envelope.Payload.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                // Optional: Filter by declared payload type
                if (!string.Equals(envelope.PayloadType, expectedType, System.StringComparison.Ordinal))
                {
                    continue; // Skip mismatched types
                }

                T? result;
                try
                {
                    result = envelope.Payload.Deserialize<T>();
                }
                catch (JsonException ex)
                {
                    logger?.LogWarning(ex,
                        "Failed to deserialize {Type} from topic {Topic}. Payload: {Payload}",
                        expectedType, topic, envelope.Payload.GetRawText());
                    continue; // Skip invalid payloads
                }

                if (result != null)
                {
                    yield return result;
                }
            }
        }

        /// <summary>
        /// Yield the raw JSON of each envelope payload.
        /// Invalid or undefined payloads are skipped.
        /// </summary>
        public static async IAsyncEnumerable<string> StreamRawAsync(
            this IEnvelopeStreamService svc,
            string topic)
        {
            await foreach (var envelope in svc.StreamAsync(topic))
            {
                if (envelope.Payload.ValueKind == JsonValueKind.Undefined ||
                    envelope.Payload.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                yield return envelope.Payload.GetRawText();
            }
        }
    }
}
