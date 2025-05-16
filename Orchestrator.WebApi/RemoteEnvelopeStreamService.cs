using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;

namespace Orchestrator.WebApi
{
    /// <summary>
    /// Receives envelopes from the TCP client and brokers them in-memory for SSE.
    /// </summary>
    public class EnvelopeStreamService : IEnvelopeStreamService
    {
        private readonly TcpJsonClient<Envelope> _client;
        private readonly LogStreamService _broker;  // in-memory broker for SSE

        public EnvelopeStreamService(
            TcpJsonClient<Envelope> client,
            LogStreamService broker)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));

            // forward all incoming envelopes into in-memory streams
            _client.MessageReceived += env =>
            {
                var json = JsonSerializer.Serialize(env);
                _broker.Push(env.Topic, json);
            };
        }

        /// <summary>
        /// Not used for outgoing in WebAPI; read-only service.
        /// </summary>
        public void Push<T>(string topic, T payload)
        {
            var json = JsonSerializer.Serialize(payload);
            _broker.Push(topic, json);
            //throw new NotSupportedException("Read-only broker");
        }
        /// <summary>
        /// Stream raw JSON envelopes for a given topic.
        /// </summary>
        public IAsyncEnumerable<string> StreamRawAsync(
            string topic,
            [EnumeratorCancellation] CancellationToken ct = default)
            => _broker.StreamRawAsync(topic);

        /// <summary>
        /// Stream typed payloads by deserializing envelope.Payload.
        /// </summary>
        public async IAsyncEnumerable<T> StreamRawAsync<T>(
            string topic,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var raw in _broker.StreamRawAsync(topic))
            {
                var env = JsonSerializer.Deserialize<Envelope>(raw);
                if (env?.Payload.ValueKind == JsonValueKind.Object)
                {
                    yield return JsonSerializer.Deserialize<T>(
                        env.Payload.GetRawText())!;
                }
            }
        }

        /// <summary>
        /// Full envelope stream for generic consumers.
        /// </summary>
        async IAsyncEnumerable<Envelope> IEnvelopeStreamService.StreamAsync(
            string topic)
        {
            await foreach (var raw in _broker.StreamRawAsync(topic))
            {
                var env = JsonSerializer.Deserialize<Envelope>(raw);
                if (env != null)
                    yield return env;
            }
        }
    }
}
