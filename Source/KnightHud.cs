using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// KNIGHT STATUS PANEL (user 2026-11-25: "a small UI stylised panel that has the hangar view of the Knight, uses green cutouts of the
    /// Knights and shows the health of each part" - for ALL Knights; "take a look at the hangar UI and make it like that"). When one Knight is
    /// selected (any faction) a small hangar cogitator sits above the inspect pane: the hangar bezel + CRT screen, the class's green
    /// schematic with every plate shaded by its part's health (part maps approved one by one per Knight in previews, baked by
    /// Tools/hud/bake_hud.py into Textures/UI/LIK_Hud), reactor (R) and throne (T) markers, every part's health bar, integrity, void
    /// shield, and the three bezel lamps lit green / amber / red for the Knight's overall state. Setting: Visuals > "Knight status panel".
    /// </summary>
    public class GameComponent_KnightHud : GameComponent
    {
        public GameComponent_KnightHud(Game game) { }

        // the hangar bezel without its purity seal (Textures/UI/LIK_Hud/bezel_noseal, Tools/hud/bezel_noseal.py), 1340 x 840; its frame starts at x 60.
        // Sized so the frame is exactly as wide as the inspect pane and sits right on top of its tabs (user 2026-11-25).
        const float FrameLeft = 60f, FrameWidth = 1280f, TabStrip = 31f;
        static readonly Rect ScreenRect = new Rect(106f, 92f, 1188f, 644f);          // the CRT inside the bezel (bezel pixels)
        static readonly Vector2[] Lamps = { new Vector2(682f, 789f), new Vector2(712f, 789f), new Vector2(741f, 789f) };
        static readonly Color[] LampCol = { new Color(0.31f, 1f, 0.35f), new Color(1f, 0.75f, 0.16f), new Color(1f, 0.24f, 0.16f) };
        static readonly string[] Parts = { "RightArmor", "LeftArmor", "FrontArmor", "RightLeg", "LeftLeg" };

        // reactor / throne marker spots on each class's schematic (fractions, from the approved previews)
        static readonly Dictionary<string, Vector2[]> Markers = new Dictionary<string, Vector2[]>
        {
            { "Armiger",   new[] { new Vector2(0.5f, 0.42f),  new Vector2(0.5f, 0.17f) } },
            { "Questoris", new[] { new Vector2(0.5f, 0.48f),  new Vector2(0.5f, 0.30f) } },
            { "Cerastus",  new[] { new Vector2(0.5f, 0.42f),  new Vector2(0.5f, 0.265f) } },
            { "Dominus",   new[] { new Vector2(0.5f, 0.28f),  new Vector2(0.5f, 0.455f) } },
            { "Acastus",   new[] { new Vector2(0.5f, 0.28f),  new Vector2(0.5f, 0.47f) } },
        };

        static readonly Dictionary<string, Texture2D> tex = new Dictionary<string, Texture2D>();
        static Texture2D T(string name)
        {
            Texture2D t;
            if (!tex.TryGetValue(name, out t)) tex[name] = t = ContentFinder<Texture2D>.Get(name, false);
            return t;
        }

        static string ClassKey(KnightClassDef cls)
        {
            string d = cls.defName;
            foreach (string k in Markers.Keys) if (d.Contains(k)) return k;
            return null;
        }

        static Color HealthColor(float h)
        {
            if (h <= 0f) return (Time.realtimeSinceStartup % 0.8f) < 0.4f ? new Color(0.90f, 0.16f, 0.10f) : new Color(0.40f, 0.06f, 0.04f);
            if (h < 0.4f) return new Color(0.95f, 0.30f, 0.20f);
            if (h < 0.75f) return new Color(0.95f, 0.75f, 0.25f);
            return new Color(0.36f, 0.96f, 0.36f);
        }

        public override void GameComponentOnGUI()
        {
            if (LIKMod.I == null || !LIKMod.I.statusPanel || WorldRendererUtility.WorldRendered || Find.CurrentMap == null) return;
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.MouseMove) return;
            if (Find.Selector.NumSelected != 1 || !(Find.Selector.SingleSelectedThing is VehiclePawn v) || !v.Spawned || v.Map != Find.CurrentMap) return;
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            if (cls == null || v.statHandler?.components == null) return;
            try { Draw(v, cls); }
            catch (System.Exception e) { Log.ErrorOnce("[LIK] Knight status panel: " + e, 0x51a8); }
        }

        static void Draw(VehiclePawn v, KnightClassDef cls)
        {
            MainTabWindow_Inspect insp = MainButtonDefOf.Inspect?.TabWindow as MainTabWindow_Inspect;
            bool open = insp != null && Find.WindowStack.IsOpen(insp);
            float paneX = open ? insp.windowRect.x : 0f, paneW = open ? insp.windowRect.width : 432f;
            float bottom = open ? insp.windowRect.y - TabStrip : UI.screenHeight - 35f;
            float SC = paneW / FrameWidth;
            float W = 1340f * SC, H = 840f * SC;
            Rect panel = new Rect(paneX - FrameLeft * SC, bottom - H, W, H);
            Rect scr = new Rect(panel.x + ScreenRect.x * SC, panel.y + ScreenRect.y * SC, ScreenRect.width * SC, ScreenRect.height * SC);

            // CRT screen
            if (CogUI.crtBg != null) GUI.DrawTexture(scr, CogUI.crtBg, ScaleMode.StretchToFill);
            else CogUI.Fill(scr, new Color(0.015f, 0.06f, 0.02f));

            // title strip
            string who = v.Faction == null || v.Faction.IsPlayer ? "" : " // " + v.Faction.Name.ToUpperInvariant();
            Rect title = new Rect(scr.x + 6f, scr.y + 6f, scr.width - 13f, 16f);
            CogUI.Fill(title, CogUI.G3);
            CogUI.Label(new Rect(title.x + 5f, title.y - 1f, title.width - 8f, 18f), "KNIGHT HEALTH // " + v.LabelShort.ToUpperInvariant() + who, CogUI.INK, 12, true, TextAnchor.MiddleLeft);

            // the schematic: rings behind, then each part layer tinted by its health, neutral areas and dark seams
            float P = scr.height - 36f;
            Rect pic = new Rect(scr.x + 10f, scr.y + 28f, P, P);
            Vector2 c = pic.center;
            for (int k = 1; k <= 3; k++) CogUI.Ring(c, k * P / 6f, CogUI.G1);
            var byKey = new Dictionary<string, VehicleComponent>();
            foreach (VehicleComponent comp in v.statHandler.components) if (comp?.props?.key != null) byKey[comp.props.key] = comp;
            string ck = ClassKey(cls);
            if (ck != null)
            {
                Texture2D rest = T("UI/LIK_Hud/" + ck + "_Rest");
                if (rest != null) GUI.DrawTexture(pic, rest, ScaleMode.StretchToFill);
                foreach (string p in Parts)
                {
                    Texture2D layer = T("UI/LIK_Hud/" + ck + "_" + p);
                    if (layer == null) continue;
                    VehicleComponent comp;
                    GUI.color = byKey.TryGetValue(p, out comp) ? HealthColor(comp.HealthPercent) : HealthColor(1f);
                    GUI.DrawTexture(pic, layer, ScaleMode.StretchToFill);
                }
                GUI.color = Color.white;
                Texture2D seam = T("UI/LIK_Hud/" + ck + "_Seam");
                if (seam != null) GUI.DrawTexture(pic, seam, ScaleMode.StretchToFill);

                // reactor + throne markers
                Vector2[] mk = Markers[ck];
                Marker(pic, mk[0], "R", byKey.TryGetValue("Reactor", out VehicleComponent rc) ? rc : null);
                Marker(pic, mk[1], "T", byKey.TryGetValue("Throne", out VehicleComponent tc) ? tc : null);
            }

            // the list: integrity, void shield, every part (most damaged first)
            float x = pic.xMax + 12f, y = scr.y + 30f, w = scr.xMax - 8f - x;
            float integrity = v.GetStatValue(VehicleStatDefOf.BodyIntegrity);
            Row(ref y, x, w, "INTEGRITY", integrity, true, null, null);
            CompKnightHead kc = v.GetComp<CompKnightHead>();
            if (kc != null && kc.voidShield)
            {
                float max = kc.ShieldMax, cur = kc.shieldCharge < 0f ? max : kc.shieldCharge;
                Row(ref y, x, w, "VOID SHIELD", max > 0f ? cur / max : 0f, true, new Color(0.45f, 0.85f, 1f), null);
            }
            if (kc != null && IonShield.Fitted(v))
                Row(ref y, x, w, "ION GAUNTLET", IonShield.Charge(kc) / IonShield.Capacity, true, new Color(0.45f, 0.85f, 1f), "Ion gauntlet shield charge");
            if (kc != null)
            {
                // the torso ion shield: which way it is set (IonShieldTorso.cs)
                string mode = IonShieldTorso.ModeNames[Mathf.Clamp(kc.ionMode, 0, 4)].ToUpperInvariant();
                bool down = IonShieldTorso.Down(kc);
                float ion = down ? 0f : IonShieldTorso.Charge(v, kc) / IonShieldTorso.Capacity(v);
                Row(ref y, x, w, "ION SHIELD " + (down ? "DOWN" : mode), ion, true, new Color(0.45f, 0.85f, 1f),
                    "Ion shield (" + mode.ToLowerInvariant() + "): " + (down ? "collapsed, restarting" : IonShieldTorso.Charge(v, kc).ToString("0") + " / " + IonShieldTorso.Capacity(v).ToString("0")));
            }
            y += 2f;
            foreach (VehicleComponent comp in v.statHandler.components.Where(q => q?.props != null).OrderBy(q => q.HealthPercent))
                if (y < scr.yMax - 14f) Row(ref y, x, w, comp.props.label.ToUpperInvariant(), comp.HealthPercent, false, null,
                                            comp.props.label.CapitalizeFirst() + ": " + comp.Health.ToString("0") + " / " + comp.MaxHealth.ToString("0"));

            // scanlines over the screen, then the bezel over everything
            if (CogUI.crtOver != null) GUI.DrawTexture(scr, CogUI.crtOver, ScaleMode.StretchToFill);
            Texture2D bezel = T("UI/LIK_Hud/bezel_noseal") ?? CogUI.bezel;
            if (bezel != null) GUI.DrawTexture(panel, bezel, ScaleMode.StretchToFill);

            // status lamps: green / amber / red for the overall state
            int lamp = integrity >= 0.75f ? 0 : integrity >= 0.4f ? 1 : 2;
            Texture2D glow = T("UI/LIK_Hud/lamp_glow");
            if (glow != null)
            {
                Vector2 lp = new Vector2(panel.x + Lamps[lamp].x * SC, panel.y + Lamps[lamp].y * SC);
                GUI.color = LampCol[lamp];
                GUI.DrawTexture(new Rect(lp.x - 11f, lp.y - 11f, 22f, 22f), glow, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }
            // the panel takes the mouse so clicks on it do not go through to the map
            if (Mouse.IsOver(panel) && Event.current.type == EventType.MouseDown) Event.current.Use();
        }

        static void Marker(Rect pic, Vector2 f, string letter, VehicleComponent comp)
        {
            Vector2 m = new Vector2(pic.x + f.x * pic.width, pic.y + f.y * pic.height);
            Rect r = new Rect(m.x - 12f, m.y - 12f, 24f, 24f);
            Color col = HealthColor(comp != null ? comp.HealthPercent : 1f);
            Texture2D disc = T("UI/LIK_Hud/marker_disc"), rings = T("UI/LIK_Hud/marker_rings");
            if (disc != null) GUI.DrawTexture(r, disc, ScaleMode.StretchToFill);
            if (rings != null) { GUI.color = col; GUI.DrawTexture(r, rings, ScaleMode.StretchToFill); GUI.color = Color.white; }
            CogUI.Label(r, letter, col, 11, true, TextAnchor.MiddleCenter);
            if (comp != null) TooltipHandler.TipRegion(r, comp.props.label.CapitalizeFirst() + ": " + comp.HealthPercent.ToStringPercent());
        }

        static void Row(ref float y, float x, float w, string label, float pct, bool big, Color? colour, string tip)
        {
            float h = big ? 16f : 12f;
            float lw = w * 0.55f;
            Rect row = new Rect(x, y, w, h);
            CogUI.Label(new Rect(x, y - 2f, lw, h + 3f), label, big ? CogUI.G4 : CogUI.G3, big ? 11 : 10, big);
            Rect bar = new Rect(x + lw, y + 2f, w - lw - 32f, h - 5f);
            CogUI.Fill(bar, CogUI.G1);
            if (pct > 0f) CogUI.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(pct), bar.height), colour ?? HealthColor(pct));
            CogUI.Label(new Rect(bar.xMax + 2f, y - 2f, 32f, h + 3f), pct.ToStringPercent(), CogUI.G4, 10, false, TextAnchor.UpperRight);
            if (tip != null) TooltipHandler.TipRegion(row, tip);
            y += h + 1f;
        }
    }
}
