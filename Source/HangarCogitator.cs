using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// The hangar window as a 40k cogitator (user 2026-11-16; mockup mod assets/_ui_mockups/hangar_cogitator_mock_v5.png, crest C, one big
    /// purity seal at the side): a riveted gunmetal machine (Textures/UI/LIK_Hangar/bezel.png, Tools/gen_hangar_ui.py) around ONE green CRT.
    /// Left: class select, modules, reactor waveform, litany log. Centre: the Knight (green schematic or painted), the Mechanicus emblem behind it,
    /// slot cards in LEVEL MIRRORED ROWS (top mount / head centred on top; then right/left pairs sorted top to bottom) with leader lines to
    /// crosshairs on the mounts, and a status strip. Right: data-slate of the selected slot (stats, range ring, special rules) + requisition.
    /// The two physical keys under the screen order / close. Text: Bahnschrift (headings) + Consolas (data) when Windows has them.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CogUI
    {
        public const float TW = 1340f, TH = 840f, FX = 60f;
        public static readonly Rect Screen = new Rect(106f, 92f, 1188f, 644f);
        public static readonly Color G1 = C(12, 52, 16), G2 = C(42, 170, 52), G3 = C(92, 245, 92), G4 = C(200, 255, 180), INK = C(2, 10, 3),
                                     RED = C(225, 95, 75), PANEL = C(4, 16, 6), PANEL_SEL = C(14, 50, 20), BAR_DIM = C(24, 96, 30);
        public static Texture2D bezel, crtBg, crtOver, crtRoll, emblem, reticle, keyBrass, keyBrassHover, keySteel, keySteelHover;
        static Font mono, head;
        static GUIStyle style;
        static bool tried;

        static Color C(int r, int g, int b) { return new Color(r / 255f, g / 255f, b / 255f); }
        public static Color C2(int r, int g, int b) { return C(r, g, b); }

        static CogUI()
        {
            string p = "UI/LIK_Hangar/";
            bezel = ContentFinder<Texture2D>.Get(p + "bezel", false);
            crtBg = ContentFinder<Texture2D>.Get(p + "crt_bg", false);
            crtOver = ContentFinder<Texture2D>.Get(p + "crt_overlay", false);
            crtRoll = ContentFinder<Texture2D>.Get(p + "crt_roll", false);
            emblem = ContentFinder<Texture2D>.Get(p + "mech_icon", false);
            reticle = ContentFinder<Texture2D>.Get(p + "reticle", false);
            keyBrass = ContentFinder<Texture2D>.Get(p + "key_brass", false);
            keyBrassHover = ContentFinder<Texture2D>.Get(p + "key_brass_hover", false);
            keySteel = ContentFinder<Texture2D>.Get(p + "key_steel", false);
            keySteelHover = ContentFinder<Texture2D>.Get(p + "key_steel_hover", false);
        }

        /// <summary>All textures present (else the window falls back to the plain screen).</summary>
        public static bool Ready { get { return bezel != null && crtBg != null && keyBrass != null && keySteel != null; } }

        static void Fonts()
        {
            if (tried) return;
            tried = true;
            try { mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 14); } catch { mono = null; }
            try { head = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiBold Condensed", "Bahnschrift Condensed", "Bahnschrift", "Arial Narrow", "Arial" }, 16); } catch { head = null; }
            style = new GUIStyle(Text.CurFontStyle) { richText = false, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
        }

        /// <summary>Text on the cogitator. heading = Bahnschrift, else Consolas.</summary>
        public static void Label(Rect r, string s, Color c, int size = 12, bool heading = false, TextAnchor a = TextAnchor.UpperLeft, bool wrap = false, bool bold = false)
        {
            Fonts();
            Font f = heading ? head : mono;
            style.font = f ?? Text.CurFontStyle.font;
            style.fontSize = size;
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            style.alignment = a;
            style.wordWrap = wrap;
            style.normal.textColor = c;
            GUI.Label(r, s, style);
        }

        public static float Width(string s, int size, bool heading = false)
        {
            Fonts();
            style.font = (heading ? head : mono) ?? Text.CurFontStyle.font;
            style.fontSize = size;
            style.wordWrap = false;
            return style.CalcSize(new GUIContent(s)).x;
        }

        public static void Fill(Rect r, Color c) { Widgets.DrawBoxSolid(r, c); }

        public static void Border(Rect r, Color c, float t = 2f)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>A screen panel: green frame + solid green title bar with dark text (the reference holo-display).</summary>
        public static void Panel(Rect r, string title)
        {
            Border(r, G2);
            if (title == null) return;
            Fill(new Rect(r.x, r.y, r.width, 19f), G3);
            Label(new Rect(r.x + 6f, r.y, r.width - 10f, 19f), title, INK, 14, true, TextAnchor.MiddleLeft);
        }

        public static void Tex(Rect r, Texture t, Color c)
        {
            if (t == null) return;
            GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
            GUI.color = Color.white;
        }

        /// <summary>A 128 px weapon / item icon: the middle band (where the side-on weapon is) stretched into a 2:1 strip, tinted phosphor.</summary>
        public static void IconStrip(Rect area, Texture t, Color tint)
        {
            if (t == null) return;
            float h = Mathf.Min(area.height, area.width / 2f), w = h * 2f;
            Rect r = new Rect(area.center.x - w / 2f, area.center.y - h / 2f, w, h);
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, t, new Rect(0f, 0.25f, 1f, 0.5f));
            GUI.color = Color.white;
        }

        public static void Line(Vector2 a, Vector2 b, Color c, float w = 1f) { Widgets.DrawLine(a, b, c, w); }

        /// <summary>
        /// Old-CRT life (user 2026-11-16): the brighter refresh band rolling down the screen (one pass every ~7 s), a faint flicker, and now and
        /// then a thin interference line darting across. Clipped to the screen; drawn under the scanline overlay.
        /// </summary>
        public static void CrtWave(Rect S)
        {
            float t = Time.realtimeSinceStartup;
            GUI.BeginGroup(S);
            if (crtRoll != null)
            {
                float bandH = 170f, period = 7f;
                float y = ((t % period) / period) * (S.height + bandH) - bandH;
                GUI.DrawTexture(new Rect(0f, y, S.width, bandH), crtRoll, ScaleMode.StretchToFill);
            }
            // flicker: tiny brightness wobble of the whole tube
            float fl = 0.025f * (Mathf.PerlinNoise(t * 9f, 0.3f) - 0.5f) + (Mathf.PerlinNoise(t * 31f, 7.1f) > 0.86f ? 0.03f : 0f);
            if (fl > 0f) Fill(new Rect(0f, 0f, S.width, S.height), new Color(0.6f, 1f, 0.6f, fl));
            else Fill(new Rect(0f, 0f, S.width, S.height), new Color(0f, 0f, 0f, -fl * 1.5f));
            // interference: about every 5 s a thin bright line sweeps down fast
            float g = t % 5.3f;
            if (g < 0.35f)
            {
                float gy = (g / 0.35f) * S.height;
                Fill(new Rect(0f, gy, S.width, 2f), new Color(0.75f, 1f, 0.75f, 0.18f));
                Fill(new Rect(0f, gy + 3f, S.width, 6f), new Color(0.6f, 1f, 0.6f, 0.05f));
            }
            GUI.EndGroup();
        }

        public static void Ring(Vector2 c, float r, Color col, float w = 1f, float from = 0f, float to = 360f, int seg = 48)
        {
            Vector2 last = Vector2.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = (from + (to - from) * i / seg - 90f) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (i > 0) Line(last, p, col, w);
                last = p;
            }
        }

        /// <summary>A physical key under the screen; returns true when pressed.</summary>
        public static bool Key(Rect r, string label, bool brass)
        {
            bool over = Mouse.IsOver(r);
            GUI.DrawTexture(r, brass ? (over ? keyBrassHover : keyBrass) : (over ? keySteelHover : keySteel));
            Label(new Rect(r.x + 1f, r.y + 2f, r.width, r.height), label, new Color(1f, 1f, 1f, 0.25f), 20, true, TextAnchor.MiddleCenter, false, true);
            Label(r, label, brass ? C(40, 26, 6) : C(18, 18, 20), 20, true, TextAnchor.MiddleCenter, false, true);
            return Widgets.ButtonInvisible(r);
        }

        // ------------------------------------------------------------------ the machine for ANY window size (paint window, sticker editor)
        static Texture2D frame9, topBracket, speaker, seal;
        static GUIStyle frameStyle;
        static bool frameLoaded;

        /// <summary>The 9-slice frame + ornaments exist (Tools/gen_hangar_ui.py).</summary>
        public static bool FrameReady
        {
            get
            {
                if (!frameLoaded)
                {
                    frameLoaded = true;
                    frame9 = ContentFinder<Texture2D>.Get("UI/LIK_Hangar/frame9", false);
                    topBracket = ContentFinder<Texture2D>.Get("UI/LIK_Hangar/top_bracket", false);
                    speaker = ContentFinder<Texture2D>.Get("UI/LIK_Hangar/speaker", false);
                    seal = ContentFinder<Texture2D>.Get("UI/LIK_Hangar/seal", false);
                    if (frame9 != null) frameStyle = new GUIStyle { normal = { background = frame9 }, border = new RectOffset(90, 90, 124, 144) };
                }
                return Ready && frame9 != null;
            }
        }

        /// <summary>Window area -> the machine's frame (the left FX strip is outside the frame, room for the purity seal).</summary>
        public static Rect FrameOf(Rect win) { return new Rect(win.x + FX, win.y, win.width - FX, win.height); }

        /// <summary>The CRT inside a frame.</summary>
        public static Rect ScreenOf(Rect win) { Rect f = FrameOf(win); return new Rect(f.x + 46f, f.y + 92f, f.width - 92f, f.height - 196f); }

        /// <summary>Draws the green CRT (background, rolling band, scanlines) and returns its rect. Content goes on top, then MachineFront.</summary>
        public static Rect MachineBack(Rect win)
        {
            Rect S = ScreenOf(win);
            GUI.DrawTexture(S, crtBg, ScaleMode.StretchToFill);
            CrtWave(S);
            GUI.DrawTexture(S, crtOver, ScaleMode.StretchToFill);
            return S;
        }

        /// <summary>The frame over everything: 9-slice body, top bracket with the emblem, vox grille, the purity seal on the left.</summary>
        public static void MachineFront(Rect win)
        {
            Rect f = FrameOf(win);
            if (Event.current.type == EventType.Repaint) frameStyle.Draw(f, false, false, false, false);
            if (topBracket != null) GUI.DrawTexture(new Rect(f.center.x - 160f, f.y, 320f, 96f), topBracket);
            if (speaker != null) GUI.DrawTexture(new Rect(f.x + 100f, f.y, 110f, 92f), speaker);
            if (seal != null) GUI.DrawTexture(new Rect(f.x + 14f - 100f, f.y + 150f - 90f, 220f, 520f), seal);
        }

        /// <summary>Header line at the top of a screen (local coordinates): telemetry text + double rule. Returns the y below it.</summary>
        public static float Header(Rect R, string title, string right)
        {
            Label(new Rect(R.x + 12f, R.y + 4f, R.width - 240f, 20f), title, G3, 16, true);
            Label(new Rect(R.xMax - 230f, R.y + 4f, 218f, 20f), right, G3, 16, true, TextAnchor.UpperRight);
            Fill(new Rect(R.x + 8f, R.y + 25f, R.width - 16f, 2f), G2);
            Fill(new Rect(R.x + 8f, R.y + 29f, R.width - 16f, 1f), G1);
            return R.y + 36f;
        }

        /// <summary>Neutral 'pict-glass' panel: true colours (paint previews, colour wheels, sticker designs) are never tinted green.</summary>
        public static readonly Color GLASS = new Color(0.075f, 0.08f, 0.085f, 1f);

        /// <summary>A green framed button on the screen.</summary>
        public static bool Button(Rect r, string label, bool active = false)
        {
            bool over = Mouse.IsOver(r);
            Fill(r, active ? PANEL_SEL : over ? C(10, 36, 14) : PANEL);
            Border(r, active || over ? G4 : G2, 1f);
            Label(r, label.ToUpperInvariant(), active || over ? G4 : G3, 14, true, TextAnchor.MiddleCenter);
            return Widgets.ButtonInvisible(r);
        }

        /// <summary>[X] toggle.</summary>
        public static void Check(Rect r, string label, ref bool v)
        {
            Rect sq = new Rect(r.x + 2f, r.center.y - 9f, 18f, 18f);
            if (v) Fill(sq, G3); else Border(sq, G3, 1f);
            Label(new Rect(r.x + 26f, r.y, r.width - 26f, r.height), label, v ? G4 : G2, 13, false, TextAnchor.MiddleLeft, false, true);
            if (Mouse.IsOver(r)) Border(r, G1, 1f);
            if (Widgets.ButtonInvisible(r)) v = !v;
        }

        /// <summary>Plain screen text that follows Text.Anchor (drop-in for Widgets.Label in the old layouts).</summary>
        public static void Lbl(Rect r, string s) { Label(r, s, G3, Text.Font == GameFont.Tiny ? 11 : 13, false, Text.Anchor, true); }

        /// <summary>Segmented green slider (drag or click). label = the value text shown at the right.</summary>
        public static float Slider(Rect r, float v, float min, float max, string label)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            float lw = Mathf.Min(70f, r.width * 0.3f);
            Rect bar = new Rect(r.x, r.y, r.width - lw - 6f, r.height);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && bar.ExpandedBy(0f, 4f).Contains(e.mousePosition)) { GUIUtility.hotControl = id; v = Mathf.Lerp(min, max, Mathf.InverseLerp(bar.x, bar.xMax, e.mousePosition.x)); GUI.changed = true; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) { v = Mathf.Lerp(min, max, Mathf.InverseLerp(bar.x, bar.xMax, e.mousePosition.x)); GUI.changed = true; e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
            int n = Mathf.Max(6, Mathf.FloorToInt(bar.width / 9f));
            float sw = bar.width / n, f = Mathf.InverseLerp(min, max, v);
            for (int i = 0; i < n; i++)
                Fill(new Rect(bar.x + i * sw, bar.y + 2f, sw - 2f, bar.height - 4f), (i + 0.5f) / n <= f ? G3 : C(12, 32, 14));
            Label(new Rect(bar.xMax + 6f, r.y - 2f, lw, r.height + 4f), label, G4, 12, false, TextAnchor.MiddleLeft, false, true);
            return v;
        }

        // ------------------------------------------------------------------ weapon data for the data-slate
        public static VehicleTurretDef TurretDef(KnightClassDef cls, string key)
        {
            var props = cls?.vehicle?.GetCompProperties<CompProperties_VehicleTurrets>();
            return props?.turrets?.FirstOrDefault(t => t.key == key)?.def;
        }

        /// <summary>The special rules of a weapon, from its shot's effect extensions (the same fields the game uses).</summary>
        public static List<string> Rites(VehicleTurretDef td)
        {
            var l = new List<string>();
            if (td == null) return l;
            if (td.defName != null && td.defName.Contains("iongauntlet")) { l.Add("ION SHIELD: front arc absorbs ranged fire, halves melee"); return l; }
            ThingDef pd = td.projectile;
            DefModExtension_ShotFx fx = pd?.GetModExtension<DefModExtension_ShotFx>();
            DefModExtension_MeleeFx mf = pd?.GetModExtension<DefModExtension_MeleeFx>();
            if (fx != null)
            {
                if (fx.supercharge > 1f) l.Add("SUPERCHARGE mode: x" + fx.supercharge.ToString("0.#") + " dmg - HAZARDOUS, 1 volley in 6 burns the Knight");
                if (fx.pierce > 0f) l.Add("LAS BEAM: burns through cover to the target");
                if (fx.vaporise > 0f) l.Add("VAPORISE: infantry struck are evaporated");
                if (fx.antiArmour > 1f) l.Add("ANTI-ARMOUR: x" + fx.antiArmour.ToString("0.#") + " vs war machines, buildings, monsters");
                if (fx.ignoreCover) l.Add("TORRENT: never misses, cover does not stop it");
                if (fx.arcRadius > 0f) l.Add("THUNDERSHOCK: arcs to foes within " + fx.arcRadius.ToString("0") + " cells");
                if (fx.shieldMult > 1f) l.Add("SHIELDBREAKER: shields take x" + fx.shieldMult.ToString("0"));
                if (fx.meltaRange > 0f) l.Add("MELTA: x" + fx.meltaMult.ToString("0.#") + " within " + fx.meltaRange.ToString("0") + " cells");
                if (fx.massReactive > 0f) l.Add("MASS-REACTIVE: shells burst inside the target");
                if (fx.suppress > 0f) l.Add("SUPPRESSIVE: pins troops near each hit");
                if (fx.panic > 0f) l.Add("PANIC: " + (fx.panic * 100f).ToString("0") + "% caught in it flee");
                if (fx.stunChance > 0f) l.Add("STUN: " + (fx.stunChance * 100f).ToString("0") + "% chance");
                if (fx.braceMult > 1f) l.Add("BRACED: x" + fx.braceMult.ToString("0.#") + " fired standing still");
                if (fx.gravField > 0f) l.Add("GRAVITON FIELD: crushes, worse on heavy armour");
                if (fx.growRange > 0f || fx.conversionBands) l.Add("CONVERSION: stronger the farther the target");
                if (fx.stream) l.Add("FLAME STREAM: sets the ground alight");
                if (fx.braceRange > 1f) l.Add("ORDNANCE: x" + fx.braceRange.ToString("0.#") + " range standing still");
                if (fx.deflagrate > 0f) l.Add("DEFLAGRATE: victims combust, burning those beside them");
                if (fx.overcharge > 0f) l.Add("OVERCHARGE mode: longer burst, overheats, weak past half range");
                if (fx.mustBrace) l.Add("MOTIONLESS: fired on the move the beam fizzles");
                if (fx.irradiate > 0f) l.Add("IRRADIATE: radiation sickness keeps building (not mechs)");
                if (fx.shockVehicles > 0f) l.Add("SHOCK: " + (fx.shockVehicles * 100f).ToString("0") + "% to stun a struck war machine");
                if (fx.breachChance > 0f) l.Add("BREACHING: 1 round in " + Mathf.RoundToInt(1f / fx.breachChance) + " punches through armour");
                if (fx.braceExtra > 0f) l.Add("HEAVY: extra rounds fired standing still");
                if (fx.antiAir > 1f) l.Add("ANTI-AIR: x" + fx.antiAir.ToString("0.#") + " vs flying targets");
                if (fx.interceptPods) l.Add("INTERCEPT: strikes enemy drop pods and shuttles coming down");
            }
            if (mf != null)
            {
                if (mf.arcStrike) l.Add("SWEEP: strikes everything in the arc");
                if (mf.knockback > 0f) l.Add("IMPACT: throws struck foes back");
                if (mf.stunTicks > 0) l.Add("SHOCK: stuns what it strikes");
                if (mf.exoshock > 0f) l.Add("EXOSHOCK: " + (mf.exoshock * 100f).ToString("0") + "% secondary blast inside");
                if (mf.ignite > 0f) l.Add("AFLAME: " + (mf.ignite * 100f).ToString("0") + "% set alight");
                if (mf.antiArmour > 1f) l.Add("ARMOUR-BREAKER: x" + mf.antiArmour.ToString("0.#") + " vs war machines, buildings");
                if (mf.reapingBlow > 1f) l.Add("REAPING BLOW: x" + mf.reapingBlow.ToString("0.#") + " when 3+ foes are caught");
                if (mf.panic > 0f) l.Add("TERROR: " + (mf.panic * 100f).ToString("0") + "% of foes nearby flee");
            }
            if (l.Count == 0) l.Add("STANDARD PATTERN");
            return l;
        }
    }

    public partial class Window_ConfigureKnight
    {
        static bool showPainted;             // pict-feed: green schematic (default) or the Knight in its colours
        int selSlot;                         // data-slate: mount index, -2 = head

        public override void PreOpen()
        {
            base.PreOpen();
            if (!CogUI.Ready) return;
            doWindowBackground = false;
            drawShadow = false;
            doCloseX = false;
            draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (!CogUI.Ready) { DrawClassic(inRect); return; }
            if (devFree && (refitKnight == null || !refitKnight.Spawned)) { Close(); return; }
            if (classes.Count == 0)
            {
                GUI.DrawTexture(new Rect(0f, 0f, CogUI.TW, CogUI.TH), CogUI.crtBg);
                GUI.DrawTexture(new Rect(0f, 0f, CogUI.TW, CogUI.TH), CogUI.bezel);
                CogUI.Label(new Rect(CogUI.Screen.x + 20f, CogUI.Screen.y + 40f, 800f, 30f), "NO KNIGHT PATTERN THIS HANGAR CAN BUILD IS SANCTIFIED (RESEARCHED) YET.", CogUI.G3, 18, true);
                if (CogUI.Key(new Rect(CogUI.FX + 1055f, CogUI.TH - 78f, 170f, 56f), "CLOSE", false)) Close();
                return;
            }
            if (pendingClass >= 0) { int pcl = pendingClass; pendingClass = -1; if (pcl < classes.Count) { SetClass(pcl, null); selSlot = 0; } }
            KnightClassDef cls = classes[classIdx];
            if (selSlot >= cls.mounts.Count || (selSlot == -2 && cls.heads.Count == 0)) selSlot = 0;
            Rect S = CogUI.Screen;
            GUI.DrawTexture(S, CogUI.crtBg);

            // what the order / refit needs (used by several panels)
            var order = BuildOrder();
            var need = new Dictionary<ThingDef, int>();
            List<string> refitKeys = null;
            if (devFree) { }
            else if (refitKnight != null)
            {
                refitKeys = order.turretKeys;
                foreach (ThingDef d in Building_KnightHangar.RefitNeeds(refitKnight, cls, refitKeys, headIdx, lsOn, vsOn, null)) need[d] = need.ContainsKey(d) ? need[d] + 1 : 1;
            }
            else foreach (ThingDef d in order.RequiredItems) need[d] = need.ContainsKey(d) ? need[d] + 1 : 1;
            int total = 0, have = 0;
            foreach (var kv in need) { total += kv.Value; have += Mathf.Min(kv.Value, Stock(kv.Key)); }

            // ---- header line
            string mode = devFree ? "DEV REFIT (FREE)" : refitKnight != null ? "RITE OF REFIT" : "RITE OF ASSEMBLY";
            int id = hangar != null ? hangar.thingIDNumber : refitKnight.thingIDNumber;
            CogUI.Label(new Rect(S.x + 12f, S.y + 4f, 900f, 20f), "HOLO.DISP. ++ IN.//" + (id * 7919 % 100000000).ToString("00000000") + "K-" + (classIdx + 1) + " [PRIMARY]   //   MANUFACTORUM AUSPEX :: " + mode, CogUI.G3, 16, true);
            CogUI.Label(new Rect(S.xMax - 200f, S.y + 4f, 188f, 20f), "EMIT.PNL.V17-" + (91462 + id % 997), CogUI.G3, 16, true, TextAnchor.UpperRight);
            CogUI.Fill(new Rect(S.x + 8f, S.y + 25f, S.width - 16f, 2f), CogUI.G2);
            CogUI.Fill(new Rect(S.x + 8f, S.y + 29f, S.width - 16f, 1f), CogUI.G1);

            DrawLeft(S, cls, need);
            DrawCentre(S, cls, total, have, need.Count);
            DrawRight(S, cls, need);

            // CRT finish + the machine around it
            CogUI.CrtWave(S);
            GUI.DrawTexture(S, CogUI.crtOver);
            GUI.DrawTexture(new Rect(0f, 0f, CogUI.TW, CogUI.TH), CogUI.bezel);

            // ---- the physical keys
            Rect brass = new Rect(CogUI.FX + 760f, CogUI.TH - 78f, 280f, 56f), steel = new Rect(CogUI.FX + 1055f, CogUI.TH - 78f, 170f, 56f);
            if (devFree)
            {
                var devOrder = order;
                if (Building_KnightHangar.RefitNeeds(refitKnight, cls, devOrder.turretKeys, headIdx, lsOn, vsOn, new List<ThingDef>()).Count > 0
                    || (refitKnight.GetComp<CompKnightHead>()?.lifeSupport ?? false) != lsOn || (refitKnight.GetComp<CompKnightHead>()?.voidShield ?? false) != vsOn
                    || !Building_KnightHangar.CurrentKeys(refitKnight, cls).SequenceEqual(devOrder.turretKeys))
                    Building_KnightHangar.DevRefit(refitKnight, cls, devOrder.turretKeys, headIdx, lsOn, vsOn);
            }
            else if (refitKnight != null)
            {
                if (CogUI.Key(brass, "REFIT NOW", true) && hangar.Refit(refitKnight, cls, refitKeys, headIdx, lsOn, vsOn)) Close();
            }
            else if (CogUI.Key(brass, hangar.order != null ? "REPLACE ORDER" : "SANCTIFY ORDER", true))
            {
                hangar.order = order;
                Messages.Message(cls.LabelCap + " assembly ordered. Builders will haul in the parts and assemble it.", hangar, MessageTypeDefOf.PositiveEvent, false);
                Close();
            }
            if (CogUI.Key(steel, "CLOSE", false)) Close();
        }

        // ------------------------------------------------------------------ left column
        void DrawLeft(Rect S, KnightClassDef cls, Dictionary<ThingDef, int> need)
        {
            float x = S.x + 10f, w = 230f, y = S.y + 36f;
            float h = 26f + classes.Count * 33f;
            Rect box = new Rect(x, y, w, h);
            CogUI.Panel(box, "CLASS SELECT");
            for (int i = 0; i < classes.Count; i++)
            {
                Rect r = new Rect(x + 8f, y + 24f + i * 33f, w - 16f, 27f);
                bool sel = i == classIdx, over = refitKnight == null && Mouse.IsOver(r);
                if (sel) CogUI.Fill(r, CogUI.C2(20, 70, 30));
                CogUI.Border(r, sel ? CogUI.G3 : over ? CogUI.G2 : CogUI.G1, 1f);
                Rect sq = new Rect(r.x + 5f, r.y + 7f, 13f, 13f);
                if (sel) CogUI.Fill(sq, CogUI.G4); else CogUI.Border(sq, CogUI.G2, 1f);
                CogUI.Label(new Rect(r.x + 26f, r.y, 140f, r.height), classes[i].label.ToUpperInvariant(), sel ? CogUI.G4 : CogUI.G2, 15, true, TextAnchor.MiddleLeft);
                IntVec2 sz = classes[i].vehicle != null ? classes[i].vehicle.size : new IntVec2(0, 0);
                CogUI.Label(new Rect(r.xMax - 60f, r.y, 54f, r.height), sz.x + "x" + sz.z, CogUI.G2, 11, false, TextAnchor.MiddleRight);
                if (refitKnight == null && !sel && Widgets.ButtonInvisible(r)) pendingClass = i;
            }
            y += h + 10f;

            // modules
            var mods = new List<KeyValuePair<string, bool>>();
            float mh = 26f + Mathf.Max(1, (cls.lifeSupport != null ? 1 : 0) + (cls.voidShield != null ? 1 : 0)) * 30f + 4f;
            CogUI.Panel(new Rect(x, y, w, mh), "MODULES");
            float my = y + 26f;
            if (cls.lifeSupport == null && cls.voidShield == null) CogUI.Label(new Rect(x + 10f, my, w - 20f, 22f), "NONE FOR THIS PATTERN", CogUI.G2, 12);
            if (cls.lifeSupport != null)
            {
                if (Toggle(new Rect(x + 8f, my, w - 16f, 24f), "LIFE SUPPORT", lsOn, cls.lifeSupport,
                    "Life-support module (optional): greatly slows the pilot's neural strain, ageing and damage shock. " + Stock(cls.lifeSupport) + " in stock.")) lsOn = !lsOn;
                my += 30f;
            }
            if (cls.voidShield != null)
            {
                if (Toggle(new Rect(x + 8f, my, w - 16f, 24f), "VOID SHIELD", vsOn, cls.voidShield,
                    "Void shield generator (optional carapace module): absorbs about " + Mathf.RoundToInt(cls.voidShieldCapacity) + " damage, then recharges a few seconds after the last hit. " + Stock(cls.voidShield) + " in stock.")) vsOn = !vsOn;
                my += 30f;
            }
            y += mh + 10f;

            // reactor harmonics (decorative, alive)
            Rect wave = new Rect(x, y, w, 110f);
            CogUI.Panel(wave, "REACTOR HARMONICS");
            float t = Time.realtimeSinceStartup, mid = wave.y + 64f;
            CogUI.Line(new Vector2(wave.x + 6f, mid), new Vector2(wave.xMax - 6f, mid), CogUI.G1);
            Vector2 last = Vector2.zero;
            for (int i = 0; i <= 54; i++)
            {
                float px = wave.x + 6f + i * (w - 12f) / 54f;
                float v = Mathf.Sin(i * 0.42f + t * 2.1f) * Mathf.Sin(i * 0.09f + t * 0.7f) * 26f + Mathf.Sin(i * 1.3f + t * 5f) * 5f;
                Vector2 p = new Vector2(px, mid + v);
                if (i > 0) CogUI.Line(last, p, CogUI.G3);
                last = p;
            }
            y += 120f;

            // litany log
            Rect log = new Rect(x, y, w, S.yMax - 10f - y);
            CogUI.Panel(log, "LITANY LOG");
            var lines = new List<KeyValuePair<string, bool>>();
            lines.Add(new KeyValuePair<string, bool>(devFree ? "> dev rite: free + instant" : refitKnight != null ? "> rite of refit begun" : "> rite of assembly begun", false));
            foreach (var kv in need)
            {
                bool ok = Stock(kv.Key) >= kv.Value;
                lines.Add(new KeyValuePair<string, bool>((ok ? "> " : "> AWAITING: ") + kv.Key.label + (ok ? ": sanctified" : " " + Stock(kv.Key) + "/" + kv.Value), !ok));
            }
            lines.Add(new KeyValuePair<string, bool>("> 0x" + ((int)(t * 13f) % 255).ToString("X2") + " 0xA1 0x3C 0x7F", false));
            lines.Add(new KeyValuePair<string, bool>("> praise the omnissiah", false));
            int maxLines = Mathf.Max(1, (int)((log.height - 28f) / 15f));
            for (int i = 0; i < lines.Count && i < maxLines; i++)
                CogUI.Label(new Rect(log.x + 8f, log.y + 24f + i * 15f, w - 14f, 15f), lines[i].Key, lines[i].Value ? CogUI.RED : CogUI.G2, 11);
        }

        bool Toggle(Rect r, string label, bool on, ThingDef item, string tip)
        {
            Rect sq = new Rect(r.x + 2f, r.y + 2f, 20f, 20f);
            if (on) CogUI.Fill(sq, CogUI.G3); else CogUI.Border(sq, CogUI.G3, 1f);
            CogUI.Label(new Rect(r.x + 30f, r.y, r.width - 30f, r.height), label + (on ? "  [ON]" : "  [--]"), on ? CogUI.G4 : CogUI.G2, 13, false, TextAnchor.MiddleLeft, false, true);
            if (Mouse.IsOver(r)) CogUI.Border(r, CogUI.G2, 1f);
            TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        // ------------------------------------------------------------------ centre: the Knight + slot cards
        struct Slot { public int mount; public Vector2 point; public string label; public string name; public Texture icon; public string tip; }

        void DrawCentre(Rect S, KnightClassDef cls, int total, int have, int kinds)
        {
            float x0 = S.x + 250f, x1 = S.x + 930f;
            Rect pane = new Rect(x0, S.y + 36f, x1 - x0, S.height - 46f);
            CogUI.Panel(pane, "PICT-FEED // " + cls.label.ToUpperInvariant() + " // " + (showPainted ? "PICT" : "SCHEMATIC"));
            Rect tog = new Rect(pane.xMax - 120f, pane.y + 1f, 116f, 17f);
            CogUI.Fill(tog, Mouse.IsOver(tog) ? CogUI.G4 : CogUI.C2(150, 240, 150));
            CogUI.Label(tog, showPainted ? "[ SCHEMATIC ]" : "[ PAINTED ]", CogUI.INK, 12, true, TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(tog, "Show the Knight as a green schematic or in its paint scheme.");
            if (Widgets.ButtonInvisible(tog)) showPainted = !showPainted;

            float ks = 380f;
            Rect body = new Rect((x0 + x1) / 2f - ks / 2f, S.y + 150f, ks, ks);
            Vector2 c = body.center;
            if (CogUI.emblem != null) CogUI.Tex(new Rect(c.x - 205f, c.y - 205f, 410f, 410f), CogUI.emblem, new Color(0.04f, 0.18f, 0.05f, 1f));
            for (int k = 1; k <= 3; k++) CogUI.Ring(c, 70f * k, CogUI.G1);
            Texture2D schem = ContentFinder<Texture2D>.Get("UI/LIK_Hangar/Schematic/" + cls.defName, false);
            if (!showPainted && schem != null) GUI.DrawTexture(body, schem, ScaleMode.ScaleToFit);
            else if (KnightPaint.Paintable(cls.vehicle))
            {
                PaintChoice pc = refitKnight != null ? KnightPaint.Current(refitKnight) : paintChoice ?? KnightPaint.Defaults(cls.vehicle);
                KnightPaint.DrawClassPicture(body, cls, pc, cls.label);
            }
            else DrawPicture(body, BodyTex(cls, skinIdx == 0 ? null : cls.skins[skinIdx - 1]), cls.label);
            bool flatPic = HangarPicture(cls) != null;
            float cell = ks / Mathf.Max(1f, cls.drawCells);

            // the slots: where each mount sits on the picture
            var left = new List<Slot>(); var right = new List<Slot>(); var top = new List<Slot>();
            for (int i = 0; i < cls.mounts.Count; i++)
            {
                KnightMount m = cls.mounts[i];
                WeaponOption cur = choice[i] < 0 ? null : m.options[choice[i]];
                Vector2 p = flatPic && m.uiPos.x >= 0f ? new Vector2(body.x + m.uiPos.x * body.width, body.y + m.uiPos.y * body.height)
                                                       : new Vector2(c.x + m.pos.x * cell, c.y - m.pos.y * cell);
                var s = new Slot { mount = i, point = p, label = m.label.ToUpperInvariant() + (m.optional ? " (OPT.)" : ""),
                                   name = cur == null ? "- EMPTY -" : cur.weapon.LabelCap.ToString(), icon = cur?.weapon.uiIcon,
                                   tip = cur == null ? m.label + ": empty (optional). Click to fit a weapon."
                                       : m.label + ": " + cur.weapon.LabelCap + " (" + Stock(cur.weapon) + " in stock). Click to choose a weapon" + (m.optional ? " or leave it empty." : ".") };
                if (p.x < c.x - ks * 0.1f) left.Add(s); else if (p.x > c.x + ks * 0.1f) right.Add(s); else top.Add(s);
            }
            if (cls.heads.Count > 0)
            {
                KnightHead hd = cls.heads[Mathf.Clamp(headIdx, 0, cls.heads.Count - 1)];
                top.Add(new Slot { mount = -2, point = new Vector2(c.x + cls.headPos.x * cell, c.y - cls.headPos.y * cell), label = "HEAD", name = hd.label.CapitalizeFirst(),
                                   icon = hd.item.uiIcon, tip = "Head: " + hd.label.CapitalizeFirst() + " (" + Stock(hd.item) + " in stock). Click to choose a head." });
            }
            left = left.OrderBy(s => s.point.y).ToList();
            right = right.OrderBy(s => s.point.y).ToList();
            // level mirrored rows (user 2026-11-16): row i holds the i-th left and the i-th right slot at the same height
            const float CW = 160f;
            int rows = Mathf.Max(left.Count, right.Count);
            float rowTop = S.y + 170f, rowBottom = pane.yMax - 76f;
            float CH = rows == 0 ? 108f : Mathf.Clamp((rowBottom - rowTop - (rows - 1) * 10f) / rows, 62f, 108f);
            for (int i = 0; i < rows; i++)
            {
                float y = rowTop + i * (CH + 10f);
                if (i < left.Count) Card(new Rect(x0 + 10f, y, CW, CH), left[i], 1);
                if (i < right.Count) Card(new Rect(x1 - 10f - CW, y, CW, CH), right[i], -1);
            }
            float tw = top.Count * CW + (top.Count - 1) * 10f;
            for (int i = 0; i < top.Count; i++) Card(new Rect((x0 + x1) / 2f - tw / 2f + i * (CW + 10f), S.y + 48f, CW, 98f), top[i], 0);

            // status strip
            Rect eq = new Rect(x0 + 8f, pane.yMax - 68f, pane.width - 16f, 60f);
            CogUI.Border(eq, CogUI.G3);
            float t = Time.realtimeSinceStartup, ey = eq.y + 36f;
            int bars = 40;
            float bw = (eq.width - 30f) / bars;
            for (int i = 0; i < bars; i++)
            {
                float hgt = 4f + 18f * Mathf.Abs(Mathf.Sin(i * 0.7f + t * 1.7f) * Mathf.Cos(i * 0.23f + t * 0.6f));
                CogUI.Fill(new Rect(eq.x + 15f + i * bw, ey - hgt, bw - 3f, hgt - 2f), CogUI.G2);
                CogUI.Fill(new Rect(eq.x + 15f + i * bw, ey + 2f, bw - 3f, hgt / 3f), CogUI.C2(20, 90, 26));
            }
            CogUI.Fill(new Rect(eq.x + 15f, ey, eq.width - 30f, 2f), CogUI.G3);
            string status = devFree ? "DEV REFIT :: FREE & INSTANT"
                          : refitKnight != null ? (total == 0 ? "NO CHANGES TO SANCTIFY" : "REFIT :: " + have + "/" + total + " PIECES IN STOCK")
                          : have >= total ? "ALL PARTS PRESENT  " + have + "/" + total : "AWAITING PARTS  " + have + "/" + total;
            float sw = CogUI.Width(status, 26, true) + 24f;
            CogUI.Fill(new Rect(eq.center.x - sw / 2f, eq.y + 8f, sw, 32f), new Color(0.01f, 0.05f, 0.015f, 0.85f));
            CogUI.Label(new Rect(eq.x, eq.y + 6f, eq.width, 34f), status, have >= total || devFree ? CogUI.G4 : CogUI.RED, 26, true, TextAnchor.MiddleCenter);
        }

        /// <summary>One slot card; side 1 = left column (line leaves its right edge), -1 = right column, 0 = top centre (line leaves its bottom).</summary>
        void Card(Rect b, Slot s, int side)
        {
            bool sel = s.mount == selSlot, over = Mouse.IsOver(b);
            Vector2 from = side == 0 ? new Vector2(b.center.x, b.yMax) : new Vector2(side > 0 ? b.xMax : b.x, b.center.y);
            Color lc = sel ? CogUI.G3 : CogUI.G2;
            if (side == 0) CogUI.Line(from, s.point, lc, sel ? 2f : 1f);
            else
            {
                Vector2 elbow = from + new Vector2(16f * side, 0f);
                CogUI.Line(from, elbow, lc, sel ? 2f : 1f);
                CogUI.Line(elbow, s.point, lc, sel ? 2f : 1f);
            }
            CogUI.Tex(new Rect(s.point.x - 11f, s.point.y - 11f, 22f, 22f), CogUI.reticle, CogUI.G4);
            CogUI.Fill(b, sel ? CogUI.PANEL_SEL : over ? CogUI.C2(8, 30, 12) : CogUI.PANEL);
            CogUI.Border(b, sel || over ? CogUI.G4 : CogUI.G2);
            CogUI.Fill(new Rect(b.x, b.y, b.width, 18f), sel ? CogUI.G3 : CogUI.BAR_DIM);
            CogUI.Label(new Rect(b.x + 6f, b.y, b.width - 8f, 18f), s.label, sel ? CogUI.INK : CogUI.G4, 14, true, TextAnchor.MiddleLeft);
            float nameH = 30f, iconH = Mathf.Max(20f, b.height - 18f - nameH - 6f);
            CogUI.IconStrip(new Rect(b.x + 8f, b.y + 21f, b.width - 16f, iconH), s.icon, new Color(0.55f, 1f, 0.6f));
            CogUI.Label(new Rect(b.x + 4f, b.yMax - nameH - 2f, b.width - 8f, nameH), s.name, sel ? CogUI.G4 : CogUI.G3, 12, false, TextAnchor.MiddleCenter, true, true);
            TooltipHandler.TipRegion(b, s.tip);
            if (Widgets.ButtonInvisible(b))
            {
                selSlot = s.mount;
                if (s.mount == -2) OpenHeadMenu(); else OpenWeaponMenu(s.mount);
            }
        }

        // ------------------------------------------------------------------ right column: data-slate + requisition
        void DrawRight(Rect S, KnightClassDef cls, Dictionary<ThingDef, int> need)
        {
            float x = S.x + 940f, w = S.xMax - 10f - x;
            Rect slate = new Rect(x, S.y + 36f, w, 384f);
            string slotName = selSlot == -2 ? "HEAD" : selSlot >= 0 && selSlot < cls.mounts.Count ? cls.mounts[selSlot].label.ToUpperInvariant() : "-";
            CogUI.Panel(slate, "DATA-SLATE :: " + slotName);
            float y = slate.y + 26f;
            ThingDef item = null; VehicleTurretDef td = null;
            if (selSlot == -2 && cls.heads.Count > 0) item = cls.heads[Mathf.Clamp(headIdx, 0, cls.heads.Count - 1)].item;
            else if (selSlot >= 0 && selSlot < cls.mounts.Count && choice[selSlot] >= 0)
            {
                WeaponOption o = cls.mounts[selSlot].options[choice[selSlot]];
                item = o.weapon;
                td = CogUI.TurretDef(cls, o.turretKey);
            }
            Rect iconBox = new Rect(x + 8f, y, w - 16f, 74f);
            CogUI.Border(iconBox, CogUI.G2, 1f);
            if (item != null) CogUI.IconStrip(iconBox.ContractedBy(4f), item.uiIcon, new Color(0.55f, 1f, 0.6f));
            y += 82f;
            CogUI.Label(new Rect(x + 10f, y, w - 20f, 46f), item != null ? item.LabelCap.ToString().ToUpperInvariant() : "NO WEAPON FITTED", CogUI.G4, 19, true, TextAnchor.UpperLeft, true);
            y += 46f;
            if (item != null) CogUI.Label(new Rect(x + 10f, y, w - 20f, 16f), "in stock: " + Stock(item) + (devFree ? "   (dev: free)" : ""), CogUI.G2, 11);
            y += 20f;
            if (td != null && td.projectile?.projectile != null)
            {
                ProjectileProperties pp = td.projectile.projectile;
                int dmg = pp.GetDamageAmount((Thing)null);
                float ap = pp.GetArmorPenetration((Thing)null);
                FireMode fm = td.fireModes != null && td.fireModes.Count > 0 ? td.fireModes[0] : null;
                int burst = fm != null ? Mathf.Max(1, fm.shotsPerBurst.max) : 1;
                float cyc = fm != null ? Mathf.Max(1f, fm.ticksBetweenBursts.max + fm.ticksBetweenShots * (burst - 1)) : 60f;
                Bar(new Rect(x + 10f, y, 130f, 26f), "DAMAGE", dmg * burst / 400f, dmg + (burst > 1 ? " x" + burst : ""));
                Bar(new Rect(x + 10f, y + 28f, 130f, 26f), "ARMOUR PEN", ap / 4f, ap.ToString("0.#"));
                Bar(new Rect(x + 10f, y + 56f, 130f, 26f), "RATE", burst * 600f / cyc / 30f, (burst * 600f / cyc).ToString("0.#") + "/10s");
                // range ring
                Vector2 rc = new Vector2(slate.xMax - 40f, y + 40f);
                CogUI.Ring(rc, 30f, CogUI.G1); CogUI.Ring(rc, 15f, CogUI.G1);
                CogUI.Ring(rc, 30f, CogUI.G4, 4f, 0f, 360f * Mathf.Clamp01(td.maxRange / 70f), 40);
                CogUI.Label(new Rect(rc.x - 30f, rc.y - 10f, 60f, 20f), td.maxRange.ToString("0"), CogUI.G4, 16, true, TextAnchor.MiddleCenter);
                CogUI.Label(new Rect(rc.x - 30f, rc.y + 31f, 60f, 14f), "RANGE", CogUI.G2, 9, false, TextAnchor.MiddleCenter);
                y += 88f;
            }
            CogUI.Label(new Rect(x + 10f, y, w - 20f, 20f), "RITES OF WAR", CogUI.G4, 16, true);
            y += 21f;
            List<string> rites = item == null ? new List<string> { selSlot == -2 ? "" : "Click a slot to fit a weapon." } : td != null ? CogUI.Rites(td) : new List<string> { "Head of the Knight: its pilot's view and bearing." };
            foreach (string r in rites)
            {
                if (y > slate.yMax - 18f) break;
                float h = Mathf.Min(slate.yMax - 4f - y, Text.CalcHeight(r, w - 30f) + 2f);
                CogUI.Label(new Rect(x + 10f, y, w - 20f, h), "> " + r, CogUI.G3, 11, false, TextAnchor.UpperLeft, true);
                y += h + 2f;
            }

            // requisition (or dev note)
            Rect req = new Rect(x, slate.yMax + 10f, w, S.yMax - 10f - slate.yMax - 10f);
            CogUI.Panel(req, devFree ? "DEV MODE" : "REQUISITION");
            float ry = req.y + 24f;
            if (devFree)
                CogUI.Label(new Rect(x + 10f, ry, w - 20f, 120f), "Click a slot and pick anything: it is fitted at once, fully loaded, at no cost. Lost (STC) weapons are offered too. Nothing is taken from or returned to stock.", CogUI.G3, 11, false, TextAnchor.UpperLeft, true);
            else
            {
                if (need.Count == 0) CogUI.Label(new Rect(x + 10f, ry, w - 20f, 30f), refitKnight != null ? "Pick a different weapon or head to change something." : "-", CogUI.G2, 11, false, TextAnchor.UpperLeft, true);
                int maxRows = Mathf.Max(1, (int)((req.height - 60f) / 16f)), n = 0;
                foreach (var kv in need)
                {
                    if (n++ >= maxRows) { CogUI.Label(new Rect(x + 10f, ry, w - 20f, 16f), "...", CogUI.G2, 11); break; }
                    int st = Stock(kv.Key);
                    bool ok = st >= kv.Value;
                    CogUI.Label(new Rect(x + 10f, ry, w - 60f, 16f), (ok ? "[+] " : "[X] ") + kv.Value + " " + kv.Key.LabelCap, ok ? CogUI.G3 : CogUI.RED, 11);
                    CogUI.Label(new Rect(req.xMax - 52f, ry, 44f, 16f), st + "/" + kv.Value, ok ? CogUI.G4 : CogUI.RED, 11, false, TextAnchor.UpperRight, false, true);
                    ry += 16f;
                }
            }
            // heraldry line
            float hy = req.yMax - 28f;
            CogUI.Label(new Rect(x + 10f, hy, 80f, 20f), "HERALDRY", CogUI.G3, 12, false, TextAnchor.MiddleLeft, false, true);
            if (KnightPaint.Paintable(cls.vehicle))
            {
                PaintChoice pc = refitKnight != null ? KnightPaint.Current(refitKnight) : paintChoice ?? KnightPaint.Defaults(cls.vehicle);
                KnightPaint.DrawSwatch(new Rect(x + 86f, hy + 2f, 48f, 16f), pc.one, pc.two);
                Rect rp = new Rect(x + 140f, hy, w - 150f, 20f);
                CogUI.Label(rp, "> repaint", Mouse.IsOver(rp) ? CogUI.G4 : CogUI.G2, 12, false, TextAnchor.MiddleLeft);
                TooltipHandler.TipRegion(rp, KnightPaint.NameOf(cls.vehicle, pc) + " - click to choose colours and stickers.");
                VehiclePawn rk = refitKnight;
                if (Widgets.ButtonInvisible(rp))
                    Find.WindowStack.Add(new Window_PaintKnight(cls, rk, pc, ch => { if (rk != null) KnightPaint.Apply(rk, ch); else paintChoice = ch; }));
            }
            else if (refitKnight == null && cls.skins.Count > 0)
            {
                Rect rp = new Rect(x + 86f, hy, w - 96f, 20f);
                string nm = skinIdx == 0 ? "Standard" : cls.skins[skinIdx - 1].LabelCap.ToString();
                CogUI.Label(rp, "< " + nm + " >", Mouse.IsOver(rp) ? CogUI.G4 : CogUI.G2, 12, false, TextAnchor.MiddleLeft);
                TooltipHandler.TipRegion(rp, "Livery - click to cycle.");
                if (Widgets.ButtonInvisible(rp)) skinIdx = (skinIdx + 1) % (cls.skins.Count + 1);
            }
            else CogUI.Label(new Rect(x + 86f, hy, w - 96f, 20f), "standard", CogUI.G2, 12, false, TextAnchor.MiddleLeft);
        }

        static void Bar(Rect r, string label, float v, string txt)
        {
            CogUI.Label(new Rect(r.x, r.y, r.width, 13f), label, CogUI.G2, 10);
            int on = Mathf.RoundToInt(Mathf.Clamp01(v) * 10f);
            for (int i = 0; i < 10; i++) CogUI.Fill(new Rect(r.x + i * 11f, r.y + 14f, 8f, 9f), i < on ? CogUI.G3 : CogUI.C2(12, 32, 14));
            CogUI.Label(new Rect(r.x + 114f, r.y + 10f, 70f, 16f), txt, CogUI.G4, 11, false, TextAnchor.MiddleLeft, false, true);
        }
    }
}
