using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;
using Verse.AI;

namespace LIK
{
    /// <summary>
    /// ION SHIELD - every Knight's own (user 2026-11-25, after a lore + rules check: every Imperial Knight carries an ion shield projected from
    /// its torso, angled toward the enemy; it deflects and slows shots rather than soaking them like a Titan's void shield, and does nothing
    /// against melee; tabletop: 5+ invulnerable vs ranged on every class). User: "reduced shots that get through and the deflected shots;
    /// a button that directs the shield to face one of the Knight's directions or an auto mode".
    ///  - covers a 180-degree arc centred on its direction; only RANGED / explosive damage arriving inside that arc is affected
    ///  - each such hit: DeflectChance it glances off completely, otherwise it gets through reduced to PassThrough of its damage
    ///  - shots from outside the arc (the unshielded side) and every melee blow get through in full
    ///  - direction: Auto (toward the enemies, weighted by distance - AI Knights always use this) or fixed Front / Right / Rear / Left of the Knight
    /// Works with or without Combat Extended (hooked on VehicleStatHandler.ApplyDamage, which every hit on a vehicle goes through, ahead of CE).
    /// Stacks with the Questoris void shield and the Cerastus ion gauntlet.
    /// </summary>
    public static class IonShieldTorso
    {
        public const float DeflectChance = 1f / 3f;     // tabletop 5+
        public const float PassThrough = 0.7f;          // a shot that is not deflected is still slowed: 30 % less damage
        public const float Arc = 180f;
        public static readonly string[] ModeNames = { "Auto", "Front", "Right", "Rear", "Left" };

        // CHARGE (user 2026-11-25 "add a charge"): everything the shield stops (a whole deflected shot, the 30 % taken off one that gets through)
        // drains it. Empty = it collapses: every shot gets through in full until it restarts (weak) CollapseTicks later. Recharges once the Knight
        // has not been hit for RechargeDelay (empty -> full in ~RechargeSeconds). Bigger Knights, bigger generators.
        const int CollapseTicks = 480, RechargeDelay = 300;
        const float RechargeSeconds = 20f;
        public static float Capacity(VehiclePawn v)
        {
            string c = KnightLoadout.ClassFor(v.VehicleDef)?.defName ?? "";
            if (c.Contains("Armiger")) return 300f;
            if (c.Contains("Acastus")) return 1200f;
            if (c.Contains("Dominus")) return 800f;
            return 600f;                                    // Questoris, Cerastus
        }
        public static float Charge(VehiclePawn v, CompKnightHead c) { if (c.torsoCharge < 0f) c.torsoCharge = Capacity(v); return c.torsoCharge; }
        public static bool Down(CompKnightHead c) { return Find.TickManager.TicksGame < c.torsoDownUntil; }

        /// <summary>Every 30 ticks: restart after a collapse, recharge after a quiet spell.</summary>
        public static void Tick(VehiclePawn v, CompKnightHead c)
        {
            if (KnightLoadout.ClassFor(v.VehicleDef) == null) return;
            int now = Find.TickManager.TicksGame;
            float cap = Capacity(v), charge = Charge(v, c);
            if (c.torsoDownUntil > 0 && now >= c.torsoDownUntil)
            {
                c.torsoDownUntil = 0;
                c.torsoCharge = cap * 0.25f;
                DefDatabase<SoundDef>.GetNamedSilentFail("EnergyShield_Reset")?.PlayOneShot(new TargetInfo(v.Position, v.Map));
                return;
            }
            if (Down(c)) return;
            if (charge < cap && now - c.lastTorsoHit >= RechargeDelay) c.torsoCharge = Mathf.Min(cap, charge + cap / (RechargeSeconds * 2f));
        }

