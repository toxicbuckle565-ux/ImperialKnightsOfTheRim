using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LIK
{
    /// <summary>
    /// Reactor meltdown visuals, v2 (user 2026-11-22: Blender fireball approved - 'lets go with in game visuals').
    ///  - FIREBALL + MUSHROOM: a Mantaflow fire/smoke simulation (Tools/meltdown_blender_sim.py) played as a flipbook, two layers:
    ///    soot (alpha blended) + fire (additive glow), cross-faded between frames so it plays smooth while slowed down to a giant's pace.
    ///  - EXPOSURE: a white-out for an instant, then the world goes dark around the blast; the darkness follows the fireball's real LIGHT
    ///    (measured from each rendered frame, MeltdownFxData.Light) with a ~1 s eye-adaptation lag, with a warm cast while it burns.
    ///  - GROUND: shockwave ring + dust kicked up along it, a tan base surge, a smoke ring round the stem, the crater glowing red-hot.
    /// Mushroom scale: 1 = Dominus / Acastus, 0.6 = Questoris / Cerastus, 0 = Armiger (a short fireball, no mushroom). Visual only, not saved.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MeltdownFxArt
    {
        public static readonly Material Puff, Dark, Additive, Ring, Smoke, Fire;
        public static readonly Mesh[] Cells;
        public static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();

        static MeltdownFxArt()
        {
            Puff = MaterialPool.MatFrom(new MaterialRequest(MakePuff(), ShaderDatabase.Transparent));
            Texture2D soft = MakeSoft();
            Dark = MaterialPool.MatFrom(new MaterialRequest(soft, ShaderDatabase.Transparent));
            Additive = MaterialPool.MatFrom(new MaterialRequest(soft, ShaderDatabase.MoteGlow));
            Ring = MaterialPool.MatFrom(new MaterialRequest(MakeRing(), ShaderDatabase.MoteGlow));
            Smoke = MaterialPool.MatFrom(new MaterialRequest(ContentFinder<Texture2D>.Get("Things/LIK_Meltdown/MeltdownSmoke"), ShaderDatabase.Transparent));
            Fire = MaterialPool.MatFrom(new MaterialRequest(ContentFinder<Texture2D>.Get("Things/LIK_Meltdown/MeltdownFire"), ShaderDatabase.MoteGlow));
            Cells = new Mesh[MeltdownFxData.Frames];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = CellMesh(i);
        }

        /// <summary>A unit plane (like MeshPool.plane10) showing one frame of the atlas.</summary>
        static Mesh CellMesh(int i)
        {
            int col = i % MeltdownFxData.Cols, row = i / MeltdownFxData.Cols;
            float u0 = col / (float)MeltdownFxData.Cols, u1 = (col + 1) / (float)MeltdownFxData.Cols;
            float v1 = 1f - row / (float)MeltdownFxData.Rows, v0 = 1f - (row + 1) / (float)MeltdownFxData.Rows;   // png row 0 = top
            var m = new Mesh { name = "LIK_MeltdownCell" + i };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A lumpy soft smoke puff: a round blob with value noise in it (white; tinted per draw).</summary>
        static Texture2D MakePuff()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LIK_MeltdownPuff" };
            var px = new Color[S * S];
            var rnd = new System.Random(4242);
            float[,] g = new float[9, 9];
            for (int y = 0; y < 9; y++) for (int x = 0; x < 9; x++) g[x, y] = (float)rnd.NextDouble();
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (S - 1f), v = y / (S - 1f);
                    float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float gx = u * 8f, gy = v * 8f; int ix = Mathf.Min(7, (int)gx), iy = Mathf.Min(7, (int)gy);
                    float fx = gx - ix, fy = gy - iy; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                    float n = Mathf.Lerp(Mathf.Lerp(g[ix, iy], g[ix + 1, iy], fx), Mathf.Lerp(g[ix, iy + 1], g[ix + 1, iy + 1], fx), fy);
                    float a = Mathf.Clamp01(1f - r * (0.75f + 0.45f * n));
                    a = a * a * (3 - 2 * a);
                    float shade = 0.75f + 0.25f * n;
                    px[y * S + x] = new Color(shade, shade, shade, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D MakeSoft()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LIK_MeltdownSoft" };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x / (S - 1f) - 0.5f, dy = y / (S - 1f) - 0.5f;
                    float q = Mathf.Clamp01(1f - (dx * dx + dy * dy) * 4f);  // reaches 0 at the circle's edge (no square edges)
                    float a = q * q * q;
                    px[y * S + x] = new Color(a, a, a, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>The shockwave: a soft bright annulus with a sharper outer front.</summary>
        static Texture2D MakeRing()
        {
            const int S = 256;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LIK_MeltdownRing" };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x / (S - 1f) - 0.5f, dy = y / (S - 1f) - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = r > 1f ? 0f : (r > 0.9f ? (1f - r) / 0.1f : Mathf.Pow(Mathf.Clamp01((r - 0.55f) / 0.35f), 2f));
                    px[y * S + x] = new Color(a, a, a, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static void Quad(Material m, Vector3 c, float alt, float w, float h, Color col, float rot = 0f)
        {
            Mpb.SetColor(ShaderPropertyIDs.Color, col);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(new Vector3(c.x, alt, c.z), Quaternion.AngleAxis(rot, Vector3.up), new Vector3(w, 1f, h)), m, 0, null, 0, Mpb);
        }

        public static void Frame(Material m, int i, Vector3 c, float alt, float size, Color col)
        {
            Mpb.SetColor(ShaderPropertyIDs.Color, col);
            Graphics.DrawMesh(Cells[Mathf.Clamp(i, 0, Cells.Length - 1)], Matrix4x4.TRS(new Vector3(c.x, alt, c.z), Quaternion.identity, new Vector3(size, 1f, size)), m, 0, null, 0, Mpb);
        }
    }

    public class MapComponent_MeltdownFx : MapComponent
    {
        class Fx { public Vector3 c; public float r; public float mush; public int start; public int seed; }
        readonly List<Fx> fx = new List<Fx>();
        const float Play = 12f;                                    // the simulation plays over 12 s (slowed: a giant's fireball)
        const float Life = 17f;                                    // seconds until everything is gone

        public MapComponent_MeltdownFx(Map map) : base(map) { }

        /// <summary>mushroom: 0 = none (Armiger), 0.6 smaller (Questoris, Cerastus), 1 = Dominus / Acastus.</summary>
        public void Start(Vector3 c, float radius, float mushroom)
        {
            fx.Add(new Fx { c = c, r = radius, mush = mushroom, start = Find.TickManager.TicksGame, seed = Rand.Int });
        }

        float T(Fx f) { return (Find.TickManager.TicksGame - f.start) / 60f; }
        static float Smooth(float a) { a = Mathf.Clamp01(a); return a * a * (3f - 2f * a); }

        /// <summary>Flipbook position (frames, fractional) at time t: quick at first (the burst), easing into the slow climb.</summary>
        static float FramePos(float t, bool small)
        {
            float play = small ? Play * 0.6f : Play;
            return Mathf.Pow(Mathf.Clamp01(t / play), 0.85f) * (MeltdownFxData.Frames - 1);
        }

        static float LightAt(float t, bool small)
        {
            if (t < 0f) return 1f;
            float fp = FramePos(t, small); int i = Mathf.Min((int)fp, MeltdownFxData.Frames - 2);
            return Mathf.Lerp(MeltdownFxData.Light[i], MeltdownFxData.Light[i + 1], fp - i);
        }

        public override void MapComponentUpdate()
        {
            if (fx.Count == 0) return;
            fx.RemoveAll(f => T(f) > Life);
            if (Find.CurrentMap != map) return;
            float altDark = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.05f;
            float altCloud = AltitudeLayer.MetaOverlays.AltitudeFor() - 0.05f;
            foreach (Fx f in fx)
            {
                float t = T(f), r = f.r;
                bool small = f.mush <= 0f;
                float s = r * (small ? 0.5f : f.mush);
                // EXPOSURE follows the fireball's light, lagged ~1 s like an eye adapting
                float light = Mathf.Max(LightAt(t, small), LightAt(t - 1f, small) * 0.85f);
                float dark = Smooth(t / 0.3f) * Smooth((Mathf.Pow(light, 0.6f) - 0.05f) / 0.5f) * Mathf.Clamp01(r / 12f + 0.35f);
                if (dark > 0.01f)
                {
                    MeltdownFxArt.Quad(MeltdownFxArt.Dark, f.c, altDark, r * 7f, r * 7f, new Color(0f, 0f, 0.02f, 0.93f * dark));
                    MeltdownFxArt.Quad(MeltdownFxArt.Dark, f.c, altDark + 0.001f, r * 14f, r * 14f, new Color(0f, 0f, 0.02f, 0.6f * dark));
                }
                // the light thrown on everything around (warm, strongest while it is dark)
                MeltdownFxArt.Quad(MeltdownFxArt.Additive, f.c + new Vector3(0f, 0f, s * 0.6f), altDark + 0.005f, r * 6f, r * 5f, new Color(0.85f, 0.28f, 0.06f) * (0.55f * light * (0.4f + 0.6f * dark)));
                DrawGround(f, t, s, light, altDark + 0.01f);
                // the CRATER glowing red-hot long after
                float cr = Smooth(t / 0.5f) * (1f - Smooth((t - 4f) / (Life - 4f)));
                MeltdownFxArt.Quad(MeltdownFxArt.Additive, f.c, altDark + 0.015f, r * 1.3f, r * 1.3f, new Color(0.85f, 0.18f, 0.03f) * (0.7f * cr));
                DrawCloud(f, t, s, small, altCloud);
            }
        }

        void DrawGround(Fx f, float t, float s, float light, float alt)
        {
            var rnd = new System.Random(f.seed);
            float r = f.r;
            // SHOCKWAVE: a bright front racing out, dust kicked up along it
            float sw = Smooth(t / 1.6f), fw = 1f - Smooth((t - 0.9f) / 2.4f);
            if (sw > 0.02f && fw > 0.01f)
            {
                float R = r * (0.6f + 5f * sw);
                MeltdownFxArt.Quad(MeltdownFxArt.Ring, f.c, alt, R * 2f, R * 2f, new Color(1f, 0.85f, 0.6f) * (0.75f * fw));
                Color dust = Color.Lerp(new Color(0.85f, 0.5f, 0.28f), new Color(0.45f, 0.39f, 0.33f), sw);
                for (int i = 0; i < 70; i++)
                {
                    float a = i / 70f * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.08f, d = 0.82f + 0.16f * (float)rnd.NextDouble();
                    Vector3 p = f.c + new Vector3(Mathf.Cos(a) * R * d, 0f, Mathf.Sin(a) * R * d);
                    float w = r * (0.45f + 0.3f * (float)rnd.NextDouble()) * (0.6f + 0.8f * sw);
                    Color col = dust; col.a = 0.4f * fw;
                    MeltdownFxArt.Quad(MeltdownFxArt.Puff, p, alt + 0.001f + i * 0.00001f, w * 1.3f, w, col, a * Mathf.Rad2Deg + 90f);
                }
            }
            // BASE SURGE: pale tan dust rolling out round the foot of the stem, lit by the fireball
            float surgeR = s * (0.45f + 1.15f * Smooth((t - 0.2f) / 4.5f));
            float sa = Mathf.Min(1f, t / 0.6f) * (1f - Smooth((t - 8f) / 7f));
            if (sa > 0.01f)
            {
                Color tan = Color.Lerp(new Color(0.78f, 0.66f, 0.52f), new Color(0.42f, 0.34f, 0.27f), Smooth((t - 1f) / 6f));
                tan = new Color(Mathf.Min(1.2f, tan.r + 0.35f * light), Mathf.Min(1.2f, tan.g + 0.21f * light), Mathf.Min(1.2f, tan.b + 0.09f * light));
                for (int i = 0; i < 36; i++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = 0.3f + 0.7f * Mathf.Sqrt((float)rnd.NextDouble());
                    Vector3 p = f.c + new Vector3(Mathf.Cos(a) * surgeR * d, 0f, Mathf.Sin(a) * surgeR * d * 0.8f);
                    float w = s * (0.7f + 0.45f * (float)rnd.NextDouble()) * (0.5f + 0.6f * Smooth((t - 0.2f) / 3f));
                    Color col = tan; col.a = 0.8f * sa;
                    MeltdownFxArt.Quad(MeltdownFxArt.Puff, p, alt + 0.003f + i * 0.00002f, w, w * 0.8f, col, (float)rnd.NextDouble() * 360f + t * 4f);
                }
            }
        }

        void DrawCloud(Fx f, float t, float s, bool small, float alt)
        {
            float size = s * 3f;                                       // one frame = 34 sim units; the burst/blast fills the lower part
            Vector3 centre = f.c + new Vector3(0f, 0f, size * (0.5f - MeltdownFxData.GroundV));
            float fade = small ? 1f - Smooth((t - 3.5f) / 3f) : 1f - Smooth((t - Play) / (Life - Play - 0.5f));
            if (fade <= 0.005f) return;
            float fp = FramePos(t, small);
            int i = Mathf.Min((int)fp, MeltdownFxData.Frames - 1), j = Mathf.Min(i + 1, MeltdownFxData.Frames - 1);
            float k = fp - i;
            float a = Smooth(t / 0.08f) * fade;
            // SMOKE ring round the stem (smoke, not glow), once the head has spread
            float ringA = small ? 0f : Smooth((t - 3.5f) / 1.5f) * (1f - Smooth((t - 10f) / 4f)) * fade;
            if (ringA > 0.01f)
            {
                var rnd = new System.Random(f.seed + 7);
                Vector3 rc = f.c + new Vector3(0f, 0f, size * 0.2f);
                float rr = size * 0.17f;
                for (int n = 0; n < 40; n++)
                {
                    float an = n / 40f * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.1f;
                    Vector3 p = rc + new Vector3(Mathf.Cos(an) * rr, 0f, Mathf.Sin(an) * rr * 0.3f);
                    float w = s * (0.35f + 0.2f * (float)rnd.NextDouble());
                    Color col = new Color(0.48f, 0.45f, 0.42f, 0.3f * ringA);
                    MeltdownFxArt.Quad(MeltdownFxArt.Puff, p, alt - 0.002f + (Mathf.Sin(an) > 0f ? 0f : 0.004f), w * 1.3f, w * 0.5f, col, (float)rnd.NextDouble() * 360f);
                }
            }
            // soot (alpha) then fire (glow), each cross-faded between two frames
            MeltdownFxArt.Frame(MeltdownFxArt.Smoke, i, centre, alt, size, new Color(1f, 1f, 1f, a));
            if (k > 0.01f) MeltdownFxArt.Frame(MeltdownFxArt.Smoke, j, centre, alt + 0.001f, size, new Color(1f, 1f, 1f, a * k));
            MeltdownFxArt.Frame(MeltdownFxArt.Fire, i, centre, alt + 0.002f, size, new Color(1f, 1f, 1f) * (1.15f * a * (1f - k)));
            if (k > 0.01f) MeltdownFxArt.Frame(MeltdownFxArt.Fire, j, centre, alt + 0.003f, size, new Color(1f, 1f, 1f) * (1.15f * a * k));
        }

        /// <summary>The overexposure: the whole screen whites out for an instant.</summary>
        public override void MapComponentOnGUI()
        {
            if (fx.Count == 0 || Find.CurrentMap != map || Event.current.type != EventType.Repaint) return;
            float a = 0f;
            foreach (Fx f in fx) { float t = T(f); a = Mathf.Max(a, (1f - Smooth(t / 0.7f)) * Mathf.Clamp01(f.r / 12f + 0.35f)); }
            if (a > 0.01f) Widgets.DrawBoxSolid(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), new Color(1f, 0.97f, 0.9f, 0.85f * a));
        }
    }
}
