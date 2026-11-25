using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LIK
{
    public static partial class KnightShotUtil
    {
        /// <summary>User 2026-11-12: "pawns surviving direct blasts from the magna lascannon is unrealistic, they would be instantly evaporated".
        /// Every pawn within the radius (not vehicles, nothing bigger than a thrumbo) is killed and its body, gear and weapon burned away,
        /// leaving ash, sparks and smoke.</summary>
        public static void Vaporise(Map map, IntVec3 cell, float radius, Thing launcher, ThingDef shot)
        {
            if (map == null || !cell.InBounds(map)) return;
            var victims = new List<Pawn>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Thing t in c.GetThingList(map))
                    if (t is Pawn p && !(p is Vehicles.VehiclePawn) && !p.Dead && p.BodySize <= 4f && p != launcher && !victims.Contains(p)
                        && (c == cell || !Transhuman(p))) victims.Add(p);
            }
            foreach (Pawn p in victims)
            {
                IntVec3 at = p.Position;
                try
                {
                    p.equipment?.DestroyAllEquipment();
                    p.Kill(new DamageInfo(DamageDefOf.Flame, 9999f, 99f, -1f, launcher, null, shot), null);
                    Corpse body = p.Corpse;
                    if (body != null && !body.Destroyed) body.Destroy();
                    FilthMaker.TryMakeFilth(at, map, ThingDefOf.Filth_Ash, 3);
                    FleckMaker.ThrowMicroSparks(at.ToVector3Shifted(), map);
                    FleckMaker.ThrowSmoke(at.ToVector3Shifted(), map, 1.5f);
                }
                catch (System.Exception e) { Log.Warning("[LIK] vaporise " + p.LabelShort + ": " + e.Message); }
            }
            if (victims.Count > 0)
                Messages.Message(victims.Count == 1 ? victims[0].LabelShortCap + " was evaporated by the blast." : victims.Count + " were evaporated by the blast.",
                    new TargetInfo(cell, map), MessageTypeDefOf.NeutralEvent, false);
        }

        // User 2026-11-24: Custodes AND Space Marines (incl. Primaris) survive the SPLASH - only the cell the shot lands on evaporates them;
        // around it they take the shot's normal damage. RimDark 40k - Mankinds Finest (BEWH_*); soft check, nothing happens without it.
        static readonly string[] ToughXenotypes = { "BEWH_Custodes", "BEWH_SpaceMarine", "BEWH_PrimarisSpaceMarine" };
        static readonly string[] MarineGenes = { "BEWH_SinewCoil", "BEWH_Magnificat", "BEWH_BelisarianFurnace", "BEWH_ProtoOssmodula", "BEWH_Musculeator",
                                                 "BEWH_Mentanifex", "BEWH_Vigoranis", "BEWH_Hyperanatomica", "BEWH_Furybound" };
        const string CustodesGene = "BEWH_HyperionMuscleStrands";
        static List<GeneDef> marineGenes; static GeneDef custodesGene; static bool genesLooked;

        public static bool Transhuman(Pawn p)
        {
            if (p.genes == null) return false;
            string x = p.genes.Xenotype?.defName;
            if (x != null && System.Array.IndexOf(ToughXenotypes, x) >= 0) return true;
            if (!genesLooked)
            {
                genesLooked = true;
                custodesGene = DefDatabase<GeneDef>.GetNamedSilentFail(CustodesGene);
                var l = new List<GeneDef>();
                foreach (string n in MarineGenes) { GeneDef g = DefDatabase<GeneDef>.GetNamedSilentFail(n); if (g != null) l.Add(g); }
                marineGenes = l.Count == MarineGenes.Length ? l : null;
            }
            if (custodesGene != null && p.genes.HasActiveGene(custodesGene)) return true;     // implanted / custom-xenotype Custodes
            if (marineGenes == null) return false;
            foreach (GeneDef g in marineGenes) if (!p.genes.HasActiveGene(g)) return false;  // the full Astartes gene set, not a half-made aspirant
            return true;
        }
    }

}
