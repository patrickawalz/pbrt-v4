import sys, math
# usage: cornell.py <name> <mutations per pixel> <seed> [extra integrator parameters]
name, mpp, seed = sys.argv[1], sys.argv[2], sys.argv[3]
extra = sys.argv[4] if len(sys.argv) > 4 else ''
print('LookAt 0 -3.8 1  0 0 1  0 0 1')
print('Camera "perspective" "float fov" 39')
print(f'Integrator "volbidirjrlt" "integer maxdepth" 8 "integer mutationsperpixel" {mpp} "integer bootstrapsamples" 100000 "integer seed" {seed} {extra}')
print('Sampler "independent" "integer pixelsamples" 1')
print(f'Film "rgb" "string filename" "/home/claude/out/cornell/{name}.exr" "integer xresolution" 256 "integer yresolution" 256')
print('WorldBegin')
def quad(p, flip=False):
    idx = '0 2 1 0 3 2' if flip else '0 1 2 0 2 3'
    print('  Shape "trianglemesh" "point3 P" [ %s ] "integer indices" [ %s ]' % (' '.join('%g' % c for v in p for c in v), idx))
print('AttributeBegin\n  Material "diffuse" "rgb reflectance" [ 0.73 0.73 0.73 ]')
quad([(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0)])
quad([(-1, -1, 2), (1, -1, 2), (1, 1, 2), (-1, 1, 2)], True)
quad([(-1, 1, 0), (1, 1, 0), (1, 1, 2), (-1, 1, 2)])
# (the image is mirrored in x: +x is on the left of the picture)
print('  Material "diffuse" "rgb reflectance" [ 0.65 0.06 0.05 ]')
quad([(1, -1, 0), (1, 1, 0), (1, 1, 2), (1, -1, 2)], True)
print('  Material "diffuse" "rgb reflectance" [ 0.12 0.45 0.15 ]')
quad([(-1, -1, 0), (-1, 1, 0), (-1, 1, 2), (-1, -1, 2)])
print('AttributeEnd')
print('AttributeBegin\n  AreaLightSource "diffuse" "rgb L" [ 17 14 9 ] "bool twosided" false')
quad([(-0.25, -0.25, 1.99), (0.25, -0.25, 1.99), (0.25, 0.25, 1.99), (-0.25, 0.25, 1.99)], True)
print('AttributeEnd')
# the conductor box, in the back left corner of the picture
cx, cy, h, half, angle = 0.42, 0.38, 1.2, 0.3, math.radians(20)
c = [(cx + math.cos(angle) * x - math.sin(angle) * y, cy + math.sin(angle) * x + math.cos(angle) * y)
     for x, y in ((-half, -half), (half, -half), (half, half), (-half, half))]
print('AttributeBegin\n  Material "conductor" "spectrum eta" "metal-Al-eta" "spectrum k" "metal-Al-k" "float roughness" 0')
for i in range(4):
    a, b = c[i], c[(i + 1) % 4]
    quad([(a[0], a[1], 0), (b[0], b[1], 0), (b[0], b[1], h), (a[0], a[1], h)])
quad([(c[0][0], c[0][1], h), (c[1][0], c[1][1], h), (c[2][0], c[2][1], h), (c[3][0], c[3][1], h)])
print('AttributeEnd')
print('AttributeBegin\n  Material "dielectric" "float eta" 1.5\n  Translate -0.42 -0.35 0.36\n  Shape "sphere" "float radius" 0.36\nAttributeEnd')
