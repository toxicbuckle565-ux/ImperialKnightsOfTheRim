using System.Linq;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// KNIGHT DEATH (user 2026-11-25: "knights not destroying when the health goes down"). Vehicle Framework only kills a vehicle when its
    /// BodyIntegrity - the AVERAGE health of every part - drops below 1 %, so a gutted Knight kept standing. A Knight now falls when its plasma
    /// reactor is destroyed or its overall integrity drops below DeathIntegrity. The normal death path follows (meltdown 1 in 6, pilot, wreck).
    /// Two hooks share the check:
    ///  - VehicleStatHandler.ApplyDamage (postfix): every hit on a vehicle goes through it. Combat Extended REPLACES it (VehiclesCompat
    ///    VehicleArmorPatch writes part health directly and never calls VehicleComponent.TakeDamage) - a Harmony postfix still runs.
    ///    (2026-11-25: user's Dominus 7.7 % / Armiger 8.9 % under CE never died with only the part hook.)
    ///  - VehicleComponent.TakeDamage(VehiclePawn, ref DamageInfo, bool) (postfix): direct part damage without CE.
    /// </summary>
    public static class KnightDeath
    {
        public const float DeathIntegrity = 0.25f;

        public static void Check(VehiclePawn vehicle, DamageInfo dinfo)
        {
            if (vehicle == null || !vehicle.Spawned || vehicle.Destroyed || vehicle.Dead) return;
            if (vehicle.def.GetModExtension<DefModExtension_KnightToll>() == null) return;      // Knights only
            VehicleComponent reactor = null;
            try { reactor = vehicle.statHandler?.GetComponent("Reactor"); } catch { }
            bool reactorGone = reactor != null && reactor.Health <= 0f;
            float integrity = vehicle.GetStatValue(VehicleStatDefOf.BodyIntegrity);
            if (!reactorGone && integrity >= DeathIntegrity) return;
            if (Prefs.DevMode) Log.Message("[LIK] " + vehicle.LabelShort + " falls: " + (reactorGone ? "plasma reactor destroyed" : "integrity " + integrity.ToStringPercent()));
            try { vehicle.Kill(dinfo); }
            catch (System.Exception e) { Log.Error("[LIK] Knight death failed for " + vehicle.LabelShort + ": " + e); }
        }
    }

    [HarmonyPatch(typeof(VehicleStatHandler), nameof(VehicleStatHandler.ApplyDamage))]
    public static class Patch_KnightDeathOnHit
    {
        static readonly AccessTools.FieldRef<VehicleStatHandler, VehiclePawn> vehicleRef = AccessTools.FieldRefAccess<VehicleStatHandler, VehiclePawn>("vehicle");

        [HarmonyPriority(Priority.Last)]
        static void Postfix(VehicleStatHandler __instance, DamageInfo __0)
        {
            VehiclePawn v;
            try { v = vehicleRef(__instance); } catch { return; }
            KnightDeath.Check(v, __0);
        }
    }

    [HarmonyPatch]
    public static class Patch_KnightDeath
    {
        // VehicleComponent has two TakeDamage overloads (DLL-verified); the hit on a part is TakeDamage(VehiclePawn _, ref DamageInfo, bool)
        static System.Reflection.MethodBase TargetMethod()
        {
            return typeof(VehicleComponent).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == nameof(VehicleComponent.TakeDamage) && m.GetParameters().Length >= 2
                                     && m.GetParameters()[0].ParameterType == typeof(VehiclePawn)
                                     && m.GetParameters()[1].ParameterType == typeof(DamageInfo).MakeByRefType());
        }

        // parameters by position: VF names the vehicle parameter '_'
        static void Postfix(VehiclePawn __0, ref DamageInfo __1) { KnightDeath.Check(__0, __1); }
    }
}
