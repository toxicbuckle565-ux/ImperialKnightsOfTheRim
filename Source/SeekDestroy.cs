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
    /// <summary>Which Knights have "Seek &amp; destroy" switched on (saved with the game, keyed by thingIDNumber).</summary>
    public class GameComponent_Seek : GameComponent
    {
        HashSet<int> on = new HashSet<int>();
        public static GameComponent_Seek Instance { get { return Current.Game?.GetComponent<GameComponent_Seek>(); } }
        public GameComponent_Seek(Game game) { }
        public bool Get(Thing t) { return on.Contains(t.thingIDNumber); }
        public void Set(Thing t, bool v) { if (v) on.Add(t.thingIDNumber); else on.Remove(t.thingIDNumber); }

        public override void ExposeData()
        {
            List<int> list = on.ToList();
            Scribe_Collections.Look(ref list, "seek", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) on = list != null ? new HashSet<int>(list) : new HashSet<int>();
        }
    }

    /// <summary>Adds the "Seek &amp; destroy" toggle to every player Knight (works on a multi-selection too).</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_KnightSeekGizmo
    {
        public static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(__instance.VehicleDef) == null) return;
            __result = __result.Concat(Make(__instance));
        }

        static IEnumerable<Gizmo> Make(VehiclePawn v)
        {
            GameComponent_Seek c = GameComponent_Seek.Instance;
            if (c == null) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "Seek & destroy",
                defaultDesc = "While on, this Knight hunts down hostiles on its own: it advances on the nearest enemy until its weapons are in range, " +
                              "and every weapon fires on whatever it can reach, each one picking its own target. Needs the engine running (ignition on)." +
                              (v.VehicleDef.defName == "LIK_Acastus" ? "\n\nAcastus: raises its rocket launcher while enemies are on the map and stows it when none are left." : ""),
                icon = TexCommand.Attack,
                isActive = () => c.Get(v),
                toggleAction = () => c.Set(v, !c.Get(v)),
                Disabled = !v.Drafted,
                disabledReason = "Start the engine first."
            };
        }
    }

    /// <summary>
    /// Drives Knights that have Seek &amp; destroy on. Weapons already pick their own targets through Vehicle Framework's per-turret auto-targeting
    /// (each turret scans its own range and firing arc), so this only (1) makes sure every turret's auto-targeting is on and (2) moves the Knight
    /// toward the nearest enemy until the weapons can engage.
    /// </summary>
    public class MapComponent_KnightSeek : MapComponent
    {
        const int Interval = 60;
        readonly Dictionary<int, IntVec3> lastDest = new Dictionary<int, IntVec3>();
        // Knights the player moved by hand: where to, and until when the Knight holds there at the very least
        readonly Dictionary<int, KeyValuePair<IntVec3, int>> manualHold = new Dictionary<int, KeyValuePair<IntVec3, int>>();
        const int HoldTicks = 600;      // 10 s

        // user 2026-11-15: "pathing bugs ... repeating cannot move there". A Knight is 6-8 cells wide and Vehicle Framework needs the WHOLE
        // footprint clear along the route; region reachability (CanReachVehicle) says yes to spots the real path search then rejects, and the
        // 1-second re-order loop repeated "Cannot path here" forever. Now: only spots where the whole Knight fits, and a spot that failed is
        // left alone for 10 s.
        readonly Dictionary<int, List<KeyValuePair<IntVec3, int>>> badDest = new Dictionary<int, List<KeyValuePair<IntVec3, int>>>();
        const int BadDestTicks = 600;
        /// <summary>Destinations this component ordered (thingIDNumber -> cell): Patch_QuietSeekPathFail hides VF's "cannot path" for these.</summary>
        public static readonly Dictionary<int, IntVec3> SeekOrders = new Dictionary<int, IntVec3>();

        bool IsBad(VehiclePawn v, IntVec3 c)
        {
            List<KeyValuePair<IntVec3, int>> l;
            if (!badDest.TryGetValue(v.thingIDNumber, out l)) return false;
            int now = Find.TickManager.TicksGame;
            l.RemoveAll(e => e.Value < now);
            foreach (var e in l) if (e.Key.DistanceToSquared(c) <= 9) return true;
            return false;
        }

        void MarkBad(VehiclePawn v, IntVec3 c)
        {
            List<KeyValuePair<IntVec3, int>> l;
            if (!badDest.TryGetValue(v.thingIDNumber, out l)) badDest[v.thingIDNumber] = l = new List<KeyValuePair<IntVec3, int>>();
            l.Add(new KeyValuePair<IntVec3, int>(c, Find.TickManager.TicksGame + BadDestTicks));
        }

        /// <summary>A cell the whole Knight can stand on (either facing) and walk to.</summary>
        bool Fits(VehiclePawn v, IntVec3 c)
        {
            if (!c.InBounds(map) || IsBad(v, c)) return false;
            if (!v.DrivableRectOnCell(c, Ext_Vehicles.DestinationHitboxReq.AnyRotation)) return false;
            return v.CanReachVehicle(c, PathEndMode.OnCell, Danger.Deadly, TraverseMode.ByPawn);
        }

        /// <summary>Our last order ended without the Knight getting there (no path): remember the spot as bad.</summary>
        void CheckFailed(VehiclePawn v)
        {
            IntVec3 last;
            if (!lastDest.TryGetValue(v.thingIDNumber, out last)) return;
            if (v.vehiclePather.Moving || (v.CurJobDef == JobDefOf.Goto && v.CurJob != null && v.CurJob.targetA.Cell == last)) return;
            if (v.Position.DistanceTo(last) > 2.5f) MarkBad(v, last);
            lastDest.Remove(v.thingIDNumber);
            lock (SeekOrders) SeekOrders.Remove(v.thingIDNumber);
        }

        void Order(VehiclePawn v, IntVec3 dest)
        {
            if (!dest.IsValid || !dest.InBounds(map)) return;
            Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            lock (SeekOrders) SeekOrders[v.thingIDNumber] = dest;
            if (v.jobs.TryTakeOrderedJob(job, JobTag.Misc, false)) lastDest[v.thingIDNumber] = dest;
            else lock (SeekOrders) SeekOrders.Remove(v.thingIDNumber);
        }

        bool EnemyInReach(VehiclePawn v, float range)
        {
            if (range <= 0f) return false;
            foreach (IAttackTarget at in map.attackTargetsCache.GetPotentialTargetsFor(v))
            {
                Thing th = at.Thing;
                if (th == null || !th.Spawned || at.ThreatDisabled(v) || !GenHostility.HostileTo(th, v)) continue;
                Pawn p = th as Pawn;
                if (p != null && (p.Downed || p.Dead)) continue;
                if (v.Position.DistanceTo(th.Position) <= range && GenSight.LineOfSight(v.Position, th.Position, map)) return true;
            }
            return false;
        }

        public MapComponent_KnightSeek(Map map) : base(map) { }

        // user 2026-11-24: conversion beams must be fired standing still (lore: moving negates the beam - Projectile_KnightShell fizzles it),
        // so Seek & destroy acknowledges them: when one starts charging, a Knight on Seek halts (only its own seek order is ended - a player
        // order is never touched) and makes no new move until the burst is away.
        readonly Dictionary<int, int> shotHold = new Dictionary<int, int>();

        /// <summary>A Knight of another faction (a Knight house's raider / reinforcement): always run by the Seek &amp; destroy AI.</summary>
        public static bool IsAIKnight(VehiclePawn v) => v?.Faction != null && v.Faction != Faction.OfPlayer && KnightLoadout.ClassFor(v.VehicleDef) != null;

        public static void HoldForShot(VehiclePawn v, int ticks)
        {
            if (v == null || !v.Spawned) return;
            GameComponent_Seek seek = GameComponent_Seek.Instance;
            if (!IsAIKnight(v) && (v.Faction != Faction.OfPlayer || seek == null || !seek.Get(v))) return;
            MapComponent_KnightSeek comp = v.Map.GetComponent<MapComponent_KnightSeek>();
            if (comp == null) return;
            int until = Find.TickManager.TicksGame + ticks;
            int had; comp.shotHold.TryGetValue(v.thingIDNumber, out had);
            comp.shotHold[v.thingIDNumber] = Mathf.Max(had, until);
            comp.StopOwnOrder(v);
            if (Prefs.DevMode) Log.Message("[LIK] seek & destroy: " + v.LabelShort + " halts for a conversion shot (" + ticks + " ticks)");
        }

        bool HoldingForShot(VehiclePawn v)
        {
            int until;
            if (!shotHold.TryGetValue(v.thingIDNumber, out until)) return false;
            if (Find.TickManager.TicksGame < until) return true;
            shotHold.Remove(v.thingIDNumber);
            return false;
        }

        /// <summary>Ends this component's own Goto (never a player's order).</summary>
        void StopOwnOrder(VehiclePawn v)
        {
            IntVec3 mine;
            Job cur = v.CurJob;
            if (cur == null || cur.def != JobDefOf.Goto || !lastDest.TryGetValue(v.thingIDNumber, out mine) || cur.targetA.Cell != mine) return;
            lastDest.Remove(v.thingIDNumber);
            lock (SeekOrders) SeekOrders.Remove(v.thingIDNumber);
            v.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 15 == 4) { DropGoneTargets(); PawnsFirst(); }
            if (Find.TickManager.TicksGame % Interval != 17) return;
            GameComponent_Seek seek = GameComponent_Seek.Instance;
            if (seek == null) return;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn).ToList())
            {
                VehiclePawn v = t as VehiclePawn;
                if (v == null) continue;
                // Knight raids (user 2026-11-24): every NON-player Knight (a Knight house's raiders or reinforcements) is driven by this AI too
                bool ai = IsAIKnight(v);
                if (!ai && (v.Faction != Faction.OfPlayer || !seek.Get(v))) continue;
                if (ai)
                {
                    if (v.Downed || v.Dead) continue;
                    // legs gone (VF turns the engine off at MoveSpeed <= 0.1): don't start it again - the Knight is crippled, its guns still fire
                    bool crippled = v.GetStatValue(VehicleStatDefOf.MoveSpeed) <= 0.1f;
                    if (!v.Drafted && !crippled) { try { if (v.ignition != null && v.CanDraft().Accepted) v.ignition.Drafted = true; } catch { } }
                    if (crippled) { if (v.vehiclePather.Moving) v.vehiclePather.StopDead(); continue; }
                    if (!v.Drafted) continue;                                   // no pilot aboard: the Knight stands
                }
                else if (!v.Drafted || v.Downed || v.Dead) { seek.Set(v, false); continue; }       // engine off: seeking ends
                if (KnightLanding.Recovering(v)) continue;          // still recovering from an orbital drop
                try { Launcher(v); Run(v); }
                catch (System.Exception e) { Log.ErrorOnce("[LIK] seek & destroy failed for " + v.LabelShort + ": " + e, v.thingIDNumber ^ 0x5eed); }
            }
        }

        /// <summary>
        /// User 2026-11-11: "when enemies leave the tile the mech still attacks the last area where the enemy was". Vehicle Framework only drops a
        /// target that died or went down; one that LEFT THE MAP (or was otherwise removed) keeps its last position, and the turret goes on shelling
        /// that spot. Four times a second every Knight weapon is checked: a target that is no longer on this map is forgotten. Ground the player
        /// aimed at on purpose (a cell, not a thing) is left alone. Applies to every Knight, with or without Seek and destroy.
        /// </summary>
        void DropGoneTargets()
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                VehiclePawn v = pawns[i] as VehiclePawn;
                if (v == null || v.CompVehicleTurrets == null || KnightLoadout.ClassFor(v.VehicleDef) == null) continue;
                IReadOnlyList<VehicleTurret> list = v.CompVehicleTurrets.Turrets;
                if (list == null) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    VehicleTurret tr = list[k];
                    if (!tr.targetInfo.IsValid || !tr.targetInfo.HasThing) continue;
                    Thing th = tr.targetInfo.Thing;
                    if (th == null || th.Destroyed || !th.Spawned || th.Map != map)
                    {
                        try { tr.SetTarget(LocalTargetInfo.Invalid); }
                        catch (System.Exception e) { Log.ErrorOnce("[LIK] could not clear a gone target: " + e, 77120612); }
                    }
                }
            }
        }

        // user 2026-11-13: Seek & destroy raises the Acastus rocket launcher while there are enemies, and stows it once none are left
        // (after a few quiet checks in a row, so a short gap between targets does not make it flap)
        readonly Dictionary<int, int> quietChecks = new Dictionary<int, int>();
        const int QuietChecksToStow = 3;      // x Interval (60 ticks) = ~3 s

        void Launcher(VehiclePawn v)
        {
            if (v.VehicleDef == null || v.VehicleDef.defName != "LIK_Acastus") return;
            bool enemies = FindTarget(v) != null;
            int id = v.thingIDNumber;
            if (enemies)
            {
                quietChecks[id] = 0;
                if (!Patch_RocketToggleGizmo.Raised(v)) Patch_RocketToggleGizmo.SetRaised(v, true);
                return;
            }
            int q; quietChecks.TryGetValue(id, out q); quietChecks[id] = ++q;
            if (q >= QuietChecksToStow && Patch_RocketToggleGizmo.Raised(v)) Patch_RocketToggleGizmo.SetRaised(v, false);
        }

        void Run(VehiclePawn v)
        {
            CompVehicleTurrets turrets = v.CompVehicleTurrets;
            if (turrets == null) return;
            float standoff = float.MaxValue;
            foreach (VehicleTurret tr in turrets.Turrets)
            {
                if (tr.CanAutoTarget && !tr.AutoTarget) tr.AutoTarget = true;                 // every gun scans for its own target
                if (tr.TurretDisabled || !tr.HasAmmo) continue;
                if (tr.def.maxRange > 0f) standoff = Mathf.Min(standoff, tr.MaxRange);        // close in until even the shortest-ranged working gun reaches
            }
            if (standoff == float.MaxValue) standoff = 25f;
            standoff = Mathf.Clamp(standoff * 0.85f, 3f, 40f);
            // user 2026-11-25 "the chain-cleaver never gets close enough": a melee arm (fitted, or the arm's other mode) sets the distance
            float melee = KnightFacing.MeleeReach(v);
            if (melee > 0f) standoff = Mathf.Max(2.5f, melee * 0.75f);
            float longest = 0f;
            foreach (VehicleTurret tr in turrets.Turrets)
                if (!tr.TurretDisabled && tr.HasAmmo && tr.def.maxRange > longest) longest = tr.MaxRange;

            // ---- the player's own movement orders come first (user 2026-11-11: "any inputs to movement or repositioning get overwritten")
            int id = v.thingIDNumber;
            int now = Find.TickManager.TicksGame;
            Job cur = v.CurJob;
            IntVec3 mine;
            bool ours = cur != null && cur.def == JobDefOf.Goto && lastDest.TryGetValue(id, out mine) && cur.targetA.Cell == mine;
            if (cur != null && !ours && (cur.playerForced || cur.def == JobDefOf.Goto))
            {
                // an order that did not come from here: leave it alone, and remember where it sends the Knight
                manualHold[id] = new KeyValuePair<IntVec3, int>(cur.targetA.IsValid ? cur.targetA.Cell : v.Position, now + HoldTicks);
                return;
            }
            KeyValuePair<IntVec3, int> hold;
            if (manualHold.TryGetValue(id, out hold))
            {
                // the Knight stays where the player put it for a while, and for as long as it has something to shoot at from there;
                // it only goes hunting again when that spot has nothing left in reach
                bool there = v.Position.DistanceTo(hold.Key) <= 3f;
                if (there && (now < hold.Value || EnemyInReach(v, longest))) { KnightFacing.FaceBest(v, map); return; }   // holding where the player put it: still turn to the enemy
                manualHold.Remove(id);
            }

            if (!v.CanMoveFinal) return;
            if (HoldingForShot(v)) { StopOwnOrder(v); return; }        // a conversion gun is charging / firing: stand still (user 2026-11-24)
            CheckFailed(v);
            // user 2026-11-25: an AI Knight shut in (dropped into a base) rams its way out towards where it wants to go (KnightRam.cs)
            if (IsAIKnight(v) && KnightRam.Step(v, map, c => Order(v, c))) return;
            if (WalkOverPawns(v)) return;
            Thing target = FindTarget(v);
            // user 2026-11-25 "friendly knights don't move with the caravan": a house Knight keeps with its own people - follows them when there is
            // nobody to fight, and leaves the map with them (or when none of them are left)
            // 2026-11-25 fix (user: raid at 10000 pts, no Knights): drop-pod raids - the infantry was still in the pods when the Knights spawned,
            // 'none of their people left' sent them off the map at once. Now: a grace period after arriving, and a hostile Knight with something
            // to fight never leaves just because its people are gone - only a real retreat (most of the group exiting) takes it home.
            // user 2026-11-25: the grace only stops a fresh Knight from LEAVING (its people may still be in drop pods) - it follows them at once
            if (IsAIKnight(v))
            {
                List<Pawn> g = Group(v);
                NoteKnights(v);
                bool fresh = JustArrived(v);
                bool retreat = !fresh && g.Count > 0 && g.Count(Leaving) * 2 > g.Count;
                if (retreat && v.Faction.HostileTo(Faction.OfPlayer)) retreat = WillRetreat(v);   // a hostile house would rather die (user 2026-11-25)
                if (retreat) { KnightRam.SetGoal(v, KnightRam.NearestEdge(v, map)); LeaveMap(v); return; }   // the raid broke / the visit is over: go with them
                if (target == null)                                            // nothing to fight: keep with them, or go home
                {
                    // user 2026-11-25: Knights that dropped ahead of their raid wait for the troops still on the way (GameComponent_KnightRaidFollow)
                    if (g.Count == 0)
                    {
                        if (!fresh && !GameComponent_KnightRaidFollow.Pending(map, v.Faction)) { KnightRam.SetGoal(v, KnightRam.NearestEdge(v, map)); LeaveMap(v); }
                        else KnightRam.SetGoal(v, IntVec3.Invalid);
                        return;
                    }
                    IntVec3 centre = new IntVec3((int)g.Average(p => p.Position.x), 0, (int)g.Average(p => p.Position.z));
                    KnightRam.SetGoal(v, v.Position.DistanceTo(centre) > 15f ? centre : IntVec3.Invalid);
                    Escort(v); return;
                }
                // something to fight: fight (a lone Knight fights on)
            }
            if (target == null)
            {
                // nobody left to hunt (dead, fled the map): stop marching to where the last one stood
                IntVec3 was;
                if (lastDest.TryGetValue(v.thingIDNumber, out was))
                {
                    lastDest.Remove(v.thingIDNumber);
                    if (v.vehiclePather.Moving && v.CurJobDef == JobDefOf.Goto && v.CurJob != null && v.CurJob.targetA.Cell == was)
                        v.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
                }
                return;
            }

            float dist = v.Position.DistanceTo(target.Position);
            bool engaged = dist <= standoff && GenSight.LineOfSight(v.Position, target.Position, map);
            if (IsAIKnight(v)) KnightRam.SetGoal(v, engaged ? IntVec3.Invalid : target.Position);   // nobody in its sights: it must get to them
            if (engaged)
            {
                KnightFacing.FaceBest(v, map);                                          // user 2026-11-25: turn the guns onto the most enemies
                lastDest.Remove(id); lock (SeekOrders) SeekOrders.Remove(id);         // stopping here on purpose is not a failed order
                if (v.vehiclePather.Moving && v.CurJobDef == JobDefOf.Goto) v.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
                return;
            }

            IntVec3 last;
            if (lastDest.TryGetValue(v.thingIDNumber, out last) && v.CurJobDef == JobDefOf.Goto && v.vehiclePather.Moving
                && last.DistanceTo(target.Position) < Mathf.Max(4f, standoff * 0.4f)) return;   // already heading there
            // still walking a leg of a long march (Leg) that brings it closer: let it finish instead of re-ordering every second
            if (lastDest.TryGetValue(v.thingIDNumber, out last) && v.CurJobDef == JobDefOf.Goto && v.vehiclePather.Moving
                && v.Position.DistanceTo(last) > 10f && last.DistanceTo(target.Position) < v.Position.DistanceTo(target.Position)) return;

            IntVec3 dest;
            if (!FindStandCell(v, target, standoff, melee > 0f, out dest)) return;
            Order(v, Leg(v, dest));
        }

        // ------------------------------------------------------------------ house Knights keep with their people (user 2026-11-25)
        public const int ArrivalGraceTicks = 900;                                   // 15 s (user: 60 s was too long) - drop pods land in a few seconds
        public static readonly Dictionary<int, int> Arrived = new Dictionary<int, int>();   // thingIDNumber -> tick (KnightRaidUtil.SpawnKnights)
        static bool JustArrived(VehiclePawn v)
        {
            int t;
            return Arrived.TryGetValue(v.thingIDNumber, out t) && Find.TickManager.TicksGame - t < ArrivalGraceTicks;
        }

        // ------------------------------------------------------------------ Knights rather die than retreat (user 2026-11-25)
        // "the knights keep retreating once most the pawns are downed ... have it dependent on the amount of remaining knights and greatly
        // reduce the probability": when a hostile house's infantry breaks, its Knights do NOT go with them by default. Each Knight rolls whether
        // it falls back, and the chance only grows as the house's Knights on this map fall: all Knights still standing = 0 % (they fight on),
        // the last of four = ~15 %, at most 20 %. It rolls again each time another Knight is lost; once it has decided to go, it goes.
        // A Knight that fights on still leaves when nothing is left to fight. Friendly / visiting Knights still leave with their people.
        const float MaxRetreatChance = 0.20f;
        static readonly Dictionary<string, int> peakKnights = new Dictionary<string, int>();               // map+faction -> most Knights seen
        static readonly Dictionary<int, KeyValuePair<int, bool>> retreatRoll = new Dictionary<int, KeyValuePair<int, bool>>();   // id -> (Knights alive at roll, retreat)

        bool WillRetreat(VehiclePawn v)
        {
            int alive = map.mapPawns.SpawnedPawnsInFaction(v.Faction).OfType<VehiclePawn>().Count(k => IsAIKnight(k) && !k.Downed && !k.Dead);
            string key = map.uniqueID + "/" + v.Faction.loadID;
            int peak;
            if (!peakKnights.TryGetValue(key, out peak) || alive > peak) peakKnights[key] = peak = alive;
            KeyValuePair<int, bool> r;
            if (retreatRoll.TryGetValue(v.thingIDNumber, out r) && (r.Value || r.Key <= alive)) return r.Value;   // decided, and no new losses since
            float lost = peak > 0 ? 1f - (float)alive / peak : 0f;
            bool go = Rand.Chance(MaxRetreatChance * lost);
            retreatRoll[v.thingIDNumber] = new KeyValuePair<int, bool>(alive, go);
            if (Prefs.DevMode) Log.Message("[LIK] " + v.LabelShort + " of " + v.Faction.Name + ": infantry broke, " + alive + "/" + peak + " Knights left, "
                + (MaxRetreatChance * lost * 100f).ToString("0") + "% to retreat -> " + (go ? "falls back" : "fights on"));
            return go;
        }

        /// <summary>Peak Knight count is tracked from every check, so losses before the infantry breaks count too.</summary>
        void NoteKnights(VehiclePawn v)
        {
            if (!v.Faction.HostileTo(Faction.OfPlayer)) return;
            string key = map.uniqueID + "/" + v.Faction.loadID;
            int alive = map.mapPawns.SpawnedPawnsInFaction(v.Faction).OfType<VehiclePawn>().Count(k => IsAIKnight(k) && !k.Downed && !k.Dead);
            int peak;
            if (!peakKnights.TryGetValue(key, out peak) || alive > peak) peakKnights[key] = alive;
        }

        List<Pawn> Group(VehiclePawn v)
        {
            return map.mapPawns.SpawnedPawnsInFaction(v.Faction).Where(p => !(p is VehiclePawn) && !p.Dead && !p.Downed).ToList();
        }

        static bool Leaving(Pawn p)
        {
            string duty = p.mindState?.duty?.def?.defName;
            return duty != null && duty.StartsWith("ExitMap");
        }

        /// <summary>Their people are going (most of them have an exit-map duty) or none are left.</summary>
        bool GroupLeaving(VehiclePawn v)
        {
            List<Pawn> g = Group(v);
            return g.Count == 0 || g.Count(Leaving) * 2 > g.Count;
        }

        void Escort(VehiclePawn v)
        {
            List<Pawn> g = Group(v);
            if (g.Count == 0) return;
            IntVec3 centre = new IntVec3((int)g.Average(p => p.Position.x), 0, (int)g.Average(p => p.Position.z));
            if (v.Position.DistanceTo(centre) <= 15f) return;                                      // close enough: hold with them
            IntVec3 last;
            if (lastDest.TryGetValue(v.thingIDNumber, out last) && v.CurJobDef == JobDefOf.Goto && v.vehiclePather.Moving
                && last.DistanceTo(centre) < 12f) return;                                          // already on the way
            foreach (IntVec3 c in GenRadial.RadialCellsAround(centre, 10f, true))
                if (Fits(v, c)) { Order(v, Leg(v, c)); return; }
        }

        /// <summary>Walk to the nearest map edge the Knight fits along; once there, it leaves (with its pilot).</summary>
        void LeaveMap(VehiclePawn v)
        {
            int half = Mathf.Max(v.def.size.x, v.def.size.z) / 2;
            if (v.Position.DistanceToEdge(map) <= half + 2)
            {
                if (Prefs.DevMode) Log.Message("[LIK] " + v.LabelShort + " of " + v.Faction?.Name + " leaves the map with its people");
                foreach (Pawn p in Meltdown.Crew(v).ToList())
                {
                    VehicleRoleHandler h = v.handlers.FirstOrDefault(x => x.thingOwner != null && x.thingOwner.Contains(p));
                    if (h != null && v.TryRemovePawn(p, h)) { if (!p.Destroyed) Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Decide); }
                }
                v.Destroy(DestroyMode.Vanish);
                return;
            }
            IntVec3 last;
            if (lastDest.TryGetValue(v.thingIDNumber, out last) && v.CurJobDef == JobDefOf.Goto && v.vehiclePather.Moving
                && last.DistanceToEdge(map) <= half + 4) return;                                   // already heading out
            // the nearest edge point, then a spot near it where the whole Knight fits
            IntVec3 p0 = v.Position;
            int dx0 = p0.x, dx1 = map.Size.x - 1 - p0.x, dz0 = p0.z, dz1 = map.Size.z - 1 - p0.z;
            int m = Mathf.Min(Mathf.Min(dx0, dx1), Mathf.Min(dz0, dz1));
            IntVec3 edge = m == dx0 ? new IntVec3(half + 1, 0, p0.z) : m == dx1 ? new IntVec3(map.Size.x - 2 - half, 0, p0.z)
                         : m == dz0 ? new IntVec3(p0.x, 0, half + 1) : new IntVec3(p0.x, 0, map.Size.z - 2 - half);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(edge, 12f, true))
                if (c.InBounds(map) && c.DistanceToEdge(map) <= half + 2 && Fits(v, c)) { Order(v, Leg(v, c)); return; }
        }

        // user 2026-11-25 (Knight raid test): raiders ordered straight across the map (~150 cells) got "pathing ... ran out of cells to process"
        // from Vehicle Framework and stood at the edge. Long walks go in legs: a spot ~LegCells along the way where the whole Knight fits;
        // the next check (every Interval) orders the next leg from there.
        const float LegCells = 25f;          // 45 still failed from the map edge ("ran out of cells") - user 2026-11-25

        IntVec3 Leg(VehiclePawn v, IntVec3 dest)
        {
            float d = v.Position.DistanceTo(dest);
            if (d <= LegCells + 8f) return dest;
            Vector3 dir = (dest - v.Position).ToVector3(); dir.y = 0f; dir.Normalize();
            for (float step = LegCells; step >= 15f; step -= 10f)
            {
                IntVec3 c = (v.Position.ToVector3Shifted() + dir * step).ToIntVec3();
                foreach (IntVec3 x in GenRadial.RadialCellsAround(c, 6f, true))
                    if (Fits(v, x)) return x;
            }
            // 2026-11-25 (critical error: two Knights at the map edge were ordered 116 cells in one go, VF's background pathfinder "ran out of
            // cells", and right after that the game's job pool was corrupted - "same driver for 2 pawns" - until rendering broke):
            // never order a walk longer than a leg. Nothing fits on the way: no order now, the next check tries again from wherever it stands.
            return IntVec3.Invalid;
        }

        /// <summary>Nearest hostile PAWN; only when no hostile pawn is left, the nearest turret / building that can attack
        /// (user 2026-11-13: Seek &amp; destroy kept going for turrets across the map instead of the people next to it).</summary>
        Thing FindTarget(VehiclePawn v)
        {
            Thing bestPawn = null, bestOther = null;
            float dPawn = float.MaxValue, dOther = float.MaxValue;
            foreach (IAttackTarget at in map.attackTargetsCache.GetPotentialTargetsFor(v))
            {
                Thing th = at.Thing;
                if (th == null || !th.Spawned || at.ThreatDisabled(v) || !AttackTargetFinder.IsAutoTargetable(at)) continue;
                if (!GenHostility.HostileTo(th, v)) continue;
                Pawn p = th as Pawn;
                if (p != null && (p.Downed || p.Dead)) continue;
                float d = v.Position.DistanceToSquared(th.Position);
                if (p != null) { if (d < dPawn) { dPawn = d; bestPawn = th; } }
                else if (d < dOther) { dOther = d; bestOther = th; }
            }
            return bestPawn ?? bestOther;
        }

        /// <summary>
        /// The weapons of Knights on Seek &amp; destroy: a gun that is busy with a turret / building (or idle) switches to the nearest hostile pawn
        /// it can hit from where it is - in range, inside its firing arc, with a clear line. Pawns are the threat; turrets wait.
        /// </summary>
        void PawnsFirst()
        {
            GameComponent_Seek seek = GameComponent_Seek.Instance;
            if (seek == null) return;
            List<Pawn> foes = null;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                VehiclePawn v = pawns[i] as VehiclePawn;
                if (v == null || v.Faction != Faction.OfPlayer || !seek.Get(v) || KnightLanding.Recovering(v) || v.CompVehicleTurrets == null) continue;
                if (foes == null)
                    foes = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer) && !p.IsPrisoner).ToList();
                if (foes.Count == 0) return;
                foreach (VehicleTurret tr in v.CompVehicleTurrets.Turrets)
                {
                    try
                    {
                        if (!tr.AutoTarget || tr.TurretDisabled || !tr.HasAmmo) continue;
                        Thing cur = tr.targetInfo.IsValid ? tr.targetInfo.Thing : null;
                        if (cur is Pawn cp && !cp.Dead && !cp.Downed) continue;          // already on a pawn
                        if (tr.targetInfo.IsValid && cur == null) continue;               // ground the player aimed at
                        Pawn best = null; float bd = float.MaxValue;
                        Vector3 loc = tr.TurretLocation;
                        foreach (Pawn p in foes)
                        {
                            if (!tr.InRange(p) || !tr.AngleBetween(p.DrawPos)) continue;
                            float d = (p.DrawPos - loc).sqrMagnitude;
                            if (d >= bd) continue;
                            if (!Patch_PierceTargetsThroughWalls.PiercesAt(tr, p.DrawPos) && !tr.TryFindShootLineFromTo(loc.ToIntVec3(), p, out _)) continue;   // burn-through guns ignore walls
                            bd = d; best = p;
                        }
                        if (best != null) tr.SetTarget(best);
                    }
                    catch (System.Exception e) { Log.ErrorOnce("[LIK] seek & destroy targeting failed: " + e.Message, 0x5ee4 ^ v.thingIDNumber); }
                }
            }
        }

        /// <summary>
        /// User 2026-11-13: the Knight stood still while a pawn hacked at its feet. A hostile pawn close to the Knight's footprint is walked
        /// over: the Knight strides through its cell (the stomp crushes what ends up underfoot). True if it set off.
        /// </summary>
        bool WalkOverPawns(VehiclePawn v)
        {
            CellRect body = v.OccupiedRect();
            CellRect near = body.ExpandedBy(2).ClipInsideMap(map);
            Pawn foe = null; float best = float.MaxValue;
            foreach (IntVec3 c in near)
            {
                if (body.Contains(c)) continue;
                foreach (Thing t in c.GetThingList(map))
                    if (t is Pawn p && !(p is VehiclePawn) && !p.Dead && !p.Downed && p.HostileTo(v))
                    {
                        float d = (p.DrawPos - v.DrawPos).sqrMagnitude;
                        if (d < best) { best = d; foe = p; }
                    }
            }
            if (foe == null) return false;
            // aim past the pawn so the stride carries the Knight over it
            int size = Mathf.Max(v.def.size.x, v.def.size.z);
            Vector3 dir = (foe.DrawPos - v.DrawPos); dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            IntVec3 aim = (foe.DrawPos + dir * (size * 0.5f + 1f)).ToIntVec3();
            IntVec3 dest = IntVec3.Invalid;
            IntVec3 last;
            if (lastDest.TryGetValue(v.thingIDNumber, out last) && v.vehiclePather.Moving && last.DistanceTo(aim) <= 3f) return true;   // already striding there
            foreach (IntVec3 c in GenRadial.RadialCellsAround(aim, 3f, true))
                if (Fits(v, c)) { dest = c; break; }
            if (!dest.IsValid) return false;
            Order(v, dest);
            return lastDest.TryGetValue(v.thingIDNumber, out last) && last == dest;
        }

        bool FindStandCell(VehiclePawn v, Thing target, float standoff, bool melee, out IntVec3 result)
        {
            IntVec3 tp = target.Position;
            // melee: a spot INSIDE the arm's reach (the ring used to start at the reach and go 4 cells further out - never close enough)
            float inner = melee ? Mathf.Max(Mathf.Max(v.def.size.x, v.def.size.z) * 0.5f, standoff - 3f) : standoff;
            float outer = melee ? standoff : standoff + 4f;
            if (inner > outer - 1f) inner = Mathf.Max(0f, outer - 1.5f);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(tp, inner, outer)
                         .Where(c => c.InBounds(map))
                         .OrderBy(c => c.DistanceToSquared(v.Position)).Take(80))
            {
                if (!GenSight.LineOfSight(c, tp, map)) continue;
                if (!Fits(v, c)) continue;
                result = c;
                return true;
            }
            result = IntVec3.Invalid;
            return false;
        }
    }

    /// <summary>
    /// Vehicle Framework posts "Cannot path here." whenever a path search fails. For moves Seek &amp; destroy ordered on its own that is noise
    /// (it picks another spot by itself), so the message is dropped for those; the player's own move orders still get it.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_QuietSeekPathFail
    {
        [System.ThreadStatic] static VehiclePawn current;
        static string text;

        static System.Reflection.MethodBase TargetMethod() { return AccessTools.Method(typeof(VehiclePathFollower), "GeneratePath"); }
        // Vehicle Framework 1.7 rewrote its pathfinding: no GeneratePath -> skip this cosmetic patch (the message is just not hidden)
        static bool Prepare() { return TargetMethod() != null; }

        static void Prefix(VehiclePathFollower __instance)
        {
            current = Traverse.Create(__instance).Field("vehicle").GetValue<VehiclePawn>();
        }

        static void Finalizer() { current = null; }

        public static bool Quiet(string msg)
        {
            VehiclePawn v = current;
            if (v == null || msg == null) return false;
            if (text == null) text = "VF_NoPathForVehicle".Translate();
            if (msg != text) return false;
            IntVec3 d;
            lock (MapComponent_KnightSeek.SeekOrders)
                return MapComponent_KnightSeek.SeekOrders.TryGetValue(v.thingIDNumber, out d) && v.CurJob != null && v.CurJob.targetA.Cell == d;
        }
    }

    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) })]
    public static class Patch_QuietSeekPathFailMsg
    {
        static bool Prefix(string text) { return !Patch_QuietSeekPathFail.Quiet(text); }
    }
}
