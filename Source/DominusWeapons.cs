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
    /// Dominus weapon pass (user 2026-11-16: "make their weapons accurate"). Only what the sources give (10th ed. datasheets + Codex lore):
    ///  - plasma decimator  : Supercharge fire mode (more damage + AP) that is Hazardous - 1 volley in 6 vents plasma over the Knight.
    ///  - volcano lance     : Titan Hunter - extra damage on war machines, buildings and monsters (DefModExtension_ShotFx.antiArmour on shells).
    ///  - conflagration cann: Torrent + Ignores Cover (fire mode canMiss false + ShotFx.ignoreCover: nothing in the line stops the flames).
    ///  - thundercoil harpoon: Thundershock - arcing energy to the target and each enemy within 6 cells (50 %, D3 mortal wounds); the spear is on
    ///                        a chain to its generator and is reeled back after every shot (Codex: "fired, then reeled back in again and again").
    ///  - shieldbreaker     : Anti-Titanic + its name - shields (Knight void / ion shields, shield belts, shield bubbles) take x3.
    ///  - twin meltagun     : Melta - x1.5 damage within half range (Projectile_KnightShell.DamageAmount).
    /// </summary>
    public static class DominusWeapons
    {
        public const string DecimatorDef = "LIK_Turret_dominus_decimator";
        public const float HazardChance = 1f / 6f, HazardDamage = 110f, SuperchargeAP = 1.33f;
        public static bool pendingSupercharge;           // set while a supercharged decimator turret is firing (its shot reads it in Launch)

        class Volley { public int last = -9999; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, Volley> volleys = new System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, Volley>();

        public static bool Supercharged(VehicleTurret t)
        {
            // any turret with a 'Supercharge' fire mode (decimator; Questoris plasma executor 2026-11-23)
            return t?.def != null && t.CurrentFireMode != null && t.CurrentFireMode.label != null
                   && t.CurrentFireMode.label.StartsWith("Supercharge");
        }

        /// <summary>Hazardous: rolled once per volley (the first shot after a pause), after which the volley still fires.</summary>
        public static void HazardCheck(VehicleTurret t)
        {
            VehiclePawn v = t.vehicle;
            if (v == null || !v.Spawned) return;
            int now = Find.TickManager.TicksGame;
            Volley vo = volleys.GetValue(t, _ => new Volley());
            bool first = now - vo.last > 30 || now < vo.last;
            vo.last = now;
            if (!first || !Rand.Chance(HazardChance)) return;
            Map map = v.Map;
            Vector3 m = MuzzleUtil.World(t);
            v.TakeDamage(new DamageInfo(DamageDefOf.Burn, HazardDamage, 2f, -1f, v));
            if (m.ShouldSpawnMotesAt(map))
            {
                GlowFx.Fire(m, map, 2.2f);
                for (int i = 0; i < 8; i++) FleckMaker.ThrowMicroSparks(m + Gen.RandomHorizontalVector(1.2f), map);
                FleckMaker.ThrowSmoke(m, map, 2.5f);
                GlowFx.Lightning(m, map, 2.5f);
            }
            if (v.Faction == Faction.OfPlayer)
                Messages.Message(v.LabelShort + ": the supercharged " + t.def.label + " vented its overload - plasma washed over the Knight!", v, MessageTypeDefOf.NegativeEvent, false);
        }

        // ------------------------------------------------------------------ big targets
        /// <summary>War machines, buildings and monsters (Titan Hunter: VEHICLE or MONSTER; Anti-Titanic).</summary>
        public static bool BigTarget(Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (t is VehiclePawn || t is Building) return !(t is Building b && b.def.building != null && b.def.building.isNaturalRock);
            Pawn p = t as Pawn;
            return p != null && !p.Dead && ((p.RaceProps.IsMechanoid && p.BodySize >= 2f) || p.BodySize >= 2.5f);
        }

        /// <summary>Extra damage on the war machine / building / monster the shell hit (the target, or what stands in the impact cell).</summary>
        public static void AntiArmour(Map map, IntVec3 cell, float radius, Thing target, float mult, int dmg, float ap, Thing who, ThingDef weapon, DamageDef dd)
        {
            if (map == null || mult <= 1f || !cell.InBounds(map)) return;
            var hit = new HashSet<Thing>();
            if (target != null && target.Spawned && target.Map == map && target.OccupiedRect().ExpandedBy(Mathf.CeilToInt(radius)).Contains(cell)) hit.Add(target);
            foreach (Thing t in cell.GetThingList(map)) hit.Add(t);
            foreach (Thing t in hit)
            {
                if (t == who || !BigTarget(t)) continue;
                t.TakeDamage(new DamageInfo(dd ?? DamageDefOf.Bomb, dmg * (mult - 1f), ap, -1f, who, null, weapon));
            }
        }

        // ------------------------------------------------------------------ shieldbreaker
        static readonly AccessTools.FieldRef<CompShield, float> beltEnergy = AccessTools.FieldRefAccess<CompShield, float>("energy");
        static readonly System.Reflection.MethodInfo beltBreak = AccessTools.Method(typeof(CompShield), "Break");

        /// <summary>The shield part of the blast: the shields around the impact lose `extra` more (x3 overall with the normal hit).</summary>
        public static void ShieldBreak(Map map, IntVec3 cell, float radius, float extra, Thing who)
        {
            if (map == null || extra <= 0f || !cell.InBounds(map)) return;
            Vector3 at = cell.ToVector3Shifted();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToArray())
            {
                if (p == who || p.Dead) continue;
                VehiclePawn v = p as VehiclePawn;
                if (v != null)
                {
                    // Knight shields reach beyond the hull: anything whose shield the blast touches
                    CompKnightHead c = v.GetComp<CompKnightHead>();
                    if (c == null) continue;
                    float reach = Mathf.Max(v.def.size.x, v.def.size.z) * 0.5f + 1.6f + radius;
                    if ((v.DrawPos - at).MagnitudeHorizontal() > reach) continue;
                    if (c.voidShield) c.Absorb(extra);
                    if (IonShield.Fitted(v)) IonShield.Intercept(v, c, new DamageInfo(DamageDefOf.Bomb, extra, 0f, -1f, who));
                    continue;
                }
                if (p.Position.DistanceTo(cell) > radius + 0.5f || p.apparel == null) continue;
                foreach (Apparel a in p.apparel.WornApparel)
                {
                    CompShield s = a.TryGetComp<CompShield>();
                    if (s == null || beltEnergy(s) <= 0f) continue;
                    beltEnergy(s) -= extra * 0.033f;                      // vanilla: 0.033 energy per point of damage
                    if (beltEnergy(s) <= 0f) beltBreak?.Invoke(s, null);
                }
            }
        }

        // ------------------------------------------------------------------ thundershock
        public static void Thundershock(Map map, IntVec3 cell, Thing who, DefModExtension_ShotFx fx, ThingDef weapon)
        {
            if (map == null || !cell.InBounds(map)) return;
            Vector3 from = cell.ToVector3Shifted();
            int n = 0;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToArray())
            {
                if (n >= 12) break;
                if (p == who || p.Dead || p.Position.DistanceTo(cell) > fx.arcRadius) continue;
                if (who != null && who.Faction != null ? !p.HostileTo(who.Faction) : p.Faction == null) continue;
                if (!Rand.Chance(fx.arcChance)) continue;
                n++;
                if (p.DrawPos.ShouldSpawnMotesAt(map))
                {
                    ShotFx.BoltPublic(from, p.DrawPos, map, 0.6f, 0.5f);
                    KnightEffects.ElectricFlash(p.DrawPos, map, 0.8f);
                }
                float dmg = Rand.Range(fx.arcDamageMin, fx.arcDamageMax);
                p.TakeDamage(new DamageInfo(DamageDefOf.Burn, dmg, 99f, -1f, who, null, weapon));    // mortal wounds: no armour save
            }
            if (from.ShouldSpawnMotesAt(map)) { KnightEffects.ElectricFlash(from, map, 1.4f); GlowFx.Lightning(from, map, 2.5f); }
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    public static class Patch_DecimatorSupercharge
    {
        static void Prefix(VehicleTurret __instance)
        {
            DominusWeapons.pendingSupercharge = false;
            try
            {
                if (!DominusWeapons.Supercharged(__instance)) return;
                DominusWeapons.pendingSupercharge = true;
                DominusWeapons.HazardCheck(__instance);
            }
            catch (System.Exception e) { Log.ErrorOnce("[LIK] decimator supercharge: " + e, 77161116); }
        }

        static void Finalizer() { DominusWeapons.pendingSupercharge = false; }
    }

    /// <summary>Shield bubbles (mech / vanilla low shields): a shieldbreaker caught by one tears x3 out of it.</summary>
    [HarmonyPatch(typeof(CompProjectileInterceptor), nameof(CompProjectileInterceptor.CheckIntercept))]
    public static class Patch_ShieldbreakerBubble
    {
        static readonly AccessTools.FieldRef<CompProjectileInterceptor, int> hp = AccessTools.FieldRefAccess<CompProjectileInterceptor, int>("currentHitPoints");
        static readonly System.Reflection.MethodInfo breakHp = AccessTools.Method(typeof(CompProjectileInterceptor), "BreakShieldHitpoints");

        static void Postfix(CompProjectileInterceptor __instance, Projectile projectile, bool __result)
        {
            if (!__result || projectile == null) return;
            DefModExtension_ShotFx fx = projectile.def.GetModExtension<DefModExtension_ShotFx>();
            if (fx == null || fx.shieldMult <= 1f) return;
            try
            {
                if (hp(__instance) <= 0) return;
                hp(__instance) -= Mathf.RoundToInt(projectile.DamageAmount * (fx.shieldMult - 1f));
                if (hp(__instance) <= 0) { hp(__instance) = 0; breakHp?.Invoke(__instance, new object[] { new DamageInfo(DamageDefOf.Bomb, projectile.DamageAmount, 0f, -1f, projectile.Launcher) }); }
            }
            catch (System.Exception e) { Log.ErrorOnce("[LIK] shieldbreaker vs shield bubble: " + e, 77161117); }
        }
    }

    /// <summary>
    /// Thundercoil harpoon chain (visual): while the spear flies a chain runs from the arm to it; after the hit the spear is reeled back along it.
    /// Drawn every frame, follows the Knight. Not saved (it lasts about a second).
    /// </summary>
    [StaticConstructorOnStartup]
    public class MapComponent_HarpoonTether : MapComponent
    {
        const int HoldTicks = 12, ReelTicks = 40;
        const float LinkLen = 0.55f;
        class Tether { public Projectile shot; public Thing launcher; public Vector3 offset, tip, dir; public int impact = -1; public Material head; public float headSize; }
        readonly List<Tether> list = new List<Tether>();
        static Material chain;

        public MapComponent_HarpoonTether(Map map) : base(map) { }

        public static void Add(Projectile shot, Vector3 origin)
        {
            MapComponent_HarpoonTether c = shot.Map?.GetComponent<MapComponent_HarpoonTether>();
            if (c == null) return;
            Thing l = shot.Launcher;
            var t = new Tether { shot = shot, launcher = l, offset = l != null ? origin - l.DrawPos : origin, tip = origin, dir = Vector3.forward };
            try { t.head = shot.def.graphicData?.Graphic?.MatSingle; t.headSize = shot.def.graphicData != null ? shot.def.graphicData.drawSize.x : 2f; } catch { }
            c.list.Add(t);
        }

        public static void Impact(Projectile shot, Vector3 at)
        {
            MapComponent_HarpoonTether c = shot.Map?.GetComponent<MapComponent_HarpoonTether>();
            if (c == null) return;
            foreach (Tether t in c.list)
                if (t.shot == shot && t.impact < 0) { t.tip = at; t.impact = Find.TickManager.TicksGame; }
        }

        Vector3 Anchor(Tether t) { return t.launcher != null && t.launcher.Spawned && t.launcher.Map == map ? t.launcher.DrawPos + t.offset : t.offset; }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Tether t = list[i];
                if (t.impact < 0 && (t.shot == null || t.shot.Destroyed)) { t.impact = now; }      // gone without a hit: reel in from where it was
                if (t.impact >= 0 && now - t.impact > HoldTicks + ReelTicks) { list.RemoveAt(i); continue; }
                // the generator's charge crawling along the chain
                if (now % 5 == i % 5)
                {
                    Vector3 a = Anchor(t), b = Tip(t, now);
                    Vector3 q = Vector3.Lerp(a, b, Rand.Value);
                    if (q.ShouldSpawnMotesAt(map)) FleckMaker.ThrowMicroSparks(q, map);
                }
            }
        }

        Vector3 Tip(Tether t, int now)
        {
            if (t.impact < 0)
            {
                if (t.shot != null && t.shot.Spawned) t.tip = t.shot.ExactPosition;
                return t.tip;
            }
            float f = Mathf.Clamp01((now - t.impact - HoldTicks) / (float)ReelTicks);
            f = f * f * (3f - 2f * f);
            return Vector3.Lerp(t.tip, Anchor(t), f);
        }

        public override void MapComponentUpdate()
        {
            if (list.Count == 0 || Find.CurrentMap != map) return;
            if (chain == null) chain = MaterialPool.MatFrom("Things/Knights/Effects/harpoon_chain", ShaderDatabase.Transparent);
            int now = Find.TickManager.TicksGame;
            float y = AltitudeLayer.Projectile.AltitudeFor() - 0.02f;
            foreach (Tether t in list)
            {
                Vector3 a = Anchor(t), b = Tip(t, now);
                a.y = y; b.y = y;
                Vector3 d = b - a;
                float len = d.MagnitudeHorizontal();
                if (len < 0.05f) continue;
                Vector3 dir = d / len;
                Quaternion rot = Quaternion.LookRotation(dir);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / LinkLen));
                float step = len / n;
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = a + dir * (step * (k + 0.5f));
                    // alternate links: one seen flat (open oval), the next edge-on (a thin bar)
                    Vector3 s = k % 2 == 0 ? new Vector3(0.42f, 1f, step * 1.25f) : new Vector3(0.16f, 1f, step * 1.25f);
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, rot, s), chain, 0);
                }
                // reeling back: the spear itself comes home along the chain, point still outward
                if (t.impact >= 0 && t.head != null && now - t.impact >= 1)
                {
                    Vector3 h = b; h.y = y + 0.01f;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(h, rot, new Vector3(t.headSize, 1f, t.headSize)), t.head, 0);
                }
            }
        }
    }
}
