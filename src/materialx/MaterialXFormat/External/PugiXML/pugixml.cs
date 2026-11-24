// pugixml parser - version 1.9, Copyright (C) 2006-2018 by Arseny Kapoulkine,
// MIT licence (see readme.txt in this directory), with the MaterialX changes: a
// node type and a parse option for blank lines, and '<' and '>' written as they are.
//
// This file is a C# port of the parts of MaterialXFormat/External/PugiXML/pugixml.cpp
// that MaterialX uses: the document tree, the in-place parser and the writer.
// The parser works on a zero-terminated UTF-8 buffer exactly as the C++ does,
// with indices in place of pointers. XPath and the wide-character mode are not ported.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MaterialX.PugiXml
{
    // xml_node_type Definition
    public enum xml_node_type
    {
        node_null,
        node_document,
        node_element,
        node_pcdata,
        node_cdata,
        node_comment,
        node_pi,
        node_declaration,
        node_doctype,
        node_newline
    }

    // xml_parse_status Definition
    public enum xml_parse_status
    {
        status_ok = 0,
        status_file_not_found,
        status_io_error,
        status_out_of_memory,
        status_internal_error,
        status_unrecognized_tag,
        status_bad_pi,
        status_bad_comment,
        status_bad_cdata,
        status_bad_doctype,
        status_bad_pcdata,
        status_bad_start_element,
        status_bad_attribute,
        status_bad_end_element,
        status_end_element_mismatch,
        status_append_invalid_root,
        status_no_document_element
    }

    // xml_parse_result Definition
    public struct xml_parse_result
    {
        public xml_parse_status status;
        public long offset;

        public xml_parse_result(xml_parse_status status, long offset = 0)
        {
            this.status = status;
            this.offset = offset;
        }

        public static implicit operator bool(xml_parse_result result)
        {
            return result.status == xml_parse_status.status_ok;
        }

        public string description()
        {
            switch (status)
            {
                case xml_parse_status.status_ok:
                    return "No error";
                case xml_parse_status.status_file_not_found:
                    return "File was not found";
                case xml_parse_status.status_io_error:
                    return "Error reading from file/stream";
                case xml_parse_status.status_out_of_memory:
                    return "Could not allocate memory";
                case xml_parse_status.status_internal_error:
                    return "Internal error occurred";
                case xml_parse_status.status_unrecognized_tag:
                    return "Could not determine tag type";
                case xml_parse_status.status_bad_pi:
                    return "Error parsing document declaration/processing instruction";
                case xml_parse_status.status_bad_comment:
                    return "Error parsing comment";
                case xml_parse_status.status_bad_cdata:
                    return "Error parsing CDATA section";
                case xml_parse_status.status_bad_doctype:
                    return "Error parsing document type declaration";
                case xml_parse_status.status_bad_pcdata:
                    return "Error parsing PCDATA section";
                case xml_parse_status.status_bad_start_element:
                    return "Error parsing start element tag";
                case xml_parse_status.status_bad_attribute:
                    return "Error parsing element attribute";
                case xml_parse_status.status_bad_end_element:
                    return "Error parsing end element tag";
                case xml_parse_status.status_end_element_mismatch:
                    return "Start-end tags mismatch";
                case xml_parse_status.status_append_invalid_root:
                    return "Unable to append nodes: root is not an element or document";
                case xml_parse_status.status_no_document_element:
                    return "No document element found";
                default:
                    return "Unknown error";
            }
        }
    }

    // Parse and Format Options
    public static class pugi
    {
        public const uint parse_minimal = 0x0000;
        public const uint parse_pi = 0x0001;
        public const uint parse_comments = 0x0002;
        public const uint parse_cdata = 0x0004;
        public const uint parse_ws_pcdata = 0x0008;
        public const uint parse_escapes = 0x0010;
        public const uint parse_eol = 0x0020;
        public const uint parse_wconv_attribute = 0x0040;
        public const uint parse_wnorm_attribute = 0x0080;
        public const uint parse_declaration = 0x0100;
        public const uint parse_doctype = 0x0200;
        public const uint parse_ws_pcdata_single = 0x0400;
        public const uint parse_trim_pcdata = 0x0800;
        public const uint parse_fragment = 0x1000;
        public const uint parse_embed_pcdata = 0x2000;
        // MaterialX: This flag determines if newlines are added to the DOM tree. This flag is off by default.
        public const uint parse_newlines = 0x4000;
        public const uint parse_default = parse_cdata | parse_escapes | parse_wconv_attribute | parse_eol;
        public const uint parse_full = parse_default | parse_pi | parse_comments | parse_declaration | parse_doctype;

        public const uint format_indent = 0x01;
        public const uint format_write_bom = 0x02;
        public const uint format_raw = 0x04;
        public const uint format_no_declaration = 0x08;
        public const uint format_no_escapes = 0x10;
        public const uint format_save_file_text = 0x20;
        public const uint format_indent_attributes = 0x40;
        public const uint format_no_empty_element_tags = 0x80;
        public const uint format_default = format_indent;
    }

    // xml_attribute Definition
    public class xml_attribute
    {
        // xml_attribute Public Methods
        public bool empty()
        {
            return _null;
        }

        public string name()
        {
            return _name != null ? _name : "";
        }

        public string value()
        {
            return _value != null ? _value : "";
        }

        public bool set_value(string rhs)
        {
            if (_null)
            {
                return false;
            }
            _value = rhs;
            return true;
        }

        // xml_attribute Internal Members
        internal static readonly xml_attribute Null = new xml_attribute { _null = true };

        internal bool _null;
        internal string _name;
        internal string _value;
        internal int name_offset = -1;
        internal int value_offset = -1;
    }

    // xml_node Definition
    public class xml_node
    {
        // xml_node Public Methods
        public bool empty()
        {
            return _type == xml_node_type.node_null;
        }

        public xml_node_type type()
        {
            return _type;
        }

        public string name()
        {
            return _name != null ? _name : "";
        }

        public string value()
        {
            return _value != null ? _value : "";
        }

        public List<xml_attribute> attributes()
        {
            return _attributes;
        }

        public List<xml_node> children()
        {
            return _children;
        }

        public xml_node child(string name)
        {
            foreach (xml_node i in _children)
            {
                if (i._name != null && i._name == name)
                {
                    return i;
                }
            }
            return Null;
        }

        public xml_attribute attribute(string name)
        {
            foreach (xml_attribute i in _attributes)
            {
                if (i._name != null && i._name == name)
                {
                    return i;
                }
            }
            return xml_attribute.Null;
        }

        public bool set_name(string rhs)
        {
            if (_type != xml_node_type.node_element && _type != xml_node_type.node_pi && _type != xml_node_type.node_declaration)
            {
                return false;
            }
            _name = rhs;
            return true;
        }

        public bool set_value(string rhs)
        {
            if (_type != xml_node_type.node_pcdata && _type != xml_node_type.node_cdata && _type != xml_node_type.node_comment &&
                _type != xml_node_type.node_pi && _type != xml_node_type.node_doctype)
            {
                return false;
            }
            _value = rhs;
            return true;
        }

        public xml_attribute append_attribute(string name)
        {
            if (_type != xml_node_type.node_element && _type != xml_node_type.node_declaration)
            {
                return xml_attribute.Null;
            }
            xml_attribute a = new xml_attribute();
            a._name = name;
            _attributes.Add(a);
            return a;
        }

        public xml_node append_child(xml_node_type type = xml_node_type.node_element)
        {
            if (!allow_insert_child(_type, type))
            {
                return Null;
            }
            xml_node n = new xml_node(type);
            n._parent = this;
            _children.Add(n);
            if (type == xml_node_type.node_declaration)
            {
                n._name = "xml";
            }
            return n;
        }

        public xml_node append_child(string name)
        {
            xml_node result = append_child(xml_node_type.node_element);
            result.set_name(name);
            return result;
        }

        // xml_node Internal Methods
        internal xml_node(xml_node_type type)
        {
            _type = type;
        }

        internal static bool allow_insert_child(xml_node_type parent, xml_node_type child)
        {
            if (parent != xml_node_type.node_document && parent != xml_node_type.node_element)
            {
                return false;
            }
            if (child == xml_node_type.node_document || child == xml_node_type.node_null)
            {
                return false;
            }
            if (parent != xml_node_type.node_document && (child == xml_node_type.node_declaration || child == xml_node_type.node_doctype))
            {
                return false;
            }
            return true;
        }

        // xml_node Internal Members
        internal static readonly xml_node Null = new xml_node(xml_node_type.node_null);

        internal xml_node_type _type;
        internal string _name;
        internal string _value;
        internal int name_offset = -1;
        internal int value_offset = -1;
        internal xml_node _parent;
        internal List<xml_node> _children = new List<xml_node>();
        internal List<xml_attribute> _attributes = new List<xml_attribute>();
    }

    // xml_document Definition
    public class xml_document : xml_node
    {
        // xml_document Public Methods
        public xml_document() : base(xml_node_type.node_document)
        {
        }

        public void reset()
        {
            _children.Clear();
            _attributes.Clear();
        }

        public xml_parse_result load_string(string contents, uint options = pugi.parse_default)
        {
            return load_buffer(Encoding.UTF8.GetBytes(contents), options, false);
        }

        public xml_parse_result load(Stream stream, uint options = pugi.parse_default)
        {
            reset();
            byte[] contents;
            try
            {
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    contents = memory.ToArray();
                }
            }
            catch (IOException)
            {
                return new xml_parse_result(xml_parse_status.status_io_error);
            }
            return load_buffer(contents, options, true);
        }

        public xml_parse_result load_file(string path, uint options = pugi.parse_default)
        {
            reset();
            byte[] contents;
            try
            {
                if (!File.Exists(path))
                {
                    return new xml_parse_result(xml_parse_status.status_file_not_found);
                }
                contents = File.ReadAllBytes(path);
            }
            catch (System.Exception)
            {
                return new xml_parse_result(xml_parse_status.status_io_error);
            }
            // if no data is available, the document has no document element
            if (contents.Length == 0)
            {
                return new xml_parse_result(xml_parse_status.status_no_document_element);
            }
            return load_buffer(contents, options, true);
        }

        public xml_parse_result load_buffer(byte[] contents, uint options = pugi.parse_default, bool detectEncoding = true)
        {
            reset();

            // get actual encoding and convert the contents to UTF-8
            byte[] utf8 = detectEncoding ? xml_parser.convert_buffer(contents) : contents;

            // the buffer the parser works on is zero-terminated
            byte[] buffer = new byte[utf8.Length + 1 + 8];
            Array.Copy(utf8, buffer, utf8.Length);
            int length = utf8.Length + 1;

            xml_parser parser = new xml_parser(buffer);
            xml_parse_result res = parser.parse(length, this, options);
            parser.resolve(this);
            return res;
        }

        public void save(TextWriter writer, string indent = "\t", uint flags = pugi.format_default)
        {
            StringBuilder buffered_writer = new StringBuilder();

            if ((flags & pugi.format_write_bom) != 0)
            {
                buffered_writer.Append('﻿');
            }

            if ((flags & pugi.format_no_declaration) == 0 && !xml_output.has_declaration(this))
            {
                buffered_writer.Append("<?xml version=\"1.0\"");
                buffered_writer.Append("?>");
                if ((flags & pugi.format_raw) == 0)
                {
                    buffered_writer.Append('\n');
                }
            }

            xml_output.node_output(buffered_writer, this, indent, flags, 0);

            writer.Write(buffered_writer.ToString());
        }
    }

    // Character Tables
    internal static class chartype
    {
        public const int ct_parse_pcdata = 1;   // \0, &, \r, <
        public const int ct_parse_attr = 2;     // \0, &, \r, ', "
        public const int ct_parse_attr_ws = 4;  // \0, &, \r, ', ", \n, tab
        public const int ct_space = 8;          // \r, \n, space, tab
        public const int ct_parse_cdata = 16;   // \0, ], >, \r
        public const int ct_parse_comment = 32; // \0, -, >, \r
        public const int ct_symbol = 64;        // Any symbol > 127, a-z, A-Z, 0-9, _, :, -, .
        public const int ct_start_symbol = 128; // Any symbol > 127, a-z, A-Z, _, :

        public static readonly byte[] chartype_table = new byte[256]
        {
            55,  0,   0,   0,   0,   0,   0,   0,      0,   12,  12,  0,   0,   63,  0,   0,   // 0-15
            0,   0,   0,   0,   0,   0,   0,   0,      0,   0,   0,   0,   0,   0,   0,   0,   // 16-31
            8,   0,   6,   0,   0,   0,   7,   6,      0,   0,   0,   0,   0,   96,  64,  0,   // 32-47
            64,  64,  64,  64,  64,  64,  64,  64,     64,  64,  192, 0,   1,   0,   48,  0,   // 48-63
            0,   192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192, // 64-79
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 0,   0,   16,  0,   192, // 80-95
            0,   192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192, // 96-111
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 0, 0, 0, 0, 0,           // 112-127

            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192, // 128+
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192,
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192,
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192,
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192,
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192,
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192,
            192, 192, 192, 192, 192, 192, 192, 192,    192, 192, 192, 192, 192, 192, 192, 192
        };

        public const int ctx_special_pcdata = 1;   // Any symbol >= 0 and < 32 (except \t, \r, \n), &, <, >
        public const int ctx_special_attr = 2;     // Any symbol >= 0 and < 32 (except \t), &, <, >, "
        public const int ctx_start_symbol = 4;     // Any symbol > 127, a-z, A-Z, _
        public const int ctx_digit = 8;            // 0-9
        public const int ctx_symbol = 16;          // Any symbol > 127, a-z, A-Z, 0-9, _, -, .

        public static readonly byte[] chartypex_table = new byte[256]
        {
            3,  3,  3,  3,  3,  3,  3,  3,     3,  0,  2,  3,  3,  2,  3,  3,     // 0-15
            3,  3,  3,  3,  3,  3,  3,  3,     3,  3,  3,  3,  3,  3,  3,  3,     // 16-31
            0,  0,  2,  0,  0,  0,  3,  0,     0,  0,  0,  0,  0, 16, 16,  0,     // 32-47
            24, 24, 24, 24, 24, 24, 24, 24,    24, 24, 0,  0,  3,  0,  3,  0,     // 48-63

            0,  20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,    // 64-79
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 0,  0,  0,  0,  20,    // 80-95
            0,  20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,    // 96-111
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 0,  0,  0,  0,  0,     // 112-127

            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,    // 128+
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20,
            20, 20, 20, 20, 20, 20, 20, 20,    20, 20, 20, 20, 20, 20, 20, 20
        };
    }

    // gap Definition
    internal struct gap
    {
        public int end;
        public int size;
        private byte[] b;

        public gap(byte[] buffer)
        {
            b = buffer;
            end = -1;
            size = 0;
        }

        // Push new gap, move s count bytes further (skipping the gap).
        // Collapse previous gap.
        public void push(ref int s, int count)
        {
            if (end >= 0) // there was a gap already; collapse it
            {
                // Move [old_gap_end, new_gap_start) to [old_gap_start, ...)
                Buffer.BlockCopy(b, end, b, end - size, s - end);
            }

            s += count; // end of current gap

            // "merge" two gaps
            end = s;
            size += count;
        }

        // Collapse all gaps, return past-the-end pointer
        public int flush(int s)
        {
            if (end >= 0)
            {
                // Move [old_gap_end, current_pos) to [old_gap_start, ...)
                Buffer.BlockCopy(b, end, b, end - size, s - end);

                return s - size;
            }
            else
            {
                return s;
            }
        }
    }

    // xml_parser Definition
    internal class xml_parser
    {
        // xml_parser Public Methods
        public xml_parser(byte[] buffer)
        {
            b = buffer;
            error_offset = -1;
            error_status = xml_parse_status.status_ok;
        }

        // The encoding detection of get_buffer_encoding, and conversion to UTF-8.
        public static byte[] convert_buffer(byte[] contents)
        {
            int size = contents.Length;

            // skip encoding autodetection if input buffer is too small
            if (size < 4)
            {
                return contents;
            }

            byte d0 = contents[0];
            byte d1 = contents[1];
            byte d2 = contents[2];
            byte d3 = contents[3];

            // look for BOM in first few bytes
            if (d0 == 0 && d1 == 0 && d2 == 0xfe && d3 == 0xff)
            {
                return transcode(contents, new UTF32Encoding(true, false, false));
            }
            if (d0 == 0xff && d1 == 0xfe && d2 == 0 && d3 == 0)
            {
                return transcode(contents, new UTF32Encoding(false, false, false));
            }
            if (d0 == 0xfe && d1 == 0xff)
            {
                return transcode(contents, new UnicodeEncoding(true, false, false));
            }
            if (d0 == 0xff && d1 == 0xfe)
            {
                return transcode(contents, new UnicodeEncoding(false, false, false));
            }
            if (d0 == 0xef && d1 == 0xbb && d2 == 0xbf)
            {
                return contents;
            }

            // look for <, <? or <?xm in various encodings
            if (d0 == 0 && d1 == 0 && d2 == 0 && d3 == 0x3c)
            {
                return transcode(contents, new UTF32Encoding(true, false, false));
            }
            if (d0 == 0x3c && d1 == 0 && d2 == 0 && d3 == 0)
            {
                return transcode(contents, new UTF32Encoding(false, false, false));
            }
            if (d0 == 0 && d1 == 0x3c && d2 == 0 && d3 == 0x3f)
            {
                return transcode(contents, new UnicodeEncoding(true, false, false));
            }
            if (d0 == 0x3c && d1 == 0 && d2 == 0x3f && d3 == 0)
            {
                return transcode(contents, new UnicodeEncoding(false, false, false));
            }

            // look for utf16 < followed by node name (this may fail, but is better than utf8 since it's zero terminated so early)
            if (d0 == 0 && d1 == 0x3c)
            {
                return transcode(contents, new UnicodeEncoding(true, false, false));
            }
            if (d0 == 0x3c && d1 == 0)
            {
                return transcode(contents, new UnicodeEncoding(false, false, false));
            }

            // no known BOM detected; parse declaration
            if (d0 == 0x3c && d1 == 0x3f && d2 == 0x78 && d3 == 0x6d)
            {
                string encoding = declaration_encoding(contents);
                if (encoding != null)
                {
                    string lower = encoding.ToLowerInvariant();
                    if (lower == "iso-8859-1" || lower == "latin1")
                    {
                        return transcode(contents, Encoding.Latin1);
                    }
                }
            }

            return contents;
        }

        public xml_parse_result parse(int length, xml_node root, uint optmsk)
        {
            // early-out for empty documents
            if (length == 0)
            {
                return new xml_parse_result((optmsk & pugi.parse_fragment) != 0 ? xml_parse_status.status_ok : xml_parse_status.status_no_document_element);
            }

            // get last child of the root before parsing
            int first_root_child_parsed = root._children.Count;

            // save last character and make buffer zero-terminated (speeds up parsing)
            byte endch = b[length - 1];
            b[length - 1] = 0;

            // skip BOM to make sure it does not end up as part of parse output
            int buffer_data = parse_skip_bom(0);

            // perform actual parsing
            parse_tree(buffer_data, root, optmsk, endch);

            xml_parse_result result = new xml_parse_result(error_status, error_offset >= 0 ? error_offset : 0);

            if (result)
            {
                // since we removed last character, we have to handle the only possible false positive (stray <)
                if (endch == '<')
                {
                    return new xml_parse_result(xml_parse_status.status_unrecognized_tag, length - 1);
                }

                // check if there are any element nodes parsed
                if ((optmsk & pugi.parse_fragment) == 0 && !has_element_node_siblings(root, first_root_child_parsed))
                {
                    return new xml_parse_result(xml_parse_status.status_no_document_element, length - 1);
                }
            }
            else
            {
                // roll back offset if it occurs on a null terminator in the source buffer
                if (result.offset > 0 && result.offset == length - 1 && endch == 0)
                {
                    result.offset--;
                }
            }

            return result;
        }

        // Turn the buffer offsets recorded while parsing into strings.
        public void resolve(xml_node node)
        {
            if (node.name_offset >= 0)
            {
                node._name = text_at(node.name_offset);
                node.name_offset = -1;
            }
            if (node.value_offset >= 0)
            {
                node._value = text_at(node.value_offset);
                node.value_offset = -1;
            }
            foreach (xml_attribute a in node._attributes)
            {
                if (a.name_offset >= 0)
                {
                    a._name = text_at(a.name_offset);
                    a.name_offset = -1;
                }
                if (a.value_offset >= 0)
                {
                    a._value = text_at(a.value_offset);
                    a.value_offset = -1;
                }
            }
            foreach (xml_node child in node._children)
            {
                resolve(child);
            }
        }

        // xml_parser Private Methods
        private static byte[] transcode(byte[] contents, Encoding encoding)
        {
            return Encoding.UTF8.GetBytes(encoding.GetString(contents));
        }

        private static string declaration_encoding(byte[] data)
        {
            // parse_declaration_encoding: look for encoding="..." inside <?xml ... ?>
            int size = data.Length;
            int end = -1;
            for (int i = 0; i + 1 < size; i++)
            {
                if (data[i] == '?' && data[i + 1] == '>')
                {
                    end = i;
                    break;
                }
            }
            if (end < 0)
            {
                return null;
            }
            string declaration = Encoding.Latin1.GetString(data, 0, end);
            int pos = declaration.IndexOf("encoding", StringComparison.Ordinal);
            if (pos < 0)
            {
                return null;
            }
            pos += "encoding".Length;
            while (pos < declaration.Length && (chartype.chartype_table[(byte)declaration[pos]] & chartype.ct_space) != 0)
            {
                pos++;
            }
            if (pos >= declaration.Length || declaration[pos] != '=')
            {
                return null;
            }
            pos++;
            while (pos < declaration.Length && (chartype.chartype_table[(byte)declaration[pos]] & chartype.ct_space) != 0)
            {
                pos++;
            }
            if (pos >= declaration.Length || (declaration[pos] != '"' && declaration[pos] != '\''))
            {
                return null;
            }
            char delimiter = declaration[pos];
            int start = pos + 1;
            int close = declaration.IndexOf(delimiter, start);
            if (close < 0)
            {
                return null;
            }
            return declaration.Substring(start, close - start);
        }

        private string text_at(int offset)
        {
            int end = offset;
            while (b[end] != 0)
            {
                end++;
            }
            return Encoding.UTF8.GetString(b, offset, end - offset);
        }

        private bool IS(int index, int ct)
        {
            return (chartype.chartype_table[b[index]] & ct) != 0;
        }

        private static bool ISCH(byte c, int ct)
        {
            return (chartype.chartype_table[c] & ct) != 0;
        }

        private static bool ENDSWITH(byte c, char e, byte endch)
        {
            return c == e || (c == 0 && endch == e);
        }

        private int THROW_ERROR(xml_parse_status err, int m)
        {
            error_offset = m;
            error_status = err;
            return -1;
        }

        private static xml_node append_new_node(xml_node node, xml_node_type type)
        {
            xml_node child = new xml_node(type);
            child._parent = node;
            node._children.Add(child);
            return child;
        }

        private static int utf8_any(byte[] result, int at, uint ch)
        {
            // U+0000..U+007F
            if (ch < 0x80)
            {
                result[at] = (byte)ch;
                return at + 1;
            }
            // U+0080..U+07FF
            else if (ch < 0x800)
            {
                result[at] = (byte)(0xC0 | (ch >> 6));
                result[at + 1] = (byte)(0x80 | (ch & 0x3F));
                return at + 2;
            }
            // U+0800..U+FFFF
            else if (ch < 0x10000)
            {
                result[at] = (byte)(0xE0 | (ch >> 12));
                result[at + 1] = (byte)(0x80 | ((ch >> 6) & 0x3F));
                result[at + 2] = (byte)(0x80 | (ch & 0x3F));
                return at + 3;
            }
            // U+10000..U+10FFFF
            else
            {
                result[at] = (byte)(0xF0 | (ch >> 18));
                result[at + 1] = (byte)(0x80 | ((ch >> 12) & 0x3F));
                result[at + 2] = (byte)(0x80 | ((ch >> 6) & 0x3F));
                result[at + 3] = (byte)(0x80 | (ch & 0x3F));
                return at + 4;
            }
        }

        private int strconv_escape(int s, ref gap g)
        {
            int stre = s + 1;

            switch ((char)b[stre])
            {
                case '#': // &#...
                {
                    uint ucsc = 0;

                    if (b[stre + 1] == 'x') // &#x... (hex code)
                    {
                        stre += 2;

                        byte ch = b[stre];

                        if (ch == ';')
                        {
                            return stre;
                        }

                        for (;;)
                        {
                            if ((uint)(ch - '0') <= 9)
                            {
                                ucsc = 16 * ucsc + (uint)(ch - '0');
                            }
                            else if ((uint)((ch | ' ') - 'a') <= 5)
                            {
                                ucsc = 16 * ucsc + (uint)((ch | ' ') - 'a' + 10);
                            }
                            else if (ch == ';')
                            {
                                break;
                            }
                            else // cancel
                            {
                                return stre;
                            }

                            ch = b[++stre];
                        }

                        ++stre;
                    }
                    else // &#... (dec code)
                    {
                        byte ch = b[++stre];

                        if (ch == ';')
                        {
                            return stre;
                        }

                        for (;;)
                        {
                            if ((uint)(ch - '0') <= 9)
                            {
                                ucsc = 10 * ucsc + (uint)(ch - '0');
                            }
                            else if (ch == ';')
                            {
                                break;
                            }
                            else // cancel
                            {
                                return stre;
                            }

                            ch = b[++stre];
                        }

                        ++stre;
                    }

                    s = utf8_any(b, s, ucsc);

                    g.push(ref s, stre - s);
                    return stre;
                }

                case 'a': // &a
                {
                    ++stre;

                    if (b[stre] == 'm') // &am
                    {
                        if (b[++stre] == 'p' && b[++stre] == ';') // &amp;
                        {
                            b[s++] = (byte)'&';
                            ++stre;

                            g.push(ref s, stre - s);
                            return stre;
                        }
                    }
                    else if (b[stre] == 'p') // &ap
                    {
                        if (b[++stre] == 'o' && b[++stre] == 's' && b[++stre] == ';') // &apos;
                        {
                            b[s++] = (byte)'\'';
                            ++stre;

                            g.push(ref s, stre - s);
                            return stre;
                        }
                    }
                    break;
                }

                case 'g': // &g
                {
                    if (b[++stre] == 't' && b[++stre] == ';') // &gt;
                    {
                        b[s++] = (byte)'>';
                        ++stre;

                        g.push(ref s, stre - s);
                        return stre;
                    }
                    break;
                }

                case 'l': // &l
                {
                    if (b[++stre] == 't' && b[++stre] == ';') // &lt;
                    {
                        b[s++] = (byte)'<';
                        ++stre;

                        g.push(ref s, stre - s);
                        return stre;
                    }
                    break;
                }

                case 'q': // &q
                {
                    if (b[++stre] == 'u' && b[++stre] == 'o' && b[++stre] == 't' && b[++stre] == ';') // &quot;
                    {
                        b[s++] = (byte)'"';
                        ++stre;

                        g.push(ref s, stre - s);
                        return stre;
                    }
                    break;
                }

                default:
                    break;
            }

            return stre;
        }

        private int strconv_comment(int s, byte endch)
        {
            gap g = new gap(b);

            while (true)
            {
                while (!IS(s, chartype.ct_parse_comment))
                {
                    ++s;
                }

                if (b[s] == '\r') // Either a single 0x0d or 0x0d 0x0a pair
                {
                    b[s++] = (byte)'\n'; // replace first one with 0x0a

                    if (b[s] == '\n')
                    {
                        g.push(ref s, 1);
                    }
                }
                else if (b[s] == '-' && b[s + 1] == '-' && ENDSWITH(b[s + 2], '>', endch)) // comment ends here
                {
                    b[g.flush(s)] = 0;

                    return s + (b[s + 2] == '>' ? 3 : 2);
                }
                else if (b[s] == 0)
                {
                    return -1;
                }
                else
                {
                    ++s;
                }
            }
        }

        private int strconv_cdata(int s, byte endch)
        {
            gap g = new gap(b);

            while (true)
            {
                while (!IS(s, chartype.ct_parse_cdata))
                {
                    ++s;
                }

                if (b[s] == '\r') // Either a single 0x0d or 0x0d 0x0a pair
                {
                    b[s++] = (byte)'\n'; // replace first one with 0x0a

                    if (b[s] == '\n')
                    {
                        g.push(ref s, 1);
                    }
                }
                else if (b[s] == ']' && b[s + 1] == ']' && ENDSWITH(b[s + 2], '>', endch)) // CDATA ends here
                {
                    b[g.flush(s)] = 0;

                    return s + 1;
                }
                else if (b[s] == 0)
                {
                    return -1;
                }
                else
                {
                    ++s;
                }
            }
        }

        private int strconv_pcdata(int s, bool opt_trim, bool opt_eol, bool opt_escape)
        {
            gap g = new gap(b);

            int begin = s;

            while (true)
            {
                while (!IS(s, chartype.ct_parse_pcdata))
                {
                    ++s;
                }

                if (b[s] == '<') // PCDATA ends here
                {
                    int end = g.flush(s);

                    if (opt_trim)
                    {
                        while (end > begin && IS(end - 1, chartype.ct_space))
                        {
                            --end;
                        }
                    }

                    b[end] = 0;

                    return s + 1;
                }
                else if (opt_eol && b[s] == '\r') // Either a single 0x0d or 0x0d 0x0a pair
                {
                    b[s++] = (byte)'\n'; // replace first one with 0x0a

                    if (b[s] == '\n')
                    {
                        g.push(ref s, 1);
                    }
                }
                else if (opt_escape && b[s] == '&')
                {
                    s = strconv_escape(s, ref g);
                }
                else if (b[s] == 0)
                {
                    int end = g.flush(s);

                    if (opt_trim)
                    {
                        while (end > begin && IS(end - 1, chartype.ct_space))
                        {
                            --end;
                        }
                    }

                    b[end] = 0;

                    return s;
                }
                else
                {
                    ++s;
                }
            }
        }

        private int strconv_attribute_wnorm(int s, byte end_quote, bool opt_escape)
        {
            gap g = new gap(b);

            // trim leading whitespaces
            if (IS(s, chartype.ct_space))
            {
                int str = s;

                do
                {
                    ++str;
                }
                while (IS(str, chartype.ct_space));

                g.push(ref s, str - s);
            }

            while (true)
            {
                while (!IS(s, chartype.ct_parse_attr_ws | chartype.ct_space))
                {
                    ++s;
                }

                if (b[s] == end_quote)
                {
                    int str = g.flush(s);

                    do
                    {
                        b[str--] = 0;
                    }
                    while (IS(str, chartype.ct_space));

                    return s + 1;
                }
                else if (IS(s, chartype.ct_space))
                {
                    b[s++] = (byte)' ';

                    if (IS(s, chartype.ct_space))
                    {
                        int str = s + 1;
                        while (IS(str, chartype.ct_space))
                        {
                            ++str;
                        }

                        g.push(ref s, str - s);
                    }
                }
                else if (opt_escape && b[s] == '&')
                {
                    s = strconv_escape(s, ref g);
                }
                else if (b[s] == 0)
                {
                    return -1;
                }
                else
                {
                    ++s;
                }
            }
        }

        private int strconv_attribute_wconv(int s, byte end_quote, bool opt_escape)
        {
            gap g = new gap(b);

            while (true)
            {
                while (!IS(s, chartype.ct_parse_attr_ws))
                {
                    ++s;
                }

                if (b[s] == end_quote)
                {
                    b[g.flush(s)] = 0;

                    return s + 1;
                }
                else if (IS(s, chartype.ct_space))
                {
                    if (b[s] == '\r')
                    {
                        b[s++] = (byte)' ';

                        if (b[s] == '\n')
                        {
                            g.push(ref s, 1);
                        }
                    }
                    else
                    {
                        b[s++] = (byte)' ';
                    }
                }
                else if (opt_escape && b[s] == '&')
                {
                    s = strconv_escape(s, ref g);
                }
                else if (b[s] == 0)
                {
                    return -1;
                }
                else
                {
                    ++s;
                }
            }
        }

        private int strconv_attribute_eol(int s, byte end_quote, bool opt_escape)
        {
            gap g = new gap(b);

            while (true)
            {
                while (!IS(s, chartype.ct_parse_attr))
                {
                    ++s;
                }

                if (b[s] == end_quote)
                {
                    b[g.flush(s)] = 0;

                    return s + 1;
                }
                else if (b[s] == '\r')
                {
                    b[s++] = (byte)'\n';

                    if (b[s] == '\n')
                    {
                        g.push(ref s, 1);
                    }
                }
                else if (opt_escape && b[s] == '&')
                {
                    s = strconv_escape(s, ref g);
                }
                else if (b[s] == 0)
                {
                    return -1;
                }
                else
                {
                    ++s;
                }
            }
        }

        private int strconv_attribute_simple(int s, byte end_quote, bool opt_escape)
        {
            gap g = new gap(b);

            while (true)
            {
                while (!IS(s, chartype.ct_parse_attr))
                {
                    ++s;
                }

                if (b[s] == end_quote)
                {
                    b[g.flush(s)] = 0;

                    return s + 1;
                }
                else if (opt_escape && b[s] == '&')
                {
                    s = strconv_escape(s, ref g);
                }
                else if (b[s] == 0)
                {
                    return -1;
                }
                else
                {
                    ++s;
                }
            }
        }

        private int strconv_attribute(int s, byte end_quote, uint optmask)
        {
            // get bitmask for flags (wconv wnorm eol escapes)
            uint bits = (optmask >> 4) & 15;
            bool escape = (bits & 1) != 0;
            if (bits >= 8)
            {
                return strconv_attribute_wnorm(s, end_quote, escape);
            }
            if (bits >= 4)
            {
                return strconv_attribute_wconv(s, end_quote, escape);
            }
            if (bits >= 2)
            {
                return strconv_attribute_eol(s, end_quote, escape);
            }
            return strconv_attribute_simple(s, end_quote, escape);
        }

        // DOCTYPE consists of nested sections of the following possible types:
        // <!-- ... -->, <? ... ?>, "...", '...'
        // <![...]]>
        // <!...>
        // First group can not contain nested groups
        // Second group can contain nested groups of the same type
        // Third group can contain all other groups
        private int parse_doctype_primitive(int s)
        {
            if (b[s] == '"' || b[s] == '\'')
            {
                // quoted string
                byte ch = b[s++];
                while (b[s] != 0 && !(b[s] == ch))
                {
                    ++s;
                }
                if (b[s] == 0)
                {
                    return THROW_ERROR(xml_parse_status.status_bad_doctype, s);
                }

                s++;
            }
            else if (b[s] == '<' && b[s + 1] == '?')
            {
                // <? ... ?>
                s += 2;
                while (b[s] != 0 && !(b[s] == '?' && b[s + 1] == '>'))
                {
                    ++s;
                }
                if (b[s] == 0)
                {
                    return THROW_ERROR(xml_parse_status.status_bad_doctype, s);
                }

                s += 2;
            }
            else if (b[s] == '<' && b[s + 1] == '!' && b[s + 2] == '-' && b[s + 3] == '-')
            {
                s += 4;
                while (b[s] != 0 && !(b[s] == '-' && b[s + 1] == '-' && b[s + 2] == '>'))
                {
                    ++s;
                }
                if (b[s] == 0)
                {
                    return THROW_ERROR(xml_parse_status.status_bad_doctype, s);
                }

                s += 3;
            }
            else
            {
                return THROW_ERROR(xml_parse_status.status_bad_doctype, s);
            }

            return s;
        }

        private int parse_doctype_ignore(int s)
        {
            int depth = 0;

            s += 3;

            while (b[s] != 0)
            {
                if (b[s] == '<' && b[s + 1] == '!' && b[s + 2] == '[')
                {
                    // nested ignore section
                    s += 3;
                    depth++;
                }
                else if (b[s] == ']' && b[s + 1] == ']' && b[s + 2] == '>')
                {
                    // ignore section end
                    s += 3;

                    if (depth == 0)
                    {
                        return s;
                    }

                    depth--;
                }
                else
                {
                    s++;
                }
            }

            return THROW_ERROR(xml_parse_status.status_bad_doctype, s);
        }

        private int parse_doctype_group(int s, byte endch)
        {
            int depth = 0;

            s += 2;

            while (b[s] != 0)
            {
                if (b[s] == '<' && b[s + 1] == '!' && b[s + 2] != '-')
                {
                    if (b[s + 2] == '[')
                    {
                        // ignore
                        s = parse_doctype_ignore(s);
                        if (s < 0)
                        {
                            return s;
                        }
                    }
                    else
                    {
                        // some control group
                        s += 2;
                        depth++;
                    }
                }
                else if (b[s] == '<' || b[s] == '"' || b[s] == '\'')
                {
                    // unknown tag (forbidden), or some primitive group
                    s = parse_doctype_primitive(s);
                    if (s < 0)
                    {
                        return s;
                    }
                }
                else if (b[s] == '>')
                {
                    if (depth == 0)
                    {
                        return s;
                    }

                    depth--;
                    s++;
                }
                else
                {
                    s++;
                }
            }

            if (depth != 0 || endch != '>')
            {
                return THROW_ERROR(xml_parse_status.status_bad_doctype, s);
            }

            return s;
        }

        private int parse_exclamation(int s, xml_node cursor, uint optmsk, byte endch)
        {
            // parse node contents, starting with exclamation mark
            ++s;

            if (b[s] == '-') // '<!-...'
            {
                ++s;

                if (b[s] == '-') // '<!--...'
                {
                    ++s;

                    if ((optmsk & pugi.parse_comments) != 0)
                    {
                        cursor = append_new_node(cursor, xml_node_type.node_comment); // Append a new node on the tree.
                        cursor.value_offset = s; // Save the offset.
                    }

                    if ((optmsk & pugi.parse_eol) != 0 && (optmsk & pugi.parse_comments) != 0)
                    {
                        s = strconv_comment(s, endch);

                        if (s < 0)
                        {
                            return THROW_ERROR(xml_parse_status.status_bad_comment, cursor.value_offset);
                        }
                    }
                    else
                    {
                        // Scan for terminating '-->'.
                        while (b[s] != 0 && !(b[s] == '-' && b[s + 1] == '-' && ENDSWITH(b[s + 2], '>', endch)))
                        {
                            ++s;
                        }
                        if (b[s] == 0)
                        {
                            return THROW_ERROR(xml_parse_status.status_bad_comment, s);
                        }

                        if ((optmsk & pugi.parse_comments) != 0)
                        {
                            b[s] = 0; // Zero-terminate this segment at the first terminating '-'.
                        }

                        s += (b[s + 2] == '>' ? 3 : 2); // Step over the '\0->'.
                    }
                }
                else
                {
                    return THROW_ERROR(xml_parse_status.status_bad_comment, s);
                }
            }
            else if (b[s] == '[')
            {
                // '<![CDATA[...'
                if (b[++s] == 'C' && b[++s] == 'D' && b[++s] == 'A' && b[++s] == 'T' && b[++s] == 'A' && b[++s] == '[')
                {
                    ++s;

                    if ((optmsk & pugi.parse_cdata) != 0)
                    {
                        cursor = append_new_node(cursor, xml_node_type.node_cdata); // Append a new node on the tree.
                        cursor.value_offset = s; // Save the offset.

                        if ((optmsk & pugi.parse_eol) != 0)
                        {
                            s = strconv_cdata(s, endch);

                            if (s < 0)
                            {
                                return THROW_ERROR(xml_parse_status.status_bad_cdata, cursor.value_offset);
                            }
                        }
                        else
                        {
                            // Scan for terminating ']]>'.
                            while (b[s] != 0 && !(b[s] == ']' && b[s + 1] == ']' && ENDSWITH(b[s + 2], '>', endch)))
                            {
                                ++s;
                            }
                            if (b[s] == 0)
                            {
                                return THROW_ERROR(xml_parse_status.status_bad_cdata, s);
                            }

                            b[s++] = 0; // Zero-terminate this segment.
                        }
                    }
                    else // Flagged for discard, but we still have to scan for the terminator.
                    {
                        // Scan for terminating ']]>'.
                        while (b[s] != 0 && !(b[s] == ']' && b[s + 1] == ']' && ENDSWITH(b[s + 2], '>', endch)))
                        {
                            ++s;
                        }
                        if (b[s] == 0)
                        {
                            return THROW_ERROR(xml_parse_status.status_bad_cdata, s);
                        }

                        ++s;
                    }

                    s += (b[s + 1] == '>' ? 2 : 1); // Step over the last ']>'.
                }
                else
                {
                    return THROW_ERROR(xml_parse_status.status_bad_cdata, s);
                }
            }
            else if (b[s] == 'D' && b[s + 1] == 'O' && b[s + 2] == 'C' && b[s + 3] == 'T' && b[s + 4] == 'Y' && b[s + 5] == 'P' && ENDSWITH(b[s + 6], 'E', endch))
            {
                s -= 2;

                if (cursor._parent != null)
                {
                    return THROW_ERROR(xml_parse_status.status_bad_doctype, s);
                }

                int mark = s + 9;

                s = parse_doctype_group(s, endch);
                if (s < 0)
                {
                    return s;
                }

                if (b[s] != 0)
                {
                    b[s++] = 0;
                }

                if ((optmsk & pugi.parse_doctype) != 0)
                {
                    while (IS(mark, chartype.ct_space))
                    {
                        ++mark;
                    }

                    cursor = append_new_node(cursor, xml_node_type.node_doctype);

                    cursor.value_offset = mark;
                }
            }
            else if (b[s] == 0 && endch == '-')
            {
                return THROW_ERROR(xml_parse_status.status_bad_comment, s);
            }
            else if (b[s] == 0 && endch == '[')
            {
                return THROW_ERROR(xml_parse_status.status_bad_cdata, s);
            }
            else
            {
                return THROW_ERROR(xml_parse_status.status_unrecognized_tag, s);
            }

            return s;
        }

        private int parse_question(int s, ref xml_node ref_cursor, uint optmsk, byte endch)
        {
            // load into registers
            xml_node cursor = ref_cursor;
            byte ch = 0;

            // parse node contents, starting with question mark
            ++s;

            // read PI target
            int target = s;

            if (!IS(s, chartype.ct_start_symbol))
            {
                return THROW_ERROR(xml_parse_status.status_bad_pi, s);
            }

            while (IS(s, chartype.ct_symbol))
            {
                ++s;
            }
            if (b[s] == 0)
            {
                return THROW_ERROR(xml_parse_status.status_bad_pi, s);
            }

            // determine node type; stricmp / strcasecmp is not portable
            bool declaration = (b[target] | ' ') == 'x' && (b[target + 1] | ' ') == 'm' && (b[target + 2] | ' ') == 'l' && target + 3 == s;

            if (declaration ? (optmsk & pugi.parse_declaration) != 0 : (optmsk & pugi.parse_pi) != 0)
            {
                if (declaration)
                {
                    // disallow non top-level declarations
                    if (cursor._parent != null)
                    {
                        return THROW_ERROR(xml_parse_status.status_bad_pi, s);
                    }

                    cursor = append_new_node(cursor, xml_node_type.node_declaration);
                }
                else
                {
                    cursor = append_new_node(cursor, xml_node_type.node_pi);
                }

                cursor.name_offset = target;

                ch = b[s];
                b[s] = 0;
                ++s;

                // parse value/attributes
                if (ch == '?')
                {
                    // empty node
                    if (!ENDSWITH(b[s], '>', endch))
                    {
                        return THROW_ERROR(xml_parse_status.status_bad_pi, s);
                    }
                    s += (b[s] == '>') ? 1 : 0;

                    cursor = cursor._parent;
                }
                else if (ISCH(ch, chartype.ct_space))
                {
                    while (IS(s, chartype.ct_space))
                    {
                        ++s;
                    }

                    // scan for tag end
                    int value = s;

                    while (b[s] != 0 && !(b[s] == '?' && ENDSWITH(b[s + 1], '>', endch)))
                    {
                        ++s;
                    }
                    if (b[s] == 0)
                    {
                        return THROW_ERROR(xml_parse_status.status_bad_pi, s);
                    }

                    if (declaration)
                    {
                        // replace ending ? with / so that 'element' terminates properly
                        b[s] = (byte)'/';

                        // we exit from this function with cursor at node_declaration, which is a signal to parse() to go to LOC_ATTRIBUTES
                        s = value;
                    }
                    else
                    {
                        // store value and step over >
                        cursor.value_offset = value;

                        cursor = cursor._parent;

                        ch = b[s];
                        b[s] = 0;
                        ++s;

                        s += (b[s] == '>') ? 1 : 0;
                    }
                }
                else
                {
                    return THROW_ERROR(xml_parse_status.status_bad_pi, s);
                }
            }
            else
            {
                // scan for tag end
                while (b[s] != 0 && !(b[s] == '?' && ENDSWITH(b[s + 1], '>', endch)))
                {
                    ++s;
                }
                if (b[s] == 0)
                {
                    return THROW_ERROR(xml_parse_status.status_bad_pi, s);
                }

                s += (b[s + 1] == '>' ? 2 : 1);
            }

            // store from registers
            ref_cursor = cursor;

            return s;
        }

        // The body of LOC_ATTRIBUTES in parse_tree. Returns the new position, or -1 after an error.
        private int parse_attributes(int s, ref xml_node cursor, uint optmsk, byte endch)
        {
            byte ch;
            while (true)
            {
                while (IS(s, chartype.ct_space)) // Eat any whitespace.
                {
                    ++s;
                }

                if (IS(s, chartype.ct_start_symbol)) // <... #...
                {
                    xml_attribute a = new xml_attribute(); // Make space for this attribute.
                    cursor._attributes.Add(a);

                    a.name_offset = s; // Save the offset.

                    while (IS(s, chartype.ct_symbol)) // Scan for a terminator.
                    {
                        ++s;
                    }
                    ch = b[s]; // Save char in 'ch', terminate & step over.
                    b[s] = 0;
                    ++s;

                    if (ISCH(ch, chartype.ct_space))
                    {
                        while (IS(s, chartype.ct_space)) // Eat any whitespace.
                        {
                            ++s;
                        }

                        ch = b[s];
                        ++s;
                    }

                    if (ch == '=') // '<... #=...'
                    {
                        while (IS(s, chartype.ct_space)) // Eat any whitespace.
                        {
                            ++s;
                        }

                        if (b[s] == '"' || b[s] == '\'') // '<... #="...'
                        {
                            ch = b[s]; // Save quote char to avoid breaking on "''" -or- '""'.
                            ++s; // Step over the quote.
                            a.value_offset = s; // Save the offset.

                            s = strconv_attribute(s, ch, optmsk);

                            if (s < 0)
                            {
                                return THROW_ERROR(xml_parse_status.status_bad_attribute, a.value_offset);
                            }

                            // After this line the loop continues from the start;
                            // Whitespaces, / and > are ok, symbols and EOF are wrong,
                            // everything else will be detected
                            if (IS(s, chartype.ct_start_symbol))
                            {
                                return THROW_ERROR(xml_parse_status.status_bad_attribute, s);
                            }
                        }
                        else
                        {
                            return THROW_ERROR(xml_parse_status.status_bad_attribute, s);
                        }
                    }
                    else
                    {
                        return THROW_ERROR(xml_parse_status.status_bad_attribute, s);
                    }
                }
                else if (b[s] == '/')
                {
                    ++s;

                    if (b[s] == '>')
                    {
                        cursor = cursor._parent;
                        s++;
                        break;
                    }
                    else if (b[s] == 0 && endch == '>')
                    {
                        cursor = cursor._parent;
                        break;
                    }
                    else
                    {
                        return THROW_ERROR(xml_parse_status.status_bad_start_element, s);
                    }
                }
                else if (b[s] == '>')
                {
                    ++s;

                    break;
                }
                else if (b[s] == 0 && endch == '>')
                {
                    break;
                }
                else
                {
                    return THROW_ERROR(xml_parse_status.status_bad_start_element, s);
                }
            }
            return s;
        }

        private int parse_tree(int s, xml_node root, uint optmsk, byte endch)
        {
            bool pcdata_trim = (optmsk & pugi.parse_trim_pcdata) != 0;
            bool pcdata_eol = (optmsk & pugi.parse_eol) != 0;
            bool pcdata_escape = (optmsk & pugi.parse_escapes) != 0;

            byte ch = 0;
            xml_node cursor = root;
            int mark = s;

            while (b[s] != 0)
            {
                bool at_tag;
                if (b[s] == '<')
                {
                    ++s;
                    at_tag = true;
                }
                else
                {
                    at_tag = false;
                }

                while (true)
                {
                    if (at_tag)
                    {
                        // LOC_TAG:
                        if (IS(s, chartype.ct_start_symbol)) // '<#...'
                        {
                            cursor = append_new_node(cursor, xml_node_type.node_element); // Append a new node to the tree.

                            cursor.name_offset = s;

                            while (IS(s, chartype.ct_symbol)) // Scan for a terminator.
                            {
                                ++s;
                            }
                            ch = b[s]; // Save char in 'ch', terminate & step over.
                            b[s] = 0;
                            ++s;

                            if (ch == '>')
                            {
                                // end of tag
                            }
                            else if (ISCH(ch, chartype.ct_space))
                            {
                                s = parse_attributes(s, ref cursor, optmsk, endch);
                                if (s < 0)
                                {
                                    return s;
                                }

                                // !!!
                            }
                            else if (ch == '/') // '<#.../'
                            {
                                if (!ENDSWITH(b[s], '>', endch))
                                {
                                    return THROW_ERROR(xml_parse_status.status_bad_start_element, s);
                                }

                                cursor = cursor._parent; // Pop.

                                s += (b[s] == '>') ? 1 : 0;
                            }
                            else if (ch == 0)
                            {
                                // we stepped over null terminator, backtrack & handle closing tag
                                --s;

                                if (endch != '>')
                                {
                                    return THROW_ERROR(xml_parse_status.status_bad_start_element, s);
                                }
                            }
                            else
                            {
                                return THROW_ERROR(xml_parse_status.status_bad_start_element, s);
                            }
                        }
                        else if (b[s] == '/')
                        {
                            ++s;

                            mark = s;

                            int name = cursor.name_offset;
                            if (name < 0)
                            {
                                return THROW_ERROR(xml_parse_status.status_end_element_mismatch, mark);
                            }

                            while (IS(s, chartype.ct_symbol))
                            {
                                if (b[s++] != b[name++])
                                {
                                    return THROW_ERROR(xml_parse_status.status_end_element_mismatch, mark);
                                }
                            }

                            if (b[name] != 0)
                            {
                                if (b[s] == 0 && b[name] == endch && b[name + 1] == 0)
                                {
                                    return THROW_ERROR(xml_parse_status.status_bad_end_element, s);
                                }
                                else
                                {
                                    return THROW_ERROR(xml_parse_status.status_end_element_mismatch, mark);
                                }
                            }

                            cursor = cursor._parent; // Pop.

                            while (IS(s, chartype.ct_space))
                            {
                                ++s;
                            }

                            if (b[s] == 0)
                            {
                                if (endch != '>')
                                {
                                    return THROW_ERROR(xml_parse_status.status_bad_end_element, s);
                                }
                            }
                            else
                            {
                                if (b[s] != '>')
                                {
                                    return THROW_ERROR(xml_parse_status.status_bad_end_element, s);
                                }
                                ++s;
                            }
                        }
                        else if (b[s] == '?') // '<?...'
                        {
                            s = parse_question(s, ref cursor, optmsk, endch);
                            if (s < 0)
                            {
                                return s;
                            }

                            if (cursor._type == xml_node_type.node_declaration)
                            {
                                s = parse_attributes(s, ref cursor, optmsk, endch);
                                if (s < 0)
                                {
                                    return s;
                                }
                            }
                        }
                        else if (b[s] == '!') // '<!...'
                        {
                            s = parse_exclamation(s, cursor, optmsk, endch);
                            if (s < 0)
                            {
                                return s;
                            }
                        }
                        else if (b[s] == 0 && endch == '?')
                        {
                            return THROW_ERROR(xml_parse_status.status_bad_pi, s);
                        }
                        else
                        {
                            return THROW_ERROR(xml_parse_status.status_unrecognized_tag, s);
                        }

                        // back to the outer loop
                        break;
                    }

                    mark = s; // Save this offset while searching for a terminator.

                    // MaterialX: Check for newlines and add a new node if required.
                    if ((optmsk & pugi.parse_newlines) != 0)
                    {
                        if (IS(s, chartype.ct_space))
                        {
                            int lineCount = 0;
                            while (IS(s, chartype.ct_space))
                            {
                                if (b[s] == '\n')
                                {
                                    lineCount++;
                                }
                                ++s;
                            }
                            for (int i = 1; i < lineCount; i++)
                            {
                                cursor = append_new_node(cursor, xml_node_type.node_newline);
                                cursor = cursor._parent;
                            }
                        }
                    }
                    else
                    {
                        while (IS(s, chartype.ct_space)) // Eat whitespace if no genuine PCDATA here.
                        {
                            ++s;
                        }
                    }

                    if (b[s] == '<' || b[s] == 0)
                    {
                        // We skipped some whitespace characters because otherwise we would take the tag branch instead of PCDATA one
                        if ((optmsk & (pugi.parse_ws_pcdata | pugi.parse_ws_pcdata_single)) == 0 || (optmsk & pugi.parse_trim_pcdata) != 0)
                        {
                            break;
                        }
                        else if ((optmsk & pugi.parse_ws_pcdata_single) != 0)
                        {
                            if (b[s] != '<' || b[s + 1] != '/' || cursor._children.Count != 0)
                            {
                                break;
                            }
                        }
                    }

                    if ((optmsk & pugi.parse_trim_pcdata) == 0)
                    {
                        s = mark;
                    }

                    if (cursor._parent != null || (optmsk & pugi.parse_fragment) != 0)
                    {
                        if ((optmsk & pugi.parse_embed_pcdata) != 0 && cursor._parent != null && cursor._children.Count == 0 && cursor.value_offset < 0)
                        {
                            cursor.value_offset = s; // Save the offset.
                        }
                        else
                        {
                            cursor = append_new_node(cursor, xml_node_type.node_pcdata); // Append a new node on the tree.

                            cursor.value_offset = s; // Save the offset.

                            cursor = cursor._parent; // Pop since this is a standalone.
                        }

                        s = strconv_pcdata(s, pcdata_trim, pcdata_eol, pcdata_escape);

                        if (b[s] == 0)
                        {
                            goto done;
                        }
                    }
                    else
                    {
                        while (b[s] != 0 && !(b[s] == '<')) // '...<'
                        {
                            ++s;
                        }
                        if (b[s] == 0)
                        {
                            goto done;
                        }

                        ++s;
                    }

                    // We're after '<'
                    at_tag = true;
                }
            }

        done:
            // check that last tag is closed
            if (cursor != root)
            {
                return THROW_ERROR(xml_parse_status.status_end_element_mismatch, s);
            }

            return s;
        }

        private int parse_skip_bom(int s)
        {
            return (b[s] == 0xef && b[s + 1] == 0xbb && b[s + 2] == 0xbf) ? s + 3 : s;
        }

        private static bool has_element_node_siblings(xml_node root, int first)
        {
            for (int i = first; i < root._children.Count; i++)
            {
                if (root._children[i]._type == xml_node_type.node_element)
                {
                    return true;
                }
            }

            return false;
        }

        // xml_parser Private Members
        private byte[] b;
        private int error_offset;
        private xml_parse_status error_status;
    }

    // Output Functions
    internal static class xml_output
    {
        private static bool ISX(char c, int ct)
        {
            return ((c < 128 ? chartype.chartypex_table[c] : chartype.chartypex_table[128]) & ct) != 0;
        }

        public static void text_output_escaped(StringBuilder writer, string s, int type)
        {
            int i = 0;
            while (i < s.Length)
            {
                int prev = i;

                // While *s is a usual symbol
                while (i < s.Length && !ISX(s[i], type))
                {
                    ++i;
                }

                writer.Append(s, prev, i - prev);

                if (i >= s.Length)
                {
                    break;
                }

                switch (s[i])
                {
                    case '&':
                        writer.Append("&amp;");
                        ++i;
                        break;
                    case '<':
                        // MaterialX: written as it is
                        writer.Append(s[i]);
                        ++i;
                        break;
                    case '>':
                        // MaterialX: written as it is
                        writer.Append(s[i]);
                        ++i;
                        break;
                    case '"':
                        writer.Append("&quot;");
                        ++i;
                        break;
                    default: // s is not a usual symbol
                    {
                        uint ch = s[i++];

                        writer.Append("&#");
                        writer.Append((char)((ch / 10) + '0'));
                        writer.Append((char)((ch % 10) + '0'));
                        writer.Append(';');
                        break;
                    }
                }
            }
        }

        public static void text_output(StringBuilder writer, string s, int type, uint flags)
        {
            if ((flags & pugi.format_no_escapes) != 0)
            {
                writer.Append(s);
            }
            else
            {
                text_output_escaped(writer, s, type);
            }
        }

        public static void text_output_cdata(StringBuilder writer, string s)
        {
            int i = 0;
            do
            {
                writer.Append("<![CDATA[");

                int prev = i;

                // look for ]]> sequence - we can't output it as is since it terminates CDATA
                while (i < s.Length && !(s[i] == ']' && i + 2 < s.Length && s[i + 1] == ']' && s[i + 2] == '>'))
                {
                    ++i;
                }

                // skip ]] if we stopped at ]]>, > will go to the next CDATA section
                if (i < s.Length)
                {
                    i += 2;
                }

                writer.Append(s, prev, i - prev);

                writer.Append("]]>");
            }
            while (i < s.Length);
        }

        public static void text_output_indent(StringBuilder writer, string indent, int indent_length, uint depth)
        {
            for (uint i = 0; i < depth; ++i)
            {
                writer.Append(indent, 0, indent_length);
            }
        }

        public static void node_output_comment(StringBuilder writer, string s)
        {
            writer.Append("<!--");

            int i = 0;
            while (i < s.Length)
            {
                int prev = i;

                // look for -\0 or -- sequence - we can't output it since -- is illegal in comment body
                while (i < s.Length && !(s[i] == '-' && (i + 1 >= s.Length || s[i + 1] == '-')))
                {
                    ++i;
                }

                writer.Append(s, prev, i - prev);

                if (i < s.Length)
                {
                    writer.Append("- ");
                    ++i;
                }
            }

            writer.Append("-->");
        }

        public static void node_output_pi_value(StringBuilder writer, string s)
        {
            int i = 0;
            while (i < s.Length)
            {
                int prev = i;

                // look for ?> sequence - we can't output it since ?> terminates PI
                while (i < s.Length && !(s[i] == '?' && i + 1 < s.Length && s[i + 1] == '>'))
                {
                    ++i;
                }

                writer.Append(s, prev, i - prev);

                if (i < s.Length)
                {
                    writer.Append("? >");
                    i += 2;
                }
            }
        }

        public static void node_output_attributes(StringBuilder writer, xml_node node, string indent, int indent_length, uint flags, uint depth)
        {
            const string default_name = ":anonymous";

            foreach (xml_attribute a in node._attributes)
            {
                if ((flags & (pugi.format_indent_attributes | pugi.format_raw)) == pugi.format_indent_attributes)
                {
                    writer.Append('\n');

                    text_output_indent(writer, indent, indent_length, depth + 1);
                }
                else
                {
                    writer.Append(' ');
                }

                writer.Append(a._name != null ? a._name : default_name);
                writer.Append("=\"");

                if (a._value != null)
                {
                    text_output(writer, a._value, chartype.ctx_special_attr, flags);
                }

                writer.Append('"');
            }
        }

        public static bool node_output_start(StringBuilder writer, xml_node node, string indent, int indent_length, uint flags, uint depth)
        {
            const string default_name = ":anonymous";
            string name = node._name != null ? node._name : default_name;

            writer.Append('<');
            writer.Append(name);

            if (node._attributes.Count != 0)
            {
                node_output_attributes(writer, node, indent, indent_length, flags, depth);
            }

            // element nodes can have value if parse_embed_pcdata was used
            if (node._value == null)
            {
                if (node._children.Count == 0)
                {
                    if ((flags & pugi.format_no_empty_element_tags) != 0)
                    {
                        writer.Append("></");
                        writer.Append(name);
                        writer.Append('>');

                        return false;
                    }
                    else
                    {
                        if ((flags & pugi.format_raw) == 0)
                        {
                            writer.Append(' ');
                        }

                        writer.Append("/>");

                        return false;
                    }
                }
                else
                {
                    writer.Append('>');

                    return true;
                }
            }
            else
            {
                writer.Append('>');

                text_output(writer, node._value, chartype.ctx_special_pcdata, flags);

                if (node._children.Count == 0)
                {
                    writer.Append("</");
                    writer.Append(name);
                    writer.Append('>');

                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        public static void node_output_end(StringBuilder writer, xml_node node)
        {
            const string default_name = ":anonymous";
            string name = node._name != null ? node._name : default_name;

            writer.Append("</");
            writer.Append(name);
            writer.Append('>');
        }

        public static void node_output_simple(StringBuilder writer, xml_node node, uint flags)
        {
            const string default_name = ":anonymous";

            switch (node._type)
            {
                case xml_node_type.node_pcdata:
                    text_output(writer, node._value != null ? node._value : "", chartype.ctx_special_pcdata, flags);
                    break;

                case xml_node_type.node_cdata:
                    text_output_cdata(writer, node._value != null ? node._value : "");
                    break;

                case xml_node_type.node_comment:
                    node_output_comment(writer, node._value != null ? node._value : "");
                    break;

                case xml_node_type.node_newline:
                    writer.Append("");
                    break;

                case xml_node_type.node_pi:
                    writer.Append("<?");
                    writer.Append(node._name != null ? node._name : default_name);

                    if (node._value != null)
                    {
                        writer.Append(' ');
                        node_output_pi_value(writer, node._value);
                    }

                    writer.Append("?>");
                    break;

                case xml_node_type.node_declaration:
                    writer.Append("<?");
                    writer.Append(node._name != null ? node._name : default_name);
                    node_output_attributes(writer, node, "", 0, flags | pugi.format_raw, 0);
                    writer.Append("?>");
                    break;

                case xml_node_type.node_doctype:
                    writer.Append("<!DOCTYPE");

                    if (node._value != null)
                    {
                        writer.Append(' ');
                        writer.Append(node._value);
                    }

                    writer.Append('>');
                    break;

                default:
                    break;
            }
        }

        private const uint indent_newline = 1;
        private const uint indent_indent = 2;

        private static xml_node next_sibling(xml_node node)
        {
            xml_node parent = node._parent;
            if (parent == null)
            {
                return null;
            }
            int index = parent._children.IndexOf(node);
            return index + 1 < parent._children.Count ? parent._children[index + 1] : null;
        }

        public static void node_output(StringBuilder writer, xml_node root, string indent, uint flags, uint depth)
        {
            int indent_length = ((flags & (pugi.format_indent | pugi.format_indent_attributes)) != 0 && (flags & pugi.format_raw) == 0) ? indent.Length : 0;
            uint indent_flags = indent_indent;

            // The position of each node among its siblings, so that moving to the next sibling takes constant time.
            Stack<int> positions = new Stack<int>();
            int position = 0;

            xml_node node = root;

            do
            {
                // begin writing current node
                if (node._type == xml_node_type.node_pcdata || node._type == xml_node_type.node_cdata)
                {
                    node_output_simple(writer, node, flags);

                    indent_flags = 0;
                }
                else
                {
                    if ((indent_flags & indent_newline) != 0 && (flags & pugi.format_raw) == 0)
                    {
                        writer.Append('\n');
                    }

                    if (node._type != xml_node_type.node_newline)
                    {
                        if ((indent_flags & indent_indent) != 0 && indent_length != 0)
                        {
                            text_output_indent(writer, indent, indent_length, depth);
                        }
                    }

                    if (node._type == xml_node_type.node_element)
                    {
                        indent_flags = indent_newline | indent_indent;

                        if (node_output_start(writer, node, indent, indent_length, flags, depth))
                        {
                            // element nodes can have value if parse_embed_pcdata was used
                            if (node._value != null)
                            {
                                indent_flags = 0;
                            }

                            positions.Push(position);
                            position = 0;
                            node = node._children[0];
                            depth++;
                            continue;
                        }
                    }
                    else if (node._type == xml_node_type.node_document)
                    {
                        indent_flags = indent_indent;

                        if (node._children.Count != 0)
                        {
                            positions.Push(position);
                            position = 0;
                            node = node._children[0];
                            continue;
                        }
                    }
                    else
                    {
                        node_output_simple(writer, node, flags);

                        indent_flags = indent_newline | indent_indent;
                    }
                }

                // continue to the next node
                while (node != root)
                {
                    xml_node parent = node._parent;
                    if (position + 1 < parent._children.Count)
                    {
                        position++;
                        node = parent._children[position];
                        break;
                    }

                    node = parent;
                    position = positions.Pop();

                    // write closing node
                    if (node._type == xml_node_type.node_element)
                    {
                        depth--;

                        if ((indent_flags & indent_newline) != 0 && (flags & pugi.format_raw) == 0)
                        {
                            writer.Append('\n');
                        }

                        if ((indent_flags & indent_indent) != 0 && indent_length != 0)
                        {
                            text_output_indent(writer, indent, indent_length, depth);
                        }

                        node_output_end(writer, node);

                        indent_flags = indent_newline | indent_indent;
                    }
                }
            }
            while (node != root);

            if ((indent_flags & indent_newline) != 0 && (flags & pugi.format_raw) == 0)
            {
                writer.Append('\n');
            }
        }

        public static bool has_declaration(xml_node node)
        {
            foreach (xml_node child in node._children)
            {
                xml_node_type type = child._type;

                if (type == xml_node_type.node_declaration)
                {
                    return true;
                }
                if (type == xml_node_type.node_element)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
