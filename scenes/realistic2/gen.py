# Writes the scenes that compare camera "realistic" with "realistic2": the same world, only the camera differs.
import sys

WORLD = '''
Sampler "independent" "integer pixelsamples" %(spp)d "integer seed" 1
%(integrator)s
Film "rgb" "integer xresolution" 480 "integer yresolution" 320 "string filename" "%(name)s.exr"
WorldBegin
# The small bright light, high in the left of the image
AttributeBegin
  AreaLightSource "diffuse" "blackbody L" 5500 "float power" 0 "float scale" 1500
  Translate -0.5 0.85 3.4
  Shape "sphere" "float radius" 0.08
AttributeEnd
# A dim fill so that the room reads
AttributeBegin
  AreaLightSource "diffuse" "rgb L" [ 0.5 0.55 0.65 ]
  Translate 0 2.4 2
  Shape "bilinearmesh" "point3 P" [ -1.5 0 -1.5  1.5 0 -1.5  -1.5 0 1.5  1.5 0 1.5 ]
AttributeEnd
AttributeBegin
  Material "diffuse" "rgb reflectance" [ 0.25 0.25 0.27 ]
  Shape "bilinearmesh" "point3 P" [ -4 -0.6 -1  4 -0.6 -1  -4 -0.6 6  4 -0.6 6 ]
  Material "diffuse" "rgb reflectance" [ 0.08 0.09 0.12 ]
  Shape "bilinearmesh" "point3 P" [ -4 -0.6 5  4 -0.6 5  -4 3 5  4 3 5 ]
AttributeEnd
AttributeBegin
  Material "coateddiffuse" "rgb reflectance" [ 0.6 0.15 0.1 ] "float roughness" 0.05
  Translate 0.45 -0.25 3.1
  Shape "sphere" "float radius" 0.35
AttributeEnd
AttributeBegin
  Material "conductor" "spectrum eta" "metal-Au-eta" "spectrum k" "metal-Au-k" "float roughness" 0.15
  Translate -0.35 -0.38 2.6
  Shape "sphere" "float radius" 0.22
AttributeEnd
AttributeBegin
  Material "dielectric" "float eta" 1.5
  Translate 1.05 -0.35 2.5
  Shape "sphere" "float radius" 0.25
AttributeEnd
'''

CAMERA = 'Camera "%(type)s" "string lensfile" "dgauss.50mm.dat" "float aperturediameter" 9 "float focusdistance" 3 %(extra)s\n'

VOLPATH = 'Integrator "volpath" "integer maxdepth" 5'
BDPT = 'Integrator "bdpt" "integer maxdepth" 5'
MLT = 'Integrator "mlt" "string pathestimator" "bdpt" "integer maxdepth" 5 "integer mutationsperpixel" %d'
MLTVOLPATH = 'Integrator "mlt" "string pathestimator" "volpath" "integer maxdepth" 5 "integer mutationsperpixel" %d'
ANALYSER = 'Integrator "volpath" "integer maxdepth" 5 "bool polarised" true "float analyser" '
ALL = ('"bool ghosts" true "string coating" "single" "float sensorreflectance" 0.05 '
       '"float dirt" 0.03 "float dirtg" 0.9 "float barrelalbedo" 0.05')

# name, camera, camera parameters, integrator, samples per pixel as a fraction of the command line's
VARIANTS = [
    ("flare_a_realistic", "realistic", '', VOLPATH, 1),
    ("flare_b_ghosts_uncoated", "realistic2", '"bool ghosts" true', VOLPATH, 1),
    ("flare_c_ghosts_single", "realistic2", '"bool ghosts" true "string coating" "single"', VOLPATH, 1),
    ("flare_d_ghosts_broadband", "realistic2", '"bool ghosts" true "string coating" "broadband"', VOLPATH, 1),
    ("flare_e_all", "realistic2", ALL, VOLPATH, 1),
    ("flare_f_all_bdpt", "realistic2", ALL, BDPT, 0.5),
    ("flare_g_all_mlt", "realistic2", ALL, MLT, 1),
    ("flare_h_all_mlt_volpath", "realistic2", ALL, MLTVOLPATH, 1),
    ("flare_i_ghosts_analyser_0", "realistic2", '"bool ghosts" true', ANALYSER + "0", 1),
    ("flare_j_ghosts_analyser_90", "realistic2", '"bool ghosts" true', ANALYSER + "90", 1),
]

spp = int(sys.argv[1]) if len(sys.argv) > 1 else 256
for name, kind, extra, integrator, fraction in VARIANTS:
    samples = max(1, int(spp * fraction))
    with open(name + ".pbrt", "w") as f:
        f.write('LookAt 0 0.35 0  0 0.45 3  0 1 0\n')
        f.write(CAMERA % {"type": kind, "extra": extra})
        f.write(WORLD % {"spp": samples, "name": name,
                         "integrator": integrator % samples if "%d" in integrator else integrator})
