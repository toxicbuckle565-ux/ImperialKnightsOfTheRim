using HarmonyLib;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// User 2026-11-13: "the lascannons should also be able to target through walls since they can shoot through them". Weapons whose shot burns
    /// through cover (DefModExtension_ShotFx.pierce > 0: lascannon, las-impulsor, twin magna lascannon, volcano lance) may pick and keep targets
    /// behind walls - in range and inside the gun's firing arc is enough. Vehicle Framework's target check otherwise demands a clear line of sight.
    /// The shot itself then burns through what is in the way (KnightShotUtil.BurnPath), as before.
    /// </summary>
    [HarmonyPatch(typeof(TargetingHelper), nameof(TargetingHelper.TargetMeetsRequirements),
        new[] { typeof(VehicleTurret), typeof(IntVec3), typeof(LocalTargetInfo), typeof(IntVec3) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    public static class Patch_PierceTargetsThroughWalls
    {
        public static bool Pierces(VehicleTurret t)
        {
            ThingDef p = t?.ProjectileDef;
            DefModExtension_ShotFx fx = p?.GetModExtension<DefModExtension_ShotFx>();
            return fx != null && fx.pierce > 0f;
        }

        // user 2026-11-25: "change the Acastus targeting through walls to about a third, but its range stays the same" - an Acastus gun picks
        // targets behind walls only within a third of its range; further out it needs a clear line like any gun (the shot still burns through).
        public const float AcastusBlindShare = 1f / 3f;

        /// <summary>May this burn-through gun pick a target this far away without a clear line of sight?</summary>
        public static bool PiercesAt(VehicleTurret t, Vector3 targetPos)
        {
            if (!Pierces(t)) return false;
            string dn = t.def?.defName;
            if (dn == null || !dn.StartsWith("LIK_Turret_acastus_")) return true;
            return (targetPos - t.TurretLocation).MagnitudeHorizontal() <= t.MaxRange * AcastusBlindShare;
        }

        static void Postfix(VehicleTurret turret, IntVec3 root, LocalTargetInfo target, ref IntVec3 goodDest, ref bool __result)
        {
            if (__result || !Pierces(turret) || !target.IsValid) return;
            VehiclePawn v = turret.vehicle;
            Map map = v?.Map;
            if (map == null || !target.Cell.InBounds(map)) return;
            if (target.HasThing)
            {
                Thing th = target.Thing;
                if (th == v || !th.Spawned || th.Destroyed || th.Map != map) return;
                if (th is Pawn p && (p.Dead)) return;
            }
            if (!turret.InRange(target) || !turret.AngleBetween(target.CenterVector3)) return;
            if (!PiercesAt(turret, target.CenterVector3)) return;                // Acastus: only the near third through walls
            goodDest = target.Cell;
            __result = true;
        }
    }
}
