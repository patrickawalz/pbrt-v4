# envmap.py <out.pfm>: the test image of the infinite light (kinds `map`, `mapglass` of scene.py): 64 by 64, a dim ground
# of 0.25 with three bright patches
import sys, numpy as np
n = 64
y, x = np.mgrid[0:n, 0:n]
img = np.full((n, n, 3), 0.25, dtype=np.float32)
for (cx, cy, amp, col) in ((20, 22, 25.0, (1, 0.9, 0.7)), (44, 40, 10.0, (0.6, 0.8, 1.0)), (32, 10, 6.0, (1, 1, 1))):
    g = amp * np.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (2 * 2.5 ** 2))
    for c in range(3):
        img[..., c] += g * col[c]
with open(sys.argv[1], 'wb') as f:
    f.write(b'PF\n%d %d\n-1.0\n' % (n, n))
    f.write(img[::-1].astype('<f4').tobytes())
