using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ARO.NetCore
{
    /// <summary>
    /// The realistic people of the crowd (owner direction of 2026-11-15: "their face have to show like real people"): bodies and faces from the CC0 MakeHuman base, Kano dress made by our code,
    /// exported by tools/blender/people_kit/export.py as one file (people.bytes) and one texture atlas. Per design: two levels of detail, each with 8 walk and 4 idle frames posed by the rig
    /// (positions and normals), UVs inside a tile and the group of every vertex (0 = skin, painted in the skin-tone tile; 1 = cloth, painted in the colourway tile).
    /// The game draws them in place of the code-built figures (PeopleKit) when the file is present; the frame numbering is PeopleKit's (0..7 walk, 8..11 idle).
    /// Layout (little-endian), VERSION 1: "APPL", u16 version, designs, walk frames, idle frames, atlas px, tile px; per design: u8 name length, name, u8 child, f32 height, u8 skins, u8 colourways,
    /// u16 skin tiles, u16 cloth tiles, u8 LODs; per LOD: u32 vertices, u32 indices, u8 group[v], u16 uv[2v], indices (u16 below 65536 vertices, else u32), per frame int16 pos[3v] (x 4/32767 m)
    /// and int8 normal[3v] (x 1/127).
    /// </summary>
    public sealed class PeopleAsset
    {
        public const int Version = 1;
        public const float PosScale = 4f / 32767f;
        public int AtlasSize, TileSize, WalkFrames, IdleFrames;
        public int FrameCount => WalkFrames + IdleFrames;
        public readonly List<RealDesign> Designs = new List<RealDesign>();

        public sealed class RealDesign
        {
            public string Id; public bool Child; public float Height; public int[] SkinTiles, ClothTiles; public RealLod[] Lods;
            readonly Dictionary<int, V2[]> _uv = new Dictionary<int, V2[]>();
            internal PeopleAsset Owner;

            /// <summary>Atlas UVs (v up) of a LOD for one skin tone and colourway. Cached.</summary>
            public V2[] AtlasUv(int lod, int skin, int colourway)
            {
                lod = Clamp(lod, 0, Lods.Length - 1); skin = Mod(skin, SkinTiles.Length); colourway = Mod(colourway, ClothTiles.Length);
                int key = (lod << 16) | (skin << 8) | colourway;
                lock (_uv)
                {
                    if (_uv.TryGetValue(key, out var r)) return r;
                    var L = Lods[lod]; r = new V2[L.Group.Length]; int n = Owner.AtlasSize / Owner.TileSize; float t = Owner.TileSize, a = Owner.AtlasSize;
                    for (int i = 0; i < r.Length; i++)
                    {
                        int tile = L.Group[i] == 0 ? SkinTiles[skin] : ClothTiles[colourway]; int tx = tile % n * Owner.TileSize, ty = tile / n * Owner.TileSize;
                        r[i] = new V2((tx + L.U[i] * t) / a, 1f - (ty + t) / a + L.V[i] * t / a);       // tile rows are counted from the top of the image; UV v from the bottom
                    }
                    _uv[key] = r; return r;
                }
            }
        }

        public sealed class RealLod
        {
            public byte[] Group; public float[] U, V; public int[] Tris; public V3[][] Pos, Nrm;
            public int VertexCount => Group.Length;
            public int TriangleCount => Tris.Length / 3;
        }

        public RealDesign Find(string id) { foreach (var d in Designs) if (d.Id == id) return d; return null; }

        /// <summary>Parses people.bytes. Throws FormatException (with what is wrong) on a bad or truncated file, an unknown version or an index out of range.</summary>
        public static PeopleAsset Parse(byte[] data)
        {
            if (data == null || data.Length < 16) throw new FormatException("people asset: empty or too short");
            var r = new BinaryReader(new MemoryStream(data, false));
            try
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "APPL") throw new FormatException("people asset: not a people file (magic)");
                int ver = r.ReadUInt16(); if (ver != Version) throw new FormatException($"people asset: version {ver}, this game reads {Version}");
                var a = new PeopleAsset(); int nd = r.ReadUInt16(); a.WalkFrames = r.ReadUInt16(); a.IdleFrames = r.ReadUInt16(); a.AtlasSize = r.ReadUInt16(); a.TileSize = r.ReadUInt16();
                if (a.WalkFrames != PeopleKit.WalkFrames || a.IdleFrames != PeopleKit.IdleFrames) throw new FormatException($"people asset: {a.WalkFrames}+{a.IdleFrames} frames, the crowd plays {PeopleKit.WalkFrames}+{PeopleKit.IdleFrames}");
                if (a.TileSize <= 0 || a.AtlasSize < a.TileSize || a.AtlasSize % a.TileSize != 0) throw new FormatException("people asset: bad atlas or tile size");
                int tiles = (a.AtlasSize / a.TileSize) * (a.AtlasSize / a.TileSize);
                for (int d = 0; d < nd; d++)
                {
                    var D = new RealDesign { Owner = a }; int ln = r.ReadByte(); D.Id = Encoding.UTF8.GetString(r.ReadBytes(ln)); D.Child = r.ReadByte() != 0; D.Height = r.ReadSingle();
                    int ns = r.ReadByte(), nc = r.ReadByte(); if (ns == 0 || nc == 0) throw new FormatException($"people asset: {D.Id} has no skin tone or colourway");
                    D.SkinTiles = new int[ns]; D.ClothTiles = new int[nc];
                    for (int i = 0; i < ns; i++) D.SkinTiles[i] = Tile(r.ReadUInt16(), tiles, D.Id);
                    for (int i = 0; i < nc; i++) D.ClothTiles[i] = Tile(r.ReadUInt16(), tiles, D.Id);
                    int nl = r.ReadByte(); if (nl == 0) throw new FormatException($"people asset: {D.Id} has no level of detail");
                    D.Lods = new RealLod[nl];
                    for (int l = 0; l < nl; l++)
                    {
                        int nv = checked((int)r.ReadUInt32()), ni = checked((int)r.ReadUInt32());
                        if (nv <= 0 || ni <= 0 || ni % 3 != 0) throw new FormatException($"people asset: {D.Id} LOD {l} has {nv} vertices and {ni} indices");
                        var L = new RealLod { Group = r.ReadBytes(nv), U = new float[nv], V = new float[nv], Tris = new int[ni], Pos = new V3[a.FrameCount][], Nrm = new V3[a.FrameCount][] };
                        if (L.Group.Length != nv) throw new EndOfStreamException();
                        for (int i = 0; i < nv; i++) { L.U[i] = r.ReadUInt16() / 65535f; L.V[i] = r.ReadUInt16() / 65535f; if (L.Group[i] > 1) throw new FormatException($"people asset: {D.Id} vertex group {L.Group[i]}"); }
                        bool wide = nv >= 65536;
                        for (int i = 0; i < ni; i++) { int k = wide ? checked((int)r.ReadUInt32()) : r.ReadUInt16(); if (k >= nv) throw new FormatException($"people asset: {D.Id} LOD {l} index {k} beyond {nv} vertices"); L.Tris[i] = k; }
                        for (int f = 0; f < a.FrameCount; f++)
                        {
                            var p = new V3[nv]; var q = new V3[nv];
                            for (int i = 0; i < nv; i++) p[i] = new V3(r.ReadInt16() * PosScale, r.ReadInt16() * PosScale, r.ReadInt16() * PosScale);
                            for (int i = 0; i < nv; i++) q[i] = new V3(r.ReadSByte() / 127f, r.ReadSByte() / 127f, r.ReadSByte() / 127f);
                            L.Pos[f] = p; L.Nrm[f] = q;
                        }
                        D.Lods[l] = L;
                    }
                    a.Designs.Add(D);
                }
                if (r.BaseStream.Position != data.Length) throw new FormatException($"people asset: {data.Length - r.BaseStream.Position} bytes after the last design");
                return a;
            }
            catch (EndOfStreamException) { throw new FormatException("people asset: the file is truncated"); }
        }

        static int Tile(int t, int tiles, string id) { if (t >= tiles) throw new FormatException($"people asset: {id} uses tile {t} of {tiles}"); return t; }
        static int Mod(int a, int n) => ((a % n) + n) % n;
        static int Clamp(int a, int lo, int hi) => a < lo ? lo : (a > hi ? hi : a);
    }
}
