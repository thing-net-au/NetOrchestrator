using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public class LogStreamService : ILogStreamService
    {
        private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();

        /// <inheritdoc />
        public void Push(string serviceName, string message)
        {
            if (message == null) return;
            var channel = _channels.GetOrAdd(serviceName, _ => Channel.CreateUnbounded<string>());
            channel.Writer.TryWrite(message);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> StreamAsync(string serviceName)
        {
            var channel = _channels.GetOrAdd(serviceName, _ => Channel.CreateUnbounded<string>());
            while (await channel.Reader.WaitToReadAsync())
            {
                while (channel.Reader.TryRead(out var msg))
                {
                    yield return msg;
                }
            }
        }
    }

 
}
