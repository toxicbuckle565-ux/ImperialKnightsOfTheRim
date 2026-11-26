using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// KNIGHTS GUARD THEIR HOUSE (user 2026-11-25: Knight-house settlements had no Knights; "work with other settlement mods that expand or
    /// fortify them"): the base itself is left to whoever builds it (vanilla, Vanilla Base Generation Expanded, any fortification mod) - the
    /// Knights are added AFTER the whole map has been generated (postfix on every MapGenerator.GenerateMap overload), on open, unroofed ground
    /// in or right around the house's buildings. How many: the raid table (KnightRaidUtil.Pick) at the current threat points, at least one.
    /// They are ordinary house Knights: the seek AI keeps them with their people and sends them at intruders, and they ram out if walled in.
    /// </summary>
    public static class KnightSettlementGuard
    {
        const float MinPoints = 2500f;      // a castle always fields real Knights, not just an Armiger now and then
        const int MaxGuards = 4;

        public static void Garrison(Map map)
        {
            if (!(map?.Parent is Settlement s) || s.Faction == null || s.Faction.IsPlayer || !KnightHouseUtil.IsKnightHouse(s.Faction)) return;
            Faction f = s.Faction;

            // where the base is: the house's own buildings (whatever layout / mod made them)
            List<Building> own = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).OfType<Building>().Where(b => b.Faction == f).ToList();
            CellRect area = own.Count > 0 ? CellRect.FromLimits(own.Min(b => b.Position.x), own.Min(b => b.Position.z), own.Max(b => b.Position.x), own.Max(b => b.Position.z))
                                          : CellRect.CenteredOn(map.Center, 20);
            IntVec3 centre = area.CenterCell;
            float reach = Mathf.Min(Mathf.Max(area.Width, area.Height) * 0.5f + 18f, GenRadial.MaxRadialPatternRadius - 1f);

            float points = Mathf.Max(MinPoints, StorytellerUtility.DefaultThreatPointsNow(Find.AnyPlayerHomeMap ?? map));
            float spent;
            List<KnightClassDef> classes = KnightRaidUtil.Pick(f, points, out spent).Take(MaxGuards).ToList();
            if (classes.Count == 0)
            {
                KnightClassDef any = KnightHouseUtil.ClassesOf(f).FirstOrDefault(c => c.defName.Contains("Questoris")) ?? KnightHouseUtil.ClassesOf(f).FirstOrDefault();
                if (any != null) classes.Add(any);
            }

            var placed = new List<KeyValuePair<IntVec3, float>>();
            var names = new List<string>();
            foreach (KnightClassDef cls in classes)
            {
                VehiclePawn k = KnightHouseUtil.GenerateCrewedKnight(f, cls);
                if (k == null) continue;
                IntVec3 spot;
                if (!FindPost(k, map, centre, reach, placed, out spot)) { k.Destroy(); continue; }
                Rot4 face = Rot4.FromAngleFlat((spot - centre).AngleFlat);          // facing out from the keep
                GenSpawn.Spawn(k, spot, map, face, WipeMode.VanishOrMoveAside, false);
                k.GetComp<CompKnightHead>()?.Apply();
                placed.Add(new KeyValuePair<IntVec3, float>(spot, Mathf.Max(k.def.size.x, k.def.size.z)));
                names.Add(k.def.label);
            }
            if (Prefs.DevMode && names.Count > 0) Log.Message("[LIK] " + f.Name + " settlement '" + s.Label + "' is guarded by: " + string.Join(", ", names) + " (" + points.ToString("0") + " pts)");
        }

        /// <summary>A post where the whole Knight stands on open ground: standable, no building, no roof, apart from the other Knights.
        /// Searched outward from the middle of the base, so courtyards come first and the outside of the walls after.</summary>
        static bool FindPost(VehiclePawn v, Map map, IntVec3 centre, float reach, List<KeyValuePair<IntVec3, float>> placed, out IntVec3 spot)
        {
            spot = IntVec3.Invalid;
            int size = Mathf.Max(v.def.size.x, v.def.size.z);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(centre, reach, true))
            {
                if (!c.InBounds(map) || c.DistanceToEdge(map) < size) continue;
                if (placed.Any(p => p.Key.DistanceTo(c) < (p.Value + size) * 0.6f + 2f)) continue;
                CellRect r = GenAdj.OccupiedRect(c, Rot4.South, v.def.size);
                bool ok = true;
                foreach (IntVec3 x in r)
                {
                    if (!x.InBounds(map) || !x.Standable(map) || x.GetEdifice(map) != null || x.Roofed(map)) { ok = false; break; }
                    TerrainDef t = x.GetTerrain(map);
                    if (t == null || t.IsWater) { ok = false; break; }
                }
                if (!ok) continue;
                spot = c; return true;
            }
            return false;
        }
    }

    /// <summary>After any map is generated (every GenerateMap overload, whatever mod built the base): garrison Knight-house settlements.
    /// Deferred one tick so the map (and Vehicle Framework's map components) are fully set up before the Knights spawn.</summary>
    [HarmonyPatch]
    public static class Patch_KnightSettlementGuard
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(MapGenerator).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "GenerateMap" && m.ReturnType == typeof(Map)).Cast<MethodBase>();
        }

        static void Postfix(Map __result)
        {
            Map map = __result;
            if (!(map?.Parent is Settlement s) || !KnightHouseUtil.IsKnightHouse(s.Faction)) return;
            GameComponent_LIKDeferred.Next(() =>
            {
                try { if (Find.Maps.Contains(map)) KnightSettlementGuard.Garrison(map); }
                catch (System.Exception e) { Log.Error("[LIK] Knight settlement garrison failed: " + e); }
            });
        }
    }
}
