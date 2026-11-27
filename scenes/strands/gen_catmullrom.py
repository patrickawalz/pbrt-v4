# Writes catmullrom_{uniform,centripetal,chordal}.pbrt: the same strands ("rocap" above, "rocylinder" below) through the
# same points at uneven distances, with the "parameterization" of the basis "catmull-rom" 0, 0.5 and 1. The points are
# the small spheres. Only the parameterization differs between the three files.
import random

def strands():
    random.seed(11)
    result = []
    for row in range(2):
        for column in range(3):
            ox, oy = -3.3 + 3.3 * column, 1.6 - 3.2 * row
            points = []
            x, y = ox - 1.2, oy
            for i in range(9):
                # long steps and very short ones in turn, with a turn at each
                length = 0.9 if i % 3 != 1 else 0.07
                x += length * random.uniform(0.3, 1.0)
                y = oy + random.uniform(-0.75, 0.75) * (1.0 if i % 3 != 1 else 0.15) + (points[-1][1] - oy if i % 3 == 1 else 0)
                points.append((x, y, random.uniform(-0.1, 0.1)))
            result.append((row, points))
    return result

def scene(name, power, label):
    lines = []
    lines.append('LookAt 0 0 -11.5  0 0 0  0 1 0')
    lines.append('Camera "perspective" "float fov" 34')
    lines.append('Sampler "independent" "integer pixelsamples" 64 "integer seed" 7')
    lines.append('Integrator "volpath" "integer maxdepth" 4')
    lines.append('Film "rgb" "integer xresolution" 720 "integer yresolution" 420 "string filename" "%s.exr"' % name)
    lines.append('WorldBegin')
    lines.append('LightSource "infinite" "rgb L" [0.45 0.5 0.6]')
    lines.append('LightSource "distant" "point3 from" [-3 5 -8] "point3 to" [0 0 0] "rgb L" [2.6 2.5 2.3]')
    lines.append('AttributeBegin')
    lines.append('Material "diffuse" "rgb reflectance" [0.62 0.62 0.62]')
    lines.append('Shape "trianglemesh" "point3 P" [-9 -5 0.6  9 -5 0.6  9 5 0.6  -9 5 0.6] "integer indices" [0 1 2 0 2 3]')
    lines.append('AttributeEnd')
    for row, points in strands():
        shape = "rocap" if row == 0 else "rocylinder"
        flat = ' '.join('%.5f %.5f %.5f' % p for p in points)
        radii = ' '.join('%.4f' % (0.075 - 0.006 * i) for i in range(len(points)))
        lines.append('Material "diffuse" "rgb reflectance" [%s]' % ("0.75 0.38 0.12" if row == 0 else "0.16 0.42 0.72"))
        lines.append('Shape "%s" "point3 P" [%s] "float radius" [%s] "string basis" "catmull-rom" "string wrap" "pinned"%s'
                     % (shape, flat, radii, '' if power == 0 else ' "float parameterization" %g' % power))
        lines.append('Material "diffuse" "rgb reflectance" [0.05 0.05 0.05]')
        for p in points:
            lines.append('AttributeBegin')
            lines.append('Translate %.5f %.5f %.5f' % (p[0], p[1], p[2] - 0.1))
            lines.append('Shape "sphere" "float radius" 0.035')
            lines.append('AttributeEnd')
    open(name + '.pbrt', 'w').write('\n'.join(lines) + '\n')

scene('catmullrom_uniform', 0, 'uniform')
scene('catmullrom_centripetal', 0.5, 'centripetal')
scene('catmullrom_chordal', 1, 'chordal')
