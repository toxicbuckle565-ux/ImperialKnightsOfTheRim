using System.Collections.Generic;
using HarmonyLib;
using SmashTools;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// Where the MUZZLE of a weapon really is (user 2026-11-11, screenshot: "the charge up and shot are off from the actual weapon").
    /// Vehicle Framework launches every shot from  turret position + projectileOffset along the aim, one number per weapon for every facing. The
    /// Knight weapons are 35-degree pictures, a different one per facing, so the muzzle sits at a different distance (and a little to one side)
    /// in each. Tools/gen_assembly.py measures the tip of the barrel in every turret picture and writes it here:
    ///   side = "base" | "north" | "east" | "south" | "west", with "|L" appended for the mirrored arm picture
    ///   pos  = (sideways, forward) in cells from the turret's turn point, in the weapon's own aim frame (forward = where it points)
    /// </summary>
    public class MuzzlePoint
    {
        public string side;
        public Vector2 pos;
    }

    public class DefModExtension_Muzzle : DefModExtension
    {
        public List<MuzzlePoint> points;
        Dictionary<string, Vector2> map;

        public bool TryGet(string side, out Vector2 pos)
        {
            if (map == null)
            {
                map = new Dictionary<string, Vector2>();
                if (points != null) foreach (MuzzlePoint p in points) if (p.side != null) map[p.side] = p.pos;
            }
            return map.TryGetValue(side, out pos);
        }
    }

    public static class MuzzleUtil
    {
        /// <summary>The muzzle in the weapon's aim frame for the way the Knight faces right now (same picture choice as FacingArt), incl. the alignment nudge.</summary>
        public static bool Local(VehicleTurret t, out Vector2 pos)
        {
            pos = Vector2.zero;
            if (t == null || t.def == null || t.vehicle == null) return false;
            DefModExtension_Muzzle ext = t.def.GetModExtension<DefModExtension_Muzzle>();
            if (ext == null) return false;

            Rot8 o = t.vehicle.FullRotation;
            string side = o == Rot8.North ? "north" : o == Rot8.East ? "east" : o == Rot8.South ? "south" : o == Rot8.West ? "west" : null;
            string dn = t.def.defName;
            string key = t.key ?? "";
            bool preOriented = dn.StartsWith("LIK_Turret_armiger_") || dn.StartsWith("LIK_Turret_questoris_") || dn.StartsWith("LIK_Turret_dominus_");
            bool isRight = key.StartsWith("armR") || key.StartsWith("shoulderR") || key.StartsWith("headR");
            if (!preOriented && side == "south") isRight = !isRight;

            float sideAngle = o.AsAngle;
            if (side != null && FacingArt.AimMode(t))
            {
                // aim pictures: the picture shown is the one for where the weapon points (see FacingArt)
                float snapped;
                string aimSide = FacingArt.SideOfAngle(t.TurretRotation, out snapped);
                Vector2 tmp;
                if (ext.TryGet(aimSide, out tmp) || ext.TryGet(aimSide + "|L", out tmp)) { side = aimSide; sideAngle = snapped; }
            }
            bool facingPic = false;
            if (side != null)
            {
                if (isRight && ext.TryGet(side + "|L", out pos)) facingPic = true;
                else if (ext.TryGet(side, out pos)) facingPic = true;
            }
            if (!facingPic && !ext.TryGet("base", out pos)) return false;

            // the alignment nudge slides the PICTURE (Patch_TurretAxisShift), in the picture's own frame: turn it into the aim frame
            if (side != null)
            {
                Vector2 a = AlignTool.GetAxis(t.vehicle, key.Split('_')[0], side, AlignTool.WeaponOf(t));
                if (a != Vector2.zero)
                {
                    float deg = facingPic && preOriented ? sideAngle : (!facingPic && dn.StartsWith("LIK_Turret_acastus_") && key.StartsWith("socket") ? 180f : 0f);
                    float r = deg * Mathf.Deg2Rad, sn = Mathf.Sin(r), cs = Mathf.Cos(r);
                    pos.x += a.x * cs - a.y * sn;       // along the aim's right-hand side
                    pos.y += a.x * sn + a.y * cs;       // along the aim
                }
            }
            return true;
        }

        public static Vector3 World(VehicleTurret t)
        {
            Vector2 p;
            if (!Local(t, out p)) p = new Vector2(0f, t.def.projectileOffset);
            return t.TurretLocation + new Vector3(p.x, 1f, p.y).RotatedBy(t.TurretRotation);
        }
    }

    /// <summary>
    /// Just before a Knight weapon fires, its def's launch offsets are set to the muzzle of the picture shown right now. (The def is shared by
    /// every Knight carrying that weapon, but it is rewritten before each shot and shots are fired one at a time.)
    /// </summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.FireTurret))]
    public static class Patch_KnightMuzzle
    {
        static readonly Dictionary<VehicleTurretDef, List<float>> barrels = new Dictionary<VehicleTurretDef, List<float>>();   // the def's own sideways barrel offsets

        static void Prefix(VehicleTurret __instance)
        {
            VehicleTurretDef def = __instance.def;
            if (def == null || def.defName == null || !def.defName.StartsWith("LIK_Turret_")) return;
            Vector2 p;
            if (!MuzzleUtil.Local(__instance, out p)) return;

            List<float> own;
            if (!barrels.TryGetValue(def, out own))
            {
                own = def.projectileShifting != null ? new List<float>(def.projectileShifting) : new List<float>();
                barrels[def] = own;
                if (def.projectileShifting == null) def.projectileShifting = new List<float>();
            }
            def.projectileOffset = p.y;
            List<float> list = def.projectileShifting;
            if (own.Count == 0)
            {
                if (list.Count == 0) list.Add(p.x); else list[0] = p.x;
            }
            else
                for (int i = 0; i < own.Count && i < list.Count; i++) list[i] = own[i] + p.x;
        }
    }
}
