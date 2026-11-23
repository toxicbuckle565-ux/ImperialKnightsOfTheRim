using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LIK
{
    /// <summary>
    /// In-game choice of what Knight parts, weapons and modules cost to forge (user 2026-11-11: "config for prices and materials in game with tiers
    /// like playing for fun, arcade, challenge, realistic"). Mod settings -> Imperial Knights -> "Forge costs".
    ///
    /// The recipe defs (LIK_Make_*, written by the generator) hold the CHALLENGE costs. A tier multiplies every material and the forging work,
    /// and the easier tiers drop the rare materials altogether:
    ///   For fun    steel and components only, a fraction of the amounts, almost no work
    ///   Arcade     adds plasteel and a few advanced components; no gold; about half
    ///   Challenge  as written: steel, plasteel, components, advanced components, gold on the big ones
    ///   Realistic  everything, and much more of it - a Knight weapon is the work of a whole colony
    /// Applied when the game starts and whenever the setting changes; bills already queued simply ask for the new amounts.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CostConfig
    {
        public class Tier
        {
            public string label, desc;
            public float steel, plasteel, comp, spacer, gold, other, work;
        }

        public static readonly Tier[] Tiers =
        {
            new Tier { label = "Playing for fun", desc = "Steel and components only, a fraction of the amounts, almost no forging work. For trying everything out.",
                       steel = 0.25f, plasteel = 0f, comp = 0.25f, spacer = 0f, gold = 0f, other = 0.25f, work = 0.15f },
            new Tier { label = "Arcade", desc = "About half price. Steel, plasteel, components and a few advanced components. No gold.",
                       steel = 0.45f, plasteel = 0.4f, comp = 0.5f, spacer = 0.3f, gold = 0f, other = 0.45f, work = 0.5f },
            new Tier { label = "Challenge", desc = "The standard late-game costs: every material, gold on the big weapons.",
                       steel = 1f, plasteel = 1f, comp = 1f, spacer = 1f, gold = 1f, other = 1f, work = 1f },
            new Tier { label = "Realistic", desc = "Far more of everything, above all plasteel, advanced components and gold, and twice the work. A Knight weapon is a colony-wide project.",
                       steel = 1.6f, plasteel = 1.8f, comp = 1.6f, spacer = 2f, gold = 2f, other = 1.6f, work = 2f },
        };
        public const int DefaultTier = 2;

        class Original { public List<KeyValuePair<IngredientCount, float>> ingredients; public float work; }
        static readonly Dictionary<RecipeDef, Original> originals = new Dictionary<RecipeDef, Original>();

        static CostConfig()
        {
            foreach (RecipeDef r in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (r.defName == null || !r.defName.StartsWith("LIK_Make_") || r.ingredients == null) continue;
                originals[r] = new Original { work = r.workAmount, ingredients = r.ingredients.Select(i => new KeyValuePair<IngredientCount, float>(i, i.GetBaseCount())).ToList() };
            }
            Apply();
        }

        public static Tier Current
        {
            get { return Tiers[Mathf.Clamp(LIKMod.I != null ? LIKMod.I.costTier : DefaultTier, 0, Tiers.Length - 1)]; }
        }

        static float Factor(Tier t, IngredientCount ic)
        {
            ThingDef d = ic.IsFixedIngredient ? ic.FixedIngredient : null;
            string n = d != null ? d.defName : "";
            switch (n)
            {
                case "Steel": return t.steel;
                case "Plasteel": return t.plasteel;
                case "ComponentIndustrial": return t.comp;
                case "ComponentSpacer": return t.spacer;
                case "Gold": return t.gold;
                default: return t.other;
            }
        }

        static float Round(IngredientCount ic, float v)
        {
            ThingDef d = ic.IsFixedIngredient ? ic.FixedIngredient : null;
            if (d != null && d.defName.StartsWith("Component")) return Mathf.Max(1f, Mathf.Round(v));
            return v >= 100f ? Mathf.Max(10f, Mathf.Round(v / 10f) * 10f) : Mathf.Max(5f, Mathf.Round(v / 5f) * 5f);
        }

        public static void Apply()
        {
            Tier t = Current;
            float extra = LIKMod.I != null ? Mathf.Clamp(LIKMod.I.costExtra, 0.1f, 5f) : 1f;
            foreach (KeyValuePair<RecipeDef, Original> kv in originals)
            {
                var list = new List<IngredientCount>();
                foreach (KeyValuePair<IngredientCount, float> ing in kv.Value.ingredients)
                {
                    float f = Factor(t, ing.Key);
                    if (f <= 0f) continue;                                   // this tier does not ask for the material at all
                    ing.Key.SetBaseCount(Round(ing.Key, ing.Value * f * extra));
                    list.Add(ing.Key);
                }
                if (list.Count == 0 && kv.Value.ingredients.Count > 0)        // never a free recipe: keep the first material at its minimum
                {
                    IngredientCount first = kv.Value.ingredients[0].Key;
                    first.SetBaseCount(Round(first, 1f));
                    list.Add(first);
                }
                kv.Key.ingredients = list;
                kv.Key.workAmount = Mathf.Max(200f, Mathf.Round(kv.Value.work * t.work * extra / 100f) * 100f);
            }
        }
    }
}
