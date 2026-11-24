using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// Look of a Knight shot (on every LIK_Proj_* def, written by Tools/gen_knights.py - see SHOT_FX there).
    /// style: tracer | shell | rocket | heavy | plasma | flame | graviton | laser | melta | volkite | lightning | conversion | melee
    /// power: 0.5 (stubber) .. 3 (magna lascannon); scales every effect.
    /// </summary>
    public class DefModExtension_ShotFx : DefModExtension
    {
        public string style = "shell";
        public float power = 1f;
        public string beam;            // FleckDef drawn as a glowing line from muzzle to target when fired (energy weapons); null = none
        public float beamWidth = 1f;   // multiplier (volcano lance = huge)
        public bool whiteHot;          // beam is mostly a wide white core with a thin coloured edge (twin magna lascannon)
        public bool twin;              // two parallel beams (twin magna lascannon, twin conversion beam, twin meltagun)
        public float growRange;        // conversion beams: > 0 = damage grows with the distance to the target; this is the range at which it is strongest
        public bool conversionBands;   // twin conversion beam cannon rework 2026-11-13: range bands, growing blast, flash, braced bonus, chain bursts (Conversion.cs)
        public bool stream;            // flamethrower: no projectile picture, a stream of flame along the arc; sets the ground alight where it lands
        public SoundDef impactSound;   // non-explosive shots (lascannon ...): played where the shot lands; explosive ones use soundExplode
        public float pierce;           // > 0: instant beam that burns through cover; hit points of cover one shot can burn away (KnightShotUtil.BurnPath)
        public float vaporise;         // > 0: pawns within this many cells of the impact are evaporated outright (magna lascannon, volcano lance; user 2026-11-12)
        // ---- Cerastus weapon pass 2026-11-15 (abilities only where a source gives one; KnightEffects.cs)
        public float massReactive;     // bolt shells: extra damage from the shell detonating INSIDE what it hit
        public float suppress;         // HH3 Suppressive: radius around each hit in which pawns are pinned down (LIK_Suppressed)
        public float panic;            // HH3 Panic: chance per living thing caught in the blast to break and flee
        public float stunChance;       // HH3 Stun / HH2 Concussive: chance a struck pawn is stunned ...
        public int stunTicks;          // ... for this long
        public float sweep;            // flamers (user 2026-11-15): the burst walks across a band this many cells to each side of the target (TurretMotion)
        public int sweepShots = 12;    // shots in one sweep
        public float antiArmour;       // > 1: extra damage on vehicles, buildings and big mechs (Armourbane; lascutter cutter beam)
        public float braceMult;        // > 1: damage multiplier when fired standing still (HH3 Ordnance)
        // Acastus lore pass 2026-11-23 (AcastusWeapons.cs)
        public float shockVehicles;    // HH3 Shock (Stunned): chance a struck vehicle / big mech is stunned ...
        public int shockTicks;         // ... for this long
        public float breachChance;     // HH3 Breaching (6+): chance a round punches through armour ...
        public float breachAP;         // ... with this armour penetration
        public float braceExtra;       // HH3 / 40k Heavy (FP): braced, chance per round to fire one extra round
        public float antiAir;          // 40k Anti-Fly / HH3 Skyfire: damage multiplier against flying things
        public bool interceptPods;     // Helios: strikes hostile drop pods / shuttles coming down near the Knight
        // Questoris lore pass 2026-11-23 (QuestorisWeapons.cs)
        public float closeExtra;       // Rapid Fire: within closeRange, chance per round of a second round
        public float closeRange;
        public float braceAP;          // > 1: HH3 Heavy (AP) - armour piercing multiplier when braced
        public float shredChance;      // HH3 Shred: chance per round of +25 % damage
        public int chainHits;          // 40k Sustained Hits: a hit arcs on to 1..chainHits more enemies ...
        public float chainRadius;      // ... within this radius (half damage each)
        public float gravPin;          // 40k Styrix grav-pin: infantry hit are slowed for this many seconds
        public float lowIntensity;     // > 0: 'Low intensity' fire mode damage multiplier (las-impulsor) ...
        public float lowIntensityAP = 1f; // ... and its armour piercing multiplier
        public float poolGrowth;       // flamers (user 2026-11-15): each further shot on the same spot widens the burning area by this many cells ...
        public float poolMax;          // ... up to this radius (FirePools)
        public float gravField;        // > 0: graviton singularity - gravity multiplied over the blast for this many seconds (Gravity.cs) ...
        public float gravTick = 5f;    // ... crushing damage every quarter second inside it (before mass / armour scaling)
        // Dominus pass 2026-11-16 (DominusWeapons.cs)
        public float supercharge;      // > 1: plasma decimator - damage multiplier of a shot fired in its Supercharge fire mode (Hazardous)
        public bool ignoreCover;       // Torrent / Ignores Cover: nothing in the line of fire stops the shot except solid walls
        public float arcRadius;        // Thundershock: energy arcs to the target and every enemy within this many cells ...
        public float arcChance;        // ... each struck with this chance ...
        public float arcDamageMin, arcDamageMax;   // ... for this much damage, no armour save (mortal wounds)
        public bool tether;            // thundercoil harpoon: chain from the arm to the spear, reeled back after the hit (visual)
        public float shieldMult;       // > 1: shieldbreaker - shields hit by the blast lose this many times the damage
        public float meltaRange;       // > 0: Melta - within this many cells of the gun ...
        public float meltaMult = 1f;   // ... the damage is multiplied by this
        // ---- ARMIGER pass 2026-11-17 (ArmigerWeapons.cs) ----
        public float braceRange;       // > 1: HH3 Ordnance (R) - the def range is the braced range; while the Knight walks the turret reaches range / this
        public float deflagrate;       // > 0: volkite Deflagrate - a living target it wounds combusts: a burst of this much Flame damage hits those next to it
        public float overcharge;       // > 0: a fire mode labelled 'Overcharge...' - its shots lose half their power beyond this fraction of the range
        public bool mustBrace;         // conversion beamer (lore): fired while the Knight moves, the beam's intensity is negated - the shot fizzles
        public float irradiate;        // > 0: irad-cleanser - radiation sickness severity added to living things in the blast (it keeps building)
        // conversion bands, per weapon (defaults = the Acastus twin conversion beam cannon, 2026-11-13)
        public float convShortEnd = 0.25f, convLongStart = 0.58f;
        public float convMult0 = 0.6f, convMult1 = 1.0f, convMult2 = 1.8f;
        public float convRadius0 = 1.5f, convRadius1 = 2.5f, convRadius2 = 4.0f;
        public bool convFlash = true, convChain = true;
        public float convBraced = 1.5f;
    }

    /// <summary>
    /// All visual effects of Knight weapons: muzzle blast, beam, flight trail, impact, lingering smoke/glow. Effects only - damage is untouched.
    /// Lore notes per style:
    ///  laser      las weapons: searing red beam with a white-hot core; the target flashes and bursts as its surface vaporises
    ///  melta      thermal/melta: short roaring heat beam, shimmering air, the target glows and melts
    ///  volkite    deflagrating crimson ray: what it hits bursts into flame and ash
    ///  conversion conversion beamer: matter turns to energy at the target - the blast grows with distance
    ///  lightning  lightning cannon / lightning lock: forked blue-white arcs
    ///  plasma     blue-white star-hot bolt, blinding detonation, lingering glow
    ///  graviton   no fire: a crushing distortion wave, cracked ground, flung debris
    ///  shell      battle cannon / siege shells: high-explosive burst, dirt, debris, smoke column
    ///  rocket     missiles: fiery blast, thick smoke, debris
    ///  heavy      thundercoil harpoon: heavy strike with an electric discharge
    ///  flame      promethium / phosphor / rad: fire bursts
    ///  tracer     autocannon, stubber, gatling: sparks and dirt
    ///  bolt       bolters (2026-11-15): self-propelled mass-reactive shells - rocket flare + exhaust streak, detonate inside the target
    /// </summary>
    public static class ShotFx
    {
        static readonly Dictionary<string, FleckDef> fleckCache = new Dictionary<string, FleckDef>();
        static readonly Dictionary<string, EffecterDef> effCache = new Dictionary<string, EffecterDef>();
        static ThingDef ash;
        static bool ashLooked;
        static readonly Color Dust = new Color(0.5f, 0.46f, 0.4f, 0.7f);

        static FleckDef Fleck(string name)
        {
            if (name == null) return null;
            FleckDef d;
            if (!fleckCache.TryGetValue(name, out d)) { d = DefDatabase<FleckDef>.GetNamedSilentFail(name); fleckCache[name] = d; }
            return d;
        }

        static EffecterDef Eff(string name)
        {
            EffecterDef d;
            if (!effCache.TryGetValue(name, out d)) { d = DefDatabase<EffecterDef>.GetNamedSilentFail(name); effCache[name] = d; }
            return d;
        }

        public static DefModExtension_ShotFx Of(Thing t) { return t.def.GetModExtension<DefModExtension_ShotFx>(); }

        /// <summary>
        /// Conversion beamers turn the target's own matter into energy, and the beam needs distance to build: weak point blank, devastating at the
        /// end of its range. Multiplier for damage and armour penetration: x0.6 at the muzzle, x1.0 at 40 % of growRange, x1.6 at growRange or beyond.
        /// </summary>
        public static float DistanceFactor(DefModExtension_ShotFx fx, Vector3 origin, Vector3 dest)
        {
            if (fx == null || fx.growRange <= 0f) return 1f;
            if (fx.conversionBands) return ConversionBeam.Mult(fx, origin, dest);
            float t = Mathf.Clamp01((dest - origin).MagnitudeHorizontal() / fx.growRange);
            return t < 0.4f ? Mathf.Lerp(0.6f, 1f, t / 0.4f) : Mathf.Lerp(1f, 1.6f, (t - 0.4f) / 0.6f);
        }

        static bool Energy(string s) { return s == "laser" || s == "melta" || s == "volkite" || s == "lightning" || s == "conversion" || s == "plasma"; }

        /// <summary>Camera shake that weakens with distance from the view, like vanilla explosions.</summary>
        public static void Shake(Vector3 pos, Map map, float strength)
        {
            if (strength <= 0f || map != Find.CurrentMap || Find.Camera == null) return;
            float dist = (pos - Find.Camera.transform.position).magnitude;
            if (dist < 1f) dist = 1f;
            Find.CameraDriver.shaker.DoShake(Mathf.Min(strength / dist, 1.2f));
        }

        // ------------------------------------------------------------------ charge-up (energy gathering at the muzzle before the shot)
        static Color ChargeColor(string s)
        {
            switch (s)
            {
                case "laser": return new Color(1f, 0.22f, 0.12f);
                case "conversion": return new Color(1f, 0.86f, 0.45f);
                case "plasma": return new Color(0.45f, 0.75f, 1f);
                case "graviton": return new Color(0.55f, 0.45f, 0.85f);
                case "melta": return new Color(1f, 0.55f, 0.15f);
                case "volkite": return new Color(0.24f, 0.99f, 0.35f);   // green like the painted volkites (user 2026-11-23; was red)
                case "lightning": return new Color(0.6f, 0.8f, 1f);
                default: return new Color(1f, 0.7f, 0.3f);
            }
        }

        /// <summary>
        /// One step of a weapon charging (called every 2 ticks by MapComponent_ShotFx while the shot is held back). t = 0 (start) .. 1 (about to fire).
        /// A ball of light swells at the muzzle, specks and streaks of energy are drawn into it faster and faster, and it flares just before the shot.
        /// Graviton weapons give no light: dust and grit are dragged toward the gun and the ground trembles.
        /// </summary>
        public static void ChargeTick(DefModExtension_ShotFx fx, Vector3 muzzle, Map map, float t, int step)
        {
            GlowFx.Full++;
            try { ChargeInner(fx, muzzle, map, t, step); } finally { GlowFx.Full--; }
        }

        static void ChargeInner(DefModExtension_ShotFx fx, Vector3 muzzle, Map map, float t, int step)
        {
            if (fx == null || map == null || !muzzle.InBounds(map) || !muzzle.ShouldSpawnMotesAt(map)) return;
            float p = fx.power;
            string s = fx.style;
            Color col = ChargeColor(s);
            float reach = (1.3f + 0.7f * p) * (1.15f - 0.45f * t);       // how far out the energy is gathered from; tightens as the charge builds
            int motes = 1 + (t > 0.4f ? 1 : 0) + (t > 0.75f ? 1 : 0);
            FleckDef mote = s == "graviton" ? null : Fleck("LIK_ChargeMote");

            for (int i = 0; i < motes; i++)
            {
                Vector3 q = muzzle + Gen.RandomHorizontalVector(1f).normalized * reach * Rand.Range(0.7f, 1.1f);
                if (!q.InBounds(map)) continue;
                if (mote != null)
                {
                    FleckCreationData d = FleckMaker.GetDataStatic(q, map, mote, s == "graviton" ? Rand.Range(0.7f, 1.3f) * (0.6f + 0.25f * p) : Rand.Range(0.25f, 0.5f) * (0.6f + 0.3f * p));
                    d.velocityAngle = (muzzle - q).AngleFlat();
                    d.velocitySpeed = (muzzle - q).MagnitudeHorizontal() / 0.26f;      // reaches the muzzle as it fades
                    d.rotation = Rand.Range(0f, 360f);
                    if (s == "graviton") d.instanceColor = new Color(0.5f, 0.46f, 0.42f, 0.8f); else d.instanceColor = col;
                    map.flecks.CreateFleck(d);
                }
                if (s != "graviton" && (i == 0 || t > 0.5f))
                    LineTint(q, muzzle + (q - muzzle) * 0.15f, Fleck("LIK_ChargeStreak"), map, 0.1f + 0.08f * p * t, col);
            }

            if (s == "graviton")
            {
                // no light and no pull: space warps at the muzzle, a dark purple rim forms, the ground starts to shake
                StatTint(muzzle, map, "LIK_ChargeGlow", (0.8f + 0.8f * p) * (0.3f + 0.7f * t), new Color(col.r, col.g, col.b, 0.35f));
                if (step % 3 == 0) Stat(muzzle, map, "LIK_GravDistortSmall", 1.2f + 2.2f * t);
                if (step % 6 == 0) Stat(muzzle, map, "LIK_GravRipple", 1.5f + 1.5f * t);
                if (step % 4 == 0) Shake(muzzle, map, 0.6f * p * t);
                return;
            }

            float size = (0.9f + 1.5f * p) * (0.2f + 0.8f * t * t) * Rand.Range(0.9f, 1.1f);
            StatTint(muzzle, map, "LIK_ChargeGlow", size, col);
            if (t > 0.35f) StatTint(muzzle, map, "LIK_ChargeGlow", size * 0.42f, Color.white);          // white-hot centre
            if (Rand.Chance(0.15f + 0.5f * t)) FleckMaker.ThrowMicroSparks(muzzle + Gen.RandomHorizontalVector(0.3f), map);
            if (s == "plasma")
            {
                if (step % 5 == 0) FleckMaker.ThrowSmoke(muzzle + Gen.RandomHorizontalVector(0.4f), map, 0.4f + 0.3f * p * t);    // coils venting heat
                if (t > 0.6f && step % 3 == 0) GlowFx.Lightning(muzzle, map, (0.5f + 0.6f * p) * t);
            }
            else if (t > 0.7f && step % 3 == 0) GlowFx.Lightning(muzzle, map, (0.4f + 0.4f * p) * t);
            if (t > 0.88f) Stat(muzzle, map, "SparkFlash", 2f + 2.5f * p);
        }

        static void StatTint(Vector3 pos, Map map, string fleck, float scale, Color tint)
        {
            scale = Mathf.Min(scale * GlowFx.ScaleFor(fleck), GlowFx.CapFor(fleck)); if (scale <= 0.05f) return;
            FleckDef d = Fleck(fleck);
            if (d == null || !pos.InBounds(map)) return;
            FleckCreationData data = FleckMaker.GetDataStatic(pos, map, d, scale);
            data.instanceColor = tint;
            map.flecks.CreateFleck(data);
        }

        // ------------------------------------------------------------------ small helpers
        static void Stat(Vector3 pos, Map map, string fleck, float scale)
        {
            scale = Mathf.Min(scale * GlowFx.ScaleFor(fleck), GlowFx.CapFor(fleck)); if (scale <= 0.05f) return;
            FleckDef d = Fleck(fleck);
            if (d != null && pos.InBounds(map)) FleckMaker.Static(pos, map, d, scale);
        }

        static void StatRot(Vector3 pos, Map map, string fleck, float scale)
        {
            FleckDef d = Fleck(fleck);
            if (d == null || !pos.InBounds(map)) return;
            FleckCreationData data = FleckMaker.GetDataStatic(pos, map, d, scale);
            data.rotation = Rand.Range(0f, 360f);
            map.flecks.CreateFleck(data);
        }

        static void Line(Vector3 a, Vector3 b, FleckDef def, Map map, float width)
        {
            if (def == null) return;
            Vector3 v = b - a;
            float len = v.MagnitudeHorizontal();
            if (len < 0.1f) return;
            FleckCreationData d = FleckMaker.GetDataStatic(a + v * 0.5f, map, def);
            d.exactScale = new Vector3(len, 1f, width);
            d.rotation = Mathf.Atan2(-v.z, v.x) * 57.29578f;
            map.flecks.CreateFleck(d);
        }

        static void LineTint(Vector3 a, Vector3 b, FleckDef def, Map map, float width, Color tint)
        {
            if (def == null) return;
            Vector3 v = b - a;
            float len = v.MagnitudeHorizontal();
            if (len < 0.1f) return;
            FleckCreationData d = FleckMaker.GetDataStatic(a + v * 0.5f, map, def);
            d.exactScale = new Vector3(len, 1f, width);
            d.rotation = Mathf.Atan2(-v.z, v.x) * 57.29578f;
            d.instanceColor = tint;
            map.flecks.CreateFleck(d);
        }

        /// <summary>Ground left red hot: a long slow red glow with a short yellow-white centre.</summary>
        public static void HotGround(Vector3 pos, Map map, float size)
        {
            if (map == null || !pos.InBounds(map) || pos.ToIntVec3().Fogged(map)) return;
            // 2026-11-22 (user: the flame / glow effects were too bright and white): additive glows stacked to white - capped size, one extra blob
            size = Mathf.Min(size * 0.75f, 5f);
            Stat(pos, map, "LIK_HotGround", size);
            Stat(pos, map, "LIK_HotGroundCore", size * 0.4f);
            Stat(pos + Gen.RandomHorizontalVector(size * 0.25f), map, "LIK_HotGround", size * Rand.Range(0.45f, 0.7f));
        }

        /// <summary>Smoke and exhaust glow of an indirect-fire missile, drawn above the fog of war.</summary>
        static void HighTrail(Vector3 pos, Map map, float p, int n)
        {
            FleckDef smoke = Fleck("LIK_TrailSmokeHigh"), glow = Fleck("LIK_TrailGlowHigh");
            if (smoke != null)
            {
                FleckCreationData d = FleckMaker.GetDataStatic(pos, map, smoke, Rand.Range(1.5f, 2.5f) * (0.5f + 0.25f * p));
                d.rotationRate = Rand.Range(-30f, 30f);
                d.velocityAngle = Rand.Range(30, 40);
                d.velocitySpeed = Rand.Range(0.5f, 0.7f);
                map.flecks.CreateFleck(d);
            }
            if (glow != null)
            {
                FleckCreationData d = FleckMaker.GetDataStatic(pos, map, glow, Rand.Range(4f, 6f) * (0.3f + 0.12f * p));
                d.rotationRate = Rand.Range(-3f, 3f);
                map.flecks.CreateFleck(d);
            }
        }

        /// <summary>A forked lightning bolt from a to b: jagged segments of the vanilla EMP line.</summary>
        public static void BoltPublic(Vector3 a, Vector3 b, Map map, float width, float jitter) { Bolt(a, b, map, width, jitter); }

        static void Bolt(Vector3 a, Vector3 b, Map map, float width, float jitter)
        {
            FleckDef d = Fleck("LineEMP");
            if (d == null) return;
            float len = (b - a).MagnitudeHorizontal();
            int segs = Mathf.Clamp(Mathf.RoundToInt(len / 7f), 1, 10);
            Vector3 prev = a;
            for (int i = 1; i <= segs; i++)
            {
                Vector3 next = Vector3.Lerp(a, b, (float)i / segs);
                if (i < segs) next += Gen.RandomHorizontalVector(jitter);
                Line(prev, next, d, map, width);
                prev = next;
            }
        }

        static void Debris(Vector3 pos, Map map, int count, float spread, float scale)
        {
            FleckDef d = Fleck("ThrownDebris");
            if (d == null) return;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = pos + Gen.RandomHorizontalVector(spread * 0.4f);
                if (!p.InBounds(map)) continue;
                FleckCreationData data = FleckMaker.GetDataStatic(p, map, d, Rand.Range(0.6f, 1.3f) * scale);
                data.rotation = Rand.Range(0f, 360f);
                data.rotationRate = Rand.Range(100f, 300f) * (Rand.Bool ? 1f : -1f);
                data.velocityAngle = Rand.Range(0f, 360f);
                data.velocitySpeed = Rand.Range(1.5f, 4.5f) * Mathf.Max(1f, spread * 0.5f);
                map.flecks.CreateFleck(data);
            }
        }

        static void DustRing(Vector3 pos, Map map, float r, int n, float size)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 p = pos + Quaternion.AngleAxis(360f * i / n + Rand.Range(-10f, 10f), Vector3.up) * Vector3.forward * r;
                if (p.InBounds(map)) FleckMaker.ThrowDustPuffThick(p, map, size, Dust);
            }
        }

        static void Effect(string name, IntVec3 cell, Map map, float scale)
        {
            EffecterDef d = Eff(name);
            if (d == null) return;
            Effecter e = d.Spawn(cell, map, scale);
            e.Cleanup();
        }

        // ------------------------------------------------------------------ muzzle
        public static void Muzzle(DefModExtension_ShotFx fx, Vector3 origin, Vector3 dest, Map map)
        {
            GlowFx.Full++;
            try { MuzzleInner(fx, origin, dest, map); } finally { GlowFx.Full--; }
        }

        static void MuzzleInner(DefModExtension_ShotFx fx, Vector3 origin, Vector3 dest, Map map)
        {
            if (fx == null || map == null || fx.style == "melee") return;
            float p = fx.power;
            string s = fx.style;
            Vector3 dir = (dest - origin).Yto0().normalized;
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);
            bool visible = origin.ShouldSpawnMotesAt(map);

            if (visible)
            {
                FleckMaker.Static(origin, map, FleckDefOf.ShotFlash, 7f + 8f * p);
                if (Energy(s))
                {
                    GlowFx.Lightning(origin, map, 1.2f + 1.2f * p);
                    Stat(origin, map, "SparkFlash", 3f + 3f * p);
                    if (s == "melta") { Stat(origin + dir * 1.5f, map, "HeatGlow_Intense", 2f + 1.2f * p); FleckMaker.ThrowSmoke(origin, map, 0.5f + 0.3f * p); }
                    if (s == "plasma") Stat(origin, map, "GrowingFlash", 0.6f + 0.5f * p);
                }
                else if (s == "flame") { GlowFx.Fire(origin, map, 0.8f * p); FleckMaker.ThrowSmoke(origin, map, 0.6f); }
                else if (s == "rocket")
                {
                    // backblast behind the launcher
                    for (int i = 0; i < 3 + Mathf.RoundToInt(p * 2f); i++) FleckMaker.ThrowSmoke(origin - dir * (0.6f * i) + Gen.RandomHorizontalVector(0.5f), map, 0.8f + 0.4f * p);
                    GlowFx.Fire(origin - dir, map, 0.6f + 0.3f * p);
                }
                else
                {
                    int puffs = s == "tracer" || s == "bolt" ? (Rand.Chance(0.5f) ? 1 : 0) : 2 + Mathf.RoundToInt(p * 2.5f);
                    for (int i = 0; i < puffs; i++)
                        FleckMaker.ThrowSmoke(origin + dir * (0.5f * i) + Gen.RandomHorizontalVector(0.4f), map, 0.6f + 0.45f * p);
                    if (p >= 1.2f)
                    {
                        Stat(origin, map, "SparkFlash", 4f + 3f * p);
                        DustRing(origin - dir * 2f, map, 1.5f + p, 5 + Mathf.RoundToInt(p * 2f), 1.8f + p);   // the ground under the gun kicks up
                        if (p >= 1.8f) Stat(origin, map, "LIK_Shockwave_S", 1f);
                    }
                }
            }

            // ---- beam
            if (fx.beam != null)
            {
                float w = (0.45f + 0.5f * p) * fx.beamWidth;
                float off = fx.twin ? 0.3f + 0.12f * p : 0f;
                for (int k = fx.twin ? -1 : 0; k <= 1; k += 2)
                {
                    Vector3 o = side * (off * k);
                    if (s == "lightning")
                    {
                        Bolt(origin + o, dest, map, 1.2f + 0.8f * p, 0.8f + 0.5f * p);
                        Bolt(origin + o, dest, map, 0.8f + 0.5f * p, 1.4f + 0.7f * p);
                        if (p >= 1f) Bolt(origin + o, dest + Gen.RandomHorizontalVector(1.5f), map, 0.7f, 2f);
                    }
                    else
                    {
                        if (fx.whiteHot)
                        {
                            // a tight, blinding lance: thin warm halo, yellow-orange edge, and a white core that fills most of the beam
                            LineTint(origin + o, dest + o, Fleck(fx.beam), map, w * 2.2f, new Color(1f, 0.82f, 0.45f, 0.22f));
                            Line(origin + o, dest + o, Fleck(fx.beam), map, w);
                            Line(origin + o, dest + o, Fleck("LIK_Beam_Core"), map, w * 0.62f);
                        }
                        else
                        {
                            LineTint(origin + o, dest + o, Fleck(fx.beam), map, w * 3.5f, new Color(1f, 1f, 1f, 0.28f));   // soft halo: the air around the beam glows
                            Line(origin + o, dest + o, Fleck(fx.beam), map, w);                       // coloured glow
                            Line(origin + o, dest + o, Fleck("LIK_Beam_Core"), map, w * 0.3f);         // white-hot core
                        }
                    }
                    if (!fx.twin) break;
                }
                // sparks / heat along a strong beam
                if (p >= 1f && s != "lightning")
                {
                    float len = (dest - origin).MagnitudeHorizontal();
                    int n = Mathf.Min(10, Mathf.RoundToInt(len / 8f));
                    for (int i = 1; i <= n; i++)
                    {
                        Vector3 q = Vector3.Lerp(origin, dest, (float)i / (n + 1));
                        if (!q.ShouldSpawnMotesAt(map)) continue;
                        if (s == "melta") Stat(q, map, "HeatGlow_Intense", 2f + p);
                        else { FleckMaker.ThrowMicroSparks(q, map); GlowFx.Lightning(q, map, 0.7f + 0.5f * p); }
                    }
                }
            }
            if (p >= 1.0f) Shake(origin, map, (s == "tracer" || s == "bolt" ? 0.8f : 2.2f) * p);
        }

        // ------------------------------------------------------------------ flight
        public static void Trail(DefModExtension_ShotFx fx, Vector3 pos, Map map, int n, bool overhead = false, bool glow = false)
        {
            GlowFx.Full++;
            try { TrailInner(fx, pos, map, n, overhead, glow); } finally { GlowFx.Full--; }
        }

        static void TrailInner(DefModExtension_ShotFx fx, Vector3 pos, Map map, int n, bool overhead, bool glow)
        {
            if (fx == null || map == null || !pos.InBounds(map)) return;
            if (overhead && fx.style == "rocket" && pos.ToIntVec3().Fogged(map)) { HighTrail(pos, map, fx.power, n); return; }   // crossing unexplored ground: draw above the fog
            if (!pos.ShouldSpawnMotesAt(map)) return;
            float p = fx.power;
            switch (fx.style)
            {
                case "rocket":
                    FleckMaker.ThrowSmoke(pos, map, 0.5f + 0.25f * p);
                    GlowFx.Fire(pos, map, 0.3f + 0.12f * p);
                    if (n % 3 == 0) FleckMaker.ThrowMicroSparks(pos, map);
                    break;
                case "shell":
                case "heavy":
                    if (n % 2 == 0) FleckMaker.ThrowSmoke(pos, map, 0.3f + 0.15f * p);
                    break;
                case "bolt":
                    // a bolt is a small rocket (lore: self-propelled mass-reactive shell): bright motor flare and a thin exhaust streak
                    GlowFx.Fire(pos, map, 0.22f + 0.1f * p);
                    if (n % 2 == 0) Stat(pos, map, "SparkFlash", 0.9f + 0.5f * p);
                    if (n % 2 == 1) FleckMaker.ThrowSmoke(pos, map, 0.25f + 0.1f * p);
                    break;
                case "plasma":
                    GlowFx.Lightning(pos, map, 0.8f + 0.6f * p);
                    if (n % 2 == 0) Stat(pos, map, "SparkFlash", 1.5f + p);
                    break;
                case "flame":
                    if (fx.stream)
                    {
                        FlameStream(pos, map, p, n);
                        if (glow) { GlowFx.Fire(pos, map, 0.9f + 0.4f * p); if (n % 2 == 0) Stat(pos, map, "LIK_ChargeGlow", 2.2f + 0.6f * p); }
                        break;
                    }
                    GlowFx.Fire(pos, map, 0.4f + 0.2f * p);
                    if (n % 3 == 0) FleckMaker.ThrowSmoke(pos, map, 0.4f);
                    break;
                case "graviton":
                    if (n % 2 == 0) Stat(pos, map, "LIK_Shockwave_T", 1f);
                    if (p >= 2.5f)
                    {
                        // singularity cannon (user 2026-11-15): the space around the shot warps as it passes (real background distortion), a purple halo
                        if (n % 2 == 0) Stat(pos, map, "LIK_GravDistortSmall", Rand.Range(1.4f, 1.8f));      // a miniature black hole: small warp, thin ring
                        if (n % 4 == 0) Stat(pos, map, "LIK_GravRipple", Rand.Range(1.1f, 1.4f));
                    }
                    break;
            }
        }

        /// <summary>
        /// One tick of a flamethrower shot in flight: tongues of flame at its place on the arc. The stream is thin at the nozzle and billows out
        /// as it travels (n = ticks since it left the gun), with black smoke rolling off the top.
        /// </summary>
        static void FlameStream(Vector3 pos, Map map, float p, int n)
        {
            FleckDef f = Fleck("LIK_FlameStream");
            float grow = Mathf.Min(1f, 0.3f + n * 0.055f);
            // user 2026-11-22: the stacked stream flames turned into a solid white cone hiding the fight - tinted orange, see-through, fewer
            float g = GlowFx.G;
            if (f != null)
                for (int i = 0; i < (g < 0.5f ? 1 : 2); i++)
                {
                    FleckCreationData d = FleckMaker.GetDataStatic(pos + Gen.RandomHorizontalVector(0.45f * grow), map, f, Rand.Range(1.3f, 2.3f) * grow * (0.75f + 0.25f * p));
                    d.instanceColor = new Color(1f, Mathf.Lerp(0.55f, 1f, g), Mathf.Lerp(0.25f, 1f, g), Mathf.Lerp(0.45f, 1f, g));
                    d.rotation = Rand.Range(0f, 360f);
                    d.rotationRate = Rand.Range(-120f, 120f);
                    d.velocityAngle = Rand.Range(0f, 360f);
                    d.velocitySpeed = Rand.Range(0.2f, 0.9f);
                    map.flecks.CreateFleck(d);
                }
            if (n % 3 == 0) GlowFx.Fire(pos, map, 0.3f + 0.4f * grow);
            if (n % 4 == 0) FleckMaker.ThrowSmoke(pos + new Vector3(Rand.Range(-0.3f, 0.3f), 0f, 0.5f + 0.4f * grow), map, 0.4f + 0.4f * grow);
            if (n % 4 == 0) FleckMaker.ThrowMicroSparks(pos, map);
        }

        /// <summary>Burning fuel splashes down: every cell in the radius that can burn is set alight (the fuel the shot spilled burns on bare ground too).</summary>
        public static void Ignite(IntVec3 cell, Map map, float radius, Thing instigator)
        {
            if (map == null || !cell.InBounds(map)) return;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, Mathf.Clamp(radius, 0.9f, 5.9f), true))
            {
                if (!c.InBounds(map)) continue;
                FireUtility.TryStartFireIn(c, map, Rand.Range(0.3f, 0.75f), instigator);
            }
            HotGround(cell.ToVector3Shifted(), map, 1.5f + radius * 0.5f);
        }

        // ------------------------------------------------------------------ impact
        /// <summary>Explosive shot. origin = where it was fired from (the conversion beam grows with distance).</summary>
        public static void Blast(DefModExtension_ShotFx fx, IntVec3 cell, Map map, float radius, Vector3 origin)
        {
            if (fx == null || map == null || !cell.InBounds(map) || fx.style == "melee") return;
            float p = fx.power;
            string s = fx.style;
            Vector3 pos = cell.ToVector3Shifted();
            if (fx.stream)
            {
                if (cell.ShouldSpawnMotesAt(map))
                {
                    FleckDef fb = Fleck("BlastFlame");
                    float g = GlowFx.G; Color fc = new Color(Mathf.Lerp(0.5f, 1f, g), Mathf.Lerp(0.4f, 1f, g), Mathf.Lerp(0.3f, 1f, g));
                    if (fb != null) foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, Mathf.Min(radius, 1.9f), true)) if (c.InBounds(map) && Rand.Chance(Mathf.Lerp(0.25f, 0.5f, g))) FleckMaker.ThrowExplosionCell(c, map, fb, fc);
                    if (Rand.Chance(0.5f)) FleckMaker.ThrowSmoke(pos + Gen.RandomHorizontalVector(radius * 0.6f), map, 1.0f + 0.3f * p);
                }
                Scorch(cell, map, p, Mathf.Clamp(radius, 0.9f, 3.9f));
                return;
            }
            float r = Mathf.Clamp(radius, 0.9f, 5.9f);
            if (s == "conversion")
            {
                // matter -> energy: weak up close, immense at range
                float far = Mathf.Clamp01((pos - origin).MagnitudeHorizontal() / (fx.growRange > 0f ? fx.growRange : 70f));
                p *= Mathf.Lerp(0.6f, 1.5f, far);
                r = Mathf.Clamp(r * Mathf.Lerp(0.8f, 1.8f, far), 0.9f, 5.9f);
            }

            if (cell.ShouldSpawnMotesAt(map))
            {
                // ---- the blast picture on every cell (the Knight damage types have none of their own)
                string blastName = s == "flame" || s == "melta" || s == "volkite" || s == "rocket" ? "BlastFlame" : (s == "graviton" || s == "lightning" ? "BlastEMP" : "BlastDry");
                Color col = s == "plasma" ? new Color(0.5f, 0.78f, 1f) : s == "graviton" ? new Color(0.7f, 0.55f, 1f) : s == "conversion" ? new Color(1f, 0.95f, 0.7f)
                          : s == "laser" ? (fx.whiteHot ? new Color(1f, 0.9f, 0.6f) : new Color(1f, 0.6f, 0.5f)) : Color.white;
                FleckDef blast = Fleck(blastName);
                if (blast != null)
                {
                    // user 2026-11-22: the per-cell blast glow stacked into a white sheet hiding the fight - limited to the inner blast, dimmed
                    float g = GlowFx.G, cr = Mathf.Min(r, Mathf.Lerp(1.9f, 5.9f, g));
                    Color cc = new Color(col.r * Mathf.Lerp(0.45f, 1f, g), col.g * Mathf.Lerp(0.45f, 1f, g), col.b * Mathf.Lerp(0.45f, 1f, g), col.a);
                    foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, cr, true))
                        if (c.InBounds(map) && Rand.Chance(Mathf.Lerp(0.45f, 1f, g))) FleckMaker.ThrowExplosionCell(c, map, blast, cc);
                }
                if (s == "rocket" || s == "volkite")   // fire AND a dry burst on the inner cells
                {
                    FleckDef dry = Fleck("BlastDry");
                    if (dry != null) foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, r * 0.6f, true)) if (c.InBounds(map)) FleckMaker.ThrowExplosionCell(c, map, dry, Color.white);
                }

                GlowFx.Flash(pos, map, Mathf.Max(7f, r * 5.5f + 5f * p));   // was max(12, 9r + 8p) - user 2026-11-22: too bright/white

                if (s == "graviton")
                {
                    // implosion: no fire, space itself buckles
                    Stat(pos, map, p >= 1.8f ? "LIK_Shockwave_L" : "LIK_Shockwave_S", 1f);
                    Stat(pos, map, "PsycastSkipInnerExit", 1.5f + 1.2f * p);
                    Stat(pos, map, "PsycastSkipOuterRingExit", 1.5f + 1.2f * p);
                    for (int i = 0; i < 1 + Mathf.RoundToInt(p); i++) StatRot(pos + Gen.RandomHorizontalVector(r * 0.5f), map, "GroundCrack", 1.5f + p);
                    Debris(pos, map, 6 + Mathf.RoundToInt(p * 5f), r, 1f);
                    DustRing(pos, map, r + 0.5f, 8 + Mathf.RoundToInt(p * 3f), 2f + p);
                }
                else
                {
                    bool energy = Energy(s);
                    if (p >= 1.4f) Stat(pos, map, p >= 2.2f ? "LIK_Shockwave_L" : "LIK_Shockwave_S", 1f);
                    if (energy)
                    {
                        // searing energy detonation: rays, radial sparks, heat distortion (the vanilla heat-wave burst), scaled to the weapon
                        if (p >= 1.2f && GlowFx.G >= 0.5f) Effect("Vaporize_Heatwave", cell, map, Mathf.Clamp(0.25f + 0.22f * p, 0.3f, 1f) * GlowFx.G);   // vanilla vaporize burst: the biggest white glare
                        Stat(pos, map, "GrowingFlash", 0.5f + 0.5f * p);
                        GlowFx.Lightning(pos, map, 1.5f + 1.5f * p);
                        if (s == "lightning" || s == "plasma")
                            for (int i = 0; i < 3 + Mathf.RoundToInt(p * 2f); i++) Bolt(pos, pos + Gen.RandomHorizontalVector(r + 2f), map, 0.7f, 0.6f);
                        if (s == "melta") Stat(pos, map, "HeatGlow_Intense", 3f + 1.8f * p);   // was 5 + 3p
                    }
                    if (s == "heavy") for (int i = 0; i < 5; i++) Bolt(pos, pos + Gen.RandomHorizontalVector(r + 2.5f), map, 0.8f, 0.7f);

                    GlowFx.Fire(pos, map, 1f + 0.8f * p);
                    int smoke = 4 + Mathf.RoundToInt(p * 4f);
                    for (int i = 0; i < smoke; i++) FleckMaker.ThrowSmoke(pos + Gen.RandomHorizontalVector(r * 0.8f), map, 1.2f + 0.7f * p);
                    int sparks = 4 + Mathf.RoundToInt(p * 5f);
                    for (int i = 0; i < sparks; i++) FleckMaker.ThrowMicroSparks(pos + Gen.RandomHorizontalVector(r * 0.7f), map);
                    DustRing(pos, map, r + 0.6f, 6 + Mathf.RoundToInt(p * 4f), 2f + 1.2f * p);
                    if (!energy || p >= 2f)
                    {
                        Debris(pos, map, 4 + Mathf.RoundToInt(p * 5f), r, 1f);
                        if (p >= 1.2f) for (int i = 0; i < 1 + Mathf.RoundToInt(p * 0.7f); i++) StatRot(pos + Gen.RandomHorizontalVector(r * 0.4f), map, "GroundCrack", 1.2f + 0.9f * p);
                    }
                }
            }
            if (s != "graviton")
            {
                // no ash on plain impacts (user 2026-11-23: plasma hits covered the field in white 'extinguisher powder')
                if (p >= 1.2f) MapComponent_ShotFx.Add(map, pos, s, p, r, Mathf.RoundToInt(120f + 150f * p));
            }
            if (Energy(s) && s != "lightning") HotGround(pos, map, 3f + r * 2f + 1.5f * p);
            Shake(pos, map, 3f * p * Mathf.Max(1f, r * 0.5f));
        }

        /// <summary>The first flame of a burst hitting: one bright ignition - fire flash, glow and glowing ground (later shots of the burst don't).</summary>
        public static void IgnitionGlow(IntVec3 cell, Map map, float radius, float p)
        {
            if (map == null || !cell.InBounds(map) || !cell.ShouldSpawnMotesAt(map)) return;
            Vector3 pos = cell.ToVector3Shifted();
            GlowFx.Flash(pos, map, 5f + radius * 2f);          // smaller (was 8 + 3r), user 2026-11-22
            GlowFx.Fire(pos, map, 1.4f + 0.5f * p);
            StatTint(pos, map, "LIK_ChargeGlow", 3f + radius, new Color(1f, 0.6f, 0.25f, 0.8f));
            HotGround(pos, map, 2f + radius * 0.8f);
            Shake(pos, map, 1.2f * p);
        }

        /// <summary>Non-explosive shot (autocannon round, lascannon bolt): where it lands.</summary>
        public static void Hit(DefModExtension_ShotFx fx, Vector3 pos, Map map)
        {
            if (fx == null || map == null || !pos.InBounds(map)) return;
            float p = fx.power;
            string s = fx.style;
            if (pos.ShouldSpawnMotesAt(map))
            {
                FleckMaker.ThrowMicroSparks(pos, map);
                if (Energy(s))
                {
                    // las / beam hit: the surface flashes to vapour - a small burst, sparks and a glow, no shrapnel
                    GlowFx.Lightning(pos, map, 0.7f + 0.7f * p);
                    GlowFx.Flash(pos, map, 3f + 3f * p);        // smaller (was 5 + 5p), user 2026-11-22
                    Stat(pos, map, "SparkFlash", 3f + 3f * p);
                    if (s == "lightning") for (int i = 0; i < 3; i++) Bolt(pos, pos + Gen.RandomHorizontalVector(2.5f), map, 0.6f, 0.5f);
                    if (p >= 1f)
                    {
                        FleckDef dry = Fleck("BlastDry");
                        if (dry != null && pos.ToIntVec3().InBounds(map)) FleckMaker.ThrowExplosionCell(pos.ToIntVec3(), map, dry, new Color(1f, 0.6f, 0.5f));
                        for (int i = 0; i < 2 + Mathf.RoundToInt(p * 2f); i++) FleckMaker.ThrowSmoke(pos + Gen.RandomHorizontalVector(0.6f), map, 0.6f + 0.4f * p);
                        for (int i = 0; i < 4; i++) FleckMaker.ThrowMicroSparks(pos + Gen.RandomHorizontalVector(0.8f), map);
                        if (p >= 1.3f) Stat(pos, map, "HeatGlow_Intense", 2f + 1.2f * p);   // was 3 + 2p
                    }
                }
                else
                {
                    FleckMaker.ThrowDustPuffThick(pos, map, 1.2f + p, Dust);
                    Stat(pos, map, "SparkFlash", 1.5f + 1.5f * p);
                    if (Rand.Chance(0.6f)) Debris(pos, map, 1 + Mathf.RoundToInt(p), 1f, 0.6f);
                    if (p >= 1f) FleckMaker.ThrowSmoke(pos, map, 0.4f + 0.3f * p);
                }
            }
            if (Energy(s) && p >= 1f) { if (s != "lightning") HotGround(pos, map, 2.5f + 2f * p); Shake(pos, map, 1.5f * p); }
        }

        /// <summary>A las beam burning through an obstacle: molten glow, sparks, smoke; 'stopped' = the beam ended here.</summary>
        public static void Melt(IntVec3 cell, Map map, float p, bool stopped)
        {
            if (!cell.InBounds(map)) return;
            Vector3 pos = cell.ToVector3Shifted();
            if (cell.ShouldSpawnMotesAt(map))
            {
                Stat(pos, map, "HeatGlow_Intense", 2f + 1.2f * p);                              // was 3 + 2p (user 2026-11-22: too bright)
                GlowFx.Fire(pos, map, 0.6f + 0.3f * p);
                GlowFx.Lightning(pos, map, 0.55f + 0.35f * p);
                for (int i = 0; i < 3; i++) FleckMaker.ThrowMicroSparks(pos + Gen.RandomHorizontalVector(0.5f), map);
                FleckMaker.ThrowSmoke(pos, map, 0.8f + 0.4f * p);
                if (stopped) Stat(pos, map, "SparkFlash", 4f + 3f * p);
            }
            HotGround(pos, map, 3f + 1.2f * p);
            MapComponent_ShotFx.Add(map, pos, "melta", Mathf.Min(p, 1.2f), 0.6f, 150);
        }

        static void Scorch(IntVec3 cell, Map map, float p, float radius)
        {
            if (p < 0.6f) return;
            if (!ashLooked) { ashLooked = true; ash = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Ash"); }
            if (ash == null) return;
            int n = Rand.Chance(Mathf.Min(1f, p * 0.35f)) ? 1 : 0;      // flame streams only: a light dusting of ash, not piles (2026-11-23)
            for (int i = 0; i < n; i++)
            {
                IntVec3 c = cell + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(radius))];
                if (c.InBounds(map) && c.Walkable(map)) FilthMaker.TryMakeFilth(c, map, ash, 1);
            }
        }

        /// <summary>Where the picture of an arcing projectile is right now (Projectile.DrawAt lifts it by the same amount).</summary>
        public static Vector3 ArcPos(Vector3 exact, Vector3 origin, Vector3 destination, float arcFactor, float fraction)
        {
            if (arcFactor <= 0f) return exact;
            float dist2 = (destination - origin).MagnitudeHorizontalSquared();
            if (arcFactor * arcFactor > dist2 * 0.2f * 0.2f) arcFactor = Mathf.Sqrt(dist2) * 0.2f;
            return exact + new Vector3(0f, 0f, 1f) * (arcFactor * GenMath.InverseParabola(fraction));
        }
    }

    /// <summary>Aftermath of a big hit: a smoke column, flickering fire / plasma glow and sparks for a few seconds. Purely visual, not saved.</summary>
    /// <summary>user 2026-11-22: 'the crazy glowing of the weapon effects is blocking all the action underneath' - every flash and glow of
    /// the Knights' weapons goes through here and is scaled by the 'Weapon flash and glow' setting (default 35 %).</summary>
    public static class GlowFx
    {
        public static float G { get { return Mathf.Clamp01(LIKMod.I.fxGlow); } }
        /// <summary>user 2026-11-23: the lascannons looked like tiny lasers once every glow was scaled down - the WEAPON itself (muzzle, beam, glow
        /// along the beam) is thin and short-lived and never hides the fight, so while it is drawn (Full > 0) nothing is dimmed. Impacts are only
        /// dimmed a little; what keeps the field visible is a SIZE CAP on the big flashes (the stacked white sheets came from those).</summary>
        public static int Full;
        static float K { get { return Full > 0 ? 1f : Mathf.Lerp(0.6f, 1f, G); } }
        public static float Cap { get { return Full > 0 ? 999f : Mathf.Lerp(6f, 60f, G * G); } }
        static readonly System.Collections.Generic.HashSet<string> GlowFlecks = new System.Collections.Generic.HashSet<string>
            { "GrowingFlash", "HeatGlow_Intense", "SparkFlash", "LIK_ChargeGlow", "LIK_HotGround", "LIK_HotGroundCore", "YellowSparkFlash" };
        public static float ScaleFor(string fleck) { return GlowFlecks.Contains(fleck) ? K : 1f; }
        public static float CapFor(string fleck) { return GlowFlecks.Contains(fleck) ? Cap * 0.6f : 999f; }
        public static void Flash(Vector3 pos, Map map, float size) { size = Mathf.Min(size * K, Cap); if (size >= 1f) FleckMaker.Static(pos, map, FleckDefOf.ExplosionFlash, size); }
        public static void Lightning(Vector3 pos, Map map, float size) { FleckMaker.ThrowLightningGlow(pos, map, Mathf.Min(size * K, Cap * 0.4f)); }
        public static void Fire(Vector3 pos, Map map, float size) { FleckMaker.ThrowFireGlow(pos, map, Mathf.Min(size * K, Cap * 0.4f)); }
    }

    public class MapComponent_ShotFx : MapComponent
    {
        struct Linger { public Vector3 pos; public string style; public float p, r; public int until; }
        readonly List<Linger> list = new List<Linger>();

        // weapons charging up right now (not saved: a charge interrupted by a reload of the game simply loses its light show)
        class Charge { public Vehicles.VehicleTurret turret; public int start, end; }
        readonly List<Charge> charges = new List<Charge>();

        public void StartCharge(Vehicles.VehicleTurret turret, int ticks)
        {
            int now = Find.TickManager.TicksGame;
            charges.RemoveAll(c => c.turret == turret);
            charges.Add(new Charge { turret = turret, start = now, end = now + ticks });
        }

        void TickCharges(int now)
        {
            for (int i = charges.Count - 1; i >= 0; i--)
            {
                Charge c = charges[i];
                Vehicles.VehicleTurret t = c.turret;
                if (now >= c.end || now < c.start || t == null || t.vehicle == null || !t.vehicle.Spawned || t.vehicle.Map != map || !t.targetInfo.IsValid)
                {
                    charges.RemoveAt(i);
                    continue;
                }
                if ((now - c.start) % 2 != 0) continue;
                ThingDef proj = t.def != null ? t.def.projectile : null;
                DefModExtension_ShotFx fx = proj != null ? proj.GetModExtension<DefModExtension_ShotFx>() : null;
                if (fx == null) continue;
                Vector3 muzzle = MuzzleUtil.World(t);      // the tip of the barrel in the picture shown right now
                ShotFx.ChargeTick(fx, muzzle, map, (now - c.start) / (float)(c.end - c.start), (now - c.start) / 2);
            }
        }

        public MapComponent_ShotFx(Map map) : base(map) { }

        // ---- overhead mountain being blasted open by missiles: damage per cell (saved with the game)
        public const float RoofHitPoints = 600f;
        Dictionary<int, float> roofDamage = new Dictionary<int, float>();
        static float lastRoofMessage = -999f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref roofDamage, "roofDamage", LookMode.Value, LookMode.Value);
            if (roofDamage == null) roofDamage = new Dictionary<int, float>();
        }

        /// <summary>A missile detonates on overhead mountain at 'center'. Returns true if the centre cell is open to the sky afterwards.</summary>
        public static bool BlastRoof(Map map, IntVec3 center, float radius, float damage, SoundDef sound, DefModExtension_ShotFx fx, bool tellPlayer)
        {
            MapComponent_ShotFx comp = map.GetComponent<MapComponent_ShotFx>();
            if (comp == null) return false;
            float r = Mathf.Clamp(radius, 0.9f, 5.9f);
            int opened = 0;
            float centerPct = 0f;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(center, r, true))
            {
                if (!c.InBounds(map)) continue;
                RoofDef roof = map.roofGrid.RoofAt(c);
                if (roof == null || !roof.isThickRoof) continue;
                int idx = map.cellIndices.CellToIndex(c);
                float d;
                comp.roofDamage.TryGetValue(idx, out d);
                d += damage * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(c.DistanceTo(center) / r));
                if (d >= RoofHitPoints)
                {
                    comp.roofDamage.Remove(idx);
                    map.roofGrid.SetRoof(c, null);
                    opened++;
                    ThingDef rubble = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_RubbleRock");
                    if (rubble != null && c.Walkable(map)) FilthMaker.TryMakeFilth(c, map, rubble, Rand.RangeInclusive(1, 3));
                }
                else
                {
                    comp.roofDamage[idx] = d;
                    if (c == center) centerPct = d / RoofHitPoints;
                }
            }

            // the blast on the mountainside: sound, shake, and smoke / fire glow drawn above the fog (the ground below is usually unexplored)
            Vector3 pos = center.ToVector3Shifted();
            if (sound != null) sound.PlayOneShot(new TargetInfo(center, map));
            float p = fx != null ? fx.power : 1f;
            ShotFx.Shake(pos, map, 3f * p);
            FleckDef smoke = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_TrailSmokeHigh"), glow = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_TrailGlowHigh");
            for (int i = 0; i < 4 + Mathf.RoundToInt(p * 2f); i++)
            {
                Vector3 q = pos + Gen.RandomHorizontalVector(r * 0.7f);
                if (!q.InBounds(map)) continue;
                if (glow != null) FleckMaker.Static(q, map, glow, 3f + 2f * p);
                if (smoke != null)
                {
                    FleckCreationData data = FleckMaker.GetDataStatic(q, map, smoke, Rand.Range(2f, 3.5f));
                    data.rotationRate = Rand.Range(-30f, 30f);
                    data.velocityAngle = Rand.Range(0, 360);
                    data.velocitySpeed = Rand.Range(0.3f, 0.8f);
                    map.flecks.CreateFleck(data);
                }
            }

            bool centerOpen = map.roofGrid.RoofAt(center) == null || !map.roofGrid.RoofAt(center).isThickRoof;
            if (tellPlayer && (opened > 0 || Time.realtimeSinceStartup - lastRoofMessage > 5f))
            {
                lastRoofMessage = Time.realtimeSinceStartup;
                string text = opened > 0
                    ? "Missiles broke through the overhead mountain (" + opened + (opened == 1 ? " cell" : " cells") + " opened)."
                    : "Missiles are blasting into overhead mountain: " + Mathf.RoundToInt(centerPct * 100f) + "% of the way through at the aim point.";
                Messages.Message(text, new TargetInfo(center, map), opened > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent, false);
            }
            return centerOpen;
        }

        public static void Add(Map map, Vector3 pos, string style, float p, float r, int ticks)
        {
            MapComponent_ShotFx c = map.GetComponent<MapComponent_ShotFx>();
            if (c == null || c.list.Count > 60) return;
            c.list.Add(new Linger { pos = pos, style = style, p = p, r = r, until = Find.TickManager.TicksGame + ticks });
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (charges.Count > 0) TickCharges(now);
            if (list.Count == 0) return;
            if (now % 7 != 0) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Linger l = list[i];
                if (now >= l.until) { list.RemoveAt(i); continue; }
                if (!l.pos.ShouldSpawnMotesAt(map)) continue;
                Vector3 q = l.pos + Gen.RandomHorizontalVector(l.r * 0.6f);
                if (!q.InBounds(map)) continue;
                bool plasma = l.style == "plasma" || l.style == "lightning" || l.style == "conversion";
                FleckMaker.ThrowSmoke(q, map, 0.9f + 0.5f * l.p);
                if (plasma) GlowFx.Lightning(q, map, 0.8f + 0.5f * l.p);
                else if (Rand.Chance(0.7f)) GlowFx.Fire(q, map, 0.5f + 0.3f * l.p);
                if (Rand.Chance(0.5f)) FleckMaker.ThrowMicroSparks(q, map);
            }
        }
    }

    /// <summary>Explosive Knight shot (shells, missiles, plasma, energy blasts).</summary>
    public class Projectile_KnightShell : Projectile_Explosive, IKnightShot
    {
        int fxTicks;
        bool braced;      // conversion beam: fired while the Knight stood still (x1.5)
        bool firstOfBurst; // flamers: the first shot of a burst glows (not saved: purely visual)
        bool supercharged; // plasma decimator fired in its Supercharge mode (DominusWeapons)
        bool breaching, shredding, lowMode;   // Acastus / Questoris lore passes
        float overchargeReach; // lightning lock fired in its Overcharge mode: beyond this distance the shot has half power (0 = not overcharged)
        static readonly Dictionary<int, int> lastStreamShot = new Dictionary<int, int>();

        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref braced, "braced", false); Scribe_Values.Look(ref supercharged, "supercharged", false);
            Scribe_Values.Look(ref breaching, "breaching", false); Scribe_Values.Look(ref shredding, "shredding", false); Scribe_Values.Look(ref lowMode, "lowMode", false);
            Scribe_Values.Look(ref overchargeReach, "overchargeReach", 0f);
        }

        public override int DamageAmount
        {
            get
            {
                DefModExtension_ShotFx fx = ShotFx.Of(this);
                float m = ShotFx.DistanceFactor(fx, origin, destination);
                float dist = (destination - origin).MagnitudeHorizontal();
                if (fx != null && supercharged && fx.supercharge > 1f) m *= fx.supercharge;
                if (fx != null && fx.meltaRange > 0f && dist <= fx.meltaRange) m *= fx.meltaMult;
                // HH3 Heavy / Ordnance on explosive shots (thermal spear); the conversion bands apply their own braced bonus
                if (fx != null && braced && fx.braceMult > 1f && !fx.conversionBands) m *= fx.braceMult;
                if (overchargeReach > 0f && dist > overchargeReach) m *= 0.5f;
                if (fx != null && fx.antiAir > 1f && AcastusWeapons.Flying(usedTarget.Thing ?? intendedTarget.Thing)) m *= fx.antiAir;
                if (shredding) m *= 1.25f;
                if (lowMode && fx != null && fx.lowIntensity > 0f) m *= fx.lowIntensity;
                return Mathf.Max(1, Mathf.RoundToInt(base.DamageAmount * m));
            }
        }
        public override float ArmorPenetration
        {
            get
            {
                DefModExtension_ShotFx fx = ShotFx.Of(this);
                float ap = base.ArmorPenetration * ShotFx.DistanceFactor(fx, origin, destination) * (supercharged ? DominusWeapons.SuperchargeAP : 1f);
                if (fx != null && braced && fx.braceAP > 1f) ap *= fx.braceAP;
                if (fx != null && lowMode && fx.lowIntensity > 0f) ap *= fx.lowIntensityAP;
                return breaching && fx != null ? Mathf.Max(ap, fx.breachAP) : ap;
            }
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            // flamer sweep: this shot of the burst lands at its place on the band (set by TurretMotion just before the turret fired)
            if (TurretMotion.pendingSweep) { TurretMotion.pendingSweep = false; usedTarget = new LocalTargetInfo(TurretMotion.pendingCell); }
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            braced = launcher is Vehicles.VehiclePawn lv && lv.vehiclePather != null && !lv.vehiclePather.Moving;
            if (!Spawned) return;
            DefModExtension_ShotFx fx = ShotFx.Of(this);
            if (fx != null && fx.supercharge > 1f && DominusWeapons.pendingSupercharge) supercharged = true;
            if (fx != null && fx.overcharge > 0f && ArmigerWeapons.pendingOverchargeReach > 0f) overchargeReach = ArmigerWeapons.pendingOverchargeReach;
            if (fx != null && fx.breachChance > 0f) breaching = Rand.Chance(fx.breachChance);
            if (fx != null && fx.shredChance > 0f) shredding = Rand.Chance(fx.shredChance);
            if (fx != null && fx.lowIntensity > 0f) lowMode = QuestorisWeapons.LowIntensityMode;
            AcastusWeapons.BracedExtra(this, fx, braced, launcher, origin, usedTarget, intendedTarget, hitFlags, equipment);
            QuestorisWeapons.RapidFire(this, fx, launcher, origin, usedTarget, intendedTarget, hitFlags, equipment);
            // conversion beamer (lore): the Knight moved while it fired - the beam's intensity is negated and the shot is useless
            if (fx != null && fx.mustBrace && launcher is Vehicles.VehiclePawn mv && mv.vehiclePather != null && mv.vehiclePather.Moving)
            {
                ArmigerWeapons.Fizzle(this.origin, destination, Map, mv);
                Destroy();
                return;
            }
            if (fx != null && fx.tether) MapComponent_HarpoonTether.Add(this, this.origin);
            if (fx != null && fx.stream && launcher != null)
            {
                int key = launcher.thingIDNumber * 397 ^ def.shortHash, now = Find.TickManager.TicksGame, last;
                firstOfBurst = !lastStreamShot.TryGetValue(key, out last) || now - last > 25 || now < last;
                lastStreamShot[key] = now;
            }
            if (fx != null && fx.pierce > 0f)
            {
                // instant beam: burn through whatever is in the way, then hit at once (the projectile is never seen)
                Vector3 end;
                if (KnightShotUtil.BurnPath(this.launcher, Map, this.origin, destination, this.usedTarget.Thing, fx.pierce, fx.power, out end))
                {
                    destination = end;
                    this.usedTarget = new LocalTargetInfo(end.ToIntVec3());
                }
                ShotFx.Muzzle(fx, this.origin, destination, Map);
                ticksToImpact = 0;
                if (DestinationCell.InBounds(Map)) Position = DestinationCell;
                ImpactSomething();
                return;
            }
            ShotFx.Muzzle(fx, this.origin, destination, Map);
        }

        public bool KnightIntercept(IntVec3 c)
        {
            if (destination.ToIntVec3() == c) return false;
            DefModExtension_ShotFx ifx = ShotFx.Of(this);
            if (ifx != null && ifx.ignoreCover)
            {
                // Torrent / Ignores Cover: the flames wash over cover and people in the way - only a solid wall stops them
                foreach (Thing w in c.GetThingList(Map)) if (CanHit(w) && KnightShotUtil.SolidBlocker(w)) { Impact(w); return true; }
                return false;
            }
            List<Thing> list = c.GetThingList(Map);
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (!CanHit(t)) continue;
                if (KnightShotUtil.SolidBlocker(t)) { Impact(t); return true; }
                Pawn p = t as Pawn;
                if (p != null && Rand.Chance(KnightShotUtil.PawnInterceptChance(p, launcher, origin, c))) { Impact(p); return true; }
            }
            return false;
        }

        protected override void TickInterval(int delta)
        {
            if (Spawned && !landed)
                ShotFx.Trail(ShotFx.Of(this), ShotFx.ArcPos(ExactPosition, origin, destination, def.projectile.arcHeightFactor, DistanceCoveredFractionArc), Map, fxTicks++, def.projectile.flyOverhead, firstOfBurst);
            base.TickInterval(delta);
        }

        /// <summary>
        /// Indirect-fire missiles and overhead mountain (user 2026-11-11): instead of vanishing (vanilla mortar rule) the missile detonates on the
        /// mountain and chips away at the thick roof. Enough hits blast a hole; missiles landing on an opened cell then fall through and hit normally.
        /// </summary>
        protected override void ImpactSomething()
        {
            if (def.projectile.flyOverhead && Map != null && Position.InBounds(Map))
            {
                RoofDef roof = Map.roofGrid.RoofAt(Position);
                if (roof != null && roof.isThickRoof)
                {
                    bool player = launcher != null && launcher.Faction == Faction.OfPlayerSilentFail;
                    bool open = MapComponent_ShotFx.BlastRoof(Map, Position, def.projectile.explosionRadius, DamageAmount, def.projectile.soundExplode, ShotFx.Of(this), player);
                    if (!open) { Destroy(); return; }     // spent on the mountainside
                }
            }
            base.ImpactSomething();
        }

        protected override void Explode()
        {
            Map map = Map;
            IntVec3 cell = Position;
            Vector3 from = origin;
            Thing who = launcher;
            DefModExtension_ShotFx fx = ShotFx.Of(this);
            float radius = def.projectile.explosionRadius;
            if (fx != null && fx.conversionBands)
            {
                int dmg = DamageAmount; float ap = ArmorPenetration; Vector3 dest = destination; ThingDef weapon = equipmentDef; Thing intended = intendedTarget.Thing;
                Destroy();
                ConversionBeam.Detonate(this, fx, map, cell, from, dest, who, dmg, ap, braced, weapon, intended);
                return;
            }
            if (fx != null && fx.gravField > 0f)
            {
                int gdmg = DamageAmount; SoundDef gs = def.projectile.soundExplode;
                Destroy();
                Gravity.Impact(map, cell, radius, who, gdmg, fx.gravField, fx.gravTick, gs);
                return;
            }
            if (fx != null && fx.vaporise > 0f) KnightShotUtil.Vaporise(map, cell, fx.vaporise, who, def);
            ThingDef shotDef = def;
            int dmg0 = DamageAmount; float ap0 = ArmorPenetration; ThingDef weapon0 = equipmentDef; Thing target0 = usedTarget.Thing ?? intendedTarget.Thing;
            if (fx != null && fx.shieldMult > 1f) DominusWeapons.ShieldBreak(map, cell, radius, dmg0 * (fx.shieldMult - 1f), who);
            if (fx != null && fx.tether) MapComponent_HarpoonTether.Impact(this, ExactPosition);
            bool super0 = supercharged;
            base.Explode();
            if (fx != null && fx.antiArmour > 1f) DominusWeapons.AntiArmour(map, cell, radius, target0, fx.antiArmour, dmg0, ap0, who, weapon0, shotDef.projectile.damageDef);
            AcastusWeapons.Shock(target0, fx, who, map);
            if (fx != null && fx.gravPin > 0f) QuestorisWeapons.GravPin(map, cell.ToVector3Shifted(), radius + 0.5f, fx.gravPin, who);
            if (fx != null && fx.chainHits > 0) QuestorisWeapons.ChainHits(target0, fx, map, cell.ToVector3Shifted(), dmg0, ap0, shotDef.projectile.damageDef, who, weapon0);
            if (fx != null && fx.arcRadius > 0f) DominusWeapons.Thundershock(map, cell, who, fx, weapon0);
            if (super0) ShotFx.Blast(fx, cell, map, radius * 1.3f, from);      // supercharged: a bigger, brighter detonation
            if (KnightLethal.Is(shotDef)) KnightLethal.Strike(map, cell, radius, who, shotDef);
            ShotFx.Blast(fx, cell, map, radius, from);
            if (fx != null && fx.stream && firstOfBurst) ShotFx.IgnitionGlow(cell, map, radius, fx.power);
            if (fx != null && fx.stream)
            {
                if (fx.poolGrowth > 0f) FirePools.Hit(map, cell, radius, who, fx.poolGrowth, fx.poolMax);
                else ShotFx.Ignite(cell, map, radius, who);
            }
            if (fx != null && fx.panic > 0f) KnightEffects.Panic(map, cell, radius + 0.5f, who, fx.panic);
            if (fx != null && fx.suppress > 0f) KnightEffects.Suppress(map, cell.ToVector3Shifted(), radius + fx.suppress, who);
            if (fx != null && fx.irradiate > 0f) ArmigerWeapons.Irradiate(map, cell, radius, who, fx.irradiate);
        }
    }

    /// <summary>Non-explosive Knight shot (autocannon, stubber, gatling, lascannon ...).</summary>
    public class Bullet_Knight : Bullet, IKnightShot
    {
        int fxTicks;
        bool braced, breaching, shredding, lowMode;

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref braced, "braced", false); Scribe_Values.Look(ref breaching, "breaching", false); Scribe_Values.Look(ref shredding, "shredding", false); Scribe_Values.Look(ref lowMode, "lowMode", false); }

        public override int DamageAmount
        {
            get
            {
                DefModExtension_ShotFx fx = ShotFx.Of(this);
                float m = ShotFx.DistanceFactor(fx, origin, destination) * (braced && fx != null && fx.braceMult > 1f ? fx.braceMult : 1f);
                if (fx != null && fx.antiAir > 1f && AcastusWeapons.Flying(usedTarget.Thing)) m *= fx.antiAir;
                if (shredding) m *= 1.25f;
                if (lowMode && fx != null && fx.lowIntensity > 0f) m *= fx.lowIntensity;
                return Mathf.Max(1, Mathf.RoundToInt(base.DamageAmount * m));
            }
        }
        public override float ArmorPenetration
        {
            get
            {
                DefModExtension_ShotFx fx = ShotFx.Of(this);
                float ap = base.ArmorPenetration * ShotFx.DistanceFactor(fx, origin, destination);
                if (fx != null && braced && fx.braceAP > 1f) ap *= fx.braceAP;
                if (fx != null && lowMode && fx.lowIntensity > 0f) ap *= fx.lowIntensityAP;
                return breaching && fx != null ? Mathf.Max(ap, fx.breachAP) : ap;      // HH3 Breaching: this round punches through
            }
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            braced = launcher is Vehicles.VehiclePawn lv && lv.vehiclePather != null && !lv.vehiclePather.Moving;
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
            if (!Spawned) return;
            DefModExtension_ShotFx fx = ShotFx.Of(this);
            if (fx != null && fx.breachChance > 0f) breaching = Rand.Chance(fx.breachChance);
            if (fx != null && fx.shredChance > 0f) shredding = Rand.Chance(fx.shredChance);
            if (fx != null && fx.lowIntensity > 0f) lowMode = QuestorisWeapons.LowIntensityMode;
            AcastusWeapons.BracedExtra(this, fx, braced, launcher, origin, usedTarget, intendedTarget, hitFlags, equipment);
            QuestorisWeapons.RapidFire(this, fx, launcher, origin, usedTarget, intendedTarget, hitFlags, equipment);
            if (fx != null && fx.pierce > 0f)
            {
                // instant beam: burn through whatever is in the way, then hit at once (the projectile is never seen)
                Vector3 end;
                if (KnightShotUtil.BurnPath(this.launcher, Map, this.origin, destination, this.usedTarget.Thing, fx.pierce, fx.power, out end))
                {
                    destination = end;
                    this.usedTarget = new LocalTargetInfo(end.ToIntVec3());
                }
                ShotFx.Muzzle(fx, this.origin, destination, Map);
                ticksToImpact = 0;
                if (DestinationCell.InBounds(Map)) Position = DestinationCell;
                ImpactSomething();
                return;
            }
            ShotFx.Muzzle(fx, this.origin, destination, Map);
        }

        public bool KnightIntercept(IntVec3 c)
        {
            if (destination.ToIntVec3() == c) return false;
            DefModExtension_ShotFx bfx = ShotFx.Of(this);
            if (bfx != null && bfx.ignoreCover)
            {
                foreach (Thing w in c.GetThingList(Map)) if (CanHit(w) && KnightShotUtil.SolidBlocker(w)) { Impact(w); return true; }
                return false;
            }
            List<Thing> list = c.GetThingList(Map);
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (!CanHit(t)) continue;
                if (KnightShotUtil.SolidBlocker(t)) { Impact(t); return true; }
                Pawn p = t as Pawn;
                if (p != null && Rand.Chance(KnightShotUtil.PawnInterceptChance(p, launcher, origin, c))) { Impact(p); return true; }
            }
            return false;
        }

        protected override void TickInterval(int delta)
        {
            if (Spawned && !landed) ShotFx.Trail(ShotFx.Of(this), ExactPosition, Map, fxTicks++);
            base.TickInterval(delta);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            Vector3 pos = ExactPosition;
            DefModExtension_ShotFx fx = ShotFx.Of(this);
            Thing who = launcher;
            ThingDef weapon = equipmentDef;
            base.Impact(hitThing, blockedByShield);
            ShotFx.Hit(fx, pos, map);
            if (fx != null && fx.impactSound != null && map != null && pos.ToIntVec3().InBounds(map)) fx.impactSound.PlayOneShot(new TargetInfo(pos.ToIntVec3(), map));
            if (fx == null || map == null || blockedByShield) return;
            // anti-tank (Armourbane): the extra on war machines and structures
            if (fx.antiArmour > 1f && hitThing != null && !hitThing.Destroyed && (hitThing is Building || hitThing is Vehicles.VehiclePawn || hitThing is Pawn mp && mp.RaceProps.IsMechanoid && mp.BodySize >= 2f))
                hitThing.TakeDamage(new DamageInfo(def.projectile.damageDef, DamageAmount * (fx.antiArmour - 1f), ArmorPenetration, -1f, who, null, weapon));
            if (fx.massReactive > 0f) KnightEffects.MassReactive(hitThing, fx.massReactive, who, weapon, map, pos);
            AcastusWeapons.Shock(hitThing, fx, who, map);
            QuestorisWeapons.ChainHits(hitThing, fx, map, pos, DamageAmount, ArmorPenetration, def.projectile.damageDef, who, weapon);
            if (fx.gravPin > 0f) QuestorisWeapons.GravPin(map, pos, 1.5f, fx.gravPin, who);
            if (breaching && hitThing != null && pos.ShouldSpawnMotesAt(map)) { FleckMaker.ThrowMicroSparks(pos, map); FleckMaker.ThrowMicroSparks(pos, map); }
            if (fx.deflagrate > 0f && hitThing is Pawn dp) ArmigerWeapons.Deflagrate(dp, fx.deflagrate, who, weapon, map);
            if (fx.suppress > 0f) KnightEffects.Suppress(map, pos, fx.suppress, who);
            if (fx.style == "lightning" && fx.stunChance > 0f && hitThing is Pawn ap0 && pos.ShouldSpawnMotesAt(map))
            {
                foreach (Thing near in GenRadial.RadialDistinctThingsAround(ap0.Position, map, 3.5f, true))
                    if (near is Pawn np && np != ap0 && !np.Dead && (who == null || np.Faction != who.Faction)) { ShotFx.BoltPublic(ap0.DrawPos, np.DrawPos, map, 0.5f, 0.4f); break; }
                KnightEffects.ElectricFlash(pos, map, 0.6f);
            }
            if (fx.stunChance > 0f && hitThing is Pawn sp && !sp.Dead && !sp.Destroyed && sp.stances?.stunner != null && Rand.Chance(fx.stunChance))
            {
                sp.stances.stunner.StunFor(fx.stunTicks, who, false, true);
                KnightEffects.ElectricFlash(sp.DrawPos, map, 0.7f);
            }
        }
    }
}
