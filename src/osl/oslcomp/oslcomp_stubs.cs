// Temporary: members of OSLCompilerImpl whose C++ translation unit has not been ported yet. Each goes when its file lands.
namespace OSL
{
    namespace pvt
    {
        public partial class OSLCompilerImpl
        {
            public void add_struct_fields(StructSpec structspec, OIIO.ustring basename, SymType symtype, int arraylen, ASTNode node = null, ASTNode init = null)
            {
                throw new System.NotImplementedException();
            }
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
            public void push_nesting(bool isloop = false) { throw new System.NotImplementedException(); }
            public void pop_nesting(bool isloop = false) { throw new System.NotImplementedException(); }
        }
    }
}
