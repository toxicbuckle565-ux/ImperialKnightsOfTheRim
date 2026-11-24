using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace LIK
{
    /// <summary>
    /// User 2026-11-23 (filming the release clip): heavy Knight fire turned the battlefield into a white-hot blob. Every explosive Knight shell
    /// or missile makes vanilla's ExplosionFlash (about 6x the blast radius), and a few of our own flashes skipped GlowFx - dozens of additive
    /// glows landing on the same spot within a second add up to pure white.
    /// One guard on every fleck the game creates (ours and vanilla's), for the bright additive glows only:
    ///  - size cap from the 'Shot and hit glare' setting (GlowFx.Cap)
    ///  - crowding: the glow already put on an area in the last half second counts against a budget; new glows there shrink, or are skipped
    /// Smoke, sparks, debris and the weapons' own muzzle flashes and beams (GlowFx.Full) are untouched. At 100 % glare it is effectively off.
    /// </summary>
    [HarmonyPatch(typeof(FleckManager), nameof(FleckManager.CreateFleck))]
    public static class Patch_GlowGuard
    {
        static readonly HashSet<string> Bright = new HashSet<string>
        {
            "ExplosionFlash", "LightningGlow", "FireGlow", "HeatGlow", "HeatGlow_Intense", "GrowingFlash", "SparkFlash", "YellowSparkFlash",
            "ShotFlash", "PsycastAreaEffect", "LIK_HotGround", "LIK_HotGroundCore", "LIK_ChargeGlow"
        };
        const int Window = 30;                       // ticks a glow counts against its area (half a second)

        struct Recent { public Vector3 pos; public float size; public int tick; }
        static readonly Dictionary<int, List<Recent>> byMap = new Dictionary<int, List<Recent>>();

        static bool Prefix(FleckManager __instance, ref FleckCreationData fleckData)
        {
            FleckDef def = fleckData.def;
            Map map = __instance?.parent;
            if (def == null || map == null || GlowFx.Full > 0 || !Bright.Contains(def.defName) || Find.TickManager == null) return true;
            float g = GlowFx.G;
            if (g >= 0.99f) return true;                                   // full glare: no guard
            float size = fleckData.exactScale.HasValue ? Mathf.Max(fleckData.exactScale.Value.x, fleckData.exactScale.Value.z) : fleckData.scale;
            if (size <= 0f) return true;

            float cap = GlowFx.Cap;
            float budget = Mathf.Lerp(8f, 40f, g);                         // total glow size allowed on one area per half second (35 % -> ~19)
            int now = Find.TickManager.TicksGame;
            if (!byMap.TryGetValue(map.uniqueID, out List<Recent> list)) byMap[map.uniqueID] = list = new List<Recent>();
            list.RemoveAll(r => now - r.tick > Window || now < r.tick);

            Vector3 p = fleckData.spawnPosition;
            float reach = Mathf.Max(4f, Mathf.Min(size, cap) * 0.5f);
            float crowd = 0f;
            foreach (Recent r in list)
            {
                float dx = r.pos.x - p.x, dz = r.pos.z - p.z;
                if (dx * dx + dz * dz < reach * reach) crowd += r.size;
            }
            float allowed = Mathf.Min(size, cap, budget - crowd);
            if (allowed < 0.6f) return false;                              // the area is already lit up: skip this glow
            if (allowed < size)
            {
                float k = allowed / size;
                fleckData.scale *= k;
                if (fleckData.exactScale.HasValue) fleckData.exactScale = fleckData.exactScale.Value * k;
            }
            if (list.Count < 400) list.Add(new Recent { pos = p, size = allowed, tick = now });
            return true;
        }
    }
}
