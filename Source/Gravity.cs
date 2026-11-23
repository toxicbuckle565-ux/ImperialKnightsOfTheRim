using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// Graviton singularity cannon (user design 2026-11-15): NO pull. Where the shot lands gravity is MULTIPLIED for a few seconds: everything
    /// in the field becomes immensely heavy - slowed almost to a stop and crushed under its own weight, both worse the more armour it wears
    /// and the bigger it is (graviton lore: a target's own mass and armour turned against it). Buildings buckle.
    /// Visuals: real background distortion pushing DOWN over the field (vanilla MoteGlowDistortBackground shader + our pattern), a purple rim,
    /// purple rings pressing inward, and on everything caught: a pressure shadow, grit falling onto it, armour sparking, dust dragged.
    /// </summary>
    public static class Gravity
    {
        public class Field { public Map map; public IntVec3 cell; public Vector3 pos; public float radius, tickDamage; public int start, end; public Thing instigator; public bool misfire; }
        public static readonly List<Field> fields = new List<Field>();
        const int Pass = 15;                 // effects every quarter second

        static FleckDef F(string n) { return DefDatabase<FleckDef>.GetNamedSilentFail(n); }
        static HediffDef crushed;
        static HediffDef Crushed => crushed ?? (crushed = DefDatabase<HediffDef>.GetNamedSilentFail("LIK_GravityCrushed"));

        /// <summary>0 (none) .. 2 (super-heavy): natural armour plus the best armour worn.</summary>
        public static float Armour(Pawn p)
        {
            float a = p.GetStatValue(StatDefOf.ArmorRating_Sharp);
            float worn = 0f;
            if (p.apparel != null) foreach (Apparel ap in p.apparel.WornApparel) worn = Mathf.Max(worn, ap.GetStatValue(StatDefOf.ArmorRating_Sharp));
            if (p is Vehicles.VehiclePawn) a = Mathf.Max(a, 1.6f);           // a Knight / war machine: thick plating
            return Mathf.Clamp(a + worn, 0f, 2f);
        }

        /// <summary>Full strength at the centre, about 30 % at the rim (user 2026-11-15). Big things count from their part nearest the centre.</summary>
        public static float Falloff(Thing t, IntVec3 center, float radius)
        {
            float d = 9999f;
            foreach (IntVec3 c in t.OccupiedRect()) d = Mathf.Min(d, c.DistanceTo(center));
            return Mathf.Lerp(1f, 0.3f, Mathf.Pow(Mathf.Clamp01(d / Mathf.Max(0.5f, radius)), 1.3f));
        }

        public static float MassFactor(Pawn p) { return Mathf.Lerp(0.6f, 2.5f, Mathf.Clamp01((p.BodySize - 0.5f) / 4f)); }
        public static float ArmourFactor(float a) { return 1f + a * 0.75f; }

        static bool Spared(Thing t, Thing instigator)
        {
            if (t == instigator) return true;
            return instigator != null && instigator.Faction != null && t.Faction == instigator.Faction;
        }

        /// <summary>The shot lands: the crushing blow, the snap of the field forming, and the field itself.</summary>
        public static void Impact(Map map, IntVec3 cell, float radius, Thing instigator, int damage, float seconds, float tickDamage, SoundDef sound)
        {
            if (map == null || !cell.InBounds(map)) return;
            Vector3 pos = cell.ToVector3Shifted();
            sound?.PlayOneShot(new TargetInfo(cell, map));
            ImpactFx(map, pos, radius);
            // the blow itself: scaled per target by its mass and armour
            List<Thing> hit = new List<Thing>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Thing t in c.GetThingList(map)) if ((t is Pawn || t is Building) && !Spared(t, instigator) && !hit.Contains(t)) hit.Add(t);
            }
            foreach (Thing t in hit)
            {
                if (t.Destroyed) continue;
                float mult = (t is Pawn p ? MassFactor(p) * ArmourFactor(Armour(p)) : 2f) * Falloff(t, cell, radius);      // buildings buckle: double; weaker toward the rim
                t.TakeDamage(new DamageInfo(DamageDefOf.Crush, damage * mult, 2f, -1f, instigator));
            }
            int now = Find.TickManager.TicksGame, life = Mathf.RoundToInt(seconds * 60f);
            fields.Add(new Field { map = map, cell = cell, pos = pos, radius = radius, tickDamage = tickDamage, start = now, end = now + life, instigator = instigator });
            FieldFlecks(map, pos, radius, seconds);
        }

        public static void MisfireField(Map map, IntVec3 cell, float radius, Thing knight, float seconds, float tickDamage, SoundDef sound)
        {
            Vector3 pos = cell.ToVector3Shifted();
            sound?.PlayOneShot(new TargetInfo(cell, map));
            ImpactFx(map, pos, radius);
            int now = Find.TickManager.TicksGame;
            fields.Add(new Field { map = map, cell = cell, pos = pos, radius = radius, tickDamage = tickDamage, start = now, end = now + Mathf.RoundToInt(seconds * 60f), instigator = knight, misfire = true });
            FieldFlecks(map, pos, radius, seconds);
        }

        static void ImpactFx(Map map, Vector3 pos, float r)
        {
            if (!pos.ShouldSpawnMotesAt(map)) { ShotFx.Shake(pos, map, 6f); return; }
            GlowFx.Flash(pos, map, 10f + r * 4f);
            FleckDef glow = F("LIK_ChargeGlow");
            if (glow != null) { FleckCreationData g = FleckMaker.GetDataStatic(pos, map, glow, 4f + r * 1.5f); g.instanceColor = new Color(0.75f, 0.5f, 1f, 0.9f); map.flecks.CreateFleck(g); }
            FleckDef sw = F("LIK_Shockwave_L");
            if (sw != null) FleckMaker.Static(pos, map, sw, 1f);
            FleckDef crack = F("GroundCrack");
            for (int i = 0; crack != null && i < 4; i++)
            {
                FleckCreationData c = FleckMaker.GetDataStatic(pos + Gen.RandomHorizontalVector(r * 0.5f), map, crack, 1.8f + r * 0.5f);
                c.rotation = Rand.Range(0f, 360f); map.flecks.CreateFleck(c);
            }
            // dust slammed flat: pushed outward along the ground, never thrown up
            FleckDef dust = F("DustPuff");
            for (int i = 0; dust != null && i < 16; i++)
            {
                float a = 22.5f * i + Rand.Range(-8f, 8f);
                Vector3 q = pos + Vector3.forward.RotatedBy(a) * Rand.Range(0.4f, r * 0.6f);
                if (!q.InBounds(map)) continue;
                FleckCreationData d = FleckMaker.GetDataStatic(q, map, dust, Rand.Range(1.4f, 2.2f));
                d.velocityAngle = a; d.velocitySpeed = Rand.Range(2.5f, 4.5f); d.instanceColor = new Color(0.45f, 0.42f, 0.4f, 0.75f);
                map.flecks.CreateFleck(d);
            }
            ThingDef rubble = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_RubbleRock");
            if (rubble != null) for (int i = 0; i < 4; i++) { IntVec3 c = (pos + Gen.RandomHorizontalVector(r * 0.7f)).ToIntVec3(); if (c.InBounds(map) && c.Walkable(map)) FilthMaker.TryMakeFilth(c, map, rubble, 1); }
            ShotFx.Shake(pos, map, 6f);
        }

        /// <summary>The field's long-lived pictures: the real distortion pushing down, and the purple rim.</summary>
        static void FieldFlecks(Map map, Vector3 pos, float r, float seconds)
        {
            Vector3 p = pos; p.y = AltitudeLayer.MoteOverheadLow.AltitudeFor();
            FleckDef warp = F("LIK_GravDistort"), rim = F("LIK_GravRim");
            if (warp != null) map.flecks.CreateFleck(FleckMaker.GetDataStatic(p, map, warp, r * 2.3f));
            if (rim != null) map.flecks.CreateFleck(FleckMaker.GetDataStatic(p, map, rim, r * 2.15f));
            FleckDef core = F("LIK_GravDistortCore");
            if (core != null) map.flecks.CreateFleck(FleckMaker.GetDataStatic(p, map, core, r * 1.0f));
        }

        public static void Tick(Map map)
        {
            if (fields.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = fields.Count - 1; i >= 0; i--)
            {
                Field f = fields[i];
                if (now >= f.end || f.map == null || f.map.Index < 0) { fields.RemoveAt(i); continue; }
                if (f.map != map) continue;
                int age = now - f.start;
                if (f.pos.ShouldSpawnMotesAt(map) && age % 20 == 0) Pulse(map, f);
                if (age % Pass == 0) Press(map, f, now);
            }
        }

        /// <summary>Rings pressing inward and small extra warps shimmering across the field.</summary>
        static void Pulse(Map map, Field f)
        {
            FleckDef ripple = F("LIK_GravRipple"), small = F("LIK_GravDistortSmall");
            Vector3 alt = new Vector3(0f, AltitudeLayer.MoteOverheadLow.AltitudeFor(), 0f);
            if (ripple != null)
            {
                Vector3 q = f.pos; q.y = alt.y;
                map.flecks.CreateFleck(FleckMaker.GetDataStatic(q, map, ripple, f.radius * Rand.Range(1.5f, 2.1f)));
                map.flecks.CreateFleck(FleckMaker.GetDataStatic(q, map, ripple, f.radius * Rand.Range(0.6f, 1.0f)));      // the heavy middle
            }
            // warps drifting DOWN across the field (toward the bottom of the screen): the distortion visibly flows downward
            if (small != null)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 q = f.pos + new Vector3(Rand.Range(-f.radius * 0.7f, f.radius * 0.7f), 0f, Rand.Range(-f.radius * 0.2f, f.radius * 0.7f)); q.y = alt.y;
                    if (!q.InBounds(map)) continue;
                    FleckCreationData d = FleckMaker.GetDataStatic(q, map, small, Rand.Range(1.8f, 2.8f));
                    d.velocityAngle = 180f; d.velocitySpeed = Rand.Range(1.6f, 2.4f);
                    map.flecks.CreateFleck(d);
                }
        }

        /// <summary>A quarter-second of crushing gravity on everything inside.</summary>
        static void Press(Map map, Field f, int now)
        {
            List<Thing> inside = new List<Thing>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(f.cell, f.radius, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Thing t in c.GetThingList(map)) if ((t is Pawn || t is Building) && (f.misfire ? t != f.instigator : !Spared(t, f.instigator)) && !inside.Contains(t)) inside.Add(t);
            }
            bool fx = f.pos.ShouldSpawnMotesAt(map);
            foreach (Thing t in inside)
            {
                if (t.Destroyed) continue;
                if (f.misfire && t == f.instigator) continue;
                if (t is Pawn p)
                {
                    if (p.Dead) continue;
                    float a = Armour(p);
                    float fo = Falloff(p, f.cell, f.radius);
                    p.TakeDamage(new DamageInfo(DamageDefOf.Crush, f.tickDamage * MassFactor(p) * ArmourFactor(a) * fo, 2f, -1f, f.instigator));
                    if (p.Dead || p.Destroyed) continue;
                    Weigh(p, a, fo);
                    if (fx) Affected(map, p, a);
                }
                else t.TakeDamage(new DamageInfo(DamageDefOf.Crush, f.tickDamage * 2f * Falloff(t, f.cell, f.radius), 2f, -1f, f.instigator));
            }
        }

        /// <summary>The slowdown: severity grows with armour and size (a power-armoured giant is pinned almost still).</summary>
        static void Weigh(Pawn p, float armour, float falloff)
        {
            if (Crushed == null || p is Vehicles.VehiclePawn) return;
            float sev = Mathf.Clamp((0.35f + 0.25f * armour + 0.06f * Mathf.Min(p.BodySize, 5f)) * (0.45f + 0.55f * falloff), 0.2f, 1f);
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Crushed);
            if (h == null) { h = HediffMaker.MakeHediff(Crushed, p); h.Severity = sev; p.health.AddHediff(h); }
            else h.Severity = Mathf.Max(h.Severity, sev);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = 90;               // lifts a moment after leaving the field
        }

        /// <summary>What it looks like on one crushed thing: pressure shadow, grit falling onto it, armour sparking, dust dragged.</summary>
        static void Affected(Map map, Pawn p, float armour)
        {
            Vector3 at = p.DrawPos;
            FleckDef shadow = F("LIK_GravShadow"), dust = F("DustPuff");
            if (shadow != null) { Vector3 s = at; s.y = AltitudeLayer.MoteLow.AltitudeFor(); map.flecks.CreateFleck(FleckMaker.GetDataStatic(s, map, shadow, 1.4f + p.BodySize * 0.9f)); }
            // grit dropping straight onto it (from just above, toward the bottom of the screen)
            for (int k = 0; dust != null && k < 2; k++)
            {
                FleckCreationData d = FleckMaker.GetDataStatic(at + new Vector3(Rand.Range(-0.5f, 0.5f), 0f, Rand.Range(0.6f, 1.0f)), map, dust, Rand.Range(0.35f, 0.6f));
                d.velocityAngle = 180f; d.velocitySpeed = Rand.Range(2.5f, 4f); d.instanceColor = new Color(0.35f, 0.32f, 0.3f, 0.85f);
                map.flecks.CreateFleck(d);
            }
            // armour buckling: more the heavier the armour
            if (Rand.Chance(0.25f + 0.35f * armour))
            {
                for (int k = 0; k < 1 + Mathf.RoundToInt(armour * 2f); k++) FleckMaker.ThrowMicroSparks(at + Gen.RandomHorizontalVector(0.4f), map);
                FleckDef debris = F("ThrownDebris");
                if (debris != null && armour > 0.8f && Rand.Chance(0.4f))
                {
                    FleckCreationData d = FleckMaker.GetDataStatic(at, map, debris, Rand.Range(0.4f, 0.7f));
                    d.rotation = Rand.Range(0f, 360f); d.rotationRate = Rand.Range(100f, 300f);
                    d.velocityAngle = Rand.Range(90f, 270f); d.velocitySpeed = Rand.Range(1f, 2.5f);
                    map.flecks.CreateFleck(d);
                }
            }
            // dragging itself along: heavy scrapes in the dust
            if (p.pather != null && p.pather.Moving) FleckMaker.ThrowDustPuffThick(at + new Vector3(0f, 0f, -0.3f), map, 0.9f + p.BodySize * 0.3f, new Color(0.45f, 0.42f, 0.4f, 0.7f));
        }
    }

    /// <summary>
    /// OVERLOAD (HH3 Overload (1): the singularity can collapse at the gun). user 2026-11-15: 3 % chance on every shot, or certain once the
    /// cannon has overheated. Each shot adds heat; heat bleeds off slowly, so a steady rate of fire is fine but firing back to back stacks it.
    /// Above 70 % the muzzle vents violet sparks and smoke (and the player is warned once). A misfire collapses a small singularity at the
    /// Knight's own arm (crushes the Knight and anyone near it, friend or foe) and jams the cannon for 10 s; the heat is vented.
    /// Heat / jam are not saved (they cool off in seconds anyway).
    /// </summary>
    public static class Overload
    {
        public const string TurretDef = "LIK_Turret_cerastus_singularity";
        const float HeatPerShot = 45f, DecayPerTick = 0.08f, MisfireChance = 0.03f;
        const int JamTicks = 600;
        class Heat { public float heat; public int last, jamUntil; public bool warned; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vehicles.VehicleTurret, Heat> heats = new System.Runtime.CompilerServices.ConditionalWeakTable<Vehicles.VehicleTurret, Heat>();

        static Heat Get(Vehicles.VehicleTurret t, int now)
        {
            Heat h = heats.GetValue(t, _ => new Heat { last = now });
            h.heat = Mathf.Max(0f, h.heat - DecayPerTick * Mathf.Max(0, now - h.last));
            h.last = now;
            if (h.heat < 50f) h.warned = false;
            return h;
        }

        public static float HeatPct(Vehicles.VehicleTurret t) { return Get(t, Find.TickManager.TicksGame).heat; }
        public static bool Jammed(Vehicles.VehicleTurret t) { int now = Find.TickManager.TicksGame; return now < Get(t, now).jamUntil; }

        /// <summary>FireTurret prefix: false = this shot does not leave (jammed, or it misfired).</summary>
        public static bool Fire(Vehicles.VehicleTurret t)
        {
            Vehicles.VehiclePawn v = t.vehicle;
            if (v == null || !v.Spawned) return true;
            int now = Find.TickManager.TicksGame;
            Heat h = Get(t, now);
            if (now < h.jamUntil) return false;
            bool over = h.heat >= 100f;
            if (over || Rand.Chance(MisfireChance))
            {
                Misfire(v, t, over);
                h.heat = 0f; h.jamUntil = now + JamTicks; h.warned = false;
                return false;
            }
            h.heat += HeatPerShot;
            if (h.heat >= 70f)
            {
                Vector3 m = MuzzleUtil.World(t);
                if (m.ShouldSpawnMotesAt(v.Map))
                {
                    for (int i = 0; i < 4; i++) FleckMaker.ThrowMicroSparks(m + Gen.RandomHorizontalVector(0.6f), v.Map);
                    FleckMaker.ThrowSmoke(m, v.Map, 1.2f);
                    FleckDef rip = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_GravRipple");
                    if (rip != null) FleckMaker.Static(m, v.Map, rip, 1.8f);
                }
                if (!h.warned && v.Faction == Faction.OfPlayer)
                {
                    h.warned = true;
                    Messages.Message(v.LabelShort + ": the graviton singularity cannon is overheating - let it cool or it will overload.", v, MessageTypeDefOf.CautionInput, false);
                }
            }
            return true;
        }

        static void Misfire(Vehicles.VehiclePawn v, Vehicles.VehicleTurret t, bool overheated)
        {
            Map map = v.Map;
            Vector3 at = t.TurretLocation;
            IntVec3 cell = at.ToIntVec3();
            if (!cell.InBounds(map)) cell = v.Position;
            SoundDef s = DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_impact_graviton");
            // the singularity collapses at the arm: the Knight takes the blow, a small field crushes everything near it
            v.TakeDamage(new DamageInfo(DamageDefOf.Crush, 150f, 2f, -1f, v));
            Gravity.MisfireField(map, cell, 2.5f, v, 2f, 5f, s);
            if (v.Faction == Faction.OfPlayer)
                Messages.Message(v.LabelShort + ": the graviton singularity cannon OVERLOADED" + (overheated ? " from heat" : "") + " - the singularity collapsed at its own arm! The cannon is venting.", v, MessageTypeDefOf.NegativeEvent, false);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Vehicles.VehicleTurret), nameof(Vehicles.VehicleTurret.FireTurret))]
    public static class Patch_SingularityOverload
    {
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First)]
        static bool Prefix(Vehicles.VehicleTurret __instance)
        {
            if (__instance.def == null || __instance.def.defName != Overload.TurretDef) return true;
            try { return Overload.Fire(__instance); }
            catch (System.Exception e) { Log.ErrorOnce("[LIK] singularity overload: " + e, 77151115); return true; }
        }
    }

    public class MapComponent_Gravity : MapComponent
    {
        public MapComponent_Gravity(Map map) : base(map) { }
        public override void MapComponentTick() { Gravity.Tick(map); }
    }
}
