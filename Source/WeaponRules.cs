using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>Implemented by both Knight projectile classes: what may stop the shot on its way.</summary>
    public interface IKnightShot
    {
        bool KnightIntercept(IntVec3 c);
    }

    /// <summary>
    /// Knight guns sit several metres up: trees, chunks, sandbags and other low objects no longer stop a shot on its way (vanilla rolls a chance for
    /// every such thing the shot passes). Only solid walls / rock and (hostile) pawns in the path can. Cover next to the TARGET still works, because
    /// Vehicle Framework rolls that when the shot is fired.
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "CheckForFreeIntercept")]
    public static class Patch_KnightShotIntercept
    {
        static bool Prefix(Projectile __instance, IntVec3 c, ref bool __result)
        {
            IKnightShot k = __instance as IKnightShot;
            if (k == null) return true;
            __result = k.KnightIntercept(c);
            return false;
        }
    }

    public static partial class KnightShotUtil
    {
        /// <summary>Chance that a pawn standing in the path is hit instead (vanilla numbers); never for pawns that are not hostile to the shooter.</summary>
        public static float PawnInterceptChance(Pawn pawn, Thing launcher, Vector3 origin, IntVec3 c)
        {
            if (launcher?.Faction == null || pawn.Faction == null || !pawn.Faction.HostileTo(launcher.Faction)) return 0f;
            float f = 0.4f * Mathf.Clamp(pawn.BodySize, 0.1f, 2f);
            if (pawn.GetPosture() != PawnPosture.Standing) f *= 0.1f;
            return f * VerbUtility.InterceptChanceFactorFromDistance(origin, c);
        }

        public static bool SolidBlocker(Thing t)
        {
            if (t.def.Fillage != FillCategory.Full) return false;
            Building_Door door = t as Building_Door;
            return door == null || !door.Open;
        }

        // ------------------------------------------------------------------ piercing beams (lascannons)
        /// <summary>
        /// A las beam burns through what stands between the gun and the target: every wall, rock, tree or piece of cover on the line loses hit points
        /// from the beam's budget. What is burnt away lets the beam through; if the budget runs out the beam stops in that obstacle.
        /// Returns true when the beam was stopped (end = where).
        /// </summary>
        public static bool BurnPath(Thing launcher, Map map, Vector3 origin, Vector3 dest, Thing target, float budget, float power, out Vector3 end)
        {
            end = dest;
            IntVec3 a = origin.ToIntVec3(), b = dest.ToIntVec3();
            if (!a.InBounds(map) || !b.InBounds(map)) return false;
            CellRect own = launcher != null && launcher.Spawned ? launcher.OccupiedRect() : CellRect.Empty;
            List<IntVec3> cells = GenSight.BresenhamCellsBetween(a, b);
            var things = new List<Thing>();
            int fxDone = 0;   // effects only on the first 40 burnt cells of a very long tunnel
            bool meltSound = false;   // the hiss and crumble of the beam eating through, once per shot, where it first bites
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                if (c == b || !c.InBounds(map) || own.Contains(c)) continue;
                things.Clear();
                things.AddRange(c.GetThingList(map));
                bool burnt = false;
                foreach (Thing t in things)
                {
                    if (t == launcher || t == target || t is Pawn || t.Destroyed || !t.def.useHitPoints) continue;
                    if (t.def.category != ThingCategory.Building && t.def.category != ThingCategory.Plant && t.def.category != ThingCategory.Item) continue;
                    bool solid = SolidBlocker(t);
                    // low objects do not stop a Knight's shot (see Patch_KnightShotIntercept), so furniture, sandbags and chunks are left alone;
                    // only solid walls / rock and trees standing in the beam are burnt
                    if (!solid && (t.def.category != ThingCategory.Plant || t.def.fillPercent < 0.2f)) continue;
                    if (HomeProtection.Spares(launcher, t)) continue;      // 'Protect home' (user 2026-11-25): the beam passes through the colony's own walls unharmed
                    // user 2026-11-11: a lascannon goes through the thickest fortification - the beam is never stopped. Every obstacle takes
                    // 'budget' burn damage: ordinary walls and most rock are destroyed outright, the very toughest are left damaged and glowing.
                    float hp = t.HitPoints;
                    burnt = true;
                    if (hp <= budget)
                    {
                        t.TakeDamage(new DamageInfo(DamageDefOf.Burn, hp * 4f + 50f, 99f, -1f, launcher));
                        if (!t.Destroyed) t.Destroy(DestroyMode.KillFinalize);
                    }
                    else t.TakeDamage(new DamageInfo(DamageDefOf.Burn, budget, 99f, -1f, launcher));
                }
                if (burnt && fxDone < 40) { ShotFx.Melt(c, map, power, false); fxDone++; }
                if (burnt && !meltSound)
                {
                    meltSound = true;
                    SoundDef melt = DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_impact_las_melt_through");
                    if (melt != null) melt.PlayOneShot(new TargetInfo(c, map));
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ targets the player picked by hand for a piercing weapon
        static readonly Dictionary<VehicleTurret, LocalTargetInfo> manual = new Dictionary<VehicleTurret, LocalTargetInfo>();

        public static void NoteTarget(VehicleTurret t, LocalTargetInfo target)
        {
            if (target.IsValid && TurretTargeter.Turret == t) manual[t] = target;
            else manual.Remove(t);
        }

        public static bool IsManual(VehicleTurret t, LocalTargetInfo target)
        {
            LocalTargetInfo m;
            return manual.TryGetValue(t, out m) && m == target;
        }

        public static float PierceOf(VehicleTurret t)
        {
            ThingDef p = t?.ProjectileDef;
            DefModExtension_ShotFx fx = p?.GetModExtension<DefModExtension_ShotFx>();
            return fx == null ? 0f : fx.pierce;
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.SetTarget))]
    public static class Patch_NotePierceTarget
    {
        static void Postfix(VehicleTurret __instance, LocalTargetInfo target)
        {
            if (KnightShotUtil.PierceOf(__instance) > 0f) KnightShotUtil.NoteTarget(__instance, target);
        }
    }

    /// <summary>
    /// A piercing weapon (lascannon) may be aimed BY THE PLAYER at something it cannot see - the beam burns its way there. Automatic targeting keeps
    /// the normal line-of-sight rule, so a Knight never decides by itself to shoot through your own walls.
    /// </summary>
    [HarmonyPatch(typeof(TargetingHelper), nameof(TargetingHelper.TargetMeetsRequirements), new[] { typeof(VehicleTurret), typeof(IntVec3), typeof(LocalTargetInfo), typeof(IntVec3) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    public static class Patch_PierceIgnoresSight
    {
        static bool Prefix(VehicleTurret turret, LocalTargetInfo target, ref IntVec3 goodDest, ref bool __result)
        {
            if (turret?.vehicle == null || KnightShotUtil.PierceOf(turret) <= 0f) return true;
            if (TurretTargeter.Turret != turret && !KnightShotUtil.IsManual(turret, target)) return true;
            if (!target.IsValid || target.Thing == turret.vehicle || turret.vehicle.Map == null) return true;
            if (target.HasThing && (!target.Thing.Spawned || target.Thing.Destroyed)) return true;
            goodDest = target.Cell;
            if (!goodDest.InBounds(turret.vehicle.Map)) return true;
            __result = turret.InRange(target) && turret.AngleBetween(target.CenterVector3);
            return false;
        }
    }
}
