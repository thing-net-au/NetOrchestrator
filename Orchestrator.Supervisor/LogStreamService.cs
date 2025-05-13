using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Supervisor
{
    /// <summary>
    /// Streams log messages from processes to connected clients.
    /// </summary>
    public class LogStreamService : IEnvelopeStreamService, IAsyncDisposable
    {
        private record ChannelInfo(Channel<string> Chan, FixedSizedQueue<string> History);

        private readonly ConcurrentDictionary<string, ChannelInfo> _channels = new();

        public void Push(string serviceName, string message)
        {
            if (message == null) return;

            var info = _channels.GetOrAdd(serviceName, _ =>
            {
                var options = new BoundedChannelOptions(1000)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                };
                return new ChannelInfo(
                    Channel.CreateBounded<string>(options),
                    new FixedSizedQueue<string>(10)
                );
            });

            info.History.Enqueue(message);
            info.Chan.Writer.TryWrite(message);
        }
        // internal push of a raw JSON envelope
        private void PushRaw(string topic, string envelopeJson)
        {
            var info = _channels.GetOrAdd(topic, _ =>
            {
                var options = new BoundedChannelOptions(1000)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                };
                return new ChannelInfo(
                    Channel.CreateBounded<string>(options),
                    new FixedSizedQueue<string>(10)
                );
            });

            info.History.Enqueue(envelopeJson);
            info.Chan.Writer.TryWrite(envelopeJson);
        }

        // <-- Rename your old StreamAsync(string) to StreamRawAsync(string):
        public async IAsyncEnumerable<string> StreamRawAsync(string serviceName)
        {
            if (_channels.TryGetValue(serviceName, out var info))
            {
                // replay history
                foreach (var msg in info.History.Items)
                    yield return msg;

                // live tail
                var reader = info.Chan.Reader;
                while (await reader.WaitToReadAsync())
                    while (reader.TryRead(out var msg))
                        yield return msg;
            }
        }

        public ValueTask DisposeAsync()
        {
            foreach (var info in _channels.Values)
                info.Chan.Writer.Complete();
            return ValueTask.CompletedTask;
        }

        // public API for pushing a strongly-typed payload
        void IEnvelopeStreamService.Push<T>(string topic, T payload)
        {
            JsonElement element = payload switch
            {
                JsonElement je => je,
                _ => JsonSerializer.SerializeToElement(payload)
            };

            var env = new Envelope
            {
                Topic = topic,
                Type = typeof(T).FullName!,
                Payload = element
            };

            // now serialize your envelope (the Payload will be in‐lined as raw JSON)
            var envJson = JsonSerializer.Serialize(env);
            // push envJson into your raw‐string channel…
            PushRaw(topic, envJson);
        }

        // interface implementation: deserialize each raw JSON into Envelope
        IAsyncEnumerable<Envelope> IEnvelopeStreamService.StreamAsync(string topic)
        {
            return InternalStream();
            async IAsyncEnumerable<Envelope> InternalStream()
            {
                await foreach (var raw in StreamRawAsync(topic))
                {
                    yield return JsonSerializer.Deserialize<Envelope>(raw)!;
                }
            }
        }
    }

}
