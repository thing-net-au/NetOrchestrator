using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;
using System.Text.Json;
using Orchestrator.Core.Extensions;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TcpJsonClient<Envelope> _client;
    private readonly string[] _serviceNames;
    private readonly int _pid;
    private readonly DateTimeOffset _start;

    public Worker(
        ILogger<Worker> logger,
        TcpJsonClient<Envelope> client,
        IConfigurationLoader cfg
    )
    {
        _logger = logger;
        _client = client;
        _serviceNames = cfg.GetConfiguredServices().ToArray();
        _pid = Environment.ProcessId;
        _start = DateTimeOffset.UtcNow;
    }

    public override async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Worker starting (pid={Pid})", _pid);
        await RetryConnect(_client, "envelope", ct);
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker tick loop entering");

        // kick off heartbeat loop
        var pumps = new List<Task>
        {
            HeartbeatLoop(stoppingToken)
        };

        // when any of them ends (i.e. cancellation), we're done
        await Task.WhenAny(Task.WhenAll(pumps), Task.Delay(Timeout.Infinite, stoppingToken));
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var env = new Envelope("HostHeartBeat", new WorkerStatus
            {
                ServiceName = "HostHeartbeat",
                ProcessId = _pid,
                Timestamp = now,
                Healthy = true,
                Message = JsonSerializer.Serialize(new
                {
                    Timestamp = now,
                    Uptime = (now - _start).TotalSeconds
                }),
                UptimeSeconds = (now - _start).TotalSeconds
            });
            await _client.SendAsync(env);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Worker stopping");
        _client.Dispose();
        await base.StopAsync(ct);
    }

    private async Task RetryConnect<T>(TcpJsonClient<T> client, string name, CancellationToken ct)
    {
        const int delayMs = 10_000;
        while (!ct.IsCancellationRequested)
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
                try { await Task.Delay(delayMs, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
