// (not in pbrt-v4) progressive.cs
//
// RenderSession: what makes a render progressive, and what a front end holds while it runs.
//
// An integrator renders in passes of one sample a pixel and asks the session, before each, whether there is to be another:
// the session says no when its sample count or its time has been reached, or when it has been told to stop. A render always
// ends on a whole pass. A session that a front end made and gave neither a count nor a time renders until it is stopped. A
// render from the command line makes its own session, whose count is the sampler's unless --halt-spp or --halt-time say
// otherwise: that render is pbrt-v4's. Ctrl+C on the command line stops the session, and the image of the passes done is
// written.
//
// The session pauses and resumes the render by the thread pool's PauseTokenSource, and adds and removes its threads.
//
// The session has the one thread that writes the pixel sums of the film. The render threads take their samples into tile
// buffers of their own and hand them over (Film::BeginTile, EndTile); the session's thread adds them to the film
// (Film::MergeTiles). Because nothing else writes those sums, that thread can make an image of the film at any moment
// without stopping anyone, and does at the interval asked for: the image with the settings of the light layers, which a
// viewer takes from LatestImage. Splats go to the film directly, as in pbrt-v4; their sums are atomic.
//
//     front end:    RenderSession session = new RenderSession();  session.HaltSpp = 500;  session.DevelopInterval = 0.5;
//                   RenderSession.Next = session;                 // the next Render() is this session's
//                   (a thread of its own)  RenderCPU(scene);
//                   session.Pause(), Resume(), Stop(), Threads, LatestImage, Passes, Elapsed ...

using System;
using System.Diagnostics;
using System.Threading;

namespace pbrt
{
    using static pbrt;

    // RenderSession Definition
    public sealed class RenderSession : IDisposable
    {
        // The session of the next render, if a front end has made one; a render that finds none makes its own
        public static RenderSession Next;
        // The session of the render that is running, if one is
        public static RenderSession Active;

        // RenderSession Public Members
        // Stop after this many samples a pixel (0: no such limit)
        public int HaltSpp;
        // Stop after this many seconds of rendering, at the end of the pass they run out in (0: no such limit)
        public double HaltTime;
        // Make an image of the film this often, in seconds (0: never, until the end)
        public double DevelopInterval;
        // What the splats of an image are multiplied by, beside the share of the samples taken: 1, but for an integrator whose
        // splats are scaled by a constant of its own
        public Float SplatUnit = 1;
        // Called by the session's thread after it has made an image
        public event Action<RenderSession> Developed;

        // RenderSession Public Methods
        public RenderSession()
        {
            HaltSpp = Options.haltSpp;
            HaltTime = Options.haltTime;
        }

        // (called by the integrator) The render starts: the film, and the sample count of the sampler
        public static RenderSession Begin(Film film, int samplerSpp)
        {
            // A session that a front end made renders until its sample count or its time, and with neither until it is
            // stopped. A render that makes its own session, the command line's, has the sampler's count for one, as in pbrt-v4.
            RenderSession session = Interlocked.Exchange(ref Next, null);
            bool own = session == null;
            if (own)
            {
                session = new RenderSession();
            }
            session.Start(film, own ? samplerSpp : 0);
            Volatile.Write(ref Active, session);
            return session;
        }

        // (called by the integrator) Whether the pass of this number, counted from 0, is to be rendered
        public bool NextPass(int pass)
        {
            if (Volatile.Read(ref stopRequested))
            {
                return false;
            }
            if (haltSpp > 0 && pass >= haltSpp)
            {
                return false;
            }
            if (HaltTime > 0 && Elapsed >= HaltTime)
            {
                return false;
            }
            return true;
        }

        // (called by the integrator) A pass has been rendered
        public void PassDone(int passes)
        {
            Volatile.Write(ref passesDone, passes);
        }

        // (called by the integrator) Everything handed over so far is in the film when this returns
        public void Flush()
        {
            long request = Interlocked.Increment(ref flushRequested);
            wake.Set();
            SpinWait wait = new SpinWait();
            while (Interlocked.Read(ref flushDone) < request)
            {
                wait.SpinOnce();
            }
        }

        // (called by the integrator) The render is over: the film is whole, and the last image is made if images were asked for
        public void End()
        {
            if (filmThread == null)
            {
                return;
            }
            Volatile.Write(ref ending, true);
            wake.Set();
            filmThread.Join();
            filmThread = null;
            clock.Stop();
            Volatile.Write(ref rendering, false);
            Interlocked.CompareExchange(ref Active, null, this);
        }

        public void Pause()
        {
            if (!IsPaused)
            {
                ParallelJob.threadPool.PauseSource.Pause();
                clock.Stop();
            }
        }

