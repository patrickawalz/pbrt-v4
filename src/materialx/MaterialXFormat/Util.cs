// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXFormat/Util.h and Util.cpp.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MaterialX
{
    // Format Utility Functions
    public static partial class Mx
    {
        public static string readFile(FilePath filePath)
        {
            try
            {
                if (File.Exists(filePath.asString()))
                {
                    // The bytes of the file as they are: one character per byte is not
                    // assumed, the text is read as UTF-8 as the rest of the port does.
                    return File.ReadAllText(filePath.asString(), new UTF8Encoding(false));
                }
            }
            catch (System.Exception)
            {
            }
            return EMPTY_STRING;
        }

        public static void getSubdirectories(List<FilePath> rootDirectories, FileSearchPath searchPath, List<FilePath> subDirectories)
        {
            foreach (FilePath root in rootDirectories)
            {
                FilePath rootPath = searchPath.find(root);
                if (rootPath.exists())
                {
                    List<FilePath> childDirectories = rootPath.getSubDirectories();
                    subDirectories.AddRange(childDirectories);
                }
            }
        }

        public static void loadDocuments(FilePath rootPath, FileSearchPath searchPath, StringSet skipFiles,
                                         StringSet includeFiles, List<Document> documents, List<string> documentsPaths,
                                         XmlReadOptions readOptions = null, List<string> errors = null)
        {
            foreach (FilePath dir in rootPath.getSubDirectories())
            {
                foreach (FilePath file in dir.getFilesInDirectory(MTLX_EXTENSION))
                {
                    if (!skipFiles.Contains(file.asString()) &&
                        (includeFiles.Count == 0 || includeFiles.Contains(file.asString())))
                    {
                        Document doc = createDocument();
                        FilePath filePath = dir / file;
                        try
                        {
                            FileSearchPath readSearchPath = new FileSearchPath(searchPath);
                            readSearchPath.append(dir);
                            readFromXmlFile(doc, filePath, readSearchPath, readOptions);
                            documents.Add(doc);
                            documentsPaths.Add(filePath.asString());
                        }
                        catch (Exception e)
                        {
                            if (errors != null)
                            {
                                errors.Add("Failed to load: " + filePath.asString() + ". Error: " + e.what());
                            }
                        }
                    }
                }
            }
        }

        public static void loadLibrary(FilePath file, Document doc, FileSearchPath searchPath = null, XmlReadOptions readOptions = null)
        {
            Document libDoc = createDocument();
            readFromXmlFile(libDoc, file, searchPath, readOptions);
            doc.importLibrary(libDoc);
        }

        public static StringSet loadLibraries(List<FilePath> libraryFolders,
                                              FileSearchPath searchPath,
                                              Document doc,
                                              StringSet excludeFiles = null,
                                              XmlReadOptions readOptions = null)
        {
            if (excludeFiles == null)
            {
                excludeFiles = new StringSet();
            }

            // Append environment path to the specified search path.
            FileSearchPath librarySearchPath = new FileSearchPath(searchPath);
            librarySearchPath.append(getEnvironmentPath());

            StringSet loadedLibraries = new StringSet();
            if (libraryFolders.Count == 0)
            {
                // No library folders specified so scan in all search paths
                foreach (FilePath libraryPath in librarySearchPath)
                {
                    foreach (FilePath path in libraryPath.getSubDirectories())
                    {
                        foreach (FilePath filename in path.getFilesInDirectory(MTLX_EXTENSION))
                        {
                            if (!excludeFiles.Contains(filename.asString()))
                            {
                                FilePath file = path / filename;
                                if (!loadedLibraries.Contains(file.asString()))
                                {
                                    loadLibrary(file, doc, searchPath, readOptions);
                                    loadedLibraries.Add(file.asString());
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Look for specific library folders in the search paths
                foreach (FilePath libraryName in libraryFolders)
                {
                    FilePath libraryPath = librarySearchPath.find(libraryName);
                    foreach (FilePath path in libraryPath.getSubDirectories())
                    {
                        foreach (FilePath filename in path.getFilesInDirectory(MTLX_EXTENSION))
                        {
                            if (!excludeFiles.Contains(filename.asString()))
                            {
                                FilePath file = path / filename;
                                if (!loadedLibraries.Contains(file.asString()))
                                {
                                    loadLibrary(file, doc, searchPath, readOptions);
                                    loadedLibraries.Add(file.asString());
                                }
                            }
                        }
                    }
                }
            }
            return loadedLibraries;
        }

        public static void flattenFilenames(Document doc, FileSearchPath searchPath = null, StringResolver customResolver = null)
        {
            if (searchPath == null)
            {
                searchPath = new FileSearchPath();
            }

            foreach (Element elem in doc.traverseTree())
            {
                ValueElement valueElem = elem.asA<ValueElement>();
                if (valueElem == null || valueElem.getType() != FILENAME_TYPE_STRING)
                {
                    continue;
                }

                FilePath unresolvedValue = new FilePath(valueElem.getValueString());
                if (unresolvedValue.isEmpty())
                {
                    continue;
                }
                StringResolver elementResolver = elem.createStringResolver();
                // If the path is already absolute then don't allow an additional prefix
                // as this would make the path invalid.
                if (unresolvedValue.isAbsolute())
                {
                    elementResolver.setFilePrefix(EMPTY_STRING);
                }
                string resolvedString = valueElem.getResolvedValueString(elementResolver);

                // Convert relative to absolute pathing if the file is not already found
                if (!searchPath.isEmpty())
                {
                    FilePath resolvedValue = new FilePath(resolvedString);
                    if (!resolvedValue.isAbsolute())
                    {
                        for (int i = 0; i < searchPath.size(); i++)
                        {
                            FilePath testPath = searchPath[i] / resolvedValue;
                            testPath = testPath.getNormalized();
                            if (testPath.exists())
                            {
                                resolvedString = testPath.asString();
                                break;
                            }
                        }
                    }
                }

                // Apply any custom filename resolver
                if (customResolver != null && StringResolver.isResolvedType(FILENAME_TYPE_STRING))
                {
                    resolvedString = customResolver.resolve(resolvedString, FILENAME_TYPE_STRING);
                }

                valueElem.setValueString(resolvedString);
            }

            // Remove any file prefix attributes
            foreach (Element elem in doc.traverseTree())
            {
                if (elem.hasFilePrefix())
                {
                    elem.removeAttribute(Element.FILE_PREFIX_ATTRIBUTE);
                }
            }
        }

        public static FileSearchPath getSourceSearchPath(Document doc)
        {
            StringSet pathSet = new StringSet();
            foreach (Element elem in doc.traverseTree())
            {
                if (elem.hasSourceUri())
                {
                    FilePath sourceFilename = new FilePath(elem.getSourceUri());
                    pathSet.Add(sourceFilename.getParentPath().asString());
                }
            }

            FileSearchPath searchPath = new FileSearchPath();
            foreach (string path in pathSet)
            {
                searchPath.append(new FilePath(path));
            }

            return searchPath;
        }

        public static FileSearchPath getDefaultDataSearchPath()
        {
            FilePath REQUIRED_LIBRARY_FOLDER = new FilePath("libraries/targets");
            FilePath currentPath = FilePath.getModulePath();

            FileSearchPath searchPath = new FileSearchPath();
            while (!currentPath.isEmpty())
            {
                if ((currentPath / REQUIRED_LIBRARY_FOLDER).exists())
                {
                    searchPath.append(new FileSearchPath(currentPath.asString()));
                    break;
                }
                currentPath = currentPath.getParentPath();
            }

            return searchPath;
        }
    }
}
