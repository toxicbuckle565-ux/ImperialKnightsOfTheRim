using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using SmashTools.Rendering;
using Verse;

namespace LIK
{
    /// <summary>
    /// Extra weapon-arm motion on top of the aim (user 2026-11-15: "the chainfist only thrusts forward and looks boring"; "the flame cannon
    /// should sweep across an area instead of a single point"). Purely how the weapon moves and where the flame lands - no new abilities.
    ///  - melee SWING: the arm winds up to one side, sweeps through the strike and recovers; the tip leaves a streak of sparks. Alternates sides.
    ///  - flame SWEEP: the shots of one burst walk across a band at the target, side to side; the arm turns with the stream.
    /// The angle is added to the turret picture's rotation about its pivot (the elbow) in Patch_TurretMotion.
    /// </summary>
    public static class TurretMotion
    {
        public class State
        {
            // swing
            public int swingStart = -9999, swingTicks, swingDir = 1;
            public float swingArc;
            public bool impactFx;
            public Vector3 lastTip; public bool hasTip;
            public string look;
            // thrust (lunge along the aim)
            public int thrustStart = -9999, thrustTicks; public float thrustDist; public bool thrustFx;
            // sweep
            public int lastShot = -9999, shotIdx, sweepDir = 1, sweepUntil = -9999;
            public float sweepAngle;
        }

        static readonly ConditionalWeakTable<VehicleTurret, State> states = new ConditionalWeakTable<VehicleTurret, State>();
        public static readonly List<VehicleTurret> active = new List<VehicleTurret>();     // main thread only (trail effects)

        public static State Get(VehicleTurret t) { return states.GetValue(t, _ => new State()); }

        /// <summary>Degrees to add to the turret picture's rotation right now (render threads: reads only).</summary>
        public static float Offset(VehicleTurret t)
        {
            if (!states.TryGetValue(t, out State s)) return 0f;
            int now = Find.TickManager?.TicksGame ?? 0;
            float a = 0f;
            int st = now - s.swingStart;
            if (st >= 0 && st < s.swingTicks) a += SwingAngle(s, st / (float)s.swingTicks);
            if (now < s.sweepUntil) a += s.sweepAngle;
            else if (now < s.sweepUntil + 12) a += s.sweepAngle * (1f - (now - s.sweepUntil) / 12f);     // ease back to the aim
            return a;
        }

        /// <summary>Lunge: fast drive out (first 25 %), a short hold, then back on the rail (last 60 %). 0..1 of the distance.</summary>
        static float ThrustFrac(float t)
        {
            if (t < 0.25f) { float u = t / 0.25f; return 1f - (1f - u) * (1f - u); }
            if (t < 0.4f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.6f);
        }

        /// <summary>Cells the picture is pushed along its aim right now (render threads: reads only).</summary>
        public static float ThrustOffset(VehicleTurret t)
        {
            if (!states.TryGetValue(t, out State s)) return 0f;
            int st = (Find.TickManager?.TicksGame ?? 0) - s.thrustStart;
            return st >= 0 && st < s.thrustTicks ? ThrustFrac(st / (float)s.thrustTicks) * s.thrustDist : 0f;
        }

        public static void StartThrust(VehicleTurret t, float dist, int ticks)
        {
            State s = Get(t);
            int now = Find.TickManager.TicksGame;
            if (now - s.thrustStart < s.thrustTicks) return;
            s.thrustStart = now; s.thrustTicks = Mathf.Max(8, ticks); s.thrustDist = dist; s.thrustFx = false; s.hasTip = false;
            if (!active.Contains(t)) active.Add(t);
        }

        /// <summary>Wind-up to +arc (first 30 %), fast sweep to -arc (next 35 %), recover to 0 (last 35 %); swingDir flips the side.</summary>
        static float SwingAngle(State s, float t)
        {
            float a;
            if (t < 0.3f) a = Mathf.SmoothStep(0f, 1f, t / 0.3f);
            else if (t < 0.65f) { float u = (t - 0.3f) / 0.35f; a = 1f - 2f * (u * u * (3f - 2f * u)); }
            else a = -1f + Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.35f);
            return a * s.swingArc * s.swingDir;
        }

