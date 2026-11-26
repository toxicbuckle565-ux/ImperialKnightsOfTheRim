using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Vehicles;
using Verse;
using LudeonTK;

namespace LIK
{
    /// <summary>
    /// KNIGHT FACTIONS - framework (user 2026-11-24). Everything later features build on:
    ///  - DefModExtension_KnightHouse marks a FactionDef as a Knight house (Defs/Factions/LIK_KnightHouse.xml);
    ///  - WorldComponent_KnightHouses gives every Knight-house faction one of the lore houses of KnightPaint.Houses (its name and armour
    ///    colour become the faction's name and colour; two houses in one world never share a house);
    ///  - KnightHouseUtil.GenerateCrewedKnight builds a ready Knight of that house: random legal loadout (lost STC weapons only as rare
    ///    relics), random head, house colours, and a pilot of the house (noble, or a Bondsman for an Armiger) with the Throne bond and
    ///    enough Piloting. Raids, reinforcements, caravans and Freeblades will call this.
    /// </summary>
    public class DefModExtension_KnightHouse : DefModExtension
    {
        public List<KnightClassDef> knightClasses;      // null = every Knight class
        public float relicWeaponChance = 0.15f;         // chance per mount that a lost (STC) weapon is allowed as a house relic
        // user 2026-11-25 "add a renegade knight faction till we add the chaos knight": a renegade house takes its name / colours from
        // KnightPaint.RenegadeHouses instead, and its own pilots
        public bool renegade;
        public PawnKindDef nobleKind;                   // null = LIK_KnightNoble
        public PawnKindDef bondsmanKind;                // null = LIK_Bondsman
    }

    public class WorldComponent_KnightHouses : WorldComponent
    {
        Dictionary<int, string> houseOf = new Dictionary<int, string>();   // faction loadID -> house label (KnightPaint.Houses)

        public WorldComponent_KnightHouses(World world) : base(world) { }

