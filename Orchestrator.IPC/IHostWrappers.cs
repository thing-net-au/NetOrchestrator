using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::Orchestrator.Core.Interfaces;
using global::Orchestrator.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Interfaces;
using Orchestrator.IPC;

namespace Orchestrator.IPC
{
    /// <summary>
    /// Spins up a TcpJsonClient<T> at host start, and
    /// pushes every incoming message into the ILogStreamService.
    /// </summary>
    public class TcpJsonClientHost<T> : IHostedService
    {
        private readonly TcpJsonClient<T> _client;
        private readonly ILogStreamService _logs;
        private readonly IpcSettings _opts;
        private readonly string _streamName;
        private readonly int _connectTimeout;

        public TcpJsonClientHost(
            TcpJsonClient<T> client,
            ILogStreamService logs,
            IOptions<IpcSettings> optsAccessor)
        {
            _client = client;
            _logs = logs;
            _opts = optsAccessor.Value;
            _connectTimeout = _opts.ConnectTimeoutMs;   // add this to your IpcSettings
            _streamName = typeof(T) == typeof(WorkerStatus)
                              ? "Worker"                // or pick whatever stream-name you like
                              : "InternalStatus";
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // any time the client gets a T, we serialize it and push
            _client.MessageReceived += msg =>
                _logs.Push(_streamName, JsonSerializer.Serialize(msg));

            // now actually connect
            await _client.ConnectAsync(_connectTimeout);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _client.Dispose();
            return Task.CompletedTask;
        }
    }
}
