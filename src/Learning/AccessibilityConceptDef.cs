using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// A ConceptDef authored by RimWorld Access to document the mod's own
    /// accessibility features (keyboard navigation, menus, screen reader flow).
    ///
    /// These chapters ride RimWorld's existing Learning Helper infrastructure:
    /// they are browsable in the "?" menu, read line-by-line through the same
    /// detail view, and track knowledge like any vanilla concept. Subclassing
    /// (rather than string-matching defNames) lets the menu detect our chapters
    /// by type and present them in their own dedicated section.
    /// </summary>
    public class AccessibilityConceptDef : ConceptDef
    {
        /// <summary>
        /// Optional grouping label (e.g. "Getting around", "Colonists") used to
        /// organize chapters within the RimWorld Access docs view. Not required.
        /// </summary>
        public string category;
    }
}
