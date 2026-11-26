using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    /// <summary>
    /// LIFE SUPPORT KEEPS THE PILOT (user 2026-11-25: "it also needs to care for the pawn's health, food, sleep, and the mood I'd like it to
    /// keep stable at a low level, around 10%"). On top of slowing strain / ageing (Toll.cs), a Knight with the life-support module fitted
    /// looks after everyone sealed inside it:
    ///  - food and rest are kept full (fed and rested through the Throne Mechanicum's feeds)
    ///  - wounds are sealed (untended injuries get tended) and slowly heal
    ///  - mood never drops below MoodLevel (user: "mood should be stopped at 10%") and no mental break can start while sealed in
    ///  - it is a machine that keeps the pilot alive to keep fighting, not a doctor: infections and diseases still need real medicine outside
    /// </summary>
    public static class LifeSupport
    {
        public const float MoodLevel = 0.10f;
        const float TendQuality = 0.6f, HealPer250 = 0.25f;

        /// <summary>The Knight this pawn is sealed in, when it has life support fitted.</summary>
        public static VehiclePawn KeptBy(Pawn p)
        {
            if (p == null || p is VehiclePawn) return null;
            IThingHolder h = p.ParentHolder;
            for (int i = 0; h != null && i < 6; i++, h = h.ParentHolder)
            {
                VehiclePawn v = h as VehiclePawn ?? (h as VehicleRoleHandler)?.vehicle;
                if (v != null) return v.GetComp<CompKnightHead>()?.lifeSupport == true ? v : null;
            }
            return null;
        }

        /// <summary>Every 250 ticks from the Knight (CompKnightHead.CompTick).</summary>
        public static void Tick(VehiclePawn v, CompKnightHead c)
        {
            if (!c.lifeSupport || KnightLoadout.ClassFor(v.VehicleDef) == null) return;
            foreach (Pawn p in Meltdown.Crew(v))
            {
                if (p.needs != null)
                {
                    if (p.needs.food != null) p.needs.food.CurLevel = p.needs.food.MaxLevel;
                    if (p.needs.rest != null) p.needs.rest.CurLevel = p.needs.rest.MaxLevel;
                    if (p.needs.mood != null && p.needs.mood.CurLevel < MoodLevel) p.needs.mood.CurLevel = MoodLevel;   // a floor, not a fixed level
                }
                if (p.health?.hediffSet == null) continue;
                foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                {
                    if (inj.TendableNow()) inj.Tended(TendQuality, TendQuality);
                    else if (!inj.IsPermanent()) inj.Heal(HealPer250);
                }
            }
        }
    }

    /// <summary>The mood need keeps recalculating itself from thoughts: while sealed in a life-support Knight it cannot fall below the floor.</summary>
    [HarmonyPatch(typeof(Need_Mood), nameof(Need_Mood.NeedInterval))]
    public static class Patch_LifeSupportMood
    {
        static void Postfix(Need_Mood __instance, Pawn ___pawn)
        {
            if (__instance.CurLevel < LifeSupport.MoodLevel && LifeSupport.KeptBy(___pawn) != null) __instance.CurLevel = LifeSupport.MoodLevel;
        }
    }

    /// <summary>No mental break starts while the pilot is sealed in and sedated by life support.</summary>
    [HarmonyPatch(typeof(MentalBreaker), nameof(MentalBreaker.CanDoRandomMentalBreaks), MethodType.Getter)]
    public static class Patch_LifeSupportNoBreaks
    {
        static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (__result && LifeSupport.KeptBy(___pawn) != null) __result = false;
        }
    }
}