        static void Collapse(VehiclePawn v, CompKnightHead c, float fromAngle)
        {
            c.torsoCharge = 0f;
            c.torsoDownUntil = Find.TickManager.TicksGame + CollapseTicks;
            Map map = v.Map;
            Vector3 centre = v.DrawPos;
            float r = IonShield.Radius(v);
            for (int i = 0; i <= 10; i++)
            {
                Vector3 q = centre + Vector3.forward.RotatedBy(fromAngle + Mathf.Lerp(-90f, 90f, i / 10f)) * r;
                if (!q.InBounds(map)) continue;
                FleckMaker.ThrowMicroSparks(q, map);
                if (i % 3 == 0) KnightEffects.ElectricFlash(q, map, 0.6f);
            }
            DefDatabase<SoundDef>.GetNamedSilentFail("EnergyShield_Broken")?.PlayOneShot(new TargetInfo(v.Position, map));
            if (v.Faction == Faction.OfPlayer) Messages.Message("The ion shield of " + v.LabelShort + " is overloaded and collapses!", v, MessageTypeDefOf.NegativeEvent, false);
        }
        static int lastSound;

        public static float Direction(VehiclePawn v, CompKnightHead c)
        {
            float face = v.FullRotation.AsAngle;
            switch (c.ionMode)
            {
                case 1: return face;
                case 2: return face + 90f;
                case 3: return face + 180f;
                case 4: return face + 270f;
                default: return c.ionAutoAngle >= 0f ? c.ionAutoAngle : face;
            }
        }

        /// <summary>Every second: auto mode turns the shield toward the enemies (closer ones count more).</summary>
        public static void AutoAim(VehiclePawn v, CompKnightHead c)
        {
            if (c.ionMode != 0 || v.Faction == null || v.Map == null || KnightLoadout.ClassFor(v.VehicleDef) == null) return;
            Vector3 sum = Vector3.zero;
            foreach (IAttackTarget t in v.Map.attackTargetsCache.TargetsHostileToFaction(v.Faction))
            {
                Thing th = t.Thing;
                if (th == null || !th.Spawned || t.ThreatDisabled(null)) continue;
                if (th is Pawn p && (p.Downed || p.Dead)) continue;
                Vector3 d = (th.DrawPos - v.DrawPos).Yto0();
                float dist = d.magnitude;
                if (dist < 0.5f || dist > 70f) continue;
                sum += d / (dist * dist);                                  // direction weighted by 1/distance
            }
            c.ionAutoAngle = sum.sqrMagnitude > 1e-6f ? Mathf.Repeat(sum.AngleFlat(), 360f) : -1f;
        }

        /// <summary>A hit arriving at a Knight: deflected (amount 0), slowed, or untouched.</summary>
        public static void Intercept(VehiclePawn v, ref DamageInfo dinfo)
        {
            if (dinfo.Amount <= 0f || dinfo.Def == null || !(dinfo.Def.isRanged || dinfo.Def.isExplosive)) return;   // melee, fire, ... pass
            if (!v.Spawned || KnightLoadout.ClassFor(v.VehicleDef) == null) return;
            CompKnightHead c = v.GetComp<CompKnightHead>();
            if (c == null) return;
            Thing inst = dinfo.Instigator;
            if (inst == v) return;
            float fromAngle = inst != null && inst.Spawned && inst.Map == v.Map ? (inst.DrawPos - v.DrawPos).Yto0().AngleFlat() : dinfo.Angle + 180f;
            if (Mathf.Abs(Mathf.DeltaAngle(Direction(v, c), fromAngle)) > Arc / 2f) return;             // the unshielded side
            if (Down(c)) return;                                                                          // collapsed: everything gets through
            float charge = Charge(v, c);
            bool deflect = Rand.Chance(DeflectChance);
            float before = dinfo.Amount;
            float stop = Mathf.Min(charge, deflect ? before : before * (1f - PassThrough));                 // what the shield takes, as far as its charge goes
            dinfo.SetAmount(before - stop);
            c.torsoCharge = charge - stop;
            c.lastTorsoHit = Find.TickManager.TicksGame;
            Fx(v, fromAngle, before, deflect && stop >= before - 0.01f);
            if (c.torsoCharge <= 0.01f) Collapse(v, c, fromAngle);
        }

