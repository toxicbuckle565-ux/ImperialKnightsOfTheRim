using HarmonyLib;
using RimWorld;
using Verse;

namespace LIK
{
    /// <summary>
    /// ONE rotatable Knight hangar (user 2026-11-27: "why not just make it rotate like any other building but have the north view the same as
    /// the south view"). RimWorld swaps a building's width/length when it is turned sideways, but the sideways art needs its own 18x12 footprint
    /// (the south art is 20x11), so the hangar is still three ThingDefs: LIK_KnightHangar (south/north, drawn unrotated) and the hidden
    /// LIK_KnightHangarWest / LIK_KnightHangarEast (declared 12x18 -> 18x12 when placed facing west/east). While placing, the rotation keys
    /// and buttons turn the ghost as usual and this patch swaps the designator's def to the one for that rotation. The Architect menu shows
    /// only the original button (the side defs have canGenerateDefaultDesignator = false).
    /// </summary>
    [HarmonyPatch(typeof(Designator_Place), nameof(Designator_Place.SelectedUpdate))]
    public static class HangarPlacement
    {
        static readonly AccessTools.FieldRef<Designator_Build, BuildableDef> EntDef = AccessTools.FieldRefAccess<Designator_Build, BuildableDef>("entDef");
        static readonly AccessTools.FieldRef<Designator_Place, Rot4> PlacingRot = AccessTools.FieldRefAccess<Designator_Place, Rot4>("placingRot");

        static ThingDef ForRot(Rot4 r)
        {
            string n = r == Rot4.West ? "LIK_KnightHangarWest" : r == Rot4.East ? "LIK_KnightHangarEast" : "LIK_KnightHangar";
            return DefDatabase<ThingDef>.GetNamedSilentFail(n);
        }

        /// <summary>Make the designator place the hangar def that belongs to the current rotation.</summary>
        public static void Sync(Designator_Place d)
        {
            if (!(d is Designator_Build b)) return;
            ThingDef cur = EntDef(b) as ThingDef;
            if (cur == null || !HangarGeo.AllDefs.Contains(cur)) return;
            ThingDef want = ForRot(PlacingRot(d));
            if (want != null && want != cur) EntDef(b) = want;
        }

        static void Prefix(Designator_Place __instance) { Sync(__instance); }
    }

    /// <summary>Same sync right before a click places it (the rotation may have changed this frame).</summary>
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.DesignateSingleCell))]
    public static class HangarPlacementClick
    {
        static void Prefix(Designator_Build __instance) { HangarPlacement.Sync(__instance); }
    }

    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.CanDesignateCell))]
    public static class HangarPlacementCheck
    {
        static void Prefix(Designator_Build __instance) { HangarPlacement.Sync(__instance); }
    }
}
