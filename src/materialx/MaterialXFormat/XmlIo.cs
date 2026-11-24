// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXFormat/XmlIo.h and XmlIo.cpp.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MaterialX.PugiXml;
using static MaterialX.Mx;

namespace MaterialX
{
    // XmlReadFunction Definition
    public delegate void XmlReadFunction(Document doc, FilePath filename, FileSearchPath searchPath, XmlReadOptions readOptions);

    // XmlReadOptions Definition
    public class XmlReadOptions
    {
        // XmlReadOptions Public Methods
        public XmlReadOptions()
        {
            readComments = false;
            readNewlines = false;
            upgradeVersion = true;
            readXIncludeFunction = readFromXmlFile;
            parentXIncludes = new List<string>();
        }

        public XmlReadOptions(XmlReadOptions other)
        {
            readComments = other.readComments;
            readNewlines = other.readNewlines;
            upgradeVersion = other.upgradeVersion;
            readXIncludeFunction = other.readXIncludeFunction;
            parentXIncludes = new List<string>(other.parentXIncludes);
        }

        // XmlReadOptions Public Members

        // If true, then XML comments will be read into documents as comment elements. Defaults to false.
        public bool readComments;

        // If true, then XML newlines will be read into documents as newline elements. Defaults to false.
        public bool readNewlines;

        // If true, then documents from earlier versions of MaterialX will be upgraded to the current version. Defaults to true.
        public bool upgradeVersion;

        // If provided, this function will be invoked when an XInclude reference needs to be read into a document.
        // Defaults to readFromXmlFile.
        public XmlReadFunction readXIncludeFunction;

        // The vector of parent XIncludes at the scope of the current document. Defaults to an empty vector.
        public List<string> parentXIncludes;
    }

    // XmlWriteOptions Definition
    public class XmlWriteOptions
    {
        // XmlWriteOptions Public Methods
        public XmlWriteOptions()
        {
            writeXIncludeEnable = true;
        }

        // XmlWriteOptions Public Members

        // If true, elements with source file markings will be written as XIncludes rather than explicit data. Defaults to true.
        public bool writeXIncludeEnable;

        // If provided, this function will be used to exclude specific elements (those returning false) from the write
        // operation. Defaults to null.
        public Func<Element, bool> elementPredicate;

        // If true, any missing parent directories of the output file will be created before writing. Defaults to false.
        public bool createDirectories = false;
    }

    // ExceptionParseError Definition
    public class ExceptionParseError : Exception
    {
        public ExceptionParseError(string msg) : base(msg)
        {
        }
    }

    // ExceptionFileMissing Definition
    public class ExceptionFileMissing : Exception
    {
        public ExceptionFileMissing(string msg) : base(msg)
        {
        }
    }

    // XML Functions
    public static partial class Mx
    {
        public const string MTLX_EXTENSION = "mtlx";
        public const int MAX_XINCLUDE_DEPTH = 256;
        public const int MAX_XML_TREE_DEPTH = 256;

        private const string XINCLUDE_TAG = "xi:include";
        private const string XINCLUDE_NAMESPACE = "xmlns:xi";
        private const string XINCLUDE_URL = "http://www.w3.org/2001/XInclude";

        private static void documentToXml(Document doc, xml_node xmlRoot, XmlWriteOptions writeOptions)
        {
            List<(Element, xml_node)> elemStack = new List<(Element, xml_node)>();
            elemStack.Add((doc, xmlRoot));
            while (elemStack.Count != 0)
            {
                Element elem = elemStack[elemStack.Count - 1].Item1;
                xml_node xmlNode = elemStack[elemStack.Count - 1].Item2;
                elemStack.RemoveAt(elemStack.Count - 1);

                bool writeXIncludeEnable = writeOptions != null ? writeOptions.writeXIncludeEnable : true;
                Func<Element, bool> elementPredicate = writeOptions != null ? writeOptions.elementPredicate : null;

                if (elem.getName().Length != 0)
                {
                    xmlNode.append_attribute(Element.NAME_ATTRIBUTE).set_value(elem.getName());
                }

                foreach (string attrName in elem.getAttributeNames())
                {
                    xml_attribute xmlAttr = xmlNode.append_attribute(attrName);
                    xmlAttr.set_value(elem.getAttribute(attrName));
                }

                StringSet writtenSourceFiles = new StringSet();
                foreach (Element child in elem.getChildren())
                {
                    if (elementPredicate != null && !elementPredicate(child))
                    {
                        continue;
                    }

                    // Write XInclude references if requested.
                    if (writeXIncludeEnable && child.hasSourceUri())
                    {
                        string sourceUri = child.getSourceUri();
                        if (sourceUri != elem.getDocument().getSourceUri())
                        {
                            if (!writtenSourceFiles.Contains(sourceUri))
                            {
                                if (xmlNode.attribute(XINCLUDE_NAMESPACE).empty())
                                {
                                    xmlNode.append_attribute(XINCLUDE_NAMESPACE).set_value(XINCLUDE_URL);
                                }
                                xml_node includeNode = xmlNode.append_child(XINCLUDE_TAG);
                                xml_attribute includeAttr = includeNode.append_attribute("href");
                                FilePath includePath = new FilePath(sourceUri);

                                // Write relative include paths in Posix format, and absolute
                                // include paths in native format.
                                FilePath.Format includeFormat = includePath.isAbsolute() ? FilePath.FormatNative : FilePath.Format.FormatPosix;
                                includeAttr.set_value(includePath.asString(includeFormat));

                                writtenSourceFiles.Add(sourceUri);
                            }
                            continue;
                        }
                    }

                    // Write XML comments.
                    if (child.getCategory() == CommentElement.CATEGORY)
                    {
                        xml_node xmlChild = xmlNode.append_child(xml_node_type.node_comment);
                        xmlChild.set_value(child.getAttribute(Element.DOC_ATTRIBUTE));
                        continue;
                    }

                    // Write XML newlines.
                    if (child.getCategory() == NewlineElement.CATEGORY)
                    {
                        xml_node xmlChild = xmlNode.append_child(xml_node_type.node_newline);
                        xmlChild.set_value("\n");
                        continue;
                    }

                    // Push child data onto the stack.
                    xml_node xmlChildElement = xmlNode.append_child(child.getCategory());
                    elemStack.Add((child, xmlChildElement));
                }
            }
        }

