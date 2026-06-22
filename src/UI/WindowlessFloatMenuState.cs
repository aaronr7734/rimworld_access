using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a "virtual" float menu without actually displaying a window.
    /// Stores FloatMenuOptions and handles keyboard navigation, then executes the selected option.
    /// </summary>
    public static class WindowlessFloatMenuState
    {
        private static List<FloatMenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static bool givesColonistOrders = false;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static List<object> savedSelection = null;
        private static bool announceOnExecute = true;
        private static List<Def> currentInfoCardDefs = null;
        // Optional callback invoked once when the menu closes; the bool is true when the close was a
        // cancel (Escape) and false when an option was chosen. Used by the paint/plan color pickers
        // to announce the held placement entry, or cancel the tool on an initial-picker escape.
        private static System.Action<bool> onCloseCallback = null;

        // Cached reflection for FloatMenuOption.shownItem (private ThingDef field used as icon)
        private static readonly FieldInfo shownItemField = typeof(FloatMenuOption).GetField("shownItem", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Gets whether the windowless menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the currently selected index in the menu.
        /// </summary>
        public static int SelectedIndex => selectedIndex;

        /// <summary>
        /// Opens the windowless menu with the given options.
        /// </summary>
        /// <param name="playOpenSound">Whether to play FloatMenu_Open sound. Set to false when
        /// the vanilla FloatMenu constructor already played it (e.g. DialogInterceptionPatch).</param>
        public static void Open(List<FloatMenuOption> options, bool colonistOrders, int startIndex = 0, bool announceSelection = true, bool playOpenSound = true, List<Def> infoCardDefs = null, System.Action<bool> onClose = null)
        {
            currentOptions = options;
            currentInfoCardDefs = infoCardDefs;
            onCloseCallback = onClose;
            selectedIndex = System.Math.Max(0, System.Math.Min(startIndex, options.Count - 1));
            isActive = true;
            givesColonistOrders = colonistOrders;
            // The context-menu lesson is taught earlier (on first colonist selection), before the
            // player would think to press ]; here we only teach generic menu navigation, and only
            // for non-order menus, so it lands on a screen the lesson actually describes.
            if (!colonistOrders)
                DocsTeacher.Teach("RWA_MenuNavigation");
            announceOnExecute = announceSelection;
            typeahead.ClearSearch();

            // Save current selection - some FloatMenu actions expect specific objects to be selected
            // Find.Selector throws during chargen (no map), so guard with CurrentMap check
            savedSelection = Find.CurrentMap != null ? Find.Selector?.SelectedObjects?.ToList() : null;

            // Play menu open sound (matches vanilla FloatMenu constructor behavior)
            if (playOpenSound)
                SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();

            // Announce the first option
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Closes the windowless menu.
        /// </summary>
        public static void Close()
        {
            currentOptions = null;
            currentInfoCardDefs = null;
            selectedIndex = 0;
            isActive = false;
            onCloseCallback = null;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Closes the menu as a cancel (Escape), running the on-close callback. Selection-based
        /// closes run the callback from <see cref="ExecuteSelected"/> after the action instead.
        /// </summary>
        public static void Cancel()
        {
            System.Action<bool> cb = onCloseCallback;
            onCloseCallback = null;
            Close();
            cb?.Invoke(true);
        }

        /// <summary>
        /// Moves selection to the next option.
        /// </summary>
        public static void SelectNext()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, currentOptions.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Moves selection to the previous option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, currentOptions.Count);
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Executes the currently selected option.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            FloatMenuOption selectedOption = currentOptions[selectedIndex];
            bool shiftHeld = Event.current.shift;

            if (selectedOption.Disabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.LabelUnavailable".Loc(selectedOption.Label));
                return;
            }

            // Capture the world cursor BEFORE running the option. A world "Jump to..." option moves
            // the cursor to the destination during its action (via CameraJumperPatch), so we need the
            // pre-jump tile to compute and announce the move delta afterward.
            PlanetTile worldJumpOrigin = WorldNavigationState.IsActive
                ? WorldNavigationState.CurrentSelectedTile
                : PlanetTile.Invalid;

            // Restore saved selection before executing - some actions check Find.Selector.SelectedObjects
            // Find.Selector throws during chargen (no map), so guard with CurrentMap check
            if (savedSelection != null && Find.CurrentMap != null)
            {
                Find.Selector.ClearSelection();
                foreach (var obj in savedSelection)
                {
                    if (obj is ISelectable selectable)
                    {
                        Find.Selector.Select(selectable, playSound: false, forceDesignatorDeselect: false);
                    }
                }
            }

            // Capture the on-close callback before Close() clears it, so we can run it AFTER the
            // option's action (which may itself speak, e.g. "Color: Gray").
            System.Action<bool> closeCb = onCloseCallback;

            // Close the menu BEFORE executing the action
            // This allows the action to open a new menu if needed
            Close();

            // Play colonist ordered sound manually (since we bypass the visual FloatMenu,
            // Chosen()'s internal sound doesn't fire — same pattern as manual draft sounds)
            if (givesColonistOrders)
                SoundDefOf.ColonistOrdered.PlayOneShotOnCamera();

            // Pass colonistOrdering: false to Chosen() since we handle the sound ourselves.
            // Capture the option's localized label so any callback-based Targeter.BeginTargeting
            // call inside the action (e.g., "Force [pawn] to wear [apparel]") can announce it
            // as the second-phase prompt. Cleared in finally to prevent leaking to unrelated calls.
            PendingTargetingContext.Set(selectedOption.Label);
            try
            {
                selectedOption.Chosen(false, null);
            }
            finally
            {
                PendingTargetingContext.Clear();
            }

            // Announce selection - but skip if the action entered placement mode
            // (placement mode already announces its own message, e.g., "bed selected. Size: 1 by 2...")
            // Also skip if the caller suppressed the announcement (e.g., bill config submenus
            // where the callback already announces the updated state with position context)
            // Also skip if the action started a new targeting session — ItemTargetingState.Open
            // already announced the same label as part of "<label>. Navigate with arrow keys...".
            // Without this guard the user hears the option label twice back-to-back.
            bool targetingStarted = Find.Targeter?.IsTargeting == true;

            // If this menu was the world "Jump to..." menu (or any world action that relocated the
            // selection/camera), follow it with our navigation cursor and announce the move distance,
            // direction, and full tile info as if the user had arrowed there. No-op otherwise; when
            // it announces, suppress the generic "Selected" announcement to avoid speaking twice.
            bool worldJumpAnnounced = WorldNavigationState.FollowExternalJumpAndAnnounce(worldJumpOrigin);

            if (!ArchitectState.IsInPlacementMode && announceOnExecute && !targetingStarted && !worldJumpAnnounced)
            {
                if (shiftHeld && givesColonistOrders)
                    TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.SelectedQueued".Loc(selectedOption.Label, "Queued".Translate()));
                else
                    TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.Selected".Loc(selectedOption.Label));
            }

            // Run the on-close callback last (after the option's own announcement).
            closeCb?.Invoke(false);
        }

        /// <summary>
        /// Jumps to the first option in the menu.
        /// </summary>
        public static void JumpToFirst()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            // During an active search, Home goes to the first match and keeps the search.
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetFirstMatch();
                AnnounceCurrentSelection();
                return;
            }

            selectedIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Jumps to the last option in the menu.
        /// </summary>
        public static void JumpToLast()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedIndex = typeahead.GetLastMatch();
                AnnounceCurrentSelection();
                return;
            }

            selectedIndex = MenuHelper.JumpToLast(currentOptions.Count);
            typeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Handles typeahead character input for the float menu.
        /// Called from UnifiedKeyboardPatch to process alphanumeric characters.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        /// <summary>
        /// Handles backspace key for typeahead search.
        /// Called from UnifiedKeyboardPatch.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetItemLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Gets whether typeahead search has no matches.
        /// </summary>
        public static bool HasNoMatches => typeahead.HasNoMatches;

        /// <summary>
        /// Clears the typeahead search and announces the action.
        /// Returns true if there was an active search to clear.
        /// </summary>
        public static bool ClearTypeaheadSearch()
        {
            if (!typeahead.HasActiveSearch)
                return false;

            typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentSelection();
            return true;
        }

        /// <summary>
        /// Handles keyboard input for the menu, including typeahead search.
        /// </summary>
        /// <returns>True if input was handled, false otherwise.</returns>
        public static bool HandleInput()
        {
            if (!isActive || currentOptions == null || currentOptions.Count == 0)
                return false;

            if (Event.current.type != EventType.KeyDown)
                return false;

            KeyCode key = Event.current.keyCode;

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                JumpToFirst();
                Event.current.Use();
                return true;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                JumpToLast();
                Event.current.Use();
                return true;
            }

            // Handle Escape - clear search FIRST, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    Event.current.Use();
                    return true;
                }
                // Let the caller handle normal escape (close menu)
                return false;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetItemLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                Event.current.Use();
                return true;
            }

            // Handle * key - consume to prevent passthrough
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isStar = key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8);
            if (isStar)
            {
                Event.current.Use();
                return true;
            }

            // Handle Up arrow - navigate with search awareness
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    SelectPrevious();
                }
                Event.current.Use();
                return true;
            }

            // Handle Down arrow - navigate with search awareness
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    SelectNext();
                }
                Event.current.Use();
                return true;
            }

            // Handle Enter - execute selected
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ExecuteSelected();
                Event.current.Use();
                return true;
            }

            // Handle Alt+I - open info card for the selected option's associated Def
            if (KeyboardHelper.IsAltHeld && key == KeyCode.I)
            {
                TryOpenInfoCardForSelected();
                Event.current.Use();
                return true;
            }

            // Handle typeahead characters
            // Skip if Alt is held - Alt+key combos are shortcuts, not search input
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !KeyboardHelper.IsAltHeld)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the list of labels for all current options.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            var labels = new List<string>();
            if (currentOptions != null)
            {
                foreach (var option in currentOptions)
                {
                    labels.Add(option.Label ?? "");
                }
            }
            return labels;
        }

        /// <summary>
        /// Announces the current selection without search context.
        /// Includes tooltip text if available.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            var option = currentOptions[selectedIndex];
            string optionText = option.Label;
            if (option.Disabled)
            {
                optionText += "RimWorldAccess.UI.FloatMenu.UnavailableSuffix".Translate();
            }

            // Get tooltip text if available
            string tooltipText = GetTooltipText(option);
            string position = MenuHelper.FormatPosition(selectedIndex, currentOptions.Count);

            // Build announcement - don't add period before tooltip since tooltips usually have their own
            if (!string.IsNullOrEmpty(tooltipText))
            {
                TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.OptionTooltip".Loc(optionText, tooltipText, position));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.UI.Item.WithPosition".Loc(optionText, position));
            }
        }

        /// <summary>
        /// Gets the tooltip text for a FloatMenuOption, if available.
        /// </summary>
        private static string GetTooltipText(FloatMenuOption option)
        {
            if (option.tooltip == null || !option.tooltip.HasValue)
                return null;

            var tip = option.tooltip.Value;

            // Try textGetter first (dynamic tooltip), then static text
            string text = null;
            if (tip.textGetter != null)
            {
                try
                {
                    text = tip.textGetter();
                }
                catch
                {
                    // Ignore errors from dynamic tooltip getters
                }
            }

            if (string.IsNullOrEmpty(text))
            {
                text = tip.text;
            }

            return string.IsNullOrEmpty(text) ? null : text.Trim();
        }

        /// <summary>
        /// Tries to open an info card for the currently selected float menu option.
        /// Extracts a Def from the option's shownItem (private ThingDef) or iconThing fields.
        /// </summary>
        public static void TryOpenInfoCardForSelected()
        {
            if (currentOptions == null || selectedIndex < 0 || selectedIndex >= currentOptions.Count)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }

            var option = currentOptions[selectedIndex];

            // Check caller-provided info card defs first (e.g. xenotype selector)
            if (currentInfoCardDefs != null && selectedIndex < currentInfoCardDefs.Count &&
                currentInfoCardDefs[selectedIndex] != null)
            {
                InfoCardState.OpenInfoCardForDef(currentInfoCardDefs[selectedIndex]);
                return;
            }

            // Try shownItem first (private ThingDef used as icon - common for recipes/bills)
            Def def = null;
            if (shownItemField != null)
            {
                def = shownItemField.GetValue(option) as ThingDef;
            }

            // Fall back to iconThing's def
            if (def == null && option.iconThing?.def != null)
            {
                def = option.iconThing.def;
            }

            // Fall back to revalidateClickTarget's def
            if (def == null && option.revalidateClickTarget?.def != null)
            {
                def = option.revalidateClickTarget.def;
            }

            InfoCardState.TryOpenInfoCardForDef(def);
        }

        /// <summary>
        /// Announces the current selection with search context if applicable.
        /// Includes tooltip text if available.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            var option = currentOptions[selectedIndex];
            string optionText = option.Label;
            if (option.Disabled)
            {
                optionText += "RimWorldAccess.UI.FloatMenu.UnavailableSuffix".Translate();
            }

            // Get tooltip text if available
            string tooltipText = GetTooltipText(option);

            if (typeahead.HasActiveSearch)
            {
                if (!string.IsNullOrEmpty(tooltipText))
                {
                    TolkHelper.SpeakData(
                        "RimWorldAccess.UI.FloatMenu.OptionTooltipSearch".Translate(optionText, tooltipText)
                        + typeahead.BuildSearchContextSuffix());
                }
                else
                {
                    TolkHelper.SpeakData(typeahead.BuildItemAnnouncement(optionText));
                }
            }
            else
            {
                string position = MenuHelper.FormatPosition(selectedIndex, currentOptions.Count);
                if (!string.IsNullOrEmpty(tooltipText))
                {
                    TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.OptionTooltip".Loc(optionText, tooltipText, position));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.UI.Item.WithPosition".Loc(optionText, position));
                }
            }
        }
    }
}
