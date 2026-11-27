using System.Collections.Generic;
using RimWorld;
using SmashTools;
using UnityEngine;
using Verse;

namespace LIK
{
    /// <summary>Which way a hangar def faces (user 2026-11-26: West/East hangars next to the original South one; North left out because the
    /// pillars and crane rail would hide the bay). Each facing is its own ThingDef (sizes differ: 20x11 south, 18x12 sideways), grouped in
    /// one dropdown in the Architect menu. The Aquila and everything else turn with the building.</summary>
    public class DefModExtension_HangarFacing : DefModExtension
    {
        public string facing = "South";
        public Rot4 Rot { get { return facing == "West" ? Rot4.West : facing == "East" ? Rot4.East : Rot4.South; } }
    }

    /// <summary>The art geometry of one hangar facing (all in cells). South = HangarArtData (unchanged), West/East = HangarArtDataSides.</summary>
    public class HangarGeo
    {
        public Rot4 facing = Rot4.South;
        public string tex;                                   // layer texture prefix
        public float drawW, drawH, offX, offZ;               // building graphic size + offset from the footprint centre
        public float bayX, bayZ;                             // drawn bay centre from the hangar's TrueCenter
        public bool clipZ;                                   // doors slide and clip along screen z (sideways hangars) instead of x
        public float hole0, hole1;                           // door clip range along that axis, from the picture centre
        public float slide, drop;                            // door travel (slide already foreshortened on screen)
        public float lip;                                    // the bay's front lip (hazard line), cells below the bay centre on screen
        public Dictionary<string, float> feetZ;              // class -> picture centre above its feet (minus 0.4) in this facing's view
        public Dictionary<string, float[]> layers;           // name -> centre x, centre z (from the picture centre), width, height

        public Rot8 Rot8 { get { return new Rot8(facing); } }
        /// <summary>Where a Knight standing on the map puts its feet (screen z from the bay centre). South: just behind the front lip (measured
        /// 3.07 = lip 3.32 - 0.25). Sideways hangars: the docked pose (the 'front' there is the apron side, not the bottom of the screen).</summary>
        public float FeetFromBayCentre { get { return facing == Rot4.South ? -(lip - 0.25f) : -0.4f; } }
        /// <summary>User 2026-11-27 (first in-game look): Knights in the sideways hangars stood too far forward -> 2 cells further back, away
        /// from the apron (onto the eagle's head). West hangar: +x, East: -x, South: 0.</summary>
        public float BackX { get { return facing == Rot4.West ? 2f : facing == Rot4.East ? -2f : 0f; } }

        public static readonly HangarGeo South = new HangarGeo
        {
            facing = Rot4.South, tex = "Things/Knights/Hangar/hangar_",
            drawW = HangarArtData.DrawW, drawH = HangarArtData.DrawH, offX = HangarArtData.OffX, offZ = HangarArtData.OffZ,
            bayX = HangarArtData.BayX, bayZ = HangarArtData.BayZ,
            clipZ = false, hole0 = HangarArtData.HoleX0, hole1 = HangarArtData.HoleX1,
            slide = HangarArtData.Slide, drop = HangarArtData.Drop, lip = 3.32f,
            feetZ = HangarArtData.FeetZ, layers = HangarArtData.Layers,
        };

        static readonly Dictionary<ThingDef, HangarGeo> byDef = new Dictionary<ThingDef, HangarGeo>();
        public static HangarGeo For(ThingDef def)
        {
            if (def == null) return South;
            if (byDef.TryGetValue(def, out HangarGeo g)) return g;
            Rot4 r = def.GetModExtension<DefModExtension_HangarFacing>()?.Rot ?? Rot4.South;
            g = r == Rot4.West ? HangarArtDataSides.West : r == Rot4.East ? HangarArtDataSides.East : South;
            byDef[def] = g;
            return g;
        }

        /// <summary>Every Knight hangar def (South, West, East).</summary>
        static List<ThingDef> all;
        public static List<ThingDef> AllDefs
        {
            get
            {
                if (all == null)
                {
                    all = new List<ThingDef>();
                    foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
                        if (d.thingClass != null && typeof(Building_KnightHangar).IsAssignableFrom(d.thingClass)) all.Add(d);
                }
                return all;
            }
        }

        /// <summary>This class's picture centre above its feet (minus 0.4) in this facing's view (side pictures for West/East).</summary>
        public float FeetZFor(string classOrDefName)
        {
            string n = classOrDefName ?? "";
            foreach (KeyValuePair<string, float> kv in feetZ) if (n.Contains(kv.Key)) return kv.Value;
            return 0f;
        }
    }
}
