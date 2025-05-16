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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cfg = _opts.Value;

            _client = new TcpJsonClient<Envelope>(cfg.Host, cfg.LogPort);
            _client.MessageReceived += env =>
            {
                // forward every envelope onto its topic
                //_envelopes.Push(env.Topic, env);
                switch (env.Topic.ToLowerInvariant())
                {
                    case "hostheartbeat":
                        //swallow for now
                        break;
                   case "servicestatus":
                        //swallow for now
                        break;
                    case "consolelogmessage":
                        //Convert to ConsoleLogMessage
                   _logger.LogInformation("Received envelope: {Topic} {Payload}", env.Topic, env.Payload);
                     var msg = env.Payload.Deserialize<ConsoleLogMessage>();
                        if (msg == null)
                        {
                            _logger.LogError($"Failed to deserialize ConsoleLogMessage from {env.Payload}");
                            break;
                        }
                        // forward to the console message stream
                        _consoleMessages.Push(msg.Name, msg);
                        break;
                    default:
                        _logger.LogError($"topic: {env.Topic} not found. message {env.Payload}");
                        break;

                }
            };

            // Only one TCP client now
            _ = ConnectWithRetry(_client, "EnvelopeStream", _cts.Token);

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
