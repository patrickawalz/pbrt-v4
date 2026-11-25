# The scenes of the "sweep" shape as area lights (python3 genlights.py)
import math

head = '''LookAt 0 2.2 -5.2  0 0.7 0.3  0 1 0
Camera "perspective" "float fov" 36
Sampler "independent" "integer pixelsamples" %d "integer seed" 7
Integrator "path" "integer maxdepth" 5
Film "rgb" "integer xresolution" 360 "integer yresolution" 240 "string filename" "%s.exr"
WorldBegin
AttributeBegin
Material "diffuse" "rgb reflectance" [0.7 0.7 0.7]
Shape "trianglemesh" "point3 P" [-8 0 -8  8 0 -8  8 0 8  -8 0 8] "integer indices" [0 2 1 0 3 2]
Shape "trianglemesh" "point3 P" [-8 0 2.6  8 0 2.6  8 6 2.6  -8 6 2.6] "integer indices" [0 2 1 0 3 2]
AttributeEnd
AttributeBegin
Material "coateddiffuse" "rgb reflectance" [0.6 0.25 0.2] "float roughness" 0.15
Translate 0.2 0.5 0.6
Shape "sphere" "float radius" 0.5
AttributeEnd
'''

# The curved lights: channels of an arc of 120 degrees that narrow to a point, of four colors
curved = [((-2.2, 0.25, 0.4), (-1.9, 1.3, -0.2), (-1.2, 1.6, 0.8), (-0.6, 0.9, 1.4), (4.0, 2.2, 1.2)),
          ((0.9, 0.15, -0.6), (1.3, 0.2, 0.2), (1.9, 0.5, 0.3), (2.4, 1.3, 0.9), (1.2, 2.4, 4.0)),
          ((-0.9, 1.9, 1.6), (-0.2, 2.3, 1.2), (0.6, 2.2, 1.9), (1.3, 1.7, 1.5), (3.5, 3.2, 2.6)),
          ((-0.8, 0.06, -1.3), (-0.4, 0.07, -0.9), (0.1, 0.06, -1.3), (0.5, 0.08, -0.9), (1.4, 3.6, 1.6))]
half = math.radians(60)
w = 0.09
radius = w / math.sin(half)
arc = '"integer crossdegree" 2 "point2 cross" [%g %g  0 %g  %g %g] "float crossweights" [1 %g 1] "float scale" [1 0.15]' % \
      (-w, radius * (1 - math.cos(half)), radius * (1 - 1 / math.cos(half)), w, radius * (1 - math.cos(half)), math.cos(half))

def curved_scene(name, spp, extra):
    with open(name + '.pbrt', 'w') as f:
        f.write(head % (spp, name))
        for p0, p1, p2, p3, color in curved:
            f.write('AttributeBegin\nAreaLightSource "diffuse" "rgb L" [%g %g %g] "float scale" 4 "bool twosided" true\n' % color)
            f.write('Shape "sweep" "point3 P" [' + '  '.join('%g %g %g' % p for p in (p0, p1, p2, p3)) + '] "normal N" [0 1 0.2  0.3 1 0] ' + arc + extra + '\nAttributeEnd\n')

# The straight lights: flat strips, the same surface as "sweep" and as "ribbon3"
straight = [((-2.2, 0.25, 0.4), (-0.7, 1.5, 1.3), (4.0, 2.2, 1.2)), ((0.9, 0.12, -0.6), (2.4, 0.9, 0.6), (1.2, 2.4, 4.0)),
            ((-0.9, 2.0, 1.6), (1.3, 1.8, 1.5), (3.5, 3.2, 2.6)), ((-0.8, 0.06, -1.3), (0.5, 0.08, -0.9), (1.4, 3.6, 1.6))]

def straight_scene(name, spp, shape):
    with open(name + '.pbrt', 'w') as f:
        f.write(head % (spp, name))
        for a, b, color in straight:
            points = [tuple(a[k] + (b[k] - a[k]) * i / 3 for k in range(3)) for i in range(4)]
            text = '  '.join('%g %g %g' % p for p in points)
            f.write('AttributeBegin\nAreaLightSource "diffuse" "rgb L" [%g %g %g] "float scale" 4 "bool twosided" true\n' % color)
            if shape == 'sweep':
                f.write('Shape "sweep" "point3 P" [' + text + '] "normal N" [0 1 0.2  0 1 0.2] "integer crossdegree" 1 "point2 cross" [-0.09 0  0.09 0]\n')
            else:
                f.write('Shape "ribbon3" "string basis" "bezier" "integer degree" 3 "point3 P" [' + text + '] "normal N" [0 1 0.2  0 1 0.2  0 1 0.2  0 1 0.2] "float width" [%s]%s\n' %
                        ('0.09', ' "bool solidanglesampling" false' if shape == 'ribbon3area' else ''))
            f.write('AttributeEnd\n')

curved_scene('light_arc', 16, '')
curved_scene('light_arc_one', 16, ' "integer splitdepth" 0')
curved_scene('light_arc_ref', 400, '')
straight_scene('light_flat_sweep', 16, 'sweep')
straight_scene('light_flat_ribbon3', 16, 'ribbon3')
straight_scene('light_flat_ribbon3area', 16, 'ribbon3area')
straight_scene('light_flat_ref', 400, 'sweep')
