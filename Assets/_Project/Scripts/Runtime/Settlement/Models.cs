using System.Collections.Generic;
using UnityEngine;

namespace EpochsUnbound.Settlement
{
    /// <summary>
    /// Procedural low-poly models for buildings, citizens and trees. Every part is coloured by pointing its UVs at one
    /// texel of an 8x4 palette texture, so a model is a single mesh with a single material (instancing-friendly).
    /// Models are built at real size with their base at y = 0.
    /// </summary>
    // ponytail: placeholder art generated in code; swap for imported models (same footprints) when an art pass happens.
    public static class Models
    {
        public const int Wall = 0, Roof = 1, Wood = 2, Stone = 3, Glass = 4, Crop = 5, Wheat = 6, Soil = 7,
                         Skin = 8, Hair = 9, Trousers = 10, ShirtRed = 11, ShirtBlue = 12, ShirtGreen = 13, ShirtOchre = 14, Cloth = 15,
                         Pine = 16, PineDark = 17, Leaf = 18, LeafDark = 19, Bark = 20;

        public static readonly int[] Shirts = { ShirtRed, ShirtBlue, ShirtGreen, ShirtOchre };

        static readonly Color32[] PaletteColours =
        {
            new(222, 205, 170, 255), new(150, 62, 44, 255), new(104, 70, 42, 255), new(140, 138, 132, 255),
            new(120, 170, 205, 255), new(88, 150, 60, 255), new(214, 180, 82, 255), new(112, 82, 52, 255),
            new(232, 186, 150, 255), new(58, 40, 30, 255), new(52, 60, 92, 255), new(190, 56, 48, 255),
            new(56, 98, 178, 255), new(62, 140, 74, 255), new(204, 146, 46, 255), new(236, 236, 230, 255),
            new(38, 84, 46, 255), new(28, 64, 38, 255), new(74, 122, 44, 255), new(52, 98, 36, 255),
            new(84, 60, 40, 255), new(0, 0, 0, 255), new(0, 0, 0, 255), new(0, 0, 0, 255),
            new(0, 0, 0, 255), new(0, 0, 0, 255), new(0, 0, 0, 255), new(0, 0, 0, 255),
            new(0, 0, 0, 255), new(0, 0, 0, 255), new(0, 0, 0, 255), new(0, 0, 0, 255),
        };

