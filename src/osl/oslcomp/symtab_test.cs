// (not in OpenShadingLanguage) symtab_test.cs

using System.Collections.Generic;
using OIIO;
using testing;
using static OIIO.TypeDescs;
using static testing.Gtest;

namespace OSL
{
    namespace pvt
    {
        public static class OslSymtabTests
        {
            [TEST("OslSymtab", "ScopesAndClashes")]
            public static void OslSymtab_ScopesAndClashes()
            {
                SymbolTable table = new SymbolTable();
                Symbol outer = new Symbol(new ustring("x"), TypeFloat, SymType.SymTypeLocal);
                table.insert(outer);
                EXPECT_EQ(outer.scope(), 0);
                table.push();
                EXPECT_TRUE(table.find(new ustring("x")) == outer);
                EXPECT_TRUE(table.clash(new ustring("x")) == null);   // shadowing in a new scope is not a clash
                Symbol inner = new Symbol(new ustring("x"), TypeInt, SymType.SymTypeLocal);
                table.insert(inner);
                EXPECT_TRUE(table.find(new ustring("x")) == inner);
                EXPECT_TRUE(table.clash(new ustring("x")) == inner);
                EXPECT_TRUE(table.find(new ustring("x"), inner) == outer);   // searching outside of 'inner'
                EXPECT_TRUE(table.find_exact(new ustring(inner.mangled())) == inner);
                table.pop();
                EXPECT_TRUE(table.find(new ustring("x")) == outer);
            }

            [TEST("OslSymtab", "ConstantsAndStructs")]
            public static void OslSymtab_ConstantsAndStructs()
            {
                ConstantSymbol i = new ConstantSymbol(new ustring("$c1"), 7);
                ConstantSymbol f = new ConstantSymbol(new ustring("$c2"), 2.5f);
                ConstantSymbol s = new ConstantSymbol(new ustring("$c3"), new ustring("abc"));
                ConstantSymbol c = new ConstantSymbol(new ustring("$c4"), TypeColor, 1, 2, 3);
                EXPECT_EQ(i.get_int(), 7);
                EXPECT_EQ(i.floatval(), 7.0f);
                EXPECT_EQ(f.floatval(), 2.5f);
                EXPECT_EQ(s.get_string().@string(), "abc");
                EXPECT_TRUE(c.get_vec3() == new Vec3(1, 2, 3));
                EXPECT_TRUE(i.is_constant());

                SymbolTable table = new SymbolTable();
                int id = table.new_struct(new ustring("symtab_test_struct"));
                table.add_struct_field(TypeFloat, new ustring("a"));
                table.add_struct_field(TypeColor, new ustring("b"));
                StructSpec spec = TypeSpec.structspec(id);
                EXPECT_EQ(spec.numfields(), 2);
                EXPECT_TRUE(table.find(new ustring("symtab_test_struct")).is_structure());
            }

            [TEST("OslSymtab", "VariableLifetimes")]
            public static void OslSymtab_VariableLifetimes()
            {
                // a = 1; b = a + 2; (a is read at op 1, written at op 0; b written at op 1)
                Symbol a = new Symbol(new ustring("a"), TypeFloat, SymType.SymTypeLocal);
                Symbol b = new Symbol(new ustring("b"), TypeFloat, SymType.SymTypeLocal);
                Symbol one = new ConstantSymbol(new ustring("$1"), 1.0f);
                Symbol two = new ConstantSymbol(new ustring("$2"), 2.0f);
                List<Symbol> opargs = new List<Symbol> { a, one, b, a, two };
                List<Opcode> code = new List<Opcode>();
                code.Add(new Opcode(new ustring("assign"), new ustring("___main___"), 0, 2));
                code.Add(new Opcode(new ustring("add"), new ustring("___main___"), 2, 3));
                SymtabFunctions.track_variable_lifetimes_main(code, opargs, new List<Symbol> { a, b, one, two });
                EXPECT_EQ(a.firstwrite(), 0);
                EXPECT_EQ(a.lastread(), 1);
                EXPECT_EQ(b.firstwrite(), 1);
                EXPECT_TRUE(!b.everread());
            }
        }
    }
}
