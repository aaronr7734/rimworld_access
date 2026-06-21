#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// Marshals work from the dev-bridge's background HTTP thread onto RimWorld's main
    /// thread. Unity/RimWorld game state (Find.*, DefDatabase, .Translate()) is not
    /// thread-safe; touching it from the HttpListener thread crashes or returns garbage.
    /// The HTTP handler enqueues a delegate via <see cref="RunOnMainThread"/> and blocks
    /// until <see cref="DrainPending"/> (called every frame from UIRootOnGUI) runs it.
    /// Debug-only: the whole DevBridge module is excluded from Release builds.
    /// </summary>
    internal static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

        /// <summary>Drain all pending work. MUST be called on the main thread.</summary>
        internal static void DrainPending()
        {
            while (queue.TryDequeue(out Action action))
            {
                // Each action captures its own errors and signals its waiter; a throw here
                // would only happen if signalling itself failed, so swallow to keep draining.
                try { action(); }
                catch { /* intentionally ignored - see above */ }
            }
        }

        /// <summary>
        /// Run <paramref name="func"/> on the main thread and block the calling thread until
        /// it completes, returning its result. Rethrows any exception the delegate raised.
        /// Times out if the main thread never drains (e.g. game frozen) so the HTTP request
        /// fails fast instead of hanging the listener forever.
        /// </summary>
        internal static T RunOnMainThread<T>(Func<T> func, int timeoutSeconds = 30)
        {
            using (var done = new ManualResetEventSlim(false))
            {
                T result = default(T);
                Exception captured = null;

                queue.Enqueue(() =>
                {
                    try { result = func(); }
                    catch (Exception e) { captured = e; }
                    finally { done.Set(); }
                });

                if (!done.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                    throw new TimeoutException(
                        "Dev bridge eval timed out - the main thread did not drain the queue. " +
                        "Is the game frozen, or is UIRootOnGUI not running?");

                if (captured != null)
                    throw captured;

                return result;
            }
        }
    }
}
#endif