        public static Texture2D CreatePalette()
        {
            var tex = new Texture2D(8, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Palette" };
            tex.SetPixels32(PaletteColours);
            tex.Apply(false, true);
            return tex;
        }

        static Vector2 Uv(int colour) => new((colour % 8 + 0.5f) / 8f, (colour / 8 + 0.5f) / 4f);

        // ---------- buildings ----------

        public static Mesh Building(BuildingDef def)
        {
            var b = new Builder();
            float w = def.Size.x, d = def.Size.z, h = def.Size.y;
            switch (def.Kind)
            {
                case BuildingKind.House:
                    b.Box(new Vector3(0, 1.3f, 0), new Vector3(w * 0.8f, 2.6f, d * 0.7f), Wall);
                    b.Gable(new Vector3(0, 2.6f, 0), w * 0.92f, d * 0.86f, h - 2.6f, Roof, Wall);
                    b.Box(new Vector3(0, 0.95f, -d * 0.35f), new Vector3(0.9f, 1.9f, 0.1f), Wood);           // door
                    b.Box(new Vector3(-w * 0.25f, 1.6f, -d * 0.35f), new Vector3(0.8f, 0.7f, 0.1f), Glass);  // windows
                    b.Box(new Vector3(w * 0.25f, 1.6f, -d * 0.35f), new Vector3(0.8f, 0.7f, 0.1f), Glass);
                    b.Box(new Vector3(w * 0.25f, h - 0.3f, d * 0.15f), new Vector3(0.5f, 1.6f, 0.5f), Stone); // chimney
                    break;

                case BuildingKind.TownCentre:
                    b.Box(new Vector3(0, 0.3f, 0), new Vector3(w, 0.6f, d), Stone);                                        // plinth
                    b.Box(new Vector3(0, 0.6f + 2.4f, d * 0.05f), new Vector3(w * 0.72f, 4.8f, d * 0.55f), Wall);         // hall
                    b.Gable(new Vector3(0, 5.4f, d * 0.05f), w * 0.8f, d * 0.65f, 3f, Roof, Wall);
                    b.Box(new Vector3(0, 1.6f, -d * 0.23f), new Vector3(2f, 2.4f, 0.12f), Wood);                          // gate
                    for (int i = -1; i <= 1; i += 2)
                        b.Box(new Vector3(i * w * 0.2f, 3.2f, -d * 0.23f), new Vector3(1.2f, 1f, 0.12f), Glass);
                    var tower = new Vector3(w * 0.36f, 0.6f, -d * 0.3f);
                    b.Cylinder(tower, 1.8f, h + 2f, 10, Stone);
                    b.Cone(tower + Vector3.up * (h + 2f), 2.2f, 2.6f, 10, Roof);
                    b.Box(tower + new Vector3(0, h + 5.4f, 0), new Vector3(0.12f, 2.2f, 0.12f), Wood);                    // flag pole
                    b.Box(tower + new Vector3(0.6f, h + 6f, 0), new Vector3(1.1f, 0.7f, 0.06f), ShirtRed);              // flag
                    break;

                case BuildingKind.Farm:
                {
                    const int rows = 12;
                    float rowW = w * 0.9f / rows;
                    for (int i = 0; i < rows; i++)
                    {
                        float x = -w * 0.45f + rowW * (i + 0.5f);
                        b.Box(new Vector3(x, 0.15f, d * 0.08f), new Vector3(rowW * 0.55f, 0.3f, d * 0.74f), i % 4 < 2 ? Crop : Wheat);
                        b.Box(new Vector3(x + rowW * 0.5f, 0.05f, d * 0.08f), new Vector3(rowW * 0.45f, 0.1f, d * 0.74f), Soil);
                    }
                    var barn = new Vector3(-w * 0.3f, 0, -d * 0.38f);
                    b.Box(barn + new Vector3(0, 1.6f, 0), new Vector3(6f, 3.2f, 4.5f), ShirtRed);
                    b.Gable(barn + new Vector3(0, 3.2f, 0), 6.6f, 5f, 2f, Wood, ShirtRed);
                    for (int i = 0; i <= 10; i++)                                                                       // fence posts
                    {
                        float t = i / 10f - 0.5f;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            b.Box(new Vector3(t * w * 0.96f, 0.6f, side * d * 0.48f), new Vector3(0.15f, 1.2f, 0.15f), Wood);
                            b.Box(new Vector3(side * w * 0.48f, 0.6f, t * d * 0.96f), new Vector3(0.15f, 1.2f, 0.15f), Wood);
                        }
                    }
                    break;
                }

                case BuildingKind.LumberCamp:
                    for (int i = 0; i < 4; i++)                                                                         // shed posts
                        b.Box(new Vector3((i % 2 - 0.5f) * w * 0.5f, 1.4f, (i / 2 - 0.5f) * d * 0.5f), new Vector3(0.25f, 2.8f, 0.25f), Wood);
                    b.Box(new Vector3(0, 2.9f, 0), new Vector3(w * 0.62f, 0.25f, d * 0.62f), Roof);
                    for (int i = 0; i < 3; i++)                                                                         // log pile, 3-2-1
                    for (int j = 0; j < 3 - i; j++)
                        b.CylinderX(new Vector3(-w * 0.05f, 0.28f + i * 0.48f, d * 0.36f + (j - (2 - i) * 0.5f) * 0.56f), 0.28f, w * 0.7f, 6, Wood);
                    b.Cylinder(new Vector3(w * 0.38f, 0, -d * 0.35f), 0.4f, 0.5f, 8, Wheat);                         // stumps
                    b.Cylinder(new Vector3(-w * 0.4f, 0, -d * 0.3f), 0.35f, 0.4f, 8, Wheat);
                    break;
            }
            return b.ToMesh(def.Name);
        }

        /// <summary>Foundation size and colour per building, as a fraction of its footprint.</summary>
        public static (float scale, int colour) Foundation(BuildingKind kind) => kind switch
        {
            BuildingKind.TownCentre => (1f, Stone),
            BuildingKind.House => (0.84f, Stone),
            _ => (0.9f, Soil),
        };

