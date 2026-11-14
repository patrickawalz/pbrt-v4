// (not in pbrt-v4) Any number of texture coordinate sets ("UV channels") on a mesh.
//
// Channel 0 is pbrt's "uv": it is interpolated at every hit and its tangents are the dpdu and dpdv of the interaction,
// as before. The other channels are stored on the mesh and cost nothing at a hit: the interaction keeps which primitive
// it is on and where, and SurfaceInteraction.UVChannel() interpolates a channel and computes its tangents when a
// texture mapping asks for it.
//
// Scene syntax, on "trianglemesh" and "bilinearmesh" (both forms can be mixed):
//   "point2 uv1" [...]  "point2 uv2" [...]      channels by number: 1, 2, ... as long as the next one is there
//   "point2 uv:detail" [...]                    a channel with a name; it takes the next free number
// and on a texture with the "uv" mapping:
//   "integer uvchannel" 2      or      "string uvchannel" "detail"
// A mesh without the channel that a texture asks for (and every shape that is not a mesh) gives channel 0.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // UVChannelNames Definition
    // The names of the channels of all the meshes, each with a number of its own: a mapping compares numbers at a hit
    public static class UVChannelNames
    {
        public static int Id(string name)
        {
            lock (ids)
            {
                if (!ids.TryGetValue(name, out int id))
                {
                    id = ids.Count;
                    ids.Add(name, id);
                }
                return id;
            }
        }

        private static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
    }

    // UVChannels Definition
    // The channels of a mesh after the first: uv[k - 1] is channel k, one value for every vertex
    public sealed unsafe class UVChannels
    {
        public UVChannels(Point2f[][] uv, int[] nameIds)
        {
            this.uv = uv;
            this.nameIds = nameIds;
        }

        // The values of the channel with the name nameId, or without a name (nameId < 0) of the channel number
        // `channel`; null where the mesh does not have it
        public Point2f[] Find(int channel, int nameId)
        {
            if (nameId >= 0)
            {
                for (int i = 0; i < nameIds.Length; ++i)
                {
                    if (nameIds[i] == nameId)
                    {
                        return uv[i];
                    }
                }
                return null;
            }
            if (channel >= 1 && channel <= uv.Length)
            {
                return uv[channel - 1];
            }
            return null;
        }

        public int Count()
        {
            return uv.Length;
        }

        // The channels in the parameters of a mesh with nVertices vertices; null if there is none
        public static UVChannels Create(ParameterDictionary parameters, int nVertices, string shape, FileLoc loc)
        {
            List<Point2f[]> channels = new List<Point2f[]>();
            List<int> names = new List<int>();
            for (int number = 1; ; ++number)
            {
                List<Point2f> values = parameters.GetPoint2fArray("uv" + number);
                if (values.Count == 0)
                {
                    break;
                }
                if (values.Count != nVertices)
                {
                    pbrt.Error(loc, pbrt.StringPrintf("Number of \"uv%d\"s for %s must match \"P\"s. Discarding this channel and those after it.",
                                                      number, shape));
                    break;
                }
                channels.Add(values.ToArray());
                names.Add(-1);
            }
            List<string> named = new List<string>();
            foreach (ParsedParameter parameter in parameters.GetParameterVector())
            {
                if (parameter.type == "point2" && parameter.name.StartsWith("uv:") && parameter.name.Length > 3)
                {
                    named.Add(parameter.name);
                }
            }
            foreach (string name in named)
            {
                List<Point2f> values = parameters.GetPoint2fArray(name);
                if (values.Count != nVertices)
                {
                    pbrt.Error(loc, pbrt.StringPrintf("Number of \"%s\"s for %s must match \"P\"s. Discarding this channel.", name, shape));
                    continue;
                }
                channels.Add(values.ToArray());
                names.Add(UVChannelNames.Id(name.Substring(3)));
            }
            if (channels.Count == 0)
            {
                return null;
            }
            return new UVChannels(channels.ToArray(), names.ToArray());
        }

        // What a step along the tangent vector t changes of the (u, v) whose tangents are dpdu and dpdv: the least
        // squares solution of t = du dpdu + dv dpdv, as SurfaceInteraction.ComputeDifferentials() has it
        public static Vector2f TangentToUV(Vector3f dpdu, Vector3f dpdv, Vector3f t)
        {
            Float ata00 = pbrt.Dot(dpdu, dpdu), ata01 = pbrt.Dot(dpdu, dpdv), ata11 = pbrt.Dot(dpdv, dpdv);
            Float invDet = 1 / pbrt.DifferenceOfProducts(ata00, ata11, ata01, ata01);
            invDet = pbrt.IsFinite(invDet) ? invDet : 0f;
            Float atb0 = pbrt.Dot(dpdu, t), atb1 = pbrt.Dot(dpdv, t);
            Float du = pbrt.DifferenceOfProducts(ata11, atb0, ata01, atb1) * invDet;
            Float dv = pbrt.DifferenceOfProducts(ata00, atb1, ata01, atb0) * invDet;
            du = pbrt.IsFinite(du) ? pbrt.Clamp(du, -1e8f, 1e8f) : 0f;
            dv = pbrt.IsFinite(dv) ? pbrt.Clamp(dv, -1e8f, 1e8f) : 0f;
            return new Vector2f(du, dv);
        }

        private Point2f[][] uv;
        private int[] nameIds;
    }
}
