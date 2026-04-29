using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ILogStreamService _logStream;
        private readonly IEnumerable<IInternalHealth> _internalHealthProviders;
        private readonly IOptions<OrchestratorConfig> _config;

        public Worker(
            ILogger<Worker> logger,
            ILogStreamService logStream,
            IEnumerable<IInternalHealth> internalHealthProviders,
            IOptions<OrchestratorConfig> config)
        {
            _logger = logger;
            _logStream = logStream;
            _internalHealthProviders = internalHealthProviders;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker heartbeat at {Time:O}.", DateTimeOffset.Now);

                // Push fresh InternalStatus from each provider
                foreach (var health in _internalHealthProviders)
                {
                    var status = health.GetStatus();
                    var json = JsonSerializer.Serialize(status);
                    _logStream.Push("InternalStatus", json);
                }

                var intervalMs = _config.Value.Global.HealthCheckInterval;
                if (intervalMs <= 0) intervalMs = GlobalConfig.DefaultHealthCheckIntervalMs;
                await Task.Delay(intervalMs, stoppingToken);
            }
        }
    }
}
