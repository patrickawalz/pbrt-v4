# pbrt-v4 (C# port)

A C# (.NET 10) port of [pbrt-v4](https://github.com/mmp/pbrt-v4), CPU only. The directory layout and file names
follow the C++ sources: `src/pbrt` holds the renderer (a `.h`/`.cpp` pair becomes one `.cs`; `*_test.cpp` becomes
`*_test.cs` in the same directory) and `src/ext` holds the external libraries.

    dotnet build src/pbrt/pbrt_lib.csproj -c Release
    dotnet build src/pbrt/cmd/pbrt_exe.csproj -c Release
    dotnet build src/pbrt/cmd/pbrt_test.csproj -c Release

The RGB-to-spectrum tables (`rgbspectrum_*.cs`, about 38 MB of source each) are not checked in: building
`pbrt_lib` first builds and runs `src/pbrt/cmd/rgb2spec_opt` (as the C++ CMake build does) to generate them into
`src/pbrt/obj/rgbspec/`, and compiles them into the library. Generation takes about a minute; it is repeated only
when `rgb2spec_opt.cs` changes. Compiling the tables needs a few GB of memory.

Not ported: the GPU/OptiX code, the wavefront integrator, the GLFW display server.

pbrt is Copyright 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys, licensed under the Apache License 2.0.
