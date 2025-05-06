using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

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
            while (!stoppingToken.IsCancellationRequested)
            {
                // 1) Your normal work log
                var msg = $"Worker running at: {DateTimeOffset.Now:O}";
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(msg);
                }
                _logStream.Push("Worker", msg);

                // 2) Push fresh InternalStatus from each provider
                foreach (var health in _internalHealthProviders)
                {
                    var status = health.GetStatus();
                    var json = JsonSerializer.Serialize(status);
                    _logStream.Push("InternalStatus", json);
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
