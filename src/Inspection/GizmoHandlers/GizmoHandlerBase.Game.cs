using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Default no-op IGizmoHandler so a concrete handler only overrides the duty
    /// its original ladder branch actually had.
    /// </summary>
    public abstract class GizmoHandlerBase : IGizmoHandler
    {
        public virtual bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;
            return false;
        }

        public virtual bool TryDescribe(Gizmo gizmo, GizmoDescriptionFragments fragments)
        {
            return false;
        }

        public virtual bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            return false;
        }

        public virtual bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            return false;
        }

        public virtual bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            return false;
        }

        public virtual bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = null;
            return false;
        }

        public virtual bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            return false;
        }

        public virtual bool TryCaptureRightClickState(Gizmo gizmo, ISelectable owner, out object snapshot)
        {
            snapshot = null;
            return false;
        }

        public virtual string DescribeRightClickChange(Gizmo gizmo, ISelectable owner, object snapshot)
        {
            return null;
        }
    }
}
