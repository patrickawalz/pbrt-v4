# Generates src/pbrt/atmosphere_data.cs from the libRadtran 2.0.6 data files (read as data only):
#   ozone cross sections (Serdyuchenko-Gorshelev, IUP Bremen, file given as the second argument) at 223 K, the U.S. standard atmosphere ozone profile (afglus.dat),
#   the OPAC aerosol mass profiles (standard_aerosol_files), and Mie optical properties of the OPAC species inso, waso (50 % RH)
#   and soot computed here from the OPAC refractive indices and log-normal size distributions (size_distr.cfg).
import numpy as np, miepython, sys, re
D = '/tmp/libradtran_x/libRadtran-2.0.6/data/'
OUT = sys.argv[1]

# --- ozone cross section at 223 K, averaged over 2 nm bins centred on even nm, 360 to 830 nm (cm^2 -> m^2)
# Serdyuchenko-Gorshelev 2014 (IUP Bremen, 213-1100 nm on a 0.01 nm grid, column 9 is 223 K) as used by the sky model of Wilkie et al.
# Usage: gen_tables.py OUT.cs SerdyuchenkoGorshelev5digits_latest.dat
def read_columns(path, ncol):
    rows = []
    for line in open(path):
        p = line.split()
        if len(p) != ncol:
            continue
        try:
            rows.append([float(x) for x in p])
        except ValueError:
            pass
    return np.array(rows)
sg = read_columns(sys.argv[2], 12)
w = sg[:, 0]
sig = np.maximum(sg[:, 8], 0.0) * 1e-4                                # m^2 (the file is in cm^2 per molecule)
oz_lambda = np.arange(360, 832, 2.0)
oz_sigma = np.array([sig[(w >= l - 1) & (w < l + 1)].mean() for l in oz_lambda])

# --- ozone and air profile of the U.S. standard atmosphere
rows = np.loadtxt(D + 'atmmod/afglus.dat', comments='#')[::-1]       # ascending altitude
prof_z = rows[:, 0] * 1000.0                                          # m
prof_o3 = rows[:, 4] * 1e6                                            # m^-3

# --- aerosol mass profiles (g/m^3) of the libRadtran standard files
def read_profile(name):
    z, c = [], []
    for line in open(D + 'aerosol/OPAC/standard_aerosol_files/%s.dat' % name):
        if line.startswith('#') or not line.strip():
            continue
        v = [float(x) for x in line.split()]
        z.append(v[0]); c.append(v[1:4])           # inso, waso, soot (suso is left out)
    z = np.array(z); c = np.array(c)
    o = np.argsort(z)
    return z[o] * 1000.0, c[o]
profiles = {n: read_profile(n) for n in ['continental_clean', 'continental_average', 'continental_polluted']}

# --- Mie optical properties
def read_refr(path):
    a = np.loadtxt(path)
    return a[:, 0], a[:, 1], a[:, 2]
def cfg(no, rh):
    for line in open(D + 'aerosol/OPAC/size_distr.cfg'):
        if line.startswith('#') or not line.strip():
            continue
        v = line.split()
        if int(v[0]) == no and float(v[1]) == rh:
            return dict(rmin=float(v[2]), rmax=float(v[3]), rmod=float(v[4]), rho=float(v[5]), sigma=float(v[6]))
species = [('inso', 1, 0, 'inso00_refr.dat'), ('waso', 2, 50, 'waso50_refr.dat'), ('soot', 3, 0, 'soot00_refr.dat')]
lam_grid = np.arange(360.0, 831.0, 10.0)
result = {}
for name, no, rh, fn in species:
    c = cfg(no, rh)
    wl, nr, ni = read_refr(D + 'aerosol/OPAC/refractive_indices/' + fn)
    # log-normal number size distribution in ln r, radii in micrometres
    lnr = np.linspace(np.log(c['rmin']), np.log(c['rmax']), 600)
    r = np.exp(lnr)
    weight = np.exp(-0.5 * ((lnr - np.log(c['rmod'])) / np.log(c['sigma'])) ** 2)   # n(r) dr = weight dlnr
    vol = 4.0 / 3.0 * np.pi * r ** 3
    k, ssa, g = [], [], []
    for lam in lam_grid:
        mre = np.interp(lam * 1e-3, wl, nr); mim = np.interp(lam * 1e-3, wl, ni)
        m = complex(mre, mim)                    # the files list the imaginary part negative (miepython's convention)
        x = 2 * np.pi * r / (lam * 1e-3)
        qext, qsca, qback, gg = miepython.efficiencies_mx(m, x)
        area = np.pi * r ** 2
        cext = np.sum(qext * area * weight); csca = np.sum(qsca * area * weight)
        gsum = np.sum(qsca * gg * area * weight)
        mass = c['rho'] * np.sum(vol * weight)               # g per (um^3 g/cm^3 units): k = C / (rho V) in m^2/g
        k.append(cext / mass); ssa.append(csca / cext); g.append(gsum / csca)
    result[name] = (np.array(k), np.array(ssa), np.array(g))
    print(name, 'k_ext(550) = %.3f m2/g, ssa %.3f, g %.3f' % (np.interp(550, lam_grid, result[name][0]), np.interp(550, lam_grid, result[name][1]),
                                                                np.interp(550, lam_grid, result[name][2])), file=sys.stderr)

