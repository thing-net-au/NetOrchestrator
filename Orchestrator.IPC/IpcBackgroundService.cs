// Project: Orchestrator.IPC
// File: IpcBackgroundService.cs

using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.IPC
{
    /// <summary>
    /// Background service that listens on a NamedPipe and dispatches
    /// incoming JSON-RPC–style requests to the IpcServer.
    /// </summary>
    public class IpcBackgroundService : BackgroundService
    {
        private readonly IIpcServer _ipc;
        private readonly ILogger<IpcBackgroundService> _logger;
        private const string PipeName = "orc_ipc_pipe";

        public IpcBackgroundService(IIpcServer ipc, ILogger<IpcBackgroundService> logger)
        {
            _ipc = ipc;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting IPC listener on pipe '{PipeName}'", PipeName);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for a client to connect
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
                                        NamedPipeServerStream.MaxAllowedServerInstances,
                                        PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(stoppingToken);

                _ = HandleClient(server, stoppingToken);
            }
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                using var reader = new StreamReader(pipe);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };

                // Simple JSON-RPC: { "method": "RequestNeighborExecution", "params": ["MyService"] }
                var json = await reader.ReadLineAsync().WithCancellation(token);
                var doc = JsonDocument.Parse(json);
                var method = doc.RootElement.GetProperty("method").GetString();
                var args = doc.RootElement.GetProperty("params").EnumerateArray()
                                    .Select(e => e.GetString()).ToArray();

                switch (method)
                {
                    case "RequestNeighborExecution":
                        await _ipc.RequestNeighborExecution(args[0]);
                        await writer.WriteLineAsync("{\"result\":\"ok\"}");
                        break;
                    default:
                        await writer.WriteLineAsync("{\"error\":\"Unknown method\"}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC client");
            }
            finally
            {
                if (pipe.IsConnected) pipe.Disconnect();
            }
        }
    }
}
