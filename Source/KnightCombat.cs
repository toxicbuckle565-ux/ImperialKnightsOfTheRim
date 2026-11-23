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
    /// Landing recovery (user 2026-11-13: "a second of pause before the knight moves or attacks after a landing, as if it's recovering").
    /// For RecoverTicks after an orbital drop the Knight does not move on its own (Seek &amp; destroy, stomp) and its weapons do not fire:
    /// their auto-targeting is switched off and restored afterwards. Not saved (a save inside that second simply skips the rest of it).
    /// </summary>
    public static class KnightLanding
    {
        public const int RecoverTicks = 60;
        static readonly Dictionary<int, int> until = new Dictionary<int, int>();
        static readonly Dictionary<int, List<KeyValuePair<VehicleTurret, bool>>> savedAuto = new Dictionary<int, List<KeyValuePair<VehicleTurret, bool>>>();

        public static bool Recovering(VehiclePawn v)
        {
            return v != null && until.TryGetValue(v.thingIDNumber, out int t) && Find.TickManager.TicksGame < t;
        }

        public static void Start(VehiclePawn v)
        {
            if (v == null) return;
            until[v.thingIDNumber] = Find.TickManager.TicksGame + RecoverTicks;
            var list = new List<KeyValuePair<VehicleTurret, bool>>();
            IReadOnlyList<VehicleTurret> ts = v.CompVehicleTurrets?.Turrets;
            if (ts != null)
                foreach (VehicleTurret tr in ts)
                {
                    list.Add(new KeyValuePair<VehicleTurret, bool>(tr, tr.AutoTarget));
                    try { if (tr.AutoTarget) tr.AutoTarget = false; if (tr.targetInfo.IsValid) tr.SetTarget(LocalTargetInfo.Invalid); } catch { }
                }
            savedAuto[v.thingIDNumber] = list;
        }

        /// <summary>Called a few times a second: gives back the weapons of Knights whose recovery is over.</summary>
        public static void Tick()
        {
            if (until.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (int id in until.Where(kv => now >= kv.Value).Select(kv => kv.Key).ToList())
            {
                until.Remove(id);
                if (savedAuto.TryGetValue(id, out var list))
                {
                    foreach (var kv in list) { try { if (kv.Value && kv.Key.CanAutoTarget && !kv.Key.AutoTarget) kv.Key.AutoTarget = true; } catch { } }
                    savedAuto.Remove(id);
                }
            }
        }
    }

    /// <summary>Ticks KnightLanding (not tied to one map: the Knights may be anywhere).</summary>
    public class GameComponent_KnightCombat : GameComponent
    {
        public GameComponent_KnightCombat(Game game) { }
        public override void GameComponentTick() { if (Find.TickManager.TicksGame % 5 == 0) KnightLanding.Tick(); }
    }

    /// <summary>
    /// User 2026-11-13: "make the weapons more powerful against pawns". Damage that a Knight deals (its weapons, blasts, stomps) to a pawn that is
    /// not a vehicle is multiplied, and cuts through more armour. Vehicles, buildings and turrets take normal damage.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_KnightDamageVsPawns
    {
        public const float DamageMult = 2.5f;
        public static readonly bool CEActive = ModsConfig.IsActive("CETeam.CombatExtended");
        public const float ArmourPenMult = 1.75f;
        static readonly AccessTools.StructFieldRef<DamageInfo, float> armourPen = AccessTools.StructFieldRefAccess<DamageInfo, float>("armorPenetrationInt");

        static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (!(__instance is Pawn) || __instance is VehiclePawn || CEActive) return;     // Combat Extended does its own realistic damage
            if (!(dinfo.Instigator is VehiclePawn v) || KnightLoadout.ClassFor(v.VehicleDef) == null) return;
            dinfo.SetAmount(dinfo.Amount * DamageMult);
            armourPen(ref dinfo) = armourPen(ref dinfo) * ArmourPenMult;
        }
    }

    public static class KnightLethal
    {
        /// <summary>Shots that nothing on foot survives (user 2026-11-13: "nothing should survive the power of the magna lascannon").</summary>
        static readonly HashSet<string> lethal = new HashSet<string> { "LIK_Proj_magna" };
        public static bool Is(ThingDef projectile) { return projectile != null && lethal.Contains(projectile.defName); }

        /// <summary>Every hostile pawn (not a vehicle) in the heart of the blast is killed outright.</summary>
        public static void Strike(Map map, IntVec3 cell, float radius, Thing launcher, ThingDef projectile)
        {
            if (map == null || !cell.InBounds(map)) return;
            float core = Mathf.Max(1.5f, radius * 0.75f);
            var victims = new List<Pawn>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, core, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Thing t in c.GetThingList(map))
                    if (t is Pawn p && !(p is VehiclePawn) && !p.Dead && (launcher == null || p.HostileTo(launcher)) && !victims.Contains(p)) victims.Add(p);
            }
            foreach (Pawn p in victims)
            {
                try { p.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f, 99f, -1f, launcher, null, projectile)); }
                catch (System.Exception e) { Log.ErrorOnce("[LIK] lethal shot could not kill " + p.LabelShort + ": " + e.Message, 0x1e7a1 ^ p.thingIDNumber); }
            }
        }
    }

    /// <summary>
    /// FRIENDLY FIRE setting (user 2026-11-13, default OFF): damage caused by a player Knight - its guns, blasts, burn-through beams, stomps and
    /// drop impacts - is cancelled when it would land on something friendly: the player's pawns, prisoners and slaves, Knights and other vehicles,
    /// buildings, and anything of a non-hostile faction. Enemies and wild animals are hit as normal.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    [HarmonyPriority(Priority.First)]
    public static class Patch_KnightNoFriendlyFire
    {
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (LIKMod.I.friendlyFire) return true;
            if (!(dinfo.Instigator is VehiclePawn v) || v.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(v.VehicleDef) == null) return true;
            if (!Friendly(__instance)) return true;
            __result = new DamageWorker.DamageResult();
            return false;
        }

        public static bool Friendly(Thing t)
        {
            if (t == null) return false;
            Faction f = t.Faction;
            if (t is Pawn p)
            {
                if (p.IsPrisonerOfColony || p.IsSlaveOfColony || p.HostFaction == Faction.OfPlayer) return true;
                if (f == null) return false;                       // wild animals and the like
            }
            if (f == null) return false;                           // unowned walls, ruins, trees
            return f == Faction.OfPlayer || !f.HostileTo(Faction.OfPlayer);
        }
    }
}
