using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace LIK
{
    // ------------------------------------------------------------------ settings
    /// <summary>Each toll has a multiplier: 1 = the default intensity, 0 = off.</summary>
    public class LIKSettings : ModSettings
    {
        public float strainMult = 1f;     // neural strain build-up while piloting
        public float wrathMult = 1f;      // machine-spirit wrath: unfit pilots strain more, damage to the Knight lashes back
        public float ageMult = 1f;        // biological ageing of the pilot
        public bool requireSkill = true;  // a Knight needs a minimum Piloting level to be driven
        public float skillEffect = 1f;    // how much Piloting skill lessens tolls / raises speed and accuracy (0 = no effect)
        public float trainingMult = 1f;   // speed of Piloting training (squire drills, mentoring, driving, combat)
        public float sizeMult = 1f;       // how strongly the Knight's size scales every toll (0 = size ignored)
        public float traitMult = 1f;      // how much the pilot's personality (traits) lessens/worsens the tolls
        public float combatMult = 1f;     // extra strain while enemies are near (0 = off)
        public bool alignHelper = false;   // weapon alignment helper: "Align weapons" button on every Knight
        public bool alignTint = false;     // helper: weapons orange, body-cover layers cyan (weapon colour needs a restart)
        public bool alignCross = false;    // helper: green cross on every weapon pivot
        public int costTier = CostConfig.DefaultTier;   // forge costs: 0 for fun, 1 arcade, 2 challenge, 3 realistic (CostConfig.cs)
        public float costExtra = 1f;        // fine tuning on top of the tier
        public bool aimTurnBetween = false;   // 2026-11-15: aim pictures (32-step Dominus top turrets) also turned flat by the leftover angle (jolts at each swap)
        public bool aimPictures = true;    // TRIAL: Questoris / Dominus top weapons show the picture for where the weapon points (FacingArt.AimMode)
        public bool dropWar = true;         // drop-zone warfare: an enemy base hit by an orbital drop sends reinforcement waves (DropZoneWar.cs)
        public float dropWarWaves = 1f;     // number of waves (x)
        public float dropWarStrength = 1f;  // size of each wave (x)
        public bool stomp = true;           // Knights stomp hostile pawns that come close (Stomp.cs)
        public float weaponGlow = 0.7f;     // 2026-11-23: glow of painted energy parts (plasma coils, volkite cells) - Source/WeaponGlow.cs
        public bool statusPanel = true;     // user 2026-11-25: Knight status panel (green schematic + part health) when a Knight is selected - KnightHud.cs
        public bool weaponLight = true;     // 2026-11-23: energy weapons light up the ground around their Knight (steady + flash when firing)
        public float smokeAmount = 0.5f;   // user 2026-11-23: battle smoke and dust (Source/GlowGuard.cs), 100 % = unlimited
        public float craterDays = 5f;      // user 2026-11-25: drop / meltdown craters fade away after this many days (0 = never) - Source/Craters.cs
        public float fxGlow = 0.35f;       // user 2026-11-22: weapon impact glow strength (1 = the old full glare that hid the fight)
        public bool raidDropOnBase = false; // user 2026-11-25: enemy Knights of a pod raid may drop into the Home area / onto buildings (KnightRaids.cs)
        public bool friendlyFire = false;   // user 2026-11-13: off = Knight weapons, blasts and stomps never hurt anything friendly (KnightCombat.cs)
        public bool strainPaused = true;    // user 2026-11-13: TEMPORARY - neural strain switched off (and cleared) until it is reworked
        public int dropGapSeconds = 0;      // group orbital drop: 0 = all at once, else seconds between Knights (hangar button)

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref strainMult, "strainMult", 1f);
            Scribe_Values.Look(ref wrathMult, "wrathMult", 1f);
            Scribe_Values.Look(ref ageMult, "ageMult", 1f);
            Scribe_Values.Look(ref traitMult, "traitMult", 1f);
            Scribe_Values.Look(ref sizeMult, "sizeMult", 1f);
            Scribe_Values.Look(ref combatMult, "combatMult", 1f);
            Scribe_Values.Look(ref requireSkill, "requireSkill", true);
            Scribe_Values.Look(ref skillEffect, "skillEffect", 1f);
            Scribe_Values.Look(ref trainingMult, "trainingMult", 1f);
            Scribe_Values.Look(ref alignHelper, "alignHelper", false);
            Scribe_Values.Look(ref alignTint, "alignTint", false);
            Scribe_Values.Look(ref alignCross, "alignCross", false);
            Scribe_Values.Look(ref aimPictures, "aimPictures", true);
            Scribe_Values.Look(ref aimTurnBetween, "aimTurnBetween", false);
            Scribe_Values.Look(ref costTier, "costTier", CostConfig.DefaultTier);
            Scribe_Values.Look(ref costExtra, "costExtra", 1f);
            Scribe_Values.Look(ref dropWar, "dropWar", true);
            Scribe_Values.Look(ref dropWarWaves, "dropWarWaves", 1f);
            Scribe_Values.Look(ref dropWarStrength, "dropWarStrength", 1f);
            Scribe_Values.Look(ref dropGapSeconds, "dropGapSeconds", 0);
            Scribe_Values.Look(ref stomp, "stomp", true);
            Scribe_Values.Look(ref friendlyFire, "friendlyFire", false);
            Scribe_Values.Look(ref raidDropOnBase, "raidDropOnBase", false);
            Scribe_Values.Look(ref fxGlow, "fxGlow", 0.35f);
            Scribe_Values.Look(ref smokeAmount, "smokeAmount", 0.5f);
            Scribe_Values.Look(ref craterDays, "craterDays", 5f);
            Scribe_Values.Look(ref statusPanel, "statusPanel", true);
            Scribe_Values.Look(ref weaponGlow, "weaponGlow", 0.7f);
            Scribe_Values.Look(ref weaponLight, "weaponLight", true);
            Scribe_Values.Look(ref strainPaused, "strainPaused", true);
        }
    }

    public class LIKMod : Mod
    {
        public static LIKSettings S;
        public static LIKSettings I { get { return S ?? (S = new LIKSettings()); } }

        public LIKMod(ModContentPack content) : base(content)
        {
            S = GetSettings<LIKSettings>();
            // which build is running (2026-11-13: a game started 20 s before a rebuild finished still ran the old code)
            try
            {
                string dll = System.IO.Path.Combine(content.RootDir, "Assemblies", "LocalImperialKnights.dll");
                Log.Message("[LIK] Imperial Knights build " + (System.IO.File.Exists(dll) ? System.IO.File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm:ss") : "?"));
            }
            catch { }
        }

        public override string SettingsCategory() { return "Imperial Knights"; }

        Vector2 settingsScroll;
        float settingsHeight = 1200f;

        // ---- small helpers: section header, slider with its value in the label, explanations as hover tooltips (user 2026-11-23: UI was confusing)
        static void Header(Listing_Standard l, string text)
        {
            l.Gap(6f);
            Text.Font = GameFont.Medium; l.Label(text); Text.Font = GameFont.Small;
            l.GapLine(6f);
        }
        static float Slide(Listing_Standard l, string label, string value, float v, float min, float max, string tip, float snap = 10f)
        {
            Rect r = l.GetRect(Text.LineHeight);
            Widgets.Label(r.LeftPart(0.75f), label);
            Text.Anchor = TextAnchor.MiddleRight; Widgets.Label(r.RightPart(0.25f), value); Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            TooltipHandler.TipRegion(r, tip);
            v = Mathf.Round(l.Slider(v, min, max) * snap) / snap;
            l.Gap(2f);
            return v;
        }
        static string X(float f) { return f <= 0f ? "off" : "x" + f.ToString("0.0#"); }
        static string Pct(float f) { return (f * 100f).ToString("F0") + "%"; }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // two columns in a scroll view: left = pilots, right = forge / combat / visuals (2026-11-23 clean-up)
            LIKSettings s = I;
            Rect view = new Rect(0f, 0f, inRect.width - 20f, settingsHeight);
            Widgets.BeginScrollView(inRect, ref settingsScroll, view);
            var l = new Listing_Standard();
            l.ColumnWidth = view.width / 2f - 17f;
            l.Begin(view);

            // ================= left column: piloting + tolls
            Header(l, "Piloting");
            l.CheckboxLabeled("Minimum Piloting skill to drive a Knight", ref s.requireSkill,
                "Armiger 4, Questoris 8, Cerastus 10, Dominus 12, Acastus 14.");
            s.skillEffect = Slide(l, "Skill effect", X(s.skillEffect), s.skillEffect, 0f, 3f,
                "How much Piloting skill lessens the tolls and raises Knight speed and gunnery. 0 = skill has no effect.");
            s.trainingMult = Slide(l, "Training speed", X(s.trainingMult), s.trainingMult, 0.1f, 5f,
                "How fast Piloting is learned: squire training, mentoring, driving and combat.");

            Header(l, "Pilot tolls");
            Text.Font = GameFont.Tiny; l.Label("x1 = normal, 0 = off, higher = harsher. Hover a setting for details."); Text.Font = GameFont.Small;
            l.CheckboxLabeled("Pause neural strain (being reworked)", ref s.strainPaused,
                "While on, Knights cause no neural strain or feedback shock, and existing strain is cleared.");
            s.strainMult = Slide(l, "Neural strain", X(s.strainMult), s.strainMult, 0f, 5f, "Builds up on the pilot while the Knight is running.");
            s.wrathMult = Slide(l, "Machine-spirit wrath", X(s.wrathMult), s.wrathMult, 0f, 5f, "Unfit pilots strain more, and a damaged Knight lashes back at its pilot.");
            s.ageMult = Slide(l, "Ageing", X(s.ageMult), s.ageMult, 0f, 5f, "Each hour on the Throne Mechanicum ages the pilot.");
            s.sizeMult = Slide(l, "Knight size", X(s.sizeMult), s.sizeMult, 0f, 3f, "Bigger Knights take a heavier toll. 0 = size is ignored.");
            s.combatMult = Slide(l, "Combat stress", X(s.combatMult), s.combatMult, 0f, 3f, "Strain builds faster the more enemies are near.");
            s.traitMult = Slide(l, "Personality", X(s.traitMult), s.traitMult, 0f, 3f, "How much pilot traits (tough, iron-willed, nervous ...) change the tolls. 0 = traits ignored.");
            float leftH = l.CurHeight;

            // ================= right column: forge, combat, visuals
            l.NewColumn();
            Header(l, "Forge costs");
            CostConfig.Tier tier = CostConfig.Current;
            if (l.ButtonTextLabeled("Cost level (parts, weapons, modules)", tier.label))
            {
                var menu = new List<FloatMenuOption>();
                for (int i = 0; i < CostConfig.Tiers.Length; i++)
                {
                    int ii = i;
                    menu.Add(new FloatMenuOption(CostConfig.Tiers[i].label + " - " + CostConfig.Tiers[i].desc, delegate { s.costTier = ii; CostConfig.Apply(); }));
                }
                Find.WindowStack.Add(new FloatMenu(menu));
            }
            Text.Font = GameFont.Tiny;
            l.Label(tier.desc);
            Text.Font = GameFont.Small;
            float oldExtra = s.costExtra;
            s.costExtra = Slide(l, "Fine tune", "x" + s.costExtra.ToString("0.00"), s.costExtra, 0.25f, 3f, "Multiplies every cost of the chosen level.", 20f);
            if (oldExtra != s.costExtra) CostConfig.Apply();

            Header(l, "Combat");
            l.CheckboxLabeled("Stomp", ref s.stomp, "A running Knight stamps on enemies that come right up to it, and crushes any under it.");
            l.CheckboxLabeled("Friendly fire", ref s.friendlyFire, "Knight weapons, blasts and stomps can hurt your own colonists, Knights, buildings and allies.");
            l.CheckboxLabeled("Enemy Knights may drop into your base", ref s.raidDropOnBase, "Off: Knights of a drop-pod raid land on open ground outside your Home area.\nOn: they can land right in your base like a centre-drop raid - the impact crater flattens every building, wall and item in it. A Knight never lands with one of your pawns under its feet.");
            l.CheckboxLabeled("Drop-zone warfare", ref s.dropWar, "An enemy base hit by an orbital drop sends waves of reinforcements.");
            if (s.dropWar)
            {
                s.dropWarWaves = Slide(l, "   Waves", "x" + s.dropWarWaves.ToString("0.00"), s.dropWarWaves, 0.25f, 4f, "Number of waves. x1 = 3 for a Questoris, 4 for a Cerastus, Dominus or Acastus.", 4f);
                s.dropWarStrength = Slide(l, "   Wave strength", "x" + s.dropWarStrength.ToString("0.00"), s.dropWarStrength, 0.25f, 5f, "Size of each wave. Every wave is 35% heavier than the one before.", 4f);
            }

            Header(l, "Visuals");
            s.fxGlow = Slide(l, "Shot and hit glare", Pct(s.fxGlow), s.fxGlow, 0f, 1f, "Glare of Knight weapon shots and hits. Smoke, debris and shockwaves are not affected.", 20f);
            s.smokeAmount = Slide(l, "Battle smoke", Pct(s.smokeAmount), s.smokeAmount, 0f, 1f, "How much smoke and dust piles up where the fighting is. Lower it to keep the battlefield visible. 100% = no limit.", 20f);
            s.craterDays = Slide(l, "Craters fade after", s.craterDays <= 0f ? "never" : s.craterDays.ToString("0") + (s.craterDays == 1f ? " day" : " days"), s.craterDays, 0f, 30f, "Orbital drop and reactor meltdown craters slowly fade into the ground over the second half of this time, then are gone. 0 = they stay until deconstructed.", 1f);
            s.weaponGlow = Slide(l, "Weapon part glow", Pct(s.weaponGlow), s.weaponGlow, 0f, 1f, "Glow of plasma coils, volkite cells and other energy parts on the weapons.", 20f);
            l.CheckboxLabeled("Knight status panel", ref s.statusPanel, "When one Knight is selected (any faction), a small auspex panel above the inspect pane shows its schematic with each part shaded by health, every part's health, integrity and void shield.");
            l.CheckboxLabeled("Energy weapons light the ground", ref s.weaponLight, "A steady glow around the Knight, and a flash when it fires.");
            l.CheckboxLabeled("Top guns turn to aim", ref s.aimPictures, "Questoris and Dominus top weapons show the picture for where they aim (off = where the Knight faces).");
            l.CheckboxLabeled("Smooth aim between steps", ref s.aimTurnBetween, "Guns with 3D aim pictures also tilt the picture between steps (off = clean steps only).");

            if (Prefs.DevMode)      // art alignment helper: modding tool, only shown in Dev mode (2026-11-23)
            {
                Header(l, "Dev: weapon alignment");
                l.CheckboxLabeled("\"Align weapons\" button on Knights", ref s.alignHelper, "Nudge weapon positions and turning points in game, hide weapons, save to a file.");
                if (s.alignHelper)
                {
                    l.CheckboxLabeled("   Tint weapons and body layers", ref s.alignTint, "Weapons orange, body-cover layers cyan. Weapon colour needs a game restart.");
                    l.CheckboxLabeled("   Cross on turning points", ref s.alignCross);
                }
            }
            l.Gap(12f);
            if (l.ButtonText("Reset all to defaults")) { s.costTier = CostConfig.DefaultTier; s.costExtra = 1f; CostConfig.Apply(); s.aimPictures = true; s.aimTurnBetween = false; s.strainMult = 1f; s.wrathMult = 1f; s.ageMult = 1f; s.traitMult = 1f; s.sizeMult = 1f; s.combatMult = 1f; s.requireSkill = true; s.skillEffect = 1f; s.trainingMult = 1f; s.alignHelper = false; s.alignTint = false; s.alignCross = false; s.stomp = true; s.friendlyFire = false; s.raidDropOnBase = false; s.dropWar = true; s.dropWarWaves = 1f; s.dropWarStrength = 1f; s.fxGlow = 0.35f; s.smokeAmount = 0.5f; s.craterDays = 5f; s.statusPanel = true; s.weaponGlow = 0.7f; s.weaponLight = true; }
            float rightH = l.CurHeight;
            l.End();
            Widgets.EndScrollView();
            settingsHeight = Mathf.Max(leftH, rightH) + 20f;      // the scroll area follows the taller column
        }
    }

    // ------------------------------------------------------------------ defs
    /// <summary>Per-Knight toll values (set on the VehicleDef; the settings multiply them).</summary>
    public class DefModExtension_KnightToll : DefModExtension
    {
        public float strainPerHour = 0.05f;     // neural strain (0..1) per hour of piloting
        public float ageDaysPerHour = 0.25f;    // biological days of age per hour of piloting
        public float feedbackScale = 1f;        // how hard damage to the Knight hits the pilot
        public float referenceSize = 4f;        // footprint (cells) the per-hour values above are written for (the Armiger)
        public int minPiloting = 0;             // Piloting skill needed to drive this Knight
    }

    /// <summary>Data-driven: a trait (and degree) that lessens or worsens a pilot's tolls. See Defs/PilotTraits.</summary>
    public class PilotTraitModifierDef : Def
    {
        public TraitDef trait;
        public int degree;
        public float strain = 1f;
        public float feedback = 1f;
        public float age = 1f;
    }

    [DefOf]
    public static class LIKDefOf
    {
        public static HediffDef LIK_NeuralStrain;
        public static HediffDef LIK_FeedbackShock;
        static LIKDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(LIKDefOf)); }
    }

    // ------------------------------------------------------------------ logic
    public static class TollUtil
    {
        public const float RecoveryPerHour = 0.03f;

        /// <summary>The crew that actually drives the Knight (movement roles).</summary>
        public static List<Pawn> Pilots(VehiclePawn v)
        {
            var list = new List<Pawn>();
            if (v?.handlers == null) return list;
            foreach (VehicleRoleHandler h in v.handlers)
                if (h.RequiredForMovement && h.thingOwner != null)
                    foreach (Pawn p in h.thingOwner.InnerListForReading) list.Add(p);
            return list;
        }

        /// <summary>The machine spirit favours a disciplined mind: 2.0 for Intellectual 0, about 1.3 at 10, 0.6 at 20 (before the wrath setting).</summary>
        public static float MindFactor(Pawn p)
        {
            int skill = p.skills != null ? p.skills.GetSkill(SkillDefOf.Intellectual).Level : 8;
            return Mathf.Lerp(2f, 0.6f, Mathf.Clamp01(skill / 20f));
        }

        /// <summary>Combined multipliers from the pilot's traits (each scaled by the personality setting, floor x0.25).</summary>
        public static void TraitFactors(Pawn p, out float strain, out float feedback, out float age)
        {
            strain = feedback = age = 1f;
            float k = LIKMod.I.traitMult;
            if (k <= 0f || p?.story?.traits == null) return;
            foreach (PilotTraitModifierDef m in DefDatabase<PilotTraitModifierDef>.AllDefsListForReading)
            {
                if (m.trait == null || !p.story.traits.HasTrait(m.trait, m.degree)) continue;
                strain *= Mathf.Lerp(1f, m.strain, k);
                feedback *= Mathf.Lerp(1f, m.feedback, k);
                age *= Mathf.Lerp(1f, m.age, k);
            }
            strain = Mathf.Max(0.25f, strain);
            feedback = Mathf.Max(0.25f, feedback);
            age = Mathf.Max(0.25f, age);
        }

        /// <summary>Personality and Piloting skill together.</summary>
        public static void PilotFactors(Pawn p, out float strain, out float feedback, out float age)
        {
            TraitFactors(p, out strain, out feedback, out age);
            float sk = PilotingUtil.TollFactor(p);
            strain *= sk; feedback *= sk; age *= sk;
        }

        public const float SizeExponent = 1.3f;

        /// <summary>
        /// Bigger Knights take a heavier toll: (footprint / reference size) ^ 1.3, the exponent scaled by the size setting.
        /// With the defaults: Armiger (4) x1.0, Questoris (6) about x1.7, Dominus (7) about x2.1.
        /// </summary>
        public static float SizeFactor(VehiclePawn v)
        {
            var ext = v?.def.GetModExtension<DefModExtension_KnightToll>();
            float k = LIKMod.I.sizeMult;
            if (ext == null || k <= 0f) return 1f;
            float size = Mathf.Max(v.def.size.x, v.def.size.z);
            return Mathf.Pow(size / Mathf.Max(1f, ext.referenceSize), SizeExponent * k);
        }

        public const float LifeSupportStrain = 0.35f, LifeSupportAge = 0.4f, LifeSupportFeedback = 0.6f;
        public const float CombatPerEnemy = 0.12f;
        public const int CombatEnemyCap = 15;
        public const float CombatRange = 35f;

        /// <summary>A fitted life-support module greatly slows strain, ageing and damage shock; the effect grows with the Knight's size.</summary>
        public static void LifeSupportFactors(VehiclePawn v, out float strain, out float feedback, out float age)
        {
            strain = feedback = age = 1f;
            if (v?.GetComp<CompKnightHead>()?.lifeSupport != true) return;
            // the bigger the Knight, the bigger its life-support plant: the Armiger gets the base effect, larger Knights get progressively more
            var ext = v.def.GetModExtension<DefModExtension_KnightToll>();
            float ratio = ext == null ? 1f : Mathf.Clamp(ext.referenceSize / Mathf.Max(1f, Mathf.Max(v.def.size.x, v.def.size.z)), 0.1f, 1f);
            strain = Mathf.Max(0.05f, LifeSupportStrain * ratio * ratio);
            age = Mathf.Max(0.10f, LifeSupportAge * ratio * ratio);
            feedback = Mathf.Max(0.15f, LifeSupportFeedback * ratio);
        }

        /// <summary>Combat stress: x1 with no enemies near, up to about x2.8 with fifteen or more within range (scaled by the combat setting).</summary>
        public static float CombatFactor(VehiclePawn v)
        {
            float k = LIKMod.I.combatMult;
            if (k <= 0f || v?.Map == null) return 1f;
            int n = 0;
            float r2 = CombatRange * CombatRange;
            foreach (IAttackTarget at in v.Map.attackTargetsCache.GetPotentialTargetsFor(v))
            {
                Thing th = at.Thing;
                if (th == null || !th.Spawned || at.ThreatDisabled(v) || !GenHostility.HostileTo(th, v)) continue;
                Pawn p = th as Pawn;
                if (p != null && (p.Downed || p.Dead)) continue;
                if (v.Position.DistanceToSquared(th.Position) > r2) continue;
                if (++n >= CombatEnemyCap) break;
            }
            return 1f + CombatPerEnemy * n * k;
        }

        /// <summary>Knight health 0..1: components weighted by their max health (what VF shows as body integrity, near enough).</summary>
        public static float Integrity(VehiclePawn v)
        {
            if (v?.statHandler?.components == null || v.statHandler.components.Count == 0) return 1f;
            float h = 0f, m = 0f;
            foreach (VehicleComponent c in v.statHandler.components) { h += Mathf.Max(0f, c.Health); m += c.MaxHealth; }
            return m > 0f ? Mathf.Clamp01(h / m) : 1f;
        }

        /// <summary>User 2026-11-12: strain should follow the Knight's health. Healthy Knight x0.5, half wrecked x1.75, near destroyed ~x3.</summary>
        public static float DamageStrainFactor(VehiclePawn v) { return 0.5f + 2.5f * (1f - Integrity(v)); }

        public static void AddStrain(Pawn p, float amount)
        {
            if (p?.health == null || amount <= 0f || LIKDefOf.LIK_NeuralStrain == null || LIKMod.I.strainPaused) return;
            Hediff h = p.health.GetOrAddHediff(LIKDefOf.LIK_NeuralStrain);
            h.Severity = Mathf.Min(h.def.maxSeverity, h.Severity + amount);
        }

        public static void Recover(Pawn p, float amount)
        {
            Hediff h = p?.health?.hediffSet?.GetFirstHediffOfDef(LIKDefOf.LIK_NeuralStrain);
            if (h != null) h.Severity -= amount;
        }
    }

    /// <summary>Every 250 ticks: pilots of a running Knight gain strain and age; everyone else recovers.</summary>
    public class MapComponent_KnightToll : MapComponent
    {
        const int Interval = 250;

        public MapComponent_KnightToll(Map map) : base(map) { }

        /// <summary>Neural strain paused (temporary): remove strain and feedback shock from every pawn on this map, Knight crews included.</summary>
        void ClearStrain()
        {
            if (LIKDefOf.LIK_NeuralStrain == null) return;
            var all = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
            foreach (Pawn sp in map.mapPawns.AllPawnsSpawned) if (sp is VehiclePawn kv) all.AddRange(kv.AllPawnsAboard);
            foreach (Pawn p in all)
            {
                var hs = p?.health?.hediffSet;
                if (hs == null) continue;
                Hediff h = hs.GetFirstHediffOfDef(LIKDefOf.LIK_NeuralStrain);
                if (h != null) p.health.RemoveHediff(h);
                if (LIKDefOf.LIK_FeedbackShock != null)
                {
                    Hediff f = hs.GetFirstHediffOfDef(LIKDefOf.LIK_FeedbackShock);
                    if (f != null) p.health.RemoveHediff(f);
                }
            }
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0) return;
            LIKSettings s = LIKMod.I;
            if (s.strainPaused) ClearStrain();
            float hours = Interval / 2500f;
            var driving = new HashSet<Pawn>();
            var parked = new List<Pawn>();

            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn).ToList())
            {
                VehiclePawn v = t as VehiclePawn;
                if (v == null) continue;
                var ext = v.def.GetModExtension<DefModExtension_KnightToll>();
                if (ext == null) continue;
                float sizeF = TollUtil.SizeFactor(v);
                float lsStrain, lsFeedback, lsAge;
                TollUtil.LifeSupportFactors(v, out lsStrain, out lsFeedback, out lsAge);
                float combat = v.Drafted ? TollUtil.CombatFactor(v) : 1f;
                float wreck = TollUtil.DamageStrainFactor(v);
                foreach (Pawn p in TollUtil.Pilots(v))
                {
                    if (!v.Drafted) { parked.Add(p); continue; }
                    driving.Add(p);
                    if (PilotingDefOf.LIK_Piloting != null && p.skills != null)
                        p.skills.Learn(PilotingDefOf.LIK_Piloting, 150f * hours * s.trainingMult);
                    float tStrain, tFeedback, tAge;
                    TollUtil.PilotFactors(p, out tStrain, out tFeedback, out tAge);
                    if (s.strainMult > 0f)
                    {
                        float wrath = 1f + (TollUtil.MindFactor(p) - 1f) * s.wrathMult;
                        TollUtil.AddStrain(p, ext.strainPerHour * hours * s.strainMult * Mathf.Max(0.1f, wrath) * tStrain * sizeF * lsStrain * combat * wreck);
                    }
                    if (s.ageMult > 0f && p.ageTracker != null)
                        p.ageTracker.AgeBiologicalTicks += (long)(ext.ageDaysPerHour * 60000f * hours * s.ageMult * tAge * sizeF * lsAge);
                }
            }

            foreach (Pawn p in parked) TollUtil.Recover(p, TollUtil.RecoveryPerHour * hours);
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                if (!driving.Contains(p)) TollUtil.Recover(p, TollUtil.RecoveryPerHour * hours);
        }
    }

    /// <summary>Machine-spirit wrath: damage to a Knight's components is felt through the neural link.</summary>
    [HarmonyPatch]
    public static class Patch_ComponentDamageFeedback
    {
        static readonly Dictionary<Pawn, int> lastMessage = new Dictionary<Pawn, int>();

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(VehicleComponent), "TakeDamage",
                new[] { typeof(VehiclePawn), typeof(DamageInfo).MakeByRefType(), typeof(bool) });
        }

        static void Prefix(VehicleComponent __instance, out float __state)
        {
            __state = __instance.Health;
        }

        static void Postfix(VehicleComponent __instance, VehiclePawn __0, float __state)
        {
            LIKSettings s = LIKMod.I;
            if (s.wrathMult <= 0f || __0 == null || s.strainPaused) return;
            float lost = __state - __instance.Health;
            if (lost <= 0f) return;
            var ext = __0.def.GetModExtension<DefModExtension_KnightToll>();
            if (ext == null) return;

            bool destroyed = __instance.Health <= 0.01f && __state > 0.01f;
            float spike = (lost / Mathf.Max(1f, __instance.MaxHealth) * 0.5f + (destroyed ? 0.10f : 0f)) * s.wrathMult * ext.feedbackScale * TollUtil.SizeFactor(__0);
            float lsS, lsF, lsA;
            TollUtil.LifeSupportFactors(__0, out lsS, out lsF, out lsA);
            spike *= lsF;
            int now = Find.TickManager.TicksGame;
            foreach (Pawn p in TollUtil.Pilots(__0))
            {
                float tStrain, tFeedback, tAge;
                TollUtil.PilotFactors(p, out tStrain, out tFeedback, out tAge);
                TollUtil.AddStrain(p, spike * tFeedback);
                if (!destroyed || LIKDefOf.LIK_FeedbackShock == null) continue;
                p.health.AddHediff(LIKDefOf.LIK_FeedbackShock);
                int last;
                if (!lastMessage.TryGetValue(p, out last) || now - last > 900)
                {
                    lastMessage[p] = now;
                    Messages.Message("The machine spirit of the " + __0.def.label + " lashes out as its " + __instance.props.label + " fails: " + p.LabelShortCap + " reels from the feedback.", p, MessageTypeDefOf.NegativeEvent, false);
                }
            }
        }
    }


    /// <summary>Void shield: a fitted generator soaks up damage before the Knight's components take it.</summary>
    [HarmonyPatch]
    public static class Patch_VoidShield
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(VehicleComponent), "TakeDamage",
                new[] { typeof(VehiclePawn), typeof(DamageInfo).MakeByRefType(), typeof(bool) });
        }

        static void Prefix(VehiclePawn __0, ref DamageInfo __1)
        {
            CompKnightHead c = __0?.GetComp<CompKnightHead>();
            if (c == null || !c.voidShield) return;
            float soaked = c.Absorb(__1.Amount);
            if (soaked > 0f) __1.SetAmount(Mathf.Max(0f, __1.Amount - soaked));
        }
    }

    [StaticConstructorOnStartup]
    public static class LIKStartup
    {
        static LIKStartup()
        {
            // 2026-11-23 (RimWorld 1.7): PatchAll stops at the first patch whose target is missing, which left most of the mod unpatched when
            // Vehicle Framework 1.7 changed a method. Each patch class is now applied on its own; one that no longer fits is skipped with a warning.
            var harmony = new Harmony("local.imperialknights");
            var failed = new System.Collections.Generic.List<string>();
            foreach (System.Type t in AccessTools.GetTypesFromAssembly(typeof(LIKStartup).Assembly))
            {
                if (!t.IsDefined(typeof(HarmonyPatch), false)) continue;
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (System.Exception e) { failed.Add(t.Name + " (" + (e.InnerException ?? e).Message + ")"); }
            }
            if (failed.Count > 0) Log.Warning("[LIK] " + failed.Count + " patch(es) skipped (game or Vehicle Framework version differs): " + string.Join("; ", failed));
        }
    }
}
