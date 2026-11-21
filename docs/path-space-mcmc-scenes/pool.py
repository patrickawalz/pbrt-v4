# usage: pool.py <name> <integrator> <clear|murky> <samples> [seed] [extra integrator parameters]
# A pool seen from above its rippled surface, lit by a small light over it: everything under the water is seen and lit through
# a refracting surface (a specular vertex at both ends of every path).
import sys, os, math
name, integ, kind, spp = sys.argv[1:5]
seed = sys.argv[5] if len(sys.argv) > 5 else '1'
extra = sys.argv[6] if len(sys.argv) > 6 else ''
res = int(os.environ.get("RES", "256"))
depth = 7
integrators = {
 'volpath': f'Integrator "volpath" "integer maxdepth" {depth}\nSampler "independent" "integer pixelsamples" {spp} "integer seed" {seed}',
 'bdpt': f'Integrator "bdpt" "integer maxdepth" {depth}\nSampler "independent" "integer pixelsamples" {spp} "integer seed" {seed}',
 'volpathmlt': f'Integrator "volpathmlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
 'volbidirmlt': f'Integrator "volbidirmlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
 'volbidirjrlt': f'Integrator "volbidirjrlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
 'mlt': f'Integrator "mlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} {extra}\nSampler "independent" "integer pixelsamples" 1',
}
print('LookAt 0 -5.2 4.2  0 0.1 0.4  0 0 1')
if kind == 'murky':
    print('MakeNamedMedium "water" "string type" "homogeneous" "rgb sigma_a" [ 0.10 0.03 0.02 ] "rgb sigma_s" [ 0.30 0.35 0.40 ] "float g" 0.6')
print('Camera "perspective" "float fov" 40')
print(integrators[integ])
print(f'Film "rgb" "string filename" "/home/claude/out/pool/{name}.exr" "integer xresolution" {res} "integer yresolution" {res*3//4}')
print('WorldBegin')
print('''AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.75 0.75 0.7 ]
  Shape "trianglemesh" "point3 P" [ -3 -3 0  3 -3 0  3 3 0  -3 3 0 ] "integer indices" [ 0 1 2 0 2 3 ]
  Material "diffuse" "rgb reflectance" [ 0.25 0.45 0.7 ]
  Shape "trianglemesh" "point3 P" [ -2 2 0  2 2 0  2 2 1.6  -2 2 1.6 ] "integer indices" [ 0 1 2 0 2 3 ]
  Shape "trianglemesh" "point3 P" [ -2 -2 0  -2 2 0  -2 2 1.6  -2 -2 1.6 ] "integer indices" [ 0 1 2 0 2 3 ]
  Shape "trianglemesh" "point3 P" [ 2 -2 0  2 2 0  2 2 1.6  2 -2 1.6 ] "integer indices" [ 0 2 1 0 3 2 ]
  Shape "trianglemesh" "point3 P" [ -2 -2 0  2 -2 0  2 -2 1.6  -2 -2 1.6 ] "integer indices" [ 0 2 1 0 3 2 ]
  Material "diffuse" "rgb reflectance" [ 0.8 0.3 0.2 ]
  Translate 0.7 0.5 0.35
  Shape "sphere" "float radius" 0.35
AttributeEnd
AttributeBegin
  AreaLightSource "diffuse" "rgb L" [ 225 212 187 ] "bool twosided" false
  Shape "trianglemesh" "point3 P" [ -0.3 0.1 5  0.3 0.1 5  0.3 0.7 5  -0.3 0.7 5 ] "integer indices" [ 0 2 1 0 3 2 ]
AttributeEnd''')
n = 96
def h(x, y):
    return 1.2 + 0.06 * (math.sin(3.1 * x + 0.5) * math.cos(2.3 * y) + 0.6 * math.sin(5.2 * x + 4.1 * y + 1.0))
def grad(x, y):
    hx = 0.06 * (3.1 * math.cos(3.1 * x + 0.5) * math.cos(2.3 * y) + 0.6 * 5.2 * math.cos(5.2 * x + 4.1 * y + 1.0))
    hy = 0.06 * (-2.3 * math.sin(3.1 * x + 0.5) * math.sin(2.3 * y) + 0.6 * 4.1 * math.cos(5.2 * x + 4.1 * y + 1.0))
    return hx, hy
P, N, I = [], [], []
for j in range(n + 1):
    for i in range(n + 1):
        x, y = -2 + 4 * i / n, -2 + 4 * j / n
        P.append('%.5f %.5f %.5f' % (x, y, h(x, y)))
        hx, hy = grad(x, y)
        l = math.sqrt(hx * hx + hy * hy + 1)
        N.append('%.5f %.5f %.5f' % (-hx / l, -hy / l, 1 / l))
for j in range(n):
    for i in range(n):
        a = j * (n + 1) + i
        I.append('%d %d %d %d %d %d' % (a, a + 1, a + n + 2, a, a + n + 2, a + n + 1))
print('AttributeBegin')
if kind == 'murky':
    print('  MediumInterface "water" ""')
print('  Material "dielectric" "float eta" 1.33')
print('  Shape "trianglemesh" "point3 P" [ %s ] "normal N" [ %s ] "integer indices" [ %s ]' % (' '.join(P), ' '.join(N), ' '.join(I)))
print('AttributeEnd')