# --- check: aerosol optical depth at 550 nm of the three standard profiles
for n, (z, c) in profiles.items():
    tau = 0
    for i in range(len(z) - 1):
        for s, (nm, *_ ) in enumerate(species):
            kk = np.interp(550, lam_grid, result[nm][0])
            tau += 0.5 * (c[i, s] + c[i + 1, s]) * kk * (z[i + 1] - z[i])
    print(n, 'AOD(550) = %.3f' % tau, file=sys.stderr)


# --- ozone cross sections at the 11 temperatures of the Serdyuchenko-Gorshelev data (193 K to 293 K), 2 nm bins, m^2
oz_temperatures = np.arange(193.0, 294.0, 10.0)
oz_by_temperature = []
for T in oz_temperatures:
    column = 1 + int(round((293.0 - T) / 10.0))                       # columns: wavelength, 293 K, 283 K, ... 193 K
    sigT = np.maximum(sg[:, column], 0.0) * 1e-4
    oz_by_temperature.append(np.array([sigT[(w >= l - 1) & (w < l + 1)].mean() for l in oz_lambda]))

# --- the air profile of the U.S. standard atmosphere: temperature, O2 and NO2 number densities (afglus: cm^-3 -> m^-3)
prof_T = rows[:, 2]
prof_o2 = rows[:, 5] * 1e6
prof_no2 = rows[:, 8] * 1e6

# --- NO2 cross section (Bogumil et al., IUP Bremen): (C0 + C1 (T - 273.15) + C2 (T - 273.15)^2) * 1e-20 cm^2, 2 nm bins, m^2
no2 = np.loadtxt(D + 'crs/crs_NO2_UBremen_cf.dat', comments='#')
no2_c = []
for col in (1, 2, 3):
    no2_c.append(np.array([no2[(no2[:, 0] >= l - 1) & (no2[:, 0] < l + 1), col].mean() if ((no2[:, 0] >= l - 1) & (no2[:, 0] < l + 1)).any() else 0.0
                           for l in oz_lambda]) * 1e-20 * 1e-4)

# --- O2-O2 collision pair cross section (Greenblatt et al. 1990, libRadtran file in 1e-46 cm^5 molecule^-2), 2 nm bins, m^5
o4 = np.loadtxt(D + 'crs/crs_o4_greenblatt.dat', comments='#')
o4_sigma = np.array([o4[(o4[:, 0] >= l - 1) & (o4[:, 0] < l + 1), 1].mean() if ((o4[:, 0] >= l - 1) & (o4[:, 0] < l + 1)).any() else 0.0
                     for l in oz_lambda]) * 1e-46 * 1e-10

# --- the inputs of the Mie solver of the engine: refractive indices (wavelength in nm, real part, imaginary part as a positive absorption) and
# the log-normal size distributions (rmin, rmax, rmod in micrometres, sigma, density in g/cm^3)
mie_inputs = {}
for name, no, rh, fn in species:
    c = cfg(no, rh)
    wl, nr, ni = read_refr(D + 'aerosol/OPAC/refractive_indices/' + fn)
    mie_inputs[name] = (wl * 1e3, nr, np.abs(ni), [c['rmin'], c['rmax'], c['rmod'], c['sigma'], c['rho']])

def jagged(name, rows_):
    s_ = 'public static readonly double[][] %s =\n        {\n' % name
    for r_ in rows_:
        s_ += '            new double[] { ' + ', '.join('%.5e' % v for v in r_) + ' },\n'
    return s_ + '        };\n'

def arr(name, a, fmt='%.6g', per=8):
    s = 'public static readonly double[] %s =\n        {\n' % name
    items = [fmt % v for v in a]
    for i in range(0, len(items), per):
        s += '            ' + ', '.join(items[i:i + per]) + ',\n'
    return s + '        };\n'

