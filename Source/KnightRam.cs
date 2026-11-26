using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// RAMMING (user 2026-11-25): an AI Knight that dropped into a base (or walked up to one) has no path out - Vehicle Framework never routes
    /// through walls. When it has somewhere to go (a target it cannot reach, its people, the map edge) and has stood stuck for a few seconds,
    /// it RAMS the wall in front of it, towards that goal: every ram is one heavy blow to the row of buildings across its whole width.
    /// A weak wall gives at the first ram; an armoured one (plasteel, a void base's walls) takes a few. Once a row is down it ploughs on
    /// into the gap and rams the next, until a normal path opens. It never rams natural rock (a mountain base stays a mountain), deep
    /// water, or buildings of its own side / friends (a friendly house Knight never breaks into YOUR base).
    /// </summary>
    public static class KnightRam
    {
        const int StuckChecks = 3;           // seek checks run every second: 3 s standing still with a goal = stuck
        const int RamTicks = 150;            // 2.5 s between rams (wind-up, blow, recover)
        const int BreachTicks = 900;         // after a ram / push it keeps going in breach mode for 15 s (no waiting for 'stuck' again)
        const int Depth = 3;                 // rows ahead looked at (a 2-cell thick wall + the gap behind)

        static readonly Dictionary<int, IntVec3> goal = new Dictionary<int, IntVec3>();
        static readonly Dictionary<int, KeyValuePair<IntVec3, int>> watch = new Dictionary<int, KeyValuePair<IntVec3, int>>();
        static readonly Dictionary<int, int> nextRam = new Dictionary<int, int>();
        static readonly Dictionary<int, int> breachUntil = new Dictionary<int, int>();

        /// <summary>Where the AI wants this Knight to get to (invalid = it is where it should be: fighting, holding, waiting).</summary>
        public static void SetGoal(VehiclePawn v, IntVec3 c)
        {
            if (c.IsValid) goal[v.thingIDNumber] = c; else goal.Remove(v.thingIDNumber);
        }

        /// <summary>Ram power of one blow, by class (wall hit points: wood ~195, steel 300, stone ~450-500, plasteel ~840).</summary>
        static float Power(VehiclePawn v)
        {
            string c = KnightLoadout.ClassFor(v.VehicleDef)?.defName ?? "";
            if (c.Contains("Armiger")) return 350f;
            if (c.Contains("Questoris")) return 600f;
            if (c.Contains("Cerastus")) return 650f;
            if (c.Contains("Dominus")) return 700f;
            if (c.Contains("Acastus")) return 900f;
            return 500f;
        }

        public static IntVec3 NearestEdge(VehiclePawn v, Map map)
        {
            IntVec3 p = v.Position;
            int dx0 = p.x, dx1 = map.Size.x - 1 - p.x, dz0 = p.z, dz1 = map.Size.z - 1 - p.z;
            int m = Mathf.Min(Mathf.Min(dx0, dx1), Mathf.Min(dz0, dz1));
            return m == dx0 ? new IntVec3(0, 0, p.z) : m == dx1 ? new IntVec3(map.Size.x - 1, 0, p.z) : m == dz0 ? new IntVec3(p.x, 0, 0) : new IntVec3(p.x, 0, map.Size.z - 1);
        }

        /// <summary>Called by the seek AI every check (before its own moves). True = the Knight is ramming / ploughing on (the AI stops there).</summary>
        public static bool Step(VehiclePawn v, Map map, System.Action<IntVec3> order)
        {
            int id = v.thingIDNumber, now = Find.TickManager.TicksGame;
            IntVec3 g;
            if (!goal.TryGetValue(id, out g) || v.Position.DistanceTo(g) < 8f) { watch.Remove(id); return false; }
            if (v.vehiclePather.Moving) { watch.Remove(id); return false; }

            KeyValuePair<IntVec3, int> w;
            int still = watch.TryGetValue(id, out w) && w.Key == v.Position ? w.Value + 1 : 0;
            watch[id] = new KeyValuePair<IntVec3, int>(v.Position, still);
            int until;
            bool breaching = breachUntil.TryGetValue(id, out until) && now < until;
            if (still < (breaching ? 1 : StuckChecks)) return false;

            // the side facing the goal first, then the two sides next to it (never straight back)
            Rot4 main = Rot4.FromAngleFlat((g - v.Position).AngleFlat);
            float ang = (g - v.Position).AngleFlat;
            var sides = new List<Rot4> { main, main.Rotated(RotationDirection.Clockwise), main.Rotated(RotationDirection.Counterclockwise) }
                .OrderBy(r => Mathf.Abs(Mathf.DeltaAngle(r.AsAngle, ang))).ToList();
            foreach (Rot4 rot in sides)
            {
                List<Thing> row;
                bool open;
                if (!Look(v, map, rot, out row, out open)) continue;          // rock / water / friendly buildings that way
                if (row != null && row.Count > 0)
                {
                    breachUntil[id] = now + BreachTicks;
                    int next;
                    if (nextRam.TryGetValue(id, out next) && now < next) return true;   // winding up for the next blow
                    nextRam[id] = now + RamTicks;
                    Ram(v, map, rot, row);
                    return true;
                }
                if (open)
                {
                    // the way ahead is clear now: plough on into the gap
                    IntVec3 to = v.Position + rot.FacingCell * 3;
                    if (to.InBounds(map) && v.DrivableRectOnCell(to, Ext_Vehicles.DestinationHitboxReq.AnyRotation))
                    {
                        breachUntil[id] = now + BreachTicks;
                        order(to);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The rows just ahead of the Knight on that side: the first row with something to ram, or 'open' if all rows are clear.
        /// False when that way is shut for good (natural rock, impassable ground, the map edge, buildings of its own side or friends).</summary>
        static bool Look(VehiclePawn v, Map map, Rot4 rot, out List<Thing> row, out bool open)
        {
            row = null; open = false;
            CellRect r = v.OccupiedRect();
            IntVec3 c = r.CenterCell;
            int half = Mathf.Max(v.def.size.x, v.def.size.z) / 2;
            for (int d = 1; d <= Depth; d++)
            {
                var found = new List<Thing>();
                for (int l = -half; l <= half; l++)
                {
                    IntVec3 x = rot == Rot4.North ? new IntVec3(c.x + l, 0, r.maxZ + d)
                              : rot == Rot4.South ? new IntVec3(c.x + l, 0, r.minZ - d)
                              : rot == Rot4.East ? new IntVec3(r.maxX + d, 0, c.z + l)
                              : new IntVec3(r.minX - d, 0, c.z + l);
                    if (!x.InBounds(map)) return false;
                    TerrainDef ter = x.GetTerrain(map);
                    if (ter == null || ter.passability == Traversability.Impassable) return false;
                    foreach (Thing t in x.GetThingList(map))
                    {
                        if (t is Building b)
                        {
                            if (b.def.passability == Traversability.Standable) continue;
                            if (b.def.building != null && b.def.building.isNaturalRock) return false;            // a mountain stays a mountain
                            if (b.Faction != null && !b.Faction.HostileTo(v.Faction)) return false;              // never its own / friends' walls
                            if (!b.def.useHitPoints) return false;                                              // indestructible
                            found.Add(b);
                        }
                        else if (t is Plant p && p.def.passability != Traversability.Standable) found.Add(p); // trees
                    }
                }
                if (found.Count > 0) { row = found.Distinct().ToList(); return true; }
            }
            open = true;
            return true;
        }

        static void Ram(VehiclePawn v, Map map, Rot4 rot, List<Thing> row)
        {
            try { if (v.Rotation != rot) v.Rotation = rot; } catch { }           // turn to face the wall
            float power = Power(v) * Rand.Range(0.85f, 1.15f);
            Vector3 hit = Vector3.zero;
            int n = 0;
            foreach (Thing t in row)
            {
                if (t.Destroyed) continue;
                hit += t.TrueCenter(); n++;
                t.TakeDamage(new DamageInfo(DamageDefOf.Blunt, power, 2f, rot.AsAngle, v));
            }
            if (n == 0) return;
            hit /= n;
            IntVec3 hc = hit.ToIntVec3();
            int size = Mathf.Max(v.def.size.x, v.def.size.z);
            // the blow: crash of metal on wall, the Knight's own heavy step, dust and sparks along the wall, a shake
            DefDatabase<SoundDef>.GetNamedSilentFail("BuildingDestroyed_Metal_Medium")?.PlayOneShot(new TargetInfo(hc, map));
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            if (cls != null) DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_step_" + cls.defName.Replace("LIK_Class_", ""))?.PlayOneShot(new TargetInfo(hc, map));
            if (hc.ShouldSpawnMotesAt(map))
            {
                foreach (Thing t in row.Take(8))
                {
                    FleckMaker.ThrowDustPuffThick(t.TrueCenter() + Gen.RandomHorizontalVector(0.6f), map, Rand.Range(1.5f, 2.5f), new Color(0.7f, 0.65f, 0.55f, 0.8f));
                    FleckMaker.ThrowMicroSparks(t.TrueCenter(), map);
                }
            }
            ShotFx.Shake(hit, map, 1f + size * 0.2f);
            FilthMaker.TryMakeFilth(hc, map, ThingDefOf.Filth_RubbleBuilding);
            if (Prefs.DevMode) Log.Message("[LIK] " + v.LabelShort + " rams " + row.Count + " thing(s) for " + power.ToString("0") + " (" + string.Join(", ", row.Where(t => !t.Destroyed).Select(t => t.LabelShort + " " + t.HitPoints + "/" + t.MaxHitPoints)) + ")");
        }
    }
}
