using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// ORBITAL DROP LAUNCH WINDOW (user 2026-11-12: "pull up the rimworld ritual ui, you select what knights drop, then it moves you to select
    /// the landing tile, then you select the dropped knights' positions"). Lists every Knight stowed on this ship: tick the ones to drop,
    /// choose the timing, Begin -> world tile -> place each Knight on the landing map.
    /// </summary>
    public class Dialog_KnightDrop : Window
    {
        readonly Map map;
        readonly List<Building_KnightHangar> hangars;
        readonly HashSet<Building_KnightHangar> chosen = new HashSet<Building_KnightHangar>();
        // user 2026-11-13: Seek & destroy switched on as each Knight lands, per Knight, on by default (remembered per hangar this session)
        public static readonly Dictionary<int, bool> seekOnLanding = new Dictionary<int, bool>();
        public static bool SeekOnLanding(Building_KnightHangar h) { bool s; return h == null || !seekOnLanding.TryGetValue(h.thingIDNumber, out s) || s; }
        Vector2 scroll;
        static readonly int[] Gaps = { 0, 2, 5, 10 };
        // user 2026-11-15: the list shows each Knight in ITS OWN paint + stickers (front view), not the class icon. Sticker layers are built
        // once per Knight while the window is open and freed on close.
        const float RowH = 92f, PicSize = 84f;
        readonly Dictionary<VehiclePawn, List<Stickers.LayerTex>> stickerPics = new Dictionary<VehiclePawn, List<Stickers.LayerTex>>();

        public override Vector2 InitialSize => CogUI.FrameReady ? new Vector2(1150f, 820f) : new Vector2(660f, 640f);
        protected override float Margin => CogUI.FrameReady ? 0f : base.Margin;

        public override void PreOpen()
        {
            base.PreOpen();
            if (!CogUI.FrameReady) return;
            doWindowBackground = false; drawShadow = false; doCloseX = false; draggable = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            foreach (List<Stickers.LayerTex> l in stickerPics.Values) Stickers.Release(l);
            stickerPics.Clear();
        }

        void DrawKnightPicture(Rect r, VehiclePawn v)
        {
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            if (cls != null && KnightPaint.Paintable(v.VehicleDef))
            {
                try
                {
                    PaintChoice p = KnightPaint.Current(v);
                    if (!stickerPics.TryGetValue(v, out List<Stickers.LayerTex> st))
                    {
                        st = Stickers.HasMaps(cls) ? Stickers.Build(cls, "base", "south", p.stickers) : new List<Stickers.LayerTex>();
                        stickerPics[v] = st;
                    }
                    KnightPaint.DrawFacing(r, cls, p, "south", st);
                    return;
                }
                catch (System.Exception e)
                {
                    Log.WarningOnce("[LIK] drop window: could not draw " + v.LabelShort + " in its paint: " + e.Message, v.thingIDNumber ^ 0x4c494b);
                }
            }
            if (v.def.uiIcon != null) GUI.DrawTexture(r, v.def.uiIcon, ScaleMode.ScaleToFit);
        }

        public Dialog_KnightDrop(Map map, IEnumerable<Building_KnightHangar> preselected)
        {
            this.map = map;
            hangars = map.listerBuildings.AllBuildingsColonistOfClass<Building_KnightHangar>().Where(h => h.StowedKnight != null).ToList();
            foreach (Building_KnightHangar h in preselected) if (OrbitalDropUtil.ReadyToDrop(h)) chosen.Add(h);
            doCloseButton = false; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
        }

        static string NotReady(Building_KnightHangar h)
        {
            VehiclePawn v = h.StowedKnight;
            if (!OrbitalDropUtil.CanDrop(v)) return "too light to drop";
            if (!OrbitalDropUtil.OnGravship(h)) return "hangar not on gravship floor";
            if (!OrbitalDropUtil.InOrbit(h.Map)) return "not in orbit";
            return null;                            // no pilot aboard is not a block: pick one with "Choose pilot" (2026-11-22)
        }

        static bool NeedsPilot(Building_KnightHangar h) { return h.StowedKnight != null && !OrbitalDropUtil.HasPilot(h.StowedKnight); }
        static bool Ready(Building_KnightHangar h) { return NotReady(h) == null && !NeedsPilot(h); }

        static VehicleRoleHandler PilotSeat(VehiclePawn v)
        {
            return v.handlers?.FirstOrDefault(x => x.role != null && (x.role.HandlingTypes & HandlingType.Movement) != 0 && x.AreSlotsAvailable);
        }

        /// <summary>Docked Knight without a pilot (the pilot climbed out on docking): a free colonist on this ship boards it from the hangar gantry.</summary>
        void ChoosePilot(VehiclePawn v)
        {
            VehicleRoleHandler seat = PilotSeat(v);
            if (seat == null) return;
            var opts = new List<FloatMenuOption>();
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                string why = BoardReasons.Why(seat, p) ?? (seat.CanOperateRole(p) ? null : p.LabelShortCap + " cannot take the pilot seat");
                if (why != null) { opts.Add(new FloatMenuOption(p.LabelShortCap + ": " + why, null)); continue; }
                Pawn pilot = p;
                opts.Add(new FloatMenuOption(p.LabelShortCap, delegate { Board(v, seat, pilot); }));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("No free colonists on this ship", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        static void Board(VehiclePawn v, VehicleRoleHandler seat, Pawn p)
        {
            IntVec3 at = p.Position; Map m = p.Map;
            p.jobs?.StopAll();
            p.DeSpawn();
            if (!v.TryAddPawn(p, seat))
            {
                GenSpawn.Spawn(p, at, m);
                Messages.Message(p.LabelShortCap + " could not board " + v.LabelShort + ".", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Messages.Message(p.LabelShortCap + " climbs the gantry into " + v.LabelShort + "'s Throne.", MessageTypeDefOf.TaskCompletion, false);
        }

        // ------------------------------------------------------------------ cogitator look (user 2026-11-22: "the drop window could also use a new
        // UI like the other one we made"): the hangar machine (frame, emblem bracket, vox grille, purity seal), green CRT; Knight pictures on
        // neutral pict-glass so their paint reads true; physical keys under the screen.
        public override void DoWindowContents(Rect inRect)
        {
            if (!CogUI.FrameReady) { DrawPlain(inRect); return; }
            Rect S = CogUI.MachineBack(inRect);
            GUI.BeginGroup(S);
            DrawCog(new Rect(0f, 0f, S.width, S.height));
            GUI.EndGroup();
            CogUI.MachineFront(inRect);
            Rect f = CogUI.FrameOf(inRect);
            if (CogUI.Key(new Rect(f.xMax - 480f, inRect.height - 78f, 280f, 56f), chosen.Count == 0 ? "CHOOSE KNIGHTS" : "BEGIN DROP", true)) Begin();
            if (CogUI.Key(new Rect(f.xMax - 190f, inRect.height - 78f, 170f, 56f), "CANCEL", false)) Close();
        }

        void Begin()
        {
            if (chosen.Count == 0) { Messages.Message("Tick at least one Knight to drop.", MessageTypeDefOf.RejectInput, false); return; }
            var list = hangars.Where(h => chosen.Contains(h) && OrbitalDropUtil.ReadyToDrop(h)).ToList();
            Close();
            if (list.Count > 0) OrbitalDropUtil.StartTargeting(list);
        }

        const float CogRowH = 112f, CogPic = 96f;

        void DrawCog(Rect R)
        {
            string ship = map?.Parent?.LabelCap ?? "GRAVSHIP";
            float y = CogUI.Header(R, "AUSPEX ++ ORBITAL DESCENT // " + ship.ToUpperInvariant(), "DESCENSUS.V1");
            CogUI.Label(new Rect(R.x + 12f, y, R.width - 24f, 20f), "1. MARK THE KNIGHTS TO DROP    2. PICK THE WORLD TILE    3. PLACE EACH KNIGHT, IN THE ORDER LISTED",
                CogUI.G2, 12, false, TextAnchor.UpperLeft, false, true);
            y += 26f;

            float rightW = 300f;
            Rect L = new Rect(R.x + 12f, y, R.width - rightW - 36f, R.yMax - y - 12f);
            Rect Rt = new Rect(L.xMax + 12f, y, rightW, L.height);

            // ---- list header
            int ready = hangars.Count(Ready);
            CogUI.Label(new Rect(L.x, L.y + 4f, L.width - 230f, 22f), "KNIGHTS IN DROP CRADLES  [" + chosen.Count + " / " + ready + " READY MARKED]", CogUI.G3, 15, true);
            if (CogUI.Button(new Rect(L.xMax - 224f, L.y, 120f, 28f), "Select all"))
                foreach (Building_KnightHangar h in hangars) if (Ready(h)) chosen.Add(h);
            if (CogUI.Button(new Rect(L.xMax - 98f, L.y, 98f, 28f), "Clear")) chosen.Clear();

            // ---- the Knights
            Rect outR = new Rect(L.x, L.y + 36f, L.width, L.height - 36f);
            Rect view = new Rect(0f, 0f, outR.width - 16f, Mathf.Max(outR.height - 4f, hangars.Count * (CogRowH + 8f)));
            Widgets.BeginScrollView(outR, ref scroll, view);
            float ry = 0f;
            if (hangars.Count == 0)
                CogUI.Label(new Rect(0f, 10f, view.width, 40f), "NO KNIGHT IS CLAMPED IN A DROP CRADLE ON THIS SHIP.", CogUI.RED, 15, true);
            foreach (Building_KnightHangar h in hangars) { CogRow(new Rect(0f, ry, view.width, CogRowH), h); ry += CogRowH + 8f; }
            Widgets.EndScrollView();

            // ---- right column: timing
            Rect tp = new Rect(Rt.x, Rt.y, Rt.width, 250f);
            CogUI.Panel(tp, "DESCENT TIMING");
            float ty = tp.y + 34f;
            for (int gi = 0; gi < Gaps.Length; gi++)
            {
                int g = Gaps[gi];
                bool sel = LIKMod.I.dropGapSeconds == g;
                if (CogUI.Button(new Rect(tp.x + 12f, ty, tp.width - 24f, 30f), g == 0 ? "All at once" : "Every " + g + " seconds", sel) && !sel)
                { LIKMod.I.dropGapSeconds = g; LIKMod.I.Write(); }
                ty += 36f;
            }
            CogUI.Label(new Rect(tp.x + 12f, ty + 2f, tp.width - 24f, 60f), LIKMod.I.dropGapSeconds <= 0
                ? "ALL KNIGHTS STRIKE THE GROUND AT THE SAME MOMENT."
                : "KNIGHTS LAND ONE AFTER ANOTHER, " + LIKMod.I.dropGapSeconds + " SECONDS APART, IN THE ORDER YOU PLACE THEM.", CogUI.G2, 12, false, TextAnchor.UpperLeft, true);

            // ---- right column: summary
            Rect sp = new Rect(Rt.x, tp.yMax + 12f, Rt.width, Rt.yMax - tp.yMax - 12f);
            CogUI.Panel(sp, "DROP AUGURY");
            float sy = sp.y + 34f;
            void Row(string k, string v, Color c) { CogUI.Label(new Rect(sp.x + 12f, sy, 130f, 18f), k, CogUI.G2, 12); CogUI.Label(new Rect(sp.x + 140f, sy, sp.width - 152f, 18f), v, c, 12, false, TextAnchor.UpperLeft, false, true); sy += 22f; }
            Row("MARKED", chosen.Count + (chosen.Count == 1 ? " KNIGHT" : " KNIGHTS"), chosen.Count > 0 ? CogUI.G4 : CogUI.RED);
            Row("RANGE", OrbitalDropUtil.MaxRange + " WORLD TILES", CogUI.G3);
            Row("SEEK & DESTROY", chosen.Count(SeekOnLanding) + " OF " + chosen.Count, CogUI.G3);
            int noPilot = hangars.Count(h => NotReady(h) == null && NeedsPilot(h));
            if (noPilot > 0) Row("NO PILOT", noPilot + (noPilot == 1 ? " KNIGHT" : " KNIGHTS"), CogUI.RED);
            sy += 8f;
            CogUI.Label(new Rect(sp.x + 12f, sy, sp.width - 24f, sp.yMax - sy - 8f),
                "THE CRADLE RELEASES. THE ABYSS RECEIVES. THE OMNISSIAH GUIDES THE FALL; THE MACHINE SPIRIT ENDURES THE IMPACT.", CogUI.G1, 12, false, TextAnchor.UpperLeft, true);
        }

        void CogRow(Rect row, Building_KnightHangar h)
        {
            VehiclePawn v = h.StowedKnight;
            if (v == null) return;
            string why = NotReady(h);
            bool needPilot = why == null && NeedsPilot(h);
            if (needPilot) chosen.Remove(h);
            bool on = chosen.Contains(h);
            CogUI.Fill(row, on ? CogUI.PANEL_SEL : CogUI.PANEL);
            CogUI.Border(row, on ? CogUI.G4 : Mouse.IsOver(row) ? CogUI.G3 : CogUI.G1, on ? 2f : 1f);

            Rect pic = new Rect(row.x + 8f, row.y + (row.height - CogPic) / 2f, CogPic, CogPic);
            CogUI.Fill(pic, CogUI.GLASS);
            CogUI.Border(pic, CogUI.G1, 1f);
            DrawKnightPicture(pic.ContractedBy(3f), v);

            float tx = pic.xMax + 14f, tw = row.width - (pic.xMax + 14f) - 200f;
            CogUI.Label(new Rect(tx, row.y + 10f, tw, 24f), v.LabelShortCap.ToUpperInvariant(), why == null ? CogUI.G4 : CogUI.G2, 18, true);
            CogUI.Label(new Rect(tx, row.y + 36f, tw, 18f), (v.def.label ?? "").ToUpperInvariant() + "  //  " + h.LabelShortCap.ToUpperInvariant(), CogUI.G2, 12);
            string pilots = string.Join(", ", TollUtil.Pilots(v).Select(p => p.LabelShort));
            if (why != null) CogUI.Label(new Rect(tx, row.y + 60f, tw, 40f), "CANNOT DROP: " + why.ToUpperInvariant(), CogUI.RED, 13, false, TextAnchor.UpperLeft, true);
            else if (needPilot) CogUI.Label(new Rect(tx, row.y + 60f, tw, 40f), "NO PILOT ABOARD - CHOOSE ONE TO BOARD FROM THE GANTRY", CogUI.RED, 13, false, TextAnchor.UpperLeft, true);
            else CogUI.Label(new Rect(tx, row.y + 60f, tw, 40f), "PILOT: " + pilots.ToUpperInvariant(), CogUI.G3, 13, false, TextAnchor.UpperLeft, true);

            Rect ctl = new Rect(row.xMax - 190f, row.y + 12f, 178f, row.height - 24f);
            if (needPilot)
            {
                if (CogUI.Button(new Rect(ctl.x, ctl.center.y - 18f, ctl.width, 36f), "Choose pilot")) ChoosePilot(v);
                return;
            }
            if (why != null) return;
            Rect sr = new Rect(ctl.x, ctl.y + 4f, ctl.width, 28f);
            bool sd = SeekOnLanding(h), sdWas = sd;
            CogUI.Check(sr, "SEEK & DESTROY", ref sd);
            TooltipHandler.TipRegion(sr, "On: this Knight lands with Seek & destroy switched on - it hunts down the enemy by itself" +
                (v.def.defName == "LIK_Acastus" ? " and raises its rocket launcher while enemies are left" : "") + ".\nOff: it lands and waits for your orders.");
            if (sd != sdWas) seekOnLanding[h.thingIDNumber] = sd;
            bool was = on;
            CogUI.Check(new Rect(ctl.x, ctl.yMax - 32f, ctl.width, 28f), on ? "MARKED TO DROP" : "MARK TO DROP", ref on);
            if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - 200f, row.height))) on = !on;
            if (on != was) { if (on) chosen.Add(h); else chosen.Remove(h); }
        }

        /// <summary>The plain window (used when the cogitator textures are missing).</summary>
        void DrawPlain(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), "Orbital drop");
            Text.Font = GameFont.Small;
            float y = inRect.y + 40f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 44f), "1. Tick the Knights to drop.   2. Pick the world tile.   3. Click a landing spot for each Knight, in the order listed.");
            y += 48f;
            // list header: count + select all / clear (moved up next to the list, user 2026-11-12: the old layout was confusing)
            int ready = hangars.Count(Ready);
            Widgets.Label(new Rect(inRect.x, y + 6f, 300f, 24f), "Knights on this ship (" + chosen.Count + " of " + ready + " ready chosen)");
            if (Widgets.ButtonText(new Rect(inRect.xMax - 210f, y, 110f, 30f), "Select all"))
                foreach (Building_KnightHangar h in hangars) if (Ready(h)) chosen.Add(h);
            if (Widgets.ButtonText(new Rect(inRect.xMax - 95f, y, 95f, 30f), "Clear")) chosen.Clear();
            y += 36f;

            // the Knights
            Rect outR = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - 150f);
            Rect view = new Rect(0f, 0f, outR.width - 16f, hangars.Count * (RowH + 4f));
            Widgets.BeginScrollView(outR, ref scroll, view);
            float ry = 0f;
            foreach (Building_KnightHangar h in hangars)
            {
                VehiclePawn v = h.StowedKnight;
                Rect row = new Rect(0f, ry, view.width, RowH);
                if (chosen.Contains(h)) Widgets.DrawHighlightSelected(row); else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                string why = NotReady(h);
                Rect icon = new Rect(row.x + 4f, row.y + (RowH - PicSize) / 2f, PicSize, PicSize);
                DrawKnightPicture(icon, v);
                string pilots = string.Join(", ", TollUtil.Pilots(v).Select(p => p.LabelShort));
                Rect text = new Rect(row.x + PicSize + 14f, row.y + (RowH - 46f) / 2f, row.width - PicSize - 202f, 46f);
                GUI.color = why == null ? Color.white : Color.gray;
                Widgets.Label(text, v.LabelShortCap + "  (" + v.def.label + ")\n" + (why == null ? "Pilot: " + (pilots.Length > 0 ? pilots : "none - choose one") : "Cannot drop: " + why));
                GUI.color = Color.white;
                if (why == null && NeedsPilot(h))
                {
                    chosen.Remove(h);
                    if (Widgets.ButtonText(new Rect(row.xMax - 150f, row.y + (RowH - 32f) / 2f, 140f, 32f), "Choose pilot")) ChoosePilot(v);
                }
                else if (why == null)
                {
                    // seek & destroy on landing
                    Rect sr = new Rect(row.xMax - 186f, row.y + (RowH - 28f) / 2f, 140f, 28f);
                    bool sd = SeekOnLanding(h), sdWas = sd;
                    Widgets.CheckboxLabeled(sr, "Seek & destroy", ref sd);
                    TooltipHandler.TipRegion(sr, "On: this Knight lands with Seek & destroy switched on - it hunts down the enemy by itself" +
                        (v.def.defName == "LIK_Acastus" ? " and raises its rocket launcher while enemies are left" : "") + ".\nOff: it lands and waits for your orders.");
                    if (sd != sdWas) seekOnLanding[h.thingIDNumber] = sd;
                    bool on = chosen.Contains(h), was = on;
                    Widgets.Checkbox(new Vector2(row.xMax - 34f, row.y + (RowH - 24f) / 2f), ref on);
                    if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - 192f, row.height))) on = !on;
                    if (on != was) { if (on) chosen.Add(h); else chosen.Remove(h); }
                }
                ry += RowH + 4f;
            }
            Widgets.EndScrollView();

            // timing: one box per choice, circle on the LEFT of its own label, chosen box highlighted
            y = inRect.yMax - 140f;
            Widgets.DrawLineHorizontal(inRect.x, y - 6f, inRect.width);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "Landing timing");
            y += 26f;
            float bw = (inRect.width - 3 * 8f) / Gaps.Length;
            for (int gi = 0; gi < Gaps.Length; gi++)
            {
                int g = Gaps[gi];
                Rect b = new Rect(inRect.x + gi * (bw + 8f), y, bw, 32f);
                bool sel = LIKMod.I.dropGapSeconds == g;
                Widgets.DrawOptionBackground(b, sel);
                Widgets.RadioButton(new Vector2(b.x + 6f, b.y + 4f), sel);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(b.x + 34f, b.y, b.width - 36f, b.height), g == 0 ? "All at once" : "Every " + g + " s");
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(b) && !sel) { LIKMod.I.dropGapSeconds = g; LIKMod.I.Write(); }
            }
            y += 36f;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), LIKMod.I.dropGapSeconds <= 0
                ? "All Knights hit the ground at the same moment."
                : "Knights land one after another, " + LIKMod.I.dropGapSeconds + " seconds apart, in the order you place them.");
            GUI.color = Color.white;

            // begin
            Rect go = new Rect(inRect.xMax - 200f, inRect.yMax - 40f, 200f, 40f);
            if (chosen.Count == 0) GUI.color = Color.gray;
            if (Widgets.ButtonText(go, chosen.Count == 0 ? "Choose a Knight" : "Begin (" + chosen.Count + (chosen.Count == 1 ? " Knight)" : " Knights)")) && chosen.Count > 0)
                Begin();
            GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(inRect.xMax - 310f, inRect.yMax - 40f, 100f, 40f), "Cancel")) Close();
        }
    }

    /// <summary>Runs queued actions on the next frame (the targeter stops itself after its click callback, so a chained pick must start later).</summary>
    public class GameComponent_LIKDeferred : GameComponent
    {
        static readonly List<System.Action> queue = new List<System.Action>();
        public GameComponent_LIKDeferred(Game game) { }
        public static void Next(System.Action a) { queue.Add(a); }
        public override void GameComponentUpdate()
        {
            if (queue.Count == 0) return;
            var run = queue.ToList(); queue.Clear();
            foreach (System.Action a in run) a();
        }
    }
}

