using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// A float menu with no window of its own: holds the FloatMenuOptions and executes the chosen
    /// one. Keyboard navigation belongs to <see cref="Shell.FloatMenuOverlayScope"/>.
    /// </summary>
    public static class WindowlessFloatMenuState
    {
        private static List<FloatMenuOption> currentOptions = null;
        private static int selectedIndex = 0;
        private static bool isActive = false;
        private static bool givesColonistOrders = false;
        private static List<object> savedSelection = null;
        private static bool announceOnExecute = true;
        private static List<Def> currentInfoCardDefs = null;
        // Folded into the FIRST selection announcement only, then cleared, so title and first option
        // are one utterance. A caller speaking the title separately produces two, and the reader's
        // pause at the seam reads as a stray period that SpeechSanitizer cannot remove, since it only
        // ever sees one utterance at a time.
        private static string pendingTitle = null;
        // Pure display data for the visual twin's title strip, never spoken: pendingTitle is nulled
        // after the first announcement, so it cannot serve here.
        private static string displayTitle = null;
        // Invoked once when the menu closes, true for a cancel and false when an option was chosen.
        // NOT invoked when the chosen option opened another windowless menu: that option is a
        // drill-in whose flow has not finished, and the deepest menu's callback owns the return.
        // Without this rule a two-level flow would re-open the grandparent on top of the submenu.
        private static System.Action<bool> onCloseCallback = null;

        private static int batchDepth;
        private static bool batchAnnouncePending;

        public static bool IsActive => isActive;

        internal static bool IsBatching => batchDepth > 0;

        // Defers opening announcements to EndBatch: grouped gizmos may open one menu each, and only the survivor is spoken.
        internal static void BeginBatch()
        {
            if (batchDepth++ == 0)
                batchAnnouncePending = false;
        }

        /// <summary>The outermost end announces the surviving menu. Returns whether a menu opened inside the batch.</summary>
        internal static bool EndBatch()
        {
            if (batchDepth == 0)
                return false;
            bool opened = batchAnnouncePending;
            if (--batchDepth == 0 && opened && isActive)
                AnnounceCurrentSelection();
            return opened;
        }

        /// <summary>True only while a chosen option's delegate runs, arming DialogInterceptionPatch to redirect a vanilla FloatMenu it opens into another windowless one.</summary>
        public static bool IsExecutingOption { get; private set; }

        public static int SelectedIndex => selectedIndex;

        /// <summary>
        /// The live option list for the visual twin: the SAME instance the keyboard navigates, so row
        /// order and announced order can never diverge.
        /// </summary>
        internal static List<FloatMenuOption> CurrentOptions => currentOptions;

        /// <summary>Whether these options are colonist orders, for the twin's DoGUI calls.</summary>
        internal static bool GivesColonistOrders => givesColonistOrders;

        /// <summary>The menu's title, for the visual twin's title strip only.</summary>
        internal static string DisplayTitle => displayTitle;

        /// <summary>
        /// Opens the windowless menu.
        /// </summary>
        /// <param name="playOpenSound">False when the vanilla FloatMenu constructor already played
        /// the open sound.</param>
        /// <param name="announceFirst">False when the caller already spoke the reason this menu
        /// (re)opened, so a second announcement of the unchanged row would break
        /// one-announcement-per-action.</param>
        public static void Open(List<FloatMenuOption> options, bool colonistOrders, int startIndex = 0, bool announceSelection = true, bool playOpenSound = true, List<Def> infoCardDefs = null, System.Action<bool> onClose = null, string titleText = null, bool announceFirst = true)
        {
            currentOptions = options;
            currentInfoCardDefs = infoCardDefs;
            onCloseCallback = onClose;
            pendingTitle = string.IsNullOrEmpty(titleText) ? null : titleText.Trim();
            displayTitle = pendingTitle;
            selectedIndex = System.Math.Max(0, System.Math.Min(startIndex, options.Count - 1));
            isActive = true;
            givesColonistOrders = colonistOrders;
            // Order menus get the context-menu lesson earlier, on first colonist selection, so only
            // non-order menus teach generic navigation here.
            if (!colonistOrders)
                DocsTeacher.Teach("RWA_MenuNavigation");
            announceOnExecute = announceSelection;

            // Some FloatMenu actions expect specific objects to be selected. Find.Selector throws
            // during chargen, where there is no map.
            savedSelection = Find.CurrentMap != null ? Find.Selector?.SelectedObjects?.ToList() : null;

            if (playOpenSound)
                SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();

            if (!announceFirst)
                return;
            if (batchDepth > 0)
                batchAnnouncePending = true;
            else
                AnnounceCurrentSelection();
        }

        /// <summary>
        /// A titled, non-colonist menu that never re-announces the picked option, since the caller
        /// speaks its own outcome. An empty list rejects instead of opening, speaking
        /// <paramref name="emptyAnnouncementKey"/> when one is given.
        /// </summary>
        public static void OpenTitled(string title, List<FloatMenuOption> options, string emptyAnnouncementKey = null)
        {
            if (options == null || options.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                if (!string.IsNullOrEmpty(emptyAnnouncementKey))
                {
                    TolkHelper.SpeakData(emptyAnnouncementKey.Translate());
                }
                return;
            }
            Open(options, colonistOrders: false, announceSelection: false, titleText: title);
        }

        public static void Close()
        {
            currentOptions = null;
            currentInfoCardDefs = null;
            selectedIndex = 0;
            isActive = false;
            onCloseCallback = null;
            pendingTitle = null;
            displayTitle = null;
        }

        /// <summary>
        /// Closes as a cancel, running the on-close callback; a selection-based close runs it from
        /// <see cref="ExecuteSelected"/> after the action instead.
        /// </summary>
        public static void Cancel()
        {
            System.Action<bool> cb = onCloseCallback;
            onCloseCallback = null;
            Close();
            cb?.Invoke(true);
        }

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

            // A checkbox row toggles in place: the menu stays open with the cursor held on
            // the row, and the refreshed state is the whole announcement.
            System.Func<bool> checkState = FloatMenuOptionFacets.CheckStateOf(selectedOption);
            if (checkState != null)
            {
                selectedOption.action?.Invoke();
                // A mod's own check row plays its own toggle sound from its action.
                if (selectedOption is CheckboxFloatMenuOption)
                {
                    (checkState() ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff)
                        .PlayOneShotOnCamera();
                }
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                    DescribeOption(selectedOption), TranslatedShellVocabulary.Instance));
                return;
            }

            // A world "Jump to..." option moves the cursor during its action, so the pre-jump tile
            // must be captured here to compute the move delta afterwards.
            PlanetTile worldJumpOrigin = WorldNavigationState.IsActive
                ? WorldNavigationState.CurrentSelectedTile
                : PlanetTile.Invalid;

            // Some actions read Find.Selector.SelectedObjects, which throws during chargen.
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

            // Captured before Close clears it, to run AFTER the option's action, which may speak.
            System.Action<bool> closeCb = onCloseCallback;

            // Close before executing, so the action is free to open a new menu.
            Close();

            // Bypassing the visual FloatMenu means Chosen's own sound never fires.
            if (givesColonistOrders)
                SoundDefOf.ColonistOrdered.PlayOneShotOnCamera();

            // Chosen takes colonistOrdering: false because the sound is handled above. The label is
            // published so a Targeter.BeginTargeting call inside the action can announce it as the
            // second-phase prompt; the finally clears it so it never leaks to an unrelated call.
            PendingTargetingContext.Set(selectedOption.Label);
            IsExecutingOption = true;
            try
            {
                selectedOption.Chosen(false, null);
            }
            finally
            {
                IsExecutingOption = false;
                PendingTargetingContext.Clear();
            }

            // A started targeting session already announced this same label, so announcing again
            // would say it twice. Find.Targeter throws without a map (chargen, the main-menu
            // ideoligion builder), and targeting can never be active there, so the CurrentMap gate
            // is both necessary and correct.
            bool targetingStarted = Find.CurrentMap != null && ExternalMapTargeting.MapTargetingActive;

            // A world action that relocated the selection or camera is followed with the navigation
            // cursor and announced as if the user had arrowed there; a no-op otherwise.
            bool worldJumpAnnounced = WorldNavigationState.FollowExternalJumpAndAnnounce(worldJumpOrigin);

            // Close cleared isActive above, so it being true again means the action drilled into
            // another windowless menu, whose Open already announced its first item; the parent
            // option's label would otherwise leak in around it.
            bool openedSubMenu = isActive;

            if (!ArchitectState.IsInPlacementMode && announceOnExecute && !targetingStarted && !worldJumpAnnounced && !openedSubMenu)
            {
                if (shiftHeld && givesColonistOrders)
                    TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.SelectedQueued".Loc(selectedOption.Label, "Queued".Translate()));
                else
                    TolkHelper.Speak("RimWorldAccess.UI.FloatMenu.Selected".Loc(selectedOption.Label));
            }

            // Last, after the option's own announcement, and never for a drill-in.
            if (!openedSubMenu)
            {
                closeCb?.Invoke(false);
            }
        }

        /// <summary>
        /// Mouse activation from the visual twin: moves the selection to the clicked row and runs the
        /// SAME ExecuteSelected the Enter claim runs, so there is one activation path. Never call
        /// FloatMenuOption.Chosen directly from the twin — that skips the saved-selection restore,
        /// PendingTargetingContext, IsExecutingOption and the on-close callback.
        /// </summary>
        internal static void ActivateIndex(int index)
        {
            if (!isActive || currentOptions == null) return;
            if (index < 0 || index >= currentOptions.Count) return;
            selectedIndex = index;
            ExecuteSelected();
        }

        // This state routes no keys: FloatMenuOverlayScope owns the cursor, navigation, search and
        // every landing announcement. What stays here is the menu's data, its opening announcement
        // and its dismiss/choose mutations.

        /// <summary>
        /// Records where the keyboard cursor rests, without announcing, keeping the datum the visual
        /// twin's focus ring, its auto-scroll and <see cref="ExecuteSelected"/> read in step with it.
        /// </summary>
        internal static void PublishSelectedIndex(int index)
        {
            if (currentOptions == null || index < 0 || index >= currentOptions.Count)
                return;
            selectedIndex = index;
        }

        private static void AnnounceCurrentSelection()
        {
            if (currentOptions == null || currentOptions.Count == 0)
                return;

            if (selectedIndex < 0 || selectedIndex >= currentOptions.Count)
                return;

            string body = ComposeOptionFocus(currentOptions[selectedIndex], selectedIndex, currentOptions.Count);

            // The first announcement folds the title in as one utterance so the seam is sanitized.
            if (!string.IsNullOrEmpty(pendingTitle))
            {
                string title = pendingTitle;
                pendingTitle = null;
                TolkHelper.SpeakData(title + ". " + body);
            }
            else
            {
                TolkHelper.SpeakData(body);
            }
        }

        /// <summary>
        /// One option as an <see cref="ElementRole.MenuItem"/> row: label, the standard disabled
        /// state word when vanilla drew it unclickable, and its tooltip (or a registered
        /// <see cref="FloatMenuOptionDetails"/> source's text) in Extras. Position is left
        /// to the caller. Shared by this state's opening announcement and
        /// <see cref="Shell.FloatMenuOverlayScope"/>'s row describe, so the two cannot drift.
        /// </summary>
        internal static ElementDescription DescribeOption(FloatMenuOption option)
        {
            var d = new ElementDescription();
            d.Label = option.Label;
            System.Func<bool> checkState = FloatMenuOptionFacets.CheckStateOf(option);
            if (checkState != null)
            {
                d.Role = ElementRole.Checkbox;
                d.Check = checkState() ? CheckState.Checked : CheckState.Unchecked;
            }
            else
            {
                d.Role = ElementRole.MenuItem;
            }
            d.Disabled = option.Disabled;
            d.Extras = GetTooltipText(option) ?? FloatMenuOptionDetails.ExtrasFor(option);
            return d;
        }

        /// <summary>The opening announcement's body: <see cref="DescribeOption"/> plus this row's position, through the shared composer.</summary>
        private static string ComposeOptionFocus(FloatMenuOption option, int index, int count)
        {
            ElementDescription d = DescribeOption(option);
            d.PositionIndex = index + 1;
            d.PositionCount = count;

            var options = default(ComposeOptions);
            options.IncludePosition = TextDialogShared.PositionPartEnabled;
            return AnnouncementComposer.ComposeFocus(d, TranslatedShellVocabulary.Instance, options);
        }

        /// <summary>The option's tooltip text, preferring its dynamic getter; null when it has none.</summary>
        private static string GetTooltipText(FloatMenuOption option)
        {
            if (option.tooltip == null || !option.tooltip.HasValue)
                return FloatMenuOptionFacets.TooltipOf(option);

            var tip = option.tooltip.Value;

            string text = null;
            if (tip.textGetter != null)
            {
                try
                {
                    text = tip.textGetter();
                }
                catch
                {
                    // A dynamic getter may throw; the static text below is the fallback.
                }
            }

            if (string.IsNullOrEmpty(text))
            {
                text = tip.text;
            }

            return string.IsNullOrEmpty(text) ? null : text.Trim();
        }

        /// <summary>
        /// Opens an info card for the selected option, resolving its Def from the caller-supplied
        /// list, then the option's private shownItem, iconThing and revalidateClickTarget, then a
        /// registered FloatMenuOptionDetails source.
        /// </summary>
        public static void TryOpenInfoCardForSelected()
        {
            if (currentOptions == null || selectedIndex < 0 || selectedIndex >= currentOptions.Count)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }

            var option = currentOptions[selectedIndex];

            if (currentInfoCardDefs != null && selectedIndex < currentInfoCardDefs.Count &&
                currentInfoCardDefs[selectedIndex] != null)
            {
                InfoCardState.OpenInfoCardForDef(currentInfoCardDefs[selectedIndex]);
                return;
            }

            Def def = BuildingReflection.GetShownItem(option);

            if (def == null && option.iconThing?.def != null)
            {
                def = option.iconThing.def;
            }

            if (def == null && option.revalidateClickTarget?.def != null)
            {
                def = option.revalidateClickTarget.def;
            }

            if (def == null)
            {
                def = FloatMenuOptionDetails.InfoDefFor(option);
            }

            InfoCardState.TryOpenInfoCardForDef(def);
        }
    }
}
