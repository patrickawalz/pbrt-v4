// OSL port: liboslcomp/oslgram.y (the token declarations)
//
// Bison numbers the named tokens from 258 and returns a single character as its own code; the same convention is kept here so that the
// lexer and the parser exchange the same integers as in the C++.

namespace OSL
{
    namespace pvt
    {

        public static class OslTokens
        {
            public const int IDENTIFIER = 258;
            public const int STRING_LITERAL = 259;
            public const int INT_LITERAL = 260;
            public const int FLOAT_LITERAL = 261;
            public const int COLORTYPE = 262;
            public const int FLOATTYPE = 263;
            public const int INTTYPE = 264;
            public const int MATRIXTYPE = 265;
            public const int NORMALTYPE = 266;
            public const int POINTTYPE = 267;
            public const int STRINGTYPE = 268;
            public const int VECTORTYPE = 269;
            public const int VOIDTYPE = 270;
            public const int CLOSURE = 271;
            public const int OUTPUT = 272;
            public const int PUBLIC = 273;
            public const int STRUCT = 274;
            public const int BREAK = 275;
            public const int CONTINUE = 276;
            public const int DO = 277;
            public const int ELSE = 278;
            public const int FOR = 279;
            public const int IF_TOKEN = 280;
            public const int ILLUMINATE = 281;
            public const int ILLUMINANCE = 282;
            public const int RETURN = 283;
            public const int WHILE = 284;
            public const int RESERVED = 285;
            public const int ADD_ASSIGN = 286;
            public const int SUB_ASSIGN = 287;
            public const int MUL_ASSIGN = 288;
            public const int DIV_ASSIGN = 289;
            public const int BIT_AND_ASSIGN = 290;
            public const int BIT_OR_ASSIGN = 291;
            public const int XOR_ASSIGN = 292;
            public const int SHL_ASSIGN = 293;
            public const int SHR_ASSIGN = 294;
            public const int OR_OP = 295;
            public const int AND_OP = 296;
            public const int EQ_OP = 297;
            public const int NE_OP = 298;
            public const int GE_OP = 299;
            public const int LE_OP = 300;
            public const int SHL_OP = 301;
            public const int SHR_OP = 302;
            public const int UMINUS_PREC = 303;
            public const int NOT_OP = 304;
            public const int INCREMENT = 305;
            public const int DECREMENT = 306;
            public const int METADATA_BEGIN = 307;
        }

    }  // namespace pvt
}  // namespace OSL
