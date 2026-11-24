using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// Range display of a Knight weapon (shown while its gizmo is hovered or it is being aimed): on top of Vehicle Framework's range circle / arc lines,
    /// shade the ground inside the range (and arc) that the weapon can NOT hit because something solid is in the way - the "shadow" behind walls and rock.
    /// Uses the same test Vehicle Framework uses when it accepts a target: GenSight.LineOfSight from the Knight's position to the cell.
    /// </summary>
    [HarmonyPatch(typeof(VehicleTurret), "DrawTargeter")]
    public static class Patch_RangeShadow
    {
        static void Postfix(VehicleTurret __instance)
        {
            if (!__instance.GizmoHighlighted && TurretTargeter.Turret != __instance) return;
            VehiclePawn v = __instance.vehicle;
            if (v == null || !v.Spawned || v.def.GetModExtension<DefModExtension_KnightToll>() == null) return;
            RangeShadow.Draw(__instance, v);
        }
    }

    [StaticConstructorOnStartup]
    public static class RangeShadow
    {
        static Mesh mesh;
        static Material mat;
        static readonly List<Vector3> verts = new List<Vector3>();
        static readonly List<int> tris = new List<int>();
        static bool[] seen = new bool[0];

        // what the cached mesh was built for
        static VehicleTurret keyTurret;
        static IntVec3 keyRoot;
        static int keyAngle, keyMapId;
        static float keyMin, keyMax, builtAt;

        const float MinRebuildGap = 0.25f;   // seconds between rebuilds while the Knight is moving / turning
        const float RefreshEvery = 2f;       // rebuild anyway (walls get built and destroyed)

        public static void Draw(VehicleTurret t, VehiclePawn v)
        {
            ThingDef proj = t.ProjectileDef;
            if (proj?.projectile == null || proj.projectile.flyOverhead) return;   // indirect fire is not blocked by walls
            if (KnightShotUtil.PierceOf(t) > 0f) return;                           // piercing beams burn through cover
            Map map = v.Map;
            float max = t.MaxRange, min = t.MinRange;
            if (max <= 0f || max >= 9999f) return;
            int angle = Mathf.RoundToInt(v.Rotation.AsAngle + v.Angle);
            float now = Time.realtimeSinceStartup;
            bool changed = keyTurret != t || keyRoot != v.Position || keyAngle != angle || keyMapId != map.uniqueID || keyMin != min || keyMax != max;
            if (mesh == null || (changed && now - builtAt > MinRebuildGap) || now - builtAt > RefreshEvery || keyTurret != t)
            {
                keyTurret = t; keyRoot = v.Position; keyAngle = angle; keyMapId = map.uniqueID; keyMin = min; keyMax = max; builtAt = now;
                Build(t, v, map, min, max);
            }
            if (mesh.vertexCount == 0) return;
            if (mat == null) mat = SolidColorMaterials.NewSolidColorMaterial(new Color(0.85f, 0.12f, 0.10f, 0.28f), ShaderDatabase.MetaOverlay);
            Graphics.DrawMesh(mesh, new Vector3(0f, AltitudeLayer.MetaOverlays.AltitudeFor(), 0f), Quaternion.identity, mat, 0);
        }

        static float Clamp360(float a) { a %= 360f; return a < 0f ? a + 360f : a; }

        static void Build(VehicleTurret t, VehiclePawn v, Map map, float min, float max)
        {
            if (mesh == null) { mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic(); }
            verts.Clear(); tris.Clear();

            IntVec3 root = v.Position;
            Vector3 tl = t.TurretLocation;
            int x0 = Mathf.Max(0, Mathf.Min(root.x, Mathf.FloorToInt(tl.x - max)));
            int x1 = Mathf.Min(map.Size.x - 1, Mathf.Max(root.x, Mathf.CeilToInt(tl.x + max)));
            int z0 = Mathf.Max(0, Mathf.Min(root.z, Mathf.FloorToInt(tl.z - max)));
            int z1 = Mathf.Min(map.Size.z - 1, Mathf.Max(root.z, Mathf.CeilToInt(tl.z + max)));
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            if (w <= 0 || h <= 0 || !root.InBounds(map)) { mesh.Clear(); return; }
            if (seen.Length < w * h) seen = new bool[w * h];
            else System.Array.Clear(seen, 0, w * h);

            // 1) sweep: walk a sight line from the Knight to every cell on the edge of the area; everything walked over before the first blocker is in view
            for (int x = x0; x <= x1; x++) { Walk(root, x, z0, map, x0, z0, w); Walk(root, x, z1, map, x0, z0, w); }
            for (int z = z0 + 1; z < z1; z++) { Walk(root, x0, z, map, x0, z0, w); Walk(root, x1, z, map, x0, z0, w); }

            // the weapon's arc (same maths as VehicleTurret.AngleBetween)
            Vector2 arc = t.angleRestricted;
            bool arcOn = arc != Vector2.zero;
            float a0 = 0f, span = 360f;
            if (arcOn)
            {
                float b = t.attachedTo != null ? t.attachedTo.TurretRotation : v.Rotation.AsAngle + v.Angle;
                a0 = Clamp360(arc.x + b);
                span = Clamp360(arc.y + b) - a0;
                if (span < 0f) span += 360f;
            }
            float min2 = min * min, max2 = max * max;
            FogGrid fog = map.fogGrid;

            // 2) every cell the sweep did not reach, inside range + arc: confirm with the exact test, then merge runs along each row into one quad
            for (int z = z0; z <= z1; z++)
            {
                int runStart = -1;
                for (int x = x0; x <= x1 + 1; x++)
                {
                    bool shade = false;
                    if (x <= x1 && !seen[(z - z0) * w + (x - x0)])
                    {
                        float dx = x - tl.x, dz = z - tl.z;      // Vehicle Framework measures range to the cell's integer coordinates
                        float d2 = dx * dx + dz * dz;
                        if (d2 >= min2 && d2 <= max2)
                        {
                            IntVec3 c = new IntVec3(x, 0, z);
                            if (!fog.IsFogged(c))
                            {
                                bool inArc = true;
                                if (arcOn)
                                {
                                    float ang = Clamp360(new Vector3(x + 0.5f - tl.x, 0f, z + 0.5f - tl.z).AngleFlat() - a0);
                                    inArc = ang < span;
                                }
                                if (inArc && !GenSight.LineOfSight(root, c, map)) shade = true;
                            }
                        }
                    }
                    if (shade) { if (runStart < 0) runStart = x; }
                    else if (runStart >= 0) { Quad(runStart, x, z); runStart = -1; }
                }
            }

            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(new Vector3(map.Size.x / 2f, 0f, map.Size.z / 2f), new Vector3(map.Size.x, 10f, map.Size.z));
        }

        /// <summary>Same cell walk as GenSight.LineOfSight; marks cells in view. The first blocking cell is marked too (a wall's face can be shot), then the walk stops.</summary>
        static void Walk(IntVec3 start, int ex, int ez, Map map, int x0, int z0, int w)
        {
            bool flag = start.x != ex ? start.x < ex : start.z < ez;
            int dx = Mathf.Abs(ex - start.x), dz = Mathf.Abs(ez - start.z);
            int x = start.x, z = start.z;
            int n = 1 + dx + dz;
            int sx = ex > start.x ? 1 : -1, sz = ez > start.z ? 1 : -1;
            dx *= 4; dz *= 4;
            int err = dx / 2 - dz / 2;
            IntVec3 c = default(IntVec3);
            while (n > 0)
            {
                seen[(z - z0) * w + (x - x0)] = true;
                c.x = x; c.z = z;
                if (!c.CanBeSeenOverFast(map)) return;
                if (err > 0 || (err == 0 & flag)) { x += sx; err -= dz; }
                else { z += sz; err += dx; }
                n--;
            }
        }

        static void Quad(int xa, int xb, int z)
        {
            int i = verts.Count;
            verts.Add(new Vector3(xa, 0f, z));
            verts.Add(new Vector3(xa, 0f, z + 1));
            verts.Add(new Vector3(xb, 0f, z + 1));
            verts.Add(new Vector3(xb, 0f, z));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
    }
}