        public static WorldComponent_KnightHouses Instance => Find.World?.GetComponent<WorldComponent_KnightHouses>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref houseOf, "houseOf", LookMode.Value, LookMode.Value);
            if (houseOf == null) houseOf = new Dictionary<int, string>();
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            AssignAll();
        }

        /// <summary>Give every Knight-house faction without a house one (an unused one if possible) and refresh names / colours.</summary>
        public void AssignAll()
        {
            if (Find.FactionManager == null) return;
            foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (!KnightHouseUtil.IsKnightHouse(f)) continue;
                if (!houseOf.ContainsKey(f.loadID))
                {
                    var used = new HashSet<string>(houseOf.Values);
                    List<KnightPaint.Scheme> pool = KnightHouseUtil.SchemesFor(f);
                    List<KnightPaint.Scheme> free = pool.Where(h => !used.Contains(h.label)).ToList();
                    KnightPaint.Scheme pick = (free.Count > 0 ? free : pool).RandomElement();
                    houseOf[f.loadID] = pick.label;
                    Log.Message("[LIK] Knight house: faction " + f.loadID + " is " + pick.label);
                }
                Apply(f);
            }
        }

        void Apply(Faction f)
        {
            KnightPaint.Scheme? s = SchemeOf(f);
            if (s == null) return;
            f.Name = s.Value.label;
            f.color = s.Value.one;
        }

        public KnightPaint.Scheme? SchemeOf(Faction f)
        {
            string label;
            if (f == null || !houseOf.TryGetValue(f.loadID, out label)) return null;
            foreach (KnightPaint.Scheme h in KnightHouseUtil.SchemesFor(f)) if (h.label == label) return h;
            return null;
        }
    }

    public static class KnightHouseUtil
    {
        public static bool IsKnightHouse(Faction f) => f?.def != null && f.def.HasModExtension<DefModExtension_KnightHouse>();

        public static KnightPaint.Scheme? SchemeOf(Faction f) => WorldComponent_KnightHouses.Instance?.SchemeOf(f);

        public static bool IsRenegade(Faction f) => f?.def?.GetModExtension<DefModExtension_KnightHouse>()?.renegade == true;

        /// <summary>The houses a faction can be: the loyal houses, or (renegade faction) the renegade ones.</summary>
        public static List<KnightPaint.Scheme> SchemesFor(Faction f) => IsRenegade(f) ? KnightPaint.RenegadeHouses : KnightPaint.Houses;

        public static IEnumerable<KnightClassDef> ClassesOf(Faction f)
        {
            DefModExtension_KnightHouse ext = f?.def?.GetModExtension<DefModExtension_KnightHouse>();
            return ext?.knightClasses != null && ext.knightClasses.Count > 0 ? ext.knightClasses : DefDatabase<KnightClassDef>.AllDefsListForReading;
        }

        /// <summary>A random legal weapon key for every mount: optional mounts are sometimes left empty; lost (STC) weapons only as rare relics.</summary>
        public static List<string> RandomKeys(KnightClassDef cls, float relicChance)
        {
            var keys = new List<string>();
            foreach (KnightMount m in cls.mounts)
            {
                bool relic = Rand.Chance(relicChance);
                List<WeaponOption> ok = m.options.Where(o => o != null && (relic || o.weapon == null || !STCUtil.WeaponLocked(o.weapon))).ToList();
                if (ok.Count == 0) ok = m.options.Where(o => o != null).ToList();
                if (m.optional && (ok.Count == 0 || Rand.Chance(0.25f))) { keys.Add(KnightMount.None); continue; }
                keys.Add(ok.Count > 0 ? ok.RandomElement().turretKey : KnightMount.None);
            }
            return keys;
        }

        /// <summary>
        /// A complete Knight of this house, NOT spawned: loadout, head, house colours, and a bonded pilot of the house in the pilot seat.
        /// Spawn it with GenSpawn.Spawn(knight, cell, map, Rot4.South). Returns null if the class / faction can't make one.
        /// </summary>
        public static VehiclePawn GenerateCrewedKnight(Faction f, KnightClassDef cls)
        {
            if (f == null || cls?.vehicle == null) return null;
            VehiclePawn knight = VehicleSpawner.GenerateVehicle(cls.vehicle, f);
            if (knight == null) return null;
            DefModExtension_KnightHouse ext = f.def.GetModExtension<DefModExtension_KnightHouse>();
            KnightLoadout.Apply(knight, cls, RandomKeys(cls, ext?.relicWeaponChance ?? 0.15f));
            if (cls.heads != null && cls.heads.Count > 0) KnightLoadout.SetHead(knight, cls, Rand.Range(0, cls.heads.Count));
            CompKnightHead kc = knight.GetComp<CompKnightHead>();
            if (kc != null)
            {
                kc.lifeSupport = cls.lifeSupport != null && Rand.Chance(0.7f);
                if (cls.voidShield != null) kc.voidShield = Rand.Chance(0.6f);   // (a void shield on a mount is set by KnightLoadout.Apply)
                kc.shieldCharge = -1f;
            }
            Paint(knight, f);
            Pawn pilot = GeneratePilot(f, knight, cls);
            if (pilot != null)
            {
                VehicleRoleHandler seat = knight.handlers?.FirstOrDefault(h => h.role != null && (h.role.HandlingTypes & HandlingType.Movement) != 0 && h.AreSlotsAvailable);
                if (seat == null || !knight.TryAddPawn(pilot, seat))
                {
                    Log.Warning("[LIK] Knight house: could not seat " + pilot.LabelShort + " in a " + cls.label);
                    pilot.Destroy();
                }
            }
            return knight;
        }

        /// <summary>House colours on the Knight (armour / trim from the house; third colour = trim).</summary>
        public static void Paint(VehiclePawn knight, Faction f)
        {
            if (!KnightPaint.Paintable(knight.VehicleDef)) return;
            KnightPaint.Scheme? s = SchemeOf(f);
            if (s == null) { KnightPaint.EnsureInitial(knight); return; }
            KnightPaint.Apply(knight, new PaintChoice { one = s.Value.one, two = s.Value.two, three = s.Value.two });
        }

        /// <summary>A pilot of the house fit for this Knight: a noble (Bondsman for an Armiger), Throne-bonded, with at least the Knight's
        /// minimum Piloting (nobles a little more).</summary>
        public static Pawn GeneratePilot(Faction f, VehiclePawn knight, KnightClassDef cls)
        {
            bool armiger = cls.defName.Contains("Armiger");
            DefModExtension_KnightHouse ext = f?.def?.GetModExtension<DefModExtension_KnightHouse>();
            PawnKindDef kind = (armiger ? ext?.bondsmanKind : ext?.nobleKind) ?? DefDatabase<PawnKindDef>.GetNamedSilentFail(armiger ? "LIK_Bondsman" : "LIK_KnightNoble");
            if (kind == null) return null;
            Pawn p;
            try
            {
                p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, f, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, mustBeCapableOfViolence: true));
            }
            catch (System.Exception e) { Log.Warning("[LIK] Knight house pilot generation failed: " + e.Message); return null; }
            SkillRecord sk = PilotingDefOf.LIK_Piloting != null ? p.skills?.GetSkill(PilotingDefOf.LIK_Piloting) : null;
            if (sk != null)
            {
                int need = PilotingUtil.MinLevelFor(knight);
                sk.Level = Mathf.Clamp(Mathf.Max(sk.Level, need + Rand.RangeInclusive(armiger ? 0 : 2, armiger ? 3 : 6)), 0, 20);
                sk.passion = Passion.Major;
            }
            PilotTraining.GrantBond(p);
            return p;
        }
    }

    public static class LIKHouseDebugActions
    {
        static Faction AnyHouse() => Find.FactionManager?.AllFactionsListForReading.FirstOrDefault(KnightHouseUtil.IsKnightHouse);

        [DebugAction("Imperial Knights", "Knight houses: list", allowedGameStates = AllowedGameStates.Playing)]
        static void ListHouses()
        {
            WorldComponent_KnightHouses.Instance?.AssignAll();
            var lines = Find.FactionManager.AllFactionsListForReading.Where(KnightHouseUtil.IsKnightHouse)
                .Select(f => f.Name + " (faction " + f.loadID + ", goodwill " + f.PlayerGoodwill + ", " + (f.HostileTo(Faction.OfPlayer) ? "hostile" : "not hostile") + ")").ToList();
            Log.Message("[LIK] Knight houses in this world: " + (lines.Count == 0 ? "none (start a new world with a Knight house, or use 'Knight houses: add one')" : string.Join("; ", lines)));
        }

        [DebugAction("Imperial Knights", "Knight houses: add renegade house to this world", allowedGameStates = AllowedGameStates.Playing)]
        static void AddRenegade() { AddHouse("LIK_RenegadeHouse"); }

        [DebugAction("Imperial Knights", "Knight houses: add one to this world", allowedGameStates = AllowedGameStates.Playing)]
        static void AddHouse() { AddHouse("LIK_KnightHouse"); }

        static void AddHouse(string defName)
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            Faction f = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def));
            Find.FactionManager.Add(f);
            WorldComponent_KnightHouses.Instance?.AssignAll();
            Log.Message("[LIK] added Knight house: " + f.Name);
        }

        [DebugAction("Imperial Knights", "Knight houses: spawn crewed house Knight...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void SpawnHouseKnight()
        {
            Faction f = AnyHouse();
            if (f == null) { Messages.Message("No Knight house in this world - use 'Knight houses: add one to this world' first.", MessageTypeDefOf.RejectInput, false); return; }
            var opts = new List<DebugMenuOption>();
            foreach (KnightClassDef cls in KnightHouseUtil.ClassesOf(f))
            {
                KnightClassDef c = cls;
                opts.Add(new DebugMenuOption(f.Name + ": " + c.label, DebugMenuOptionMode.Tool, delegate
                {
                    VehiclePawn k = KnightHouseUtil.GenerateCrewedKnight(f, c);
                    if (k == null) return;
                    GenSpawn.Spawn(k, UI.MouseCell(), Find.CurrentMap, Rot4.South, WipeMode.VanishOrMoveAside, false);
                    k.GetComp<CompKnightHead>()?.Apply();
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }
    }
}
