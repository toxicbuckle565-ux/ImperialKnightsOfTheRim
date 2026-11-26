using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// What a Knight melee strike does besides its damage (2026-11-15, Cerastus weapon pass). Abilities only where a source gives one
    /// (user rule): shock lance = Impact (knockback) + Shock/Stun + Exoshock (HH2: a pierce sets off secondary explosions in the same target).
    /// </summary>
    public class DefModExtension_MeleeFx : DefModExtension
    {
        public float knockback;        // cells a struck pawn is thrown back (only pawns up to maxKnockSize body size)
        public float maxKnockSize = 3.5f;
        public int stunTicks;          // struck pawns are stunned this long (mechs and turrets too: the electrostatic discharge)
        public float exoshock;         // chance per struck target of a secondary explosion inside it
        public float exoshockDamage;   // damage of that secondary explosion (Bomb, on the same target only)
        public string look;            // "electric" = blue-white arcs where it strikes, "chain" = sparks + torn metal
        public float swingArc;         // > 0: the arm swings through the strike, this many degrees to each side (TurretMotion; visual only)
        public bool slash;             // a crescent slash mark drawn over the whole strike area (visual only)
        public float ignite;           // chance per thing struck to catch fire (HH3 Aflame; warblade)
        public bool arcStrike;         // the strike hits EVERYTHING in the arm's swing (swingArc each side, out to the weapon's reach) - user 2026-11-15
        public int swingTicks = 18;
        public float antiArmour;       // > 1: buildings, vehicles and big mechs in the blow take this many times the damage (siege claw, chain-cleaver strike)
        public float panic;            // chance per living enemy near the blow to break and flee (reaper chain-cleaver: 'its very sound makes enemies flee')
        public float reapingBlow;      // > 1: HH3 Reaping Blow - a sweep that catches 3+ enemies hits each of them this many times harder
        public float thrust;           // > 0: the arm LUNGES this many cells along its aim and snaps back (shock lance on its rail; visual only)
        public int thrustTicks = 20;
        // Questoris lore pass 2026-11-23 (QuestorisWeapons.MeleeAfter)
        public float shockVehicles;    // HH3 Shock: chance a struck vehicle / big mech is stunned ...
        public int shockTicks;         // ... for this long
        public bool hurl;              // 8th ed. thunderstrike: a slain vehicle / monster is thrown at another enemy
        public float shred;            // HH3 Shred: chance per struck pawn of +25 % damage
    }

    /// <summary>A Knight's melee strike (the 'projectile' that carries a close-combat blow) with optional extra effects.</summary>
    public class Projectile_KnightMelee : Projectile_Explosive
    {
        bool arc; Vector3 pivot; float aim, reach, half;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref arc, "arc", false); Scribe_Values.Look(ref pivot, "pivot");
            Scribe_Values.Look(ref aim, "aim"); Scribe_Values.Look(ref reach, "reach"); Scribe_Values.Look(ref half, "half");
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            if (TurretMotion.pendingArc)
            {
                TurretMotion.pendingArc = false;
                arc = true; pivot = TurretMotion.arcPivot; aim = TurretMotion.arcAim; reach = TurretMotion.arcReach; half = TurretMotion.arcHalf;
            }
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        }

        /// <summary>The whole swing: every enemy pawn, building and tree inside the arc gets the full blow.</summary>
        void ArcStrike(DefModExtension_MeleeFx fx)
        {
            Map map = Map;
            Thing who = launcher;
            DamageDef dd = def.projectile.damageDef;
            int dmg = DamageAmount; float ap = ArmorPenetration;
            ThingDef weapon = equipmentDef;
            SoundDef snd = def.projectile.soundExplode;
            IntVec3 pc = pivot.ToIntVec3();
            List<Thing> hit = new List<Thing>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(pc, Mathf.Min(reach, GenRadial.MaxRadialPatternRadius - 1f), true))
            {
                if (!c.InBounds(map)) continue;
                Vector3 v = c.ToVector3Shifted() - pivot; v.y = 0f;
                if (v.MagnitudeHorizontal() < 1.2f) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(aim, v.AngleFlat())) > half) continue;
                foreach (Thing t in c.GetThingList(map))
                {
                    if (t == who || hit.Contains(t)) continue;
                    if (!(t is Pawn) && !(t is Building) && !(t is Plant)) continue;
                    if (who != null && who.Faction != null && t.Faction == who.Faction) continue;     // never its own side
                    if (t is Pawn pw && (pw.Dead || pw is Vehicles.VehiclePawn vp && vp.Faction == who?.Faction)) continue;
                    hit.Add(t);
                }
            }
            Vector3 dirAim = Vector3.forward.RotatedBy(aim);
            Destroy();
            if (snd != null) snd.PlayOneShot(new TargetInfo((pivot + dirAim * reach * 0.6f).ToIntVec3(), map));
            KnightEffects.ArcSlash(map, pivot, aim, reach, half, fx.look);
            if (fx.look == "flame") KnightEffects.BurningArc(map, pivot, aim, reach, half, who);
            // HH3 Reaping Blow (outnumbered -> extra attacks): a sweep through 3+ enemies hits each of them harder
            int foes = 0;
            foreach (Thing t in hit) if (t is Pawn fp && !fp.Dead) foes++;
            if (fx.reapingBlow > 1f && foes >= 3) dmg = Mathf.RoundToInt(dmg * fx.reapingBlow);
            int fxN = 0;
            foreach (Thing t in hit)
            {
                if (t.Destroyed) continue;
                Vector3 tp = t.DrawPos;
                float mult = fx.antiArmour > 1f && KnightEffects.WarMachine(t) ? fx.antiArmour : 1f;
                t.TakeDamage(new DamageInfo(dd, dmg * mult, ap, (tp - pivot).AngleFlat(), who, null, weapon));
                if (fxN++ < 8 && tp.ShouldSpawnMotesAt(map)) { KnightEffects.HitBurst(map, tp, fx.look); if (fx.look != "flame") KnightEffects.Grind(map, tp, 24); }
                if (fx.ignite > 0f && !t.Destroyed && Rand.Chance(fx.ignite)) { if (t is Pawn ip && !ip.Dead) ip.TryAttachFire(Rand.Range(0.4f, 0.7f), who); else FireUtility.TryStartFireIn(t.Position, map, 0.5f, who); }
            }
            if (fx.panic > 0f) KnightEffects.Panic(map, (pivot + dirAim * reach * 0.5f).ToIntVec3(), reach * 0.8f, who, fx.panic);
        }

        protected override void Explode()
        {
            DefModExtension_MeleeFx afx = def.GetModExtension<DefModExtension_MeleeFx>();
            if (arc && afx != null && afx.arcStrike && Map != null) { ArcStrike(afx); return; }
            Map map = Map;
            IntVec3 cell = Position;
            Vector3 from = origin;
            Thing who = launcher;
            float radius = def.projectile.explosionRadius;
            DefModExtension_MeleeFx fx = def.GetModExtension<DefModExtension_MeleeFx>();
            // who stands in the strike (collected before the blow: the dead drop as corpses)
            List<Pawn> struck = new List<Pawn>();
            if (map != null)
                foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, Mathf.Max(0.9f, radius), true))
                {
                    if (!c.InBounds(map)) continue;
                    foreach (Thing t in c.GetThingList(map))
                        if (t is Pawn p && p != who && !struck.Contains(p) && (who == null || p.Faction == null || p.Faction != who.Faction)) struck.Add(p);
                }
            int dmg0 = DamageAmount; float ap0 = ArmorPenetration; ThingDef weapon0 = equipmentDef; Thing target0 = usedTarget.Thing ?? intendedTarget.Thing;
            DamageDef dd0 = def.projectile.damageDef;
            // 2026-11-25 (Steam bug report: "the Armiger chain cleaver damages the Knight it is attached to"): the blow's blast lands right next
            // to the Knight's own body - the striking Knight is never hurt by its own melee blow
            KnightMeleeGuard.striker = who;
            try { base.Explode(); }
            finally { KnightMeleeGuard.striker = null; }
            if (fx == null || map == null) return;
            if (fx.antiArmour > 1f) DominusWeapons.AntiArmour(map, cell, radius, target0, fx.antiArmour, dmg0, ap0, who, weapon0, dd0);
            if (fx.panic > 0f) KnightEffects.Panic(map, cell, radius + 2.5f, who, fx.panic);
            if (fx.look == "electric" && cell.InBounds(map)) KnightEffects.ElectricFlash(cell.ToVector3Shifted(), map, 1.4f);
            if (fx.slash && cell.InBounds(map)) KnightEffects.Slash(map, cell.ToVector3Shifted(), from, Mathf.Max(1f, radius), fx.look);
            foreach (Pawn p in struck)
            {
                if (p.Destroyed || p.Dead || p.Map != map) continue;
                if (fx.exoshock > 0f && Rand.Chance(fx.exoshock)) KnightEffects.Exoshock(p, fx.exoshockDamage, who);
                if (p.Destroyed || p.Dead) continue;
                if (fx.stunTicks > 0 && p.stances?.stunner != null) p.stances.stunner.StunFor(fx.stunTicks, who, false, true);
                if (fx.knockback > 0f) KnightEffects.Knockback(p, from, fx.knockback, fx.maxKnockSize);
            }
            QuestorisWeapons.MeleeAfter(fx, struck, map, from, dmg0, ap0, dd0, who, weapon0);
        }
    }

    public static class KnightMeleeGuard { [System.ThreadStatic] public static Thing striker; }

    public static class KnightEffects
    {
        static HediffDef suppressed;
        static TraitDef nerves; static bool nervesLooked;
        static TraitDef Nerves { get { if (!nervesLooked) { nervesLooked = true; nerves = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves"); } return nerves; } }
        static HediffDef Suppressed => suppressed ?? (suppressed = DefDatabase<HediffDef>.GetNamedSilentFail("LIK_Suppressed"));

        /// <summary>Buildings, vehicles and big mechs (what Armourbane / Armour-breaker weapons are made for).</summary>
        public static bool WarMachine(Thing t)
        {
            return t is Building || t is Vehicles.VehiclePawn || t is Pawn p && p.RaceProps.IsMechanoid && p.BodySize >= 2f;
        }

        static bool Enemy(Pawn p, Thing instigator)
        {
            if (p == null || p.Dead || p.Destroyed) return false;
            if (p is Vehicles.VehiclePawn) return false;
            if (instigator == null || instigator.Faction == null) return true;
            return p.Faction != instigator.Faction;
        }

        /// <summary>Castigator bolt cannon (HH3 Suppressive): pawns hit or near the hits are pinned down - slower to move and to aim for a few seconds.</summary>
        public static void Suppress(Map map, Vector3 pos, float radius, Thing instigator)
        {
            if (map == null || Suppressed == null) return;
            IntVec3 cell = pos.ToIntVec3();
            if (!cell.InBounds(map)) return;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(cell, map, radius, true))
            {
                Pawn p = t as Pawn;
                if (!Enemy(p, instigator) || p.RaceProps.IsMechanoid && p.BodySize >= 2.5f) continue;     // big war machines shrug it off
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Suppressed);
                if (h == null) { h = HediffMaker.MakeHediff(Suppressed, p); h.Severity = 0.3f; p.health.AddHediff(h); }
                else h.Severity = Mathf.Min(1f, h.Severity + 0.15f);
                HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
                if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, 300);
            }
        }

        /// <summary>Acheron flame cannon (HH3 Panic): living things caught in the fire may break and run. Mechs and the mindless don't panic.</summary>
        public static void Panic(Map map, IntVec3 cell, float radius, Thing instigator, float chance)
        {
            if (map == null || chance <= 0f || !cell.InBounds(map)) return;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(cell, map, Mathf.Max(0.9f, radius), true))
            {
                Pawn p = t as Pawn;
                if (!Enemy(p, instigator) || p.Downed || p.RaceProps.IsMechanoid || p.InMentalState || p.mindState?.mentalStateHandler == null) continue;
                if (p.RaceProps.Humanlike && p.story?.traits != null && Nerves != null && p.story.traits.DegreeOfTrait(Nerves) >= 2) continue;   // iron-willed
                if (!Rand.Chance(chance)) continue;
                p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, "the Knight's firestorm", true, false, false, null, false);
            }
        }

        /// <summary>Mass-reactive bolt (lore): the shell bores in and then detonates inside its target.</summary>
        public static void MassReactive(Thing hit, float damage, Thing instigator, ThingDef weapon, Map map, Vector3 pos)
        {
            if (map != null && pos.ToIntVec3().InBounds(map) && pos.ShouldSpawnMotesAt(map))
            {
                GlowFx.Flash(pos, map, 4f);
                FleckDef dry = DefDatabase<FleckDef>.GetNamedSilentFail("BlastDry");
                if (dry != null) FleckMaker.ThrowExplosionCell(pos.ToIntVec3(), map, dry, new Color(1f, 0.85f, 0.6f));
                GlowFx.Fire(pos, map, 0.6f);
                FleckMaker.ThrowSmoke(pos, map, 0.7f);
                for (int i = 0; i < 2; i++) FleckMaker.ThrowMicroSparks(pos + Gen.RandomHorizontalVector(0.4f), map);
            }
            if (hit == null || hit.Destroyed || damage <= 0f) return;
            if (!(hit is Pawn) && !(hit is Building)) return;
            if (hit is Pawn pw && pw.Dead) return;
            hit.TakeDamage(new DamageInfo(DamageDefOf.Bomb, damage, 0.5f, -1f, instigator, null, weapon));
        }

        /// <summary>Exoshock (HH2 shock lance): the strike sets off a chain of secondary explosions inside the same target.</summary>
        public static void Exoshock(Pawn p, float damage, Thing instigator)
        {
            Map map = p.Map;
            Vector3 pos = p.DrawPos;
            if (pos.ShouldSpawnMotesAt(map))
            {
                ElectricFlash(pos, map, 1f);
                GlowFx.Flash(pos, map, 6f);
                FleckDef blast = DefDatabase<FleckDef>.GetNamedSilentFail("BlastEMP");
                if (blast != null) FleckMaker.ThrowExplosionCell(p.Position, map, blast, new Color(0.6f, 0.8f, 1f));
            }
            SoundDef s = DefDatabase<SoundDef>.GetNamedSilentFail("Explosion_EMP");
            s?.PlayOneShot(new TargetInfo(p.Position, map));
            for (int i = 0; i < 2 && !p.Dead && !p.Destroyed; i++)
                p.TakeDamage(new DamageInfo(DamageDefOf.Bomb, damage * 0.5f, 0.8f, -1f, instigator));
            if (!p.Dead && !p.Destroyed && (p.RaceProps.IsMechanoid)) p.TakeDamage(new DamageInfo(DamageDefOf.EMP, damage * 0.3f, 0f, -1f, instigator));
        }

        /// <summary>The lance's Impact: the struck thing is hurled back along the line of the thrust until something stops it.</summary>
        public static void Knockback(Pawn p, Vector3 from, float cells, float maxSize)
        {
            if (p == null || p.Dead || p.Destroyed || !p.Spawned || p.BodySize > maxSize || p is Vehicles.VehiclePawn) return;
            Map map = p.Map;
            Vector3 dir = (p.DrawPos - from).Yto0();
            if (dir.sqrMagnitude < 0.01f) dir = Gen.RandomHorizontalVector(1f);
            dir.Normalize();
            IntVec3 start = p.Position, end = start;
            int n = Mathf.RoundToInt(cells);
            for (int i = 1; i <= n; i++)
            {
                IntVec3 c = (start.ToVector3Shifted() + dir * i).ToIntVec3();
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) break;
                end = c;
            }
            if (end == start) return;
            // dust thrown up along the path, a heavy landing
            if (start.ShouldSpawnMotesAt(map))
                for (int i = 0; i <= (end - start).LengthHorizontal; i++)
                {
                    Vector3 q = start.ToVector3Shifted() + dir * i;
                    FleckMaker.ThrowDustPuffThick(q, map, 1.2f, new Color(0.5f, 0.46f, 0.4f, 0.7f));
                }
            p.Position = end;
            p.Notify_Teleported(true, true);
            if (end.ShouldSpawnMotesAt(map)) FleckMaker.ThrowDustPuffThick(end.ToVector3Shifted(), map, 2.2f, new Color(0.5f, 0.46f, 0.4f, 0.8f));
        }

        /// <summary>A crescent slash over the strike area, bulging away from the Knight, with sparks and grit thrown off along the arc.</summary>
        public static void Slash(Map map, Vector3 center, Vector3 from, float radius, string look)
        {
            if (!center.ShouldSpawnMotesAt(map)) return;
            FleckDef def = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_Slash");
            Vector3 dir = (center - from).Yto0();
            float angle = dir.sqrMagnitude > 0.01f ? dir.AngleFlat() : Rand.Range(0f, 360f);
            float size = radius * 2.9f;
            // the crescent sits slightly behind the strike centre so its arc runs through the area of effect
            Vector3 at = center - dir.normalized * radius * 0.25f;
            at.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            if (def != null && look == "chain")
            {
                // reaper chainfist = two chain blades turning opposite ways: two slashes crossing in an X
                for (int k = -1; k <= 1; k += 2)
                {
                    FleckCreationData x = FleckMaker.GetDataStatic(at, map, def, size * 1.12f);
                    x.rotation = angle + 34f * k + Rand.Range(-6f, 6f); x.instanceColor = new Color(1f, 0.7f, 0.38f, 0.95f);
                    map.flecks.CreateFleck(x);
                    FleckCreationData xi = FleckMaker.GetDataStatic(at + dir.normalized * radius * 0.12f, map, def, size * 0.8f);
                    xi.rotation = x.rotation; xi.instanceColor = Color.white;
                    map.flecks.CreateFleck(xi);
                }
                ChainImpact(map, center, radius);
                Grind(map, center, 32);
            }
            else if (def != null)
            {
                Color outer = look == "chain" ? new Color(1f, 0.72f, 0.4f, 0.95f) : new Color(0.7f, 0.85f, 1f, 0.95f);
                FleckCreationData d = FleckMaker.GetDataStatic(at, map, def, size);
                d.rotation = angle + Rand.Range(-12f, 12f); d.instanceColor = outer;
                map.flecks.CreateFleck(d);
                FleckCreationData d2 = FleckMaker.GetDataStatic(at + dir.normalized * radius * 0.15f, map, def, size * 0.72f);
                d2.rotation = d.rotation + Rand.Range(-6f, 6f); d2.instanceColor = Color.white;
                map.flecks.CreateFleck(d2);
            }
            // sparks / torn grit thrown outward along the arc
            Vector3 fwd = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(-60f, 60f, i / 8f);
                Vector3 p = at + fwd.RotatedBy(a) * radius * 1.05f;
                if (!p.InBounds(map)) continue;
                FleckMaker.ThrowMicroSparks(p, map);
                if (look == "chain" && i % 3 == 1) FleckMaker.ThrowDustPuffThick(p, map, 1.1f, new Color(0.5f, 0.46f, 0.4f, 0.6f));
            }
        }

        /// <summary>The swing's whole fan: slashes along arcs around the arm at three distances, sparks and grit off the outer edge.</summary>
        public static void ArcSlash(Map map, Vector3 pivot, float aim, float reach, float half, string look)
        {
            FleckDef def = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_Slash");
            Vector3 u = Vector3.forward.RotatedBy(aim);
            float alt = AltitudeLayer.MoteOverhead.AltitudeFor();
            // the slash picture: arc radius 0.44 x picture size, arc centre 0.24 x size below the picture centre
            float[] radii = { reach * 0.45f, reach * 0.75f, reach * 1.0f };
            for (int i = 0; def != null && i < radii.Length; i++)
            {
                float size = radii[i] / 0.44f;
                Vector3 at = pivot + u * (0.24f * size); at.y = alt;
                if (!at.InBounds(map)) continue;
                FleckCreationData d = FleckMaker.GetDataStatic(at, map, def, size);
                d.rotation = aim + Rand.Range(-4f, 4f);
                d.instanceColor = i == 1 ? (look == "flame" ? new Color(1f, 0.85f, 0.5f, 1f) : Color.white)
                    : (look == "chain" ? new Color(1f, 0.7f, 0.38f, 0.9f) : look == "flame" ? new Color(1f, 0.45f, 0.12f, 0.95f) : new Color(0.7f, 0.85f, 1f, 0.9f));
                map.flecks.CreateFleck(d);
            }
            for (int i = 0; i <= 12; i++)
            {
                Vector3 p = pivot + u.RotatedBy(Mathf.Lerp(-half, half, i / 12f)) * reach;
                if (!p.InBounds(map) || !p.ShouldSpawnMotesAt(map)) continue;
                FleckMaker.ThrowMicroSparks(p, map);
                if (look == "chain" && i % 2 == 0) FleckMaker.ThrowDustPuffThick(p, map, 1.3f, new Color(0.5f, 0.46f, 0.4f, 0.6f));
            }
            ShotFx.Shake(pivot + u * reach * 0.5f, map, 3f);
        }

        /// <summary>One thing caught by the swing.</summary>
        public static void HitBurst(Map map, Vector3 pos, string look)
        {
            FleckDef spark = DefDatabase<FleckDef>.GetNamedSilentFail("SparkFlash");
            if (spark != null) FleckMaker.Static(pos, map, spark, 4f);
            for (int k = 0; k < 4; k++) FleckMaker.ThrowMicroSparks(pos + Gen.RandomHorizontalVector(0.5f), map);
            FleckDef debris = DefDatabase<FleckDef>.GetNamedSilentFail("ThrownDebris");
            if (look == "flame") { GlowFx.Fire(pos, map, 0.9f); FleckDef fb = DefDatabase<FleckDef>.GetNamedSilentFail("BlastFlame"); if (fb != null && pos.ToIntVec3().InBounds(map)) FleckMaker.ThrowExplosionCell(pos.ToIntVec3(), map, fb, Color.white); }
            for (int k = 0; debris != null && look == "chain" && k < 3; k++)
            {
                FleckCreationData d = FleckMaker.GetDataStatic(pos, map, debris, Rand.Range(0.6f, 1.1f));
                d.rotation = Rand.Range(0f, 360f); d.rotationRate = Rand.Range(200f, 500f);
                d.velocityAngle = Rand.Range(0f, 360f); d.velocitySpeed = Rand.Range(3f, 7f);
                map.flecks.CreateFleck(d);
            }
        }

        /// <summary>The fist biting in: spark flash, flying shards, dust ring, cracked ground, a hard shake.</summary>
        static void ChainImpact(Map map, Vector3 pos, float radius)
        {
            FleckDef spark = DefDatabase<FleckDef>.GetNamedSilentFail("SparkFlash");
            if (spark != null) FleckMaker.Static(pos, map, spark, 7f);
            GlowFx.Fire(pos, map, 0.8f);
            FleckDef debris = DefDatabase<FleckDef>.GetNamedSilentFail("ThrownDebris");
            for (int k = 0; debris != null && k < 10; k++)
            {
                FleckCreationData d = FleckMaker.GetDataStatic(pos + Gen.RandomHorizontalVector(0.6f), map, debris, Rand.Range(0.7f, 1.4f));
                d.rotation = Rand.Range(0f, 360f); d.rotationRate = Rand.Range(200f, 500f) * (Rand.Bool ? 1f : -1f);
                d.velocityAngle = Rand.Range(0f, 360f); d.velocitySpeed = Rand.Range(4f, 9f);
                map.flecks.CreateFleck(d);
            }
            for (int k = 0; k < 8; k++)
            {
                Vector3 q = pos + Quaternion.AngleAxis(45f * k + Rand.Range(-10f, 10f), Vector3.up) * Vector3.forward * (radius + 0.4f);
                if (q.InBounds(map)) FleckMaker.ThrowDustPuffThick(q, map, 1.6f, new Color(0.5f, 0.46f, 0.4f, 0.7f));
            }
            FleckDef crack = DefDatabase<FleckDef>.GetNamedSilentFail("GroundCrack");
            if (crack != null) { FleckCreationData c = FleckMaker.GetDataStatic(pos, map, crack, 1.6f + radius * 0.6f); c.rotation = Rand.Range(0f, 360f); map.flecks.CreateFleck(c); }
            ShotFx.Shake(pos, map, 3f);
        }

        // chains still biting after the hit: a shower of sparks and grit at the strike point for a moment (not saved, visual only)
        struct GrindSpot { public Map map; public Vector3 pos; public int until; }
        static readonly List<GrindSpot> grinds = new List<GrindSpot>();

        public static void Grind(Map map, Vector3 pos, int ticks)
        {
            if (grinds.Count < 30) grinds.Add(new GrindSpot { map = map, pos = pos, until = Find.TickManager.TicksGame + ticks });
        }

        // the warblade's slash keeps burning along its path for a moment (visual; real fire only where the strike ignited something)
        class BurnArc { public Map map; public int start, until; public List<Vector3> spots; }
        static readonly List<BurnArc> burns = new List<BurnArc>();
        static FleckDef[] fireFrames;

        public static void BurningArc(Map map, Vector3 pivot, float aim, float reach, float half, Thing instigator)
        {
            int now = Find.TickManager.TicksGame;
            Vector3 u = Vector3.forward.RotatedBy(aim);
            // fixed fire spots along the three slash lines (they flicker in place, user 2026-11-15: random darts looked wrong)
            List<Vector3> spots = new List<Vector3>();
            float[] bands = { 0.45f, 0.75f, 1.0f };
            foreach (float band in bands)
            {
                int count = Mathf.RoundToInt(4 + 5 * band);
                for (int i = 0; i < count; i++)
                {
                    float a = Mathf.Lerp(-half, half, (i + Rand.Range(0.2f, 0.8f)) / count);
                    Vector3 p = pivot + u.RotatedBy(a) * reach * band * Rand.Range(0.94f, 1.04f);
                    if (p.InBounds(map)) spots.Add(p);
                }
            }
            if (burns.Count < 12) burns.Add(new BurnArc { map = map, start = now, until = now + 150, spots = spots });
            // a few spots along the outer edge actually catch (Aflame)
            for (int i = 0; i < 9; i++)
            {
                IntVec3 c = (pivot + u.RotatedBy(Mathf.Lerp(-half, half, i / 8f)) * reach * Rand.Range(0.75f, 1f)).ToIntVec3();
                if (c.InBounds(map) && Rand.Chance(0.3f)) FireUtility.TryStartFireIn(c, map, 0.35f, instigator);
            }
            ShotFx.HotGround(pivot + u * reach * 0.8f, map, 2f + reach * 0.4f);
        }

        static void TickBurns(Map map, int now)
        {
            if (fireFrames == null) fireFrames = new[] { DefDatabase<FleckDef>.GetNamedSilentFail("LIK_ArcFireA"), DefDatabase<FleckDef>.GetNamedSilentFail("LIK_ArcFireB"), DefDatabase<FleckDef>.GetNamedSilentFail("LIK_ArcFireC") };
            for (int i = burns.Count - 1; i >= 0; i--)
            {
                BurnArc b = burns[i];
                if (now >= b.until || b.map == null || b.map.Index < 0) { burns.RemoveAt(i); continue; }
                if (b.map != map || (now - b.start) % 6 != 0) continue;
                float life = 1f - (now - b.start) / (float)(b.until - b.start);             // dies down over time
                foreach (Vector3 p in b.spots)
                {
                    if (life < 0.5f && Rand.Chance(0.5f - life)) continue;                     // spots go out one by one at the end
                    if (!p.ShouldSpawnMotesAt(map)) continue;
                    FleckDef f = fireFrames[Rand.Range(0, 3)];
                    if (f != null)
                    {
                        Vector3 q = p; q.y = AltitudeLayer.MoteOverheadLow.AltitudeFor();
                        map.flecks.CreateFleck(FleckMaker.GetDataStatic(q, map, f, Rand.Range(0.85f, 1.15f) * (0.45f + 0.55f * life)));
                    }
                    if (Rand.Chance(0.06f)) FleckMaker.ThrowSmoke(p, map, 0.5f + 0.5f * life);
                    if (Rand.Chance(0.03f)) FleckMaker.ThrowMicroSparks(p, map);
                }
            }
        }

        public static void TickGrinds(Map map)
        {
            if (burns.Count > 0) TickBurns(map, Find.TickManager.TicksGame);
            if (grinds.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = grinds.Count - 1; i >= 0; i--)
            {
                GrindSpot g = grinds[i];
                if (now >= g.until || g.map == null || g.map.Index < 0) { grinds.RemoveAt(i); continue; }
                if (g.map != map || !g.pos.ShouldSpawnMotesAt(map)) continue;
                for (int k = 0; k < 2; k++) FleckMaker.ThrowMicroSparks(g.pos + Gen.RandomHorizontalVector(0.7f), map);
                if (now % 4 == 0) FleckMaker.ThrowDustPuffThick(g.pos + Gen.RandomHorizontalVector(0.5f), map, 0.9f, new Color(0.35f, 0.32f, 0.3f, 0.6f));
                if (now % 6 == 0) GlowFx.Fire(g.pos, map, 0.35f);
            }
        }

        /// <summary>Crackling blue-white discharge (shock lance).</summary>
        public static void ElectricFlash(Vector3 pos, Map map, float scale)
        {
            if (!pos.InBounds(map) || !pos.ShouldSpawnMotesAt(map)) return;
            GlowFx.Lightning(pos, map, 1.5f * scale);
            FleckDef spark = DefDatabase<FleckDef>.GetNamedSilentFail("SparkFlash");
            if (spark != null) FleckMaker.Static(pos, map, spark, 4f * scale);
            FleckDef line = DefDatabase<FleckDef>.GetNamedSilentFail("LineEMP");
            if (line == null) return;
            for (int i = 0; i < 4; i++)
            {
                Vector3 b = pos + Gen.RandomHorizontalVector(2.2f * scale);
                Vector3 v = b - pos;
                float len = v.MagnitudeHorizontal();
                if (len < 0.2f) continue;
                FleckCreationData d = FleckMaker.GetDataStatic(pos + v * 0.5f, map, line);
                d.exactScale = new Vector3(len, 1f, 0.6f);
                d.rotation = Mathf.Atan2(-v.z, v.x) * 57.29578f;
                map.flecks.CreateFleck(d);
            }
        }
    }
}

