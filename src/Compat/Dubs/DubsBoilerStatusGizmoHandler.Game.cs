using System;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// DubsBadHygiene.Gizmo_BoilerStatus draws boiler.Props.GizmoLabel over a fill bar labelled
    /// "PowerMode / PowerModes". It returns GizmoState.Clear and overrides no ProcessInput, so it
    /// is presented as a read-only status readout.
    /// </summary>
    internal sealed class DubsBoilerStatusGizmoHandler : GizmoHandlerBase
    {
        private readonly FieldInfo boiler;
        private readonly PropertyInfo props;
        private readonly FieldInfo powerMode;
        private readonly FieldInfo gizmoLabel;
        private readonly FieldInfo powerModes;
        private readonly bool ready;

        internal DubsBoilerStatusGizmoHandler(Type gizmoType)
        {
            var surface = new ReflectionSurface("DubsBoilerStatusGizmoHandler");
            surface.Supplied("DubsBadHygiene.Gizmo_BoilerStatus", gizmoType);
            Type compType = surface.Type("DubsBadHygiene.CompBoiler");
            Type propsType = surface.Type("DubsBadHygiene.CompProperties_CompBoiler");
            boiler = surface.Field(gizmoType, "boiler");
            props = surface.Property(compType, "Props");
            powerMode = surface.Field(compType, "PowerMode");
            gizmoLabel = surface.Field(propsType, "GizmoLabel");
            powerModes = surface.Field(propsType, "PowerModes");
            ready = surface.Ready;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = ready ? Guarded.FieldOf<string>(gizmoLabel, Props(gizmo), "DubsBoilerStatusGizmoHandler label") : null;
            return !string.IsNullOrEmpty(label);
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready)
            {
                return false;
            }
            object comp = boiler.GetValue(gizmo);
            object compProps = Props(gizmo);
            if (comp == null || compProps == null)
            {
                return false;
            }
            // The gizmo's own unlocalized "F0 / F0" readout.
            status = ((int)powerMode.GetValue(comp)).ToString("F0") + " / " + ((int)powerModes.GetValue(compProps)).ToString("F0");
            return true;
        }

        private object Props(Gizmo gizmo)
        {
            object comp = boiler.GetValue(gizmo);
            return comp == null ? null : Guarded.Get<object>(props.GetGetMethod(), comp, "DubsBoilerStatusGizmoHandler props");
        }
    }
}
