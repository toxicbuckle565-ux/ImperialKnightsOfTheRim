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
        // role split (user 2026-11-24): conversion beams are the FORTRESS-breakers (lore: "reduce the mightiest edifices to ash"), the magna
        // lascannon the anti-tank gun - extra damage to hostile buildings +100 %, to vehicles only +25 % (was +50 % to both)
        public const float SundererBuildingMult = 1.0f, SundererVehicleMult = 0.25f;

        const float MaxRadius = 8f;
        static float lastArmour, lastArmourMult = 1f;   // for the dev readout only

        /// <summary>Matter (0..2) of what the shot struck. A pawn / vehicle on the impact cell (else the intended pawn within 1.5 cells): its armour.
        /// Otherwise a wall / building there (else the intended building within 1.5 cells): user 2026-11-24 "the explosion should also increase
        /// when hitting the wall" - max hit points / 400 (stuff-based: wood ~0.5, steel ~0.75, granite ~1.3, plasteel / natural rock 2).
        /// Bare ground = 0.</summary>
        static float StruckArmour(Map map, IntVec3 cell, Thing intended)
        {
            Pawn p = cell.GetFirstPawn(map);
            if (p == null && intended is Pawn ip && ip.Spawned && ip.Map == map && ip.OccupiedRect().ClosestCellTo(cell).DistanceTo(cell) <= 1.5f) p = ip;
            if (p != null && !p.Dead)
            {
                try { return Gravity.Armour(p); } catch { return 0f; }
            }
            Building b = cell.GetEdifice(map);
            if (b == null && intended is Building ib && ib.Spawned && ib.Map == map && ib.OccupiedRect().ClosestCellTo(cell).DistanceTo(cell) <= 1.5f) b = ib;
            if (b == null || !b.def.useHitPoints) return 0f;
            return Mathf.Clamp(b.MaxHitPoints / WallHpPerArmour, 0f, 2f);
        }
        const float WallHpPerArmour = 400f;

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
            // user 2026-11-24 (lore: an antimatter beam - the TARGET's matter annihilates): more armour = more matter = a bigger explosion.
            // Armour of the thing struck, 0..2 (Gravity.Armour: natural + best worn, CE-aware; Knights / vehicles >= 1.6) -> radius x (1 + 0.5 a).
            float armour = StruckArmour(map, cell, intended);
            float armourMult = 1f + 0.5f * armour;
            radius = Mathf.Min(radius * armourMult, MaxRadius);
            lastArmour = armour; lastArmourMult = armourMult;
            if (braced && fx != null && fx.convBraced > 1f) damage = Mathf.RoundToInt(damage * fx.convBraced);
            armourPen *= band == 0 ? 0.7f : band == 1 ? 1f : 1.4f;     // HH3: AP 4 / 3 / 2 - armour-cutting grows with the band (user 2026-11-23)
            ThingDef projDef = shot.def;

            GenExplosion.DoExplosion(cell, map, radius, shot.def.projectile.damageDef, launcher, damage, armourPen, shot.def.projectile.soundExplode, weapon, projDef,
                intended, direction: origin.AngleToFlat(dest), screenShakeFactor: shot.def.projectile.screenShakeFactor * (0.7f + 0.4f * band));

            Sunder(map, cell, radius, launcher, damage, armourPen, projDef);
            int stunned = 0;
            if (fx == null || fx.convFlash) stunned = Flash(map, cell, radius, band, launcher);
            ShotFx.Blast(fx, cell, map, radius, origin);
            bool chain = band == 2 && (fx == null || fx.convChain);
            if (chain) map.GetComponent<MapComponent_ConversionChain>()?.Queue(cell, radius, damage, armourPen, launcher, projDef);
            if (fx != null && fx.suppress > 0f) KnightEffects.Suppress(map, cell.ToVector3Shifted(), radius + fx.suppress, launcher);
            if (Prefs.DevMode) Readout(fx, map, cell, origin, dest, band, damage, armourPen, radius, braced, stunned, chain, projDef);
        }

        /// <summary>DEV MODE readout (user 2026-11-24: "build the readout first before we see if the conversion is even working correctly"):
        /// a floating label at every conversion impact, coloured by band, plus the full numbers in the log.</summary>
        static void Readout(DefModExtension_ShotFx fx, Map map, IntVec3 cell, Vector3 origin, Vector3 dest, int band, int damage, float ap, float radius,
                            bool braced, int stunned, bool chain, ThingDef projDef)
        {
            float full = fx != null && fx.growRange > 0f ? fx.growRange : 180f;
            float dist = (dest - origin).MagnitudeHorizontal();
            float s = fx != null ? fx.convShortEnd : 0.25f, l = fx != null ? fx.convLongStart : 0.58f;
            bool br = braced && fx != null && fx.convBraced > 1f;
            Color col = band == 0 ? new Color(0.6f, 0.8f, 1f) : band == 1 ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.35f, 0.25f);
            string label = "CONV " + BandName(band).ToUpper() + " " + dist.ToString("0") + "c x" + DamageMult(fx, band).ToString("0.0#") + " r" + radius.ToString("0.0")
                         + (lastArmour > 0f ? " mass " + lastArmour.ToString("0.0") : "")
                         + (br ? " BRACED" : "") + (chain ? " +chain" : "") + (stunned > 0 ? " stun " + stunned : "");
            try { MoteMaker.ThrowText(cell.ToVector3Shifted() + new Vector3(0f, 0f, 0.8f), map, label, col, 4f); } catch { }
            Log.Message("[LIK] conversion " + projDef.defName + ": distance " + dist.ToString("0.0") + " cells of " + full.ToString("0")
                + " (short < " + (s * full).ToString("0") + ", long > " + (l * full).ToString("0") + ") -> band " + BandName(band)
                + " | damage " + damage + " (x" + DamageMult(fx, band).ToString("0.0#") + (br ? ", braced x" + fx.convBraced.ToString("0.0#") : "") + ")"
                + " | AP " + ap.ToString("0.##") + " | struck armour/mass " + lastArmour.ToString("0.00") + " -> radius x" + lastArmourMult.ToString("0.00")
                + " | radius " + radius.ToString("0.0") + " | stunned " + stunned + " | chain " + (chain ? "yes" : "no"));
        }

        /// <summary>Lore (user 2026-11-24): moving while firing negates the beam. The shot that was fired on the move (not braced) does no damage:
        /// it fizzles where it lands - sparks, a wisp of smoke and a dying flicker.</summary>
        public static void Fizzle(DefModExtension_ShotFx fx, Map map, IntVec3 cell, Vector3 origin, Vector3 dest, ThingDef projDef)
        {
            if (map == null || !cell.InBounds(map)) return;
            Vector3 pos = cell.ToVector3Shifted();
            if (cell.ShouldSpawnMotesAt(map))
            {
                for (int i = 0; i < 3; i++) FleckMaker.ThrowMicroSparks(pos + new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.4f, 0.4f)), map);
                FleckMaker.ThrowSmoke(pos, map, 0.9f);
                FleckDef glow = DefDatabase<FleckDef>.GetNamedSilentFail("LightningGlow");
                if (glow != null) FleckMaker.Static(pos, map, glow, 0.8f);
            }
            if (Prefs.DevMode)
            {
                float dist = (dest - origin).MagnitudeHorizontal();
                try { MoteMaker.ThrowText(pos + new Vector3(0f, 0f, 0.8f), map, "CONV FIZZLED (moving) " + dist.ToString("0") + "c", new Color(0.7f, 0.7f, 0.7f), 3f); } catch { }
                Log.Message("[LIK] conversion " + projDef.defName + ": FIZZLED - fired while the Knight was moving (distance " + dist.ToString("0.0") + " cells)");
            }
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
                    t.TakeDamage(new DamageInfo(DamageDefOf.Bomb, damage * (t is VehiclePawn ? SundererVehicleMult : SundererBuildingMult), armourPen, -1f, launcher, null, projDef));
                }
            }
        }

        /// <summary>The matter-to-energy flash: a bright burst, and hostile pawns that survive near it are dazzled (stunned).</summary>
        static int Flash(Map map, IntVec3 cell, float radius, int band, Thing launcher)
        {
            int n = 0;
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
                n++;
            }
            return n;
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
