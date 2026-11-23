using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SmashTools;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using Verse;

namespace LIK
{
    /// <summary>
    /// STICKERS (user 2026-11-12: "add a sticker option rather than the preselected options" - placed by clicking the Knight in a large preview,
    /// each with its own colours; checks / hazard / camo are sticker designs). A sticker is stored as a point + surface normal on the 3D model,
    /// a size (model units), a turn angle and three colours. For every facing it is projected onto the visible surface with the surface maps
    /// of Tools/sticker_maps.py (depth, armour layer and shading per pixel), so it sits on the same spot in all four directions and is hidden
    /// where that surface faces away. Stickers only cover ARMOUR pixels of the part they were put on (carapace, shoulder pad or leg); with Over trim also its gold trim.
    /// </summary>
    public class KnightSticker : IExposable
    {
        public string design = "disc";
        public Vector3 pos, normal = Vector3.up;
        public float size = 30f, angle;
        public bool flip;                       // mirrored picture
        public bool erase;                      // eraser: its shape cuts away the stickers drawn before it (below it), showing the armour paint
        public bool wrap;                       // wrap the picture round the whole part (carapace drum / shin cylinders / pad faces) instead of a flat decal
        public float tiles = 1f;                // pattern repeat: the picture is tiled this many times across the sticker
        public int layer = 64;                  // 64 carapace, 128 shoulder pad, 192 leg (the surface map's armour layer)
        public bool overTrim;                   // also covers the gold trim of its part, not only the armour plates (user 2026-11-13)
        public bool pieceOnly;                  // stays on the one mesh piece under its spot (a pad shell, a lip, a plate) and bends round all its faces (user 2026-11-17)
        public bool sideOnly;                   // shows only on the half of the Knight (left / right of the centre line) its spot is on - chevrons (2026-11-17)
        public Vector3 view;                    // the direction it is projected from = the view it was placed / last moved in (zero = old saves: surface normal)
        public Vector3 up;                      // the sticker's own 'up' on the armour (zero = default frame). Carried along when it is dragged, so it bends over
                                                // the surface without turning (user 2026-11-17: 'warp but not rotate ... we don't have to fight the sticker')
        public int pieceId;                     // that piece, remembered when 'This piece only' is switched on (0 = look it up at the spot once). Re-looking it
                                                // up from the moving spot let a dragged sticker slip onto the neighbouring piece (user recording 2026-11-17)
        public Color c1 = Color.white, c2 = new Color(0.08f, 0.08f, 0.09f), c3 = new Color(0.85f, 0.7f, 0.15f);

        public KnightSticker Copy() { return (KnightSticker)MemberwiseClone(); }

        public void ExposeData()
        {
            Scribe_Values.Look(ref design, "design", "disc");
            Scribe_Values.Look(ref pos, "pos");
            Scribe_Values.Look(ref normal, "normal", Vector3.up);
            Scribe_Values.Look(ref size, "size", 14f);
            Scribe_Values.Look(ref angle, "angle", 0f);
            Scribe_Values.Look(ref flip, "flip", false);
            Scribe_Values.Look(ref erase, "erase", false);
            Scribe_Values.Look(ref wrap, "wrap", false);
            Scribe_Values.Look(ref tiles, "tiles", 1f);
            Scribe_Values.Look(ref layer, "layer", 64);
            Scribe_Values.Look(ref overTrim, "overTrim", false);
            Scribe_Values.Look(ref pieceOnly, "pieceOnly", false);
            Scribe_Values.Look(ref pieceId, "pieceId", 0);
            Scribe_Values.Look(ref up, "up", Vector3.zero);
            Scribe_Values.Look(ref view, "view", Vector3.zero);
            Scribe_Values.Look(ref sideOnly, "sideOnly", false);
            Scribe_Values.Look(ref c1, "c1", Color.white);
            Scribe_Values.Look(ref c2, "c2", new Color(0.08f, 0.08f, 0.09f));
            Scribe_Values.Look(ref c3, "c3", new Color(0.85f, 0.7f, 0.15f));
        }

        public static string LayerLabel(int layer) { return layer == 128 ? "shoulder pad" : layer == 192 ? "leg" : "carapace"; }
    }

    /// <summary>One surface map (one body picture, one facing).</summary>
    public class StickerMap
    {
        public int w, h;
        Color32[] px;
        Matrix4x4 m, inv;
        public float unitsPerPixel;

        public StickerMap(int w, int h, Color32[] px, float[] M)
        {
            this.w = w; this.h = h; this.px = px;
            m = Matrix4x4.identity;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) m[r, c] = M[r * 4 + c];
            inv = m.inverse;
            unitsPerPixel = new Vector3(M[0], M[4], M[8]).magnitude;
        }

        int Idx(int x, int y) { return (h - 1 - y) * w + x; }      // pictures load bottom row first

        public bool In(int x, int y) { return x >= 0 && y >= 0 && x < w && y < h; }
        /// <summary>The armour part of an ARMOUR pixel (64 / 128 / 192), 0 for anything else (trim included).</summary>
        public int Layer(int x, int y) { if (!In(x, y)) return 0; int b = px[Idx(x, y)].b; return b == 64 || b == 128 || b == 192 ? b : 0; }
        /// <summary>The part of an armour OR trim pixel (trim is stored as part + 32 by Tools/sticker_maps.py), 0 for anything else.</summary>
        public int Part(int x, int y) { if (!In(x, y)) return 0; int b = px[Idx(x, y)].b; return b == 0 ? 0 : b & 0xC0; }
        public float Shade(int x, int y) { return px[Idx(x, y)].a / 255f; }

        // piece id per pixel (<variant>_<facing>_pid.png from Tools/sticker_maps.py; one connected mesh part each), null = this map has none
        ushort[] pid;
        public bool HasPieces => pid != null;
        public void SetPieces(Color32[] c) { pid = new ushort[c.Length]; for (int i = 0; i < c.Length; i++) pid[i] = (ushort)((c[i].r << 8) | c[i].g); }
        public int Piece(int x, int y) { return pid == null || !In(x, y) ? 0 : pid[Idx(x, y)]; }

        public bool Pos(int x, int y, out Vector3 p)
        {
            p = default;
            if (!In(x, y)) return false;
            Color32 c = px[Idx(x, y)];
            int d = (c.r << 8) | c.g;
            if (d == 0) return false;
            p = m.MultiplyPoint3x4(new Vector3(x, y, (d - 1) / 65534f));
            return true;
        }

        public Vector2 Project(Vector3 p) { Vector3 q = inv.MultiplyPoint3x4(p); return new Vector2(q.x, q.y); }

        // smoothed surface directions on a 4 px grid (2026-11-13: per-pixel normals made small bevels skip stickers -> blue specks)
        Vector3[] sn; int snw, snh; const int SN = 4;
        readonly Dictionary<int, RectInt> layerBounds = new Dictionary<int, RectInt>();