namespace LIK
{
    /// <summary>Flattens the plants around a drop impact as an outward shockwave (~30 ticks) instead of all in one tick (freeze fix 2026-11-12).
    /// Not saved: if the game is saved mid-wave, the remaining plants simply stay.</summary>
    public class MapComponent_ImpactClear : Verse.MapComponent
    {
        class Wave { public Verse.IntVec3 c; public float radius; public int i; }
        readonly System.Collections.Generic.List<Wave> waves = new System.Collections.Generic.List<Wave>();

        public MapComponent_ImpactClear(Verse.Map map) : base(map) { }

        public void Add(Verse.IntVec3 c, float radius) { waves.Add(new Wave { c = c, radius = radius }); }

        public override void MapComponentTick()
        {
            for (int w = waves.Count - 1; w >= 0; w--)
            {
                Wave wv = waves[w];
                int total = Verse.GenRadial.NumCellsInRadius(wv.radius);
                int end = System.Math.Min(total, wv.i + total / 30 + 1);      // radial pattern is ordered by distance: an expanding ring
                for (; wv.i < end; wv.i++)
                {
                    Verse.IntVec3 x = wv.c + Verse.GenRadial.RadialPattern[wv.i];
                    if (!x.InBounds(map)) continue;
                    RimWorld.Plant pl = x.GetPlant(map);
                    if (pl != null && !pl.Destroyed) pl.Destroy();
                }
                if (wv.i >= total) waves.RemoveAt(w);
            }
        }
    }
}
