using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    /// <summary>
    /// Coordinates pause and cancellation requests between a run view and module boundaries.
    /// </summary>
    internal sealed class RunControl : IDisposable
    {
        private readonly object sync = new object();
        private readonly ManualResetEventSlim continueGate = new ManualResetEventSlim(true);
        private TaskCompletionSource<bool> pauseCompletion;
        private bool paused;
        private bool cancellationRequested;

        internal bool IsPaused
        {
            get
            {
                lock (sync)
                    return paused;
            }
        }

        internal bool IsCancellationRequested
        {
            get
            {
                lock (sync)
                    return cancellationRequested;
            }
        }

        internal void Pause()
        {
            lock (sync)
            {
                if (cancellationRequested || paused)
                    return;

                paused = true;
                continueGate.Reset();
                pauseCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        internal void Resume()
        {
            lock (sync)
            {
                paused = false;
                continueGate.Set();
                pauseCompletion?.TrySetResult(true);
                pauseCompletion = null;
            }
        }

        internal void RequestCancellation()
        {
            lock (sync)
            {
                cancellationRequested = true;
                paused = false;
                continueGate.Set();
                pauseCompletion?.TrySetResult(false);
                pauseCompletion = null;
            }
        }

        /// <summary>
        /// Blocks only at a module boundary. Returns false when no further module may start.
        /// </summary>
        internal bool WaitIfPaused()
        {
            if (IsCancellationRequested)
                return false;

            continueGate.Wait();
            return !IsCancellationRequested;
        }

        /// <summary>
        /// Asynchronously waits at a module boundary without blocking the UI message loop.
        /// </summary>
        internal Task<bool> WaitIfPausedAsync()
        {
            lock (sync)
            {
                if (cancellationRequested)
                    return Task.FromResult(false);

                return paused
                    ? pauseCompletion.Task
                    : Task.FromResult(true);
            }
        }

        public void Dispose()
        {
            continueGate.Dispose();
        }
    }
}
