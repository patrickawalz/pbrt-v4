# usage: sheet.py <name> <integrator> <samples> [seed] [extra integrator parameters]
# A point light over a rippled sheet of water (a triangle mesh with normals) and a floor of two triangles under it, seen
# from under the sheet: the caustic on the floor is light -> one refraction -> floor, which bdpt reaches from the light.
import sys, os, math
name, integ, spp = sys.argv[1:4]
seed = sys.argv[4] if len(sys.argv) > 4 else '1'
extra = sys.argv[5] if len(sys.argv) > 5 else ''
res = int(os.environ.get("RES", "96"))
n = int(os.environ.get("GRID", "48"))
depth = 4
integrators = {
 'bdpt': f'Integrator "bdpt" "integer maxdepth" {depth}\nSampler "independent" "integer pixelsamples" {spp} "integer seed" {seed}',
 'volpathmlt': f'Integrator "volpathmlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
 'volbidirmlt': f'Integrator "volbidirmlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
}
print('LookAt 0 -1.9 0.95  0 0.2 0  0 0 1')
print('Camera "perspective" "float fov" 60')
print(integrators[integ])
print(f'Film "rgb" "string filename" "/home/claude/out/sheet/{name}.exr" "integer xresolution" {res} "integer yresolution" {res*3//4}')
print('WorldBegin')
print('LightSource "point" "rgb I" [ 30 28 24 ] "point3 from" [ 0.2 0.4 4 ]')
print('''AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.7 0.7 0.65 ]
  Shape "trianglemesh" "point3 P" [ -2.5 -2.5 0  2.5 -2.5 0  2.5 2.5 0  -2.5 2.5 0 ] "integer indices" [ 0 1 2 0 2 3 ]
AttributeEnd''')
def h(x, y):
    return 1.2 + 0.06 * (math.sin(3.1 * x + 0.5) * math.cos(2.3 * y) + 0.6 * math.sin(5.2 * x + 4.1 * y + 1.0))
def grad(x, y):
    hx = 0.06 * (3.1 * math.cos(3.1 * x + 0.5) * math.cos(2.3 * y) + 0.6 * 5.2 * math.cos(5.2 * x + 4.1 * y + 1.0))
    hy = 0.06 * (-2.3 * math.sin(3.1 * x + 0.5) * math.sin(2.3 * y) + 0.6 * 4.1 * math.cos(5.2 * x + 4.1 * y + 1.0))
    return hx, hy
P, N, I = [], [], []
for j in range(n + 1):
    for i in range(n + 1):
        x, y = -3 + 6 * i / n, -3 + 6 * j / n
        P.append('%.5f %.5f %.5f' % (x, y, h(x, y)))
        hx, hy = grad(x, y)
        l = math.sqrt(hx * hx + hy * hy + 1)
        N.append('%.5f %.5f %.5f' % (-hx / l, -hy / l, 1 / l))
for j in range(n):
    for i in range(n):
        a = j * (n + 1) + i
        I.append('%d %d %d %d %d %d' % (a, a + 1, a + n + 2, a, a + n + 2, a + n + 1))
print('AttributeBegin')
print('  Material "dielectric" "float eta" 1.33')
print('  Shape "trianglemesh" "point3 P" [ %s ] "normal N" [ %s ] "integer indices" [ %s ]' % (' '.join(P), ' '.join(N), ' '.join(I)))
print('AttributeEnd')
