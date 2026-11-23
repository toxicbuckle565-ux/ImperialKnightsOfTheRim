using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// REACTOR MELTDOWN + ESCAPE (user 2026-11-22; lore: tabletop 'Deadly Demise' on every Knight - Gallant/Errant D6, Castellan D6+2,
    /// Porphyrion 2D6 - and the 'Noble Sacrifice' stratagem: "the scion overloads their Knight's plasma core to take the enemy with them"; no
    /// canon ejection seat, so the pilot climbs out through the carapace hatch).
    ///  - A destroyed Knight's reactor goes up 1 time in 6 (the tabletop roll). A pilot still inside dies.
    ///  - 'Overload reactor' (badly damaged Knight): 5 s countdown, then a bigger, certain blast. A pilot still inside dies - except, impossibly
    ///    rarely (1 %), when the blast takes down 3+ enemies or one big target: the Emperor saves them (downed, burned, 'saved by the Emperor').
    ///  - 'Abandon Knight': the pilot climbs out through the carapace hatch (3 s), then stands beside the Knight.
    /// </summary>
    public static class Meltdown
    {
        public const float NaturalChance = 1f / 6f, MiracleChance = 0.01f;
        public const int OverloadTicks = 300, ClimbTicks = 180, MiracleCheckTicks = 120;
        public const int BuildUpTicks = 90;                    // the meltdown sound starts with a 1.5 s build-up; the blast lands on its bang
        public static bool devNextSaved;                       // dev switch: the next sacrifice that meets the criteria is saved

        public static string Cls(VehiclePawn v) { KnightClassDef c = KnightLoadout.ClassFor(v.VehicleDef); return c == null ? "" : KnightPaint.ClassKey(c); }

        /// <summary>Blast radius (cells) and damage by class - Armiger small to Acastus biggest (tabletop D6 .. 2D6).</summary>
        public static void Blast(VehiclePawn v, bool overload, out float radius, out int damage) { Blast(Cls(v), overload, out radius, out damage); }

        public static void Blast(string cls, bool overload, out float radius, out int damage)
        {
            switch (cls)
            {
                // user 2026-11-22: 'the explosions should be much larger' (about double the radius of the first version: 3.9 / 5.9 / 6.9 / 8.9)
                case "Armiger": radius = 7.9f; damage = 60; break;
                case "Dominus": radius = 13.9f; damage = 110; break;
                case "Acastus": radius = 16.9f; damage = 140; break;
                default: radius = 11.9f; damage = 90; break;                    // Questoris, Cerastus
            }
            if (overload) { radius = Mathf.Min(radius * 1.5f, 24.9f); damage = Mathf.RoundToInt(damage * 1.6f); }
        }

        /// <summary>Badly damaged: reactor under half, or the Knight as a whole under half.</summary>
        public static bool BadlyDamaged(VehiclePawn v)
        {
            var comps = v.statHandler?.components;
            if (comps == null || comps.Count == 0) return false;
            float sum = 0f;
            foreach (VehicleComponent c in comps)
            {
                sum += c.HealthPercent;
                if (c.props?.key == "Reactor" && c.HealthPercent < 0.5f) return true;
            }
            return sum / comps.Count < 0.5f;
        }

        public static List<Pawn> Crew(VehiclePawn v) { return v.AllPawnsAboard.Where(p => p != null && !p.Dead).ToList(); }

        /// <summary>The blast itself: explosion, fire, crater, shockwave, quake. Returns the enemies in reach before it (for the miracle check).</summary>
        public static List<Pawn> Detonate(VehiclePawn v, bool overload)
        {
            return Detonate(v.Map, v.Position, v.TrueCenter(), Cls(v), overload, v.Faction, v);
        }

        /// <summary>The blast at a spot (the Knight may already be gone: a natural meltdown goes off after its build-up). Plays no sound -
        /// LIK_Snd_reactor_meltdown was started BuildUpTicks earlier so its bang lands now.</summary>
        public static List<Pawn> Detonate(Map map, IntVec3 c, Vector3 cv, string cls, bool overload, Faction side, Thing knight)
        {
            Blast(cls, overload, out float radius, out int dmg);
            var enemies = new List<Pawn>();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                if (p != knight && !p.Dead && !p.Downed && p.HostileTo(side ?? Faction.OfPlayer) && p.Position.InHorDistOf(c, radius)) enemies.Add(p);
            FleckMaker.Static(cv, map, FleckDefOf.ExplosionFlash, radius * 2.2f);
            // user 2026-11-22: exposure flash, darkness, glowing fireball + crater, mushroom cloud on the bigger Knights (MeltdownFx.cs)
            map.GetComponent<MapComponent_MeltdownFx>()?.Start(cv, radius, cls == "Armiger" ? 0f : (cls == "Questoris" || cls == "Cerastus") ? 0.6f : 1f);
            FleckDef wave = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_Shockwave_L");
            if (wave != null) FleckMaker.Static(cv, map, wave, radius * 1.3f);
            for (int i = 0; i < 12; i++) FleckMaker.ThrowSmoke(cv + Gen.RandomHorizontalVector(radius * 0.5f), map, radius * 0.35f);
            Find.CameraDriver.shaker.DoShake(4f + radius * 0.5f);
            if (Find.CurrentMap == map) KnightQuake.Start(0.6f + radius * 0.06f, 1.6f);
            GenExplosion.DoExplosion(c, map, radius, DamageDefOf.Bomb, knight, dmg, 0.6f, chanceToStartFire: 0.5f, damageFalloff: true,
                ignoredThings: knight != null ? new List<Thing> { knight } : null, doSoundEffects: false, screenShakeFactor: 0f);
            Devastate(map, c, radius, dmg, knight);
            ThingDef crater = DefDatabase<ThingDef>.GetNamedSilentFail("LIK_KnightCrater_" + cls);
            if (crater != null && c.InBounds(map) && !c.GetTerrain(map).IsWater) GenSpawn.Spawn(crater, c, map, WipeMode.Vanish);
            return enemies;
        }

        /// <summary>
        /// user 2026-11-22: 'buildings less than 10 tiles from the centre still stand, same with the more powerful enemies' - the vanilla
        /// explosion falls off steeply, is stopped by walls and barely scratches heavy armour. On top of it:
        ///  - the CORE (60 % of the radius) is obliterated like the drop crater: buildings (walls too - no cover from a reactor), items and
        ///    corpses are destroyed, every pawn dies (enemy vehicles too - a Knight caught in it may go up in turn), built roofs cave in,
        ///    plants are flattened by the outward wave. Natural rock stands.
        ///  - the BLAST ZONE (core .. radius): a pressure wave that ignores walls and most armour, falling off to 40 % at the edge.
        /// </summary>
        static void Devastate(Map map, IntVec3 c, float radius, int dmg, Thing knight)
        {
            float r = Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f), core = r * 0.6f;
            var cells = GenRadial.RadialCellsAround(c, r, true).Where(x => x.InBounds(map)).ToList();
            var done = new HashSet<Thing>();
            var coreRoof = new List<IntVec3>();
            foreach (IntVec3 x in cells)
            {
                float d = x.DistanceTo(c);
                bool inCore = d <= core;
                if (inCore && x.Roofed(map) && !x.GetRoof(map).isThickRoof) coreRoof.Add(x);
                foreach (Thing t in x.GetThingList(map).ToList())
                {
                    if (t == knight || t.Destroyed || t is Mote || t is Filth || t is Plant || !done.Add(t)) continue;
                    if (t is Pawn p)
                    {
                        if (p.Dead) continue;
                        if (inCore) { p.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f, 99f, -1f, knight)); continue; }
                        float f = 1f - 0.6f * (d - core) / Mathf.Max(0.1f, r - core);
                        for (int i = 0; i < 3 && !p.Dead && !p.Destroyed; i++)
                            p.TakeDamage(new DamageInfo(DamageDefOf.Bomb, dmg * f * 0.6f, 2f, -1f, knight));
                        continue;
                    }
                    if (t.def.category == ThingCategory.Building)
                    {
                        if (t.def.building != null && t.def.building.isNaturalRock) continue;
                        if (inCore) { if (t.def.destroyable) t.Destroy(t.def.useHitPoints ? DestroyMode.KillFinalize : DestroyMode.Vanish); }
                        else if (t.def.useHitPoints)
                        {
                            float f = 1f - 0.6f * (d - core) / Mathf.Max(0.1f, r - core);
                            t.TakeDamage(new DamageInfo(DamageDefOf.Bomb, dmg * f * 2f, 2f, -1f, knight));
                        }
                        continue;
                    }
                    if (inCore && (t.def.category == ThingCategory.Item || t is Corpse) && t.def.destroyable) t.Destroy(DestroyMode.Vanish);
                }
            }
            if (coreRoof.Count > 0) RoofCollapserImmediate.DropRoofInCells(coreRoof, map);
            map.GetComponent<MapComponent_ImpactClear>()?.Add(c, core);
        }

        public static SoundDef MeltdownSound => DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_reactor_meltdown");
        public static SoundDef AlarmSound => DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_reactor_alarm");

        public static bool Big(Pawn p) { return p is VehiclePawn || (p.RaceProps != null && p.RaceProps.baseBodySize >= 2.5f); }
    }

    /// <summary>A destroyed Knight: 1 in 6 its reactor goes up (or always, at the end of an overload). Pilots still inside are held back from
    /// VF's disembark and dealt with after the blast.</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.Kill), new[] { typeof(DamageInfo?), typeof(DestroyMode), typeof(bool) })]
    public static class Patch_KnightMeltdownOnDeath
    {
        static void Prefix(VehiclePawn __instance, out MeltdownState __state)
        {
            __state = null;
            VehiclePawn v = __instance;
            if (v == null || !v.Spawned || v.def.GetModExtension<DefModExtension_KnightToll>() == null) return;
            MapComponent_Meltdown mc = v.Map.GetComponent<MapComponent_Meltdown>();
            bool overload = mc != null && mc.IsOverloading(v);
            // destroyed mid-countdown, before the build-up started: it goes the natural way (build-up now, blast 1.5 s later)
            if (overload && mc.OverloadLeft(v) > Meltdown.BuildUpTicks) { mc.ClearOverload(v); overload = false; goto critical; }
            if (!overload && !Rand.Chance(Meltdown.NaturalChance)) return;
            critical:
            __state = new MeltdownState { overload = overload, map = v.Map, pos = v.Position, label = v.LabelShort, knightFaction = v.Faction };
            // the crew does not get out: take them out of the throne now (unspawned), their fate is decided after the blast
            foreach (Pawn p in Meltdown.Crew(v))
            {
                VehicleRoleHandler h = v.handlers.FirstOrDefault(x => x.thingOwner != null && x.thingOwner.Contains(p));
                if (h != null && v.TryRemovePawn(p, h)) __state.crew.Add(p);
            }
            if (overload)
            {
                // the build-up already played over the last 1.5 s of the countdown (MapComponent_Meltdown) - the bang is now
                __state.enemies = Meltdown.Detonate(v, true);
                mc?.ClearOverload(v);
            }
            else
            {
                // user 2026-11-22: 'a reactor meltdown needs a build-up, then the explosion' - the core goes critical as the Knight falls: the
                // meltdown sound starts (1.5 s build-up), the exposed core glows, and the blast lands on the bang
                Meltdown.MeltdownSound?.PlayOneShot(new TargetInfo(v.Position, v.Map));
                mc?.ScheduleNatural(v, __state.crew);
                __state.natural = true;
            }
        }

        static void Postfix(MeltdownState __state)
        {
            if (__state == null || __state.natural) return;          // natural: the scheduled blast deals with the crew
            __state.map.GetComponent<MapComponent_Meltdown>()?.AfterBlast(__state);
        }
    }

    public class MeltdownState
    {
        public bool overload, natural; public Map map; public IntVec3 pos; public string label; public Faction knightFaction;
        public List<Pawn> crew = new List<Pawn>();
        public List<Pawn> enemies = new List<Pawn>();
    }

    /// <summary>Overload countdowns, hatch climbs, and the crew held 'in the blast' until their fate is known (saved with the map).</summary>
    public class MapComponent_Meltdown : MapComponent, IThingHolder
    {
        class Pending : IExposable
        {
            public int due; public IntVec3 pos; public string label; public List<Pawn> enemies = new List<Pawn>(); public List<int> crewIds = new List<int>();
            public void ExposeData()
            {
                Scribe_Values.Look(ref due, "due"); Scribe_Values.Look(ref pos, "pos"); Scribe_Values.Look(ref label, "label");
                Scribe_Collections.Look(ref enemies, "enemies", LookMode.Reference); Scribe_Collections.Look(ref crewIds, "crewIds", LookMode.Value);
                if (Scribe.mode == LoadSaveMode.PostLoadInit) { enemies = enemies?.Where(x => x != null).ToList() ?? new List<Pawn>(); crewIds = crewIds ?? new List<int>(); }
            }
        }

        /// <summary>A Knight that fell with its reactor going critical: the blast goes off BuildUpTicks later at its spot.</summary>
        class Natural : IExposable
        {
            public int due; public IntVec3 pos; public Vector3 cv; public string cls, label; public Faction side; public List<int> crewIds = new List<int>();
            public void ExposeData()
            {
                Scribe_Values.Look(ref due, "due"); Scribe_Values.Look(ref pos, "pos"); Scribe_Values.Look(ref cv, "cv");
                Scribe_Values.Look(ref cls, "cls"); Scribe_Values.Look(ref label, "label"); Scribe_References.Look(ref side, "side");
                Scribe_Collections.Look(ref crewIds, "crewIds", LookMode.Value);
                if (Scribe.mode == LoadSaveMode.PostLoadInit) crewIds = crewIds ?? new List<int>();
            }
        }
        List<Natural> naturals = new List<Natural>();
        readonly Dictionary<VehiclePawn, Sustainer> alarms = new Dictionary<VehiclePawn, Sustainer>();   // not saved: restarted on load

        public void ScheduleNatural(VehiclePawn v, List<Pawn> crew)
        {
            var n = new Natural { due = Find.TickManager.TicksGame + Meltdown.BuildUpTicks, pos = v.Position, cv = v.TrueCenter(), cls = Meltdown.Cls(v), label = v.LabelShort, side = v.Faction };
            foreach (Pawn p in crew) { held.TryAdd(p); n.crewIds.Add(p.thingIDNumber); }
            naturals.Add(n);
        }

        void FireNatural(Natural n)
        {
            Meltdown.Detonate(map, n.pos, n.cv, n.cls, false, n.side, null);
            var crew = held.InnerListForReading.Where(p => n.crewIds.Contains(p.thingIDNumber)).ToList();
            foreach (Pawn p in crew) { held.Remove(p); KillInBlast(p, n.pos); }
            if (crew.Count > 0)
                Find.LetterStack.ReceiveLetter("Reactor meltdown", n.label + "'s reactor went critical as the Knight fell. " + string.Join(", ", crew.Select(p => p.LabelShort)) +
                    (crew.Count == 1 ? " was" : " were") + " still in the Throne Mechanicum.", LetterDefOf.Death, new TargetInfo(n.pos, map));
        }

        void StopAlarm(VehiclePawn v)
        {
            if (v != null && alarms.TryGetValue(v, out Sustainer s)) { if (s != null && !s.Ended) s.End(); alarms.Remove(v); }
        }

        Dictionary<VehiclePawn, int> overloads = new Dictionary<VehiclePawn, int>();      // Knight -> tick it blows
        Dictionary<VehiclePawn, int> climbs = new Dictionary<VehiclePawn, int>();         // Knight -> tick the pilot is out
        ThingOwner<Pawn> held;                                                            // crew caught in an overload, fate pending
        List<Pending> pending = new List<Pending>();
        List<VehiclePawn> tmpK; List<int> tmpV; List<VehiclePawn> tmpK2; List<int> tmpV2;

        public MapComponent_Meltdown(Map map) : base(map) { held = new ThingOwner<Pawn>(this, false, LookMode.Deep); }
        public ThingOwner GetDirectlyHeldThings() { return held; }
        public void GetChildHolders(List<IThingHolder> outChildren) { ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings()); }
        public IThingHolder ParentHolder => null;

        public bool IsOverloading(VehiclePawn v) { return overloads.ContainsKey(v); }
        public int OverloadLeft(VehiclePawn v) { return overloads.TryGetValue(v, out int t) ? t - Find.TickManager.TicksGame : -1; }
        public bool IsClimbing(VehiclePawn v) { return climbs.ContainsKey(v); }
        public void ClearOverload(VehiclePawn v) { overloads.Remove(v); StopAlarm(v); }

        public void StartOverload(VehiclePawn v)
        {
            overloads[v] = Find.TickManager.TicksGame + Meltdown.OverloadTicks;
            Messages.Message(v.LabelShortCap + "'s pilot has overloaded the plasma core. It will go critical in " + (Meltdown.OverloadTicks / 60) + " seconds.",
                v, MessageTypeDefOf.ThreatBig, false);
            // the alarm (LIK_Snd_reactor_alarm) is started and kept running by MapComponentTick
        }

        public void CancelOverload(VehiclePawn v)
        {
            if (!overloads.Remove(v)) return;
            StopAlarm(v);
            Messages.Message(v.LabelShortCap + ": reactor overload aborted - the core settles.", v, MessageTypeDefOf.NeutralEvent, false);
        }

        public void StartClimb(VehiclePawn v)
        {
            climbs[v] = Find.TickManager.TicksGame + Meltdown.ClimbTicks;
            if (v.ignition != null && v.ignition.Drafted) v.ignition.Drafted = false;
            v.jobs?.StopAll();
            Messages.Message("The carapace hatch of " + v.LabelShort + " opens - the pilot is climbing out.", v, MessageTypeDefOf.NeutralEvent, false);
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (climbs.Count > 0)
                foreach (VehiclePawn v in climbs.Keys.ToList())
                {
                    if (v == null || v.Destroyed || !v.Spawned) { climbs.Remove(v); continue; }
                    if (now % 30 == 0) FleckMaker.ThrowDustPuffThick(v.TrueCenter() + new Vector3(0f, 0f, 1.5f), map, 1.2f, new Color(0.7f, 0.7f, 0.7f));
                    if (now < climbs[v]) continue;
                    climbs.Remove(v);
                    if (Meltdown.Crew(v).Count == 0) continue;
                    v.DisembarkAll();
                    Messages.Message("The pilot of " + v.LabelShort + " is out of the hatch.", v, MessageTypeDefOf.NeutralEvent, false);
                }
            if (overloads.Count > 0)
                foreach (VehiclePawn v in overloads.Keys.ToList())
                {
                    if (v == null || v.Destroyed || !v.Spawned) { overloads.Remove(v); StopAlarm(v); continue; }
                    int left = overloads[v] - now;
                    // constant heavy alarm for the whole countdown (user 2026-11-22: no charge-up)
                    if (!alarms.TryGetValue(v, out Sustainer al) || al == null || al.Ended)
                    {
                        SoundDef ad = Meltdown.AlarmSound;
                        alarms[v] = ad?.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(v), MaintenanceType.PerTick));
                    }
                    alarms[v]?.Maintain();
                    // the meltdown sound starts with its 1.5 s build-up so the bang lands on the blast
                    if (left == Meltdown.BuildUpTicks) Meltdown.MeltdownSound?.PlayOneShot(new TargetInfo(v.Position, map));
                    // user 2026-11-22: no charge-up flashes and NO pulsing - the Knight heats up steadily, red-hot (MapComponentUpdate draws it);
                    // a few embers come off it in the last two seconds
                    if (left < 120 && now % 10 == 0) FleckMaker.ThrowMicroSparks(v.TrueCenter() + Gen.RandomHorizontalVector(1.5f), map);
                    if (left == 180 || left == 120 || left == 60) MoteMaker.ThrowText(v.TrueCenter() + new Vector3(0f, 0f, 2f), map, (left / 60).ToString(), Color.red, 1.5f);
                    if (left > 0) continue;
                    v.Kill(null, DestroyMode.KillFinalize, false);      // Patch_KnightMeltdownOnDeath does the blast (overload = certain)
                }
            for (int i = naturals.Count - 1; i >= 0; i--)
            {
                Natural n = naturals[i];
                int nl = n.due - now;
                if (nl > 0 && now % 6 == 0) FleckMaker.ThrowMicroSparks(n.cv + Gen.RandomHorizontalVector(1.2f), map);
                if (nl > 0) continue;
                naturals.RemoveAt(i);
                FireNatural(n);
            }
            for (int i = pending.Count - 1; i >= 0; i--)
                if (now >= pending[i].due) { Resolve(pending[i]); pending.RemoveAt(i); }
        }

        /// <summary>The overloading Knight glowing red-hot: a steady rise from dull red to bright orange-white (no pulsing), drawn over it.</summary>
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            // a fallen Knight's exposed core during the build-up: a fast-growing white-orange glow at its spot
            foreach (Natural n in naturals)
            {
                float f = Mathf.Clamp01(1f - (n.due - Find.TickManager.TicksGame) / (float)Meltdown.BuildUpTicks);
                Meltdown.Blast(n.cls, false, out float r, out int _);
                float size = r * (0.5f + 1.1f * f * f);
                DropLaunchFx.Quad(DropLaunchFx.Glow, n.cv, AltitudeLayer.MoteOverhead.AltitudeFor(), size, size,
                    Color.Lerp(new Color(0.8f, 0.2f, 0.03f), new Color(1f, 0.85f, 0.6f), f) * (0.5f + 0.8f * f));
            }
            if (overloads.Count == 0) return;
            foreach (KeyValuePair<VehiclePawn, int> kv in overloads)
            {
                VehiclePawn v = kv.Key;
                if (v == null || !v.Spawned) continue;
                float f = Mathf.Clamp01(1f - (kv.Value - Find.TickManager.TicksGame) / (float)Meltdown.OverloadTicks);
                float heat = f * f * (3f - 2f * f);                                     // smooth, ever rising
                Color col = Color.Lerp(new Color(0.45f, 0.05f, 0.0f), new Color(1.0f, 0.55f, 0.2f), heat) * (0.35f + 0.9f * heat);
                col.a = 1f;
                Vector2 ds = v.VehicleDef.graphicData.drawSize;
                float size = Mathf.Max(ds.x, ds.y) * (0.85f + 0.25f * heat);
                Vector3 c = v.DrawPos;
                float alt = AltitudeLayer.MoteOverhead.AltitudeFor();
                DropLaunchFx.Quad(DropLaunchFx.Glow, c, alt, size, size, col);
                if (heat > 0.5f) DropLaunchFx.Quad(DropLaunchFx.Glow, c, alt + 0.01f, size * 0.55f, size * 0.55f, new Color(1f, 0.8f, 0.5f) * ((heat - 0.5f) * 1.6f));
            }
        }

        /// <summary>After the blast: a natural meltdown kills the crew at once; an overload decides after the explosion has done its work.</summary>
        public void AfterBlast(MeltdownState s)
        {
            if (!s.overload)
            {
                foreach (Pawn p in s.crew) KillInBlast(p, s.pos);
                if (s.crew.Count > 0)
                    Find.LetterStack.ReceiveLetter("Reactor meltdown", s.label + "'s reactor went critical as the Knight fell. " + string.Join(", ", s.crew.Select(p => p.LabelShort)) +
                        (s.crew.Count == 1 ? " was" : " were") + " still in the Throne Mechanicum.", LetterDefOf.Death, new TargetInfo(s.pos, map));
                return;
            }
            var pd = new Pending { due = Find.TickManager.TicksGame + Meltdown.MiracleCheckTicks, pos = s.pos, label = s.label, enemies = s.enemies };
            foreach (Pawn p in s.crew) { held.TryAdd(p); pd.crewIds.Add(p.thingIDNumber); }
            pending.Add(pd);
        }

        void Resolve(Pending pd)
        {
            var crew = held.InnerListForReading.Where(p => pd.crewIds.Contains(p.thingIDNumber)).ToList();
            int down = pd.enemies.Count(e => e != null && (e.Dead || e.Downed));
            bool big = pd.enemies.Any(e => e != null && (e.Dead || e.Downed) && Meltdown.Big(e));
            bool worthy = down >= 3 || big;
            foreach (Pawn p in crew)
            {
                held.Remove(p);
                bool saved = worthy && (Meltdown.devNextSaved || Rand.Chance(Meltdown.MiracleChance));
                if (saved)
                {
                    Meltdown.devNextSaved = false;
                    IntVec3 at = CellFinder.StandableCellNear(pd.pos, map, 3f);
                    if (!at.IsValid) at = pd.pos;
                    GenSpawn.Spawn(p, at, map);
                    HealthUtility.DamageUntilDowned(p, false, DamageDefOf.Flame);
                    p.health.AddHediff(DefDatabase<HediffDef>.GetNamed("LIK_SavedByEmperor"));
                    Find.LetterStack.ReceiveLetter("Saved by the Emperor", "The plasma core of " + pd.label + " consumed everything around it - " + down + " of the enemy fell with it. Yet in the glass of the crater " + p.LabelShort +
                        " still breathes, burned and broken but alive.\n\nNo one can explain it. The Emperor protects.", LetterDefOf.PositiveEvent, p);
                }
                else
                {
                    KillInBlast(p, pd.pos);
                    Find.LetterStack.ReceiveLetter("Noble sacrifice", p.LabelShortCap + " overloaded " + pd.label + "'s plasma core and took " + down + " of the enemy with them. Their name will be read in the halls of their house.",
                        LetterDefOf.Death, new TargetInfo(pd.pos, map));
                }
            }
        }

        void KillInBlast(Pawn p, IntVec3 pos)
        {
            if (p.Dead) return;
            if (!p.Spawned)
            {
                IntVec3 at = CellFinder.StandableCellNear(pos, map, 3f);
                GenSpawn.Spawn(p, at.IsValid ? at : pos, map);
            }
            p.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f, 99f));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref overloads, "overloads", LookMode.Reference, LookMode.Value, ref tmpK, ref tmpV);
            Scribe_Collections.Look(ref climbs, "climbs", LookMode.Reference, LookMode.Value, ref tmpK2, ref tmpV2);
            Scribe_Deep.Look(ref held, "held", this);
            Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
            Scribe_Collections.Look(ref naturals, "naturals", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                overloads = overloads ?? new Dictionary<VehiclePawn, int>(); overloads.RemoveAll(kv => kv.Key == null);
                climbs = climbs ?? new Dictionary<VehiclePawn, int>(); climbs.RemoveAll(kv => kv.Key == null);
                held = held ?? new ThingOwner<Pawn>(this, false, LookMode.Deep);
                pending = pending ?? new List<Pending>();
                naturals = naturals ?? new List<Natural>();
            }
        }
    }

    /// <summary>The Knight's buttons: Overload reactor (badly damaged), Abandon Knight (climb out through the hatch), DEV: next sacrifice saved.</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_MeltdownGizmos
    {
        static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            VehiclePawn v = __instance;
            if (v == null || !v.Spawned || v.Faction != Faction.OfPlayer || v.def.GetModExtension<DefModExtension_KnightToll>() == null) return;
            __result = __result.Concat(Extra(v));
        }

        static IEnumerable<Gizmo> Extra(VehiclePawn v)
        {
            MapComponent_Meltdown mc = v.Map.GetComponent<MapComponent_Meltdown>();
            if (mc == null) yield break;
            bool crew = Meltdown.Crew(v).Count > 0;
            if (mc.IsOverloading(v))
            {
                int left = Mathf.Max(0, mc.OverloadLeft(v)) / 60 + 1;
                yield return new Command_Action
                {
                    defaultLabel = "Abort overload (" + left + " s)",
                    defaultDesc = "Shut the overload down before the plasma core goes critical.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LIK_Overload", false) ?? TexCommand.ClearPrioritizedWork,
                    action = () => mc.CancelOverload(v)
                };
            }
            else
            {
                var ov = new Command_Action
                {
                    defaultLabel = "Overload reactor",
                    defaultDesc = "Noble Sacrifice: sensing the end, the pilot overloads the Knight's plasma core to take the enemy with them. In " + (Meltdown.OverloadTicks / 60) +
                                  " seconds the core goes critical - a far bigger blast than a Knight simply falling. Anyone still in the Throne Mechanicum dies (climb out first with 'Abandon Knight' to live).\n\nOnly when the Knight is badly damaged (reactor or the Knight as a whole under half).",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LIK_Overload", false) ?? TexCommand.Attack,
                    // user 2026-11-22: confirm first - no accidental overloads
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Overload " + v.LabelShort + "'s plasma core?\n\nIn " + (Meltdown.OverloadTicks / 60) + " seconds the Knight is destroyed in a reactor blast. Anyone still in the Throne Mechanicum dies unless they climb out first with 'Abandon Knight'. You can still abort during the countdown.",
                        () => mc.StartOverload(v), destructive: true, title: "Noble Sacrifice"))
                };
                if (!Meltdown.BadlyDamaged(v)) ov.Disable("The Knight is not badly damaged (reactor or the Knight as a whole must be under half).");
                yield return ov;
            }
            if (crew)
            {
                var ab = new Command_Action
                {
                    defaultLabel = mc.IsClimbing(v) ? "Climbing out..." : "Abandon Knight",
                    defaultDesc = "The pilot unplugs from the Throne Mechanicum and climbs out through the carapace hatch (" + (Meltdown.ClimbTicks / 60) + " s). The Knight stops while they climb; once out they are on foot.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LIK_Abandon", false) ?? TexCommand.ReleaseAnimals,
                    action = () => mc.StartClimb(v)
                };
                if (mc.IsClimbing(v)) ab.Disable("The pilot is already climbing out.");
                yield return ab;
            }
            if (Prefs.DevMode)
                yield return new Command_Toggle
                {
                    defaultLabel = "DEV: next sacrifice saved",
                    defaultDesc = "Dev mode only. The next Noble Sacrifice that takes down 3+ enemies or a big target saves its pilot (skips the 1 % roll).",
                    icon = TexCommand.DesirePower,
                    isActive = () => Meltdown.devNextSaved,
                    toggleAction = () => Meltdown.devNextSaved = !Meltdown.devNextSaved
                };
        }
    }
}
