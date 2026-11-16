using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ARO.NetCore;
using Xunit;

/// <summary>
/// The realistic people file (tools/blender/people_kit/export.py -> people.bytes, read by PeopleAsset). The real file is built in CI from the CC0 MakeHuman base and is not in this repository, so
/// these tests write files of the same layout: a valid file reads back exactly (frames, normals, UVs in the atlas tiles), and every kind of damage is refused with a FormatException rather than
/// drawing garbage. The Blender side has the mirror test (tools/blender/tests/test_people_kit.py: write, read back, the ids match PeopleKit's designs).
/// </summary>
public class PeopleAssetTests
{
    sealed class Lod { public byte[] Group; public ushort[] Uv; public int[] Tris; public short[][] Pos; public sbyte[][] Nrm; }

    static Lod Quad(int frames, float lift = 0f)
    {
        // a unit quad facing +z: four vertices (two skin, two cloth), two triangles; each frame lifts it by 1 cm more
        var L = new Lod { Group = new byte[] { 0, 0, 1, 1 }, Uv = new ushort[] { 0, 0, 65535, 0, 65535, 65535, 0, 65535 }, Tris = new[] { 0, 2, 1, 0, 3, 2 }, Pos = new short[frames][], Nrm = new sbyte[frames][] };
        for (int f = 0; f < frames; f++)
        {
            short S(float m) => (short)Math.Round(m / PeopleAsset.PosScale);
            float y = lift + 0.01f * f;
            L.Pos[f] = new[] { S(0), S(y), S(0), S(1), S(y), S(0), S(1), S(1 + y), S(0), S(0), S(1 + y), S(0) };
            L.Nrm[f] = new sbyte[] { 0, 0, 127, 0, 0, 127, 0, 0, 127, 0, 0, 127 };
        }
        return L;
    }

