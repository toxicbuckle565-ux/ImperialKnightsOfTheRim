using System.Reflection;
using HarmonyLib;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// Camera+ compatibility (user 2026-11-22: "all Knights' bodies disappear when zoomed far out"). Camera+ (brrainz.cameraplus) shows a dot
    /// marker for pawns when zoomed out and hides the pawn itself - for Vehicle Framework vehicles it skips VehicleRenderer's Draw phase, which
    /// draws only the BODY, so the carapace, pauldrons and weapons stayed while the body vanished. Knights are huge and easy to see from far
    /// away: they never get a Camera+ marker, so they are never hidden. Applied only when Camera+ is loaded.
    /// </summary>
    public static class CameraPlusCompat
    {
        public static bool IsKnight(Pawn p)
        {
            return p is VehiclePawn v && v.VehicleDef != null && v.VehicleDef.defName.StartsWith("LIK_");
        }
    }

    [HarmonyPatch]
    public static class Patch_CameraPlus_NoKnightMarker
    {
        static MethodBase target;
        static bool Prepare() { target = AccessTools.Method("CameraPlus.DotTools:ShouldShowMarker"); return target != null; }
        static MethodBase TargetMethod() { return target; }
        static void Postfix(Pawn pawn, ref bool __result) { if (__result && CameraPlusCompat.IsKnight(pawn)) __result = false; }
    }

    [HarmonyPatch]
    public static class Patch_CameraPlus_RenderKnight
    {
        static MethodBase target;
        static bool Prepare() { target = AccessTools.Method("CameraPlus.DotTools:ShouldRenderPawn"); return target != null; }
        static MethodBase TargetMethod() { return target; }
        static void Postfix(Pawn pawn, ref bool __result) { if (!__result && CameraPlusCompat.IsKnight(pawn) && !pawn.Dead) __result = true; }
    }
}
