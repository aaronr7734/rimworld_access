using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A read-time correction for a base-game (or DLC) ConceptDef's help text.
    ///
    /// RimWorld Access is the only way our users experience RimWorld, so the mod's
    /// documentation must be authoritative: where a vanilla concept's help text is
    /// inaccurate or incomplete for keyboard / screen-reader play, we author corrected
    /// text here and the Learning Helper substitutes it when reading that concept
    /// (see <see cref="ConceptHelpOverrides"/>).
    ///
    /// <see cref="concept"/> is the target concept's defName as a plain string rather than
    /// a ConceptDef reference on purpose: an override for a DLC concept then causes no load
    /// error when that DLC is absent — the concept simply never loads, so the override never
    /// matches. The text lives in Defs/ConceptDefs/Overrides_Vanilla.xml.
    /// </summary>
    public class ConceptHelpOverrideDef : Def
    {
        /// <summary>defName of the vanilla/DLC ConceptDef whose help text this replaces.</summary>
        public string concept;

        /// <summary>Corrected help text. Paragraphs separated by literal \n\n (converted to
        /// newlines at XML load), matching how the chapter ConceptDefs are authored.</summary>
        public string helpText;

        // NOTE: the optional corrected label reuses the inherited Def.label field. When set in
        // XML, the Learning Helper shows and announces it in place of the vanilla concept's label
        // (e.g. presenting WorldCameraMovement as "The world map"). It localizes via DefInjected
        // like any other Def label. See ConceptHelpOverrides.GetLabel.
    }
}
