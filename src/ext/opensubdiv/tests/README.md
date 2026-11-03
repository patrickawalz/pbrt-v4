# OpenSubdiv port tests

- `oracle/osd_oracle.cpp`: dumps what the native OpenSubdiv 3.8.0 computes for a mesh (build against a native build:
  `g++ -std=c++17 -O1 -w -I<osd> osd_oracle.cpp <osd>/build/lib/libosdCPU.a -lpthread -o osd_oracle`).
- `gen_meshes.py` writes `meshes/*.txt`; run the oracle on each to produce `expected/*.txt`.
- `osdtest.cs` (project `src/pbrt/cmd/osdtest.csproj`) runs the C# port on every mesh and compares token by token
  (numeric tolerance 2e-6 abs + 2e-5 rel): `dotnet osdtest.dll src/ext/opensubdiv/tests [name-filter]`.
- Legacy Gregory patches (types 6/7) have no basis evaluation, so the dumps print `noeval` for them.
