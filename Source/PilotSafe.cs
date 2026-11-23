using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// User 2026-11-11: "pawns dying in the knight from nearby explosions even though the knight is still fine".
    /// Vehicle Framework gives every hit on a crew cell a chance (30 % by default) to wound the pawn sitting there. A Knight's pilot sits in an
    /// armoured throne deep in the hull: nothing reaches them while the Knight stands. The role's chanceToHit is 0 in the defs, and this makes sure
    /// of it for any other route damage might take. What the pilot DOES suffer from damage to the Knight is the neural feedback (see Toll.cs).
    /// When the Knight is destroyed the pilot is thrown out and is as vulnerable as anyone.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_PilotSafeInKnight
    {
        static void Postfix(Pawn __instance, ref bool absorbed)
        {
            if (absorbed || __instance.Spawned) return;
            VehicleRoleHandler h = __instance.ParentHolder as VehicleRoleHandler;
            if (h == null || h.vehicle == null || h.vehicle.Destroyed) return;
            if (h.vehicle.def.GetModExtension<DefModExtension_KnightToll>() != null) absorbed = true;
        }
    }

    /// <summary>
    /// User 2026-11-12: "pawns still dying in the knight ... nothing shows in their log; they should be impervious till the Knight is destroyed".
    /// The likely killer: the piloting tolls (neural burnout -50% consciousness, machine-spirit feedback -25%, plus their pain) can drive
    /// consciousness to zero, which RimWorld treats as death with no damage entry. While a pawn sits in an intact Knight nothing kills them;
    /// the player is told what would have (once per pawn per in-game hour) so the cause is visible. Tolls still weaken the pilot.
    /// </summary>
    public static class PilotSafeUtil
    {
        public static bool InIntactKnight(Pawn p)
        {
            if (p == null || p.Spawned) return false;
            VehicleRoleHandler h = p.ParentHolder as VehicleRoleHandler;
            return h != null && h.vehicle != null && !h.vehicle.Destroyed && h.vehicle.def.GetModExtension<DefModExtension_KnightToll>() != null;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDead))]
    public static class Patch_PilotCannotDieInKnight
    {
        static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> pawnRef = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
        static readonly Dictionary<int, int> told = new Dictionary<int, int>();

        static void Postfix(Pawn_HealthTracker __instance, ref bool __result)
        {
            if (!__result || __instance.Dead) return;
            Pawn p = pawnRef(__instance);
            if (!PilotSafeUtil.InIntactKnight(p)) return;
            // user 2026-11-12: "I'd still like the neural strain death" - total neural burnout still kills inside the Knight
            foreach (Hediff h in __instance.hediffSet.hediffs)
                if (h.def == LIKDefOf.LIK_NeuralStrain && h.CauseDeathNow()) return;
            __result = false;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (told.TryGetValue(p.thingIDNumber, out int last) && now - last < 2500) return;
            told[p.thingIDNumber] = now;
            string cause = null;
            foreach (Hediff h in __instance.hediffSet.hediffs) if (h.CauseDeathNow()) { cause = h.LabelCap; break; }
            if (cause == null) { PawnCapacityDef cap = __instance.ShouldBeDeadFromRequiredCapacity(); if (cap != null) cause = "no " + cap.label + " left"; }
            if (cause == null) cause = "their wounds";
            Messages.Message(p.LabelShortCap + " would have died (" + cause + ") - the Knight's throne keeps the pilot alive while the Knight stands.", p, MessageTypeDefOf.NegativeHealthEvent, false);
            Log.Message("[LIK] Pilot " + p.LabelShort + " kept alive inside the Knight; would have died of: " + cause);
        }
    }

    /// <summary>Diagnostics: if a pilot still dies inside an intact Knight by some other route, say how.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_LogPilotDeathInKnight
    {
        static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            if (!PilotSafeUtil.InIntactKnight(__instance)) return;
            Log.Warning("[LIK] Pilot " + __instance.LabelShort + " died inside an intact Knight. Damage: " + (dinfo.HasValue ? dinfo.Value.ToString() : "none")
                + "; culprit: " + (exactCulprit != null ? exactCulprit.def.defName : "none") + " | " + System.Environment.StackTrace);
        }
    }
}
