using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using SmashTools;
using UnityEngine;
using HarmonyLib;
using Vehicles;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// ORBITAL DROP (design agreed 2026-11-05, built 2026-11-11 on user request "i wanna build the orbital drop for the knight").
    /// 1. A docked Knight is STOWED inside the hangar (despawned into the building, pilot stays aboard in stasis). The gravship then carries
    ///    it as part of the building - a spawned vehicle is never moved by the gravship itself.
    /// 2. With the hangar on an ORBIT map (Odyssey), "Orbital drop" picks a surface tile, then the landing spot. Empty land gets a camp map.
    /// 3. The Knight falls as a KnightDropFaller and lands with an impact blast; built roofs cave in, mountain roof is refused.
    /// </summary>
    public partial class Building_KnightHangar
    {
        ThingOwner<Thing> stowed;
        ThingOwner<Thing> Stowed { get { return stowed ?? (stowed = new ThingOwner<Thing>(this, true, LookMode.Deep)); } }
        public VehiclePawn StowedKnight { get { return Stowed.Count > 0 ? Stowed[0] as VehiclePawn : null; } }

        public ThingOwner GetDirectlyHeldThings() { return Stowed; }
        public void GetChildHolders(List<IThingHolder> outChildren) { ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings()); }

        // DOCKING = STOWING (user 2026-11-22: 'combine the docking as the same as stowing'): a Knight ordered to dock is clamped into the
        // cradle as soon as it stands still in the bay. Per hangar: the pilot climbs out (default) or stays aboard in stasis.
        public bool pilotStaysAboard;
        VehiclePawn dockingKnight;                  // ordered to dock here, not arrived yet
        VehiclePawn undockedKnight;                 // undocked by the player: not clamped again until it has walked out of the bay

        void ExposeDrop()
        {
            Scribe_Deep.Look(ref stowed, "stowedKnight", this);
            Scribe_Values.Look(ref pilotStaysAboard, "pilotStaysAboard", false);
            Scribe_References.Look(ref dockingKnight, "dockingKnight");
            Scribe_References.Look(ref undockedKnight, "undockedKnight");
        }

        /// <summary>The Knight this hangar services: the docked (clamped) one, else one standing in the bay.</summary>
        public VehiclePawn ServicedKnight { get { return StowedKnight ?? FindDockedKnight(); } }

        /// <summary>A Knight standing idle in the bay is clamped in (user 2026-11-22: after loading, an old-style parked Knight showed in the
        /// wrong place until it was moved). Not the one the player just undocked, until it has left the bay; not one being driven.</summary>
        public void AutoDock()
        {
            if (!Spawned || StowedKnight != null || Launching) return;
            VehiclePawn v = FindDockedKnight();
            if (undockedKnight != null && v != undockedKnight) undockedKnight = null;          // it walked out (or is gone)
            if (v == null || v == undockedKnight) return;
            if ((v.vehiclePather != null && v.vehiclePather.Moving) || (v.CurJob != null && v.CurJob.def == JobDefOf.Goto)) return;
            DockKnight(v);
        }

        /// <summary>The dock order (right-click the hangar): the Knight walks to the bay and is clamped in when it arrives.</summary>
        public void OrderDock(VehiclePawn v) { dockingKnight = v; }

        /// <summary>Called every TickRare: clamps in the Knight that was ordered to dock once it stands still in the bay.</summary>
        void CheckDockArrival()
        {
            AutoDock();
            VehiclePawn v = dockingKnight;
            if (v == null) return;
            if (v.Destroyed || !v.Spawned || v.Map != Map || StowedKnight != null) { dockingKnight = null; return; }
            bool moving = (v.vehiclePather != null && v.vehiclePather.Moving) || (v.CurJob != null && v.CurJob.def == JobDefOf.Goto);
            if (moving) return;
            if (FindDockedKnight() == v) DockKnight(v);
            dockingKnight = null;                   // arrived (docked) or stopped somewhere else (order given up)
        }

        string DropInspect()
        {
            VehiclePawn v = StowedKnight;
            if (v == null) return null;
            string pilot = OrbitalDropUtil.HasPilot(v) ? "pilot aboard, held in stasis" : "no pilot aboard";
            return "Docked: " + v.LabelShort + " (clamped in the cradle; " + pilot + ")" + (OrbitalDropUtil.InOrbit(Map) && OrbitalDropUtil.OnGravship(this) ? " - ready for orbital drop (range " + OrbitalDropUtil.MaxRange + " tiles)" : "");
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // gravship flights despawn with WillReplace and keep the Knight inside; real destruction releases it
            if (Spawned && StowedKnight != null && mode != DestroyMode.WillReplace) Release(true);
            base.Destroy(mode);
        }

        /// <summary>Every weapon back to its rest angle (straight ahead, Knight facing south), target cleared - set at once, since a docked
        /// Knight is off the map and its turrets no longer tick round on their own.</summary>
        static void NeutralGuns(VehiclePawn v, Rot8 facing)
        {
            var turrets = v.CompVehicleTurrets?.Turrets;
            if (turrets == null) return;
            foreach (VehicleTurret t in turrets)
            {
                try
                {
                    t.SetTarget(LocalTargetInfo.Invalid);
                    float a = t.attachedTo != null ? t.defaultAngleRotated : VehicleTurret.TurretRotationFor(facing, t.defaultAngleRotated);
                    t.TurretRotation = a;
                    t.TurretRotationTargeted = t.TurretRotation;
                }
                catch (System.Exception e) { Log.WarningOnce("[LIK] could not straighten " + t.def?.defName + ": " + e.Message, t.GetHashCode()); }
            }
        }

        /// <summary>Docks = stows the Knight: despawned into the hangar's cradle (drawn on the bay), carried by a gravship, serviced here.</summary>
        public void DockKnight(VehiclePawn v)
        {
            if (v == null || !v.Spawned || StowedKnight != null) return;
            if (v.ignition != null && v.ignition.Drafted) v.ignition.Drafted = false;
            List<Pawn> left = new List<Pawn>();
            if (!pilotStaysAboard && v.AllPawnsAboard.Count > 0)
            {
                left.AddRange(v.AllPawnsAboard);
                v.DisembarkAll();                   // while the Knight is still on the map (the pilot steps out beside it)
            }
            NeutralGuns(v, Geo.Rot8);               // user 2026-11-22: guns straight when docked
            v.DeSpawn();
            if (!Stowed.TryAdd(v)) { GenSpawn.Spawn(v, KnightCell(v.VehicleDef), Map, KnightFacing, WipeMode.VanishOrMoveAside); return; }
            // no door cycle on dock (user 2026-11-21): the doors belong to the orbital drop
            string crew = left.Count > 0 ? " " + string.Join(", ", left.Select(p => p.LabelShort)) + " climbs out." :
                          v.HasEnoughOperators ? " The pilot stays aboard, held in stasis." : "";
            DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_hangar_clamps_lock")?.PlayOneShot(new TargetInfo(Position, Map));
            Messages.Message(v.LabelShort + " is docked and clamped into the cradle." + crew, this, MessageTypeDefOf.TaskCompletion, false);
        }

        void Release(bool forced)
        {
            VehiclePawn v = StowedKnight;
            if (v == null || !Spawned) return;
            Stowed.Remove(v);
            // no door cycle on release (user 2026-11-21): the doors belong to the orbital drop only
            GenSpawn.Spawn(v, KnightCell(v.VehicleDef), Map, KnightFacing, WipeMode.VanishOrMoveAside);
            v.GetComp<CompKnightHead>()?.Apply();
            undockedKnight = v;                                  // stays standing until it walks out (AutoDock skips it)
            if (!forced) DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_hangar_clamps_release")?.PlayOneShot(new TargetInfo(Position, Map));
            if (!forced) Messages.Message(v.LabelShort + " is undocked and stands in the bay.", v, MessageTypeDefOf.TaskCompletion, false);
        }

        /// <summary>Why a Knight of this class cannot be placed on its bay spot (null = free). Since the 2026-11-22 shrink a big Knight reaches
        /// behind the hangar (under the gantry overhang); the spawn wipes things in the way, so any building or rock there blocks it instead.</summary>
        public string KnightSpotBlocked(VehicleDef def)
        {
            if (def == null || !Spawned) return null;
            foreach (IntVec3 c in GenAdj.OccupiedRect(KnightCell(def), KnightFacing, def.size))
            {
                if (!c.InBounds(Map)) return "the Knight would stand off the map edge";
                foreach (Thing t in c.GetThingList(Map))
                {
                    if (t == this) continue;
                    if (t is Pawn p) return p.LabelShortCap + " is standing where the Knight goes";
                    if (t is Building b) return b.LabelCap + " is in the way (keep the space under the gantry behind the hangar clear)";
                }
            }
            return null;
        }

        /// <summary>Anything other than the hangar itself standing in the bay (the Knight would be spawned on it).</summary>
        string BayBlocked()
        {
            foreach (IntVec3 c in this.OccupiedRect())
                foreach (Thing t in c.GetThingList(Map))
                    if (t is Pawn p && p != null) return p.LabelShortCap + " is standing in the bay";
            return null;
        }

        IEnumerable<Gizmo> DropGizmos()
        {
            VehiclePawn dk = FindDockedKnight();
            VehiclePawn st = StowedKnight;
            yield return new Command_KnightToggle
            {
                knight = st ?? dk, iconName = "LIK_PilotAboard",
                    defaultLabel = "Pilot stays aboard",
                defaultDesc = "On: when a Knight docks here, its pilot stays in the Throne, held in stasis (no hunger, no rest needed) - ready for an orbital drop.\n\n" +
                              "Off: the pilot climbs out when the Knight docks and is free around the base. For an orbital drop, pick a pilot to board in the drop window.",
                icon = GizmoIcons.Get("LIK_PilotAboard", (st ?? dk)?.VehicleDef, TexCommand.ReleaseAnimals),
                isActive = () => pilotStaysAboard,
                toggleAction = delegate { pilotStaysAboard = !pilotStaysAboard; }
            };
            if (dk != null && st == null)
            {
                var stow = new Command_KnightAction
                {
                    knight = dk, iconName = "LIK_Dock",
                    defaultLabel = "Dock Knight",
                    defaultDesc = "Clamp the Knight standing in this bay into the cradle (the same as ordering it to dock). Docked Knights are repaired, reloaded, " +
                                  "refitted and painted here, and travel inside the hangar when a gravship launches. From orbit, a docked Knight can make an orbital drop.",
                    icon = GizmoIcons.Get("LIK_Dock", dk.VehicleDef, TexCommand.Install),
                    action = delegate { DockKnight(dk); }
                };
                if (dk.vehiclePather != null && dk.vehiclePather.Moving) stow.Disable("The Knight is still moving.");
                yield return stow;
            }
            if (st == null) yield break;

            var rel = new Command_KnightAction
            {
                knight = st, iconName = "LIK_Undock",
                    defaultLabel = "Undock Knight",
                defaultDesc = "Unclamp the docked Knight; it stands in the bay, free to walk out (board a pilot first if nobody is aboard).",
                icon = GizmoIcons.Get("LIK_Undock", st.VehicleDef, TexCommand.ReleaseAnimals),
                action = delegate { Release(false); }
            };
            string blocked = BayBlocked() ?? KnightSpotBlocked(st.VehicleDef);
            if (blocked != null) rel.Disable(blocked + ".");
            yield return rel;

            var drop = new Command_KnightAction
            {
                knight = st, iconName = "LIK_OrbitalDrop",
                    defaultLabel = "Orbital drop",
                defaultDesc = "Drop " + st.LabelShort + " from orbit onto the planet below. Pick a world tile (open land, an enemy settlement or site, or a map you already have), " +
                              "then the landing spot. The Knight hits the ground hard: everything near the landing spot is hit by the impact, " +
                              "built roofs above it cave in, and the pilot takes neural strain.\n\nOnly from orbit, with the hangar on the gravship floor. " +
                              "Reaches up to " + OrbitalDropUtil.MaxRange + " world tiles from the point below the ship. Overhead mountain blocks a landing.",
                icon = GizmoIcons.Get("LIK_OrbitalDrop", st.VehicleDef, TexCommand.Attack),
                action = delegate { OrbitalDropUtil.OpenDropWindow(this); }
            };
            drop.defaultDesc += "\n\nOpens the drop window: choose which of the ship's Knights drop and the timing, then pick the world tile and place each Knight.";
            if (!OrbitalDropUtil.CanDrop(st)) drop.Disable("The " + st.def.label + " is too light to survive an orbital drop. It can still travel stowed on the gravship.");
            else if (!ModsConfig.OdysseyActive) drop.Disable("Needs the Odyssey expansion (gravships and orbit).");
            else if (!OrbitalDropUtil.OnGravship(this)) drop.Disable("The hangar must be built entirely on gravship floor (substructure).");
            else if (!OrbitalDropUtil.InOrbit(Map)) drop.Disable("The hangar must be in orbit - on a gravship above the planet.");
            yield return drop;
        }

        /// <summary>Takes the stowed Knight out of the cradle for a drop (null if gone).</summary>
        public VehiclePawn TakeForDrop()
        {
            VehiclePawn v = StowedKnight;
            if (v == null) return null;
            Stowed.Remove(v);                                    // the launch show (doors, release, shaft fall) is started by BeginLaunch
            return v;
        }
    }

    public static class OrbitalDropUtil
    {
        /// <summary>User 2026-11-11: every Knight drops except the Armiger ("such light weight"; a transport ship for it may come later).</summary>
        public static bool CanDrop(VehiclePawn v) { return v != null && v.def.defName != "LIK_Armiger"; }

        public const int MaxRange = 20;      // world tiles from the surface point below the ship (user 2026-11-11)

        /// <summary>The hangar stands entirely on this map's gravship substructure.</summary>
        public static bool OnGravship(Building_KnightHangar h)
        {
            if (h == null || !h.Spawned || !ModsConfig.OdysseyActive) return false;
            foreach (Building_GravEngine e in h.Map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
                if (e.OnValidSubstructure(h)) return true;
            return false;
        }

        /// <summary>The surface tile directly below an orbit tile.</summary>
        public static PlanetTile Below(PlanetTile orbitTile)
        {
            PlanetLayer surface = Find.WorldGrid.FirstLayerOfDef(PlanetLayerDefOf.Surface);
            return surface == null ? PlanetTile.Invalid : surface.GetClosestTile_NewTemp(orbitTile);
        }

        public static bool InOrbit(Map map)
        {
            if (map == null || !ModsConfig.OdysseyActive || PlanetLayerDefOf.Orbit == null) return false;
            return map.Tile.Valid && map.Tile.LayerDef == PlanetLayerDefOf.Orbit;
        }

        static int lastWindowFrame = -1;
        static List<Building_KnightHangar> group = new List<Building_KnightHangar>();

        /// <summary>Hangars whose Knight can drop right now.</summary>
        /// <summary>User 2026-11-22: a Knight never drops without a pilot. A living pawn must sit in a pilot (movement) seat - strict, unlike
        /// Vehicle Framework's HasEnoughOperators, which says yes for autonomous vehicles and whenever VF's debug 'draft any vehicle' is on.</summary>
        public static bool HasPilot(VehiclePawn v)
        {
            if (v?.handlers == null) return false;
            foreach (VehicleRoleHandler h in v.handlers)
            {
                if (h?.role == null || (h.role.HandlingTypes & HandlingType.Movement) == 0 || h.thingOwner == null) continue;
                foreach (Thing t in h.thingOwner) if (t is Pawn p && !p.Dead) return true;
            }
            return false;
        }

        public static bool ReadyToDrop(Building_KnightHangar h)
        {
            VehiclePawn v = h?.StowedKnight;
            return v != null && h.Spawned && CanDrop(v) && OnGravship(h) && InOrbit(h.Map) && HasPilot(v);
        }

        /// <summary>"Orbital drop" button (merged when several hangars are selected): opens the drop window with the selected hangars ticked.</summary>
        public static void OpenDropWindow(Building_KnightHangar clicked)
        {
            if (lastWindowFrame == Time.frameCount) return;      // the merged button calls each selected hangar in the same frame
            lastWindowFrame = Time.frameCount;
            var pre = new List<Building_KnightHangar> { clicked };
            foreach (object o in Find.Selector.SelectedObjects)
                if (o is Building_KnightHangar h && h != clicked && h.Map == clicked.Map) pre.Add(h);
            Find.WindowStack.Add(new Dialog_KnightDrop(clicked.Map, pre));
        }

        /// <summary>The window's Begin: pick the world tile for these Knights.</summary>
        public static void StartTargeting(List<Building_KnightHangar> hangars)
        {
            if (hangars == null || hangars.Count == 0) return;
            group = hangars.ToList();
            Building_KnightHangar hangar = group[0];
            PlanetLayer surface = Find.WorldGrid.FirstLayerOfDef(PlanetLayerDefOf.Surface);
            if (surface == null) { Messages.Message("No planet surface found.", MessageTypeDefOf.RejectInput, false); return; }
            PlanetTile below = Below(hangar.Map.Tile);
            if (!below.Valid) { Messages.Message("No planet surface below the ship.", MessageTypeDefOf.RejectInput, false); return; }
            origin = below;
            Find.WorldSelector.SelectedLayer = surface;
            CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(below)));
            Find.WorldTargeter.BeginTargeting(t => ChoseTile(hangar, t), true, null, false,
                () => GenDraw.DrawWorldRadiusRing(origin, MaxRange),
                t => Label(t), t => Problem(t) == null, below, true);
        }

        static PlanetTile origin = PlanetTile.Invalid;

        /// <summary>The map whose landing spot is being chosen right now (kept alive until the pick ends).</summary>
        public static Map pendingMap;

        static string Problem(GlobalTargetInfo t)
        {
            if (!t.Tile.Valid) return "Not a tile";
            if (t.Tile.LayerDef != PlanetLayerDefOf.Surface) return "Choose a tile on the planet surface";
            if (origin.Valid && Find.WorldGrid.TraversalDistanceBetween(origin, t.Tile, true, MaxRange + 1) > MaxRange)
                return "Out of range: a Knight can only drop within " + MaxRange + " tiles of the point below the ship";
            if (Find.World.Impassable(t.Tile) || Find.WorldGrid[t.Tile].WaterCovered) return "Cannot land here (water or impassable)";
            MapParent mp = Find.WorldObjects.MapParentAt(t.Tile);
            if (mp != null && !mp.HasMap)
            {
                if (mp is Settlement st)
                {
                    if (st.Faction == Faction.OfPlayer) return "Your own settlement has no map loaded";
                    if (!st.Attackable) return "Cannot be attacked";
                    if (st.EnterCooldownBlocksEntering()) return "Cannot enter yet (" + st.EnterCooldownTicksLeft().ToStringTicksToPeriod() + ")";
                }
                else if (!(mp is Site)) return "Cannot land there";
            }
            if (mp == null && Find.WorldObjects.AnyWorldObjectAt(t.Tile)) return "Something is already there";
            if (mp == null && WorldObjectDefOf.Camp == null) return "Cannot make a camp here";
            return null;
        }

        static TaggedString Label(GlobalTargetInfo t)
        {
            if (!t.IsValid) return "";
            string p = Problem(t);
            if (p != null) return p.Colorize(ColoredText.WarningColor);
            MapParent mp = Find.WorldObjects.MapParentAt(t.Tile);
            if (mp == null) return "Drop onto open land (a camp map is made)";
            string lab = "Drop into " + mp.LabelCap;
            if (mp.Faction != null && mp.Faction != Faction.OfPlayer && !mp.Faction.HostileTo(Faction.OfPlayer) && (mp is Settlement || (mp is Site site && site.MainSitePartDef.considerEnteringAsAttack)))
                lab += " - ATTACK: " + mp.Faction.Name + " will become hostile";
            else if (mp.Faction != null && mp.Faction.HostileTo(Faction.OfPlayer)) lab += " (enemy)";
            return lab;
        }

        /// <summary>Where the drop window was opened (the ship) and a camp this drop created - to undo both on cancel.</summary>
        static Map originMap;
        static MapParent createdCamp;

        /// <summary>The player cancelled while placing Knights: back to the ship, and a camp made only for this drop is removed again.</summary>
        static void CancelDrop(Map target)
        {
            if (pendingMap == target) pendingMap = null;
            try
            {
                if (createdCamp != null && !createdCamp.Destroyed && (createdCamp.Map == null || !createdCamp.Map.mapPawns.AnyColonistSpawned))
                    createdCamp.Destroy();
            }
            catch (System.Exception e) { Log.Warning("[LIK] could not remove the drop camp: " + e.Message); }
            createdCamp = null;
            if (originMap != null && Find.Maps.Contains(originMap))
            {
                Current.Game.CurrentMap = originMap;
                CameraJumper.TryJump(originMap.Center, originMap);
            }
        }

        static bool ChoseTile(Building_KnightHangar hangar, GlobalTargetInfo t)
        {
            originMap = hangar?.Map;
            createdCamp = null;
            if (Problem(t) != null || hangar == null || !hangar.Spawned || hangar.StowedKnight == null) return false;
            MapParent mp = Find.WorldObjects.MapParentAt(t.Tile);
            Map map;
            System.Action onLaunch = null;
            if (mp is Settlement settlement && !settlement.HasMap)
            {
                // same as vanilla transport pods attacking a settlement
                map = GetOrGenerateMapUtility.GetOrGenerateMap(settlement.Tile, null);
                EnsureGarrison(map, settlement.Faction);
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
                onLaunch = () =>
                {
                    TaggedString label = "LetterLabelCaravanEnteredEnemyBase".Translate();
                    TaggedString text = ("A Knight is falling from orbit onto " + settlement.Label + ".").CapitalizeFirst();
                    SettlementUtility.AffectRelationsOnAttacked(settlement, ref text);
                    Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NeutralEvent, new LookTargets(settlement), settlement.Faction);
                };
            }
            else if (mp is Site site && !site.HasMap)
            {
                // same as vanilla transport pods visiting a site
                map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, site.PreferredMapSize, null);
                Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
                onLaunch = () =>
                {
                    if (site.Faction != null && site.Faction != Faction.OfPlayer && site.MainSitePartDef.considerEnteringAsAttack)
                        Faction.OfPlayer.TryAffectGoodwillWith(site.Faction, Faction.OfPlayer.GoodwillToMakeHostile(site.Faction), true, true, HistoryEventDefOf.AttackedSettlement);
                };
            }
            else if (mp != null)
            {
                map = mp.Map;
                if (mp is Settlement st2 && st2.Faction != null && st2.Faction != Faction.OfPlayer && st2.Faction.HostileTo(Faction.OfPlayer)) EnsureGarrison(map, st2.Faction);
            }
            else
            {
                var camp = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Camp);
                camp.Tile = t.Tile;
                camp.SetFaction(Faction.OfPlayer);
                Find.WorldObjects.Add(camp);
                createdCamp = camp;
                map = GetOrGenerateMapUtility.GetOrGenerateMap(t.Tile, Find.World.info.initialMapSize, null);
            }
            if (map == null) return false;
            PickCell(group.Where(ReadyToDrop).ToList(), map, onLaunch);
            return true;
        }

        /// <summary>BUG 2026-11-12 (user: dropped on a V.O.I.D. base at night - buildings and turrets, zero pawns, base "defeated" at once):
        /// if a freshly generated enemy base has no humanlike people of its faction, generate the normal settlement garrison and spawn it
        /// around the base, defending it. Logged, so the log shows when this safety net was needed.</summary>
        static void EnsureGarrison(Map map, Faction f)
        {
            if (map == null || f == null || f.IsPlayer) return;
            // 2026-11-13 (user: "nobody spawned at the base", the base was declared defeated before the Knights landed): use the GAME'S OWN test.
            // A settlement counts as defeated when no HUMANLIKE pawn of its faction is an active threat - monsters, mechs, animals and sleepers do
            // not count. The old check accepted any fighter and skipped V.O.I.D., so such bases were 'defeated' the moment the drop began.
            if (!SettlementDefeatUtility.IsDefeated(map, f)) return;
            try
            {
                var parms = new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Settlement, tile = map.Tile, faction = f, points = new FloatRange(1150f, 1600f).RandomInRange, inhabitants = true };
                List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(parms, true).ToList();
                if (pawns.Count == 0) { Log.Warning("[LIK] Orbital drop: " + f.Name + "'s base had no people and no garrison could be generated."); return; }
                IntVec3 centre = map.Center;
                var buildings = map.listerBuildings.allBuildingsNonColonist.Where(b => b.Faction == f).ToList();
                if (buildings.Count > 0) centre = new IntVec3((int)buildings.Average(b => b.Position.x), 0, (int)buildings.Average(b => b.Position.z));
                Lord lord = LordMaker.MakeNewLord(f, new LordJob_DefendBase(f, centre, 0), map);
                int placed = 0;
                foreach (Pawn p in pawns)
                {
                    if (CellFinder.TryFindRandomCellNear(centre, map, 25, c => c.Standable(map) && c.GetFirstPawn(map) == null, out IntVec3 cell)
                        || CellFinder.TryFindRandomCellNear(centre, map, 60, c => c.Standable(map), out cell))
                    {
                        GenSpawn.Spawn(p, cell, map);
                        lord.AddPawn(p);
                        placed++;
                    }
                    else Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard);
                }
                Log.Warning("[LIK] Orbital drop: " + f.Name + "'s base was generated with no people - spawned a garrison of " + placed + ".");
            }
            catch (System.Exception e) { Log.Error("[LIK] Orbital drop garrison: " + e); }
        }

        /// <summary>User 2026-11-12: place every dropping Knight yourself, one after another. Right-click / Esc cancels the whole drop.</summary>
        static void PickCell(List<Building_KnightHangar> hangars, Map map, System.Action onLaunch)
        {
            if (hangars.Count == 0) return;
            pendingMap = map;
            Current.Game.CurrentMap = map;
            CameraJumper.TryJump(map.Center, map);
            PickSpot(hangars, map, onLaunch, new List<IntVec3>());
        }

        static void PickSpot(List<Building_KnightHangar> hangars, Map map, System.Action onLaunch, List<IntVec3> spots)
        {
            int k = spots.Count;
            VehiclePawn v = hangars[k].StowedKnight;
            if (v == null) { Messages.Message("The Knight is gone.", MessageTypeDefOf.RejectInput, false); if (pendingMap == map) pendingMap = null; return; }
            bool clicked = false;
            System.Func<IntVec3, string> problem = c =>
            {
                string p = LandingProblem(v, map, c);
                if (p != null) return p;
                CellRect r = Footprint(v, c);
                for (int i = 0; i < spots.Count; i++)
                    if (Footprint(hangars[i].StowedKnight, spots[i]).ExpandedBy(1).Overlaps(r)) return "Too close to " + hangars[i].StowedKnight.LabelShort;
                return null;
            };
            var tp = new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false, canTargetItems = false };
            Find.Targeter.BeginTargeting(tp,
                lt =>
                {
                    clicked = true;
                    var next = spots.ToList(); next.Add(lt.Cell);
                    if (next.Count < hangars.Count) GameComponent_LIKDeferred.Next(() => PickSpot(hangars, map, onLaunch, next));   // the targeter stops after this callback
                    else
                    {
                        if (LaunchGroup(hangars, map, next)) { onLaunch?.Invoke(); createdCamp = null; }
                        else CancelDrop(map);
                        if (pendingMap == map) pendingMap = null;
                    }
                },
                lt =>
                {
                    for (int i = 0; i < spots.Count; i++) GenDraw.DrawFieldEdges(Footprint(hangars[i].StowedKnight, spots[i]).Cells.ToList(), new Color(0.55f, 0.9f, 1f));
                    if (lt.IsValid) GenDraw.DrawFieldEdges(Footprint(v, lt.Cell).Cells.ToList(), problem(lt.Cell) == null ? Color.green : Color.red);
                },
                lt => lt.IsValid && problem(lt.Cell) == null,
                null,
                () =>
                {
                    if (clicked) return;                         // went on to the next Knight (or launched)
                    CancelDrop(map);                             // cancelled: nothing drops, back to the ship
                    Messages.Message("Orbital drop cancelled.", MessageTypeDefOf.RejectInput, false);
                },
                null, true,
                lt =>
                {
                    string head = (hangars.Count > 1 ? "Place " + (k + 1) + "/" + hangars.Count + ": " : "Place ") + v.LabelShort;
                    string p = lt.IsValid ? problem(lt.Cell) : null;
                    Widgets.MouseAttachedLabel(p != null ? head + " - " + p : head);
                });
        }

        public static CellRect Footprint(VehiclePawn v, IntVec3 c) { return GenAdj.OccupiedRect(c, Rot4.South, v.def.size); }

        public static string LandingProblem(VehiclePawn v, Map map, IntVec3 c)
        {
            CellRect r = Footprint(v, c);
            if (!r.InBounds(map) || r.minX < 2 || r.minZ < 2 || r.maxX > map.Size.x - 3 || r.maxZ > map.Size.z - 3) return "Too close to the map edge";
            foreach (IntVec3 x in r)
            {
                RoofDef roof = x.GetRoof(map);
                if (roof != null && roof.isThickRoof) return "Overhead mountain";
                TerrainDef ter = x.GetTerrain(map);
                if (ter == null || ter.passability == Traversability.Impassable || ter.defName.Contains("Deep")) return "Cannot stand here";
                Building b = x.GetEdifice(map);
                if (b != null && b.def.passability == Traversability.Impassable && b.def.building != null && b.def.building.isNaturalRock) return "Solid rock";
            }
            return null;
        }

        static bool LaunchGroup(List<Building_KnightHangar> hangars, Map map, List<IntVec3> spots)
        {
            if (hangars.Any(h => h == null || !h.Spawned || h.StowedKnight == null)) { Messages.Message("A hangar or Knight is gone - drop cancelled.", MessageTypeDefOf.RejectInput, false); return false; }
            Building_KnightHangar empty = hangars.FirstOrDefault(h => !HasPilot(h.StowedKnight));
            if (empty != null) { Messages.Message(empty.StowedKnight.LabelShortCap + " has no pilot aboard - drop cancelled. Every dropping Knight needs a pilot.", MessageTypeDefOf.RejectInput, false); return false; }
            int gapTicks = Mathf.Max(0, LIKMod.I.dropGapSeconds) * 60;
            string first = hangars[0].StowedKnight.LabelShort;
            int group = (Rand.Int & 0x7fffffff) | 1;                 // the Knights of this drop (war horn after the last one lands)
            int firstId = hangars[0].StowedKnight.thingIDNumber;     // the first to land sounds the horn
            Map ship = hangars[0].Map;
            Building_KnightHangar firstHangar = hangars[0];
            int now = Find.TickManager.TicksGame;
            // user 2026-11-22: ALL hangars open at the same time; the Knights let go one after another by the drop timing; the camera stays
            // on the ship until the LAST Knight has fallen away, then cuts to the target, where they land one after another (same timing).
            var fallers = new List<KnightDropFaller>();
            int lastGone = 0;
            for (int i = 0; i < hangars.Count; i++)
            {
                bool seek = Dialog_KnightDrop.SeekOnLanding(hangars[i]);
                VehiclePawn v = hangars[i].TakeForDrop();
                if (v == null) continue;
                hangars[i].BeginLaunch(v, now, Building_KnightHangar.ReleaseAt + i * gapTicks / 60f);
                lastGone = Mathf.Max(lastGone, hangars[i].LaunchLeadTicks);
                var f = (KnightDropFaller)ThingMaker.MakeThing(KnightDropDefOf.LIK_KnightDropIncoming);
                f.Load(v);
                f.group = group; f.firstKnightId = firstId; f.seekOnLanding = seek;
                GenSpawn.Spawn(f, spots[i], map, Rot4.South);
                fallers.Add(f);
            }
            for (int i = 0; i < fallers.Count; i++) fallers[i].delayTicks = lastGone + 20 + i * gapTicks;   // land after the camera cut
            IntVec3 firstSpot = spots[0];
            GameComponent_LIKDeferred.Next(() =>
            {
                if (ship == null || !Find.Maps.Contains(ship)) return;
                Current.Game.CurrentMap = ship;
                CameraJumper.TryJump(firstHangar.Position, ship);
                if (Find.TickManager.Paused) Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;     // the show needs the clock running
            });
            GameComponent_LIKTimer.After(lastGone, () =>
            {
                if (map == null || !Find.Maps.Contains(map)) return;
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(firstSpot, map);
            });
            Messages.Message(hangars.Count == 1 ? first + " is dropping from the ship." : hangars.Count + " Knights are dropping from the ship.",
                new TargetInfo(firstHangar.Position, ship), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }
    }

    /// <summary>
    /// BUG 2026-11-11 (user: "the knight was lost and i got a notification that the caravan was lost"): a new camp / settlement / site map
    /// is removed as soon as no colonist is on it. While the landing spot was being picked, and during the fall (the pilot is inside the
    /// faller, not on the map), there was none - so the map was deleted with the falling Knight on it. Keep such maps alive.
    /// </summary>
    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
    public static class Patch_KeepDropMap
    {
        static readonly AccessTools.FieldRef<MapPawns, Map> mapRef = AccessTools.FieldRefAccess<MapPawns, Map>("map");

        static void Postfix(MapPawns __instance, ref bool __result)
        {
            if (__result) return;
            Map m = mapRef(__instance);
            if (m == null) return;
            if (OrbitalDropUtil.pendingMap == m) { __result = true; return; }
            if (KnightDropDefOf.LIK_KnightDropIncoming != null && m.listerThings.ThingsOfDef(KnightDropDefOf.LIK_KnightDropIncoming).Count > 0) { __result = true; return; }
            // BUG 2026-11-13 (user: cancelled a drop, colonists gone "in limbo"): a ship whose crew all sit in Knights stowed in hangars has no
            // colonist the game can see, so once the view moved to the target map the SHIP map could be removed with them. Keep it.
            foreach (Building_KnightHangar h in m.listerBuildings.AllBuildingsColonistOfClass<Building_KnightHangar>())
            {
                VehiclePawn st = h.StowedKnight;
                if (st == null) continue;
                foreach (Pawn c in st.AllPawnsAboard)
                    if (!c.Dead && (c.IsColonist || c.Faction == Faction.OfPlayer)) { __result = true; return; }
            }
            foreach (Pawn p in __instance.AllPawnsSpawned)
            {
                if (!(p is VehiclePawn v) || v.Faction != Faction.OfPlayer || !BoardReasons.IsKnight(v)) continue;
                foreach (Pawn c in v.AllPawnsAboard)
                    if (!c.Dead && c.IsColonist) { __result = true; return; }
            }
        }
    }

    /// <summary>
    /// User 2026-11-11 "also a camera shake": vanilla caps every shake at 0.2 cells (x the screen-shake option) and it fades in under
    /// half a second, so the impact barely moved the camera. This adds a real quake on top: big, decaying, still scaled by the
    /// player's screen-shake setting (0 = off) and frozen while paused.
    /// </summary>
    public static class KnightQuake
    {
        static float start = -999f, mag, dur;

        public static void Start(float magnitude, float seconds)
        {
            start = Time.realtimeSinceStartup; mag = magnitude; dur = seconds;
        }

        public static Vector3 Offset()
        {
            float t = Time.realtimeSinceStartup - start;
            if (t < 0f || t > dur) return Vector3.zero;
            float f = 1f - t / dur;
            float k = mag * f * f * Prefs.ScreenShakeIntensity;
            return new Vector3(Mathf.Sin(t * 31f) * k + Mathf.Sin(t * 53f) * k * 0.4f, 0f, Mathf.Sin(t * 37f + 1.3f) * k + Mathf.Sin(t * 61f) * k * 0.4f);
        }
    }

    [HarmonyPatch(typeof(CameraShaker), nameof(CameraShaker.ShakeOffset), MethodType.Getter)]
    public static class Patch_KnightQuake
    {
        static void Postfix(ref Vector3 __result)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null || Find.TickManager.Paused) return;
            __result += KnightQuake.Offset();
        }
    }

    [DefOf]
    public static class KnightDropDefOf
    {
        public static ThingDef LIK_KnightDropIncoming;
        public static ThingDef LIK_KnightCrater_Small;
        public static ThingDef LIK_KnightCrater_Medium;
        public static ThingDef LIK_KnightCrater_Large;
        public static FleckDef LIK_ImpactRock;
        static KnightDropDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(KnightDropDefOf)); }
    }

    /// <summary>The falling Knight: drawn plunging from the sky with a fire trail, then an impact blast and the Knight spawns.</summary>
    public class KnightDropFaller : Thing, IThingHolder
    {
        // user 2026-11-11: "it should land like a meteorite, very fast, make the world shake, create an impact crater and be loud;
        // no other effects except maybe some rocks launching into the air" (no rocket/fire/smoke trail, no slowing down)
        // user 2026-11-11: "it should come in at orbit speed" - restored 2026-11-22 (it had been slowed to 1 s for the re-entry flame,
        // which the user then removed)
        const int FallTicks = 20;             // ~0.33 s
        const float StartHeight = 150f;       // cells above the landing spot (~450 cells/s)
        ThingOwner<Thing> inner;
        int ticksLeft = FallTicks;
        bool soundPlayed;
        public int delayTicks;                // group drop in sequence: waits (unseen, high above) before it starts to fall
        public int group, firstKnightId = -1; // the drop this Knight belongs to, and the Knight that landed first (sounds the war horn)
        public bool seekOnLanding = true;     // switch Seek & destroy on as it lands (drop window, user 2026-11-13)
        public bool raidDrop;                 // a Knight-house raid opening from orbit (KnightRaids.cs, user 2026-11-25): an AI Knight

        ThingOwner<Thing> Inner { get { return inner ?? (inner = new ThingOwner<Thing>(this, true, LookMode.Deep)); } }
        VehiclePawn Knight { get { return Inner.Count > 0 ? Inner[0] as VehiclePawn : null; } }
        public ThingOwner GetDirectlyHeldThings() { return Inner; }
        public void GetChildHolders(List<IThingHolder> outChildren) { ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings()); }

        public void Load(VehiclePawn v) { Inner.TryAdd(v); }
        public string KnightLabel { get { return Knight?.def.label ?? "Knight"; } }

        float Height { get { return StartHeight * ticksLeft / (float)FallTicks; } }   // constant, very high speed - no braking

        // "DropPod_Fall" is a looping (sustainer) sound - played as a one-shot it logged an error. First one-shot of these that exists.
        static SoundDef fallSound; static bool fallSoundLooked;
        static SoundDef FallSound()
        {
            if (fallSoundLooked) return fallSound;
            fallSoundLooked = true;
            foreach (string n in new[] { "Bombardment_PreImpact", "MortarRound_PreImpact", "MeteoriteIncoming" })
            {
                SoundDef s = DefDatabase<SoundDef>.GetNamedSilentFail(n);
                if (s != null && !s.sustain) { fallSound = s; break; }
            }
            return fallSound;
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned) return;
            if (Knight == null) { Destroy(); return; }
            if (delayTicks > 0) { delayTicks--; return; }
            if (!soundPlayed)
            {
                soundPlayed = true;
                FallSound()?.PlayOneShot(new TargetInfo(Position, Map));   // the incoming whistle
            }
            if (--ticksLeft <= 0) Impact();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            VehiclePawn v = Knight;
            if (v == null || delayTicks > 0) return;
            Vector3 c = this.TrueCenter();
            Vector3 k = c + new Vector3(0f, 0f, Height);
            float alt = AltitudeLayer.Skyfaller.AltitudeFor();
            // user 2026-11-22: re-entry blaze + smoke REMOVED after the in-game test (did not like them; the smoke also hid the body at the
            // landing for a second - smoke draws above the body, below the carapace / pauldrons / guns). Plain fall + shadow + impact.
            OrbitalDropDraw.DrawKnight(v, k, alt);
            // shadow grows as it nears the ground
            float near = 1f - Height / StartHeight;
            Vector2 ds = v.VehicleDef.graphicData.drawSize;
            Material sh = OrbitalDropDraw.Shadow;
            if (sh != null)
            {
                Vector3 sp = new Vector3(c.x, AltitudeLayer.Shadows.AltitudeFor(), c.z);
                Matrix4x4 m = Matrix4x4.TRS(sp, Quaternion.identity, new Vector3(ds.x * (0.3f + 0.6f * near), 1f, ds.y * (0.2f + 0.4f * near)));
                Graphics.DrawMesh(MeshPool.plane10, m, sh, 0);
            }
        }

        void Impact()
        {
            Map map = Map; IntVec3 c = Position; VehiclePawn v = Knight;
            int size = Mathf.Max(v.def.size.x, v.def.size.z);
            CellRect r = OrbitalDropUtil.Footprint(v, c);

            // built roofs cave in (mountain roof was refused at targeting)
            var roofCells = r.ExpandedBy(1).ClipInsideMap(map).Cells.Where(x => x.Roofed(map) && !x.GetRoof(map).isThickRoof).ToList();
            if (roofCells.Count > 0) RoofCollapserImmediate.DropRoofInCells(roofCells, map);

            var sw = System.Diagnostics.Stopwatch.StartNew();     // freeze hunt (2026-11-12): logs the slow parts if the impact takes > 50 ms
            long tDamage = 0;
            // the blow itself: straight damage, no explosion graphics (no fireball, no fire)
            float radius = size * 0.5f + 3.5f;
            float dmg = 40f + size * 8f;
            var hit = new HashSet<Thing>();
            Vector3 cv = r.CenterVector3;
            foreach (IntVec3 x in GenRadial.RadialCellsAround(c, radius, true))
            {
                if (!x.InBounds(map)) continue;
                foreach (Thing t in x.GetThingList(map).ToList())
                {
                    if (t == this || t == v || !hit.Add(t) || !t.def.useHitPoints && !(t is Pawn)) continue;
                    if (t is Mote || t is Filth) continue;
                    float d = Mathf.Sqrt((t.TrueCenter() - cv).sqrMagnitude);
                    float amount = dmg * Mathf.Clamp01(1f - 0.75f * d / radius);
                    if (amount < 1f) continue;
                    t.TakeDamage(new DamageInfo(DamageDefOf.Bomb, Mathf.RoundToInt(amount), 0.5f, -1f, null));
                }
            }
            tDamage = sw.ElapsedMilliseconds;

            // loud
            DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_GiantBomb")?.PlayOneShot(new TargetInfo(c, map));
            DefDatabase<SoundDef>.GetNamedSilentFail("Thunder_OnMap")?.PlayOneShot(new TargetInfo(c, map));
            // the world shakes
            Find.CameraDriver.shaker.DoShake(4f + size * 0.6f);
            if (Find.CurrentMap == map) KnightQuake.Start(0.6f + size * 0.1f, 1.4f + size * 0.08f);

            // rocks launched into the air, landing as rubble
            FleckDef rock = KnightDropDefOf.LIK_ImpactRock;
            for (int i = 0; i < 24 + size * 5; i++)
            {
                Vector3 from = cv + new Vector3(Rand.Range(-size * 0.5f, size * 0.5f), 0f, Rand.Range(-size * 0.5f, size * 0.5f));
                if (rock != null)
                {
                    FleckCreationData fd = FleckMaker.GetDataStatic(from, map, rock, Rand.Range(0.6f, 1.4f) + size * 0.08f);
                    fd.velocityAngle = Rand.Range(0f, 360f);
                    fd.velocitySpeed = Rand.Range(3f, 7f) + size * 0.6f;
                    fd.rotationRate = Rand.Range(-400f, 400f);
                    map.flecks.CreateFleck(fd);
                }
                IntVec3 land = (from + Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * Vector3.forward * Rand.Range(size * 0.6f, radius + 2f)).ToIntVec3();
                if (land.InBounds(map) && Rand.Chance(0.6f)) FilthMaker.TryMakeFilth(land, map, ThingDefOf.Filth_RubbleRock);
            }

            // user 2026-11-11: vegetation and trees inside the crater are destroyed too
            ThingDef craterFor = DefDatabase<ThingDef>.GetNamedSilentFail("LIK_KnightCrater_" + v.def.defName.Replace("LIK_", ""));
            float clear = Mathf.Max(radius, craterFor?.graphicData != null ? craterFor.graphicData.drawSize.x * 0.42f : radius);
            // user 2026-11-12: ~1 s freeze at impact. Destroying hundreds of plants in one tick is the likely cost (each removal updates
            // the path/region data, Vehicle Framework's included), so they are now flattened by an outward shockwave over ~30 ticks.
            long tPlants = sw.ElapsedMilliseconds;
            map.GetComponent<MapComponent_ImpactClear>()?.Add(c, Mathf.Min(clear, GenRadial.MaxRadialPatternRadius - 1f));
            tPlants = sw.ElapsedMilliseconds - tPlants;

            // user 2026-11-22: a Knight dropped onto a building was pushed to the nearest free spot - the drop now OBLITERATES everything in
            // its impact crater: buildings, walls, furniture, rock, items and corpses are destroyed, pawns under the Knight's footprint are
            // crushed (pawns elsewhere in the crater took the blow above), built roofs over the crater cave in
            long tObliterate = sw.ElapsedMilliseconds;
            var craterCells = GenRadial.RadialCellsAround(c, Mathf.Min(clear, GenRadial.MaxRadialPatternRadius - 1f), true).Where(x => x.InBounds(map)).ToList();
            foreach (IntVec3 x in craterCells)
                foreach (Thing t in x.GetThingList(map).ToList())
                {
                    if (t == this || t.Destroyed || t is Mote || t is Filth) continue;
                    if (t is Pawn p)
                    {
                        if (r.Contains(x) && !p.Dead) p.Kill(new DamageInfo(DamageDefOf.Crush, 9999f, 99f, -1f, v));
                        continue;
                    }
                    if (t is Plant && !r.Contains(x)) continue;                                    // the rest go with the shockwave
                    if (t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Plant)
                        t.Destroy(t.def.category == ThingCategory.Building && t.def.useHitPoints ? DestroyMode.KillFinalize : DestroyMode.Vanish);
                }
            foreach (Thing corpse in craterCells.SelectMany(x => x.GetThingList(map)).OfType<Corpse>().Where(k => !k.Destroyed && r.Contains(k.Position)).ToList()) corpse.Destroy();
            var craterRoof = craterCells.Where(x => x.Roofed(map) && !x.GetRoof(map).isThickRoof).ToList();
            if (craterRoof.Count > 0) RoofCollapserImmediate.DropRoofInCells(craterRoof, map);
            tObliterate = sw.ElapsedMilliseconds - tObliterate;

            long tSpawn = sw.ElapsedMilliseconds;
            Inner.Remove(v);
            Destroy();
            GenSpawn.Spawn(v, c, map, Rot4.South, WipeMode.VanishOrMoveAside);
            v.GetComp<CompKnightHead>()?.Apply();
            // user 2026-11-12: the Knight lands with its engine already running
            try { if (v.ignition != null && !v.ignition.Drafted && v.CanDraft().Accepted) v.ignition.Drafted = true; }
            catch (System.Exception e) { Log.Warning("[LIK] Could not start the engine after the drop: " + e.Message); }
            tSpawn = sw.ElapsedMilliseconds - tSpawn;

            // impact crater under the Knight (flat, walkable; deconstruct it to clear it)
            ThingDef crater = DefDatabase<ThingDef>.GetNamedSilentFail("LIK_KnightCrater_" + v.def.defName.Replace("LIK_", "")) ?? (size <= 4 ? KnightDropDefOf.LIK_KnightCrater_Small : size <= 6 ? KnightDropDefOf.LIK_KnightCrater_Medium : KnightDropDefOf.LIK_KnightCrater_Large);
            if (crater != null && c.GetTerrain(map) is TerrainDef ter && !ter.IsWater) GenSpawn.Spawn(crater, c, map, WipeMode.Vanish);

            KnightLanding.Start(v);          // a second to recover from the landing before it moves or fires (user 2026-11-13)
            if (seekOnLanding && v.Drafted) GameComponent_Seek.Instance?.Set(v, true);
            // user 2026-11-13: on an enemy base, once the whole drop is down, the first Knight to land sounds its war horn
            if (group != 0 && !map.listerThings.ThingsOfDef(KnightDropDefOf.LIK_KnightDropIncoming).Any(t => t is KnightDropFaller o && o != this && o.group == group && o.Spawned))
                map.GetComponent<MapComponent_DropHorn>()?.Queue(firstKnightId, map);
            if (raidDrop)
            {
                // a house Knight opening a raid: driven by the AI (MapComponent_KnightSeek); the letter was sent when the drop began
                MapComponent_KnightSeek.Arrived[v.thingIDNumber] = Find.TickManager.TicksGame;
            }
            else
            {
                foreach (Pawn p in TollUtil.Pilots(v)) TollUtil.AddStrain(p, 0.12f);
                Messages.Message(v.LabelShort + " has made planetfall.", v, MessageTypeDefOf.PositiveEvent, false);
                MapComponent_DropZoneWar.Start(map, v);     // drop-zone warfare: the enemy sends reinforcement waves (2026-11-12)
            }
            if (sw.ElapsedMilliseconds > 50)
                Log.Message("[LIK] Drop impact took " + sw.ElapsedMilliseconds + " ms (damage " + tDamage + ", obliterate " + tObliterate + ", knight spawn + engine " + tSpawn + ", plants queued " + tPlants + ")");
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // never lose the Knight: if the faller is removed early, put it down where it is
            VehiclePawn v = Knight;
            if (v != null && Spawned)
            {
                Map map = Map; IntVec3 c = Position;
                Inner.Remove(v);
                base.Destroy(mode);
                GenSpawn.Spawn(v, c, map, Rot4.South, WipeMode.VanishOrMoveAside);
                return;
            }
            base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inner, "inner", this);
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", FallTicks);
            Scribe_Values.Look(ref soundPlayed, "soundPlayed", false);
            Scribe_Values.Look(ref delayTicks, "delayTicks", 0);
            Scribe_Values.Look(ref group, "group", 0);
            Scribe_Values.Look(ref firstKnightId, "firstKnightId", -1);
            Scribe_Values.Look(ref seekOnLanding, "seekOnLanding", true);
            Scribe_Values.Look(ref raidDrop, "raidDrop", false);
        }
    }

    [StaticConstructorOnStartup]
    public static class OrbitalDropDraw
    {
        public static readonly Material Shadow = MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowDropPod", ShaderDatabase.Transparent);
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static readonly HashSet<int> failed = new HashSet<int>();
        // Knights whose turret renderers / overlays were re-registered. Keyed by the OBJECT, not the ID: reloading a save in the same session
        // makes new Knight objects with the same IDs, which an ID set wrongly skipped (user 2026-11-12: weapons gone after a second reload).
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VehiclePawn, object> revalidated = new System.Runtime.CompilerServices.ConditionalWeakTable<VehiclePawn, object>();
        /// <summary>After a refit of a docked (unspawned) Knight: register its turret renderers again on the next draw.</summary>
        public static void ForgetTurrets(VehiclePawn v) { if (v != null) revalidated.Remove(v); }

        /// <summary>The whole Knight as it looks in game (body, livery, equipped weapons static), facing south.
        /// Uses Vehicle Framework's own render pipeline; falls back to the plain body picture if that fails.</summary>
        public static void DrawKnight(VehiclePawn v, Vector3 center, float altitude, float alpha = 1f) { DrawKnight(v, center, altitude, Rot8.South, alpha); }
        /// <summary>Facing: the sideways hangars (2026-11-26) show the docked / launching Knight from the side.</summary>
        public static void DrawKnight(VehiclePawn v, Vector3 center, float altitude, Rot8 rot, float alpha = 1f)
        {
            if (!failed.Contains(v.thingIDNumber))
            {
                try
                {
                    // BUG 2026-11-12 (user: after a reload a stowed Knight showed the body only): VF registers turret renderers only in
                    // RevalidateTurrets (spawn / AddTurret); loading a save rebuilds the turrets but an unspawned Knight never re-registers them.
                    // the body-cover overlays (pauldrons, hull, occluders) are not saved either and are only rebuilt on spawn: without them
                    // the weapons drew on top of the body (user 2026-11-12)
                    if (!revalidated.TryGetValue(v, out _)) { revalidated.Add(v, null); KnightPaint.EnsureInitial(v); v.CompVehicleTurrets?.RevalidateTurrets(); v.GetComp<CompKnightHead>()?.Apply(true); }
                    Vector3 loc = new Vector3(center.x, altitude, center.z);
                    v.DrawTracker.DynamicDrawPhaseAt(DrawPhase.EnsureInitialized, in loc, rot, 0f);
                    v.DrawTracker.DynamicDrawPhaseAt(DrawPhase.ParallelPreDraw, in loc, rot, 0f);
                    v.DrawTracker.DynamicDrawPhaseAt(DrawPhase.Draw, in loc, rot, 0f);
                    return;
                }
                catch (System.Exception e)
                {
                    failed.Add(v.thingIDNumber);
                    Log.Warning("[LIK] could not draw the stowed/falling Knight with its weapons, showing the body only: " + e.Message);
                }
            }
            DrawBody(v, center, altitude, rot);
        }

        static void DrawBody(VehiclePawn v, Vector3 center, float altitude, Rot8 rot)
        {
            GraphicData gd = v.VehicleDef.graphicData;
            if (gd == null) return;
            string path = gd.texPath + (rot == Rot8.West ? "_west" : rot == Rot8.East ? "_east" : "_south");
            if (!mats.TryGetValue(path, out Material m))
            {
                m = MaterialPool.MatFrom(path, ShaderDatabase.Cutout);
                mats[path] = m;
            }
            Matrix4x4 mx = Matrix4x4.TRS(new Vector3(center.x, altitude, center.z), Quaternion.identity, new Vector3(gd.drawSize.x, 1f, gd.drawSize.y));
            Graphics.DrawMesh(MeshPool.plane10, mx, m, 0);
        }
    }

    /// <summary>Draws stowed Knights in their hangar bays (the Knight itself is not on the map while stowed).</summary>
    public class MapComponent_StowedKnights : MapComponent
    {
        public MapComponent_StowedKnights(Map map) : base(map) { }
        bool loadDocked;                            // idle Knights in the bays clamped in on the first frame (not saved: once per load)

        public override void MapComponentUpdate()
        {
            FacingArt.ProcessPaintQueue();
            Stickers.PurgeMaps();                   // sticker surface maps unused for a minute are freed          // painted weapon pictures asked for by the render threads (materials are made on the main thread)
            if (!loadDocked)
            {
                loadDocked = true;
                foreach (Building_KnightHangar h in map.listerBuildings.AllBuildingsColonistOfClass<Building_KnightHangar>().ToList())
                    try { h.AutoDock(); } catch (System.Exception e) { Log.Warning("[LIK] dock on load: " + e.Message); }
            }
            if (Find.CurrentMap != map || WorldRendererUtility.WorldSelected) return;
            foreach (Building_KnightHangar h in map.listerBuildings.AllBuildingsColonistOfClass<Building_KnightHangar>())
            {
                if (h.Launching) { h.DrawLaunch(); continue; }       // orbital drop under way: doors, release, shaft fall
                VehiclePawn v = h.StowedKnight;
                if (v == null) continue;
                OrbitalDropDraw.DrawKnight(v, h.StowedDrawPos(v.VehicleDef), AltitudeLayer.PawnState.AltitudeFor(), h.Geo.Rot8);   // on the bay, not the hangar centre (2026-11-22)
            }
        }
    }

    /// <summary>War horn after an orbital drop onto an enemy base (user 2026-11-13): when the last Knight of the drop is down, the Knight that
    /// landed first sounds its war horn, a moment after the impact so it is not drowned out. Not saved (a save in that second skips the horn).</summary>
    public class MapComponent_DropHorn : MapComponent
    {
        const int DelayTicks = 90;     // 1.5 s after the last impact
        readonly List<KeyValuePair<int, int>> pending = new List<KeyValuePair<int, int>>();   // knight id, tick to sound
        public MapComponent_DropHorn(Map map) : base(map) { }

        public static bool EnemyBase(Map map)
        {
            MapParent mp = map?.Parent;
            if (mp == null || mp.Faction == null || mp.Faction == Faction.OfPlayer || !mp.Faction.HostileTo(Faction.OfPlayer)) return false;
            return mp is Settlement || mp is Site;
        }

        public void Queue(int knightId, Map m)
        {
            if (knightId < 0 || !EnemyBase(m)) return;
            pending.Add(new KeyValuePair<int, int>(knightId, Find.TickManager.TicksGame + DelayTicks));
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (now < pending[i].Value) continue;
                int id = pending[i].Key; pending.RemoveAt(i);
                VehiclePawn v = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == id) as VehiclePawn;
                if (v == null || v.Dead || v.Downed) continue;
                try
                {
                    // the horn has a cooldown: the drop itself must never block it
                    CompKnightHead c = v.GetComp<CompKnightHead>(); if (c != null) c.lastHornTick = -99999;
                    if (KnightSound.SoundHorn(v)) MoteMaker.ThrowText(v.DrawPos + new Vector3(0f, 0f, 2f), map, "War horn!", 4f);
                }
                catch (System.Exception e) { Log.Warning("[LIK] drop war horn failed: " + e.Message); }
            }
        }
    }

    /// <summary>2026-11-13: no "base defeated" while the drop is still in the air (the Knights and their pilots are not on the map yet).</summary>
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    public static class Patch_NoDefeatDuringDrop
    {
        static bool Prefix(Settlement factionBase)
        {
            Map m = factionBase?.Map;
            if (m == null) return true;
            if (OrbitalDropUtil.pendingMap == m) return false;
            return KnightDropDefOf.LIK_KnightDropIncoming == null || m.listerThings.ThingsOfDef(KnightDropDefOf.LIK_KnightDropIncoming).Count == 0;
        }
    }
}
