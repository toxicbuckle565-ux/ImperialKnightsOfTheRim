using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Vehicles;
using Verse.AI.Group;

namespace LIK
{
    /// <summary>
    /// DROP-ZONE WARFARE (user 2026-11-12: fights end too fast - "there just isn't enough enemies and they aren't tough enough"; the Knight's
    /// power stays as it is). When a Knight makes planetfall on an enemy base or site, that faction answers: waves of reinforcements
    /// (its own combat groups) arrive over the following hours, walking in from the map edge or, for advanced factions, dropping in by pod,
    /// and assault the Knight. Bigger Knight = more and heavier waves; each wave is heavier than the last. Mod settings: on/off, number of
    /// waves, wave strength. The waves stop when they run out or the faction is defeated or no longer hostile.
    /// </summary>
    public class MapComponent_DropZoneWar : MapComponent
    {
        public Faction faction;
        public int wavesLeft, wavesTotal, nextTick = -1;
        public float basePoints;
        public float topPoints;            // the strongest single Knight's wave budget
        public int knights;                // Knights of this drop that have landed (each extra one adds ONE wave, not a whole set)
        // user 2026-11-13: "the V.O.I.D. waves are comedically big" - every Knight used to add its own full set of waves (3 Knights = ~12),
        // each 35 % heavier than the last, and cheap monster factions turned that budget into hordes. Now:
        const int MaxWavesBase = 6;        // x the waves setting
        const float ExtraKnightPts = 0.2f, MaxKnightMult = 1.6f;
        const float WaveGrowth = 0.2f, MaxWaveGrowth = 2f;
        const int MaxPawnsBase = 20;       // per wave, x the strength setting (at least 6)

        public MapComponent_DropZoneWar(Map map) : base(map) { }

        public bool Active => faction != null && wavesLeft > 0;

        /// <summary>Called when a Knight lands from orbit.</summary>
        public static void Start(Map map, VehiclePawn v)
        {
            LIKSettings s = LIKMod.I;
            if (map == null || v == null || !s.dropWar) return;
            Faction f = map.ParentFaction;
            if (f == null || f.IsPlayer || f.defeated || !f.HostileTo(Faction.OfPlayer)) return;
            if (!(map.Parent is Settlement) && !(map.Parent is Site)) return;
            if (f.def.pawnGroupMakers == null || !f.def.pawnGroupMakers.Any(g => g.kindDef == PawnGroupKindDefOf.Combat)) return;
            var c = map.GetComponent<MapComponent_DropZoneWar>();
            if (c == null) return;
            int size = Mathf.Max(v.def.size.x, v.def.size.z);
            int waves = Mathf.Max(1, Mathf.RoundToInt((2f + (size - 4) * 0.5f) * s.dropWarWaves));      // Questoris 3, Cerastus/Dominus 4, Acastus 4 (x setting)
            // user 2026-11-12: scale by the faction's strength and the type of base
            float fStr = FactionStrength(f), fBase = BaseFactor(map, f, out string baseKind);
            if (map.Parent is Site) waves = Mathf.Max(1, waves - 1);
            float pts = (500f + size * 150f) * s.dropWarStrength * Find.Storyteller.difficulty.threatScale * fStr * fBase;
            int maxWaves = Mathf.Max(1, Mathf.RoundToInt(MaxWavesBase * s.dropWarWaves));
            if (c.Active && c.faction == f)
            {
                // another Knight on the same field: ONE more wave (up to the cap) and a little heavier - not a whole new set
                c.knights++;
                if (c.wavesTotal < maxWaves) { c.wavesLeft++; c.wavesTotal++; }
                c.topPoints = Mathf.Max(c.topPoints, pts);
                c.basePoints = c.topPoints * Mathf.Min(MaxKnightMult, 1f + ExtraKnightPts * (c.knights - 1));
                return;      // one alarm message per drop is enough
            }
            c.faction = f; c.knights = 1; c.topPoints = pts; c.basePoints = pts;
            c.wavesLeft = Mathf.Min(waves, maxWaves); c.wavesTotal = c.wavesLeft;
            c.nextTick = Find.TickManager.TicksGame + 1500;     // ~25 s at normal speed: the alarm goes out
            Messages.Message(f.Name + " is calling in reinforcements against the Knight - " + c.wavesLeft + " waves are coming (faction strength x" + fStr.ToString("0.##") +
                ", " + baseKind + " x" + fBase.ToString("0.##") + ").", new LookTargets(v), MessageTypeDefOf.ThreatBig, false);
        }

