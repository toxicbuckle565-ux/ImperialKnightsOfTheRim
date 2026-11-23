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
    /// QUESTORIS WEAPON LORE PASS (user 2026-11-23: "build all of it"). Sources: Wahapedia 40k 10th (Paladin, Errant, Warden, Crusader, Gallant,
    /// Preceptor, Forge World Magaera / Styrix), Knight Defender (new codex), HH3 Knight Questoris / Magaera / Styrix; 8th ed. thunderstrike rule.
    ///  - RAPID FIRE (battle cannon, stubber, plasma-fusil, phosphor): within half range every round may be doubled (closeExtra / closeRange).
    ///  - HEAVY (FP & AP) braced: more rounds (braceExtra, AcastusWeapons) and better armour piercing (braceAP).
    ///  - SHRED (lightning cannon, chierovile): a chance per round of +25 % damage.
    ///  - SUSTAINED HITS (lightning cannon): a hit arcs on to 1-2 more enemies nearby (chainHits / chainRadius).
    ///  - GRAV-PIN (graviton crusher, 40k Styrix): infantry hit are pinned - the gravity slow for gravPin seconds.
    ///  - LOW INTENSITY mode (las-impulsor): more, lighter shots (lowIntensity damage x, lowIntensityAP x).
    ///  - Melee: SHOCK on vehicles (thunderstrike, Hekaton: HH3 Shock), HURL (thunderstrike, 8th ed.: a slain vehicle / monster is thrown at
    ///    another enemy nearby), SHRED (reaper chainsword).
    /// </summary>
    public static class QuestorisWeapons
    {
        /// <summary>The fire mode label of the turret firing right now (set by Patch_TurretFireMode, read by the shot in Launch).</summary>
        public static string pendingMode;

        public static bool LowIntensityMode { get { return pendingMode != null && pendingMode.StartsWith("Low intensity"); } }

        // ------------------------------------------------------------ extra rounds
        static bool spawning;

        /// <summary>Fire one more round of the same shot at the same target (slight spread on a cell target).</summary>
        public static void ExtraRound(Projectile shot, Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
                                      ProjectileHitFlags flags, Thing equipment)
        {
            if (spawning || launcher == null || !launcher.Spawned) return;
            spawning = true;
            try
            {
                var extra = (Projectile)GenSpawn.Spawn(shot.def, launcher.Position, launcher.Map);
                LocalTargetInfo aim = usedTarget;
                if (!usedTarget.HasThing)
                {
                    IntVec3 c = usedTarget.Cell + GenRadial.RadialPattern[Rand.Range(1, 9)];
                    if (c.InBounds(launcher.Map)) aim = new LocalTargetInfo(c);
                }
                extra.Launch(launcher, origin + Gen.RandomHorizontalVector(0.3f), aim, intendedTarget, flags, false, equipment);
            }
            catch (System.Exception e) { Log.WarningOnce("[LIK] extra round: " + e.Message, 7713002); }
            finally { spawning = false; }
        }

        public static bool Spawning { get { return spawning; } }

        /// <summary>Rapid Fire: within half range, a chance per round of a second round.</summary>
        public static void RapidFire(Projectile shot, DefModExtension_ShotFx fx, Thing launcher, Vector3 origin, LocalTargetInfo usedTarget,
                                     LocalTargetInfo intendedTarget, ProjectileHitFlags flags, Thing equipment)
        {
            if (spawning || fx == null || fx.closeExtra <= 0f || fx.closeRange <= 0f) return;
            if ((usedTarget.CenterVector3 - origin).MagnitudeHorizontal() > fx.closeRange || !Rand.Chance(fx.closeExtra)) return;
            ExtraRound(shot, launcher, origin, usedTarget, intendedTarget, flags, equipment);
        }

        // ------------------------------------------------------------ sustained hits: the hit arcs on
        public static void ChainHits(Thing hit, DefModExtension_ShotFx fx, Map map, Vector3 pos, int damage, float ap, DamageDef dd, Thing who, ThingDef weapon)
        {
            if (fx == null || fx.chainHits <= 0 || map == null || !pos.InBounds(map)) return;
            int n = Rand.RangeInclusive(1, fx.chainHits);
            IntVec3 from = pos.ToIntVec3();
            var foes = GenRadial.RadialDistinctThingsAround(from, map, fx.chainRadius > 0f ? fx.chainRadius : 3.5f, true)
                .OfType<Pawn>().Where(p => p != hit && !p.Dead && p.Spawned && (who == null || p.HostileTo(who))).InRandomOrder().Take(n).ToList();
            foreach (Pawn p in foes)
            {
                p.TakeDamage(new DamageInfo(dd, damage * 0.5f, ap, -1f, who, null, weapon));
                if (pos.ShouldSpawnMotesAt(map)) ShotFx.BoltPublic(pos, p.DrawPos, map, 0.55f, 0.45f);
            }
        }

        // ------------------------------------------------------------ grav-pin
        static HediffDef crushed;
        static HediffDef Crushed { get { return crushed ?? (crushed = DefDatabase<HediffDef>.GetNamedSilentFail("LIK_GravityCrushed")); } }

        public static void GravPin(Map map, Vector3 at, float radius, float seconds, Thing who)
        {
            if (map == null || seconds <= 0f || Crushed == null || !at.InBounds(map)) return;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(at.ToIntVec3(), map, Mathf.Max(1f, radius), true))
            {
                if (!(t is Pawn p) || p is VehiclePawn || p.Dead || (who != null && !p.HostileTo(who)) || AcastusWeapons.WarMachine(p)) continue;   // infantry
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Crushed);
                if (h == null) { h = HediffMaker.MakeHediff(Crushed, p); h.Severity = 0.55f; p.health.AddHediff(h); }
                else h.Severity = Mathf.Max(h.Severity, 0.55f);
                HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
                if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, Mathf.RoundToInt(seconds * 60f));
                if (p.Spawned && p.DrawPos.ShouldSpawnMotesAt(map)) FleckMaker.ThrowDustPuffThick(p.DrawPos, map, 1.2f, new Color(0.45f, 0.4f, 0.55f, 0.7f));
            }
        }

        // ------------------------------------------------------------ melee: shock, hurl, shred
        public static void MeleeAfter(DefModExtension_MeleeFx fx, List<Pawn> struck, Map map, Vector3 from, int dmg, float ap, DamageDef dd, Thing who, ThingDef weapon)
        {
            if (fx == null || map == null) return;
            bool hurled = false;
            foreach (Pawn p in struck)
            {
                if (p.Destroyed && !p.Dead) continue;
                if (p.Dead)
                {
                    if (fx.hurl && !hurled && (AcastusWeapons.WarMachine(p) || p.BodySize >= 2.5f)) hurled = Hurl(p, map, from, who, weapon);
                    continue;
                }
                if (p.Map != map) continue;
                if (fx.shred > 0f && Rand.Chance(fx.shred)) p.TakeDamage(new DamageInfo(dd, dmg * 0.25f, ap, -1f, who, null, weapon));
                if (p.Dead) continue;
                if (fx.shockVehicles > 0f && AcastusWeapons.WarMachine(p) && p.stances?.stunner != null && Rand.Chance(fx.shockVehicles))
                {
                    p.stances.stunner.StunFor(fx.shockTicks > 0 ? fx.shockTicks : 120, who, false, true);
                    KnightEffects.ElectricFlash(p.DrawPos, map, 1.2f);
                    MoteMaker.ThrowText(p.DrawPos + new Vector3(0f, 0f, 1f), map, "SHOCKED", new Color(1f, 0.85f, 0.5f), 2.5f);
                }
            }
        }

        /// <summary>8th ed. thunderstrike gauntlet: a vehicle or monster slain by it is hurled at another enemy within 9 cells (D3 mortal wounds
        /// on a 4+ - here: 1 in 2 throws land, 30-90 crushing damage). The wreck / corpse lands at the victim's feet.</summary>
        static bool Hurl(Pawn dead, Map map, Vector3 from, Thing who, ThingDef weapon)
        {
            Thing body = dead.Corpse != null && dead.Corpse.Spawned ? (Thing)dead.Corpse : null;
            IntVec3 start = body != null ? body.Position : dead.PositionHeld;
            if (!start.IsValid || !start.InBounds(map)) return false;
            Pawn target = GenRadial.RadialDistinctThingsAround(start, map, 9f, true).OfType<Pawn>()
                .Where(p => !p.Dead && p.Spawned && p != dead && (who == null || p.HostileTo(who))).OrderBy(p => p.Position.DistanceTo(start)).FirstOrDefault();
            if (target == null) return false;
            Vector3 a = start.ToVector3Shifted(), b = target.DrawPos;
            // the arc of the thrown wreck: dust and debris along the throw, a crash where it lands
            for (int i = 1; i <= 6; i++)
            {
                Vector3 q = Vector3.Lerp(a, b, i / 7f) + new Vector3(0f, 0f, Mathf.Sin(i / 7f * Mathf.PI) * 2f);
                if (q.ShouldSpawnMotesAt(map)) FleckMaker.ThrowDustPuffThick(q, map, 1.4f, new Color(0.45f, 0.42f, 0.38f, 0.7f));
            }
            if (b.ShouldSpawnMotesAt(map))
            {
                FleckMaker.ThrowDustPuffThick(b, map, 3f, new Color(0.5f, 0.46f, 0.4f, 0.8f));
                for (int i = 0; i < 6; i++) FleckMaker.ThrowMicroSparks(b + Gen.RandomHorizontalVector(1f), map);
            }
            if (body != null && !body.Destroyed && target.Position.InBounds(map))
            {
                IntVec3 land = target.Position;
                if (body.def.category == ThingCategory.Item && land.Walkable(map)) { body.DeSpawn(); GenPlace.TryPlaceThing(body, land, map, ThingPlaceMode.Near); }
            }
            bool landed = Rand.Chance(0.5f);
            if (landed) target.TakeDamage(new DamageInfo(DamageDefOf.Crush, Rand.Range(30f, 90f), 2f, -1f, who, null, weapon));
            MoteMaker.ThrowText(b + new Vector3(0f, 0f, 1f), map, landed ? "HURLED" : "HURLED - missed", new Color(1f, 0.8f, 0.4f), 2.5f);
            return true;
        }
    }

    /// <summary>Records the fire mode of the turret that is firing (Low intensity las-impulsor etc.).</summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    public static class Patch_TurretFireMode
    {
        static void Prefix(VehicleTurret __instance) { QuestorisWeapons.pendingMode = __instance?.CurrentFireMode?.label; }
        static void Finalizer() { QuestorisWeapons.pendingMode = null; }
    }
}
