# ratio.py <reference exr> <out png> <label> <run exr>...: the mean of the runs over the reference, in blocks of 8 pixels,
# as colours from blue (3% under) through white (equal) to red (3% over)
import sys, numpy as np
sys.path.insert(0, '/home/claude'); sys.path.insert(0, '/home/claude/renders/pbr/glow/ring')
from exrstat import load
import importlib.util
spec = importlib.util.spec_from_file_location('tone', '/home/claude/renders/pbr/glow/ring/tone.py')
src = open('/home/claude/renders/pbr/glow/ring/tone.py').read()
ns = {}
exec(src[:src.index("if __name__")] if "if __name__" in src else src[:src.index("    exr, out, label, exposure")].rsplit('\n', 2)[0], ns)
ref = load(sys.argv[1]).mean(axis=2)
runs = np.mean([load(p).mean(axis=2) for p in sys.argv[4:]], axis=0)
h, w = ref.shape
b = 8
R = ref[:h // b * b, :w // b * b].reshape(h // b, b, w // b, b).mean(axis=(1, 3))
M = runs[:h // b * b, :w // b * b].reshape(h // b, b, w // b, b).mean(axis=(1, 3))
ratio = M / R - 1
t = np.clip(ratio / 0.03, -1, 1)
rgb = np.ones(t.shape + (3,))
rgb[..., 0] = np.where(t < 0, 1 + t, 1)
rgb[..., 1] = 1 - np.abs(t)
rgb[..., 2] = np.where(t > 0, 1 - t, 1)
big = np.repeat(np.repeat(rgb, b, axis=0), b, axis=1)
label = sys.argv[3] + ' | whole image: %+.2f%% | blue = 3%% under, white = equal, red = 3%% over' % (100 * (runs.mean() / ref.mean() - 1))
ns['labelled'](big, label, sys.argv[2])
