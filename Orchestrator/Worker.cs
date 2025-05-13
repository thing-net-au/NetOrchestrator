using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;
using System.Text.Json;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IEnvelopeStreamService _envelopeService;
    private readonly TcpJsonClient<Envelope> _client;
    private readonly string[] _serviceNames;
    private readonly int _pid;
    private readonly DateTimeOffset _start;

    public Worker(
        ILogger<Worker> logger,
        IEnvelopeStreamService envelopeService,
        TcpJsonClient<Envelope> client,
        IConfigurationLoader cfg
    )
    {
        _logger = logger;
        _envelopeService = envelopeService;
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

        // kick off a pump per service for both kinds of streams
        var pumps = new List<Task>();
        foreach (var svc in _serviceNames)
        {
            pumps.Add(PumpPlainLogLines(svc, stoppingToken));
            pumps.Add(PumpProcessEnvelopes(svc, stoppingToken));
        }

        // also your heartbeat/tick loop
        pumps.Add(HeartbeatLoop(stoppingToken));

        // when any of them ends (i.e. cancellation), we're done
        await Task.WhenAny(Task.WhenAll(pumps), Task.Delay(Timeout.Infinite, stoppingToken));
    }

    private async Task PumpPlainLogLines(string svc, CancellationToken ct)
    {
        await foreach (var ws in _envelopeService.StreamAsync<WorkerStatus>(svc))
        {
            // either re-wrap it in an envelope:
            var env = new Envelope
            {
                Topic = svc,
                Payload = JsonSerializer.SerializeToElement(ws)
            };
            await _client.SendAsync(env);

        }
    }

    private async Task PumpProcessEnvelopes(string svc, CancellationToken ct)
    {
        // this is your “raw JSON” stream:
        await foreach (var json in _envelopeService.StreamRawAsync(svc)
                                                   .WithCancellation(ct))
        {
            Envelope? env;
            try
            {
                env = JsonSerializer.Deserialize<Envelope>(json);
                if (env is null) continue;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse envelope JSON for {Svc}: {Json}", svc, json);
                continue;
            }

            try
            {
                await _client.SendAsync(env);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to send process envelope for {Svc}", svc);
            }
        }
    }


    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var env = new Envelope
            {
                Topic = "HostHeartbeat",
                Payload = JsonSerializer.SerializeToElement(new WorkerStatus
                {
                    ServiceName = "HostHeartbeat",
                    ProcessId = _pid,
                    Timestamp = now.UtcDateTime,
                    Message = JsonSerializer.Serialize(new
                    {
                        Timestamp = now,
                        Uptime = (now - _start).TotalSeconds
                    })
                })
            };
            await _client.SendAsync(env);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
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
