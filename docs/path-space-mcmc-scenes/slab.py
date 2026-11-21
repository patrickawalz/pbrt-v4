# usage: slab.py <name> <integrator> <samples> [seed] [extra integrator parameters]
# A point light over a slab of glass (a rippled top and a flat bottom, two triangle meshes with normals) and a floor under
# it, seen from under the slab: the caustic on the floor is light -> two refractions -> floor.
import sys, os, math
name, integ, spp = sys.argv[1:4]
seed = sys.argv[4] if len(sys.argv) > 4 else '1'
extra = sys.argv[5] if len(sys.argv) > 5 else ''
res = int(os.environ.get("RES", "96"))
n = int(os.environ.get("GRID", "5"))
depth = 5
integrators = {
 'bdpt': f'Integrator "bdpt" "integer maxdepth" {depth}\nSampler "independent" "integer pixelsamples" {spp} "integer seed" {seed}',
 'volpathmlt': f'Integrator "volpathmlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
 'volbidirmlt': f'Integrator "volbidirmlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
}
print('LookAt 0 -1.9 0.95  0 0.2 0  0 0 1')
print('Camera "perspective" "float fov" 60')
print(integrators[integ])
print(f'Film "rgb" "string filename" "/home/claude/out/slab/{name}.exr" "integer xresolution" {res} "integer yresolution" {res*3//4}')
print('WorldBegin')
print('LightSource "point" "rgb I" [ 30 28 24 ] "point3 from" [ 0.2 0.4 4 ]')
print('''AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.7 0.7 0.65 ]
  Shape "trianglemesh" "point3 P" [ -2.5 -2.5 0  2.5 -2.5 0  2.5 2.5 0  -2.5 2.5 0 ] "integer indices" [ 0 1 2 0 2 3 ]
AttributeEnd''')
def h(x, y):
    return 1.45 + 0.05 * (math.sin(1.3 * x + 0.5) * math.cos(1.1 * y) + 0.6 * math.sin(1.7 * x + 1.4 * y + 1.0))
def grad(x, y):
    hx = 0.05 * (1.3 * math.cos(1.3 * x + 0.5) * math.cos(1.1 * y) + 0.6 * 1.7 * math.cos(1.7 * x + 1.4 * y + 1.0))
    hy = 0.05 * (-1.1 * math.sin(1.3 * x + 0.5) * math.sin(1.1 * y) + 0.6 * 1.4 * math.cos(1.7 * x + 1.4 * y + 1.0))
    return hx, hy
def mesh(top):
    P, N, I = [], [], []
    for j in range(n + 1):
        for i in range(n + 1):
            x, y = -3 + 6 * i / n, -3 + 6 * j / n
            if top:
                P.append('%.5f %.5f %.5f' % (x, y, h(x, y)))
                hx, hy = grad(x, y)
                l = math.sqrt(hx * hx + hy * hy + 1)
                N.append('%.5f %.5f %.5f' % (-hx / l, -hy / l, 1 / l))
            else:
                P.append('%.5f %.5f %.5f' % (x, y, 1.2))
                N.append('0 0 -1')
    for j in range(n):
        for i in range(n):
            a = j * (n + 1) + i
            I.append('%d %d %d %d %d %d' % ((a, a + 1, a + n + 2, a, a + n + 2, a + n + 1) if top else (a, a + n + 2, a + 1, a, a + n + 1, a + n + 2)))
    print('  Shape "trianglemesh" "point3 P" [ %s ] "normal N" [ %s ] "integer indices" [ %s ]' % (' '.join(P), ' '.join(N), ' '.join(I)))
print('AttributeBegin\n  Material "dielectric" "float eta" 1.5')
mesh(True)
mesh(False)
print('AttributeEnd')
