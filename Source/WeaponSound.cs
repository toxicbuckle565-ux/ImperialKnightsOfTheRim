using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// Firing sound of a Knight weapon turret (on every LIK_Turret_* def, written by Tools/weapon_sounds.py + gen_assembly.py).
    /// Vehicle Framework plays VehicleTurretDef.shotSound on EVERY bullet; the Knight sounds are real recordings that are often a whole burst
    /// (a gatling burst, a ripple of four missiles), so the turrets carry no shotSound and this decides when a recording starts.
    ///
    /// CHARGE-UP (user 2026-11-11: "have the charge up built into the weapons and also have charge up effects"): when chargeTicks > 0 the weapon
    /// does not fire the moment it is ready. The recording (which begins with the weapon charging) starts, the muzzle gathers energy
    /// (ShotFx.ChargeTick) and the shot leaves chargeTicks later - exactly where the discharge is in the recording.
    /// </summary>
    public class DefModExtension_ShotSound : DefModExtension
    {
        public SoundDef sound;
        public bool perBurst;            // the recording is a whole burst: start it once per burst, not per bullet
        public int minGapTicks;          // never start it more often than this (melee: strikes come faster than the recording is long)
        public List<BurstSound> byBurst; // fire-mode dependent recordings (Ironstorm: single / burst of 4 / whole silo), matched by shots per burst
        public int chargeTicks;          // > 0: charge-up before every burst (delay of the first shot); the recording starts with the charge
    }

    public class BurstSound
    {
        public int shots;
        public SoundDef sound;
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.PostTurretFire))]
    public static class Patch_KnightShotSound
    {
        class State { public int last = -999999; }
        static readonly ConditionalWeakTable<VehicleTurret, State> states = new ConditionalWeakTable<VehicleTurret, State>();

        static void Postfix(VehicleTurret __instance)
        {
            DefModExtension_ShotSound ext = __instance.def != null ? __instance.def.GetModExtension<DefModExtension_ShotSound>() : null;
            if (ext == null || ext.chargeTicks > 0) return;      // charged weapons: the recording started with the charge (Patch_KnightCharge)
            VehiclePawn v = __instance.vehicle;
            if (v == null || !v.Spawned) return;

            FireMode mode = __instance.CurrentFireMode;
            int shots = mode != null ? Mathf.Max(1, mode.shotsPerBurst.max) : 1;
            int between = mode != null ? mode.ticksBetweenShots : 1;

            SoundDef snd = ext.sound;
            bool once = ext.perBurst;
            if (ext.byBurst != null)
            {
                // the recording made for the largest burst that is not larger than the one being fired
                int best = -1;
                foreach (BurstSound b in ext.byBurst)
                    if (b.sound != null && b.shots <= shots && b.shots > best) { best = b.shots; snd = b.sound; }
                once = true;
            }
            if (snd == null) return;

            int gap = ext.minGapTicks;
            if (once && shots > 1) gap = Mathf.Max(gap, (shots - 1) * between + 1);
            if (gap > 0)
            {
                State st = states.GetOrCreateValue(__instance);
                int now = Find.TickManager.TicksGame;
                if (now >= st.last && now - st.last < gap) return;
                st.last = now;
            }
            snd.PlayOneShot(new TargetInfo(v.Position, v.Map));
        }
    }

    /// <summary>
    /// The charge-up. VF queues a burst with GenerateTurretData (ticksTillShot = 0 -> the first shot leaves at once); for a charged weapon the first
    /// shot is held back by chargeTicks. VF keeps the turret aimed while it waits and drops the burst if the target is lost (the charge is wasted).
    /// The burst cooldown already started when the burst was queued, so the charge does not lower the rate of fire unless it is longer than the cooldown.
    /// </summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.GenerateTurretData))]
    public static class Patch_KnightCharge
    {
        static void Postfix(VehicleTurret __instance, CompVehicleTurrets.TurretData __result)
        {
            if (__result == null || __instance.def == null) return;
            DefModExtension_ShotSound ext = __instance.def.GetModExtension<DefModExtension_ShotSound>();
            if (ext == null || ext.chargeTicks <= 0) return;
            VehiclePawn v = __instance.vehicle;
            if (v == null || !v.Spawned) return;
            if (v.def.GetModExtension<DefModExtension_KnightToll>() == null) return;   // Knights only
            if (__instance.def.defName == Overload.TurretDef && Overload.Jammed(__instance)) return;   // venting after an overload: no charge-up

            __result.ticksTillShot = ext.chargeTicks;
            if (ext.sound != null) ext.sound.PlayOneShot(new TargetInfo(v.Position, v.Map));
            MapComponent_ShotFx comp = v.Map.GetComponent<MapComponent_ShotFx>();
            if (comp != null) comp.StartCharge(__instance, ext.chargeTicks);
        }
    }
}
