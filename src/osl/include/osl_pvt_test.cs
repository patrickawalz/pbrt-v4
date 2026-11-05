// (not in OpenShadingLanguage) osl_pvt_test.cs

using System.Collections.Generic;
using OIIO;
using testing;
using static OIIO.TypeDescs;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static class OslPvtTests
        {
            [TEST("OslPvt", "TypeSpecBasics")]
            public static void OslPvt_TypeSpecBasics()
            {
                TypeSpec f = TypeFloat, c = TypeColor, p = TypePoint, v = TypeVector, n = TypeNormal, m = TypeMatrix, s = TypeString;
                EXPECT_TRUE(f.is_float() && f.is_scalarnum() && !f.is_triple());
                EXPECT_TRUE(c.is_color() && c.is_triple() && !c.is_vectriple());
                EXPECT_TRUE(p.is_vectriple() && v.is_vectriple() && n.is_vectriple());
                EXPECT_TRUE(m.is_matrix() && s.is_string());
                EXPECT_EQ(c.@string(), "color");
                EXPECT_EQ(m.@string(), "matrix");
                EXPECT_EQ(f.code_from_type(), "f");
                EXPECT_EQ(c.code_from_type(), "c");
                EXPECT_EQ(c.runtime_typecode(), "v");
                EXPECT_EQ(f.runtime_typecode(true), "df");
                EXPECT_EQ(m.runtime_typecode(true), "m");

                TypeSpec arr = TypeFloat;
                arr.make_array(5);
                EXPECT_TRUE(arr.is_array() && arr.is_sized_array() && arr.arraylength() == 5);
                EXPECT_EQ(arr.@string(), "float[5]");
                EXPECT_EQ(arr.code_from_type(), "f[5]");
                EXPECT_TRUE(arr.elementtype() == f);
                arr.make_array(-1);
                EXPECT_TRUE(arr.is_unsized_array());
                EXPECT_EQ(arr.@string(), "float[]");

                TypeSpec cl = new TypeSpec(TypeColor, true);
                EXPECT_TRUE(cl.is_closure() && !cl.is_color());
                EXPECT_EQ(cl.@string(), "closure color");
                EXPECT_EQ(cl.code_from_type(), "C");
                EXPECT_TRUE(new TypeSpec().is_unknown());
            }

            [TEST("OslPvt", "EquivalentAndAssignable")]
            public static void OslPvt_EquivalentAndAssignable()
            {
                TypeSpec p = TypePoint, v = TypeVector, c = TypeColor, f = TypeFloat, i = TypeInt;
                EXPECT_TRUE(PvtFunctions.equivalent(p, v));           // vector-like triples are interchangeable
                EXPECT_TRUE(PvtFunctions.equivalent(p, c));           // OIIO's equivalent() ignores the vector semantics, so does OSL's
                EXPECT_TRUE(!PvtFunctions.equivalent(f, i));
                EXPECT_TRUE(PvtFunctions.assignable(f, i));
                EXPECT_TRUE(PvtFunctions.assignable(c, f));
                EXPECT_TRUE(!PvtFunctions.assignable(i, f));
                TypeSpec cl = new TypeSpec(TypeColor, true);
                EXPECT_TRUE(PvtFunctions.assignable(cl, cl));
                EXPECT_TRUE(!PvtFunctions.assignable(cl, c));
                TypeSpec fa = TypeFloat, fu = TypeFloat;
                fa.make_array(3);
                fu.make_array(-1);
                EXPECT_TRUE(PvtFunctions.equivalent(fa, fu));          // an unsized array matches a sized one
                EXPECT_TRUE(PvtFunctions.relaxed_equivalent(fu, fa));
                TypeSpec v3 = TypeVector;
                EXPECT_TRUE(PvtFunctions.relaxed_equivalent(v3, f));   // one float may set a vec3
            }

            [TEST("OslPvt", "TypeCodes")]
            public static void OslPvt_TypeCodes()
            {
                List<TypeSpec> types = new List<TypeSpec>();
                TypeSpec.typespecs_from_codes("fcpvnmsi", types);
                EXPECT_EQ(types.Count, 8);
                EXPECT_TRUE(types[1].is_color() && types[2].is_point() && types[3].is_vector() && types[4].is_normal());
                EXPECT_EQ(TypeSpec.typelist_from_code("fc.?"), "float, color, ..., <any>");
                int advance;
                TypeSpec a = TypeSpec.type_from_code("f[3]c", 0, out advance);
                EXPECT_TRUE(a.is_sized_array() && a.arraylength() == 3);
                EXPECT_EQ(advance, 4);
                TypeSpec u = TypeSpec.type_from_code("f[]", 0, out advance);
                EXPECT_TRUE(u.is_unsized_array());
                EXPECT_EQ(advance, 3);
                TypeSpec cc = TypeSpec.type_from_code("C", 0, out advance);
                EXPECT_TRUE(cc.is_closure());
            }

            [TEST("OslPvt", "StructsAndSymbols")]
            public static void OslPvt_StructsAndSymbols()
            {
                TypeSpec t = new TypeSpec("pvt_test_struct", 0);
                EXPECT_TRUE(t.is_structure() && t.structure() > 0);
                StructSpec ss = t.structspec();
                ss.add_field(TypeFloat, new ustring("a"));
                ss.add_field(TypeColor, new ustring("b"));
                EXPECT_EQ(ss.numfields(), 2);
                EXPECT_EQ(ss.lookup_field(new ustring("b")), 1);
                EXPECT_EQ(ss.lookup_field(new ustring("zz")), -1);
                EXPECT_EQ(t.@string(), "struct pvt_test_struct");
                EXPECT_EQ(TypeSpec.structure_id("pvt_test_struct"), t.structure());
                EXPECT_EQ(t.code_from_type(), "S" + t.structure());
                TypeSpec back = TypeSpec.type_from_code("S" + t.structure());
                EXPECT_TRUE(back == t);

                Symbol sym = new Symbol(new ustring("x"), TypeFloat, SymType.SymTypeLocal);
                sym.scope(3);
                EXPECT_EQ(sym.mangled(), "___3_x");
                Symbol again = new Symbol(new ustring("___3_x"), TypeFloat, SymType.SymTypeLocal);
                EXPECT_EQ(again.unmangled(), "x");
                EXPECT_TRUE(!sym.everused());
                sym.mark_rw(4, true, false);
                sym.mark_rw(9, false, true);
                EXPECT_TRUE(sym.everread() && sym.everwritten());
                EXPECT_EQ(sym.firstuse(), 4);
                EXPECT_EQ(sym.lastuse(), 9);
                Symbol k = new Symbol(new ustring("$const1"), TypeColor, SymType.SymTypeConst);
                k.set_dataptr(SymArena.Absolute, new float[] { 0.25f, 0.5f, 1 });
                EXPECT_TRUE(k.get_vec3() == new Vec3(0.25f, 0.5f, 1));
                EXPECT_EQ(k.print_vals(), "0.25 0.5 1");
                EXPECT_EQ(new Symbol(new ustring("default"), TypeFloat, SymType.SymTypeLocal).cpp_safe_name(), "default_osl");
                EXPECT_EQ(PvtFunctions.shadertypename(PvtFunctions.shadertype_from_name("surface")), "surface");
            }

            [TEST("OslPvt", "UstringAndOpcode")]
            public static void OslPvt_UstringAndOpcode()
            {
                ustring a = new ustring("hello"), b = new ustring(new string("hello".ToCharArray()));
                EXPECT_TRUE(a == b && a.hash() == b.hash());
                ustring back;
                EXPECT_TRUE(ustring.from_hash(a.hash(), out back) && back == a);
                EXPECT_EQ(Strutil.escape_chars("a\n\"b\""), "a\\n\\\"b\\\"");
                EXPECT_EQ(Strutil.unescape_chars("a\\n\\x41\\101"), "a\nAA");

                Opcode op = new Opcode(new ustring("add"), new ustring(""), 0, 3);
                EXPECT_TRUE(op.argwrite(0) && !op.argread(0) && op.argread(1) && op.argread(2));
                op.argwriteonly(1);
                EXPECT_TRUE(op.argwrite(1) && !op.argread(1));
                op.add_jump(7);
                op.add_jump(9);
                EXPECT_EQ(op.jump(0), 7);
                EXPECT_EQ(op.farthest_jump(), 9);
                Opcode same = new Opcode(op);
                EXPECT_TRUE(Opcode.equivalent(op, same));
                same.add_jump(11);
                EXPECT_TRUE(!Opcode.equivalent(op, same));
            }
        }
    }
}
