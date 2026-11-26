using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;

namespace LIK
{
    /// <summary>
    /// "PROTECT HOME" (user 2026-11-25: "the magna lascannon [is] a big issue for players defending a base - add a toggle to the Knight that
    /// disables wall damage on home tiles"). A toggle on every player Knight, ON by default. While on, on a player home map, that Knight's weapons
    /// do no damage to the colony's own buildings (anything owned by the player) or to anything standing in the Home area (mountain-base rock
    /// walls included): explosions leave them unharmed and a piercing beam (magna lascannon etc.) passes through them and still burns what is
    /// beyond. Natural rock anywhere on the home map is spared too (2026-11-25). Enemy buildings are hit as normal. Enemy Knights are never affected.
    /// </summary>
    public static class HomeProtection
    {
        /// <summary>True when this attacker is a player Knight with Protect home on and the thing is a colony structure on a home map.</summary>
        /// <summary>The Knight behind a hit: the Knight itself, or (2026-11-25, user "walls get damaged") its pilot / crew when a shot or blast
        /// is credited to the pawn sitting inside it.</summary>
        public static VehiclePawn KnightOf(Thing attacker)
        {
            if (attacker is VehiclePawn v) return v;
            if (attacker is Pawn p)
            {
                IThingHolder h = p.ParentHolder;
                for (int i = 0; h != null && i < 6; i++, h = h.ParentHolder)
                {
                    if (h is VehiclePawn hv) return hv;
                    if (h is VehicleRoleHandler rh && rh.vehicle != null) return rh.vehicle;
                }
            }
            return null;
        }

        public static bool Spares(Thing attacker, Thing t)
        {
            VehiclePawn v = KnightOf(attacker);
            if (v == null || v.Faction != Faction.OfPlayer || t == null || t is Pawn) return false;
            if (KnightLoadout.ClassFor(v.VehicleDef) == null) return false;
            CompKnightHead comp = v.GetComp<CompKnightHead>();
            if (comp == null || !comp.protectHome) return false;
            Map map = t.MapHeld;
            if (map == null || !map.IsPlayerHome) return false;
            if (t.def.category != ThingCategory.Building) return false;
            if (t.Faction == Faction.OfPlayer) return true;                          // walls, doors, turrets, furniture the colony owns
            // 2026-11-25 (user screenshot: a magna lascannon beam carved a trench through the mountain next to the base): natural rock is spared
            // ANYWHERE on the home map, not just inside the Home area - the mountain is part of the home tile
            if (t.def.building != null && t.def.building.isNaturalRock) return true;
            return t.Spawned && map.areaManager?.Home != null && map.areaManager.Home[t.Position];   // rock walls etc. inside the Home area
        }
    }

    /// <summary>Damage from a protecting Knight to a colony structure is dropped (explosions, impacts, chain bursts, anything with the Knight as instigator).</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_HomeProtectionDamage
    {
        static int lastLog = -9999;

        [HarmonyPriority(Priority.First)]
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!HomeProtection.Spares(dinfo.Instigator, __instance))
            {
                // dev mode: a Knight weapon still hurting one of the colony's buildings on a home map - say who / what, so a missed path shows up
                if (Prefs.DevMode && __instance.Faction == Faction.OfPlayer && __instance.def.category == ThingCategory.Building
                    && __instance.MapHeld != null && __instance.MapHeld.IsPlayerHome
                    && ((dinfo.Weapon != null && dinfo.Weapon.defName.StartsWith("LIK_")) || HomeProtection.KnightOf(dinfo.Instigator) != null))
                {
                    int now = Find.TickManager.TicksGame;
                    if (now - lastLog > 60)
                    {
                        lastLog = now;
                        Log.Message("[LIK] Protect home MISSED: " + __instance.LabelShort + " hit by " + dinfo.Def?.defName + " " + dinfo.Amount.ToString("0")
                            + ", instigator=" + (dinfo.Instigator == null ? "null" : dinfo.Instigator.GetType().Name + " '" + dinfo.Instigator.LabelShort + "' faction=" + dinfo.Instigator.Faction?.Name)
                            + ", weapon=" + dinfo.Weapon?.defName);
                    }
                }
                return true;
            }
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_HomeProtectionGizmo
    {
        static Texture2D icon;
        static Texture2D Icon => icon ?? (icon = ContentFinder<Texture2D>.Get("UI/Designators/HomeAreaOn", false) ?? BaseContent.BadTex);

        static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(__instance.VehicleDef) == null) return;
            CompKnightHead comp = __instance.GetComp<CompKnightHead>();
            if (comp == null) return;
            __result = __result.Concat(new Gizmo[]
            {
                new Command_Toggle
                {
                    defaultLabel = "Protect home",
                    defaultDesc = "On your home map this Knight's weapons do not damage the colony's own walls and buildings, the map's natural rock " +
                                  "(mountains), nor anything inside the Home area. Piercing beams pass through them and still burn what is beyond. " +
                                  "Enemy buildings and cover are hit as normal. Turn off to let the Knight blast through your own walls.",
                    icon = Icon,
                    isActive = () => comp.protectHome,
                    toggleAction = () => comp.protectHome = !comp.protectHome,
                }
            });
        }
    }
}
