using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SmashTools;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// The top missile launchers are fixed in the hull (user 2026-11-10: "Knight turns itself"): they accept a target in any direction, but before a
    /// salvo is launched at a target that is not in front, the Knight turns on the spot to face it. While the Knight is walking the launcher holds
    /// its fire until the target is in front or the Knight has stopped and turned.
    /// </summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.PushTurretToQueue))]
    public static class Patch_MissileFacing
    {
        // hull-fixed launchers are recognised by what they fire
        static readonly HashSet<string> launcherProjectiles = new HashSet<string> { "LIK_Proj_missile", "LIK_Proj_ironstorm16", "LIK_Proj_stormspear", "LIK_Proj_helios", "LIK_Proj_shieldbreaker" };

        static int lastStowedMessage = -9999;

        public static bool IsHullLauncher(VehicleTurret t)
        {
            return t?.def?.projectile != null && launcherProjectiles.Contains(t.def.projectile.defName);
        }

        static bool Prefix(VehicleTurret __instance)
        {
            VehiclePawn v = __instance.vehicle;
            if (v == null || !v.Spawned || !IsHullLauncher(__instance)) return true;
            if (v.def.GetModExtension<DefModExtension_KnightToll>() == null) return true;
            // Acastus: the top launcher fires only while it is raised (stowed = full walking speed, see Patch_RocketSpeed)
            if (v.VehicleDef != null && v.VehicleDef.defName == "LIK_Acastus" && !Patch_RocketToggleGizmo.Raised(v))
            {
                if (v.Faction == RimWorld.Faction.OfPlayerSilentFail && Find.TickManager.TicksGame - lastStowedMessage > 600)
                {
                    lastStowedMessage = Find.TickManager.TicksGame;
                    Messages.Message(v.LabelShort + ": raise the rocket system to fire the launcher.", v, RimWorld.MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }
            LocalTargetInfo target = __instance.targetInfo;
            if (!target.IsValid) return true;

            Vector3 to = target.CenterVector3 - v.DrawPos;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return true;
            Rot4 want = Rot4.FromAngleFlat(to.AngleFlat());
            if (v.Rotation == want) return true;                              // target is in the front quarter: launch
            if (v.vehiclePather != null && v.vehiclePather.Moving) return false;   // walking: hold fire, try again next tick
            if (!v.CellRectStandable(v.Map, v.Position, want)) return true;    // no room to turn here (footprint would hit something): launch anyway
            v.FullRotation = new Rot8(want);
            return false;                                                     // turned; the launcher re-aims and launches on a following tick
        }
    }
}
