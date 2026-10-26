// PtexMutex.h

using System;

namespace Ptex
{
    // Automatically acquire and release lock within enclosing scope (use with `using`).
    public struct AutoMutex : IDisposable
    {
        private readonly Mutex _m;
        public AutoMutex(Mutex m) { _m = m; _m.@lock(); }
        public void Dispose() { _m.unlock(); }
    }

    public struct AutoSpin : IDisposable
    {
        private readonly SpinLock _m;
        public AutoSpin(SpinLock m) { _m = m; _m.@lock(); }
        public void Dispose() { _m.unlock(); }
    }
}
