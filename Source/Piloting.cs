using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    [DefOf]
    public static class PilotingDefOf
    {
        public static SkillDef LIK_Piloting;
        public static JobDef LIK_SquireTraining;
        public static JobDef LIK_MentorSquire;
        public static JobDef LIK_RitualOfBecoming;
        public static HediffDef LIK_SquireReadiness;
        public static HediffDef LIK_ThroneBonded;
        public static HediffDef LIK_BecomingShock;
        public static HediffDef LIK_ThroneMadness;
        static PilotingDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(PilotingDefOf)); }
    }

    public static class PilotingUtil
    {
        // piloting-level curves (level 0..20)
        static readonly SimpleCurve TollCurve = new SimpleCurve { new CurvePoint(0f, 1.3f), new CurvePoint(8f, 1.0f), new CurvePoint(20f, 0.55f) };
        static readonly SimpleCurve SpeedCurve = new SimpleCurve { new CurvePoint(0f, 0.85f), new CurvePoint(8f, 1.0f), new CurvePoint(20f, 1.3f) };

        public static int Level(Pawn p)
        {
            if (p?.skills == null || PilotingDefOf.LIK_Piloting == null) return 0;
            return p.skills.GetSkill(PilotingDefOf.LIK_Piloting).Level;
        }

        /// <summary>The Knight this pawn is currently aboard (null if none / not one of ours).</summary>
        public static VehiclePawn KnightOf(Pawn p)
        {
            VehiclePawn v = p?.GetVehicle();
            return v != null && v.def.GetModExtension<DefModExtension_KnightToll>() != null ? v : null;
        }

        /// <summary>Multiplier on strain / feedback / ageing from the pilot's skill: x1.3 untrained, x1.0 at 8, x0.55 at 20 (scaled by the setting).</summary>
        public static float TollFactor(Pawn p)
        {
            float k = LIKMod.I.skillEffect;
            if (k <= 0f) return 1f;
            return Mathf.Lerp(1f, TollCurve.Evaluate(Level(p)), k);
        }

        public static float SpeedFactor(Pawn p)
        {
            float k = LIKMod.I.skillEffect;
            if (k <= 0f) return 1f;
            return Mathf.Lerp(1f, SpeedCurve.Evaluate(Level(p)), k);
        }

        /// <summary>Chance that a Knight's shot is aimed on target, from the pilot's Piloting level: 30% untrained, rising evenly to 95% at level 20.
        /// Weather, smoke, cover and target size still apply on top (Vehicle Framework's shot report).</summary>
        public static float HitChance(Pawn p)
        {
            if (LIKMod.I.skillEffect <= 0f) return 1f;   // skill effects switched off: Vehicle Framework's default (always on target)
            return Mathf.Lerp(0.30f, 0.95f, Mathf.Clamp01(Level(p) / 20f));
        }

        /// <summary>The best pilot aboard decides the gunnery.</summary>
        public static float HitChance(VehiclePawn v)
        {
            Pawn best = null;
            foreach (Pawn p in TollUtil.Pilots(v))
                if (best == null || Level(p) > Level(best)) best = p;
            return HitChance(best);
        }

        public static int MinLevelFor(VehiclePawn v)
        {
            var ext = v?.def.GetModExtension<DefModExtension_KnightToll>();
            return ext == null ? 0 : ext.minPiloting;
        }
    }

    // ------------------------------------------------------------------ patches
    /// <summary>Knight gunnery = the pilot's Piloting skill. Vehicle Framework fires turrets without a shooter (its 'shooter ability' factor is always 100%),
    /// so the factor is set here for Knights. Needs the turret fire modes to have NO spreadRadius (that would skip the hit roll entirely).</summary>
    [HarmonyPatch(typeof(TurretShotReport), nameof(TurretShotReport.HitReportFor))]
    public static class Patch_PilotingAccuracy
    {
        static readonly AccessTools.StructFieldRef<TurretShotReport, float> ShooterFactor =
            AccessTools.StructFieldRefAccess<TurretShotReport, float>("factorFromShooterAndDist");

        static void Postfix(VehiclePawn vehicle, VehicleTurret turret, ref TurretShotReport __result)
        {
            if (vehicle == null || vehicle.def.GetModExtension<DefModExtension_KnightToll>() == null) return;
            if (turret?.CurrentFireMode == null || !turret.CurrentFireMode.canMiss) return;
            ShooterFactor(ref __result) = PilotingUtil.HitChance(vehicle);
        }
    }

    /// <summary>Everyone starts at piloting 0 (the random 0-4 start would let strangers pilot a Knight); it has to be trained.</summary>
    [HarmonyPatch(typeof(PawnGenerator), "FinalLevelOfSkill")]
    public static class Patch_StartingPilotingLevel
    {
        static void Postfix(SkillDef sk, ref int __result)
        {
            if (sk == PilotingDefOf.LIK_Piloting) __result = 0;
        }
    }

    /// <summary>A Knight needs a minimum piloting level to be driven.</summary>
    [HarmonyPatch(typeof(VehicleRoleHandler), nameof(VehicleRoleHandler.CanOperateRole), new[] { typeof(Pawn) })]
    public static class Patch_PilotingRequirement
    {
        static void Postfix(VehicleRoleHandler __instance, Pawn pawn, ref bool __result)
        {
            if (!__result || !LIKMod.I.requireSkill) return;
            if ((__instance.role.HandlingTypes & HandlingType.Movement) == 0) return;
            if (PilotTraining.NeedsThrone(__instance.vehicle) && !PilotTraining.Bonded(pawn))
            {
                __result = false;
                JobFailReason.Is("has not undergone the Ritual of Becoming");
                return;
            }
            int need = PilotingUtil.MinLevelFor(__instance.vehicle);
            if (need <= 0) return;
            if (PilotingUtil.Level(pawn) < need)
            {
                __result = false;
                JobFailReason.Is("needs piloting " + need + " (has " + PilotingUtil.Level(pawn) + ")");
            }
        }
    }

    /// <summary>Skilled pilots make the Knight faster (and clumsy ones slower).</summary>
    [HarmonyPatch(typeof(VehicleStatHandler), nameof(VehicleStatHandler.GetStatValue), new[] { typeof(VehicleStatDef) })]
    public static class Patch_PilotingSpeed
    {
        static readonly AccessTools.FieldRef<VehicleStatHandler, VehiclePawn> vehicleRef = AccessTools.FieldRefAccess<VehicleStatHandler, VehiclePawn>("vehicle");

        static void Postfix(VehicleStatHandler __instance, VehicleStatDef vehicleStatDef, ref float __result)
        {
            if (vehicleStatDef != VehicleStatDefOf.MoveSpeed || LIKMod.I.skillEffect <= 0f) return;
            VehiclePawn v = vehicleRef(__instance);
            if (v == null || v.def.GetModExtension<DefModExtension_KnightToll>() == null) return;
            List<Pawn> pilots = TollUtil.Pilots(v);
            if (pilots.Count == 0) return;
            __result *= PilotingUtil.SpeedFactor(pilots[0]);
        }
    }
}
