# Open Shading Language in C# (`src/osl`)

A port of OpenShadingLanguage (commit 4483c00, 2026-10-30) in the style of the pbrt-v4 port: the C++ is followed file by file and
function by function, with its names, its structure and its two stages (`oslc` writes `.oso`, the runtime loads and optimizes it). Where
the C++ uses LLVM, the port has a .NET code generator instead. Improvements over the C++ are additions that are off unless asked for;
with them off the results are those of the C++.

## Mapping

| C++ | C# | Notes |
|---|---|---|
| `liboslcomp` (11k lines): `osllex.l`, `oslgram.y`, `ast`, `symtab`, `typecheck`, `codegen`, `oslcomp` | `src/osl/oslcomp` | flex and bison are replaced by the lexer port (done) and a parser built from the same grammar; clang's preprocessor by a C preprocessor of its own |
| `oslc` | `oslc` command | writes `.oso` exactly as the C++ does, so compiled shaders of either side load in the other |
| `liboslexec` (72k lines): `osolex.l`, `osogram.y` (the `.oso` reader), `shadingsys`, `instance`, `master`, `runtimeoptimize`, `constfold`, `closure`, the `op*.cpp` | `src/osl/oslexec` | the optimizer and constant folder are ported unchanged (they work on the instruction list, not on LLVM) |
| `llvm_gen.cpp`, `llvm_instance.cpp`, `llvm_util.cpp`, `llvm_ops.cpp`, `backendllvm`, the batched backend | `src/osl/oslexec/il` | System.Reflection.Emit: see below |
| `include/OSL` (19k lines), `liboslnoise`, `liboslquery` | `src/osl`, `liboslnoise` | `Dual<T>` stays (it is also the reference for the autodiff option) |
| OpenImageIO pieces used (`TypeDesc`, `ustring`, `Strutil`, `Filesystem`, texture system) | `src/osl/oiio` | only what OSL calls; texture lookups go to the host through `RendererServices` as in OSL |
| `testshade`, `testrender`, `testsuite` | `src/osl/testshade`, tests | the OSL testsuite's reference outputs are the tests |

## The .NET backend

The instruction list that `runtimeoptimize` leaves is translated to IL, as `llvm_gen.cpp` translates it to LLVM IR: one method per shader
group layer, symbols as locals, the closure and renderer calls as calls to static methods (the port of `llvm_ops.cpp`, `opnoise`,
`opcolor`, ...). Two targets share the emitter:

* **In memory**: `DynamicMethod` / `AssemblyBuilder.DefineDynamicAssembly`, for rendering (the JIT that LLVM is in the C++).
* **To disk**: `PersistedAssemblyBuilder` writes the shader group as a `.dll` with the entry points `osl_group_<name>_init` and
  `osl_group_<name>_run`, that a program loads later with no compile step (the way `.oso` is the form between the two stages, the `.dll`
  is the form after the third).

## Order of work

1. Foundations: `TypeDesc`, string interning, vector, color and matrix types, `Dual<T>`.
2. **oslcomp**: lexer (done, `osllex_test.cs`), grammar and parser, preprocessor, `ast`, `symtab`, `typecheck`, `codegen`, the `.oso` writer, `oslc`. Check: every shader of `src/shaders` and the testsuite compiles to the same `.oso` as the C++ oslc.
3. **oslexec front**: the `.oso` reader, masters and instances, shader groups and connections, `ShadingSystem`, constant folding and runtime optimization. Check: the optimized instruction list matches the C++ one (`--debug` dumps).
4. **IL backend** and the runtime library (math, color, matrix, noise, string, texture and renderer services, closures). Check: the testsuite's reference images and text output.
5. `testshade` and `testrender` ports for the checks.

## Optional additions (off by default)

* **Derivatives**: exact forward-mode derivatives in place of the finite-difference duals, for `Dx`/`Dy`, bump and texture filtering.
* **Spectral and RGB**: colors can be spectra (the RGB-to-spectrum tables already in `pbrt_lib`); RGB textures are upsampled to spectra on read, so operations written on colors, among them the Photoshop-style blend modes (multiply, screen, overlay, soft light, ...), are evaluated on the spectrum.
* **Magi integration** (`src/osl/magi`, its own project): closures evaluate to Magi's BSDF and Material (spectral, polarised), shaders can supply volumes, lights and displacement through the unified Material class.
* **Performance**: batched shading with `System.Runtime.Intrinsics` in the spirit of OSL's batched mode.

## Decisions taken without asking

* The grammar of `oslgram.y` is kept as a grammar, not rewritten as recursive descent: the parser is table driven from the same productions, with bison's precedence and conflict resolution (the mid-rule actions that open scopes are the reason to keep the structure).
* A C preprocessor is part of the port (oslc uses clang's): `#include`, `#define` with arguments, conditionals, `#pragma`, line directives.
* Strings are interned `string`s (`ustring`); `ustringhash` is the hash of the same.
