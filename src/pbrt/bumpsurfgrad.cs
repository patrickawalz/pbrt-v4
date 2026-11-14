// (not in pbrt-v4) Surface gradient bump stack evaluation: the replacement of NormalMap()/BumpMap() in materials.cs for the
// materials that ask for it ("string normalmapframework" "surfgrad").

using System;

namespace pbrt
{
    // Per-UV-set cached data
    public struct SurfgradUVSet
    {
        public Point2f uv;
        public Vector3f dpdu, dpdv, t, b;
        public Float dudx, dudy, dvdx, dvdy;
    }

    public static unsafe partial class pbrt
    {
        public static Point2f RepeatUV(Point2f uv)
        {
            Float u = uv[0] - std.floor(uv[0]);
            Float v = uv[1] - std.floor(uv[1]);
            return new Point2f(u, v);
        }

        // Builds the frame + all UV sets from the context. This replaces the pixel shader prologue of Listing 9 entirely;
        // every quantity is analytic.
        public static SurfgradFrame InitSurfgrad(NormalBumpEvalContext ctx, BumpStack stack, SurfgradUVSet* sets)
        {
            // NOTE: shading.dpdu/dpdv are used here so that the TBN path reproduces pbrt's existing NormalMap() exactly.
            // The scale-dependent path takes the geometric dpdu/dpdv (gdpdu, gdpdv): for triangles with shading normals
            // pbrt rewrites dpdv as Cross(ns, dpdu), which discards the true |dP/dv|.
            SurfgradFrame f = MakeSurfgradFrame(ctx.p, ctx.shading.n, ctx.shading.dpdu, ctx.shading.dpdv, ctx.dpdx, ctx.dpdy);

            f.gdpdu = ctx.gdpdu;
            f.gdpdv = ctx.gdpdv;
            sets[0].uv = ctx.uv;
            sets[0].dpdu = f.dpdu;
            sets[0].dpdv = f.dpdv;
            sets[0].t = f.t;
            sets[0].b = f.b;
            sets[0].dudx = ctx.dudx;
            sets[0].dudy = ctx.dudy;
            sets[0].dvdx = ctx.dvdx;
            sets[0].dvdy = ctx.dvdy;

            for (int i = 1; i < stack.nUVSets && i < BumpStack.MaxUVSets; ++i)
            {
                UVSetTransform x = stack.uvSets[i];
                // The coordinates that the transformation starts from: the first ones, or another channel of the mesh
                // with its own tangents
                Point2f uv0 = ctx.uv;
                Vector3f dpdu0 = f.dpdu, dpdv0 = f.dpdv;
                Float dudx0 = ctx.dudx, dudy0 = ctx.dudy, dvdx0 = ctx.dvdx, dvdy0 = ctx.dvdy;
                if (x.IsOtherChannel() && ctx.si != null)
                {
                    uv0 = ctx.si.UVChannel(x.channel, x.channelName, &dpdu0, &dpdv0);
                    Vector2f duvdx = UVChannels.TangentToUV(dpdu0, dpdv0, ctx.dpdx);
                    Vector2f duvdy = UVChannels.TangentToUV(dpdu0, dpdv0, ctx.dpdy);
                    dudx0 = duvdx.x;
                    dvdx0 = duvdx.y;
                    dudy0 = duvdy.x;
                    dvdy0 = duvdy.y;
                }
                sets[i].uv = x.Apply(uv0);
                UVSetDerivatives(dpdu0, dpdv0, x, &sets[i].dpdu, &sets[i].dpdv, &sets[i].dudx, &sets[i].dudy, &sets[i].dvdx, &sets[i].dvdy, dudx0,
                                 dudy0, dvdx0, dvdy0);
                TangentBasisFromDerivatives(f.n, sets[i].dpdu, sets[i].dpdv, &sets[i].t, &sets[i].b, null);
            }
            return f;
        }

