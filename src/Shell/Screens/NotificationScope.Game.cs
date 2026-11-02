using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the notification menu (messages, letters and alerts).
    /// The mod owns no window here — the surface is the windowless
    /// <see cref="NotificationMenuState"/> — so the scope rides the focus stack through
    /// <see cref="NotificationScopeMirror"/> instead of a WindowStack mirror. The scope owns
    /// the list cursor, the row typeahead, the detail-view helper and every announcement;
    /// the state owns only the letter/message model and its domain actions.
    ///
    /// One flat content region, never a table. It deliberately does NOT opt into the shared
    /// cross-region typeahead engine (<see cref="ScreenScope.EnableTypeahead"/> stays false):
    /// that engine announces through the generic composer, which cannot reproduce this
    /// screen's exact wording. It keeps its own <see cref="listTypeahead"/> and overrides
    /// <see cref="CharSink"/>/<see cref="HandleChar"/>, and overrides
    /// <see cref="MoveItem"/>/<see cref="MoveItemEdge"/> for the list-vs-detail split and the
    /// search suffix. Enter is NOT re-claimed — the base's own Activate claim has no gate, so
    /// a second claim could never be reached; <see cref="ActivateContentItem"/> is the
    /// extension point.
    ///
    /// Left/Right and Escape/Backspace are claimed directly here because the base's claims
    /// for those ids are gated OFF for this screen and never fire, leaving these later
    /// registrations as the ones the dispatcher reaches. Left/Right route to the
    /// button-navigation routers unconditionally, list view included:
    /// TwoLevelMenuHelper.ValidateButtonNavigationState speaks the right "open the item
    /// first" / "no buttons here" message there, so this is behavior to preserve, not a
    /// guard to add. No UI sounds on any navigation or activation path — deliberate silence.
    ///
    /// <b>Info-card round trip (Alt+I on a hyperlink target).</b> A letter's hyperlink action
    /// can open a real <c>Dialog_InfoCard</c> WITHOUT closing this menu first, so the scope
    /// tracks <see cref="NotificationMenuState"/>'s open generation
    /// (<see cref="lastSeenOpenGeneration"/>): a pop/re-push caused by an info card, as
    /// opposed to a fresh open, leaves the list cursor, search and detail-view position
    /// exactly where the user left them. Reading a long letter, glancing at an item card and
    /// resuming the SAME letter is this screen's core use case.
    /// </summary>
    public sealed class NotificationScope : ScreenScope
    {
        private readonly TypeaheadSearchHelper listTypeahead = new TypeaheadSearchHelper();
        private readonly TwoLevelMenuHelper detailHelper;

        private bool announcedOpen;
        private int lastSeenOpenGeneration = -1;

        public NotificationScope()
        {
            detailHelper = new TwoLevelMenuHelper(
                getContentLineCount: () => {
                    NotificationMenuState.NotificationItem item = CurrentItem();
                    return item != null ? item.ExplanationLines.Length : 0;
                },
                populateButtons: (buttons) => {
                    NotificationMenuState.PopulateButtons(CurrentItem(), buttons);
                },
                getHeaderAnnouncement: () => {
                    NotificationMenuState.NotificationItem item = CurrentItem();
                    return item != null
                        ? "RimWorldAccess.Notifications.List.HeaderInDetail".Translate(
                            NotificationMenuState.GetTypeLabel(item.Type), item.Label).ToString()
                        : "";
                },
                getContentLineAnnouncement: (lineIndex) => {
                    NotificationMenuState.NotificationItem item = CurrentItem();
                    if (item == null)
                        return "";
                    string[] lines = item.ExplanationLines;
                    return lineIndex >= 0 && lineIndex < lines.Length ? lines[lineIndex] : "";
                },
                endOfItemMessage: "RimWorldAccess.Notifications.Detail.EndOfLetter".Translate(),
                startOfItemMessage: "RimWorldAccess.Notifications.Detail.StartOfLetter".Translate()
            );

            Claim(SharedMenuGrammar.PreviousHorizontal, delegate { detailHelper.SelectPreviousButton(); });
            Claim(SharedMenuGrammar.NextHorizontal, delegate { detailHelper.SelectNextButton(); });

            Claim(SharedMenuGrammar.Cancel, OnCancel);

            // Backspace (search-clear) only applies in list view — detail view has no search to clear.
            Claim(SharedMenuGrammar.SearchBackspace, delegate { HandleBackspace(); }, when: ListViewMode);

            Claim("notifications.delete", delegate { DeleteSelected(); });
        }

        internal const string ScreenName = "notifications";

        public override string Name
        {
            get { return ScreenName; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Vanilla's Assign/Animals bottom-buttons Region concept doesn't apply — the per-item detail buttons are content, not window buttons.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>Own hand-built row search (see class remarks) instead of the shared cross-region typeahead engine.</summary>
        public override ICharSink CharSink
        {
            get { return this; }
        }

        private bool ListViewMode()
        {
            return !detailHelper.IsInDetailView;
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>No vanilla MainButtonDef backs this overlay (it isn't a real tab) — dedicated key.</summary>
        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Notifications.Menu.RegionName".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            return items != null ? items.Count : 0;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            if (items != null && index >= 0 && index < items.Count)
            {
                d.Label = items[index].Label;
            }
            return d;
        }

        /// <summary>
        /// Enter: not in detail view enters it; in detail view on a button activates it; in
        /// detail view off a button is a no-op, leaving the arrows to keep navigating.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (detailHelper.IsInDetailView)
            {
                if (detailHelper.IsInButtonsSection)
                {
                    ActivateButtonInternal();
                }
                return;
            }
            EnterDetailViewInternal();
        }

        public override void OnPush()
        {
            base.OnPush();
            LetterButtonRingPatch.CurrentProvider = FocusedLetter;

            // Only reset on a genuinely fresh Open(): the generation bumps there, never on
            // Refresh(), so an info-card round trip keeps the user's position.
            int generation = NotificationMenuState.OpenGeneration;
            if (generation == lastSeenOpenGeneration)
                return;
            lastSeenOpenGeneration = generation;

            listTypeahead.ClearSearch();
            detailHelper.Reset();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;

            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            if (items == null || items.Count == 0)
                return; // NotificationMenuState.Open() already refused to open/announce for an empty list.

            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
            if (ScreenReturn.TryTakeCursor(Name, out ListReturnCursor saved))
            {
                int row = items.FindIndex(i => i.SameSource(saved.Item as NotificationMenuState.NotificationItem));
                row = row >= 0 ? row : Math.Min(saved.Index, items.Count - 1);
                if (row >= 0)
                    Model.CurrentRegion?.MoveTo(row);
            }
            detailHelper.RefreshButtons();
            AnnounceCurrentSelection();
        }

        public override void OnPop()
        {
            base.OnPop();
            LetterButtonRingPatch.CurrentProvider = null;
            // Deliberately no state reset here — see OnPush's generation guard.
        }

        /// <summary>
        /// The letter the list cursor sits on, or null for a message/alert row or while the
        /// detail view has focus. Read without RefreshModel: this runs from the letter
        /// stack's own draw, which must not rebuild the model mid-frame.
        /// </summary>
        private Letter FocusedLetter()
        {
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            ListModel region = Model.CurrentRegion;
            int idx = region == null || detailHelper.IsInDetailView ? -1 : region.Index;
            return items != null && idx >= 0 && idx < items.Count ? items[idx].GetSourceLetter() : null;
        }

        // Up/Down/Home/End: detail-view-aware and search-suffix-preserving.

        protected override void MoveItem(int delta)
        {
            if (detailHelper.IsInDetailView)
            {
                if (delta > 0)
                    detailHelper.SelectNextDetailPosition();
                else
                    detailHelper.SelectPreviousDetailPosition();
                return;
            }

            if (listTypeahead.HasActiveSearch && !listTypeahead.HasNoMatches)
            {
                int current = CurrentIndex();
                int newIndex = delta > 0 ? listTypeahead.GetNextMatch(current) : listTypeahead.GetPreviousMatch(current);
                if (newIndex >= 0)
                {
                    RefreshModel();
                    Model.CurrentRegion?.MoveTo(newIndex);
                    AnnounceWithSearch();
                }
                return;
            }

            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            region.MoveBy(delta);
            detailHelper.ResetDetailPosition();
            detailHelper.RefreshButtons();
            AnnounceCurrentSelection();
        }

        protected override void MoveItemEdge(bool first)
        {
            if (detailHelper.IsInDetailView)
            {
                if (first)
                    detailHelper.JumpToDetailStart();
                else
                    detailHelper.JumpToDetailEnd();
                return;
            }

            if (listTypeahead.HasActiveSearch && !listTypeahead.HasNoMatches)
            {
                RefreshModel();
                Model.CurrentRegion?.MoveTo(first ? listTypeahead.GetFirstMatch() : listTypeahead.GetLastMatch());
            }
            else
            {
                RefreshModel();
                ListModel region = Model.CurrentRegion;
                if (region == null || region.IsEmpty)
                    return;
                if (first)
                    region.MoveFirst();
                else
                    region.MoveLast();
                listTypeahead.ClearSearch();
            }
            detailHelper.ResetDetailPosition();
            detailHelper.RefreshButtons();
            AnnounceCurrentSelection();
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            if (listTypeahead.HasActiveSearch)
            {
                listTypeahead.ClearSearchAndAnnounce();
                AnnounceWithSearch();
            }
            else if (detailHelper.IsInDetailView)
            {
                GoBackToListInternal();
                TwoLevelMenuHelper.SpeakReturnToList();
            }
            else
            {
                NotificationMenuState.Close();
                TolkHelper.Speak("RimWorldAccess.Notifications.Menu.Closed".Loc());
            }
        }

        private void HandleBackspace()
        {
            if (!listTypeahead.HasActiveSearch)
                return;
            List<string> labels = GetItemLabels();
            if (listTypeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    RefreshModel();
                    Model.CurrentRegion?.MoveTo(newIndex);
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// PRESERVED QUIRK — do not add a detail-view guard: this is gated only on the scope
        /// being live, so typing while reading a letter in detail view silently re-searches
        /// the list in the background (moving the cursor, speaking the match) without leaving
        /// detail view. It looks like an oversight and is preserved deliberately.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (!TypeaheadMatcher.AcceptsSearchChar(c, listTypeahead.HasActiveSearch))
                return false;

            List<string> labels = GetItemLabels();
            if (listTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    RefreshModel();
                    Model.CurrentRegion?.MoveTo(newIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                listTypeahead.SpeakNoMatches();
            }
            return true;
        }

        private void EnterDetailViewInternal()
        {
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            if (items == null || items.Count == 0)
                return;
            int idx = CurrentIndex();
            if (idx < 0 || idx >= items.Count)
                return;

            listTypeahead.ClearSearch();
            detailHelper.RefreshButtons();
            detailHelper.EnterDetailView();
            detailHelper.AnnounceDetailPosition();
        }

        private void GoBackToListInternal()
        {
            detailHelper.GoBackToList();
            listTypeahead.ClearSearch();
            AnnounceCurrentSelection();
        }

        /// <summary>Activates the currently selected detail-view button.</summary>
        private void ActivateButtonInternal()
        {
            if (!detailHelper.ActivateCurrentButton())
                return;

            ButtonInfo button = detailHelper.GetCurrentButton();
            if (button == null)
                return;

            string buttonLabel = button.Label;

            try
            {
                button.Action?.Invoke();

                // CRITICAL: the action may have closed the menu itself (a dialog that closes
                // NotificationMenuState in its own Open) — no post-action processing then.
                if (!NotificationMenuState.IsActive)
                    return;

                // The button may have removed the letter.
                NotificationMenuState.Refresh();
                List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;

                if (items.Count == 0)
                {
                    NotificationMenuState.Close();
                    TolkHelper.Speak("RimWorldAccess.Notifications.Action.ActivatedNoneRemaining".Loc(buttonLabel));
                    return;
                }

                // RefreshModel's SetCount auto-clamps the cursor into the new count.
                RefreshModel();

                // An identity flag carried from the button's construction site, never guessed
                // from the translated label, which would break in every non-English language.
                if (button.IsJumpAction)
                {
                    NotificationMenuState.Close();
                    MapNavigationState.SpeakJumpedTo("RimWorldAccess.Notifications.Target.LocationFallback".Translate());
                }
                else
                {
                    detailHelper.GoBackToList();
                    detailHelper.RefreshButtons();
                    TolkHelper.Speak("RimWorldAccess.Notifications.Action.ActivatedBackToList".Loc(buttonLabel));
                    AnnounceCurrentSelection();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to activate button: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Notifications.Menu.FailedToActivateButton".Loc());
            }
        }

        /// <summary>Deletes the currently selected letter; only letters can be deleted.</summary>
        private void DeleteSelected()
        {
            if (!NotificationMenuState.IsActive)
                return;
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            if (items == null || items.Count == 0)
                return;
            int idx = CurrentIndex();
            if (idx < 0 || idx >= items.Count)
                return;

            NotificationMenuState.NotificationItem item = items[idx];

            if (item.Type != NotificationMenuState.NotificationType.Letter)
            {
                TolkHelper.Speak("RimWorldAccess.Notifications.Menu.OnlyLettersDeletable".Loc(), SpeechPriority.High);
                return;
            }

            Letter letter = item.GetSourceLetter();
            if (letter == null)
            {
                TolkHelper.Speak("RimWorldAccess.Notifications.Menu.CannotDeleteLetter".Loc(), SpeechPriority.High);
                return;
            }

            string deletedLabel = item.Label;
            Find.LetterStack.RemoveLetter(letter);

            NotificationMenuState.Refresh();
            List<NotificationMenuState.NotificationItem> refreshed = NotificationMenuState.Notifications;

            if (refreshed.Count == 0)
            {
                NotificationMenuState.Close();
                TolkHelper.Speak("RimWorldAccess.Notifications.Action.DeletedNoneRemaining".Loc(deletedLabel));
                return;
            }

            RefreshModel();
            detailHelper.GoBackToList();
            detailHelper.ResetDetailPosition();
            detailHelper.RefreshButtons();

            int newIdx = CurrentIndex();
            if (newIdx < 0 || newIdx >= refreshed.Count)
                newIdx = refreshed.Count - 1;
            NotificationMenuState.NotificationItem newItem = refreshed[newIdx];
            string typeLabel = NotificationMenuState.GetTypeLabel(newItem.Type);
            string position = MenuHelper.FormatPosition(newIdx, refreshed.Count);
            TolkHelper.Speak("RimWorldAccess.Notifications.Action.DeletedNextItem".Loc(
                deletedLabel, typeLabel, newItem.Label, position));
        }

        // Announcements are hand-built, not composed — see the class remarks.

        private void AnnounceCurrentSelection()
        {
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            if (items == null || items.Count == 0)
                return;

            if (detailHelper.IsInDetailView)
            {
                detailHelper.AnnounceDetailPosition();
                return;
            }

            int idx = CurrentIndex();
            if (idx < 0 || idx >= items.Count)
                return;
            NotificationMenuState.NotificationItem item = items[idx];
            string announcement = "RimWorldAccess.Notifications.List.LineWithPosition".Translate(
                NotificationMenuState.GetTypeLabel(item.Type),
                item.Label,
                MenuHelper.FormatPosition(idx, items.Count));
            TolkHelper.SpeakData(announcement);
        }

        private void AnnounceWithSearch()
        {
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            if (items == null || items.Count == 0)
                return;

            int idx = CurrentIndex();
            if (idx < 0 || idx >= items.Count)
                return;
            NotificationMenuState.NotificationItem item = items[idx];
            string announcement = "RimWorldAccess.Notifications.List.LineWithPosition".Translate(
                NotificationMenuState.GetTypeLabel(item.Type),
                item.Label,
                MenuHelper.FormatPosition(idx, items.Count));

            if (listTypeahead.HasActiveSearch)
            {
                announcement += listTypeahead.BuildSearchContextSuffix();
            }
            TolkHelper.SpeakData(announcement);
        }

        private int CurrentIndex()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            return region == null ? -1 : region.Index;
        }

        private NotificationMenuState.NotificationItem CurrentItem()
        {
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            int idx = CurrentIndex();
            return items != null && idx >= 0 && idx < items.Count ? items[idx] : null;
        }

        private List<string> GetItemLabels()
        {
            List<NotificationMenuState.NotificationItem> items = NotificationMenuState.Notifications;
            var labels = new List<string>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    labels.Add(item.Label);
                }
            }
            return labels;
        }
    }

    /// <summary>
    /// Keeps <see cref="NotificationScope"/> in lockstep with
    /// <see cref="NotificationMenuState.IsActive"/>, reconciled every OnGUI pass by the shell
    /// dispatcher. It also stands down while an info card is open over the menu, since a
    /// hyperlink button's vanilla action can open a real <see cref="Verse.Dialog_InfoCard"/>
    /// without closing the menu first; popping drops the menu's AnyLiveModal contribution so
    /// the info-card handler runs unshadowed, and
    /// <see cref="NotificationScope.OnPush"/>'s open-generation guard restores the user's
    /// position on the way back. This mirror itself carries no state.
    ///
    /// Every other button action closes the menu BEFORE opening whatever it opens, so
    /// IsActive is already false by the time another screen's scope would need to stand down;
    /// no further gates are needed. The one exception is a generic vanilla DiaOption.action,
    /// whose per-letter target cannot be enumerated statically: a real window it opens layers
    /// via its own scope, and a windowless overlay it might open is unverified.
    ///
    /// Chain-independent: nothing else in the reconcile list opens from inside the
    /// notification menu or vice versa (its sole opener is an ambient claim gated off while
    /// any live modal scope is stacked), so its reconcile-list position never matters.
    /// </summary>
    internal static class NotificationScopeMirror
    {
        private static readonly NotificationScope scope = new NotificationScope();

        public static void Reconcile()
        {
            if (NotificationMenuState.IsActive && !InfoCardState.IsActive)
            {
                // Push re-floats an already-stacked scope to the top, so an unconditional
                // per-frame Push would hoist this scope back above a window-attached scope
                // opened from a letter option that does not close the menu first, masking it
                // completely. Pushing only when absent lets such windows layer above.
                if (!FocusStack.Contains(scope))
                {
                    FocusStack.Push(scope);
                }
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// Rings the focused letter's button on the letter stack. Letters draw OUTSIDE every
    /// window (LetterStack's own UIRoot pass), so no draw bracket reaches them and the ring
    /// is painted from the row method itself. Patched on the DECLARING
    /// <see cref="Letter"/> — no subclass overrides DrawButtonAt, so one patch covers every
    /// letter. The rect recomputed here is vanilla's RESTING one (decompiled
    /// Verse/Letter.cs:85-86); the arrival slide and periodic bounce animate a COPY, so the
    /// ring holds the resting position while the icon moves.
    /// </summary>
    [HarmonyPatch(typeof(Letter), nameof(Letter.DrawButtonAt))]
    internal static class LetterButtonRingPatch
    {
        /// <summary>Supplied by <see cref="NotificationScope"/> while it drives; null leaves the postfix a single static read.</summary>
        internal static Func<Letter> CurrentProvider;

        [HarmonyPostfix]
        public static void Postfix(Letter __instance, float topY)
        {
            try
            {
                Func<Letter> provider = CurrentProvider;
                if (provider == null || !ReferenceEquals(__instance, provider()))
                {
                    return;
                }
                Rect rect = new Rect((float)UI.screenWidth - 38f - 12f, topY, 38f, 30f);
                FocusRing.Draw(rect);
                UiPointerFollow.NotifyFocusedRect(GuiSpace.VisibleScreenRect(rect));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Letter button ring draw error", ex);
            }
        }
    }
}
