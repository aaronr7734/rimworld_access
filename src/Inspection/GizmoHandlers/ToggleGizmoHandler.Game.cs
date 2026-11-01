using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Command_Toggle: toggles through vanilla's own ProcessInput, then speaks
    /// the refreshed state. A checkbox row must not throw the player out of the
    /// menu, so no epilogue runs: the G menu stays open and the list rebuilds in
    /// place with the cursor held on the row. One-shot activations (hotkey,
    /// inspect tree) close instead, so their announcement carries the label.
    ///
    /// Description-wise a toggle IS a checkbox, so this handler reports its live
    /// state as a <see cref="RimWorldAccess.Shell.CheckState"/> for the speech
    /// path (which renders it through the shell's shared checkbox vocabulary) and
    /// keeps the older pre-composed suffix for the float-menu MenuLabel rows,
    /// which are single strings with no element grammar of their own.
    /// </summary>
    internal sealed class ToggleGizmoHandler : GizmoHandlerBase
    {
        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            if (!(gizmo is Command_Toggle toggle))
            {
                runEpilogue = true;
                return false;
            }

            try
            {
                // Propagate to grouped gizmos first (vanilla order)
                GizmoNavigationState.PropagateToGroupedGizmos(gizmo, ctx.FakeEvent);
                gizmo.ProcessInput(ctx.FakeEvent);
                GizmoNavigationState.ProcessGroupInput(gizmo, ctx.FakeEvent);

                // One announcement per action: the REFRESHED state in the mod's
                // standard state-change grammar. Bare ("checked") while the row
                // stays focused; label-prefixed ("Draft, checked") when the menu
                // is closing behind a one-shot activation.
                // A right-click may change other rows or nothing, so its caller speaks the outcome.
                bool menuStaysOpen = GizmoNavigationState.MenuSupportsInPlaceRefresh;
                if (ctx.FakeEvent.button != 1)
                    GizmoNavigationState.SpeakGizmoStateChange(toggle, includeLabel: !menuStaysOpen);
                if (menuStaysOpen)
                    GizmoNavigationState.RefreshListKeepingCursor(toggle);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in Command_Toggle execution: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(ctx.GizmoLabel, ex.Message), SpeechPriority.High);
            }
            return true;
        }

        public override bool TryDescribe(Gizmo gizmo, GizmoDescriptionFragments fragments)
        {
            if (!(gizmo is Command_Toggle toggle))
                return false;

            bool isOn = toggle.isActive?.Invoke() ?? false;

            // The speech path reads this and presents the gizmo as a checkbox.
            fragments.ToggleState = isOn
                ? RimWorldAccess.Shell.CheckState.Checked
                : RimWorldAccess.Shell.CheckState.Unchecked;

            // The pre-composed suffix, whose wording differs per call site. The
            // float-menu rows splice it into their single-string label; the
            // speech path no longer reads it (ToggleState above is its input),
            // but both shapes stay on the carrier for any other surface that
            // needs a one-string row.
            if (fragments.Mode == GizmoDescriptionMode.Speech)
            {
                fragments.ToggleStateSuffix = (isOn
                    ? "RimWorldAccess.Inspection.Gizmo.StateOn"
                    : "RimWorldAccess.Inspection.Gizmo.StateOff").Translate();
            }
            else
            {
                // Checkbox vocabulary, never On/Off: a toggle IS a checkbox, and "off"
                // leaves the direction ambiguous ("is pause off, or is pausing off?").
                string state = (isOn
                    ? "RimWorldAccess.Shell.State.Checked"
                    : "RimWorldAccess.Shell.State.Unchecked").Translate();
                fragments.ToggleStateSuffix = "RimWorldAccess.Inspection.Gizmo.MenuStateSuffix".Translate(state);
            }
            return true;
        }
    }
}