        // Accumulation
        // Note that volume gradients are accumulated separately and projected onto the tangent plane once at the end: the
        // volume gradient is a linear operator too, so this saves one projection per volumetric layer (Section 4.5).
        public static Vector3f AccumulateSurfaceGradient(BumpStack stack, SurfgradFrame frame, SurfgradUVSet* sets, TextureEvalContext baseCtx)
        {
            Vector3f surfGrad = new Vector3f(0, 0, 0);
            Vector3f volGrad = new Vector3f(0, 0, 0);

            for (int i = 0; i < stack.nLayers && i < BumpStack.MaxBumpLayers; ++i)
            {
                BumpLayer layer = stack.layers[i];
                SurfgradUVSet set = sets[Clamp(layer.uvSet, 0, BumpStack.MaxUVSets - 1)];

                // Paired tangent-normal compositing (Section 4.2): a normal-map layer followed by a normal-map layer with a
                // non-PD blend is composited as a pair. WO / UDN convert each rewritten normal in its own tangent basis
                // (separate unwraps are fine); RNM collapses the pair to one normal and therefore uses the first layer's
                // unwrap.
                if (layer.kind == BumpLayerKind.TangentNormalMap && layer.image != null && i + 1 < stack.nLayers &&
                    stack.layers[i + 1].kind == BumpLayerKind.TangentNormalMap && stack.layers[i + 1].image != null &&
                    stack.layers[i + 1].blend != TangentNormalBlend.PartialDerivative)
                {
                    BumpLayer next = stack.layers[i + 1];
                    SurfgradUVSet setB = sets[Clamp(next.uvSet, 0, BumpStack.MaxUVSets - 1)];
                    Vector3f a = SampleNormalMap(layer.image, set.uv);
                    Vector3f b = SampleNormalMap(next.image, setB.uv);
                    if (next.blend == TangentNormalBlend.Reoriented)
                    {
                        Vector3f m = ReorientedNormalMapping(a, b);
                        Vector2f d = TangentSpaceNormalToDerivative(m, layer.flipVertDeriv);
                        surfGrad += layer.scale * SurfgradFromTBN(d, set.t, set.b);
                    }
                    else
                    {
                        Vector3f a2, b2;
                        BlendTangentNormals(next.blend, a, b, &a2, &b2);
                        Vector2f da = TangentSpaceNormalToDerivative(a2, layer.flipVertDeriv);
                        Vector2f db = TangentSpaceNormalToDerivative(b2, next.flipVertDeriv);
                        surfGrad += layer.scale * SurfgradFromTBN(da, set.t, set.b);
                        surfGrad += next.scale * SurfgradFromTBN(db, setB.t, setB.b);
                    }
                    ++i;
                    continue;
                }

                TextureEvalContext ctx = baseCtx;
                ctx.uv = set.uv;
                ctx.dudx = set.dudx;
                ctx.dudy = set.dudy;
                ctx.dvdx = set.dvdx;
                ctx.dvdy = set.dvdy;

                switch (layer.kind)
                {
                    case BumpLayerKind.TangentNormalMap:
                    {
                        if (layer.image == null)
                        {
                            break;
                        }
                        Vector3f m = SampleNormalMap(layer.image, set.uv);
                        Vector2f d = TangentSpaceNormalToDerivative(m, layer.flipVertDeriv);
                        surfGrad += layer.scale * SurfgradFromTBN(d, set.t, set.b);
                        break;
                    }
                    case BumpLayerKind.ShadingNormalMap:
                    {
                        if (layer.image == null)
                        {
                            break;
                        }
                        Vector3f m = SampleNormalMap(layer.image, set.uv);
                        // Rotate from the map's frame (identity for rendering-space maps) into rendering space.
                        Vector3f v = m.x * layer.axisX + m.y * layer.axisY + m.z * layer.axisZ;
                        surfGrad += layer.scale * SurfgradFromPerturbedNormal(v, frame);
                        break;
                    }
                    case BumpLayerKind.HeightTexture:
                    {
                        if (layer.height == null)
                        {
                            break;
                        }
                        if (layer.scaleDependent)
                        {
                            SurfgradFrame lf = frame;
                            // Use the true geometric derivatives for the base unwrap (triangles with shading normals
                            // overwrite shading.dpdv, losing |dP/dv|).
                            lf.dpdu = (layer.uvSet == 0 && LengthSquared(frame.gdpdu) > 0) ? frame.gdpdu : set.dpdu;
                            lf.dpdv = (layer.uvSet == 0 && LengthSquared(frame.gdpdv) > 0) ? frame.gdpdv : set.dpdv;
                            Vector3f nv = (Vector3f)frame.n;
                            lf.det = Dot(nv, Cross(lf.dpdu, lf.dpdv));
                            lf.flipSign = lf.det < 0 ? -1f : 1f;
                            lf.invDet = lf.flipSign / std.max<Float>(SurfgradDetEpsilon, std.abs(lf.det));
                            // (the change of the height along the derivatives that the gradient is built from)
                            Vector2f d = HeightDerivativeUV(layer.height, ctx, lf.dpdu, lf.dpdv);
                            surfGrad += layer.scale * SurfgradFromParametricDeriv(d, lf);
                        }
                        else
                        {
                            Vector2f d = HeightDerivativeUV(layer.height, ctx, set.dpdu, set.dpdv);
                            surfGrad += layer.scale * SurfgradFromTBN(d, set.t, set.b);
                        }
                        break;
                    }
                    case BumpLayerKind.VolumeTexture:
                    {
                        if (layer.height == null)
                        {
                            break;
                        }
                        volGrad += layer.scale * VolumeGradientFromTexture(layer.height, ctx);
                        break;
                    }
                    case BumpLayerKind.Decal:
                    {
                        if (layer.image == null)
                        {
                            break;
                        }
                        Vector3f q = frame.p - layer.decalOrigin;
                        Float dx = Dot(q, layer.axisX) * layer.decalInvExtent.x;
                        Float dy = Dot(q, layer.axisY) * layer.decalInvExtent.y;
                        Float dz = Dot(q, layer.axisZ) * layer.decalInvExtent.z;
                        if (dx < -1 || dx > 1 || dy < -1 || dy > 1 || dz < -1 || dz > 1)
                        {
                            break;
                        }
                        Point2f uv = new Point2f(0.5f * (dx + 1), 0.5f * (dy + 1));
                        Vector3f m = SampleNormalMap(layer.image, uv);
                        Vector2f d = TangentSpaceNormalToDerivative(m, layer.flipVertDeriv);
                        // Spatial falloff modulates the volume gradient (footnote 2).
                        Float falloff = 1 - Sqr(dz);
                        volGrad += (layer.scale * falloff) * DecalVolumeGradient(d, layer.axisX * layer.decalInvExtent.x,
                                                                                  layer.axisY * layer.decalInvExtent.y);
                        break;
                    }
                    case BumpLayerKind.Triplanar:
                    {
                        if (layer.image == null)
                        {
                            break;
                        }
                        Float s = layer.triplanarScale;
                        Point3f p = frame.p;
                        Vector3f m0 = SampleNormalMap(layer.image, RepeatUV(new Point2f(s * p.z, s * p.y)));
                        Vector3f m1 = SampleNormalMap(layer.image, RepeatUV(new Point2f(s * p.x, s * p.z)));
                        Vector3f m2 = SampleNormalMap(layer.image, RepeatUV(new Point2f(s * p.x, s * p.y)));
                        Vector2f dX = s * TangentSpaceNormalToDerivative(m0, layer.flipVertDeriv);
                        Vector2f dY = s * TangentSpaceNormalToDerivative(m1, layer.flipVertDeriv);
                        Vector2f dZ = s * TangentSpaceNormalToDerivative(m2, layer.flipVertDeriv);
                        Vector3f w = TriplanarWeights(frame.n, layer.triplanarSharpness);
                        volGrad += layer.scale * TriplanarVolumeGradient(w, dX, dY, dZ);
                        break;
                    }
                }
            }

            if (volGrad != new Vector3f(0, 0, 0))
            {
                surfGrad += SurfgradFromVolumeGradient(volGrad, frame);
            }
            return surfGrad;
        }

