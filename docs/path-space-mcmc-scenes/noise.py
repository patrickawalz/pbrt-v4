import sys, numpy as np
sys.path.insert(0, '/home/claude')
from exrstat import load
ref = load(sys.argv[1])
runs = [load(p) for p in sys.argv[2:]]
lum = lambda a: a.mean(axis=2)
r = lum(ref)
e = [np.sqrt(np.mean(((lum(x) - r) / (r + 0.01 * r.mean())) ** 2)) for x in runs]
m = np.array([x.mean() for x in runs])
print('n=%d  mean ratio %.4f +- %.4f   relative rmse %.4f +- %.4f' % (len(runs), m.mean() / ref.mean(), m.std(ddof=1) / np.sqrt(len(runs)) / ref.mean(), np.mean(e), np.std(e, ddof=1) / np.sqrt(len(e))))
