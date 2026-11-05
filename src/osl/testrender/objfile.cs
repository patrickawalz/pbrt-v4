// OSL port: the part of rapidobj (testrender/rapidobj) that Scene::add_model uses
//
// A Wavefront OBJ reader for triangle models: vertex positions, normals and texture coordinates, the faces of each group or object as a shape (polygons
// are fanned into triangles), the "usemtl" material of each face, and the material names of the "mtllib" file next to the model.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OIIO;

namespace OSL
{
    public struct ObjIndex
    {
        public int position, texcoord, normal;
    }

    public sealed class ObjShape
    {
        public string name = "";
        public List<ObjIndex> indices = new List<ObjIndex>();  // three per triangle
        public List<int> material_ids = new List<int>();  // one per triangle (empty when the shape has no materials)
    }

    public sealed class ObjFile
    {
        public List<float> positions = new List<float>();
        public List<float> normals = new List<float>();
        public List<float> texcoords = new List<float>();
        public List<string> materials = new List<string>();
        public List<ObjShape> shapes = new List<ObjShape>();

        private static float parse_float(string s)
        {
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int resolve(string s, int count)
        {
            if (s.Length == 0)
            {
                return -1;
            }
            int i = int.Parse(s, CultureInfo.InvariantCulture);
            return i < 0 ? count + i : i - 1;
        }

        public static ObjFile parse(string filename, ErrorHandler errhandler)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(filename);
            }
            catch (Exception e)
            {
                errhandler.errorfmt("Error while reading {} - {}", filename, e.Message);
                return null;
            }
            ObjFile obj = new ObjFile();
            ObjShape shape = null;
            string pending_name = "";
            int current_material = -1;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                string[] tok = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (tok[0])
                {
                    case "v":
                        obj.positions.Add(parse_float(tok[1]));
                        obj.positions.Add(parse_float(tok[2]));
                        obj.positions.Add(parse_float(tok[3]));
                        break;
                    case "vn":
                        obj.normals.Add(parse_float(tok[1]));
                        obj.normals.Add(parse_float(tok[2]));
                        obj.normals.Add(parse_float(tok[3]));
                        break;
                    case "vt":
                        obj.texcoords.Add(parse_float(tok[1]));
                        obj.texcoords.Add(tok.Length > 2 ? parse_float(tok[2]) : 0.0f);
                        break;
                    case "g":
                    case "o":
                        pending_name = line.Substring(tok[0].Length).Trim();
                        shape = null;
                        break;
                    case "mtllib":
                        {
                            string mtlpath = Path.Combine(Path.GetDirectoryName(filename), line.Substring(tok[0].Length).Trim());
                            if (File.Exists(mtlpath))
                            {
                                foreach (string mline in File.ReadAllLines(mtlpath))
                                {
                                    string m = mline.Trim();
                                    if (m.StartsWith("newmtl "))
                                    {
                                        obj.materials.Add(m.Substring(7).Trim());
                                    }
                                }
                            }
                        }
                        break;
                    case "usemtl":
                        current_material = obj.materials.IndexOf(line.Substring(tok[0].Length).Trim());
                        break;
                    case "f":
                        {
                            if (shape == null)
                            {
                                shape = new ObjShape();
                                shape.name = pending_name;
                                obj.shapes.Add(shape);
                            }
                            int nv = tok.Length - 1;
                            ObjIndex[] face = new ObjIndex[nv];
                            for (int i = 0; i < nv; i++)
                            {
                                string[] parts = tok[i + 1].Split('/');
                                face[i].position = resolve(parts[0], obj.positions.Count / 3);
                                face[i].texcoord = parts.Length > 1 ? resolve(parts[1], obj.texcoords.Count / 2) : -1;
                                face[i].normal = parts.Length > 2 ? resolve(parts[2], obj.normals.Count / 3) : -1;
                            }
                            for (int i = 1; i + 1 < nv; i++)
                            {
                                shape.indices.Add(face[0]);
                                shape.indices.Add(face[i]);
                                shape.indices.Add(face[i + 1]);
                                if (current_material >= 0)
                                {
                                    shape.material_ids.Add(current_material);
                                }
                            }
                        }
                        break;
                }
            }
            return obj;
        }
    }
}
