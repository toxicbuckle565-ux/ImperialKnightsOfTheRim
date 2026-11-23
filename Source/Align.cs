using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using SmashTools;
using SmashTools.Rendering;
using Vehicles;
using Vehicles.Rendering;
using Verse;

namespace LIK
{
    /// <summary>
    /// Alignment helper (mod settings > Imperial Knights > weapon position adjustments): weapons are tinted orange and the body-cover layers cyan, and every Knight
    /// gets an "Align weapons" button that nudges each mount live for the facing the Knight currently has. "Save" writes Tools/user_align.json, which I fold
    /// back into the generator (and re-render the cover layers with the same shift).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AlignTool
    {
        public static bool Enabled { get { return LIKMod.I.alignHelper; } }
        public static bool TintOn { get { return LIKMod.I.alignHelper && LIKMod.I.alignTint; } }
        public static bool CrossOn { get { return LIKMod.I.alignHelper && LIKMod.I.alignCross; } }
        public static readonly string Root;
        public static readonly Color WeaponTint = new Color(1f, 0.55f, 0.25f);
        public static readonly Color CoverTint = new Color(0.3f, 0.95f, 1f);

        static AlignTool()
        {
            ModContentPack m = LoadedModManager.RunningModsListForReading.FirstOrDefault(x => x.PackageIdPlayerFacing != null && x.PackageIdPlayerFacing.ToLowerInvariant() == "local.imperialknights");
            Root = m?.RootDir;
            if (Root != null) LoadAxisDefaults(Root);
            if (Enabled) Log.Message("[LIK] alignment helper ON (mod settings > Imperial Knights)");
        }

        /// <summary>Mounts hidden from the alignment window ("pawnId|mount"): their weapon and cover layer are not drawn.</summary>
        public static readonly HashSet<string> HiddenKeys = new HashSet<string>();
        /// <summary>Axis nudge ("pawnId|mount|side" -> cells, in the weapon picture's own frame): where the picture sits relative to the point it turns around.</summary>
        public static readonly Dictionary<string, Vector2> AxisShift = new Dictionary<string, Vector2>();
        /// <summary>Saved axis nudges (Data/axis_offsets.json): "LIK_Class|mount|side" -> cells. Used for every weapon on that mount.</summary>
        public static readonly Dictionary<string, Vector2> AxisDefaults = new Dictionary<string, Vector2>();
        public static Vector2 GetAxis(VehiclePawn v, string mount, string side, string weapon)
        {
            // a weapon-only nudge (Data/axis_offsets.json key "<mount>__<weapon>", e.g. armR__cerastus_warblade) wins over the mount-wide one,
            // unless the align tool is nudging this mount live right now
            Vector2 w;
            if (weapon != null && !AxisShift.ContainsKey(v.thingIDNumber + "|" + mount + "|" + side)
                && AxisDefaults.TryGetValue(v.VehicleDef.defName + "|" + mount + "__" + weapon + "|" + side, out w)) return w;
            return GetAxis(v, mount, side);
        }

        public static Vector2 GetAxis(VehiclePawn v, string mount, string side)
        {
            Vector2 a;
            if (AxisShift.TryGetValue(v.thingIDNumber + "|" + mount + "|" + side, out a)) return a;
            return AxisDefaults.TryGetValue(v.VehicleDef.defName.Replace("LIK_", "LIK_") + "|" + mount + "|" + side, out a) ? a : Vector2.zero;
        }
        static void LoadAxisDefaults(string root)
        {
            try
            {
                string path = Path.Combine(root, "Data", "axis_offsets.json");
                if (!File.Exists(path)) return;
                string txt = File.ReadAllText(path);
                // tiny hand parser for {"LIK_X":{"mount":{"side":[x,y]}}}
                var re = new System.Text.RegularExpressions.Regex(@"""(LIK_\w+)""\s*:\s*\{|""(\w+)""\s*:\s*\{|""(north|east|south|west)""\s*:\s*\[\s*(-?[0-9.]+)\s*,\s*(-?[0-9.]+)\s*\]");
                string cls = null, mount = null;
                foreach (System.Text.RegularExpressions.Match m in re.Matches(txt))
                {
                    if (m.Groups[1].Success) { cls = m.Groups[1].Value; mount = null; }
                    else if (m.Groups[3].Success) AxisDefaults[cls + "|" + mount + "|" + m.Groups[3].Value] = new Vector2(float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture));
                    else if (m.Groups[2].Success) mount = m.Groups[2].Value;
                }
                Log.Message("[LIK] weapon axis nudges loaded: " + AxisDefaults.Count);
            }
            catch (System.Exception e) { Log.Warning("[LIK] could not read axis_offsets.json: " + e.Message); }
        }
        /// <summary>"cerastus_warblade" for LIK_Turret_cerastus_warblade.</summary>
        public static string WeaponOf(Vehicles.VehicleTurret t) { string d = t?.def?.defName; return d != null && d.StartsWith("LIK_Turret_") ? d.Substring("LIK_Turret_".Length) : null; }

        public static bool IsHidden(VehiclePawn v, string mount) { return HiddenKeys.Count > 0 && v != null && HiddenKeys.Contains(v.thingIDNumber + "|" + mount); }

        public static string SideOf(Rot4 r) { return r == Rot4.North ? "north" : r == Rot4.East ? "east" : r == Rot4.South ? "south" : "west"; }

        public static Vector2 Get(VehicleTurretRender rp, string side)
        {
            Vector2? v = side == "north" ? rp.north : side == "east" ? rp.east : side == "south" ? rp.south : rp.west;
            return v ?? Vector2.zero;
        }

        public static void Set(VehicleTurretRender rp, string side, Vector2 val)
        {
            if (side == "north") rp.north = val; else if (side == "east") rp.east = val; else if (side == "south") rp.south = val; else rp.west = val;
            rp.RecacheOffsets();
        }
    }

    /// <summary>Applies the live axis nudge last (after the facing-picture patch): the picture slides against the turn point, rotating with the weapon.</summary>
    [HarmonyPatch(typeof(VehicleTurret), "ParallelPreRenderResults")]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_TurretAxisShift
    {
        public static void Postfix(VehicleTurret __instance, ref TransformData transformData, ref PreRenderResults __result)
        {
            if ((AlignTool.AxisShift.Count == 0 && AlignTool.AxisDefaults.Count == 0) || !__result.draw || __instance.vehicle == null || __instance.key == null) return;
            Rot8 o = transformData.orientation;
            string side = o == Rot8.North ? "north" : o == Rot8.East ? "east" : o == Rot8.South ? "south" : o == Rot8.West ? "west" : null;
            if (side == null) return;
            if (FacingArt.lastTurret == __instance && FacingArt.lastSide != null) side = FacingArt.lastSide;      // aim pictures: the nudge belongs to the picture shown
            Vector2 a = AlignTool.GetAxis(__instance.vehicle, __instance.key.Split('_')[0], side, AlignTool.WeaponOf(__instance));
            if (a == Vector2.zero) return;
            __result.position += __result.quaternion * new Vector3(a.x, 0f, a.y);
        }
    }

    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_AlignGizmo
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, VehiclePawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            if (AlignTool.Enabled && __instance.Faction == Faction.OfPlayer && KnightLoadout.ClassFor(__instance.VehicleDef) != null)
            {
                VehiclePawn v = __instance;
                yield return new Command_Action
                {
                    defaultLabel = "Align weapons",
                    defaultDesc = "Nudge each weapon mount for the way the Knight is facing right now (debug helper).",
                    icon = TexButton.Add,
                    action = () => Find.WindowStack.Add(new Window_AlignWeapons(v))
                };
            }
        }
    }

    /// <summary>Tiny reader/writer for the nested alignment JSON files ({"a":{"b":{"south":[x,y]}}}), flattened to "a|b|south" -> (x,y).</summary>
    public static class AlignIO
    {
        static readonly System.Text.RegularExpressions.Regex Tok = new System.Text.RegularExpressions.Regex(
            @"""([^""]+)""\s*:\s*\{|(\})|""(north|east|south|west)""\s*:\s*\[\s*(-?[0-9.eE+\-]+)\s*,\s*(-?[0-9.eE+\-]+)\s*\]");
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static Dictionary<string, Vector2> Read(string path)
        {
            var flat = new Dictionary<string, Vector2>();
            if (!File.Exists(path)) return flat;
            var stack = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in Tok.Matches(File.ReadAllText(path)))
            {
                if (m.Groups[1].Success) stack.Add(m.Groups[1].Value);
                else if (m.Groups[2].Success) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                else flat[string.Join("|", stack.ToArray()) + (stack.Count > 0 ? "|" : "") + m.Groups[3].Value] =
                        new Vector2(float.Parse(m.Groups[4].Value, Inv), float.Parse(m.Groups[5].Value, Inv));
            }
            return flat;
        }

        class Node { public SortedDictionary<string, Node> kids = new SortedDictionary<string, Node>(); public SortedDictionary<string, Vector2> vals = new SortedDictionary<string, Vector2>(); }

        /// <summary>Nested JSON object from "a|b|side" keys (only keys starting with prefix, prefix stripped).</summary>
        public static string Write(Dictionary<string, Vector2> flat, string prefix = "")
        {
            var root = new Node();
            foreach (var kv in flat)
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                string[] p = kv.Key.Substring(prefix.Length).Split('|');
                Node n = root;
                for (int i = 0; i < p.Length - 1; i++) { if (!n.kids.TryGetValue(p[i], out Node c)) n.kids[p[i]] = c = new Node(); n = c; }
                n.vals[p[p.Length - 1]] = kv.Value;
            }
            var sb = new StringBuilder();
            Emit(root, sb, 0);
            return sb.ToString();
        }

        static void Emit(Node n, StringBuilder sb, int depth)
        {
            string ind = new string(' ', depth + 1);
            var parts = new List<string>();
            foreach (var kv in n.vals) parts.Add(ind + "\"" + kv.Key + "\":[" + kv.Value.x.ToString("0.####", Inv) + "," + kv.Value.y.ToString("0.####", Inv) + "]");
            foreach (var kv in n.kids) { var s = new StringBuilder(); Emit(kv.Value, s, depth + 1); parts.Add(ind + "\"" + kv.Key + "\":" + s); }
            sb.Append("{\n").Append(string.Join(",\n", parts.ToArray())).Append('\n').Append(new string(' ', depth)).Append('}');
        }
    }

    public class Window_AlignWeapons : Window
    {
        readonly VehiclePawn v;
        float step = 0.05f;
        bool axisMode;
        // 2026-11-23: saves only what was changed in this window. "This weapon only" (default) -> the fitted weapon's own nudge/pivot,
        // "whole arm" -> the mount (every weapon on it). Before, Save wrote the fitted weapon's final position as the ARM position, which
        // baked that weapon's own nudge into the arm and moved every other weapon on it.
        bool weaponOnly = true;
        readonly Dictionary<string, Vector2> origPos = new Dictionary<string, Vector2>();    // "mount|side" -> position before the first nudge
        readonly Dictionary<string, Vector2> origAxis = new Dictionary<string, Vector2>();   // "mount|side" -> pivot before the first nudge
        readonly HashSet<string> touched = new HashSet<string>();
        static readonly float[] steps = { 0.02f, 0.05f, 0.1f, 0.25f };

        public Window_AlignWeapons(VehiclePawn v)
        {
            this.v = v;
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize { get { return new Vector2(440f, 120f + 30f * MountKeys().Count); } }

        List<string> MountKeys()
        {
            var comp = v.CompVehicleTurrets;
            if (comp == null) return new List<string>();
            return comp.Turrets.Where(t => t.key != null).Select(t => t.key.Split('_')[0]).Distinct().ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            string side = AlignTool.SideOf(v.Rotation);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), "Facing: " + side + "   (turn the Knight to edit another facing)");
            float x = 0f;
            foreach (float s in steps)
            {
                if (Widgets.ButtonText(new Rect(x, 26f, 60f, 24f), s.ToString("0.00") + (Mathf.Approximately(s, step) ? " *" : ""))) step = s;
                x += 64f;
            }
            float y = 58f;
            var comp = v.CompVehicleTurrets;
            foreach (string mount in MountKeys())
            {
                var turrets = comp.Turrets.Where(t => t.key != null && t.key.Split('_')[0] == mount).ToList();
                Vector2 cur = AlignTool.Get(turrets[0].renderProperties, side);
                string hk = v.thingIDNumber + "|" + mount;
                bool shown = !AlignTool.HiddenKeys.Contains(hk);
                bool was = shown;
                Widgets.Checkbox(new Vector2(0f, y), ref shown, 24f);
                if (shown != was)
                {
                    if (shown) AlignTool.HiddenKeys.Remove(hk); else AlignTool.HiddenKeys.Add(hk);
                    v.GetComp<CompKnightHead>()?.Apply();      // adds / removes the cover layer
                }
                if (Widgets.ButtonText(new Rect(26f, y, 40f, 24f), "solo"))
                {
                    foreach (string other in MountKeys()) { string ok = v.thingIDNumber + "|" + other; if (other == mount) AlignTool.HiddenKeys.Remove(ok); else AlignTool.HiddenKeys.Add(ok); }
                    v.GetComp<CompKnightHead>()?.Apply();
                }
                string wpn = AlignTool.WeaponOf(turrets[0]);
                Vector2 shownVal = axisMode ? AlignTool.GetAxis(v, mount, side, wpn) : cur;
                Widgets.Label(new Rect(70f, y, 130f, 24f), mount + " " + shownVal.x.ToString("0.00") + "," + shownVal.y.ToString("0.00"));
                Vector2 d = Vector2.zero;
                if (Widgets.ButtonText(new Rect(204f, y, 36f, 24f), "<")) d.x = -step;
                if (Widgets.ButtonText(new Rect(242f, y, 36f, 24f), ">")) d.x = step;
                if (Widgets.ButtonText(new Rect(280f, y, 36f, 24f), "^")) d.y = step;
                if (Widgets.ButtonText(new Rect(318f, y, 36f, 24f), "v")) d.y = -step;
                if (d != Vector2.zero)
                {
                    string tk = mount + "|" + side;
                    if (!origPos.ContainsKey(tk)) { origPos[tk] = cur; origAxis[tk] = AlignTool.GetAxis(v, mount, side, wpn); }
                    if (axisMode) AlignTool.AxisShift[v.thingIDNumber + "|" + mount + "|" + side] = AlignTool.GetAxis(v, mount, side, wpn) + d;
                    else foreach (VehicleTurret t in turrets) AlignTool.Set(t.renderProperties, side, AlignTool.Get(t.renderProperties, side) + d);
                    touched.Add(mount + "|" + side);
                }
                y += 30f;
            }
            if (Widgets.ButtonText(new Rect(342f, 26f, 90f, 24f), axisMode ? "mode: AXIS" : "mode: position")) axisMode = !axisMode;
            if (Widgets.ButtonText(new Rect(262f, 26f, 76f, 24f), "show all"))
            {
                foreach (string other in MountKeys()) AlignTool.HiddenKeys.Remove(v.thingIDNumber + "|" + other);
                v.GetComp<CompKnightHead>()?.Apply();
            }
            if (Widgets.ButtonText(new Rect(0f, inRect.height - 30f, 150f, 28f), "Save changes")) Save();
            if (Widgets.ButtonText(new Rect(156f, inRect.height - 30f, inRect.width - 156f, 28f), weaponOnly ? "scope: THIS WEAPON only" : "scope: WHOLE ARM (all its weapons)"))
                weaponOnly = !weaponOnly;
        }

        void Save()
        {
            if (origPos.Count == 0) { Messages.Message("Nothing changed since the last save.", MessageTypeDefOf.RejectInput, false); return; }
            string cls = v.VehicleDef.defName;
            string alignPath = Path.Combine(AlignTool.Root, "Tools", "user_align_" + cls + ".json");
            string weapPath = Path.Combine(AlignTool.Root, "Tools", "user_align_weapons_" + cls + ".json");
            string axisPath = Path.Combine(AlignTool.Root, "Data", "axis_offsets.json");
            var align = AlignIO.Read(alignPath);          // "offsets|mount|side", "axis|mount|side"
            var wn = AlignIO.Read(weapPath);              // "weapon|mount|side" -> nudge added on top of the generator's nudge
            var comp = v.CompVehicleTurrets;
            var log = new List<string>();
            bool alignChanged = false, weapChanged = false, axisChanged = false;
            foreach (string tk in origPos.Keys.ToList())
            {
                string mount = tk.Split('|')[0], side = tk.Split('|')[1];
                var turrets = comp.Turrets.Where(t => t.key != null && t.key.Split('_')[0] == mount).ToList();
                if (turrets.Count == 0) continue;
                List<string> weapons = turrets.Select(AlignTool.WeaponOf).Where(w => w != null).Distinct().ToList();   // fitted weapon + its alternate mode
                string wpn = AlignTool.WeaponOf(turrets[0]);
                Vector2 dp = AlignTool.Get(turrets[0].renderProperties, side) - origPos[tk];
                Vector2 live = AlignTool.GetAxis(v, mount, side, wpn);
                Vector2 da = live - origAxis[tk];
                bool movedPos = dp.sqrMagnitude > 1e-8f, movedAxis = da.sqrMagnitude > 1e-8f;
                if (weaponOnly)
                {
                    if (movedPos) foreach (string w in weapons) { string k = w + "|" + mount + "|" + side; wn[k] = (wn.TryGetValue(k, out Vector2 o) ? o : Vector2.zero) + dp; weapChanged = true; }
                    if (movedAxis) foreach (string w in weapons) { AlignTool.AxisDefaults[cls + "|" + mount + "__" + w + "|" + side] = live; axisChanged = true; }
                    if (movedPos || movedAxis) log.Add(string.Join("+", weapons.ToArray()) + " " + mount + " " + side + (movedPos ? " pos " + dp.ToString("F3") : "") + (movedAxis ? " pivot " + da.ToString("F3") : ""));
                }
                else
                {
                    if (movedPos)
                    {
                        string k = "offsets|" + mount + "|" + side;
                        align[k] = (align.TryGetValue(k, out Vector2 o) ? o : origPos[tk]) + dp; alignChanged = true;
                    }
                    if (movedAxis)
                    {
                        // the arm's pivot moves by the change; weapon-only pivots on that arm move with it
                        string mk = cls + "|" + mount + "|" + side;
                        AlignTool.AxisDefaults[mk] = (AlignTool.AxisDefaults.TryGetValue(mk, out Vector2 a0) ? a0 : Vector2.zero) + da;
                        foreach (string k in AlignTool.AxisDefaults.Keys.Where(k => k.StartsWith(cls + "|" + mount + "__") && k.EndsWith("|" + side)).ToList())
                            AlignTool.AxisDefaults[k] += da;
                        axisChanged = true;
                    }
                    if (movedPos || movedAxis) log.Add("arm " + mount + " " + side + (movedPos ? " pos " + dp.ToString("F3") : "") + (movedAxis ? " pivot " + da.ToString("F3") : ""));
                }
                AlignTool.AxisShift.Remove(v.thingIDNumber + "|" + mount + "|" + side);   // the saved value now shows the same thing
            }
            origPos.Clear(); origAxis.Clear(); touched.Clear();
            try
            {
                if (alignChanged)
                {
                    var axisFlat = AlignTool.AxisDefaults.Where(kv => kv.Key.StartsWith(cls + "|") && !kv.Key.Contains("__")).ToDictionary(kv => kv.Key.Substring(cls.Length + 1), kv => kv.Value);
                    File.WriteAllText(alignPath, "{\"class\":\"" + cls + "\",\n\"offsets\":" + AlignIO.Write(align, "offsets|") + ",\n\"axis\":" + AlignIO.Write(axisFlat) + "}\n");
                }
                if (weapChanged) File.WriteAllText(weapPath, AlignIO.Write(wn) + "\n");
                if (axisChanged) File.WriteAllText(axisPath, AlignIO.Write(AlignTool.AxisDefaults) + "\n");
            }
            catch (System.Exception e) { Log.Error("[LIK] alignment save failed: " + e); return; }
            string msg = (weaponOnly ? "Saved for this weapon only: " : "Saved for the whole arm: ") + string.Join("; ", log.ToArray());
            Messages.Message(log.Count == 0 ? "Nothing moved." : msg, MessageTypeDefOf.TaskCompletion, false);
            Log.Message("[LIK] alignment " + msg + (weapChanged ? " -> " + weapPath : "") + (alignChanged ? " -> " + alignPath : "") + (axisChanged ? " -> " + axisPath : ""));
        }
    }

