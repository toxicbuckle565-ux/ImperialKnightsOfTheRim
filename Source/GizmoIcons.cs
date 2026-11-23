using System.Collections.Generic;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// Hangar button icons (user 2026-11-22): each icon shows the Knight class in that hangar. Pictures from Tools/gen_gizmo_icons.py:
    /// UI/Commands/Knights/LIK_&lt;name&gt;_&lt;Class&gt; per class, UI/Commands/LIK_&lt;name&gt; generic (Questoris; empty bay / no Knight).
    /// </summary>
    public static class GizmoIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name, VehicleDef knight, Texture2D fallback)
        {
            string cls = knight != null ? KnightLoadout.ClassFor(knight)?.defName : null;
            if (cls != null && cls.StartsWith("LIK_Class_")) cls = cls.Substring("LIK_Class_".Length);
            string key = name + "|" + (cls ?? "");
            if (!cache.TryGetValue(key, out Texture2D t))
            {
                if (cls != null) t = ContentFinder<Texture2D>.Get("UI/Commands/Knights/" + name + "_" + cls, false);
                if (t == null) t = ContentFinder<Texture2D>.Get("UI/Commands/" + name, false);
                cache[key] = t;
            }
            return t ?? fallback;
        }
    }
}

namespace LIK
{
    using RimWorld;

    /// <summary>
    /// In-game painted button icons (user 2026-11-22: "build them in" - each Knight in its OWN paint): the glyph layer under the Knight,
    /// the Knight's body in its current colours (KnightPaint.DrawFacing, the paint window's own drawing; no stickers at icon size), then
    /// the glyph layer over it. Layers + Knight rects: Tools/gen_gizmo_icons.py --install (Textures/UI/Commands/Layers, GizmoIconData.cs).
    /// Falls back to the static per-class picture (the command's icon) when the Knight is not paintable or data is missing.
    /// </summary>
    public static class KnightIconDraw
    {
        static readonly Dictionary<string, Texture2D> layers = new Dictionary<string, Texture2D>();

        static Texture2D Layer(string name)
        {
            if (!layers.TryGetValue(name, out Texture2D t)) { t = ContentFinder<Texture2D>.Get("UI/Commands/Layers/" + name, false); layers[name] = t; }
            return t;
        }

        /// <summary>True if it drew the icon.</summary>
        public static bool Draw(Command c, Rect rect, VehiclePawn knight, string iconName, GizmoRenderParms parms)
        {
            if (knight == null || iconName == null || !KnightPaint.Paintable(knight.VehicleDef)) return false;
            KnightClassDef cls = KnightLoadout.ClassFor(knight.VehicleDef);
            if (cls == null) return false;
            string clsName = cls.defName.StartsWith("LIK_Class_") ? cls.defName.Substring("LIK_Class_".Length) : cls.defName;
            if (!GizmoIconData.Knight.TryGetValue(iconName + "|" + clsName, out float[] k)) return false;
            try
            {
                float s = rect.width * 0.85f * c.iconDrawScale;                 // same fit as Command.DrawIcon
                Rect inner = new Rect(rect.center.x - s / 2f, rect.center.y - s / 2f, s, s);
                Color tint = c.Disabled && !parms.lowLight ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;
                if (parms.lowLight) tint.a = 0.6f;
                GUI.color = tint;
                Texture2D under = Layer(iconName + "_under"), over = Layer(iconName + "_over");
                if (under != null) GUI.DrawTexture(inner, under);
                float u = s / 128f;
                KnightPaint.DrawFacing(new Rect(inner.x + k[0] * u, inner.y + k[1] * u, k[2] * u, k[3] * u), cls, KnightPaint.Current(knight), "south", null);
                GUI.color = tint;
                if (over != null) GUI.DrawTexture(inner, over);
                return true;
            }
            finally { GUI.color = Color.white; }
        }
    }

    /// <summary>A hangar button whose icon is the Knight in its own paint.</summary>
    public class Command_KnightAction : Command_Action
    {
        public VehiclePawn knight;
        public string iconName;
        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            if (!KnightIconDraw.Draw(this, rect, knight, iconName, parms)) base.DrawIcon(rect, buttonMat, parms);
        }
    }

    public class Command_KnightToggle : Command_Toggle
    {
        public VehiclePawn knight;
        public string iconName;
        public override void DrawIcon(Rect rect, Material buttonMat, GizmoRenderParms parms)
        {
            if (!KnightIconDraw.Draw(this, rect, knight, iconName, parms)) base.DrawIcon(rect, buttonMat, parms);
        }
    }
}
