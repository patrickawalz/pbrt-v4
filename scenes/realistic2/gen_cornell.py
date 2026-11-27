# Writes the Cornell box scenes of camera "realistic2": three cameras, each with five integrators. The world is
# cornell_flare_world.pbrt.
CAMERAS = [
    ("plain",
     'Cornell box through the 50 mm double Gauss lens, wide open, with none of the stray light: what camera "realistic" gives',
     '"float aperturediameter" 17.1', 100),
    ("wide",
     'Cornell box through the 50 mm double Gauss lens, wide open and without coatings: the ghosts of the bulb, the veiling\n'
     '# glare, the vignetting and the discs of the lights out of focus',
     '"float aperturediameter" 17.1\n'
     '    "bool ghosts" true "float sensorreflectance" 0.03\n'
     '    "float dirt" 0.02 "float dirtg" 0.9 "float barrelalbedo" 0.04', 100),
    ("stopped",
     'Cornell box through the 50 mm double Gauss lens with a single coating, stopped down to a pentagon of 3 mm: the star of\n'
     '# diffraction around the bulb, pentagonal ghosts and discs, and no vignetting',
     '"float aperturediameters" [ 3 ] "string apertures" [ "pentagon" ] "string diffraction" "aperture"\n'
     '    "bool ghosts" true "string coating" "single" "float sensorreflectance" 0.03\n'
     '    "float dirt" 0.02 "float dirtg" 0.9 "float barrelalbedo" 0.04', 3200),
]

INTEGRATORS = [
    ("mlt-volpath", 'Integrator "mlt" "string pathestimator" "volpath" "integer maxdepth" 8 "integer mutationsperpixel" 512'),
    ("mlt-bdpt", 'Integrator "mlt" "string pathestimator" "bdpt" "integer maxdepth" 8 "integer mutationsperpixel" 512'),
    ("jrlt-volpath", 'Integrator "jrlt" "string pathestimator" "volpath" "integer maxdepth" 8 "integer mutationsperpixel" 512'),
    ("jrlt-bdpt", 'Integrator "jrlt" "string pathestimator" "bdpt" "integer maxdepth" 8 "integer mutationsperpixel" 512'),
    ("bdpt", 'Integrator "bdpt" "integer maxdepth" 8'),
]

for camera, comment, parameters, iso in CAMERAS:
    for integrator, line in INTEGRATORS:
        name = "cornell_flare_%s-%s" % (camera, integrator)
        with open(name + ".pbrt", "w") as f:
            f.write("# %s\n" % comment)
            f.write("LookAt 0 0.275 -1.45  0 0.275 0  0 1 0\n")
            f.write('Camera "realistic2" "string lensfile" "dgauss.50mm.dat" "float focusdistance" 1.4\n    %s\n' % parameters)
            f.write('Sampler "independent" "integer pixelsamples" 256 "integer seed" 1\n')
            f.write(line + "\n")
            f.write('Film "rgb" "integer xresolution" 512 "integer yresolution" 512 "float iso" %d "string filename" "%s.exr"\n'
                    % (iso, name))
            f.write("WorldBegin\n")
            f.write('Include "cornell_flare_world.pbrt"\n')
