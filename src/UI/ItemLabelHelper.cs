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
        /// entirely. Vanilla's own suffix is stripped and the same GenLabel.LabelExtras facts
        /// re-added comma-separated, so they read as distinct and a named weapon keeps them.
        /// </summary>
        public static string LabelWithCondition(Thing thing)
        {
            string label = thing.LabelCap;
            if (label.NullOrEmpty())
                return label;

            string vanillaExtras = GenLabel.LabelExtras(thing, includeHp: true, includeQuality: true);
            if (!vanillaExtras.NullOrEmpty() && label.EndsWith(vanillaExtras))
                label = label.Substring(0, label.Length - vanillaExtras.Length);

            QualityCategory? quality = thing.TryGetQuality(out QualityCategory qc) ? qc : (QualityCategory?)null;
            string wornByCorpse = thing is Apparel apparel && apparel.WornByCorpse
                ? (string)"WornByCorpseChar".Translate()
                : null;
            return WithExtras(label, quality, thing, wornByCorpse);
        }

        /// <summary><paramref name="label"/> with a comma-separated "(quality, condition, trailing)" suffix of whichever parts exist.</summary>
        public static string WithExtras(string label, QualityCategory? quality, Thing conditionSource, string trailing = null)
        {
            var parts = new List<string>();
            if (quality.HasValue)
                parts.Add(quality.Value.GetLabel());
            string condition = ConditionPercent(conditionSource);
            if (condition != null)
                parts.Add(condition);
            if (trailing != null)
                parts.Add(trailing);
            return parts.Count > 0 ? label + " (" + string.Join(", ", parts) + ")" : label;
        }

        /// <summary>The damaged-item percent, or null; mirrors GenLabel.LabelExtras' hit-point gate (RimWorld/GenLabel.cs:193).</summary>
        public static string ConditionPercent(Thing thing)
        {
            if (thing == null || !thing.def.useHitPoints || thing.HitPoints >= thing.MaxHitPoints || thing.def.stackLimit != 1)
                return null;
            return ((float)thing.HitPoints / thing.MaxHitPoints).ToStringPercent();
        }
    }
}
