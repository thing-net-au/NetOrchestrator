using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;

namespace Orchestrator.WebApi
{

    /// <summary>
    /// On startup, connect two TcpJsonClient<T> and hook their MessageReceived
    /// into the ILogStreamService so your SSE endpoints pick them up.
    /// </summary>
    public class StartupJsonClients : IHostedService, IDisposable
    {
        private readonly ILogStreamService _logs;
        private readonly ILogger<StartupJsonClients> _logger;
        private readonly IOptions<IpcSettings> _opts;
        private TcpJsonClient<WorkerStatus> _logClient;
        private TcpJsonClient<InternalStatus> _statusClient;
        private const int _connectRetryMs = 10000;
        private CancellationTokenSource _cts;

        public StartupJsonClients(
            ILogStreamService logs,
            ILogger<StartupJsonClients> logger,
            IOptions<IpcSettings> opts)
        {
            _logs = logs;
            _logger = logger;
            _opts = opts;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var cfg = _opts.Value;

            // log client
            _logClient = new TcpJsonClient<WorkerStatus>(cfg.Host, cfg.LogPort);
            _logClient.MessageReceived += ws =>
                _logs.Push(ws.ServiceName, JsonSerializer.Serialize(ws));

            // status client
            _statusClient = new TcpJsonClient<InternalStatus>(cfg.Host, cfg.StatusPort);
            _statusClient.MessageReceived += st =>
                _logs.Push("InternalStatus", JsonSerializer.Serialize(st));

            // fire & forget connect loops
            _ = ConnectWithRetry(_logClient, "WorkerStatus", _cts.Token);
            _ = ConnectWithRetry(_statusClient, "InternalStatus", _cts.Token);
        }

        private async Task ConnectWithRetry<T>(TcpJsonClient<T> client, string name, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to {Name} server...", name);
                    await client.ConnectAsync(timeoutMs: _connectRetryMs);
                    _logger.LogInformation("Connected to {Name} server", name);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to {Name} server; retrying in {Delay}ms", name, _connectRetryMs);
                    try { await Task.Delay(_connectRetryMs, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            _logClient?.Dispose();
            _statusClient?.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