    /// <summary>Green cross on the point every weapon turns around (alignment helper only).</summary>
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    public static class Patch_PivotCross
    {
        static void Postfix()
        {
            if (!AlignTool.CrossOn || Event.current.type != EventType.Repaint || Find.CurrentMap == null) return;
            Color old = GUI.color;
            foreach (VehiclePawn v in Find.CurrentMap.mapPawns.AllPawnsSpawned.OfType<VehiclePawn>())
            {
                if (v.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(v.VehicleDef) == null) continue;
                var comp = v.CompVehicleTurrets;
                if (comp == null) continue;
                foreach (VehicleTurret t in comp.Turrets)
                {
                    if (t == null || t.key == null || AlignTool.IsHidden(v, t.key.Split('_')[0])) continue;
                    Vector3 world = v.DrawPos + t.DrawPosition(v.FullRotation);
                    Vector2 sp = Find.Camera.WorldToScreenPoint(world);
                    float x = sp.x / Prefs.UIScale, y = (Screen.height - sp.y) / Prefs.UIScale;
                    GUI.color = new Color(0.1f, 1f, 0.1f);
                    Widgets.DrawBoxSolid(new Rect(x - 8f, y - 1f, 16f, 2f), GUI.color);
                    Widgets.DrawBoxSolid(new Rect(x - 1f, y - 8f, 2f, 16f), GUI.color);
                }
            }
            GUI.color = old;
        }
    }
}
