using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    /// <summary>Marks a RecipeDef as "forge this Knight": the recipe makes no item, it spawns the vehicle.</summary>
    public class DefModExtension_KnightProduct : DefModExtension
    {
        public VehicleDef vehicleDef;
    }

    /// <summary>
    /// Finishing the forge bill creates the Knight vehicle (VehicleSpawner) on the nearest clear ground around the
    /// Mechanicum forge. The ingredients are consumed by the normal bill code before this runs.
    /// </summary>
    public class RecipeWorker_ForgeKnight : RecipeWorker
    {
        const float SearchRadius = 22f;

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            var ext = recipe.GetModExtension<DefModExtension_KnightProduct>();
            Map map = billDoer?.Map;
            if (ext?.vehicleDef == null || map == null)
            {
                Log.Error("[LIK] forge recipe " + recipe.defName + " has no vehicleDef or the worker has no map.");
                return;
            }
            Thing forge = billDoer.CurJob != null ? billDoer.CurJob.GetTarget(TargetIndex.A).Thing : null;
            IntVec3 origin = forge != null ? forge.Position : billDoer.Position;

            VehiclePawn knight = VehicleSpawner.GenerateVehicle(ext.vehicleDef, billDoer.Faction ?? Faction.OfPlayer);
            if (knight == null) { Log.Error("[LIK] could not generate " + ext.vehicleDef.defName); return; }

            IntVec2 size = ext.vehicleDef.size;
            Rot4 rot = Rot4.South;
            IntVec3 spot = IntVec3.Invalid;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(origin, SearchRadius, true))
            {
                if (FootprintClear(c, rot, size, map, forge)) { spot = c; break; }
            }
            if (!spot.IsValid)
            {
                spot = origin;
                Log.Warning("[LIK] no clear ground for the " + ext.vehicleDef.label + " near the forge; placing it on the forge (items may be moved aside).");
            }
            GenSpawn.Spawn(knight, spot, map, rot, WipeMode.VanishOrMoveAside, false);
            Messages.Message("The " + ext.vehicleDef.label + " rises from the forge, ready for a pilot and promethium.", knight, MessageTypeDefOf.PositiveEvent);
        }

        static bool FootprintClear(IntVec3 center, Rot4 rot, IntVec2 size, Map map, Thing forge)
        {
            CellRect rect = GenAdj.OccupiedRect(center, rot, size);
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map) || !cell.Walkable(map) || cell.Fogged(map)) return false;
                if (cell.GetEdifice(map) != null) return false;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Pawn || (forge != null && things[i] == forge)) return false;
            }
            return true;
        }
    }
}
