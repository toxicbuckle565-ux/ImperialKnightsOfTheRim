using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// "INFINITE EXPLOSIONS" (user 2026-11-25). VF's Reactor_Explosive rolls on EVERY damaging hit while the part is at or below its health
    /// threshold and still above 0. The blast is centred on the Knight, so it hits its own promethium tank / reactor again; Knight armour soaks
    /// most of it, the part never reaches 0, every hit re-rolls - an endless chain. For Knights the reactor now decides itself: each explosive part
    /// goes up at most ONCE, and never from the Knight's own blast. (Not saved: after a reload a part could go once more.)
    /// </summary>
    [HarmonyPatch(typeof(Reactor_Explosive), nameof(Reactor_Explosive.Hit))]
    public static class Patch_KnightExplosiveOnce
    {
        static readonly HashSet<string> exploded = new HashSet<string>();
        static readonly MethodInfo spawn = AccessTools.Method(typeof(Reactor_Explosive), "SpawnExploder");

        // parameters by position (VehiclePawn, VehicleComponent, ref DamageInfo, Penetration) - DLL-verified signature
        static bool Prefix(Reactor_Explosive __instance, VehiclePawn __0, VehicleComponent __1, ref DamageInfo __2)
        {
            VehiclePawn v = __0;
            if (v == null || v.def.GetModExtension<DefModExtension_KnightToll>() == null) return true;    // not a Knight: VF as usual
            if (!v.Spawned || __1 == null) return false;
            if (__2.Instigator == v) return false;                                                       // its own blast never chains
            string key = v.thingIDNumber + "|" + __1.props?.key;
            if (exploded.Contains(key)) return false;                                                    // already went up once
            if (__1.Health > 0f && __1.HealthPercent <= __instance.healthPercent && Rand.Chance(__instance.chance))
            {
                exploded.Add(key);
                if (Prefs.DevMode) Log.Message("[LIK] " + v.LabelShort + ": " + __1.props?.label + " explodes (once)");
                try { spawn?.Invoke(__instance, new object[] { v, __1 }); }
                catch (System.Exception e) { Log.ErrorOnce("[LIK] Knight explosive part failed: " + e, 0x4b5e01); }
            }
            return false;
        }
    }
}
