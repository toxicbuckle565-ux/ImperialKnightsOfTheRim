using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace LIK
{
    /// <summary>
    /// The Knight's own sounds that the vehicle def cannot do by itself (user 2026-11-11: "we need the knight sounds and war horns").
    /// On every Knight VehicleDef, written by Tools/gen_knights.py from the table in Tools/gen_knight_sounds.py.
    /// (Reactor hum, start-up / shut-down and the hatch are plain Vehicle Framework sound events in the def.)
    /// </summary>
    public class DefModExtension_KnightSound : DefModExtension
    {
        public SoundDef step;            // one footfall
        public int stepTicks = 36;       // ticks between footfalls while walking
        public float stepShake = 0.4f;   // camera shake of a footfall (fades with distance from the view)
        public SoundDef horn;            // war horn
    }

    public static class KnightSound
    {
        public const int HornCooldownTicks = 480;   // 8 s: about the length of the longest horn

        static Texture2D hornIcon;
        public static Texture2D HornIcon
        {
            get
            {
                if (hornIcon == null) hornIcon = ContentFinder<Texture2D>.Get("UI/Knights/WarHorn", false) ?? TexCommand.Attack;
                return hornIcon;
            }
        }

        /// <summary>Footfalls while the Knight walks. Called every tick from CompKnightHead.CompTick.</summary>
        public static void StepTick(CompKnightHead comp)
        {
            VehiclePawn v = comp.parent as VehiclePawn;
            if (v == null || !v.Spawned || v.vehiclePather == null) return;
            int now = Find.TickManager.TicksGame;
            if (!v.vehiclePather.Moving)
            {
                comp.nextStepTick = now + 8;     // the first footfall comes shortly after it sets off
                return;
            }
            if (now < comp.nextStepTick) return;
            DefModExtension_KnightSound ext = v.def.GetModExtension<DefModExtension_KnightSound>();
            if (ext == null || ext.step == null) { comp.nextStepTick = now + 600; return; }
            comp.nextStepTick = now + Mathf.Max(10, ext.stepTicks);
            ext.step.PlayOneShot(new TargetInfo(v.Position, v.Map));
            ShotFx.Shake(v.DrawPos, v.Map, ext.stepShake);
            if (v.DrawPos.ShouldSpawnMotesAt(v.Map)) FleckMaker.ThrowDustPuff(v.DrawPos + Gen.RandomHorizontalVector(1.2f), v.Map, 1.2f + ext.stepShake * 2f);
        }

        public static bool SoundHorn(VehiclePawn v)
        {
            if (v == null || !v.Spawned) return false;
            DefModExtension_KnightSound ext = v.def.GetModExtension<DefModExtension_KnightSound>();
            CompKnightHead comp = v.GetComp<CompKnightHead>();
            if (ext == null || ext.horn == null || comp == null) return false;
            int now = Find.TickManager.TicksGame;
            if (now - comp.lastHornTick < HornCooldownTicks && now >= comp.lastHornTick) return false;
            comp.lastHornTick = now;
            ext.horn.PlayOneShot(new TargetInfo(v.Position, v.Map));
            ShotFx.Shake(v.DrawPos, v.Map, 1.5f + ext.stepShake * 2f);
            return true;
        }
    }

    /// <summary>"Sound war horn" button on every player Knight (works on a multi-selection: every selected Knight answers).</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_KnightHornGizmo
    {
        public static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(__instance.VehicleDef) == null) return;
            if (__instance.def.GetModExtension<DefModExtension_KnightSound>() == null) return;
            __result = __result.Concat(Make(__instance));
        }

        static IEnumerable<Gizmo> Make(VehiclePawn v)
        {
            yield return new Command_Action
            {
                defaultLabel = "Sound war horn",
                defaultDesc = "The Knight sounds its war horn: a challenge to the foe and a call to its own side, heard across the battlefield. " +
                              "Needs the reactor running (ignition on).",
                icon = KnightSound.HornIcon,
                action = () => KnightSound.SoundHorn(v),
                Disabled = !v.Drafted,
                disabledReason = "Start the engine first."
            };
        }
    }
}