        /// <summary>Unit cube (base at y = 0) in one palette colour, for foundations.</summary>
        public static Mesh Block(int colour)
        {
            var b = new Builder();
            b.Box(new Vector3(0, 0.5f, 0), Vector3.one, colour);
            return b.ToMesh("Block");
        }

        // ---------- trees ----------

        /// <summary>Conifer about 9 m tall: trunk and three stacked cones.</summary>
        public static Mesh PineTree()
        {
            var b = new Builder();
            b.Cylinder(Vector3.zero, 0.22f, 2.2f, 6, Bark);
            b.Cone(new Vector3(0, 1.6f, 0), 2.3f, 3.4f, 8, PineDark);
            b.Cone(new Vector3(0, 3.6f, 0), 1.8f, 3.0f, 8, Pine);
            b.Cone(new Vector3(0, 5.6f, 0), 1.2f, 3.3f, 8, Pine);
            return b.ToMesh("Pine");
        }

        /// <summary>Broadleaf tree about 8 m tall: trunk and a lumpy crown of two faceted blobs.</summary>
        public static Mesh BroadleafTree()
        {
            var b = new Builder();
            b.Cylinder(Vector3.zero, 0.28f, 3.2f, 6, Bark);
            b.Blob(new Vector3(0, 5.2f, 0), new Vector3(2.6f, 2.4f, 2.6f), Leaf);
            b.Blob(new Vector3(0.9f, 4.4f, 0.6f), new Vector3(1.7f, 1.5f, 1.7f), LeafDark);
            b.Blob(new Vector3(-0.8f, 4.6f, -0.7f), new Vector3(1.6f, 1.4f, 1.6f), LeafDark);
            return b.ToMesh("Broadleaf");
        }

        // ---------- citizens ----------

        /// <summary>A 1.7 m low-poly person facing +Z, feet at the origin.</summary>
        public static Mesh Citizen(int shirt)
        {
            var b = new Builder();
            b.Box(new Vector3(-0.1f, 0.42f, 0), new Vector3(0.15f, 0.84f, 0.18f), Trousers);   // legs
            b.Box(new Vector3(0.1f, 0.42f, 0), new Vector3(0.15f, 0.84f, 0.18f), Trousers);
            b.Box(new Vector3(0, 1.13f, 0), new Vector3(0.42f, 0.6f, 0.24f), shirt);            // torso
            b.Box(new Vector3(-0.27f, 1.12f, 0), new Vector3(0.11f, 0.56f, 0.13f), shirt);     // arms
            b.Box(new Vector3(0.27f, 1.12f, 0), new Vector3(0.11f, 0.56f, 0.13f), shirt);
            b.Box(new Vector3(-0.27f, 0.79f, 0), new Vector3(0.1f, 0.12f, 0.12f), Skin);      // hands
            b.Box(new Vector3(0.27f, 0.79f, 0), new Vector3(0.1f, 0.12f, 0.12f), Skin);
            b.Box(new Vector3(0, 1.53f, 0), new Vector3(0.24f, 0.26f, 0.24f), Skin);           // head
            b.Box(new Vector3(0, 1.69f, -0.02f), new Vector3(0.26f, 0.07f, 0.27f), Hair);     // hair
            b.Box(new Vector3(0, 1.58f, -0.06f), new Vector3(0.26f, 0.16f, 0.17f), Hair);
            return b.ToMesh("Citizen");
        }

        // ---------- mesh building ----------

        /// <summary>Flat-shaded mesh builder. Faces orient themselves away from the shape centre given.</summary>
        sealed class Builder
        {
            readonly List<Vector3> _v = new(), _n = new();
            readonly List<Vector2> _uv = new();
            readonly List<int> _t = new();

            /// <summary>Convex polygon, fan-triangulated, wound so its normal points away from <paramref name="inside"/>.</summary>
            void Poly(Vector3 inside, int colour, params Vector3[] p)
            {
                var centroid = Vector3.zero;
                foreach (var q in p) centroid += q;
                centroid /= p.Length;
                var normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
                if (Vector3.Dot(normal, centroid - inside) < 0) { System.Array.Reverse(p); normal = -normal; }
                int start = _v.Count;
                var uv = Uv(colour);
                foreach (var q in p) { _v.Add(q); _n.Add(normal); _uv.Add(uv); }
                for (int i = 1; i < p.Length - 1; i++) { _t.Add(start); _t.Add(start + i); _t.Add(start + i + 1); }
            }

