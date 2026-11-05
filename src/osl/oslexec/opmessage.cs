// OSL port: liboslexec/opmessage.cpp (setmessage / getmessage)
//
// The shadeops take the ShaderGlobals as the first argument. Messages live in the ShadingContext's message list; values are copied into its pool.

using System;
using OIIO;
using OSL.pvt;

namespace OSL
{
    public static unsafe partial class OslRuntime
    {
        private static string hstr(ulong h)
        {
            return OslStrings.from_hash(h).@string();
        }

        public static void osl_setmessage(ShaderGlobals* sg, ulong name, long type_, void* val, int layeridx, ulong sourcefile, int sourceline)
        {
            TypeDesc type = TypeDescBits.from_long(type_);
            bool is_closure = type.basetype == TypeDesc.UNKNOWN;  // indicates closure
            if (is_closure)
            {
                type.basetype = (byte)TypeDesc.PTR;  // for closures, we store a pointer
            }
            ShadingContext ctx = ShaderGlobalsAccess.get_context(sg);
            MessageList messages = ctx.messages();

            // If there is already a message by this name, error
            Message m = messages.find(name);
            if (m != null)
            {
                if (m.name == name)
                {
                    if (m.has_data())
                    {
                        ctx.runtime_errorfmt("message \"{}\" already exists (created here: {}:{}) cannot set again from {}:{}", hstr(name), hstr(m.sourcefile),
                                     m.sourceline, hstr(sourcefile), sourceline);
                    }
                    else
                    {
                        // NOTE: this cannot be triggered when strict_messages=false because we won't record "failed" getmessage calls
                        ctx.runtime_errorfmt("message \"{}\" was queried before being set (queried here: {}:{}) setting it now ({}:{}) would lead to "
                                     + "inconsistent results", hstr(name), hstr(m.sourcefile), m.sourceline, hstr(sourcefile), sourceline);
                    }
                    return;
                }
            }
            // The message didn't exist -- create it
            messages.add(name, val, type, layeridx, sourcefile, sourceline);
        }

        public static int osl_getmessage(ShaderGlobals* sg, ulong source, ulong name, long type_, void* val, int derivs, int layeridx, ulong sourcefile,
                                         int sourceline)
        {
            TypeDesc type = TypeDescBits.from_long(type_);
            bool is_closure = type.basetype == TypeDesc.UNKNOWN;  // indicates closure
            if (is_closure)
            {
                type.basetype = (byte)TypeDesc.PTR;  // for closures, we store a pointer
            }
            if (source == Strings.trace.hash())
            {
                return ShaderGlobalsAccess.get_renderer(sg).getmessage(sg, source, name, type, val, derivs != 0) ? 1 : 0;
            }
            ShadingContext ctx = ShaderGlobalsAccess.get_context(sg);
            MessageList messages = ctx.messages();
            Message m = messages.find(name);
            if (m != null)
            {
                if (m.name == name)
                {
                    if (m.type != type)
                    {
                        ctx.runtime_errorfmt("type mismatch for message \"{}\" ({} as {} here: {}:{}) cannot fetch as {} from {}:{}", hstr(name),
                                     m.has_data() ? "created" : "queried", m.type.basetype == TypeDesc.PTR ? "closure color" : m.type.c_str(),
                                     hstr(m.sourcefile), m.sourceline, is_closure ? "closure color" : type.c_str(), hstr(sourcefile), sourceline);
                        return 0;
                    }
                    if (!m.has_data())
                    {
                        // The message was queried before it was set: it is still not there
                        return 0;
                    }
                    if (m.layeridx > layeridx)
                    {
                        ctx.runtime_errorfmt("message \"{}\" was set by layer #{} ({}:{}) but is being queried by layer #{} ({}:{}) - messages may only be "
                                     + "transferred from nodes that appear earlier in the shading network", hstr(name), m.layeridx, hstr(m.sourcefile),
                                     m.sourceline, layeridx, hstr(sourcefile), sourceline);
                        return 0;
                    }
                    long size = type.size();
                    Buffer.MemoryCopy(m.data, val, size, size);
                    if (derivs != 0)
                    {
                        new Span<byte>((byte*)val + size, (int)(2 * size)).Clear();
                    }
                    return 1;
                }
            }
            // Message not found. If strict messages, record that it was queried so a later setmessage can be flagged
            if (ctx.shadingsys().strict_messages())
            {
                messages.add(name, null, type, layeridx, sourcefile, sourceline);
            }
            return 0;
        }
    }
}
