using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Item labels that keep the condition and quality suffix vanilla shows.
    /// </summary>
    public static class ItemLabelHelper
    {
        /// <summary>
        /// The item's cap label with a "(quality, condition)" suffix. Vanilla glues those two
        /// facts into one parenthesis separated by a space ("excellent 43%"), which reads as if
        /// the percent qualifies the quality, and a comp such as CompUniqueWeapon replaces the
        /// whole label with just the item's name (Odyssey unique weapons), dropping the suffix
        /// entirely. Vanilla's own suffix is stripped and the same facts re-added comma-separated,
        /// so quality and condition read as the distinct things they are and a named weapon keeps
        /// them. The parts mirror GenLabel.LabelExtras.
        /// </summary>
        public static string LabelWithCondition(Thing thing)
        {
            string label = thing.LabelCap;
            if (label.NullOrEmpty())
                return label;

            string vanillaExtras = GenLabel.LabelExtras(thing, includeHp: true, includeQuality: true);
            if (!vanillaExtras.NullOrEmpty() && label.EndsWith(vanillaExtras))
                label = label.Substring(0, label.Length - vanillaExtras.Length);

            var parts = new List<string>();
            if (thing.TryGetQuality(out QualityCategory quality))
                parts.Add(quality.GetLabel());
            string condition = ConditionPercent(thing);
            if (condition != null)
                parts.Add(condition);
            if (thing is Apparel apparel && apparel.WornByCorpse)
                parts.Add("WornByCorpseChar".Translate());

            return parts.Count > 0 ? label + " (" + string.Join(", ", parts) + ")" : label;
        }

        /// <summary>The damaged-item percent GenLabel.LabelExtras shows, or null when vanilla shows none.</summary>
        public static string ConditionPercent(Thing thing)
        {
            if (thing == null || !thing.def.useHitPoints || thing.HitPoints >= thing.MaxHitPoints || thing.def.stackLimit != 1)
                return null;
            return ((float)thing.HitPoints / thing.MaxHitPoints).ToStringPercent();
        }
    }
}
