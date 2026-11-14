// (not in pbrt-v4) Surface gradient-based bump mapping framework.
// After: Morten S. Mikkelsen, "Surface Gradient-Based Bump Mapping Framework", JCGT vol. 9, no. 3, 2020.
//
// Every bump input (tangent-space normal map, object-space normal map, height texture, volume texture, decal,
// triplanar projection) becomes a surface gradient: a vector in the tangent plane. Surface gradients are linear, so
// they are scaled and summed across layers and across sets of texture coordinates, and resolved to one normal at the end.

using System;

namespace pbrt
{
    // SurfgradFrame Definition
    // Cached per-shading-point state. This is the analytic replacement for the pixel shader prologue of Listing 9 in the
    // paper: pbrt has exact parametric derivatives, so sigmaX/sigmaY/flip_sign/GenBasisTB() are all unnecessary.
    public struct SurfgradFrame
    {
        // Base (unperturbed) unit-length normal; "n" in Eq. (5).
        public Normal3f n;
        // Parametric derivatives of the surface for UV set 0.
        public Vector3f dpdu, dpdv;
        // Orthonormal tangent basis for UV set 0; "t" and "b" of Eq. (8).
        public Vector3f t, b;
        // Rendering-space position and screen differentials (filter widths).
        public Point3f p;
        public Vector3f dpdx, dpdy;
        // det(N) = n . (dpdu x dpdv), its sign, and sign(det)/max(eps,|det|).
        public Float det, flipSign, invDet;
        // True geometric dpdu/dpdv (triangles overwrite shading.dpdv with Cross(ns, dpdu), which discards |dP/dv|); used
        // by the scale-dependent height path.
        public Vector3f gdpdu, gdpdv;
        // Lengths used to rebuild shading dpdu/dpdv after the resolve.
        public Float ulen, vlen;
    }

    // Secondary Texture Coordinate Sets
    // A set is a channel of texture coordinates of the mesh (uvchannels.cs; the first one by default) followed by an
    // affine map (u1,v1) = J * (u0,v0) + o with a constant 2x2 Jacobian J. dpdu1/dpdv1 then follow from J^-1 by the chain
    // rule, exactly, with no screen-space derivatives (cf. Listing 4).
    public struct UVSetTransform
    {
        // (u1,v1) = (j00*u0 + j01*v0 + ou, j10*u0 + j11*v0 + ov)
        public Float j00, j01, j10, j11;
        public Float ou, ov;
        // The channel of the mesh that (u0, v0) are: its number, or the id of its name (UVChannelNames) if not negative
        public int channel, channelName;

        public static UVSetTransform Identity()
        {
            UVSetTransform x = new UVSetTransform();
            x.j00 = 1;
            x.j01 = 0;
            x.j10 = 0;
            x.j11 = 1;
            x.ou = 0;
            x.ov = 0;
            x.channel = 0;
            x.channelName = -1;
            return x;
        }

        public Point2f Apply(Point2f uv)
        {
            return new Point2f(j00 * uv[0] + j01 * uv[1] + ou, j10 * uv[0] + j11 * uv[1] + ov);
        }

        public bool IsOtherChannel()
        {
            return channel != 0 || channelName >= 0;
        }

        // Scale/rotate/translate constructor equivalent to a UVMapping.
        public static UVSetTransform ScaleRotate(Float su, Float sv, Float thetaRad, Float ou, Float ov)
        {
            Float c = std.cos(thetaRad), s = std.sin(thetaRad);
            UVSetTransform x = Identity();
            x.j00 = su * c;
            x.j01 = -su * s;
            x.j10 = sv * s;
            x.j11 = sv * c;
            x.ou = ou;
            x.ov = ov;
            return x;
        }
    }

    // Compositing Modes for Two Tangent-Space Normal Maps (Section 4.2, Table 1)
    public enum TangentNormalBlend : int
    {
        PartialDerivative,  // PD: surface gradients summed unchanged
        Whiteout,           // WO
        UDN,                // UDN
        Reoriented          // RNM (Eq. 18), same unwrap only
    }

    // Bump Layer Description
    public enum BumpLayerKind
    {
        TangentNormalMap,  // tangent-space normal map, TBN style (Eq. 17)
        ShadingNormalMap,  // object/rendering-space normal map (Listing 5)
        HeightTexture,     // FloatTexture height, TBN or scale-dependent
        VolumeTexture,     // FloatTexture evaluated as a space function
        Decal,             // parallel projection of an image (Listing 15)
        Triplanar          // triplanar projection of an image (Listing 13)
    }