        private static void elementFromXml(xml_node xmlNode, Element elem, XmlReadOptions readOptions, int depth = 1)
        {
            // Store attributes in element.
            foreach (xml_attribute xmlAttr in xmlNode.attributes())
            {
                if (xmlAttr.name() != Element.NAME_ATTRIBUTE)
                {
                    elem.setAttribute(xmlAttr.name(), xmlAttr.value());
                }
            }

            // Create child elements and recurse.
            foreach (xml_node xmlChild in xmlNode.children())
            {
                string category = xmlChild.name();
                if (category == XINCLUDE_TAG)
                {
                    continue;
                }

                // Get child element information from XML.
                string name = xmlChild.attribute(Element.NAME_ATTRIBUTE).value();
                // Check for duplicate child names.
                Element previous = elem.getChild(name);
                if (previous != null)
                {
                    continue;
                }

                // Enforce maximum tree depth.
                if (depth >= MAX_XML_TREE_DEPTH)
                {
                    throw new ExceptionParseError("Maximum tree depth exceeded.");
                }

                // Create the child element.
                Element child = elem.addChildOfCategory(category, name);
                elementFromXml(xmlChild, child, readOptions, depth + 1);

                // Handle the interpretation of XML comments and newlines.
                if (readOptions != null && category.Length == 0)
                {
                    if (readOptions.readComments && xmlChild.type() == xml_node_type.node_comment)
                    {
                        child = elem.changeChildCategory(child, CommentElement.CATEGORY);
                        child.setDocString(xmlChild.value());
                    }
                    else if (readOptions.readNewlines && xmlChild.type() == xml_node_type.node_newline)
                    {
                        child = elem.changeChildCategory(child, NewlineElement.CATEGORY);
                    }
                }
            }
        }

        private static void documentFromXml(Document doc, xml_document xmlDoc, FileSearchPath searchPath, XmlReadOptions readOptions)
        {
            xml_node xmlRoot = xmlDoc.child(Document.CATEGORY);
            if (xmlRoot.empty())
            {
                throw new ExceptionParseError("No root MaterialX element found.");
            }

            // Process XInclude directives.
            XmlReadFunction readXIncludeFunction = readOptions != null ? readOptions.readXIncludeFunction : readFromXmlFile;
            if (readXIncludeFunction != null)
            {
                foreach (xml_node xmlChild in xmlRoot.children())
                {
                    if (xmlChild.name() == XINCLUDE_TAG)
                    {
                        // Read XInclude attributes.
                        string filename = xmlChild.attribute("href").value();

                        // Check for XInclude cycles.
                        List<string> parents = readOptions != null ? readOptions.parentXIncludes : new List<string>();
                        if (parents.Contains(filename))
                        {
                            throw new ExceptionParseError("XInclude cycle detected.");
                        }

                        // Enforce maximum XInclude depth.
                        if (parents.Count >= MAX_XINCLUDE_DEPTH)
                        {
                            throw new ExceptionParseError("Maximum XInclude depth exceeded.");
                        }

                        // Read the included file into a library document.
                        Document library = createDocument();
                        XmlReadOptions xiReadOptions = readOptions != null ? new XmlReadOptions(readOptions) : new XmlReadOptions();
                        xiReadOptions.parentXIncludes.Add(filename);
                        readXIncludeFunction(library, new FilePath(filename), searchPath, xiReadOptions);

                        // Import the library document.
                        doc.importLibrary(library);
                    }
                }
            }

            // Build the element tree.
            elementFromXml(xmlRoot, doc, readOptions);

            // Upgrade version if requested.
            if (readOptions == null || readOptions.upgradeVersion)
            {
                doc.upgradeVersion();
            }
        }

