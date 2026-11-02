using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The describers GizmoNavigationState consults after replaying a right-click that opened no
    /// menu and flipped no toggle.
    /// </summary>
    public static class RightClickOutcomeRegistry
    {
        private static readonly List<IRightClickOutcomeDescriber> describers = new List<IRightClickOutcomeDescriber>();

        public static void Register(IRightClickOutcomeDescriber describer)
        {
            if (describer != null && !describers.Contains(describer))
                describers.Add(describer);
        }

        /// <summary>Captures every describer that claims the gizmo, in registration order.</summary>
        internal static List<KeyValuePair<IRightClickOutcomeDescriber, object>> Capture(Gizmo gizmo, ISelectable owner)
        {
            var captured = new List<KeyValuePair<IRightClickOutcomeDescriber, object>>();
            foreach (IRightClickOutcomeDescriber describer in describers)
            {
                try
                {
                    if (describer.TryCapture(gizmo, owner, out object snapshot))
                        captured.Add(new KeyValuePair<IRightClickOutcomeDescriber, object>(describer, snapshot));
                }
                catch (System.Exception ex)
                {
                    ModLogger.LimitedError("RightClickOutcomeRegistry.Capture", ex);
                }
            }
            return captured;
        }

        internal static void AppendChanges(List<KeyValuePair<IRightClickOutcomeDescriber, object>> captured,
            Gizmo gizmo, ISelectable owner, List<string> changes)
        {
            foreach (KeyValuePair<IRightClickOutcomeDescriber, object> entry in captured)
            {
                try
                {
                    string change = entry.Key.DescribeChange(gizmo, owner, entry.Value);
                    if (!string.IsNullOrEmpty(change))
                        changes.Add(change);
                }
                catch (System.Exception ex)
                {
                    ModLogger.LimitedError("RightClickOutcomeRegistry.DescribeChange", ex);
                }
            }
        }
    }
}
