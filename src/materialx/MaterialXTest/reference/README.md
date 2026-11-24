# Reference outputs of the MaterialX C++ library

`reference.tar.gz` holds what MaterialX 1.39.6 (commit 48106ed), built with
g++ 13.3 on Linux, writes for the 198 documents of `resources/Materials`.
The tests in `Reference.cs` produce the same outputs with the C# port and
require every file to be identical, byte for byte.

| Directory | Written by | Contents |
| --- | --- | --- |
| `gold` | `gold.cpp` | The document written back as XML, the validation result with the node definition and implementation of every node, and the OSL source of every renderable element with the default options. |
| `gold3` | `gold2.cpp` | Reading with comments and newlines, writing without XIncludes, `prettyPrint`, tree and graph functions, values, topological order, dot output, flattening, unit conversion, translation to four shading models, and OSL generation with a reduced interface, with the graph refactoring options, and with the OSL network generator. |
| `genoslnetwork` | `LibsToOso.cpp` | The OSL source of the 810 node shaders and the implementation document for the `genoslnetwork` target. |

The programs were compiled against the installed library, for example

    g++ -std=c++17 -O1 gold2.cpp -Iinstall/include -Linstall/lib \
        -lMaterialXGenOsl -lMaterialXGenShader -lMaterialXFormat -lMaterialXCore -o gold2_gen

and run as `gold_gen <root> <documents> <output>`. `gold3` was written with
the library directory extended by `genoslnetwork`, so that the network
generator finds its implementations. `LibsToOso` was run with `/bin/true` as
the compiler, since only the generated source and the implementation document
are compared.

## The two places where the reference is not the stock library

Both concern an order that the C++ library leaves to its environment. The
patches make that order a fixed one, and the port uses the same.

`GraphElement::flattenSubgraphs` walks a `std::unordered_map` keyed by node
pointer, so the order in which it visits the nodes of a subgraph depends on
where the allocator placed them. For 12 of the 198 documents that order
changes the result: the order of the generated geometry nodes, and which node
a multi-output connection ends on. `flatten-order.patch` makes the walk follow
the order in which the nodes were added. Only the `.flat.xml` files of `gold3`
depend on it.

`FilePath::getFilesInDirectory` and `FilePath::getSubDirectories` return the
entries of a directory in the order the operating system lists them, which
differs between file systems. That order decides the order in which the data
libraries are loaded, and with it the order of the node definitions.
`directory-order.patch` sorts the entries by name. The output of `LibsToOso`
and 106 of the 5,023 files of `gold3` depend on it; `gold` does not.

The port was compared with the stock library as well, on the machine the
reference was written on and with its directory order: every file was
identical except the 12 flattened documents described above.
