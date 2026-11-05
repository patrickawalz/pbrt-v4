# Displacement: diced and shell-mapped

Magi has two kinds of geometric displacement. Which one a shape gets depends on the shape, not on a global switch.

| Shapes | Displacement | How |
| --- | --- | --- |
| sphere, cylinder, disk, cone, paraboloid, hyperboloid, torus, patch, patchmesh (bilinear and bicubic), bilinearmesh, nupatch (NURBS, trimmed), catmarksubdiv, subdivisionmesh, curvedpatches, teapot | **diced** | the parameter domain is diced into micropolygons, displaced along the normal, intersected as triangles |
| trianglemesh, plymesh | **tessellated** (default) or **shell mapped** | `"string displacementmode"` |
| displacedmesh | shell mapped | tessellation free (Ogaki), unchanged |
| curves, points, blobby, volume, sdf, ribbon, rocap, procedural, loopsubdiv, polygon shapes | none | a given `"texture displacement"` is ignored with a warning |

A shape without a displacement never reaches the dicing code: `Shape.Create` creates it exactly as before (same classes, same intersection routines).

## Diced displacement

```
Texture "bumps" "float" "fbm" "integer octaves" 6
Shape "sphere" "float radius" 1
      "texture displacement" "bumps"
      "float displacementscale" 0.1   # default 1
      "float displacementoffset" 0    # default 0
      "float edgelength" 0.02         # default 1, as for plymesh
      "float displacementbound" 0.2   # optional promise, checked
```

* The displacement is `displacementscale * texture + displacementoffset`, along the unit normal of the shape. The texture is evaluated at the object space point of the undisplaced surface and at the `uv` of the shape. As in RenderMan the normal is the oriented one: `ReverseOrientation` flips the direction of the displacement as well as the shading normals.
* `edgelength` is the length of an edge of a micropolygon **in render space** (so a non-uniform transform is accounted for), multiplied by the `displacementEdgeScale` option, as for `plymesh`. `dicemaxrate` (default 1024) caps the number of segments of one edge, `dicemaxquads` (default 8 000 000) caps the micropolygons of a shape: the edge length grows to fit, with a warning.
* `displacementbound` is not needed for the bounds of the BVH (the triangles carry their exact bounds); it is a promise that the dicer checks, and it warns about the vertices that exceed it.
* `dicecreaseangle` (degrees, default 80): the normals of two triangles that meet at a vertex are averaged unless the angle between them is larger.
* The material `displacement` keeps its meaning (bump mapping at shading time). Using the same texture for both would displace twice; the shape parameter is explicit on purpose.
* A shape cannot have both `"texture displacement"` and the tessellation-free `"string displacement"` of the curved patches.

### Crack free

Every patch (a quadric is one patch; a NURBS surface has one per Bezier cell; a subdivision surface or a patch mesh one per bicubic patch) is diced with a rate per **edge**, computed from the render space length of that edge alone. The interior is a regular grid; a ring of triangles (a zipper) joins it to the border, so the border of a patch has exactly the number of segments of its edge. Two patches that share an edge therefore share its vertices. The vertices of all patches are then welded by position (tolerance 2e-7 of the size), a welded vertex is displaced once, on the mean normal of the patches that meet there, with the texture evaluated at the first of them. The mesh is watertight, also across torus and sphere seams, `phimax` boundaries and patch seams where the base normals are only C0.

### Normals and uv

Normals are recomputed from the displaced triangles (angle weighted, split at creases and where the `uv` differ). `uv` are those of the shape; `dpdu`/`dpdv` are derived by the triangle from them. The triangles are those of pbrt-v4, so area, sampling and area lights are the triangle ones, exact for the displaced mesh.

### Per shape

* Quadrics: the grid boundary is the exact partial boundary (`zmin`, `zmax`, `phimax`, `innerradius`, `thetamax`, `phimin`). Poles and apexes collapse to one vertex.
* Bicubic patches, the teapot, subdivision surfaces (limit patches including Gregory ones, with their limit normals), curved patches: one patch per Bezier patch. Ptex face indices are kept.
* Bilinear patches (`patchmesh` with `"type" "bilinear"`, `bilinearmesh`): diced as the bilinear surface, which is not planar in general; this is why `bilinearmesh` is diced although it is a quad mesh.
* NURBS: dice the whole domain per Bezier cell and clip the micropolygons by the trim winding test. A micropolygon with a mix of visible and invisible corners is cut (marching triangles); the new vertex is found by bisection on the edge, to 1e-14 in the parameter, so the visible boundary vertices lie on the trim curve and the error of the boundary is the chord error of the micropolygon. A trim feature smaller than a micropolygon that crosses no edge of the grid is missed; lower `edgelength` to resolve it.
* Polygons are triangles; a polygon with a displacement is ignored with a warning (use `trianglemesh` or `plymesh`).

## Meshes: tessellated or shell mapped

```
Shape "plymesh" "string filename" "bunny.ply" "texture displacement" "bumps"
      "string displacementmode" "tessellate"   # default: edges are split to "edgelength", the vertices displaced
Shape "plymesh" ... "string displacementmode" "shell"   # the same as Shape "displacedmesh": no tessellation
```

`tessellate` is the existing behavior of `plymesh` (and now also of `trianglemesh`, which needs `uv`). `shell` maps to `Shape "displacedmesh"`; it samples the texture on the `uv` square, so it supports textures of `uv` only, and `displacementscale` and `displacementoffset` are its `scale` and `offset`. Shell mapping is not available for the curved shapes.
