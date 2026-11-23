using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// ACASTUS WEAPON LORE PASS (user 2026-11-23: "build all of it"). Sources: Wahapedia 40k 10th + Horus Heresy 3rd edition datasheets
    /// (Acastus Porphyrion / Asterius), fandom wiki, Warhammer Community (Lexicanum blocked).
    ///  - SHOCK (HH3 neutron-wave battery = magna lascannon, "Shock (Stunned)"): a struck vehicle / big mech may be stunned (1 in 3, rolls of 5-6).
    ///  - BREACHING (HH3 autocannon "Breaching (6+)"): one round in six punches through armour like a heavy weapon.
    ///  - HEAVY (FP) (HH3 autocannon, 40k ironstorm Heavy): fired standing still (braced), the weapon fires extra rounds.
    ///  - ANTI-AIR (40k Helios "Anti-Fly 2+", HH3 hyperios "Skyfire"): far more damage to flying things.
    ///  - HELIOS INTERCEPTION (lore: "protect the Knight against enemy aircraft ... where the danger from airborne assault is apparent"):
    ///    hostile drop pods and shuttles coming down near a Knight with Helios missiles are struck in the air; they still land, wrecked.
    /// </summary>
    public static class AcastusWeapons
    {
        /// <summary>A war machine or monster: Shock only works on vehicles and walkers (HH3 rule).</summary>
        public static bool WarMachine(Thing t)
        {
            return t is Vehicles.VehiclePawn || t is Pawn p && (p.RaceProps.IsMechanoid && p.BodySize >= 2f || p.BodySize >= 4f);
        }

        public static void Shock(Thing hit, DefModExtension_ShotFx fx, Thing who, Map map)
        {
            if (fx == null || fx.shockVehicles <= 0f || !(hit is Pawn p) || p.Dead || p.Destroyed || !WarMachine(p) || p.stances?.stunner == null) return;
            if (!Rand.Chance(fx.shockVehicles)) return;
            p.stances.stunner.StunFor(fx.shockTicks > 0 ? fx.shockTicks : 120, who, false, true);
            if (map != null && p.Spawned)
            {
                KnightEffects.ElectricFlash(p.DrawPos, map, 1.2f);
                for (int i = 0; i < 6; i++) FleckMaker.ThrowMicroSparks(p.DrawPos + Gen.RandomHorizontalVector(1.2f), map);
                MoteMaker.ThrowText(p.DrawPos + new Vector3(0f, 0f, 1f), map, "SHOCKED", new Color(1f, 0.85f, 0.5f), 2.5f);
            }
        }

        /// <summary>Anything in flight: flying pawns (1.6 flight), and Vehicle Framework aircraft.</summary>
        public static bool Flying(Thing t)
        {
            if (t is Pawn p)
            {
                if (p.flight != null && p.flight.Flying) return true;
                if (p is Vehicles.VehiclePawn v && v.AllComps.Any(c => c.GetType().Name == "CompVehicleLauncher")) return true;
            }
            return false;
        }

        /// <summary>Heavy (FP): braced, a chance per round to fire one more round at the same target (1.0 = every round doubled).</summary>
        public static void BracedExtra(Projectile shot, DefModExtension_ShotFx fx, bool braced, Thing launcher, Vector3 origin, LocalTargetInfo usedTarget,
                                       LocalTargetInfo intendedTarget, ProjectileHitFlags flags, Thing equipment)
        {
            if (QuestorisWeapons.Spawning || fx == null || fx.braceExtra <= 0f || !braced) return;
            if (Rand.Chance(Mathf.Min(1f, fx.braceExtra))) QuestorisWeapons.ExtraRound(shot, launcher, origin, usedTarget, intendedTarget, flags, equipment);
        }
    }

    /// <summary>
    /// HELIOS DEFENCE MISSILES - interception of airborne assault: every 0.5 s, each player Knight with Helios missiles fitted (piloted, not
    /// downed) strikes ONE hostile drop pod / shuttle coming down within its Helios range (cooldown 3 s per Knight). The craft is hit in the
    /// air (missile streak + flash at it) and its passengers take the blast; it still lands, smoking.
    /// </summary>
    public class MapComponent_HeliosIntercept : MapComponent
    {
        // Helios (Acastus) and, user 2026-11-23, the Questoris twin Icarus autocannon (HH3 Skyfire + Rapid Tracking: intercepting)
        static readonly HashSet<string> Interceptors = new HashSet<string> { "LIK_Turret_acastus_helios", "LIK_Turret_questoris_icarus" };
        readonly Dictionary<int, int> nextShot = new Dictionary<int, int>();
        readonly HashSet<int> struck = new HashSet<int>();

        public MapComponent_HeliosIntercept(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 30 != 0) return;
            var craft = map.listerThings.AllThings.Where(t => t is Skyfaller s && s.Spawned && !struck.Contains(t.thingIDNumber) && Hostile(s)).ToList();
            if (craft.Count == 0) return;
            foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (!(p is Vehicles.VehiclePawn v) || v.Downed || v.Dead || !OrbitalDropUtil.HasPilot(v)) continue;
                Vehicles.VehicleTurret helios = v.CompVehicleTurrets?.Turrets?.FirstOrDefault(t => Interceptors.Contains(t.def.defName));
                if (helios == null) continue;
                if (nextShot.TryGetValue(v.thingIDNumber, out int ready) && now < ready) continue;
                float range = helios.MaxRange > 0f ? helios.MaxRange : 56f;
                Thing target = craft.Where(c => !struck.Contains(c.thingIDNumber) && (c.DrawPos - v.DrawPos).MagnitudeHorizontal() <= range)
                                    .OrderBy(c => (c.DrawPos - v.DrawPos).MagnitudeHorizontal()).FirstOrDefault();
                if (target == null) continue;
                nextShot[v.thingIDNumber] = now + 180;
                struck.Add(target.thingIDNumber);
                Strike(v, (Skyfaller)target, helios.def.defName.Contains("icarus") ? "Icarus autocannon fire" : "Helios missiles");
            }
            if (struck.Count > 200) struck.Clear();
        }

        static IEnumerable<Pawn> Passengers(Skyfaller s)
        {
            if (s.innerContainer == null) yield break;
            foreach (Thing t in s.innerContainer)
            {
                if (t is Pawn p) yield return p;
                else if (t is ActiveTransporter at && at.Contents?.innerContainer != null)
                    foreach (Thing u in at.Contents.innerContainer) if (u is Pawn q) yield return q;
            }
        }

        static bool Hostile(Skyfaller s)
        {
            foreach (Pawn p in Passengers(s)) if (p.HostileTo(Faction.OfPlayer)) return true;
            return false;
        }

        void Strike(Vehicles.VehiclePawn knight, Skyfaller craft, string weaponLabel)
        {
            Vector3 at = craft.DrawPos;
            Vector3 from = knight.DrawPos + new Vector3(0f, 0f, 1.5f);
            // the missile streak up to the craft, a flash and smoke where it hits
            FleckDef beam = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_Beam_Core");
            if (beam != null)
            {
                Vector3 d = at - from; float len = d.MagnitudeHorizontal();
                FleckCreationData fd = FleckMaker.GetDataStatic(from + d * 0.5f, map, beam, 1f);
                fd.rotation = d.AngleFlat(); fd.exactScale = new Vector3(0.25f, 1f, len); fd.instanceColor = new Color(1f, 0.8f, 0.5f, 0.8f);
                map.flecks.CreateFleck(fd);
            }
            if (at.ShouldSpawnMotesAt(map))
            {
                GlowFx.Flash(at, map, 6f);
                GlowFx.Fire(at, map, 1.6f);
                for (int i = 0; i < 5; i++) FleckMaker.ThrowSmoke(at + Gen.RandomHorizontalVector(1f), map, 1.4f);
                for (int i = 0; i < 6; i++) FleckMaker.ThrowMicroSparks(at + Gen.RandomHorizontalVector(1f), map);
            }
            DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_impact_missile")?.PlayOneShot(new TargetInfo(craft.Position, map));
            ThingDef weapon = DefDatabase<ThingDef>.GetNamedSilentFail("LIK_Weapon_acastus_helios");
            int hit = 0;
            foreach (Pawn p in Passengers(craft).ToList())
            {
                if (p.Dead) continue;
                for (int i = 0; i < 2; i++) p.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 35f, 1.2f, -1f, knight, null, weapon));
                hit++;
            }
            Messages.Message(weaponLabel + " from " + knight.LabelShort + " strike an incoming enemy craft (" + hit + " aboard).",
                new TargetInfo(craft.Position, map), MessageTypeDefOf.PositiveEvent, false);
        }
    }
}
