using System.Linq;
using HarmonyLib;
using RimWorld;
using Vehicles;
using Verse;
using Verse.AI.Group;

namespace LIK
{
    /// <summary>
    /// THE HOUSEHOLD STANDS WITH ITS KNIGHTS (user 2026-11-25: "knights aren't fleeing now but the pawns are"). A raid's lord breaks and flees
    /// when it has lost a share of its pawns (Trigger_FractionPawnsLost). For a hostile Knight house that check is held back while any of the
    /// house's Knights is still fighting on that map: the troops fight on beside them. Once the last Knight has fallen they may break as normal.
    /// </summary>
    [HarmonyPatch(typeof(Trigger_FractionPawnsLost), nameof(Trigger_FractionPawnsLost.ActivateOn))]
    public static class Patch_HouseholdHoldsWithKnights
    {
        static bool Prefix(Lord lord, ref bool __result)
        {
            Faction f = lord?.faction;
            Map map = lord?.Map;
            if (f == null || map == null || !KnightHouseUtil.IsKnightHouse(f) || !f.HostileTo(Faction.OfPlayer)) return true;
            bool knightStands = map.mapPawns.SpawnedPawnsInFaction(f).OfType<VehiclePawn>()
                .Any(k => !k.Dead && !k.Downed && KnightLoadout.ClassFor(k.VehicleDef) != null);
            if (!knightStands) return true;
            __result = false;
            return false;
        }
    }
}