with open(OUT, 'w') as f:
    f.write('// (not in pbrt-v4) Generated by gen_tables.py from the data of libRadtran 2.0.6 (www.libradtran.org): the Serdyuchenko-Gorshelev (IUP Bremen) ozone cross\n')
    f.write('// sections at 223 K (Serdyuchenko et al. 2014, Gorshelev et al. 2014, from the IUP Bremen data page), the U.S. standard atmosphere ozone profile (AFGL-TR-86-0110), the standard aerosol mass profiles of\n')
    f.write('// OPAC (Hess, Koepke and Schult 1998) and Mie optical properties computed from the OPAC refractive indices and size distributions.\n')
    f.write('// Do not edit by hand.\n\nnamespace pbrt\n{\n    public static class AtmosphereData\n    {\n')
    f.write('        public const double OzoneLambdaStart = 360.0, OzoneLambdaStep = 2.0, AerosolLambdaStart = 360.0, AerosolLambdaStep = 10.0;\n\n')
    f.write('        // Ozone absorption cross section in m^2, every 2 nm from 360 nm\n        ' + arr('OzoneCrossSection', oz_sigma, '%.5e').replace('\n        ', '\n        '))
    f.write('\n        // Altitudes in m and the ozone number density in 1/m^3 of the U.S. standard atmosphere\n        ' + arr('OzoneProfileAltitude', prof_z, '%.1f'))
    f.write('        ' + arr('OzoneProfileDensity', prof_o3, '%.5e'))
    for n, (z, c) in profiles.items():
        cname = ''.join(p.capitalize() for p in n.split('_'))
        f.write('\n        // Aerosol mass concentration in g/m^3 of the %s profile (inso, waso, soot) at the altitudes in m\n        ' % n + arr(cname + 'Altitude', z, '%.1f'))
        for s, nm in enumerate(['Inso', 'Waso', 'Soot']):
            f.write('        ' + arr(cname + nm, c[:, s], '%.5e'))
    for nm, key in [('Inso', 'inso'), ('Waso', 'waso'), ('Soot', 'soot')]:
        k, ssa, g = result[key]
        f.write('\n        // %s: mass extinction coefficient in m^2/g, single scattering albedo and asymmetry parameter every 10 nm from 360 nm\n        ' % key + arr(nm + 'MassExtinction', k, '%.5e'))
        f.write('        ' + arr(nm + 'Albedo', ssa, '%.5f'))
        f.write('        ' + arr(nm + 'Asymmetry', g, '%.5f'))
    f.write('\n        // Ozone absorption cross section in m^2 at 193 K, 203 K, ... 293 K (rows), every 2 nm from 360 nm\n        ' + arr('OzoneTemperatures', oz_temperatures, '%.1f'))
    f.write('        ' + jagged('OzoneCrossSectionByTemperature', oz_by_temperature))
    f.write('\n        // Air temperature in K, O2 and NO2 number densities in 1/m^3 of the U.S. standard atmosphere at the altitudes in m\n        ' + arr('AirProfileAltitude', prof_z, '%.1f'))
    f.write('        ' + arr('AirProfileTemperature', prof_T, '%.2f'))
    f.write('        ' + arr('AirProfileO2Density', prof_o2, '%.5e'))
    f.write('        ' + arr('AirProfileNO2Density', prof_no2, '%.5e'))
    f.write('\n        // NO2 cross section in m^2 = C0 + C1 (T - 273.15) + C2 (T - 273.15)^2, every 2 nm from 360 nm (zero above 760 nm)\n        ' + arr('No2C0', no2_c[0], '%.5e'))
    f.write('        ' + arr('No2C1', no2_c[1], '%.5e'))
    f.write('        ' + arr('No2C2', no2_c[2], '%.5e'))
    f.write('\n        // O2-O2 collision pair absorption cross section in m^5 per molecule pair, every 2 nm from 360 nm\n        ' + arr('O4CrossSection', o4_sigma, '%.5e'))
    for nm, key in [('Inso', 'inso'), ('Waso', 'waso'), ('Soot', 'soot')]:
        wl_, nr_, ni_, sd_ = mie_inputs[key]
        f.write('\n        // %s: refractive index (wavelength in nm, real part, imaginary part) and size distribution (rmin, rmax, rmod in um, sigma, density in g/cm^3)\n        ' % key + arr(nm + 'RefractiveWavelength', wl_, '%.1f'))
        f.write('        ' + arr(nm + 'RefractiveReal', nr_, '%.5f'))
        f.write('        ' + arr(nm + 'RefractiveImaginary', ni_, '%.5e'))
        f.write('        ' + arr(nm + 'SizeDistribution', sd_, '%.6g'))
    f.write('    }\n}\n')
print('written', OUT, file=sys.stderr)
