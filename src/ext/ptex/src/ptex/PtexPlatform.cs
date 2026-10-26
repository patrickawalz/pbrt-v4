// PtexPlatform.h
// Platform-specific classes, functions, and includes. The pthread/Windows mutex and spin
// locks and the compiler atomics map onto System.Threading.

using System;
using System.Threading;

namespace Ptex
{
    // Mutex
    public sealed class Mutex
    {
        private readonly object _mutex = new object();
        public void @lock() { Monitor.Enter(_mutex); }
        public bool trylock() { return Monitor.TryEnter(_mutex); }
        public void unlock() { Monitor.Exit(_mutex); }
    }

    // SpinLock
    public sealed class SpinLock
    {
        private int _spinlock;
        public void @lock()
        {
            SpinWait w = new SpinWait();
            while (Interlocked.CompareExchange(ref _spinlock, 1, 0) != 0)
                w.SpinOnce();
        }
        public bool trylock() { return Interlocked.CompareExchange(ref _spinlock, 1, 0) == 0; }
        public void unlock() { Volatile.Write(ref _spinlock, 0); }
    }

    public static class PtexPlatform
    {
        // Atomics (ATOMIC_ADD32 etc: return the new value)
        public static int AtomicAdd(ref int target, int value) { return Interlocked.Add(ref target, value); }
        public static uint AtomicAdd(ref uint target, uint value) { return Interlocked.Add(ref target, value); }
        public static long AtomicAdd(ref long target, long value) { return Interlocked.Add(ref target, value); }
        public static ulong AtomicAdd(ref ulong target, ulong value) { return Interlocked.Add(ref target, value); }
        public static int AtomicIncrement(ref int target) { return Interlocked.Increment(ref target); }
        public static uint AtomicIncrement(ref uint target) { return Interlocked.Increment(ref target); }
        public static long AtomicIncrement(ref long target) { return Interlocked.Increment(ref target); }
        public static ulong AtomicIncrement(ref ulong target) { return Interlocked.Increment(ref target); }
        public static int AtomicSubtract(ref int target, int value) { return Interlocked.Add(ref target, -value); }
        public static uint AtomicSubtract(ref uint target, uint value) { return Interlocked.Add(ref target, (uint)(-(int)value)); }
        public static long AtomicSubtract(ref long target, long value) { return Interlocked.Add(ref target, -value); }
        public static ulong AtomicSubtract(ref ulong target, ulong value) { return Interlocked.Add(ref target, (ulong)(-(long)value)); }
        public static int AtomicDecrement(ref int target) { return Interlocked.Decrement(ref target); }
        public static uint AtomicDecrement(ref uint target) { return Interlocked.Decrement(ref target); }
        public static long AtomicDecrement(ref long target) { return Interlocked.Decrement(ref target); }
        public static ulong AtomicDecrement(ref ulong target) { return Interlocked.Decrement(ref target); }

        public static bool AtomicCompareAndSwap(ref int target, int oldvalue, int newvalue)
        {
            return Interlocked.CompareExchange(ref target, newvalue, oldvalue) == oldvalue;
        }
        public static bool AtomicCompareAndSwap(ref uint target, uint oldvalue, uint newvalue)
        {
            return Interlocked.CompareExchange(ref target, newvalue, oldvalue) == oldvalue;
        }
        public static bool AtomicCompareAndSwap(ref long target, long oldvalue, long newvalue)
        {
            return Interlocked.CompareExchange(ref target, newvalue, oldvalue) == oldvalue;
        }
        public static bool AtomicCompareAndSwap(ref ulong target, ulong oldvalue, ulong newvalue)
        {
            return Interlocked.CompareExchange(ref target, newvalue, oldvalue) == oldvalue;
        }
        // (reference-typed variant used for the pointer CAS operations of the C++)
        public static bool AtomicCompareAndSwap<T>(ref T target, T oldvalue, T newvalue) where T : class
        {
            return ReferenceEquals(Interlocked.CompareExchange(ref target, newvalue, oldvalue), oldvalue);
        }

        public static void AtomicStore<T>(ref T target, T value)
        {
            Interlocked.MemoryBarrier();
            target = value;
        }
        public static void PtexMemoryFence() { Interlocked.MemoryBarrier(); }

        public const int CACHE_LINE_SIZE = 64;
    }
}
