import sys
name, integ, kind = sys.argv[1], sys.argv[2], sys.argv[3]
import os
res = int(os.environ.get("RES", "96"))
integrators = {
 'volpath': 'Integrator "volpath" "integer maxdepth" 5\nSampler "independent" "integer pixelsamples" %s "integer seed" %s',
 'bdpt': 'Integrator "bdpt" "integer maxdepth" 5\nSampler "independent" "integer pixelsamples" %s "integer seed" %s',
 'volpathmlt': 'Integrator "volpathmlt" "integer maxdepth" 5 "integer mutationsperpixel" %s "integer chains" 200 "integer bootstrapsamples" 40000 "integer seed" %s\nSampler "independent" "integer pixelsamples" 1',
 'volbidirmlt': 'Integrator "volbidirmlt" "integer maxdepth" 5 "integer mutationsperpixel" %s "integer chains" 200 "integer bootstrapsamples" 40000 "integer seed" %s\nSampler "independent" "integer pixelsamples" 1',
 'volpathjrlt': 'Integrator "volpathjrlt" "integer maxdepth" 5 "integer mutationsperpixel" %s "integer chains" 200 "integer bootstrapsamples" 40000 "integer seed" %s\nSampler "independent" "integer pixelsamples" 1',
 'volbidirjrlt': 'Integrator "volbidirjrlt" "integer maxdepth" 5 "integer mutationsperpixel" %s "integer chains" 200 "integer bootstrapsamples" 40000 "integer seed" %s\nSampler "independent" "integer pixelsamples" 1',
 'mlt': 'Integrator "mlt" "integer maxdepth" 5 "integer mutationsperpixel" %s "integer chains" 200 "integer bootstrapsamples" 40000\nSampler "independent" "integer pixelsamples" 1',
}
spp = sys.argv[4]; seed = sys.argv[5] if len(sys.argv) > 5 else '1'
medium = kind in ('fog', 'hetero', 'chroma', 'glassfog', 'fogball', 'pointfog', 'glow')
media = []
print('LookAt 0 -6 1.5  0 0 1.2  0 0 1')
header = integrators[integ] % ((spp, seed) if integ != 'mlt' else (spp,))
header += f'\nFilm "rgb" "string filename" "/home/claude/out/mcmc/{name}.exr" "integer xresolution" {res} "integer yresolution" {res*3//4}'
if kind == 'glow':
    print('MakeNamedMedium "glow" "string type" "homogeneous" "rgb sigma_a" [ 0.6 0.6 0.6 ] "rgb sigma_s" [ 0.8 0.8 0.8 ] "float g" 0.2 "rgb Le" [ 1.0 0.45 0.12 ] "float Lescale" 2.5')
if kind in ('fog', 'glassfog', 'fogball', 'pointfog', 'glow'):
    print('MakeNamedMedium "air" "string type" "homogeneous" "rgb sigma_a" [ 0.02 0.02 0.02 ] "rgb sigma_s" [ 0.25 0.25 0.25 ] "float g" 0.3')
if kind == 'chroma':
    print('MakeNamedMedium "air" "string type" "homogeneous" "rgb sigma_a" [ 0.02 0.05 0.15 ] "rgb sigma_s" [ 0.1 0.25 0.5 ] "float g" 0.3')
if kind == 'hetero':
    import random
    random.seed(3)
    n = 8
    d = ' '.join('%.3f' % (random.random() ** 2) for _ in range(n * n * n))
    print(f'MakeNamedMedium "air" "string type" "uniformgrid" "integer nx" {n} "integer ny" {n} "integer nz" {n} "float density" [ {d} ] "point3 p0" [ -2.1 -6.5 -0.1 ] "point3 p1" [ 2.1 2.1 3.1 ] "rgb sigma_a" [ 0.05 0.05 0.05 ] "rgb sigma_s" [ 0.9 0.9 0.9 ] "float g" 0.3')
if medium:
    print('MediumInterface "" "air"')
