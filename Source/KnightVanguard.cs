using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Vehicles;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace LIK
{
    /// <summary>
    /// THE KNIGHTS LEAD (user 2026-11-25: "want the knights to lead the troops, that way they are more likely to be targeted first"). While a
    /// hostile Knight house has Knights standing on the map, its attacking troops escort them (vanilla Escort duty on the nearest Knight of
    /// their house): they keep close behind it and fight whatever comes near, instead of charging ahead on their own. The Knights' seek AI
    /// drives the advance, so the Knights walk in front and draw the fire. When the last Knight falls the troops go back to a normal assault;
    /// a lord that breaks / leaves (exit duties) is never touched.
    /// </summary>
    public class MapComponent_KnightVanguard : MapComponent
    {
        const int Interval = 120;           // 2 s
        const float EscortRadius = 9f;
        readonly HashSet<int> escorting = new HashSet<int>();     // pawns we gave the escort duty (to hand them back to the assault)

        public MapComponent_KnightVanguard(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0) return;
            if (escorting.Count == 0 && !map.mapPawns.AllPawnsSpawned.Any(p => p is VehiclePawn)) return;
            var factions = new HashSet<Faction>();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                if (p.Faction != null && !p.Faction.IsPlayer && KnightHouseUtil.IsKnightHouse(p.Faction) && p.Faction.HostileTo(Faction.OfPlayer)) factions.Add(p.Faction);
            foreach (Faction f in factions) Lead(f);
            // pawns that left the map / died: forget them
            if (escorting.Count > 0) escorting.RemoveWhere(id => !map.mapPawns.AllPawnsSpawned.Any(p => p.thingIDNumber == id));
        }

        void Lead(Faction f)
        {
            List<Pawn> all = map.mapPawns.SpawnedPawnsInFaction(f);
            List<VehiclePawn> knights = all.OfType<VehiclePawn>().Where(k => !k.Dead && !k.Downed && KnightLoadout.ClassFor(k.VehicleDef) != null).ToList();
            foreach (Pawn p in all)
            {
                if (p is VehiclePawn || p.Dead || p.Downed || p.mindState == null) continue;
                Lord lord = p.GetLord();
                if (lord == null || !(lord.LordJob is LordJob_AssaultColony)) continue;          // only the raid's attackers
                string duty = p.mindState.duty?.def?.defName;
                if (duty != null && duty.StartsWith("ExitMap")) continue;                     // the raid broke / is leaving: leave them be
                if (knights.Count > 0)
                {
                    VehiclePawn lead = knights.MinBy(k => k.Position.DistanceToSquared(p.Position));
                    if (p.mindState.duty?.def == DutyDefOf.Escort && p.mindState.duty.focus.Thing == lead) continue;
                    p.mindState.duty = new PawnDuty(DutyDefOf.Escort, lead, EscortRadius);
                    escorting.Add(p.thingIDNumber);
                }
                else if (escorting.Remove(p.thingIDNumber) && p.mindState.duty?.def == DutyDefOf.Escort)
                {
                    p.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);              // no Knight left: back to the assault
                }
            }
        }
    }
}
