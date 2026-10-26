// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// utf8proc.h, utf8proc.c -- C# port of utf8proc 2.11.0 (Unicode 17.0.0).
//
//  Copyright (c) 2014-2021 Steven G. Johnson, Jiahao Chen, Peter Colberg, Tony Kelman, Scott P. Jones, and other contributors.
//  Copyright (c) 2009 Public Software Group e. V., Berlin, Germany
//  (MIT license; see ext/utf8proc/LICENSE.md in the pbrt-v4 sources.)
//
// The pointer-based API is kept: strings are byte* (UTF-8), code point buffers int*.

using System;
using System.Runtime.InteropServices;

namespace utf8proc
{
    public unsafe delegate int utf8proc_custom_func(int codepoint, void* data);

    // The property struct of utf8proc.h; fields are read from the packed ushort table.
    public struct utf8proc_property_t
    {
        public short category, combining_class, bidi_class, decomp_type;
        public ushort decomp_seqindex, casefold_seqindex, uppercase_seqindex, lowercase_seqindex, titlecase_seqindex;
        public ushort comb_index, comb_length, comb_issecond;
        public uint bidi_mirrored, comp_exclusion, ignorable, control_boundary, charwidth, ambiguous_width, pad, boundclass, indic_conjunct_break;
    }

    public static unsafe partial class Utf8Proc
    {
        public const int UTF8PROC_VERSION_MAJOR = 2;
        public const int UTF8PROC_VERSION_MINOR = 11;
        public const int UTF8PROC_VERSION_PATCH = 0;

        // utf8proc_option_t
        public const int UTF8PROC_NULLTERM = 1;
        public const int UTF8PROC_STABLE = 2;
        public const int UTF8PROC_COMPAT = 4;
        public const int UTF8PROC_COMPOSE = 8;
        public const int UTF8PROC_DECOMPOSE = 16;
        public const int UTF8PROC_IGNORE = 32;
        public const int UTF8PROC_REJECTNA = 64;
        public const int UTF8PROC_NLF2LS = 128;
        public const int UTF8PROC_NLF2PS = 256;
        public const int UTF8PROC_STRIPCC = 512;
        public const int UTF8PROC_CASEFOLD = 1024;
        public const int UTF8PROC_CHARBOUND = 2048;
        public const int UTF8PROC_LUMP = 4096;
        public const int UTF8PROC_STRIPMARK = 8192;
        public const int UTF8PROC_STRIPNA = 16384;
        public const int UTF8PROC_NLF2LF = UTF8PROC_NLF2LS | UTF8PROC_NLF2PS;

        public const int UTF8PROC_ERROR_NOMEM = -1;
        public const int UTF8PROC_ERROR_OVERFLOW = -2;
        public const int UTF8PROC_ERROR_INVALIDUTF8 = -3;
        public const int UTF8PROC_ERROR_NOTASSIGNED = -4;
        public const int UTF8PROC_ERROR_INVALIDOPTS = -5;

