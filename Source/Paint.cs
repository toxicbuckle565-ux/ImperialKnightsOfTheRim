using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using Verse;

namespace LIK
{
    /// <summary>A paint job: armour (one), trim (two), colour three (kept for VF; unused by the masks) and the stickers.</summary>
    public class PaintChoice
    {
        public Color one = Color.white, two = Color.white, three = Color.white;
        public List<KnightSticker> stickers = new List<KnightSticker>();
        public PaintChoice Copy()
        {
            var c = (PaintChoice)MemberwiseClone();
            c.stickers = stickers != null ? stickers.Select(s => s.Copy()).ToList() : new List<KnightSticker>();
            return c;
        }
    }

    /// <summary>
    /// HANGAR PAINT (user 2026-11-12: "can we rather add a built in color changer into the hangar?"). Knights whose pictures have colour masks
    /// (Tools/paint_masks.py: armour panels = mask red = colour one, trim = mask green = colour two) are drawn with Vehicle Framework's
    /// CutoutComplexRGB shader, so the armour and trim take the Knight's own colours (VF saves them with the Knight).
    /// User 2026-11-12: colour wheels instead of VF's colour picker, no VF camo patterns (camo is a sticker), stickers instead of the
    /// whole-part checks / hazard patterns (Stickers.cs), a turnable preview.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class KnightPaint
    {
        public struct Scheme
        {
            public string label; public Color one, two;
            public Scheme(string l, int r1, int g1, int b1, int r2, int g2, int b2) { label = l; one = C(r1, g1, b1); two = C(r2, g2, b2); }
        }

        static Color C(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

        // armour (colour one) / trim (colour two) of each loyal house. Re-checked house by house on the Warhammer 40k fandom wiki's
        // "Colours" / "House Colours" entries, then checked against each house's official colour-scheme art (trim = plate rims) (user 2026-11-13). Terryn = the exact colours of the
        // user's own Acastus painting. Checks, stripes, halved schemes and symbols are stickers. Silver trim = Mechanicus houses' bare metal edges.
        public static readonly List<Scheme> Houses = new List<Scheme>
        {
            new Scheme("House Cadmus", 46, 98, 56, 196, 198, 204),          // forest / bottle green, silver (yellow details)
            new Scheme("House Coldshroud", 222, 172, 46, 196, 198, 204),   // golden-yellow, silver rims (mottled grey panels = stickers)
            new Scheme("House Degallio", 226, 220, 198, 196, 198, 204),   // off-white, steel rims (yellow / black details)
            new Scheme("House Donar", 40, 82, 52, 206, 164, 74),            // dark green, burnished gold (black panels, red symbols)
            new Scheme("House Griffith", 220, 212, 184, 196, 198, 204),     // bone, silver steel rims (from the wiki colour-scheme art; cyan blue / red / dragon heraldry = stickers)
            new Scheme("House Hawkshroud", 232, 190, 40, 196, 198, 204),   // yellow, steel rims (black / white checks)
            new Scheme("House Hermetika", 176, 34, 32, 58, 58, 62),         // crimson red, black
            new Scheme("House Krast", 186, 36, 34, 196, 198, 204),          // Mechanicus red, silver
            new Scheme("House Mortan", 48, 48, 52, 206, 164, 74),   // black, gold rims (yellow / red details)
            new Scheme("House Orhlacc", 92, 58, 124, 168, 140, 92),         // purple, muted brass edging (sable details)
            new Scheme("House Raven", 158, 28, 30, 196, 198, 204),          // deep red, silver / iron edges
            new Scheme("House Taranis", 194, 38, 34, 196, 198, 204),        // red, silver (white / black details)
            new Scheme("House Terryn", 84, 126, 188, 216, 191, 134),        // azure blue, gold (red / white details)
            new Scheme("House Vornherr", 196, 30, 28, 206, 164, 74),   // scarlet, gold rims (sun-fire yellow panels, black details)
            new Scheme("House Vulker", 166, 32, 32, 196, 198, 204),         // deep red, bare adamantium silver
            new Scheme("House Vyronii", 44, 100, 62, 196, 198, 204),   // emerald green, steel rims (white / red details)
            new Scheme("House Zavora", 194, 36, 34, 196, 198, 204),   // halved red / white (white half: sticker), steel rims, black hazard
        };

        static readonly int ColorOneID = Shader.PropertyToID("_ColorOne");
        static readonly int ColorTwoID = Shader.PropertyToID("_ColorTwo");
        static readonly int ColorThreeID = Shader.PropertyToID("_ColorThree");
        static readonly int MaskTexID = Shader.PropertyToID("_MaskTex");
        static readonly Dictionary<string, Material> uiMats = new Dictionary<string, Material>();

        public static bool Paintable(VehicleDef d)
        {
            return d != null && d.graphicData != null && d.graphicData.shaderType != null && d.graphicData.shaderType == VehicleShaderTypeDefOf.CutoutComplexRGB;
        }

        public static string ClassKey(KnightClassDef cls) { return cls.defName.Substring("LIK_Class_".Length); }

        /// <summary>The shoulder-plate overlay (CompKnightHead.Apply).</summary>
        public static void ApplyOverlays(VehiclePawn v, KnightClassDef cls, GraphicOverlayRenderer renderer)
        {
            if (cls.pauldron == null) return;
            renderer.RemoveOverlays("LIK_Pauldron");
            renderer.AddOverlay("LIK_Pauldron", GraphicOverlay.Create(cls.pauldron, v));
        }

        public static PaintChoice Defaults(VehicleDef d)
        {
            return new PaintChoice { one = d.graphicData.color, two = d.graphicData.colorTwo, three = (d.graphicData as GraphicDataRGB)?.colorThree ?? Color.white };
        }

        /// <summary>Copied armour + trim colours (paint window 'Copy colours' / 'Paste colours', 2026-11-17). Kept for the game session.</summary>
        public static PaintChoice Clipboard;

        /// <summary>The player's other Knights on any map (paintable classes), to copy colours from.</summary>
        public static List<VehiclePawn> OtherKnights(VehiclePawn except)
        {
            var l = new List<VehiclePawn>();
            foreach (Map m in Find.Maps)
                foreach (Pawn pw in m.mapPawns.AllPawnsSpawned)
                {
                    VehiclePawn v = pw as VehiclePawn;
                    if (v == null || v == except || v.Faction != Faction.OfPlayer || v.patternData == null) continue;
                    if (KnightLoadout.ClassFor(v.VehicleDef) == null) continue;
                    l.Add(v);
                }
            return l;
        }

        public static PaintChoice Current(VehiclePawn v)
        {
            PaintChoice c = v?.patternData != null ? new PaintChoice { one = v.patternData.color, two = v.patternData.colorTwo, three = v.patternData.colorThree } : Defaults(v.VehicleDef);
            CompKnightHead kc = v?.GetComp<CompKnightHead>();
            if (kc?.stickers != null) c.stickers = kc.stickers.Select(s => s.Copy()).ToList();
            return c;
        }

        /// <summary>Paint a Knight at once (main thread).</summary>
        public static void Apply(VehiclePawn v, PaintChoice p)
        {
            if (v == null || p == null || !Paintable(v.VehicleDef)) return;
            try
            {
                if (v.patternData == null) v.patternData = new PatternData(v);
                v.patternToPaint = null;
                bool switched = UseRgbPattern(v);
                v.DrawColor = p.one;
                v.DrawColorTwo = p.two;
                v.DrawColorThree = p.three;
                // VF logs an error if its materials are recoloured before they exist (a Knight just loaded / stowed): build them first
                if (switched) RebuildMaterials(v);
                Graphic_Vehicle g = v.VehicleGraphic;
                if (g != null) v.Notify_ColorChanged();
                FacingArt.Warm(v);
                CompKnightHead kc = v.GetComp<CompKnightHead>();
                if (kc != null)
                {
                    kc.paintInit = true;
                    kc.stickers = p.stickers != null ? p.stickers.Select(s => s.Copy()).ToList() : new List<KnightSticker>();
                    kc.stickerVersion++;
                    kc.Apply(true);
                }
            }
            catch (Exception e) { Log.Warning("[LIK] could not paint " + v.LabelShort + ": " + e.Message); }
        }

        /// <summary>Knights saved before they could be painted have plain white paint data (the pictures would show grey): give them the
        /// class colours once.</summary>
        public static void EnsureInitial(VehiclePawn v)
        {
            CompKnightHead kc = v?.GetComp<CompKnightHead>();
            if (kc == null || !Paintable(v.VehicleDef)) return;
            if (!kc.paintInit)
            {
                kc.paintInit = true;
                // plain white paint data (saved before the class was paintable): give it the class colours (Apply also moves it to the RGB shader)
                if (v.patternData == null || (IsWhite(v.patternData.color) && IsWhite(v.patternData.colorTwo)))
                {
                    Apply(v, Defaults(v.VehicleDef));
                    return;
                }
            }
            // Every Knight still on VF's Default pattern - loaded from a save OR freshly spawned with the def's colours (2026-11-15: a new
            // Cerastus showed white trim) - moves to the 3-colour paint shader, else the trim stays white.
            if (UseRgbPattern(v))
            {
                try { RebuildMaterials(v); if (v.VehicleGraphic != null) v.Notify_ColorChanged(); FacingArt.Warm(v); kc.Apply(true); }
                catch (Exception e) { Log.Warning("[LIK] could not refresh the paint of " + v.LabelShort + ": " + e.Message); }
            }
        }

        /// <summary>The Knights' own pattern entry: VF draws every vehicle with its PATTERN shader, which only paints the mask's red part
        /// (the Default pattern is solid red) - the green trim channel stayed unpainted / white (user 2026-11-13). This entry makes VF use
        /// its 3-colour RGB shader instead: red mask = colour one (armour), green = colour two (trim).</summary>
        public static PatternDef RgbPattern => rgbPattern ?? (rgbPattern = DefDatabase<PatternDef>.GetNamedSilentFail("LIK_KnightPaint"));
        static PatternDef rgbPattern;

        /// <summary>Puts the Knight on the RGB paint pattern. True if it changed (its materials must then be rebuilt).</summary>
        static bool UseRgbPattern(VehiclePawn v)
        {
            PatternDef rgb = RgbPattern;
            if (rgb == null) return false;
            if (v.patternData == null) v.patternData = new PatternData(v);
            if (v.patternData.patternDef == rgb) return false;
            v.patternData.patternDef = rgb;
            return true;
        }

        /// <summary>VF picks a material's shader when it first caches it: drop the body's cached materials and build them again.</summary>
        static void RebuildMaterials(VehiclePawn v)
        {
            if (RGBMaterialPool.TargetCached(v)) RGBMaterialPool.Release(v);
            v.ResetGraphic();
            // the weapons follow the body's pattern: rebuild them too (else their UI picture has no material -> NRE in RGBMaterialPool.GetUi)
            try { v.CompVehicleTurrets?.RevalidateTurrets(); } catch (Exception e) { Log.Warning("[LIK] could not refresh the weapons of " + v.LabelShort + ": " + e.Message); }
        }

        static bool IsWhite(Color c) { return c.r > 0.99f && c.g > 0.99f && c.b > 0.99f; }

        public static long Key(Color one, Color two)
        {
            Color32 a = one, b = two;
            return ((long)a.r << 40) | ((long)a.g << 32) | ((long)a.b << 24) | ((long)b.r << 16) | ((long)b.g << 8) | b.b;
        }

        static string Hex(Color c) { return ColorUtility.ToHtmlStringRGB(c); }

        /// <summary>A material that draws a masked picture with these colours (main thread only).</summary>
        public static Material MakeMat(Texture2D main, Texture2D mask, Color one, Color two, Color three)
        {
            Shader sh = VehicleShaderTypeDefOf.CutoutComplexRGB?.Shader;
            if (sh == null || main == null || mask == null) return null;
            var m = new Material(sh) { mainTexture = main, name = main.name + "_LIKpaint" };
            m.SetTexture(MaskTexID, mask);
            m.color = Color.white;
            m.SetColor(ColorOneID, one);
            m.SetColor(ColorTwoID, two);
            m.SetColor(ColorThreeID, three);
            return m;
        }

        /// <summary>Draws a picture (+ its colour mask "<path>m" if any) with these colours into 'fit'. False if the picture is missing.</summary>
        static bool DrawMasked(Rect fit, string path, PaintChoice p)
        {
            Texture2D main = ContentFinder<Texture2D>.Get(path, false);
            if (main == null) return false;
            Texture2D mask = ContentFinder<Texture2D>.Get(path + "m", false);
            if (mask == null) { GUI.DrawTexture(fit, main, ScaleMode.ScaleToFit); return true; }
            string key = path + "|" + Hex(p.one) + Hex(p.two);
            if (!uiMats.TryGetValue(key, out Material mat))
            {
                mat = MakeMat(main, mask, p.one, p.two, p.three);
                if (uiMats.Count > 400) uiMats.Clear();
                uiMats[key] = mat;
            }
            if (mat == null) GUI.DrawTexture(fit, main, ScaleMode.ScaleToFit);
            else GenUI.DrawTextureWithMaterial(fit, main, mat);
            return true;
        }

        static Rect Fit(Rect r, Texture2D t)
        {
            float s = Mathf.Min(r.width / t.width, r.height / t.height);
            return new Rect(0f, 0f, t.width * s, t.height * s) { center = r.center };
        }

        /// <summary>The picture of a class for the hangar windows (flat hangar view, else the front view) with these colours.</summary>
        public static void DrawClassPicture(Rect r, KnightClassDef cls, PaintChoice p, string fallback)
        {
            string path = !string.IsNullOrEmpty(cls.hangarTex) ? cls.hangarTex : cls.vehicle.graphicData.texPath + "_south";
            Texture2D t = ContentFinder<Texture2D>.Get(path, false);
            if (t != null)
            {
                if (Paintable(cls.vehicle)) DrawMasked(Fit(r, t), path, p);
                else GUI.DrawTexture(r, t, ScaleMode.ScaleToFit);
                return;
            }
            Widgets.DrawBox(r.ContractedBy(4f));
            Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(r, fallback); Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>The Knight as it stands in game in one facing, with its shoulder plates and stickers. Returns the picture's rect.</summary>
        public static Rect DrawFacing(Rect r, KnightClassDef cls, PaintChoice p, string facing, List<Stickers.LayerTex> stickers)
        {
            string body = cls.vehicle.graphicData.texPath + "_" + facing;
            Texture2D main = ContentFinder<Texture2D>.Get(body, false);
            if (main == null) { DrawClassPicture(r, cls, p, cls.label); return default; }
            Rect fit = Fit(r, main);
            DrawMasked(fit, body, p);
            float s = fit.width / main.width;
            if (stickers != null)
                foreach (Stickers.LayerTex t in stickers)
                    if (t.layer != 128) GUI.DrawTexture(new Rect(fit.x + t.x0 * s, fit.y + t.y0 * s, t.w * s, t.h * s), t.tex);
            if (cls.pauldron != null) DrawMasked(fit, cls.pauldron.graphicData.texPath + "_" + facing, p);
            if (stickers != null)
                foreach (Stickers.LayerTex t in stickers)
                    if (t.layer == 128) GUI.DrawTexture(new Rect(fit.x + t.x0 * s, fit.y + t.y0 * s, t.w * s, t.h * s), t.tex);
            return fit;
        }

        public static void DrawSwatch(Rect r, Color one, Color two)
        {
            Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width * 0.62f, r.height), one);
            Widgets.DrawBoxSolid(new Rect(r.x + r.width * 0.62f, r.y, r.width * 0.38f, r.height), two);
            GUI.color = new Color(0f, 0f, 0f, 0.8f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
        }

        public static string NameOf(VehicleDef d, PaintChoice p)
        {
            PaintChoice def = Defaults(d);
            long k = Key(p.one, p.two);
            string st = p.stickers != null && p.stickers.Count > 0 ? " + " + p.stickers.Count + " sticker" + (p.stickers.Count == 1 ? "" : "s") : "";
            if (k == Key(def.one, def.two)) return "Standard colours" + st;
            foreach (Scheme s in Houses) if (Key(s.one, s.two) == k) return s.label + st;
            return "Custom colours" + st;
        }
    }

    /// <summary>The hangar's paint window: the Knight (turnable) in its chosen colours, colour wheels for armour and trim, the house colours and
    /// the sticker editor.</summary>
    public class Window_PaintKnight : Window
    {
        readonly KnightClassDef cls;
        readonly VehiclePawn knight;                       // null = choosing for an assembly order
        readonly Action<PaintChoice> onApply;
        readonly PaintChoice p;
        Vector2 scroll;
        int facingIdx = 2;
        int slot;                                          // 0 armour, 1 trim
        bool dragging;
        int version, builtVersion = -1;
        string builtFacing;
        List<Stickers.LayerTex> built = new List<Stickers.LayerTex>();

        public override Vector2 InitialSize => CogUI.FrameReady ? new Vector2(1150f, 820f) : new Vector2(1040f, 700f);
        protected override float Margin => CogUI.FrameReady ? 0f : base.Margin;

        public override void PreOpen()
        {
            base.PreOpen();
            if (!CogUI.FrameReady) return;
            doWindowBackground = false; drawShadow = false; doCloseX = false; draggable = true;
        }

        public Window_PaintKnight(KnightClassDef cls, VehiclePawn knight, PaintChoice start, Action<PaintChoice> onApply)
        {
            this.cls = cls; this.knight = knight; this.onApply = onApply;
            p = (start ?? KnightPaint.Defaults(cls.vehicle)).Copy();
            doCloseX = true; absorbInputAroundWindow = true; forcePause = true;
        }

        public override void PostClose() { Stickers.Release(built); }

        // ------------------------------------------------------------------ cogitator look (user 2026-11-16: same UI style for the paint window and the stickers)
        // Same machine as the hangar (9-slice frame, emblem bracket, vox grille, purity seal), green screen chrome; the Knight preview and the
        // colour wheel sit on neutral pict-glass so the colours read true.
        public override void DoWindowContents(Rect inRect)
        {
            if (!CogUI.FrameReady) { DrawPlain(inRect); return; }
            Rect S = CogUI.MachineBack(inRect);
            GUI.BeginGroup(S);
            DrawCog(new Rect(0f, 0f, S.width, S.height));
            GUI.EndGroup();
            CogUI.MachineFront(inRect);
            Rect f = CogUI.FrameOf(inRect);
            if (CogUI.Key(new Rect(f.xMax - 480f, inRect.height - 78f, 280f, 56f), knight != null ? "PAINT NOW" : "USE SCHEME", true)) { onApply?.Invoke(p.Copy()); Close(); }
            if (CogUI.Key(new Rect(f.xMax - 190f, inRect.height - 78f, 170f, 56f), "CANCEL", false)) Close();
        }

        void DrawCog(Rect R)
        {
            string facing = Stickers.Facings[facingIdx];
            float y0 = CogUI.Header(R, "HOLO.DISP. ++ HERALDRY // " + (knight != null ? knight.LabelShort : cls.label).ToUpperInvariant() + " [PRIMARY]", "SCRIPTORIUM.V3");

            // ---- the Knight on pict-glass (turnable)
            Rect pane = new Rect(10f, y0, 420f, 450f);
            CogUI.Panel(pane, "PICT-GLASS // FACING " + facing.ToUpperInvariant());
            Rect glass = new Rect(pane.x + 4f, pane.y + 23f, pane.width - 8f, pane.height - 27f);
            CogUI.Fill(glass, CogUI.GLASS);
            if (builtVersion != version || builtFacing != facing)
            {
                Stickers.Release(built);
                built = Stickers.Build(cls, "base", facing, p.stickers);
                builtVersion = version; builtFacing = facing;
            }
            KnightPaint.DrawFacing(glass.ContractedBy(6f), cls, p, facing, built);
            if (CogUI.Button(new Rect(glass.x + 6f, glass.y + 6f, 40f, 28f), "<")) facingIdx = (facingIdx + 1) % 4;
            if (CogUI.Button(new Rect(glass.xMax - 46f, glass.y + 6f, 40f, 28f), ">")) facingIdx = (facingIdx + 3) % 4;
            TooltipHandler.TipRegion(new Rect(glass.x + 6f, glass.y + 6f, 40f, 28f), "Turn the Knight");
            TooltipHandler.TipRegion(new Rect(glass.xMax - 46f, glass.y + 6f, 40f, 28f), "Turn the Knight");
            float y = pane.yMax + 8f;
            CogUI.Label(new Rect(10f, y, 420f, 22f), KnightPaint.NameOf(cls.vehicle, p).ToUpperInvariant(), CogUI.G4, 18, true);
            y += 28f;
            if (Stickers.HasMaps(cls))
            {
                if (CogUI.Button(new Rect(10f, y, 420f, 32f), "Transfers / stickers  (" + p.stickers.Count + ")"))
                    Find.WindowStack.Add(new Window_StickerEditor(cls, p, () => version++));
                CogUI.Label(new Rect(10f, y + 36f, 420f, 40f), "> checks, hazard stripes, camo and your own transfers: pick one and click the Knight. Each has its own colours.", CogUI.G2, 11, false, TextAnchor.UpperLeft, true);
            }

            // ---- pigment: armour / trim tabs + the colour wheel on glass
            float cx = 444f, cw = 290f;
            for (int k = 0; k < 2; k++)
            {
                Rect tab = new Rect(cx + k * (cw / 2f), y0, cw / 2f - 4f, 30f);
                bool sel = k == slot;
                CogUI.Fill(tab, sel ? CogUI.PANEL_SEL : CogUI.PANEL);
                CogUI.Border(tab, sel ? CogUI.G4 : CogUI.G2, sel ? 2f : 1f);
                Widgets.DrawBoxSolid(new Rect(tab.x + 6f, tab.y + 6f, 18f, 18f), k == 0 ? p.one : p.two);
                CogUI.Border(new Rect(tab.x + 6f, tab.y + 6f, 18f, 18f), CogUI.INK, 1f);
                CogUI.Label(new Rect(tab.x + 32f, tab.y, tab.width - 34f, 30f), k == 0 ? "ARMOUR" : "TRIM", sel ? CogUI.G4 : CogUI.G2, 15, true, TextAnchor.MiddleLeft);
                if (Widgets.ButtonInvisible(tab)) slot = k;
            }
            Rect pig = new Rect(cx, y0 + 38f, cw, 300f);
            CogUI.Panel(pig, "PIGMENT");
            Rect pg = new Rect(pig.x + 4f, pig.y + 23f, pig.width - 8f, pig.height - 27f);
            CogUI.Fill(pg, CogUI.GLASS);
            if (!Input.GetMouseButton(0)) dragging = false;
            Color c = slot == 0 ? p.one : p.two;
            if (ColourWheel.Draw(new Rect(pg.x + 8f, pg.y + 8f, pg.width - 16f, pg.height - 12f), ref c, ref dragging, "LIKpaint", true))
            {
                if (slot == 0) p.one = c; else p.two = c;
            }
            CogUI.Label(new Rect(cx, pig.yMax + 8f, cw, 60f), "> armour = the plates, trim = the edging and emblems. Metal, lights and the outline keep their own colours.", CogUI.G2, 11, false, TextAnchor.UpperLeft, true);

            // ---- house colours
            float rx = cx + cw + 14f, rw = R.width - 10f - rx;
            PaintChoice d = KnightPaint.Defaults(cls.vehicle);
            if (SchemeRow(new Rect(rx, y0, rw, 32f), "Standard colours", d.one, d.two)) { p.one = d.one; p.two = d.two; }
            // ---- copy / paste a colour scheme (user 2026-11-17: match Knights whose colours are not in the house list). Armour + trim only:
            // sticker positions belong to one class's body, so they are not carried over.
            float bw = (rw - 6f) / 2f;
            if (CogUI.Button(new Rect(rx, y0 + 38f, bw, 30f), "Copy colours")) { KnightPaint.Clipboard = new PaintChoice { one = p.one, two = p.two }; }
            TooltipHandler.TipRegion(new Rect(rx, y0 + 38f, bw, 30f), "Remember these armour and trim colours, to paste them on another Knight.");
            bool canPaste = KnightPaint.Clipboard != null;
            if (CogUI.Button(new Rect(rx + bw + 6f, y0 + 38f, bw, 30f), canPaste ? "Paste colours" : "Paste (nothing copied)") && canPaste)
            { p.one = KnightPaint.Clipboard.one; p.two = KnightPaint.Clipboard.two; }
            if (canPaste) KnightPaint.DrawSwatch(new Rect(rx + bw + 6f + bw - 34f, y0 + 44f, 28f, 18f), KnightPaint.Clipboard.one, KnightPaint.Clipboard.two);
            Rect list = new Rect(rx, y0 + 76f, rw, R.height - 10f - y0 - 76f);
            CogUI.Panel(list, "YOUR KNIGHTS // KNIGHT HOUSES");
            Rect outer = new Rect(list.x + 4f, list.y + 23f, list.width - 8f, list.height - 27f);
            List<VehiclePawn> mine = KnightPaint.OtherKnights(knight);
            float head = 26f, mineH = mine.Count > 0 ? head + mine.Count * 36f + 8f : 0f;
            Rect view = new Rect(0f, 0f, outer.width - 16f, mineH + head + KnightPaint.Houses.Count * 36f);
            Widgets.BeginScrollView(outer, ref scroll, view);
            float yy = 0f;
            if (mine.Count > 0)
            {
                CogUI.Label(new Rect(4f, yy, view.width, head), "> YOUR KNIGHTS - click to copy their colours", CogUI.G2, 12, true, TextAnchor.MiddleLeft);
                yy += head;
                foreach (VehiclePawn o in mine)
                {
                    PaintChoice oc = KnightPaint.Current(o);
                    if (SchemeRow(new Rect(0f, yy, view.width, 32f), o.LabelShort + " (" + o.def.label + ")", oc.one, oc.two)) { p.one = oc.one; p.two = oc.two; }
                    yy += 36f;
                }
                yy += 8f;
            }
            CogUI.Label(new Rect(4f, yy, view.width, head), "> KNIGHT HOUSES", CogUI.G2, 12, true, TextAnchor.MiddleLeft);
            yy += head;
            for (int i = 0; i < KnightPaint.Houses.Count; i++)
            {
                KnightPaint.Scheme s = KnightPaint.Houses[i];
                if (SchemeRow(new Rect(0f, yy + i * 36f, view.width, 32f), s.label, s.one, s.two)) { p.one = s.one; p.two = s.two; }
            }
            Widgets.EndScrollView();
        }

        static readonly Dictionary<string, Texture2D> emblemCache = new Dictionary<string, Texture2D>();

        /// <summary>The house's emblem from the House heraldry sticker set (icon / emblem / heraldry picture named after the house), or null.</summary>
        static Texture2D HouseEmblem(string label)
        {
            if (emblemCache.TryGetValue(label, out Texture2D t)) return t;
            string key = label.Replace("House ", "").Trim().ToLowerInvariant();
            string best = null;
            foreach (string dn in Stickers.Designs)
            {
                if (!Stickers.Category(dn).StartsWith("House")) continue;           // "House icons" / "House banners" (Tools/sort_stickers.py, 2026-11-17)
                string n = dn.ToLowerInvariant();
                int slash = n.LastIndexOf('/');
                if (slash >= 0) n = n.Substring(slash + 1);
                if (!n.StartsWith(key)) continue;
                if (n.Contains("icon") || n.Contains("emblem") || n.Contains("heraldry")) { best = dn; break; }
                if (best == null) best = dn;
            }
            t = best != null ? Stickers.DesignThumb(best) : null;
            emblemCache[label] = t;
            return t;
        }

        bool SchemeRow(Rect r, string label, Color a, Color b)
        {
            bool sel = KnightPaint.Key(a, b) == KnightPaint.Key(p.one, p.two), over = Mouse.IsOver(r);
            CogUI.Fill(r, sel ? CogUI.PANEL_SEL : over ? CogUI.C2(10, 36, 14) : CogUI.PANEL);
            CogUI.Border(r, sel ? CogUI.G4 : over ? CogUI.G2 : CogUI.G1, sel ? 2f : 1f);
            KnightPaint.DrawSwatch(new Rect(r.x + 6f, r.y + 6f, 44f, r.height - 12f), a, b);
            Texture2D em = HouseEmblem(label);
            if (em != null) GUI.DrawTexture(new Rect(r.x + 56f, r.y + 3f, r.height - 6f, r.height - 6f), em, ScaleMode.ScaleToFit);
            CogUI.Label(new Rect(r.x + 60f + r.height, r.y, r.width - 64f - r.height, r.height), label.ToUpperInvariant(), sel ? CogUI.G4 : CogUI.G3, 14, true, TextAnchor.MiddleLeft);
            return Widgets.ButtonInvisible(r);
        }

        /// <summary>The old plain paint window (used when the machine textures are missing).</summary>
        void DrawPlain(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), knight != null ? "Paint " + knight.LabelShort : "Paint scheme: " + cls.label);
            Text.Font = GameFont.Small;
            string facing = Stickers.Facings[facingIdx];

            // ---- the Knight (turnable)
            Rect pic = new Rect(0f, 40f, 420f, 420f);
            Widgets.DrawBoxSolid(pic, new Color(0.05f, 0.06f, 0.09f, 0.9f));
            if (builtVersion != version || builtFacing != facing)
            {
                Stickers.Release(built);
                built = Stickers.Build(cls, "base", facing, p.stickers);
                builtVersion = version; builtFacing = facing;
            }
            KnightPaint.DrawFacing(pic.ContractedBy(6f), cls, p, facing, built);
            if (Widgets.ButtonText(new Rect(pic.x + 6f, pic.y + 6f, 40f, 30f), "<")) facingIdx = (facingIdx + 1) % 4;
            if (Widgets.ButtonText(new Rect(pic.xMax - 46f, pic.y + 6f, 40f, 30f), ">")) facingIdx = (facingIdx + 3) % 4;
            TooltipHandler.TipRegion(new Rect(pic.x + 6f, pic.y + 6f, 40f, 30f), "Turn the Knight");
            TooltipHandler.TipRegion(new Rect(pic.xMax - 46f, pic.y + 6f, 40f, 30f), "Turn the Knight");
            Widgets.Label(new Rect(0f, pic.yMax + 4f, 420f, 24f), KnightPaint.NameOf(cls.vehicle, p));
            if (Stickers.HasMaps(cls))
            {
                if (Widgets.ButtonText(new Rect(0f, pic.yMax + 32f, 420f, 36f), "Stickers... (" + p.stickers.Count + ")"))
                    Find.WindowStack.Add(new Window_StickerEditor(cls, p, () => version++));
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.75f, 0.75f, 0.75f);
                Widgets.Label(new Rect(0f, pic.yMax + 72f, 420f, 50f), "Checks, hazard stripes, camo and your own transfers: pick one and click the Knight in the big view. Each has its own colours.");
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }

