using RimWorld;
using UnityEngine;
using Verse;

namespace LIK
{
    /// <summary>
    /// Orbital-drop and meltdown craters (user 2026-11-25: "a natural timer that the craters disappear after"): a crater stays sharp for the
    /// first half of its life (setting "Craters fade after", default 5 days), then slowly fades into the ground and is gone. 0 days = it stays
    /// until deconstructed (the old behaviour). Craters from older saves start their clock when the save is loaded.
    /// </summary>
    public class KnightCrater : Building
    {
        int born = -1;
        const int FadeSteps = 32;                    // material cache steps (MaterialPool keeps one material per step and texture)

        static float LifeTicks { get { return LIKMod.I.craterDays * GenDate.TicksPerDay; } }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (born < 0) born = Find.TickManager.TicksGame;
        }

        int Age { get { return Find.TickManager.TicksGame - born; } }

        float Alpha
        {
            get
            {
                float life = LifeTicks;
                if (life <= 0f) return 1f;
                float fadeFrom = life * 0.5f;
                if (Age <= fadeFrom) return 1f;
                return Mathf.Clamp01(1f - (Age - fadeFrom) / (life - fadeFrom));
            }
        }

        public override void TickRare()
        {
            base.TickRare();
            float life = LifeTicks;
            if (life > 0f && Age >= life && Spawned) Destroy(DestroyMode.Vanish);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float a = Alpha;
            if (a >= 0.999f) { base.DrawAt(drawLoc, flip); return; }
            if (a <= 0.02f) return;
            Graphic g = Graphic is Graphic_Random gr ? gr.SubGraphicFor(this) : Graphic;
            Material src = g?.MatSingle;
            if (src == null || !(src.mainTexture is Texture2D tex)) return;
            int step = Mathf.Clamp(Mathf.RoundToInt(a * FadeSteps), 1, FadeSteps);
            Material m = MaterialPool.MatFrom(tex, src.shader, new Color(1f, 1f, 1f, step / (float)FadeSteps));
            Vector2 ds = def.graphicData.drawSize;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawLoc, Quaternion.identity, new Vector3(ds.x, 1f, ds.y)), m, 0);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            float life = LifeTicks;
            if (life <= 0f) return s;
            float left = Mathf.Max(0f, life - Age);
            string line = left <= GenDate.TicksPerDay ? "Almost grown over." : "Grows over in " + (left / GenDate.TicksPerDay).ToString("0.#") + " days.";
            return s.NullOrEmpty() ? line : s + "\n" + line;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref born, "born", -1);
        }
    }
}
