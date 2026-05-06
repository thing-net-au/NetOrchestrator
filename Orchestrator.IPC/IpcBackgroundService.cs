using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.IPC
{
    /// <summary>
    /// Background service that listens on a NamedPipe and dispatches
    /// incoming JSON-RPC–style requests to the IpcServer.
    /// </summary>
    public class IpcBackgroundService : BackgroundService, IInternalHealth
    {
        private readonly IIpcServer _ipc;
        private readonly ILogger<IpcBackgroundService> _logger;
        private const string PipeName = "orc_ipc_pipe";
        private DateTime _lastRun = DateTime.MinValue;

        public InternalStatus GetStatus()
        {
            var maxAge = TimeSpan.FromSeconds(30);
            var age = DateTime.UtcNow - _lastRun;

            return new InternalStatus
            {
                Name = nameof(IpcBackgroundService),
                IsHealthy = _lastRun != DateTime.MinValue && age <= maxAge,
                Details = $"Last run at {_lastRun:O}, age={age.TotalSeconds:n1}s"
            };
        }

        public IpcBackgroundService(IIpcServer ipc, ILogger<IpcBackgroundService> logger)
        {
            _ipc = ipc;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting IPC listener on pipe {PipeName}.", PipeName);

            while (!stoppingToken.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(stoppingToken);
                _lastRun = DateTime.UtcNow;

                _ = HandleClient(server, stoppingToken);
            }
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                using var reader = new StreamReader(pipe);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };

                var json = await reader.ReadLineAsync().WithCancellation(token);
                if (string.IsNullOrWhiteSpace(json))
                {
                    await writer.WriteLineAsync("{\"error\":\"Empty payload\"}");
                    return;
                }

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("method", out var methodElement) ||
                    methodElement.ValueKind != JsonValueKind.String)
                {
                    await writer.WriteLineAsync("{\"error\":\"Missing method\"}");
                    return;
                }

                var method = methodElement.GetString();
                var args = doc.RootElement.TryGetProperty("params", out var paramsElement) &&
                           paramsElement.ValueKind == JsonValueKind.Array
                    ? paramsElement.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                    : Array.Empty<string>();

                switch (method)
                {
                    case "RequestNeighborExecution" when args.Length >= 1:
                        await _ipc.RequestNeighborExecution(args[0]!);
                        await writer.WriteLineAsync("{\"result\":\"ok\"}");
                        break;
                    case "RequestNeighborExecution":
                        await writer.WriteLineAsync("{\"error\":\"Missing service name\"}");
                        break;
                    default:
                        await writer.WriteLineAsync("{\"error\":\"Unknown method\"}");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("IPC client handling cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC client.");
            }
            finally
            {
                if (pipe.IsConnected)
                {
                    pipe.Disconnect();
                }
            }
        }
    }
}
