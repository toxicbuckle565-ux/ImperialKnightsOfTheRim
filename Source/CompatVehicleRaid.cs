using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LIK
{
    /// <summary>
    /// VEHICLE RAID FRAMEWORK (gabrieel1482.raidvehicleframework) - hands off the Knights (Steam bug report 2026-11-25: "it doesn't work with
    /// Vehicle Raid Framework, the pilot doesn't spawn in the Knight"; user: "make it so the Knight mod doesn't interact with it, since we have
    /// the raids ourselves"). VRF builds enemy vehicle raids / settlements / ambushes from every vehicle kind it finds; a Knight needs a
    /// Throne-bonded pilot that only our Knight-house code makes, so VRF's Knights came out empty. Our Knights are removed from VRF's vehicle
    /// list (its settings / auto-fill) and from every candidate list it spawns from. Knight raids stay with the Knight houses (KnightRaids.cs).
    /// Nothing is patched when VRF is not loaded.
    /// </summary>
    public static class CompatVehicleRaid
    {
        public static bool IsKnightKind(PawnKindDef k)
        {
            return k?.race != null && k.race.GetModExtension<DefModExtension_KnightToll>() != null;
        }
    }

    [HarmonyPatch]
    public static class Patch_VRF_VehicleKinds
    {
        static MethodBase target;
        static bool Prepare()
        {
            System.Type t = AccessTools.TypeByName("VehicleRaidFramework.VRF_VehicleKindCache");
            target = t == null ? null : AccessTools.PropertyGetter(t, "AllVehicleKinds");
            return target != null;
        }
        static MethodBase TargetMethod() { return target; }
        static void Postfix(List<PawnKindDef> __result) { __result?.RemoveAll(CompatVehicleRaid.IsKnightKind); }
    }

    [HarmonyPatch]
    public static class Patch_VRF_Eligible
    {
        static MethodBase target;
        static bool Prepare()
        {
            System.Type t = AccessTools.TypeByName("VehicleRaidFramework.VRF_ContextVehicleCandidates");
            target = t == null ? null : AccessTools.Method(t, "GetEligible");
            return target != null;
        }
        static MethodBase TargetMethod() { return target; }

        // List<(PawnKindDef, VRF_NaturalRaidVehicleEntry, VehicleDef)> - the entry type is VRF's own, so the tuple is read by reflection
        static void Postfix(object __result)
        {
            if (!(__result is System.Collections.IList list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                object item = list[i];
                FieldInfo f = item?.GetType().GetField("Item1");
                if (f != null && CompatVehicleRaid.IsKnightKind(f.GetValue(item) as PawnKindDef)) list.RemoveAt(i);
            }
        }
    }
}
