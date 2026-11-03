# OpenSubdiv 3.8.0 -> C# strict port (Sdc, Vtr, Far)

Source of truth: /tmp/osd/OpenSubdiv/opensubdiv/{sdc,vtr,far}. Apache-2.0 (Pixar); keep the license header of each file
as a comment block at the top of the C# file.

Conventions
- One C# file per C++ .h/.cpp pair, same base name, in src/ext/opensubdiv/{sdc,vtr,far}/ (e.g. vtr/level.cs).
- Namespaces: OpenSubdiv.Sdc, OpenSubdiv.Vtr, OpenSubdiv.Far. Drop the OPENSUBDIV_VERSION / OpenSubdiv::v3_8_0 wrappers.
  Each file starts with `using System; using System.Collections.Generic;` and the needed `using OpenSubdiv.X;`.
- Keep every class, method, field, enum value and argument name EXACTLY as in C++ (same case). Other agents code against
  the C++ headers for APIs they do not own, so names must match the headers 1:1.
- Types: int->int, unsigned->uint, short->short, unsigned short->ushort, unsigned char->byte, bool->bool, float->float,
  double->double, size_t->int where it is an index/count (use long only if needed), Index=int, LocalIndex=ushort,
  Sdc::Crease::Rule etc. are enums. Typedefs `Vtr::Index`, `Vtr::LocalIndex`, `Far::Index`, `Far::LocalIndex`,
  `IndexArray` etc: Index is plain `int` (no wrapper type); LocalIndex is `ushort`.
- Vtr::Array<T>/ConstArray<T> (vtr/array.h): implement as readonly structs wrapping (T[] array, int offset, int size), indexer
  returns `ref T` (ConstArray: `T`), with begin/size/operator[] equivalents (`size()`, `this[i]`, `begin()` -> offset, IsEmpty..).
  typedefs IndexArray = Array<int>, ConstIndexArray = ConstArray<int>, LocalIndexArray = Array<ushort>,
  ConstLocalIndexArray = ConstArray<ushort> are expressed with `using IndexArray = OpenSubdiv.Vtr.Array<int>;` per file.
  `Array<T>` has a pointer-compatible conversion to ConstArray<T> (implicit operator).
- std::vector<T> -> `List<T>`-like semantics: use `T[]` with explicit count when the C++ relies on resize/size, or
  `System.Collections.Generic.List<T>` when code does push_back. Prefer a small shared helper class
  OpenSubdiv.Vtr.StdVector<T> (file vtr/stdvector.cs, owned by the Vtr-core agent) with: Count, this[int] (ref return),
  Resize(n[,val]), Clear(), PushBack(v), Reserve(n), Data (T[]), Array/ConstArray views via helper `Arr()`. All agents may use it:
  `public sealed class StdVector<T> { public T[] Data; public int Count; ... }` with `ref T this[int i]`.
  A vector<int>::operator[] used as an `int*` base pointer for Array wrappers becomes `new Array<int>(v.Data, offset, size)`.
- Pointers into arrays -> (array, offset) pairs. Do not use unsafe pointers except where unavoidable.
- Templates over Real (float/double): generic classes `PrimvarRefinerReal<REAL> where REAL: unmanaged, INumber<REAL>`
  is acceptable via System.Numerics generic math; alternatively write the float and double versions by code generation of the same
  source text (a `Real` alias cannot be generic in C#). The Far agent owning primvarRefiner/stencilTable/patchBasis decides
  (generic math is preferred; keep it exact: no extra rounding differences, use `REAL.CreateChecked`).
- Template member functions taking a "T" with Clear/AddWithWeight (primvarRefiner): use generic constraint on an interface
  `IPrimvar<T> { void Clear(); void AddWithWeight(T src, float weight); }` defined in far/primvarRefiner.cs; the oracle's own
  test uses a simple Vec3 struct implementing it. Since structs are copied, use `ref T` in the generic arrays (T[] + index).
- Behaviour must be IDENTICAL, including integer arithmetic order, sorting (std::sort -> same comparison; use stable
  careful equivalents where result order matters), and floating-point evaluation order. Use `float` ops in float (do not
  silently promote to double: C# `float*float` is float; take care with `1.0f` vs `1.0` literals).
- assert()/ASSERT -> `System.Diagnostics.Debug.Assert`. Far::Error()/Warning() -> throw / console as in far/error.cpp
  (static class Far.Error with ErrorType enum, Error(type,msg), Warning(msg), SetErrorCallback/SetWarningCallback).
- Unsafe is allowed (AllowUnsafeBlocks true) but avoid.
- Static const tables (e.g. patchBasis weights) are `static readonly` arrays.
- Not ported: Bfr, Osd, Far::TopologyRefinerFactory's template-specialisation machinery beyond what Far::TopologyDescriptor
  needs (the factory is ported as an abstract generic `TopologyRefinerFactory<MESH>` with the TopologyDescriptor specialisation
  implemented in topologyDescriptor.cs / topologyRefinerFactory.cs).
- Do NOT modify files owned by another agent. If you need something missing from another module, code against the C++
  API name and note it in your final report.
