using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;

namespace Orchestrator
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IEnumerable<IInternalHealth> _healthProviders;
        private readonly TcpJsonClient<WorkerStatus> _logClient;
        private readonly TcpJsonClient<InternalStatus> _statusClient;
        private readonly int _pid;
        private readonly DateTimeOffset _startTime;

        public Worker(
            ILogger<Worker> logger,
            IOptions<IpcSettings> ipcOpts,
            IEnumerable<IInternalHealth> healthProviders)
        {
            _logger = logger;
            _healthProviders = healthProviders;
            _pid = Environment.ProcessId;
            _startTime = DateTimeOffset.UtcNow;

            var opts = ipcOpts.Value;
            _logClient = new TcpJsonClient<WorkerStatus>(opts.Host, opts.LogPort);
            _statusClient = new TcpJsonClient<InternalStatus>(opts.Host, opts.StatusPort);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting (pid={Pid})", _pid);

            // Retry connect until both servers are up
            await ConnectWithRetry(_logClient, "log", cancellationToken);
            await ConnectWithRetry(_statusClient, "status", cancellationToken);

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker.ExecuteAsync running ticks every 1s");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;

                // 1) publish WorkerStatus
                var ws = new WorkerStatus
                {
                    ServiceName = "Worker",
                    ProcessId = _pid,
                    Timestamp = now.UtcDateTime,
                    Message = $"Worker running at: {now:O}"
                };
                await _logClient.SendAsync(ws);

                // 2) publish InternalStatus ticks
                foreach (var health in _healthProviders)
                {
                    var ist = health.GetStatus();
                    // ensure Timestamp is set
                    ist.Timestamp = DateTime.UtcNow;
                    await _statusClient.SendAsync(ist);
                }

                // 3) heartbeat
                var hb = new WorkerStatus
                {
                    ServiceName = "HostHeartbeat",
                    ProcessId = _pid,
                    Timestamp = now.UtcDateTime,
                    Message = JsonSerializer.Serialize(new
                    {
                        Timestamp = now,
                        Uptime = (now - _startTime).TotalSeconds
                    })
                };
                await _logClient.SendAsync(hb);

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }

            _logger.LogInformation("Worker stopping ticks");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker stopping, disposing clients");
            _logClient.Dispose();
            _statusClient.Dispose();
            await base.StopAsync(cancellationToken);
        }

        private async Task ConnectWithRetry<T>(
            TcpJsonClient<T> client,
            string name,
            CancellationToken token)
        {
            const int retryMs = 1000;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to {Name} server...", name);
                    await client.ConnectAsync();
                    _logger.LogInformation("Connected to {Name} server", name);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to {Name} server; retrying in {Ms}ms", name, retryMs);
                    try { await Task.Delay(retryMs, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
            token.ThrowIfCancellationRequested();
        }
    }
}