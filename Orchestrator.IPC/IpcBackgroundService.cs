using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.IPC
{
    /// <summary>
    /// Background service that listens on a NamedPipe and dispatches
    /// incoming JSON-RPC–style requests to the IpcServer.
    /// On Windows the pipe is secured to the current user only.
    /// On Linux the named pipe is created with OS-level filesystem permissions.
    /// </summary>
    public class IpcBackgroundService : BackgroundService, IInternalHealth
    {
        private readonly IIpcServer _ipc;
        private readonly ILogger<IpcBackgroundService> _logger;
        private readonly IOptions<OrchestratorConfig> _config;
        private const string PipeName = "orc_ipc_pipe";
        private volatile bool _hasRun;
        private DateTime _lastRun = DateTime.MinValue;
        private readonly object _lastRunLock = new();

        public InternalStatus GetStatus()
        {
            DateTime lastRun;
            lock (_lastRunLock) { lastRun = _lastRun; }
            var staleness = TimeSpan.FromMilliseconds(_config.Value.Global.HealthCheckInterval * 2);
            var isHealthy = !_hasRun || (DateTime.UtcNow - lastRun) < staleness;
            return new InternalStatus
            {
                Name = nameof(IpcBackgroundService),
                IsHealthy = isHealthy,
                Details = $"Last run at {lastRun:O}"
            };
        }

        public IpcBackgroundService(
            IIpcServer ipc,
            ILogger<IpcBackgroundService> logger,
            IOptions<OrchestratorConfig> config)
        {
            _ipc = ipc;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting IPC listener on pipe '{PipeName}'.", PipeName);

            while (!stoppingToken.IsCancellationRequested)
            {
                NamedPipeServerStream server = CreatePipeServer();
                try
                {
                    await server.WaitForConnectionAsync(stoppingToken);
                    _hasRun = true;
                    lock (_lastRunLock) { _lastRun = DateTime.UtcNow; }
                    _ = HandleClient(server, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    server.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error waiting for IPC client connection.");
                    server.Dispose();
                }
            }
        }

        private static NamedPipeServerStream CreatePipeServer()
        {
            if (OperatingSystem.IsWindows())
            {
                // Restrict pipe to the current user only
                var security = new PipeSecurity();
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                security.AddAccessRule(new PipeAccessRule(
                    identity.User!,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    System.Security.AccessControl.AccessControlType.Allow));

                return NamedPipeServerStreamAcl.Create(
                    PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0, outBufferSize: 0,
                    security);
            }

            // On Linux the named pipe maps to a Unix domain socket; the filesystem
            // ACL on /tmp/.orc_ipc_pipe is governed by the process umask (typically
            // 0600 for a service account).
            return new NamedPipeServerStream(
                PipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                using var reader = new System.IO.StreamReader(pipe);
                using var writer = new System.IO.StreamWriter(pipe) { AutoFlush = true };

                // Simple JSON-RPC: { "method": "...", "params": [...] }
                var json = await reader.ReadLineAsync().WithCancellation(token);
                if (json == null) return;

                var doc = JsonDocument.Parse(json);
                var method = doc.RootElement.GetProperty("method").GetString();
                var args = doc.RootElement.GetProperty("params").EnumerateArray()
                                         .Select(e => e.GetString()).ToArray();

                switch (method)
                {
                    case "RequestNeighborExecution":
                        if (args[0] != null)
                            await _ipc.RequestNeighborExecution(args[0]!);
                        await writer.WriteLineAsync("{\"result\":\"ok\"}");
                        break;

                    case "ReportStatus":
                        // params[0] is the JSON-serialised WorkerStatus
                        if (args[0] != null)
                        {
                            var status = JsonSerializer.Deserialize<WorkerStatus>(args[0]!);
                            if (status != null)
                                await _ipc.ReportStatus(status);
                        }
                        await writer.WriteLineAsync("{\"result\":\"ok\"}");
                        break;

                    default:
                        await writer.WriteLineAsync("{\"error\":\"Unknown method\"}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC client.");
            }
            finally
            {
                if (pipe.IsConnected) pipe.Disconnect();
                pipe.Dispose();
            }
        }
    }
}

