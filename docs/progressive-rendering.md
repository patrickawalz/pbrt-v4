# Progressive rendering and light layers (design draft, not in pbrt-v4)

Branch `pbr-progressive`, cut from `pbr-materials` at `35574a7`. Nothing here is implemented yet.

The aim is the smallest change that makes every integrator render progressively, without the film or the integrators losing
the feel of pbrt-v4:

- a render that runs until told to stop: by a sample count, by a time, or by the user;
- pause and resume, through a `PauseToken` / `PauseTokenSource` pair modelled on `CancellationToken` / `CancellationTokenSource`;
- threads added and removed while it runs;
- a film that can be looked at and written at any moment, with no lock that a render thread ever waits on;
- light layers: the image kept as one buffer per group of lights, rebalanced after (or during) the render.

LuxRender is the reference for the features (`Pause`, `Resume`, `AddThread`, `RemoveThread`, `haltspp`, `halttime`, light groups
with a scale, a colour and a temperature each, and per-thread contribution buffers drained into the film). Its code is GPL and
was read for structure only; the classes below are pbrt-v4's own, extended.

## 1. What changes, and what does not

| | pbrt-v4 today | here |
|---|---|---|
| `Film` interface | `AddSample`, `AddSplat`, `GetPixelRGB`, `GetImage`, `WriteImage` | the same; one overload of `AddSplat` with a layer, and a merge call used by the film thread |
| `Li` and the integrators' sampling code | returns one `SampledSpectrum` | unchanged; one extra line where emitted light is added, and only if layers are wanted |
| `Integrator::Render` | waves of 1, 2, 4 ... 64 samples to `spp`, then write | passes of one sample until a stop condition, same sample indices |
| `ParallelFor`, `ParallelFor2D`, `ThreadLocal` | fixed threads, one job list under a mutex | same calls; ranges are stolen between threads, threads come and go, pause is inside the pool |
| scene files | | nothing new in the grammar: `"string layer"` on lights, and three options |

A scene rendered from the command line with no new option gives the image it gives now, bit for bit: the sample indices, the
order in which a pixel's samples are added, and the arithmetic are the same. That is the first test.

## 2. PauseToken and PauseTokenSource

`src/pbrt/util/pausetoken.cs`. The shape of the .NET cancellation pair, with "paused" a state that can be left again.

```csharp
public readonly struct PauseToken
{
    public bool IsPaused { get; }
    public bool CanBePaused { get; }                     // false for default(PauseToken), as CancellationToken.None
    public void WaitWhilePaused();                       // returns at once when not paused
    public void WaitWhilePaused(CancellationToken cancellationToken);   // a stop ends a pause
    public Task WaitWhilePausedAsync(CancellationToken cancellationToken);
}

public sealed class PauseTokenSource : IDisposable
{
    public PauseToken Token { get; }
    public bool IsPaused { get; }
    public void Pause();
    public void Resume();
    public static PauseTokenSource CreateLinkedTokenSource(PauseToken token1, PauseToken token2);   // paused while either is
    public void Dispose();                               // the standard dispose pattern
}
```

Inside: a volatile flag for the fast path (one read when nothing is paused), a `ManualResetEventSlim` for the threads that
wait, and a `TaskCompletionSource` for the asynchronous wait. A linked source is how a thread that has been removed stays
parked: its token is the render's token linked with its own.

## 3. The thread pool

`ThreadPool` in `util/parallel.cs` keeps its public face. Three things change inside it.

**Stealing instead of one list.** Today every chunk of a `ParallelFor` is taken under the pool's mutex. Each loop instead gives
every worker a range `[begin, end)` of its own, held in one atomic 64-bit word. A worker takes chunks from the front of its
range; a worker that has run out takes the back half of another's with one compare-and-swap. No deque of tasks is needed:
every job pbrt has is a loop over a range (`ParallelFor2D` is a loop over tiles). The mutex and the condition variable remain
only for sleeping when there is no loop at all.

**Threads come and go.** `AddThread()` starts a worker. It has no range, so its first act is to steal one: that is the whole
of "spooling up the render", and no integrator knows it happened. `ThreadLocal<T>` already makes its value on a thread's first
use, so samplers and scratch buffers follow. `RemoveThread()` pauses one worker's own `PauseTokenSource`; the worker parks at
its next chunk boundary and what is left of its range stays where the others can steal it, so no work is lost. A removed
thread is a parked thread; `AddThread()` resumes a parked one before it starts a new one.