        // utf8proc_category_t
        public const int UTF8PROC_CATEGORY_CN = 0;
        public const int UTF8PROC_CATEGORY_LU = 1;
        public const int UTF8PROC_CATEGORY_LL = 2;
        public const int UTF8PROC_CATEGORY_LT = 3;
        public const int UTF8PROC_CATEGORY_LM = 4;
        public const int UTF8PROC_CATEGORY_LO = 5;
        public const int UTF8PROC_CATEGORY_MN = 6;
        public const int UTF8PROC_CATEGORY_MC = 7;
        public const int UTF8PROC_CATEGORY_ME = 8;
        public const int UTF8PROC_CATEGORY_ND = 9;
        public const int UTF8PROC_CATEGORY_NL = 10;
        public const int UTF8PROC_CATEGORY_NO = 11;
        public const int UTF8PROC_CATEGORY_PC = 12;
        public const int UTF8PROC_CATEGORY_PD = 13;
        public const int UTF8PROC_CATEGORY_PS = 14;
        public const int UTF8PROC_CATEGORY_PE = 15;
        public const int UTF8PROC_CATEGORY_PI = 16;
        public const int UTF8PROC_CATEGORY_PF = 17;
        public const int UTF8PROC_CATEGORY_PO = 18;
        public const int UTF8PROC_CATEGORY_SM = 19;
        public const int UTF8PROC_CATEGORY_SC = 20;
        public const int UTF8PROC_CATEGORY_SK = 21;
        public const int UTF8PROC_CATEGORY_SO = 22;
        public const int UTF8PROC_CATEGORY_ZS = 23;
        public const int UTF8PROC_CATEGORY_ZL = 24;
        public const int UTF8PROC_CATEGORY_ZP = 25;
        public const int UTF8PROC_CATEGORY_CC = 26;
        public const int UTF8PROC_CATEGORY_CF = 27;
        public const int UTF8PROC_CATEGORY_CS = 28;
        public const int UTF8PROC_CATEGORY_CO = 29;
        // utf8proc_bidi_class_t
        public const int UTF8PROC_BIDI_CLASS_L = 1;
        public const int UTF8PROC_BIDI_CLASS_LRE = 2;
        public const int UTF8PROC_BIDI_CLASS_LRO = 3;
        public const int UTF8PROC_BIDI_CLASS_R = 4;
        public const int UTF8PROC_BIDI_CLASS_AL = 5;
        public const int UTF8PROC_BIDI_CLASS_RLE = 6;
        public const int UTF8PROC_BIDI_CLASS_RLO = 7;
        public const int UTF8PROC_BIDI_CLASS_PDF = 8;
        public const int UTF8PROC_BIDI_CLASS_EN = 9;
        public const int UTF8PROC_BIDI_CLASS_ES = 10;
        public const int UTF8PROC_BIDI_CLASS_ET = 11;
        public const int UTF8PROC_BIDI_CLASS_AN = 12;
        public const int UTF8PROC_BIDI_CLASS_CS = 13;
        public const int UTF8PROC_BIDI_CLASS_NSM = 14;
        public const int UTF8PROC_BIDI_CLASS_BN = 15;
        public const int UTF8PROC_BIDI_CLASS_B = 16;
        public const int UTF8PROC_BIDI_CLASS_S = 17;
        public const int UTF8PROC_BIDI_CLASS_WS = 18;
        public const int UTF8PROC_BIDI_CLASS_ON = 19;
        public const int UTF8PROC_BIDI_CLASS_LRI = 20;
        public const int UTF8PROC_BIDI_CLASS_RLI = 21;
        public const int UTF8PROC_BIDI_CLASS_FSI = 22;
        public const int UTF8PROC_BIDI_CLASS_PDI = 23;
        // utf8proc_decomp_type_t
        public const int UTF8PROC_DECOMP_TYPE_FONT = 1;
        public const int UTF8PROC_DECOMP_TYPE_NOBREAK = 2;
        public const int UTF8PROC_DECOMP_TYPE_INITIAL = 3;
        public const int UTF8PROC_DECOMP_TYPE_MEDIAL = 4;
        public const int UTF8PROC_DECOMP_TYPE_FINAL = 5;
        public const int UTF8PROC_DECOMP_TYPE_ISOLATED = 6;
        public const int UTF8PROC_DECOMP_TYPE_CIRCLE = 7;
        public const int UTF8PROC_DECOMP_TYPE_SUPER = 8;
        public const int UTF8PROC_DECOMP_TYPE_SUB = 9;
        public const int UTF8PROC_DECOMP_TYPE_VERTICAL = 10;
        public const int UTF8PROC_DECOMP_TYPE_WIDE = 11;
        public const int UTF8PROC_DECOMP_TYPE_NARROW = 12;
        public const int UTF8PROC_DECOMP_TYPE_SMALL = 13;
        public const int UTF8PROC_DECOMP_TYPE_SQUARE = 14;
        public const int UTF8PROC_DECOMP_TYPE_FRACTION = 15;
        public const int UTF8PROC_DECOMP_TYPE_COMPAT = 16;
        // utf8proc_boundclass_t
        public const int UTF8PROC_BOUNDCLASS_START = 0;
        public const int UTF8PROC_BOUNDCLASS_OTHER = 1;
        public const int UTF8PROC_BOUNDCLASS_CR = 2;
        public const int UTF8PROC_BOUNDCLASS_LF = 3;
        public const int UTF8PROC_BOUNDCLASS_CONTROL = 4;
        public const int UTF8PROC_BOUNDCLASS_EXTEND = 5;
        public const int UTF8PROC_BOUNDCLASS_L = 6;
        public const int UTF8PROC_BOUNDCLASS_V = 7;
        public const int UTF8PROC_BOUNDCLASS_T = 8;
        public const int UTF8PROC_BOUNDCLASS_LV = 9;
        public const int UTF8PROC_BOUNDCLASS_LVT = 10;
        public const int UTF8PROC_BOUNDCLASS_REGIONAL_INDICATOR = 11;
        public const int UTF8PROC_BOUNDCLASS_SPACINGMARK = 12;
        public const int UTF8PROC_BOUNDCLASS_PREPEND = 13;
        public const int UTF8PROC_BOUNDCLASS_ZWJ = 14;
        public const int UTF8PROC_BOUNDCLASS_E_BASE = 15;
        public const int UTF8PROC_BOUNDCLASS_E_MODIFIER = 16;
        public const int UTF8PROC_BOUNDCLASS_GLUE_AFTER_ZWJ = 17;
        public const int UTF8PROC_BOUNDCLASS_E_BASE_GAZ = 18;
        public const int UTF8PROC_BOUNDCLASS_EXTENDED_PICTOGRAPHIC = 19;
        public const int UTF8PROC_BOUNDCLASS_E_ZWG = 20;
        // Indic conjunct break
        public const int UTF8PROC_INDIC_CONJUNCT_BREAK_NONE = 0;
        public const int UTF8PROC_INDIC_CONJUNCT_BREAK_LINKER = 1;
        public const int UTF8PROC_INDIC_CONJUNCT_BREAK_CONSONANT = 2;
        public const int UTF8PROC_INDIC_CONJUNCT_BREAK_EXTEND = 3;

        private const int SSIZE_MAX = int.MaxValue;  // (size_t)SIZE_MAX/2 does not matter for managed int sizes
        private const ushort UINT16_MAX = 65535;

        public static readonly sbyte[] utf8proc_utf8class = {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            4, 4, 4, 4, 4, 4, 4, 4, 0, 0, 0, 0, 0, 0, 0, 0 };

        private const int UTF8PROC_HANGUL_SBASE = 0xAC00;
        private const int UTF8PROC_HANGUL_LBASE = 0x1100;
        private const int UTF8PROC_HANGUL_VBASE = 0x1161;
        private const int UTF8PROC_HANGUL_TBASE = 0x11A7;
        private const int UTF8PROC_HANGUL_LCOUNT = 19;
        private const int UTF8PROC_HANGUL_VCOUNT = 21;
        private const int UTF8PROC_HANGUL_TCOUNT = 28;
        private const int UTF8PROC_HANGUL_NCOUNT = 588;
        private const int UTF8PROC_HANGUL_SCOUNT = 11172;

        public static string utf8proc_version()
        {
            return UTF8PROC_VERSION_MAJOR + "." + UTF8PROC_VERSION_MINOR + "." + UTF8PROC_VERSION_PATCH;
        }

        public static string utf8proc_unicode_version() { return "17.0.0"; }

        public static string utf8proc_errmsg(long errcode)
        {
            switch (errcode)
            {
                case UTF8PROC_ERROR_NOMEM:
                    return "Memory for processing UTF-8 data could not be allocated.";
                case UTF8PROC_ERROR_OVERFLOW:
                    return "UTF-8 string is too long to be processed.";
                case UTF8PROC_ERROR_INVALIDUTF8:
                    return "Invalid UTF-8 string";
                case UTF8PROC_ERROR_NOTASSIGNED:
                    return "Unassigned Unicode code point found in UTF-8 string.";
                case UTF8PROC_ERROR_INVALIDOPTS:
                    return "Invalid options for UTF-8 processing chosen.";
                default:
                    return "An unknown error occurred while processing UTF-8 data.";
            }
        }

