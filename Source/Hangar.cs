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
    // ------------------------------------------------------------------ data (Defs/Assembly/LIK_KnightClasses.xml, generated)
    public class WeaponOption
    {
        public ThingDef weapon;          // the forged weapon item that is consumed
        public string turretKey;         // key of the matching reference turret in the vehicle def
        public bool autoAlt;             // the Knight changes between the weapon and its alternate by itself, by range (reaper chainfist: fist / built-in bolter)
        public List<string> altTurretKeys;     // alternate-mode turrets that REPLACE this weapon on the same mount when the Knight is switched (siege claw <-> rad cleanser)
    }

    public class KnightMount
    {
        public string key;
        public string label;
        public const string None = "none";
        public bool optional;                // may be left empty (turret key "none")
        public Vector2 pos;                  // where the mount sits on the Knight (cells; x right, y forward when the Knight faces north)
        public Vector2 uiPos = new Vector2(-1f, -1f);   // where the slot box sits on the flat hangar picture (fractions 0..1 of the picture; x<0 = not set, use pos)
        public List<WeaponOption> options = new List<WeaponOption>();
    }

    /// <summary>A head variant: a forged item, drawn as an overlay on the Knight (see CompKnightHead).</summary>
    public class KnightHead
    {
        public string id;
        public string label;
        public ThingDef item;
        public GraphicDataOverlay overlay;
    }

    /// <summary>A body-mounted backpack (ammo box / fuel canisters + their feed line) belonging to one weapon on one arm. Drawn as an overlay so it
    /// stays on the body while the turret swings (see CompKnightHead.Apply).</summary>
    public class KnightBackpack
    {
        public string weapon;               // weapon id without the LIK_Weapon_ prefix, e.g. cerastus_acheronflame
        public string side;                 // L or R (the arm it is fitted to)
        public string mount;                // optional: any mount key (armL, top, side ...) - used by the depth "occluder" overlays (body pixels that hide the weapon)
        public GraphicDataOverlay overlay;
    }

    /// <summary>One buildable Knight class: its vehicle, the four structural parts, a weapon list per mount and its liveries.</summary>
    public class KnightClassDef : Def
    {
        public VehicleDef vehicle;
        public ResearchProjectDef research;
        public int assembleWork = 20000;
        public int constructionSkill = 8;
        public float drawCells = 8f;            // the body picture covers this many cells
        public Vector2 headPos;              // head position (cells from the centre, north view)
        public ThingDef lifeSupport;         // optional life-support module item
        public ThingDef voidShield;          // optional void shield generator item (carapace option)
        public GraphicDataOverlay pauldron;  // optional overlay with only the shoulder plates, drawn above the turrets
        public GraphicDataOverlay hull;      // optional overlay: the body with the legs cut out, drawn just above the arm weapons (they pass under the hull, in front of the legs)
        public string hangarTex;             // optional flat (0 degree) hangar picture, drawn by the user (texture path without extension)
        public float voidShieldCapacity = 600f; // damage the shield absorbs before it collapses
        public List<ThingDef> parts = new List<ThingDef>();
        public List<KnightMount> mounts = new List<KnightMount>();
        public List<RetextureDef> skins = new List<RetextureDef>();
        public List<KnightHead> heads = new List<KnightHead>();
        public List<KnightBackpack> backpacks = new List<KnightBackpack>();

        public bool Unlocked { get { return research == null || research.IsFinished; } }
    }

    [DefOf]
    public static class HangarDefOf
    {
        public static ThingDef LIK_KnightHangar;          // the HEAVY hangar (the original def name)
        public static JobDef LIK_AssembleKnight;
        public static JobDef LIK_StudySTC;
        public static ThingDef LIK_MechanicumManufactorum;
        static HangarDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(HangarDefOf)); }
    }

    /// <summary>Which Knight footprints a hangar can build (one hangar for all classes since 2026-11-21: 0..99).</summary>
    public class DefModExtension_HangarSize : DefModExtension
    {
        public int minFootprint = 0;
        public int maxFootprint = 99;
    }

    // ------------------------------------------------------------------ the order
    public class HangarOrder : IExposable
    {
        public KnightClassDef cls;
        public List<string> turretKeys = new List<string>();     // one per mount, in the class's mount order
        public RetextureDef skin;                                // null = the standard livery
        public int headIndex;                                    // index into the class's heads
        public bool lifeSupport;                                 // optional life-support module fitted
        public bool voidShield;                                  // optional void shield generator fitted
        public bool paint;                                       // a paint scheme was chosen (paintable classes)
        public Color paintOne = Color.white, paintTwo = Color.white, paintThree = Color.white;
        public List<KnightSticker> stickers = new List<KnightSticker>();                // stickers chosen with the paint scheme (Stickers.cs)
        public float work;

        /// <summary>The items to deliver, in the slot order of the hangar's delivery row: four parts, the head, then one weapon per mount.</summary>
        public List<ThingDef> RequiredItems
        {
            get
            {
                var list = new List<ThingDef>(cls.parts);
                if (cls.heads.Count > 0) list.Add(cls.heads[Mathf.Clamp(headIndex, 0, cls.heads.Count - 1)].item);
                if (lifeSupport && cls.lifeSupport != null) list.Add(cls.lifeSupport);
                if (voidShield && cls.voidShield != null) list.Add(cls.voidShield);
                for (int i = 0; i < cls.mounts.Count; i++)
                {
                    if (cls.mounts[i].optional && i < turretKeys.Count && turretKeys[i] == KnightMount.None) continue;
                    WeaponOption opt = cls.mounts[i].options.FirstOrDefault(o => i < turretKeys.Count && o.turretKey == turretKeys[i]) ?? cls.mounts[i].options[0];
                    list.Add(opt.weapon);
                }
                return list;
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref cls, "cls");
            Scribe_Collections.Look(ref turretKeys, "turretKeys", LookMode.Value);
            Scribe_Defs.Look(ref skin, "skin");
            Scribe_Values.Look(ref work, "work", 0f);
            Scribe_Values.Look(ref headIndex, "headIndex", 0);
            Scribe_Values.Look(ref lifeSupport, "lifeSupport", false);
            Scribe_Values.Look(ref voidShield, "voidShield", false);
            Scribe_Values.Look(ref paint, "paint", false);
            Scribe_Values.Look(ref paintOne, "paintOne", Color.white);
            Scribe_Collections.Look(ref stickers, "stickers", LookMode.Deep);
            if (stickers == null) stickers = new List<KnightSticker>();
            Scribe_Values.Look(ref paintTwo, "paintTwo", Color.white);
            Scribe_Values.Look(ref paintThree, "paintThree", Color.white);
            if (turretKeys == null) turretKeys = new List<string>();
        }
    }

    // ------------------------------------------------------------------ weapon loadout + skin
    public static class KnightLoadout
    {
        public static void SetHead(VehiclePawn v, KnightClassDef cls, int index)
        {
            CompKnightHead comp = v?.GetComp<CompKnightHead>();
            if (comp == null || cls == null || cls.heads.Count == 0) return;
            comp.headId = cls.heads[Mathf.Clamp(index, 0, cls.heads.Count - 1)].id;
        }

        public static KnightClassDef ClassFor(VehicleDef def)
        {
            if (def == null) return null;
            foreach (KnightClassDef c in DefDatabase<KnightClassDef>.AllDefsListForReading)
                if (c.vehicle == def) return c;
            return null;
        }

        /// <summary>
        /// Every candidate weapon is a reference turret in the vehicle def, and Vehicle Framework creates them all.
        /// Keep the chosen ones (adding any that is missing) and remove the rest. keys == null means the defaults (first option per mount).
        /// </summary>
        public static void Apply(VehiclePawn v, KnightClassDef cls, IList<string> keys)
        {
            CompVehicleTurrets comp = v?.CompVehicleTurrets;
            if (comp == null || cls == null) return;
            var keep = new HashSet<string>();
            for (int i = 0; i < cls.mounts.Count; i++)
            {
                KnightMount m = cls.mounts[i];
                string want = keys != null && i < keys.Count ? keys[i] : null;
                if (m.optional && want == KnightMount.None) continue;
                keep.Add(m.options.Any(o => o.turretKey == want) ? want : m.options[0].turretKey);
            }
            // alternate-mode turrets are never fitted at assembly (the weapon starts in its normal mode)
            foreach (KnightMount m in cls.mounts)
                foreach (WeaponOption o in m.options)
                {
                    if (!keep.Contains(o.turretKey)) comp.RemoveTurret(o.turretKey);
                    if (o.altTurretKeys != null) foreach (string x in o.altTurretKeys) comp.RemoveTurret(x);
                }
            foreach (string k in keep)
            {
                if (comp.GetTurret(k) != null) continue;
                VehicleTurret reference = comp.Props.turrets.FirstOrDefault(t => t.key == k);
                if (reference != null) comp.CopyAndAddTurret(reference);
                else Log.Warning("[LIK] no reference turret " + k + " on " + v.def.defName);
            }
            if (v.Spawned) v.GetComp<CompKnightHead>()?.Apply();      // refresh weapon backpack overlays
            Patch_RocketToggleGizmo.Refresh(v);                        // a raised Acastus launcher shows the launcher fitted now
        }
    }

    /// <summary>The chosen head is remembered on the Knight and drawn as an overlay (re-added every time the Knight spawns or a save loads).</summary>
    public class CompProperties_KnightHead : CompProperties
    {
        public CompProperties_KnightHead() { compClass = typeof(CompKnightHead); }
    }

    public class CompKnightHead : ThingComp
    {
        public string headId;
        public bool lifeSupport;            // a life-support module is fitted (see TollUtil.LifeSupportFactors)
        public bool protectHome = true;     // user 2026-11-25: this Knight's weapons spare the colony's walls / buildings on home maps (HomeProtection.cs)
        public bool voidShield;             // a void shield generator is fitted
        public float shieldCharge = -1f;    // remaining shield (<0 = not yet initialised = full)
        public float ionCharge = -1f;       // ion gauntlet shield energy (<0 = full); IonShield.cs
        public int ionDownUntil, lastIonHit;
        public int ionMode;                 // user 2026-11-25: torso ion shield direction - 0 auto, 1 front, 2 right, 3 rear, 4 left (IonShieldTorso.cs); saved
        public float ionAutoAngle = -1f;    // where auto mode points it now (not saved)
        public float torsoCharge = -1f;     // torso ion shield energy (<0 = full); saved
        public int torsoDownUntil, lastTorsoHit;   // collapsed until / last hit (ticks); saved
        int lastShieldHit = -99999;
        public int nextStepTick;            // footsteps (KnightSound.StepTick); not saved
        public int lastHornTick = -99999;   // war horn (KnightSound); not saved
        public bool groundAttack;           // the player allowed this Knight's weapons to be aimed at bare ground (GroundAttack.cs); saved
        public bool rocketsOpen;            // Acastus: the top rocket launcher is raised (open body picture)
        public RetextureDef skinBeforeRockets; // livery to restore when the launcher is stowed
        public bool paintInit;              // paintable Knight: its colours were set up (KnightPaint.EnsureInitial)
        public List<KnightSticker> stickers = new List<KnightSticker>(); // stickers (Stickers.cs)
        public int stickerVersion;          // bumped when the stickers change (drawing cache); not saved

        public float ShieldMax
        {
            get { KnightClassDef c = KnightLoadout.ClassFor((parent as VehiclePawn)?.VehicleDef); return c != null ? c.voidShieldCapacity : 600f; }
        }

        /// <summary>Soaks up damage with the void shield; returns the amount absorbed.</summary>
        public float Absorb(float amount)
        {
            if (!voidShield || amount <= 0f) return 0f;
            if (shieldCharge < 0f) shieldCharge = ShieldMax;
            lastShieldHit = Find.TickManager.TicksGame;
            float soaked = Mathf.Min(amount, shieldCharge);
            if (soaked <= 0f) return 0f;
            shieldCharge -= soaked;
            if (shieldCharge <= 0.01f && parent.Spawned)
                Messages.Message("The void shield of the " + parent.LabelShort + " collapses!", parent, MessageTypeDefOf.NegativeEvent, false);
            return soaked;
        }

        public override void CompTick()
        {
            base.CompTick();
            KnightSound.StepTick(this);
            if (parent.IsHashIntervalTick(30) && parent is VehiclePawn iv && iv.Spawned && IonShield.Fitted(iv)) IonShield.Tick(iv, this);
            if (parent.IsHashIntervalTick(60) && parent is VehiclePawn tv && tv.Spawned) IonShieldTorso.AutoAim(tv, this);
            if (parent.IsHashIntervalTick(30) && parent is VehiclePawn sv && sv.Spawned) IonShieldTorso.Tick(sv, this);
            if (!voidShield || !parent.IsHashIntervalTick(30)) return;
            if (shieldCharge < 0f) shieldCharge = ShieldMax;
            float max = ShieldMax;
            if (shieldCharge >= max || Find.TickManager.TicksGame - lastShieldHit < 600) return;
            shieldCharge = Mathf.Min(max, shieldCharge + max / 50f);            // full recharge about 25 s after the last hit
        }

        public override string CompInspectStringExtra()
        {
            string s = voidShield ? "Void shield: " + Mathf.RoundToInt(shieldCharge < 0f ? ShieldMax : shieldCharge) + " / " + Mathf.RoundToInt(ShieldMax) : null;
            // graviton singularity cannon heat (Gravity.cs Overload)
            if (parent is VehiclePawn hv && hv.CompVehicleTurrets != null)
                foreach (VehicleTurret t in hv.CompVehicleTurrets.Turrets)
                    if (t.def != null && t.def.defName == Overload.TurretDef)
                    {
                        string h = Overload.Jammed(t) ? "Singularity cannon: OVERLOADED - venting" : "Singularity cannon heat: " + Mathf.RoundToInt(Mathf.Min(100f, Overload.HeatPct(t))) + "%";
                        s = s == null ? h : s + "\n" + h;
                    }
            return s;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref headId, "headId");
            Scribe_Values.Look(ref lifeSupport, "lifeSupport", false);
            Scribe_Values.Look(ref voidShield, "voidShield", false);
            Scribe_Values.Look(ref shieldCharge, "shieldCharge", -1f);
            Scribe_Values.Look(ref ionCharge, "ionCharge", -1f);
            Scribe_Values.Look(ref ionDownUntil, "ionDownUntil", 0);
            Scribe_Values.Look(ref rocketsOpen, "rocketsOpen", false);
            Scribe_Values.Look(ref groundAttack, "groundAttack", false);
            Scribe_Values.Look(ref protectHome, "protectHome", true);
            Scribe_Values.Look(ref ionMode, "ionMode", 0);
            Scribe_Values.Look(ref torsoCharge, "torsoCharge", -1f);
            Scribe_Values.Look(ref torsoDownUntil, "torsoDownUntil", 0);
            Scribe_Values.Look(ref lastTorsoHit, "lastTorsoHit", 0);
            Scribe_Defs.Look(ref skinBeforeRockets, "skinBeforeRockets");
            Scribe_Values.Look(ref paintInit, "paintInit", false);
            Scribe_Collections.Look(ref stickers, "stickers", LookMode.Deep);
            if (stickers == null) stickers = new List<KnightSticker>();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // Saves spawn things on a loader thread; GraphicOverlay must be created on the main thread.
            LongEventHandler.ExecuteWhenFinished(delegate { KnightPaint.EnsureInitial(parent as VehiclePawn); Apply(); });
        }

        public void Apply() { Apply(false); }

        /// <summary>Is this weapon's alternate mode fitted on the mount right now? (b.weapon is "cerastus_chainfist" etc., turret keys "armL_" + weapon.)</summary>
        static bool AltFitted(KnightClassDef cls, CompVehicleTurrets tc, string mountKey, string weapon)
        {
            string key = mountKey + "_" + weapon;
            foreach (KnightMount m in cls.mounts)
                foreach (WeaponOption o in m.options)
                    if (o.turretKey == key && o.altTurretKeys != null)
                        foreach (string a in o.altTurretKeys)
                            if (tc.GetTurret(a) != null) return true;
            return false;
        }

        /// <summary>evenUnspawned: also for a stowed / falling Knight (orbital drop draws it; the overlays are not saved, 2026-11-12).</summary>
        public void Apply(bool evenUnspawned)
        {
            VehiclePawn v = parent as VehiclePawn;
            if (v == null || (!v.Spawned && !evenUnspawned)) return;
            KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
            if (cls == null) return;
            KnightHead head = headId == null ? null : cls.heads.FirstOrDefault(h => h.id == headId);
            try
            {
                var renderer = v.DrawTracker?.overlayRenderer;
                if (renderer == null) return;
                KnightPaint.ApplyOverlays(v, cls, renderer);          // shoulder plates (Paint.cs)
                if (cls.hull != null)
                {
                    renderer.RemoveOverlays("LIK_Hull");
                    renderer.AddOverlay("LIK_Hull", GraphicOverlay.Create(cls.hull, v));
                }
                // weapon backpacks: shown only while that weapon is fitted on that arm
                CompVehicleTurrets tc = v.CompVehicleTurrets;
                foreach (KnightBackpack b in cls.backpacks)
                {
                    string mountKey = b.mount ?? ("arm" + b.side);
                    string id = (b.mount != null ? "LIK_Occluder_" : "LIK_Backpack_") + b.weapon + "_" + mountKey;
                    renderer.RemoveOverlays(id);
                    if (AlignTool.TintOn && b.mount != null && b.overlay != null && b.overlay.graphicData != null) b.overlay.graphicData.color = AlignTool.CoverTint;
                    // fitted in either mode: the weapon itself or its alternate mode (chainfist bolter, shock lance blast) - same arm picture,
                    // so the same occluders / backpacks (2026-11-15: they went missing after a reload in the alternate mode)
                    if (tc != null && b.overlay != null && !AlignTool.IsHidden(v, mountKey) && (tc.GetTurret(mountKey + "_" + b.weapon) != null || AltFitted(cls, tc, mountKey, b.weapon)))
                        renderer.AddOverlay(id, GraphicOverlay.Create(b.overlay, v));
                }
                if (head?.overlay == null) return;
                renderer.RemoveOverlays("LIK_Head");
                renderer.AddOverlay("LIK_Head", GraphicOverlay.Create(head.overlay, v));
            }
            catch (System.Exception e)
            {
                Log.Warning("[LIK] could not draw the " + headId + " head on " + v.LabelShort + ": " + e.Message);
            }
        }
    }

    /// <summary>Knights spawned any other way (dev menu, etc.) get the default weapon on every mount instead of all candidates.</summary>
    [HarmonyPatch(typeof(VehicleSpawner), nameof(VehicleSpawner.GenerateVehicle), new[] { typeof(VehicleGenerationRequest) })]
    public static class Patch_KnightDefaultLoadout
    {
        static void Postfix(VehiclePawn __result)
        {
            if (__result == null) return;
            KnightClassDef cls = KnightLoadout.ClassFor(__result.VehicleDef);
            if (cls != null)
            {
                KnightLoadout.Apply(__result, cls, null);
                KnightLoadout.SetHead(__result, cls, 0);
            }
        }
    }

    // ------------------------------------------------------------------ the hangar building
    public partial class Building_KnightHangar : Building, IThingHolder     // stowing + orbital drop: OrbitalDrop.cs
    {
        public HangarOrder order;

        /// <summary>Delivery row: the bottom row of the 9x9 floor, one slot per required item.</summary>
        int HalfX { get { return (def.size.x - 1) / 2; } }
        int HalfZ { get { return (def.size.z - 1) / 2; } }

        public IntVec3 SlotCell(int i)
        {
            int w = def.size.x;                                           // delivery area: bottom two rows
            return new IntVec3(Position.x - HalfX + i % w, Position.y, Position.z - HalfZ + i / w);
        }

        /// <summary>Footprint check against the hangar's size extension (the single Knight hangar accepts every class).</summary>
        public bool Supports(KnightClassDef c)
        {
            var e = def.GetModExtension<DefModExtension_HangarSize>();
            if (e == null || c?.vehicle == null) return true;
            int f = Mathf.Max(c.vehicle.size.x, c.vehicle.size.z);
            return f >= e.minFootprint && f <= e.maxFootprint;
        }

        public Thing ItemAt(int slot)
        {
            if (order == null) return null;
            List<ThingDef> req = order.RequiredItems;
            if (slot >= req.Count || !Spawned) return null;
            foreach (Thing t in SlotCell(slot).GetThingList(Map))
                if (t.def == req[slot]) return t;
            return null;
        }

        public bool IsSlotCell(IntVec3 c)
        {
            return (c.z == Position.z - HalfZ || c.z == Position.z - HalfZ + 1) && c.x >= Position.x - HalfX && c.x <= Position.x + HalfX;
        }

        /// <summary>Every item is either already on its slot or lying somewhere reachable.</summary>
        public bool AllItemsAvailable(Pawn pawn)
        {
            if (order == null) return false;
            List<ThingDef> req = order.RequiredItems;
            var used = new HashSet<Thing>();
            for (int i = 0; i < req.Count; i++)
            {
                if (ItemAt(i) != null) continue;
                Thing loose = FindLoose(pawn, req[i], used);
                if (loose == null) return false;
                used.Add(loose);
            }
            return true;
        }

        public Thing FindLoose(Pawn pawn, ThingDef def, HashSet<Thing> exclude = null)
        {
            Thing best = null;
            float bd = float.MaxValue;
            foreach (Thing t in Map.listerThings.ThingsOfDef(def))
            {
                if (!t.Spawned || t.IsForbidden(pawn) || IsSlotCell(t.Position) || (exclude != null && exclude.Contains(t))) continue;
                if (!pawn.CanReach(t, PathEndMode.Touch, Danger.Some) || !pawn.CanReserve(t)) continue;
                float d = (t.Position - pawn.Position).LengthHorizontalSquared;
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        public bool Complete(Pawn doer)
        {
            if (order == null || !Spawned) return false;
            if (FindDockedKnight() != null || StowedKnight != null) return false;   // the bay is occupied by a docked or stowed Knight
            if (KnightSpotBlocked(order.cls.vehicle) != null) return false;          // never wipe a wall behind the hangar
            List<ThingDef> req = order.RequiredItems;
            var items = new List<Thing>();
            for (int i = 0; i < req.Count; i++)
            {
                Thing t = ItemAt(i);
                if (t == null) return false;
                items.Add(t);
            }
            KnightClassDef cls = order.cls;
            VehiclePawn knight = VehicleSpawner.GenerateVehicle(cls.vehicle, doer?.Faction ?? Faction.OfPlayer);
            if (knight == null) return false;
            KnightLoadout.Apply(knight, cls, order.turretKeys);
            if (order.skin != null) knight.SetRetexture(order.skin);
            KnightLoadout.SetHead(knight, cls, order.headIndex);
            CompKnightHead kc = knight.GetComp<CompKnightHead>();
            if (kc != null) { kc.lifeSupport = order.lifeSupport && cls.lifeSupport != null; kc.voidShield = order.voidShield && cls.voidShield != null; kc.shieldCharge = -1f; }
            foreach (Thing t in items) t.Destroy();
            GenSpawn.Spawn(knight, KnightCell(knight.VehicleDef), Map, Rot4.South, WipeMode.VanishOrMoveAside, false);   // feet on the drawn bay (true 35-degree art)
            knight.GetComp<CompKnightHead>()?.Apply();          // after the skin, which can reset the drawing
            if (KnightPaint.Paintable(knight.VehicleDef))
            {
                if (order.paint) KnightPaint.Apply(knight, new PaintChoice { one = order.paintOne, two = order.paintTwo, three = order.paintThree, stickers = (order.stickers ?? new List<KnightSticker>()).Select(x => x.Copy()).ToList() });
                else KnightPaint.EnsureInitial(knight);
            }
            Messages.Message("The " + cls.label + " is assembled and stands in the hangar, ready for a pilot and promethium.", knight, MessageTypeDefOf.PositiveEvent);
            order = null;
            return true;
        }

        /// <summary>Which weapon (turret key) sits on each mount of an existing Knight.</summary>
        public static List<string> CurrentKeys(VehiclePawn v, KnightClassDef cls)
        {
            var keys = new List<string>();
            CompVehicleTurrets comp = v.CompVehicleTurrets;
            foreach (KnightMount m in cls.mounts)
            {
                WeaponOption cur = comp == null ? null : m.options.FirstOrDefault(o => comp.GetTurret(o.turretKey) != null || (o.altTurretKeys != null && o.altTurretKeys.Any(a => comp.GetTurret(a) != null)));
                keys.Add(cur != null ? cur.turretKey : (m.optional ? KnightMount.None : m.options[0].turretKey));
            }
            return keys;
        }

        public static int CurrentHeadIndex(VehiclePawn v, KnightClassDef cls)
        {
            string id = v.GetComp<CompKnightHead>()?.headId;
            return Mathf.Max(0, cls.heads.FindIndex(h => h.id == id));
        }

        /// <summary>Pieces a refit needs: the new weapon of every changed mount and a new head if it changed.</summary>
        public static List<ThingDef> RefitNeeds(VehiclePawn v, KnightClassDef cls, IList<string> keys, int headIdx, bool ls, bool vs, List<ThingDef> returned)
        {
            var need = new List<ThingDef>();
            List<string> cur = CurrentKeys(v, cls);
            for (int i = 0; i < cls.mounts.Count; i++)
            {
                string want = i < keys.Count ? keys[i] : cur[i];
                if (want == cur[i]) continue;
                WeaponOption o = cls.mounts[i].options.FirstOrDefault(x => x.turretKey == want);
                WeaponOption old = cls.mounts[i].options.FirstOrDefault(x => x.turretKey == cur[i]);
                if (o != null) need.Add(o.weapon);
                if (old != null && returned != null) returned.Add(old.weapon);
            }
            if (cls.heads.Count > 0 && headIdx != CurrentHeadIndex(v, cls))
            {
                need.Add(cls.heads[headIdx].item);
                if (returned != null) returned.Add(cls.heads[CurrentHeadIndex(v, cls)].item);
            }
            bool hasLs = v.GetComp<CompKnightHead>()?.lifeSupport ?? false;
            if (cls.lifeSupport != null && ls != hasLs)
            {
                if (ls) need.Add(cls.lifeSupport);
                else if (returned != null) returned.Add(cls.lifeSupport);
            }
            bool hasVs = v.GetComp<CompKnightHead>()?.voidShield ?? false;
            if (cls.voidShield != null && vs != hasVs)
            {
                if (vs) need.Add(cls.voidShield);
                else if (returned != null) returned.Add(cls.voidShield);
            }
            return need;
        }

        /// <summary>
        /// DEV (user 2026-11-11): change a Knight's weapons / modules on the spot, anywhere on the map - no hangar, no pieces taken from stock,
        /// nothing returned, and the new weapons arrive with full magazines. Used by the dev refit window (Window_ConfigureKnight.devFree).
        /// </summary>
        public static void DevRefit(VehiclePawn v, KnightClassDef cls, IList<string> keys, int headIdx, bool ls, bool vs)
        {
            if (v == null || cls == null || !v.Spawned) return;
            KnightLoadout.Apply(v, cls, keys);
            CompKnightHead lc = v.GetComp<CompKnightHead>();
            if (cls.heads.Count > 0) { KnightLoadout.SetHead(v, cls, headIdx); if (lc != null) lc.Apply(); }
            if (lc != null && cls.lifeSupport != null) lc.lifeSupport = ls;
            if (lc != null && cls.voidShield != null) { if (lc.voidShield != vs) lc.shieldCharge = -1f; lc.voidShield = vs; }
            CompVehicleTurrets ct = v.CompVehicleTurrets;
            if (ct != null && ct.Turrets != null)
                foreach (VehicleTurret t in ct.Turrets)
                {
                    if (t.def.magazineCapacity <= 0 || t.shellCount >= t.def.magazineCapacity) continue;
                    if (t.loadedAmmo == null && t.def.ammunition != null) t.loadedAmmo = t.def.ammunition.AllowedThingDefs.FirstOrDefault();
                    if (t.savedAmmoType == null) t.savedAmmoType = t.loadedAmmo;
                    t.SetMagazineCount(t.def.magazineCapacity);
                }
        }

        public bool Refit(VehiclePawn v, KnightClassDef cls, IList<string> keys, int headIdx, bool ls, bool vs)
        {
            if (!Spawned || ServicedKnight != v) { Messages.Message("The Knight is no longer docked here.", this, MessageTypeDefOf.RejectInput, false); return false; }
            var returned = new List<ThingDef>();
            List<ThingDef> need = RefitNeeds(v, cls, keys, headIdx, ls, vs, returned);
            if (need.Count == 0) { Messages.Message("Nothing to change.", this, MessageTypeDefOf.RejectInput, false); return false; }
            var taken = new List<Thing>();
            foreach (ThingDef d in need)
            {
                Thing found = Map.listerThings.ThingsOfDef(d).FirstOrDefault(t => t.Spawned && !taken.Contains(t));
                if (found == null) { Messages.Message("Missing " + d.label + " in stock.", this, MessageTypeDefOf.RejectInput, false); return false; }
                taken.Add(found);
            }
            foreach (Thing t in taken) t.SplitOff(1).Destroy();
            KnightLoadout.Apply(v, cls, keys);
            if (!v.Spawned) OrbitalDropDraw.ForgetTurrets(v);       // docked (clamped) Knight: redraw with the new weapons
            if (cls.heads.Count > 0) { KnightLoadout.SetHead(v, cls, headIdx); v.GetComp<CompKnightHead>()?.Apply(); }
            CompKnightHead lc = v.GetComp<CompKnightHead>();
            if (lc != null && cls.lifeSupport != null) lc.lifeSupport = ls;
            if (lc != null && cls.voidShield != null) { if (lc.voidShield != vs) lc.shieldCharge = -1f; lc.voidShield = vs; }
            foreach (ThingDef d in returned)
            {
                Thing item = ThingMaker.MakeThing(d);
                GenPlace.TryPlaceThing(item, SlotCell(0), Map, ThingPlaceMode.Near);
            }
            serviceNotified = false;
            Messages.Message(v.LabelShort + " refitted. New weapons are loaded from the dock's steel supply.", v.Spawned ? new LookTargets(v) : new LookTargets(this), MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        void CancelOrder()
        {
            if (order == null) return;
            foreach (Thing t in Enumerable.Range(0, order.RequiredItems.Count).Select(ItemAt).Where(t => t != null).ToList())
            {
                var cf = t.TryGetComp<CompForbiddable>();
                if (cf != null) cf.Forbidden = false;         // delivered parts go back to the colony
            }
            order = null;
        }

        // ---------------------------------------------------------------- docking: repair + rearm
        const float RepairFractionPerRare = 0.012f;       // of each damaged component's max health, every 250 ticks (about 12% per hour)
        const float SteelPerRepairedHealth = 0.1f;
        VehiclePawn docked;
        bool serviceNotified;
        int lastShortageTick = -99999;

        /// <summary>A Knight of a class this hangar fits, standing on (or within a cell of) its bay spot. Not "fully inside the footprint" any more:
        /// since the 2026-11-22 shrink (20x11, platform only) a big Knight's rect reaches into the gantry overhang behind the hangar.</summary>
        public VehiclePawn FindDockedKnight()
        {
            if (!Spawned) return null;
            CellRect rect = this.OccupiedRect();
            foreach (Thing t in Map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn))
            {
                VehiclePawn v = t as VehiclePawn;
                if (v == null || v.Faction != Faction.OfPlayer) continue;
                KnightClassDef cls = KnightLoadout.ClassFor(v.VehicleDef);
                if (cls == null || !Supports(cls)) continue;
                IntVec3 spot = KnightCell(v.VehicleDef);
                if (System.Math.Abs(v.Position.x - spot.x) <= 1 && System.Math.Abs(v.Position.z - spot.z) <= 1) return v;
                CellRect kr = v.OccupiedRect();
                if (kr.minX >= rect.minX && kr.maxX <= rect.maxX && kr.minZ >= rect.minZ && kr.maxZ <= rect.maxZ) return v;
            }
            return null;
        }

        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned) return;
            VehiclePawn k = FindDockedKnight();
            if (k != docked) { docked = k; serviceNotified = false; }
            CheckDockArrival();                                   // docking = stowing (2026-11-22)
            EndStaleLaunch();                                     // a launch also ends when nobody is watching the ship (2026-11-23)
            VehiclePawn sk = StowedKnight;
            if (sk != null) { if (sk != docked) { docked = sk; serviceNotified = false; } ServiceKnight(sk); }
            else if (docked != null) { ServiceKnight(docked); SettleOnBay(docked); }
        }

        void ServiceKnight(VehiclePawn v)
        {
            bool changed = false, shortage = false, anyLeft = false;
            ThingDef lastShortOf = null;      // what the dock ran out of (steel for repairs and guns, chemfuel for flame cannons)
            if (v.statHandler != null && v.statHandler.components != null)
                foreach (VehicleComponent c in v.statHandler.components)
                {
                    if (c.HealthPercent >= 1f) continue;
                    float heal = Mathf.Min(c.MaxHealth - c.Health, c.MaxHealth * RepairFractionPerRare);
                    if (heal <= 0f) continue;
                    if (!DockUtil.TakeSteel(Map, Mathf.CeilToInt(heal * SteelPerRepairedHealth))) { shortage = true; anyLeft = true; break; }
                    c.HealComponent(heal);
                    changed = true;
                    if (c.HealthPercent < 1f) anyLeft = true;
                }
            CompVehicleTurrets turrets = v.CompVehicleTurrets;
            if (turrets != null)

                foreach (VehicleTurret t in turrets.Turrets)
                {
                    if (t.def.ammunition == null) continue;                 // energy weapons: reactor powered, nothing to load
                    int cap = t.def.magazineCapacity;
                    int missing = cap - t.shellCount;
                    if (missing <= 0) continue;
                    int cost = cap >= 500 ? 0 : Mathf.CeilToInt(missing * t.def.chargePerAmmoCount);       // melee-style weapons have no real magazine
                    // each weapon is refilled with what it fires: steel for guns and missiles, chemfuel for the flame cannons
                    ThingDef ammo = t.def.ammunition.AllowedThingDefs.FirstOrDefault() ?? ThingDefOf.Steel;
                    if (cost > 0 && !DockUtil.Take(Map, ammo, cost)) { shortage = true; anyLeft = true; lastShortOf = ammo; continue; }
                    if (t.loadedAmmo == null && t.def.ammunition != null) t.loadedAmmo = t.def.ammunition.AllowedThingDefs.FirstOrDefault();
                    if (t.savedAmmoType == null) t.savedAmmoType = t.loadedAmmo;
                    t.SetMagazineCount(cap);
                    changed = true;
                }
            if (shortage && Find.TickManager.TicksGame - lastShortageTick > 5000)
            {
                lastShortageTick = Find.TickManager.TicksGame;
                Messages.Message("The dock cannot finish servicing " + v.LabelShort + ": it needs " + (lastShortOf != null ? lastShortOf.label : "steel") + ".", v.Spawned ? new LookTargets(v) : new LookTargets(this), MessageTypeDefOf.RejectInput, false);
            }
            if (!anyLeft && !shortage && !serviceNotified && changed)
            {
                serviceNotified = true;
                Messages.Message(v.LabelShort + " is fully repaired and rearmed.", v.Spawned ? new LookTargets(v) : new LookTargets(this), MessageTypeDefOf.PositiveEvent, false);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            foreach (Gizmo g in PilotTraining.HangarGizmos(this)) yield return g;
            if (Prefs.DevMode)                           // dev only (user 2026-11-21): the doors belong to the orbital drop; this is for testing
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Cycle bay doors",
                    defaultDesc = "Dev mode only. Sound the warning beacons and open the blast doors over the drop shaft, then close them again (the orbital drop plays this).",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LIK_BayDoors", false) ?? BaseContent.BadTex,
                    action = PlayDoorCycle
                };
            yield return new Command_Action
            {
                defaultLabel = "Configure Knight assembly",
                defaultDesc = "Choose the Knight class, a weapon for every mount and a livery. Builders then haul the forged parts and weapons in and assemble it.",
                icon = GizmoIcons.Get("LIK_ConfigureAssembly", order?.cls?.vehicle, TexCommand.Attack),
                action = delegate { Find.WindowStack.Add(new Window_ConfigureKnight(this)); }
            };
            VehiclePawn dk = ServicedKnight;
            if (dk != null)
                yield return new Command_KnightAction
                {
                    knight = dk, iconName = "LIK_Refit",
                    defaultLabel = "Refit docked Knight",
                    defaultDesc = "Change the weapons and head of the Knight in this bay. New pieces are taken from stock on the map; the pieces taken off come back as items. " +
                                  "Repairs and reloading happen automatically while it is docked.",
                    icon = GizmoIcons.Get("LIK_Refit", dk.VehicleDef, TexCommand.Attack),
                    action = delegate { Find.WindowStack.Add(new Window_ConfigureKnight(this, dk)); }
                };
            VehiclePawn pk = dk ?? StowedKnight;
            if (pk != null && KnightPaint.Paintable(pk.VehicleDef))
            {
                KnightClassDef pcls = KnightLoadout.ClassFor(pk.VehicleDef);
                yield return new Command_KnightAction
                {
                    knight = pk, iconName = "LIK_Paint",
                    defaultLabel = "Paint Knight",
                    defaultDesc = "Repaint the Knight in this bay: a Knight-house scheme or your own armour and trim colours. Done at once, free of charge.",
                    icon = GizmoIcons.Get("LIK_Paint", pk.VehicleDef, TexCommand.Install),
                    action = delegate
                    {
                        Find.WindowStack.Add(new Window_PaintKnight(pcls, pk, KnightPaint.Current(pk), ch =>
                        {
                            KnightPaint.Apply(pk, ch);
                            Messages.Message(pk.LabelShort + " is repainted: " + KnightPaint.NameOf(pk.VehicleDef, ch) + ".", pk.Spawned ? new LookTargets(pk) : new LookTargets(this), MessageTypeDefOf.PositiveEvent, false);
                        }));
                    }
                };
            }
            foreach (Gizmo g in DropGizmos()) yield return g;
            if (order != null)
                yield return new Command_Action
                {
                    defaultLabel = "Cancel assembly",
                    defaultDesc = "Cancel the current order. Parts already delivered become available again; no progress is kept.",
                    icon = TexCommand.ClearPrioritizedWork,
                    action = CancelOrder
                };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string st = DropInspect();
            if (!st.NullOrEmpty()) s += (s.NullOrEmpty() ? "" : "\n") + st;
            VehiclePawn sv = StowedKnight;                       // docked (clamped): DropInspect names it, add the repair state
            if (sv != null)
            {
                float hp2 = 0f; int n2 = 0;
                if (sv.statHandler != null && sv.statHandler.components != null) foreach (VehicleComponent c in sv.statHandler.components) { hp2 += c.HealthPercent; n2++; }
                s += " - " + Mathf.RoundToInt(n2 == 0 ? 100f : 100f * hp2 / n2) + "% repaired, servicing uses steel";
            }
            VehiclePawn dk = sv == null ? FindDockedKnight() : null;
            if (dk != null)
            {
                float hp = 0f; int n = 0;
                if (dk.statHandler != null && dk.statHandler.components != null) foreach (VehicleComponent c in dk.statHandler.components) { hp += c.HealthPercent; n++; }
                s += (s.NullOrEmpty() ? "" : "\n") + "In the bay (not docked): " + dk.LabelShort + " (" + Mathf.RoundToInt(n == 0 ? 100f : 100f * hp / n) + "% repaired; servicing uses steel)";
            }
            if (order == null) return s + (s.NullOrEmpty() ? "" : "\n") + "No assembly ordered. Dock a Knight here to repair and rearm it.";
            List<ThingDef> req = order.RequiredItems;
            int have = Enumerable.Range(0, req.Count).Count(i => ItemAt(i) != null);
            return s + (s.NullOrEmpty() ? "" : "\n") + "Assembling: " + order.cls.LabelCap + " (" + have + "/" + req.Count + " parts delivered, " +
                   Mathf.RoundToInt(100f * order.work / Mathf.Max(1f, order.cls.assembleWork)) + "% assembled)";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref order, "order");
            ExposeDrop();
        }
    }

    public static class DockUtil
    {
        /// <summary>Consume steel from anywhere on the map (stockpiles included). False if there is not enough.</summary>
        public static bool TakeSteel(Map map, int amount) { return Take(map, ThingDefOf.Steel, amount); }

        /// <summary>Consume a resource from anywhere on the map. False (and nothing taken) if there is not enough.</summary>
        public static bool Take(Map map, ThingDef def, int amount)
        {
            if (amount <= 0) return true;
            int have = 0;
            foreach (Thing t in map.listerThings.ThingsOfDef(def)) have += t.stackCount;
            if (have < amount) return false;
            int left = amount;
            foreach (Thing t in map.listerThings.ThingsOfDef(def).ToList())
            {
                if (left <= 0) break;
                int take = Mathf.Min(left, t.stackCount);
                t.SplitOff(take).Destroy();
                left -= take;
            }
            return true;
        }
    }

    /// <summary>Knights are only repaired at a hangar dock: builders no longer repair them by hand.</summary>
    [HarmonyPatch(typeof(WorkGiver_RepairVehicle), nameof(WorkGiver_RepairVehicle.CanBeWorkedOn))]
    public static class Patch_NoManualKnightRepair
    {
        static void Postfix(VehiclePawn vehicle, ref bool __result)
        {
            if (__result && vehicle != null && KnightLoadout.ClassFor(vehicle.VehicleDef) != null) __result = false;
        }
    }

    // ------------------------------------------------------------------ the configuration window
    /// <summary>
    /// Mech-shaped assembly screen: class cards across the top, the selected Knight in the middle with a clickable slot at the
    /// position of every weapon mount (and the head), liveries as picture swatches, and the parts the order needs.
    /// </summary>
    public partial class Window_ConfigureKnight : Window
    {
        readonly Building_KnightHangar hangar;
        readonly List<KnightClassDef> classes;
        int classIdx;
        int pendingClass = -1;
        int[] choice;
        int skinIdx;                                   // 0 = standard livery
        PaintChoice paintChoice;                       // paintable classes: the scheme picked in the paint window (null = standard)
        int headIdx;
        bool lsOn;                                      // life-support module wanted
        bool vsOn;                                      // void shield generator wanted
        readonly VehiclePawn refitKnight;               // non-null = refitting a docked Knight instead of ordering a new one
        readonly bool devFree;                          // DEV refit: no hangar, no cost, every weapon offered, changes applied the moment they are picked
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        public override Vector2 InitialSize { get { return CogUI.Ready ? new Vector2(CogUI.TW, CogUI.TH) : new Vector2(1000f, 780f); } }
        protected override float Margin { get { return CogUI.Ready ? 0f : base.Margin; } }

        public Window_ConfigureKnight(Building_KnightHangar hangar)
        {
            this.hangar = hangar;
            doCloseX = true;
            closeOnClickedOutside = false;
            forcePause = false;
            classes = DefDatabase<KnightClassDef>.AllDefsListForReading.Where(c => c.Unlocked && hangar.Supports(c)).ToList();
            if (hangar.order != null && classes.Contains(hangar.order.cls))
            {
                classIdx = classes.IndexOf(hangar.order.cls);
                SetClass(classIdx, hangar.order);
            }
            else SetClass(0, null);
        }

        /// <summary>DEV refit of any Knight anywhere (opened from the Knight's own dev button): the hangar screen with everything free and instant.</summary>
        public Window_ConfigureKnight(VehiclePawn knight)
        {
            devFree = true;
            refitKnight = knight;
            doCloseX = true;
            closeOnClickedOutside = false;
            forcePause = false;
            draggable = true;
            KnightClassDef c = KnightLoadout.ClassFor(knight.VehicleDef);
            classes = new List<KnightClassDef> { c };
            classIdx = 0;
            choice = new int[c.mounts.Count];
            List<string> keys = Building_KnightHangar.CurrentKeys(knight, c);
            for (int i = 0; i < c.mounts.Count; i++)
                choice[i] = c.mounts[i].optional && keys[i] == KnightMount.None ? -1 : Mathf.Max(0, c.mounts[i].options.FindIndex(o => o.turretKey == keys[i]));
            headIdx = Building_KnightHangar.CurrentHeadIndex(knight, c);
            lsOn = knight.GetComp<CompKnightHead>()?.lifeSupport ?? false;
            vsOn = knight.GetComp<CompKnightHead>()?.voidShield ?? false;
            skinIdx = 0; paintChoice = null;
        }

        /// <summary>Refit mode: only the docked Knight's class, starting from its current weapons and head.</summary>
        public Window_ConfigureKnight(Building_KnightHangar hangar, VehiclePawn knight) : this(hangar)
        {
            refitKnight = knight;
            KnightClassDef c = KnightLoadout.ClassFor(knight.VehicleDef);
            classes.Clear();
            classes.Add(c);
            classIdx = 0;
            choice = new int[c.mounts.Count];
            List<string> keys = Building_KnightHangar.CurrentKeys(knight, c);
            for (int i = 0; i < c.mounts.Count; i++)
                choice[i] = c.mounts[i].optional && keys[i] == KnightMount.None ? -1 : Mathf.Max(0, c.mounts[i].options.FindIndex(o => o.turretKey == keys[i]));
            headIdx = Building_KnightHangar.CurrentHeadIndex(knight, c);
            lsOn = knight.GetComp<CompKnightHead>()?.lifeSupport ?? false;
            vsOn = knight.GetComp<CompKnightHead>()?.voidShield ?? false;
            skinIdx = 0; paintChoice = null;
        }

        void SetClass(int idx, HangarOrder from)
        {
            classIdx = idx;
            if (classes.Count == 0) { choice = new int[0]; return; }
            KnightClassDef c = classes[idx];
            choice = new int[c.mounts.Count];
            skinIdx = 0; paintChoice = null;
            headIdx = 0;
            lsOn = from != null && from.lifeSupport;
            vsOn = from != null && from.voidShield;
            if (from != null)
            {
                headIdx = Mathf.Clamp(from.headIndex, 0, Mathf.Max(0, c.heads.Count - 1));
                for (int i = 0; i < c.mounts.Count && i < from.turretKeys.Count; i++)
                    choice[i] = c.mounts[i].optional && from.turretKeys[i] == KnightMount.None ? -1 : Mathf.Max(0, c.mounts[i].options.FindIndex(o => o.turretKey == from.turretKeys[i]));
                if (from.skin != null) skinIdx = c.skins.IndexOf(from.skin) + 1;
            }
        }

        static Texture2D Tex(string path)
        {
            if (path == null) return null;
            Texture2D t;
            if (!texCache.TryGetValue(path, out t))
            {
                t = ContentFinder<Texture2D>.Get(path, false);
                texCache[path] = t;
            }
            return t;
        }

        /// <summary>The flat hangar picture of a class, or null when it has none (yet).</summary>
        static Texture2D HangarPicture(KnightClassDef c)
        {
            return string.IsNullOrEmpty(c.hangarTex) ? null : Tex(c.hangarTex);
        }

        static Texture2D BodyTex(KnightClassDef c, RetextureDef skin)
        {
            Texture2D flat = HangarPicture(c);
            if (flat != null) return flat;
            string path = skin != null && skin.graphicData != null ? skin.graphicData.texPath : c.vehicle.graphicData.texPath;
            // all Knight art is drawn at 35 degrees above the horizon (2026-11-06): without a flat hangar picture the hangar shows the front (south) picture
            return Tex(path + "_south");
        }

        int Stock(ThingDef def)
        {
            int n = 0;
            Map map = hangar != null ? hangar.Map : (refitKnight != null ? refitKnight.Map : null);
            if (map == null) return 0;
            foreach (Thing t in map.listerThings.ThingsOfDef(def)) n += t.stackCount;
            return n;
        }

        void DrawPicture(Rect r, Texture2D tex, string fallback)
        {
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            else
            {
                Widgets.DrawBox(r.ContractedBy(4f));
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, fallback);
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        /// <summary>A clickable slot box: picture of what is fitted, a small caption above and the name below.</summary>
        bool DrawSlot(Vector2 centre, float size, string caption, string name, Texture2D icon, string tip)
        {
            Rect box = new Rect(centre.x - size / 2f, centre.y - size / 2f, size, size);
            bool over = Mouse.IsOver(box);
            Widgets.DrawBoxSolid(box, over ? new Color(0.25f, 0.45f, 0.25f, 0.9f) : new Color(0.07f, 0.09f, 0.12f, 0.85f));
            GUI.color = over ? Color.white : new Color(0.6f, 0.85f, 1f);
            Widgets.DrawBox(box, 2);
            GUI.color = Color.white;
            if (icon != null) GUI.DrawTexture(box.ContractedBy(6f), icon, ScaleMode.ScaleToFit);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(centre.x - 60f, box.y - 17f, 120f, 17f), caption);
            string shown = name.Length > 22 ? name.Substring(0, 21) + "." : name;
            Widgets.Label(new Rect(centre.x - 70f, box.yMax, 140f, 17f), shown);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(box, tip);
            return Widgets.ButtonInvisible(box);
        }

        void OpenWeaponMenu(int mi)
        {
            KnightMount m = classes[classIdx].mounts[mi];
            var menu = new List<FloatMenuOption>();
            if (m.optional) menu.Add(new FloatMenuOption("No weapon (leave empty)", delegate { choice[mi] = -1; }));
            for (int j = 0; j < m.options.Count; j++)
            {
                int jj = j;
                WeaponOption o = m.options[j];
                if (!devFree && jj != choice[mi] && STCUtil.WeaponLocked(o.weapon) && Stock(o.weapon) == 0) continue;      // a lost pattern nobody has recovered
                menu.Add(new FloatMenuOption(devFree ? o.weapon.LabelCap.ToString() : o.weapon.LabelCap + "  (" + Stock(o.weapon) + " in stock)", delegate { choice[mi] = jj; }, o.weapon.uiIcon, Color.white));
            }
            Find.WindowStack.Add(new FloatMenu(menu));
        }

        void OpenHeadMenu()
        {
            KnightClassDef cls = classes[classIdx];
            var menu = new List<FloatMenuOption>();
            for (int j = 0; j < cls.heads.Count; j++)
            {
                int jj = j;
                KnightHead h = cls.heads[j];
                menu.Add(new FloatMenuOption(h.label.CapitalizeFirst() + "  (" + Stock(h.item) + " in stock)", delegate { headIdx = jj; }, h.item.uiIcon, Color.white));
            }
            Find.WindowStack.Add(new FloatMenu(menu));
        }

        /// <summary>The old plain screen - only used when the cogitator textures are missing (HangarCogitator.cs).</summary>
        void DrawClassic(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            if (devFree && (refitKnight == null || !refitKnight.Spawned)) { Close(); return; }
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), devFree ? "DEV refit (free, instant): " + refitKnight.LabelShort
                : refitKnight != null ? "Refit docked Knight: " + refitKnight.LabelShort : "Configure Knight assembly");
            Text.Font = GameFont.Small;
            if (classes.Count == 0)
            {
                Widgets.Label(new Rect(0f, 50f, inRect.width, 60f), "No Knight class this hangar can build is researched yet.");
                return;
            }
            if (pendingClass >= 0) { int pcl = pendingClass; pendingClass = -1; if (pcl < classes.Count) SetClass(pcl, null); }
            KnightClassDef cls = classes[classIdx];

            // ---- class cards (each Knight shown)
            float cx = 0f;
            for (int i = 0; i < classes.Count; i++)
            {
                Rect card = new Rect(cx, 40f, 150f, 112f);
                bool sel = i == classIdx;
                Widgets.DrawBoxSolid(card, sel ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : new Color(0.07f, 0.09f, 0.12f, 0.85f));
                GUI.color = sel ? Color.green : (Mouse.IsOver(card) ? Color.white : new Color(0.6f, 0.7f, 0.8f));
                Widgets.DrawBox(card, sel ? 3 : 1);
                GUI.color = Color.white;
                if (KnightPaint.Paintable(classes[i].vehicle)) KnightPaint.DrawClassPicture(new Rect(card.x + 33f, card.y + 4f, 84f, 84f), classes[i], KnightPaint.Defaults(classes[i].vehicle), "?");
                else DrawPicture(new Rect(card.x + 33f, card.y + 4f, 84f, 84f), BodyTex(classes[i], null), "?");
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(card.x, card.y + 88f, card.width, 22f), classes[i].label);
                Text.Anchor = TextAnchor.UpperLeft;
                if (refitKnight == null && Widgets.ButtonInvisible(card) && !sel) pendingClass = i;      // applied next frame: switching now would resize choice[] under this frame's stale cls
                cx += 156f;
            }

            // ---- the Knight, with its slots
            Rect view = new Rect(0f, 164f, 600f, inRect.height - 164f - 8f);
            Widgets.DrawBoxSolid(view, new Color(0.05f, 0.06f, 0.09f, 0.9f));
            GUI.color = new Color(0.35f, 0.45f, 0.6f);
            Widgets.DrawBox(view, 1);
            GUI.color = Color.white;
            float cell = Mathf.Min(view.width, view.height - 60f) / Mathf.Max(1f, cls.drawCells);
            Vector2 centre = new Vector2(view.center.x, view.center.y + 6f);
            Rect body = new Rect(centre.x - cls.drawCells * cell / 2f, centre.y - cls.drawCells * cell / 2f, cls.drawCells * cell, cls.drawCells * cell);
            bool flatPic = HangarPicture(cls) != null;
            if (flatPic)
            {
                float side = Mathf.Min(view.width - 8f, view.height - 60f);          // the flat picture is a square that fills the view
                body = new Rect(centre.x - side / 2f, centre.y - side / 2f, side, side);
                cell = side / Mathf.Max(1f, cls.drawCells);
            }
            Texture2D bodyTex = BodyTex(cls, skinIdx == 0 ? null : cls.skins[skinIdx - 1]);
            bool paintable = KnightPaint.Paintable(cls.vehicle);
            PaintChoice pc = null;
            if (paintable)
            {
                pc = refitKnight != null ? KnightPaint.Current(refitKnight) : paintChoice ?? KnightPaint.Defaults(cls.vehicle);
                KnightPaint.DrawClassPicture(body, cls, pc, cls.label + "\n(no picture yet)");
            }
            else DrawPicture(body, bodyTex, cls.label + "\n(no picture yet)");

            float boxSize = Mathf.Clamp(cell * 0.9f, 48f, 68f);
            for (int i = 0; i < cls.mounts.Count; i++)
            {
                KnightMount m = cls.mounts[i];
                WeaponOption cur = choice[i] < 0 ? null : m.options[choice[i]];
                Vector2 p = flatPic && m.uiPos.x >= 0f
                    ? new Vector2(body.x + m.uiPos.x * body.width, body.y + m.uiPos.y * body.height)
                    : new Vector2(centre.x + m.pos.x * cell, centre.y - m.pos.y * cell);
                string tip = cur == null ? m.label + ": empty (optional). Click to fit a weapon."
                    : m.label + ": " + cur.weapon.LabelCap + " (" + Stock(cur.weapon) + " in stock). Click to choose a weapon" + (m.optional ? " or leave it empty." : ".");
                if (DrawSlot(p, boxSize, m.label + (m.optional ? " (optional)" : ""), cur == null ? "None" : cur.weapon.LabelCap, cur?.weapon.uiIcon, tip)) OpenWeaponMenu(i);
            }
            if (cls.heads.Count > 0)
            {
                KnightHead head = cls.heads[Mathf.Clamp(headIdx, 0, cls.heads.Count - 1)];
                Vector2 hp = new Vector2(centre.x + cls.headPos.x * cell, centre.y - cls.headPos.y * cell);
                if (DrawSlot(hp, boxSize, "Head", head.label.CapitalizeFirst(), head.item.uiIcon,
                             "Head: " + head.label.CapitalizeFirst() + " (" + Stock(head.item) + " in stock). Click to choose a head.")) OpenHeadMenu();
            }

            if (cls.lifeSupport != null)
            {
                Vector2 lp = new Vector2(view.x + 60f, view.y + 50f);
                string lsTip = "Life-support module (optional): " + (lsOn ? "FITTED" : "not fitted") + " (" + Stock(cls.lifeSupport) + " in stock). It greatly slows the pilot's neural strain, ageing and damage shock. Click to " + (lsOn ? "remove" : "fit") + " it.";
                if (DrawSlot(lp, boxSize, "Life support (optional)", lsOn ? "Fitted" : "None", lsOn ? cls.lifeSupport.uiIcon : null, lsTip)) lsOn = !lsOn;
            }

            if (cls.voidShield != null)
            {
                Vector2 vp = new Vector2(view.x + 60f, view.y + 50f + boxSize + 34f);
                string vsTip = "Void shield generator (optional carapace module): " + (vsOn ? "FITTED" : "not fitted") + " (" + Stock(cls.voidShield) + " in stock). It absorbs about " + Mathf.RoundToInt(cls.voidShieldCapacity) + " damage, then recharges a few seconds after the last hit. Click to " + (vsOn ? "remove" : "fit") + " it.";
                if (DrawSlot(vp, boxSize, "Void shield (optional)", vsOn ? "Fitted" : "None", vsOn ? cls.voidShield.uiIcon : null, vsTip)) vsOn = !vsOn;
            }

            // ---- right panel: liveries + parts + buttons
            float rx = view.xMax + 14f;
            float rw = inRect.width - rx;
            float y = 164f;
            if (paintable)
            {
                Widgets.Label(new Rect(rx, y, rw, 24f), "Paint scheme");
                y += 26f;
                KnightPaint.DrawSwatch(new Rect(rx, y + 4f, 60f, 26f), pc.one, pc.two);
                Widgets.Label(new Rect(rx + 68f, y + 6f, rw - 68f, 24f), KnightPaint.NameOf(cls.vehicle, pc));
                y += 36f;
                VehiclePawn rk = refitKnight;
                if (Widgets.ButtonText(new Rect(rx, y, Mathf.Min(rw, 220f), 30f), rk != null ? "Repaint now..." : "Choose colours..."))
                    Find.WindowStack.Add(new Window_PaintKnight(cls, rk, pc, ch =>
                    {
                        if (rk != null) KnightPaint.Apply(rk, ch);
                        else paintChoice = ch;
                    }));
                y += 40f;
            }
            int skinCount = refitKnight != null || paintable ? 0 : cls.skins.Count + 1;
            if (refitKnight == null && !paintable) { Widgets.Label(new Rect(rx, y, rw, 24f), "Livery"); y += 26f; }
            float sw = 88f;
            for (int k = 0; k < skinCount; k++)
            {
                RetextureDef skin = k == 0 ? null : cls.skins[k - 1];
                int col = k % 3, row = k / 3;
                Rect r = new Rect(rx + col * (sw + 6f), y + row * (sw + 24f), sw, sw + 20f);
                bool sel = k == skinIdx;
                Widgets.DrawBoxSolid(r, sel ? new Color(0.15f, 0.3f, 0.15f, 0.9f) : new Color(0.07f, 0.09f, 0.12f, 0.85f));
                GUI.color = sel ? Color.green : (Mouse.IsOver(r) ? Color.white : new Color(0.6f, 0.7f, 0.8f));
                Widgets.DrawBox(r, sel ? 3 : 1);
                GUI.color = Color.white;
                DrawPicture(new Rect(r.x + 4f, r.y + 2f, sw - 8f, sw - 8f), BodyTex(cls, skin), "");
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(r.x, r.y + sw - 4f, r.width, 20f), skin == null ? "Standard" : skin.LabelCap.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(r)) skinIdx = k;
            }
            if (skinCount > 0) y += ((skinCount - 1) / 3 + 1) * (sw + 24f) + 8f;

            if (devFree)
            {
                // every change is applied the moment it is picked: nothing is taken from stock and nothing is handed back
                var devOrder = BuildOrder();
                if (Building_KnightHangar.RefitNeeds(refitKnight, cls, devOrder.turretKeys, headIdx, lsOn, vsOn, new List<ThingDef>()).Count > 0
                    || (refitKnight.GetComp<CompKnightHead>()?.lifeSupport ?? false) != lsOn || (refitKnight.GetComp<CompKnightHead>()?.voidShield ?? false) != vsOn
                    || !Building_KnightHangar.CurrentKeys(refitKnight, cls).SequenceEqual(devOrder.turretKeys))
                    Building_KnightHangar.DevRefit(refitKnight, cls, devOrder.turretKeys, headIdx, lsOn, vsOn);
                Widgets.Label(new Rect(rx, y, rw, 24f), "Dev mode");
                y += 24f;
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.6f, 1f, 0.6f);
                Widgets.Label(new Rect(rx, y, rw, 90f), "Click a slot and pick anything: it is fitted at once, fully loaded, at no cost. Lost (STC) weapons are offered too. Nothing is taken from or returned to stock.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(inRect.width - 100f, inRect.height - 44f, 100f, 38f), "Close")) Close();
                return;
            }
            Widgets.Label(new Rect(rx, y, rw, 24f), refitKnight != null ? "Pieces needed for the refit" : "Parts needed");
            y += 24f;
            var order = BuildOrder();
            var need = new Dictionary<ThingDef, int>();
            List<string> refitKeys = null;
            if (refitKnight != null)
            {
                refitKeys = order.turretKeys;
                foreach (ThingDef d in Building_KnightHangar.RefitNeeds(refitKnight, cls, refitKeys, headIdx, lsOn, vsOn, null)) need[d] = need.ContainsKey(d) ? need[d] + 1 : 1;
                if (need.Count == 0) { Text.Font = GameFont.Tiny; Widgets.Label(new Rect(rx, y, rw, 36f), "Pick a different weapon or head to change something."); Text.Font = GameFont.Small; y += 36f; }
            }
            else foreach (ThingDef d in order.RequiredItems) need[d] = need.ContainsKey(d) ? need[d] + 1 : 1;
            foreach (KeyValuePair<ThingDef, int> kv in need)
            {
                int have = Stock(kv.Key);
                GUI.color = have >= kv.Value ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.55f, 0.55f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(rx, y, rw, 18f), kv.Value + " x " + kv.Key.LabelCap + "  (" + have + " in stock)");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                y += 18f;
            }

            Rect confirm = new Rect(rx, inRect.height - 44f, 190f, 38f);
            if (refitKnight != null)
            {
                if (Widgets.ButtonText(confirm, "Refit now") && hangar.Refit(refitKnight, cls, refitKeys, headIdx, lsOn, vsOn)) Close();
            }
            else if (Widgets.ButtonText(confirm, hangar.order != null ? "Replace current order" : "Order assembly"))
            {
                hangar.order = order;
                Messages.Message(cls.LabelCap + " assembly ordered. Builders will haul in the parts and assemble it.", hangar, MessageTypeDefOf.PositiveEvent, false);
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.width - 100f, inRect.height - 44f, 100f, 38f), "Close")) Close();
        }

        HangarOrder BuildOrder()
        {
            KnightClassDef cls = classes[classIdx];
            var o = new HangarOrder { cls = cls, skin = skinIdx == 0 || skinIdx > cls.skins.Count ? null : cls.skins[skinIdx - 1], headIndex = headIdx, lifeSupport = lsOn, voidShield = vsOn,
                                      paint = paintChoice != null, paintOne = paintChoice?.one ?? Color.white, paintTwo = paintChoice?.two ?? Color.white, paintThree = paintChoice?.three ?? Color.white,
                                      stickers = paintChoice?.stickers != null ? paintChoice.stickers.Select(x => x.Copy()).ToList() : new List<KnightSticker>() };
            for (int i = 0; i < cls.mounts.Count; i++) o.turretKeys.Add(choice[i] < 0 ? KnightMount.None : cls.mounts[i].options[choice[i]].turretKey);
            return o;
        }
    }

    // ------------------------------------------------------------------ assembling
    public class WorkGiver_AssembleKnight : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Thing t in pawn.Map.listerThings.ThingsOfDef(HangarDefOf.LIK_KnightHangar)) yield return t;
        }

        public override PathEndMode PathEndMode { get { return PathEndMode.Touch; } }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_KnightHangar h = t as Building_KnightHangar;
            if (h == null || h.order == null || t.IsForbidden(pawn)) return false;
            if (pawn.skills == null || pawn.skills.GetSkill(SkillDefOf.Construction).Level < h.order.cls.constructionSkill)
            {
                JobFailReason.Is("needs construction " + h.order.cls.constructionSkill);
                return false;
            }
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;
            if (h.FindDockedKnight() != null) { JobFailReason.Is("a Knight is docked in the bay"); return false; }
            if (h.StowedKnight != null) { JobFailReason.Is("a Knight is stowed in the bay"); return false; }
            string spotBlocked = h.KnightSpotBlocked(h.order.cls.vehicle);
            if (spotBlocked != null) { JobFailReason.Is(spotBlocked); return false; }
            if (!h.AllItemsAvailable(pawn)) { JobFailReason.Is("missing parts or weapons"); return false; }
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(HangarDefOf.LIK_AssembleKnight, t);
        }
    }

    /// <summary>Haul every required part to its slot in the hangar's delivery row, then work until the Knight is assembled.</summary>
    public class JobDriver_AssembleKnight : JobDriver
    {
        int idx;
        Thing item;

        Building_KnightHangar Hangar { get { return (Building_KnightHangar)TargetThingA; } }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Hangar.order == null);

            globalFinishActions.Add(delegate (JobCondition c)
            {
                if (pawn.carryTracker?.CarriedThing != null)
                {
                    Thing dropped;
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out dropped);
                }
            });

            Toil pick = new Toil();
            Toil gotoItem = new Toil();
            Toil takeItem = new Toil();
            Toil gotoSlot = new Toil();
            Toil drop = new Toil();
            Toil work = new Toil();

            pick.defaultCompleteMode = ToilCompleteMode.Instant;
            pick.initAction = delegate
            {
                List<ThingDef> req = Hangar.order.RequiredItems;
                while (idx < req.Count && Hangar.ItemAt(idx) != null) idx++;
                if (idx >= req.Count) { JumpToToil(work); return; }
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried != null && carried.def == req[idx]) { JumpToToil(gotoSlot); return; }
                item = Hangar.FindLoose(pawn, req[idx]);
                if (item == null)
                {
                    Messages.Message(pawn.LabelShortCap + " cannot find the " + req[idx].label + " for the assembly.", pawn, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                }
            };

            gotoItem.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            gotoItem.initAction = delegate
            {
                if (item == null || item.Destroyed || !item.Spawned) { JumpToToil(pick); return; }
                pawn.pather.StartPath(item, PathEndMode.Touch);
            };

            takeItem.defaultCompleteMode = ToilCompleteMode.Instant;
            takeItem.initAction = delegate
            {
                if (item == null || item.Destroyed || !item.Spawned) { JumpToToil(pick); return; }
                if (pawn.carryTracker.TryStartCarry(item, 1) <= 0) { EndJobWith(JobCondition.Incompletable); return; }
            };

            gotoSlot.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            gotoSlot.initAction = delegate { pawn.pather.StartPath(Hangar.SlotCell(idx), PathEndMode.OnCell); };

            drop.defaultCompleteMode = ToilCompleteMode.Instant;
            drop.initAction = delegate
            {
                Thing result;
                pawn.carryTracker.TryDropCarriedThing(Hangar.SlotCell(idx), ThingPlaceMode.Direct, out result);
                if (result != null)
                {
                    var cf = result.TryGetComp<CompForbiddable>();
                    if (cf != null) cf.Forbidden = true;          // keep haulers off the delivered parts
                }
                idx++;
                JumpToToil(pick);
            };

            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.socialMode = RandomSocialMode.Off;
            work.initAction = delegate { pawn.pather.StopDead(); };
            work.tickAction = delegate
            {
                HangarOrder o = Hangar.order;
                pawn.rotationTracker.FaceTarget(Hangar.Position);
                o.work += pawn.GetStatValue(StatDefOf.GeneralLaborSpeed);
                if (pawn.IsHashIntervalTick(80)) FleckMaker.ThrowMicroSparks(pawn.DrawPos, pawn.Map);
                if (o.work < o.cls.assembleWork) return;
                if (!Hangar.Complete(pawn))
                {
                    Messages.Message("The assembly could not be completed: a part is missing from the delivery row, or a Knight is docked in the bay.", Hangar, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                EndJobWith(JobCondition.Succeeded);
            };
            work.WithProgressBar(TargetIndex.A, () => Hangar.order == null ? 1f : Mathf.Clamp01(Hangar.order.work / Mathf.Max(1f, Hangar.order.cls.assembleWork)));

            yield return pick;
            yield return gotoItem;
            yield return takeItem;
            yield return gotoSlot;
            yield return drop;
            yield return work;
        }
    }
}