        static void Fx(VehiclePawn v, float fromAngle, float amount, bool deflect)
        {
            Map map = v.Map;
            if (map == null) return;
            IonShieldDraw.Hit(v, fromAngle - IonShield.FacingAngle(v), deflect ? 2.6f + Mathf.Min(3f, amount / 20f) : 1.5f);
            Vector3 hit = v.DrawPos + Vector3.forward.RotatedBy(fromAngle) * IonShield.Radius(v);
            if (!hit.InBounds(map) || !hit.ShouldSpawnMotesAt(map)) return;
            for (int k = 0; k < (deflect ? 4 : 1); k++) FleckMaker.ThrowMicroSparks(hit + Gen.RandomHorizontalVector(0.4f), map);
            int now = Find.TickManager.TicksGame;
            if (deflect && now - lastSound > 8)
            {
                lastSound = now;
                DefDatabase<SoundDef>.GetNamedSilentFail("EnergyShield_AbsorbDamage")?.PlayOneShot(new TargetInfo(hit.ToIntVec3(), map));
            }
        }
    }

    /// <summary>Every hit on a vehicle goes through VehicleStatHandler.ApplyDamage (Combat Extended replaces its body, a prefix still runs first).</summary>
    [HarmonyPatch(typeof(VehicleStatHandler), nameof(VehicleStatHandler.ApplyDamage))]
    public static class Patch_IonShieldTorso
    {
        static readonly AccessTools.FieldRef<VehicleStatHandler, VehiclePawn> vehicleRef = AccessTools.FieldRefAccess<VehicleStatHandler, VehiclePawn>("vehicle");

        [HarmonyPriority(Priority.First)]
        static void Prefix(VehicleStatHandler __instance, ref DamageInfo __0)
        {
            VehiclePawn v;
            try { v = vehicleRef(__instance); } catch { return; }
            if (v != null) IonShieldTorso.Intercept(v, ref __0);
        }
    }

    /// <summary>The shield direction button on the player's Knights: Auto or one of the Knight's four sides.</summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_IonShieldTorsoGizmo
    {
        static Texture2D icon;
        static Texture2D Icon => icon ?? (icon = ContentFinder<Texture2D>.Get("Things/Knights/Effects/ion_hit", false) ?? BaseContent.BadTex);

        static void Postfix(VehiclePawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Faction != Faction.OfPlayer || KnightLoadout.ClassFor(__instance.VehicleDef) == null) return;
            CompKnightHead c = __instance.GetComp<CompKnightHead>();
            if (c == null) return;
            __result = __result.Concat(new Gizmo[]
            {
                new Command_Action
                {
                    defaultLabel = "Ion shield: " + IonShieldTorso.ModeNames[Mathf.Clamp(c.ionMode, 0, 4)],
                    defaultDesc = "Where this Knight's ion shield faces. It covers half the Knight (180 degrees) around that direction: a shot or blast from that side "
                                + "is deflected 1 time in 3, and every other one still gets through 30 % weaker. Shots from the other side and all melee blows get through in full.\n\n"
                                + "Everything it stops drains its charge (" + IonShieldTorso.Charge(__instance, c).ToString("0") + " / " + IonShieldTorso.Capacity(__instance).ToString("0") + "). Emptied, it collapses for 8 s; it recharges once the Knight has not been hit for 5 s.\n\n"
                                + "Auto: the shield turns toward the enemies by itself (closer ones count more). Front / Right / Rear / Left: fixed to that side of the Knight.",
                    icon = Icon,
                    action = () => Find.WindowStack.Add(new FloatMenu(Enumerable.Range(0, 5).Select(i =>
                        new FloatMenuOption(IonShieldTorso.ModeNames[i] + (i == c.ionMode ? " (current)" : ""), () => c.ionMode = i)).ToList()))
                }
            });
        }
    }
}