**Pause is in the pool.** Between chunks a worker calls `token.WaitWhilePaused(stop)`. Pausing the render is then pausing one
token, for every integrator that renders through `ParallelFor`, with no code in the integrators. The cost while running is one
volatile read per chunk.

A chunk is a tile of one pass, so pause, stop and thread removal all take effect within one tile's worth of work.

## 4. The render loop

`ImageTileIntegrator::Render` keeps its structure. The wave loop becomes

```csharp
for (int pass = 0; !session.Finished(pass); ++pass)
{
    ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
    {
        // as now: for each pixel of the tile, StartPixelSample(pPixel, pass), EvaluatePixelSample(...)
    });
}
```

and the rest (the display server hook, the progress reporter, the final `WriteImage`) stays. The `spp` of the sampler becomes
the default stop condition, so nothing changes for a scene that asks for nothing new.

`RenderSession` (`src/pbrt/progressive.cs`) is the one new object. It owns the stop `CancellationTokenSource`, the render
`PauseTokenSource`, the stop conditions and the film thread, and is what a front end holds:

```csharp
public sealed class RenderSession : IDisposable
{
    public void Start();  public void Pause();  public void Resume();  public void Stop();  public void Wait();
    public int Threads { get; set; }                     // AddThread / RemoveThread until it matches
    public int HaltSpp { get; set; }                     // 0: none
    public double HaltTime { get; set; }                 // seconds of rendering, pauses not counted; 0: none
    public int Passes { get; }  public double Elapsed { get; }  public double SamplesPerSecond { get; }
    public Image Develop();                              // the latest finished image, see section 5
    public LightLayers Layers { get; }
}
```

Stop conditions are checked by the film thread, which cancels the stop token; whichever comes first ends the render. Scene and
command line: `Option "integer haltspp"`, `Option "float halttime"`, `--halt-spp`, `--halt-time`. `pbrt_exe` gets a few keys
(pause, resume, more threads, fewer, write now, stop) and the existing `--display-server` shows the film as it fills.

The other integrators:

- **Light tracer, BDPT**: already tile loops; the same change.
- **MLT and its relatives**: the bootstrap runs once and the chains are kept. A pass is one mutation per pixel shared among the
  chains, so the chain loop becomes the same `for pass` loop and the image is `b / passes` times the splats.
- **SPPM**: left as it is, or removed, as decided earlier.

Samplers: a render without an end needs a sampler that has sample `n` for any `n`. `independent` does (and is the only one
used here); `halton` and `sobol` do; the ones built for a fixed count (`stratified`, `pmj02bn`, `zsobol`) stop at their count.

## 5. The film: buffers in, one thread out

pbrt's film has two kinds of sum, and they are already different:

- **Pixel samples** (`AddSample`): `rgbSum` and `weightSum`, plain doubles. Safe today only because a tile belongs to one
  thread. Not safe to read while a tile is being written: a reader can see the sum of a sample without its weight.
- **Splats** (`AddSplat`): atomic doubles, added from any thread to any pixel. Always safe to read.

The design keeps both and adds one thread.

**Render threads write a tile buffer, not the film.** A worker renders its tile for the pass into a small buffer of its own:
`rgbSum` and `weightSum` for each pixel of the tile and each layer. `AddSample` is the call it has always been; the film puts
the sample in the calling thread's tile buffer. When the tile is done the buffer goes on a lock-free queue and the worker
takes an empty one from a pool. It never waits.

**The film thread is the only writer of the pixel sums.** It takes buffers off the queue and adds them into the film. Because
it is alone, the pixel sums are always whole, and it can develop them into an image without stopping anyone. It also checks
the stop conditions, and on a timer develops the display image and writes the partial file. A finished image is published by
swapping a reference, so a viewer reads it without a lock too.

**Splats stay as they are.** They go straight into the film's atomic sums. Sending every splat through one thread would make
that thread the limit of an MLT or light-tracing render, where each sample is a splat over a filter's footprint; the atomics
are already lock-free and already safe to read. What the film thread adds is their normalisation: workers count the paths
they have finished in an atomic counter, and the splat scale is the pixel count over that counter, at any moment and not only
at the end of a pass.

What a reader sees: the pixel-sample part is exact at all times. The splat part is exact when the render is paused or
stopped, and while running differs only by the splats of the paths in flight. A written file from a paused or finished
render is exact; a checkpoint meant for resuming pauses for one tile's time first.

The cost is one addition per pixel per pass on the film thread, and a pool of tile buffers (two per thread).

