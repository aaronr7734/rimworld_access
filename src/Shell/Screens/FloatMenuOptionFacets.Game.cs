using System;
using System.Runtime.CompilerServices;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// What a mod's own <see cref="FloatMenuOption"/> shows but keeps out of reach: a check mark
    /// painted by its extraPartOnGUI, or a tooltip held in a private field and drawn by its own
    /// DoGUI. A compat module attaches these as the option is built; the windowless menu reads
    /// them, so the row speaks and toggles like a <see cref="CheckboxFloatMenuOption"/>.
    /// Weakly keyed, since options are rebuilt every time a menu opens.
    /// </summary>
    internal static class FloatMenuOptionFacets
    {
        private sealed class Facets
        {
            public Func<bool> CheckState;
            public string Tooltip;
        }

        private static readonly ConditionalWeakTable<FloatMenuOption, Facets> table =
            new ConditionalWeakTable<FloatMenuOption, Facets>();

        public static void SetCheckState(FloatMenuOption option, Func<bool> checkState)
        {
            if (option != null && checkState != null)
            {
                table.GetOrCreateValue(option).CheckState = checkState;
            }
        }

        public static void SetTooltip(FloatMenuOption option, string tooltip)
        {
            if (option != null && !string.IsNullOrEmpty(tooltip))
            {
                table.GetOrCreateValue(option).Tooltip = tooltip;
            }
        }

        /// <summary>The row's live check state, from <see cref="CheckboxFloatMenuOption"/> or an attached facet; null for a plain row.</summary>
        public static Func<bool> CheckStateOf(FloatMenuOption option)
        {
            if (option is CheckboxFloatMenuOption checkbox)
            {
                return checkbox.StateGetter;
            }
            Facets facets;
            return option != null && table.TryGetValue(option, out facets) ? facets.CheckState : null;
        }

        public static string TooltipOf(FloatMenuOption option)
        {
            Facets facets;
            return option != null && table.TryGetValue(option, out facets) ? facets.Tooltip : null;
        }
    }
}
