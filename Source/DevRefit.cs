using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// DEV tool (user 2026-11-11: "a dev mode to instantaneously swap to any weapons for the knights by pulling the hangar ui and having no cost to swap").
    /// With RimWorld's Development mode on, every player Knight has a "DEV: refit" button that opens the hangar screen for that Knight wherever it
    /// stands. Whatever is picked there is fitted at once, fully loaded, for free (Building_KnightHangar.DevRefit).
    /// </summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_KnightDevRefitGizmo
    {
        public static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!Prefs.DevMode || __instance.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(__instance.VehicleDef) == null) return;
            __result = __result.Concat(Make(__instance));
        }

        static IEnumerable<Gizmo> Make(VehiclePawn v)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: refit",
                defaultDesc = "Development mode only. Opens the hangar screen for this Knight right here: pick any weapon for any mount and it is fitted " +
                              "instantly, fully loaded, at no cost. No hangar or stock needed.",
                icon = TexCommand.ForbidOff,
                action = () =>
                {
                    Find.WindowStack.TryRemove(typeof(Window_ConfigureKnight), false);
                    Find.WindowStack.Add(new Window_ConfigureKnight(v));
                }
            };
        }
    }
}
