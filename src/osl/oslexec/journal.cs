// OSL port: liboslexec/journal.cpp
//
// decode_message turns a format specification (the hash of a {fmt} string) and the encoded arguments back into text; this is what the default
// RendererServices::errorfmt/printfmt use. TrackRecentlyReported and Report2ErrorHandler are the error de-duplication and reporting of journal.h.
// The binary journal buffer of the C++ (Organization/PageInfo/Writer/Reader, used to collect messages from many threads on a GPU) is not ported: here
// messages go straight from the RendererServices to the ShadingContext.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;

namespace OSL
{
    public static unsafe class Journal
    {
        /// Size in bytes of an encoded argument of the given type (pvt::size_of_encoded_type)
        public static int size_of_encoded_type(EncodedType t)
        {
            switch (t)
            {
                case EncodedType.kUstringHash: return 8;
                case EncodedType.kInt32: return 4;
                case EncodedType.kFloat: return 4;
                case EncodedType.kInt64: return 8;
                case EncodedType.kDouble: return 8;
                case EncodedType.kUInt32: return 4;
                case EncodedType.kUInt64: return 8;
                case EncodedType.kPointer: return IntPtr.Size;
                case EncodedType.kTypeDesc: return 8;
                default: return 0;
            }
        }

        private static string format_safe(string fmt, object arg)
        {
            try
            {
                return Fmt.format(fmt, arg);
            }
            catch (Exception e)
            {
                return "MIS-FORMAT: format \"" + fmt + "\" threw \"" + e.Message + "\"";
            }
        }

        /// Build the message text from the format (given by its ustring hash) and the encoded arguments. Returns the text; 'bytes_consumed' is the
        /// size of the argument values that were used (the return value of the C++).
        public static string decode_message(ulong format_hash, int arg_count, EncodedType* arg_types, byte* arg_values)
        {
            int consumed;
            return decode_message(format_hash, arg_count, arg_types, arg_values, out consumed);
        }

        public static string decode_message(ulong format_hash, int arg_count, EncodedType* arg_types, byte* arg_values, out int arg_offset)
        {
            StringBuilder built = new StringBuilder();
            string format = OslStrings.from_hash(format_hash).@string();
            int len = format.Length;
            int arg_index = 0;
            arg_offset = 0;
            const int rr_buf_size = 128;
            for (int j = 0; j < len;)
            {
                char cur_char = format[j++];
                if (cur_char == '{')
                {
                    bool is_rr_complete = false;
                    StringBuilder rr = new StringBuilder();
                    rr.Append(cur_char);
                    int rr_len = 1;
                    do
                    {
                        char next_char = j < len ? format[j] : '\0';
                        ++j;
                        if (rr_len == 1 && next_char == '{')
                        {
                            break;
                        }
                        rr.Append(next_char);
                        ++rr_len;
                        if (next_char == '}')
                        {
                            is_rr_complete = true;
                        }
                    }
                    while (!is_rr_complete && j < len && rr_len < rr_buf_size);
                    string replacement_region = rr.ToString();
                    if (!is_rr_complete)
                    {
                        built.Append(replacement_region);
                    }
                    else if (arg_index < arg_count)
                    {
                        EncodedType arg_type = arg_types[arg_index];
                        byte* src = arg_values + arg_offset;
                        string replacement;
                        switch (arg_type)
                        {
                            case EncodedType.kUstringHash:
                                replacement = format_safe(replacement_region, OslStrings.from_hash(*(ulong*)src).@string());
                                break;
                            case EncodedType.kInt32: replacement = format_safe(replacement_region, *(int*)src); break;
                            case EncodedType.kFloat: replacement = format_safe(replacement_region, *(float*)src); break;
                            case EncodedType.kDouble: replacement = format_safe(replacement_region, *(double*)src); break;
                            case EncodedType.kInt64: replacement = format_safe(replacement_region, *(long*)src); break;
                            case EncodedType.kUInt32: replacement = format_safe(replacement_region, *(uint*)src); break;
                            case EncodedType.kUInt64: replacement = format_safe(replacement_region, *(ulong*)src); break;
                            case EncodedType.kPointer:
                                replacement = format_safe(replacement_region, "0x" + ((long)*(void**)src).ToString("x"));
                                break;
                            case EncodedType.kTypeDesc:
                                replacement = format_safe(replacement_region, (*(TypeDesc*)src).ToString());
                                break;
                            default:
                                throw new InvalidOperationException("unhandled EncodedType");
                        }
                        arg_offset += size_of_encoded_type(arg_type);
                        ++arg_index;
                        if (replacement.Length > 1024)
                        {
                            replacement = replacement.Substring(0, 1024);
                        }
                        built.Append(replacement);
                    }
                }
                else
                {
                    built.Append(cur_char);
                    if (cur_char == '}' && j < len && format[j] == '}')
                    {
                        ++j;
                    }
                }
            }
            return built.ToString();
        }
    }

