using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SmashTools;
using SmashTools.Rendering;
using UnityEngine;
using Vehicles;
using Vehicles.Rendering;
using Verse;

namespace LIK
{
    /// <summary>
    /// Optional per-facing turret pictures. Vehicle Framework draws a turret with ONE picture for every facing; here, for any LIK weapon turret
    /// whose picture is  X.png  you may also supply  X_north.png / X_east.png / X_south.png / X_west.png  (same canvas size). When the Knight faces
    /// that way the matching picture is drawn; any facing without its own file keeps using X.png.
    /// Materials are built once at startup (the game renders turrets on worker threads, where loading is not allowed).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class FacingArt
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        // paintable pictures (a colour mask <picture>m.png next to them, Tools/paint_masks.py): key -> picture, mask, class default colours
        struct PaintSrc { public Texture2D main, mask; public long defKey; }
        static readonly Dictionary<string, PaintSrc> paintSrc = new Dictionary<string, PaintSrc>();
        static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, long), Material> painted = new System.Collections.Concurrent.ConcurrentDictionary<(string, long), Material>();
        static readonly System.Collections.Concurrent.ConcurrentQueue<(string key, Color one, Color two, Color three)> paintQueue = new System.Collections.Concurrent.ConcurrentQueue<(string key, Color one, Color two, Color three)>();
        static readonly string[] sides = { "north", "east", "south", "west" };

        static readonly HashSet<string> occKeys = new HashSet<string>();          // "<weapon>|<mount>" with depth occluders
        static readonly Dictionary<string, string> altBase = new Dictionary<string, string>();   // alternate-mode weapon -> its main weapon

        /// <summary>Does this turret's weapon (or the main weapon of its mode) have depth occluders on this mount?</summary>
        public static bool HasOccluder(VehicleTurret t)
        {
            string d = t.def.defName.StartsWith("LIK_Turret_") ? t.def.defName.Substring("LIK_Turret_".Length) : t.def.defName;
            if (altBase.TryGetValue(d, out string b)) d = b;
            string mount = (t.key ?? "").Split('_')[0];
            return occKeys.Contains(d + "|" + mount);
        }

        static FacingArt()
        {
            foreach (KnightClassDef kc in DefDatabase<KnightClassDef>.AllDefsListForReading)
            {
                if (kc.backpacks != null) foreach (KnightBackpack b in kc.backpacks) if (b.mount != null) occKeys.Add(b.weapon + "|" + b.mount);
                foreach (KnightMount m in kc.mounts)
                    foreach (WeaponOption op in m.options)
                        if (op.altTurretKeys != null)
                            foreach (string a in op.altTurretKeys)
                            {
                                int i1 = a.IndexOf('_'), i2 = op.turretKey.IndexOf('_');
                                if (i1 > 0 && i2 > 0) altBase[a.Substring(i1 + 1)] = op.turretKey.Substring(i2 + 1);
                            }
            }
            foreach (KnightClassDef kc in DefDatabase<KnightClassDef>.AllDefsListForReading)
            {
                if (kc.backpacks != null && kc.backpacks.Any(b => b.mount != null)) OccluderClasses.Add(kc.defName.Substring("LIK_Class_".Length).ToLowerInvariant());
                if (kc.hull != null) HullClasses.Add(kc.defName.Substring("LIK_Class_".Length).ToLowerInvariant());
            }
            int n = 0;
            foreach (VehicleTurretDef def in DefDatabase<VehicleTurretDef>.AllDefsListForReading)
            {
                if (!def.defName.StartsWith("LIK_Turret_") || def.graphicData == null) continue;
                string path = def.graphicData.texPath;
                // AIM pictures (user 2026-11-15, Dominus top turrets): X_aim00 .. X_aim15 = the weapon pointing at screen angle k*22.5 (0 = up,
                // clockwise), rendered from the 3D model; X_aimNN_L = the mirrored turret. Swivel point = picture centre.
                // 2026-11-15: 32 pictures (every 11.25 deg) when X_aim31 exists, else 16
                if (ContentFinder<Texture2D>.Get(path + "_aim00", false) != null)
                {
                    int AimSteps = ContentFinder<Texture2D>.Get(path + "_aim31", false) != null ? 32 : 16;
                    bool all = true;
                    for (int k = 0; k < AimSteps && all; k++) all = ContentFinder<Texture2D>.Get(path + "_" + AimName(k), false) != null;
                    if (all)
                    {
                        for (int k = 0; k < AimSteps; k++)
                            foreach (bool l in new[] { false, true })
                            {
                                string p = path + "_" + AimName(k) + (l ? "_L" : "");
                                if (ContentFinder<Texture2D>.Get(p, false) == null) continue;
                                string key = def.defName + "|" + AimName(k) + (l ? "|L" : "");
                                mats[key] = MaterialPool.MatFrom(p, ShaderDatabase.Cutout);
                                RegisterPaint(def, key, p);
                                n++;
                                // optional X_aimNN_top(_L): a part of the weapon drawn ABOVE the hull (Dominus meltagun targeters, 2026-11-16)
                                string tp = path + "_" + AimName(k) + "_top" + (l ? "_L" : "");
                                if (ContentFinder<Texture2D>.Get(tp, false) != null)
                                {
                                    // paintable like the body when a mask X_aimNN_top(_L)m.png exists (targeter shell = house colour, 2026-11-16)
                                    mats[key + "|top"] = MaterialPool.MatFrom(tp, ShaderDatabase.Cutout);
                                    RegisterPaint(def, key + "|top", tp, true);     // stays house-coloured in align-tint mode (user screenshot: white)
                                    TopMats[key] = mats[key + "|top"];
                                }
                            }
                        AimDefs[def] = AimSteps;
                    }
                    else Log.Warning("[LIK] " + def.defName + ": some of the aim pictures are missing - using the per-facing pictures");
                }
                foreach (string s in sides)
                {
                    if (ContentFinder<Texture2D>.Get(path + "_" + s, false) != null)
                    {
                        mats[def.defName + "|" + s] = AlignTool.TintOn ? MaterialPool.MatFrom(path + "_" + s, ShaderDatabase.Cutout, AlignTool.WeaponTint) : MaterialPool.MatFrom(path + "_" + s, ShaderDatabase.Cutout);
                        RegisterPaint(def, def.defName + "|" + s, path + "_" + s);
                        n++;
                    }
                    // optional X_<side>_L.png: the mirrored picture (used on the Knight's RIGHT arm since 2026-11-07 swap; the normal picture then goes on the left arm) in the front/back views (the normal picture is the right arm; the arms mirror each other)
                    if (ContentFinder<Texture2D>.Get(path + "_" + s + "_L", false) != null)
                    {
                        mats[def.defName + "|" + s + "|L"] = AlignTool.TintOn ? MaterialPool.MatFrom(path + "_" + s + "_L", ShaderDatabase.Cutout, AlignTool.WeaponTint) : MaterialPool.MatFrom(path + "_" + s + "_L", ShaderDatabase.Cutout);
                        RegisterPaint(def, def.defName + "|" + s + "|L", path + "_" + s + "_L");
                        n++;
                    }
                }
            }
            // back-view carapace pieces drawn over the Dominus meltagun targeters (user 2026-11-16 red loop): full body canvas, house-paintable
            VehicleTurretDef melta = DefDatabase<VehicleTurretDef>.GetNamedSilentFail("LIK_Turret_dominus_twinmelta");
            if (melta != null)
                foreach (string sd in new[] { "R", "L" })
                {
                    string cp = "Things/Knights/Dominus/TargeterCover/tgt_north_" + sd;
                    if (ContentFinder<Texture2D>.Get(cp, false) == null) continue;
                    string ck = melta.defName + "|cover|" + sd;
                    mats[ck] = MaterialPool.MatFrom(cp, ShaderDatabase.Cutout);
                    RegisterPaint(melta, ck, cp, true);
                    CoverMats[sd] = mats[ck];
                }
            LoadAimShifts();
            if (n > 0) Log.Message("[LIK] per-facing turret pictures found: " + n);
        }

        /// <summary>A per-facing picture with a colour mask: its default material is drawn in the class colours; other colours are made on demand.</summary>
        static void RegisterPaint(VehicleTurretDef def, string key, string picPath, bool evenWhenTinted = false)
        {
            Texture2D mask = ContentFinder<Texture2D>.Get(picPath + "m", false);
            if (mask == null || (AlignTool.TintOn && !evenWhenTinted)) return;
            string cls = def.defName.Split('_')[2];
            KnightClassDef kc = DefDatabase<KnightClassDef>.GetNamedSilentFail("LIK_Class_" + char.ToUpperInvariant(cls[0]) + cls.Substring(1));
            if (kc == null || !KnightPaint.Paintable(kc.vehicle)) return;
            Texture2D main = ContentFinder<Texture2D>.Get(picPath, false);
            PaintChoice d = KnightPaint.Defaults(kc.vehicle);
            Color a = d.one, b = d.two;
            Material m = KnightPaint.MakeMat(main, mask, d.one, d.two, d.three);
            if (m == null) return;
            mats[key] = m;
            paintSrc[key] = new PaintSrc { main = main, mask = mask, defKey = KnightPaint.Key(a, b) };
        }

        /// <summary>Render threads: the picture in this Knight's colours, or the default-coloured one until it is made (next frame).</summary>
        public static Material Paint(VehiclePawn v, string key, Material fallback)
        {
            if (v?.patternData == null || !paintSrc.TryGetValue(key, out PaintSrc src)) return fallback;
            PatternData pd = v.patternData;
            long k = KnightPaint.Key(pd.color, pd.colorTwo);
            if (k == src.defKey) return fallback;
            if (painted.TryGetValue((key, k), out Material m)) return m;
            paintQueue.Enqueue((key, pd.color, pd.colorTwo, pd.colorThree));
            return fallback;
        }

        public static void ProcessPaintQueue()
        {
            int guard = 0;
            while (guard++ < 64 && paintQueue.TryDequeue(out var q))
            {
                long k = KnightPaint.Key(q.one, q.two);
                if (painted.ContainsKey((q.key, k)) || !paintSrc.TryGetValue(q.key, out PaintSrc src)) continue;
                Material m = KnightPaint.MakeMat(src.main, src.mask, q.one, q.two, q.three);
                if (m != null) painted[(q.key, k)] = m;
            }
        }

        /// <summary>Main thread, right after a Knight is painted: make all its class's weapon pictures in the new colours.</summary>
        public static void Warm(VehiclePawn v)
        {
            if (v?.patternData == null) return;
            string prefix = "LIK_Turret_" + v.VehicleDef.defName.Substring("LIK_".Length).ToLowerInvariant() + "_";
            long k = KnightPaint.Key(v.patternData.color, v.patternData.colorTwo);
            foreach (KeyValuePair<string, PaintSrc> e in paintSrc)
            {
                if (!e.Key.StartsWith(prefix) || k == e.Value.defKey || painted.ContainsKey((e.Key, k))) continue;
                Material m = KnightPaint.MakeMat(e.Value.main, e.Value.mask, v.patternData.color, v.patternData.colorTwo, v.patternData.colorThree);
                if (m != null) painted[(e.Key, k)] = m;
            }
        }

        /// <summary>Side view: is this arm weapon the far one (mounted higher on screen than the other arm's weapon)?</summary>
        public static bool IsFarArm(VehicleTurret t, Rot8 o, string group = "arm")
        {
            if (t.vehicle?.CompVehicleTurrets == null || t.key == null) return false;
            string mine = t.key.Split('_')[0];
            float z = t.renderProperties.OffsetFor(o).y;
            foreach (VehicleTurret other in t.vehicle.CompVehicleTurrets.Turrets)
            {
                if (other == t || other.key == null || !other.key.StartsWith(group) || other.key.StartsWith(mine)) continue;
                return other.renderProperties.OffsetFor(o).y < z;
            }
            return false;
        }

        public static readonly HashSet<string> UpperHullClasses = new HashSet<string> { "cerastus", "dominus", "questoris", "armiger" };   // questoris + armiger: pauldron hull 2026-11-20 (occluders retired)   // hull overlay in all four facings (see Patch_TurretFacingArt)
        public static readonly HashSet<string> HullClasses = new HashSet<string>();       // class keys (lower case) with a Hull overlay (body minus legs) drawn over the arm weapons
        public static readonly HashSet<string> OccluderClasses = new HashSet<string>();   // class keys (lower case) that ship Occluder overlays

        // ---- AIM PICTURES (user 2026-11-11, a trial: "see if it'll look good when swapping between the angles of each weapon for the top weapons
        // on the Dominus and Questoris"). Normally a weapon shows the picture for the way the KNIGHT faces and is turned by how far it aims off
        // that. With the option on (mod settings), the top / shoulder / head / side weapons of those two classes show the picture for the way the
        // WEAPON points - north, east, south or west, whichever is nearest - and are only turned by the remainder (at most 45 degrees).
        [System.ThreadStatic] public static VehicleTurret lastTurret;      // the turret whose picture was just chosen, and the side chosen
        [System.ThreadStatic] public static string lastSide;               // (read by the axis-nudge patch that runs right after)

        public static readonly Dictionary<VehicleTurretDef, int> AimDefs = new Dictionary<VehicleTurretDef, int>();   // def -> number of aim pictures
        static readonly string[] aimNames = Enumerable.Range(0, 32).Select(k => "aim" + k.ToString("00")).ToArray();
        public static string AimName(int k) { return aimNames[k]; }
        public static float AimHysteresis(int steps) { return steps >= 32 ? 2.5f : 4f; }

        // ---- TOP PARTS (user 2026-11-16 "missing camera bits on top of where the twin melta guns are"): the Dominus meltaguns are drawn under
        // the hull so the chest dome hides their mounting post - which also hid the targeter sitting on top of the dome ring. X_aimNN_top.png
        // holds just that part; it is drawn right after the weapon, at the same place, lifted above the hull.
        public static readonly Dictionary<string, Material> TopMats = new Dictionary<string, Material>();   // same keys as the aim materials
        public static readonly Dictionary<string, Material> CoverMats = new Dictionary<string, Material>(); // "R"/"L" -> back-view carapace piece
        public class TopRef { public Material m; public string key; public float lift; }
        public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, TopRef> TopBox = new System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, TopRef>();
        /// <summary>Pick (or clear) the top part for the aim picture this turret shows now.</summary>
        /// <param name="lift">height above the turret's own draw position (south/side: over the hull; north: over the body, under the hull)</param>
        public static void SetTop(VehicleTurret t, bool show, float lift = 4 * 0.0036585368f)
        {
            Material m = null; string mk = null;
            if (show && AimDefs.ContainsKey(t.def) && AimLast.TryGetValue(t, out int k))
            {
                bool right = t.key != null && (t.key.StartsWith("shoulderR") || t.key.StartsWith("headR") || t.key.StartsWith("armR"));
                string an = AimName(k);
                mk = Key(t.def, an, true);
                if (!(right && TopMats.TryGetValue(mk, out m))) { mk = Key(t.def, an, false); TopMats.TryGetValue(mk, out m); }
            }
            if (m == null && !TopBox.TryGetValue(t, out _)) return;
            TopRef r = TopBox.GetValue(t, _ => new TopRef());
            r.m = m; r.key = mk == null ? null : mk + "|top"; r.lift = lift;
        }
        public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, StrongBox> AimLastBox = new System.Runtime.CompilerServices.ConditionalWeakTable<VehicleTurret, StrongBox>();
        public class StrongBox { public int k; }
        public static class AimLast
        {
            public static bool TryGetValue(VehicleTurret t, out int k)
            {
                if (AimLastBox.TryGetValue(t, out StrongBox b)) { k = b.k; return true; }
                k = -1; return false;
            }
            public static int Set(VehicleTurret t, int k) { AimLastBox.GetValue(t, _ => new StrongBox()).k = k; return k; }
        }
        /// <summary>Screen shift (cells, + = up) that puts the pin-centred aim picture where the old per-facing picture was drawn.</summary>
        public static readonly Dictionary<string, Vector2> AimShift = new Dictionary<string, Vector2>
        {
            { "LIK_Turret_dominus_siegebreaker", new Vector2(0f, -0.484f) },     // measured: 124 px of 1280 on a 5-cell picture
            { "LIK_Turret_dominus_shieldbreaker", new Vector2(0f, -0.398f) },    // measured: 102 px of 1024 on a 4-cell picture
        };

        /// <summary>More shifts from Data/aim_shifts.json  {"LIK_Turret_x": [dx, dz], ...}  (written by the aim-picture install scripts).</summary>
        static void LoadAimShifts()
        {
            try
            {
                if (AlignTool.Root == null) return;
                string path = System.IO.Path.Combine(AlignTool.Root, "Data", "aim_shifts.json");
                if (!System.IO.File.Exists(path)) return;
                var re = new System.Text.RegularExpressions.Regex("\"(LIK_\\w+)\"\\s*:\\s*\\[\\s*(-?[0-9.]+)\\s*,\\s*(-?[0-9.]+)\\s*\\]");
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                foreach (System.Text.RegularExpressions.Match m in re.Matches(System.IO.File.ReadAllText(path)))
                    AimShift[m.Groups[1].Value] = new Vector2(float.Parse(m.Groups[2].Value, inv), float.Parse(m.Groups[3].Value, inv));
            }
            catch (System.Exception e) { Log.Warning("[LIK] could not read aim_shifts.json: " + e.Message); }
        }

        public static bool AimMode(VehicleTurret t)
        {
            if (LIKMod.I == null || !LIKMod.I.aimPictures || t == null || t.def == null || t.key == null || t.key.StartsWith("arm")) return false;
            string dn = t.def.defName;
            return dn.StartsWith("LIK_Turret_questoris_") || dn.StartsWith("LIK_Turret_dominus_");
        }

        /// <summary>Nearest of the four picture directions to an absolute angle (0 = north, clockwise); snapped = that direction's angle.</summary>
        public static string SideOfAngle(float angle, out float snapped)
        {
            int q = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 90f) % 4;
            snapped = q * 90f;
            return sides[q];
        }

        // PERF (2026-11-13): the picture keys are made once and reused - this runs for every Knight weapon every frame on the render threads
        static readonly System.Collections.Concurrent.ConcurrentDictionary<(VehicleTurretDef, string, bool), string> keys = new System.Collections.Concurrent.ConcurrentDictionary<(VehicleTurretDef, string, bool), string>();

        public static string Key(VehicleTurretDef def, string side, bool mirrored)
        {
            return keys.GetOrAdd((def, side, mirrored), k => k.Item1.defName + "|" + k.Item2 + (k.Item3 ? "|L" : ""));
        }

        public static bool Has(VehicleTurretDef def, string side)
        {
            return mats.ContainsKey(Key(def, side, false)) || mats.ContainsKey(Key(def, side, true));
        }

        public static bool TryGet(VehicleTurretDef def, string side, out Material m, bool mirrored = false)
        {
            return mats.TryGetValue(Key(def, side, mirrored), out m);
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), "ParallelPreRenderResults")]
    public static class Patch_TurretFacingArt
    {
        public static void Postfix(VehicleTurret __instance, ref TransformData transformData, ref PreRenderResults __result)
        {
            if (!__result.draw || __instance.def == null) return;
            // Workshop bug report 2026-11-24 (Andersbot): other Vehicle Framework mods' turret graphics broke. This runs for EVERY VF turret;
            // further down defName.Split('_')[2] threw on other mods' turret names (and "socket*" keys could hide their guns). Knights only.
            if (!__instance.def.defName.StartsWith("LIK_Turret_")) return;
            if (AlignTool.HiddenKeys.Count > 0 && __instance.vehicle != null && __instance.key != null && AlignTool.IsHidden(__instance.vehicle, __instance.key.Split('_')[0])) { __result.draw = false; return; }
            Rot8 o = transformData.orientation;
            string side = o == Rot8.North ? "north" : o == Rot8.East ? "east" : o == Rot8.South ? "south" : o == Rot8.West ? "west" : null;
            if (side == null)
            {
                // diagonal facing (2026-11-16, Dominus proof of concept): weapons with 3D aim pictures show the picture for where they point
                // (it does not depend on the Knight's facing); VF's own diagonal mount offsets place them. Other weapons keep VF's default.
                if (FacingArt.AimDefs.ContainsKey(__instance.def))
                {
                    string near = o == Rot8.NorthEast || o == Rot8.NorthWest ? "north" : "south";
                    // turned toward the camera (SE / SW) the arm weapons hang in front of the body, like facing south; turned away they stay under it
                    if (near == "south" && key0Diag(__instance) && __instance.drawLayer < 1)
                        __result.position.y += (1 - __instance.drawLayer) * 0.0036585368f;
                    ApplyFacingPicture(__instance, near, o, ref __result);
                }
                return;
            }
            string key0 = __instance.key ?? "";
            const float L = 0.0036585368f;
            // Acastus shoulder sockets in the side views (user 2026-11-15): the far one is hidden by the hull, the near one sits under the body
            // so its socket connection is covered and only what sticks out past the hull shows
            // seen from behind (user 2026-11-15 screenshot) the socket guns sit behind the carapace too
            if (key0.StartsWith("socket") && side == "north")
            {
                __result.position.y -= (__instance.drawLayer + 1) * L;
                ApplyFacingPicture(__instance, side, o, ref __result);
                return;
            }
            if (key0.StartsWith("socket") && (side == "east" || side == "west"))
            {
                bool nearSocket = side == "east" ? key0.StartsWith("socketR") : key0.StartsWith("socketL");
                if (!nearSocket) { __result.draw = false; return; }
                __result.position.y -= (__instance.drawLayer + 1) * L;
                ApplyFacingPicture(__instance, side, o, ref __result);
                return;
            }
            string clsKey = __instance.def.defName.Split('_')[2];
            bool occClass = FacingArt.OccluderClasses.Contains(clsKey);
            // Dominus twin meltaguns flanking the head (user 2026-11-16 screenshots): seen from behind they are in front of the Knight -> under
            // the body; in a side view the far one is behind the head -> under the body, the near one stays on top; facing south on top
            if (clsKey == "dominus" && key0.StartsWith("head"))
            {
                // user 2026-11-16 (side view screenshot): the near meltagun's body showed over the torso - it sits tucked beside the head, so in
                // the side views BOTH are behind the body and only what sticks out past the Knight's outline (the barrels) shows
                bool hide = side != "south";
                if (hide) __result.position.y -= (__instance.drawLayer + 1) * L;
                // shown: like the arm weapons - just over the body picture, UNDER the hull overlay (0.0025), so the socket ring in the hull
                // covers the gun's mounting post (user 2026-11-16: "the melta guns also below their turret socket and on top of the body")
                // 2026-11-20 (user screenshot, right melta aimed sideways looked chopped): the arm guns sit at the SAME +0.001, so the arm's
                // shoulder housing randomly covered the melta. The meltas are in front of the arms -> 0.0017: over the arms, under the hull (0.0025)
                else __result.position.y += (0 - __instance.drawLayer) * L + 0.0017f;
                ApplyFacingPicture(__instance, side, o, ref __result);
                // targeter on top of the dome (user 2026-11-16 screenshots): front view both; side views only the NEAR gun's (the far dome is
                // behind the torso - drawn, it floated over the arm weapon); back view none (domes are on the chest, behind the torso)
                bool sideView = side == "east" || side == "west";
                // back view (user 2026-11-16): the targeters are drawn just over the body and UNDER the north hull, which now holds the whole
                // top carapace - so only what the carapace does not cover shows. The gun itself sits one layer under the body there (its
                // position.y was lowered by (drawLayer+1)*L from body + drawLayer*L), so +L+0.001 = just over the body, under the hull (0.0025).
                // user 2026-11-16 (in-game screenshot, position right): show the WHOLE targeter on top in the back view too, like the front view
                if (side == "north") FacingArt.SetTop(__instance, true, (__instance.drawLayer + 1) * L + 4 * L);
                else FacingArt.SetTop(__instance, !hide || (sideView && !FacingArt.IsFarArm(__instance, o, "head")));
                return;
            }
            // Dominus top weapons (shoulder siegebreakers, top shieldbreaker): over the ENTIRE body, hull overlay included, in every facing
            // Questoris socket (side) guns, 4 per-facing pictures again (user 2026-11-20 "the occlusion on all need to be done"): the mount is on
            // the Knight's +X side beside the chin. Seen from behind (north) and from the far side (east: camera at -X; the user's aligned
            // positions agree - east sits higher, i.e. further away) the body is in front of it -> under the body picture, only what sticks
            // out past the Knight's outline shows. Front (south) and near side (west) stay on top (qTop below).
            if (clsKey == "questoris" && key0.StartsWith("side") && (side == "north" || side == "east"))
            {
                __result.position.y -= (__instance.drawLayer + 1) * L;
                ApplyFacingPicture(__instance, side, o, ref __result);
                return;
            }
            // Questoris top carapace + side (socket) guns (2026-11-20, pauldron hull added): above the hull overlay like the Dominus top weapons,
            // except the side gun seen from behind (it sits at the front beside the chin) - that keeps its old layering
            bool qTop = (clsKey == "questoris" && (key0.StartsWith("top") || (key0.StartsWith("side") && side != "north")))
                     || (clsKey == "armiger" && key0.StartsWith("carapace"));   // Armiger carapace melta/stubber over its new pauldron hull
            if ((clsKey == "dominus" && (key0.StartsWith("shoulder") || key0.StartsWith("top"))) || qTop)
            {
                // user 2026-11-16 "pauldrons showing through in some areas": VF overlays carry their own layer (default 1 = +L) on top of
                // their draw offset, so +L was not always clear of the hull overlay - lift well above every body overlay
                __result.position.y += 4 * L;
                ApplyFacingPicture(__instance, side, o, ref __result);
                return;
            }
            // classes with an UPPER-BODY hull in every facing (Cerastus, user 2026-11-15): arm weapons just above the body picture and below the
            // hull overlay (carapace, pauldrons, shoulder joints) - over the legs, under the shoulders; the far arm in a side view stays under the body
            if (FacingArt.UpperHullClasses.Contains(clsKey) && key0.StartsWith("arm") && __instance.drawLayer < 1)
            {
                bool far = side == "east" ? !key0.StartsWith("armR") : side == "west" && !key0.StartsWith("armL");
                // Dominus (2026-11-16): its hand alignment puts the LEFT arm low (near the camera) in BOTH side views, so near/far is taken
                // from the positions instead of the names - in a side view the arm mounted lower on screen is the near one
                if (clsKey == "dominus" && (side == "east" || side == "west")) far = FacingArt.IsFarArm(__instance, o);
                // seen from behind (user 2026-11-16) the Dominus arm guns point away, in front of the Knight: fully behind the body, or their
                // outlines show around the carapace
                if (clsKey == "dominus" && side == "north") far = true;
                if (!far) __result.position.y += (0 - __instance.drawLayer) * L + 0.001f;
                ApplyFacingPicture(__instance, side, o, ref __result);
                return;
            }
            // seen from BEHIND (user 2026-11-15): the arm weapons lie OVER the legs and feet but UNDER the carapace, pauldrons and shoulder joints -
            // they sit just above the body picture and below the class's Hull overlay (hull_north = the upper body)
            if (occClass && side == "north" && key0.StartsWith("arm") && __instance.drawLayer < 1 && FacingArt.HullClasses.Contains(clsKey))
            {
                __result.position.y += (0 - __instance.drawLayer) * L + 0.001f;
                ApplyFacingPicture(__instance, side, o, ref __result);
                return;
            }
            // classes with depth occluders (see Textures/.../<Class>/Occluder): the weapon is drawn ABOVE the body in every facing and an overlay of the
            // body pixels that really hide it is drawn on top of the weapon. Weapons WITHOUT occluders (ion gauntlet shield) use the plain layering below.
            if (occClass && __instance.drawLayer < 1 && FacingArt.HasOccluder(__instance))
            {
                __result.position.y += (1 - __instance.drawLayer) * 0.0036585368f;
                ApplyFacingPicture(__instance, side, o, ref __result);
                return;
            }
            // arm weapons sit UNDER the body (drawLayer -1) in the front/back views. In the side views only the arm nearest the camera is on top:
            // facing east the Knight's right arm is nearest, facing west its left arm; the far arm stays behind the body.
            if ((side == "east" || side == "west") && __instance.drawLayer < 1 && __instance.key != null)
            {
                bool near = side == "east" ? __instance.key.StartsWith("armR") : __instance.key.StartsWith("armL");
                if (near || !__instance.key.StartsWith("arm")) __result.position.y += (1 - __instance.drawLayer) * 0.0036585368f;
            }
            // facing south the arms hang in FRONT of the hull and the thigh armour (muzzles point at the camera): lift them above the body; the pauldron overlay stays above them
            // classes with a Hull overlay: only just above the body picture (so above the legs) and BELOW the hull overlay (0.0025), the socket/top turrets and the pauldrons
            if (side == "south" && __instance.drawLayer < 1 && __instance.key != null && __instance.key.StartsWith("arm"))
                __result.position.y += FacingArt.HullClasses.Contains(__instance.def.defName.Split('_')[2]) ? (0 - __instance.drawLayer) * 0.0036585368f + 0.001f : (1 - __instance.drawLayer) * 0.0036585368f;
            ApplyFacingPicture(__instance, side, o, ref __result);
        }

        static bool key0Diag(VehicleTurret t) { return t.key != null && t.key.StartsWith("arm"); }

        static void ApplyFacingPicture(VehicleTurret __instance, string side, Rot8 o, ref PreRenderResults __result)
        {
            Material m;
            float faceAngle = o.AsAngle;
            if (FacingArt.AimDefs.TryGetValue(__instance.def, out int steps))
            {
                float step = 360f / steps;
                // 16 aim pictures: the one nearest to where the weapon points, turned only by the remainder (at most 11.25 degrees)
                float abs = Mathf.Repeat(__result.quaternion.eulerAngles.y, 360f);
                int k = Mathf.RoundToInt(abs / step) % steps;
                // user 2026-11-15 "noticeably jitters": near the halfway angle between two pictures (and while VF's aim hunts by a degree) the
                // picture flipped every frame. Hysteresis: keep the current picture until the aim is 4 degrees past the halfway point.
                if (FacingArt.AimLast.TryGetValue(__instance, out int prev) && prev != k
                    && Mathf.Abs(Mathf.DeltaAngle(abs, prev * step)) <= step / 2f + FacingArt.AimHysteresis(steps))
                    k = prev;
                FacingArt.AimLast.Set(__instance, k);
                string an = FacingArt.AimName(k);
                FacingArt.lastTurret = __instance;
                FacingArt.lastSide = side;
                // user 2026-11-15 "still jittering": turning a 35-degree picture flat tilts its oval and the tilt snapped back at every swap.
                // Default: show the true 3D picture only (at 32 steps the barrel is at most 5.6 degrees off the aim); the old tilt is a setting.
                if (LIKMod.I != null && LIKMod.I.aimTurnBetween) __result.quaternion = Quaternion.AngleAxis(-k * step, Vector3.up) * __result.quaternion;
                else __result.quaternion = Quaternion.identity;
                // the aim pictures are centred on the swivel pin, the old per-facing ones on the middle of the turret: keep it where it sat
                if (FacingArt.AimShift.TryGetValue(__instance.def.defName, out Vector2 sh)) { __result.position.x += sh.x; __result.position.z += sh.y; }
                bool right = __instance.key != null && (__instance.key.StartsWith("shoulderR") || __instance.key.StartsWith("headR") || __instance.key.StartsWith("armR"));
                if (right && FacingArt.TryGet(__instance.def, an, out m, true)) { __result.material = FacingArt.Paint(__instance.vehicle, FacingArt.Key(__instance.def, an, true), m); return; }
                if (FacingArt.TryGet(__instance.def, an, out m)) __result.material = FacingArt.Paint(__instance.vehicle, FacingArt.Key(__instance.def, an, false), m);
                return;
            }
            if (FacingArt.AimMode(__instance))
            {
                // the picture for where the weapon points; the sprite's own rotation at this moment is its absolute aim
                float snapped;
                string aimSide = FacingArt.SideOfAngle(__result.quaternion.eulerAngles.y, out snapped);
                if (FacingArt.Has(__instance.def, aimSide)) { side = aimSide; faceAngle = snapped; }
            }
            FacingArt.lastTurret = __instance;
            FacingArt.lastSide = side;
            // VF rotates every turret sprite by the vehicle's facing angle. The per-facing pictures are already drawn for that facing
            // (south = muzzle toward the camera, east = muzzle right ...), so cancel the facing part of the rotation; aiming still adds on top.
            bool hasFacingPic = FacingArt.TryGet(__instance.def, side, out m) || FacingArt.TryGet(__instance.def, side, out m, true);
            // Only the newer sets (Armiger/Questoris/Dominus) are drawn already facing the right way. Cerastus/Acastus pictures are still the old
            // convention (muzzle UP in every picture, the game turns them), so they keep VF's own rotation.
            string dn = __instance.def.defName;
            bool preOriented = dn.StartsWith("LIK_Turret_armiger_") || dn.StartsWith("LIK_Turret_questoris_") || dn.StartsWith("LIK_Turret_dominus_");
            // Acastus socket guns have ONE picture, drawn as a south view (muzzle pointing down); VF expects muzzle up, so it turned them the wrong way: add 180
            if (!hasFacingPic && dn.StartsWith("LIK_Turret_acastus_") && __instance.key != null && __instance.key.StartsWith("socket"))
                __result.quaternion = Quaternion.AngleAxis(180f, Vector3.up) * __result.quaternion;
            if (hasFacingPic && preOriented) __result.quaternion = Quaternion.AngleAxis(-faceAngle, Vector3.up) * __result.quaternion;
            bool isRight = __instance.key != null && (__instance.key.StartsWith("armR") || __instance.key.StartsWith("shoulderR") || __instance.key.StartsWith("headR"));
            // old-convention sets (Cerastus/Acastus) are turned 180 deg by VF when facing south, which mirrors left<->right: use the other arm's picture there
            if (!preOriented && side == "south") isRight = !isRight;
            if (isRight && FacingArt.TryGet(__instance.def, side, out m, true)) { __result.material = FacingArt.Paint(__instance.vehicle, FacingArt.Key(__instance.def, side, true), m); return; }
            if (FacingArt.TryGet(__instance.def, side, out m)) __result.material = FacingArt.Paint(__instance.vehicle, FacingArt.Key(__instance.def, side, false), m);
        }
    }

    /// <summary>Draws a turret's top part (FacingArt.TopMats) right after the turret itself, above the hull overlay.</summary>
    [HarmonyPatch(typeof(VehicleTurret), "Draw")]
    public static class Patch_TurretTopPart
    {
        static readonly AccessTools.FieldRef<VehicleTurret, PreRenderResults> resultsRef = AccessTools.FieldRefAccess<VehicleTurret, PreRenderResults>("results");
        static void Postfix(VehicleTurret __instance)
        {
            if (FacingArt.TopMats.Count == 0 || !FacingArt.TopBox.TryGetValue(__instance, out FacingArt.TopRef top) || top.m == null) return;
            if (__instance.vehicle == null || __instance.vehicle.FullRotation.IsDiagonal) return;        // SetTop runs every frame in the cardinal facings only
            PreRenderResults r = resultsRef(__instance);
            if (!r.draw || r.mesh == null) return;
            Vector3 p = r.position; p.y += top.lift;
            Material m = top.key != null ? FacingArt.Paint(__instance.vehicle, top.key, top.m) : top.m;
            Graphics.DrawMesh(r.mesh, p, r.quaternion, m, 0);
            // back view: the carapace in front of this targeter (body canvas, centred on the Knight) goes over it
            if (__instance.vehicle.FullRotation == Rot8.North && __instance.key != null)
            {
                string sd = __instance.key.StartsWith("headR") ? "R" : "L";
                if (FacingArt.CoverMats.TryGetValue(sd, out Material cm))
                {
                    Vector3 c = __instance.vehicle.DrawPos; c.y = p.y + 0.0005f;
                    Vector2 ds = __instance.vehicle.VehicleDef.graphicData.drawSize;
                    Material pm = FacingArt.Paint(__instance.vehicle, "LIK_Turret_dominus_twinmelta|cover|" + sd, cm);
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(ds.x, 1f, ds.y)), pm, 0);
                }
            }
        }
    }
}
