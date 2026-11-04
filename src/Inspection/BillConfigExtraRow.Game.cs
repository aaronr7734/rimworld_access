using System;
using System.Collections.Generic;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// One row a registered source adds to the bill menu, for controls a mod draws into
    /// Dialog_BillConfig's repeat-mode section. Rows are spliced in where that section ends, so they
    /// keep the dialog's own reading order. Either <see cref="Standard"/> names a vanilla row to reuse
    /// as-is, or the delegates describe the row; a null delegate means the row lacks that behavior.
    /// </summary>
    internal sealed class BillConfigExtraRow
    {
        /// <summary>The vanilla bill rows a mod's own panel redraws verbatim, offered so it can reuse them.</summary>
        internal enum StandardRow
        {
            IncludeEquipped,
            IncludeTainted,
            HpRange,
            QualityRange,
            LimitToAllowedStuff,
        }

        public StandardRow? Standard;

        public Func<string> Label;
        public Func<string> SearchLabel;
        public Func<string> Description;
        public Shell.ElementRole Role;
        public bool Editable;
        public Action Execute;
        public Action<int, int> Adjust;
        public Action JumpMin;
        public Action JumpMax;
        public Func<string> NumericText;
        public Action<int> ApplyNumeric;

        /// <summary>The listing-ring token the mod's draw is marked with for this row, and the caption it must have drawn.</summary>
        public string RingKey;
        public Func<string> RingTripwire;

        public static BillConfigExtraRow Reuse(StandardRow row)
        {
            return new BillConfigExtraRow { Standard = row };
        }

        private static readonly List<Func<Bill_Production, IEnumerable<BillConfigExtraRow>>> sources =
            new List<Func<Bill_Production, IEnumerable<BillConfigExtraRow>>>();

        internal static IReadOnlyList<Func<Bill_Production, IEnumerable<BillConfigExtraRow>>> Sources
        {
            get { return sources; }
        }

        /// <summary>Asked on every row refresh; returns nothing for a bill it has no rows for.</summary>
        public static void RegisterSource(Func<Bill_Production, IEnumerable<BillConfigExtraRow>> source)
        {
            if (source != null)
            {
                sources.Add(source);
            }
        }
    }
}