    static byte[] Write(Action<BinaryWriter> tamper = null, string magic = "APPL", ushort version = 1, int walk = 8, int idle = 4, int atlas = 2048, int tile = 256, int[] skinTiles = null, Lod[] lods = null, byte extra = 0)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(magic)); w.Write(version); w.Write((ushort)2); w.Write((ushort)walk); w.Write((ushort)idle); w.Write((ushort)atlas); w.Write((ushort)tile);
        foreach (var (id, child) in new[] { ("man_kaftan_cap", false), ("boy_school", true) })
        {
            var b = Encoding.UTF8.GetBytes(id); w.Write((byte)b.Length); w.Write(b); w.Write((byte)(child ? 1 : 0)); w.Write(child ? 1.40f : 1.78f);
            var st = skinTiles ?? (child ? new[] { 6, 7, 8 } : new[] { 0, 1, 2 }); w.Write((byte)st.Length); w.Write((byte)3);
            foreach (var t in st) w.Write((ushort)t);
            foreach (var t in child ? new[] { 9, 10, 11 } : new[] { 3, 4, 5 }) w.Write((ushort)t);
            var ls = lods ?? new[] { Quad(walk + idle), Quad(walk + idle, 0.5f) }; w.Write((byte)ls.Length);
            foreach (var L in ls)
            {
                w.Write((uint)L.Group.Length); w.Write((uint)L.Tris.Length); w.Write(L.Group);
                foreach (var u in L.Uv) w.Write(u);
                foreach (var t in L.Tris) w.Write((ushort)t);
                for (int f = 0; f < L.Pos.Length; f++) { foreach (var p in L.Pos[f]) w.Write(p); foreach (var n in L.Nrm[f]) w.Write(n); }
            }
        }
        tamper?.Invoke(w);
        for (int i = 0; i < extra; i++) w.Write((byte)0);
        return ms.ToArray();
    }

    [Fact]
    public void AValidFileReadsBackExactly()
    {
        var a = PeopleAsset.Parse(Write());
        Assert.Equal(2, a.Designs.Count); Assert.Equal(12, a.FrameCount); Assert.Equal(2048, a.AtlasSize); Assert.Equal(256, a.TileSize);
        var m = a.Find("man_kaftan_cap"); var b = a.Find("boy_school");
        Assert.NotNull(m); Assert.NotNull(b); Assert.False(m.Child); Assert.True(b.Child); Assert.Equal(1.78f, m.Height, 3); Assert.Null(a.Find("nobody"));
        Assert.Equal(2, m.Lods.Length); var L = m.Lods[0];
        Assert.Equal(4, L.VertexCount); Assert.Equal(2, L.TriangleCount); Assert.Equal(new[] { 0, 2, 1, 0, 3, 2 }, L.Tris);
        Assert.Equal(12, L.Pos.Length);
        for (int f = 0; f < 12; f++) { Assert.Equal(0.01f * f, L.Pos[f][0].Y, 3); Assert.Equal(1f, L.Pos[f][2].X, 3); Assert.Equal(1f, L.Nrm[f][1].Z, 3); }
        Assert.Equal(0.5f, m.Lods[1].Pos[0][0].Y, 3);
    }

    [Fact]
    public void SkinVerticesUseTheSkinToneTileAndClothTheColourwayTile()
    {
        var a = PeopleAsset.Parse(Write()); var m = a.Find("man_kaftan_cap"); float t = 256f / 2048f;
        var uv = m.AtlasUv(0, 1, 2);                                        // skin tone 1 -> tile 1 (column 1, top row); colourway 2 -> tile 5 (column 5, top row)
        Assert.Equal(1 * t, uv[0].X, 4); Assert.Equal(1f - t, uv[0].Y, 4);     // skin vertex at the tile's (0, 0)
        Assert.Equal(2 * t, uv[1].X, 4); Assert.Equal(1f - t, uv[1].Y, 4);     // skin vertex at (1, 0): the tile's right edge
        Assert.Equal(6 * t, uv[2].X, 4); Assert.Equal(1f, uv[2].Y, 4);         // cloth vertex at (1, 1): tile 5's top right corner, the top of the atlas
        Assert.Equal(5 * t, uv[3].X, 4);
        var b = a.Find("boy_school").AtlasUv(0, 0, 0);                      // the boy's tiles 6 and 9: the second is in the second row (8 tiles per row)
        Assert.Equal(6 * t, b[0].X, 4); Assert.Equal(1 * t, b[3].X, 4); Assert.Equal(1f - 2 * t + t, b[3].Y, 4);   // cloth vertex at (0, 1): tile 9's top left
        Assert.Same(uv, m.AtlasUv(0, 1, 2));                                 // cached
        Assert.Equal(m.AtlasUv(0, 1, 2)[0].X, m.AtlasUv(0, 4, 5)[0].X, 5);   // indices wrap like the crowd's colourways and skins
        Assert.Equal(4, m.AtlasUv(9, 0, 0).Length);                          // a LOD beyond the last is the last
    }

    public static IEnumerable<object[]> Damaged => new[]
    {
        new object[] { "magic", Write(magic: "NOPE") },
        new object[] { "version", Write(version: 2) },
        new object[] { "frames", Write(walk: 6, lods: new[] { Quad(10) }) },
        new object[] { "tile size", Write(atlas: 2000) },
        new object[] { "tile beyond the atlas", Write(skinTiles: new[] { 0, 1, 64 }) },
        new object[] { "trailing bytes", Write(extra: 3) },
        new object[] { "index beyond the vertices", Write(lods: new[] { new Lod { Group = new byte[] { 0, 0, 1, 1 }, Uv = new ushort[8], Tris = new[] { 0, 1, 4 }, Pos = Quad(12).Pos, Nrm = Quad(12).Nrm } }) },
        new object[] { "vertex group", Write(lods: new[] { new Lod { Group = new byte[] { 0, 0, 1, 2 }, Uv = new ushort[8], Tris = new[] { 0, 1, 2 }, Pos = Quad(12).Pos, Nrm = Quad(12).Nrm } }) },
        new object[] { "indices not in threes", Write(lods: new[] { new Lod { Group = new byte[] { 0, 0, 1, 1 }, Uv = new ushort[8], Tris = new[] { 0, 1 }, Pos = Quad(12).Pos, Nrm = Quad(12).Nrm } }) },
    };

    [Theory, MemberData(nameof(Damaged))]
    public void ADamagedFileIsRefused(string what, byte[] data)
    {
        var e = Assert.Throws<FormatException>(() => PeopleAsset.Parse(data));
        Assert.StartsWith("people asset:", e.Message);
        Assert.False(string.IsNullOrEmpty(what));
    }

    [Fact]
    public void ATruncatedFileIsRefusedAtEveryLength()
    {
        var full = Write();
        foreach (int n in new[] { 0, 3, 15, 20, 40, full.Length / 3, full.Length / 2, full.Length - 1 })
            Assert.Throws<FormatException>(() => PeopleAsset.Parse(full.Take(n).ToArray()));
        Assert.Throws<FormatException>(() => PeopleAsset.Parse(null));
    }

    [Fact]
    public void TheFileCarriesTheCrowdsFrameNumbering()
    {
        Assert.Equal(8, PeopleKit.WalkFrames); Assert.Equal(4, PeopleKit.IdleFrames);      // tools/blender/people_kit/export.py WALK_FRAMES, IDLE_FRAMES
        Assert.Equal(4f / 32767f, PeopleAsset.PosScale);                                     // export.py POS_SCALE
    }
}