        public static void StartSwing(VehicleTurret t, float arc, int ticks, string look)
        {
            State s = Get(t);
            int now = Find.TickManager.TicksGame;
            if (now - s.swingStart < s.swingTicks) return;              // still swinging: the strike lands within this swing
            s.swingStart = now; s.swingTicks = Mathf.Max(6, ticks); s.swingArc = arc; s.swingDir = -s.swingDir;
            s.impactFx = false; s.hasTip = false; s.look = look;
            if (!active.Contains(t)) active.Add(t);
            if (Prefs.DevMode && diag < 6) { diag++; Log.Message("[LIK] swing start: " + t.def.defName + " key=" + t.key + " arc=" + arc + " ticks=" + ticks + " dir=" + s.swingDir); }
        }

        /// <summary>Where the weapon's tip is right now, swing included.</summary>
        public static int diag, diagDraw;     // dev-mode diagnostics (2026-11-15: user saw no swing)

        public static Vector3 Tip(VehicleTurret t, float extraAngle)
        {
            Vector2 p;
            if (!MuzzleUtil.Local(t, out p)) p = new Vector2(0f, t.def.projectileOffset);
            return t.TurretLocation + new Vector3(p.x, 0f, p.y).RotatedBy(t.TurretRotation + extraAngle);
        }

