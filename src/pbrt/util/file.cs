// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        private static string searchDirectory = "";

        // File and Filename Functions
        public static void SetSearchDirectory(string filename)
        {
            string path = filename;
            if (!Directory.Exists(path))
                path = Path.GetDirectoryName(path) ?? "";
            searchDirectory = path;
        }

        private static bool IsAbsolutePath(string filename)
        {
            if (filename.Length == 0)
                return false;
            return Path.IsPathRooted(filename);
        }

        // Mirrors filesystem::path::extension(): the text after the last '.', without the dot.
        private static string PathExtension(string filename)
        {
            string name = Path.GetFileName(filename);
            if (name == ".." || name == ".")
                return "";
            int pos = name.LastIndexOf('.');
            if (pos < 0)
                return "";
            return name.Substring(pos + 1);
        }

        public static bool HasExtension(string filename, string e)
        {
            string ext = e;
            if (ext.Length != 0 && ext[0] == '.')
                ext = ext.Substring(1);

            string filenameExtension = PathExtension(filename);
            if (ext.Length > filenameExtension.Length)
                return false;
            return filenameExtension.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }

        public static string RemoveExtension(string filename)
        {
            string ext = PathExtension(filename);
            if (ext.Length == 0)
                return filename;
            return filename.Substring(0, filename.Length - ext.Length - 1);
        }

        public static string ResolveFilename(string filename)
        {
            if (searchDirectory.Length == 0 || filename.Length == 0 || IsAbsolutePath(filename))
                return filename;

            string filepath = Path.Combine(searchDirectory, filename);
            if (File.Exists(filepath) || Directory.Exists(filepath))
                return Path.GetFullPath(filepath);
            return filename;
        }

        public static List<string> MatchingFilenames(string filenameBase)
        {
            List<string> filenames = new List<string>();

            string dirStr = Path.GetDirectoryName(filenameBase) ?? "";
            string baseName = Path.GetFileName(filenameBase);
            if (dirStr.Length == 0)
                dirStr = ".";
            if (!Directory.Exists(dirStr))
                ErrorExit($"{dirStr}: unable to open directory\n");

            foreach (string f in Directory.EnumerateFiles(dirStr))
            {
                string n = Path.GetFileName(f);
                if (n.StartsWith(baseName, StringComparison.Ordinal))
                    filenames.Add(Path.Combine(Path.GetDirectoryName(filenameBase) ?? "", n));
            }
            return filenames;
        }

        public static bool FileExists(string filename)
        {
            try
            {
                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                    return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool RemoveFile(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                    return false;
                File.Delete(filename);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ReadFileContents(): the text of the file. (std::string holds raw bytes in C++; use
        // ReadFileBytes() where the contents are binary.)
        public static string ReadFileContents(string filename)
        {
            return Encoding.UTF8.GetString(ReadFileBytes(filename));
        }

        public static byte[] ReadFileBytes(string filename)
        {
            try
            {
                return File.ReadAllBytes(filename);
            }
            catch (Exception e)
            {
                ErrorExit($"{filename}: {e.Message}");
                return null;
            }
        }

        public static byte[] ReadDecompressedFileBytes(string filename)
        {
            byte[] compressed = ReadFileBytes(filename);
            CHECK_GT(compressed.Length, 4);
            try
            {
                using (MemoryStream input = new MemoryStream(compressed))
                using (GZipStream gz = new GZipStream(input, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    gz.CopyTo(output);
                    byte[] decompressed = output.ToArray();
                    LOG_VERBOSE($"Decompressed {filename} from {compressed.Length} to {decompressed.Length} bytes");
                    return decompressed;
                }
            }
            catch (InvalidDataException)
            {
                ErrorExit($"{filename}: invalid or corrupt compressed data");
                return null;
            }
        }

        public static string ReadDecompressedFileContents(string filename)
        {
            return Encoding.UTF8.GetString(ReadDecompressedFileBytes(filename));
        }

        public static FileStream FOpenRead(string filename)
        {
            try
            {
                return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static FileStream FOpenWrite(string filename)
        {
            try
            {
                return new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<Float> ReadFloatFile(string filename)
        {
            FileStream f = FOpenRead(filename);
            if (f == null)
            {
                Error($"{filename}: unable to open file");
                return new List<Float>();
            }

            int c;
            bool inNumber = false;
            char[] curNumber = new char[32];
            int curNumberPos = 0;
            int lineNumber = 1;
            List<Float> values = new List<Float>();
            while ((c = f.ReadByte()) != -1)
            {
                if (c == '\n')
                    ++lineNumber;
                if (inNumber)
                {
                    if (curNumberPos >= curNumber.Length)
                        LOG_FATAL($"Overflowed buffer for parsing number in file: {filename} at line {lineNumber}");
                    // Note: this is not very robust, and would accept something
                    // like 0.0.0.0eeee-+--2 as a valid number.
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' ||
                        c == '+')
                    {
                        CHECK_LT(curNumberPos, curNumber.Length);
                        curNumber[curNumberPos++] = (char)c;
                    }
                    else
                    {
                        string number = new string(curNumber, 0, curNumberPos);
                        Float v;
                        if (!Atof(number, &v))
                            ErrorExit($"{filename}: unable to parse float value \"{number}\"");
                        values.Add(v);
                        inNumber = false;
                        curNumberPos = 0;
                    }
                }
                else
                {
                    if ((c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+')
                    {
                        inNumber = true;
                        curNumber[curNumberPos++] = (char)c;
                    }
                    else if (c == '#')
                    {
                        while ((c = f.ReadByte()) != '\n' && c != -1)
                            ;
                        ++lineNumber;
                    }
                    else if (!(c == ' ' || (c >= '\t' && c <= '\r')))
                    {
                        Error($"{filename}: unexpected character \"{(char)c}\" found at line {lineNumber}.");
                        return new List<Float>();
                    }
                }
            }
            f.Close();
            return values;
        }

        public static bool WriteFileContents(string filename, string contents)
        {
            try
            {
                File.WriteAllBytes(filename, Encoding.UTF8.GetBytes(contents));
                return true;
            }
            catch (Exception e)
            {
                Error($"{filename}: {e.Message}");
                return false;
            }
        }
    }
}
