using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// FIRES ON KNIGHTS BURN OUT (user 2026-11-25 screenshot: "fires aren't going out after whole in-game hours"). A fire attached to a pawn
    /// only ends when the pawn beats it out (JobGiver_ExtinguishSelf) or walks into water - a Knight (a Vehicle Framework vehicle) never does
    /// either, so it burned forever. Now a fire stuck on a Knight burns out by itself after BurnTicks (the flames on its armour die once the
    /// promethium / debris on it is spent); a Knight standing in fire can still catch again.
    /// </summary>
    public class MapComponent_KnightFire : MapComponent
    {
        const int Interval = 60;
        const int BurnMin = 600, BurnMax = 1200;                     // 10-20 s at x1
        readonly Dictionary<int, int> outAt = new Dictionary<int, int>();   // fire thingIDNumber -> tick it goes out

        public MapComponent_KnightFire(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % Interval != 0) return;
            List<Thing> fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire);
            if (fires.Count == 0) { if (outAt.Count > 0) outAt.Clear(); return; }
            var seen = new HashSet<int>();
            foreach (Fire f in fires.OfType<Fire>().ToList())
            {
                if (!(f.parent is VehiclePawn v) || v.def.GetModExtension<DefModExtension_KnightToll>() == null) continue;
                seen.Add(f.thingIDNumber);
                int t;
                if (!outAt.TryGetValue(f.thingIDNumber, out t)) { outAt[f.thingIDNumber] = now + Rand.Range(BurnMin, BurnMax); continue; }
                if (now >= t && !f.Destroyed) f.Destroy();
            }
            if (outAt.Count > seen.Count) foreach (int id in outAt.Keys.Where(k => !seen.Contains(k)).ToList()) outAt.Remove(id);
        }
    }
}
