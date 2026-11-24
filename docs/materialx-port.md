# MaterialX in C#

`src/materialx` is a C# port of the MaterialX library, version 1.39.6
(commit 48106ed). It is written file by file from the C++ with the names
kept, and it is checked against the C++ library byte for byte.

MaterialX is Copyright Contributors to the MaterialX Project and is licensed
under Apache 2.0 (`src/materialx/LICENSE`). pugixml is under the MIT licence
(`MaterialXFormat/External/PugiXML/readme.txt`).

## What is ported

| Module | Files | Lines | Contents |
| --- | --- | --- | --- |
| `MaterialXCore` | 17 | 12,800 | Elements, documents, nodes, definitions, looks, geometry, values, units, traversal, and the upgrade of documents from 1.22 to 1.39 |
| `MaterialXFormat` | 5 | 4,200 | Files and search paths, reading and writing XML, loading libraries; pugixml with the changes MaterialX made to it |
| `MaterialXGenShader` | 23 | 8,500 | Shader graphs, the type system, colour management, units, the graph refactoring passes, shader translation |
| `MaterialXGenOsl` | 6 | 2,300 | The OSL shader generator, the OSL network generator, and the `LibsToOso` tool |
| `MaterialXTest` | 20 | 6,300 | The tests of the four modules above |

The data libraries (`libraries`) and the test documents
(`resources/Materials`, without their images) are those of the same commit.

Build and test:

    dotnet build src/materialx/materialx_lib.csproj -c Release
    dotnet run -c Release --project src/materialx/MaterialXTest

## What is not ported

- **The other shader generators**: GLSL, MSL, Slang, MDL, and the hardware
  module they share (`MaterialXGenHw`).
- **`MaterialXRender`** and everything on it: image loading, the renderers,
  the viewer, the graph editor.
- **The OpenColorIO colour management system** (`OcioColorManagementSystem`,
  `Nodes/OcioNode`). It needs OpenColorIO and is off in a default build of
  MaterialX. The default colour management system is ported.
- **The Python and JavaScript bindings**, and tracing.

## How it follows the C++

One C# file stands for a header and its source file. Classes, methods and
constants have their C++ names. Free functions and global constants are
static members of `MaterialX.Mx`, so `mx::createDocument()` is
`Mx.createDocument()`.

Where C# differs, the port does this:

| C++ | C# |
| --- | --- |
| `shared_ptr<T>` | a reference; a null pointer is `null` |
| `operator==` on elements, paths, iterators | `isEqual`, because `==` on references compares identity, which the C++ uses on pointers |
| `validate(string* message)` | `validate(StringBuilder message)` |
| `getOptions() = options` | `getOptions().assign(options)` |
| iterators: `++it`, `it != end()`, `*it` | `next()`, `isEnd()` or `isEqual(end())`, `getElement()`; they also work in `foreach` |
| a child's weak pointer to its parent | a reference, so an element keeps its document alive and cannot be orphaned by the document going out of scope |
| the macros of `ShaderStage.h` | written out as `if (stage.getName() == ...)` |

Some output of the library depends on details of the C++ standard library,
and the port reproduces them: the order `std::sort` leaves equal elements in
(the generator sorts nodes with it), the text a stream writes for a float,
and the hash that names types.

## How it is checked

`MaterialXTest/reference/reference.tar.gz` holds what the C++ library writes
for the 198 documents of `resources/Materials`. Three tests produce the same
with the port and require every file to be identical:

| Test | Compared | Files |
| --- | --- | --- |
| `Reference.Documents` | the document written back as XML (with the upgrade of old versions), validation messages, the node definition and implementation of every node, the OSL shader of every renderable element | 198 XML, 198 logs, 862 shaders |
| `Reference.Extra` | reading with comments and newlines, writing without XIncludes, tree and graph functions, values, topological order, dot output, flattening, unit conversion, translation to four shading models, OSL generation with other options, the OSL network generator | 5,023 |
| `Reference.LibsToOso` | the 810 node shaders and the implementation document of the `genoslnetwork` target, and what the tool prints | 812 |

All of them are identical. `MaterialXTest/reference/README.md` says how the
reference was written, and names the two places where the C++ library was
patched for it: both are orders that the C++ leaves to the allocator or to
the file system, which the port fixes.

The 73 other tests are the test cases of MaterialXTest for the four modules.
A few had to change, and each says so in a comment marked
`(not in MaterialX)`: parts that upstream runs with the GLSL generator run
with the OSL generator; two tests that need the images and environment maps
of the MaterialX repository use stand-in files; the orphaned-element check
does not apply.

All 862 generated material shaders and all 810 node shaders also compile
with the OSL compiler of this repository (`src/osl/oslc`).
