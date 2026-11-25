// (not in pbrt-v4) util/pausetoken_test.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using testing;

namespace pbrt
{
    using static testing.Gtest;

    public static class PauseTokenTests
    {
        [TEST("PauseToken", "ATokenOfNoSource")]
        public static void ATokenOfNoSource()
        {
            PauseToken none = default(PauseToken);
            EXPECT_FALSE(none.CanBePaused);
            EXPECT_FALSE(none.IsPaused);
            none.WaitWhilePaused();
            EXPECT_TRUE(none.WaitWhilePausedAsync(CancellationToken.None).IsCompleted);
        }

        [TEST("PauseToken", "PauseAndResume")]
        public static void PauseAndResume()
        {
            using PauseTokenSource source = new PauseTokenSource();
            PauseToken token = source.Token;
            EXPECT_TRUE(token.CanBePaused);
            EXPECT_FALSE(token.IsPaused);
            token.WaitWhilePaused();

            source.Pause();
            source.Pause();
            EXPECT_TRUE(token.IsPaused);
            EXPECT_TRUE(source.IsPaused);
            int passed = 0;
            Thread[] waiters = new Thread[4];
            for (int i = 0; i < waiters.Length; ++i)
            {
                waiters[i] = new Thread(() =>
                {
                    token.WaitWhilePaused();
                    Interlocked.Increment(ref passed);
                });
                waiters[i].Start();
            }
            Task asynchronous = token.WaitWhilePausedAsync(CancellationToken.None);
            Thread.Sleep(100);
            EXPECT_EQ(Volatile.Read(ref passed), 0);
            EXPECT_FALSE(asynchronous.IsCompleted);

            source.Resume();
            foreach (Thread waiter in waiters)
            {
                EXPECT_TRUE(waiter.Join(5000));
            }
            EXPECT_EQ(passed, 4);
            EXPECT_TRUE(asynchronous.Wait(5000));
            EXPECT_FALSE(token.IsPaused);

            // Again: a source is paused and resumed any number of times
            source.Pause();
            EXPECT_TRUE(token.IsPaused);
            source.Resume();
            token.WaitWhilePaused();
        }

        [TEST("PauseToken", "ACancellationEndsAWait")]
        public static void ACancellationEndsAWait()
        {
            using PauseTokenSource source = new PauseTokenSource();
            using CancellationTokenSource stop = new CancellationTokenSource();
            source.Pause();
            bool cancelled = false;
            Thread waiter = new Thread(() =>
            {
                try
                {
                    source.Token.WaitWhilePaused(stop.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
            });
            waiter.Start();
            Thread.Sleep(50);
            stop.Cancel();
            EXPECT_TRUE(waiter.Join(5000));
            EXPECT_TRUE(cancelled);
            EXPECT_TRUE(source.IsPaused);
        }

        [TEST("PauseToken", "LinkedSources")]
        public static void LinkedSources()
        {
            using PauseTokenSource render = new PauseTokenSource();
            using PauseTokenSource thread = new PauseTokenSource();
            using PauseTokenSource both = PauseTokenSource.CreateLinkedTokenSource(render.Token, thread.Token);
            PauseToken token = both.Token;
            EXPECT_FALSE(token.IsPaused);
            render.Pause();
            EXPECT_TRUE(token.IsPaused);
            thread.Pause();
            int passed = 0;
            Thread waiter = new Thread(() =>
            {
                token.WaitWhilePaused();
                Interlocked.Increment(ref passed);
            });
            waiter.Start();
            Task asynchronous = token.WaitWhilePausedAsync(CancellationToken.None);
            Thread.Sleep(50);
            // Paused until neither is
            render.Resume();
            Thread.Sleep(100);
            EXPECT_EQ(Volatile.Read(ref passed), 0);
            EXPECT_TRUE(token.IsPaused);
            EXPECT_FALSE(asynchronous.IsCompleted);
            thread.Resume();
            EXPECT_TRUE(waiter.Join(5000));
            EXPECT_EQ(passed, 1);
            EXPECT_TRUE(asynchronous.Wait(5000));
            // And by its own pause
            both.Pause();
            EXPECT_TRUE(token.IsPaused);
            EXPECT_FALSE(render.IsPaused);
            both.Resume();
            EXPECT_FALSE(token.IsPaused);
        }
    }
}
