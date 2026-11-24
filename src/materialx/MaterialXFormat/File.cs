// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXFormat/File.h and File.cpp.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // File Constants and Functions
    public static partial class Mx
    {
        public static readonly string PATH_LIST_SEPARATOR = OperatingSystem.IsWindows() ? ";" : ":";
        public const string MATERIALX_SEARCH_PATH_ENV_VAR = "MATERIALX_SEARCH_PATH";

        public static FileSearchPath getEnvironmentPath()
        {
            return getEnvironmentPath(PATH_LIST_SEPARATOR);
        }

        public static FileSearchPath getEnvironmentPath(string sep)
        {
            string searchPathEnv = getEnviron(MATERIALX_SEARCH_PATH_ENV_VAR);
            return new FileSearchPath(searchPathEnv, sep);
        }
    }

    // FilePath Definition
    // A value type in the C++; here a class, so the places where the C++ copies
    // a path use the copy constructor.
    public class FilePath
    {
        // FilePath Public Types
        public enum Type
        {
            TypeRelative = 0,
            TypeAbsolute = 1,
            TypeNetwork = 2
        }

        public enum Format
        {
            FormatWindows = 0,
            FormatPosix = 1
        }

        public static readonly Format FormatNative = OperatingSystem.IsWindows() ? Format.FormatWindows : Format.FormatPosix;

        // FilePath Public Methods
        public FilePath()
        {
            _vec = new List<string>();
            _type = Type.TypeRelative;
        }

        public FilePath(FilePath other)
        {
            _vec = new List<string>(other._vec);
            _type = other._type;
        }

        public FilePath(string str)
        {
            assign(str != null ? str : EMPTY_STRING);
        }

        public bool isEqual(FilePath rhs)
        {
            return System.Linq.Enumerable.SequenceEqual(_vec, rhs._vec) &&
                   _type == rhs._type;
        }

        public static implicit operator FilePath(string str)
        {
            return new FilePath(str);
        }

        public static implicit operator string(FilePath path)
        {
            return path.asString();
        }

        public override string ToString()
        {
            return asString();
        }

        public void assign(string str)
        {
            _type = Type.TypeRelative;
            _vec = splitString(str, VALID_SEPARATORS);
            if (str.Length != 0)
            {
                if (str[0] == PREFERRED_SEPARATOR_POSIX)
                {
                    _type = Type.TypeAbsolute;
                }
                else if (str.Length >= 2)
                {
                    if (hasWindowsDriveSpecifier(str))
                    {
                        _type = Type.TypeAbsolute;
                    }
                    else if (str[0] == '\\' && str[1] == '\\')
                    {
                        _type = Type.TypeNetwork;
                    }
                }
            }
        }

        public string asString()
        {
            return asString(FormatNative);
        }

        public string asString(Format format)
        {
            StringBuilder str = new StringBuilder();

            if (format == Format.FormatPosix && isAbsolute())
            {
                // Don't prepend a POSIX separator on a Windows absolute path
                if (_vec.Count == 0 || !hasWindowsDriveSpecifier(_vec[0]))
                {
                    str.Append("/");
                }
            }
            else if (format == Format.FormatWindows && _type == Type.TypeNetwork)
            {
                str.Append("\\\\");
            }

            for (int i = 0; i < _vec.Count; i++)
            {
                str.Append(_vec[i]);
                if (i + 1 < _vec.Count)
                {
                    if (format == Format.FormatPosix)
                    {
                        str.Append(PREFERRED_SEPARATOR_POSIX);
                    }
                    else
                    {
                        str.Append(PREFERRED_SEPARATOR_WINDOWS);
                    }
                }
            }

            return str.ToString();
        }

        public bool isEmpty()
        {
            return _vec.Count == 0;
        }

        public bool isAbsolute()
        {
            return _type != Type.TypeRelative;
        }

        public string getBaseName()
        {
            if (isEmpty())
            {
                return EMPTY_STRING;
            }

            return _vec[_vec.Count - 1];
        }

        public FilePath getParentPath()
        {
            FilePath parent = new FilePath(this);
            if (!parent.isEmpty())
            {
                parent._vec.RemoveAt(parent._vec.Count - 1);
            }
            return parent;
        }

        public string getExtension()
        {
            string baseName = getBaseName();
            int i = baseName.LastIndexOf('.');
            return i != -1 ? baseName.Substring(i + 1) : EMPTY_STRING;
        }

        public void addExtension(string ext)
        {
            assign(asString() + "." + ext);
        }

        public void removeExtension()
        {
            if (!isEmpty())
            {
                string baseName = _vec[_vec.Count - 1];
                int i = baseName.LastIndexOf('.');
                if (i != -1)
                {
                    _vec[_vec.Count - 1] = baseName.Substring(0, i);
                }
            }
        }

        public static FilePath operator /(FilePath lhs, FilePath rhs)
        {
            if (rhs.isAbsolute())
            {
                throw new Exception("Appended path must be relative.");
            }

            FilePath combined = new FilePath(lhs);
            foreach (string str in rhs._vec)
            {
                combined._vec.Add(str);
            }
            return combined;
        }

        public static FilePath operator /(FilePath lhs, string rhs)
        {
            return lhs / new FilePath(rhs);
        }

        public int size()
        {
            return _vec.Count;
        }

        public string this[int index]
        {
            get
            {
                return _vec[index];
            }
        }

        public FilePath getNormalized()
        {
            FilePath res = new FilePath();
            foreach (string str in _vec)
            {
                if (str == CURRENT_PATH_STRING)
                {
                    continue;
                }
                if (str == PARENT_PATH_STRING && !res.isEmpty() && res[res.size() - 1] != PARENT_PATH_STRING)
                {
                    res._vec.RemoveAt(res._vec.Count - 1);
                    continue;
                }
                res._vec.Add(str);
            }
            res._type = _type;
            return res;
        }

        public bool exists()
        {
            string path = asString();
            return File.Exists(path) || Directory.Exists(path);
        }

        public bool isDirectory()
        {
            return Directory.Exists(asString());
        }

        public List<FilePath> getFilesInDirectory(string extension = EMPTY_STRING)
        {
            List<FilePath> files = new List<FilePath>();

            DirectoryInfo dir = openDirectory();
            if (dir != null)
            {
                foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
                {
                    if (!isDirectoryEntry(entry) && (extension.Length == 0 || new FilePath(entry.Name).getExtension() == extension))
                    {
                        files.Add(new FilePath(entry.Name));
                    }
                }
            }

            return files;
        }

        public List<FilePath> getSubDirectories()
        {
            if (!isDirectory())
            {
                return new List<FilePath>();
            }

            List<FilePath> dirs = new List<FilePath>();
            dirs.Add(new FilePath(this));

            DirectoryInfo dir = openDirectory();
            if (dir != null)
            {
                foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
                {
                    string path = entry.Name;
                    if (path == "." || path == "..")
                    {
                        continue;
                    }

                    FilePath newDir = this / path;
                    if (isDirectoryEntry(entry))
                    {
                        List<FilePath> newDirs = newDir.getSubDirectories();
                        dirs.AddRange(newDirs);
                    }
                }
            }

            return dirs;
        }

        public void createDirectory(bool recursive = false)
        {
            if (recursive)
            {
                if (isEmpty() || exists())
                {
                    return;
                }
                FilePath parent = getParentPath();
                if (!parent.isEmpty() && !parent.exists())
                {
                    parent.createDirectory(true);
                }
            }

            // As mkdir: one level, and failure is not reported.
            try
            {
                string path = asString();
                string parentPath = Path.GetDirectoryName(Path.GetFullPath(path));
                if (parentPath == null || Directory.Exists(parentPath))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch (System.Exception)
            {
            }
        }

        public bool removeDirectory(bool recursive = false)
        {
            if (isEmpty())
            {
                return false;
            }

            if (recursive)
            {
                DirectoryInfo dir = openDirectory();
                if (dir != null)
                {
                    foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
                    {
                        string entryName = entry.Name;
                        if (entryName == CURRENT_PATH_STRING || entryName == PARENT_PATH_STRING)
                        {
                            continue;
                        }

                        FilePath entryPath = this / entryName;
                        bool isLink = entry.LinkTarget != null;
                        if ((entry.Attributes & FileAttributes.Directory) != 0 && !isLink)
                        {
                            entryPath.removeDirectory(true);
                        }
                        else
                        {
                            try
                            {
                                entry.Delete();
                            }
                            catch (System.Exception)
                            {
                            }
                        }
                    }
                }
            }

            try
            {
                Directory.Delete(asString(), false);
            }
            catch (System.Exception)
            {
                return false;
            }
            return true;
        }

        public bool setCurrentPath()
        {
            try
            {
                Directory.SetCurrentDirectory(asString());
            }
            catch (System.Exception)
            {
                return false;
            }
            return true;
        }

        public static FilePath getCurrentPath()
        {
            try
            {
                return new FilePath(Directory.GetCurrentDirectory());
            }
            catch (System.Exception e)
            {
                throw new Exception("Error in getCurrentPath: " + e.Message);
            }
        }

        public static FilePath getModulePath()
        {
            string path = Environment.ProcessPath;
            if (path == null)
            {
                throw new Exception("Error in getModulePath: the path of the process is not known");
            }
            return new FilePath(path).getParentPath();
        }

        public static FilePath createTemporaryDirectory()
        {
            return createTemporaryDirectory(new FilePath());
        }

        public static FilePath createTemporaryDirectory(FilePath parentDir)
        {
            FilePath tempParent = parentDir.isEmpty() ? getSystemTemporaryDirectory() : parentDir;
            tempParent.createDirectory(true);
            if (!tempParent.isDirectory())
            {
                throw new Exception("Error in createTemporaryDirectory: parent path is not a directory: " + tempParent.asString());
            }

            const string letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            for (int attempt = 0; attempt < TEMPORARY_DIRECTORY_MAX_ATTEMPTS; attempt++)
            {
                char[] suffix = new char[6];
                for (int i = 0; i < suffix.Length; i++)
                {
                    suffix[i] = letters[Random.Shared.Next(letters.Length)];
                }
                FilePath tempDir = tempParent / (TEMPORARY_DIRECTORY_PREFIX + new string(suffix));
                if (!tempDir.exists())
                {
                    try
                    {
                        Directory.CreateDirectory(tempDir.asString());
                    }
                    catch (System.Exception e)
                    {
                        throw new Exception("Error in createTemporaryDirectory: " + e.Message);
                    }
                    return tempDir;
                }
            }
            throw new Exception("Error in createTemporaryDirectory: no unique name found in " + tempParent.asString());
        }

        public static FilePath getSystemTemporaryDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                return new FilePath(Path.GetTempPath());
            }
            foreach (string envVar in TEMPORARY_DIRECTORY_ENV_VARS)
            {
                string tempDir = getEnviron(envVar);
                if (tempDir.Length != 0)
                {
                    return new FilePath(tempDir);
                }
            }
            return new FilePath(TEMPORARY_DIRECTORY_DEFAULT);
        }

        // FilePath Private Methods
        private static bool hasWindowsDriveSpecifier(string val)
        {
            return val.Length > 1 && ((val[0] >= 'a' && val[0] <= 'z') || (val[0] >= 'A' && val[0] <= 'Z')) && (val[1] == ':');
        }

        private DirectoryInfo openDirectory()
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(asString().Length != 0 ? asString() : ".");
                return dir.Exists ? dir : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        // An entry the directory itself records as a directory (d_type == DT_DIR);
        // a link to a directory is not one.
        private static bool isDirectoryEntry(FileSystemInfo entry)
        {
            return (entry.Attributes & FileAttributes.Directory) != 0 && entry.LinkTarget == null;
        }

        // FilePath Private Members
        private const string VALID_SEPARATORS = "/\\";

        private const char PREFERRED_SEPARATOR_WINDOWS = '\\';
        private const char PREFERRED_SEPARATOR_POSIX = '/';

        private const string CURRENT_PATH_STRING = ".";
        private const string PARENT_PATH_STRING = "..";

        private const string TEMPORARY_DIRECTORY_PREFIX = "materialx_tmp_";
        private const int TEMPORARY_DIRECTORY_MAX_ATTEMPTS = 1000;
        private static readonly string[] TEMPORARY_DIRECTORY_ENV_VARS = new string[] { "TMPDIR", "TMP", "TEMP", "TEMPDIR" };
        private const string TEMPORARY_DIRECTORY_DEFAULT = "/tmp";

        private List<string> _vec;
        private Type _type;
    }

    // FileSearchPath Definition
    public class FileSearchPath : IEnumerable<FilePath>
    {
        // FileSearchPath Public Methods
        public FileSearchPath()
        {
        }

        public FileSearchPath(FileSearchPath other)
        {
            foreach (FilePath path in other._paths)
            {
                _paths.Add(new FilePath(path));
            }
        }

        public FileSearchPath(string searchPath) : this(searchPath, PATH_LIST_SEPARATOR)
        {
        }

        public FileSearchPath(string searchPath, string sep)
        {
            foreach (string path in splitString(searchPath, sep))
            {
                if (path.Length != 0)
                {
                    append(new FilePath(path));
                }
            }
        }

        public static implicit operator FileSearchPath(string searchPath)
        {
            return new FileSearchPath(searchPath);
        }

        public static implicit operator FileSearchPath(FilePath path)
        {
            return new FileSearchPath(path.asString());
        }

        public string asString()
        {
            return asString(PATH_LIST_SEPARATOR);
        }

        public string asString(string sep)
        {
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < _paths.Count; i++)
            {
                str.Append(_paths[i].asString());
                if (i + 1 < _paths.Count)
                {
                    str.Append(sep);
                }
            }
            return str.ToString();
        }

        public void append(FilePath path)
        {
            _paths.Add(new FilePath(path));
        }

        public void append(FileSearchPath searchPath)
        {
            foreach (FilePath path in new List<FilePath>(searchPath._paths))
            {
                _paths.Add(new FilePath(path));
            }
        }

        public void prepend(FilePath path)
        {
            _paths.Insert(0, new FilePath(path));
        }

        public void clear()
        {
            _paths.Clear();
        }

        public int size()
        {
            return _paths.Count;
        }

        public bool isEmpty()
        {
            return _paths.Count == 0;
        }

        public FilePath this[int index]
        {
            get
            {
                return _paths[index];
            }
            set
            {
                _paths[index] = value;
            }
        }

        public FilePath find(FilePath filename)
        {
            if (_paths.Count == 0 || filename.isEmpty())
            {
                return new FilePath(filename);
            }
            if (!filename.isAbsolute())
            {
                foreach (FilePath path in _paths)
                {
                    FilePath combined = path / filename;
                    if (combined.exists())
                    {
                        return combined;
                    }
                }
            }
            return new FilePath(filename);
        }

        public IEnumerator<FilePath> GetEnumerator()
        {
            return _paths.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // FileSearchPath Private Members
        private List<FilePath> _paths = new List<FilePath>();
    }
}
