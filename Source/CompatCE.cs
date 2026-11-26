using System;
using HarmonyLib;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// Combat Extended (user 2026-11-24): Knights keep firing OUR projectiles so every shot effect (vaporise, conversion bands, graviton
    /// fields, harpoon, glow ...) keeps working; Compat/CombatExtended only rescales the numbers. But Vehicle Framework's FireTurret picks
    /// its path with `if (LaunchProjectileCE is null)` - a static delegate CE sets for EVERY turret - so with CE loaded our turrets went
    /// through CE_Utility.LaunchProjectileCE, which cast our projectile to ProjectileCE and threw (InvalidCastException on every shot).
    /// For Knight turrets only, the delegate is cleared for the duration of the shot and put back afterwards (finalizer: also on errors).
    /// Other mods' vehicles keep CE firing.
    /// </summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    [HarmonyPriority(Priority.First)]
    public static class Patch_KnightTurretVanillaUnderCE
    {
        static Func<ThingDef, ThingDef, Def, Vector2, LocalTargetInfo, VehiclePawn, float, float, float, float, object> saved;

        static void Prefix(VehicleTurret __instance, out bool __state)
        {
            __state = false;
            if (VehicleTurret.LaunchProjectileCE == null || __instance?.def == null || !__instance.def.defName.StartsWith("LIK_Turret_")) return;
            // 2026-11-26: Compat/CombatExtended now swaps Knight turret projectiles for CE ones (BulletCE / ProjectileCE_Explosive, incl.
            // our LIK_CE_Proj_*). Those MUST go through CE's launcher - the vanilla path casts them to Verse.Projectile and threw
            // InvalidCastException (Acastus autocannon/ironstorm). Only a projectile that really is a Verse.Projectile takes the vanilla path.
            ThingDef proj = __instance.loadedAmmo?.projectileWhenLoaded ?? __instance.def.projectile;
            if (proj?.thingClass == null || !typeof(Projectile).IsAssignableFrom(proj.thingClass)) return;
            saved = VehicleTurret.LaunchProjectileCE;
            VehicleTurret.LaunchProjectileCE = null;
            __state = true;
        }

        static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) { VehicleTurret.LaunchProjectileCE = saved; saved = null; }
            return __exception;
        }
    }
}
