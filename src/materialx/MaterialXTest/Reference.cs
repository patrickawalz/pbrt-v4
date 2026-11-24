// (not in MaterialX) Reference.cs: compares the port with the outputs of the
// MaterialX C++ library that reference/reference.tar.gz holds. Every file
// must be identical. reference/README.md says how the outputs were written.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using MaterialX;
using testing;
using static MaterialXTest.Catch;

public static class ReferenceTests
{
    private static string sourceDirectory([CallerFilePath] string file = "")
    {
        return Path.GetDirectoryName(file);
    }

    // The root of the port: the directory that holds libraries and resources.
    private static string materialXRoot()
    {
        return Path.GetDirectoryName(sourceDirectory());
    }

    private static string _referenceDirectory;

    // Unpack the reference outputs once into a temporary directory.
    private static string referenceDirectory()
    {
        if (_referenceDirectory == null)
        {
            string directory = FilePath.createTemporaryDirectory().asString();
            using (FileStream file = File.OpenRead(Path.Combine(sourceDirectory(), "reference", "reference.tar.gz")))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gzip, directory, false);
            }
            _referenceDirectory = directory;
        }
        return _referenceDirectory;
    }

    private static void copyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.GetDirectories(source))
        {
            copyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    // A program that accepts any arguments and writes nothing: LibsToOso runs
    // the compiler through the shell and only reads the error file it leaves.
    private static string standInCompiler()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.SystemDirectory, "hostname.exe");
        }
        return "/bin/true";
    }

    // The documents are named relative to the root, as they were when the
    // reference was written: the order of the source URIs of a document
    // follows the spelling of the paths.
    private static int runFromRoot(Func<int> run)
    {
        string current = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(materialXRoot());
        try
        {
            return run();
        }
        finally
        {
            Directory.SetCurrentDirectory(current);
        }
    }

    [TEST("Reference", "Documents")]
    public static void Reference_Documents()
    {
        string gold = Path.Combine(referenceDirectory(), "gold");
        int result = runFromRoot(() => MtlxGold.Main(new string[] { materialXRoot(), Path.Combine("resources", "Materials"), gold }));
        REQUIRE(result == 0);
    }

    [TEST("Reference", "LibsToOso")]
    public static void Reference_LibsToOso()
    {
        string reference = Path.Combine(referenceDirectory(), "genoslnetwork");
        string output = FilePath.createTemporaryDirectory().asString();
        string osos = Path.Combine(output, "osos");
        Directory.CreateDirectory(osos);

        TextWriter console = Console.Out;
        StringWriter captured = new StringWriter();
        captured.NewLine = "\n";
        int result;
        Console.SetOut(captured);
        try
        {
            result = LibsToOso.Main(new string[]
            {
                "--path", materialXRoot(),
                "--outputOsoPath", osos,
                "--outputMtlxPath", output,
                "--libraryRelativeOsoPath", "libraries/targets/genoslnetwork/osos",
                "--oslCompilerPath", standInCompiler(),
                "--oslIncludePath", Path.Combine(materialXRoot(), "libraries", "stdlib", "genosl", "include")
            });
        }
        finally
        {
            Console.SetOut(console);
        }
        REQUIRE(result == 0);
        REQUIRE(captured.ToString() == File.ReadAllText(Path.Combine(referenceDirectory(), "LibsToOso.out")));

        int compared = 0;
        foreach (string file in Directory.GetFiles(reference, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(reference, file);
            string produced = Path.Combine(output, relative);
            REQUIRE(File.Exists(produced));
            bool identical = File.ReadAllText(produced) == File.ReadAllText(file);
            if (!identical)
            {
                INFO("differs: " + relative);
            }
            CHECK(identical);
            compared++;
        }
        REQUIRE(compared == 811);
        REQUIRE(Directory.GetFiles(osos, "*.osl").Length == 810);

        REQUIRE(new FilePath(output).removeDirectory(true));
    }

    [TEST("Reference", "Extra")]
    public static void Reference_Extra()
    {
        // The library directory with the implementations of the genoslnetwork
        // target added, as LibsToOso writes them.
        string root = FilePath.createTemporaryDirectory().asString();
        copyDirectory(Path.Combine(materialXRoot(), "libraries"), Path.Combine(root, "libraries"));
        copyDirectory(Path.Combine(referenceDirectory(), "genoslnetwork"), Path.Combine(root, "libraries", "targets", "genoslnetwork"));

        string gold = Path.Combine(referenceDirectory(), "gold3");
        int result = runFromRoot(() => Extra.run(root, Path.Combine("resources", "Materials"), gold, null));
        REQUIRE(result == 0);

        REQUIRE(new FilePath(root).removeDirectory(true));
    }
}
