# The scenes of the temporally unstructured volumes (src/pbrt/temporalgrid.cs): a puff of smoke that turns about the middle of
# its box by 1.6 radians while the shutter is open. python3 gen.py writes (the files are large and are not kept in the repository)
#   smoke_tuv.pbrt        the functions of time, made from the density and the velocities (the path followed in 8 steps)
#   smoke_eulerian8.pbrt  the same motion followed at every lookup, in 8 steps
#   smoke_eulerian1.pbrt  followed in one step: the straight line of Eulerian motion blur
#   smoke_still.pbrt      no motion
import numpy as np

n = 64
turn = 1.6
c = (np.arange(n) + 0.5) / n
z, y, x = np.meshgrid(c, c, c, indexing='ij')

def blob(cx, cy, cz, r):
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2 + (z - cz) ** 2) / r
    return np.where(d < 1, (1 - d * d) ** 2, 0.0)

# (a few lumps off the axis, so that the turn is seen)
density = blob(0.74, 0.5, 0.5, 0.13) + 0.8 * blob(0.66, 0.62, 0.42, 0.09) + 0.9 * blob(0.7, 0.4, 0.6, 0.08) + 0.7 * blob(0.3, 0.42, 0.5, 0.07)
density *= 1 + 0.35 * np.sin(40 * x) * np.sin(37 * y) * np.sin(43 * z)
# (the box of the medium is 2 wide: the velocities are in its units a unit of time)
vx = -turn * (y - 0.5) * 2
vy = turn * (x - 0.5) * 2
vz = np.zeros_like(vx)
velocity = np.stack([vx, vy, vz], axis=-1)

def numbers(a):
    return ' '.join('%.4g' % v for v in a.ravel())

head = '''LookAt 0 0 -4.6  0 0 0  0 1 0
Camera "perspective" "float fov" 30 "float shutteropen" 0 "float shutterclose" 1
Sampler "independent" "integer pixelsamples" 64 "integer seed" 7
Integrator "volpath" "integer maxdepth" 30
Film "rgb" "integer xresolution" 400 "integer yresolution" 400 "string filename" "%s.exr"
WorldBegin
LightSource "infinite" "rgb L" [0.08 0.1 0.14]
LightSource "distant" "point3 from" [3 4 -3] "point3 to" [0 0 0] "rgb L" [7 6.5 6]
AttributeBegin
  Material "diffuse" "rgb reflectance" [0.03 0.04 0.06]
  Shape "trianglemesh" "integer indices" [0 1 2 0 2 3] "point3 P" [-6 -6 2.5  6 -6 2.5  6 6 2.5  -6 6 2.5]
AttributeEnd
MakeNamedMedium "smoke" "string type" "temporalgrid" "integer nx" %d "integer ny" %d "integer nz" %d
    "point3 p0" [-1 -1 -1] "point3 p1" [1 1 1] "rgb sigma_a" [0.4 0.4 0.4] "rgb sigma_s" [9 9 9] "float scale" 2.5 "float g" 0.3
    %s
    "float density" [ %s ]
AttributeBegin
  MediumInterface "smoke" ""
  Material "interface"
  Shape "sphere" "float radius" 1.72
AttributeEnd
'''
velocityText = '"float velocity" [ ' + numbers(velocity) + ' ]'
scenes = {'smoke_tuv': '"integer steps" 8 "integer timesamples" 16 ' + velocityText,
          'smoke_eulerian8': '"bool eulerian" true "integer steps" 8 ' + velocityText,
          'smoke_eulerian1': '"bool eulerian" true "integer steps" 1 ' + velocityText,
          'smoke_still': ''}
for name, extra in scenes.items():
    with open(name + '.pbrt', 'w') as f:
        f.write(head % (name, n, n, n, extra, numbers(density)))