            // ---- colour wheels (armour / trim)
            float cx = 436f, cw = 300f;
            for (int k = 0; k < 2; k++)
            {
                Rect tab = new Rect(cx + k * (cw / 2f), 40f, cw / 2f - 4f, 30f);
                Widgets.DrawBoxSolid(tab, k == slot ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : new Color(0.08f, 0.1f, 0.13f, 0.85f));
                Widgets.DrawBoxSolid(new Rect(tab.x + 4f, tab.y + 5f, 20f, 20f), k == 0 ? p.one : p.two);
                Widgets.Label(new Rect(tab.x + 30f, tab.y + 5f, tab.width - 30f, 24f), k == 0 ? "Armour" : "Trim");
                if (Widgets.ButtonInvisible(tab)) slot = k;
            }
            if (!Input.GetMouseButton(0)) dragging = false;
            Color c = slot == 0 ? p.one : p.two;
            if (ColourWheel.Draw(new Rect(cx, 80f, cw, 260f), ref c, ref dragging, "LIKpaint"))
            {
                if (slot == 0) p.one = c; else p.two = c;
            }
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(cx, 350f, cw, 60f), "Armour = the plates, trim = the edging and emblems. Metal, lights and the outline keep their own colours.");
            GUI.color = Color.white; Text.Font = GameFont.Small;

            // ---- house colours
            float rx = cx + cw + 16f, rw = inRect.width - rx;
            PaintChoice d = KnightPaint.Defaults(cls.vehicle);
            if (SchemeButton(new Rect(rx, 40f, rw, 32f), "Standard colours", d.one, d.two)) { p.one = d.one; p.two = d.two; }
            float hb = (rw - 6f) / 2f;
            if (Widgets.ButtonText(new Rect(rx, 78f, hb, 28f), "Copy colours")) KnightPaint.Clipboard = new PaintChoice { one = p.one, two = p.two };
            if (Widgets.ButtonText(new Rect(rx + hb + 6f, 78f, hb, 28f), KnightPaint.Clipboard != null ? "Paste colours" : "Paste (nothing copied)") && KnightPaint.Clipboard != null)
            { p.one = KnightPaint.Clipboard.one; p.two = KnightPaint.Clipboard.two; }
            Widgets.Label(new Rect(rx, 112f, rw, 24f), "Your Knights / Knight houses");
            Rect outer = new Rect(rx, 136f, rw, inRect.height - 136f - 52f);
            List<VehiclePawn> mine = KnightPaint.OtherKnights(knight);
            Rect view = new Rect(0f, 0f, rw - 16f, (mine.Count + KnightPaint.Houses.Count) * 36f);
            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < mine.Count; i++)
            {
                PaintChoice oc = KnightPaint.Current(mine[i]);
                if (SchemeButton(new Rect(0f, i * 36f, view.width, 32f), mine[i].LabelShort + " (" + mine[i].def.label + ")", oc.one, oc.two)) { p.one = oc.one; p.two = oc.two; }
            }
            for (int i = 0; i < KnightPaint.Houses.Count; i++)
            {
                KnightPaint.Scheme s = KnightPaint.Houses[i];
                if (SchemeButton(new Rect(0f, (mine.Count + i) * 36f, view.width, 32f), s.label, s.one, s.two)) { p.one = s.one; p.two = s.two; }
            }
            Widgets.EndScrollView();

            Rect apply = new Rect(inRect.width - 330f, inRect.height - 42f, 210f, 38f);
            if (Widgets.ButtonText(apply, knight != null ? "Paint now" : "Use this scheme"))
            {
                onApply?.Invoke(p.Copy());
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 110f, inRect.height - 42f, 110f, 38f), "Cancel")) Close();
        }

        bool SchemeButton(Rect r, string label, Color a, Color b)
        {
            bool sel = KnightPaint.Key(a, b) == KnightPaint.Key(p.one, p.two);
            Widgets.DrawBoxSolid(r, sel ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : (Mouse.IsOver(r) ? new Color(0.14f, 0.17f, 0.22f, 0.9f) : new Color(0.07f, 0.09f, 0.12f, 0.85f)));
            if (sel) { GUI.color = Color.green; Widgets.DrawBox(r, 2); GUI.color = Color.white; }
            KnightPaint.DrawSwatch(new Rect(r.x + 6f, r.y + 5f, 46f, r.height - 10f), a, b);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x + 62f, r.y, r.width - 66f, r.height), label);
            Text.Anchor = TextAnchor.UpperLeft;
            return Widgets.ButtonInvisible(r);
        }
    }

    /// <summary>Vehicle Framework's own "recolour" button (inspect pane) opens the Knight paint window instead of VF's picker with its camo patterns.</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.ChangeColor))]
    public static class Patch_KnightChangeColor
    {
        static bool Prefix(VehiclePawn __instance)
        {
            if (!KnightPaint.Paintable(__instance.VehicleDef)) return true;
            KnightClassDef cls = KnightLoadout.ClassFor(__instance.VehicleDef);
            if (cls == null) return true;
            Find.WindowStack.Add(new Window_PaintKnight(cls, __instance, KnightPaint.Current(__instance), ch => KnightPaint.Apply(__instance, ch)));
            return false;
        }
    }
    /// <summary>A VF pattern that draws with the plain 3-colour RGB shader (see KnightPaint.RgbPattern).</summary>
    public class KnightPaintPatternDef : PatternDef
    {
        public override ShaderTypeDef ShaderTypeDef => VehicleShaderTypeDefOf.CutoutComplexRGB;
    }

    /// <summary>VF's overlays (shoulder pads, hull) always use the Default pattern: on a Knight painted with the RGB pattern, use that.</summary>
    [HarmonyPatch(typeof(GraphicOverlay), nameof(GraphicOverlay.PatternDef), MethodType.Getter)]
    public static class Patch_KnightOverlayPattern
    {
        static void Postfix(GraphicOverlay __instance, ref PatternDef __result)
        {
            VehiclePawn v = __instance.Vehicle;
            if (v?.patternData?.patternDef is KnightPaintPatternDef k) __result = k;
        }
    }
}
