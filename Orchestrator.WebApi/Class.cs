using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;

namespace Orchestrator.WebApi
{
    public class StartupJsonClients : IHostedService, IDisposable
    {
        private readonly IEnvelopeStreamService _envelopes;
        private readonly IConsoleLogStreamService _consoleMessages;
        private readonly ILogger<StartupJsonClients> _logger;
        private readonly IOptions<IpcSettings> _opts;

        private TcpJsonClient<Envelope> _client;
        private const int _reconnectDelayMs = 10_000;
        private CancellationTokenSource _cts;

        public StartupJsonClients(
            IEnvelopeStreamService envelopes,
            IConsoleLogStreamService consoleMessages,
            ILogger<StartupJsonClients> logger,
            IOptions<IpcSettings> opts)
        {
            _envelopes = envelopes;
            _consoleMessages = consoleMessages;
            _logger = logger;
            _opts = opts;
        }

        private void HandleEnvelope(Envelope env)
        {
            if (env == null || string.IsNullOrEmpty(env.Topic)) // null guard
                return;
            switch (env.Topic.ToLowerInvariant())
            {
                case "hostheartbeat":
                    var hb = env.Payload.Deserialize<WorkerStatus>();
                    if (hb != null)
                    {
                        var internalStatus = new InternalStatus
                        {
                            Name = hb.ServiceName,
                            IsHealthy = hb.Healthy,
                            Details = hb.Message,
                            Timestamp = hb.Timestamp.UtcDateTime
                        };
                        _envelopes.Push("HostHeartBeat", internalStatus);
                    }
                    break;
                case "servicestatus":
                    var status = env.Payload.Deserialize<ServiceStatus>();
                    if (status != null)
                        _envelopes.Push("ServiceStatus", status);
                    break;
                case "consolelogmessage":
                    _logger.LogInformation("Received envelope: {Topic} {Payload}", env.Topic, env.Payload);
                    var msg = env.Payload.Deserialize<ConsoleLogMessage>();
                    if (msg == null)
                    {
                        _logger.LogError($"Failed to deserialize ConsoleLogMessage from {env.Payload}");
                        break;
                    }
                    _consoleMessages.Push(msg.Name, msg);
                    break;
                default:
                    _logger.LogError($"topic: {env.Topic} not found. message {env.Payload}");
                    break;
            }
        }

        private void ConfigureClient(TcpJsonClient<Envelope> client)
        {
            client.MessageReceived += HandleEnvelope;
            client.Disconnected += async ex => await OnClientDisconnected(ex);
        }

        private async Task OnClientDisconnected(Exception? ex)
        {
            if (_cts.IsCancellationRequested) return;
            _logger.LogWarning(ex, "Envelope stream disconnected; attempting to reconnect...");

            _client.Dispose();
            await Task.Delay(_reconnectDelayMs, _cts.Token).ContinueWith(_ => { });

            var cfg = _opts.Value;
            _client = new TcpJsonClient<Envelope>(cfg.Host, cfg.LogPort);
            ConfigureClient(_client);
            _ = ConnectWithRetry(_client, "EnvelopeStream", _cts.Token);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cfg = _opts.Value;

            _client = new TcpJsonClient<Envelope>(cfg.Host, cfg.LogPort);
            ConfigureClient(_client);
            _ = ConnectWithRetry(_client, "EnvelopeStream", _cts.Token);
            // in your StartupJsonClients, after connecting:
    /*        _ = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // e.g. a dummy Envelope with topic "ping"
                    var ping = new Envelope("ping", new { Time = DateTime.UtcNow });
                    await _client.SendAsync(ping);
                    var pingMessage = new ConsoleLogMessage()
                    {
                        Details = "Ping Sent from WEBAPI to Supervisor."
                    };
                    //_consoleMessages.Push("_supervisor", ConsoleLogMessage();
                    await Task.Delay(TimeSpan.FromMinutes(1), _cts.Token)
                               .ContinueWith(_ => { });
                }
            });
    */
            return Task.CompletedTask;
        }

        private async Task ConnectWithRetry(
            TcpJsonClient<Envelope> client,
            string name,
            CancellationToken token)
        {
            const int retryMs = 10_000;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to {Name} server...", name);
                    await client.ConnectAsync(timeoutMs: retryMs);
                    _logger.LogInformation("Connected to {Name} server", name);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to connect to {Name} server; retrying in {Delay}ms",
                        name, retryMs);
                    try { await Task.Delay(retryMs, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();
            _client?.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cts?.Dispose();
        }
    }
}