        public void Resume()
        {
            if (IsPaused)
            {
                if (IsRendering)
                {
                    clock.Start();
                }
                ParallelJob.threadPool.PauseSource.Resume();
            }
        }

        // The render ends with the pass it is in (a paused render is resumed to finish it)
        public void Stop()
        {
            Volatile.Write(ref stopRequested, true);
            Resume();
        }

        public bool IsPaused
        {
            get { return ParallelJob.threadPool != null && ParallelJob.threadPool.PauseSource.IsPaused; }
        }

        public bool IsRendering
        {
            get { return Volatile.Read(ref rendering); }
        }

        // The number of threads that render, the thread that called Render() among them. Setting it adds threads to the pool
        // or removes them; one that is removed finishes its tile and is parked.
        public int Threads
        {
            get { return RunningThreads(); }
            set
            {
                int wanted = Math.Max(1, value);
                while (RunningThreads() < wanted)
                {
                    ParallelJob.threadPool.AddThread();
                }
                while (RunningThreads() > wanted)
                {
                    ParallelJob.threadPool.RemoveThread();
                }
            }
        }

        // The whole passes rendered
        public int Passes
        {
            get { return Volatile.Read(ref passesDone); }
        }

        // The seconds of rendering, without those of the pauses
        public double Elapsed
        {
            get { return clock.Elapsed.TotalSeconds; }
        }

        public Film Film
        {
            get { return film; }
        }

        // The last image made, and the samples a pixel it had (a fraction while a pass is under way)
        public Image LatestImage
        {
            get { return Volatile.Read(ref latestImage); }
        }

        public double LatestImageSpp
        {
            get { return Volatile.Read(ref latestImageSpp); }
        }

        // Ask for an image now: after a setting of a light layer has changed, for one
        public void RequestDevelop()
        {
            Volatile.Write(ref developRequested, true);
            wake.Set();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~RenderSession()
        {
            Dispose(false);
        }

        // RenderSession Private Methods
        private void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }
            if (disposing)
            {
                End();
                wake.Dispose();
            }
            disposed = true;
        }

        private void Start(Film renderFilm, int samplerSpp)
        {
            film = renderFilm;
            // With no limit asked for, the count given: the sampler's, or none
            haltSpp = HaltSpp > 0 ? HaltSpp : (HaltTime > 0 ? 0 : samplerSpp);
            film.SetLayerCount(LightLayers.Scene.Count);
            Volatile.Write(ref rendering, true);
            clock.Restart();
            filmThread = new Thread(FilmThread);
            filmThread.IsBackground = true;
            filmThread.Name = "film";
            filmThread.Start();
        }

        // The one thread that writes the pixel sums of the film, and makes images of it
        private void FilmThread()
        {
            Stopwatch sinceImage = Stopwatch.StartNew();
            long developedSamples = 0;
            while (true)
            {
                bool last = Volatile.Read(ref ending);
                long flush = Interlocked.Read(ref flushRequested);
                film.MergeTiles();
                Interlocked.Exchange(ref flushDone, flush);
                bool requested = Volatile.Read(ref developRequested);
                // (an image when there are samples that the last one did not have: tiles that were merged, or splats)
                long samples = film.SamplesCounted();
                if (DevelopInterval > 0 && (last || requested || (samples != developedSamples && sinceImage.Elapsed.TotalSeconds >= DevelopInterval)))
                {
                    Volatile.Write(ref developRequested, false);
                    Develop();
                    developedSamples = samples;
                    sinceImage.Restart();
                }
                if (last)
                {
                    return;
                }
                wake.Wait(20);
                wake.Reset();
            }
        }

        private void Develop()
        {
            // The splats are those of the samples taken so far: a share of a pass, while one is under way
            long samples = film.SamplesCounted();
            double spp = (double)samples / film.PixelBounds().Area();
            Float splatScale = samples > 0 ? (Float)(SplatUnit / spp) : 0;
            Image image = film.GetImage(new ImageMetadata(), splatScale, LightLayers.Scene);
            Volatile.Write(ref latestImageSpp, spp);
            Volatile.Write(ref latestImage, image);
            Action<RenderSession> developed = Developed;
            if (developed != null)
            {
                developed(this);
            }
        }

        // RenderSession Private Members
        private Film film;
        private int haltSpp;
        private Thread filmThread;
        private readonly ManualResetEventSlim wake = new ManualResetEventSlim(false);
        private readonly Stopwatch clock = new Stopwatch();
        private bool stopRequested, ending, rendering, developRequested, disposed;
        private int passesDone;
        private long flushRequested, flushDone;
        private Image latestImage;
        private double latestImageSpp;
    }
}
