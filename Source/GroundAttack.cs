using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// User 2026-11-11: firing at a single SPOT should be something you choose, not something that just happens. A Knight's weapons now accept
    /// only real targets (enemies, buildings, any thing) unless "Attack ground" is switched on for that Knight; then they can be aimed at any
    /// spot as before (bombarding a position, burning a wall down, area fire). Switching it off again drops every spot the weapons were aimed at.
    /// </summary>
    public static class GroundAttack
    {
        static int lastMessage = -9999;

        public static bool Allowed(VehiclePawn v)
        {
            CompKnightHead c = v != null ? v.GetComp<CompKnightHead>() : null;
            return c != null && c.groundAttack;
        }

        public static void Set(VehiclePawn v, bool on)
        {
            CompKnightHead c = v.GetComp<CompKnightHead>();
            if (c == null) return;
            c.groundAttack = on;
            if (on || v.CompVehicleTurrets == null) return;
            foreach (VehicleTurret t in v.CompVehicleTurrets.Turrets)
                if (t.targetInfo.IsValid && !t.targetInfo.HasThing) t.SetTarget(LocalTargetInfo.Invalid);
        }

        public static void Refused(VehiclePawn v)
        {
            if (v.Faction != Faction.OfPlayerSilentFail || Find.TickManager.TicksGame - lastMessage < 240) return;
            lastMessage = Find.TickManager.TicksGame;
            Messages.Message(v.LabelShort + ": switch on \"Attack ground\" to fire at a spot instead of a target.", v, MessageTypeDefOf.RejectInput, false);
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.SetTarget))]
    public static class Patch_GroundAttackOnly
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(VehicleTurret __instance, LocalTargetInfo target)
        {
            if (!target.IsValid || target.HasThing) return true;                 // clearing the target, or a real target: always fine
            VehiclePawn v = __instance.vehicle;
            if (v == null || KnightLoadout.ClassFor(v.VehicleDef) == null) return true;
            if (GroundAttack.Allowed(v)) return true;
            GroundAttack.Refused(v);
            return false;
        }
    }

    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_GroundAttackGizmo
    {
        public static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(__instance.VehicleDef) == null) return;
            if (__instance.GetComp<CompKnightHead>() == null) return;
            __result = __result.Concat(Make(__instance));
        }

        static IEnumerable<Gizmo> Make(VehiclePawn v)
        {
            yield return new Command_Toggle
            {
                defaultLabel = "Attack ground",
                defaultDesc = "Off: this Knight's weapons only accept real targets - enemies, buildings, objects. A click on empty ground does nothing, " +
                              "and no weapon keeps firing at a spot.\n\nOn: the weapons can also be aimed at any spot on the ground (bombard a position, " +
                              "burn through a wall, area fire). Switching it off again makes every weapon forget the spot it was aimed at.",
                icon = TexCommand.FireAtWill,
                isActive = () => GroundAttack.Allowed(v),
                toggleAction = () => GroundAttack.Set(v, !GroundAttack.Allowed(v))
            };
        }
    }
}