        // Drop-in Replacement for BumpMap()/NormalMap()
        public static void SurfgradBumpMap(BumpStack stack, NormalBumpEvalContext ctx, Vector3f* dpdus, Vector3f* dpdvs)
        {
            SurfgradUVSet* sets = stackalloc SurfgradUVSet[BumpStack.MaxUVSets];
            SurfgradFrame frame = InitSurfgrad(ctx, stack, sets);
            Vector3f surfGrad = AccumulateSurfaceGradient(stack, frame, sets, (TextureEvalContext)ctx);
            Normal3f ns = ResolveNormalFromSurfaceGradient(surfGrad, frame);
            ResolveShadingGeometry(ns, frame, dpdus, dpdvs);
        }

        // Evaluate the whole stack to a surface gradient (no resolve).
        public static Vector3f SurfgradEvaluate(BumpStack stack, NormalBumpEvalContext ctx, SurfgradFrame* frameOut)
        {
            SurfgradUVSet* sets = stackalloc SurfgradUVSet[BumpStack.MaxUVSets];
            SurfgradFrame frame = InitSurfgrad(ctx, stack, sets);
            if (frameOut != null)
            {
                *frameOut = frame;
            }
            return AccumulateSurfaceGradient(stack, frame, sets, (TextureEvalContext)ctx);
        }
    }
}
