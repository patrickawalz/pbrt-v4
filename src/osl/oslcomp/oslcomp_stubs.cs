// Temporary: members of OSLCompilerImpl whose C++ translation unit has not been ported yet. Each goes when its file lands.
namespace OSL
{
    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
        }
    }
}
namespace OSL
{
    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
            public static void coalesce_temporaries(System.Collections.Generic.List<Symbol> symtab) { throw new System.NotImplementedException(); }
            private bool op_uses_sym(Opcode op, Symbol sym, bool read = true, bool write = true) { throw new System.NotImplementedException(); }
            public void struct_field_pair(Symbol sym1, Symbol sym2, int fieldnum, out Symbol field1, out Symbol field2) { throw new System.NotImplementedException(); }
            public void struct_field_pair(StructSpec structspec, int fieldnum, OIIO.ustring sym1, OIIO.ustring sym2, out Symbol field1, out Symbol field2) { throw new System.NotImplementedException(); }
            public void push_nesting(bool isloop = false) { throw new System.NotImplementedException(); }
            public void pop_nesting(bool isloop = false) { throw new System.NotImplementedException(); }
        }
    }
}