    namespace journal
    {
        /// Remembers the most recent messages so a repeated one can be left out (journal.h)
        public class TrackRecentlyReported
        {
            private bool m_limit_errors;
            private int m_error_history_capacity;
            private bool m_limit_warnings;
            private int m_warning_history_capacity;
            private LinkedList<string> m_errseen = new LinkedList<string>();
            private LinkedList<string> m_warnseen = new LinkedList<string>();

            public TrackRecentlyReported(bool limit_errors, int error_history_capacity, bool limit_warnings, int warning_history_capacity)
            {
                m_limit_errors = limit_errors;
                m_error_history_capacity = error_history_capacity;
                m_limit_warnings = limit_warnings;
                m_warning_history_capacity = warning_history_capacity;
            }

            public bool shouldReportError(string message)
            {
                int n = 0;
                if (!m_limit_errors)
                {
                    return true;
                }
                foreach (string s in m_errseen)
                {
                    if (s == message)
                    {
                        return false;
                    }
                    ++n;
                }
                if (n >= m_error_history_capacity)
                {
                    m_errseen.RemoveFirst();
                }
                m_errseen.AddLast(message);
                return true;
            }

            public bool shouldReportWarning(string message)
            {
                int n = 0;
                if (!m_limit_errors)
                {
                    return true;
                }
                foreach (string s in m_warnseen)
                {
                    if (s == message)
                    {
                        return false;
                    }
                    ++n;
                }
                if (n >= m_warning_history_capacity)
                {
                    m_warnseen.RemoveFirst();
                }
                m_warnseen.AddLast(message);
                return true;
            }
        }

        /// Where journaled messages end up
        public abstract class Reporter
        {
            public abstract void report_error(int thread_index, int shade_index, string message);
            public abstract void report_warning(int thread_index, int shade_index, string message);
            public abstract void report_print(int thread_index, int shade_index, string message);
            public abstract void report_file_print(int thread_index, int shade_index, string filename, string message);
        }

        /// A Reporter that sends everything to an ErrorHandler, leaving out the repeats
        public class Report2ErrorHandler : Reporter
        {
            private ErrorHandler m_eh;
            private TrackRecentlyReported m_tracker;

            public Report2ErrorHandler(ErrorHandler eh, TrackRecentlyReported tracker)
            {
                m_eh = eh;
                m_tracker = tracker;
            }

            public override void report_error(int thread_index, int shade_index, string message)
            {
                if (m_tracker.shouldReportError(message))
                {
                    m_eh.errorfmt(message);
                }
            }

            public override void report_warning(int thread_index, int shade_index, string message)
            {
                if (m_tracker.shouldReportWarning(message))
                {
                    m_eh.warningfmt(message);
                }
            }

            public override void report_print(int thread_index, int shade_index, string message)
            {
                m_eh.messagefmt(message);
            }

            public override void report_file_print(int thread_index, int shade_index, string filename, string message)
            {
                m_eh.messagefmt(Fmt.format("{}:{}", filename, message));
            }
        }
    }
}
