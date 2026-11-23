using UnityEngine;

namespace LIK
{
    /// <summary>
    /// Candle glow flicker shared by the hangar and the manufactorum (user 2026-11-21: candles must not flicker in sync).
    /// Each seed (building id + cluster index) gets its own speeds and phases, so every candle cluster flickers on its own;
    /// three layered waves plus a rare short gutter dip. Never fully dark (0.45 .. 1).
    /// </summary>
    public static class CandleFlicker
    {
        static float Hash(int seed, int k)
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(k * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777215f;
        }

        public static float Value(int seed)
        {
            float t = Time.realtimeSinceStartup;
            float k1 = 0.8f + 0.45f * Hash(seed, 1), k2 = 0.8f + 0.45f * Hash(seed, 2), k3 = 0.8f + 0.45f * Hash(seed, 3);
            float p1 = 6.283f * Hash(seed, 4), p2 = 6.283f * Hash(seed, 5), p3 = 6.283f * Hash(seed, 6);
            float v = 0.72f + 0.14f * Mathf.Sin(t * 11.3f * k1 + p1) + 0.09f * Mathf.Sin(t * 23.7f * k2 + p2) + 0.05f * Mathf.Sin(t * 41.9f * k3 + p3);
            float gut = Mathf.Sin(t * (0.37f + 0.2f * Hash(seed, 7)) + p1 * 3f);               // now and then a brief gutter
            if (gut > 0.985f) v -= (gut - 0.985f) / 0.015f * 0.22f;
            return Mathf.Clamp(v, 0.45f, 1f);
        }
    }
}
