using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// User 2026-11-11: "some impact noises cut off instantly ... they'd be better with a fade out".
    /// RimWorld lets a SoundDef ring maxVoices times at once; when one more starts, the least important one is stopped dead (AudioSource.Stop).
    /// With long recordings (a 10 s explosion tail, a salvo of missiles landing) that is clearly audible. For the Knight sounds (LIK_Snd_*) the
    /// oldest copy is taken out of the game's list just before that happens and faded out over a third of a second here instead.
    /// </summary>
    [HarmonyPatch(typeof(SampleOneShotManager), nameof(SampleOneShotManager.TryAddPlayingOneShot))]
    public static class Patch_FadeCulledKnightSound
    {
        const float FadeSeconds = 0.35f;

        class Fading { public AudioSource source; public float start, left; }
        static readonly List<Fading> fading = new List<Fading>();
        static readonly AccessTools.FieldRef<SampleOneShotManager, List<SampleOneShot>> samplesOf =
            AccessTools.FieldRefAccess<SampleOneShotManager, List<SampleOneShot>>("samples");

        static void Prefix(SampleOneShotManager __instance, SampleOneShot newSample)
        {
            if (newSample == null || newSample.subDef == null) return;
            SoundDef def = newSample.subDef.parentDef;
            if (def == null || def.defName == null || !def.defName.StartsWith("LIK_Snd_")) return;
            List<SampleOneShot> samples = samplesOf(__instance);
            if (samples == null) return;

            int count = 0;
            SampleOneShot oldest = null;
            for (int i = 0; i < samples.Count; i++)
            {
                SampleOneShot s = samples[i];
                if (s.subDef != newSample.subDef) continue;
                count++;
                if (oldest == null || s.AgeRealTime > oldest.AgeRealTime) oldest = s;
            }
            if (count < def.maxVoices || oldest == null) return;

            samples.Remove(oldest);          // the game now finds a free voice and stops nothing
            AudioSource src = oldest.source;
            if (src != null && src.isPlaying) fading.Add(new Fading { source = src, start = src.volume, left = FadeSeconds });
        }

        /// <summary>Called every frame from the game's own sound update.</summary>
        public static void Update()
        {
            if (fading.Count == 0) return;
            float dt = Time.unscaledDeltaTime;
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                Fading f = fading[i];
                f.left -= dt;
                if (f.source == null || !f.source.isPlaying) { fading.RemoveAt(i); continue; }
                if (f.left <= 0f)
                {
                    f.source.Stop();         // silent by now; frees the voice for the game's pool
                    fading.RemoveAt(i);
                }
                else f.source.volume = f.start * (f.left / FadeSeconds);
            }
        }
    }

    [HarmonyPatch(typeof(SampleOneShotManager), nameof(SampleOneShotManager.SampleOneShotManagerUpdate))]
    public static class Patch_FadeCulledKnightSound_Update
    {
        static void Postfix() { Patch_FadeCulledKnightSound.Update(); }
    }
}
