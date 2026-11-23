using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using SmashTools.Rendering;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using Verse;

namespace LIK
{
    /// <summary>
    /// Weapon glow (user 2026-11-23). Two parts:
    ///  1. PICTURE GLOW - weapons whose painting has an energy colour (Tools/gen_weapon_glow.py TAGS: volkite green, plasma coils) ship a
    ///     &lt;picture&gt;_glow.png for every picture; it is drawn additively right over the weapon, on whatever picture is showing, so it turns
    ///     and swaps with the weapon. Steady pulse while crewed, brighter for a moment after each shot.
    ///  2. REAL LIGHT - every energy weapon (WeaponGlowData) lights the ground around its Knight through RimWorld's glow grid: 'steady'
    ///     weapons (plasma, volkite, power fields) all the time while crewed, 'flare' weapons (las, conversion, melta, lightning) a short flash
    ///     when they fire. One light per Knight (the glow grid identifies a light by its thing), placed on the Knight and moved with it.
    /// Settings: weapon part glow strength, light on/off (Toll.cs).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WeaponGlow
    {
        public class Box { public int tick = -99999; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, Box> lastFire = new System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, Box>();
        static readonly Dictionary<Texture, Material> glowMats = new Dictionary<Texture, Material>();
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Color");
        public const int FlashTicks = 45;

        public static void Fired(VehicleTurret t)
        {
            if (t?.def == null || !WeaponGlowData.Weapons.TryGetValue(t.def.defName, out WeaponGlowData.Entry e)) return;
            lastFire.GetValue(t, _ => new Box()).tick = Find.TickManager.TicksGame;
            if (t.vehicle != null && t.vehicle.Spawned)
                t.vehicle.Map.GetComponent<MapComponent_KnightLight>()?.Flash(t.vehicle, e.color, MuzzleUtil.World(t).ToIntVec3());   // map light from the muzzle cell
        }

        // 2026-11-23 (user: the light sat on the Knight's centre and jumped tile to tile): the steady light is now a soft pool DRAWN on the
        // ground under each glowing weapon - it moves smoothly with the weapon. The glow grid (tile lighting) is only used for the short firing flash.
        static readonly Material lightMat = MaterialPool.MatFrom("Things/Knights/Effects/weapon_light", ShaderDatabase.MoteGlow);
        const float PoolRadius = 3.2f, FlashPoolRadius = 5.5f;

        /// <summary>Local positions of the mesh corners with uv (0,0) and (1,1) - works for flipped (mirrored) meshes too.</summary>
        static readonly Dictionary<Mesh, (Vector3, Vector3)> corners = new Dictionary<Mesh, (Vector3, Vector3)>();
        static bool Corners(Mesh mesh, out Vector3 a0, out Vector3 a1)
        {
            a0 = a1 = Vector3.zero;
            if (mesh == null) return false;
            if (!corners.TryGetValue(mesh, out var cc))
            {
                Vector3[] vs = mesh.vertices; Vector2[] uvs = mesh.uv;
                Vector3 lo = Vector3.zero, hi = Vector3.zero; bool fl = false, fh = false;
                for (int k = 0; k < vs.Length && k < uvs.Length; k++)
                {
                    if (uvs[k].x < 0.5f && uvs[k].y < 0.5f) { lo = vs[k]; fl = true; }
                    if (uvs[k].x > 0.5f && uvs[k].y > 0.5f) { hi = vs[k]; fh = true; }
                }
                cc = fl && fh ? (lo, hi) : (new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f));
                corners[mesh] = cc;
            }
            a0 = cc.Item1; a1 = cc.Item2;
            return true;
        }

        static void DrawPool(VehicleTurret t, PreRenderResults r, WeaponGlowData.Entry e)
        {
            if (LIKMod.I == null || !LIKMod.I.weaponLight) return;
            float ground = AltitudeLayer.Shadows.AltitudeFor() + 0.01f;   // on the ground, under the Knight's body
            // steady pool: centred on the glowing part of the picture shown (user 2026-11-23: it sat on the turn point), turned/mirrored like it
            if (e.steady && Crewed(t.vehicle))
            {
                float i = 0.32f + 0.05f * Mathf.Sin((Find.TickManager.TicksGame + t.vehicle.thingIDNumber * 37) / 60f * 2.1f);
                Vector3 p = r.position;
                Texture tex = r.material != null ? r.material.mainTexture : null;
                if (tex != null && WeaponGlowData.Centres.TryGetValue(tex.name, out Vector2 uv) && Corners(r.mesh, out Vector3 a0, out Vector3 a1))
                    p += r.quaternion * new Vector3(Mathf.LerpUnclamped(a0.x, a1.x, uv.x), 0f, Mathf.LerpUnclamped(a0.z, a1.z, uv.y));
                Pool(p, ground, PoolRadius, e.color * i);
            }
            // firing flash: at the MUZZLE (the measured barrel tip of the picture shown, Muzzle.cs - where the shot leaves), every energy weapon
            float flash = FlashLeft(t);
            if (flash > 0f) Pool(MuzzleUtil.World(t), ground, Mathf.Lerp(1.5f, FlashPoolRadius, flash), e.color * (0.6f * flash));
        }

        static void Pool(Vector3 p, float y, float rad, Color c)
        {
            c.a = 1f;
            mpb.SetColor(ColorId, c);
            p.y = y;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, Quaternion.identity, new Vector3(rad * 2f, 1f, rad * 2f)), lightMat, 0, null, 0, mpb);
        }

        /// <summary>0..1: how much of the post-shot flash is left.</summary>
        public static float FlashLeft(VehicleTurret t)
        {
            if (!lastFire.TryGetValue(t, out Box b)) return 0f;
            int age = Find.TickManager.TicksGame - b.tick;
            return age < 0 || age >= FlashTicks ? 0f : 1f - age / (float)FlashTicks;
        }

        public static bool Crewed(VehiclePawn v)
        {
            try { return v.AllPawnsAboard.Count > 0; } catch { return true; }
        }

        /// <summary>The glow picture for the weapon picture being drawn (null if it has none). Main thread only (loads textures).</summary>
        static Material GlowMatFor(Material shown)
        {
            Texture tex = shown != null ? shown.mainTexture : null;
            if (tex == null) return null;
            if (glowMats.TryGetValue(tex, out Material m)) return m;
            m = null;
            string n = tex.name;                                             // e.g. turret_questoris_executor_south_L
            string[] parts = n.Split('_');
            if (n.StartsWith("turret_") && parts.Length > 2)
            {
                Texture2D g = ContentFinder<Texture2D>.Get("Things/Knights/Weapons/" + parts[1] + "/" + n + "_glow", false);
                if (g != null) m = MaterialPool.MatFrom(new MaterialRequest(g, ShaderDatabase.MoteGlow));
            }
            glowMats[tex] = m;
            return m;
        }

        static readonly AccessTools.FieldRef<VehicleTurret, PreRenderResults> resultsRef = AccessTools.FieldRefAccess<VehicleTurret, PreRenderResults>("results");

        public static void DrawOver(VehicleTurret t)
        {
            if (LIKMod.I == null || t?.def == null || t.vehicle == null) return;
            if (!WeaponGlowData.Weapons.TryGetValue(t.def.defName, out WeaponGlowData.Entry e)) return;
            PreRenderResults r = resultsRef(t);
            if (!r.draw || r.mesh == null) return;
            DrawPool(t, r, e);
            if (!e.pictureGlow || LIKMod.I.weaponGlow <= 0f) return;
            Material gm = GlowMatFor(r.material);
            if (gm == null) return;
            float time = (Find.TickManager.TicksGame + t.vehicle.thingIDNumber * 37) / 60f;
            float i = Crewed(t.vehicle) ? 0.75f + 0.12f * Mathf.Sin(time * 2.1f) : 0.25f;       // dim when nobody is aboard (reactor idle)
            i += 0.6f * FlashLeft(t);
            i *= LIKMod.I.weaponGlow;
            mpb.SetColor(ColorId, new Color(i, i, i, 1f));
            Vector3 p = r.position; p.y += 0.0002f;                       // just over the weapon, under the body covers drawn above it
            Graphics.DrawMesh(r.mesh, p, r.quaternion, gm, 0, null, 0, mpb);
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), "Draw")]
    public static class Patch_WeaponGlowDraw
    {
        static void Postfix(VehicleTurret __instance) { WeaponGlow.DrawOver(__instance); }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    public static class Patch_WeaponGlowFired
    {
        static void Postfix(VehicleTurret __instance) { WeaponGlow.Fired(__instance); }
    }

    /// <summary>
    /// Firing flash only (2026-11-23): a short real light (glow grid = lights tiles, so pawns and walls nearby are lit) from the WEAPON's cell,
    /// fading out in half a second. The steady glow is the drawn pool in WeaponGlow (smooth, follows the weapon). One light per Knight: the glow
    /// grid identifies a light by its thing, so each Knight gets its own never-spawned stand-in thing placed on the weapon's cell.
    /// </summary>
    public class MapComponent_KnightLight : MapComponent
    {
        class State
        {
            public Thing holder; public CompGlower glower; public bool on; public float radius;
            public int flashTick = -99999; public Color flashCol; public IntVec3 cell;
        }
        readonly Dictionary<VehiclePawn, State> states = new Dictionary<VehiclePawn, State>();
        const int FlashTicks = 30;
        static readonly List<VehiclePawn> tmp = new List<VehiclePawn>();

        public MapComponent_KnightLight(Map map) : base(map) { }

        public void Flash(VehiclePawn v, Color c, IntVec3 cell)
        {
            if (LIKMod.I == null || !LIKMod.I.weaponLight || !cell.InBounds(map)) return;
            State s = Get(v);
            s.flashTick = Find.TickManager.TicksGame; s.flashCol = c;
            if (s.on && s.cell != cell) Off(s);                            // a new shot from another weapon / cell: move the light there
            s.cell = cell;
            Refresh(s);                                                    // light up on the shot itself
        }

        State Get(VehiclePawn v)
        {
            if (!states.TryGetValue(v, out State s))
            {
                Thing holder = ThingMaker.MakeThing(ThingDefOf.Steel);       // never spawned: only gives the light a position and its own id
                var g = new CompGlower { parent = holder as ThingWithComps };
                g.Initialize(new CompProperties_Glower { glowRadius = 0f, glowColor = new ColorInt(0, 0, 0, 0) });
                states[v] = s = new State { holder = holder, glower = g };
            }
            return s;
        }

        public override void MapComponentTick()
        {
            if (states.Count == 0 || Find.TickManager.TicksGame % 5 != 0) return;
            bool enabled = LIKMod.I == null || LIKMod.I.weaponLight;
            tmp.Clear();
            foreach (VehiclePawn v in states.Keys) if (!enabled || !v.Spawned || v.Map != map) tmp.Add(v);
            foreach (VehiclePawn v in tmp) { Off(states[v]); states.Remove(v); }
            foreach (State s in states.Values) if (s.on) Refresh(s);
        }

        void Refresh(State s)
        {
            int age = Find.TickManager.TicksGame - s.flashTick;
            float f = age >= 0 && age < FlashTicks ? 1f - age / (float)FlashTicks : 0f;
            float radius = 7f * f;
            if (radius < 1f) { Off(s); return; }
            if (s.on && Mathf.Abs(s.radius - radius) < 0.9f) return;
            if (s.on) map.glowGrid.DeRegisterGlower(s.glower);
            Color col = s.flashCol;
            CompProperties_Glower pr = s.glower.Props;
            pr.glowRadius = radius;
            pr.glowColor = new ColorInt(Mathf.RoundToInt(col.r * 230f), Mathf.RoundToInt(col.g * 230f), Mathf.RoundToInt(col.b * 230f), 0);
            s.holder.Position = s.cell;
            map.glowGrid.RegisterGlower(s.glower);
            s.on = true; s.radius = radius;
        }

        void Off(State s)
        {
            if (!s.on) return;
            map.glowGrid.DeRegisterGlower(s.glower);
            s.on = false;
        }
    }
}
