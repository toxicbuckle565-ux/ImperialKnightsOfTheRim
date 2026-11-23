using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// Acastus: the top rocket launcher (Ironstorm or Helios) has no picture of its own. A toggle raises it, which swaps the body picture for the
    /// matching "open launcher" picture (RetextureDef LIK_Open_acastus_ironstorm / _helios); toggling again restores the previous livery.
    /// </summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_RocketToggleGizmo
    {
        static readonly string[] launchers = { "ironstorm", "helios" };

        public static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer || __instance.VehicleDef == null || __instance.VehicleDef.defName != "LIK_Acastus") return;
            CompKnightHead comp = __instance.GetComp<CompKnightHead>();
            if (comp == null) return;
            __result = __result.Concat(Make(__instance, comp));
        }

        /// <summary>
        /// Called whenever the weapons change (hangar refit, dev refit): a raised launcher must show the launcher that is fitted NOW. Before this
        /// the body kept the old launcher's picture until the system was stowed and raised again (user 2026-11-11).
        /// </summary>
        public static void Refresh(VehiclePawn v)
        {
            if (v == null || !v.Spawned || v.VehicleDef == null || v.VehicleDef.defName != "LIK_Acastus") return;
            CompKnightHead comp = v.GetComp<CompKnightHead>();
            if (comp == null || !comp.rocketsOpen) return;
            string fitted = Fitted(v);
            RetextureDef open = fitted == null ? null : DefDatabase<RetextureDef>.GetNamedSilentFail("LIK_Open_acastus_" + fitted);
            if (open == null)
            {
                // no launcher any more: stow
                v.SetRetexture(comp.skinBeforeRockets);
                comp.skinBeforeRockets = null;
                comp.rocketsOpen = false;
            }
            else if (v.Retexture != open) v.SetRetexture(open);
        }

        public static bool Raised(VehiclePawn v)
        {
            CompKnightHead comp = v != null ? v.GetComp<CompKnightHead>() : null;
            return comp != null && comp.rocketsOpen;
        }

        static string Fitted(VehiclePawn v)
        {
            CompVehicleTurrets t = v.CompVehicleTurrets;
            if (t == null) return null;
            foreach (string l in launchers) if (t.GetTurret("top_acastus_" + l) != null) return l;
            return null;
        }

        static IEnumerable<Gizmo> Make(VehiclePawn v, CompKnightHead comp)
        {
            string fitted = Fitted(v);
            yield return new Command_Toggle
            {
                defaultLabel = "Rocket system",
                defaultDesc = "Raise or stow the top rocket launcher.\n\nRaised: the " + (fitted ?? "launcher") + " can fire, but the Knight walks at " +
                              Mathf.RoundToInt(Patch_RocketSpeed.RaisedSpeedFactor * 100f) + "% speed.\nStowed: full walking speed, the launcher cannot fire." +
                              (fitted == null ? " No rocket launcher is fitted." : ""),
                icon = ContentFinder<Texture2D>.Get("Things/Knights/Weapons/acastus/item_acastus_" + (fitted ?? "ironstorm"), false) ?? TexCommand.Attack,
                isActive = () => comp.rocketsOpen,
                toggleAction = () => Toggle(v, comp),
                Disabled = fitted == null && !comp.rocketsOpen,
                disabledReason = "No rocket launcher is fitted."
            };
        }

        /// <summary>Raise (true) or stow (false) the launcher; nothing happens if it already is, or none is fitted. True if it changed.</summary>
        public static bool SetRaised(VehiclePawn v, bool up)
        {
            if (v == null || v.VehicleDef == null || v.VehicleDef.defName != "LIK_Acastus") return false;
            CompKnightHead comp = v.GetComp<CompKnightHead>();
            if (comp == null || comp.rocketsOpen == up) return false;
            if (up && Fitted(v) == null) return false;
            Toggle(v, comp);
            return comp.rocketsOpen == up;
        }

        static void Toggle(VehiclePawn v, CompKnightHead comp)
        {
            if (comp.rocketsOpen)
            {
                v.SetRetexture(comp.skinBeforeRockets);
                comp.skinBeforeRockets = null;
                comp.rocketsOpen = false;
                return;
            }
            string fitted = Fitted(v);
            if (fitted == null) return;
            RetextureDef open = DefDatabase<RetextureDef>.GetNamedSilentFail("LIK_Open_acastus_" + fitted);
            if (open == null) { Log.Warning("[LIK] no open-launcher picture def for " + fitted); return; }
            comp.skinBeforeRockets = v.Retexture;
            v.SetRetexture(open);
            comp.rocketsOpen = true;
        }
    }

    /// <summary>
    /// User 2026-11-11: "have the knight have a faster walk speed when the rocket system is down compared to when it's up so players have a reason
    /// to have it down". With the launcher raised the Acastus is top-heavy and walks slower; stowed it walks at full speed.
    /// </summary>
    [HarmonyPatch(typeof(VehicleStatHandler), nameof(VehicleStatHandler.GetStatValue), new[] { typeof(VehicleStatDef) })]
    public static class Patch_RocketSpeed
    {
        public const float RaisedSpeedFactor = 0.7f;
        static readonly AccessTools.FieldRef<VehicleStatHandler, VehiclePawn> vehicleRef = AccessTools.FieldRefAccess<VehicleStatHandler, VehiclePawn>("vehicle");

        static void Postfix(VehicleStatHandler __instance, VehicleStatDef vehicleStatDef, ref float __result)
        {
            if (vehicleStatDef != VehicleStatDefOf.MoveSpeed) return;
            VehiclePawn v = vehicleRef(__instance);
            if (v == null || v.VehicleDef == null || v.VehicleDef.defName != "LIK_Acastus") return;
            if (Patch_RocketToggleGizmo.Raised(v)) __result *= RaisedSpeedFactor;
        }
    }
}