        private static bool utf_cont(int ch) { return (ch & 0xc0) == 0x80; }

        public static long utf8proc_iterate(byte* str, long strlen, int* dst)
        {
            int uc;
            byte* end;

            *dst = -1;
            if (strlen == 0) return 0;
            end = str + ((strlen < 0) ? 4 : strlen);
            uc = *str++;
            if (uc < 0x80)
            {
                *dst = uc;
                return 1;
            }
            // Must be between 0xc2 and 0xf4 inclusive to be valid
            if ((uint)(uc - 0xc2) > (0xf4 - 0xc2)) return UTF8PROC_ERROR_INVALIDUTF8;
            if (uc < 0xe0)
            {         // 2-byte sequence
                // Must have valid continuation character
                if (str >= end || !utf_cont(*str)) return UTF8PROC_ERROR_INVALIDUTF8;
                *dst = ((uc & 0x1f) << 6) | (*str & 0x3f);
                return 2;
            }
            if (uc < 0xf0)
            {        // 3-byte sequence
                if ((str + 1 >= end) || !utf_cont(*str) || !utf_cont(str[1]))
                    return UTF8PROC_ERROR_INVALIDUTF8;
                // Check for surrogate chars
                if (uc == 0xed && *str > 0x9f)
                    return UTF8PROC_ERROR_INVALIDUTF8;
                uc = ((uc & 0xf) << 12) | ((*str & 0x3f) << 6) | (str[1] & 0x3f);
                if (uc < 0x800)
                    return UTF8PROC_ERROR_INVALIDUTF8;
                *dst = uc;
                return 3;
            }
            // 4-byte sequence
            // Must have 3 valid continuation characters
            if ((str + 2 >= end) || !utf_cont(*str) || !utf_cont(str[1]) || !utf_cont(str[2]))
                return UTF8PROC_ERROR_INVALIDUTF8;
            // Make sure in correct range (0x10000 - 0x10ffff)
            if (uc == 0xf0)
            {
                if (*str < 0x90) return UTF8PROC_ERROR_INVALIDUTF8;
            }
            else if (uc == 0xf4)
            {
                if (*str > 0x8f) return UTF8PROC_ERROR_INVALIDUTF8;
            }
            *dst = ((uc & 7) << 18) | ((*str & 0x3f) << 12) | ((str[1] & 0x3f) << 6) | (str[2] & 0x3f);
            return 4;
        }

        public static bool utf8proc_codepoint_valid(int uc)
        {
            return (((uint)uc) - 0xd800 > 0x07ff) && ((uint)uc < 0x110000);
        }

        public static long utf8proc_encode_char(int uc, byte* dst)
        {
            if (uc < 0x00)
            {
                return 0;
            }
            else if (uc < 0x80)
            {
                dst[0] = (byte)uc;
                return 1;
            }
            else if (uc < 0x800)
            {
                dst[0] = (byte)(0xC0 + (uc >> 6));
                dst[1] = (byte)(0x80 + (uc & 0x3F));
                return 2;
                // Note: we allow encoding 0xd800-0xdfff here, so as not to change
                // the API, however, these are actually invalid in UTF-8
            }
            else if (uc < 0x10000)
            {
                dst[0] = (byte)(0xE0 + (uc >> 12));
                dst[1] = (byte)(0x80 + ((uc >> 6) & 0x3F));
                dst[2] = (byte)(0x80 + (uc & 0x3F));
                return 3;
            }
            else if (uc < 0x110000)
            {
                dst[0] = (byte)(0xF0 + (uc >> 18));
                dst[1] = (byte)(0x80 + ((uc >> 12) & 0x3F));
                dst[2] = (byte)(0x80 + ((uc >> 6) & 0x3F));
                dst[3] = (byte)(0x80 + (uc & 0x3F));
                return 4;
            }
            else return 0;
        }

        /* internal version used for inserting 0xff bytes between graphemes */
        private static long charbound_encode_char(int uc, byte* dst)
        {
            if (uc < 0x00)
            {
                if (uc == -1)
                { /* internal value used for grapheme breaks */
                    dst[0] = (byte)0xFF;
                    return 1;
                }
                return 0;
            }
            else if (uc < 0x80)
            {
                dst[0] = (byte)uc;
                return 1;
            }
            else if (uc < 0x800)
            {
                dst[0] = (byte)(0xC0 + (uc >> 6));
                dst[1] = (byte)(0x80 + (uc & 0x3F));
                return 2;
            }
            else if (uc < 0x10000)
            {
                dst[0] = (byte)(0xE0 + (uc >> 12));
                dst[1] = (byte)(0x80 + ((uc >> 6) & 0x3F));
                dst[2] = (byte)(0x80 + (uc & 0x3F));
                return 3;
            }
            else if (uc < 0x110000)
            {
                dst[0] = (byte)(0xF0 + (uc >> 18));
                dst[1] = (byte)(0x80 + ((uc >> 12) & 0x3F));
                dst[2] = (byte)(0x80 + ((uc >> 6) & 0x3F));
                dst[3] = (byte)(0x80 + (uc & 0x3F));
                return 4;
            }
            else return 0;
        }

        private const int PropFields = 21;

        private static utf8proc_property_t GetPropertyByIndex(int index)
        {
            ReadOnlySpan<ushort> d = utf8proc_properties_data;
            int o = index * PropFields;
            utf8proc_property_t p;
            p.category = (short)d[o + 0];
            p.combining_class = (short)d[o + 1];
            p.bidi_class = (short)d[o + 2];
            p.decomp_type = (short)d[o + 3];
            p.decomp_seqindex = d[o + 4];
            p.casefold_seqindex = d[o + 5];
            p.uppercase_seqindex = d[o + 6];
            p.lowercase_seqindex = d[o + 7];
            p.titlecase_seqindex = d[o + 8];
            p.comb_index = d[o + 9];
            p.comb_length = d[o + 10];
            p.comb_issecond = d[o + 11];
            p.bidi_mirrored = d[o + 12];
            p.comp_exclusion = d[o + 13];
            p.ignorable = d[o + 14];
            p.control_boundary = d[o + 15];
            p.charwidth = d[o + 16];
            p.ambiguous_width = d[o + 17];
            p.pad = d[o + 18];
            p.boundclass = d[o + 19];
            p.indic_conjunct_break = d[o + 20];
            return p;
        }

