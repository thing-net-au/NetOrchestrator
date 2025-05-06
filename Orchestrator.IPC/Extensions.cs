using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orchestrator.IPC
{
    public static class StreamReaderExtensions
    {
        public static async Task<string> WithCancellation(
            this Task<string> originalTask,
            CancellationToken cancellationToken)
        {
            // If the original task completes first, return its result.
            // If the cancellationToken fires first, throw.
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Register cancellation to cancel the TCS
            using (cancellationToken.Register(
                () => tcs.TrySetCanceled(cancellationToken),
                useSynchronizationContext: false))
            {
                // Wait for whichever completes first
                var winner = await Task.WhenAny(originalTask, tcs.Task).ConfigureAwait(false);
                return await winner.ConfigureAwait(false);
            }
        }
    }
}
