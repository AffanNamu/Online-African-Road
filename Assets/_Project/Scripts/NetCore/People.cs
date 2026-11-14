using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>A person's mesh: positions, palette UVs, the bone of every vertex (0 body, 1 left leg, 2 right leg, 3 left arm, 4 right arm) and triangles. Local axes: x right, y up, z forward (facing).</summary>
    public sealed class PersonMesh
    {
        public V3[] Pos; public V2[] Uv; public byte[] Bone; public int[] Tris;
        public int TriangleCount => Tris.Length / 3;
        public float Height { get { float h = 0f; foreach (var p in Pos) h = Math.Max(h, p.Y); return h; } }
    }

    /// <summary>
    /// P2 of docs/FUNCTIONAL_PLACES_PROPOSAL.md (approved by the owner on 2026-11-14; "people built by code"): the people of the streets, built by code from tapered tubes and blobs, in Kano dress. Ten
    /// designs (kaftan and cap, babban riga, shirt and trousers, women in hijab and wrapper, an abaya, a market woman with a head-load, schoolchildren), three skin tones and three colourways each, two
    /// levels of detail. Bodies, robes, heads and hijabs are turned shapes with smooth rings (rounded shoulders, a waist, a jaw); men have hair, caps or a beard. Colours come from one small palette texture (one material, one draw call for a whole crowd). A rig of five rigid parts (body, legs, arms) gives a walk cycle and an idle sway;
    /// the poses are computed here, so the game only copies posed vertices. Realistic proportions and dress; no faces are modelled (no photoreal people).
    /// </summary>
    public static class PeopleKit
    {
        public const int Body = 0, LegL = 1, LegR = 2, ArmL = 3, ArmR = 4;
        public const int WalkFrames = 8, IdleFrames = 4;
        public const int PaletteSize = 8;                                   // the palette texture is 8 x 8 cells

        /// <summary>Palette colours (sRGB 0..255), cell i at column i % 8, row i / 8.</summary>
        public static readonly byte[][] Palette =
        {
            new byte[] { 74, 46, 32 }, new byte[] { 104, 68, 46 }, new byte[] { 140, 98, 68 },          // 0..2 skin: dark, medium, light brown
            new byte[] { 236, 234, 226 }, new byte[] { 222, 208, 176 }, new byte[] { 120, 170, 214 },   // 3 white, 4 cream, 5 sky blue
            new byte[] { 36, 70, 150 }, new byte[] { 128, 130, 132 }, new byte[] { 40, 120, 72 },       // 6 royal blue, 7 grey, 8 green
            new byte[] { 104, 52, 120 }, new byte[] { 22, 22, 26 }, new byte[] { 120, 30, 40 },         // 9 purple, 10 black, 11 maroon
            new byte[] { 178, 152, 102 }, new byte[] { 30, 38, 74 }, new byte[] { 214, 168, 40 },       // 12 khaki, 13 navy, 14 gold
            new byte[] { 200, 72, 40 }, new byte[] { 60, 42, 32 }, new byte[] { 200, 110, 140 },        // 15 orange-red, 16 dark brown, 17 pink
            new byte[] { 28, 120, 128 }, new byte[] { 196, 204, 214 }, new byte[] { 58, 60, 64 },       // 18 teal, 19 enamel (basin), 20 dark grey
        };
        public const int Skin0 = 0, White = 3, Cream = 4, Sky = 5, Royal = 6, Grey = 7, Green = 8, Purple = 9, Black = 10, Maroon = 11, Khaki = 12, Navy = 13, Gold = 14, Orange = 15, Brown = 16, Pink = 17, Teal = 18, Enamel = 19, DarkGrey = 20;

        public static V2 Cell(int c) => new V2(((c % PaletteSize) + 0.5f) / PaletteSize, ((c / PaletteSize) + 0.5f) / PaletteSize);

        public sealed class Design
        {
            public string Id, What; public bool Child;
            /// <summary>How far the legs swing in the walk, 0..1: a long robe shortens the stride (and hides the thighs), so its wearer's feet stay under the hem.</summary>
            public float Swing = 1f;
            /// <summary>Colourways: each is the list of garment colours this design's builder uses (main, second, accent).</summary>
            public int[][] Colourways;
            internal Action<Builder, int[], int> Build;      // (builder, colourway, skin)
        }

        public static readonly Design[] Designs =
        {
            new Design { Id = "man_kaftan_cap", What = "a man in an ankle-length kaftan and a kufi cap", Swing = 0.34f, Colourways = new[] { new[] { White, Brown, Cream }, new[] { Sky, Navy, White }, new[] { Cream, Maroon, Brown } }, Build = (b, c, s) => Kaftan(b, c, s, elder: false) },
            new Design { Id = "man_babban_riga", What = "a man in a wide babban riga over trousers, with a cap", Swing = 0.6f, Colourways = new[] { new[] { Royal, White, Gold }, new[] { White, Cream, Gold }, new[] { Teal, Navy, Gold } }, Build = Riga },
            new Design { Id = "man_shirt_trousers", What = "a man in a short-sleeved shirt and trousers", Colourways = new[] { new[] { Sky, Navy, Black }, new[] { White, DarkGrey, Brown }, new[] { Orange, Khaki, Brown } }, Build = ShirtTrousers },
            new Design { Id = "man_kaftan_plain", What = "an older man with a grey beard in a plain kaftan and cap", Swing = 0.3f, Colourways = new[] { new[] { Grey, White, DarkGrey }, new[] { Cream, Brown, Gold }, new[] { Green, White, Brown } }, Build = (b, c, s) => Kaftan(b, c, s, elder: true) },
            new Design { Id = "woman_hijab_wrapper", What = "a woman in a hijab, blouse and wrapper", Swing = 0.32f, Colourways = new[] { new[] { Green, Gold, Black }, new[] { Purple, Pink, Black }, new[] { Maroon, Orange, Black } }, Build = (b, c, s) => Wrapper(b, c, s, headLoad: false) },
            new Design { Id = "woman_hijab_long", What = "a woman in a long dress and a long hijab", Swing = 0.34f, Colourways = new[] { new[] { Teal, Cream, Black }, new[] { Pink, Purple, Black }, new[] { Navy, Sky, Black } }, Build = (b, c, s) => LongHijab(b, c, s, 1f) },
            new Design { Id = "woman_abaya", What = "a woman in a black abaya and hijab", Swing = 0.34f, Colourways = new[] { new[] { Black, Black, DarkGrey }, new[] { Navy, Black, DarkGrey }, new[] { DarkGrey, Black, Black } }, Build = (b, c, s) => LongHijab(b, c, s, 1f) },
            new Design { Id = "woman_market_headload", What = "a market woman in a head-tie and wrapper, carrying an enamel basin on her head", Swing = 0.3f, Colourways = new[] { new[] { Orange, Gold, Enamel }, new[] { Green, Gold, Enamel }, new[] { Purple, Orange, Enamel } }, Build = (b, c, s) => Wrapper(b, c, s, headLoad: true) },
            new Design { Id = "boy_school", What = "a schoolboy in a shirt and shorts with a school bag", Child = true, Colourways = new[] { new[] { White, Navy, Brown }, new[] { Khaki, Brown, Black }, new[] { Sky, Navy, Maroon } }, Build = Schoolboy },
            new Design { Id = "girl_school_hijab", What = "a schoolgirl in a white hijab and a long uniform", Child = true, Swing = 0.36f, Colourways = new[] { new[] { Navy, White, Black }, new[] { Green, White, Black }, new[] { Maroon, White, Black } }, Build = (b, c, s) => LongHijab(b, c, s, ChildHead) },
        };

        public static int SkinCount => 3;

        // ------------------------------------------------------------------------------------------------ building
        internal sealed class Builder
        {
            public readonly List<V3> P = new List<V3>(); public readonly List<V2> U = new List<V2>(); public readonly List<byte> B = new List<byte>(); public readonly List<int> T = new List<int>();
            public int Sides;    // 10 at LOD 0, 6 at LOD 1
            public bool Coarse => Sides < 10;
            int V(V3 p, int colour, int bone) { P.Add(p); U.Add(Cell(colour)); B.Add((byte)bone); return P.Count - 1; }

            /// <summary>
            /// A turned shape about a vertical axis at (x, z): `rings` holds (y, half width, half depth, z shift) for each ring from the bottom up. The rings share their vertices, so the surface is
            /// smooth. The bottom cap faces down and the top cap faces up.
            /// </summary>
            public void Lathe(int bone, int colour, float x, float z, float[] rings, bool cap0 = true, bool cap1 = true)
            {
                int n = Sides, k = rings.Length / 4, b0 = P.Count;
                for (int r = 0; r < k; r++)
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2.0 * Math.PI * (i + 0.5) / n;
                        V(new V3(x + (float)Math.Sin(a) * rings[r * 4 + 1], rings[r * 4], z + rings[r * 4 + 3] + (float)Math.Cos(a) * rings[r * 4 + 2]), colour, bone);
                    }
                int R(int r, int i) => b0 + r * n + (i % n);
                for (int r = 0; r < k - 1; r++)
                    for (int i = 0; i < n; i++) { int a0 = R(r, i), c0 = R(r, i + 1), a1 = R(r + 1, i), c1 = R(r + 1, i + 1); T.Add(a0); T.Add(c0); T.Add(a1); T.Add(a1); T.Add(c0); T.Add(c1); }
                if (cap0) { int c = V(new V3(x, rings[0], z + rings[3]), colour, bone); for (int i = 0; i < n; i++) { T.Add(c); T.Add(R(0, i + 1)); T.Add(R(0, i)); } }
                if (cap1) { int c = V(new V3(x, rings[(k - 1) * 4], z + rings[(k - 1) * 4 + 3]), colour, bone); for (int i = 0; i < n; i++) { T.Add(c); T.Add(R(k - 1, i)); T.Add(R(k - 1, i + 1)); } }
            }

            /// <summary>A tapered elliptical tube along y from y0 (radius r0) to y1 (radius r1), centred at (x, z), depth scaled by sz; capped ends.</summary>
            public void Tube(int bone, int colour, float x, float z, float y0, float y1, float r0, float r1, float sz = 1f, bool cap0 = true, bool cap1 = true, float tilt = 0f)
                => Lathe(bone, colour, x, z, new[] { y0, r0, r0 * sz, 0f, y1, r1, r1 * sz, tilt }, cap0, cap1);

            /// <summary>An ellipsoid of radii (rx, ry, rz) at c.</summary>
            public void Blob(int bone, int colour, V3 c, float rx, float ry, float rz, int rings = 0)
            {
                int n = Sides; if (rings <= 0) rings = Coarse ? 3 : 5;
                int top = V(new V3(c.X, c.Y + ry, c.Z), colour, bone);
                for (int k = 1; k < rings; k++)
                {
                    double t = Math.PI * k / rings; float y = (float)Math.Cos(t), rr = (float)Math.Sin(t);
                    for (int i = 0; i < n; i++) { double a = 2.0 * Math.PI * (i + 0.5) / n; V(new V3(c.X + (float)Math.Sin(a) * rr * rx, c.Y + y * ry, c.Z + (float)Math.Cos(a) * rr * rz), colour, bone); }
                }
                int bot = V(new V3(c.X, c.Y - ry, c.Z), colour, bone);
                int R(int k, int i) => top + 1 + (k - 1) * n + (i % n);
                for (int i = 0; i < n; i++) { T.Add(top); T.Add(R(1, i)); T.Add(R(1, i + 1)); }
                for (int k = 1; k < rings - 1; k++) for (int i = 0; i < n; i++) { int a = R(k, i), b = R(k, i + 1), d = R(k + 1, i), e = R(k + 1, i + 1); T.Add(a); T.Add(d); T.Add(b); T.Add(b); T.Add(d); T.Add(e); }
                for (int i = 0; i < n; i++) { T.Add(bot); T.Add(R(rings - 1, i + 1)); T.Add(R(rings - 1, i)); }
            }

            /// <summary>An axis-aligned box (min corner, max corner).</summary>
            public void Box(int bone, int colour, V3 a, V3 b)
            {
                int o = P.Count;
                for (int i = 0; i < 8; i++) V(new V3((i & 1) == 0 ? a.X : b.X, (i & 2) == 0 ? a.Y : b.Y, (i & 4) == 0 ? a.Z : b.Z), colour, bone);
                int[] f = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
                foreach (int k in f) T.Add(o + k);
            }

            public PersonMesh Done() => new PersonMesh { Pos = P.ToArray(), Uv = U.ToArray(), Bone = B.ToArray(), Tris = T.ToArray() };
        }

        // An adult of about 1.76 m: hip joints at 0.92 m, shoulder joints at 1.40 m, the chin at 1.52 m. A child is the adult scaled by 0.72 with a larger head.
        const float Hip = 0.92f, Sh = 1.40f, LegX = 0.09f, ChildHead = 1.14f;
        static float[] R(params float[] v) => v;
        static readonly (int bone, float x)[] LegSides = { (LegL, -LegX), (LegR, LegX) };

        static void Shoes(Builder b, int colour) { foreach (var (bone, x) in LegSides) b.Box(bone, colour, new V3(x - 0.048f, 0f, -0.07f), new V3(x + 0.048f, 0.08f, 0.17f)); }

        /// <summary>Trouser legs from the ankle to `top` (the hip, or just above a robe's hem: under a robe only the shins exist).</summary>
        static void Legs(Builder b, int colour, float top = Hip)
        {
            foreach (var (bone, x) in LegSides)
                if (top >= Hip - 0.01f) b.Lathe(bone, colour, x, 0f, R(0.075f, 0.052f, 0.058f, 0f, 0.48f, 0.063f, 0.067f, 0f, Hip, 0.088f, 0.092f, 0f));
                else b.Lathe(bone, colour, x, 0f, R(0.075f, 0.05f, 0.055f, 0f, top, 0.058f, 0.062f, 0f));
        }

        /// <summary>
        /// Arms at `ax` from the centre line: a sleeve from the shoulder down to `sleeveTo` (1.14 = short sleeve, 0.86 = to the wrist), the bare forearm below it, the hand. They hang the way arms
        /// do: the elbow a little bent, so the forearm and the hand lie slightly forward of the shoulder.
        /// </summary>
        static void Arms(Builder b, int sleeve, int skin, float sleeveTo, float r = 0.05f, float ax = 0.20f)
        {
            float Fwd(float y) => y >= 1.12f ? 0.012f * (Sh - y) / (Sh - 1.12f) : 0.012f + 0.062f * (1.12f - y) / (1.12f - 0.79f);      // forward of the shoulder: the upper arm nearly plumb, the forearm bent forward
            foreach (int side in new[] { -1, 1 })
            {
                int bone = side < 0 ? ArmL : ArmR; float x = side * ax;
                if (!b.Coarse) b.Blob(bone, sleeve, new V3(x, Sh - 0.005f, 0f), r * 1.12f, r * 1.1f, r * 1.15f, 4);                           // the shoulder, rounded
                if (sleeveTo < 1.10f) b.Lathe(bone, sleeve, x, 0f, R(sleeveTo, r * 1.02f, r * 1.02f, Fwd(sleeveTo), 1.12f, r * 1.1f, r * 1.1f, Fwd(1.12f), Sh + 0.01f, r * 1.18f, r * 1.18f, 0f));
                else b.Lathe(bone, sleeve, x, 0f, R(sleeveTo, r * 1.05f, r * 1.05f, Fwd(sleeveTo), Sh + 0.01f, r * 1.18f, r * 1.18f, 0f));
                if (sleeveTo > 0.9f) b.Lathe(bone, skin, x, 0f, R(0.84f, 0.034f, 0.034f, Fwd(0.84f), 1.12f, 0.043f, 0.043f, Fwd(1.12f), sleeveTo + 0.012f, 0.045f, 0.045f, Fwd(sleeveTo + 0.012f)));   // the forearm
                b.Blob(bone, skin, new V3(x, 0.785f, Fwd(0.785f)), 0.03f, 0.062f, 0.036f, b.Coarse ? 3 : 4);                                  // the hand: a flat mitten, thumb side forward
            }
        }

        /// <summary>Neck and head in one turned shape (chin, jaw, cheek, brow, crown), with hair, a kufi cap, a head-tie or a beard. `hs` scales the head about the chin (children).</summary>
        static void Head(Builder b, int skin, float hs = 1f, int hair = -1, int cap = -1, int headTie = -1, int beard = -1)
        {
            float Y(float y) => 1.50f + (y - 1.50f) * hs;
            b.Lathe(Body, skin, 0f, 0f, R(1.43f, 0.058f, 0.06f, 0f, Y(1.52f), 0.054f * hs, 0.06f * hs, 0.004f, Y(1.555f), 0.08f * hs, 0.09f * hs, 0.008f, Y(1.625f), 0.095f * hs, 0.105f * hs, 0.006f,
                Y(1.695f), 0.093f * hs, 0.103f * hs, 0f, Y(1.745f), 0.064f * hs, 0.072f * hs, -0.004f, Y(1.762f), 0.022f * hs, 0.026f * hs, -0.004f), false, true);
            if (hair >= 0) b.Lathe(Body, hair, 0f, -0.014f * hs, R(Y(1.64f), 0.099f * hs, 0.108f * hs, 0f, Y(1.70f), 0.098f * hs, 0.108f * hs, 0f, Y(1.75f), 0.068f * hs, 0.076f * hs, 0f, Y(1.77f), 0.024f * hs, 0.028f * hs, 0f));   // short hair: set back, so the brow stays bare
            if (cap >= 0) b.Lathe(Body, cap, 0f, -0.004f, R(Y(1.675f), 0.099f * hs, 0.109f * hs, 0f, Y(1.775f), 0.094f * hs, 0.103f * hs, 0f));          // a kufi cap
            if (headTie >= 0) b.Lathe(Body, headTie, 0f, -0.006f, R(Y(1.655f), 0.101f, 0.111f, 0f, Y(1.72f), 0.125f, 0.13f, 0f, Y(1.80f), 0.15f, 0.15f, 0f, Y(1.815f), 0.11f, 0.11f, 0f));   // a head-tie (gele)
            if (beard >= 0) b.Blob(Body, beard, new V3(0f, Y(1.535f), 0.056f), 0.052f, 0.044f, 0.036f, 3);
        }

        /// <summary>A hijab as one drape: from `bottom` (the waist or the chest) over the shoulders, close round the head, with the face left open (a face oval stands just proud of the cloth).</summary>
        static void Khimar(Builder b, int colour, int skin, float bottom, float hs = 1f)
        {
            float Y(float y) => 1.50f + (y - 1.50f) * hs; bool longOne = bottom < 1.15f;
            b.Lathe(Body, colour, 0f, 0f, R(bottom, longOne ? 0.268f : 0.25f, longOne ? 0.19f : 0.172f, 0f, 1.38f, 0.238f, 0.152f, 0f, 1.46f, 0.14f, 0.118f, 0f, Y(1.525f), 0.104f * hs, 0.108f * hs, 0f,
                Y(1.61f), 0.116f * hs, 0.122f * hs, -0.004f, Y(1.70f), 0.108f * hs, 0.116f * hs, -0.006f, Y(1.76f), 0.06f * hs, 0.068f * hs, -0.006f, Y(1.775f), 0.022f * hs, 0.026f * hs, -0.006f));
            b.Blob(Body, skin, new V3(0f, Y(1.622f), 0.078f * hs), 0.066f * hs, 0.082f * hs, 0.05f * hs, 4);                               // the face
        }

        static readonly float[] ShirtRings = { 0.90f, 0.168f, 0.118f, 0f, 1.08f, 0.152f, 0.108f, 0f, 1.28f, 0.182f, 0.128f, 0.004f, 1.395f, 0.19f, 0.116f, 0f, 1.44f, 0.14f, 0.098f, 0f, 1.465f, 0.072f, 0.066f, 0f };
        static readonly float[] KaftanRings = { 0.10f, 0.245f, 0.215f, 0f, 0.50f, 0.215f, 0.165f, 0f, 0.92f, 0.198f, 0.142f, 0f, 1.28f, 0.192f, 0.132f, 0.004f, 1.395f, 0.195f, 0.118f, 0f, 1.44f, 0.142f, 0.098f, 0f, 1.465f, 0.072f, 0.066f, 0f };
        static readonly float[] RigaRings = { 0.40f, 0.335f, 0.20f, 0f, 0.90f, 0.325f, 0.19f, 0f, 1.30f, 0.30f, 0.165f, 0f, 1.40f, 0.25f, 0.13f, 0f, 1.445f, 0.16f, 0.10f, 0f, 1.465f, 0.075f, 0.064f, 0f };
        static readonly float[] WrapperRings = { 0.08f, 0.21f, 0.19f, 0f, 0.50f, 0.198f, 0.165f, 0f, 0.95f, 0.218f, 0.162f, 0f, 1.04f, 0.178f, 0.13f, 0f };
        static readonly float[] BlouseRings = { 1.0f, 0.18f, 0.132f, 0f, 1.26f, 0.186f, 0.148f, 0.006f, 1.39f, 0.176f, 0.11f, 0f, 1.44f, 0.126f, 0.092f, 0f, 1.465f, 0.064f, 0.058f, 0f };
        static readonly float[] DressRings = { 0.06f, 0.25f, 0.218f, 0f, 0.60f, 0.215f, 0.165f, 0f, 0.95f, 0.206f, 0.15f, 0f, 1.08f, 0.182f, 0.132f, 0f, 1.27f, 0.186f, 0.144f, 0.005f, 1.39f, 0.176f, 0.11f, 0f, 1.44f, 0.126f, 0.092f, 0f, 1.465f, 0.064f, 0.058f, 0f };

        static void Kaftan(Builder b, int[] c, int skin, bool elder)
        {
            Shoes(b, Brown); Legs(b, c[0], 0.15f);
            b.Lathe(Body, c[0], 0f, 0f, KaftanRings);                                                                                           // the kaftan, to the ankle
            if (!b.Coarse) b.Box(Body, c[2], new V3(-0.024f, 1.20f, 0.11f), new V3(0.024f, 1.41f, 0.142f));                                     // the embroidered placket
            Arms(b, c[0], skin, 0.86f, 0.054f, 0.205f);
            Head(b, skin, cap: c[1], beard: elder ? Enamel : -1);
        }

        static void Riga(Builder b, int[] c, int skin)
        {
            Shoes(b, Brown); Legs(b, c[1]);                                                                                                      // the trousers show below the knee
            b.Lathe(Body, c[0], 0f, 0f, RigaRings);                                                                                             // the babban riga: wide, to below the knee
            b.Box(Body, c[2], new V3(-0.065f, 1.08f, 0.14f), new V3(0.065f, 1.37f, 0.178f));                                                    // the embroidery on the chest
            Arms(b, c[0], skin, 0.95f, 0.08f, 0.235f);                                                                                                 // its wide sleeves
            Head(b, skin, cap: c[1]);
        }

        static void ShirtTrousers(Builder b, int[] c, int skin)
        {
            Shoes(b, c[2]); Legs(b, c[1]);
            b.Lathe(Body, c[1], 0f, 0f, R(0.84f, 0.165f, 0.118f, 0f, 0.95f, 0.17f, 0.12f, 0f));                                                 // the seat and the belt line
            b.Lathe(Body, c[0], 0f, 0f, ShirtRings);
            Arms(b, c[0], skin, 1.14f, 0.05f, 0.20f);
            Head(b, skin, hair: Black);
        }

        static void Wrapper(Builder b, int[] c, int skin, bool headLoad)
        {
            Shoes(b, Black); Legs(b, Brown, 0.14f);
            b.Lathe(Body, c[0], 0f, 0f, WrapperRings);                                                                                          // the wrapper
            b.Lathe(Body, c[1], 0f, 0f, BlouseRings);                                                                                           // the blouse
            Arms(b, c[1], skin, 1.10f, 0.046f, 0.185f);
            if (headLoad)
            {
                Head(b, skin, headTie: c[1]);
                b.Lathe(Body, c[2], 0f, 0f, R(1.815f, 0.19f, 0.19f, 0f, 1.93f, 0.30f, 0.30f, 0f, 1.945f, 0.285f, 0.285f, 0f));                   // the enamel basin
                b.Blob(Body, Gold, new V3(0f, 1.94f, 0f), 0.25f, 0.07f, 0.25f, 3);                                                              // what is in it
            }
            else Khimar(b, c[0], skin, 1.22f);
        }

        static void LongHijab(Builder b, int[] c, int skin, float hs)
        {
            Shoes(b, c[2]); Legs(b, c[0], 0.13f);
            b.Lathe(Body, c[0], 0f, 0f, DressRings);                                                                                            // the long dress, uniform or abaya
            Arms(b, c[0], skin, 0.86f, 0.05f, 0.188f);
            Khimar(b, c[1], skin, 1.03f, hs);                                                                                                   // the long hijab falls to the waist
        }

        static void Schoolboy(Builder b, int[] c, int skin)
        {
            Shoes(b, Black);
            foreach (var (bone, x) in LegSides)
            {
                b.Lathe(bone, skin, x, 0f, R(0.075f, 0.042f, 0.046f, 0f, 0.60f, 0.056f, 0.06f, 0f));                                             // bare shins
                b.Lathe(bone, c[1], x, 0f, R(0.56f, 0.078f, 0.082f, 0f, Hip, 0.09f, 0.094f, 0f));                                                // shorts
            }
            b.Lathe(Body, c[1], 0f, 0f, R(0.84f, 0.165f, 0.118f, 0f, 0.95f, 0.17f, 0.12f, 0f));
            b.Lathe(Body, c[0], 0f, 0f, ShirtRings);
            b.Box(Body, c[2], new V3(-0.14f, 1.02f, -0.26f), new V3(0.14f, 1.36f, -0.115f));                                                    // the school bag
            Arms(b, c[0], skin, 1.14f, 0.046f, 0.195f);
            Head(b, skin, ChildHead, hair: Black);
        }

        // ------------------------------------------------------------------------------------------------ meshes and poses
        static readonly Dictionary<long, PersonMesh> _meshes = new Dictionary<long, PersonMesh>();
        static readonly Dictionary<long, V3[][]> _frames = new Dictionary<long, V3[][]>();
        static long Key(int design, int colourway, int skin, int lod) => ((long)design << 24) | ((long)colourway << 16) | ((long)skin << 8) | (long)lod;
        /// <summary>The pose cache key: a figure's geometry is the same in every colourway and skin tone (only the palette cells differ).</summary>
        public static int PoseKey(int design, int lod, int frame) => (Mod(design, Designs.Length) << 8) | (Math.Max(0, Math.Min(1, lod)) << 4) | Mod(frame, WalkFrames + IdleFrames);

        /// <summary>The mesh of a design in a colourway and skin tone, at LOD 0 (8-sided) or 1 (5-sided). Cached.</summary>
        public static PersonMesh Mesh(int design, int colourway, int skin, int lod)
        {
            design = Mod(design, Designs.Length); var d = Designs[design]; colourway = Mod(colourway, d.Colourways.Length); skin = Mod(skin, SkinCount); lod = Math.Max(0, Math.Min(1, lod));
            long k = Key(design, colourway, skin, lod);
            lock (_meshes)
            {
                if (_meshes.TryGetValue(k, out var m)) return m;
                var b = new Builder { Sides = lod == 0 ? 10 : 6 };
                d.Build(b, d.Colourways[colourway], Skin0 + skin);
                m = b.Done();
                if (d.Child) for (int i = 0; i < m.Pos.Length; i++) m.Pos[i] = m.Pos[i] * 0.72f;
                _meshes[k] = m; return m;
            }
        }

        static int Mod(int a, int n) => ((a % n) + n) % n;

        /// <summary>The pivot of a bone (hips and shoulders) for an adult; a child's is scaled with the mesh.</summary>
        public static V3 Pivot(int bone, bool child)
        {
            float k = child ? 0.72f : 1f;
            switch (bone) { case LegL: return new V3(-LegX, Hip, 0f) * k; case LegR: return new V3(LegX, Hip, 0f) * k; case ArmL: return new V3(-0.2f, Sh, 0f) * k; case ArmR: return new V3(0.2f, Sh, 0f) * k; default: return new V3(0f, 0f, 0f); }
        }

        /// <summary>Swing angle (degrees, about the x axis; positive moves the limb forward) of a bone at walk phase 0..1, and the body's bob (metres).</summary>
        public static float WalkAngle(int bone, float phase)
        {
            float s = (float)Math.Sin(2.0 * Math.PI * phase);
            switch (bone) { case LegL: return 26f * s; case LegR: return -26f * s; case ArmL: return -18f * s; case ArmR: return 18f * s; default: return 0f; }
        }
        public static float WalkBob(float phase) => 0.028f * Math.Abs((float)Math.Sin(2.0 * Math.PI * phase));

        /// <summary>The idle sway: arms hang with a slow small swing and the body shifts its weight.</summary>
        public static float IdleAngle(int bone, float phase)
        {
            float s = (float)Math.Sin(2.0 * Math.PI * phase);
            switch (bone) { case ArmL: return 3f * s; case ArmR: return -2.5f * s; default: return 0f; }
        }

        /// <summary>Posed positions of a mesh: walk frames 0..7 then idle frames 8..11. Cached per mesh.</summary>
        public static V3[] Frame(int design, int colourway, int skin, int lod, int frame)
        {
            design = Mod(design, Designs.Length); var d = Designs[design]; lod = Math.Max(0, Math.Min(1, lod));
            long k = Key(design, 0, 0, lod);
            V3[][] all;
            lock (_frames)
            {
                if (!_frames.TryGetValue(k, out all))
                {
                    var m = Mesh(design, 0, 0, lod); all = new V3[WalkFrames + IdleFrames][];
                    for (int f = 0; f < all.Length; f++) all[f] = Pose(m, d.Child, f < WalkFrames, (f < WalkFrames ? f : f - WalkFrames) / (float)(f < WalkFrames ? WalkFrames : IdleFrames), d.Swing);
                    _frames[k] = all;
                }
            }
            return all[Mod(frame, all.Length)];
        }

        /// <summary>Poses a mesh: every limb vertex is turned about its bone's pivot (x axis) by the walk or idle angle; the walk lifts the body by its bob.</summary>
        public static V3[] Pose(PersonMesh m, bool child, bool walk, float phase, float swing = 1f)
        {
            var o = new V3[m.Pos.Length]; float bob = walk ? WalkBob(phase) * (child ? 0.72f : 1f) * swing : 0f;
            var cos = new float[5]; var sin = new float[5];
            for (int b = 0; b < 5; b++)
            {
                double a = (walk ? WalkAngle(b, phase) * (b == LegL || b == LegR ? swing : 0.5f + 0.5f * swing) : IdleAngle(b, phase)) * Math.PI / 180.0;      // the arms follow a short stride with a smaller swing
                cos[b] = (float)Math.Cos(a); sin[b] = (float)Math.Sin(a);
            }
            for (int i = 0; i < o.Length; i++)
            {
                int b = m.Bone[i]; var p = m.Pos[i];
                if (b != Body)
                {
                    var pv = Pivot(b, child); float y = p.Y - pv.Y, z = p.Z - pv.Z;
                    p = new V3(p.X, pv.Y + y * cos[b] + z * sin[b], pv.Z + z * cos[b] - y * sin[b]);    // a positive angle swings the lower end (y below the pivot) forward, to +z
                }
                o[i] = new V3(p.X, p.Y + bob, p.Z);
            }
            return o;
        }
    }
}