        /// <summary>Main thread, before stamping.</summary>
        public void EnsureSmooth(Vector3 toward)
        {
            if (sn != null) return;
            snw = (w + SN - 1) / SN; snh = (h + SN - 1) / SN;
            var raw = new Vector3[snw * snh];
            for (int gy = 0; gy < snh; gy++)
                for (int gx = 0; gx < snw; gx++)
                    if (Normal(gx * SN, gy * SN, toward, 2, out Vector3 n)) raw[gy * snw + gx] = n;
            var o = new Vector3[snw * snh];
            for (int gy = 0; gy < snh; gy++)
                for (int gx = 0; gx < snw; gx++)
                {
                    Vector3 sum = Vector3.zero;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int xx = gx + dx, yy = gy + dy;
                            if (xx >= 0 && yy >= 0 && xx < snw && yy < snh) sum += raw[yy * snw + xx];
                        }
                    o[gy * snw + gx] = sum.sqrMagnitude > 1e-6f ? sum.normalized : Vector3.zero;
                }
            sn = o;
            for (int y = 0; y < h; y += 2)
                for (int x = 0; x < w; x += 2)
                {
                    int l = Part(x, y);
                    if (l == 0) continue;
                    if (!layerBounds.TryGetValue(l, out RectInt r)) r = new RectInt(x, y, 1, 1);
                    else { int x0 = Mathf.Min(r.xMin, x), y0 = Mathf.Min(r.yMin, y), x1 = Mathf.Max(r.xMax, x + 1), y1 = Mathf.Max(r.yMax, y + 1); r = new RectInt(x0, y0, x1 - x0, y1 - y0); }
                    layerBounds[l] = r;
                }
        }

        public Vector3 SmoothNormal(int x, int y) { return sn == null ? Vector3.zero : sn[Mathf.Min(snh - 1, y / SN) * snw + Mathf.Min(snw - 1, x / SN)]; }
        public bool LayerBounds(int layer, out RectInt r) { return layerBounds.TryGetValue(layer, out r); }

        public bool Normal(int x, int y, Vector3 toward, int step, out Vector3 n)
        {
            n = Vector3.zero;
            if (!Pos(x - step, y, out Vector3 a) || !Pos(x + step, y, out Vector3 b) || !Pos(x, y - step, out Vector3 c) || !Pos(x, y + step, out Vector3 d)) return false;
            n = Vector3.Cross(b - a, d - c);
            if (n.sqrMagnitude < 1e-8f) return false;
            n.Normalize();
            if (Vector3.Dot(n, toward) < 0f) n = -n;
            return true;
        }
    }

    [StaticConstructorOnStartup]
    public static class Stickers
    {
        public static readonly string[] Facings = { "north", "east", "south", "west" };
        static readonly Dictionary<string, StickerMap> maps = new Dictionary<string, StickerMap>();
        static readonly Dictionary<string, float[]> mapMeta = new Dictionary<string, float[]>();
        static readonly HashSet<string> metaLoaded = new HashSet<string>();
        public static readonly List<string> Designs = new List<string>();
        public static readonly List<string> Categories = new List<string>();        // "Basic" = files directly in Stickers/, else the sub-folder name
        static readonly Dictionary<string, string> designCat = new Dictionary<string, string>();
        // MEMORY (2026-11-13): nothing is decoded at start-up. A design's pixels are read from disk when a sticker is built (the last 48 kept);
        // the editor shows 72 px thumbnails made a few per frame. (Before: ~560 full pictures x3 = ~750 MB.)
        static readonly Dictionary<string, string> designPath = new Dictionary<string, string>();
        static readonly HashSet<string> designCustom = new HashSet<string>();
        static readonly Dictionary<string, Texture2D> designThumb = new Dictionary<string, Texture2D>();
        public class DesignPx { public Color32[] px; public int w, h; }
        static readonly Dictionary<string, DesignPx> designPx = new Dictionary<string, DesignPx>();
        static readonly LinkedList<string> pxOrder = new LinkedList<string>();
        const int MaxDesignsInMemory = 48;
        static int thumbFrame = -1, thumbsThisFrame;
        static readonly Dictionary<string, float> mapUsed = new Dictionary<string, float>();
        static readonly HashSet<string> mapMissing = new HashSet<string>();
        static float lastPurge;

        static string Root => LoadedModManager.GetMod<LIKMod>()?.Content?.RootDir ?? "";

        static readonly HashSet<string> fixedSet = new HashSet<string>();
        public static int DesignsVersion;                 // bumped by Reload (drawing caches rebuild)

        /// <summary>The players' own stickers (user 2026-11-12): a folder in the RimWorld install folder, apart from the mod's own Stickers
        /// folder, so mod updates never touch it and players never mix their files into the mod's.</summary>
        public static string CustomDir => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", "Knight Stickers");

        static Stickers() { LoadAll(); }

        public static void Reload()
        {
            foreach (Texture2D t in designThumb.Values) UnityEngine.Object.Destroy(t);
            designPath.Clear(); designCustom.Clear(); designPx.Clear(); pxOrder.Clear(); designThumb.Clear(); designCat.Clear(); defaultCols.Clear(); compatCats.Clear(); designCrop.Clear(); fixedSet.Clear(); fixedKnown.Clear(); Designs.Clear(); Categories.Clear();
            LoadAll();
            DesignsVersion++;
        }

        static void LoadAll()
        {
            try
            {
                LoadFolder(Path.Combine(Root, "Stickers"), "", "Basic", null);
                string cd = CustomDir;
                if (!Directory.Exists(cd))
                {
                    Directory.CreateDirectory(cd);
                    File.WriteAllText(Path.Combine(cd, "README.txt"),
                        "Your own Knight stickers (Imperial Knights mod).\r\n\r\n" +
                        "Put PNG pictures in this folder (sub-folders become their own tabs in the sticker editor). The mod's own stickers live in the mod\r\n" +
                        "folder and are not affected by anything here; mod updates never touch this folder.\r\n\r\n" +
                        "- A normal full-colour picture keeps its colours.\r\n" +
                        "- A picture painted only in pure RED, GREEN and BLUE becomes recolourable: red = colour 1, green = colour 2, blue = colour 3,\r\n" +
                        "  picked in game for each sticker. A transparent background is needed (PNG with alpha).\r\n" +
                        "- Square pictures of 256 to 512 pixels work best. Wide pictures (strips) keep their shape.\r\n\r\n" +
                        "After adding files press 'Reload stickers' in the sticker editor (or restart the game).\r\n");
                }
                LoadFolder(cd, "custom:", "Custom", "Custom - ");
                LoadCompat();
                Categories.Sort((a, b) => Order(a).CompareTo(Order(b)) != 0 ? Order(a).CompareTo(Order(b)) : string.Compare(a, b, StringComparison.Ordinal));
                Log.Message("[LIK] sticker designs: " + Designs.Count + " (custom folder: " + cd + ")");
            }
            catch (Exception e) { Log.Warning("[LIK] could not load sticker designs: " + e.Message); }
        }

        static int Order(string cat) { return cat == "Basic" ? 0 : cat.StartsWith("Custom") ? 3 : compatCats.Contains(cat) ? 2 : 1; }

        // ------------------------------------------------------------------ other 40k mods' icons (user 2026-11-17: 'a compatibility patch that adds
        // icons from other 40k mods' customization like GrimWorld and Mankind's Finest'). Read straight from the player's installed copy of that mod
        // while it is active - nothing of theirs is shipped. Keys 'compat:<set>/<file>'; a sticker whose mod is gone later is simply skipped.
        class CompatSet { public string packageId, tab, folder; public bool southOnly, crop; public string[] skip; }
        static readonly CompatSet[] compatSets =
        {
            new CompatSet { packageId = "Phonicmas.RimDark.MankindsFinest", tab = "Mankind's Finest - Legions", folder = "Textures/UI/Decoration/LegionBadges", crop = true, skip = new[] { "NoneSingle" } },
            new CompatSet { packageId = "Phonicmas.RimDark.MankindsFinest", tab = "Mankind's Finest - Chapters", folder = "Textures/Things/Armor/Imperium/PowerArmor/ChapterIcons", southOnly = true, crop = true },
            new CompatSet { packageId = "Phonicmas.RimDark.MankindsFinest", tab = "Mankind's Finest - Ranks", folder = "Textures/Things/Armor/Imperium/PowerArmor/RankIcons", southOnly = true, crop = true },
            new CompatSet { packageId = "Grimworld.AstraMilitarum", tab = "GrimWorld", folder = "CustomArmyCreator/blanks/Symbols", southOnly = true, crop = true },
        };
        static readonly HashSet<string> compatCats = new HashSet<string>();
        static readonly HashSet<string> designCrop = new HashSet<string>();

        static void LoadCompat()
        {
            foreach (CompatSet cs in compatSets)
            {
                ModContentPack mod = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => string.Equals(m.PackageId, cs.packageId, StringComparison.OrdinalIgnoreCase));
                if (mod == null) continue;
                string dir = Path.Combine(mod.RootDir, cs.folder.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                int n = 0;
                foreach (string f in Directory.GetFiles(dir, "*.png").OrderBy(x => x))
                {
                    string bn = Path.GetFileNameWithoutExtension(f);
                    if (cs.skip != null && cs.skip.Any(k => bn.Contains(k))) continue;
                    if (bn.EndsWith("_east") || bn.EndsWith("_west") || bn.EndsWith("_north")) continue;      // directional sets: the front view only
                    if (cs.southOnly && bn.EndsWith("_south")) bn = bn.Substring(0, bn.Length - 6);
                    string name = "compat:" + cs.tab + "/" + bn;
                    if (designPath.ContainsKey(name)) continue;
                    designPath[name] = f; Designs.Add(name); designCustom.Add(name);
                    if (cs.crop) designCrop.Add(name);
                    designCat[name] = cs.tab;
                    if (!Categories.Contains(cs.tab)) Categories.Add(cs.tab);
                    compatCats.Add(cs.tab);
                    n++;
                }
                if (n > 0) Log.Message("[LIK] stickers: " + n + " icons from " + mod.Name + " (" + cs.tab + ")");
            }
        }

        static void LoadFolder(string dir, string keyPrefix, string rootCat, string subPrefix)
        {
            if (!Directory.Exists(dir)) return;
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/');
            foreach (string f in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories).OrderBy(x => x))
            {
                string sub = Path.GetFullPath(Path.GetDirectoryName(f)).TrimEnd('\\', '/');
                string rel = sub.Length > root.Length ? sub.Substring(root.Length + 1).Replace('\\', '/') : "";
                string name = keyPrefix + (keyPrefix.Length > 0 && rel.Length > 0 ? rel + "/" : "") + Path.GetFileNameWithoutExtension(f);
                if (designPath.ContainsKey(name)) continue;
                designPath[name] = f; Designs.Add(name);
                if (keyPrefix.Length > 0) designCustom.Add(name);
                string cat = rel.Length == 0 ? rootCat : (subPrefix ?? "") + (subPrefix == null ? Path.GetFileName(sub) : rel);
                designCat[name] = cat;
                if (!Categories.Contains(cat)) Categories.Add(cat);
            }
        }

        static readonly HashSet<string> fixedKnown = new HashSet<string>();

        /// <summary>A design's pixels (read from disk on first use; the most recent 48 are kept).</summary>
        public static DesignPx Pixels(string d)
        {
            if (d == null || !designPath.TryGetValue(d, out string file)) return null;
            if (designPx.TryGetValue(d, out DesignPx hit)) { pxOrder.Remove(d); pxOrder.AddFirst(d); return hit; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(file))) return null;
                var dp = new DesignPx { px = t.GetPixels32(), w = t.width, h = t.height };
                if (designCrop.Contains(d)) dp = CropToContent(dp);
                designPx[d] = dp; pxOrder.AddFirst(d);
                while (pxOrder.Count > MaxDesignsInMemory) { designPx.Remove(pxOrder.Last.Value); pxOrder.RemoveLast(); }
                if (!fixedKnown.Contains(d))
                {
                    fixedKnown.Add(d);
                    // colours: a "_fixed" name, or (players' files only) a picture not painted in pure red / green / blue, keeps its own colours
                    int opaque = 0, mixed = 0;
                    for (int i = 0; i < dp.px.Length; i += 3)
                    {
                        Color32 c = dp.px[i];
                        if (c.a < 128) continue;
                        opaque++;
                        int hi = Math.Max(c.r, Math.Max(c.g, c.b)), lo = Math.Min(c.r, Math.Min(c.g, c.b)), mid = c.r + c.g + c.b - hi - lo;
                        if (mid > 70 || hi < 60) mixed++;
                    }
                    if (d.EndsWith("_fixed") || (designCustom.Contains(d) && (opaque == 0 || mixed > opaque * 0.08f))) fixedSet.Add(d);
                }
                return dp;
            }
            catch { return null; }
            finally { UnityEngine.Object.Destroy(t); }
        }

        /// <summary>Trims a picture to its non-transparent part (+2 px).</summary>
        static DesignPx CropToContent(DesignPx dp)
        {
            int x0 = dp.w, y0 = dp.h, x1 = -1, y1 = -1;
            for (int y = 0; y < dp.h; y++)
                for (int x = 0; x < dp.w; x++)
                    if (dp.px[y * dp.w + x].a > 16) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x1 < 0) return dp;
            x0 = Math.Max(0, x0 - 2); y0 = Math.Max(0, y0 - 2); x1 = Math.Min(dp.w - 1, x1 + 2); y1 = Math.Min(dp.h - 1, y1 + 2);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var o = new Color32[w * h];
            for (int y = 0; y < h; y++) Array.Copy(dp.px, (y0 + y) * dp.w + x0, o, y * w, w);
            return new DesignPx { px = o, w = w, h = h };
        }

        /// <summary>The editor's small picture of a design (recolourable ones in white / black / gold). Made a few per frame; null until then.</summary>
        public static Texture2D DesignThumb(string d)
        {
            if (d == null) return null;
            if (designThumb.TryGetValue(d, out Texture2D t)) return t;
            if (Time.frameCount != thumbFrame) { thumbFrame = Time.frameCount; thumbsThisFrame = 0; }
            if (thumbsThisFrame++ >= 6) return null;
            DesignPx dp = Pixels(d);
            if (dp == null) { designThumb[d] = null; return null; }
            bool fx = Fixed(d);
            int S = 72;
            float sc = Mathf.Max(dp.w, dp.h) / (float)S;
            int tw = Mathf.Max(1, Mathf.RoundToInt(dp.w / sc)), th = Mathf.Max(1, Mathf.RoundToInt(dp.h / sc));
            var tp = new Color32[tw * th];
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    Color32 c = dp.px[Mathf.Min(dp.h - 1, (int)(y * sc)) * dp.w + Mathf.Min(dp.w - 1, (int)(x * sc))];
                    if (!fx)
                    {
                        float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f, sum = Mathf.Max(r + g + b, 0.001f);
                        Color col = (Color.white * r + new Color(0.1f, 0.1f, 0.1f) * g + new Color(0.85f, 0.65f, 0.15f) * b) / sum;
                        c = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), c.a);
                    }
                    tp[y * tw + x] = c;
                }
            t = new Texture2D(tw, th, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(tp); t.Apply(false, true);
            designThumb[d] = t;
            return t;
        }

        public static bool HasDesign(string d) { return d != null && designPath.ContainsKey(d); }

        static readonly Dictionary<string, Color[]> defaultCols = new Dictionary<string, Color[]>();

        /// <summary>Starting colours of a recolourable design: optional '<file>.colours.txt' next to its PNG, first line = up to three hex
        /// colours (colour 1 2 3). E.g. Stickers/Wings & eagles/imperial_aquila.colours.txt makes the Aquila start black (user 2026-11-17).</summary>
        public static bool DefaultColours(string d, out Color c1, out Color c2, out Color c3)
        {
            c1 = Color.white; c2 = new Color(0.08f, 0.08f, 0.09f); c3 = new Color(0.85f, 0.7f, 0.15f);
            if (d == null || !designPath.TryGetValue(d, out string file)) return false;
            if (!defaultCols.TryGetValue(d, out Color[] cols))
            {
                cols = null;
                try
                {
                    string side = Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + ".colours.txt");
                    if (File.Exists(side))
                        foreach (string line in File.ReadAllLines(side))
                        {
                            string t = line.Trim();
                            if (t.Length == 0 || t.StartsWith("#")) continue;
                            var list = new List<Color>();
                            foreach (string hx in t.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                                if (ColorUtility.TryParseHtmlString(hx.StartsWith("#") ? hx : "#" + hx, out Color c)) list.Add(c);
                            if (list.Count > 0) cols = list.ToArray();
                            break;
                        }
                }
                catch (Exception) { cols = null; }
                defaultCols[d] = cols;
            }
            if (cols == null) return false;
            if (cols.Length > 0) c1 = cols[0];
            if (cols.Length > 1) c2 = cols[1];
            if (cols.Length > 2) c3 = cols[2];
            return true;
        }

        public static string Category(string d) { return d != null && designCat.TryGetValue(d, out string c) ? c : "Basic"; }
        public static bool Fixed(string d)
        {
            if (d == null) return false;
            if (d.EndsWith("_fixed") || fixedSet.Contains(d)) return true;
            if (designCustom.Contains(d) && !fixedKnown.Contains(d)) { Pixels(d); return fixedSet.Contains(d); }
            return false;
        }
        public static string DesignLabel(string d)
        {
            if (d == null) return "";
            string n = d.StartsWith("custom:") || d.StartsWith("compat:") ? d.Substring(d.LastIndexOfAny(new[] { ':', '/' }) + 1) : d;
            if (n.StartsWith("BEWH_")) n = n.Substring(5).Replace("iconUI_", "").Replace("Legion_", "");
            return n.Replace("_fixed", "").Replace('_', ' ').CapitalizeFirst();
        }

        /// <summary>Toward-camera direction of a facing (the art is drawn 35 degrees above the horizon; the Knight's front is model +Y).</summary>
        public static Vector3 Toward(string facing)
        {
            float ct = Mathf.Cos(35f * Mathf.Deg2Rad), st = Mathf.Sin(35f * Mathf.Deg2Rad);
            switch (facing)
            {
                case "south": return new Vector3(0f, ct, st);
                case "north": return new Vector3(0f, -ct, st);
                case "east": return new Vector3(ct, 0f, st);
                default: return new Vector3(-ct, 0f, st);
            }
        }

        public static bool HasMaps(KnightClassDef cls) { return cls != null && Map(cls, "base", "south") != null; }
        /// <summary>The class's maps carry piece ids (the 'This piece only' option).</summary>
        public static bool HasPieces(KnightClassDef cls) { StickerMap m = cls != null ? Map(cls, "base", "south") : null; return m != null && m.HasPieces; }

        public static StickerMap Map(KnightClassDef cls, string variant, string facing)
        {
            string k = KnightPaint.ClassKey(cls);
            string key = k + "|" + variant + "|" + facing;
            mapUsed[key] = Time.realtimeSinceStartup;
            if (maps.TryGetValue(key, out StickerMap sm)) return sm;
            if (mapMissing.Contains(key)) return null;
            mapMissing.Add(key);
            try
            {
                string dir = Path.Combine(Root, "Data", "StickerMaps", k);
                if (!metaLoaded.Contains(k))
                {
                    metaLoaded.Add(k);
                    string mt = Path.Combine(dir, "maps.txt");
                    if (File.Exists(mt))
                        foreach (string line in File.ReadAllLines(mt))
                        {
                            string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (p.Length < 15) continue;
                            mapMeta[k + "|" + p[0]] = p.Skip(3).Take(12).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        }
                }
                string file = Path.Combine(dir, variant + "_" + facing + ".png");
                if (!File.Exists(file) || !mapMeta.TryGetValue(k + "|" + variant + "_" + facing, out float[] M)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(file));
                sm = new StickerMap(t.width, t.height, t.GetPixels32(), M);
                UnityEngine.Object.Destroy(t);
                string pf = Path.Combine(dir, variant + "_" + facing + "_pid.png");
                if (File.Exists(pf))
                {
                    var tp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tp.LoadImage(File.ReadAllBytes(pf));
                    if (tp.width == sm.w && tp.height == sm.h) sm.SetPieces(tp.GetPixels32());
                    UnityEngine.Object.Destroy(tp);
                }
                maps[key] = sm;
                mapMissing.Remove(key);
            }
            catch (Exception e) { Log.Warning("[LIK] sticker map " + key + ": " + e.Message); }
            return sm;
        }

        /// <summary>Frees surface maps nobody used for a minute (each is ~11 MB); called every frame from the map component.</summary>
        public static void PurgeMaps()
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastPurge < 10f || maps.Count == 0) return;
            lastPurge = now;
            foreach (string k in maps.Keys.ToList())
                if (!mapUsed.TryGetValue(k, out float u) || now - u > 60f) maps.Remove(k);
        }

        /// <summary>Which body picture is showing: "base" or the raised launcher ("helios" / "ironstorm") if it has its own maps.</summary>
        public static string Variant(VehiclePawn v, KnightClassDef cls)
        {
            string dn = v?.Retexture?.defName;
            if (dn == null || !dn.StartsWith("LIK_Open_")) return "base";
            string var = dn.Substring(dn.LastIndexOf('_') + 1);
            return Map(cls, var, "south") != null ? var : "base";
        }

        /// <summary>The nearest pixel of one armour part to (x, y) in that picture (searched outward up to ~200 px).</summary>
        public static bool NearestOnLayer(KnightClassDef cls, string variant, string facing, int x, int y, int layer, out int nx, out int ny, bool trim = false)
        {
            nx = ny = 0;
            StickerMap m = Map(cls, variant, facing);
            if (m == null) return false;
            for (int r = 2; r <= 200; r += 3)
            {
                int best = int.MaxValue;
                for (int i = -r; i <= r; i += 2)
                    foreach (var (px, py) in new[] { (x + i, y - r), (x + i, y + r), (x - r, y + i), (x + r, y + i) })
                    {
                        if ((trim ? m.Part(px, py) : m.Layer(px, py)) != layer) continue;
                        int d = (px - x) * (px - x) + (py - y) * (py - y);
                        if (d < best) { best = d; nx = px; ny = py; }
                    }
                if (best != int.MaxValue) return true;
            }
            return false;
        }

        /// <summary>A click on a facing's picture (pixel) -> a new sticker spot. False if that pixel is not armour.</summary>
        /// <param name="trim">true: the gold trim counts too (Over trim stickers)</param>
        public static bool PickSpot(KnightClassDef cls, string variant, string facing, int x, int y, out Vector3 pos, out Vector3 normal, out int layer, bool trim = false)
        {
            pos = normal = default; layer = 0;
            StickerMap m = Map(cls, variant, facing);
            if (m == null) return false;
            layer = trim ? m.Part(x, y) : m.Layer(x, y);
            if (layer == 0 || !m.Pos(x, y, out pos)) return false;
            Vector3 sum = Vector3.zero;
            Vector3 tw = Toward(facing);
            for (int dy = -12; dy <= 12; dy += 4)
                for (int dx = -12; dx <= 12; dx += 4)
                    if ((trim ? m.Part(x + dx, y + dy) : m.Layer(x + dx, y + dy)) == layer && m.Normal(x + dx, y + dy, tw, 3, out Vector3 n)) sum += n;
            normal = sum.sqrMagnitude > 1e-6f ? sum.normalized : tw;
            return true;
        }

        public class LayerTex { public Texture2D tex; public int x0, y0, w, h, layer, mapW, mapH; public Material mat; public Color32[] px; }
        /// <summary>StickerRecorder: keep a copy of each layer's pixels (the textures themselves are made unreadable after upload).</summary>
        public static bool KeepPixels;

        /// <summary>Draws the stickers of one layer group into textures (one per layer) for one facing. Main thread.</summary>
        /// <param name="onlyLayer">0 = every part; else only that part (64 carapace, 128 shoulder pads, 192 legs)</param>
        public static List<LayerTex> Build(KnightClassDef cls, string variant, string facing, List<KnightSticker> list, int onlyLayer = 0)
        {
            var result = new List<LayerTex>();
            if (list == null || list.Count == 0) return result;
            StickerMap m = Map(cls, variant, facing);
            if (m == null) return result;
            Vector3 tw = Toward(facing);
            foreach (int layer in new[] { 192, 64, 128 })
            {
                if (onlyLayer != 0 && layer != onlyLayer) continue;
                var mine = list.Where(s => s.layer == layer && HasDesign(s.design) && Vector3.Dot(s.normal, tw) > -0.15f).ToList();
                if (mine.Count == 0) continue;
                // union of the stickers' boxes in this picture
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                m.EnsureSmooth(tw);
                foreach (KnightSticker s in mine)
                {
                    // wrap and 'this piece only' stickers may be laid out from a centre other than their spot: take the whole part (2026-11-17)
                    if ((s.wrap || s.pieceOnly) && m.LayerBounds(layer, out RectInt lb))
                    {
                        x0 = Mathf.Min(x0, lb.xMin); y0 = Mathf.Min(y0, lb.yMin); x1 = Mathf.Max(x1, lb.xMax); y1 = Mathf.Max(y1, lb.yMax);
                        continue;
                    }
                    Vector2 c = m.Project(s.pos);
                    float r = (s.view != Vector3.zero && !s.wrap ? s.size * 1.8f : s.size * 0.75f) / m.unitsPerPixel + 4f;
                    x0 = Mathf.Min(x0, Mathf.FloorToInt(c.x - r)); y0 = Mathf.Min(y0, Mathf.FloorToInt(c.y - r));
                    x1 = Mathf.Max(x1, Mathf.CeilToInt(c.x + r)); y1 = Mathf.Max(y1, Mathf.CeilToInt(c.y + r));
                }
                x0 = Mathf.Clamp(x0, 0, m.w - 1); y0 = Mathf.Clamp(y0, 0, m.h - 1); x1 = Mathf.Clamp(x1, 0, m.w - 1); y1 = Mathf.Clamp(y1, 0, m.h - 1);
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                if (w <= 1 || h <= 1) continue;
                var buf = new Color32[w * h];
                bool any = false;
                foreach (KnightSticker s in mine) any |= Stamp(cls, m, s, tw, buf, x0, y0, w, h, variant);
                if (!any) continue;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                t.SetPixels32(buf);
                t.Apply(false, true);
                result.Add(new LayerTex { tex = t, x0 = x0, y0 = y0, w = w, h = h, layer = layer, mapW = m.w, mapH = m.h, mat = new Material(ShaderDatabase.Cutout) { mainTexture = t }, px = KeepPixels ? buf : null });
            }
            return result;
        }

        static bool Stamp(KnightClassDef cls, StickerMap m, KnightSticker s, Vector3 tw, Color32[] buf, int x0, int y0, int w, int h, string variant = "base")
        {
            DesignPx dpx = Pixels(s.design);
            if (dpx == null) return false;
            Color32[] d = dpx.px;
            int dw = dpx.w, dh = dpx.h;
            float su = s.size * (dw >= dh ? 1f : (float)dw / dh), sv = s.size * (dh >= dw ? 1f : (float)dh / dw);
            bool fixedCol = Fixed(s.design);
            Vector3 N = s.normal.normalized;
            // PROJECTOR (2026-11-17, user: 'the image of the sticker is being cut short on the body'): a sticker placed on a curved side was projected
            // along that side's normal, so seen in the view it was placed in its square was sheared into a band ending on a diagonal. Stickers now
            // project along the view they were placed / last moved in (s.view): full upright picture in that view, cut only by its part / piece;
            // the same 3D decal shows in the other facings. Old saves (view = zero) keep the surface-normal projection.
            bool proj = s.view != Vector3.zero && !s.wrap;
            Vector3 V = proj ? s.view.normalized : N;
            FrameOf(s, V, out Vector3 T, out Vector3 B);
            float ca = Mathf.Cos(s.angle * Mathf.Deg2Rad), sa = Mathf.Sin(s.angle * Mathf.Deg2Rad);
            Vector3 T2 = T * ca + B * sa, B2 = -T * sa + B * ca;
            Vector2 c = m.Project(s.pos);
            // projector stickers reach deeper along their view, which shows up sideways in the other facings
            float r = (s.view != Vector3.zero && !s.wrap ? s.size * 1.8f : s.size * 0.75f) / m.unitsPerPixel + 4f;
            int ax = Mathf.Max(x0, Mathf.FloorToInt(c.x - r)), bx = Mathf.Min(x0 + w - 1, Mathf.CeilToInt(c.x + r));
            int ay = Mathf.Max(y0, Mathf.FloorToInt(c.y - r)), by = Mathf.Min(y0 + h - 1, Mathf.CeilToInt(c.y + r));
            bool wrap = s.wrap;
            Vector3[] fr = null; float cxm = 0f; Vector2 ab0 = default; bool box = false;
            if (wrap)
            {
                ax = x0; bx = x0 + w - 1; ay = y0; by = y0 + h - 1;
                fr = PartFrames(cls); cxm = CentreX(cls);
                // carapace: drum (one rounded shell) for the Acastus and Armiger, box faces for the others; shoulder pads: box faces for every class
                // except the Acastus (keeps its original per-face pad wrap). 2026-11-17, Armiger recording: box wrap switched faces on its round hull.
                box = s.layer == 128 ? KnightPaint.ClassKey(cls) != "Acastus" : !DrumCarapace(cls);
                ab0 = WrapAB(s.layer, s.pos, N, fr, cxm, box, N);
            }
            float tl = Mathf.Max(1f, s.tiles);
            float thick = s.size * 0.5f + 3f;
            // projector depth: a slope seen at 35 deg changes depth fast. 2026-11-22 (user: find holes on every Knight): 1.5*size+5 cut stickers
            // that straddle sharp folds on a diagonal (rear top edge, Acastus groin, Dominus waist, Cerastus chest: 0.04-1.1 % of sticker pixels);
            // Tools/sticker_projector_sim.py: 3*size+12 -> 0 holes on all five Knights, other facings gain only 0-1.3 % area.
            float reach = s.size * 3f + 12f;
            int anyHit = 0;
            Vector3 P = s.pos;
            int layerWanted = s.layer;
            bool flip = s.flip, erase = s.erase, overTrim = s.overTrim;
            // 'This piece only' = a CLIP: the sticker is laid on the armour like any other (it follows the surface, the user's 'warp'), but only the
            // pixels of its remembered piece show. Its spot may sit anywhere, even off the piece, so e.g. two hazard squares hanging over the
            // edges of two pieces make a chevron (user 2026-11-17; the earlier flat per-view 'bend' layout lost the surface following).
            int piece = s.pieceOnly && m.HasPieces ? PieceOf(cls, variant, s) : 0;
            // every face of its piece within the sticker's reach takes it, even ones turned away (a thin curled lip is covered all round - offline
            // check: Dominus lip 100 % in every facing at size 40, pads 91-98 %); thick parts are safe: their far side is beyond the sticker's depth
            float cull = piece != 0 ? -1.01f : 0.15f;
            // 'This side only' (2026-11-17): only the half of the Knight its spot is on (centre line = CentreX, the left-right mirror line)
            bool side = s.sideOnly; float scx = side ? CentreX(cls) : 0f; bool sRight = P.x >= scx;
            float tiles = s.tiles;
            Color c1 = s.c1, c2 = s.c2, c3 = s.c3;
            // PERF (2026-11-13): rows in parallel (pure maths on arrays, no Unity calls); per pixel the cheap tests come first
            System.Threading.Tasks.Parallel.For(ay, by + 1, y =>
            {
                for (int x = ax; x <= bx; x++)
                {
                    if ((overTrim ? m.Part(x, y) : m.Layer(x, y)) != layerWanted || !m.Pos(x, y, out Vector3 q)) continue;
                    if (piece != 0 && m.Piece(x, y) != piece) continue;
                    if (side && (q.x >= scx) != sRight) continue;
                    float u, v;
                    if (wrap)
                    {
                        // round the part: the picture is tiled (tile = size / repeat), turned by the angle, anchored at the sticker's spot
                        Vector2 ab = WrapAB(layerWanted, q, m.SmoothNormal(x, y), fr, cxm, box, N);
                        float da = ab.x - ab0.x, db = ab.y - ab0.y;
                        float ru = da * ca + db * sa, rv = -da * sa + db * ca;
                        // a tile never smaller than 8 model units (~60 px): finer patterns turn into moire on the 1408 px pictures (2026-11-17)
                        u = ru / Mathf.Max(su / tl, 8f) + 0.5f; v = rv / Mathf.Max(sv / tl, 8f) + 0.5f;
                        u -= Mathf.Floor(u); v -= Mathf.Floor(v);
                        if (flip) u = 1f - u;
                    }
                    else
                    {
                        Vector3 o = q - P;
                        float dep = Mathf.Abs(Vector3.Dot(o, V));
                        if (dep > (proj ? reach : thick)) continue;
                        u = Vector3.Dot(o, T2) / su + 0.5f; v = Vector3.Dot(o, B2) / sv + 0.5f;
                        if (u < 0f || u >= 1f || v < 0f || v >= 1f) continue;
                        Vector3 n = m.SmoothNormal(x, y);
                        if (proj)
                        {
                            // projector: surfaces facing the view it was placed in take it; close to its spot a piece-only sticker also covers
                            // faces turned away (a curled lip all round)
                            if (n != Vector3.zero && Vector3.Dot(n, V) < (piece != 0 && dep <= thick ? -1.01f : 0.05f)) continue;
                        }
                        else if (n != Vector3.zero && Vector3.Dot(n, N) < cull) continue;
                        if (flip) u = 1f - u;
                        if (tiles > 1.01f) { u = u * tiles; u -= Mathf.Floor(u); v = v * tiles; v -= Mathf.Floor(v); }
                    }
                    Color32 dp = d[Mathf.Clamp((int)(v * dh), 0, dh - 1) * dw + Mathf.Clamp((int)(u * dw), 0, dw - 1)];
                    if (dp.a < 8) continue;
                    Color col;
                    if (fixedCol) col = dp;
                    else
                    {
                        float wr = dp.r / 255f, wg = dp.g / 255f, wb = dp.b / 255f, sum = wr + wg + wb;
                        col = sum < 0.02f ? c1 : (c1 * wr + c2 * wg + c3 * wb) / sum;
                    }
                    float sh = m.Shade(x, y);
                    col = new Color(col.r * sh, col.g * sh, col.b * sh, 1f);
                    int bi = (h - 1 - (y - y0)) * w + (x - x0);
                    if (erase)
                    {
                        Color32 ob = buf[bi];
                        ob.a = (byte)(ob.a * (255 - dp.a) / 255);
                        buf[bi] = ob;
                    }
                    else buf[bi] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), dp.a);
                    anyHit = 1;
                }
            });
            return anyHit != 0;
        }

        /// <summary>A sticker's own right (T) and up (B) on the surface. Up = model up (Z) laid onto the surface, so at turn 0 a sticker
        /// stands upright as seen from that side; on flat tops (no up) the top of the sticker points to the Knight's back, so it reads upright
        /// from the front. Continuous - no sudden flips while dragging.</summary>
        public static void StickerFrame(Vector3 N, out Vector3 T, out Vector3 B)
        {
            Vector3 up = Vector3.forward - N * N.z;                         // model Z projected onto the surface
            Vector3 back = new Vector3(0f, -1f, 0f) - N * (-N.y);           // toward the Knight's back, on the surface
            float k = Mathf.Clamp01((Mathf.Abs(N.z) - 0.9f) / 0.09f);       // blend toward "back" only on almost-flat tops
            Vector3 b = up.normalized * (1f - k) + back.normalized * k;
            if (b.sqrMagnitude < 1e-6f) b = back;
            B = b.normalized;
            T = Vector3.Cross(B, N).normalized;                             // right-handed: right x up = toward the viewer
            B = Vector3.Cross(N, T).normalized;
        }

        /// <summary>A sticker's frame: its own carried 'up' laid onto the surface at N, else the default StickerFrame (new stickers, old saves).</summary>
        public static void FrameOf(KnightSticker s, Vector3 N, out Vector3 T, out Vector3 B)
        {
            if (s != null && s.up != Vector3.zero)
            {
                Vector3 b = s.up - N * Vector3.Dot(s.up, N);
                if (b.sqrMagnitude > 1e-4f)
                {
                    B = b.normalized;
                    T = Vector3.Cross(B, N).normalized;
                    B = Vector3.Cross(N, T).normalized;
                    return;
                }
            }
            StickerFrame(N, out T, out B);
        }

        /// <summary>Dragging: carry the sticker's current 'up' to its new spot (parallel transport) - it bends with the armour but does not turn.</summary>
        public static void CarryUp(KnightSticker s, Vector3 newNormal)
        {
            FrameOf(s, s.normal.normalized, out _, out Vector3 B);          // its 'up' where it is now
            Vector3 n = newNormal.normalized;
            Vector3 b = B - n * Vector3.Dot(B, n);
            s.up = b.sqrMagnitude > 1e-4f ? b.normalized : B;
        }

        /// <summary>
        /// Dragging in the editor (user 2026-11-17: 'warp but not rotate'): the sticker keeps the SAME angle on screen in the picture being edited
        /// while it moves to (newPos, newNormal) - it still lies on and bends with the armour. Replay of the user's recorded drag over the Armiger
        /// carapace: old frame turned 118 deg (jumps of 59), carried 'up' 63 deg, this 0 deg. Edge-on surfaces fall back to CarryUp.
        /// </summary>
        public static void KeepScreenUp(KnightSticker s, StickerMap m, Vector3 newPos, Vector3 newNormal)
        {
            if (m == null) { CarryUp(s, newNormal); return; }
            FrameOf(s, s.normal.normalized, out _, out Vector3 Bnow);
            Vector2 d = m.Project(s.pos + Bnow) - m.Project(s.pos);             // its 'up' on screen now
            Vector3 N = newNormal.normalized;
            CarryUp(s, N);                                                     // a starting frame at the new spot
            FrameOf(s, N, out Vector3 T0, out Vector3 B0);
            Vector2 a = m.Project(newPos + B0) - m.Project(newPos), t = m.Project(newPos + T0) - m.Project(newPos);
            if (d.sqrMagnitude < 1e-6f || a.sqrMagnitude + t.sqrMagnitude < 1e-6f) return;
            float th = Mathf.Atan2(-(a.x * d.y - a.y * d.x), t.x * d.y - t.y * d.x);   // turn in the surface so the picture's 'up' points along d
            if (Vector2.Dot(a * Mathf.Cos(th) + t * Mathf.Sin(th), d) < 0f) th += Mathf.PI;
            s.up = (B0 * Mathf.Cos(th) + T0 * Mathf.Sin(th)).normalized;
        }

        /// <summary>The piece a 'This piece only' sticker belongs to: remembered on the sticker (looked up at its spot the first time).</summary>
        public static int PieceOf(KnightClassDef cls, string variant, KnightSticker s)
        {
            if (s.pieceId == 0) s.pieceId = PieceAt(cls, variant, s.pos, s.normal);
            return s.pieceId;
        }

        /// <summary>The mesh piece under a sticker's spot: looked up in the picture that faces the spot most, at the pixel showing that very
        /// surface point (a nearby pixel if the exact one shows something in front). 0 = unknown.</summary>
        public static int PieceAt(KnightClassDef cls, string variant, Vector3 pos, Vector3 normal)
        {
            string best = null; float bd = -2f;
            foreach (string f in Facings) { float d = Vector3.Dot(normal, Toward(f)); if (d > bd && Map(cls, variant, f) != null) { bd = d; best = f; } }
            if (best == null) return 0;
            StickerMap m = Map(cls, variant, best);
            if (!m.HasPieces) return 0;
            Vector2 c = m.Project(pos);
            int cx = Mathf.RoundToInt(c.x), cy = Mathf.RoundToInt(c.y), found = 0; float bestD = 2.5f;
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (!m.Pos(cx + dx, cy + dy, out Vector3 q)) continue;
                    float dd = (q - pos).magnitude;
                    int pc = m.Piece(cx + dx, cy + dy);
                    if (pc != 0 && dd < bestD) { bestD = dd; found = pc; }
                }
            return found;
        }

        static readonly Dictionary<string, Vector3[]> frames = new Dictionary<string, Vector3[]>();

        /// <summary>Wrap frames of a class: [0] carapace drum (centre y, centre z, radius) round the model X axis, [1]/[2] left/right shin
        /// cylinders (centre x, centre y, radius) round the vertical axis.</summary>
        public static Vector3[] PartFrames(KnightClassDef cls)
        {
            string k = KnightPaint.ClassKey(cls);
            if (frames.TryGetValue(k, out Vector3[] f)) return f;
            float cx = CentreX(cls);
            var car = new List<Vector3>(); var legL = new List<Vector3>(); var legR = new List<Vector3>();
            foreach (string fc in Facings)
            {
                StickerMap m = Map(cls, "base", fc);
                if (m == null) continue;
                for (int y = 0; y < m.h; y += 4)
                    for (int x = 0; x < m.w; x += 4)
                    {
                        int l = m.Layer(x, y);
                        if (l == 0 || l == 128 || !m.Pos(x, y, out Vector3 q)) continue;
                        if (l == 64) car.Add(q); else if (q.x < cx) legL.Add(q); else legR.Add(q);
                    }
            }
            float Med(List<float> v) { if (v.Count == 0) return 0f; v.Sort(); return v[v.Count / 2]; }
            float Pct(List<float> v, float pc) { if (v.Count == 0) return 0f; v.Sort(); return v[Mathf.Clamp((int)(v.Count * pc), 0, v.Count - 1)]; }
            f = new Vector3[3];
            {
                float yc = Med(car.Select(q => q.y).ToList()), zc = Pct(car.Select(q => q.z).ToList(), 0.05f);
                f[0] = new Vector3(yc, zc, Mathf.Max(1f, Med(car.Select(q => Mathf.Sqrt((q.y - yc) * (q.y - yc) + (q.z - zc) * (q.z - zc))).ToList())));
            }
            int i = 1;
            foreach (List<Vector3> leg in new[] { legL, legR })
            {
                float xc = Med(leg.Select(q => q.x).ToList());
                var ys = leg.Select(q => q.y).ToList();
                float yc = Med(new List<float>(ys)) - 0.5f * (ys.Count > 0 ? ys.Max() - ys.Min() : 0f);       // axis behind the shin plate
                f[i++] = new Vector3(xc, yc, Mathf.Max(1f, Med(leg.Select(q => Mathf.Sqrt((q.x - xc) * (q.x - xc) + (q.y - yc) * (q.y - yc))).ToList())));
            }
            frames[k] = f;
            return f;
        }

        /// <summary>The Acastus carapace is one drum, so its wrap goes round a cylinder. Every other Knight's carapace is domes, plates and a chest:
        /// those get the box wrap (user 2026-11-17: 'the wrap part button seems bugged' - swirled stripes on the Dominus).</summary>
        public static bool DrumCarapace(KnightClassDef cls) { string k = KnightPaint.ClassKey(cls); return k == "Acastus" || k == "Armiger"; }   // Armiger: one rounded hull (2026-11-17)

        /// <summary>
        /// Box wrap: the pattern is laid straight across a face (side faces: across + up, top: across + front-back). It keeps the face of the
        /// sticker's own spot (N) and only changes face where the surface clearly turns to another side, so curved armour does not flicker
        /// between mappings pixel by pixel.
        /// </summary>
        static Vector2 BoxAB(Vector3 q, Vector3 n, Vector3 N)
        {
            int a0 = Dominant(N);
            float d0 = a0 == 0 ? n.x * Mathf.Sign(N.x) : a0 == 1 ? n.y * Mathf.Sign(N.y) : n.z * Mathf.Sign(N.z);
            int ax = n == Vector3.zero || d0 >= 0.35f ? a0 : Dominant(n);
            float sg = ax == a0 && d0 >= 0.35f ? Mathf.Sign(ax == 0 ? N.x : ax == 1 ? N.y : N.z) : Mathf.Sign(ax == 0 ? n.x : ax == 1 ? n.y : n.z);
            if (ax == 0) return new Vector2(sg > 0f ? q.y : -q.y, q.z);
            if (ax == 1) return new Vector2(sg > 0f ? -q.x : q.x, q.z);
            return new Vector2(-q.x, -q.y);
        }

        static int Dominant(Vector3 v) { float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z); return ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2; }

        /// <summary>Wrapped pattern coordinates of a surface point (model units along the surface).</summary>
        static Vector2 WrapAB(int layer, Vector3 q, Vector3 n, Vector3[] f, float cx, bool box = false, Vector3 N = default)
        {
            if (box && layer != 192) return BoxAB(q, n, N);
            // u = across (to the right as seen from that side), v = up (or over the top, front to back)
            if (layer == 64)
            {
                // drum only (Acastus, Armiger). The old flat side-wall mapping (|n.x| > 0.75) flicked on and off along curved sides and made the
                // stripes zig-zag in the side views (2026-11-17, offline copy of this code on every Knight's maps: mod assets/_sticker_sim)
                return new Vector2(-q.x, Mathf.Atan2(q.z - f[0].y, q.y - f[0].x) * f[0].z);            // across, and over the top
            }
            if (layer == 192)
            {
                Vector3 g = q.x < cx ? f[1] : f[2];
                return new Vector2(-Mathf.Atan2(q.y - g.y, q.x - g.x) * g.z, q.z);                      // round the shin, up the leg
            }
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);                      // shoulder pads: flat per face
            if (ax >= ay && ax >= az) return new Vector2(n.x > 0f ? q.y : -q.y, q.z);
            if (az >= ay) return new Vector2(-q.x, -q.y);
            return new Vector2(n.y > 0f ? -q.x : q.x, q.z);
        }

        static readonly Dictionary<string, float> centreX = new Dictionary<string, float>();
        public static float CentreX(KnightClassDef cls)
        {
            string k = KnightPaint.ClassKey(cls);
            if (centreX.TryGetValue(k, out float cx)) return cx;
            // best-fit mirror line written by Tools/sticker_maps.py (2026-11-17; the median armour x was off on the Acastus / Armiger)
            try
            {
                string cf = Root != null ? Path.Combine(Root, "Data", "StickerMaps", k, "centre.txt") : null;
                if (cf != null && File.Exists(cf) && float.TryParse(File.ReadAllText(cf).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cx))
                { centreX[k] = cx; return cx; }
            }
            catch (Exception) { }
            var xs = new List<float>();
            StickerMap m = Map(cls, "base", "south");
            if (m != null)
                for (int y = 0; y < m.h; y += 4)
                    for (int x = 0; x < m.w; x += 4)
                        if (m.Layer(x, y) != 0 && m.Pos(x, y, out Vector3 q)) xs.Add(q.x);
            xs.Sort();
            cx = xs.Count > 0 ? xs[xs.Count / 2] : 0f;
            centreX[k] = cx;
            return cx;
        }

        /// <summary>A copy of a sticker on the other side of the Knight (left/right mirrored), snapped onto the surface there.</summary>
        public static KnightSticker MirrorCopy(KnightClassDef cls, KnightSticker s)
        {
            float cx = CentreX(cls);
            KnightSticker c = s.Copy();
            c.pos = new Vector3(2f * cx - s.pos.x, s.pos.y, s.pos.z);
            c.normal = new Vector3(-s.normal.x, s.normal.y, s.normal.z);
            c.pieceId = 0;                                                   // the mirrored piece on the other side has its own id
            c.up = new Vector3(-s.up.x, s.up.y, s.up.z);                     // mirrored 'up' (zero stays zero = default frame)
            c.view = new Vector3(-s.view.x, s.view.y, s.view.z);             // mirrored projector direction
            c.flip = !s.flip;
            c.angle = (360f - s.angle) % 360f;
            // snap to the real surface: find the picture where the copy faces the camera most and pick that pixel
            string best = null; float bd = -2f;
            foreach (string f in Facings) { float d = Vector3.Dot(c.normal, Toward(f)); if (d > bd && Map(cls, "base", f) != null) { bd = d; best = f; } }
            if (best != null)
            {
                StickerMap m = Map(cls, "base", best);
                Vector2 px = m.Project(c.pos);
                if (PickSpot(cls, "base", best, Mathf.RoundToInt(px.x), Mathf.RoundToInt(px.y), out Vector3 pos, out Vector3 nrm, out int layer, s.overTrim) && layer == s.layer
                    && (pos - c.pos).magnitude < s.size)
                { c.pos = pos; c.normal = nrm; }
            }
            return c;
        }

        /// <summary>Height of a sticker layer above the Knight body. The Acastus's pads are a separate high overlay (0.0505); Knights whose pads are part
        /// of the upper-hull overlay (Dominus, Cerastus: hull at +0.0025, top turrets above it) put pad stickers just over the hull (2026-11-17).</summary>
        public static float LayerAltitude(int layer, KnightClassDef cls = null)
        {
            if (layer == 192) return 0.0006f;
            // Armiger (2026-11-17): its pauldrons are part of the body picture (no pad overlay, no hull) - pad stickers sit with the body, under the arm guns
            if (layer == 128 && cls?.defName != null && cls.defName.Contains("Armiger")) return 0.003f;
            if (layer == 128) return cls != null && cls.hull != null ? 0.0029f : 0.0505f;
            return 0.003f;
        }

        public static void Release(List<LayerTex> l)
        {
            if (l == null) return;
            foreach (LayerTex t in l) { if (t.mat != null) UnityEngine.Object.Destroy(t.mat); if (t.tex != null) UnityEngine.Object.Destroy(t.tex); }
            l.Clear();
        }

        // ------------------------------------------------------------------ drawing on the map
        class Cache { public int version = -1, designs = -1; public string variant; public readonly Dictionary<string, List<LayerTex>> byFacing = new Dictionary<string, List<LayerTex>>(); }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VehiclePawn, Cache> caches = new System.Runtime.CompilerServices.ConditionalWeakTable<VehiclePawn, Cache>();

        public static void Draw(VehiclePawn v, Vector3 loc, Rot8 rot, float rotation)
        {
            CompKnightHead kc = v.GetComp<CompKnightHead>();
            if (kc?.stickers == null || kc.stickers.Count == 0 || rot.IsDiagonal) return;
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            if (cls == null) return;
            string facing = rot == Rot8.North ? "north" : rot == Rot8.East ? "east" : rot == Rot8.South ? "south" : "west";
            Cache c = caches.GetOrCreateValue(v);
            string variant = Variant(v, cls);
            if (c.version != kc.stickerVersion || c.variant != variant || c.designs != DesignsVersion)
            {
                foreach (List<LayerTex> l in c.byFacing.Values) Release(l);
                c.byFacing.Clear(); c.version = kc.stickerVersion; c.variant = variant; c.designs = DesignsVersion;
            }
            if (!c.byFacing.TryGetValue(facing, out List<LayerTex> list))
            {
                list = Build(cls, variant, facing, kc.stickers);
                c.byFacing[facing] = list;
            }
            if (list.Count == 0) return;
            GraphicData gd = v.VehicleDef.graphicData;
            Quaternion q = rotation != 0f ? Quaternion.AngleAxis(rotation, Vector3.up) : Quaternion.identity;
            foreach (LayerTex t in list)
            {
                float s = gd.drawSize.x / t.mapW;
                Vector3 off = new Vector3((t.x0 + t.w / 2f - t.mapW / 2f) * s, 0f, (t.mapH / 2f - (t.y0 + t.h / 2f)) * s);
                Vector3 p = loc + q * off;
                p.y = loc.y + LayerAltitude(t.layer, cls);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, q, new Vector3(t.w * s, 1f, t.h * s)), t.mat, 0);
            }
        }
    }

    [HarmonyPatch(typeof(VehicleDrawTracker), nameof(VehicleDrawTracker.DynamicDrawPhaseAt))]
    public static class Patch_DrawStickers
    {
        static readonly AccessTools.FieldRef<VehicleDrawTracker, VehiclePawn> vehicleRef = AccessTools.FieldRefAccess<VehicleDrawTracker, VehiclePawn>("vehicle");

        // PERF (2026-11-13): typed arguments (no object[] per call) - this runs for EVERY Vehicle Framework vehicle every frame
        static void Postfix(VehicleDrawTracker __instance, DrawPhase phase, ref Vector3 drawLoc, Rot8 rot, float rotation)
        {
            if (phase != DrawPhase.Draw) return;
            try
            {
                VehiclePawn v = vehicleRef(__instance);
                if (v == null) return;
                Stickers.Draw(v, drawLoc, rot, rotation);
            }
            catch (Exception e) { Log.ErrorOnce("[LIK] sticker drawing failed: " + e, 0x51C4E2); }
        }
    }

    /// <summary>Big preview: turn the Knight, pick a sticker design and click the Knight to place it; size, turn and three colours per sticker.</summary>
    public class Window_StickerEditor : Window
    {
        readonly KnightClassDef cls;
        readonly PaintChoice p;
        readonly Action onChange;
        int facingIdx = 2;               // south
        string armed;                    // a design waiting to be placed
        int selected = -1;
        int colourSlot;
        bool dragging;
        bool snapTurn = true;              // turn slider snaps to 15 degree steps
        bool draggingSticker;             // the selected sticker follows the mouse while the button is held
        float lastDragMove;
        Vector2 grabOffset;               // where on the sticker it was grabbed (picture pixels from its centre): no jump to the cursor
        Vector2 designScroll, listScroll, catScroll;
        int recordedVersion = -1; float lastRecorded;      // StickerRecorder (bug reports)
        string category = "Basic";
        int builtVersion = -1, version;
        string builtFacing;
        List<Stickers.LayerTex> built = new List<Stickers.LayerTex>();

        // 2026-11-13 (user: cramped): grows with the screen, up to 1500 x 980
        public override Vector2 InitialSize => CogUI.FrameReady ? new Vector2(Mathf.Min(1650f, UI.screenWidth - 20f), Mathf.Min(1076f, UI.screenHeight - 20f))
                                                                 : new Vector2(Mathf.Min(1500f, UI.screenWidth - 40f), Mathf.Min(980f, UI.screenHeight - 40f));
        protected override float Margin => CogUI.FrameReady ? 0f : base.Margin;
        bool cogMode;                      // drawing inside the cogitator machine (user 2026-11-16: same UI style as the hangar)

        public override void PreOpen()
        {
            base.PreOpen();
            if (!CogUI.FrameReady) return;
            doWindowBackground = false; drawShadow = false; doCloseX = false; draggable = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            cogMode = CogUI.FrameReady;
            if (!cogMode) { DrawBody(inRect); return; }
            Rect S = CogUI.MachineBack(inRect);
            GUI.BeginGroup(S);
            DrawBody(new Rect(0f, 0f, S.width, S.height));
            GUI.EndGroup();
            CogUI.MachineFront(inRect);
            Rect f = CogUI.FrameOf(inRect);
            if (CogUI.Key(new Rect(f.xMax - 300f, inRect.height - 78f, 280f, 56f), "DONE", true)) Close();
        }

        // the same layout in both looks: these pick the cogitator widget or the vanilla one
        bool Btn(Rect r, string label) { return cogMode ? CogUI.Button(r, label) : Widgets.ButtonText(r, label); }
        void Lab(Rect r, string s) { if (cogMode) CogUI.Lbl(r, s); else Widgets.Label(r, s); }
        void Chk(Rect r, string label, ref bool v) { if (cogMode) CogUI.Check(r, label, ref v); else Widgets.CheckboxLabeled(r, label, ref v); }
        void HLine(float x, float y, float w) { if (cogMode) CogUI.Fill(new Rect(x, y, w, 1f), CogUI.G2); else Widgets.DrawLineHorizontal(x, y, w); }
        float Sld(Rect r, float v, float min, float max, bool mid, string label, string l2, string l3, float roundTo)
        {
            if (!cogMode) return Widgets.HorizontalSlider(r, v, min, max, mid, label, l2, l3, roundTo);
            float n = CogUI.Slider(r, v, min, max, label);
            return roundTo > 0f ? Mathf.Round(n / roundTo) * roundTo : n;
        }
        void Box(Rect r, Color c)
        {
            if (!cogMode) { Widgets.DrawBoxSolid(r, c); return; }
            // old palette -> cogitator: the green 'selected' tint, the sticker tiles and the Knight view (true-colour glass), everything else panel
            if (Mathf.Abs(c.g - 0.3f) < 0.01f && c.r < 0.2f) { Widgets.DrawBoxSolid(r, CogUI.PANEL_SEL); CogUI.Border(r, CogUI.G4, 1f); }
            else if (Mathf.Abs(c.r - 0.1f) < 0.01f || c.a >= 0.95f) Widgets.DrawBoxSolid(r, CogUI.GLASS);
            else { Widgets.DrawBoxSolid(r, CogUI.PANEL); CogUI.Border(r, CogUI.G1, 1f); }
        }

        public Window_StickerEditor(KnightClassDef cls, PaintChoice p, Action onChange)
        {
            this.cls = cls; this.p = p; this.onChange = onChange;
            doCloseX = true; absorbInputAroundWindow = true; forcePause = true;
            if (p.stickers.Count > 0) selected = 0;
        }

        public override void PostClose()
        {
            if (StickerRecorder.On) { StickerRecorder.Snapshot(cls, p.stickers, "editor closed"); StickerRecorder.Stop(); }
            Stickers.Release(built); onChange?.Invoke();
        }

        void Changed() { version++; fullRebuild = true; }
        void ChangedLayer(int layer) { version++; if (!fullRebuild) dirtyLayers.Add(layer); }
        bool fullRebuild = true;
        readonly HashSet<int> dirtyLayers = new HashSet<int>();
        int builtDesigns = -1;

        void DrawBody(Rect inRect)
        {
            if (cogMode) CogUI.Header(inRect, "HOLO.DISP. ++ TRANSFERS // " + cls.label.ToUpperInvariant() + " [PRIMARY]", "SCRIPTORIUM.V3");
            else { Text.Font = GameFont.Medium; Widgets.Label(new Rect(0f, 0f, 700f, 34f), "Stickers: " + cls.label); Text.Font = GameFont.Small; }
            string facing = Stickers.Facings[facingIdx];

            // ---- the Knight, big
            float side = Mathf.Max(400f, Mathf.Min(inRect.height - 80f, inRect.width - 600f));
            Rect view = new Rect(0f, 74f, side, side);
            if (Btn(new Rect(0f, 38f, 130f, 30f), "< Turn")) facingIdx = (facingIdx + 1) % 4;
            if (Btn(new Rect(140f, 38f, 130f, 30f), "Turn >")) facingIdx = (facingIdx + 3) % 4;
            Lab(new Rect(284f, 42f, side - 284f - 160f, 26f), "Facing " + facing + (armed != null ? "   -   click the armour to place: " + Stickers.DesignLabel(armed) :
                selected >= 0 ? "   -   click the armour to move the selected sticker" : ""));
            // bug recorder (user 2026-11-17): every change -> a 4-facing picture + log in <RimWorld>/LIK_StickerRecorder
            Rect recR = new Rect(side - 150f, 38f, 150f, 30f);
            if (Btn(recR, StickerRecorder.On ? "STOP RECORDING" : "Record (bug report)"))
            {
                if (StickerRecorder.On)
                {
                    StickerRecorder.Stop();
                    try { System.Diagnostics.Process.Start("explorer.exe", "\"" + StickerRecorder.Dir + "\""); } catch (Exception) { }
                }
                else { StickerRecorder.Start(cls); StickerRecorder.Snapshot(cls, p.stickers, "start"); recordedVersion = version; }
            }
            TooltipHandler.TipRegion(recR, "Records what you do here for a bug report: after every change a picture of the Knight in all four facings and a log are saved to\n"
                + System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? "", "LIK_StickerRecorder") + "\nClick again to stop (opens the folder).");
            if (StickerRecorder.On && version != recordedVersion && !draggingSticker && Time.realtimeSinceStartup - lastRecorded > 0.5f)
            {
                recordedVersion = version; lastRecorded = Time.realtimeSinceStartup;
                StickerRecorder.Snapshot(cls, p.stickers, "after change, editor facing " + facing + (selected >= 0 ? ", selected #" + selected : ""));
            }
            Box(view, new Color(0.05f, 0.06f, 0.09f, 0.95f));
            if (fullRebuild || builtFacing != facing || builtDesigns != Stickers.DesignsVersion)
            {
                Stickers.Release(built);
                built = Stickers.Build(cls, "base", facing, p.stickers);
                builtFacing = facing; builtDesigns = Stickers.DesignsVersion; fullRebuild = false; dirtyLayers.Clear();
            }
            else if (dirtyLayers.Count > 0)
            {
                // PERF (2026-11-13): dragging only rebuilds the part the sticker is on
                foreach (int l in dirtyLayers)
                {
                    var old = built.Where(t => t.layer == l).ToList();
                    Stickers.Release(old);
                    built.RemoveAll(t => t.layer == l);
                    built.AddRange(Stickers.Build(cls, "base", facing, p.stickers, l));
                }
                dirtyLayers.Clear();
            }
            Rect fit = KnightPaint.DrawFacing(view.ContractedBy(4f), cls, p, facing, built);
            // the mouse button was let go (anywhere, even outside the window): stop dragging - nothing stays stuck to the cursor
            if (!Input.GetMouseButton(0)) { draggingSticker = false; dragging = false; }
            if (draggingSticker && Event.current.type == EventType.MouseDrag && fit.width > 0f && Time.realtimeSinceStartup - lastDragMove > 0.05f
                && selected >= 0 && selected < p.stickers.Count)
            {
                lastDragMove = Time.realtimeSinceStartup;
                StickerMap dm = Stickers.Map(cls, "base", facing);
                Vector2 mp2 = Event.current.mousePosition;
                int dx = Mathf.FloorToInt((mp2.x - fit.x) / fit.width * dm.w - grabOffset.x), dy = Mathf.FloorToInt((mp2.y - fit.y) / fit.height * dm.h - grabOffset.y);
                KnightSticker ds = p.stickers[selected];
                // over armour: follow it (onto another part too); off the armour: slide along the nearest edge of the sticker's own part
                // Over trim stickers move over the gold trim too (user 2026-11-13: could not reach the centre trim)
                bool got; Vector3 dpos, dnrm; int dlayer;
                if (ds.pieceOnly && Stickers.HasPieces(cls))
                {
                    // 'This piece only' (user 2026-11-17, 2nd request): moves freely over any armour, even off its piece (only the part on its
                    // remembered piece shows - chevrons from squares hanging over piece edges); it keeps its piece and part
                    got = Stickers.PickSpot(cls, "base", facing, dx, dy, out dpos, out dnrm, out _, true);
                    if (!got && Stickers.NearestOnLayer(cls, "base", facing, dx, dy, ds.layer, out int px2, out int py2, true))
                        got = Stickers.PickSpot(cls, "base", facing, px2, py2, out dpos, out dnrm, out _, true);
                    dlayer = ds.layer;
                }
                else
                {
                    got = Stickers.PickSpot(cls, "base", facing, dx, dy, out dpos, out dnrm, out dlayer, ds.overTrim);
                    if (!got && Stickers.NearestOnLayer(cls, "base", facing, dx, dy, ds.layer, out int nx, out int ny, ds.overTrim))
                        got = Stickers.PickSpot(cls, "base", facing, nx, ny, out dpos, out dnrm, out dlayer, ds.overTrim);
                }
                StickerRecorder.Click("drag #" + selected, facing, dx, dy, got, dpos, dnrm, dlayer);
                if (got)
                {
                    if (dlayer != ds.layer) { ChangedLayer(ds.layer); ds.layer = dlayer; }
                    Stickers.KeepScreenUp(ds, dm, dpos, dnrm); ds.view = Stickers.Toward(facing); ds.pos = dpos; ds.normal = dnrm; ChangedLayer(ds.layer);
                }
                Event.current.Use();
            }
            if (Mouse.IsOver(fit) && Event.current.type == EventType.MouseDown && Event.current.button == 0 && fit.width > 0f)
            {
                StickerMap m = Stickers.Map(cls, "base", facing);
                Vector2 mp = Event.current.mousePosition;
                int px = Mathf.FloorToInt((mp.x - fit.x) / fit.width * m.w), py = Mathf.FloorToInt((mp.y - fit.y) / fit.height * m.h);
                grabOffset = Vector2.zero;
                // pressing on a sticker selects it and grabs it right there (the front one if they overlap)
                int hit = -1;
                if (armed == null)
                {
                    Vector3 tw2 = Stickers.Toward(facing);
                    for (int i = p.stickers.Count - 1; i >= 0 && hit < 0; i--)
                    {
                        KnightSticker st = p.stickers[i];
                        if (Vector3.Dot(st.normal, tw2) < -0.15f) continue;
                        Vector2 c = m.Project(st.pos);
                        float r = (st.wrap ? 12f : st.size * 0.5f) / m.unitsPerPixel;
                        if ((new Vector2(px, py) - c).sqrMagnitude <= r * r) { hit = i; grabOffset = new Vector2(px, py) - c; }
                    }
                }
                if (hit >= 0)
                {
                    selected = hit; draggingSticker = true;
                    StickerRecorder.Click("grab #" + hit, facing, px, py, true, p.stickers[hit].pos, p.stickers[hit].normal, p.stickers[hit].layer);
                    Event.current.Use();
                }
                else if (Stickers.PickSpot(cls, "base", facing, px, py, out Vector3 pos, out Vector3 nrm, out int layer, true))
                {
                    StickerRecorder.Click(armed != null ? "place " + armed : "move #" + selected, facing, px, py, true, pos, nrm, layer);
                    bool onTrim = m.Layer(px, py) == 0;        // clicked the gold trim: that sticker must cover trim, or it would not show
                    if (armed != null)
                    {
                        var s = new KnightSticker { design = armed, pos = pos, normal = nrm, layer = layer, overTrim = onTrim, view = Stickers.Toward(facing) };
                        if (selected >= 0 && selected < p.stickers.Count) { KnightSticker last = p.stickers[selected]; s.size = last.size; s.c1 = last.c1; s.c2 = last.c2; s.c3 = last.c3; s.overTrim |= last.overTrim; }
                        if (Stickers.DefaultColours(armed, out Color d1, out Color d2, out Color d3)) { s.c1 = d1; s.c2 = d2; s.c3 = d3; }   // the design's own starting colours (e.g. the Aquila: black)
                        p.stickers.Add(s); selected = p.stickers.Count - 1; armed = null;
                    }
                    else if (selected >= 0 && selected < p.stickers.Count && p.stickers[selected].pieceOnly && Stickers.HasPieces(cls))
                    {
                        // 'This piece only': a click elsewhere moves its spot there; it keeps its piece and part (only its piece shows)
                        KnightSticker s = p.stickers[selected];
                        ChangedLayer(s.layer); Stickers.KeepScreenUp(s, m, pos, nrm); s.view = Stickers.Toward(facing); s.pos = pos; s.normal = nrm;
                    }
                    else if (selected >= 0 && selected < p.stickers.Count) { KnightSticker s = p.stickers[selected]; if (onTrim) s.overTrim = true; ChangedLayer(s.layer); Stickers.KeepScreenUp(s, m, pos, nrm); s.view = Stickers.Toward(facing); s.pos = pos; s.normal = nrm; s.layer = layer; }
                    draggingSticker = selected >= 0;
                    Changed();
                }
                else
                {
                    StickerRecorder.Click("click off armour", facing, px, py, false, Vector3.zero, Vector3.zero, 0);
                    Messages.Message("Stickers go on armour plates or their gold trim (carapace, shoulder pads, legs).", MessageTypeDefOf.RejectInput, false);
                }
                Event.current.Use();
            }

            // ---- designs (layout 2026-11-13, user: cramped): the panel takes the space right of the Knight; Done, colours, options, list, grid
            float rx = view.xMax + 20f, rw = inRect.width - rx;
            Lab(new Rect(rx, 38f, rw - 270f, 24f), "Designs (click one, then the Knight)");
            if (Btn(new Rect(inRect.width - 256f, 36f, 150f, 26f), "Open custom folder"))
            {
                try { Directory.CreateDirectory(Stickers.CustomDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Stickers.CustomDir + "\""); }
                catch (Exception e) { Messages.Message("Could not open " + Stickers.CustomDir + ": " + e.Message, MessageTypeDefOf.RejectInput, false); }
            }
            TooltipHandler.TipRegion(new Rect(inRect.width - 256f, 36f, 150f, 26f), "Your own stickers: " + Stickers.CustomDir + "\nPNG pictures; pure red / green / blue ones become recolourable.");
            if (Btn(new Rect(inRect.width - 100f, 36f, 100f, 26f), "Reload")) { Stickers.Reload(); if (!Stickers.Categories.Contains(category)) category = "Basic"; }
            // category tabs: ONE row with a sideways scroll bar (user 2026-11-17: the wrapped rows took half the design grid);
            // the mouse wheel over the row scrolls it sideways too
            float cyp = 68f;
            Rect catOuter = new Rect(rx, cyp, rw, 44f);
            float catW = 0f;
            foreach (string cat in Stickers.Categories) catW += Text.CalcSize(cat).x + 26f;
            Rect catView = new Rect(0f, 0f, Mathf.Max(catW, rw), 26f);
            if (Event.current.type == EventType.ScrollWheel && Mouse.IsOver(catOuter) && catView.width > rw)
            {
                catScroll.x = Mathf.Clamp(catScroll.x + Event.current.delta.y * 40f, 0f, catView.width - rw);
                Event.current.Use();
            }
            Widgets.BeginScrollView(catOuter, ref catScroll, catView, true);
            float cxp = 0f;
            foreach (string cat in Stickers.Categories)
            {
                float bw = Text.CalcSize(cat).x + 20f;
                Rect cb = new Rect(cxp, 0f, bw, 26f);
                Box(cb, cat == category ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : new Color(0.08f, 0.1f, 0.13f, 0.85f));
                Text.Anchor = TextAnchor.MiddleCenter; Lab(cb, cat); Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(cb)) { category = cat; designScroll = Vector2.zero; }
                cxp += bw + 6f;
            }
            Widgets.EndScrollView();
            cyp += 14f;                                    // grid starts below the tab row + its scroll bar (gridTop = cyp + 34)
            bool fourCols = rw >= 560f;
            const float rowH = 36f;
            // height of the selected-sticker block: 3 slider rows, option row(s), mirror row, colour tabs + wheel
            float selH = 3 * rowH + 4f + (fourCols ? 34f : 68f) + 40f + 36f + 200f;
            float bottom = inRect.height - 4f;
            float gridTop = cyp + 34f;
            // what is left goes to the design grid (at least 2 rows) and the list (3 to 6 rows)
            float left = bottom - selH - gridTop - 30f - 16f;
            float listH = Mathf.Clamp(left * 0.35f, 90f, 180f);
            float gridH = Mathf.Max(150f, left - listH);
            List<string> shown = Stickers.Designs.Where(x => Stickers.Category(x) == category).ToList();
            Rect dOuter = new Rect(rx, gridTop, rw, gridH);
            const float cell = 74f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((rw - 16f) / cell));
            Rect dView = new Rect(0f, 0f, rw - 16f, Mathf.CeilToInt(shown.Count / (float)perRow) * cell);
            Widgets.BeginScrollView(dOuter, ref designScroll, dView);
            for (int i = 0; i < shown.Count; i++)
            {
                string d = shown[i];
                Rect b = new Rect((i % perRow) * cell, (i / perRow) * cell, cell - 4f, cell - 4f);
                Box(b, armed == d ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : new Color(0.1f, 0.12f, 0.16f, 0.9f));
                Texture2D t = Stickers.DesignThumb(d);
                if (t != null) GUI.DrawTexture(b.ContractedBy(5f), t, ScaleMode.ScaleToFit);
                TooltipHandler.TipRegion(b, Stickers.DesignLabel(d) + (Stickers.Fixed(d) ? " (own colours)" : ""));
                if (Widgets.ButtonInvisible(b)) armed = armed == d ? null : d;
            }
            Widgets.EndScrollView();

            // ---- placed stickers
            float y = dOuter.yMax + 6f;
            Lab(new Rect(rx, y, rw, 24f), "On this Knight (" + p.stickers.Count + ")  -  top of the list is in front");
            y += 26f;
            Rect lOuter = new Rect(rx, y, rw, listH);
            Box(lOuter, new Color(0.05f, 0.06f, 0.08f, 0.6f));
            Rect lView = new Rect(0f, 0f, rw - 16f, p.stickers.Count * 30f);
            Widgets.BeginScrollView(lOuter, ref listScroll, lView);
            // top row = drawn last = in front; stickers lower in the list are drawn first and can be covered (or cut by an eraser above them)
            for (int r = 0; r < p.stickers.Count; r++)
            {
                int i = p.stickers.Count - 1 - r;
                KnightSticker s = p.stickers[i];
                Rect row = new Rect(0f, r * 30f, lView.width, 28f);
                Box(row, i == selected ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : new Color(0.08f, 0.1f, 0.13f, 0.85f));
                Texture2D t = Stickers.DesignThumb(s.design);
                if (t != null) GUI.DrawTexture(new Rect(row.x + 2f, row.y + 2f, 24f, 24f), t, ScaleMode.ScaleToFit);
                Lab(new Rect(row.x + 32f, row.y + 4f, row.width - 170f, 24f), (s.erase ? "[eraser] " : "") + Stickers.DesignLabel(s.design) + " (" + KnightSticker.LayerLabel(s.layer) + (s.overTrim ? ", over trim" : "") + (s.pieceOnly ? ", this piece" : "") + (s.sideOnly ? ", this side" : "") + ")");
                Rect upR = new Rect(row.xMax - 132f, row.y + 2f, 24f, 24f), dnR = new Rect(row.xMax - 104f, row.y + 2f, 24f, 24f);
                TooltipHandler.TipRegion(upR, "Bring forward (drawn over the stickers below it)");
                TooltipHandler.TipRegion(dnR, "Send back");
                if (i < p.stickers.Count - 1 && Widgets.ButtonImage(upR, TexButton.ReorderUp)) { (p.stickers[i], p.stickers[i + 1]) = (p.stickers[i + 1], p.stickers[i]); if (selected == i) selected = i + 1; else if (selected == i + 1) selected = i; Changed(); break; }
                if (i > 0 && Widgets.ButtonImage(dnR, TexButton.ReorderDown)) { (p.stickers[i], p.stickers[i - 1]) = (p.stickers[i - 1], p.stickers[i]); if (selected == i) selected = i - 1; else if (selected == i - 1) selected = i; Changed(); break; }
                if (Btn(new Rect(row.xMax - 74f, row.y + 2f, 72f, 24f), "Remove")) { p.stickers.RemoveAt(i); selected = Mathf.Min(selected, p.stickers.Count - 1); Changed(); break; }
                if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - 136f, row.height))) { selected = i; armed = null; }
            }
            Widgets.EndScrollView();
            y = lOuter.yMax + 10f;

            // ---- the selected sticker
            if (selected >= 0 && selected < p.stickers.Count)
            {
                KnightSticker s = p.stickers[selected];
                HLine(rx, y - 4f, rw);
                const float lab = 80f;                                 // slider rows: name on the left, the slider's value label above its bar
                Lab(new Rect(rx, y + 10f, lab, 24f), "Size");
                float ns = Sld(new Rect(rx + lab, y + 14f, rw - lab - 6f, 18f), s.size, 3f, 300f, false, Mathf.RoundToInt(s.size).ToString(), null, null, 1f);
                y += rowH;
                Lab(new Rect(rx, y + 10f, lab, 24f), "Turn");
                float na = Sld(new Rect(rx + lab, y + 14f, rw - lab - 200f, 18f), s.angle, 0f, 359f, false, Mathf.RoundToInt(s.angle) + "°", null, null, 1f);
                if (snapTurn && na != s.angle) na = Mathf.Round(na / 15f) * 15f % 360f;
                if (Btn(new Rect(rx + rw - 186f, y + 8f, 48f, 26f), "-90")) na = (s.angle + 270f) % 360f;
                if (Btn(new Rect(rx + rw - 134f, y + 8f, 48f, 26f), "+90")) na = (s.angle + 90f) % 360f;
                Chk(new Rect(rx + rw - 78f, y + 9f, 76f, 24f), "Snap", ref snapTurn);
                TooltipHandler.TipRegion(new Rect(rx + rw - 78f, y + 9f, 76f, 24f), "Snap the turn to 15 degree steps (0, 45, 90 ... are easy to hit).");
                y += rowH;
                if (ns != s.size || na != s.angle) { s.size = ns; s.angle = na; Changed(); }
                Lab(new Rect(rx, y + 10f, lab, 24f), "Repeat");
                float nt = Sld(new Rect(rx + lab, y + 14f, rw - lab - 6f, 18f), s.tiles, 1f, 20f, false, s.tiles <= 1.01f ? "once" : "x" + Mathf.RoundToInt(s.tiles), null, null, 1f);
                TooltipHandler.TipRegion(new Rect(rx, y, rw, rowH), "Pattern repeat: tiles the picture across the sticker - a big sticker with a high repeat gives many small checks / stripes.");
                if (nt != s.tiles) { s.tiles = nt; Changed(); }
                y += rowH + 4f;
                // options: one row of four (two rows of two on narrow screens)
                float cw = fourCols ? (rw - 18f) / 4f : (rw - 6f) / 2f;
                float oy = y;
                Rect Opt(int k) { int col = fourCols ? k : k % 2, row = fourCols ? 0 : k / 2; return new Rect(rx + col * (cw + 6f), oy + row * 34f, cw, 28f); }
                bool fl = s.flip, er = s.erase, ot = s.overTrim, wr = s.wrap;
                string[] names = { "Flip", "Eraser", "Over trim", "Wrap part" };
                for (int k = 0; k < 4; k++) Box(Opt(k), new Color(0.08f, 0.1f, 0.13f, 0.7f));
                Chk(Opt(0).ContractedBy(6f, 2f), names[0], ref fl);
                Chk(Opt(1).ContractedBy(6f, 2f), names[1], ref er);
                Chk(Opt(2).ContractedBy(6f, 2f), names[2], ref ot);
                Chk(Opt(3).ContractedBy(6f, 2f), names[3], ref wr);
                TooltipHandler.TipRegion(Opt(0), "Mirror the picture.");
                TooltipHandler.TipRegion(Opt(1), "Eraser: this sticker's shape cuts away the stickers BELOW it in the list and shows the plain armour paint. "
                    + "E.g. a big hazard-stripe sticker with square erasers above it leaves only a band of stripes.");
                TooltipHandler.TipRegion(Opt(2), "Over trim: the sticker also covers the gold trim of its part (edges, rims, the groin plate), "
                    + "not only the armour plates. Off = it stops at the trim, like a transfer laid between the rims.");
                TooltipHandler.TipRegion(Opt(3), "Wrap whole part: wraps the picture round the whole part it is on (carapace, leg or shoulder pad), sides included, "
                    + "instead of a flat transfer. For patterns: Size and Repeat set the tile size, Turn turns the pattern. Use erasers to keep only a band of it.");
                if (fl != s.flip) { s.flip = fl; Changed(); }
                if (er != s.erase) { s.erase = er; Changed(); }
                if (ot != s.overTrim) { s.overTrim = ot; Changed(); }
                if (wr != s.wrap) { s.wrap = wr; if (wr) s.tiles = Mathf.Clamp(Mathf.Round(s.size / 20f), 1f, 20f); Changed(); }   // ~20-unit tiles: finer ones alias into moire
                y += fourCols ? 34f : 68f;
                if (Btn(new Rect(rx, y, Mathf.Min(rw, 260f), 30f), "Mirror copy to other side"))
                {
                    p.stickers.Add(Stickers.MirrorCopy(cls, s)); selected = p.stickers.Count - 1; Changed();
                }
                // the rest of the row: 'This piece only' (classes with piece maps) and 'This side only' (every class)
                float optX = rx + Mathf.Min(rw, 260f) + 14f, optW = rw - Mathf.Min(rw, 260f) - 14f;
                bool piecesHere = Stickers.HasPieces(cls);
                {
                    // user 2026-11-17: chevrons - an enlarged sticker on one half of the Knight, its mirror copy on the other half
                    Rect sr = piecesHere ? new Rect(optX + optW / 2f + 4f, y, optW / 2f - 4f, 30f) : new Rect(optX, y, Mathf.Min(220f, optW), 30f);
                    bool so = s.sideOnly;
                    Chk(sr, "This side only", ref so);
                    TooltipHandler.TipRegion(sr, "This side only: the sticker shows only on the half of the Knight (left or right of the centre line) its spot is on. "
                        + "Size it up to fill that half, then 'Mirror copy' puts its twin on the other half - hazard chevrons.");
                    if (so != s.sideOnly) { s.sideOnly = so; Changed(); }
                }
                if (piecesHere)
                {
                    // user 2026-11-17: 'optional to appear on other body parts, so you can size it up to fit the entire piece'
                    Rect pr = new Rect(optX, y, optW / 2f - 4f, 30f);
                    bool po = s.pieceOnly;
                    Chk(pr, "This piece only", ref po);
                    TooltipHandler.TipRegion(pr, "This piece only: the sticker stays on the one armour piece under it (a pad shell, the lip under it, a plate, a knee guard) "
                        + "and bends round all of that piece's faces. Size it up to cover the whole piece - it never spills onto the next part.");
                    if (po != s.pieceOnly) { s.pieceOnly = po; s.pieceId = 0; Changed(); }      // switched on: it takes the piece under its spot now
                }
                y += 40f;
                if (s.erase) { Text.Font = GameFont.Tiny; Lab(new Rect(rx, y, rw, 40f), "Eraser: no colours - it shows the armour paint through the stickers below it."); Text.Font = GameFont.Small; }
                else if (!Stickers.Fixed(s.design))
                {
                    for (int k = 0; k < 3; k++)
                    {
                        Rect tab = new Rect(rx + k * (rw / 3f), y, rw / 3f - 6f, 30f);
                        Color cur = k == 0 ? s.c1 : k == 1 ? s.c2 : s.c3;
                        Box(tab, k == colourSlot ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : new Color(0.08f, 0.1f, 0.13f, 0.85f));
                        Rect sw = new Rect(tab.x + 5f, tab.y + 5f, 20f, 20f);
                        Box(sw, cur);
                        GUI.color = new Color(0f, 0f, 0f, 0.8f); Widgets.DrawBox(sw, 1); GUI.color = Color.white;
                        Lab(new Rect(tab.x + 32f, tab.y + 5f, tab.width - 32f, 24f), "Colour " + (k + 1));
                        if (Widgets.ButtonInvisible(tab)) colourSlot = k;
                    }
                    y += 36f;
                    Color c = colourSlot == 0 ? s.c1 : colourSlot == 1 ? s.c2 : s.c3;
                    // the wheel stays left of the Done button's column
                    if (ColourWheel.Draw(new Rect(rx, y, rw - 136f, Mathf.Min(200f, inRect.height - y - 2f)), ref c, ref dragging, "LIKsticker", cogMode))
                    {
                        if (colourSlot == 0) s.c1 = c; else if (colourSlot == 1) s.c2 = c; else s.c3 = c;
                        Changed();
                    }
                }
                else { Text.Font = GameFont.Tiny; Lab(new Rect(rx, y, rw, 40f), "This sticker keeps its own painted colours."); Text.Font = GameFont.Small; }
            }

            if (!cogMode && Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 40f, 120f, 36f), "Done")) Close();
        }
    }

    /// <summary>RimWorld's own HSV colour wheel plus a brightness slider and a few quick colours.</summary>
    public static class ColourWheel
    {
        static readonly Color[] quick = { Color.white, new Color(0.08f, 0.08f, 0.09f), new Color(0.83f, 0.16f, 0.16f), new Color(0.94f, 0.75f, 0.12f), new Color(0.2f, 0.33f, 0.66f),
                                          new Color(0.27f, 0.44f, 0.2f), new Color(0.85f, 0.69f, 0.23f), new Color(0.76f, 0.76f, 0.78f), new Color(0.86f, 0.8f, 0.66f), new Color(0.34f, 0.3f, 0.52f) };

        /// <summary>Returns true when the colour changed.</summary>
        public static bool Draw(Rect r, ref Color c, ref bool dragging, string name, bool cog = false)
        {
            Color before = c;
            float wheel = Mathf.Min(r.height - 44f, r.width * 0.5f);
            Rect wr = new Rect(r.x, r.y, wheel, wheel);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Widgets.HSVColorWheel(wr, ref c, ref dragging, v, name);
            // brightness
            float nv = cog ? CogUI.Slider(new Rect(r.x, wr.yMax + 14f, wheel + 70f, 16f), v, 0f, 1f, "BRIGHT")
                           : Widgets.HorizontalSlider(new Rect(r.x, wr.yMax + 22f, wheel, 18f), v, 0f, 1f, false, "Brightness");
            if (Mathf.Abs(nv - v) > 0.001f) { Color.RGBToHSV(c, out h, out s, out _); c = Color.HSVToRGB(h, s, nv); }
            // quick colours + the current colour
            float qx = wr.xMax + 12f, qw = r.xMax - qx;
            Widgets.DrawBoxSolid(new Rect(qx, r.y, qw, 34f), c);
            GUI.color = new Color(0f, 0f, 0f, 0.8f); Widgets.DrawBox(new Rect(qx, r.y, qw, 34f), 1); GUI.color = Color.white;
            int per = Mathf.Max(1, Mathf.FloorToInt(qw / 28f));
            for (int i = 0; i < quick.Length; i++)
            {
                Rect b = new Rect(qx + (i % per) * 28f, r.y + 42f + (i / per) * 28f, 24f, 24f);
                Widgets.DrawBoxSolid(b, quick[i]);
                GUI.color = new Color(0f, 0f, 0f, 0.8f); Widgets.DrawBox(b, 1); GUI.color = Color.white;
                if (Widgets.ButtonInvisible(b)) c = quick[i];
            }
            return before != c;
        }
    }
}
