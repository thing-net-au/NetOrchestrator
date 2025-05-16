using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Orchestrator.IPC
{
    public static class IpcHelpers
    {
        /// <summary>
        /// Repeatedly attempts to connect the given TcpJsonClient until it succeeds or the token is cancelled.
        /// Uses exponential back-off from 1s up to 30s between retries.
        /// </summary>
        public static async Task ConnectWithRetry<T>(
            TcpJsonClient<T> client,
            string clientName,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    logger.LogInformation("[{Client}] Attempting to connect to {Host}:{Port}...", clientName, "","");
                    await client.ConnectAsync();
                    logger.LogInformation("[{Client}] Connected successfully.", clientName);
                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "[{Client}] Connection failed, retrying in {DelaySeconds}s...", clientName, delay.TotalSeconds);
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        // shutdown requested
                        return;
                    }
                    // exponential back-off, capped
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
                }
            }
        }
    }
}
