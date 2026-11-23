using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LIK
{
    /// <summary>
    /// Compatibility shim for "More Grav Field Extender" (CYMY.MoreGravFieldExtender, ExtenderChaining.dll), user report 2026-11-21:
    /// "grav ship extenders not expanding the size after a reload". That mod applies its settings (support per extender, radius,
    /// max extenders) in GravshipTweaks.TweaksMod.ApplySettings(), which it only calls from WriteSettings() - i.e. when its settings
    /// window is closed - never at startup, so after every game restart the extenders fall back to vanilla values.
    /// This calls its ApplySettings() once after the defs are loaded. Does nothing when that mod is not active.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CompatGravExtender
    {
        static CompatGravExtender()
        {
            try
            {
                Type t = AccessTools.TypeByName("GravshipTweaks.TweaksMod");
                if (t == null) return;
                MethodInfo m = AccessTools.Method(t, "ApplySettings");
                if (m == null || !m.IsStatic || m.GetParameters().Length != 0) { Log.Warning("[LIK] More Grav Field Extender found, but its ApplySettings() changed - startup fix skipped."); return; }
                m.Invoke(null, null);
                Log.Message("[LIK] More Grav Field Extender: applied its saved settings at startup (that mod only applies them when its settings window closes).");
            }
            catch (Exception e)
            {
                Log.Warning("[LIK] More Grav Field Extender startup fix failed: " + e.GetBaseException().Message);
            }
        }
    }
}

namespace LIK
{
    using System.Collections.Generic;
    using RimWorld;

    /// <summary>
    /// More Grav Field Extender log noise (user 2026-11-22: "got some errors in log"):
    /// 1. its CanLinkTo postfix (ExtenderChaining.Patch_CanLinkTo) prints "maxDist : 16.9" on every extender link check (hundreds per
    ///    session) - left-over debug output. Its postfix is removed and the same logic is applied here without the print.
    /// 2. it adds chained extenders to the grav engine's linked-facility list WITHOUT a real link, so when one unlinks the game logs
    ///    "Notify_LinkRemoved was called but there is no such link here." - with that mod active, a removal of a link that was never
    ///    made is now skipped quietly (the vanilla method would only log the error).
    /// Nothing happens when that mod is not active.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CompatGravExtenderQuiet
    {
        public static bool Active;
        static MethodInfo hasLinkedEngine;

        static CompatGravExtenderQuiet()
        {
            Type noisy = AccessTools.TypeByName("ExtenderChaining.Patch_CanLinkTo");
            hasLinkedEngine = AccessTools.Method("ExtenderChaining.RelaySearch:HasLinkedEngine");
            Active = noisy != null;
            if (!Active) return;
            // their patches are applied by their own startup class (order unknown) - swap the noisy postfix once everything has loaded
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                try
                {
                    var harmony = new Harmony("local.imperialknights.gravextenderquiet");
                    MethodInfo original = AccessTools.Method(typeof(CompAffectedByFacilities), "CanLinkTo");
                    MethodInfo theirs = AccessTools.Method(noisy, "Postfix");
                    if (original == null || theirs == null || hasLinkedEngine == null) { Log.Warning("[LIK] More Grav Field Extender changed - log-noise fix skipped."); return; }
                    harmony.Unpatch(original, theirs);
                    harmony.Patch(original, postfix: new HarmonyMethod(typeof(CompatGravExtenderQuiet), nameof(QuietCanLinkTo)));
                }
                catch (Exception e) { Log.Warning("[LIK] More Grav Field Extender log-noise fix failed: " + e.GetBaseException().Message); }
            });
        }

        /// <summary>Same as their postfix (extender-to-extender links through a relay chain), without the debug print.</summary>
        static void QuietCanLinkTo(CompAffectedByFacilities __instance, Thing facility, ref bool __result)
        {
            if (__result || facility == null || __instance?.parent == null) return;
            if (__instance.parent.def.defName != "GravFieldExtender" || facility.def.defName != "GravFieldExtender") return;
            CompProperties_SubstructureFootprint fp = __instance.parent.def.GetCompProperties<CompProperties_SubstructureFootprint>();
            if (fp == null) return;
            try { if ((bool)hasLinkedEngine.Invoke(null, new object[] { __instance.parent, fp.radius, __instance.parent.Map })) __result = true; }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CompAffectedByFacilities), nameof(CompAffectedByFacilities.Notify_LinkRemoved))]
    public static class Patch_QuietMissingLink
    {
        static readonly AccessTools.FieldRef<CompAffectedByFacilities, List<Thing>> linked = AccessTools.FieldRefAccess<CompAffectedByFacilities, List<Thing>>("linkedFacilities");

        static bool Prefix(CompAffectedByFacilities __instance, Thing thing)
        {
            if (!CompatGravExtenderQuiet.Active) return true;
            List<Thing> l = linked(__instance);
            return l == null || l.Contains(thing);          // no such link (a chained extender) -> nothing to remove, no error
        }
    }
}
