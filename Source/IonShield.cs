using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// Cerastus Knight-Lancer ION GAUNTLET SHIELD (2026-11-15). Sources: defence only - HH2 5+ save vs shooting at front/sides + all melee,
    /// HH3 melee; "more concentrated than the ion shield of other knight armours, but lacks its tactical flexibility". No attack.
    ///  - energy pool (Capacity). Shots and blasts from the FRONT HALF (front + sides, 180 deg) are absorbed in full while it has energy;
    ///    from behind they get through ("lacks flexibility"). Melee from any side is halved (the absorbed half drains the energy).
    ///  - empty: collapses for 10 s, then restarts. Recharges once the Knight has not been hit for 5 s (empty -> full in ~15 s).
    ///  - looks: faint blue half-dome in combat, flare + sparks where a shot meets it (big hits light the whole arc), crackle on melee,
    ///    flicker when low, shatter on collapse, soft pulse on restart. Energy bar gizmo.
    /// </summary>
    public static class IonShield
    {
        public const float Capacity = 800f;
        const int CollapseTicks = 600, RechargeDelay = 300;
        const float RechargePer30 = Capacity / 30f;          // 30 steps of 30 ticks = 15 s
        public const string TurretDef = "LIK_Turret_cerastus_iongauntlet";

        static FleckDef F(string n) { return DefDatabase<FleckDef>.GetNamedSilentFail(n); }

        public static bool Fitted(VehiclePawn v)
        {
            var comp = v?.CompVehicleTurrets;
            if (comp == null) return false;
            foreach (VehicleTurret t in comp.Turrets) if (t.def != null && t.def.defName == TurretDef) return true;
            return false;
        }

        public static float Charge(CompKnightHead c) { if (c.ionCharge < 0f) c.ionCharge = Capacity; return c.ionCharge; }
        public static bool Down(CompKnightHead c) { return Find.TickManager.TicksGame < c.ionDownUntil; }

        public static float FacingAngle(VehiclePawn v) { return v.FullRotation.AsAngle; }

        /// <summary>Damage arriving at the Knight: how much the shield takes (and the look of it).</summary>
        public static float Intercept(VehiclePawn v, CompKnightHead c, DamageInfo dinfo)
        {
            if (dinfo.Amount <= 0f || Down(c) || !v.Spawned) return 0f;
            float charge = Charge(c);
            if (charge <= 0f) return 0f;
            Thing inst = dinfo.Instigator;
            Vector3 center = v.DrawPos;
            // where it came from
            float fromAngle;
            if (inst != null && inst.Spawned && inst.Map == v.Map && inst != v) fromAngle = (inst.DrawPos - center).Yto0().AngleFlat();
            else fromAngle = dinfo.Angle + 180f;
            bool melee = dinfo.Def != null && !dinfo.Def.isRanged && !dinfo.Def.isExplosive && inst != null && inst.Spawned
                         && v.OccupiedRect().ExpandedBy(3).Contains(inst.Position);
            float want;
            if (melee) want = dinfo.Amount * 0.5f;
            else if (Mathf.Abs(Mathf.DeltaAngle(FacingAngle(v), fromAngle)) <= 90f) want = dinfo.Amount;
            else return 0f;                                                          // from behind: the gauntlet can't cover it
            float soaked = Mathf.Min(want, charge);
            c.ionCharge = charge - soaked;
            c.lastIonHit = Find.TickManager.TicksGame;
            BlockFx(v, fromAngle, soaked, melee);
            if (c.ionCharge <= 0.01f) Collapse(v, c);
            return soaked;
        }

        public static float Radius(VehiclePawn v) { return Mathf.Max(v.def.size.x, v.def.size.z) * 0.5f + 1.6f; }

        static void BlockFx(VehiclePawn v, float fromAngle, float amount, bool melee)
        {
            Map map = v.Map;
            Vector3 c = v.DrawPos;
            Vector3 hit = c + Vector3.forward.RotatedBy(fromAngle) * Radius(v);
            hit.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            if (!hit.InBounds(map) || !hit.ShouldSpawnMotesAt(map)) return;
            // the shield is invisible until struck: a patch of blue hexagons lights up where the shot meets it (drawn on the Knight, so it follows it)
            IonShieldDraw.Hit(v, fromAngle - FacingAngle(v), 2.4f + Mathf.Min(3f, amount / 20f));
            // sparks glancing off sideways along the dome
            for (int k = 0; k < 4; k++)
            {
                Vector3 q = c + Vector3.forward.RotatedBy(fromAngle + Rand.Range(-25f, 25f)) * Radius(v);
                if (q.InBounds(map)) FleckMaker.ThrowMicroSparks(q, map);
            }
            if (melee) KnightEffects.ElectricFlash(hit, map, 0.9f);
            // big hits light up the whole front of the shield for a moment
            if (amount >= 40f) IonShieldDraw.Dome(v, 0.9f);
        }

        /// <summary>The half-dome in front of the Knight.</summary>
        public static void Arc(VehiclePawn v, float alpha, string fleck = "LIK_IonArc")
        {
            FleckDef d = F(fleck);
            if (d == null || !v.Spawned) return;
            Vector3 c = v.DrawPos; c.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            if (!c.ShouldSpawnMotesAt(v.Map)) return;
            FleckCreationData data = FleckMaker.GetDataStatic(c, v.Map, d, Radius(v) * 2f / 0.9f);
            data.rotation = FacingAngle(v);
            data.instanceColor = new Color(1f, 1f, 1f, alpha);
            v.Map.flecks.CreateFleck(data);
        }

        static void Collapse(VehiclePawn v, CompKnightHead c)
        {
            c.ionCharge = 0f;
            c.ionDownUntil = Find.TickManager.TicksGame + CollapseTicks;
            Map map = v.Map;
            Vector3 center = v.DrawPos;
            float face = FacingAngle(v), r = Radius(v);
            // the arc shatters into sparks, the emitter crackles and smokes
            for (int i = 0; i <= 12; i++)
            {
                Vector3 q = center + Vector3.forward.RotatedBy(face + Mathf.Lerp(-90f, 90f, i / 12f)) * r;
                if (!q.InBounds(map)) continue;
                FleckMaker.ThrowMicroSparks(q, map);
                if (i % 3 == 0) KnightEffects.ElectricFlash(q, map, 0.6f);
            }
            VehicleTurret g = v.CompVehicleTurrets?.Turrets.FirstOrDefault(t => t.def?.defName == TurretDef);
            if (g != null) { Vector3 e = g.TurretLocation; KnightEffects.ElectricFlash(e, map, 1f); FleckMaker.ThrowSmoke(e, map, 1.4f); }
            DefDatabase<SoundDef>.GetNamedSilentFail("EnergyShield_Broken")?.PlayOneShot(new TargetInfo(v.Position, map));
            if (v.Faction == Faction.OfPlayer) Messages.Message("The ion gauntlet shield of " + v.LabelShort + " collapses!", v, MessageTypeDefOf.NegativeEvent, false);
        }

        /// <summary>Every 30 ticks: restart after a collapse, recharge, and the combat shimmer.</summary>
        public static void Tick(VehiclePawn v, CompKnightHead c)
        {
            int now = Find.TickManager.TicksGame;
            float charge = Charge(c);
            if (c.ionDownUntil > 0 && now >= c.ionDownUntil)
            {
                c.ionDownUntil = 0;
                c.ionCharge = Capacity * 0.25f;                                       // restarts weak, then recharges
                DefDatabase<SoundDef>.GetNamedSilentFail("EnergyShield_Reset")?.PlayOneShot(new TargetInfo(v.Position, v.Map));
                IonShieldDraw.Dome(v, 0.8f);
                return;
            }
            if (Down(c)) return;
            if (charge < Capacity && now - c.lastIonHit >= RechargeDelay) c.ionCharge = Mathf.Min(Capacity, charge + RechargePer30);
        }

        static bool EnemiesNear(VehiclePawn v)
        {
            if (v.Faction == null || v.Map == null) return false;
            foreach (IAttackTarget t in v.Map.attackTargetsCache.TargetsHostileToFaction(v.Faction))
                if (t.Thing != null && t.Thing.Spawned && (t.Thing.Position - v.Position).LengthHorizontalSquared < 40 * 40 && !t.ThreatDisabled(null)) return true;
            return false;
        }
    }

    /// <summary>Damage to a Knight with the ion gauntlet shield fitted: the shield takes its share first (front arc / melee half).</summary>
    // 2026-11-26 fix: was on VehicleComponent.TakeDamage, which Combat Extended never calls - the gauntlet shield did nothing under CE.
    // Now on VehicleStatHandler.ApplyDamage (every hit on a vehicle), right after the torso ion shield.
    [HarmonyPatch(typeof(VehicleStatHandler), nameof(VehicleStatHandler.ApplyDamage))]
    public static class Patch_IonShield
    {
        static readonly AccessTools.FieldRef<VehicleStatHandler, VehiclePawn> vehicleRef = AccessTools.FieldRefAccess<VehicleStatHandler, VehiclePawn>("vehicle");

        [HarmonyPriority(Priority.High)]
        static void Prefix(VehicleStatHandler __instance, ref DamageInfo __0)
        {
            VehiclePawn v;
            try { v = vehicleRef(__instance); } catch { return; }
            if (v == null || __0.Amount <= 0f || !IonShield.Fitted(v)) return;
            CompKnightHead c = v.GetComp<CompKnightHead>();
            if (c == null) return;
            float soaked = IonShield.Intercept(v, c, __0);
            if (soaked > 0f) __0.SetAmount(Mathf.Max(0f, __0.Amount - soaked));
        }
    }

    /// <summary>The gauntlet is a shield, not a weapon: it never fires (it still turns toward the enemy like an arm).</summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    public static class Patch_IonGauntletNoFire
    {
        [HarmonyPriority(Priority.First)]
        // also the Questoris void shield generator on the top mount (2026-11-26): it is a shield, not a weapon
        static bool Prefix(VehicleTurret __instance) { return __instance.def == null || (__instance.def.defName != IonShield.TurretDef && __instance.def.defName != "LIK_Turret_questoris_voidshield"); }
    }

    /// <summary>Energy bar on the Knight.</summary>
    [StaticConstructorOnStartup]
    public class Gizmo_IonShield : Gizmo
    {
        public CompKnightHead comp;
        static readonly Texture2D Full = SolidColorMaterials.NewSolidColorTexture(new Color(0.35f, 0.65f, 1f));
        static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(Color.clear);

        public Gizmo_IonShield() { Order = -95f; }
        public override float GetWidth(float maxWidth) { return 140f; }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect r = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(r);
            Rect inner = r.ContractedBy(6f);
            bool down = IonShield.Down(comp);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 20f), down ? "Ion shield: RESTARTING" : "Ion gauntlet shield");
            Rect bar = new Rect(inner.x, inner.y + 24f, inner.width, 24f);
            float frac = down ? 0f : IonShield.Charge(comp) / IonShield.Capacity;
            Widgets.FillableBar(bar, frac, Full, Empty, false);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, down ? "-" : Mathf.RoundToInt(IonShield.Charge(comp)) + " / " + Mathf.RoundToInt(IonShield.Capacity));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, "Ion gauntlet shield: absorbs shots and blasts from the Knight's front and sides in full, and half of every melee blow. Shots from behind get through.\nCollapses when drained (10 s to restart). Recharges once the Knight has not been hit for 5 s.");
            return new GizmoResult(GizmoState.Clear);
        }
    }

    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_IonShieldGizmo
    {
        static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer || !IonShield.Fitted(__instance)) return;
            CompKnightHead c = __instance.GetComp<CompKnightHead>();
            if (c != null) __result = __result.Concat(new Gizmo[] { new Gizmo_IonShield { comp = c } });
        }
    }

    /// <summary>
    /// The shield's pictures, drawn every frame ON the Knight (they follow it as it walks and turns; flecks stayed behind and faded in and
    /// out, user 2026-11-15). Invisible until struck: each hit lights a patch of blue hexagons at its place on the shield, squashed along
    /// the shield surface, blooming then fading in about half a second; big hits / a restart light the whole hexagonal front dome.
    /// Not saved (purely visual).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class IonShieldDraw
    {
        class Glow { public VehiclePawn v; public float rel, size; public bool dome; public float start, life, alpha; }
        static readonly List<Glow> glows = new List<Glow>();
        static readonly Material HitMat = MaterialPool.MatFrom("Things/Knights/Effects/ion_hit", ShaderDatabase.MoteGlow);
        static readonly Material DomeMat = MaterialPool.MatFrom("Things/Knights/Effects/ion_arc", ShaderDatabase.MoteGlow);
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        static float Now => Time.realtimeSinceStartup;

        public static void Hit(VehiclePawn v, float relAngle, float size)
        {
            if (glows.Count > 40) glows.RemoveAt(0);
            glows.Add(new Glow { v = v, rel = relAngle, size = size, start = Now, life = 0.55f, alpha = 1f });
        }

        public static void Dome(VehiclePawn v, float alpha)
        {
            glows.Add(new Glow { v = v, dome = true, start = Now, life = 0.5f, alpha = alpha });
        }

        /// <summary>Every frame (MapComponent_IonShieldDraw.MapComponentUpdate).</summary>
        public static void Draw(Map map)
        {
            if (glows.Count == 0) return;
            float now = Now;
            // paused game: the flares hold still instead of vanishing
            bool paused = Find.TickManager != null && Find.TickManager.Paused;
            for (int i = glows.Count - 1; i >= 0; i--)
            {
                Glow g = glows[i];
                if (g.v == null || !g.v.Spawned || g.v.Destroyed) { glows.RemoveAt(i); continue; }
                if (paused) g.start += Time.deltaTime;
                float t = (now - g.start) / g.life;
                if (t >= 1f) { glows.RemoveAt(i); continue; }
                if (g.v.Map != map) continue;
                float a = g.alpha * (t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f);
                Vector3 c = g.v.DrawPos; c.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                float face = IonShield.FacingAngle(g.v), r = IonShield.Radius(g.v);
                mpb.SetColor(ShaderPropertyIDs.Color, new Color(a, a, a, a));      // glow shader adds light: fade the colour too
                if (g.dome)
                {
                    float d = r * 2f / 0.9f;
                    Matrix4x4 m = Matrix4x4.TRS(c, Quaternion.AngleAxis(face, Vector3.up), new Vector3(d, 1f, d));
                    Graphics.DrawMesh(MeshPool.plane10, m, DomeMat, 0, null, 0, mpb);
                }
                else
                {
                    float ang = face + g.rel;
                    Vector3 p = c + Vector3.forward.RotatedBy(ang) * r;
                    float grow = 0.75f + 0.25f * Mathf.Min(1f, t / 0.3f);
                    // squashed across the radial direction: the patch lies along the curved shield surface
                    Matrix4x4 m = Matrix4x4.TRS(p, Quaternion.AngleAxis(ang, Vector3.up), new Vector3(g.size * grow, 1f, g.size * grow * 0.55f));
                    Graphics.DrawMesh(MeshPool.plane10, m, HitMat, 0, null, 0, mpb);
                }
            }
        }
    }

    public class MapComponent_IonShieldDraw : MapComponent
    {
        public MapComponent_IonShieldDraw(Map map) : base(map) { }
        public override void MapComponentUpdate() { if (Find.CurrentMap == map) IonShieldDraw.Draw(map); }
    }
}
