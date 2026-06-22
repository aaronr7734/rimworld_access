using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Resolves a spoken appearance description for a hair, beard, or tattoo style so screen-reader
    /// players can know what each one looks like. The descriptions live in
    /// Languages/&lt;lang&gt;/Keyed/RimWorldAccess_StyleDescriptions.xml, keyed
    /// <c>RimWorldAccess.StyleDesc.{Hair|Beard|Tattoo}_{defName}</c>.
    ///
    /// Coverage is the vanilla and DLC style sets. Modded styles (or any def without a key) resolve
    /// to an empty string, letting callers append the clause only when one exists.
    /// </summary>
    public static class StyleDescriptionHelper
    {
        /// <summary>The appearance description for a style, or empty if none is defined.</summary>
        public static string Describe(StyleItemDef def)
        {
            if (def == null) return string.Empty;

            string prefix = TypePrefix(def);
            if (prefix == null) return string.Empty;

            string key = "RimWorldAccess.StyleDesc." + prefix + "_" + def.defName;
            return key.CanTranslate() ? key.Translate().Resolve() : string.Empty;
        }

        /// <summary>True when a description exists for this style.</summary>
        public static bool HasDescription(StyleItemDef def)
        {
            if (def == null) return false;
            string prefix = TypePrefix(def);
            if (prefix == null) return false;
            return ("RimWorldAccess.StyleDesc." + prefix + "_" + def.defName).CanTranslate();
        }

        private static string TypePrefix(StyleItemDef def)
        {
            if (def is HairDef) return "Hair";
            if (def is BeardDef) return "Beard";
            if (def is TattooDef) return "Tattoo";
            return null;
        }
    }
}
