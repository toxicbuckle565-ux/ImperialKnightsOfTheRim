using System;
using HarmonyLib;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// 2026-11-25 (user: "all turrets are now centered at the middle of the Acastus" after loading a save): Vehicle Framework's
    /// VehicleTurret.PostPostLoadInit re-aims a turret that still had a target when the game was saved (AlignToTargetRestricted),
    /// which reads VehiclePawn.DrawPos before the Knight is set up -> NullReferenceException -> "Could not do PostLoadInit on Acastus Knight",
    /// and the rest of the turret load setup never runs (every weapon drawn at the Knight's centre). During loading (or while the Knight is
    /// not spawned) that re-aim is skipped: the turret simply re-aims on its next tick.
    /// </summary>
    [HarmonyPatch(typeof(VehicleTurret), "AlignToTargetRestricted")]
    public static class Patch_VFTurretAlignOnLoad
    {
        static bool Prefix(VehicleTurret __instance)
        {
            if (Scribe.mode != LoadSaveMode.Inactive) return false;
            VehiclePawn v = __instance?.vehicle;
            return v != null && v.Spawned;
        }

        static Exception Finalizer(Exception __exception, VehicleTurret __instance)
        {
            if (__exception == null) return null;
            // never let a failed re-aim break the turret (it re-aims on its next tick)
            if (__exception is NullReferenceException && (__instance?.vehicle == null || !__instance.vehicle.Spawned)) return null;
            return __exception;
        }
    }
}
