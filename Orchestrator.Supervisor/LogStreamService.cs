using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.Supervisor
{
    /// <summary>
    /// Streams log messages from processes to connected clients using bounded channels
    /// to prevent unbounded memory growth when consumers are slow.
    /// </summary>
    public class LogStreamService : ILogStreamService
    {
        private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();
        private readonly int _bufferSize;

        public LogStreamService(IOptions<OrchestratorConfig> config)
        {
            _bufferSize = config.Value.Web.StreamBufferSize > 0
                ? config.Value.Web.StreamBufferSize
                : 8192;
        }

        /// <inheritdoc />
        public void Push(string serviceName, string message)
        {
            if (message == null) return;
            var channel = GetOrCreateChannel(serviceName);
            channel.Writer.TryWrite(message);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> StreamAsync(string serviceName)
        {
            var channel = GetOrCreateChannel(serviceName);
            while (await channel.Reader.WaitToReadAsync())
            {
                while (channel.Reader.TryRead(out var msg))
                {
                    yield return msg;
                }
            }
        }

        private Channel<string> GetOrCreateChannel(string serviceName)
            => _channels.GetOrAdd(serviceName, _ =>
                Channel.CreateBounded<string>(new BoundedChannelOptions(_bufferSize)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = false,
                    SingleWriter = false
                }));
    }
}

