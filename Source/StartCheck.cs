using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// User 2026-11-11: "make it say overweight when its overweight and cant start and also any other reason it wont be able to start".
    /// Vehicle Framework's start (draft) button only says "{0} is unable to move", and that line even overwrites its own fuel / pilot reasons.
    /// For Knights this lists every reason the Knight can't start, each with what to do about it.
    /// </summary>
    public static class StartCheck
    {
        const float MinMoveSpeed = 0.1f;       // VF's CanMove threshold
        const float CrippledEfficiency = 0.05f; // a movement part at or below this is treated as wrecked

        public static List<string> Reasons(VehiclePawn v)
        {
            var r = new List<string>();
            if (v == null) return r;

            // fuel
            CompFueledTravel fuel = v.CompFueledTravel;
            if (fuel != null && fuel.EmptyTank)
                r.Add("Out of fuel. Refuel it before starting.");

            // deployed / deploying weapons
            CompVehicleTurrets turrets = v.CompVehicleTurrets;
            if (turrets != null && (turrets.Deployed || turrets.Deploying))
                r.Add("Its weapons are deployed. Pack them up first.");

            // pilots
            if (!VehicleMod.settings.debug.debugDraftAnyVehicle && !v.HasEnoughOperators && v.handlers != null)
            {
                int need = LIKMod.I.requireSkill ? PilotingUtil.MinLevelFor(v) : 0;
                foreach (VehicleRoleHandler h in v.handlers)
                {
                    if (h?.role == null || (h.role.HandlingTypes & HandlingType.Movement) == 0 || h.RoleFulfilled) continue;
                    int slots = h.role.SlotsToOperate;
                    var crew = h.thingOwner.InnerListForReading;
                    int aboard = crew.Count;
                    int able = crew.Count(p => h.CanOperateRole(p));
                    string seat = h.role.label.NullOrEmpty() ? "pilot" : h.role.label;
                    if (aboard == 0)
                        r.Add("No pilot. The " + seat + " seat is empty" + (need > 0 ? " (needs Piloting " + need + " or higher)." : "."));
                    else if (able < slots)
                    {
                        var bad = crew.Where(p => !h.CanOperateRole(p)).ToList();
                        string who = string.Join(", ", bad.Select(p => p.LabelShortCap + Why(p, need)));
                        r.Add("The " + seat + " can't drive it: " + who + ".");
                    }
                }
            }

            // things that stop it moving at all
            if (v.movementStatus != VehicleMovementStatus.Online)
                r.Add("It is immobilised (" + v.movementStatus + ") and can't walk until that is cleared.");
            if ((v.MovementPermissions & VehiclePermissions.Mobile) == 0)
                r.Add("This Knight's frame isn't able to walk.");

            float speed = v.GetStatValue(VehicleStatDefOf.MoveSpeed);
            if (speed <= MinMoveSpeed)
            {
                bool explained = false;

                // overweight
                float cap = v.GetStatValue(VehicleStatDefOf.CargoCapacity);
                float mass = MassUtility.InventoryMass(v);
                if (cap > 0f && mass > cap)
                {
                    r.Add("Overweight: carrying " + mass.ToString("0") + " kg, cargo limit " + cap.ToString("0") + " kg. " +
                          "Unload at least " + (mass - cap).ToString("0") + " kg to get under the limit." + Heaviest(v));
                    explained = true;
                }

                // wrecked movement parts
                if (v.statHandler?.components != null)
                {
                    foreach (VehicleComponent c in v.statHandler.components)
                    {
                        if (c?.props?.categories == null || !c.props.categories.Contains(VehicleStatDefOf.MoveSpeed)) continue;
                        if (c.Efficiency > CrippledEfficiency) continue;
                        r.Add("Its " + (c.props.label ?? c.props.key) + " is too damaged to walk on (" + (c.HealthPercent * 100f).ToString("0") +
                              "% health). Repair it.");
                        explained = true;
                    }
                }

                if (turrets != null && (turrets.Deployed || turrets.Deploying)) explained = true;
                if (!explained)
                    r.Add("Its move speed is down to " + speed.ToString("0.##") + ". Check the move speed line on its info card (i) for the cause.");
            }
            else
            {
                // still mobile, but warn if close to the overweight limit
                float cap = v.GetStatValue(VehicleStatDefOf.CargoCapacity);
                float mass = MassUtility.InventoryMass(v);
                if (r.Count > 0 && cap > 0f && mass > cap)
                    r.Add("Also overweight (" + mass.ToString("0") + " / " + cap.ToString("0") + " kg), so it will walk slowly.");
            }
            return r;
        }

        static string Why(Pawn p, int need)
        {
            if (p.Downed) return " (downed)";
            if (p.Dead) return " (dead)";
            if (p.InMentalState) return " (mental break)";
            if (need > 0 && PilotingUtil.Level(p) < need) return " (Piloting " + PilotingUtil.Level(p) + ", needs " + need + ")";
            return " (not able to pilot)";
        }

        static string Heaviest(VehiclePawn v)
        {
            if (v.inventory?.innerContainer == null || v.inventory.innerContainer.Count == 0) return "";
            var top = v.inventory.innerContainer
                .OrderByDescending(t => t.GetStatValue(StatDefOf.Mass) * t.stackCount)
                .Take(3)
                .Select(t => t.LabelCap + " " + (t.GetStatValue(StatDefOf.Mass) * t.stackCount).ToString("0") + " kg");
            return " Heaviest cargo: " + string.Join(", ", top) + ".";
        }

        public static string Text(VehiclePawn v, List<string> reasons)
        {
            if (reasons.Count == 1) return "Can't start " + v.LabelShortCap + ": " + reasons[0];
            return "Can't start " + v.LabelShortCap + ":\n- " + string.Join("\n- ", reasons);
        }
    }

    /// <summary>Rewrites the disabled reason on VF's start button for Knights.</summary>
    [HarmonyPatch(typeof(VehicleIgnitionController), nameof(VehicleIgnitionController.GetGizmos))]
    public static class Patch_StartReasons
    {
        static readonly AccessTools.FieldRef<VehicleIgnitionController, VehiclePawn> vehicleRef =
            AccessTools.FieldRefAccess<VehicleIgnitionController, VehiclePawn>("vehicle");
        static readonly AccessTools.FieldRef<VehicleIgnitionController, Command_Toggle> cmdRef =
            AccessTools.FieldRefAccess<VehicleIgnitionController, Command_Toggle>("draftCommand");

        static void Postfix(VehicleIgnitionController __instance, ref IEnumerable<Gizmo> __result)
        {
            VehiclePawn v = vehicleRef(__instance);
            if (v == null || v.def.GetModExtension<DefModExtension_KnightToll>() == null) return;
            __result = Wrap(__instance, v, __result);
        }

        static IEnumerable<Gizmo> Wrap(VehicleIgnitionController ctl, VehiclePawn v, IEnumerable<Gizmo> gizmos)
        {
            foreach (Gizmo g in gizmos)
            {
                if (g != null && g.Disabled && ReferenceEquals(g, cmdRef(ctl)))
                {
                    List<string> reasons = StartCheck.Reasons(v);
                    if (reasons.Count > 0) g.disabledReason = StartCheck.Text(v, reasons);
                }
                yield return g;
            }
        }
    }

    /// <summary>The same detail when a start is refused by the hotkey or any other path that calls CanDraft.</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.CanDraft))]
    public static class Patch_CanDraftReasons
    {
        static void Postfix(VehiclePawn __instance, ref AcceptanceReport __result)
        {
            if (__result.Accepted || __instance.def.GetModExtension<DefModExtension_KnightToll>() == null) return;
            List<string> reasons = StartCheck.Reasons(__instance);
            if (reasons.Count > 0) __result = StartCheck.Text(__instance, reasons);
        }
    }
}