        /* internal "unsafe" version that does not check whether uc is in range */
        private static utf8proc_property_t unsafe_get_property(int uc)
        {
            /* ASSERT: uc >= 0 && uc < 0x110000 */
            return GetPropertyByIndex(
                utf8proc_stage2table[
                    utf8proc_stage1table[uc >> 8] + (uc & 0xFF)
                ]);
        }

        public static utf8proc_property_t utf8proc_get_property(int uc)
        {
            return uc < 0 || uc >= 0x110000 ? GetPropertyByIndex(0) : unsafe_get_property(uc);
        }

        /* return whether there is a grapheme break between boundclasses lbc and tbc
           (according to the definition of extended grapheme clusters)

          Rule numbering refers to TR29 Version 29 (Unicode 9.0.0):
          http://www.unicode.org/reports/tr29/tr29-29.html

          CAVEATS:
           Please note that evaluation of GB10 (grapheme breaks between emoji zwj sequences)
           and GB 12/13 (regional indicator code points) require knowledge of previous characters
           and are thus not handled by this function. This may result in an incorrect break before
           an E_Modifier class codepoint and an incorrectly missing break between two
           REGIONAL_INDICATOR class code points if such support does not exist in the caller.

           See the special support in grapheme_break_extended, for required bookkeeping by the caller.
        */
        private static bool grapheme_break_simple(int lbc, int tbc)
        {
            return
              (lbc == UTF8PROC_BOUNDCLASS_START) ? true :       // GB1
              (lbc == UTF8PROC_BOUNDCLASS_CR &&                 // GB3
               tbc == UTF8PROC_BOUNDCLASS_LF) ? false :         // ---
              (lbc >= UTF8PROC_BOUNDCLASS_CR && lbc <= UTF8PROC_BOUNDCLASS_CONTROL) ? true :  // GB4
              (tbc >= UTF8PROC_BOUNDCLASS_CR && tbc <= UTF8PROC_BOUNDCLASS_CONTROL) ? true :  // GB5
              (lbc == UTF8PROC_BOUNDCLASS_L &&                  // GB6
               (tbc == UTF8PROC_BOUNDCLASS_L ||                 // ---
                tbc == UTF8PROC_BOUNDCLASS_V ||                 // ---
                tbc == UTF8PROC_BOUNDCLASS_LV ||                // ---
                tbc == UTF8PROC_BOUNDCLASS_LVT)) ? false :      // ---
              ((lbc == UTF8PROC_BOUNDCLASS_LV ||                // GB7
                lbc == UTF8PROC_BOUNDCLASS_V) &&                // ---
               (tbc == UTF8PROC_BOUNDCLASS_V ||                 // ---
                tbc == UTF8PROC_BOUNDCLASS_T)) ? false :        // ---
              ((lbc == UTF8PROC_BOUNDCLASS_LVT ||               // GB8
                lbc == UTF8PROC_BOUNDCLASS_T) &&                // ---
               tbc == UTF8PROC_BOUNDCLASS_T) ? false :          // ---
              (tbc == UTF8PROC_BOUNDCLASS_EXTEND ||             // GB9
               tbc == UTF8PROC_BOUNDCLASS_ZWJ ||                // ---
               tbc == UTF8PROC_BOUNDCLASS_SPACINGMARK ||        // GB9a
               lbc == UTF8PROC_BOUNDCLASS_PREPEND) ? false :    // GB9b
              (lbc == UTF8PROC_BOUNDCLASS_E_ZWG &&              // GB11 (requires additional handling below)
               tbc == UTF8PROC_BOUNDCLASS_EXTENDED_PICTOGRAPHIC) ? false : // ----
              (lbc == UTF8PROC_BOUNDCLASS_REGIONAL_INDICATOR &&          // GB12/13 (requires additional handling below)
               tbc == UTF8PROC_BOUNDCLASS_REGIONAL_INDICATOR) ? false :  // ----
              true; // GB999
        }

        private static bool grapheme_break_extended(int lbc, int tbc, int licb, int ticb, int* state)
        {
            if (state != null)
            {
                int state_bc, state_icb; /* boundclass and indic_conjunct_break state */
                if (*state == 0)
                { /* state initialization */
                    state_bc = lbc;
                    state_icb = licb == UTF8PROC_INDIC_CONJUNCT_BREAK_CONSONANT ? licb : UTF8PROC_INDIC_CONJUNCT_BREAK_NONE;
                }
                else
                { /* lbc and licb are already encoded in *state */
                    state_bc = *state & 0xff;  // 1st byte of state is bound class
                    state_icb = *state >> 8;   // 2nd byte of state is indic conjunct break
                }

                bool break_permitted = grapheme_break_simple(state_bc, tbc) &&
                   !(state_icb == UTF8PROC_INDIC_CONJUNCT_BREAK_LINKER
                    && ticb == UTF8PROC_INDIC_CONJUNCT_BREAK_CONSONANT); // GB9c

                // Special support for GB9c.  Don't break between two consonants
                // separated 1+ linker characters and 0+ extend characters in any order.
                // After a consonant, we enter LINKER state after at least one linker.
                if (ticb == UTF8PROC_INDIC_CONJUNCT_BREAK_CONSONANT
                    || state_icb == UTF8PROC_INDIC_CONJUNCT_BREAK_CONSONANT
                    || state_icb == UTF8PROC_INDIC_CONJUNCT_BREAK_EXTEND)
                    state_icb = ticb;
                else if (state_icb == UTF8PROC_INDIC_CONJUNCT_BREAK_LINKER)
                    state_icb = ticb == UTF8PROC_INDIC_CONJUNCT_BREAK_EXTEND ?
                                UTF8PROC_INDIC_CONJUNCT_BREAK_LINKER : ticb;

                // Special support for GB 12/13 made possible by GB999. After two RI
                // class codepoints we want to force a break. Do this by resetting the
                // second RI's bound class to UTF8PROC_BOUNDCLASS_OTHER, to force a break
                // after that character according to GB999 (unless of course such a break is
                // forbidden by a different rule such as GB9).
                if (state_bc == tbc && tbc == UTF8PROC_BOUNDCLASS_REGIONAL_INDICATOR)
                    state_bc = UTF8PROC_BOUNDCLASS_OTHER;
                // Special support for GB11 (emoji extend* zwj / emoji)
                else if (state_bc == UTF8PROC_BOUNDCLASS_EXTENDED_PICTOGRAPHIC)
                {
                    if (tbc == UTF8PROC_BOUNDCLASS_EXTEND) // fold EXTEND codepoints into emoji
                        state_bc = UTF8PROC_BOUNDCLASS_EXTENDED_PICTOGRAPHIC;
                    else if (tbc == UTF8PROC_BOUNDCLASS_ZWJ)
                        state_bc = UTF8PROC_BOUNDCLASS_E_ZWG; // state to record emoji+zwg combo
                    else
                        state_bc = tbc;
                }
                else
                    state_bc = tbc;

                *state = state_bc + (state_icb << 8);
                return break_permitted;
            }
            else
                return grapheme_break_simple(lbc, tbc);
        }

