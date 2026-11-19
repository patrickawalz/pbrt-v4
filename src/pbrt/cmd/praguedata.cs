// (not in pbrt-v4) praguedata: looks into, trims and combines dataset files of the Prague sky model (pragueskymodel.cs).
//
//   praguedata info <file>
//       What the file holds: a line for each dataset in it.
//   praguedata trim <input> <output> [first wavelength, nm] [last wavelength, nm]
//       Writes the input with only the channels that hold a wavelength of the range (by default 360 to 830 nm, what the
//       film samples), and everything else as it is.
//   praguedata combine <output> <input> <input> ...
//       Writes the inputs one after another to one file. A reader of one dataset (the authors' reference implementation)
//       finds the first; the "prague" light takes the dataset that suits it. Only the last may be without polarisation.
//
// The full dataset with the wide channels on the ground, for example:
//   praguedata trim PragueSkyModelDatasetSWIR.dat ground14.dat 280 839
//   praguedata combine PragueSkyFullWide.dat PragueSkyModelDatasetFull.dat ground14.dat

using System;
using System.Globalization;
using System.IO;

public static class PragueDataMain
{
    private static int Usage()
    {
        Console.Error.WriteLine("usage: praguedata info <file>");
        Console.Error.WriteLine("       praguedata trim <input> <output> [first wavelength, nm] [last wavelength, nm]");
        Console.Error.WriteLine("       praguedata combine <output> <input> <input> ...");
        return 1;
    }

    private static void Info(string filename)
    {
        Console.WriteLine(filename + ": " + new FileInfo(filename).Length + " bytes");
        foreach (string line in pbrt.PragueSkyModel.Describe(filename))
        {
            Console.WriteLine("  " + line);
        }
    }

    private static int Trim(string[] argv)
    {
        if (argv.Length < 3 || argv.Length > 5)
        {
            return Usage();
        }
        double first = 360, last = 830;
        if (argv.Length > 3 && !double.TryParse(argv[3], NumberStyles.Float, CultureInfo.InvariantCulture, out first))
        {
            Console.Error.WriteLine(argv[3] + ": not a wavelength");
            return 1;
        }
        if (argv.Length > 4 && !double.TryParse(argv[4], NumberStyles.Float, CultureInfo.InvariantCulture, out last))
        {
            Console.Error.WriteLine(argv[4] + ": not a wavelength");
            return 1;
        }
        if (!(first <= last))
        {
            Console.Error.WriteLine("the first wavelength is above the last");
            return 1;
        }
        if (Path.GetFullPath(argv[1]) == Path.GetFullPath(argv[2]))
        {
            Console.Error.WriteLine("the output is the input itself");
            return 1;
        }
        pbrt.PragueSkyModel.Trim(argv[1], argv[2], first, last);
        Info(argv[2]);
        return 0;
    }

    private static int Combine(string[] argv)
    {
        if (argv.Length < 4)
        {
            return Usage();
        }
        string[] inputs = new string[argv.Length - 2];
        for (int i = 0; i < inputs.Length; ++i)
        {
            inputs[i] = argv[i + 2];
            if (Path.GetFullPath(inputs[i]) == Path.GetFullPath(argv[1]))
            {
                Console.Error.WriteLine("the output is one of the inputs");
                return 1;
            }
        }
        pbrt.PragueSkyModel.Combine(inputs, argv[1]);
        Info(argv[1]);
        return 0;
    }

    public static int Main(string[] argv)
    {
        try
        {
            if (argv.Length == 2 && argv[0] == "info")
            {
                Info(argv[1]);
                return 0;
            }
            if (argv.Length >= 1 && argv[0] == "trim")
            {
                return Trim(argv);
            }
            if (argv.Length >= 1 && argv[0] == "combine")
            {
                return Combine(argv);
            }
            return Usage();
        }
        catch (Exception e) when (e is IOException || e is ArgumentException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
