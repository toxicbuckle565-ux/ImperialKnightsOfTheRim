using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// User 2026-11-11: "when a pawn isnt skilled enough to pilot a knight the pop up shows pawn isnt capable of violence".
    /// Vehicle Framework's board menu always uses "{0} is incapable of violence" for ANY refusal, and the seat dialogs say only
    /// "incapable of fulfilling this role". For Knights both now give the real reason.
    /// </summary>
    public static class BoardReasons
    {
        public static bool IsKnight(VehiclePawn v) => v != null && v.def.GetModExtension<DefModExtension_KnightToll>() != null;

        /// <summary>Why this pawn can't take this seat (null if it can, or the reason is unknown).</summary>
        public static string Why(VehicleRoleHandler h, Pawn p)
        {
            if (h?.role == null || p == null) return null;
            string n = p.LabelShortCap;
            if (p.Dead) return n + " is dead";
            if (p.Downed) return n + " is downed";
            if (p.InMentalState) return n + " is having a mental break";
            if (p.IsPrisoner) return n + " is a prisoner";
            if (p.IsColonyMech) return "Mechanoids can't pilot a Knight";
            if (!p.RaceProps.ToolUser) return n + " can't operate machinery";
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)) return n + " is not conscious enough";
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) return n + " can't use their hands (no manipulation)";
            if (p.Faction != h.vehicle.Faction) return n + " is not of this Knight's faction";
            HandlingType ht = h.role.HandlingTypes;
            if ((ht & HandlingType.Turret) != 0 && p.WorkTagIsDisabled(WorkTags.Violent)) return n + " is incapable of violence (this seat fires the weapons)";
            if ((ht & HandlingType.Movement) != 0 && LIKMod.I.requireSkill)
            {
                if (PilotTraining.NeedsThrone(h.vehicle) && !PilotTraining.Bonded(p))
                    return n + " has not undergone the Ritual of Becoming: a Throne Mechanicum only accepts a Throne-bonded Noble (perform the Ritual at a Knight hangar)";
                int need = PilotingUtil.MinLevelFor(h.vehicle), has = PilotingUtil.Level(p);
                if (need > 0 && has < need)
                    return n + " isn't skilled enough: Piloting " + has + ", this Knight needs " + need + ". Train as a squire under a Knight Preceptor at a Knight hangar";
            }
            return null;
        }

        // last refusal seen, so the seat-dialog message can be rewritten
        internal static Pawn lastPawn; internal static string lastWhy; internal static int lastFrame = -1;
    }

    /// <summary>Remember why a Knight seat refused a pawn.</summary>
    [HarmonyPatch(typeof(VehicleRoleHandler), nameof(VehicleRoleHandler.CanOperateRole), new[] { typeof(Pawn) })]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_RecordSeatRefusal
    {
        static void Postfix(VehicleRoleHandler __instance, Pawn pawn, bool __result)
        {
            if (__result || !BoardReasons.IsKnight(__instance.vehicle)) return;
            string w = BoardReasons.Why(__instance, pawn);
            if (w == null) return;
            BoardReasons.lastPawn = pawn; BoardReasons.lastWhy = w; BoardReasons.lastFrame = UnityEngine.Time.frameCount;
        }
    }

    /// <summary>Right-click "Cannot board ... is incapable of violence" -> the real reason.</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetFloatMenuOptions))]
    public static class Patch_BoardMenuReason
    {
        static void Postfix(VehiclePawn __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            if (!BoardReasons.IsKnight(__instance) || selPawn == null) return;
            __result = Fix(__instance, selPawn, __result);
        }

        static IEnumerable<FloatMenuOption> Fix(VehiclePawn v, Pawn p, IEnumerable<FloatMenuOption> opts)
        {
            string generic = "VF_BoardFailureNonCombatant".Translate(p.LabelShort);
            foreach (FloatMenuOption o in opts)
            {
                if (o != null && o.Disabled && o.Label != null && o.Label.Contains(generic))
                {
                    foreach (VehicleRoleHandler h in v.handlers)
                    {
                        string fail = "VF_BoardVehicleGroupFail".Translate(h.role.label, generic);
                        if (o.Label != fail) continue;
                        string why = BoardReasons.Why(h, p);
                        if (why != null) o.Label = "VF_BoardVehicleGroupFail".Translate(h.role.label, why);
                        break;
                    }
                }
                yield return o;
            }
        }
    }

    /// <summary>Seat dialogs: "X is incapable of fulfilling this role." -> the real reason.</summary>
    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) })]
    public static class Patch_SeatMessageReason
    {
        static void Prefix(ref string text)
        {
            if (BoardReasons.lastPawn == null || UnityEngine.Time.frameCount - BoardReasons.lastFrame > 2) return;
            if (text == "VF_IncapableStatusForRole".Translate(BoardReasons.lastPawn.LabelShortCap).ToString())
                text = BoardReasons.lastWhy + ".";
        }
    }
}