        public static bool utf8proc_grapheme_break_stateful(int c1, int c2, int* state)
        {
            utf8proc_property_t p1 = utf8proc_get_property(c1);
            utf8proc_property_t p2 = utf8proc_get_property(c2);
            return grapheme_break_extended((int)p1.boundclass,
                                           (int)p2.boundclass,
                                           (int)p1.indic_conjunct_break,
                                           (int)p2.indic_conjunct_break,
                                           state);
        }

        public static bool utf8proc_grapheme_break(int c1, int c2)
        {
            return utf8proc_grapheme_break_stateful(c1, c2, null);
        }

        // (entry is an index into utf8proc_sequences; it is advanced past a surrogate pair)
        private static int seqindex_decode_entry(ref int entry)
        {
            ReadOnlySpan<ushort> seq = utf8proc_sequences;
            int entry_cp = seq[entry];
            if ((entry_cp & 0xF800) == 0xD800)
            {
                entry = entry + 1;
                entry_cp = ((entry_cp & 0x03FF) << 10) | (seq[entry] & 0x03FF);
                entry_cp += 0x10000;
            }
            return entry_cp;
        }

        private static int seqindex_decode_index(uint seqindex)
        {
            int entry = (int)seqindex;
            return seqindex_decode_entry(ref entry);
        }

        private static long seqindex_write_char_decomposed(ushort seqindex, int* dst, long bufsize, int options, int* last_boundclass)
        {
            long written = 0;
            int entry = seqindex & 0x3FFF;
            int len = seqindex >> 14;
            if (len >= 3)
            {
                len = utf8proc_sequences[entry];
                entry++;
            }
            for (; len >= 0; entry++, len--)
            {
                int entry_cp = seqindex_decode_entry(ref entry);

                written += utf8proc_decompose_char(entry_cp, dst != null ? dst + written : dst,
                  (bufsize > written) ? (bufsize - written) : 0, options,
                  last_boundclass);
                if (written < 0) return UTF8PROC_ERROR_OVERFLOW;
            }
            return written;
        }

        public static int utf8proc_tolower(int c)
        {
            int cl = utf8proc_get_property(c).lowercase_seqindex;
            return cl != UINT16_MAX ? seqindex_decode_index((uint)cl) : c;
        }

        public static int utf8proc_toupper(int c)
        {
            int cu = utf8proc_get_property(c).uppercase_seqindex;
            return cu != UINT16_MAX ? seqindex_decode_index((uint)cu) : c;
        }

        public static int utf8proc_totitle(int c)
        {
            int cu = utf8proc_get_property(c).titlecase_seqindex;
            return cu != UINT16_MAX ? seqindex_decode_index((uint)cu) : c;
        }

        public static int utf8proc_islower(int c)
        {
            utf8proc_property_t p = utf8proc_get_property(c);
            return (p.lowercase_seqindex != p.uppercase_seqindex && p.lowercase_seqindex == UINT16_MAX) ? 1 : 0;
        }

        public static int utf8proc_isupper(int c)
        {
            utf8proc_property_t p = utf8proc_get_property(c);
            return (p.lowercase_seqindex != p.uppercase_seqindex && p.uppercase_seqindex == UINT16_MAX && p.category != UTF8PROC_CATEGORY_LT) ? 1 : 0;
        }

        /* return a character width analogous to wcwidth (except portable and
           hopefully less buggy than most system wcwidth functions). */
        public static int utf8proc_charwidth(int c)
        {
            return (int)utf8proc_get_property(c).charwidth;
        }

        public static bool utf8proc_charwidth_ambiguous(int c)
        {
            return utf8proc_get_property(c).ambiguous_width != 0;
        }

        public static int utf8proc_category(int c)
        {
            return utf8proc_get_property(c).category;
        }

        private static readonly string[] category_strings = { "Cn", "Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No", "Pc", "Pd", "Ps", "Pe", "Pi", "Pf", "Po", "Sm", "Sc", "Sk", "So", "Zs", "Zl", "Zp", "Cc", "Cf", "Cs", "Co" };

        public static string utf8proc_category_string(int c)
        {
            return category_strings[utf8proc_category(c)];
        }

        // (the C macro utf8proc_decompose_lump)
        private static long utf8proc_decompose_lump(int replacement_uc, int* dst, long bufsize, int options, int* last_boundclass)
        {
            return utf8proc_decompose_char(replacement_uc, dst, bufsize,
                options & ~(int)UTF8PROC_LUMP, last_boundclass);
        }

