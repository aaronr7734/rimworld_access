using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared router for the builder's windowless overlay editors (precept selection, typed
    /// precepts, deities). These overlays live on top of whatever screen launched them — the
    /// Custom-creation hub (Page_ConfigureIdeo) or the in-game reform dialog (Dialog_ReformIdeo)
    /// — so both host patches funnel input through here instead of duplicating the routing.
    ///
    /// Overlays that open a windowless float-menu sub-picker (typed-precept "Add", deity
    /// actions) need their tree refreshed when the player returns from that menu; this router
    /// tracks the float-menu open/close transition and triggers the refresh. (Precept selection
    /// refreshes via its own onChanged callback, so it isn't refreshed here.)
    /// </summary>
    public static class IdeoBuilderOverlays
    {
        private static bool floatMenuWasOpen;

        public static bool AnyActive =>
            IdeoPreceptSelectionState.IsActive
            || IdeoTypedPreceptState.IsActive
            || IdeoDeityListState.IsActive
            || IdeoAppearanceEditState.IsActive;

        public static void NoteFloatMenuOpen()
        {
            floatMenuWasOpen = true;
        }

        public static void RefreshIfReturnedFromFloatMenu()
        {
            if (!floatMenuWasOpen) return;
            floatMenuWasOpen = false;
            IdeoTypedPreceptState.NotifyPreceptAdded();
            IdeoDeityListState.NotifyReturnedFromPicker();
            IdeoAppearanceEditState.NotifyReturnedFromPicker();
        }

        public static bool RouteKeyDown(Event ev)
        {
            if (IdeoPreceptSelectionState.IsActive) return IdeoPreceptSelectionState.HandleInput(ev);
            if (IdeoTypedPreceptState.IsActive) return IdeoTypedPreceptState.HandleInput(ev);
            if (IdeoDeityListState.IsActive) return IdeoDeityListState.HandleInput(ev);
            if (IdeoAppearanceEditState.IsActive) return IdeoAppearanceEditState.HandleInput(ev);
            return false;
        }
    }
}