        /// <summary>Main thread, every tick: the streak and sparks of swinging weapons.</summary>
        public static void Tick(Map map)
        {
            int now = Find.TickManager.TicksGame;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                VehicleTurret t = active[i];
                if (t?.vehicle == null || !t.vehicle.Spawned || !states.TryGetValue(t, out State s)) { active.RemoveAt(i); continue; }
                if (t.vehicle.Map != map) continue;
                int tt = now - s.thrustStart;
                bool thrusting = tt >= 0 && tt < s.thrustTicks;
                if (thrusting) { ThrustTick(t, s, map, tt / (float)s.thrustTicks); continue; }
                int st = now - s.swingStart;
                if (st >= s.swingTicks || st < 0) { active.RemoveAt(i); continue; }
                float frac = st / (float)s.swingTicks;
                Vector3 tip = Tip(t, SwingAngle(s, frac));
                tip.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                if (s.hasTip && tip.InBounds(map) && tip.ShouldSpawnMotesAt(map))
                {
                    bool fast = frac >= 0.3f && frac < 0.65f;         // the sweep itself
                    if (fast)
                    {
                        // motion streak of the fist/blade + teeth throwing sparks
                        Streak(s.lastTip, tip, map, s.look == "chain" ? new Color(1f, 0.78f, 0.45f, 0.55f) : s.look == "flame" ? new Color(1f, 0.5f, 0.15f, 0.7f) : new Color(0.75f, 0.85f, 1f, 0.6f), 0.9f);
                        for (int k = 0; k < 3; k++) FleckMaker.ThrowMicroSparks(Vector3.Lerp(s.lastTip, tip, Rand.Value) + Gen.RandomHorizontalVector(0.4f), map);
                        if (s.look == "chain") GlowFx.Fire(tip, map, 0.35f);
                        if (s.look == "flame") { GlowFx.Fire(tip, map, 0.6f); if (Rand.Chance(0.5f)) FleckMaker.ThrowSmoke(tip, map, 0.5f); }
                    }
                    else if (Rand.Chance(0.4f)) FleckMaker.ThrowMicroSparks(tip, map);
                    if (!s.impactFx && frac >= 0.47f)
                    {
                        s.impactFx = true;
                        Impact(tip, map, s.look);
                    }
                }
                s.lastTip = tip; s.hasTip = true;
            }
        }

        /// <summary>The lance driving out: a blue-white streak along the thrust, lightning crackling round the head, a burst at full reach.</summary>
        static void ThrustTick(VehicleTurret t, State s, Map map, float frac)
        {
            Vector3 dir = Vector3.forward.RotatedBy(t.TurretRotation);
            Vector3 tip = Tip(t, 0f) + dir * ThrustFrac(frac) * s.thrustDist;
            tip.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            if (!tip.InBounds(map) || !tip.ShouldSpawnMotesAt(map)) { s.lastTip = tip; s.hasTip = true; return; }
            if (frac < 0.25f && s.hasTip)
            {
                Streak(s.lastTip - dir * 1.2f, tip, map, new Color(0.65f, 0.85f, 1f, 0.75f), 0.8f);
                ShotFx.BoltPublic(s.lastTip - dir * 1.5f, tip, map, 0.6f, 0.45f);
                GlowFx.Lightning(tip, map, 0.9f);
            }
            else if (frac < 0.6f && Rand.Chance(0.5f)) KnightEffects.ElectricFlash(tip, map, 0.45f);    // the head still crackling
            if (!s.thrustFx && frac >= 0.22f)
            {
                s.thrustFx = true;
                KnightEffects.ElectricFlash(tip, map, 1.5f);
                FleckDef sw = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_Shockwave_S");
                if (sw != null) FleckMaker.Static(tip, map, sw, 1f);
                ShotFx.Shake(tip, map, 2.5f);
            }
            s.lastTip = tip; s.hasTip = true;
        }

        static void Streak(Vector3 a, Vector3 b, Map map, Color c, float width)
        {
            FleckDef def = DefDatabase<FleckDef>.GetNamedSilentFail("LIK_ChargeStreak");
            if (def == null) return;
            Vector3 v = b - a;
            float len = v.MagnitudeHorizontal();
            if (len < 0.15f) return;
            FleckCreationData d = FleckMaker.GetDataStatic(a + v * 0.5f, map, def);
            d.exactScale = new Vector3(len + 0.6f, 1f, width);
            d.rotation = Mathf.Atan2(-v.z, v.x) * 57.29578f;
            d.instanceColor = c;
            map.flecks.CreateFleck(d);
        }

        /// <summary>The moment the swing connects: a shower of sparks and torn metal (chain), or a crackling flash (power field).</summary>
        static void Impact(Vector3 pos, Map map, string look)
        {
            FleckDef spark = DefDatabase<FleckDef>.GetNamedSilentFail("SparkFlash");
            if (spark != null) FleckMaker.Static(pos, map, spark, 5f);
            for (int k = 0; k < 10; k++) FleckMaker.ThrowMicroSparks(pos + Gen.RandomHorizontalVector(1.2f), map);
            FleckDef debris = DefDatabase<FleckDef>.GetNamedSilentFail("ThrownDebris");
            if (debris != null && look == "chain")
                for (int k = 0; k < 6; k++)
                {
                    FleckCreationData d = FleckMaker.GetDataStatic(pos + Gen.RandomHorizontalVector(0.5f), map, debris, Rand.Range(0.6f, 1.2f));
                    d.rotation = Rand.Range(0f, 360f); d.rotationRate = Rand.Range(150f, 400f) * (Rand.Bool ? 1f : -1f);
                    d.velocityAngle = Rand.Range(0f, 360f); d.velocitySpeed = Rand.Range(3f, 7f);
                    map.flecks.CreateFleck(d);
                }
            if (look == "electric") KnightEffects.ElectricFlash(pos, map, 1f);
            FleckMaker.ThrowDustPuffThick(pos, map, 1.8f, new Color(0.5f, 0.46f, 0.4f, 0.7f));
            ShotFx.Shake(pos, map, 1.5f);
        }

        // ------------------------------------------------------------------ flame sweep
        /// <summary>Where the next flame shot of this burst lands (set in the FireTurret prefix, used by Projectile_KnightShell.Launch).</summary>
        public static bool pendingSweep;
        public static IntVec3 pendingCell;
        // melee arc strike (user 2026-11-15: 'the strike area should be the entire view of the chainfist'): set just before the arm fires
        public static bool pendingArc;
        public static Vector3 arcPivot;
        public static float arcAim, arcReach, arcHalf;

        public static void PrepareSweep(VehicleTurret t, DefModExtension_ShotFx fx)
        {
            pendingSweep = false;
            if (t?.vehicle == null || !t.targetInfo.IsValid) return;
            Map map = t.vehicle.Map;
            if (map == null) return;
            State s = Get(t);
            int now = Find.TickManager.TicksGame;
            if (now - s.lastShot > 30) { s.shotIdx = 0; s.sweepDir = -s.sweepDir; } else s.shotIdx++;
            s.lastShot = now;
            int n = Mathf.Max(2, fx.sweepShots);
            float f = Mathf.Clamp01(s.shotIdx / (float)(n - 1));
            float lateral = Mathf.Lerp(-fx.sweep, fx.sweep, f) * s.sweepDir;
            Vector3 from = t.TurretLocation.Yto0();
            Vector3 target = t.targetInfo.CenterVector3.Yto0();
            Vector3 dir = target - from;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);
            Vector3 dest = target + side * lateral;
            IntVec3 cell = dest.ToIntVec3();
            if (!cell.InBounds(map)) return;
            pendingSweep = true;
            pendingCell = cell;
            // turn the arm with the stream
            float aim = dir.AngleFlat(), now2 = (dest - from).AngleFlat();
            s.sweepAngle = Mathf.DeltaAngle(aim, now2);
            s.sweepUntil = now + 20;
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    public static class Patch_TurretMotionFire
    {
        static int diag;

        static void Prefix(VehicleTurret __instance)
        {
            TurretMotion.pendingSweep = false;
            ThingDef proj = __instance.def?.projectile;
            if (Prefs.DevMode && diag < 60 && __instance.def != null && __instance.def.defName.StartsWith("LIK_Turret_cerastus_"))
            {
                diag++;
                DefModExtension_MeleeFx d = proj?.GetModExtension<DefModExtension_MeleeFx>();
                Log.Message("[LIK] fire: " + __instance.def.defName + " key=" + __instance.key + " proj=" + (proj?.defName ?? "null") + " class=" + (proj?.thingClass?.Name ?? "?")
                    + " meleeFx=" + (d != null ? "swing " + d.swingArc : "none") + " mode=" + DualMode.Index(__instance)
                    + " target dist=" + (__instance.targetInfo.IsValid ? (__instance.targetInfo.CenterVector3 - __instance.TurretLocation).MagnitudeHorizontal().ToString("F1") : "-"));
            }
            if (proj == null) return;
            DefModExtension_ShotFx fx = proj.GetModExtension<DefModExtension_ShotFx>();
            if (fx != null && fx.sweep > 0f) TurretMotion.PrepareSweep(__instance, fx);
            DefModExtension_MeleeFx mfx = proj.GetModExtension<DefModExtension_MeleeFx>();
            if (mfx != null && mfx.swingArc > 0f) TurretMotion.StartSwing(__instance, mfx.swingArc, mfx.swingTicks, mfx.look);
            if (mfx != null && mfx.thrust > 0f) TurretMotion.StartThrust(__instance, mfx.thrust, mfx.thrustTicks);
            TurretMotion.pendingArc = false;
            if (mfx != null && mfx.arcStrike && __instance.targetInfo.IsValid)
            {
                TurretMotion.pendingArc = true;
                TurretMotion.arcPivot = __instance.TurretLocation.Yto0();
                TurretMotion.arcAim = (__instance.targetInfo.CenterVector3 - __instance.TurretLocation).Yto0().AngleFlat();
                TurretMotion.arcReach = __instance.def.maxRange;
                TurretMotion.arcHalf = mfx.swingArc > 0f ? mfx.swingArc : 60f;
            }
        }

        static void Postfix() { TurretMotion.pendingSweep = false; TurretMotion.pendingArc = false; }
    }

    [HarmonyPatch(typeof(VehicleTurret), "ParallelPreRenderResults")]
    public static class Patch_TurretMotion
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(VehicleTurret __instance, ref PreRenderResults __result)
        {
            if (!__result.draw) return;
            float th = TurretMotion.ThrustOffset(__instance);
            if (th != 0f) __result.position += Vector3.forward.RotatedBy(__instance.TurretRotation) * th;      // the lance driven out along its aim
            float a = TurretMotion.Offset(__instance);
            if (a != 0f)
            {
                __result.quaternion = Quaternion.AngleAxis(a, Vector3.up) * __result.quaternion;
                if (Prefs.DevMode && TurretMotion.diagDraw < 6) { TurretMotion.diagDraw++; Log.Message("[LIK] swing drawn: " + __instance.def.defName + " angle=" + a.ToString("F0")); }
            }
        }
    }

    public class MapComponent_TurretMotion : MapComponent
    {
        public MapComponent_TurretMotion(Map map) : base(map) { }
        public override void MapComponentTick() { if (TurretMotion.active.Count > 0) TurretMotion.Tick(map); KnightEffects.TickGrinds(map); }
    }
}
