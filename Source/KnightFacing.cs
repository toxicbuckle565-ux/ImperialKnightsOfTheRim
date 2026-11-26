using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    /// <summary>
    /// FACE THE ENEMY + MELEE CHARGE (user 2026-11-25):
    ///  - "the knights always face away from enemies during combat - add a face enemy check that gets the max number of enemies within the
    ///    knight's turrets' fov": a Knight standing in a fight turns to the facing (N/E/S/W) whose weapon arcs (angleRestricted, same maths as
    ///    VehicleTurret / RangeShadow) cover the most enemy pawns in range - every gun that can bear on an enemy counts. It only turns when that
    ///    facing is clearly better, and at most every TurnTicks, so it does not twitch.
    ///  - "the reaper chain-cleaver never gets close enough to strike or sweep": a Knight with a melee arm (fitted or as the arm's other mode)
    ///    now closes to that arm's reach instead of standing at the edge of it.
    /// </summary>
    public static class KnightFacing
    {
        const int TurnTicks = 120;                                  // at most one turn every 2 s
        static readonly Dictionary<int, int> lastTurn = new Dictionary<int, int>();

        static float Clamp360(float a) { a %= 360f; return a < 0f ? a + 360f : a; }

        /// <summary>How many (gun, enemy) pairs can bear if the Knight faces rot.</summary>
        static int Score(VehiclePawn v, Rot4 rot, List<Pawn> foes, List<VehicleTurret> guns)
        {
            int n = 0;
            float baseAng = rot.AsAngle;
            foreach (VehicleTurret t in guns)
            {
                float max = t.MaxRange, min = t.def.minRange;
                Vector2 arc = t.angleRestricted;
                bool arcOn = arc != Vector2.zero && t.attachedTo == null;
                float a0 = 0f, span = 360f;
                if (arcOn)
                {
                    a0 = Clamp360(arc.x + baseAng);
                    span = Clamp360(arc.y + baseAng) - a0;
                    if (span < 0f) span += 360f;
                }
                foreach (Pawn p in foes)
                {
                    Vector3 d = (p.Position - v.Position).ToVector3();
                    float dist = d.MagnitudeHorizontal();
                    if (dist > max || dist < min) continue;
                    if (arcOn && Clamp360(d.AngleFlat() - a0) >= span) continue;
                    n++;
                }
            }
            return n;
        }

        /// <summary>Turn a standing Knight to the facing that brings the most guns onto the most enemies. True if it turned.</summary>
        public static bool FaceBest(VehiclePawn v, Map map)
        {
            if (v == null || !v.Spawned || v.vehiclePather == null || v.vehiclePather.Moving) return false;
            int now = Find.TickManager.TicksGame, last;
            if (lastTurn.TryGetValue(v.thingIDNumber, out last) && now - last < TurnTicks) return false;
            CompVehicleTurrets comp = v.CompVehicleTurrets;
            if (comp == null) return false;
            List<VehicleTurret> guns = comp.Turrets.Where(t => !t.TurretDisabled && t.MaxRange > 0f).ToList();
            if (guns.Count == 0) return false;
            float reach = guns.Max(t => t.MaxRange);
            var foes = new List<Pawn>();
            foreach (IAttackTarget at in map.attackTargetsCache.GetPotentialTargetsFor(v))
            {
                if (!(at.Thing is Pawn p) || !p.Spawned || p.Dead || p.Downed || at.ThreatDisabled(v) || !p.HostileTo(v)) continue;
                if (p.Position.InHorDistOf(v.Position, reach)) foes.Add(p);
            }
            if (foes.Count == 0) return false;
            Rot4 cur = v.Rotation;
            int have = Score(v, cur, foes, guns);
            Rot4 best = cur; int bestScore = have;
            foreach (Rot4 r in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                if (r == cur) continue;
                int s = Score(v, r, foes, guns);
                if (s > bestScore) { best = r; bestScore = s; }
            }
            // only a clear gain: at least one more gun on target, and not a coin-flip between equal sides
            if (best == cur || bestScore <= have) return false;
            try { v.Rotation = best; } catch { return false; }
            lastTurn[v.thingIDNumber] = now;
            return true;
        }

        /// <summary>The reach of the Knight's melee arm (fitted now, or the melee mode of an arm currently in gun mode); 0 = no melee arm.</summary>
        public static float MeleeReach(VehiclePawn v)
        {
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            CompVehicleTurrets comp = v.CompVehicleTurrets;
            if (cls == null || comp == null) return 0f;
            float reach = 0f;
            foreach (KnightMount m in cls.mounts)
                foreach (WeaponOption o in m.options)
                {
                    if (o == null || o.turretKey == null || (o.weapon != null && o.weapon.defName.StartsWith("LIK_VoidShield_"))) continue;   // the void shield is not a melee arm
                    bool fitted = comp.GetTurret(o.turretKey) != null || (o.altTurretKeys != null && o.altTurretKeys.Any(k => comp.GetTurret(k) != null));
                    if (!fitted) continue;
                    VehicleTurret def = comp.GetTurret(o.turretKey) ?? comp.Props.turrets.FirstOrDefault(t => t.key == o.turretKey);
                    ThingDef proj = def?.def?.projectile;
                    if (proj == null || !typeof(Projectile_KnightMelee).IsAssignableFrom(proj.thingClass)) continue;
                    if (comp.GetTurret(o.turretKey)?.TurretDisabled == true) continue;
                    float r = def.def.maxRange;
                    if (r > 0f && (reach == 0f || r < reach)) reach = r;
                }
            return reach;
        }
    }
}
