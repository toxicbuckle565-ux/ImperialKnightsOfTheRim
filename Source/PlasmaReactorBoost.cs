using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LIK
{
    /// <summary>
    /// The plasma reactor POWERS THE GRAV ENGINE (user 2026-11-22: it should act as the upgraded core): while one or two active reactors are
    /// linked to a grav engine, that engine's own substructure build circle grows (1 reactor: radius 45, 2: radius 55 - the vanilla engine
    /// is 18.9). The reactor's own 40-cell circle and its +3000 support still count. Applied where Odyssey decides the build area
    /// (GravshipUtility.InsideFootprint: placing substructure, valid / connected substructure, launch checks) and in the placement overlay.
    /// </summary>
    public static class ReactorBoost
    {
        public const string ReactorDef = "IMP_PlasmaReactor";
        public const float RadiusOne = 45f, RadiusTwo = 55f;
        static readonly Dictionary<int, KeyValuePair<int, int>> cache = new Dictionary<int, KeyValuePair<int, int>>();   // engine id -> (tick, reactors)

        /// <summary>Active plasma reactors linked to this engine (counted once per tick).</summary>
        public static int ActiveReactors(Building_GravEngine e)
        {
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(e.thingIDNumber, out var c) && c.Key == now) return c.Value;
            int n = 0;
            CompAffectedByFacilities fac = e.GetComp<CompAffectedByFacilities>();
            if (fac != null)
                foreach (Thing t in fac.LinkedFacilitiesListForReading)
                    if (t != null && t.def.defName == ReactorDef && t.Spawned && (t.TryGetComp<CompGravshipFacility>()?.CanBeActive ?? false)) n++;
            cache[e.thingIDNumber] = new KeyValuePair<int, int>(now, n);
            return n;
        }

        /// <summary>The engine's boosted build radius, or -1 without an active reactor.</summary>
        public static float Radius(Building_GravEngine e)
        {
            int n = ActiveReactors(e);
            if (n <= 0) return -1f;
            return UnityEngine.Mathf.Min(n >= 2 ? RadiusTwo : RadiusOne, GenRadial.MaxRadialPatternRadius - 1f);
        }

        /// <summary>A reactor came on or went off: the engines re-check their valid substructure and the overlay redraws.</summary>
        public static void MarkDirty(Map map)
        {
            if (map == null) return;
            cache.Clear();
            foreach (Building_GravEngine e in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>()) e.substructureDirty = true;
            map.substructureGrid?.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.InsideFootprint))]
    public static class Patch_ReactorBoostFootprint
    {
        static void Postfix(IntVec3 loc, Map map, Thing thingToExclude, ref bool __result)
        {
            if (__result || map == null || !ModsConfig.OdysseyActive) return;
            foreach (Building_GravEngine e in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (e == thingToExclude || !e.Spawned) continue;
                float r = ReactorBoost.Radius(e);
                if (r > 0f && loc.InHorDistOf(e.Position, r)) { __result = true; return; }
            }
        }
    }

    /// <summary>The placement overlay (where substructure may go) shows the boosted engine circle too.</summary>
    [HarmonyPatch(typeof(SubstructureGrid), nameof(SubstructureGrid.DrawSubstructureFootprint))]
    public static class Patch_ReactorBoostOverlay
    {
        static void Prefix(ref List<IntVec3> extraCells, Thing thingToExclude)
        {
            Map map = Find.CurrentMap;
            if (map == null || !ModsConfig.OdysseyActive) return;
            List<IntVec3> add = null;
            foreach (Building_GravEngine e in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (e == thingToExclude || !e.Spawned) continue;
                float r = ReactorBoost.Radius(e);
                if (r <= 0f) continue;
                if (add == null) add = extraCells != null ? new List<IntVec3>(extraCells) : new List<IntVec3>();
                add.AddRange(GenRadial.RadialCellsAround(e.Position, r, true));
            }
            if (add != null) extraCells = add;
        }
    }
}
