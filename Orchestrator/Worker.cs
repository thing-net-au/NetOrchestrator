using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;

namespace Orchestrator
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ILogStreamService _logStream;
        private readonly IEnumerable<IInternalHealth> _internalHealthProviders;

        public Worker(
            ILogger<Worker> logger,
            ILogStreamService logStream,
            IEnumerable<IInternalHealth> internalHealthProviders)
        {
            _logger = logger;
            _logStream = logStream;
            _internalHealthProviders = internalHealthProviders;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker telemetry loop started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var msg = $"Worker running at: {now:O}";

                _logger.LogInformation("{Message}", msg);
                _logStream.Push("Worker", msg);

                foreach (var health in _internalHealthProviders)
                {
                    var status = health.GetStatus();
                    _logStream.Push("InternalStatus", JsonSerializer.Serialize(status));
                }

                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("Worker telemetry loop stopped.");
        }
    }
}
