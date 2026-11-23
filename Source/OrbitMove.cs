using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// MOVE ABOVE TARGET (user 2026-11-12: "add a move gravship to above poi and when it does move its empty space but the gravship").
    /// Vanilla Odyssey counts every orbit tile as 20 tiles of distance (PlanetLayerDef Orbit rangeDistanceFactor 20), so a gravship in orbit can
    /// barely move. This adds a button on the pilot console, shown only in orbit: pick any point on the planet surface (a settlement, site, or
    /// plain land) and the ship flies to the free orbit tile right above it, priced like a normal flight over the ground (distance between the
    /// surface points below the ship and the target x the engine's fuel per tile, min 50). The arrival uses vanilla's own gravship flight, so the
    /// ship lands on an empty space map with only itself there. After the target is picked the vanilla launch ritual window opens (crew boards,
    /// pilot/copilot, quality, pawns-left-behind warning); when it finishes the ship flies to the stored target. Normal launch cooldown applies.
    /// </summary>
    public static class OrbitMove
    {
        static readonly System.Reflection.FieldInfo engineField = AccessTools.Field(typeof(CompPilotConsole), "engine");

        public static Building_GravEngine EngineOf(CompPilotConsole c) { return engineField?.GetValue(c) as Building_GravEngine; }

        static CompPilotConsole console;
        static Building_GravEngine engine;
        static PlanetTile below = PlanetTile.Invalid;

        public static void Start(CompPilotConsole c)
        {
            console = c; engine = EngineOf(c);
            PlanetLayer surface = Find.WorldGrid.FirstLayerOfDef(PlanetLayerDefOf.Surface);
            below = OrbitalDropUtil.Below(c.parent.Map.Tile);
            if (engine == null || surface == null || !below.Valid) { Messages.Message("No planet surface below the ship.", MessageTypeDefOf.RejectInput, false); return; }
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.SelectedLayer = surface;
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(below)));
            Find.WorldTargeter.BeginTargeting(Chose, true, null, false,
                () => { if (engine != null) GenDraw.DrawWorldRadiusRing(below, Mathf.Min(engine.MaxLaunchDistance, MaxForFuel())); },
                t => Label(t), t => Problem(t, out _, out _) == null, below, true);
        }

        static int Distance(PlanetTile to) { return Mathf.CeilToInt(Find.WorldGrid.ApproxDistanceInTiles(below, to)); }
        static float Cost(int dist) { return Mathf.Max(50f, dist * engine.FuelPerTile); }
        static int MaxForFuel() { return engine.FuelPerTile <= 0f ? 9999 : Mathf.FloorToInt(engine.TotalFuel / engine.FuelPerTile); }

        /// <summary>The free orbit tile nearest the point above a surface tile (searched outward a few rings if that one is taken).</summary>
        public static PlanetTile FreeOrbitTileAbove(PlanetTile surfaceTile, PlanetTile current)
        {
            PlanetLayer orbit = Find.WorldGrid.FirstLayerOfDef(PlanetLayerDefOf.Orbit);
            if (orbit == null) return PlanetTile.Invalid;
            PlanetTile start = orbit.GetClosestTile_NewTemp(surfaceTile);
            if (!start.Valid) return PlanetTile.Invalid;
            var seen = new HashSet<PlanetTile> { start };
            var ring = new List<PlanetTile> { start };
            var nb = new List<PlanetTile>();
            for (int r = 0; r < 6 && ring.Count > 0; r++)
            {
                foreach (PlanetTile t in ring)
                    if (t == current || (!Find.WorldObjects.AnyWorldObjectAt(t) && TileFinder.IsValidTileForNewSettlement(t, null, true))) return t;
                var next = new List<PlanetTile>();
                foreach (PlanetTile t in ring)
                {
                    nb.Clear();
                    Find.WorldGrid.GetTileNeighbors(t, nb);
                    foreach (PlanetTile n in nb) if (seen.Add(n)) next.Add(n);
                }
                ring = next;
            }
            return PlanetTile.Invalid;
        }

        static string Problem(GlobalTargetInfo t, out PlanetTile dest, out float cost)
        {
            dest = PlanetTile.Invalid; cost = 0f;
            if (engine == null || console == null || !console.parent.Spawned) return "The ship is gone";
            if (!t.Tile.Valid) return "Not a tile";
            if (t.Tile.LayerDef != PlanetLayerDefOf.Surface) return "Choose a point on the planet surface";
            int dist = Distance(t.Tile);
            cost = Cost(dist);
            if (dist > engine.MaxLaunchDistance) return "Out of range (" + dist + " / " + engine.MaxLaunchDistance + " tiles)";
            if (cost > engine.TotalFuel) return "Not enough chemfuel (" + cost.ToString("F0") + " needed)";
            dest = FreeOrbitTileAbove(t.Tile, console.parent.Map.Tile);
            if (!dest.Valid) return "No free space in orbit above that point";
            if (dest == console.parent.Map.Tile) return "The ship is already above this point";
            return null;
        }

        static TaggedString Label(GlobalTargetInfo t)
        {
            if (!t.IsValid) return "";
            string p = Problem(t, out _, out float cost);
            if (p != null) return p.Colorize(ColorLibrary.RedReadable);
            MapParent mp = Find.WorldObjects.MapParentAt(t.Tile);
            return "Move above " + (mp != null ? mp.LabelCap : (string)"here") + " - " + cost.ToString("F0") + " chemfuel";
        }

        // ---- the chosen target waits here until the vanilla launch ritual finishes (user 2026-11-12: "act similar to launch with the ui that
        // then pulls the pawns inside, that way none get left behind"). The ritual's outcome calls StartChoosingDestination_NewTemp, where
        // Patch_OrbitMoveLaunch flies to this tile instead of opening the vanilla tile picker.
        public static CompPilotConsole pendingConsole;
        public static PlanetTile pendingSurface = PlanetTile.Invalid;
        public static bool pendingConfirmed, openingOurs;

        public static void ClearPending() { pendingConsole = null; pendingSurface = PlanetTile.Invalid; pendingConfirmed = false; }

        static bool Chose(GlobalTargetInfo t)
        {
            string p = Problem(t, out PlanetTile dest, out float cost);
            if (p != null) { Messages.Message(p, MessageTypeDefOf.RejectInput, false); return false; }
            CompPilotConsole c = console;
            Pawn pilot = c.parent.Map.mapPawns.FreeColonistsSpawned.Where(CanPilot).OrderByDescending(x => x.skills.GetSkill(SkillDefOf.Intellectual).Level).FirstOrDefault();
            Precept_Ritual precept = pilot?.Ideo?.GetPrecept(PreceptDefOf.GravshipLaunch) as Precept_Ritual;
            if (precept == null) { Messages.Message("Could not open the launch ritual (no pilot or launch ritual found).", MessageTypeDefOf.RejectInput, false); return false; }
            ClearPending();
            pendingConsole = c; pendingSurface = t.Tile;
            CameraJumper.TryJump(c.parent);
            openingOurs = true;
            try
            {
                Window w = precept.GetRitualBeginWindow(c.parent, null, () => { pendingConfirmed = true; }, null, null, pilot);
                if (w == null) { ClearPending(); return true; }
                Find.WindowStack.Add(w);
            }
            finally { openingOurs = false; }
            return true;
        }

        public static bool CanPilot(Pawn p) { return p != null && !p.Downed && p.skills != null && !p.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled; }

        /// <summary>Called when the launch ritual has finished: fly to the orbit tile above the stored target. False = fall back to vanilla.</summary>
        public static bool Launch(CompPilotConsole c)
        {
            console = c; engine = EngineOf(c);
            below = OrbitalDropUtil.Below(c.parent.Map.Tile);
            PlanetTile target = pendingSurface;
            ClearPending();
            string p = Problem(new GlobalTargetInfo(target), out PlanetTile dest, out float cost);
            if (p != null) { Messages.Message("Move above target: " + p + " - choose a destination instead.", MessageTypeDefOf.RejectInput, false); return false; }
            // fuel: same proportional draw from every tank as vanilla ConsumeFuel, but with our ground-distance price
            float frac = Mathf.Clamp01(cost / engine.TotalFuel);
            foreach (CompGravshipFacility f in engine.GravshipComponents)
            {
                if (!f.CanBeActive || !f.Props.providesFuel) continue;
                CompRefuelable comp = f.parent.GetComp<CompRefuelable>();
                comp?.ConsumeFuel(comp.Fuel * frac);
            }
            if (engine.launchInfo == null) engine.launchInfo = new LaunchInfo { quality = 1f, doNegativeOutcome = false };
            engine.cooldownCompleteTick = GenTicks.TicksGame + (int)GravshipUtility.LaunchCooldownFromQuality(engine.launchInfo.quality);
            WorldComponent_GravshipController.DestroyTreesAroundSubstructure(engine.Map, engine.ValidSubstructure);
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            Find.GravshipController.InitiateTakeoff(engine, dest);
            SoundDefOf.Gravship_Launch.PlayOneShotOnCamera();
            return true;
        }

        public static Command Gizmo(CompPilotConsole c)
        {
            var cmd = new Command_Action
            {
                defaultLabel = "Move above target",
                defaultDesc = "Fly the gravship through orbit to the empty space directly above a point on the planet - an enemy base, a site or open land - " +
                              "ready for an orbital drop. Costs chemfuel like a flight of the same distance over the ground (not the 20x orbit cost) and starts the " +
                              "normal launch cooldown. Pick the target, then the normal launch ritual gathers the crew aboard; the ship arrives on an empty space map with only itself there.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/ViewOrbit", false) ?? BaseContent.BadTex,
                action = () => Start(c)
            };
            AcceptanceReport r = c.CanUseNow();
            if (!r.Accepted) cmd.Disable(r.Reason);
            else if (!c.parent.Map.mapPawns.FreeColonistsSpawned.Any(OrbitMove.CanPilot))
                cmd.Disable("Needs a colonist aboard who can pilot (not incapable of Intellectual).");
            return cmd;
        }
    }

    [HarmonyPatch(typeof(CompPilotConsole), nameof(CompPilotConsole.CompGetGizmosExtra))]
    public static class Patch_PilotConsoleOrbitMove
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompPilotConsole __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            if (__instance.parent.Spawned && __instance.parent.Faction == Faction.OfPlayer && OrbitalDropUtil.InOrbit(__instance.parent.Map) && OrbitMove.EngineOf(__instance) != null)
                yield return OrbitMove.Gizmo(__instance);
        }
    }

    /// <summary>The launch ritual finished: fly to the stored "Move above target" tile instead of opening the vanilla destination picker.</summary>
    [HarmonyPatch(typeof(CompPilotConsole), nameof(CompPilotConsole.StartChoosingDestination_NewTemp))]
    public static class Patch_OrbitMoveLaunch
    {
        public static bool Prefix(CompPilotConsole __instance, bool launching)
        {
            if (!launching || OrbitMove.pendingConsole != __instance || !OrbitMove.pendingConfirmed) return true;
            return !OrbitMove.Launch(__instance);
        }
    }

    /// <summary>A launch ritual opened the vanilla way (colonist told to pilot) forgets any stored target, so a normal launch stays normal.</summary>
    [HarmonyPatch(typeof(Precept_GravshipLaunch), nameof(Precept_GravshipLaunch.GetRitualBeginWindow))]
    public static class Patch_OrbitMoveVanillaLaunch
    {
        public static void Prefix() { if (!OrbitMove.openingOurs) OrbitMove.ClearPending(); }
    }
}