    public sealed class BumpLayer
    {
        public BumpLayerKind kind = BumpLayerKind.TangentNormalMap;
        // Bump intensity; negative inverts the bump map (see Section 3).
        public Float scale = 1;
        // Index into BumpStack.uvSets. 0 is the mesh's own parameterization.
        public int uvSet = 0;
        // When true, HeightTexture layers use Eq. (7) rather than the TBN basis.
        public bool scaleDependent = false;
        // When true, tangent-space normals are v-flipped before conversion.
        public bool flipVertDeriv = false;
        // How this layer combines with the tangent-normal layer immediately before it.
        public TangentNormalBlend blend = TangentNormalBlend.PartialDerivative;

        public Image image = null;
        public FloatTexture height = null;

        // Decal projector frame (rendering space) and inverse extent.
        public Vector3f axisX = new Vector3f(1, 0, 0), axisY = new Vector3f(0, 1, 0), axisZ = new Vector3f(0, 0, 1);
        public Point3f decalOrigin;
        public Vector3f decalInvExtent = new Vector3f(1, 1, 1);

        // Triplanar sampling frequency and blend sharpness.
        public Float triplanarScale = 1, triplanarSharpness = 3;
    }

    public sealed class BumpStack
    {
        public const int MaxBumpLayers = 8;
        public const int MaxUVSets = 4;

        public BumpLayer[] layers = new BumpLayer[MaxBumpLayers];
        public int nLayers = 0;
        public UVSetTransform[] uvSets = new UVSetTransform[MaxUVSets]
        {
            UVSetTransform.Identity(), UVSetTransform.Identity(), UVSetTransform.Identity(), UVSetTransform.Identity()
        };
        public int nUVSets = 1;  // uvSets[0] is identity

        public bool empty()
        {
            return nLayers == 0;
        }

        public BumpLayer Add()
        {
            BumpLayer layer = new BumpLayer();
            layers[nLayers++] = layer;
            return layer;
        }

        // The index of a set of texture coordinates, added if the stack does not have it yet (0 if it is full)
        public int UVSet(UVSetTransform x)
        {
            for (int i = 0; i < nUVSets; ++i)
            {
                if (uvSets[i].j00 == x.j00 && uvSets[i].j01 == x.j01 && uvSets[i].j10 == x.j10 && uvSets[i].j11 == x.j11 && uvSets[i].ou == x.ou &&
                    uvSets[i].ov == x.ov && uvSets[i].channel == x.channel && uvSets[i].channelName == x.channelName)
                {
                    return i;
                }
            }
            if (nUVSets == MaxUVSets)
            {
                return 0;
            }
            uvSets[nUVSets] = x;
            return nUVSets++;
        }
    }

    public static unsafe partial class pbrt
    {
        // Surfgrad Constants
        public const Float SurfgradDetEpsilon = 1.192093e-15f;
        public const Float SurfgradEpsilon = 1e-9f;

        // Builds an orthonormal tangent basis (t, b) for an arbitrary set of parametric derivatives. b carries the
        // handedness of the parameterization, matching sign(det(N)) in Eq. (8).
        public static void TangentBasisFromDerivatives(Normal3f n, Vector3f dpdu, Vector3f dpdv, Vector3f* t, Vector3f* b, Float* det)
        {
            Vector3f nv = (Vector3f)n;
            Float d = Dot(nv, Cross(dpdu, dpdv));
            Float sgn = d < 0 ? -1f : 1f;
            Vector3f tt = GramSchmidt(dpdu, nv);
            if (LengthSquared(tt) > 0)
            {
                tt = Normalize(tt);
            }
            else
            {
                Frame f = Frame.FromZ(nv);
                tt = f.x;
            }
            *t = tt;
            *b = sgn * Cross(nv, tt);
            if (det != null)
            {
                *det = d;
            }
        }

        public static SurfgradFrame MakeSurfgradFrame(Point3f p, Normal3f n, Vector3f dpdu, Vector3f dpdv, Vector3f dpdx, Vector3f dpdy)
        {
            SurfgradFrame f = new SurfgradFrame();
            f.p = p;
            f.n = Normalize(n);
            f.dpdu = dpdu;
            f.dpdv = dpdv;
            f.dpdx = dpdx;
            f.dpdy = dpdy;
            f.ulen = Length(dpdu);
            f.vlen = Length(dpdv);
            if (f.ulen == 0)
            {
                f.ulen = 1;
            }
            if (f.vlen == 0)
            {
                f.vlen = 1;
            }
            TangentBasisFromDerivatives(f.n, dpdu, dpdv, &f.t, &f.b, &f.det);
            f.flipSign = f.det < 0 ? -1f : 1f;
            f.invDet = f.flipSign / std.max<Float>(SurfgradDetEpsilon, std.abs(f.det));
            return f;
        }

