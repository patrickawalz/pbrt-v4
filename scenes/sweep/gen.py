# The scenes of the "sweep" shape: the same blades with one cross section or another (python3 gen.py)
import math, random

def blades(kind):
    random.seed(11)
    lines = []
    for i in range(70):
        x, z = random.uniform(-1.6, 1.6), random.uniform(-1.0, 1.4)
        height = random.uniform(0.7, 1.3)
        lean, turn = random.uniform(0.15, 0.6) * height, random.uniform(0, 2 * math.pi)
        dx, dz = math.cos(turn), math.sin(turn)
        points = [(x, 0, z), (x + 0.05 * lean * dx, 0.4 * height, z + 0.05 * lean * dz), (x + 0.35 * lean * dx, 0.8 * height, z + 0.35 * lean * dz),
                  (x + lean * dx, height, z + lean * dz)]
        # (the normal: up the lean at the foot, turned over the bend at the tip)
        normals = [(dx, 0.1, dz), (dx * 0.5, 0.9, dz * 0.5)]
        w, h = 0.07, 0.035
        text = 'Shape "sweep" "point3 P" [' + '  '.join('%.5f %.5f %.5f' % p for p in points) + '] "normal N" [' + \
               '  '.join('%.5f %.5f %.5f' % n for n in normals) + '] "float scale" [1 0.12] '
        if kind == 'flat':
            text += '"integer crossdegree" 1 "point2 cross" [%g 0  %g 0]' % (-w, w)
        elif kind == 'quadratic':
            text += '"integer crossdegree" 2 "point2 cross" [%g %g  0 %g  %g %g]' % (-w, h, -h, w, h)
        elif kind == 'arc':
            # (an arc of a circle of 100 degrees: the weight of the middle point is the cosine of half the angle)
            half = math.radians(50)
            radius = w / math.sin(half)
            text += '"integer crossdegree" 2 "point2 cross" [%g %g  0 %g  %g %g] "float crossweights" [1 %g 1]' % \
                    (-w, radius * (1 - math.cos(half)), radius * (1 - 1 / math.cos(half)), w, radius * (1 - math.cos(half)), math.cos(half))
        elif kind == 'cubic':
            text += '"integer crossdegree" 3 "point2 cross" [%g %g  %g %g  %g %g  %g %g]' % (-w, h, -w / 3, -2 * h, w / 3, 2 * h, w, -h)
        elif kind == 'keel':
            # (two quadratic pieces that meet in a keel)
            text += '"integer crossdegree" 2 "point2 cross" [%g %g  %g %g  0 %g  %g %g  %g %g]' % (-w, h, -w / 2, h, -h, w / 2, h, w, h)
        elif kind == 'tube':
            # (a circle of four arcs of a quarter turn, about a center that goes from one side of the curve to the other)
            r, s = 0.045, math.sqrt(0.5)
            circle = [(r, 0), (r, r), (0, r), (-r, r), (-r, 0), (-r, -r), (0, -r), (r, -r), (r, 0)]
            cross = []
            for section in range(2):
                shift = -0.04 if section == 0 else 0.25
                cross += ['%g %g' % (cx + shift, cy) for cx, cy in circle]
            text += '"integer crossdegree" 2 "integer sections" 2 "point2 cross" [' + '  '.join(cross) + '] "float crossweights" [1 %g 1 %g 1 %g 1 %g 1]' % (s, s, s, s)
        lines.append(text)
    return lines

for kind in ['flat', 'quadratic', 'arc', 'cubic', 'keel', 'tube']:
    with open('blades_' + kind + '.pbrt', 'w') as f:
        f.write('''LookAt 0 1.5 -3.6  0 0.5 0.2  0 1 0
Camera "perspective" "float fov" 36
Sampler "independent" "integer pixelsamples" 64 "integer seed" 7
Integrator "volpath" "integer maxdepth" 5
Film "rgb" "integer xresolution" 480 "integer yresolution" 320 "string filename" "blades_%s.exr"
WorldBegin
LightSource "infinite" "rgb L" [0.35 0.4 0.5]
LightSource "distant" "point3 from" [3 5 -2] "point3 to" [0 0 0] "rgb L" [3.2 3 2.8]
AttributeBegin
Material "diffuse" "rgb reflectance" [0.7 0.7 0.7]
Shape "trianglemesh" "point3 P" [-8 0 -8  8 0 -8  8 0 8  -8 0 8] "integer indices" [0 2 1 0 3 2]
AttributeEnd
Material "coateddiffuse" "rgb reflectance" [0.2 0.5 0.12] "float roughness" 0.3
''' % kind)
        f.write('\n'.join(blades(kind)) + '\n')
