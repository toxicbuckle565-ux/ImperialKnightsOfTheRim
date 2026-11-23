using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// Manufactorum sounds (user 2026-11-22, grimdark forge-cathedral): every recipe made there plays LIK_Snd_manufactorum_loop while a
    /// worker is at it (RecipeDef.soundWorking, set at startup so the generated recipes need no change), and a finished product sounds the
    /// last iron blow, the quench and the bell (LIK_Snd_manufactorum_done). Clips: Tools/gen_building_sounds.py.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ManufactorumSound
    {
        public const string Building = "LIK_MechanicumManufactorum";

        static ManufactorumSound()
        {
            ThingDef man = DefDatabase<ThingDef>.GetNamedSilentFail(Building);
            SoundDef loop = DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_manufactorum_loop");
            if (man == null || loop == null) return;
            int n = 0;
            foreach (RecipeDef r in man.AllRecipes) { r.soundWorking = loop; n++; }
            if (Prefs.DevMode) Log.Message("[LIK] manufactorum working sound on " + n + " recipes");
        }
    }

    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    public static class Patch_ManufactorumDoneSound
    {
        static void Postfix(IBillGiver billGiver)
        {
            if (!(billGiver is Thing t) || t.def?.defName != ManufactorumSound.Building || !t.Spawned) return;
            DefDatabase<SoundDef>.GetNamedSilentFail("LIK_Snd_manufactorum_done")?.PlayOneShot(new TargetInfo(t.Position, t.Map));
        }
    }
}
