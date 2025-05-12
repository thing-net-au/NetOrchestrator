using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;

namespace Orchestrator
{
    /// <summary>
    /// Worker runs as a hosted background service.
    /// It publishes WorkerStatus and InternalStatus over TCP/JSON every second.
    /// </summary>
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
            TcpJsonClient<WorkerStatus> logClient,
            TcpJsonClient<InternalStatus> statusClient,
            IEnumerable<IInternalHealth> healthProviders)
        {
            _logger = logger;
            _logClient = logClient;
            _statusClient = statusClient;
            _healthProviders = healthProviders;
            _pid = Environment.ProcessId;
            _startTime = DateTimeOffset.UtcNow;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting (pid={Pid})", _pid);
            await RetryConnect(_logClient, "log", cancellationToken);
            await RetryConnect(_statusClient, "status", cancellationToken);
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker tick loop entering");
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;

                
                // 1) WorkerStatus



                var ws = new WorkerStatus
                {
                    ServiceName = "Worker",
                    ProcessId = _pid,
                    Timestamp = now.UtcDateTime,
                    Message = $"Tick at {now:O}"
                };
                await _logClient.SendAsync(ws);

                // 2) InternalStatus
                foreach (var health in _healthProviders)
                {
                    var ist = health.GetStatus();
                    ist.Timestamp = DateTime.UtcNow;
                    await _statusClient.SendAsync(ist);
                }

                // 3) Heartbeat
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
            _logger.LogInformation("Worker tick loop exiting");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker stopping");
            _logClient.Dispose();
            _statusClient.Dispose();
            await base.StopAsync(cancellationToken);
        }

        private async Task RetryConnect<T>(
            TcpJsonClient<T> client,
            string name,
            CancellationToken token)
        {
            const int delayMs = 10000;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to {Name} server...", name);
                    await client.ConnectAsync(delayMs);
                    _logger.LogInformation("Connected to {Name} server", name);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to connect to {Name} server; retrying in {Delay}ms",
                        name, delayMs);
                    try { await Task.Delay(delayMs, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
    }
}
