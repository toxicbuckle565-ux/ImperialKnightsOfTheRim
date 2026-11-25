using System.Collections.Generic;
using RimWorld;
using SmashTools;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// Knight hangar art (user 2026-11-21): the building graphic is the hangar with the bay hole open and no doors; this draws, every
    /// frame, the two blast-door halves (clipped to the hole so they slide under the platform), the platform band over them, the
    /// warning beacons (flash only while the doors cycle), the flickering candle glow, the ammo store and the catwalk above the Knight
    /// (open for the Armiger, half for the Questoris and Dominus, closed otherwise). Positions/sizes: HangarArtData.cs (generated).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HangarArt
    {
        const string P = "Things/Knights/Hangar/hangar_";
        public static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        // user 2026-11-23: dropping one Knight made the OTHER hangars' doors look open - the two door meshes were shared by every hangar, and
        // DrawMesh is deferred, so all hangars drew with the UVs (door clipping) of the last one drawn. Now each hangar has its own pair.
        static readonly Dictionary<int, Mesh[]> doorMeshes = new Dictionary<int, Mesh[]>();
        static Mesh[] DoorMeshes(Building_KnightHangar h)
        {
            if (!doorMeshes.TryGetValue(h.thingIDNumber, out Mesh[] m)) doorMeshes[h.thingIDNumber] = m = new[] { NewQuad(), NewQuad() };
            return m;
        }
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

        static HangarArt()
        {
            foreach (string n in HangarArtData.Layers.Keys)
                Mats[n] = MaterialPool.MatFrom(P + n, n.StartsWith("candles") ? ShaderDatabase.MoteGlow : ShaderDatabase.Transparent);
        }

        static Mesh NewQuad()
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float Seg(float t, float a, float b) { return Ease((t - a) / (b - a)); }

        /// <summary>Door cycle timeline in seconds: beacons from 0.4, sink 0.6-1.1, slide open 1.2-3.2, hold, slide shut 5.2-7.2, rise 7.3-7.8.</summary>
        public const float CycleSeconds = 8.4f;
        public static void Timeline(float t, out float drop, out float slide, out float beacon)
        {
            if (t < 0f || t > CycleSeconds) { drop = slide = beacon = 0f; return; }
            drop = Seg(t, 0.6f, 1.1f) - Seg(t, 7.3f, 7.8f);
            slide = Seg(t, 1.2f, 3.2f) - Seg(t, 5.2f, 7.2f);
            beacon = 0f;
            if (t >= 0.4f && t <= 8.2f)
            {
                float ph = ((t - 0.4f) * 1.6f) % 1f;                                   // rotating-beacon pulse, slight irregular flicker
                beacon = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ph * 2f * Mathf.PI)), 3f);
                beacon *= 0.85f + 0.15f * Mathf.Sin(t * 47f) * Mathf.Sin(t * 13f);
                beacon = Mathf.Clamp01(beacon);
            }
        }

        static void Quad(Vector3 picCentre, string name, float alt, Color c, float dx = 0f, float dz = 0f)
        {
            float[] r = HangarArtData.Layers[name];
            Vector3 p = new Vector3(picCentre.x + r[0] + dx, alt, picCentre.z + r[1] + dz);
            mpb.SetColor(ShaderPropertyIDs.Color, c);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p, Quaternion.identity, new Vector3(r[2], 1f, r[3])), Mats[name], 0, null, 0, mpb);
        }

        /// <summary>A door half shifted by dx, clipped to the hole's x range (the part under the platform is not drawn).</summary>
        static void Door(Mesh mesh, Vector3 picCentre, string name, float alt, float dx, float dz, Color c)
        {
            float[] r = HangarArtData.Layers[name];
            float x0 = r[0] - r[2] / 2f + dx, x1 = r[0] + r[2] / 2f + dx;
            float c0 = Mathf.Max(x0, HangarArtData.HoleX0), c1 = Mathf.Min(x1, HangarArtData.HoleX1);
            if (c1 - c0 < 0.01f) return;
            float u0 = (c0 - x0) / r[2], u1 = (c1 - x0) / r[2];
            mesh.uv = new[] { new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f) };
            Vector3 p = new Vector3(picCentre.x + (c0 + c1) / 2f, alt, picCentre.z + r[1] + dz);
            mpb.SetColor(ShaderPropertyIDs.Color, c);
            Graphics.DrawMesh(mesh, Matrix4x4.TRS(p, Quaternion.identity, new Vector3(c1 - c0, 1f, r[3])), Mats[name], 0, null, 0, mpb);
        }

        public static void Draw(Building_KnightHangar h)
        {
            Vector3 pic = h.TrueCenter() + new Vector3(HangarArtData.OffX, 0f, HangarArtData.OffZ);
            float floor = AltitudeLayer.FloorEmplacement.AltitudeFor();
            h.DoorSounds();                                             // 2026-11-22 sound design
            float t = h.DoorTimelineSeconds;
            Timeline(t, out float drop, out float slide, out float beacon);
            float dark = 1f - 0.18f * drop;
            Color dc = new Color(dark, dark, dark, 1f);
            Mesh[] dm = DoorMeshes(h);
            Door(dm[0], pic, "doorL", floor + 0.010f, -slide * HangarArtData.Slide, -drop * HangarArtData.Drop, dc);
            Door(dm[1], pic, "doorR", floor + 0.011f, slide * HangarArtData.Slide, -drop * HangarArtData.Drop, dc);
            Quad(pic, "top_off", floor + 0.014f, Color.white);
            if (beacon > 0.01f) Quad(pic, "top_on", floor + 0.015f, new Color(1f, 1f, 1f, beacon));
            // candles: one glow layer per cluster, each with its own flicker (user 2026-11-21: not all in sync)
            for (int i = 0; HangarArtData.Layers.ContainsKey("candles_" + i); i++)
            {
                float fl = CandleFlicker.Value(h.thingIDNumber * 31 + i);
                Quad(pic, "candles_" + i, AltitudeLayer.PawnState.AltitudeFor() - 0.045f, new Color(fl, fl, fl, fl));   // above the overhang redraw (candles 4/5 sit on the gantry)
            }
            string ammo = h.AmmoLayer;
            if (ammo != null) Quad(pic, ammo, floor + 0.017f, Color.white);
            // catwalk above the Knight
            Quad(pic, "catwalk_" + h.CatwalkVariant, AltitudeLayer.MoteOverheadLow.AltitudeFor() - 0.01f, Color.white);
            DrawOverhang(h, pic);
        }

        // Workshop report 2026-11-24 (RiqueGaming): "scaffolding/crane displays under grass". The building graphic is printed at
        // FloorEmplacement, but its back part (gantry, cranes, pillars) hangs over cells OUTSIDE the 20x11 footprint where grass still
        // grows, and LowPlant draws above FloorEmplacement. Redraw that strip above plants AND trees (trees draw on the Building layer; user screenshot 2026-11-24) - and over PAWNS too (user 2026-11-24: "pawns should show below the gantry"; the cells behind the hangar are behind it in this view). Just under PawnState: the docked Knight (drawn at PawnState) and the catwalk stay on top.
        static Mesh overhangMesh;
        static void DrawOverhang(Building_KnightHangar h, Vector3 pic)
        {
            float picBottom = pic.z - HangarArtData.DrawH / 2f;
            float footTop = h.TrueCenter().z + h.def.size.z / 2f;
            float v0 = Mathf.Clamp01((footTop - picBottom) / HangarArtData.DrawH);
            if (v0 >= 0.999f) return;
            if (overhangMesh == null)
            {
                overhangMesh = NewQuad();
                overhangMesh.uv = new[] { new Vector2(0f, v0), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, v0) };
            }
            float hgt = HangarArtData.DrawH * (1f - v0);
            Vector3 p = new Vector3(pic.x, AltitudeLayer.PawnState.AltitudeFor() - 0.05f, footTop + hgt / 2f);
            Graphics.DrawMesh(overhangMesh, Matrix4x4.TRS(p, Quaternion.identity, new Vector3(HangarArtData.DrawW, 1f, hgt)), h.Graphic.MatSingle, 0);
        }
    }

    public partial class Building_KnightHangar
    {
        int doorCycleStart = -1;
        float doorHoldExtra;            // seconds the doors stay fully open beyond the normal hold (orbital drop: until this Knight is gone)
        const float DoorHoldEnd = 5.2f; // HangarArt.Timeline: the doors start closing here

        /// <summary>Seconds into the current door cycle (-1 when the doors are shut and idle).</summary>
        public float DoorCycleSeconds
        {
            get
            {
                if (doorCycleStart < 0) return -1f;
                float t = (Find.TickManager.TicksGame - doorCycleStart) / 60f;
                if (t > HangarArt.CycleSeconds + doorHoldExtra) { doorCycleStart = -1; doorHoldExtra = 0f; return -1f; }
                return t;
            }
        }

        /// <summary>The door timeline time: stands still at the end of the hold for doorHoldExtra seconds (user 2026-11-22: all hangars
        /// open together, each stays open until its own Knight has dropped).</summary>
int doorOpenSnd = -1, doorCloseSnd = -1;                       // the door cycle (its start tick) each sound already played for

        /// <summary>Hangar sounds (user 2026-11-22): klaxon + doors grinding open when a cycle starts, grinding shut when they close.</summary>
        public void DoorSounds()
        {
            if (doorCycleStart < 0 || !Spawned) return;
            float t = (Find.TickManager.TicksGame - doorCycleStart) / 60f;
            if (t >= 0f && doorOpenSnd != doorCycleStart)
            {
                doorOpenSnd = doorCycleStart;
                DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_hangar_doors_open")?.PlayOneShot(new TargetInfo(Position, Map));
            }
            if (t >= DoorHoldEnd + doorHoldExtra && doorCloseSnd != doorCycleStart)
            {
                doorCloseSnd = doorCycleStart;
                DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_hangar_doors_close")?.PlayOneShot(new TargetInfo(Position, Map));
            }
        }

        public float DoorTimelineSeconds
        {
            get
            {
                float t = DoorCycleSeconds;
                if (t <= DoorHoldEnd) return t;
                return Mathf.Max(DoorHoldEnd, t - doorHoldExtra);
            }
        }

        /// <summary>Open the bay doors (beacons, sink, slide), hold, and close them again.</summary>
        public void PlayDoorCycle()
        {
            if (Spawned) { doorCycleStart = Find.TickManager.TicksGame; doorHoldExtra = 0f; }
        }

        /// <summary>The cell a Knight stands on in the drawn bay (the art is drawn at the true 35-degree angle, so the bay is below centre).</summary>
        public IntVec3 BayCell { get { return Position + new IntVec3(HangarArtData.BayDX, 0, HangarArtData.BayDZ); } }

        /// <summary>Where a Knight of this class stands in the hangar: the bay cell moved back by FeetDZ so its FEET (not its picture
        /// centre) are on the drawn bay, like the knights_in_hangar previews (user 2026-11-21: 'make it fit like the preview images').</summary>
        /// <summary>A docked Knight that is idle but not on its bay spot (docked before the 2026-11-21 resize, or nudged) walks onto it.</summary>
        int settledKnightId = -1, walkTriedId = -1;
        public void SettleOnBay(VehiclePawn v)
        {
            if (v == null || !v.Spawned || v.Map != Map) return;
            // NOT skipped when drafted: a Knight ordered to dock stays drafted after it arrives (that blocked the first fix).
            // A Knight the player is driving is moving or has a Goto job, so it is still left alone.
            if (v.vehiclePather != null && v.vehiclePather.Moving) return;
            if (v.CurJob != null && v.CurJob.def == JobDefOf.Goto) return;
            IntVec3 c = KnightCellFor(v);                                       // from where the Knight is drawn (feet on the drawn bay)
            if (Prefs.DevMode) Log.Message("[LIK] hangar " + thingIDNumber + ": docked " + v.LabelShort + " at " + v.Position + " (drawn " + v.DrawPos.ToString("F2") + "), bay spot " + c + (v.Position == c ? " (ok)" : " -> placing"));
            if (v.Position == c) return;
            // no walking: Vehicle Framework shifts even-sized vehicles by a cell when placed, so a walk to a spot one cell away is a
            // no-op (log 2026-11-21: 'walk accepted' but it never moved). Place it, see where it landed, correct by that shift once.
            if (settledKnightId == v.thingIDNumber) return;                         // once per docked Knight (no retry loop)
            settledKnightId = v.thingIDNumber;
            IntVec3 was = v.Position;
            v.DeSpawn(); GenSpawn.Spawn(v, c, Map, Rot4.South, WipeMode.VanishOrMoveAside);
            if (v.Spawned && v.Position != c)
            {
                IntVec3 shift = c - v.Position;
                v.DeSpawn(); GenSpawn.Spawn(v, c + shift, Map, Rot4.South, WipeMode.VanishOrMoveAside);
            }
            v.GetComp<CompKnightHead>()?.Apply();
            if (Prefs.DevMode) Log.Message("[LIK] hangar " + thingIDNumber + ": placed " + v.LabelShort + " from " + was + " -> wanted " + c + ", now at " + v.Position + " (drawn " + v.DrawPos.ToString("F2") + ")" + (v.Spawned ? "" : " (NOT spawned)"));
        }

        /// <summary>Exact picture-centre offset above the feet (minus 0.4) for this Knight's class (cells).</summary>
        static float FeetZ(VehicleDef def)
        {
            KnightClassDef cls = def != null ? KnightLoadout.ClassFor(def) : null;
            string n = cls?.defName ?? def?.defName ?? "";
            foreach (KeyValuePair<string, float> kv in HangarArtData.FeetZ) if (n.Contains(kv.Key)) return kv.Value;
            return 0f;
        }

        // FEET ON THE FRONT OF THE BAY (user 2026-11-22, matches INGAME_placement_mock): every Knight stands with its feet just behind the
        // bay's front hazard line. Measured in anim_base_style.png (128 px/cell): bay centre y 1680, hazard line top y 2105 -> feet 0.25 cells
        // behind it = 3.07 cells in front of the bay centre. Knight pictures are centred horizontally, so x = bay centre.
        const float FeetFromBayCentre = -3.07f;

        /// <summary>Exact point (cells, world) where this class's PICTURE CENTRE goes so it stands centred in the bay, feet on the front.</summary>
        public Vector3 KnightDrawSpot(VehicleDef def)
        {
            return this.TrueCenter() + new Vector3(HangarArtData.BayX, 0f, HangarArtData.BayZ + FeetFromBayCentre + FeetZ(def) + 0.4f);
        }

        /// <summary>Cell an undocked / newly assembled Knight stands on (the game draws it centred on that cell).</summary>
        public IntVec3 KnightCell(VehicleDef def)
        {
            Vector3 t = KnightDrawSpot(def);
            return new IntVec3(Mathf.RoundToInt(t.x), 0, Mathf.RoundToInt(t.z));
        }

        // DOCKED POSE = THE PREVIEWS (user 2026-11-22, knights_in_hangar.py): feet 0.4 cells below the bay centre, on the eagle emblem.
        // Only the clamped Knight is drawn there; a Knight on the map stands at KnightCell (front of the bay) so its rect fits the hangar.
        const float DockedFeetFromBayCentre = -0.4f;

        /// <summary>Where a docked (clamped) Knight's picture centre is drawn: the preview pose, exact (not rounded to a cell).</summary>
        public Vector3 StowedDrawPos(VehicleDef def)
        {
            return this.TrueCenter() + new Vector3(HangarArtData.BayX, 0f, HangarArtData.BayZ + DockedFeetFromBayCentre + FeetZ(def) + 0.4f);
        }

        public IntVec3 KnightCellFor(VehiclePawn v) { return KnightCell(v?.VehicleDef); }

        /// <summary>Which catwalk layer: open for the Armiger, half for the Questoris and Dominus, closed for the big Knights and an empty bay.</summary>
        public string CatwalkVariant
        {
            get
            {
                VehiclePawn v = docked;
                KnightClassDef cls = v != null ? KnightLoadout.ClassFor(v.VehicleDef) : null;
                string n = cls?.defName ?? "";
                if (n.Contains("Armiger")) return "open";
                if (n.Contains("Questoris") || n.Contains("Dominus")) return "half";
                return "closed";
            }
        }

        /// <summary>Ammo store picture. No stock system yet: always full (a later step will tie it to stored ammo).</summary>
        public string AmmoLayer { get { return "ammo_100"; } }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            HangarArt.Draw(this);                 // the building graphic itself is printed in the map mesh
        }
    }
}
