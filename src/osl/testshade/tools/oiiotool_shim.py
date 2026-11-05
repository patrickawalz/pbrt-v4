#!/usr/bin/env python3
# (not in OpenShadingLanguage) Stand-in for the part of oiiotool the testsuite's runtest.py uses to compare images: "oiiotool -a -fail F -failpercent P
# -hardfail H -warn W -warnpercent P A B --diff". Every other oiiotool command line is accepted and ignored (texture creation is not supported).
import sys
import numpy as np


def load(path):
    if path.endswith(".tif") or path.endswith(".tiff"):
        import tifffile
        a = tifffile.imread(path)
        if a.dtype == np.uint8:
            a = a.astype(np.float32) / 255.0
        elif a.dtype == np.uint16:
            a = a.astype(np.float32) / 65535.0
        else:
            a = a.astype(np.float32)
    elif path.endswith(".exr"):
        import OpenEXR
        f = OpenEXR.File(path)
        chans = f.channels()
        planes = []
        for name in sorted(chans.keys()):
            px = np.asarray(chans[name].pixels, dtype=np.float32)
            if px.ndim == 2:
                px = px[:, :, None]
            planes.append((name, px))
        # order: RGBA convention when present
        order = {"R": 0, "G": 1, "B": 2, "A": 3}
        if len(planes) == 1 and planes[0][0] in ("RGB", "RGBA"):
            a = planes[0][1]
        else:
            planes.sort(key=lambda p: order.get(p[0], 10))
            a = np.concatenate([p[1] for p in planes], axis=2)
    else:
        raise ValueError("unsupported image " + path)
    if a.ndim == 2:
        a = a[:, :, None]
    return a


def main(argv):
    if "--diff" not in argv:
        return 0
    opts = {"-fail": 1e-6, "-failpercent": 0.0, "-hardfail": 1e30, "-warn": 1e-6, "-warnpercent": 0.0}
    files = []
    i = 1
    while i < len(argv):
        a = argv[i]
        if a in opts:
            opts[a] = float(argv[i + 1])
            i += 2
        elif a in ("-a", "--diff"):
            i += 1
        elif a.startswith("-"):
            i += 1
        else:
            files.append(a)
            i += 1
    if len(files) != 2:
        return 0
    try:
        a = load(files[0])
        b = load(files[1])
    except Exception as e:
        print("oiiotool shim: cannot read images:", e)
        return 1
    if a.shape != b.shape:
        print("Images do not match in size/channels:", a.shape, b.shape)
        return 1
    d = np.abs(a - b)
    d = np.where(np.isnan(a) & np.isnan(b), 0.0, d)
    pix = d.max(axis=2)
    nfail = int((pix > opts["-fail"]).sum())
    total = pix.size
    maxd = float(pix.max()) if total else 0.0
    pct = 100.0 * nfail / total if total else 0.0
    print("Comparing %s vs %s: max diff %g, %d/%d pixels over %g (%.3f%%)" % (files[0], files[1], maxd, nfail, total, opts["-fail"], pct))
    if maxd > opts["-hardfail"]:
        print("HARD FAILURE")
        return 1
    if pct > opts["-failpercent"] * 100.0 if opts["-failpercent"] < 1.0 else pct > opts["-failpercent"]:
        print("FAILURE")
        return 1
    return 0


sys.exit(main(sys.argv))
