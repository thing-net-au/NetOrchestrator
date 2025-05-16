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
    /// Receives Consoles from the TCP client and brokers them in-memory for SSE.
    /// </summary>
    public class ConsoleLogStreamService : IConsoleLogStreamService
    {
        private readonly TcpJsonClient<ConsoleLogMessage> _client;
        private readonly LogStreamService _broker;  // in-memory broker for SSE

        public ConsoleLogStreamService(
            LogStreamService broker)
        {
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));

            // forward all incoming Consoles into in-memory streams
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
        /// Stream raw JSON Consoles for a given topic.
        /// </summary>
        public IAsyncEnumerable<string> StreamRawAsync(
            string topic,
            [EnumeratorCancellation] CancellationToken ct = default)
            => _broker.StreamRawAsync(topic);

 
        /// <summary>
        /// Full Console stream for generic consumers.
        /// </summary>
        async IAsyncEnumerable<ConsoleLogMessage> IConsoleLogStreamService.StreamAsync(
            string topic)
        {
            await foreach (var raw in _broker.StreamRawAsync(topic))
            {
                var env = JsonSerializer.Deserialize<ConsoleLogMessage>(raw);
                if (env != null)
                    yield return env;
            }
        }
    }
}
