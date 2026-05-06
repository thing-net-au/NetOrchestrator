using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.IPC
{
    public static class StreamReaderExtensions
    {
        /// <summary>
        /// Awaits <paramref name="originalTask"/> but throws <see cref="OperationCanceledException"/>
        /// if <paramref name="cancellationToken"/> is signalled first.
        /// </summary>
        public static async Task<string?> WithCancellation(
            this Task<string?> originalTask,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellationToken.Register(
                () => tcs.TrySetCanceled(cancellationToken),
                useSynchronizationContext: false))
            {
                var winner = await Task.WhenAny(originalTask, tcs.Task).ConfigureAwait(false);
                return await winner.ConfigureAwait(false);
            }
        }
    }
}
