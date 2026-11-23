using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LIK
{
    /// <summary>
    /// The Mechanicum plasma reactor's look (user 2026-11-22: "the globe should be ON the reactor", the grav engine stays vanilla).
    /// The reactor's own graphic is the static globe-machine picture; this comp draws the animated layers over it every frame: back
    /// servo-arms, the plasma ball (arcs + arm lasers), front servo-arms. 96-frame loop at 14 fps; held on one frame while the reactor
    /// is not active (not linked to a grav engine). The art was made for a 3x3 grav engine and is drawn scaled to the 4x4 reactor.
    /// Art + crop rects: Tools/install_grav_engine_art.py -> Textures/Things/Imperial/PlasmaReactor + GravEngineArtData.cs.
    /// </summary>
    public class CompProperties_PlasmaReactorArt : CompProperties
    {
        public CompProperties_PlasmaReactorArt() { compClass = typeof(CompPlasmaReactorArt); }
    }

    public class CompPlasmaReactorArt : ThingComp
    {
        const string P = "Things/Imperial/PlasmaReactor/";
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        int activeCheckTick = -9999; bool active;

        static Material Mat(string path)
        {
            if (!mats.TryGetValue(path, out Material m)) { m = MaterialPool.MatFrom(P + path, ShaderDatabase.Transparent); mats[path] = m; }
            return m;
        }

        /// <summary>Linked to a grav engine and working (re-checked every 2 seconds).</summary>
        bool Active
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (now - activeCheckTick < 120) return active;
                activeCheckTick = now;
                CompGravshipFacility f = parent.GetComp<CompGravshipFacility>();
                bool now2 = f != null && f.CanBeActive;
                if (now2 != active) ReactorBoost.MarkDirty(parent.Map);     // the engine's build circle grows / shrinks (ReactorBoost)
                active = now2;
                return active;
            }
        }

        float Scale { get { return parent.def.size.x / GravEngineArtData.DrawW; } }   // 4x4 reactor / 3-cell engine art

        static void Quad(Vector3 pic, float[] r, float s, Material m, float alt)
        {
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(new Vector3(pic.x + r[0] * s, alt, pic.z + r[1] * s), Quaternion.identity,
                new Vector3(r[2] * s, 1f, r[3] * s)), m, 0);
        }

        public override void PostDraw()
        {
            base.PostDraw();
            float s = Scale;
            Vector3 pic = parent.TrueCenter() + new Vector3(0f, 0f, GravEngineArtData.OffZ * s);
            // above the static picture, which the map mesh prints a little higher than Building (user 2026-11-22: ball + front arms were
            // drawn UNDER it); BuildingOnTop is still below pawns
            float alt = AltitudeLayer.BuildingOnTop.AltitudeFor();
            int f = Active ? (int)((Time.realtimeSinceStartup + parent.thingIDNumber * 0.37f) * GravEngineArtData.FPS) % GravEngineArtData.Frames : 0;
            string n = f.ToString("00");
            Quad(pic, GravEngineArtData.ArmBack, s, Mat("armb/armb_" + n), alt + 0.001f);
            Quad(pic, GravEngineArtData.Ball, s, Mat("ball/ball_" + n), alt + 0.002f);
            Quad(pic, GravEngineArtData.ArmFront, s, Mat("armf/armf_" + n), alt + 0.003f);
        }
    }
}
