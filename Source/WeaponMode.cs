using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    /// <summary>
    /// Weapons with an alternate mode: the turret on that mount is swapped between the normal weapon and its alternate. Only one of the two is
    /// ever fitted, so the picture is the same arm and it always aims at what is actually firing.
    ///  - siege claws (built-in rad cleanser): a button per arm switches by hand.
    ///  - reaper chainfist (built-in twin heavy bolter, user 2026-11-11: "both the range weapon and the melee weapon"): autoAlt - the Knight
    ///    switches by itself, see MapComponent_KnightAutoMode. No button.
    /// </summary>
    public static class WeaponModeUtil
    {
        public static bool Swap(VehiclePawn v, string from, string to, bool message)
        {
            CompVehicleTurrets comp = v.CompVehicleTurrets;
            VehicleTurret reference = comp.Props.turrets.FirstOrDefault(t => t.key == to);
            if (reference == null) { Log.Warning("[LIK] no reference turret " + to + " on " + v.def.defName); return false; }
            VehicleTurret old = comp.GetTurret(from);
            LocalTargetInfo target = old != null ? old.targetInfo : LocalTargetInfo.Invalid;
            comp.RemoveTurret(from);
            if (comp.GetTurret(to) == null) comp.CopyAndAddTurret(reference);
            VehicleTurret now = comp.GetTurret(to);
            if (now != null)
            {
                // a freshly fitted mode is loaded and keeps engaging what the arm was already aiming at
                if (now.def.magazineCapacity > 0 && now.shellCount < now.def.magazineCapacity)
                {
                    if (now.loadedAmmo == null && now.def.ammunition != null) now.loadedAmmo = now.def.ammunition.AllowedThingDefs.FirstOrDefault();
                    if (now.savedAmmoType == null) now.savedAmmoType = now.loadedAmmo;
                    now.SetMagazineCount(now.def.magazineCapacity);
                }
                if (target.IsValid && (!target.HasThing || !target.Thing.Destroyed))
                {
                    try { now.SetTarget(target); } catch (System.Exception) { }
                }
            }
            if (message) Messages.Message(v.LabelShort + ": weapon mode switched.", v, MessageTypeDefOf.SilentInput, false);
            return true;
        }
    }

    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_KnightModeGizmo
    {
        public static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer) return;
            KnightClassDef cls = KnightLoadout.ClassFor(__instance.VehicleDef);
            if (cls == null || __instance.CompVehicleTurrets == null) return;
            __result = __result.Concat(Make(__instance, cls));
        }

        static IEnumerable<Gizmo> Make(VehiclePawn v, KnightClassDef cls)
        {
            CompVehicleTurrets comp = v.CompVehicleTurrets;
            foreach (KnightMount m in cls.mounts)
                foreach (WeaponOption o in m.options)
                {
                    if (o.autoAlt || o.altTurretKeys == null || o.altTurretKeys.Count == 0) continue;
                    string alt = o.altTurretKeys[0];
                    VehicleTurret normal = comp.GetTurret(o.turretKey), other = comp.GetTurret(alt);
                    if (normal == null && other == null) continue;           // this weapon is not fitted on this mount
                    bool inAlt = other != null;
                    VehicleTurret current = inAlt ? other : normal;
                    string from = inAlt ? alt : o.turretKey, to = inAlt ? o.turretKey : alt;
                    yield return new Command_Action
                    {
                        defaultLabel = m.label + ": " + (inAlt ? "rad cleanser" : "siege claw"),
                        defaultDesc = "Switch this arm between its two modes.\n\nSiege claw: crushing close-range strikes, cannot hit far enemies.\n" +
                                      "Rad cleanser: a flamer that reaches about 12 cells, cannot hit enemies right next to you.\n\nNow: " +
                                      (inAlt ? "RAD CLEANSER" : "SIEGE CLAW") + ". Click to switch.",
                        icon = current?.def?.graphicData != null ? ContentFinder<Texture2D>.Get(current.def.graphicData.texPath, false) ?? TexCommand.Attack : TexCommand.Attack,
                        action = () => WeaponModeUtil.Swap(v, from, to, true)
                    };
                }
        }
    }

    /// <summary>
    /// Weapons that are a melee weapon AND a gun at once (reaper chainfist + its twin heavy bolter). Twice a second every Knight carrying one
    /// looks at what that arm is fighting - its current target, or else the nearest enemy - and takes the fitting mode: the fist while the enemy
    /// is within its reach, the bolter while it is not. A little slack stops it flapping when the enemy stands right at the edge.
    /// </summary>
    public class MapComponent_KnightAutoMode : MapComponent
    {
        readonly Dictionary<int, int> lastSwap = new Dictionary<int, int>();

        public MapComponent_KnightAutoMode(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 30 != 11) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                VehiclePawn v = pawns[i] as VehiclePawn;
                if (v == null || v.CompVehicleTurrets == null) continue;
                KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
                if (cls == null) continue;
                try { Check(v, cls, now); }
                catch (System.Exception e) { Log.ErrorOnce("[LIK] auto weapon mode: " + e, 77120611); }
            }
        }

        void Check(VehiclePawn v, KnightClassDef cls, int now)
        {
            CompVehicleTurrets comp = v.CompVehicleTurrets;
            foreach (KnightMount m in cls.mounts)
                foreach (WeaponOption o in m.options)
                {
                    if (!o.autoAlt || o.altTurretKeys == null || o.altTurretKeys.Count == 0) continue;
                    string alt = o.altTurretKeys[0];
                    VehicleTurret melee = comp.GetTurret(o.turretKey), gun = comp.GetTurret(alt);
                    if (melee == null && gun == null) continue;
                    VehicleTurret cur = melee ?? gun;
                    if (DualMode.Index(cur) != 0) continue;             // the player locked this arm to one mode with its fire-mode button
                    VehicleTurret meleeRef = melee ?? comp.Props.turrets.FirstOrDefault(t => t.key == o.turretKey);
                    if (meleeRef == null || meleeRef.def == null) continue;
                    float reach = meleeRef.def.maxRange;

                    // two MELEE modes on one arm (reaper chain-cleaver strike / sweep, 2026-11-17): the alternate reaches no further than the main one.
                    // Auto picks the sweep when 3+ enemies stand within reach of the arm (Reaping Blow), else the single heavy strike.
                    VehicleTurret altRef = gun ?? comp.Props.turrets.FirstOrDefault(t => t.key == alt);
                    if (altRef?.def != null && altRef.def.maxRange <= reach + 0.5f)
                    {
                        bool wantSweep = EnemiesNear(v, reach + 1f) >= 3;
                        if (wantSweep == (gun != null)) continue;
                        int skey = v.thingIDNumber * 31 + m.key.GetHashCode(), slast;
                        if (lastSwap.TryGetValue(skey, out slast) && now >= slast && now - slast < 90) continue;
                        lastSwap[skey] = now;
                        WeaponModeUtil.Swap(v, wantSweep ? o.turretKey : alt, wantSweep ? alt : o.turretKey, false);
                        continue;
                    }

                    float dist;
                    if (cur.targetInfo.IsValid) dist = (cur.targetInfo.CenterVector3 - cur.TurretLocation).MagnitudeHorizontal();
                    else if (!NearestEnemy(v, out dist)) continue;        // nothing to fight: stay as it is

                    bool wantMelee = melee != null ? dist <= reach + 2f : dist <= reach;
                    if (wantMelee == (melee != null)) continue;
                    int key = v.thingIDNumber * 31 + m.key.GetHashCode();
                    int last;
                    if (lastSwap.TryGetValue(key, out last) && now >= last && now - last < 90) continue;
                    lastSwap[key] = now;
                    WeaponModeUtil.Swap(v, wantMelee ? alt : o.turretKey, wantMelee ? o.turretKey : alt, false);
                    if (Prefs.DevMode) Log.Message("[LIK] auto mode: " + v.LabelShort + " " + m.key + " -> " + (wantMelee ? "MELEE" : "GUN") + " (target " + dist.ToString("F1") + " cells, reach " + reach.ToString("F1") + ")");
                }
        }

        int EnemiesNear(VehiclePawn v, float radius)
        {
            if (v.Faction == null) return 0;
            int n = 0;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToFaction(v.Faction))
            {
                Thing th = t.Thing;
                if (th is Pawn p && p.Spawned && !p.Dead && !p.Downed && !t.ThreatDisabled(v) && p.Position.InHorDistOf(v.Position, radius)) n++;
            }
            return n;
        }

        bool NearestEnemy(VehiclePawn v, out float dist)
        {
            dist = 9999f;
            if (v.Faction == null) return false;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToFaction(v.Faction))
            {
                Thing th = t.Thing;
                if (th == null || !th.Spawned || t.ThreatDisabled(v)) continue;
                float d = th.Position.DistanceTo(v.Position);
                if (d < dist) dist = d;
            }
            return dist < 60f;
        }
    }

    /// <summary>
    /// Melee arms with a built-in gun (reaper chainfist + twin heavy bolter, shock lance + plasma blast): the turret's fire-mode button
    /// switches the arm (user 2026-11-15). Both turrets carry the same three fire modes (Tools/gen_assembly.py dual_modes_xml):
    /// 0 = Auto (MapComponent_KnightAutoMode picks by range), 1 = melee locked, 2 = gun locked. The mode index is saved with the turret.
    /// </summary>
    public static class DualMode
    {
        static readonly AccessTools.FieldRef<VehicleTurret, int> modeRef = AccessTools.FieldRefAccess<VehicleTurret, int>("currentFireMode");

        public static int Index(VehicleTurret t) { return t != null && t.def != null && t.def.fireModes != null && t.def.fireModes.Count == 3 ? modeRef(t) : 0; }

        /// <summary>The arm option this turret belongs to (as the melee or the gun half). melee = is this turret the melee half.</summary>
        public static bool Find(VehicleTurret t, out WeaponOption opt, out bool melee)
        {
            opt = null; melee = false;
            if (t?.vehicle == null || t.key == null) return false;
            KnightClassDef cls = KnightLoadout.ClassFor(t.vehicle.VehicleDef);
            if (cls == null) return false;
            foreach (KnightMount m in cls.mounts)
                foreach (WeaponOption o in m.options)
                {
                    if (!o.autoAlt || o.altTurretKeys == null || o.altTurretKeys.Count == 0) continue;
                    if (o.turretKey == t.key) { opt = o; melee = true; return true; }
                    if (o.altTurretKeys[0] == t.key) { opt = o; melee = false; return true; }
                }
            return false;
        }

        public static void Cycle(VehicleTurret t, WeaponOption o, bool melee)
        {
            VehiclePawn v = t.vehicle;
            int next = (Index(t) + 1) % 3;
            bool wantMelee = next == 1 ? true : next == 2 ? false : melee;       // Auto keeps whatever is fitted now
            string from = t.key, to = wantMelee ? o.turretKey : o.altTurretKeys[0];
            VehicleTurret now = t;
            if (to != from && WeaponModeUtil.Swap(v, from, to, false)) now = v.CompVehicleTurrets.GetTurret(to) ?? t;
            if (now.def.fireModes != null && now.def.fireModes.Count == 3) modeRef(now) = next;
            Messages.Message(v.LabelShort + ": " + now.def.fireModes[Index(now)].label + (next == 0 ? "" : " (locked)"), v, MessageTypeDefOf.SilentInput, false);
        }
    }

    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.CycleFireMode))]
    public static class Patch_DualModeCycle
    {
        static bool Prefix(VehicleTurret __instance)
        {
            if (__instance.def?.fireModes == null || __instance.def.fireModes.Count != 3) return true;
            if (!DualMode.Find(__instance, out WeaponOption o, out bool melee)) return true;
            try { DualMode.Cycle(__instance, o, melee); }
            catch (System.Exception e) { Log.Warning("[LIK] could not switch the weapon mode: " + e.Message); }
            return false;
        }
    }

    /// <summary>
    /// Ammo duplication fix (user 2026-11-15: "the ion gauntlet can bug out and spawn 999 steel in the cargo"). VF's TryClearChamber hands a
    /// turret's loaded magazine back as items whenever the turret is removed - and the chainfist / shock lance swap turrets on every mode
    /// change, the hangar swaps them on refits. Melee and energy weapons (incl. the ion gauntlet shield, reactor powered) have NO ammunition:
    /// any 'loaded ammo' they still hold (old saves: 999 placeholder steel, refilled for free) is deleted instead of refunded.
    /// </summary>
    [HarmonyPatch(typeof(VehicleTurret), nameof(VehicleTurret.TryClearChamber))]
    public static class Patch_NoPhantomAmmo
    {
        static bool Prefix(VehicleTurret __instance)
        {
            if (__instance?.def == null || __instance.def.ammunition != null) return true;
            if (__instance.def.defName == null || !__instance.def.defName.StartsWith("LIK_Turret_")) return true;
            __instance.loadedAmmo = null;          // nothing real was ever loaded: no refund
            return false;
        }
    }
}