A smaller alternative was considered: leave the workers writing the film directly and give each tile a version counter that
is odd while it is written (a seqlock), so a reader retries a tile that moved under it. It needs no queue and no buffers. It
is not proposed because the buffers are worth having for their own sake: the tile buffer is exactly what a second machine
would send, so the queue is where the planned render farm joins, and a single writer keeps every film type simple.

## 6. Light layers

**Scene.** A light, an area light or an emissive medium takes `"string layer" "name"`. pbrt-v4's `Attribute "light" "string
layer" "key"` then sets it for all the lights that follow in the graphics state, which is LuxRender's `LightGroup` directive
without a new directive. A light with no layer is in the layer `default`. `Light` gets an `int layer`.

**Film.** A pixel holds its sums once per layer; one layer costs what the film costs now. `LightLayers` holds, per layer, a
name, `enabled`, a scale, an RGB tint and a colour temperature, all of which can change while rendering or afterwards. The
developed image is the sum over layers of each layer times its factor. The EXR carries `R G B` (the developed sum) and
`name.R name.G name.B` for each layer, unscaled.

**Integrators.** `Li` still returns one spectrum. Where an integrator adds emitted light it knows the light, and says so:

```csharp
L += beta * Le;                         // as now
LightLayers.Add(light, beta * Le);      // new; returns at once when the scene has one layer
```

`LightLayers.Add` writes to a per-thread scratch, one spectrum per layer, cleared at `StartPixelSample`. `AddSample` reads
the scratch. Whatever part of `L` was not attributed goes to `default`, so an integrator that has not been touched still
renders correctly, into one layer, and the integrators can be done one at a time. The path tracer has three such places
(lights at infinity, an emitter hit, the light sample); volpath has two more for media. A splat is one path with one light,
so it carries its layer: `AddSplat(p, L, lambda, layer)`.

**The clamp.** `maxComponentValue` is applied to the whole sample as now, and the same factor to every layer, so the layers
at unit scale sum to the image pbrt would have written.

Polarisation, the spectral film and the G-buffer film take the same layering; `RGBFilm` is first.

## 7. Order of work

1. `PauseToken` / `PauseTokenSource`, with tests.
2. The pool: range stealing, `AddThread` / `RemoveThread`, pause. Test: every index visited once while threads are added,
   removed and paused at random.
3. `RenderSession`, the pass loop, the stop conditions, for `ImageTileIntegrator`. Test: bit-identical to today at a fixed spp.
4. Tile buffers and the film thread; display and partial writes from it.
5. Light layers: film, `"layer"`, path and volpath. Test: layers sum to the unlayered image; each layer equals a render with
   only its lights.
6. BDPT, light tracer, MLT.

Steps 1 to 3 are a usable progressive renderer; 4 makes looking at it safe; 5 is the layers.

## 8. Towards a polished production renderer

In rough order of value, each on top of the above without changing it:

- **Checkpoint and resume.** Write the film's sums, the pass count and the layer table; a later run adds to them. With
  `independent` sampling a resumed render needs nothing else. MLT also needs its chains and its bootstrap constant.
- **A noise stop condition.** Keep a second set of pixel sums for the odd passes; the difference between the two halves
  estimates the error per pixel, which gives a "stop below this noise" condition and a convergence display. It is a stop
  condition only: steering samples towards noisy pixels is adaptive sampling, which is biased unless done with care, and is
  left out.
- **Denoising outputs.** `GBufferFilm` already has albedo, normal and variance; progressive use needs them developed with the
  image.
- **First light fast.** The first passes at a fraction of the resolution, or tiles in a spiral from the centre, so the first
  picture arrives in under a second. Display only; the film is unaffected.
- **The same image for any thread count.** Already true for pixel samples (the sample index decides the random numbers).
  For MLT it needs chains tied to an index and not to a thread.
- **The render farm.** Tile buffers and splat buffers from other machines enter through the film thread's queue; the film is
  already a sum of samples that can be merged.
- **A front end.** The session object is all a viewer needs: `Develop`, the layer table, the tokens. The WPF application
  discussed before sits on it; `pbrt_exe` with keys and the display server is enough until then.
- **Scene edits while rendering.** Changing a light's power in a layer is free (it is a layer scale). Anything else is stop,
  change, reset the film, start: a restart, not a new mechanism.

## 9. Open questions

1. A removed thread is parked, not ended. Is that what is wanted, or should it exit and free its scratch memory?
2. When a time limit ends a render mid-pass, the unfinished pass is kept (the weights make it valid). Or should a render
   always end on a whole pass?
3. Layers for emissive media and for the sky models: one layer each by default, or `default`?
4. SPPM: leave it non-progressive, or remove it on this branch?
