using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// TWIN CONVERSION BEAM CANNON rework (user 2026-11-13, from the Horus Heresy / 40k rules): the beam turns the target's matter into energy and
    /// needs distance to build up.
    ///  - three RANGE BANDS (fractions of the weapon's long range, after the tabletop 18" / 42" / 72"): short = weak, small blast;
    ///    medium = full strength; long = devastating, armour-cutting, big blast;
    ///  - the BLAST grows with the band;
    ///  - BLINDING FLASH: hostile survivors around the impact are stunned (longer at range);
    ///  - BRACED (Ordnance): fired while the Knight stands still = x1.5 damage;
    ///  - CHAIN DETONATIONS, LONG RANGE ONLY: 1-3 secondary bursts around the impact as the conversion spreads;
    ///  - SUNDERER: extra damage to hostile buildings and vehicles in the blast.
    /// Only projectiles whose DefModExtension_ShotFx has conversionBands = true (LIK_Proj_twinconversion).
    /// </summary>
    public static class ConversionBeam
    {
        // band edges / multipliers / blast radii now come from the weapon's DefModExtension_ShotFx (conv* fields; defaults = the twin cannon).
        // Armiger Moirax conversion beamer (2026-11-17): HH3 bands 0-15 / 15-30 / 30-45, x0.5 / x1 / x1.6, Blast 3" in every band, no flash or chain.
        static readonly int[] StunTicks = { 90, 150, 240 };
        static readonly string[] Names = { "short", "medium", "long" };
        public const float SundererMult = 0.5f;                            // extra damage to buildings / vehicles, as a share of the shot

        public static int Band(DefModExtension_ShotFx fx, Vector3 origin, Vector3 dest)
        {
            float full = fx != null && fx.growRange > 0f ? fx.growRange : 180f;
            float t = (dest - origin).MagnitudeHorizontal() / full;
            float s = fx != null ? fx.convShortEnd : 0.25f, l = fx != null ? fx.convLongStart : 0.58f;
            return t < s ? 0 : t < l ? 1 : 2;
        }

        static float DamageMult(DefModExtension_ShotFx fx, int b) { if (fx == null) return 1f; return b == 0 ? fx.convMult0 : b == 1 ? fx.convMult1 : fx.convMult2; }
        static float Radius(DefModExtension_ShotFx fx, int b) { if (fx == null) return 1.5f; return b == 0 ? fx.convRadius0 : b == 1 ? fx.convRadius1 : fx.convRadius2; }

        public static float Mult(DefModExtension_ShotFx fx, Vector3 origin, Vector3 dest) { return DamageMult(fx, Band(fx, origin, dest)); }
        public static string BandName(int b) { return Names[Mathf.Clamp(b, 0, 2)]; }

        /// <summary>The whole impact. Called by Projectile_KnightShell.Explode instead of the vanilla explosion.</summary>
        public static void Detonate(Projectile_KnightShell shot, DefModExtension_ShotFx fx, Map map, IntVec3 cell, Vector3 origin, Vector3 dest,
                                    Thing launcher, int damage, float armourPen, bool braced, ThingDef weapon, Thing intended)
        {
            if (map == null || !cell.InBounds(map)) return;
            int band = Band(fx, origin, dest);
            float radius = Radius(fx, band);
            if (braced && fx != null && fx.convBraced > 1f) damage = Mathf.RoundToInt(damage * fx.convBraced);
            armourPen *= band == 0 ? 0.7f : band == 1 ? 1f : 1.4f;     // HH3: AP 4 / 3 / 2 - armour-cutting grows with the band (user 2026-11-23)
            ThingDef projDef = shot.def;

            GenExplosion.DoExplosion(cell, map, radius, shot.def.projectile.damageDef, launcher, damage, armourPen, shot.def.projectile.soundExplode, weapon, projDef,
                intended, direction: origin.AngleToFlat(dest), screenShakeFactor: shot.def.projectile.screenShakeFactor * (0.7f + 0.4f * band));

            Sunder(map, cell, radius, launcher, damage, armourPen, projDef);
            if (fx == null || fx.convFlash) Flash(map, cell, radius, band, launcher);
            ShotFx.Blast(fx, cell, map, radius, origin);
            if (band == 2 && (fx == null || fx.convChain)) map.GetComponent<MapComponent_ConversionChain>()?.Queue(cell, radius, damage, armourPen, launcher, projDef);
            if (fx != null && fx.suppress > 0f) KnightEffects.Suppress(map, cell.ToVector3Shifted(), radius + fx.suppress, launcher);
        }

        /// <summary>Hostile buildings and vehicles in the blast take extra damage (tabletop: Sunderer of Fortresses).</summary>
        static void Sunder(Map map, IntVec3 cell, float radius, Thing launcher, int damage, float armourPen, ThingDef projDef)
        {
            var hit = new HashSet<Thing>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Thing t in c.GetThingList(map).ToArray())
                {
                    if (!hit.Add(t) || t.Destroyed) continue;
                    bool target = t is VehiclePawn || (t is Building && t.def.useHitPoints);
                    if (!target || launcher == null || t.Faction == null || !t.HostileTo(launcher)) continue;
                    t.TakeDamage(new DamageInfo(DamageDefOf.Bomb, damage * SundererMult, armourPen, -1f, launcher, null, projDef));
                }
            }
        }

        /// <summary>The matter-to-energy flash: a bright burst, and hostile pawns that survive near it are dazzled (stunned).</summary>
        static void Flash(Map map, IntVec3 cell, float radius, int band, Thing launcher)
        {
            Vector3 pos = cell.ToVector3Shifted();
            if (cell.ShouldSpawnMotesAt(map))
            {
                FleckDef flash = DefDatabase<FleckDef>.GetNamedSilentFail("ExplosionFlash") ?? DefDatabase<FleckDef>.GetNamedSilentFail("ShotFlash");
                if (flash != null) FleckMaker.Static(pos, map, flash, radius * (3f + band));
                FleckDef glow = DefDatabase<FleckDef>.GetNamedSilentFail("LightningGlow");
                if (glow != null) FleckMaker.Static(pos, map, glow, radius * (1.5f + 0.5f * band));
            }
            float reach = radius * 2f;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToArray())
            {
                if (p is VehiclePawn || p.Dead || p.stances?.stunner == null) continue;
                if (launcher != null && !p.HostileTo(launcher)) continue;
                if ((p.Position - cell).LengthHorizontalSquared > reach * reach) continue;
                p.stances.stunner.StunFor(StunTicks[band], launcher, false, true);
            }
        }
    }

    /// <summary>Long-range conversion shots: the conversion spreads - 1-3 secondary bursts around the impact a moment later. Not saved.</summary>
    public class MapComponent_ConversionChain : MapComponent
    {
        class Burst { public IntVec3 cell; public int tick; public int damage; public float ap, radius; public Thing launcher; public ThingDef proj; }
        readonly List<Burst> pending = new List<Burst>();
        public MapComponent_ConversionChain(Map map) : base(map) { }

        public void Queue(IntVec3 centre, float radius, int damage, float ap, Thing launcher, ThingDef proj)
        {
            int n = Rand.RangeInclusive(1, 3);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < n; i++)
            {
                IntVec3 c = centre;
                for (int k = 0; k < 8; k++)
                {
                    IntVec3 x = centre + (Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * Vector3.forward * Rand.Range(radius * 0.6f, radius + 2f)).ToIntVec3();
                    if (x.InBounds(map) && x.Walkable(map)) { c = x; break; }
                }
                pending.Add(new Burst { cell = c, tick = now + 8 + i * Rand.RangeInclusive(6, 12), damage = Mathf.RoundToInt(damage * 0.4f), ap = ap * 0.6f, radius = 1.8f, launcher = launcher, proj = proj });
            }
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Burst b = pending[i];
                if (now < b.tick) continue;
                pending.RemoveAt(i);
                if (!b.cell.InBounds(map)) continue;
                try
                {
                    GenExplosion.DoExplosion(b.cell, map, b.radius, b.proj?.projectile?.damageDef ?? DamageDefOf.Bomb, b.launcher, b.damage, b.ap,
                        b.proj?.projectile?.soundExplode, null, b.proj, screenShakeFactor: 0.6f);
                    FleckDef flash = DefDatabase<FleckDef>.GetNamedSilentFail("ExplosionFlash");
                    if (flash != null && b.cell.ShouldSpawnMotesAt(map)) FleckMaker.Static(b.cell.ToVector3Shifted(), map, flash, 4f);
                }
                catch (System.Exception e) { Log.ErrorOnce("[LIK] conversion chain burst failed: " + e.Message, 0xc0a1); }
            }
        }
    }
}
