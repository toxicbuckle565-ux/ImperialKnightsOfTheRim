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
    /// ARMIGER weapon lore pass (user 2026-11-17: "research how each armiger weapon works then build them to function lore wise").
    /// Sources: Lexicanum (read through the user's Chrome) + Wahapedia Horus Heresy 3rd ed. / 40k 10th ed. profiles. Only sourced abilities:
    ///  - Armiger autocannon : HH3 Ordnance (R) - the turret's def range is its BRACED range; while the Knight walks it only reaches range / braceRange.
    ///  - lightning lock     : lore - "the Moirax overcharges its reactor to unleash sustained blasts over short ranges": an 'Overcharge' fire mode
    ///                         (longer burst, more heat through VF's heat bar) whose shots lose half their power beyond half range.
    ///  - conversion beamer  : lore - "must remain absolutely motionless when operated, or the intensity of the beam will be negated": fired on the
    ///                         move, the shot fizzles at the muzzle.
    ///  - volkite veuglaire  : HH3 Deflagrate + lore "a target can simply combust, often taking nearby comrades with them".
    ///  - irad-cleanser      : lore "boiled alive from within ... the target will likely die later from radioactive contamination" (LIK_Irradiated).
    /// Stats and the other (already existing) effects are set in Tools/gen_knights.py (PROJ / SHOT_EXTRA / MELEE_FX, 'ARMIGER pass 2026-11-17').
    /// </summary>
    public static class ArmigerWeapons
    {
        public static float pendingOverchargeReach;     // set while an overcharged turret fires: beyond this distance its shots have half power

        static HediffDef irradiated;
        static HediffDef Irradiated => irradiated ?? (irradiated = DefDatabase<HediffDef>.GetNamedSilentFail("LIK_Irradiated"));

        public static DefModExtension_ShotFx FxOf(VehicleTurret t) { return t?.def?.projectile?.GetModExtension<DefModExtension_ShotFx>(); }

        public static bool Moving(VehicleTurret t) { VehiclePawn v = t?.vehicle; return v != null && v.vehiclePather != null && v.vehiclePather.Moving; }

        /// <summary>Conversion beam fired on the move: a weak flicker at the muzzle, nothing else.</summary>
        public static void Fizzle(Vector3 from, Vector3 to, Map map, VehiclePawn v)
        {
            if (map == null) return;
            Vector3 dir = (to - from); dir.y = 0f;
            Vector3 p = from + dir.normalized * 1.2f;
            if (p.ShouldSpawnMotesAt(map))
            {
                FleckMaker.ThrowMicroSparks(p, map);
                FleckMaker.ThrowMicroSparks(p, map);
                GlowFx.Lightning(p, map, 0.8f);
                FleckMaker.ThrowSmoke(p, map, 0.7f);
            }
            if (v != null && v.Faction == Faction.OfPlayer && Find.TickManager.TicksGame % 300 < 60)
                Messages.Message(v.LabelShort + ": the conversion beam fizzled - it must be fired standing still.", v, MessageTypeDefOf.SilentInput, false);
        }

        /// <summary>Volkite Deflagrate: a living target the ray hits combusts; the burst hits (and may ignite) those right next to it.</summary>
        public static void Deflagrate(Pawn target, float damage, Thing who, ThingDef weapon, Map map)
        {
            if (map == null || target == null || target is VehiclePawn || target.RaceProps == null || !target.RaceProps.IsFlesh) return;
            IntVec3 cell = target.PositionHeld;
            if (!cell.InBounds(map)) return;
            bool dead = target.Dead || target.Destroyed;
            float radius = dead ? 2.2f : 1.5f, dmg = dead ? damage * 1.5f : damage;
            Vector3 pos = cell.ToVector3Shifted();
            if (pos.ShouldSpawnMotesAt(map))
            {
                GlowFx.Fire(pos, map, dead ? 1.6f : 1.0f);
                FleckMaker.ThrowMicroSparks(pos, map);
                FleckDef flash = DefDatabase<FleckDef>.GetNamedSilentFail("ExplosionFlash");
                if (flash != null) FleckMaker.Static(pos, map, flash, radius * 2.2f);
            }
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(cell, map, radius, true).ToList())
            {
                if (t == target || t.Destroyed) continue;
                Pawn p = t as Pawn;
                if (p == null || p.Dead || p is VehiclePawn) continue;
                if (who != null && who.Faction != null && p.Faction == who.Faction) continue;
                p.TakeDamage(new DamageInfo(DamageDefOf.Flame, dmg, 0.2f, -1f, who, null, weapon));
                if (!p.Dead && !p.Destroyed && Rand.Chance(0.35f)) p.TryAttachFire(Rand.Range(0.3f, 0.6f), who);
            }
        }

        /// <summary>Irad-cleanser: living, organic things in the blast get radiation sickness (builds over time, LIK_Irradiated). Mechs are immune.</summary>
        public static void Irradiate(Map map, IntVec3 cell, float radius, Thing who, float amount)
        {
            if (map == null || Irradiated == null || !cell.InBounds(map)) return;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(cell, map, Mathf.Max(0.9f, radius), true).ToList())
            {
                Pawn p = t as Pawn;
                if (p == null || p.Dead || p is VehiclePawn || p.RaceProps == null || !p.RaceProps.IsFlesh) continue;
                if (who != null && who.Faction != null && p.Faction == who.Faction) continue;
                float resist = Mathf.Clamp01(p.GetStatValue(StatDefOf.ToxicResistance));
                float add = amount * (1f - resist);
                if (add <= 0.001f) continue;
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Irradiated);
                if (h == null) { h = HediffMaker.MakeHediff(Irradiated, p); h.Severity = add; p.health.AddHediff(h); }
                else h.Severity = Mathf.Min(h.def.maxSeverity, h.Severity + add);
            }
        }
    }

    /// <summary>HH3 Ordnance (R): braced (standing still) the autocannon reaches its full def range; walking, only range / braceRange.</summary>
    [HarmonyPatch(typeof(VehicleTurret), "get_MaxRange")]
    public static class Patch_BraceRange
    {
        static void Postfix(VehicleTurret __instance, ref float __result)
        {
            DefModExtension_ShotFx fx = ArmigerWeapons.FxOf(__instance);
            if (fx == null || fx.braceRange <= 1f || __result <= 0f) return;
            if (ArmigerWeapons.Moving(__instance)) __result /= fx.braceRange;
        }
    }

    /// <summary>Lightning lock 'Overcharge' fire mode: its shots read how far they keep full power (Projectile_KnightShell.Launch).</summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    public static class Patch_Overcharge
    {
        static void Prefix(VehicleTurret __instance)
        {
            ArmigerWeapons.pendingOverchargeReach = 0f;
            try
            {
                DefModExtension_ShotFx fx = ArmigerWeapons.FxOf(__instance);
                if (fx == null || fx.overcharge <= 0f) return;
                string label = __instance.CurrentFireMode?.label;
                if (label == null || !label.StartsWith("Overcharge")) return;
                ArmigerWeapons.pendingOverchargeReach = Mathf.Max(1f, __instance.def.maxRange * fx.overcharge);
            }
            catch (System.Exception e) { Log.ErrorOnce("[LIK] overcharge: " + e, 77171117); }
        }

        static void Finalizer() { ArmigerWeapons.pendingOverchargeReach = 0f; }
    }
}
