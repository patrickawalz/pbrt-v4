// (not in pbrt-v4) praguetrim: writes a dataset of the Prague sky model with fewer wavelength channels.
//
//   praguetrim <dataset> <output> [first wavelength, nm] [last wavelength, nm]
//
// The channels that hold a wavelength of the range (by default 360 to 830 nm, what the film samples) are kept, and
// everything else of the dataset as it is: the result has the same format (pragueskymodel.cs).

using System;
using System.Globalization;
using System.IO;

public static class PragueTrimMain
{
    public static int Main(string[] argv)
    {
        if (argv.Length < 2 || argv.Length > 4)
        {
            Console.Error.WriteLine("usage: praguetrim <dataset> <output> [first wavelength, nm] [last wavelength, nm]");
            return 1;
        }
        double first = 360, last = 830;
        if (argv.Length > 2 && !double.TryParse(argv[2], NumberStyles.Float, CultureInfo.InvariantCulture, out first))
        {
            Console.Error.WriteLine(argv[2] + ": not a wavelength");
            return 1;
        }
        if (argv.Length > 3 && !double.TryParse(argv[3], NumberStyles.Float, CultureInfo.InvariantCulture, out last))
        {
            Console.Error.WriteLine(argv[3] + ": not a wavelength");
            return 1;
        }
        if (!(first <= last))
        {
            Console.Error.WriteLine("the first wavelength is above the last");
            return 1;
        }
        if (Path.GetFullPath(argv[0]) == Path.GetFullPath(argv[1]))
        {
            Console.Error.WriteLine("the output is the dataset itself");
            return 1;
        }
        try
        {
            pbrt.PragueSkyModel.Trim(argv[0], argv[1], first, last);
        }
        catch (IOException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        Console.WriteLine(argv[1] + ": " + new FileInfo(argv[1]).Length + " bytes (of " + new FileInfo(argv[0]).Length + ")");
        return 0;
    }
}
