#!/usr/bin/env python3
# (not in OpenShadingLanguage) Generates oiio/fmath_jit.cs from oiio/fmath.cs.
#
# The OSL shading ops run as LLVM bitcode that clang compiled from OpenImageIO's fmath.h: madd() carries "#pragma clang fp contract(fast)", and the JIT
# (which targets the host CPU) turns the contracted multiply and add into an FMA. The host side C++ (texture system, constant folding, testrender),
# compiled for a baseline x86-64, does not. fmath.cs is the host variant (separate multiply and add), jitmath is the same code with madd fused.
import re, sys, os
here = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(here, '..', 'fmath.cs')).read()
src = src.replace('public static class fmath', 'public static class jitmath')
src = re.sub(r'(\[MethodImpl\(MethodImplOptions\.AggressiveInlining\)\]\s*public static float madd\(float a, float b, float c\)\s*\{).*?(\n        \})',
             r'\1\n            return MathF.FusedMultiplyAdd(a, b, c);\2', src, count=1, flags=re.S)
header = '''// OSL port: OpenImageIO/fmath.h as the shading ops see it (GENERATED from fmath.cs by tools/gen_fmath_jit.py, do not edit)
//
// The ops of the OSL runtime library are LLVM bitcode that clang compiled from fmath.h; madd() has "#pragma clang fp contract(fast)", and the JIT
// targets the host CPU, so on a machine with FMA every madd is a fused multiply add. This is the same code as fmath.cs with madd fused (the host side
// code, such as the texture system, stays with fmath, which is compiled for a baseline x86-64 and keeps the multiply and the add apart).

'''
# drop the original header comment block (leading // lines)
lines = src.split('\n')
i = 0
while lines[i].startswith('//'):
    i += 1
open(os.path.join(here, '..', 'fmath_jit.cs'), 'w').write(header + '\n'.join(lines[i:]))
