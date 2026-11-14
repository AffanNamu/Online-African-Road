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
    /// levels of detail. Colours come from one small palette texture (one material, one draw call for a whole crowd). A rig of five rigid parts (body, legs, arms) gives a walk cycle and an idle sway;
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
            /// <summary>Colourways: each is the list of garment colours this design's builder uses (main, second, accent).</summary>
            public int[][] Colourways;
            internal Action<Builder, int[], int> Build;      // (builder, colourway, skin)
        }

        public static readonly Design[] Designs =
        {
            new Design { Id = "man_kaftan_cap", What = "a man in an ankle-length kaftan and a kufi cap", Colourways = new[] { new[] { White, Brown, Cream }, new[] { Sky, Navy, White }, new[] { Cream, Maroon, Brown } }, Build = (b, c, s) => Kaftan(b, c, s, wide: false) },
            new Design { Id = "man_babban_riga", What = "a man in a wide babban riga over trousers, with a cap", Colourways = new[] { new[] { Royal, White, Maroon }, new[] { White, Cream, Gold }, new[] { Teal, Navy, Black } }, Build = (b, c, s) => Kaftan(b, c, s, wide: true) },
            new Design { Id = "man_shirt_trousers", What = "a man in a short-sleeved shirt and trousers", Colourways = new[] { new[] { Sky, Navy, Black }, new[] { White, DarkGrey, Brown }, new[] { Orange, Khaki, Brown } }, Build = ShirtTrousers },
            new Design { Id = "man_kaftan_plain", What = "an older man in a plain kaftan and cap", Colourways = new[] { new[] { Grey, White, DarkGrey }, new[] { Cream, Brown, Gold }, new[] { Green, White, Brown } }, Build = (b, c, s) => Kaftan(b, c, s, wide: false, elder: true) },
            new Design { Id = "woman_hijab_wrapper", What = "a woman in a hijab, blouse and wrapper", Colourways = new[] { new[] { Green, Gold, Black }, new[] { Purple, Pink, Black }, new[] { Maroon, Orange, Black } }, Build = (b, c, s) => Wrapper(b, c, s, headLoad: false) },
            new Design { Id = "woman_hijab_long", What = "a woman in a long dress and a long hijab", Colourways = new[] { new[] { Teal, Teal, Black }, new[] { Pink, Purple, Black }, new[] { Navy, Sky, Black } }, Build = LongHijab },
            new Design { Id = "woman_abaya", What = "a woman in a black abaya and hijab", Colourways = new[] { new[] { Black, Black, DarkGrey }, new[] { Navy, Black, DarkGrey }, new[] { DarkGrey, Black, Black } }, Build = LongHijab },
            new Design { Id = "woman_market_headload", What = "a market woman in a head-tie and wrapper, carrying an enamel basin on her head", Colourways = new[] { new[] { Orange, Gold, Enamel }, new[] { Green, Gold, Enamel }, new[] { Purple, Orange, Enamel } }, Build = (b, c, s) => Wrapper(b, c, s, headLoad: true) },
            new Design { Id = "boy_school", What = "a schoolboy in a shirt and shorts with a school bag", Child = true, Colourways = new[] { new[] { White, Navy, Brown }, new[] { Khaki, Brown, Black }, new[] { Sky, Navy, Maroon } }, Build = Schoolboy },
            new Design { Id = "girl_school_hijab", What = "a schoolgirl in a white hijab and a long uniform", Child = true, Colourways = new[] { new[] { Navy, White, Black }, new[] { Green, White, Black }, new[] { Maroon, White, Black } }, Build = LongHijab },
        };

        public static int SkinCount => 3;

        // ------------------------------------------------------------------------------------------------ building
        internal sealed class Builder
        {
            public readonly List<V3> P = new List<V3>(); public readonly List<V2> U = new List<V2>(); public readonly List<byte> B = new List<byte>(); public readonly List<int> T = new List<int>();
            public int Sides;    // 8 at LOD 0, 5 at LOD 1
            public bool Coarse => Sides < 8;
            int V(V3 p, int colour, int bone) { P.Add(p); U.Add(Cell(colour)); B.Add((byte)bone); return P.Count - 1; }

            /// <summary>A tapered elliptical tube along y from y0 (radius r0) to y1 (radius r1), centred at (x, z), depth scaled by sz; capped ends.</summary>
            public void Tube(int bone, int colour, float x, float z, float y0, float y1, float r0, float r1, float sz = 1f, bool cap0 = true, bool cap1 = true, float tilt = 0f)
            {
                int n = Sides, b0 = P.Count;
                for (int i = 0; i < n; i++)
                {
                    double a = 2.0 * Math.PI * i / n; float cx = (float)Math.Sin(a), cz = (float)Math.Cos(a);
                    V(new V3(x + cx * r0, y0, z + cz * r0 * sz), colour, bone); V(new V3(x + cx * r1, y1, z + cz * r1 * sz + tilt), colour, bone);
                }
                for (int i = 0; i < n; i++) { int j = (i + 1) % n, a0 = b0 + 2 * i, a1 = a0 + 1, c0 = b0 + 2 * j, c1 = c0 + 1; T.Add(a0); T.Add(c0); T.Add(a1); T.Add(a1); T.Add(c0); T.Add(c1); }
                if (cap0) { int c = V(new V3(x, y0, z), colour, bone); for (int i = 0; i < n; i++) { int j = (i + 1) % n; T.Add(c); T.Add(b0 + 2 * i); T.Add(b0 + 2 * j); } }
                if (cap1) { int c = V(new V3(x, y1, z + tilt), colour, bone); for (int i = 0; i < n; i++) { int j = (i + 1) % n; T.Add(c); T.Add(b0 + 2 * j + 1); T.Add(b0 + 2 * i + 1); } }
            }

            /// <summary>An ellipsoid of radii (rx, ry, rz) at c, as a low-poly lathe (rings x sides).</summary>
            public void Blob(int bone, int colour, V3 c, float rx, float ry, float rz)
            {
                int n = Sides, rings = Coarse ? 3 : 4, b0 = P.Count;
                int top = V(new V3(c.X, c.Y + ry, c.Z), colour, bone);
                for (int k = 1; k < rings; k++)
                {
                    double t = Math.PI * k / rings; float y = (float)Math.Cos(t), rr = (float)Math.Sin(t);
                    for (int i = 0; i < n; i++) { double a = 2.0 * Math.PI * i / n; V(new V3(c.X + (float)Math.Sin(a) * rr * rx, c.Y + y * ry, c.Z + (float)Math.Cos(a) * rr * rz), colour, bone); }
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

        // proportions of an adult of about 1.72 m (a child is the adult scaled by 0.72)
        const float Hip = 0.92f, Shoulder = 1.43f, HeadY = 1.60f;

        static void Legs(Builder b, int trouser, int shoe, float hemY = 0f)
        {
            foreach (var (bone, x) in new[] { (LegL, -0.095f), (LegR, 0.095f) })
            {
                float top = hemY > 0f ? Math.Min(Hip, hemY + 0.12f) : Hip;                                                            // under a robe only the shins show
                b.Tube(bone, trouser, x, 0f, 0.07f, top, 0.058f, 0.058f + 0.027f * (top - 0.07f) / (Hip - 0.07f), 1f, false, false);
                b.Box(bone, shoe, new V3(x - 0.05f, 0f, -0.06f), new V3(x + 0.05f, 0.075f, 0.16f));                                   // shoe or sandal
            }
        }

        static void Arms(Builder b, int sleeve, int skin, float sleeveTo, float r = 0.05f)
        {
            foreach (var (bone, x) in new[] { (ArmL, -0.215f), (ArmR, 0.215f) })
            {
                b.Tube(bone, sleeve, x, 0f, sleeveTo, Shoulder, r * 1.15f, r * 1.25f, 1f, false, true);
                if (sleeveTo > 0.86f) b.Tube(bone, skin, x, 0f, 0.80f, sleeveTo, r * 0.8f, r, 1f, true, false);                        // forearm
                b.Blob(bone, skin, new V3(x, 0.76f, 0f), 0.04f, 0.055f, 0.035f);                                                       // hand
            }
        }

        static void Head(Builder b, int skin, int cap = -1, int hijab = -1, int headTie = -1)
        {
            b.Tube(Body, skin, 0f, 0f, Shoulder - 0.02f, HeadY - 0.08f, 0.055f, 0.05f, 1f, false, false);                              // neck
            b.Blob(Body, skin, new V3(0f, HeadY + 0.03f, 0.01f), 0.095f, 0.118f, 0.105f);                                                  // head
            if (cap >= 0) b.Tube(Body, cap, 0f, 0.0f, HeadY + 0.09f, HeadY + 0.17f, 0.097f, 0.092f, 1f, false, true);                 // kufi cap
            if (hijab >= 0)
            {
                b.Blob(Body, hijab, new V3(0f, HeadY + 0.035f, -0.04f), 0.112f, 0.135f, 0.118f);                                             // over the head, the face left open towards +z
                b.Tube(Body, hijab, 0f, 0f, Shoulder - 0.20f, HeadY - 0.04f, 0.27f, 0.11f, 0.8f, true, false);                          // over the shoulders
            }
            if (headTie >= 0) b.Tube(Body, headTie, 0f, -0.01f, HeadY + 0.06f, HeadY + 0.19f, 0.112f, 0.13f, 1f, false, true);        // a head-tie (gele)
        }

        static void Kaftan(Builder b, int[] c, int skin, bool wide, bool elder = false)
        {
            Legs(b, wide ? c[1] : Brown, Brown, wide ? 0.4f : 0.12f);
            if (wide)
            {
                b.Tube(Body, c[1], 0f, 0f, 0.12f, 0.60f, 0.16f, 0.17f, 0.8f);                                                               // the trousers' wide top under the riga
                b.Tube(Body, c[0], 0f, 0f, 0.38f, Shoulder, 0.40f, 0.25f, 0.62f);                                                            // the babban riga: wide, to below the knee
                b.Tube(Body, c[2], 0f, 0.10f, 1.05f, 1.36f, 0.07f, 0.05f, 0.4f, false, false);                                               // embroidery on the chest
            }
            else
            {
                b.Tube(Body, c[0], 0f, 0f, 0.10f, Shoulder, 0.25f, 0.21f, 0.72f);                                                            // the kaftan, to the ankle
                if (!b.Coarse) b.Tube(Body, c[2], 0f, 0.155f, 1.18f, 1.40f, 0.035f, 0.03f, 0.4f, false, false);                              // the neck embroidery
            }
            Arms(b, c[0], skin, 0.88f, wide ? 0.075f : 0.052f);
            Head(b, skin, cap: c[1]);
        }

        static void ShirtTrousers(Builder b, int[] c, int skin)
        {
            Legs(b, c[1], c[2]);
            b.Tube(Body, c[1], 0f, 0f, Hip - 0.06f, Hip + 0.06f, 0.17f, 0.17f, 0.75f);                                                       // belt line
            b.Tube(Body, c[0], 0f, 0f, Hip, Shoulder, 0.17f, 0.21f, 0.68f);                                                                  // shirt
            Arms(b, c[0], skin, 1.20f, 0.05f);
            Head(b, skin);
        }

        static void Wrapper(Builder b, int[] c, int skin, bool headLoad)
        {
            Legs(b, Brown, Black, 0.1f);
            b.Tube(Body, c[0], 0f, 0f, 0.08f, 1.02f, 0.21f, 0.20f, 0.75f);                                                                  // the wrapper
            b.Tube(Body, c[1], 0f, 0f, 1.0f, Shoulder, 0.19f, 0.20f, 0.70f);                                                                 // the blouse
            Arms(b, c[1], skin, 1.12f, 0.048f);
            if (headLoad)
            {
                Head(b, skin, headTie: c[1]);
                b.Tube(Body, c[2], 0f, 0f, HeadY + 0.19f, HeadY + 0.31f, 0.20f, 0.30f, 1f, true, true);                                     // the enamel basin
                b.Tube(Body, Gold, 0f, 0f, HeadY + 0.31f, HeadY + 0.37f, 0.26f, 0.18f, 1f, false, true);                                    // what is in it
            }
            else Head(b, skin, hijab: c[0]);
        }

        static void LongHijab(Builder b, int[] c, int skin)
        {
            Legs(b, c[0], c[2], 0.06f);
            b.Tube(Body, c[0], 0f, 0f, 0.06f, Shoulder, 0.25f, 0.20f, 0.74f);                                                                // the long dress or abaya
            Arms(b, c[0], skin, 0.84f, 0.055f);
            Head(b, skin, hijab: c[1]);
            b.Tube(Body, c[1], 0f, 0f, 1.0f, Shoulder - 0.05f, 0.25f, 0.26f, 0.72f, true, false);                                          // the long hijab falls to the waist
        }

        static void Schoolboy(Builder b, int[] c, int skin)
        {
            foreach (var (bone, x) in new[] { (LegL, -0.095f), (LegR, 0.095f) })
            {
                b.Tube(bone, skin, x, 0f, 0.07f, 0.62f, 0.05f, 0.06f, 1f, false, false);                                                    // bare shins
                b.Tube(bone, c[1], x, 0f, 0.62f, Hip, 0.085f, 0.09f, 1f, false, false);                                                     // shorts
                b.Box(bone, Black, new V3(x - 0.05f, 0f, -0.06f), new V3(x + 0.05f, 0.075f, 0.16f));
            }
            b.Tube(Body, c[1], 0f, 0f, Hip - 0.08f, Hip + 0.04f, 0.17f, 0.17f, 0.75f);
            b.Tube(Body, c[0], 0f, 0f, Hip, Shoulder, 0.17f, 0.20f, 0.68f);
            b.Box(Body, c[2], new V3(-0.15f, 1.0f, -0.29f), new V3(0.15f, 1.38f, -0.13f));                                                  // the school bag
            Arms(b, c[0], skin, 1.20f, 0.047f);
            Head(b, skin);
        }

        // ------------------------------------------------------------------------------------------------ meshes and poses
        static readonly Dictionary<long, PersonMesh> _meshes = new Dictionary<long, PersonMesh>();
        static readonly Dictionary<long, V3[][]> _frames = new Dictionary<long, V3[][]>();
        static long Key(int design, int colourway, int skin, int lod) => ((long)design << 24) | ((long)colourway << 16) | ((long)skin << 8) | (long)lod;

        /// <summary>The mesh of a design in a colourway and skin tone, at LOD 0 (8-sided) or 1 (5-sided). Cached.</summary>
        public static PersonMesh Mesh(int design, int colourway, int skin, int lod)
        {
            design = Mod(design, Designs.Length); var d = Designs[design]; colourway = Mod(colourway, d.Colourways.Length); skin = Mod(skin, SkinCount); lod = Math.Max(0, Math.Min(1, lod));
            long k = Key(design, colourway, skin, lod);
            lock (_meshes)
            {
                if (_meshes.TryGetValue(k, out var m)) return m;
                var b = new Builder { Sides = lod == 0 ? 8 : 5 };
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
            switch (bone) { case LegL: return new V3(-0.095f, Hip, 0f) * k; case LegR: return new V3(0.095f, Hip, 0f) * k; case ArmL: return new V3(-0.215f, Shoulder - 0.02f, 0f) * k; case ArmR: return new V3(0.215f, Shoulder - 0.02f, 0f) * k; default: return new V3(0f, 0f, 0f); }
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
            design = Mod(design, Designs.Length); var d = Designs[design]; colourway = Mod(colourway, d.Colourways.Length); skin = Mod(skin, SkinCount); lod = Math.Max(0, Math.Min(1, lod));
            long k = Key(design, colourway, skin, lod);
            V3[][] all;
            lock (_frames)
            {
                if (!_frames.TryGetValue(k, out all))
                {
                    var m = Mesh(design, colourway, skin, lod); all = new V3[WalkFrames + IdleFrames][];
                    for (int f = 0; f < all.Length; f++) all[f] = Pose(m, d.Child, f < WalkFrames, (f < WalkFrames ? f : f - WalkFrames) / (float)(f < WalkFrames ? WalkFrames : IdleFrames));
                    _frames[k] = all;
                }
            }
            return all[Mod(frame, all.Length)];
        }

        /// <summary>Poses a mesh: every limb vertex is turned about its bone's pivot (x axis) by the walk or idle angle; the walk lifts the body by its bob.</summary>
        public static V3[] Pose(PersonMesh m, bool child, bool walk, float phase)
        {
            var o = new V3[m.Pos.Length]; float bob = walk ? WalkBob(phase) * (child ? 0.72f : 1f) : 0f;
            var cos = new float[5]; var sin = new float[5];
            for (int b = 0; b < 5; b++) { double a = (walk ? WalkAngle(b, phase) : IdleAngle(b, phase)) * Math.PI / 180.0; cos[b] = (float)Math.Cos(a); sin[b] = (float)Math.Sin(a); }
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
