using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.IPC
{
    public class LogStreamService : IEnvelopeStreamService, IAsyncDisposable
    {
        // Holds the shared history AND the subscriber list for each topic
        private class TopicData
        {
            public FixedSizedQueue<string> History { get; } = new(50);
            public List<Channel<string>> Subscribers { get; } = new();
        }
        IAsyncEnumerable<Envelope> IEnvelopeStreamService.StreamAsync(string topic)
    => StreamEnvelopes(topic);

        private readonly ConcurrentDictionary<string, TopicData> _topics = new();

        // Called whenever a new log message arrives
        public void Push(string serviceName, string message)
        {
            if (message == null) return;

            var data = _topics.GetOrAdd(serviceName, _ => new TopicData());
            data.History.Enqueue(message);

            // broadcast to every subscriber
            lock (data.Subscribers)
            {
                foreach (var ch in data.Subscribers.ToArray())
                    // best-effort: if a channel is full/closed, ignore it
                    ch.Writer.TryWrite(message);
            }
        }

        // For raw‐JSON envelopes
        private void PushRaw(string topic, string envelopeJson)
            => Push(topic, envelopeJson);

        // The SSE endpoint uses this to get a per‐client stream
        public async IAsyncEnumerable<string> StreamRawAsync(string serviceName)
        {
            var data = _topics.GetOrAdd(serviceName, _ => new TopicData());

            // 1) create a dedicated channel for this subscriber
            var channel = Channel.CreateUnbounded<string>();
            lock (data.Subscribers)
            {
                data.Subscribers.Add(channel);
            }

            try
            {
                // 2) replay the shared history into this one channel
                foreach (var msg in data.History.Items)
                    yield return msg;

                // 3) then live-tail from this channel alone
                var reader = channel.Reader;
                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out var msg))
                        yield return msg;
                }
            }
            finally
            {
                // clean up when the client disconnects
                lock (data.Subscribers)
                {
                    data.Subscribers.Remove(channel);
                }
                channel.Writer.Complete();
            }
        }

        // Envelope‐typed API
        void IEnvelopeStreamService.Push<T>(string topic, T payload)
        {
            JsonElement element = payload switch
            {
                JsonElement je => je,
                _ => JsonSerializer.SerializeToElement(payload)
            };
            var env = new Envelope(topic, typeof(T).FullName!, element);
            var json = JsonSerializer.Serialize(env);
            PushRaw(topic, json);
        }

        private async IAsyncEnumerable<Envelope> StreamEnvelopes(string topic)
        {
            await foreach (var raw in StreamRawAsync(topic))
            {
                yield return JsonSerializer.Deserialize<Envelope>(raw)!;
            }
        }
        public ValueTask DisposeAsync()
        {
            // Close every subscriber channel
            foreach (var data in _topics.Values)
            {
                lock (data.Subscribers)
                {
                    foreach (var ch in data.Subscribers)
                        ch.Writer.Complete();
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
