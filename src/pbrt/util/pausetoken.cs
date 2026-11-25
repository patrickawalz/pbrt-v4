// (not in pbrt-v4) util/pausetoken.cs
//
// PauseToken and PauseTokenSource: what CancellationToken and CancellationTokenSource of the .NET framework are for cancelling, for
// pausing. A source is paused and resumed by whoever owns it; its token is handed to the code that is to stop while the source
// is paused, which asks the token and waits on it. A token of no source (default(PauseToken)) is never paused, as
// CancellationToken.None is never cancelled. A linked source is paused while it or any of the tokens it was made from is.
//
// Asking costs one volatile read when nothing is paused.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace pbrt
{
    // PauseToken Definition
    public readonly struct PauseToken
    {
        // PauseToken Public Methods
        internal PauseToken(PauseTokenSource source)
        {
            this.source = source;
        }

        // Whether the token can ever be paused: false for a token of no source
        public bool CanBePaused
        {
            get { return source != null; }
        }

        public bool IsPaused
        {
            get { return source != null && source.IsPaused; }
        }

        // Returns at once when not paused, and otherwise when resumed
        public void WaitWhilePaused()
        {
            if (source != null)
            {
                source.WaitWhilePaused(CancellationToken.None);
            }
        }

        // The same, and a cancellation ends the wait with an OperationCanceledException
        public void WaitWhilePaused(CancellationToken cancellationToken)
        {
            if (source != null)
            {
                source.WaitWhilePaused(cancellationToken);
            }
        }

        public Task WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            if (source == null)
            {
                return Task.CompletedTask;
            }
            return source.WaitWhilePausedAsync(cancellationToken);
        }

        // PauseToken Private Members
        private readonly PauseTokenSource source;
    }

    // PauseTokenSource Definition
    public class PauseTokenSource : IDisposable
    {
        // PauseTokenSource Public Methods
        public PauseTokenSource()
        {
            linked = null;
        }

        // A source that is paused while it is itself, or while either of the two tokens is
        public static PauseTokenSource CreateLinkedTokenSource(PauseToken token1, PauseToken token2)
        {
            PauseTokenSource source = new PauseTokenSource();
            source.linked = new PauseToken[] { token1, token2 };
            return source;
        }

        public PauseToken Token
        {
            get { return new PauseToken(this); }
        }

        public bool IsPaused
        {
            get
            {
                if (Volatile.Read(ref paused))
                {
                    return true;
                }
                if (linked != null)
                {
                    for (int i = 0; i < linked.Length; ++i)
                    {
                        if (linked[i].IsPaused)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public void Pause()
        {
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (paused)
                {
                    return;
                }
                resumedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                resumed.Reset();
                Volatile.Write(ref paused, true);
            }
        }

        public void Resume()
        {
            TaskCompletionSource<bool> task;
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!paused)
                {
                    return;
                }
                Volatile.Write(ref paused, false);
                resumed.Set();
                task = resumedTask;
                resumedTask = null;
            }
            task.SetResult(true);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~PauseTokenSource()
        {
            Dispose(false);
        }

        // PauseTokenSource Protected Methods
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }
            if (disposing)
            {
                // (nothing is left waiting on a source that is no more)
                TaskCompletionSource<bool> task;
                lock (sync)
                {
                    disposed = true;
                    Volatile.Write(ref paused, false);
                    resumed.Set();
                    task = resumedTask;
                    resumedTask = null;
                }
                if (task != null)
                {
                    task.SetResult(true);
                }
                resumed.Dispose();
            }
            disposed = true;
        }

        // PauseTokenSource Internal Methods
        internal void WaitWhilePaused(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (Volatile.Read(ref paused))
                {
                    try
                    {
                        resumed.Wait(cancellationToken);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    continue;
                }
                // Not paused itself: wait on a token it is linked to that is, and look again
                bool waited = false;
                if (linked != null)
                {
                    for (int i = 0; i < linked.Length; ++i)
                    {
                        if (linked[i].IsPaused)
                        {
                            linked[i].WaitWhilePaused(cancellationToken);
                            waited = true;
                        }
                    }
                }
                if (!waited)
                {
                    return;
                }
            }
        }

        internal async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task task = null;
                lock (sync)
                {
                    if (paused && resumedTask != null)
                    {
                        task = resumedTask.Task;
                    }
                }
                if (task != null)
                {
                    await task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                bool waited = false;
                if (linked != null)
                {
                    for (int i = 0; i < linked.Length; ++i)
                    {
                        if (linked[i].IsPaused)
                        {
                            await linked[i].WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                            waited = true;
                        }
                    }
                }
                if (!waited)
                {
                    return;
                }
            }
        }

        // PauseTokenSource Private Members
        private readonly object sync = new object();
        private readonly ManualResetEventSlim resumed = new ManualResetEventSlim(true);
        private TaskCompletionSource<bool> resumedTask;
        private PauseToken[] linked;
        private bool paused;
        private bool disposed;
    }
}