        /// <summary>Tribal 0.6, medieval 0.75, industrial 1.0, spacer 1.3, ultra/archotech 1.5; permanent enemies (pirates, VOID...) x1.1.</summary>
        public static float FactionStrength(Faction f)
        {
            float t;
            switch (f.def.techLevel)
            {
                case TechLevel.Animal: case TechLevel.Neolithic: t = 0.6f; break;
                case TechLevel.Medieval: t = 0.75f; break;
                case TechLevel.Industrial: t = 1.0f; break;
                case TechLevel.Spacer: t = 1.3f; break;
                case TechLevel.Ultra: case TechLevel.Archotech: t = 1.5f; break;
                default: t = 1.0f; break;
            }
            if (f.def.permanentEnemy) t *= 1.1f;
            return t;
        }

        /// <summary>Site (camp/outpost) 0.6; settlement 0.85 .. 1.35 by how built-up it is (the faction's buildings, turrets count x5).</summary>
        public static float BaseFactor(Map map, Faction f, out string kind)
        {
            if (map.Parent is Site) { kind = "small site"; return 0.6f; }
            int score = 0;
            foreach (Building b in map.listerBuildings.allBuildingsNonColonist)
                if (b.Faction == f) score += b.def.building != null && b.def.building.IsTurret ? 5 : 1;
            float k = Mathf.Clamp01(score / 250f);
            kind = k < 0.33f ? "small settlement" : k < 0.75f ? "settlement" : "fortress";
            return Mathf.Lerp(0.85f, 1.35f, k);
        }

        public override void MapComponentTick()
        {
            if (!Active || Find.TickManager.TicksGame < nextTick) return;
            if (faction.defeated || !faction.HostileTo(Faction.OfPlayer) || !LIKMod.I.dropWar) { wavesLeft = 0; return; }
            int waveNo = wavesTotal - wavesLeft + 1;
            wavesLeft--;
            nextTick = Find.TickManager.TicksGame + Rand.Range(2000, 3200);    // about one in-game hour between waves
            try { SendWave(waveNo); }
            catch (System.Exception e) { Log.Error("[LIK] Drop-zone reinforcements: " + e); }
        }

        void SendWave(int waveNo)
        {
            float points = basePoints * Mathf.Min(MaxWaveGrowth, 1f + WaveGrowth * (waveNo - 1));
            points = Mathf.Max(points, faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat));
            var parms = new IncidentParms { target = map, faction = faction, points = points, raidStrategy = RaidStrategyDefOf.ImmediateAttack };
            // advanced factions sometimes drop in by pod; everyone else marches in from the map edge
            bool pods = faction.def.techLevel >= TechLevel.Industrial && Rand.Chance(faction.def.techLevel >= TechLevel.Spacer ? 0.5f : 0.25f);
            parms.raidArrivalMode = pods ? (Rand.Chance(0.5f) ? PawnsArrivalModeDefOf.EdgeDrop : PawnsArrivalModeDefOf.CenterDrop) : PawnsArrivalModeDefOf.EdgeWalkIn;
            if (!parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms))
            {
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                if (!parms.raidArrivalMode.Worker.TryResolveRaidSpawnCenter(parms)) return;
            }
            var gp = new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Combat, tile = map.Tile, faction = faction, points = points, raidStrategy = parms.raidStrategy, generateFightersOnly = true };
            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(gp, true).ToList();
            if (pawns.Count == 0) return;
            // head-count cap: cheap monster factions turn a big budget into a horde - keep the strongest, send the rest away
            int maxPawns = Mathf.Max(6, Mathf.RoundToInt(MaxPawnsBase * LIKMod.I.dropWarStrength));
            if (pawns.Count > maxPawns)
            {
                pawns = pawns.OrderByDescending(p => p.kindDef.combatPower).ToList();
                foreach (Pawn extra in pawns.Skip(maxPawns).ToList()) Find.WorldPawns.PassToWorld(extra, PawnDiscardDecideMode.Discard);
                pawns = pawns.Take(maxPawns).ToList();
            }
            parms.raidArrivalMode.Worker.Arrive(pawns, parms);
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, false, false, false, false, false), map, pawns);
            string how = parms.raidArrivalMode == PawnsArrivalModeDefOf.EdgeWalkIn ? "march in" : "drop in";
            Messages.Message("Reinforcements (" + waveNo + "/" + wavesTotal + "): " + pawns.Count + " " + faction.def.pawnsPlural + " of " + faction.Name + " " + how + ".",
                new LookTargets(pawns), MessageTypeDefOf.ThreatBig, false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref faction, "dzwFaction");
            Scribe_Values.Look(ref wavesLeft, "dzwWavesLeft", 0);
            Scribe_Values.Look(ref wavesTotal, "dzwWavesTotal", 0);
            Scribe_Values.Look(ref nextTick, "dzwNextTick", -1);
            Scribe_Values.Look(ref basePoints, "dzwPoints", 0f);
            Scribe_Values.Look(ref topPoints, "dzwTopPoints", 0f);
            Scribe_Values.Look(ref knights, "dzwKnights", 0);
        }
    }
}
