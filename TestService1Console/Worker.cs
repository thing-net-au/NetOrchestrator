namespace TestService1Console
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int Repeat = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    Repeat++;
                    _logger.LogInformation("Worker Iteration {Repeat} running at: {time}",Repeat, DateTimeOffset.Now);
                }
                await Task.Delay(500, stoppingToken);
            }
        }
    }
}
