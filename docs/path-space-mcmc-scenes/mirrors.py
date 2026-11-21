# usage: mirrors.py <name> <integrator> <samples> [seed] [extra integrator parameters]
# A point light, two mirrors (triangle meshes with rippled normals) and a floor: the light reaches the floor directly, by
# one mirror, and by the first mirror and then the second one (two reflections).
import sys, os, math
name, integ, spp = sys.argv[1:4]
seed = sys.argv[4] if len(sys.argv) > 4 else '1'
extra = sys.argv[5] if len(sys.argv) > 5 else ''
res = int(os.environ.get("RES", "96"))
n = int(os.environ.get("GRID", "4"))
depth = 5
integrators = {
 'bdpt': f'Integrator "bdpt" "integer maxdepth" {depth}\nSampler "independent" "integer pixelsamples" {spp} "integer seed" {seed}',
 'volbidirmlt': f'Integrator "volbidirmlt" "integer maxdepth" {depth} "integer mutationsperpixel" {spp} "integer seed" {seed} {extra}\nSampler "independent" "integer pixelsamples" 1',
}
print('LookAt -0.8 -4.5 2.6  -0.8 0 0  0 0 1')
print('Camera "perspective" "float fov" 38')
print(integrators[integ])
print(f'Film "rgb" "string filename" "/home/claude/out/mirrors/{name}.exr" "integer xresolution" {res} "integer yresolution" {res*3//4}')
print('WorldBegin')
print('LightSource "point" "rgb I" [ 12 11 10 ] "point3 from" [ -1.5 0 1.5 ]')
print('''AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.7 0.7 0.65 ]
  Shape "trianglemesh" "point3 P" [ -4 -3 0  3 -3 0  3 3 0  -4 3 0 ] "integer indices" [ 0 1 2 0 2 3 ]
AttributeEnd''')
def norm(v):
    l = math.sqrt(sum(c * c for c in v))
    return [c / l for c in v]
def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]
def mirror(centre, normal, half, phase):
    normal = norm(normal)
    t1 = norm(cross(normal, [0, 1, 0]))
    t2 = cross(normal, t1)
    P, N, I = [], [], []
    for j in range(n + 1):
        for i in range(n + 1):
            a, b = -half + 2 * half * i / n, -half + 2 * half * j / n
            P.append(' '.join('%.5f' % (centre[c] + a * t1[c] + b * t2[c]) for c in range(3)))
            sa, sb = 0.10 * math.sin(3.0 * a + phase), 0.10 * math.cos(2.5 * b + phase)
            N.append(' '.join('%.5f' % c for c in norm([normal[c] + sa * t1[c] + sb * t2[c] for c in range(3)])))
    for j in range(n):
        for i in range(n):
            q = j * (n + 1) + i
            I.append('%d %d %d %d %d %d' % (q, q + 1, q + n + 2, q, q + n + 2, q + n + 1))
    print('  Shape "trianglemesh" "point3 P" [ %s ] "normal N" [ %s ] "integer indices" [ %s ]' % (' '.join(P), ' '.join(N), ' '.join(I)))
print('AttributeBegin\n  Material "conductor" "spectrum eta" "metal-Al-eta" "spectrum k" "metal-Al-k" "float roughness" 0')
mirror([1.5, 0, 1.5], [-0.911, 0, 0.412], 0.6, 0.3)
mirror([0, 0, 3.2], [0.236, 0, -0.972], 0.6, 1.1)
print('AttributeEnd')