        private static void validateParseResult(xml_parse_result result, FilePath filename = null)
        {
            if (filename == null)
            {
                filename = new FilePath();
            }

            if (result)
            {
                return;
            }

            if (result.status == xml_parse_status.status_file_not_found ||
                result.status == xml_parse_status.status_io_error ||
                result.status == xml_parse_status.status_out_of_memory)
            {
                throw new ExceptionFileMissing("Failed to open file for reading: " + filename.asString());
            }

            string desc = result.description();
            string offset = result.offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string message = "XML parse error";
            if (!filename.isEmpty())
            {
                message += " in " + filename.asString();
            }
            message += " (" + desc + " at character " + offset + ")";

            throw new ExceptionParseError(message);
        }

        private static uint getParseOptions(XmlReadOptions readOptions)
        {
            uint parseOptions = pugi.parse_default;
            if (readOptions != null)
            {
                if (readOptions.readComments)
                {
                    parseOptions |= pugi.parse_comments;
                }
                if (readOptions.readNewlines)
                {
                    parseOptions |= pugi.parse_newlines;
                }
            }
            return parseOptions;
        }

        public static void readFromXmlBuffer(Document doc, string buffer, FileSearchPath searchPath = null, XmlReadOptions readOptions = null)
        {
            searchPath = searchPath != null ? new FileSearchPath(searchPath) : new FileSearchPath();
            searchPath.append(getEnvironmentPath());

            xml_document xmlDoc = new xml_document();
            xml_parse_result result = xmlDoc.load_string(buffer, getParseOptions(readOptions));
            validateParseResult(result);

            documentFromXml(doc, xmlDoc, searchPath, readOptions);
        }

        public static void readFromXmlStream(Document doc, Stream stream, FileSearchPath searchPath = null, XmlReadOptions readOptions = null)
        {
            searchPath = searchPath != null ? new FileSearchPath(searchPath) : new FileSearchPath();
            searchPath.append(getEnvironmentPath());

            xml_document xmlDoc = new xml_document();
            xml_parse_result result = xmlDoc.load(stream, getParseOptions(readOptions));
            validateParseResult(result);

            documentFromXml(doc, xmlDoc, searchPath, readOptions);
        }

        public static void readFromXmlFile(Document doc, FilePath filename, FileSearchPath searchPath = null, XmlReadOptions readOptions = null)
        {
            searchPath = searchPath != null ? new FileSearchPath(searchPath) : new FileSearchPath();
            searchPath.append(getEnvironmentPath());
            filename = searchPath.find(filename);

            xml_document xmlDoc = new xml_document();
            xml_parse_result result = xmlDoc.load_file(filename.asString(), getParseOptions(readOptions));
            validateParseResult(result, filename);

            // This must be done before parsing the XML as the source URI
            // is used for searching for include files.
            FilePath sourcePath = (readOptions != null && readOptions.parentXIncludes.Count != 0) ?
                                  new FilePath(readOptions.parentXIncludes[0]) :
                                  new FilePath(filename);
            doc.setSourceUri(sourcePath.asString());
            if (!sourcePath.isAbsolute())
            {
                sourcePath = searchPath.find(sourcePath);
            }
            searchPath.prepend(sourcePath.getParentPath());

            documentFromXml(doc, xmlDoc, searchPath, readOptions);
        }

        public static void readFromXmlString(Document doc, string str, FileSearchPath searchPath = null, XmlReadOptions readOptions = null)
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(str)))
            {
                readFromXmlStream(doc, stream, searchPath, readOptions);
            }
        }

        public static void writeToXmlStream(Document doc, TextWriter stream, XmlWriteOptions writeOptions = null)
        {
            xml_document xmlDoc = new xml_document();
            xml_node xmlRoot = xmlDoc.append_child("materialx");
            documentToXml(doc, xmlRoot, writeOptions);
            xmlDoc.save(stream, "  ");
        }

        public static void writeToXmlFile(Document doc, FilePath filename, XmlWriteOptions writeOptions = null)
        {
            if (writeOptions != null && writeOptions.createDirectories)
            {
                filename.getParentPath().createDirectory(true);
            }

            StreamWriter ofs;
            try
            {
                ofs = new StreamWriter(filename.asString(), false, new UTF8Encoding(false));
            }
            catch (System.Exception)
            {
                throw new ExceptionFileMissing("Failed to open file for writing: " + filename.asString());
            }
            using (ofs)
            {
                writeToXmlStream(doc, ofs, writeOptions);
            }
        }

        public static string writeToXmlString(Document doc, XmlWriteOptions writeOptions = null)
        {
            StringWriter stream = new StringWriter();
            writeToXmlStream(doc, stream, writeOptions);
            return stream.ToString();
        }

        public static void prependXInclude(Document doc, FilePath filename)
        {
            if (!filename.isEmpty())
            {
                Element elem = doc.addNode("xinclude");
                elem.setSourceUri(filename.asString());
                doc.setChildIndex(elem.getName(), 0);
            }
        }
    }
}
