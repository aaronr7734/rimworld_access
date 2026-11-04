using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    internal sealed partial class ClTagEditorScope
    {
        /// <summary>
        /// DrawDefList's list, built in its order so the same unstable sort yields the same rows.
        /// Hand-mirrored because the draw computes it into a local with nothing to read back; every
        /// test inside is the mod's own helper.
        /// </summary>
        private List<ThingDef> BuildDefList(int category, List<object> items)
        {
            List<ThingDef> defList = ClCompat.TagEditor.CategoryDefs(category);
            var inTag = new HashSet<ThingDef>(items.SelectMany(ClCompat.TagEditor.ItemDefs));
            object slotsUsed = ClCompat.TagEditor.ApparelSlot(items
                .Where(item => item != addingTo && ClCompat.ItemIsApparel(item))
                .SelectMany(ClCompat.TagEditor.ItemDefs)
                .ToList());
            Func<ThingDef, bool> fitsWorn = t => !t.IsApparel
                || !ClCompat.TagEditor.SlotsIntersect(slotsUsed, ClCompat.TagEditor.ApparelSlot(t));

            List<ThingDef> defs;
            string filter = ClCompat.TagEditor.DefFilter(dialog);
            if (filter != string.Empty)
            {
                List<ThingDef> cached = ClCompat.TagEditor.CachedFilteredDefs(dialog);
                defs = cached != null
                    ? cached.Where(fitsWorn).ToList()
                    : defList.Where(fitsWorn).Where(SearchMatcher(filter)).ToList();
            }
            else
            {
                defs = defList.Where(fitsWorn).ToList();
            }

            defs = defs.Where(t => !inTag.Contains(t)).ToList();
            defs = defs.Where(t => !ClCompat.TagEditor.IsProsthetic(t) || ClCompat.TagEditor.SlotsFit(prostheticSlots, t)).ToList();
            if (addingTo != null)
            {
                bool apparel = ClCompat.ItemIsApparel(addingTo);
                defs = defs.Where(t => t.IsApparel == apparel && !ClCompat.TagEditor.IsProsthetic(t)).ToList();
            }

            int showOnly = ClCompat.TagEditor.ShowOnly(dialog);
            if (showOnly == 1)
            {
                var craftable = new HashSet<ThingDef>(DefDatabase<RecipeDef>.AllDefs
                    .Where(recipe => recipe.AvailableNow)
                    .SelectMany(recipe => recipe.products.Select(pc => pc.thingDef)));
                defs = defs.Where(def => def.IsBuildingArtificial || craftable.Contains(def)).ToList();
                defs = defs.Where(def => !def.IsBuildingArtificial || def.IsResearchFinished).ToList();
            }
            else if (showOnly == 2)
            {
                HashSet<ThingDef> inColony = ClCompat.TagEditor.ColonyDefs(dialog);
                defs = defs.Where(inColony.Contains).ToList();
            }

            Sort(defs);
            return defs;
        }

        /// <summary>DrawDefList's search test: name, mod, a stat bonus, an apparel layer, or a prosthetic's effect.</summary>
        private static Func<ThingDef, bool> SearchMatcher(string defFilter)
        {
            string filter = defFilter.ToLower();
            var layers = DefDatabase<ApparelLayerDef>.AllDefsListForReading
                .Where(l => l.LabelCap.ToString().ToLower().Contains(filter)).ToList();
            var stats = new HashSet<StatDef>(DefDatabase<StatDef>.AllDefsListForReading
                .Where(s => s.LabelCap.ToString()?.ToLowerInvariant().Contains(filter) ?? false));
            return td => (td.LabelCap.ToString()?.ToLowerInvariant().Contains(filter) ?? false)
                || (td.modContentPack?.Name.ToLowerInvariant().Contains(filter) ?? false)
                || ((td.IsApparel || td.IsWeapon) && (td.statBases?.Any(s => stats.Contains(s.stat)) ?? false))
                || ((td.IsApparel || td.IsWeapon) && (td.equippedStatOffsets?.Any(s => stats.Contains(s.stat)) ?? false))
                || (td.IsApparel && td.apparel.layers.Intersect(layers).Any())
                || ClCompat.TagEditor.EffectsOf(td).Any(effect => effect.Key.ToLowerInvariant().Contains(filter));
        }

        private void Sort(List<ThingDef> defs)
        {
            Dictionary<ThingDef, float> cache = ClCompat.TagEditor.StatCache() ?? new Dictionary<ThingDef, float>();
            float invalid = ClCompat.TagEditor.InvalidStatValue;
            Func<ThingDef, string> byName = td => td.LabelCap.RawText;
            Func<ThingDef, float> byStat = td => cache.TryGetValue(td, out float value) ? value : invalid;
            bool stat = ClCompat.TagEditor.SortByStat(dialog);
            if (ClCompat.TagEditor.Ascending(dialog))
            {
                if (stat)
                {
                    defs.SortBy(byStat);
                }
                else
                {
                    defs.SortBy(byName);
                }
            }
            else if (stat)
            {
                defs.SortByDescending(byStat);
            }
            else
            {
                defs.SortByDescending(byName);
            }
        }
    }
}
