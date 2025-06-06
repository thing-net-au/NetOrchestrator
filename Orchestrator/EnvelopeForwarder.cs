using Microsoft.Extensions.Logging;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;
using System.Text.Json;
using Orchestrator.Core.Extensions;

namespace Orchestrator
{
    public class EnvelopeForwarder : BackgroundService
    {
        private readonly IEnvelopeStreamService _stream;
        private readonly TcpJsonServer<Envelope> _server;
        private readonly IEnumerable<string> _channels;

        public EnvelopeForwarder(
            IEnvelopeStreamService stream,
            TcpJsonServer<Envelope> server,
                  IConfigurationLoader loader)
        {
            _stream = stream;
            _server = server;
            var svcNames = ((OrchestratorConfig)loader).Services.Keys;
            _channels = new[] { "ConsoleLogMessage", "ServiceStatus" }
                        .Concat(svcNames);
            
        }
        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            var tasks = _channels.Select(chan => ForwardChannel(chan, ct));
            await Task.WhenAll(tasks);
        }
        private async Task ForwardChannel(string chan, CancellationToken ct)
        {
            await foreach (var json in _stream.StreamRawAsync(chan).WithCancellation(ct))
            {
                // assume the service pushed raw JSON of Envelope
                var env = JsonSerializer.Deserialize<Envelope>(json);
                if (env != null)
                    await _server.BroadcastAsync(env);
            }
        }

    }
}