print('Camera "perspective" "float fov" 36')
print(header)
print('WorldBegin')
if medium:
    print('MediumInterface "air" "air"')
floor = 'Material "conductor" "float roughness" 0.08' if kind == 'glossy' else 'Material "diffuse" "rgb reflectance" [ 0.7 0.7 0.7 ]'
box = '''AttributeBegin
  FLOOR
  Shape "trianglemesh" "point3 P" [ -2 -7 0  2 -7 0  2 2 0  -2 2 0 ] "integer indices" [ 0 1 2 0 2 3 ]
  Material "diffuse" "rgb reflectance" [ 0.7 0.7 0.7 ]
  Shape "trianglemesh" "point3 P" [ -2 -7 3  2 -7 3  2 2 3  -2 2 3 ] "integer indices" [ 0 2 1 0 3 2 ]
  Shape "trianglemesh" "point3 P" [ -2 2 0  2 2 0  2 2 3  -2 2 3 ] "integer indices" [ 0 1 2 0 2 3 ]
  Material "diffuse" "rgb reflectance" [ 0.7 0.2 0.15 ]
  Shape "trianglemesh" "point3 P" [ -2 -7 0  -2 2 0  -2 2 3  -2 -7 3 ] "integer indices" [ 0 1 2 0 2 3 ]
  Material "diffuse" "rgb reflectance" [ 0.2 0.6 0.2 ]
  Shape "trianglemesh" "point3 P" [ 2 -7 0  2 2 0  2 2 3  2 -7 3 ] "integer indices" [ 0 2 1 0 3 2 ]
AttributeEnd
AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.5 0.5 0.5 ]
  AreaLightSource "diffuse" "rgb L" [ 12 11 9 ] "bool twosided" false
  Shape "trianglemesh" "point3 P" [ -0.6 -0.6 2.98  0.6 -0.6 2.98  0.6 0.6 2.98  -0.6 0.6 2.98 ] "integer indices" [ 0 2 1 0 3 2 ]
AttributeEnd'''
if kind in ('point', 'pointfog'):
    # (a point light and a spot light in the place of the area light)
    i = box.index('AttributeBegin\n  Material "diffuse" "rgb reflectance" [ 0.5 0.5 0.5 ]')
    box = box[:i] + '''AttributeBegin
  LightSource "point" "rgb I" [ 5 4.6 3.8 ] "point3 from" [ -0.8 0.3 2.5 ]
  LightSource "spot" "rgb I" [ 40 40 44 ] "point3 from" [ -1.5 -3.5 2.6 ] "point3 to" [ 0.5 -0.5 0.8 ] "float coneangle" 22 "float conedeltaangle" 6
AttributeEnd'''
if kind in ('thermal', 'thermal2'):
    # (a glowing surface in the place of the light: its radiance by its temperature and its material)
    box = box.replace('AreaLightSource "diffuse" "rgb L" [ 12 11 9 ] "bool twosided" false', 'AreaLightSource "%s" "float temperature" 2600 "float scale" %s "bool twosided" false %s' % (kind, os.environ.get("THERMALSCALE", "1"), os.environ.get("THERMALEXTRA", "")))
if kind == 'bb':
    # (the ordinary light with the spectrum of a blackbody of 2600 K)
    box = box.replace('"rgb L" [ 12 11 9 ]', '"blackbody L" 2600 "float scale" 12')
if kind == 'thermallow':
    box = box.replace('AreaLightSource "diffuse" "rgb L" [ 12 11 9 ] "bool twosided" false', 'AreaLightSource "thermal" "float temperature" 2600 "float scale" 0.0003 "bool twosided" false "bool blackbody" true')