        // dpdu1 = dpdu0 * du0/du1 + dpdv0 * dv0/du1, etc.: (dpdu0, dpdv0) and the screen-space differentials are those of
        // the coordinates that the transformation starts from
        public static void UVSetDerivatives(Vector3f dpdu0, Vector3f dpdv0, UVSetTransform x, Vector3f* dpdu1, Vector3f* dpdv1, Float* dudx,
                                            Float* dudy, Float* dvdx, Float* dvdy, Float dudx0, Float dudy0, Float dvdx0, Float dvdy0)
        {
            Float det = DifferenceOfProducts(x.j00, x.j11, x.j01, x.j10);
            Float invDet = (std.abs(det) < SurfgradEpsilon) ? 0 : 1 / det;
            // J^-1
            Float i00 = x.j11 * invDet, i01 = -x.j01 * invDet;
            Float i10 = -x.j10 * invDet, i11 = x.j00 * invDet;
            *dpdu1 = dpdu0 * i00 + dpdv0 * i10;
            *dpdv1 = dpdu0 * i01 + dpdv0 * i11;
            // Screen-space UV differentials for the new set (forward Jacobian).
            if (dudx != null)
            {
                *dudx = x.j00 * dudx0 + x.j01 * dvdx0;
            }
            if (dudy != null)
            {
                *dudy = x.j00 * dudy0 + x.j01 * dvdy0;
            }
            if (dvdx != null)
            {
                *dvdx = x.j10 * dudx0 + x.j11 * dvdx0;
            }
            if (dvdy != null)
            {
                *dvdy = x.j10 * dudy0 + x.j11 * dvdy0;
            }
        }

        // Tangent-Space Normal Handling
        // Listing 2: convert a tangent-space normal to a derivative (dH/du, dH/dv), clamped so the derivative stays in
        // [-128, 128] (max slope 89.55 degrees).
        public static Vector2f TangentSpaceNormalToDerivative(Vector3f m, bool flipVertDeriv)
        {
            const Float scale = 1f / 128f;
            Vector3f ma = new Vector3f(std.abs(m.x), std.abs(m.y), std.abs(m.z));
            Float zma = std.max<Float>(ma.z, scale * std.max<Float>(ma.x, ma.y));
            zma = std.max<Float>(zma, SurfgradEpsilon);
            Float s = flipVertDeriv ? -1f : 1f;
            return new Vector2f(-m.x / zma, -s * m.y / zma);
        }

        // Matches pbrt's existing NormalMap() sampling convention: (0,0) is the upper left of the image, so v is flipped
        // and no derivative flip is required.
        public static Vector3f SampleNormalMap(Image normalMap, Point2f uv)
        {
            WrapMode2D wrap = new WrapMode2D(WrapMode.Repeat);
            Point2f st = new Point2f(uv[0], 1 - uv[1]);
            return new Vector3f(2 * normalMap.BilerpChannel(st, 0, wrap) - 1, 2 * normalMap.BilerpChannel(st, 1, wrap) - 1,
                                2 * normalMap.BilerpChannel(st, 2, wrap) - 1);
        }

        // Surface Gradient Producers
        // Eq. (17) / Listing 3: scale-independent, TBN style. This is the formulation that reproduces pbrt's NormalMap().
        public static Vector3f SurfgradFromTBN(Vector2f deriv, Vector3f t, Vector3f b)
        {
            return deriv.x * t + deriv.y * b;
        }

        // Eq. (7): scale-dependent (displacement-style) surface gradient, evaluated analytically from the parametric
        // derivatives. This is the exact analogue of Listing 6 without any screen-space chain rule, and it reproduces
        // pbrt's BumpMap() up to the H*dndu terms that Blinn's approximation drops.
        public static Vector3f SurfgradFromParametricDeriv(Vector2f dHduv, SurfgradFrame f)
        {
            Vector3f nv = (Vector3f)f.n;
            Vector3f vR1 = Cross(f.dpdv, nv);
            Vector3f vR2 = Cross(nv, f.dpdu);
            return f.invDet * (dHduv.x * vR1 + dHduv.y * vR2);
        }

        // Eq. (6) / Listing 8: project a volume (space function) gradient onto the tangent plane.
        public static Vector3f SurfgradFromVolumeGradient(Vector3f grad, SurfgradFrame f)
        {
            Vector3f nv = (Vector3f)f.n;
            return grad - Dot(nv, grad) * nv;
        }

        // Listing 5: convert an already-perturbed normal (object- or rendering-space normal map) into a surface gradient
        // so it can be scaled and composited.
        public static Vector3f SurfgradFromPerturbedNormal(Vector3f v, SurfgradFrame f)
        {
            Vector3f nv = (Vector3f)f.n;
            Float k = Dot(nv, v);
            return (k * nv - v) / std.max<Float>(SurfgradEpsilon, std.abs(k));
        }

