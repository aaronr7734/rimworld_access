using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One implementation per branch of the legacy GizmoNavigationState execution
    /// and announcement ladders (see GizmoHandlerRegistry for resolution order). A
    /// handler may implement only the duty its original branch had — most ported
    /// branches only ever executed and never contributed an announcement fragment.
    /// </summary>
    public interface IGizmoHandler
    {
        /// <summary>
        /// Attempts to execute the gizmo. Returns false to let the registry try the
        /// next candidate — only meaningful for handlers with a type-specific
        /// precondition that can fail (e.g. MechanitorControlGroupGizmo when its
        /// control group cannot be resolved, which the original ladder let fall
        /// through all the way to the generic Command fallback).
        /// <paramref name="runEpilogue"/> reports whether
        /// GizmoNavigationState's shared post-execution steps (clear
        /// multi-selection, follow a planet-layer switch, close-and-announce)
        /// should run, mirroring which branches fell through to that shared code
        /// in the original ladder versus returning early themselves.
        /// </summary>
        bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue);

        /// <summary>Attempts to contribute type-specific announcement fragments; false leaves <paramref name="fragments"/> untouched.</summary>
        bool TryDescribe(Gizmo gizmo, GizmoDescriptionFragments fragments);

        /// <summary>
        /// Attempts to produce the gizmo's spoken title (the analog of what a
        /// sighted player reads on or above the gizmo). Returns false to fall
        /// back to the generic label resolution (Command.LabelCap, cleaned-up
        /// type name). Called with the gizmo's owner selected, so lazy
        /// selection-dependent properties are safe to read.
        /// </summary>
        bool TryGetLabel(Gizmo gizmo, out string label);

        /// <summary>
        /// Attempts to produce the gizmo's current status readout (the analog of
        /// a bar fill, count, or meter a sighted player sees on the gizmo, e.g.
        /// "5 / 12" or "75%"). Returns false when the type has no status facet.
        /// </summary>
        bool TryGetStatus(Gizmo gizmo, out string status);

        /// <summary>The gizmo's long description (the hover tooltip's analog); false falls back to Command.Desc and friends.</summary>
        bool TryGetDescription(Gizmo gizmo, out string description);

        /// <summary>
        /// Attempts to produce an adjustment adapter for the gizmo's slider.
        /// Claim only when the slider is adjustable right now — a resolved
        /// adapter doubles as the "is adjustable" predicate. Returns false for
        /// types without an adjustable value (or when adjustment is currently
        /// unavailable, e.g. a non-player-controlled pawn's psyfocus).
        /// </summary>
        bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter);

        /// <summary>
        /// Contributes keyboard-accessible menu options beyond the gizmo's own
        /// RightClickFloatMenuOptions — interactions vanilla exposes only through
        /// hidden hover widgets (overlay checkboxes, right-click-only ProcessInput
        /// branches). Append to <paramref name="options"/> and return whether
        /// anything was added. Unlike the single-value facets, the registry offers
        /// this to EVERY handler along the type chain and accumulates.
        /// </summary>
        bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options);

        /// <summary>Snapshots state a replayed right-click may change unseen (overlay icons only); false when the gizmo is not this handler's. Offered along the whole chain.</summary>
        bool TryCaptureRightClickState(Gizmo gizmo, ISelectable owner, out object snapshot);

        /// <summary>The spoken outcome of the right-click, or null when nothing changed since <paramref name="snapshot"/>.</summary>
        string DescribeRightClickChange(Gizmo gizmo, ISelectable owner, object snapshot);
    }
}
