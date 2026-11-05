// OSL port: liboslexec/closure.cpp (print_closure) and liboslexec/opclosure.cpp (the closure shadeops)
//
// Strings stored in closures and in the heap are ustring hashes in this port, so 'treat_ustrings_as_hash' is always true in effect; the
// two C++ entry points (osl_closure_to_string, osl_closure_to_ustringhash) therefore both return the hash of the text.

using System;
using System.Text;
using OIIO;
using OSL.pvt;

namespace OSL.pvt
{
    public static unsafe class ClosurePrinter
    {
        private static string g6(float v)
        {
            return Fmt.format_g(v, 6, false, false);
        }

        private static void print_component_value(StringBuilder o, ShadingSystemImpl ss, TypeDesc type, void* data, bool treat_ustrings_as_hash)
        {
            if (type == TypeDescs.TypeInt)
            {
                o.Append(((*(int*)data)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (type == TypeDescs.TypeFloat)
            {
                o.Append(g6(*(float*)data));
            }
            else if (type == TypeDescs.TypeColor)
            {
                Color3* c = (Color3*)data;
                o.Append('(').Append(g6(c->x)).Append(", ").Append(g6(c->y)).Append(", ").Append(g6(c->z)).Append(')');
            }
            else if (type == TypeDescs.TypeVector)
            {
                Vec3* c = (Vec3*)data;
                o.Append('(').Append(g6(c->x)).Append(", ").Append(g6(c->y)).Append(", ").Append(g6(c->z)).Append(')');
            }
            else if (type == TypeDescs.TypeString)
            {
                o.Append('"').Append(OslStrings.from_hash(*(ulong*)data).@string()).Append('"');
            }
            else if (type.basetype == TypeDesc.PTR)  // this only happens for closures
            {
                print_closure(o, *(ClosureColor**)data, ss, treat_ustrings_as_hash);
            }
        }

        private static void print_component(StringBuilder o, ClosureComponent* comp, ShadingSystemImpl ss, Color3 weight, bool treat_ustrings_as_hash)
        {
            ClosureRegistry.ClosureEntry clentry = ss.find_closure(comp->id);
            if (clentry == null)
            {
                throw new InvalidOperationException("closure id not registered");
            }
            o.Append('(').Append(g6(weight.x * comp->w.x)).Append(", ").Append(g6(weight.y * comp->w.y)).Append(", ").Append(g6(weight.z * comp->w.z))
             .Append(") * ");
            o.Append(clentry.name.@string()).Append(" (");
            for (int i = 0, nparams = clentry.@params.Count - 1; i < nparams; ++i)
            {
                if (i != 0)
                {
                    o.Append(", ");
                }
                ClosureParam param = clentry.@params[i];
                if (param.key != null)
                {
                    o.Append('"').Append(param.key).Append("\", ");
                }
                if (param.type.numelements() > 1)
                {
                    o.Append('[');
                }
                TypeDesc et = param.type.elementtype();
                for (int j = 0; j < param.type.numelements(); ++j)
                {
                    if (j != 0)
                    {
                        o.Append(", ");
                    }
                    print_component_value(o, ss, et, (byte*)ClosureComponent.data(comp) + param.offset + et.size() * j, treat_ustrings_as_hash);
                }
                if (clentry.@params[i].type.numelements() > 1)
                {
                    o.Append(']');
                }
            }
            o.Append(')');
        }

        private static void print_closure(StringBuilder o, ClosureColor* closure, ShadingSystemImpl ss, Color3 w, ref bool first, bool treat_ustrings_as_hash)
        {
            if (closure == null)
            {
                return;
            }
            switch (closure->id)
            {
                case ClosureColor.MUL:
                    ClosureMul* mul = ClosureColor.as_mul(closure);
                    print_closure(o, mul->closure, ss, mul->weight * w, ref first, treat_ustrings_as_hash);
                    break;
                case ClosureColor.ADD:
                    ClosureAdd* add = ClosureColor.as_add(closure);
                    print_closure(o, add->closureA, ss, w, ref first, treat_ustrings_as_hash);
                    print_closure(o, add->closureB, ss, w, ref first, treat_ustrings_as_hash);
                    break;
                default:
                    if (!first)
                    {
                        o.Append("\n\t+ ");
                    }
                    print_component(o, ClosureColor.as_comp(closure), ss, w, treat_ustrings_as_hash);
                    first = false;
                    break;
            }
        }

        public static void print_closure(StringBuilder o, ClosureColor* closure, ShadingSystemImpl ss, bool treat_ustrings_as_hash)
        {
            bool first = true;
            print_closure(o, closure, ss, new Color3(1, 1, 1), ref first, treat_ustrings_as_hash);
        }

        public static string print_closure(ClosureColor* closure, ShadingSystemImpl ss)
        {
            StringBuilder o = new StringBuilder();
            print_closure(o, closure, ss, true);
            return o.ToString();
        }
    }
}

namespace OSL
{
    public static unsafe partial class OslRuntime
    {
        public static void* osl_add_closure_closure(void* oec, void* a_, void* b_)
        {
            ClosureColor* a = (ClosureColor*)a_;
            ClosureColor* b = (ClosureColor*)b_;
            if (a == null)
            {
                return b;
            }
            if (b == null)
            {
                return a;
            }
            ClosureAdd* add = (ClosureAdd*)RsFallback.rs_allocate_closure(oec, sizeof(ClosureAdd), 8);
            if (add != null)
            {
                add->id = ClosureColor.ADD;
                add->closureA = a;
                add->closureB = b;
            }
            return add;
        }

        public static void* osl_mul_closure_color(void* oec, void* a_, void* w_)
        {
            ClosureColor* a = (ClosureColor*)a_;
            Color3* w = (Color3*)w_;
            if (a == null)
            {
                return null;
            }
            if (w->x == 0.0f && w->y == 0.0f && w->z == 0.0f)
            {
                return null;
            }
            if (w->x == 1.0f && w->y == 1.0f && w->z == 1.0f)
            {
                return a;
            }
            ClosureMul* mul = (ClosureMul*)RsFallback.rs_allocate_closure(oec, sizeof(ClosureMul), 8);
            if (mul != null)
            {
                mul->id = ClosureColor.MUL;
                mul->weight = *w;
                mul->closure = a;
            }
            return mul;
        }

        public static void* osl_mul_closure_float(void* oec, void* a_, float w)
        {
            ClosureColor* a = (ClosureColor*)a_;
            if (a == null)
            {
                return null;
            }
            if (w == 0.0f)
            {
                return null;
            }
            if (w == 1.0f)
            {
                return a;
            }
            ClosureMul* mul = (ClosureMul*)RsFallback.rs_allocate_closure(oec, sizeof(ClosureMul), 8);
            if (mul != null)
            {
                mul->id = ClosureColor.MUL;
                mul->weight = new Color3(w, w, w);
                mul->closure = a;
            }
            return mul;
        }

        public static void* osl_allocate_closure_component(void* oec, int id, int size)
        {
            int needed = sizeof(ClosureComponent) + size;
            ClosureComponent* comp = (ClosureComponent*)RsFallback.rs_allocate_closure(oec, needed, 16);
            if (comp != null)
            {
                comp->id = id;
                comp->w = new Vec3(1.0f, 1.0f, 1.0f);
            }
            return comp;
        }

        public static void* osl_allocate_weighted_closure_component(void* oec, int id, int size, void* w_)
        {
            Color3* w = (Color3*)w_;
            if (w->x == 0.0f && w->y == 0.0f && w->z == 0.0f)
            {
                return null;
            }
            int needed = sizeof(ClosureComponent) + size;
            ClosureComponent* comp = (ClosureComponent*)RsFallback.rs_allocate_closure(oec, needed, 16);
            if (comp != null)
            {
                comp->id = id;
                comp->w = new Vec3(w->x, w->y, w->z);
            }
            return comp;
        }

        public static ulong osl_closure_to_string(void* oec, void* c_)
        {
            ShaderGlobals* sg = (ShaderGlobals*)oec;
            return new ustring(ClosurePrinter.print_closure((ClosureColor*)c_, ShaderGlobalsAccess.get_context(sg).shadingsys())).hash();
        }

        public static ulong osl_closure_to_ustringhash(void* oec, void* c_)
        {
            return osl_closure_to_string(oec, c_);
        }
    }
}