        // Listing 15: decal projector treated as a volume bump map.
        public static Vector3f DecalVolumeGradient(Vector2f deriv, Vector3f axisX, Vector3f axisY)
        {
            return deriv.x * axisX + deriv.y * axisY;
        }

        // Listing 14.
        public static Vector3f TriplanarWeights(Normal3f n, Float k)
        {
            Vector3f w = new Vector3f(std.abs(n.x) - 0.2f, std.abs(n.y) - 0.2f, std.abs(n.z) - 0.2f);
            w = new Vector3f(std.max<Float>(0, w.x), std.max<Float>(0, w.y), std.max<Float>(0, w.z));
            w = new Vector3f(std.pow(w.x, k), std.pow(w.y, k), std.pow(w.z, k));
            Float sum = w.x + w.y + w.z;
            return sum > 0 ? w / sum : new Vector3f(0, 0, 1);
        }

        // Listing 13. Derivatives are for images sampled with (z,y), (x,z) and (x,y).
        public static Vector3f TriplanarVolumeGradient(Vector3f w, Vector2f derivX, Vector2f derivY, Vector2f derivZ)
        {
            return new Vector3f(w.z * derivZ.x + w.y * derivY.x, w.z * derivZ.y + w.x * derivX.y, w.x * derivX.x + w.y * derivY.y);
        }

        public static void BlendTangentNormals(TangentNormalBlend mode, Vector3f a, Vector3f b, Vector3f* outA, Vector3f* outB)
        {
            switch (mode)
            {
                case TangentNormalBlend.Whiteout:
                    *outA = new Vector3f(a.x, a.y, a.z * b.z);
                    *outB = new Vector3f(b.x, b.y, a.z * b.z);
                    break;
                case TangentNormalBlend.UDN:
                    *outA = a;
                    *outB = new Vector3f(b.x, b.y, a.z);
                    break;
                default:
                    *outA = a;
                    *outB = b;
                    break;
            }
        }

        // Eq. (18).
        public static Vector3f ReorientedNormalMapping(Vector3f a, Vector3f b)
        {
            a = Normalize(a);
            b = Normalize(b);
            Vector3f t = new Vector3f(a.x, a.y, a.z + 1);
            Vector3f u = new Vector3f(-b.x, -b.y, b.z);
            Float d = std.max<Float>(SurfgradEpsilon, a.z + 1);
            Vector3f m = t * (Dot(t, u) / d) - u;
            return LengthSquared(m) > 0 ? Normalize(m) : new Vector3f(0, 0, 1);
        }

        // Resolve
        // Listing 1: n' = normalize(n - surfgrad).
        public static Normal3f ResolveNormalFromSurfaceGradient(Vector3f surfGrad, SurfgradFrame f)
        {
            Vector3f nv = (Vector3f)f.n - surfGrad;
            if (LengthSquared(nv) == 0)
            {
                return f.n;
            }
            return (Normal3f)Normalize(nv);
        }

        // pbrt's shading pipeline consumes (dpdu, dpdv), not a normal, so rebuild an orthogonal pair that regenerates ns
        // exactly: Cross(dpdus, dpdvs) is parallel to ns by construction. Lengths are preserved so anisotropic BSDFs keep
        // their frame.
        public static void ResolveShadingGeometry(Normal3f ns, SurfgradFrame f, Vector3f* dpdus, Vector3f* dpdvs)
        {
            Vector3f nsv = (Vector3f)ns;
            Vector3f t = GramSchmidt(f.dpdu, nsv);
            if (LengthSquared(t) == 0)
            {
                Frame fr = Frame.FromZ(nsv);
                t = fr.x;
            }
            *dpdus = Normalize(t) * f.ulen;
            *dpdvs = Normalize(Cross(nsv, *dpdus)) * f.vlen;
        }

        // Height Derivative Helpers
        // (not as in the C++ code, which takes forward differences) The change of a height along the tangents of a set of
        // texture coordinates, from the texture: in closed form where it has one (base/texture.cs)
        public static Vector2f HeightDerivativeUV(FloatTexture height, TextureEvalContext ctx, Vector3f dpdu, Vector3f dpdv)
        {
            Float dHdu, dHdv;
            height.EvaluateDerivatives(ctx, new TextureDirections(dpdu, dpdv), &dHdu, &dHdv);
            return new Vector2f(dHdu, dHdv);
        }

        // A FloatTexture treated as a space function. Only the position is varied, so this is a true volume gradient
        // (Section 3.6) and not the surface-restricted derivative.
        public static Vector3f VolumeGradientFromTexture(FloatTexture height, TextureEvalContext ctx)
        {
            return height.Gradient(ctx);
        }
    }
}
