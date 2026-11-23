using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// REDESIGNED ORBITAL DROP, SHIP SIDE (user 2026-11-22: "doors open, Knight falls"; ship stays in orbit; watch the launch, then cut to
    /// the landing). Per Knight: beacons + bay doors open (the hangar's door cycle), the clamps let go with a jolt and a burst of steam, and
    /// the Knight drops down the shaft - shrinking and fading into the dark - then the doors close. The camera watches the first launch on
    /// the ship, then cuts to the landing map just before the first Knight comes down in its burning re-entry (KnightDropFaller).
    /// Visual only: the Knight is already inside its faller; nothing here is saved (a save mid-launch simply skips the rest of the show).
    /// </summary>
    public partial class Building_KnightHangar
    {
        // timeline (seconds from the start of this hangar's door cycle): doors fully open at 3.2 s (HangarArt.Timeline)
        public const float ReleaseAt = 3.3f;
        const float Gravity = 15f;                 // cells/s^2 on screen (v3: the drop to the lip took ~1.5 s)
        const float FallBeyondShip = 45f;          // cells it keeps falling past the ship's south edge before it is gone
        /// <summary>Ticks from this hangar's launch start until its Knight is gone below the ship (the landing fall starts then).</summary>
        public int LaunchLeadTicks { get { return Mathf.RoundToInt((releaseAt + fallSecs) * 60f) + 20; } }
        float releaseAt = ReleaseAt;               // this hangar's release (s from the shared door start): ReleaseAt + its place x drop gap
        float fallSecs = 1.5f;
        float[] shipEdge; int edgeX0;              // per map column: world z of the ship's south edge below the hangar (Knight hidden north of it)

        VehiclePawn launchKnight;
        int launchStart = -1;
        bool released;
        RenderTexture snap; Material snapMat; float snapSize; bool snapFailed;

        /// <summary>Start this hangar's launch show at the given tick (later Knights of a drop wait their turn, still standing in the bay).</summary>
        public void BeginLaunch(VehiclePawn v, int startTick, float releaseSeconds = ReleaseAt)
        {
            FreeSnapshot();
            launchKnight = v; launchStart = startTick; released = false; snapFailed = false;
            doorCycleStart = startTick;                                  // DoorCycleSeconds is negative until then (doors stay shut)
            releaseAt = Mathf.Max(ReleaseAt, releaseSeconds);
            ComputeShipEdge(v);
            // doors stay open until this Knight's top has passed under the bay's front lip (+0.4 s)
            Vector3 spot = StowedDrawPos(v.VehicleDef);
            float lip = (this.TrueCenter() + new Vector3(HangarArtData.BayX, 0f, HangarArtData.BayZ)).z - 3.32f;
            float tLip = Mathf.Sqrt(2f * Mathf.Max(0f, spot.z - (lip - VisibleTop(v))) / Gravity);
            doorHoldExtra = Mathf.Max(0f, releaseAt + tLip + 0.4f - DoorHoldEnd);
        }

        /// <summary>User 2026-11-22: after it drops under the bay's lip it is hidden by the ship, then seen again falling into space below
        /// the gravship. For every map column under the Knight: walk south from the hangar while the cell is gravship floor.</summary>
        void ComputeShipEdge(VehiclePawn v)
        {
            var ship = new HashSet<IntVec3>();
            if (ModsConfig.OdysseyActive)
                foreach (Building_GravEngine e in Map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
                    if (e.ValidSubstructureAt(Position)) { ship.UnionWith(e.ValidSubstructure); break; }
            CellRect hr = this.OccupiedRect();
            float w = Mathf.Max(v.VehicleDef.graphicData.drawSize.x, v.VehicleDef.graphicData.drawSize.y) * 1.35f;
            Vector3 spot = StowedDrawPos(v.VehicleDef);
            edgeX0 = Mathf.FloorToInt(spot.x - w / 2f);
            int n = Mathf.CeilToInt(spot.x + w / 2f) - edgeX0 + 1;
            shipEdge = new float[n];
            float lowest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int x = edgeX0 + i;
                int z = hr.minZ;                                         // the hangar itself always covers its own rows
                while (z - 1 >= 0 && ship.Contains(new IntVec3(x, 0, z - 1))) z--;
                shipEdge[i] = z;                                         // cells below z are open space
                lowest = Mathf.Min(lowest, z);
            }
            // fall until the Knight's top is FallBeyondShip cells past the lowest ship edge
            float dist = spot.z - (lowest - VisibleTop(v) - FallBeyondShip);
            fallSecs = Mathf.Sqrt(2f * Mathf.Max(1f, dist) / Gravity);
        }

        /// <summary>user 2026-11-23: purple rectangles left in a hangar - the launch only ended while it was being DRAWN, so when the camera left
        /// the ship before the fall was over (or never came back) it stayed on, doors open, and its snapshot (a RenderTexture, wiped from video
        /// memory meanwhile) was drawn blank, tinted by the plasma reactor's light. Now it also ends on the game clock (TickRare).</summary>
        public void EndStaleLaunch()
        {
            if (launchKnight != null && LaunchSeconds >= releaseAt + fallSecs + 0.5f) { launchKnight = null; launchStart = -1; FreeSnapshot(); }
        }

        float LaunchSeconds { get { return launchStart < 0 ? -999f : (Find.TickManager.TicksGame - launchStart) / 60f; } }

        void FreeSnapshot()
        {
            if (snap != null) { snap.Release(); Object.Destroy(snap); snap = null; }
            if (snapMat != null) { Object.Destroy(snapMat); snapMat = null; }
        }

        /// <summary>Called every frame by MapComponent_StowedKnights while a launch is on.</summary>
        public void DrawLaunch()
        {
            VehiclePawn v = launchKnight;
            if (v == null) return;
            float t = LaunchSeconds;
            Vector3 spot = StowedDrawPos(v.VehicleDef);
            float alt = AltitudeLayer.PawnState.AltitudeFor();
            if (t < releaseAt)
            {
                // still clamped; a shudder as the clamps let go
                if (t > releaseAt - 0.35f) spot += new Vector3(Rand.Range(-0.04f, 0.04f), 0f, Rand.Range(-0.04f, 0.04f));
                OrbitalDropDraw.DrawKnight(v, spot, alt);
                return;
            }
            if (!released) { released = true; OnRelease(v, spot); }
            float tf = t - releaseAt;
            if (tf >= fallSecs) { launchKnight = null; launchStart = -1; FreeSnapshot(); return; }
            if (snap != null && !snap.IsCreated()) { FreeSnapshot(); snapFailed = false; }   // lost from video memory: take it again
            if (snap == null && !snapFailed)
            {
                snap = KnightSnapshot.Take(v, out snapSize);
                if (snap == null) snapFailed = true;
                else snapMat = new Material(ShaderDatabase.Transparent) { mainTexture = snap };
            }
            if (snapMat == null) return;                                 // no snapshot: it is simply gone down the shaft (steam covers it)
            // v3 (user 2026-11-22: 'the shrinking looks whack'): the map camera has no perspective, so a drop straight down in the hangar's
            // 35-degree view is a slide straight SOUTH at the SAME size, under gravity. v4 (user: 'still show it falling below the gravship'):
            // seen in the bay down to the front lip (hazard line top, 3.32 cells in front of the bay centre), hidden under the ship, then
            // seen again in space past the ship's south edge (per column), fading out as it falls away.
            float s = snapSize;
            Vector3 shaft = this.TrueCenter() + new Vector3(HangarArtData.BayX, 0f, HangarArtData.BayZ);
            float lip = shaft.z - 3.32f;
            float cz = spot.z - 0.5f * Gravity * tf * tf;
            float cx = spot.x;
            float bx0 = cx - s / 2f, bz0 = cz - s / 2f, bz1 = cz + s / 2f;
            // in the bay, above the lip: darker as it nears the lip
            float inBay = Mathf.Clamp01((spot.z - cz) / Mathf.Max(1f, spot.z - (lip - VisibleTop(v))));
            float d1 = 1f - 0.45f * inBay;                               // user 2026-11-22: gentler (was 0.7)
            DrawPart(bx0, bx0 + s, Mathf.Max(bz0, lip), bz1, cx, cz, s, alt, new Color(d1, d1, d1, 1f));
            // below the ship, in space: one strip per map column, each clipped at that column's ship edge
            if (shipEdge != null)
            {
                float fq = Mathf.Clamp01((tf / fallSecs - 0.4f) / 0.6f); float fade = 1f - fq * fq * (3f - 2f * fq);   // longer, smoother fade (was the last 30%)
                Color c2 = new Color(0.9f, 0.9f, 0.9f, fade);
                for (int i = 0; i < shipEdge.Length; i++)
                {
                    float x0 = Mathf.Max(edgeX0 + i, bx0), x1 = Mathf.Min(edgeX0 + i + 1, bx0 + s);
                    float top = Mathf.Min(bz1, shipEdge[i]);
                    DrawPart(x0, x1, bz0, top, cx, cz, s, alt, c2);
                }
            }
        }

        /// <summary>Draws the part of the snapshot inside world rect [x0,x1] x [z0,z1] (snapshot square centred cx,cz, size s).
        /// Each part gets its own mesh: Graphics.DrawMesh renders later, so a shared mesh would end up with the last UVs only.</summary>
        void DrawPart(float x0, float x1, float z0, float z1, float cx, float cz, float s, float alt, Color c)
        {
            if (x1 - x0 < 0.001f || z1 - z0 < 0.001f) return;
            float u0 = (x0 - (cx - s / 2f)) / s, u1 = (x1 - (cx - s / 2f)) / s;
            float v0 = (z0 - (cz - s / 2f)) / s, v1 = (z1 - (cz - s / 2f)) / s;
            Mesh m = ClipMeshes.Get(u0, v0, u1, v1);
            MaterialPropertyBlock mpb = ClipMeshes.Block(c);
            Graphics.DrawMesh(m, Matrix4x4.TRS(new Vector3((x0 + x1) / 2f, alt, (z0 + z1) / 2f), Quaternion.identity, new Vector3(x1 - x0, 1f, z1 - z0)), snapMat, 0, null, 0, mpb);
        }

        /// <summary>Top of the Knight's body picture above the picture centre (cells), from its south texture.</summary>
        static float VisibleTop(VehiclePawn v)
        {
            Vector2 ds = v.VehicleDef.graphicData.drawSize;
            return ds.y * 0.5f;
        }

        void OnRelease(VehiclePawn v, Vector3 spot)
        {
            if (!Spawned) return;
            Vector2 ds = v.VehicleDef.graphicData.drawSize;
            // steam from the clamps along both sides of the Knight
            for (int i = 0; i < 14; i++)
            {
                Vector3 at = spot + new Vector3(Rand.Range(-ds.x * 0.45f, ds.x * 0.45f), 0f, Rand.Range(-ds.y * 0.35f, ds.y * 0.1f));
                if (at.ToIntVec3().InBounds(Map)) FleckMaker.ThrowSmoke(at, Map, Rand.Range(1.2f, 2.4f) + ds.x * 0.06f);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector3 at = spot + new Vector3(Rand.Range(-ds.x * 0.4f, ds.x * 0.4f), 0f, Rand.Range(-ds.y * 0.3f, 0f));
                if (at.ToIntVec3().InBounds(Map)) FleckMaker.ThrowAirPuffUp(at, Map);
            }
            Find.CameraDriver.shaker.DoShake(1.2f);
            (DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_hangar_clamps_release") ?? DropLaunchFx.ReleaseSound())?.PlayOneShot(new TargetInfo(Position, Map));
        }

        /// <summary>The Knight to draw in the bay: the docked one, or the one being launched (null when the bay is empty).</summary>
        public bool Launching { get { return launchKnight != null; } }
    }

    /// <summary>A per-frame pool of quads with their own UVs (and colour blocks), for drawing clipped pieces of a picture.</summary>
    public static class ClipMeshes
    {
        static readonly List<Mesh> meshes = new List<Mesh>();
        static readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        static int frame = -1, used, usedB;

        static void NewFrame() { if (Time.frameCount != frame) { frame = Time.frameCount; used = 0; usedB = 0; } }

        public static Mesh Get(float u0, float v0, float u1, float v1)
        {
            NewFrame();
            if (used == meshes.Count)
            {
                var m = new Mesh();
                m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
                m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                m.RecalculateNormals(); m.RecalculateBounds();
                meshes.Add(m);
            }
            Mesh mm = meshes[used++];
            mm.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
            return mm;
        }

        public static MaterialPropertyBlock Block(Color c)
        {
            NewFrame();
            if (usedB == blocks.Count) blocks.Add(new MaterialPropertyBlock());
            MaterialPropertyBlock b = blocks[usedB++];
            b.SetColor(ShaderPropertyIDs.Color, c);
            return b;
        }
    }

    /// <summary>One picture of the whole Knight (body, paint, weapons, stickers) rendered off-screen by a hidden camera, so the falling Knight
    /// can be scaled and faded - Vehicle Framework draws a Knight at a fixed size only.</summary>
    public static class KnightSnapshot
    {
        public static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();
        static Camera cam;
        const float PxPerCell = 64f;
        static readonly Vector3 Away = new Vector3(-5000f, 0f, -5000f);   // far outside every map: the main camera never sees it

        public static RenderTexture Take(VehiclePawn v, out float sizeCells)
        {
            Vector2 ds = v.VehicleDef.graphicData.drawSize;
            sizeCells = Mathf.Max(ds.x, ds.y) * 1.35f;                       // weapons reach past the body picture
            RenderTexture rt = null;
            try
            {
                int px = Mathf.Clamp(Mathf.RoundToInt(sizeCells * PxPerCell), 256, 2048);
                rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32) { name = "LIK_KnightSnapshot" };
                rt.Create();
                if (cam == null)
                {
                    var go = new GameObject("LIK_KnightSnapshotCam");
                    Object.DontDestroyOnLoad(go);
                    cam = go.AddComponent<Camera>();
                    cam.enabled = false;
                    cam.orthographic = true;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                    cam.cullingMask = 1;                                     // layer 0 (Graphics.DrawMesh default)
                    cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);    // looking straight down, +z up on screen (like the map camera)
                    cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
                }
                float alt = AltitudeLayer.PawnState.AltitudeFor();
                cam.transform.position = new Vector3(Away.x, alt + 50f, Away.z);
                cam.orthographicSize = sizeCells / 2f;
                cam.targetTexture = rt;
                OrbitalDropDraw.DrawKnight(v, Away, alt);
                cam.Render();
                cam.targetTexture = null;
                if (IsEmpty(rt))
                {
                    Log.Warning("[LIK] orbital drop: the off-screen picture of " + v.LabelShort + " came out empty - the shaft fall is skipped (steam only).");
                    rt.Release(); Object.Destroy(rt); return null;
                }
                return rt;
            }
            catch (System.Exception e)
            {
                Log.Warning("[LIK] orbital drop: could not picture " + v.LabelShort + " for the shaft fall: " + e.Message);
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
                if (cam != null) cam.targetTexture = null;
                return null;
            }
        }

        /// <summary>True if nothing was drawn (checked once on a 32x32 copy).</summary>
        static bool IsEmpty(RenderTexture rt)
        {
            RenderTexture small = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(rt, small);
                RenderTexture.active = small;
                var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                tex.Apply();
                bool any = tex.GetPixels32().Any(c => c.a > 20);
                Object.Destroy(tex);
                return !any;
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(small); }
        }
    }

    /// <summary>Burning re-entry look (user 2026-11-22): a flame streak behind the Knight, a plasma sheath around it, a bright bow shock in
    /// front, smoke left in the sky. Textures are made in code (no art files).</summary>
    [StaticConstructorOnStartup]
    public static class DropLaunchFx
    {
        public static readonly Material Streak, Glow;
        public static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();

        static DropLaunchFx()
        {
            Streak = MaterialPool.MatFrom(new MaterialRequest(MakeStreak(), ShaderDatabase.MoteGlow));
            Glow = MaterialPool.MatFrom(new MaterialRequest(MakeGlow(), ShaderDatabase.MoteGlow));
        }

        static Texture2D MakeStreak()
        {
            const int W = 64, H = 256;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LIK_ReentryStreak" };
            var px = new Color[W * H];
            Color hot = new Color(1f, 0.95f, 0.75f), mid = new Color(1f, 0.55f, 0.15f), cool = new Color(0.55f, 0.14f, 0.05f);
            for (int y = 0; y < H; y++)
            {
                float v = y / (H - 1f);                                   // 0 = head (bottom), 1 = tail (top)
                float width = 0.18f + 0.30f * (1f - v);
                Color c = v < 0.25f ? Color.Lerp(hot, mid, v / 0.25f) : Color.Lerp(mid, cool, (v - 0.25f) / 0.75f);
                float along = Mathf.Pow(1f - v, 1.3f) * Mathf.Clamp01(v * 12f + 0.3f);
                for (int x = 0; x < W; x++)
                {
                    float u = (x / (W - 1f) - 0.5f) / width;
                    float a = Mathf.Exp(-u * u * 3f) * along;
                    px[y * W + x] = new Color(c.r * a, c.g * a, c.b * a, a);
                }
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D MakeGlow()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LIK_ReentryGlow" };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x / (S - 1f) - 0.5f, dy = y / (S - 1f) - 0.5f;
                    float a = Mathf.Exp(-(dx * dx + dy * dy) * 14f);
                    px[y * S + x] = new Color(a, a, a, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static void Quad(Material m, Vector3 centre, float alt, float w, float h, Color c)
        {
            Mpb.SetColor(ShaderPropertyIDs.Color, c);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(new Vector3(centre.x, alt, centre.z), Quaternion.identity, new Vector3(w, 1f, h)), m, 0, null, 0, Mpb);
        }

        static SoundDef release; static bool releaseLooked;
        public static SoundDef ReleaseSound()
        {
            if (releaseLooked) return release;
            releaseLooked = true;
            foreach (string n in new[] { "DropPod_Leaving", "Shuttle_Leaving", "GravshipTakeoff", "Explosion_Bomb" })
            {
                SoundDef s = DefDatabase<SoundDef>.GetNamedSilentFail(n);
                if (s != null && !s.sustain) { release = s; break; }
            }
            return release;
        }
    }

    /// <summary>Runs an action after a number of game ticks (camera cut to the landing). Not saved.</summary>
    public class GameComponent_LIKTimer : GameComponent
    {
        static readonly List<KeyValuePair<int, System.Action>> due = new List<KeyValuePair<int, System.Action>>();
        public GameComponent_LIKTimer(Game game) { due.Clear(); }
        public static void After(int ticks, System.Action a) { due.Add(new KeyValuePair<int, System.Action>(Find.TickManager.TicksGame + ticks, a)); }
        public override void GameComponentTick()
        {
            if (due.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            var run = due.Where(d => d.Key <= now).ToList();
            if (run.Count == 0) return;
            due.RemoveAll(d => d.Key <= now);
            foreach (var d in run)
                try { d.Value(); } catch (System.Exception e) { Log.Error("[LIK] timed action: " + e); }
        }
    }
}
