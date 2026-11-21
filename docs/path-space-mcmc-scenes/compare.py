import sys, numpy as np
sys.path.insert(0, '/home/claude')
from exrstat import load
ref = np.load(sys.argv[1]) if sys.argv[1].endswith('.npy') else load(sys.argv[1])
runs = [load(p) for p in sys.argv[2:]]
means = np.array([r.mean() for r in runs])
print('reference mean %.5f; runs mean %.5f +- %.5f (n=%d); ratio %.4f +- %.4f' % (ref.mean(), means.mean(), means.std(ddof=1) / np.sqrt(len(runs)) if len(runs) > 1 else 0, len(runs), means.mean() / ref.mean(), (means.std(ddof=1) / np.sqrt(len(runs)) / ref.mean()) if len(runs) > 1 else 0))
avg = np.mean(runs, axis=0)
# blocks of the image: the mean of each against the reference, in units of its standard error over the runs
h, w, _ = ref.shape
chi2 = 0
rows = []
for by in range(3):
    row = []
    for bx in range(4):
        sl = (slice(by * h // 3, (by + 1) * h // 3), slice(bx * w // 4, (bx + 1) * w // 4))
        m = np.array([r[sl].mean() for r in runs])
        r0 = ref[sl].mean()
        se = m.std(ddof=1) / np.sqrt(len(runs)) if len(runs) > 1 else float('nan')
        row.append('%.3f(%+.1f)' % (m.mean() / r0, (m.mean() - r0) / se if se > 0 else 0))
        chi2 += ((m.mean() - r0) / se) ** 2 if se > 0 else 0
    rows.append(' '.join(row))
print('blocks: ratio to the reference (deviation in standard errors)')
print('\n'.join(rows))
print('sum of squared deviations %.1f over 12 blocks' % chi2)
