#!/bin/bash
# (not in OpenShadingLanguage) Runs OSL testsuite directories against the C# testshade with the stock runtest.py.
# usage: runsuite.sh [-j N] [name ...]    (no names: every directory with a run.py)  Results: /tmp/tsuite/results.txt
export LD_LIBRARY_PATH=/tmp/osl-ref/lib:/tmp/osl-ref/oiio-install/lib:/tmp/osl-ref/ocio-install/lib
SUITE=/home/claude/OpenShadingLanguage/testsuite
TOOLS=$(cd "$(dirname "$0")" && pwd)
TS=${TS_DLL:-$TOOLS/../bin/Release/net10.0/testshade.dll}
export OSL_TESTSUITE_ROOT=$SUITE
export OSL_SOURCE_DIR=/home/claude/OpenShadingLanguage
mkdir -p /tmp/tsuite/bin
ln -sfn $SUITE/common /tmp/tsuite/common
cat > /tmp/tsuite/bin/testshade <<EOS
#!/bin/bash
exec dotnet $TS $TS_ARGS "\$@"
EOS
# Texture creation (oiiotool patterns, -otex, -oenv, maketx) uses a small program on top of libOpenImageIO, so the textures are what the real tools write
if [ ! -x /tmp/tsuite/bin/oiiotool_min ] || [ $TOOLS/oiiotool_min.cpp -nt /tmp/tsuite/bin/oiiotool_min ]; then
  g++ -std=c++17 -O1 -I/tmp/osl-ref/oiio-install/include $TOOLS/oiiotool_min.cpp -o /tmp/tsuite/bin/oiiotool_min -L/tmp/osl-ref/oiio-install/lib \
      -lOpenImageIO -lOpenImageIO_Util -Wl,-rpath,/tmp/osl-ref/oiio-install/lib || exit 1
fi
ln -sf oiiotool_min /tmp/tsuite/bin/maketx
# The render-* directories compare images with the real idiff (built from the OIIO source next to the reference build)
if [ ! -x /tmp/tsuite/bin/idiff ] && [ -f /tmp/osl-ref/oiio-src/src/idiff/idiff.cpp ]; then
  g++ -std=c++17 -O1 -w -I/tmp/osl-ref/oiio-install/include -I/tmp/osl-ref/oiio-src/src/include /tmp/osl-ref/oiio-src/src/idiff/idiff.cpp \
      -o /tmp/tsuite/bin/idiff -L/tmp/osl-ref/oiio-install/lib -lOpenImageIO -lOpenImageIO_Util -Wl,-rpath,/tmp/osl-ref/oiio-install/lib || exit 1
fi
cat > /tmp/tsuite/bin/oiiotool <<EOS
#!/bin/bash
case " \$* " in
  *" --diff "*) exec python3 $TOOLS/oiiotool_shim.py "\$@" ;;
  *) exec /tmp/tsuite/bin/oiiotool_min "\$@" ;;
esac
EOS
chmod +x /tmp/tsuite/bin/oiiotool
# The stock harness finds oslc, oslinfo, testrender ... as $BUILD/bin/<app>; oslc is the reference compiler, the others are the C# ports
BUILD=/tmp/tsuite/osl-build
mkdir -p $BUILD/bin
OSLINFO_DLL=${OSLINFO_DLL:-$TOOLS/../../oslinfo/bin/Release/net10.0/oslinfo.dll}
TESTRENDER_DLL=${TESTRENDER_DLL:-$TOOLS/../../testrender/bin/Release/net10.0/testrender.dll}
printf '#!/bin/bash\nexec /tmp/osl-ref/bin/oslc "$@"\n' > $BUILD/bin/oslc
printf '#!/bin/bash\nexec dotnet %s "$@"\n' "$OSLINFO_DLL" > $BUILD/bin/oslinfo
printf '#!/bin/bash\nexec dotnet %s "$@"\n' "$TESTRENDER_DLL" > $BUILD/bin/testrender
chmod +x $BUILD/bin/*
export OSL_TESTSHADE_NAME=/tmp/tsuite/bin/testshade
export PATH=/tmp/tsuite/bin:$PATH
J=8
if [ "$1" = "-j" ]; then J=$2; shift 2; fi
if [ $# -gt 0 ]; then NAMES="$@"; else NAMES=$(cd $SUITE && for d in */; do d=${d%/}; [ -f $d/run.py ] && echo $d; done); fi
run_one() {
  n=$1
  w=/tmp/tsuite/$n
  rm -rf $w; mkdir -p $w
  (cd $w && timeout ${TS_TIMEOUT:-300} python3 $SUITE/runtest.py $w $BUILD > $w/runtest.log 2>&1)
  rc=$?
  if [ $rc -eq 0 ]; then echo "PASS $n"; else echo "FAIL $n ($rc)"; fi
}
export -f run_one
export SUITE BUILD
echo $NAMES | tr ' ' '\n' | xargs -P $J -I{} bash -c 'run_one {}' | sort -k2 > /tmp/tsuite/results.txt
grep -c PASS /tmp/tsuite/results.txt | sed 's/^/pass: /'; grep -c FAIL /tmp/tsuite/results.txt | sed 's/^/fail: /'
