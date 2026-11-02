using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks what a replayed right-click changed when the change is not a toggle visible in the
    /// gizmo menu (a mod's right-click branch that edits state drawn only as overlay icons).
    /// </summary>
    public interface IRightClickOutcomeDescriber
    {
        /// <summary>Snapshots the state this describer watches; false when the gizmo is not its own.</summary>
        bool TryCapture(Gizmo gizmo, ISelectable owner, out object snapshot);

        /// <summary>The spoken outcome, or null when nothing changed since <paramref name="snapshot"/>.</summary>
        string DescribeChange(Gizmo gizmo, ISelectable owner, object snapshot);
    }
}
