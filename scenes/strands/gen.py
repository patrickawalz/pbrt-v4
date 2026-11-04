import math, random
def f(v): return " ".join("%.5f" % x for x in v)
HEAD = '''LookAt %s  0 0.9 0.4  0 1 0
Camera "perspective" "float fov" 36
Sampler "independent" "integer pixelsamples" 64 "integer seed" 7
Integrator "volpath" "integer maxdepth" 3
Film "rgb" "integer xresolution" %d "integer yresolution" %d "string filename" "%s.exr"
WorldBegin
LightSource "infinite" "rgb L" [0.35 0.4 0.5]
LightSource "distant" "point3 from" [3 5 -2] "point3 to" [0 0 0] "rgb L" [3.2 3 2.8]
AttributeBegin
Material "diffuse" "rgb reflectance" [0.7 0.7 0.7]
Shape "trianglemesh" "point3 P" [-8 0 -8  8 0 -8  8 0 8  -8 0 8] "integer indices" [0 2 1 0 3 2]
AttributeEnd
'''
# rocap hair: a thick tapered strand, a constant tube, and a bunch of curls
def rocap():
    s = HEAD % ("0.3 3.2 -7.0", 400, 300, "hair_rocap")
    s += 'Material "diffuse" "rgb reflectance" [0.55 0.3 0.12]\n'
    # one big tapered hook (thick root, pointed tip)
    P = [(-1.6,0,0),(-1.6,1.9,0.3),(0.2,2.6,-0.2),(0.9,1.0,0.1)]
    s += 'Shape "rocap" "point3 P" [%s] "float radius" [0.28 0.2 0.1 0.0]\n' % "  ".join(f(p) for p in P)
    # a constant-width tube chain (two cubic segments) with round caps
    P2 = [(0.3,0.15,0),(0.6,0.15,0.9),(1.5,0.15,0.9),(1.8,0.15,0),(2.1,0.15,-0.9),(2.6,0.15,-0.9),(2.9,0.5,0)]
    s += 'Material "diffuse" "rgb reflectance" [0.2 0.4 0.7]\n'
    s += 'Shape "rocap" "point3 P" [%s] "float radius" 0.12\n' % "  ".join(f(p) for p in P2)
    # thin curly strands
    random.seed(3)
    s += 'Material "diffuse" "rgb reflectance" [0.8 0.7 0.3]\n'
    for i in range(24):
        x = -3.4 + 1.1 * (i % 6) * 0.35 + random.random()*0.1
        z = 0.6 + 0.35 * (i // 6)
        h = 1.0 + 0.5*random.random()
        a = random.random()*6.28
        pts = [(x,0,z),(x+0.4*math.cos(a),h*0.8,z+0.4*math.sin(a)),(x-0.4*math.cos(a),h*1.3,z-0.4*math.sin(a)),(x+0.3*math.sin(a),h*1.2,z+0.3*math.cos(a))]
        s += 'Shape "rocap" "point3 P" [%s] "float radius" [0.03 0.025 0.015 0.004]\n' % "  ".join(f(p) for p in pts)
    open("hair_rocap.pbrt","w").write(s)
# ribbons: grass blades and a twisted strip
def ribbons():
    s = HEAD % ("0 4.2 -6.0", 400, 300, "ribbons")
    random.seed(5)
    s += 'Material "diffuse" "rgb reflectance" [0.25 0.55 0.15]\n'
    for i in range(120):
        x = -2.6 + 5.2*random.random(); z = -1.2 + 3.2*random.random()
        h = 0.5 + 0.7*random.random(); a = random.random()*6.28; bend = 0.35*random.random()
        dx, dz = math.cos(a), math.sin(a)
        pts = []; nrm = []; wd = []
        n = 5
        for k in range(n):
            t = k/(n-1)
            pts.append((x+dx*bend*t*t*h, h*t, z+dz*bend*t*t*h))
            nrm.append((-dz, 0, dx))   # normal perpendicular to the lean direction: blade faces sideways
            wd.append(0.035*(1-0.9*t))
        s += 'Shape "ribbon" "point3 P" [%s] "normal N" [%s] "float width" [%s]\n' % ("  ".join(f(p) for p in pts), "  ".join(f(p) for p in nrm), " ".join("%.5f"%w for w in wd))
    # twisted strip (one half twist around a circle)
    s += 'AttributeBegin\nTranslate 0 1.5 1.2\nMaterial "diffuse" "rgb reflectance" [0.85 0.3 0.25]\n'
    pts=[]; nrm=[]; n=36
    for k in range(n+1):
        th = 2*math.pi*k/n*0.92
        c = (math.cos(th), 0.0, math.sin(th))
        pts.append((c[0]*0.9, 0.0, c[2]*0.9))
        tw = th/2   # half twist
        radial = c; up=(0,1,0)
        nm = (math.cos(tw)*up[0]+math.sin(tw)*radial[0], math.cos(tw)*up[1]+math.sin(tw)*radial[1], math.cos(tw)*up[2]+math.sin(tw)*radial[2])
        nrm.append(nm)
    s += 'Shape "ribbon" "point3 P" [%s] "normal N" [%s] "float width" 0.16\n' % ("  ".join(f(p) for p in pts), "  ".join(f(p) for p in nrm))
    s += 'AttributeEnd\n'
    open("ribbons.pbrt","w").write(s)
rocap(); ribbons()