if kind in ('sky', 'skyglass'):
    # (no ceiling and no light in the room: an infinite light of one radiance)
    box = box.replace('  Shape "trianglemesh" "point3 P" [ -2 -7 3  2 -7 3  2 2 3  -2 2 3 ] "integer indices" [ 0 2 1 0 3 2 ]\n', '')
    i = box.index('AttributeBegin\n  Material "diffuse" "rgb reflectance" [ 0.5 0.5 0.5 ]')
    box = box[:i] + 'LightSource "infinite" "rgb L" [ 0.8 0.9 1.1 ]'
if kind in ('map', 'mapglass'):
    # (no ceiling and no light in the room: an infinite light with an image that has three bright patches)
    box = box.replace('  Shape "trianglemesh" "point3 P" [ -2 -7 3  2 -7 3  2 2 3  -2 2 3 ] "integer indices" [ 0 2 1 0 3 2 ]\n', '')
    i = box.index('AttributeBegin\n  Material "diffuse" "rgb reflectance" [ 0.5 0.5 0.5 ]')
    box = box[:i] + 'LightSource "infinite" "string filename" "/home/claude/out/mcmc/envmap.pfm"'
if kind in ('sun', 'sunglass'):
    # (no ceiling and no light in the room: a distant light from above, at a slant)
    box = box.replace('  Shape "trianglemesh" "point3 P" [ -2 -7 3  2 -7 3  2 2 3  -2 2 3 ] "integer indices" [ 0 2 1 0 3 2 ]\n', '')
    i = box.index('AttributeBegin\n  Material "diffuse" "rgb reflectance" [ 0.5 0.5 0.5 ]')
    box = box[:i] + 'LightSource "distant" "point3 from" [ 0.6 -1.2 3 ] "point3 to" [ 0 0 0 ] "rgb L" [ 3 2.8 2.4 ]'
print(box.replace('FLOOR', floor))
if kind == 'door':
    # (a wall between the camera and the light, with a narrow slit: the room in front is lit through the slit alone)
    print('''AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.6 0.6 0.6 ]
  Shape "trianglemesh" "point3 P" [ -2 -1.2 0  -0.08 -1.2 0  -0.08 -1.2 3  -2 -1.2 3 ] "integer indices" [ 0 1 2 0 2 3 ]
  Shape "trianglemesh" "point3 P" [ 0.08 -1.2 0  2 -1.2 0  2 -1.2 3  0.08 -1.2 3 ] "integer indices" [ 0 1 2 0 2 3 ]
  Shape "trianglemesh" "point3 P" [ -0.08 -1.2 1  0.08 -1.2 1  0.08 -1.2 3  -0.08 -1.2 3 ] "integer indices" [ 0 1 2 0 2 3 ]
AttributeEnd''')
if kind == 'glow':
    # (a ball of a medium that emits, in the fog, in the place of the sphere)
    print('''AttributeBegin
  MediumInterface "glow" "air"
  Material "interface"
  Translate 0.5 -0.5 0.9
  Shape "sphere" "float radius" 0.7
AttributeEnd''')
elif kind == 'fogball':
    # (no fog inside the glass: the paths cross between media)
    print('''AttributeBegin
  MediumInterface "" "air"
  Material "dielectric" "float eta" 1.5
  Translate 0.5 -0.5 0.8
  Shape "sphere" "float radius" 0.8
AttributeEnd''')
elif kind in ('glass', 'glassfog', 'sunglass', 'skyglass', 'mapglass'):
    print('''AttributeBegin
  Material "dielectric" "float eta" 1.5
  Translate 0.5 -0.5 0.8
  Shape "sphere" "float radius" 0.8
AttributeEnd''')
elif kind == 'glossy':
    print('''AttributeBegin
  Material "conductor" "float roughness" 0.15 "spectrum eta" "metal-Cu-eta" "spectrum k" "metal-Cu-k"
  Translate 0.5 -0.5 0.8
  Shape "sphere" "float radius" 0.8
AttributeEnd''')
else:
    print('''AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.6 0.6 0.65 ]
  Translate 0.5 -0.5 0.8
  Shape "sphere" "float radius" 0.8
AttributeEnd''')
