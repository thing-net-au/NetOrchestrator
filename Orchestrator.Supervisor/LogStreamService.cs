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
    public class LogStreamService : ILogStreamService, IAsyncDisposable
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
                    new FixedSizedQueue<string>(100)
                );
            });

            // save history
            info.History.Enqueue(message);
            // push to channel
            info.Chan.Writer.TryWrite(message);
        }

        public async IAsyncEnumerable<string> StreamAsync(string serviceName)
        {
            if (_channels.TryGetValue(serviceName, out var info))
            {
                // replay
                foreach (var msg in info.History.Items)
                    yield return msg;

                // live
                var reader = info.Chan.Reader;
                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out var msg))
                        yield return msg;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            foreach (var info in _channels.Values)
                info.Chan.Writer.Complete();
            return ValueTask.CompletedTask;
        }
    }

}
