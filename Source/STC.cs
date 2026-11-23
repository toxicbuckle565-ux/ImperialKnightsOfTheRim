using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LIK
{
    /// <summary>On a forge recipe: the weapon is a lost pattern that needs this many STC fragments studied first.</summary>
    public class DefModExtension_RequiresSTC : DefModExtension
    {
        public string weaponKey;
        public int fragments = 1;
    }

    /// <summary>The colony's recovered STC knowledge: fragments studied per weapon (saved with the game).</summary>
    public class GameComponent_STC : GameComponent
    {
        Dictionary<string, int> applied = new Dictionary<string, int>();

        public static GameComponent_STC Instance { get { return Current.Game?.GetComponent<GameComponent_STC>(); } }

        public GameComponent_STC(Game game) { }

        public int Applied(string key)
        {
            int n;
            return applied.TryGetValue(key, out n) ? n : 0;
        }

        public void Add(string key)
        {
            applied[key] = Applied(key) + 1;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref applied, "applied", LookMode.Value, LookMode.Value);
            if (applied == null) applied = new Dictionary<string, int>();
        }
    }

    public static class STCUtil
    {
        public const string ItemPrefix = "LIK_STC_";
        public const string WeaponPrefix = "LIK_Weapon_";

        static Dictionary<string, int> required;
        static List<ThingDef> stcDefs;

        static void Build()
        {
            if (required != null) return;
            required = new Dictionary<string, int>();
            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                var e = r.GetModExtension<DefModExtension_RequiresSTC>();
                if (e != null) required[e.weaponKey] = e.fragments;
            }
            stcDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName.StartsWith(ItemPrefix)).ToList();
        }

        public static List<ThingDef> AllSTCDefs { get { Build(); return stcDefs; } }

        public static int Required(string key)
        {
            Build();
            int n;
            return required.TryGetValue(key, out n) ? n : 0;
        }

        public static string KeyOf(ThingDef stc)
        {
            return stc.defName.StartsWith(ItemPrefix) ? stc.defName.Substring(ItemPrefix.Length) : null;
        }

        public static bool Unlocked(string key)
        {
            int need = Required(key);
            if (need <= 0) return true;
            GameComponent_STC lib = GameComponent_STC.Instance;
            return lib != null && lib.Applied(key) >= need;
        }

        /// <summary>A fragment of this weapon would still help (the weapon is lost and not yet recovered).</summary>
        public static bool NeedsFragment(string key)
        {
            return key != null && Required(key) > 0 && !Unlocked(key);
        }

        /// <summary>True for a lost weapon item whose pattern has not been recovered yet.</summary>
        public static bool WeaponLocked(ThingDef weaponItem)
        {
            if (weaponItem == null || !weaponItem.defName.StartsWith(WeaponPrefix)) return false;
            return !Unlocked(weaponItem.defName.Substring(WeaponPrefix.Length));
        }
    }

    /// <summary>Lost weapons cannot be forged until enough STC fragments have been studied: the recipe is simply not available.</summary>
    [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.AvailableNow), MethodType.Getter)]
    public static class Patch_RecipeAvailableNow
    {
        static void Postfix(RecipeDef __instance, ref bool __result)
        {
            if (!__result) return;
            var e = __instance.GetModExtension<DefModExtension_RequiresSTC>();
            if (e != null && !STCUtil.Unlocked(e.weaponKey)) __result = false;
        }
    }

    /// <summary>Carry an STC fragment to the manufactorum and study it.</summary>
    public class JobDriver_StudySTC : JobDriver
    {
        const int StudyTicks = 1800;
        string key;
        bool done;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed) && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => key != null && !STCUtil.NeedsFragment(key));

            globalFinishActions.Add(delegate (JobCondition c)
            {
                if (!done && pawn.carryTracker?.CarriedThing != null)
                {
                    Thing dropped;
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out dropped);
                }
            });

            Toil go = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            go.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return go;

            Toil take = new Toil();
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            take.initAction = delegate
            {
                key = STCUtil.KeyOf(TargetThingA.def);
                if (!STCUtil.NeedsFragment(key) || pawn.carryTracker.TryStartCarry(TargetThingA, 1) <= 0) EndJobWith(JobCondition.Incompletable);
            };
            yield return take;

            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);

            Toil study = new Toil();
            study.defaultCompleteMode = ToilCompleteMode.Delay;
            study.defaultDuration = StudyTicks;
            study.handlingFacing = true;
            study.socialMode = RandomSocialMode.Off;
            study.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(TargetB);
                pawn.skills.Learn(SkillDefOf.Intellectual, 0.1f);
                if (pawn.IsHashIntervalTick(200)) FleckMaker.ThrowMicroSparks(pawn.DrawPos, pawn.Map);
            };
            study.WithProgressBarToilDelay(TargetIndex.B);
            study.AddFinishAction(delegate
            {
                if (study.actor.jobs.curDriver.ticksLeftThisToil > 0 || done) return;
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null || key == null) return;
                pawn.carryTracker.innerContainer.ClearAndDestroyContents();
                GameComponent_STC lib = GameComponent_STC.Instance;
                if (lib == null) return;
                lib.Add(key);
                done = true;
                int need = STCUtil.Required(key), have = lib.Applied(key);
                string weapon = DefDatabase<ThingDef>.GetNamedSilentFail(STCUtil.WeaponPrefix + key)?.label ?? key;
                Messages.Message(have >= need
                    ? "The STC pattern for the " + weapon + " is recovered. It can now be forged at the manufactorum."
                    : "An STC fragment for the " + weapon + " is studied (" + have + "/" + need + ").", pawn,
                    have >= need ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.TaskCompletion);
            });
            yield return study;
        }
    }

    /// <summary>Researchers (the Research work type) study any STC fragment that would still unlock something.</summary>
    public class WorkGiver_StudySTC : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (ThingDef d in STCUtil.AllSTCDefs)
            {
                if (!STCUtil.NeedsFragment(STCUtil.KeyOf(d))) continue;
                foreach (Thing t in pawn.Map.listerThings.ThingsOfDef(d)) yield return t;
            }
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return pawn.Map.listerThings.ThingsOfDef(HangarDefOf.LIK_MechanicumManufactorum).Count == 0;
        }

        public override PathEndMode PathEndMode { get { return PathEndMode.Touch; } }

        Thing FindManufactorum(Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(HangarDefOf.LIK_MechanicumManufactorum),
                PathEndMode.InteractionCell, TraverseParms.For(pawn), 9999f, delegate (Thing b)
                {
                    if (b.IsForbidden(pawn) || !pawn.CanReserve(b)) return false;
                    CompPowerTrader power = b.TryGetComp<CompPowerTrader>();
                    return power == null || power.PowerOn;
                });
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced)) return false;
            if (!STCUtil.NeedsFragment(STCUtil.KeyOf(t.def))) return false;
            return FindManufactorum(pawn) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Thing m = FindManufactorum(pawn);
            return m == null ? null : JobMaker.MakeJob(HangarDefOf.LIK_StudySTC, t, m);
        }
    }

    public static class LIKDebugActions
    {
        static void SpawnKnight(string defName)
        {
            Vehicles.VehicleDef def = DefDatabase<Vehicles.VehicleDef>.GetNamedSilentFail(defName);
            Map map = Find.CurrentMap;
            if (def == null || map == null) { Log.Warning("[LIK] spawn: no vehicle def " + defName); return; }
            Vehicles.VehiclePawn k = Vehicles.VehicleSpawner.GenerateVehicle(def, Faction.OfPlayer);
            GenSpawn.Spawn(k, UI.MouseCell(), map, Rot4.South, WipeMode.VanishOrMoveAside, false);
        }
        [DebugAction("Imperial Knights", "Spawn Armiger (click map)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnArmiger() { SpawnKnight("LIK_Armiger"); }
        [DebugAction("Imperial Knights", "Spawn Questoris (click map)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnQuestoris() { SpawnKnight("LIK_Questoris"); }
        [DebugAction("Imperial Knights", "Spawn Cerastus (click map)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnCerastus() { SpawnKnight("LIK_Cerastus"); }
        [DebugAction("Imperial Knights", "Spawn Dominus (click map)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnDominus() { SpawnKnight("LIK_Dominus"); }
        [DebugAction("Imperial Knights", "Spawn Acastus (click map)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnAcastus() { SpawnKnight("LIK_Acastus"); }
        // (Warhound Titan scale test removed 2026-11-17 - def, pictures and this action are in Desktop\Warhound Titan for the future Titan mod)

        [DebugAction("Imperial Knights", "Unlock all STC weapons", allowedGameStates = AllowedGameStates.Playing)]
        static void UnlockAllSTC()
        {
            GameComponent_STC lib = GameComponent_STC.Instance;
            if (lib == null) return;
            foreach (ThingDef d in STCUtil.AllSTCDefs)
            {
                string key = STCUtil.KeyOf(d);
                while (STCUtil.NeedsFragment(key)) lib.Add(key);
            }
            Messages.Message("All lost Knight weapons recovered.", MessageTypeDefOf.PositiveEvent, false);
        }

        [DebugAction("Imperial Knights", "Spawn 3 of every STC fragment", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnSTC()
        {
            foreach (ThingDef d in STCUtil.AllSTCDefs)
            {
                Thing t = ThingMaker.MakeThing(d);
                t.stackCount = 3;
                GenPlace.TryPlaceThing(t, UI.MouseCell(), Find.CurrentMap, ThingPlaceMode.Near);
            }
        }
    }
}
