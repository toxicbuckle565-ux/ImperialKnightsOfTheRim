using HarmonyLib;
using SmashTools;
using SmashTools.Rendering;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using Verse;

namespace LIK
{
    /// <summary>
    /// 8-direction facing with REAL diagonal pictures (2026-11-16, Dominus proof of concept; user: "the diagonal views seem messed up and
    /// facing the wrong way"). VF makes a diagonal out of a side view by turning it 45 degrees: the vehicle keeps a +-45 angle and both the
    /// body picture and every turret offset are rotated by it. A Knight with its own _northEast/_southEast/_southWest/_northWest pictures
    /// and its own diagonal mount positions (Tools/diag_mounts.py) is already drawn that way, so the extra turn is taken out again.
    /// </summary>
    public static class Diagonal
    {
        public static bool Real(Graphic_Rgb g)
        {
            return g != null && g.path != null && g.path.StartsWith("Things/Knights/") && !g.DiagonalRotated;
        }

        public static bool Real(VehiclePawn v)
        {
            return v != null && v.VehicleDef != null && v.VehicleDef.defName.StartsWith("LIK_") && v.VehicleGraphic is Graphic_Rgb g && Real(g);
        }
    }

    [HarmonyPatch]
    public static class Patch_DiagonalBodyNoTurn
    {
        static System.Reflection.MethodBase TargetMethod() { return AccessTools.Method(typeof(Graphic_Rgb), "ParallelGetPreRenderResults"); }

        static void Postfix(Graphic_Rgb __instance, ref TransformData transformData, ref PreRenderResults __result)
        {
            if (!__result.valid || !transformData.orientation.IsDiagonal || !Diagonal.Real(__instance)) return;
            __result.quaternion = Quaternion.identity;
        }
    }

    [HarmonyPatch]
    public static class Patch_DiagonalTurretNoTurn
    {
        static System.Reflection.MethodBase TargetMethod() { return AccessTools.Method(typeof(VehicleTurret.TurretDrawData), "DrawOffset"); }

        static void Prefix(VehicleTurret.TurretDrawData __instance, Rot8 rot, ref float rotation)
        {
            if (!rot.IsDiagonal) return;
            VehicleTurret t = Traverse.Create(__instance).Field("turret").GetValue<VehicleTurret>();
            if (t == null || t.attachedTo != null || !Diagonal.Real(t.vehicle)) return;
            rotation = 0f;
        }
    }
}
