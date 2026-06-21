using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Looks up RimWorld Access's authoritative help-text corrections for vanilla/DLC
    /// concepts. The Learning Helper consults this before falling back to the game's own
    /// <see cref="RimWorld.ConceptDef.HelpTextAdjusted"/>, so our corrected documentation is
    /// what screen-reader users hear. Backed by <see cref="ConceptHelpOverrideDef"/> defs,
    /// which keeps the content data-driven and localizable via DefInjected.
    /// </summary>
    public static class ConceptHelpOverrides
    {
        // concept defName -> corrected help text. Built lazily from the def database.
        private static Dictionary<string, string> overridesByConcept;
        // concept defName -> corrected label (only for overrides that set one).
        private static Dictionary<string, string> labelsByConcept;

        /// <summary>
        /// Returns the corrected help text for <paramref name="concept"/>, or null if no
        /// override is authored for it (in which case the caller uses the game's own text).
        /// </summary>
        public static string GetHelpText(RimWorld.ConceptDef concept)
        {
            if (concept == null)
                return null;

            if (overridesByConcept == null)
                Build();

            return overridesByConcept.TryGetValue(concept.defName, out string text) ? text : null;
        }

        /// <summary>
        /// Returns the corrected label for <paramref name="concept"/>, or null if no override
        /// renames it (in which case the caller uses the game's own label).
        /// </summary>
        public static string GetLabel(RimWorld.ConceptDef concept)
        {
            if (concept == null)
                return null;

            if (overridesByConcept == null)
                Build();

            return labelsByConcept.TryGetValue(concept.defName, out string label) ? label : null;
        }

        /// <summary>
        /// The label to show/announce for <paramref name="concept"/>: our authoritative override
        /// when one exists, otherwise the game's own capitalized label. Used everywhere the
        /// Learning Helper presents a concept name (list, detail header, new-lesson announcement).
        /// </summary>
        public static string DisplayLabel(RimWorld.ConceptDef concept)
        {
            if (concept == null)
                return string.Empty;

            return GetLabel(concept) ?? concept.LabelCap;
        }

        private static void Build()
        {
            overridesByConcept = new Dictionary<string, string>();
            labelsByConcept = new Dictionary<string, string>();

            foreach (ConceptHelpOverrideDef def in DefDatabase<ConceptHelpOverrideDef>.AllDefsListForReading)
            {
                if (string.IsNullOrEmpty(def.concept))
                    continue;

                if (!string.IsNullOrEmpty(def.label))
                    labelsByConcept[def.concept] = def.label.CapitalizeFirst();

                if (string.IsNullOrEmpty(def.helpText))
                    continue;

                // Last def wins on a duplicate target; warn so authoring mistakes surface.
                if (overridesByConcept.ContainsKey(def.concept))
                    Log.Warning($"[RimWorld Access] Duplicate concept help override for '{def.concept}' " +
                                $"(def {def.defName}); the later one wins.");

                overridesByConcept[def.concept] = def.helpText;
            }
        }
    }
}