            public void Box(Vector3 c, Vector3 s, int colour)
            {
                var h = s * 0.5f;
                Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
                Poly(c, colour, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1));
                Poly(c, colour, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
                Poly(c, colour, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1));
                Poly(c, colour, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));
                Poly(c, colour, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1));
                Poly(c, colour, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
            }

            /// <summary>Gable roof on a w x d base at <paramref name="b"/>, ridge along X at height h.</summary>
            public void Gable(Vector3 b, float w, float d, float h, int roof, int gable)
            {
                var inside = b + Vector3.up * h * 0.33f;
                Vector3 a0 = b + new Vector3(-w / 2, 0, -d / 2), a1 = b + new Vector3(w / 2, 0, -d / 2);
                Vector3 a2 = b + new Vector3(w / 2, 0, d / 2), a3 = b + new Vector3(-w / 2, 0, d / 2);
                Vector3 r0 = b + new Vector3(-w / 2, h, 0), r1 = b + new Vector3(w / 2, h, 0);
                Poly(inside, roof, a0, a1, r1, r0);
                Poly(inside, roof, a3, a2, r1, r0);
                Poly(inside, gable, a0, a3, r0);
                Poly(inside, gable, a1, a2, r1);
            }

            public void Cylinder(Vector3 b, float r, float h, int segs, int colour) => Tube(b, Vector3.up, r, h, segs, colour);

            public void CylinderX(Vector3 centre, float r, float length, int segs, int colour) =>
                Tube(centre - Vector3.right * length * 0.5f, Vector3.right, r, length, segs, colour);

            void Tube(Vector3 start, Vector3 axis, float r, float len, int segs, int colour)
            {
                var u = Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up;
                var v = Vector3.Cross(axis, u);
                var mid = start + axis * len * 0.5f;
                var ring0 = new Vector3[segs];
                var ring1 = new Vector3[segs];
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    var off = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
                    ring0[i] = start + off;
                    ring1[i] = start + axis * len + off;
                }
                for (int i = 0; i < segs; i++)
                {
                    int j = (i + 1) % segs;
                    Poly(mid, colour, ring0[i], ring0[j], ring1[j], ring1[i]);
                }
                Poly(mid, colour, (Vector3[])ring0.Clone());
                Poly(mid, colour, (Vector3[])ring1.Clone());
            }

            /// <summary>Faceted ellipsoid (8 segments around, 4 rings) for tree crowns.</summary>
            public void Blob(Vector3 c, Vector3 radii, int colour)
            {
                const int segs = 8, rings = 4;
                var pts = new Vector3[rings + 1, segs];
                for (int r = 0; r <= rings; r++)
                {
                    float phi = Mathf.PI * r / rings;
                    for (int s = 0; s < segs; s++)
                    {
                        float theta = 2f * Mathf.PI * (s + (r % 2) * 0.5f) / segs;
                        pts[r, s] = c + Vector3.Scale(radii, new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
                    }
                }
                for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int n = (s + 1) % segs;
                    if (r == 0) Poly(c, colour, pts[0, 0], pts[1, s], pts[1, n]);
                    else if (r == rings - 1) Poly(c, colour, pts[r, s], pts[r, n], pts[rings, 0]);
                    else
                    {
                        Poly(c, colour, pts[r, s], pts[r, n], pts[r + 1, s]);
                        Poly(c, colour, pts[r, n], pts[r + 1, n], pts[r + 1, s]);
                    }
                }
            }

            public void Cone(Vector3 b, float r, float h, int segs, int colour)
            {
                var apex = b + Vector3.up * h;
                var inside = b + Vector3.up * h * 0.3f;
                var ring = new Vector3[segs];
                for (int i = 0; i < segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    ring[i] = b + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                }
                for (int i = 0; i < segs; i++) Poly(inside, colour, ring[i], ring[(i + 1) % segs], apex);
                Poly(inside, colour, ring);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetUVs(0, _uv);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