        public static long utf8proc_decompose_char(int uc, int* dst, long bufsize, int options, int* last_boundclass)
        {
            utf8proc_property_t property;
            short category;
            int hangul_sindex;
            if (uc < 0 || uc >= 0x110000) return UTF8PROC_ERROR_NOTASSIGNED;
            property = unsafe_get_property(uc);
            category = property.category;
            hangul_sindex = uc - UTF8PROC_HANGUL_SBASE;
            if ((options & (UTF8PROC_COMPOSE | UTF8PROC_DECOMPOSE)) != 0)
            {
                if (hangul_sindex >= 0 && hangul_sindex < UTF8PROC_HANGUL_SCOUNT)
                {
                    int hangul_tindex;
                    if (bufsize >= 1)
                    {
                        dst[0] = UTF8PROC_HANGUL_LBASE +
                          hangul_sindex / UTF8PROC_HANGUL_NCOUNT;
                        if (bufsize >= 2) dst[1] = UTF8PROC_HANGUL_VBASE +
                          (hangul_sindex % UTF8PROC_HANGUL_NCOUNT) / UTF8PROC_HANGUL_TCOUNT;
                    }
                    hangul_tindex = hangul_sindex % UTF8PROC_HANGUL_TCOUNT;
                    if (hangul_tindex == 0) return 2;
                    if (bufsize >= 3) dst[2] = UTF8PROC_HANGUL_TBASE + hangul_tindex;
                    return 3;
                }
            }
            if ((options & UTF8PROC_REJECTNA) != 0)
            {
                if (category == 0) return UTF8PROC_ERROR_NOTASSIGNED;
            }
            if ((options & UTF8PROC_IGNORE) != 0)
            {
                if (property.ignorable != 0) return 0;
            }
            if ((options & UTF8PROC_STRIPNA) != 0)
            {
                if (category == 0) return 0;
            }
            if ((options & UTF8PROC_LUMP) != 0)
            {
                if (category == UTF8PROC_CATEGORY_ZS) return utf8proc_decompose_lump(0x0020, dst, bufsize, options, last_boundclass);
                if (uc == 0x2018 || uc == 0x2019 || uc == 0x02BC || uc == 0x02C8)
                    return utf8proc_decompose_lump(0x0027, dst, bufsize, options, last_boundclass);
                if (category == UTF8PROC_CATEGORY_PD || uc == 0x2212)
                    return utf8proc_decompose_lump(0x002D, dst, bufsize, options, last_boundclass);
                if (uc == 0x2044 || uc == 0x2215) return utf8proc_decompose_lump(0x002F, dst, bufsize, options, last_boundclass);
                if (uc == 0x2236) return utf8proc_decompose_lump(0x003A, dst, bufsize, options, last_boundclass);
                if (uc == 0x2039 || uc == 0x2329 || uc == 0x3008)
                    return utf8proc_decompose_lump(0x003C, dst, bufsize, options, last_boundclass);
                if (uc == 0x203A || uc == 0x232A || uc == 0x3009)
                    return utf8proc_decompose_lump(0x003E, dst, bufsize, options, last_boundclass);
                if (uc == 0x2216) return utf8proc_decompose_lump(0x005C, dst, bufsize, options, last_boundclass);
                if (uc == 0x02C4 || uc == 0x02C6 || uc == 0x2038 || uc == 0x2303)
                    return utf8proc_decompose_lump(0x005E, dst, bufsize, options, last_boundclass);
                if (category == UTF8PROC_CATEGORY_PC || uc == 0x02CD)
                    return utf8proc_decompose_lump(0x005F, dst, bufsize, options, last_boundclass);
                if (uc == 0x02CB) return utf8proc_decompose_lump(0x0060, dst, bufsize, options, last_boundclass);
                if (uc == 0x2223) return utf8proc_decompose_lump(0x007C, dst, bufsize, options, last_boundclass);
                if (uc == 0x223C) return utf8proc_decompose_lump(0x007E, dst, bufsize, options, last_boundclass);
                if ((options & UTF8PROC_NLF2LS) != 0 && (options & UTF8PROC_NLF2PS) != 0)
                {
                    if (category == UTF8PROC_CATEGORY_ZL ||
                        category == UTF8PROC_CATEGORY_ZP)
                        return utf8proc_decompose_lump(0x000A, dst, bufsize, options, last_boundclass);
                }
            }
            if ((options & UTF8PROC_STRIPMARK) != 0)
            {
                if (category == UTF8PROC_CATEGORY_MN ||
                  category == UTF8PROC_CATEGORY_MC ||
                  category == UTF8PROC_CATEGORY_ME) return 0;
            }
            if ((options & UTF8PROC_CASEFOLD) != 0)
            {
                if (property.casefold_seqindex != UINT16_MAX)
                {
                    return seqindex_write_char_decomposed(property.casefold_seqindex, dst, bufsize, options, last_boundclass);
                }
            }
            if ((options & (UTF8PROC_COMPOSE | UTF8PROC_DECOMPOSE)) != 0)
            {
                if (property.decomp_seqindex != UINT16_MAX &&
                    (property.decomp_type == 0 || (options & UTF8PROC_COMPAT) != 0))
                {
                    return seqindex_write_char_decomposed(property.decomp_seqindex, dst, bufsize, options, last_boundclass);
                }
            }
            if ((options & UTF8PROC_CHARBOUND) != 0)
            {
                bool boundary;
                boundary = grapheme_break_extended(0, (int)property.boundclass, 0, (int)property.indic_conjunct_break,
                                                   last_boundclass);
                if (boundary)
                {
                    if (bufsize >= 1) dst[0] = -1; /* sentinel value for grapheme break */
                    if (bufsize >= 2) dst[1] = uc;
                    return 2;
                }
            }
            if (bufsize >= 1) *dst = uc;
            return 1;
        }

        public static long utf8proc_decompose(byte* str, long strlen, int* buffer, long bufsize, int options)
        {
            return utf8proc_decompose_custom(str, strlen, buffer, bufsize, options, null, null);
        }

