using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    /// <summary>
    /// Pilot training from the lore (user 2026-11-21, replaces the Knight simulator; Lexicanum / Codex: Imperial Knights 8th ed.):
    ///  - squires drill at a Knight hangar; a Knight Preceptor (Piloting 10+) mentoring them makes it far faster and builds their readiness
    ///  - the Helm Mechanicum needs no Becoming: without it Piloting stops at 7 (enough for an Armiger = Bondsman, not for a Throne)
    ///  - the Ritual of Becoming (18+, one night alone with the Throne): success = Throne-bonded (any Knight, no cap);
    ///    failure = shock, lasting Throne madness or death; better readiness and a steady mind improve the odds
    ///  - pilots also learn by fighting: every kill made by their Knight
    /// </summary>
    public static class PilotTraining
    {
        public const int PreceptorLevel = 10;      // "grizzled veterans" who train squires
        public const int UnbondedCap = 7;          // Helm Mechanicum ceiling (Questoris needs 8)
        public const int RitualMinLevel = 6;
        public const int RitualTicks = 20000;      // a full night (8 hours)
        const float MentoredXpPerHour = 900f, DrillXpPerHour = 150f, PreceptorXpPerHour = 40f, XpPerKill = 300f;
        const float ReadinessPerDayMentored = 1f, DrillReadinessFactor = 0.25f;

        public static bool Bonded(Pawn p)
        {
            if (p?.health?.hediffSet == null) return false;
            if (p.health.hediffSet.HasHediff(PilotingDefOf.LIK_ThroneBonded)) return true;
            if (PilotingUtil.Level(p) > UnbondedCap) { GrantBond(p); return true; }   // pilots trained before this system: grandfathered
            return false;
        }

        public static void GrantBond(Pawn p)
        {
            if (p?.health == null || p.health.hediffSet.HasHediff(PilotingDefOf.LIK_ThroneBonded)) return;
            p.health.AddHediff(PilotingDefOf.LIK_ThroneBonded);
            Hediff r = p.health.hediffSet.GetFirstHediffOfDef(PilotingDefOf.LIK_SquireReadiness);
            if (r != null) p.health.RemoveHediff(r);
        }

        /// <summary>Every Knight except the Armiger is piloted from a Throne Mechanicum.</summary>
        public static bool NeedsThrone(VehiclePawn v)
        {
            if (v == null || !LIKMod.I.requireSkill) return false;
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            return cls != null && !cls.defName.Contains("Armiger");
        }

        public static bool IsPreceptor(Pawn p) { return PilotingUtil.Level(p) >= PreceptorLevel && Bonded(p); }

        public static float Readiness(Pawn p)
        {
            Hediff h = p?.health?.hediffSet?.GetFirstHediffOfDef(PilotingDefOf.LIK_SquireReadiness);
            return h == null ? 0f : Mathf.Clamp01(h.Severity);
        }

        public static void AddReadiness(Pawn p, float amount)
        {
            if (p?.health == null || Bonded(p)) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(PilotingDefOf.LIK_SquireReadiness);
            if (h == null) { h = p.health.AddHediff(PilotingDefOf.LIK_SquireReadiness); h.Severity = 0.001f; }
            h.Severity = Mathf.Min(1f, h.Severity + amount);
        }

        public static bool CanBeSquire(Pawn p)
        {
            return p != null && p.ageTracker != null && p.ageTracker.AgeBiologicalYears >= 13 && !Bonded(p) && PilotingUtil.Level(p) < UnbondedCap + 1
                   && !p.health.hediffSet.HasHediff(PilotingDefOf.LIK_ThroneMadness);
        }

        // -------------------------------------------------------------- the ritual
        public static string RitualProblem(Pawn p)
        {
            if (p.ageTracker.AgeBiologicalYears < 18) return "must be at least 18";
            if (Bonded(p)) return "already Throne-bonded";
            if (p.health.hediffSet.HasHediff(PilotingDefOf.LIK_ThroneMadness)) return "broken by an earlier attempt";
            if (PilotingUtil.Level(p) < RitualMinLevel) return "needs Piloting " + RitualMinLevel + " (has " + PilotingUtil.Level(p) + ")";
            if (p.Downed || p.InMentalState || p.Drafted) return "is not able right now";
            return null;
        }

        /// <summary>Chance the Ritual fails: 30% unprepared, 5% fully ready; a steady mind helps, a fragile one hurts.</summary>
        public static float FailChance(Pawn p)
        {
            float f = Mathf.Lerp(0.30f, 0.05f, Readiness(p));
            TraitDef nerves = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves"), mood = DefDatabase<TraitDef>.GetNamedSilentFail("NaturalMood");
            if (p.story?.traits != null)
            {
                if (nerves != null && p.story.traits.HasTrait(nerves))
                {
                    int d = p.story.traits.DegreeOfTrait(nerves);
                    f *= d >= 2 ? 0.5f : d == 1 ? 0.75f : d == -1 ? 1.3f : 1.6f;
                }
                if (mood != null && p.story.traits.HasTrait(mood) && p.story.traits.DegreeOfTrait(mood) < 0) f *= 1.2f;
            }
            return Mathf.Clamp(f, 0.02f, 0.6f);
        }

        public static void ResolveRitual(Pawn p, Building_KnightHangar h)
        {
            float fail = FailChance(p);
            if (!Rand.Chance(fail))
            {
                GrantBond(p);
                Find.LetterStack.ReceiveLetter("Ritual of Becoming: " + p.LabelShort,
                    p.LabelShortCap + " sat the night alone with the Throne Mechanicum and imprinted their psyche upon it. They rise changed, their childhood left behind - a Noble of the house, bonded to the Throne. They may now pilot any Knight.",
                    LetterDefOf.PositiveEvent, p);
                return;
            }
            Hediff r = p.health.hediffSet.GetFirstHediffOfDef(PilotingDefOf.LIK_SquireReadiness);
            if (r != null) r.Severity *= 0.5f;
            float roll = Rand.Value;
            if (roll < 0.15f)
            {
                Find.LetterStack.ReceiveLetter("Ritual of Becoming: " + p.LabelShort + " is dead",
                    "The Throne's engrams overwhelmed " + p.LabelShort + ". The Sacristans found them lifeless in the Throne at dawn.", LetterDefOf.Death, new TargetInfo(h.Position, h.Map));
                p.Kill(null);
                return;
            }
            if (roll < 0.55f)
            {
                p.health.AddHediff(PilotingDefOf.LIK_ThroneMadness);
                p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Psychotic, "the Ritual of Becoming", true);
                Find.LetterStack.ReceiveLetter("Ritual of Becoming: " + p.LabelShort + " was broken",
                    p.LabelShortCap + " could not master the Throne. The voices of its past riders never left them: " + p.LabelShort + " now suffers from Throne madness and can never sit a Throne.", LetterDefOf.NegativeEvent, p);
                return;
            }
            p.health.AddHediff(PilotingDefOf.LIK_BecomingShock);
            p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Psychotic, "the Ritual of Becoming", true);
            Find.LetterStack.ReceiveLetter("Ritual of Becoming: " + p.LabelShort + " failed",
                p.LabelShortCap + " was nearly overwhelmed by the Throne's ancestral engrams and fled it in a daze. They survived and may try again once recovered.", LetterDefOf.NegativeEvent, p);
        }

        public static IEnumerable<Gizmo> HangarGizmos(Building_KnightHangar h)
        {
            var cmd = new Command_Action
            {
                defaultLabel = "Ritual of Becoming",
                defaultDesc = "Seat a squire alone with the Throne Mechanicum for a full night to imprint their psyche upon it. Success makes them Throne-bonded: they may pilot any Knight and their Piloting is no longer capped at " + UnbondedCap + ". Failure can mean shock, lasting madness or death. Needs age 18+ and Piloting " + RitualMinLevel + "+; mentoring by a Knight Preceptor improves the odds.",
                icon = ContentFinder<Texture2D>.Get("UI/Icons/Rituals/Ritual", false) ?? BaseContent.BadTex,
                action = delegate
                {
                    var opts = new List<FloatMenuOption>();
                    foreach (Pawn p in h.Map.mapPawns.FreeColonistsSpawned)
                    {
                        if (Bonded(p)) continue;
                        string why = RitualProblem(p);
                        if (why != null) { opts.Add(new FloatMenuOption(p.LabelShortCap + ": " + why, null)); continue; }
                        Pawn pp = p;
                        opts.Add(new FloatMenuOption(pp.LabelShortCap + " (readiness " + Mathf.RoundToInt(Readiness(pp) * 100f) + "%, failure risk " + Mathf.RoundToInt(FailChance(pp) * 100f) + "%)", delegate
                        {
                            Job j = JobMaker.MakeJob(PilotingDefOf.LIK_RitualOfBecoming, h, h.BayCell);
                            pp.jobs.TryTakeOrderedJob(j, JobTag.Misc);
                        }));
                    }
                    if (opts.Count == 0) opts.Add(new FloatMenuOption("No colonist can attempt it (all are Throne-bonded or absent)", null));
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
            };
            yield return cmd;
            if (Prefs.DevMode)                         // user 2026-11-21: dev bypass for testing (no age/skill check, no roll, no night)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Becoming (instant)",
                    defaultDesc = "Dev mode only. Throne-bond a colonist at once, skipping the Ritual of Becoming (no age or Piloting check, no failure roll). Also clears Throne madness and Becoming shock.",
                    icon = ContentFinder<Texture2D>.Get("UI/Icons/Rituals/Ritual", false) ?? BaseContent.BadTex,
                    action = delegate
                    {
                        var opts = new List<FloatMenuOption>();
                        foreach (Pawn p in h.Map.mapPawns.FreeColonistsSpawned)
                        {
                            if (Bonded(p)) continue;
                            Pawn pp = p;
                            opts.Add(new FloatMenuOption(pp.LabelShortCap, delegate { DevBond(pp); }));
                        }
                        if (opts.Count == 0) opts.Add(new FloatMenuOption("Every colonist is already Throne-bonded", null));
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                };
            }
        }

        /// <summary>Dev bypass of the Ritual of Becoming: bond at once and clear the ritual's failure hediffs.</summary>
        public static void DevBond(Pawn p)
        {
            if (p?.health == null) return;
            foreach (HediffDef d in new[] { PilotingDefOf.LIK_ThroneMadness, PilotingDefOf.LIK_BecomingShock })
            {
                Hediff hd = p.health.hediffSet.GetFirstHediffOfDef(d);
                if (hd != null) p.health.RemoveHediff(hd);
            }
            GrantBond(p);
            Messages.Message("DEV: " + p.LabelShortCap + " is now Throne-bonded (Ritual of Becoming skipped).", p, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Imperial Knights", "Throne-bond (skip Becoming)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DebugThroneBond(Pawn p)
        {
            if (p?.health == null) return;
            if (Bonded(p)) { Messages.Message(p.LabelShortCap + " is already Throne-bonded.", p, MessageTypeDefOf.RejectInput, false); return; }
            DevBond(p);
        }

        // -------------------------------------------------------------- shared tick logic
        public static Pawn MentorOf(Pawn squire)
        {
            foreach (Pawn p in squire.Map.mapPawns.FreeColonistsSpawned)
                if (p.CurJobDef == PilotingDefOf.LIK_MentorSquire && p.CurJob.targetA.Thing == squire && p.Position.InHorDistOf(squire.Position, 4f)) return p;
            return null;
        }

        public static void SquireTick(Pawn squire, Pawn mentor)
        {
            float m = LIKMod.I.trainingMult;
            squire.skills.Learn(PilotingDefOf.LIK_Piloting, (mentor != null ? MentoredXpPerHour : DrillXpPerHour) / 2500f * m);
            AddReadiness(squire, ReadinessPerDayMentored / 60000f * m * (mentor != null ? 1f : DrillReadinessFactor));
            if (mentor != null) mentor.skills.Learn(PilotingDefOf.LIK_Piloting, PreceptorXpPerHour / 2500f * m);
        }

        public static void KillXp(VehiclePawn v)
        {
            foreach (Pawn p in TollUtil.Pilots(v)) p.skills?.Learn(PilotingDefOf.LIK_Piloting, XpPerKill * LIKMod.I.trainingMult);
        }
    }

    // ------------------------------------------------------------------ squire drills at the hangar
    public class WorkGiver_SquireTraining : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode { get { return PathEndMode.OnCell; } }
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (ThingDef d in HangarGeo.AllDefs)                     // South + West + East hangars (2026-11-26)
                foreach (Thing t in pawn.Map.listerThings.ThingsOfDef(d)) yield return t;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!PilotTraining.CanBeSquire(pawn) || PilotTraining.IsPreceptor(pawn)) return false;
            if (PilotingUtil.Level(pawn) >= PilotTraining.UnbondedCap && PilotTraining.Readiness(pawn) >= 1f) return false;   // nothing left to learn without the Ritual
            Building_KnightHangar h = t as Building_KnightHangar;
            return h != null && !t.IsForbidden(pawn) && DrillCell(pawn, h).IsValid;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_KnightHangar h = (Building_KnightHangar)t;
            return JobMaker.MakeJob(PilotingDefOf.LIK_SquireTraining, h, DrillCell(pawn, h));
        }

        /// <summary>A free standing cell next to the bay, on the apron in front of it.</summary>
        static IntVec3 DrillCell(Pawn pawn, Building_KnightHangar h)
        {
            IntVec3 bay = h.BayCell;
            for (int r = 0; r < 4; r++)
                foreach (IntVec3 c in GenRadial.RadialCellsAround(bay + h.KnightFacing.FacingCell * 3, r, true))   // on the apron side
                    if (c.InBounds(h.Map) && c.Standable(h.Map) && pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Some)) return c;
            return IntVec3.Invalid;
        }
    }

    public class JobDriver_SquireTraining : JobDriver
    {
        Pawn mentor;
        int checkTick = -999;

        public override bool TryMakePreToilReservations(bool errorOnFailed) { return pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed); }

        public override string GetReport()
        {
            return mentor != null ? "drilling at the Throne under " + mentor.LabelShort + "." : "drilling at the Throne as a squire.";
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil drill = new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 5000, socialMode = RandomSocialMode.Off, handlingFacing = true };
            drill.tickAction = delegate
            {
                pawn.rotationTracker.FaceCell(((Building_KnightHangar)TargetThingA).BayCell);
                int now = Find.TickManager.TicksGame;
                if (now - checkTick >= 60) { mentor = PilotTraining.MentorOf(pawn); checkTick = now; }
                PilotTraining.SquireTick(pawn, mentor);
                if (pawn.IsHashIntervalTick(400)) FleckMaker.ThrowMicroSparks(pawn.DrawPos, pawn.Map);
            };
            yield return drill;
        }
    }

    // ------------------------------------------------------------------ Knight Preceptor mentoring
    public class WorkGiver_MentorSquire : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode { get { return PathEndMode.Touch; } }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Pawn p in pawn.Map.mapPawns.FreeColonistsSpawned)
                if (p.CurJobDef == PilotingDefOf.LIK_SquireTraining) yield return p;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false) { return !PilotTraining.IsPreceptor(pawn); }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn squire = t as Pawn;
            if (squire == null || squire == pawn || squire.CurJobDef != PilotingDefOf.LIK_SquireTraining) return false;
            if (PilotTraining.MentorOf(squire) != null) return false;                       // already has a Preceptor
            foreach (Pawn p in pawn.Map.mapPawns.FreeColonistsSpawned)                    // someone else is on the way
                if (p != pawn && p.CurJobDef == PilotingDefOf.LIK_MentorSquire && p.CurJob.targetA.Thing == squire) return false;
            return pawn.CanReach(squire, PathEndMode.Touch, Danger.Some);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) { return JobMaker.MakeJob(PilotingDefOf.LIK_MentorSquire, t); }
    }

    public class JobDriver_MentorSquire : JobDriver
    {
        Pawn Squire { get { return TargetThingA as Pawn; } }
        public override bool TryMakePreToilReservations(bool errorOnFailed) { return true; }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Squire.CurJobDef != PilotingDefOf.LIK_SquireTraining);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil teach = new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 5000, socialMode = RandomSocialMode.Off, handlingFacing = true };
            teach.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(Squire);
                if (!pawn.Position.InHorDistOf(Squire.Position, 4f)) pawn.pather.StartPath(Squire, PathEndMode.Touch);
                if (pawn.IsHashIntervalTick(500)) MoteMaker.MakeSpeechBubble(pawn, ContentFinder<Texture2D>.Get("Things/Mote/SpeechSymbols/Speech", false) ?? BaseContent.BadTex);
            };
            yield return teach;
        }
    }

    // ------------------------------------------------------------------ the Ritual of Becoming
    public class JobDriver_RitualOfBecoming : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) { return pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed); }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil night = new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = PilotTraining.RitualTicks, socialMode = RandomSocialMode.Off };
            night.WithProgressBarToilDelay(TargetIndex.B);
            night.tickAction = delegate
            {
                if (pawn.IsHashIntervalTick(200)) FleckMaker.ThrowMicroSparks(pawn.DrawPos, pawn.Map);
                if (pawn.IsHashIntervalTick(900)) FleckMaker.ThrowLightningGlow(pawn.DrawPos, pawn.Map, 0.6f);
            };
            yield return night;
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = delegate { PilotTraining.ResolveRitual(pawn, (Building_KnightHangar)TargetThingA); }
            };
        }
    }

    // ------------------------------------------------------------------ patches
    /// <summary>Without the Ritual of Becoming, Piloting stops at the Helm Mechanicum ceiling (pilots already above it are grandfathered).</summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Patch_PilotingCap
    {
        static readonly AccessTools.FieldRef<SkillRecord, Pawn> pawnRef = AccessTools.FieldRefAccess<SkillRecord, Pawn>("pawn");

        static void Prefix(SkillRecord __instance, out int __state) { __state = __instance.levelInt; }

        static void Postfix(SkillRecord __instance, int __state)
        {
            if (__instance.def != PilotingDefOf.LIK_Piloting || !LIKMod.I.requireSkill) return;
            Pawn p = pawnRef(__instance);
            if (p?.health == null || p.health.hediffSet.HasHediff(PilotingDefOf.LIK_ThroneBonded)) return;
            if (__state > PilotTraining.UnbondedCap) { PilotTraining.GrantBond(p); return; }          // trained before this system
            if (__instance.levelInt > PilotTraining.UnbondedCap)
            {
                __instance.levelInt = PilotTraining.UnbondedCap;
                __instance.xpSinceLastLevel = __instance.XpRequiredForLevelUp - 1f;
            }
        }
    }

    /// <summary>Pilots learn by fighting: every kill their Knight makes.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_PilotKillXp
    {
        static void Postfix(DamageInfo? dinfo)
        {
            if (dinfo?.Instigator is VehiclePawn v && v.Faction == Faction.OfPlayer && v.def.GetModExtension<DefModExtension_KnightToll>() != null)
                PilotTraining.KillXp(v);
        }
    }
}
