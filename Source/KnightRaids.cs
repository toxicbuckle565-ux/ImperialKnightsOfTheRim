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
    /// KNIGHT RAIDS (user 2026-11-24, first feature on the Knight-house framework): a raid by a Knight house spends part of its points on
    /// crewed house Knights instead of more infantry (so the raid is not simply stronger), with a points gate so small early raids stay
    /// infantry-only. The Knights walk in from a map edge near the raid and are driven by the Seek &amp; destroy AI (MapComponent_KnightSeek
    /// drives every non-player Knight). Works for hostile raids and for a house's friendly reinforcement raids alike.
    /// </summary>
    public static class KnightRaidUtil
    {
        public const float BudgetShare = 0.5f;          // at most this share of the raid's points goes to Knights
        public const float KeepInfantry = 0.2f;         // the infantry keeps at least this share
        // user 2026-11-25 test: a 10000-pt raid as 3 Cerastus (5100 pts) still sent 4900 pts of infantry - "a full human raid" on top.
        // A Knight is worth far more on the field than its points cost, so the infantry loses TWICE what the Knights cost.
        public const float KnightWorth = 2f;
        public const int MaxKnights = 4;

        // class -> (points cost, minimum raid points before it can appear, weight)
        static readonly Dictionary<string, (float cost, float minPoints, float weight)> Table = new Dictionary<string, (float, float, float)>
        {
            { "Armiger",   (700f,  1200f, 3.0f) },
            { "Questoris", (1500f, 2500f, 3.0f) },
            { "Cerastus",  (1700f, 2500f, 2.0f) },
            { "Dominus",   (2000f, 3000f, 1.5f) },
            { "Acastus",   (3500f, 8000f, 0.25f) },     // user 2026-11-24 "acastus need to be rarer": was 6000 pts / weight 0.7; max one per raid
        };
        const int MaxAcastus = 1;

        static (float cost, float minPoints, float weight)? Entry(KnightClassDef c)
        {
            foreach (var kv in Table) if (c.defName.Contains(kv.Key)) return kv.Value;
            return null;
        }

        // ------------------------------------------------------------------ raid types (user 2026-11-25: "for higher raids types of knights
        // spawn with either armiger swarm and other types of raid"). Low raids field the ordinary mixed lance; bigger raids may instead come
        // as a themed formation. core = Knights it always brings first (in order, while the points last), fill = what it fills up with.
        public class RaidType
        {
            public string name; public float minPoints, weight, budget; public int max;
            public string[] core; public string[] fill;
        }
        static readonly List<RaidType> Types = new List<RaidType>
        {
            new RaidType { name = "a Knight lance",              minPoints = 0f,     weight = 4f,   budget = 0.50f, max = 4, core = new string[0],
                           fill = new[] { "Armiger", "Questoris", "Cerastus", "Dominus", "Acastus" } },
            // a pack of Armigers hunting ahead of the house (bondsmen of the War Dog / Helverin / Warglaive kind): many, light, fast
            new RaidType { name = "an Armiger hunting pack",     minPoints = 3800f,  weight = 1.5f, budget = 0.55f, max = 8, core = new[] { "Armiger", "Armiger", "Armiger" },
                           fill = new[] { "Armiger" } },
            // the classic lance of Questoris-pattern Knights
            new RaidType { name = "a Questoris lance",           minPoints = 8200f,  weight = 1f,   budget = 0.55f, max = 4, core = new[] { "Questoris", "Questoris", "Questoris" },
                           fill = new[] { "Questoris" } },
            // swift Cerastus lancers riding down the enemy
            new RaidType { name = "a Cerastus strike lance",     minPoints = 6200f,  weight = 0.8f, budget = 0.55f, max = 3, core = new[] { "Cerastus", "Cerastus" },
                           fill = new[] { "Cerastus" } },
            // a siege: Dominus Knights with Armigers screening them
            new RaidType { name = "a Dominus siege lance",       minPoints = 6000f,  weight = 1f,   budget = 0.60f, max = 6, core = new[] { "Dominus", "Armiger", "Armiger" },
                           fill = new[] { "Dominus", "Armiger" } },
            // the household at war: an Acastus at the head of its heaviest Knights
            new RaidType { name = "the household spearhead",     minPoints = 12000f, weight = 0.6f, budget = 0.65f, max = 5, core = new[] { "Acastus", "Dominus" },
                           fill = new[] { "Dominus", "Questoris", "Cerastus" } },
        };

        /// <summary>The raid type of the last Pick (for the raid's message).</summary>
        public static RaidType LastType;

        /// <summary>The Knights a raid of this many points fields, and the points they cost.</summary>
        public static List<KnightClassDef> Pick(Faction f, float points, out float spent)
        {
            List<KnightClassDef> classes = KnightHouseUtil.ClassesOf(f).Where(c => Entry(c) != null).ToList();
            KnightClassDef Of(string key) => classes.FirstOrDefault(c => c.defName.Contains(key));

            // the types this raid can be: unlocked by its points, the house has its Knights, and it can afford its whole core
            List<RaidType> ok = Types.Where(t => points >= t.minPoints
                && t.core.All(k => Of(k) != null) && t.fill.Any(k => Of(k) != null)
                && t.core.Sum(k => Entry(Of(k)).Value.cost) <= points * t.budget).ToList();      // its whole core is affordable
            RaidType type = ok.Count > 0 ? ok.RandomElementByWeight(t => t.weight) : Types[0];
            LastType = type;
            bool mixed = type.core.Length == 0;                       // the ordinary lance keeps the per-class points gates

            spent = 0f;
            var picked = new List<KnightClassDef>();
            float budget = points * type.budget;
            foreach (string k in type.core)
            {
                KnightClassDef c = Of(k);
                if (c == null || picked.Count >= type.max || Entry(c).Value.cost > budget - spent) break;
                picked.Add(c); spent += Entry(c).Value.cost;
            }
            List<KnightClassDef> fill = classes.Where(c => type.fill.Any(k => c.defName.Contains(k))).ToList();
            while (picked.Count < type.max)
            {
                float left = budget - spent;
                int acastus = picked.Count(c => c.defName.Contains("Acastus"));
                List<KnightClassDef> can = fill.Where(c => Entry(c).Value.cost <= left && (!mixed || Entry(c).Value.minPoints <= points)
                                                       && !(c.defName.Contains("Acastus") && acastus >= MaxAcastus)).ToList();
                if (can.Count == 0) break;
                KnightClassDef pick = can.RandomElementByWeight(c => Entry(c).Value.weight);
                picked.Add(pick);
                spent += Entry(pick).Value.cost;
            }
            if (Prefs.DevMode && picked.Count > 0)
                Log.Message("[LIK] " + f?.Name + " raid (" + points.ToString("0") + " pts) comes as " + type.name + ": " + string.Join(", ", picked.Select(c => c.label)));
            return picked;
        }

        /// <summary>A walk-in spot on the map edge (near the raid's own arrival point if it walked in) where the whole Knight fits.</summary>
        public static bool FindEntry(Map map, IntVec3 near, VehicleDef def, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            IntVec2 size = def.Size;
            bool Fits(IntVec3 c)
            {
                var r = new CellRect(c.x - size.x / 2, c.z - size.z / 2, size.x, size.z);     // the whole footprint
                foreach (IntVec3 x in r)
                    if (!x.InBounds(map) || !x.Standable(map) || x.GetEdifice(map) != null) return false;
                return true;
            }
            IntVec3 start = near.IsValid && near.InBounds(map) ? near : IntVec3.Invalid;
            if (!start.IsValid && !RCellFinder.TryFindRandomPawnEntryCell(out start, map, CellFinder.EdgeRoadChance_Hostile)) return false;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(start, 25f, true))
            {
                if (!c.InBounds(map) || c.DistanceToEdge(map) < Mathf.Max(size.x, size.z) / 2 + 1) continue;
                if (Fits(c)) { cell = c; return true; }
            }
            return false;
        }

        public static void SpawnKnights(Faction f, Map map, IntVec3 near, List<KnightClassDef> classes)
        {
            var spawned = new List<VehiclePawn>();
            foreach (KnightClassDef cls in classes)
            {
                VehiclePawn k = KnightHouseUtil.GenerateCrewedKnight(f, cls);
                if (k == null) continue;
                IntVec3 c;
                if (!FindEntry(map, near, cls.vehicle, out c))
                {
                    Log.Warning("[LIK] Knight raid: no room on the map edge for a " + cls.label + " - it stays home.");
                    k.Destroy(); continue;
                }
                Rot4 face = Rot4.FromAngleFlat((map.Center - c).AngleFlat);
                GenSpawn.Spawn(k, c, map, face, WipeMode.VanishOrMoveAside, false);
                k.GetComp<CompKnightHead>()?.Apply();
                MapComponent_KnightSeek.Arrived[k.thingIDNumber] = Find.TickManager.TicksGame;     // arrival grace (no follow / leave yet)
                spawned.Add(k);
            }
            if (spawned.Count == 0) return;
            string list = string.Join(", ", spawned.Select(k => k.def.label));
            bool hostile = f.HostileTo(Faction.OfPlayer);
            Messages.Message((hostile ? f.Name + " has sent its Knights into battle: " : f.Name + " sends its Knights to your aid: ") + list + ".",
                new LookTargets(spawned.Cast<Thing>()), hostile ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.PositiveEvent);
            if (Prefs.DevMode) Log.Message("[LIK] Knight raid by " + f.Name + ": " + list);
        }
    }

    /// <summary>
    /// KNIGHT RAID DROP (user 2026-11-25: "add the drop to the knight when it starts a raid, then the rest of the raid falls behind" +
    /// "the knight dropping only happens with a drop party and walks to the battlefield if the troops arrive by land as well"):
    /// a DROP-POD raid by a Knight house opens with its Knights dropping from orbit (the same meteor fall + crater as the player's orbital
    /// drop) on safe ground near where the pods will come down; the pods follow a short while later. Walk-in raids: the Knights walk in
    /// with the troops (Postfix). Armigers never drop - they walk in from a map edge. Quest raids and maps in orbit: no drop.
    /// </summary>
    public static class KnightRaidDrop
    {
        // user 2026-11-25 "far after the knights landing": the pods now come down a few seconds after the LAST Knight has landed
        public const int FollowMinTicks = 180, FollowMaxTicks = 360;     // 3-6 s (at x1) after the last Knight's impact
        public const int Stagger = 45;                                          // Knights fall one after another, ~0.75 s apart

        public static bool CanDrop(IncidentParms parms, Map map)
        {
            return parms.quest == null && parms.questTag == null && !OrbitalDropUtil.InOrbit(map) && KnightDropDefOf.LIK_KnightDropIncoming != null;
        }

        /// <summary>The raid comes down in drop pods (edge / centre / random / cluster drops, modded ones too - every pod worker is named *Drop*).</summary>
        public static bool IsDropArrival(PawnsArrivalModeDef mode)
        {
            return mode?.Worker != null && mode.Worker.GetType().Name.Contains("Drop");
        }

        /// <summary>Settles the raid strategy and arrival mode now (vanilla only picks them when unset, so it keeps these), to know before
        /// the raid starts whether it comes by pod or on foot. Virtual calls - a friendly raid uses its own choice.</summary>
        public static void ResolveArrival(IncidentWorker_Raid worker, IncidentParms parms)
        {
            try
            {
                if (parms.raidStrategy == null) AccessTools.Method(typeof(IncidentWorker_Raid), "ResolveRaidStrategy")?.Invoke(worker, new object[] { parms, PawnGroupKindDefOf.Combat });
                if (parms.raidArrivalMode == null) AccessTools.Method(typeof(IncidentWorker_Raid), "ResolveRaidArriveMode")?.Invoke(worker, new object[] { parms });
            }
            catch (System.Exception e) { Log.Warning("[LIK] Could not settle the raid's arrival early: " + e.Message); }
        }

        /// <summary>user 2026-11-25 "the spread out raid style doesn't work well with the Knights - troops would stay near the Knights": a raid
        /// that fields Knights never comes scattered or split into groups. Scattered pods -> one drop onto the base (CenterDrop), split pod groups
        /// -> one edge drop, split / distributed walk-ins -> one walk-in. The Knights land / walk in with the one body of troops.</summary>
        public static void KeepTogether(IncidentParms parms, Faction f)
        {
            string was = parms.raidArrivalMode?.defName;
            string to = was == "RandomDrop" ? "CenterDrop"
                      : was == "EdgeDropGroups" ? "EdgeDrop"
                      : was == "EdgeWalkInGroups" || was == "EdgeWalkInDistributed" || was == "EdgeWalkInDistributedGroups" ? "EdgeWalkIn"
                      : null;
            if (to == null) return;
            PawnsArrivalModeDef d = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(to);
            if (d == null) return;
            parms.raidArrivalMode = d;
            parms.spawnCenter = IntVec3.Invalid;                 // let the new arrival pick its own spot
            if (Prefs.DevMode) Log.Message("[LIK] " + f?.Name + " raid with Knights keeps together: " + was + " -> " + to);
        }

        static float ClearRadius(VehicleDef d)
        {
            int size = Mathf.Max(d.size.x, d.size.z);
            float radius = size * 0.5f + 3.5f;                           // same reach as KnightDropFaller.Impact
            ThingDef cr = DefDatabase<ThingDef>.GetNamedSilentFail("LIK_KnightCrater_" + d.defName.Replace("LIK_", ""));
            float clear = Mathf.Max(radius, cr?.graphicData != null ? cr.graphicData.drawSize.x * 0.42f : radius);
            return Mathf.Min(clear, GenRadial.MaxRadialPatternRadius - 1f);
        }

        /// <summary>A landing spot whose whole impact crater (which obliterates what is in it) holds nothing of the player's: no buildings,
        /// no player pawns, no Home area - and away from the other Knights of the drop. Searched outward from where the pods land, so a
        /// centre drop onto the base puts the Knights on the nearest open ground around it.</summary>
        static bool FindSpot(VehiclePawn v, Map map, IntVec3 focus, List<KeyValuePair<IntVec3, float>> taken, out IntVec3 spot)
        {
            spot = IntVec3.Invalid;
            float clear = ClearRadius(v.VehicleDef);
            bool onBase = LIKMod.I.raidDropOnBase;                                         // setting: may land in the base (user 2026-11-25)
            Area home = onBase ? null : map.areaManager.Home;
            float reach = Mathf.Min(60f, GenRadial.MaxRadialPatternRadius - 1f);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(focus, reach, true))
            {
                if (((c.x + c.z) & 1) != 0 || !c.InBounds(map)) continue;                     // every other cell is plenty
                if (home != null && home[c]) continue;
                if (taken.Any(o => o.Key.DistanceTo(c) < (o.Value + clear) * 0.8f)) continue;
                if (OrbitalDropUtil.LandingProblem(v, map, c) != null) continue;
                if (onBase ? !NoPawnUnder(v, map, c) : !Safe(map, c, clear)) continue;
                spot = c; return true;
            }
            return false;
        }

        /// <summary>"Drop into your base" on: anything may be flattened, but no player pawn stands under the Knight's own footprint (crushed outright).</summary>
        static bool NoPawnUnder(VehiclePawn v, Map map, IntVec3 c)
        {
            foreach (IntVec3 x in OrbitalDropUtil.Footprint(v, c).ExpandedBy(1))
            {
                if (!x.InBounds(map)) continue;
                foreach (Thing t in x.GetThingList(map))
                    if (t is Pawn p && (p.Faction == Faction.OfPlayer || p.IsColonist || p.IsPrisonerOfColony)) return false;
            }
            return true;
        }

        static bool Safe(Map map, IntVec3 c, float clear)
        {
            Area home = map.areaManager.Home;
            foreach (IntVec3 x in GenRadial.RadialCellsAround(c, clear, true))
            {
                if (!x.InBounds(map)) continue;
                if (home != null && home[x]) return false;
                RoofDef roof = x.GetRoof(map);
                if (roof != null && !roof.isNatural) return false;               // built roof = somebody's building
                foreach (Thing t in x.GetThingList(map))
                {
                    if (t.Faction == Faction.OfPlayer && (t is Building || t is Pawn)) return false;
                    if (t is Pawn p && p.IsColonist) return false;
                }
            }
            return true;
        }

        /// <summary>Drops the raid's Knights now; returns the classes that could not drop (Armigers, no safe spot) - they walk in later.</summary>
        public static List<KnightClassDef> DropKnights(Faction f, Map map, IntVec3 focus, List<KnightClassDef> classes)
        {
            var walkers = new List<KnightClassDef>();
            var fallers = new List<KnightDropFaller>();
            var taken = new List<KeyValuePair<IntVec3, float>>();
            foreach (KnightClassDef cls in classes)
            {
                if (cls.vehicle == null || cls.vehicle.defName == "LIK_Armiger") { walkers.Add(cls); continue; }   // Armigers never drop
                VehiclePawn k = KnightHouseUtil.GenerateCrewedKnight(f, cls);
                if (k == null) continue;
                IntVec3 spot;
                if (!FindSpot(k, map, focus, taken, out spot)) { k.Destroy(); walkers.Add(cls); continue; }
                taken.Add(new KeyValuePair<IntVec3, float>(spot, ClearRadius(k.VehicleDef)));
                var faller = (KnightDropFaller)ThingMaker.MakeThing(KnightDropDefOf.LIK_KnightDropIncoming);
                faller.Load(k);
                faller.raidDrop = true; faller.seekOnLanding = false;
                faller.delayTicks = 30 + fallers.Count * Stagger;
                GenSpawn.Spawn(faller, spot, map, Rot4.South);
                fallers.Add(faller);
            }
            if (fallers.Count > 0)
            {
                bool hostile = f.HostileTo(Faction.OfPlayer);
                string list = string.Join(", ", fallers.Select(x => x.KnightLabel));
                string text = hostile
                    ? f.Name + " opens its attack from orbit. Knights are falling towards the battlefield: " + list + ".\n\nTheir household troops will follow behind them in drop pods."
                    : f.Name + " sends its Knights to your aid, dropping from orbit: " + list + ".\n\nTheir household troops will follow behind them in drop pods.";
                Find.LetterStack.ReceiveLetter(hostile ? "Knights from orbit" : "Knights to your aid", text,
                    hostile ? LetterDefOf.ThreatBig : LetterDefOf.PositiveEvent, new LookTargets(fallers.Cast<Thing>()), f);
                if (Prefs.DevMode) Log.Message("[LIK] Knight raid drop by " + f.Name + ": " + list + (walkers.Count > 0 ? " (walking in later: " + string.Join(", ", walkers.Select(c => c.label)) + ")" : ""));
            }
            return walkers;
        }
    }

    /// <summary>The rest of a Knight-house raid, waiting to arrive behind the Knights that dropped (saved with the game).</summary>
    public class GameComponent_KnightRaidFollow : GameComponent
    {
        public class Entry : IExposable
        {
            public IncidentDef def; public Map map; public Faction faction; public float points;
            public RaidStrategyDef strategy; public PawnsArrivalModeDef arrival; public IntVec3 edge; public int fireTick;
            public List<KnightClassDef> walkers = new List<KnightClassDef>();
            public void ExposeData()
            {
                Scribe_Defs.Look(ref def, "def"); Scribe_References.Look(ref map, "map"); Scribe_References.Look(ref faction, "faction");
                Scribe_Values.Look(ref points, "points"); Scribe_Defs.Look(ref strategy, "strategy"); Scribe_Defs.Look(ref arrival, "arrival");
                Scribe_Values.Look(ref edge, "edge"); Scribe_Values.Look(ref fireTick, "fireTick");
                Scribe_Collections.Look(ref walkers, "walkers", LookMode.Def);
                if (Scribe.mode == LoadSaveMode.PostLoadInit && walkers == null) walkers = new List<KnightClassDef>();
            }
        }

        List<Entry> entries = new List<Entry>();
        public GameComponent_KnightRaidFollow(Game game) { }
        public static GameComponent_KnightRaidFollow Instance { get { return Current.Game?.GetComponent<GameComponent_KnightRaidFollow>(); } }

        public static bool Firing;                                 // the follow-up raid is running: no new Knights, no drop
        public static List<KnightClassDef> FiringWalkers;          // ... but these Knights (Armigers / no landing spot) walk in with it

        public void Add(Entry e) { entries.Add(e); }

        /// <summary>A house's troops are still on their way to this map (its dropped Knights wait for them instead of going home).</summary>
        public static bool Pending(Map map, Faction f)
        {
            var me = Instance;
            return me != null && me.entries.Any(e => e.map == map && e.faction == f);
        }

        public override void GameComponentTick()
        {
            if (entries.Count == 0 || Find.TickManager.TicksGame % 60 != 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (Entry e in entries.Where(x => now >= x.fireTick).ToList())
            {
                entries.Remove(e);
                if (e.def == null || e.map == null || !Find.Maps.Contains(e.map) || e.faction == null || e.faction.defeated) continue;
                var parms = new IncidentParms { target = e.map, faction = e.faction, points = e.points, raidStrategy = e.strategy, raidArrivalMode = e.arrival, forced = true };
                if (e.edge.IsValid && e.edge.InBounds(e.map)) parms.spawnCenter = e.edge;
                Firing = true; FiringWalkers = e.walkers;
                try
                {
                    if (!e.def.Worker.TryExecute(parms) && e.walkers.Count > 0)         // no infantry could come: the walkers still do
                        KnightRaidUtil.SpawnKnights(e.faction, e.map, e.edge, e.walkers);
                }
                catch (System.Exception ex) { Log.Error("[LIK] Knight raid follow-up failed: " + ex); }
                finally { Firing = false; FiringWalkers = null; }
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (entries == null) entries = new List<Entry>();
                entries.RemoveAll(e => e == null || e.map == null || e.def == null);
            }
        }
    }

    /// <summary>Raids by a Knight house: points for Knights are taken off before the infantry is generated. The Knights drop from orbit
    /// first and the infantry follows (KnightRaidDrop); where they cannot drop, the Knights walk in right after the raid.</summary>
    [HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
    public static class Patch_KnightHouseRaid
    {
        static bool Prefix(IncidentWorker_Raid __instance, IncidentParms parms, ref bool __result, out List<KnightClassDef> __state)
        {
            __state = null;
            if (GameComponent_KnightRaidFollow.Firing) { __state = GameComponent_KnightRaidFollow.FiringWalkers; return true; }   // the follow-up
            Faction f = parms?.faction;
            if (!KnightHouseUtil.IsKnightHouse(f) || !(parms.target is Map map)) return true;
            float spent;
            List<KnightClassDef> knights = KnightRaidUtil.Pick(f, parms.points, out spent);
            if (knights.Count == 0) return true;
            if (KnightRaidUtil.LastType != null)
                Messages.Message(f.Name + " comes as " + KnightRaidUtil.LastType.name + " (" + string.Join(", ", knights.Select(c => c.label)) + ").",
                    MessageTypeDefOf.NeutralEvent, false);
            parms.points = Mathf.Max(parms.points - spent * KnightRaidUtil.KnightWorth, parms.points * KnightRaidUtil.KeepInfantry);

            var follow = GameComponent_KnightRaidFollow.Instance;
            bool droppable = knights.Any(c => c.vehicle != null && c.vehicle.defName != "LIK_Armiger");
            KnightRaidDrop.ResolveArrival(__instance, parms);
            KnightRaidDrop.KeepTogether(parms, f);
            if (follow != null && droppable && KnightRaidDrop.CanDrop(parms, map) && KnightRaidDrop.IsDropArrival(parms.raidArrivalMode))
            {
                try
                {
                    // where the pods will come down (the arrival worker keeps a centre that is already set, so the pods follow the Knights there)
                    if (!parms.spawnCenter.IsValid) parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms);
                    IntVec3 edge = parms.spawnCenter;
                    if (!edge.IsValid || !edge.InBounds(map)) edge = map.Center;
                    List<KnightClassDef> walkers = KnightRaidDrop.DropKnights(f, map, edge, knights);
                    if (walkers.Count < knights.Count)                                  // at least one Knight is falling: the rest comes later
                    {
                        follow.Add(new GameComponent_KnightRaidFollow.Entry
                        {
                            def = __instance.def, map = map, faction = f, points = parms.points, strategy = parms.raidStrategy, arrival = parms.raidArrivalMode,
                            edge = edge, fireTick = Find.TickManager.TicksGame + 50 + (knights.Count - walkers.Count) * KnightRaidDrop.Stagger   // last Knight's impact
                                       + Rand.Range(KnightRaidDrop.FollowMinTicks, KnightRaidDrop.FollowMaxTicks),
                            walkers = walkers
                        });
                        __result = true;
                        return false;                                                   // no infantry now
                    }
                    knights = walkers;                                                  // nothing could drop: walk in as before
                }
                catch (System.Exception e) { Log.Error("[LIK] Knight raid drop failed, Knights walk in instead: " + e); }
            }
            __state = knights;
            return true;
        }

        static void Postfix(bool __result, IncidentParms parms, List<KnightClassDef> __state)
        {
            if (!__result || __state == null || __state.Count == 0 || !(parms.target is Map map)) return;
            // walk-in raid: from where the troops walk in. Pod raid (Armigers / Knights without a landing spot): from a map edge, not the pod spot
            IntVec3 near = KnightRaidDrop.IsDropArrival(parms.raidArrivalMode) ? IntVec3.Invalid : parms.spawnCenter;
            try { KnightRaidUtil.SpawnKnights(parms.faction, map, near, __state); }
            catch (System.Exception e) { Log.Error("[LIK] Knight raid spawn failed: " + e); }
        }
    }
}
