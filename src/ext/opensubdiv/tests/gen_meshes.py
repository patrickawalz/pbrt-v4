# Generates the test meshes of the OpenSubdiv port (text format of osd_oracle.cpp)
import itertools, math, os
def write(name, scheme, verts, faces, creases=(), corners=(), holes=(), **opt):
    with open(f'meshes/{name}.txt','w') as f:
        f.write(f'scheme {scheme}\n')
        for k,v in opt.items(): f.write(f'opt {k} {v}\n')
        for v in verts: f.write('v %.9g %.9g %.9g\n'%tuple(v))
        for fc in faces: f.write('f '+' '.join(map(str,fc))+'\n')
        for a,b,w in creases: f.write(f'crease {a} {b} {w}\n')
        for a,w in corners: f.write(f'corner {a} {w}\n')
        for h in holes: f.write(f'hole {h}\n')
cube_v=[(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]
cube_f=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
# a bumpy cube so that the limit surface is not symmetric
cube_v2=[(x+0.1*math.sin(3*y+z),y+0.07*math.cos(2*x),z+0.05*x*y) for x,y,z in cube_v]
tri_f=[]
for f in cube_f: tri_f += [(f[0],f[1],f[2]),(f[0],f[2],f[3])]
# the tutorial pyramid: 4 triangles and a quad
pyr_v=[(0,0,2),(0,-2,0),(2,0,0),(0,2,0),(-2,0,0)]
pyr_f=[(0,1,2),(0,2,3),(0,3,4),(0,4,1),(4,3,2,1)]
# open grid (boundary), n x n quads
def grid(n):
    v=[(i,j,0.3*math.sin(i)*math.cos(j)) for j in range(n+1) for i in range(n+1)]
    f=[(j*(n+1)+i,j*(n+1)+i+1,(j+1)*(n+1)+i+1,(j+1)*(n+1)+i) for j in range(n) for i in range(n)]
    return v,f
gv,gf=grid(4)
# a grid with a hole in the middle
# icosahedron for loop
t=(1+5**0.5)/2
ico_v=[(-1,t,0),(1,t,0),(-1,-t,0),(1,-t,0),(0,-1,t),(0,1,t),(0,-1,-t),(0,1,-t),(t,0,-1),(t,0,1),(-t,0,-1),(-t,0,1)]
ico_f=[(0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),(3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1)]
# an open triangle fan + strip for loop boundaries
lv=[(0,0,0),(1,0,0.2),(2,0,0),(0.5,1,0.1),(1.5,1,0),(1,2,0.3),(2.5,1,0.2)]
lf=[(0,1,3),(1,4,3),(1,2,4),(3,4,5),(2,6,4),(4,6,5)]
# an n-gon mesh: a pentagonal prism and a hexagon fan
def prism(n):
    v=[(math.cos(2*math.pi*i/n),math.sin(2*math.pi*i/n),h) for h in (0,1) for i in range(n)]
    f=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)]
    return v,f
pv,pf=prism(5)
hv,hf=prism(6)
write('cube','catmark',cube_v2,cube_f,level=3)
write('cube_crease','catmark',cube_v2,cube_f,creases=[(0,1,2.5),(1,2,1.0),(4,5,10)],corners=[(6,3.0)],level=4)
write('cube_crease_chaikin','catmark',cube_v2,cube_f,creases=[(0,1,2.5),(1,5,1.5),(5,4,3)],level=4,creasing=1)
write('cube_singlecrease','catmark',cube_v2,cube_f,creases=[(0,1,2.5),(2,3,10)],level=3,singlecrease=1,infsharp=1)
write('cube_bspline_endcap','catmark',cube_v2,cube_f,level=3,endcap=2)
write('cube_legacy_gregory','catmark',cube_v2,cube_f,level=3,endcap=4)
write('cube_noshare','catmark',cube_v2,cube_f,level=3,share=0)
write('pyramid','catmark',pyr_v,pyr_f,creases=[(4,3,3),(3,2,3),(2,1,3),(1,4,3)],level=3,infsharp=1)
write('pyramid_plain','catmark',pyr_v,pyr_f,level=3)
write('grid_boundary','catmark',gv,gf,level=3)
write('grid_boundary_corner','catmark',gv,gf,level=3,boundary=2)
write('grid_boundary_none','catmark',gv,gf,level=2,boundary=0)
write('grid_hole','catmark',gv,gf,holes=[5],level=3)
write('grid_infsharp','catmark',gv,gf,creases=[(12,13,10),(13,14,10)],level=3,infsharp=1)
write('prism5','catmark',pv,pf,level=3)
write('prism6','catmark',hv,hf,level=3,creases=[(0,1,2)])
write('tri_cube','catmark',cube_v2,tri_f,level=3)
write('uniform_cube','catmark',cube_v2,cube_f,uniform=3)
write('uniform_cube_crease','catmark',cube_v2,cube_f,creases=[(0,1,2.5),(1,2,1.0)],corners=[(6,3.0)],uniform=3)
write('uniform_grid','catmark',gv,gf,uniform=2)
write('uniform_prism5','catmark',pv,pf,uniform=2)
write('uniform_pyramid_chaikin','catmark',pyr_v,pyr_f,creases=[(4,3,3),(3,2,1.5)],creasing=1,uniform=3)
write('uniform_tri_smooth','catmark',cube_v2,tri_f,uniform=2,trisub=1)
write('loop_ico','loop',ico_v,ico_f,level=3)
write('loop_open','loop',lv,lf,level=3)
write('loop_crease','loop',ico_v,ico_f,creases=[(0,1,2.5),(1,5,10)],level=3,infsharp=1,singlecrease=1)
write('loop_bspline_endcap','loop',ico_v,ico_f,level=3,endcap=2)
write('uniform_loop_ico','loop',ico_v,ico_f,uniform=3)
write('uniform_loop_open','loop',lv,lf,creases=[(0,1,2)],uniform=3)
write('bilinear_cube','bilinear',cube_v2,cube_f,uniform=2)
write('bilinear_tri','bilinear',pyr_v,pyr_f,uniform=2)
