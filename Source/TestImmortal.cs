using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LIK
{
    /// <summary>
    /// TESTING option (mod settings, off by default): the player's colonists cannot be hurt, never go hungry or tired and stay in a perfect mood, so Knights can be tested
    /// without the pilots starving or going down. Works for pawns aboard a Knight too. The Knight tolls (neural strain etc.) are NOT touched.
    /// </summary>
    public static class TestImmortal
    {
        public static bool Applies(Pawn p)
        {
            return LIKMod.I != null && LIKMod.I.testImmortal && p != null && !(p is Vehicles.VehiclePawn)
                && p.Faction != null && p.Faction == Faction.OfPlayerSilentFail && p.RaceProps != null && p.RaceProps.Humanlike;
        }

        public static void Restore(Pawn p)
        {
            if (p.Dead) return;
            if (p.needs != null)
            {
                // every need is kept full: food, rest, and (user 2026-11-11) mood with everything that feeds it - joy, comfort, beauty, outdoors ...
                // A full mood bar means no mood-driven mental breaks. Mental states themselves are left alone (the Knight's neural strain causes some).
                foreach (Need n in p.needs.AllNeeds)
                    if (n != null && n.def != null && n.MaxLevel > 0f) n.CurLevelPercentage = 1f;
            }
            if (p.health?.hediffSet == null) return;
            foreach (Hediff h in p.health.hediffSet.hediffs.ToList())
            {
                if (h is Hediff_Injury && !h.IsPermanent()) { p.health.RemoveHediff(h); continue; }
                if (h.def == HediffDefOf.Malnutrition || h.def == HediffDefOf.BloodLoss || h.def == HediffDefOf.Hypothermia || h.def == HediffDefOf.Heatstroke)
                    p.health.RemoveHediff(h);
            }
        }
    }

    /// <summary>All damage to an immortal colonist is absorbed.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_TestImmortalDamage
    {
        static void Postfix(Pawn __instance, ref bool absorbed)
        {
            if (!absorbed && TestImmortal.Applies(__instance)) absorbed = true;
        }
    }

    public class MapComponent_TestImmortal : MapComponent
    {
        public MapComponent_TestImmortal(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (LIKMod.I == null || !LIKMod.I.testImmortal || Find.TickManager.TicksGame % 250 != 7) return;
            var done = new HashSet<Pawn>();
            foreach (Pawn p in map.mapPawns.AllPawns.ToList())
            {
                if (TestImmortal.Applies(p) && done.Add(p)) TestImmortal.Restore(p);
                Vehicles.VehiclePawn v = p as Vehicles.VehiclePawn;     // crew aboard a vehicle
                if (v?.handlers == null) continue;
                foreach (var h in v.handlers)
                    if (h.thingOwner != null)
                        foreach (Pawn c in h.thingOwner.InnerListForReading.ToList())
                            if (TestImmortal.Applies(c) && done.Add(c)) TestImmortal.Restore(c);
            }
        }
    }
}