        public static long utf8proc_decompose_custom(byte* str, long strlen, int* buffer, long bufsize, int options,
                                                     utf8proc_custom_func custom_func, void* custom_data)
        {
            /* strlen will be ignored, if UTF8PROC_NULLTERM is set in options */
            long wpos = 0;
            if ((options & UTF8PROC_COMPOSE) != 0 && (options & UTF8PROC_DECOMPOSE) != 0)
                return UTF8PROC_ERROR_INVALIDOPTS;
            if ((options & UTF8PROC_STRIPMARK) != 0 &&
                (options & UTF8PROC_COMPOSE) == 0 && (options & UTF8PROC_DECOMPOSE) == 0)
                return UTF8PROC_ERROR_INVALIDOPTS;
            {
                int uc;
                long rpos = 0;
                long decomp_result;
                int boundclass = UTF8PROC_BOUNDCLASS_START;
                while (true)
                {
                    if ((options & UTF8PROC_NULLTERM) != 0)
                    {
                        rpos += utf8proc_iterate(str + rpos, -1, &uc);
                        /* checking of return value is not necessary,
                           as 'uc' is < 0 in case of error */
                        if (uc < 0) return UTF8PROC_ERROR_INVALIDUTF8;
                        if (rpos < 0) return UTF8PROC_ERROR_OVERFLOW;
                        if (uc == 0) break;
                    }
                    else
                    {
                        if (rpos >= strlen) break;
                        rpos += utf8proc_iterate(str + rpos, strlen - rpos, &uc);
                        if (uc < 0) return UTF8PROC_ERROR_INVALIDUTF8;
                    }
                    if (custom_func != null)
                    {
                        uc = custom_func(uc, custom_data);   /* user-specified custom mapping */
                    }
                    decomp_result = utf8proc_decompose_char(
                      uc, buffer != null ? buffer + wpos : buffer, (bufsize > wpos) ? (bufsize - wpos) : 0, options,
                      &boundclass
                    );
                    if (decomp_result < 0) return decomp_result;
                    wpos += decomp_result;
                    /* prohibiting integer overflows due to too long strings: */
                    if (wpos < 0 ||
                        wpos > (long)(long.MaxValue / 2 / sizeof(int) / 2))
                        return UTF8PROC_ERROR_OVERFLOW;
                }
            }
            if ((options & (UTF8PROC_COMPOSE | UTF8PROC_DECOMPOSE)) != 0 && bufsize >= wpos)
            {
                long pos = 0;
                while (pos < wpos - 1)
                {
                    int uc1, uc2;
                    utf8proc_property_t property1, property2;
                    uc1 = buffer[pos];
                    uc2 = buffer[pos + 1];
                    property1 = unsafe_get_property(uc1);
                    property2 = unsafe_get_property(uc2);
                    if (property1.combining_class > property2.combining_class &&
                        property2.combining_class > 0)
                    {
                        buffer[pos] = uc2;
                        buffer[pos + 1] = uc1;
                        if (pos > 0) pos--; else pos++;
                    }
                    else
                    {
                        pos++;
                    }
                }
            }
            return wpos;
        }

        public static long utf8proc_normalize_utf32(int* buffer, long length, int options)
        {
            /* UTF8PROC_NULLTERM option will be ignored, 'length' is never ignored */
            if ((options & (UTF8PROC_NLF2LS | UTF8PROC_NLF2PS | UTF8PROC_STRIPCC)) != 0)
            {
                long rpos;
                long wpos = 0;
                int uc;
                for (rpos = 0; rpos < length; rpos++)
                {
                    uc = buffer[rpos];
                    if (uc == 0x000D && rpos < length - 1 && buffer[rpos + 1] == 0x000A) rpos++;
                    if (uc == 0x000A || uc == 0x000D || uc == 0x0085 ||
                        ((options & UTF8PROC_STRIPCC) != 0 && (uc == 0x000B || uc == 0x000C)))
                    {
                        if ((options & UTF8PROC_NLF2LS) != 0)
                        {
                            if ((options & UTF8PROC_NLF2PS) != 0)
                            {
                                buffer[wpos++] = 0x000A;
                            }
                            else
                            {
                                buffer[wpos++] = 0x2028;
                            }
                        }
                        else
                        {
                            if ((options & UTF8PROC_NLF2PS) != 0)
                            {
                                buffer[wpos++] = 0x2029;
                            }
                            else
                            {
                                buffer[wpos++] = 0x0020;
                            }
                        }
                    }
                    else if ((options & UTF8PROC_STRIPCC) != 0 &&
                      (uc < 0x0020 || (uc >= 0x007F && uc < 0x00A0)))
                    {
                        if (uc == 0x0009) buffer[wpos++] = 0x0020;
                    }
                    else
                    {
                        buffer[wpos++] = uc;
                    }
                }
                length = wpos;
            }
            if ((options & UTF8PROC_COMPOSE) != 0)
            {
                int* starter = null;
                bool have_starter_property = false;
                utf8proc_property_t starter_property = default;
                short max_combining_class = -1;
                long rpos;
                long wpos = 0;
                for (rpos = 0; rpos < length; rpos++)
                {
                    int current_char = buffer[rpos];
                    utf8proc_property_t current_property = unsafe_get_property(current_char);
                    if (starter != null && current_property.combining_class > max_combining_class)
                    {
                        /* combination perhaps possible */
                        int hangul_lindex;
                        int hangul_sindex;
                        hangul_lindex = *starter - UTF8PROC_HANGUL_LBASE;
                        if (hangul_lindex >= 0 && hangul_lindex < UTF8PROC_HANGUL_LCOUNT)
                        {
                            int hangul_vindex;
                            hangul_vindex = current_char - UTF8PROC_HANGUL_VBASE;
                            if (hangul_vindex >= 0 && hangul_vindex < UTF8PROC_HANGUL_VCOUNT)
                            {
                                *starter = UTF8PROC_HANGUL_SBASE +
                                  (hangul_lindex * UTF8PROC_HANGUL_VCOUNT + hangul_vindex) *
                                  UTF8PROC_HANGUL_TCOUNT;
                                have_starter_property = false;
                                continue;
                            }
                        }
                        hangul_sindex = *starter - UTF8PROC_HANGUL_SBASE;
                        if (hangul_sindex >= 0 && hangul_sindex < UTF8PROC_HANGUL_SCOUNT &&
                            (hangul_sindex % UTF8PROC_HANGUL_TCOUNT) == 0)
                        {
                            int hangul_tindex;
                            hangul_tindex = current_char - UTF8PROC_HANGUL_TBASE;
                            if (hangul_tindex >= 0 && hangul_tindex < UTF8PROC_HANGUL_TCOUNT)
                            {
                                *starter += hangul_tindex;
                                have_starter_property = false;
                                continue;
                            }
                        }
                        if (!have_starter_property)
                        {
                            starter_property = unsafe_get_property(*starter);
                            have_starter_property = true;
                        }
                        int idx = starter_property.comb_index;
                        if (idx < 0x3FF && current_property.comb_issecond != 0)
                        {
                            int len = starter_property.comb_length;
                            int max_second = utf8proc_combinations_second[idx + len - 1];
                            if (current_char <= max_second)
                            {
                                int off;
                                bool found = false;
                                // TODO: binary search? arithmetic search?
                                for (off = 0; off < len; ++off)
                                {
                                    int second = utf8proc_combinations_second[idx + off];
                                    if (current_char < second)
                                    {
                                        /* not found */
                                        break;
                                    }
                                    if (current_char == second)
                                    {
                                        /* found */
                                        int composition = utf8proc_combinations_combined[idx + off];
                                        *starter = composition;
                                        have_starter_property = false;
                                        found = true;
                                        break;
                                    }
                                }
                                if (found)
                                {
                                    /* found */
                                    continue;
                                }
                            }
                        }
                    }
                    buffer[wpos] = current_char;
                    if (current_property.combining_class != 0)
                    {
                        if (current_property.combining_class > max_combining_class)
                        {
                            max_combining_class = current_property.combining_class;
                        }
                    }
                    else
                    {
                        starter = buffer + wpos;
                        have_starter_property = false;
                        max_combining_class = -1;
                    }
                    wpos++;
                }
                length = wpos;
            }
            return length;
        }

