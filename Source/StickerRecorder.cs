using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace LIK
{
    /// <summary>
    /// STICKER RECORDER (user 2026-11-17: "the sticker map is very buggy on the armiger ... add a recorder to see the issue, it's a bit hard to
    /// describe"). Switched on with the 'Record' button in the sticker editor. While on, every change the player makes is saved to
    /// &lt;RimWorld&gt;/LIK_StickerRecorder/&lt;class&gt;_&lt;time&gt;/:
    ///   snap_NNN.png  the Knight in all 4 facings (2 x 2) with the stickers exactly as the game builds them (same Stickers.Build), drawn over
    ///                 the painted body picture; a red cross marks the last click on the facing it was made on
    ///   log.txt       every click / drag (facing, picture pixel, what the surface map returned) and the full sticker list at each snapshot
    /// Nothing is recorded while it is off; it costs nothing then.
    /// </summary>
    public static class StickerRecorder
    {
        public static bool On;
        public static string Dir;
        static int snaps;
        static string lastClickFacing; static int lastClickX = -1, lastClickY = -1;

        public static void Start(KnightClassDef cls)
        {
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "LIK_StickerRecorder");
            Dir = Path.Combine(root, (cls?.defName ?? "Knight").Replace("LIK_Class_", "") + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(Dir);
            snaps = 0; On = true; lastClickX = -1;
            Log("recording started for " + cls?.defName + " (vehicle " + cls?.vehicle?.defName + ", body " + cls?.vehicle?.graphicData?.texPath + ")");
        }

        public static void Stop() { if (On) Log("recording stopped after " + snaps + " snapshots"); On = false; }

        public static void Log(string s)
        {
            if (!On || Dir == null) return;
            try { File.AppendAllText(Path.Combine(Dir, "log.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\n"); }
            catch (Exception e) { Verse.Log.ErrorOnce("[LIK] sticker recorder log: " + e.Message, 77171120); }
        }

        public static void Click(string what, string facing, int x, int y, bool got, Vector3 pos, Vector3 nrm, int layer)
        {
            if (!On) return;
            lastClickFacing = facing; lastClickX = x; lastClickY = y;
            Log(string.Format("{0} facing={1} px=({2},{3}) surface={4} pos={5} normal={6} layer={7}", what, facing, x, y, got ? "yes" : "NO", V(pos), V(nrm), layer));
        }

        static string V(Vector3 v) { return string.Format("({0:0.00},{1:0.00},{2:0.00})", v.x, v.y, v.z); }

        static string StickerLine(int i, KnightSticker s)
        {
            return string.Format("  #{0} {1} pos={2} normal={3} size={4:0.#} angle={5:0} layer={6} flip={7} erase={8} wrap={9} tiles={10:0.#} overTrim={11} pieceOnly={12}",
                i, s.design, V(s.pos), V(s.normal), s.size, s.angle, s.layer, s.flip, s.erase, s.wrap, s.tiles, s.overTrim, s.pieceOnly);
        }

        /// <summary>Picture of the painted body for a facing (the coloured original from Tools/paint_src if there is one).</summary>
        static Color32[] Body(KnightClassDef cls, string facing, int w, int h)
        {
            string tex = cls?.vehicle?.graphicData?.texPath;
            string root = AlignTool.Root;
            if (tex == null || root == null) return null;
            foreach (string p in new[] { Path.Combine(root, "Tools", "paint_src", tex + "_" + facing + ".png"), Path.Combine(root, "Textures", tex + "_" + facing + ".png") })
            {
                if (!File.Exists(p)) continue;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(p))) { UnityEngine.Object.Destroy(t); continue; }
                Color32[] px = t.width == w && t.height == h ? t.GetPixels32() : null;
                UnityEngine.Object.Destroy(t);
                return px;
            }
            return null;
        }

        /// <summary>Builds every facing exactly like the game and saves the 2 x 2 sheet + the sticker list.</summary>
        public static void Snapshot(KnightClassDef cls, List<KnightSticker> list, string why)
        {
            if (!On || cls == null) return;
            try
            {
                snaps++;
                var sb = new StringBuilder();
                sb.AppendLine(string.Format("SNAPSHOT {0:000} ({1}) - {2} sticker(s)", snaps, why, list?.Count ?? 0));
                if (list != null) for (int i = 0; i < list.Count; i++) sb.AppendLine(StickerLine(i, list[i]));
                Color32[] sheet = null; int W = 0, H = 0;
                for (int fi = 0; fi < 4; fi++)
                {
                    string f = Stickers.Facings[fi];
                    StickerMap m = Stickers.Map(cls, "base", f);
                    if (m == null) { sb.AppendLine("  " + f + ": NO MAP"); continue; }
                    if (sheet == null) { W = m.w; H = m.h; sheet = new Color32[W * 2 * H * 2]; for (int i = 0; i < sheet.Length; i++) sheet[i] = new Color32(40, 42, 46, 255); }
                    Color32[] img = Body(cls, f, m.w, m.h) ?? new Color32[m.w * m.h];
                    Stickers.KeepPixels = true;
                    List<Stickers.LayerTex> layers;
                    try { layers = Stickers.Build(cls, "base", f, list); }
                    finally { Stickers.KeepPixels = false; }
                    int drawn = 0;
                    foreach (Stickers.LayerTex t in layers)
                    {
                        if (t.px == null) continue;
                        for (int ry = 0; ry < t.h; ry++)
                            for (int rx = 0; rx < t.w; rx++)
                            {
                                Color32 s = t.px[ry * t.w + rx];                 // sticker buffer: bottom-up, map row = y0 + h-1-ry
                                if (s.a == 0) continue;
                                int mx = t.x0 + rx, my = t.y0 + (t.h - 1 - ry);
                                if (mx < 0 || my < 0 || mx >= m.w || my >= m.h) continue;
                                int bi = (m.h - 1 - my) * m.w + mx;                // body: bottom-up too
                                Color32 b = img[bi]; float a = s.a / 255f;
                                img[bi] = new Color32((byte)(b.r + (s.r - b.r) * a), (byte)(b.g + (s.g - b.g) * a), (byte)(b.b + (s.b - b.b) * a), (byte)Mathf.Max(b.a, s.a));
                                drawn++;
                            }
                        sb.AppendLine(string.Format("  {0}: layer {1} box x {2}-{3} y {4}-{5}", f, t.layer, t.x0, t.x0 + t.w - 1, t.y0, t.y0 + t.h - 1));
                    }
                    sb.AppendLine(string.Format("  {0}: {1} layer picture(s), {2} sticker pixels drawn", f, layers.Count, drawn));
                    Stickers.Release(layers);
                    // last click: red cross
                    if (f == lastClickFacing && lastClickX >= 0)
                        for (int d = -12; d <= 12; d++)
                            foreach (var q in new[] { new Vector2Int(lastClickX + d, lastClickY), new Vector2Int(lastClickX, lastClickY + d) })
                                if (q.x >= 0 && q.y >= 0 && q.x < m.w && q.y < m.h) img[(m.h - 1 - q.y) * m.w + q.x] = new Color32(255, 30, 30, 255);
                    // place on the sheet: north top-left, east top-right, south bottom-left, west bottom-right (rows bottom-up)
                    int ox = (fi % 2) * W, oy = (fi < 2 ? 1 : 0) * H;
                    for (int y = 0; y < Mathf.Min(H, m.h); y++)
                        for (int x = 0; x < Mathf.Min(W, m.w); x++)
                        {
                            Color32 c = img[y * m.w + x];
                            if (c.a == 0) continue;
                            sheet[(oy + y) * W * 2 + ox + x] = c;
                        }
                }
                Log(sb.ToString().TrimEnd());
                if (sheet != null)
                {
                    var outTex = new Texture2D(W * 2, H * 2, TextureFormat.RGBA32, false);
                    outTex.SetPixels32(sheet); outTex.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(Dir, string.Format("snap_{0:000}.png", snaps)), outTex.EncodeToPNG());
                    UnityEngine.Object.Destroy(outTex);
                }
            }
            catch (Exception e) { Log("snapshot failed: " + e); }
        }
    }
}
