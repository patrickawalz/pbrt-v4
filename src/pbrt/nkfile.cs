// (not in pbrt-v4) nkfile.cs
//
// Files of measured complex indices of refraction (n and k over the spectrum), in the two layouts that the collections of
// such data use, read for "spectrum eta" and "spectrum k" (and any other spectrum parameter):
//
//   Sopra (also the .ior files of Maxwell Render): four numbers, then the values.
//       unit  first  last  intervals        unit: 1 eV, 2 micrometres, 3 1/cm, 4 nanometres
//       n k n k ...                         intervals + 1 pairs, evenly spaced from `first` to `last` in that unit
//     The values are separated by spaces, line ends, commas or semicolons, in any arrangement. A file with `intervals` pairs
//     instead of intervals + 1 (the fourth number taken as the count of the points) is read as such, with a warning.
//
//   Tables (luxpop.com, the tables of FreeSnell, most data copied from a handbook): one row for each point,
//       abscissa  n  k  [anything else]
//     with comments after ';' or '#'. The unit of the abscissa is the one named in the lines before the data ("eV", "um",
//     "nm", "cm-1", "Angstrom" or "(A)"); without one it is the angstrom of luxpop.com, unless only nanometres put the data
//     in the visible range. The rows can be in any order; a repeated abscissa keeps its first row.
//
// In the scene a file is named where a spectrum is, with ":n" or ":k" for the column:
//     "spectrum eta" "gold.nk:n" "spectrum k" "gold.nk:k"
// Without it, a parameter whose name ends in "k" takes k and any other takes n. Files are recognised by the extensions .nk
// and .ior. The spectrum is the table joined by lines in the wavelength; where the table does not reach the ends of the
// visible range its last value is held (with a warning), so that an index is never zero there.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace pbrt
{
    public enum IndexFileUnit
    {
        ElectronVolts = 1,
        Micrometres = 2,
        Wavenumber = 3,
        Nanometres = 4,
        Angstroms = 5
    }

    public sealed class IndexFile
    {
        // The wavelengths in nanometres, increasing, and the two parts of the index at each
        public Float[] wavelengths, n, k;
        // "sopra" or "table", and the unit of the abscissa in the file
        public string layout;
        public IndexFileUnit unit;
        // What the reader had to assume or repair
        public List<string> warnings = new List<string>();
    }

    public static class IndexFiles
    {
        public static bool IsIndexFile(string name)
        {
            string file = StripColumn(name, out bool _, out bool _);
            return file.EndsWith(".nk", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".ior", StringComparison.OrdinalIgnoreCase);
        }

        // The name of the file without ":n" or ":k"
        public static string StripColumn(string name, out bool wantsN, out bool wantsK)
        {
            wantsN = name.EndsWith(":n", StringComparison.OrdinalIgnoreCase);
            wantsK = name.EndsWith(":k", StringComparison.OrdinalIgnoreCase);
            return wantsN || wantsK ? name.Substring(0, name.Length - 2) : name;
        }

        static readonly SortedDictionary<string, IndexFile> cache = new SortedDictionary<string, IndexFile>(StringComparer.Ordinal);

        // The spectrum of a column of the file for the parameter `parameterName`; errors end the program with the file and
        // the line.
        public static Spectrum Spectrum(string name, string parameterName, FileLoc loc, Allocator alloc)
        {
            string file = pbrt.ResolveFilename(StripColumn(name, out bool wantsN, out bool wantsK));
            bool k = wantsK || (!wantsN && parameterName.EndsWith("k", StringComparison.OrdinalIgnoreCase));
            IndexFile data;
            lock (cache)
            {
                if (!cache.TryGetValue(file, out data))
                {
                    string text = null;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch (Exception e)
                    {
                        pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to read the file (%s)", file, e.Message));
                    }
                    try
                    {
                        data = Parse(text);
                    }
                    catch (FormatException e)
                    {
                        pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: %s", file, e.Message));
                    }
                    foreach (string warning in data.warnings)
                    {
                        pbrt.Warning(loc, pbrt.StringPrintf("%s: %s", file, warning));
                    }
                    cache[file] = data;
                }
            }
            return new PiecewiseLinearSpectrum(data.wavelengths, k ? data.k : data.n, alloc);
        }

        // The wavelength in nanometres of an abscissa
        public static double ToNanometres(double value, IndexFileUnit unit)
        {
            switch (unit)
            {
            case IndexFileUnit.ElectronVolts:
                // (h c in eV nm)
                return 1239.841984 / value;
            case IndexFileUnit.Micrometres:
                return value * 1000;
            case IndexFileUnit.Wavenumber:
                return 1e7 / value;
            case IndexFileUnit.Angstroms:
                return value * 0.1;
            default:
                return value;
            }
        }

        struct Row
        {
            public int line;
            public List<double> numbers;
        }

        static bool Number(string token, out double value)
        {
            // (Fortran writes exponents with D)
            token = token.Replace('D', 'E').Replace('d', 'e');
            return Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static readonly char[] separators = new char[] { ' ', '\t', ',', ';', '\r' };

        // The numbers of a line, or null if it holds anything else
        static List<double> Numbers(string text)
        {
            List<double> numbers = new List<double>();
            foreach (string token in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Number(token, out double value))
                {
                    return null;
                }
                numbers.Add(value);
            }
            return numbers;
        }

        static bool Has(string header, string word)
        {
            return header.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // The unit named in the lines before the data, if any
        static bool UnitOf(string header, out IndexFileUnit unit)
        {
            unit = IndexFileUnit.Angstroms;
            // (the first word of a table that starts with the names of its columns, as "eV n k", decides)
            foreach (string word in header.Split(new char[] { ' ', '\t', '\n', '\r', ',', ';', '#', ':', '=', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = word.Trim('(', ')').ToLowerInvariant();
                if (w == "ev")
                {
                    unit = IndexFileUnit.ElectronVolts;
                    return true;
                }
                if (w == "um" || w == "µm" || w == "μm" || w == "micron" || w == "microns" || w == "micrometer" || w == "micrometers" || w == "micrometre" ||
                    w == "micrometres")
                {
                    unit = IndexFileUnit.Micrometres;
                    return true;
                }
                if (w == "nm" || w == "nanometer" || w == "nanometers" || w == "nanometre" || w == "nanometres")
                {
                    unit = IndexFileUnit.Nanometres;
                    return true;
                }
                if (w == "cm-1" || w == "cm^-1" || w == "1/cm" || w == "wavenumber" || w == "wavenumbers")
                {
                    unit = IndexFileUnit.Wavenumber;
                    return true;
                }
                if (w == "a" && word.StartsWith("(") || w == "å" || w == "angstrom" || w == "angstroms" || w == "ångström")
                {
                    unit = IndexFileUnit.Angstroms;
                    return true;
                }
            }
            return false;
        }

        public static IndexFile Parse(string text)
        {
            // The lines: numbers, or text (comments and names of columns) that may name the unit
            List<Row> rows = new List<Row>();
            string header = "";
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; ++i)
            {
                string line = lines[i];
                int hash = line.IndexOf('#');
                string comment = "";
                if (hash >= 0)
                {
                    comment = line.Substring(hash + 1);
                    line = line.Substring(0, hash);
                }
                // A semicolon separates values in a Sopra file and starts a comment in a table: it is a comment if what
                // follows is not numbers
                int semicolon = line.IndexOf(';');
                if (semicolon >= 0 && Numbers(line.Substring(semicolon + 1)) == null)
                {
                    comment = line.Substring(semicolon + 1) + " " + comment;
                    line = line.Substring(0, semicolon);
                }
                List<double> numbers = Numbers(line);
                if (numbers == null)
                {
                    if (rows.Count != 0)
                    {
                        throw new FormatException(pbrt.StringPrintf("line %d: \"%s\" is not numbers", i + 1, line.Trim()));
                    }
                    header += line + "\n";
                    numbers = new List<double>();
                }
                if (rows.Count == 0)
                {
                    header += comment + "\n";
                }
                if (numbers.Count != 0)
                {
                    Row row = new Row();
                    row.line = i + 1;
                    row.numbers = numbers;
                    rows.Add(row);
                }
            }
            if (rows.Count == 0)
            {
                throw new FormatException("no data in the file");
            }
            foreach (Row row in rows)
            {
                foreach (double value in row.numbers)
                {
                    if (Double.IsNaN(value) || Double.IsInfinity(value))
                    {
                        throw new FormatException(pbrt.StringPrintf("line %d: a value is not finite", row.line));
                    }
                }
            }

            IndexFile file = new IndexFile();
            List<double> lambda = new List<double>(), n = new List<double>(), k = new List<double>();

            // Sopra: four numbers, the first a unit and the last a count, and after them that many pairs (or one more)
            List<double> first = rows[0].numbers;
            int remaining = 0;
            for (int i = 1; i < rows.Count; ++i)
            {
                remaining += rows[i].numbers.Count;
            }
            bool sopra = first.Count == 4 && first[0] == Math.Floor(first[0]) && first[0] >= 1 && first[0] <= 4 && first[3] == Math.Floor(first[3]) && first[3] >= 1 &&
                         (remaining == 2 * ((int)first[3] + 1) || remaining == 2 * (int)first[3]);
            if (!sopra && first.Count == 4 && first[0] == Math.Floor(first[0]) && first[0] >= 1 && first[0] <= 4 && first[3] == Math.Floor(first[3]) && first[3] >= 1 &&
                rows.Count > 1 && rows[1].numbers.Count == 2)
            {
                throw new FormatException(pbrt.StringPrintf("the first line announces %d intervals (%d pairs of n and k) but %d values follow", (int)first[3],
                                                            (int)first[3] + 1, remaining));
            }
            if (sopra)
            {
                file.layout = "sopra";
                file.unit = (IndexFileUnit)(int)first[0];
                int intervals = (int)first[3];
                int points = remaining / 2;
                if (points == intervals)
                {
                    file.warnings.Add(pbrt.StringPrintf("%d pairs of n and k for %d intervals (one short): the fourth number is taken as the count of the points",
                                                        points, intervals));
                }
                if (first[1] <= 0 || first[2] <= 0)
                {
                    throw new FormatException("line " + rows[0].line + ": the range of the spectrum is not positive");
                }
                List<double> values = new List<double>(remaining);
                for (int i = 1; i < rows.Count; ++i)
                {
                    values.AddRange(rows[i].numbers);
                }
                for (int i = 0; i < points; ++i)
                {
                    double x = points == 1 ? first[1] : first[1] + (first[2] - first[1]) * i / (points - 1);
                    lambda.Add(ToNanometres(x, file.unit));
                    n.Add(values[2 * i]);
                    k.Add(values[2 * i + 1]);
                }
            }
            else
            {
                file.layout = "table";
                List<double> abscissa = new List<double>();
                foreach (Row row in rows)
                {
                    if (row.numbers.Count < 3)
                    {
                        throw new FormatException(pbrt.StringPrintf("line %d: %d values where a row of a table has three (abscissa, n, k)", row.line,
                                                                    row.numbers.Count));
                    }
                    if (row.numbers[0] <= 0)
                    {
                        throw new FormatException(pbrt.StringPrintf("line %d: the abscissa is not positive", row.line));
                    }
                    abscissa.Add(row.numbers[0]);
                    n.Add(row.numbers[1]);
                    k.Add(row.numbers[2]);
                }
                if (!UnitOf(header, out file.unit))
                {
                    // No unit named: the angstrom of luxpop.com, unless only nanometres put the data in the visible range
                    file.unit = IndexFileUnit.Angstroms;
                    bool asAngstroms = false, asNanometres = false;
                    foreach (double x in abscissa)
                    {
                        asAngstroms |= x * 0.1 >= pbrt.Lambda_min && x * 0.1 <= pbrt.Lambda_max;
                        asNanometres |= x >= pbrt.Lambda_min && x <= pbrt.Lambda_max;
                    }
                    if (!asAngstroms && asNanometres)
                    {
                        file.unit = IndexFileUnit.Nanometres;
                    }
                    file.warnings.Add("no unit is named before the data: the abscissa is taken as " + (file.unit == IndexFileUnit.Angstroms ? "angstroms" : "nanometres"));
                }
                foreach (double x in abscissa)
                {
                    lambda.Add(ToNanometres(x, file.unit));
                }
            }

            // In the order of the wavelength, a repeated wavelength keeping its first row
            int count = lambda.Count;
            int[] order = new int[count];
            for (int i = 0; i < count; ++i)
            {
                order[i] = i;
            }
            Array.Sort(order, (int a, int b) => lambda[a] != lambda[b] ? lambda[a].CompareTo(lambda[b]) : a.CompareTo(b));
            List<Float> wavelengths = new List<Float>(), ns = new List<Float>(), ks = new List<Float>();
            int repeated = 0, negative = 0;
            foreach (int i in order)
            {
                if (wavelengths.Count != 0 && (Float)lambda[i] == wavelengths[wavelengths.Count - 1])
                {
                    ++repeated;
                    continue;
                }
                if (n[i] < 0 || k[i] < 0)
                {
                    ++negative;
                }
                wavelengths.Add((Float)lambda[i]);
                ns.Add((Float)n[i]);
                // (k is written with either sign, for the two conventions of the complex index)
                ks.Add((Float)Math.Abs(k[i]));
            }
            if (repeated != 0)
            {
                file.warnings.Add(pbrt.StringPrintf("%d repeated wavelengths: the first row of each is kept", repeated));
            }
            if (negative != 0)
            {
                file.warnings.Add(pbrt.StringPrintf("%d rows with a negative n or k: k is taken without its sign", negative));
            }
            // The ends of the visible range: the last value is held, since outside of its table a spectrum is zero
            if (wavelengths[0] > pbrt.Lambda_min || wavelengths[wavelengths.Count - 1] < pbrt.Lambda_max)
            {
                file.warnings.Add(pbrt.StringPrintf("the data cover %f to %f nm, not all of %f to %f nm: the values at the ends are held", wavelengths[0],
                                                    wavelengths[wavelengths.Count - 1], pbrt.Lambda_min, pbrt.Lambda_max));
                if (wavelengths[0] > pbrt.Lambda_min)
                {
                    wavelengths.Insert(0, pbrt.Lambda_min);
                    ns.Insert(0, ns[0]);
                    ks.Insert(0, ks[0]);
                }
                if (wavelengths[wavelengths.Count - 1] < pbrt.Lambda_max)
                {
                    wavelengths.Add(pbrt.Lambda_max);
                    ns.Add(ns[ns.Count - 1]);
                    ks.Add(ks[ks.Count - 1]);
                }
            }
            file.wavelengths = wavelengths.ToArray();
            file.n = ns.ToArray();
            file.k = ks.ToArray();
            return file;
        }
    }
}