        public static long utf8proc_reencode(int* buffer, long length, int options)
        {
            /* UTF8PROC_NULLTERM option will be ignored, 'length' is never ignored
               ASSERT: 'buffer' has one spare byte of free space at the end! */
            length = utf8proc_normalize_utf32(buffer, length, options);
            if (length < 0) return length;
            {
                long rpos, wpos = 0;
                int uc;
                if ((options & UTF8PROC_CHARBOUND) != 0)
                {
                    for (rpos = 0; rpos < length; rpos++)
                    {
                        uc = buffer[rpos];
                        wpos += charbound_encode_char(uc, ((byte*)buffer) + wpos);
                    }
                }
                else
                {
                    for (rpos = 0; rpos < length; rpos++)
                    {
                        uc = buffer[rpos];
                        wpos += utf8proc_encode_char(uc, ((byte*)buffer) + wpos);
                    }
                }
                ((byte*)buffer)[wpos] = 0;
                return wpos;
            }
        }

        // The result buffer is allocated with NativeMemory (the caller releases it with utf8proc_free).
        public static long utf8proc_map(byte* str, long strlen, byte** dstptr, int options)
        {
            return utf8proc_map_custom(str, strlen, dstptr, options, null, null);
        }

        public static void utf8proc_free(void* ptr) { NativeMemory.Free(ptr); }

        public static long utf8proc_map_custom(byte* str, long strlen, byte** dstptr, int options,
                                               utf8proc_custom_func custom_func, void* custom_data)
        {
            int* buffer;
            long result;
            *dstptr = null;
            result = utf8proc_decompose_custom(str, strlen, null, 0, options, custom_func, custom_data);
            if (result < 0) return result;
            buffer = (int*)NativeMemory.Alloc((nuint)((ulong)result * sizeof(int) + 1));
            if (buffer == null) return UTF8PROC_ERROR_NOMEM;
            result = utf8proc_decompose_custom(str, strlen, buffer, result, options, custom_func, custom_data);
            if (result < 0)
            {
                NativeMemory.Free(buffer);
                return result;
            }
            result = utf8proc_reencode(buffer, result, options);
            if (result < 0)
            {
                NativeMemory.Free(buffer);
                return result;
            }
            {
                int* newptr;
                newptr = (int*)NativeMemory.Realloc(buffer, (nuint)(result + 1));
                if (newptr != null) buffer = newptr;
            }
            *dstptr = (byte*)buffer;
            return result;
        }

        public static byte* utf8proc_NFD(byte* str)
        {
            byte* retval;
            utf8proc_map(str, 0, &retval, UTF8PROC_NULLTERM | UTF8PROC_STABLE |
              UTF8PROC_DECOMPOSE);
            return retval;
        }

        public static byte* utf8proc_NFC(byte* str)
        {
            byte* retval;
            utf8proc_map(str, 0, &retval, UTF8PROC_NULLTERM | UTF8PROC_STABLE |
              UTF8PROC_COMPOSE);
            return retval;
        }

        public static byte* utf8proc_NFKD(byte* str)
        {
            byte* retval;
            utf8proc_map(str, 0, &retval, UTF8PROC_NULLTERM | UTF8PROC_STABLE |
              UTF8PROC_DECOMPOSE | UTF8PROC_COMPAT);
            return retval;
        }

        public static byte* utf8proc_NFKC(byte* str)
        {
            byte* retval;
            utf8proc_map(str, 0, &retval, UTF8PROC_NULLTERM | UTF8PROC_STABLE |
              UTF8PROC_COMPOSE | UTF8PROC_COMPAT);
            return retval;
        }

        public static byte* utf8proc_NFKC_Casefold(byte* str)
        {
            byte* retval;
            utf8proc_map(str, 0, &retval, UTF8PROC_NULLTERM | UTF8PROC_STABLE |
              UTF8PROC_COMPOSE | UTF8PROC_COMPAT | UTF8PROC_CASEFOLD | UTF8PROC_IGNORE);
            return retval;
        }
    }
}