namespace LIK
{
    /// <summary>
    /// Acheron flame cannon (user 2026-11-15): when it keeps hammering one spot the fire there spreads - every further shot landing near the
    /// same spot widens the burning area (patchy outer ring, so it creeps outward). A spot not hit for 4 seconds starts small again. Not saved.
    /// </summary>
    public static class FirePools
    {
        class Pool { public Map map; public Vector3 center; public float radius; public int last, hits; }
        static readonly System.Collections.Generic.List<Pool> pools = new System.Collections.Generic.List<Pool>();
        const int Forget = 240;
        static int diag;
        static ThingDef fuel; static FleckDef flame; static bool looked;
        static void Look() { if (looked) return; looked = true; fuel = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Fuel"); flame = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_FlameStream"); }
        static ThingDef Fuel { get { Look(); return fuel; } }
        static FleckDef FlameFleck { get { Look(); return flame; } }
        const float Join = 3.5f;

        public static void Hit(Map map, IntVec3 cell, float baseRadius, Thing instigator, float growth, float max)
        {
            if (map == null || !cell.InBounds(map)) return;
            int now = Find.TickManager.TicksGame;
            pools.RemoveAll(p => p.map == null || p.map != map && p.map.Index < 0 || now - p.last > Forget);
            Vector3 pos = cell.ToVector3Shifted();
            Pool pool = null;
            float best = Join;
            foreach (Pool p in pools)
            {
                if (p.map != map) continue;
                float d = (p.center - pos).MagnitudeHorizontal();
                if (d <= best) { best = d; pool = p; }
            }
            if (pool == null) { pool = new Pool { map = map, center = pos, radius = baseRadius }; pools.Add(pool); }
            else
            {
                pool.radius = Mathf.Min(max, pool.radius + growth);
                pool.center = Vector3.Lerp(pool.center, pos, 0.25f);      // the fire drifts a little toward where the shots land
            }
            pool.last = now;

            IntVec3 c0 = pool.center.ToIntVec3();
            float r = Mathf.Min(pool.radius, GenRadial.MaxRadialPatternRadius - 1f);
            int n = GenRadial.NumCellsInRadius(r);
            for (int i = 0; i < n; i++)
            {
                IntVec3 c = c0 + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;
                float d = c.DistanceTo(c0);
                // the shot's own splash always burns; the widening ring catches patchily, more often the nearer it is
                bool inner = c.DistanceTo(cell) <= baseRadius;
                if (!inner && !Rand.Chance(Mathf.Lerp(0.55f, 0.2f, Mathf.Clamp01((d - baseRadius) / Mathf.Max(0.5f, r - baseRadius))))) continue;
                if (!inner && Fuel != null && c.Walkable(map) && Rand.Chance(0.6f)) FilthMaker.TryMakeFilth(c, map, Fuel, 1);
                FireUtility.TryStartFireIn(c, map, Rand.Range(0.4f, 0.85f), instigator);
            }
            if (Prefs.DevMode && diag < 40) { diag++; Log.Message("[LIK] fire pool: radius " + r.ToString("F1") + " at " + c0 + " (hit at " + cell + ")"); }
            if (++pool.hits % 3 == 1) ShotFx.HotGround(pool.center, map, 1.2f + r * 0.4f);
            if (r > baseRadius + 0.5f && pool.center.ShouldSpawnMotesAt(map))
                if (pool.hits % 2 == 0) FleckMaker.ThrowSmoke(pool.center + Gen.RandomHorizontalVector(r * 0.6f), map, 1.0f + r * 0.12f);   // smaller, user 2026-11-22: the smoke hid the fight
        }
    }
}
