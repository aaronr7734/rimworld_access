using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The real vanilla <see cref="Dialog_AssignBuildingOwner"/> driven by keyboard on the shared
    /// <see cref="ScreenScope"/> chassis: one content region holding the mod-side search row (when a
    /// third party drew a QuickSearchWidget into this dialog) followed by the dialog's own pawn rows,
    /// plus the automatic Buttons region holding Close.
    /// <b>Enter.</b> This dialog keeps closeOnAccept/closeOnCancel at the Window defaults of true, so
    /// vanilla's Enter is a plain close with no submit semantic to alias. Enter is claimed and stamped
    /// purely to stop vanilla's deferred Accept re-test from closing the dialog out from under row
    /// navigation — which the chassis default already does.
    /// <b>Escape.</b> Claimed only to RE-ENTER vanilla's own close
    /// (<c>Find.WindowStack.Notify_PressedCancel</c>): reached from windowless inspection the window
    /// opens without IMGUI focus, its GUI pass runs after the dispatcher, and an unclaimed Escape is
    /// shell-swallowed while <c>MenuOwnsInput()</c> — a keyboard trap. The chassis default
    /// <c>OwnsCancel</c> (true only during a live typeahead search) is what the re-entry needs:
    /// outside a search WindowCancelKeyRouterPatch never blocks our own call, and during one the
    /// chassis clears the buffer instead of closing.
    /// Assignment only, matching vanilla's actual structure: bed-type and medical toggles are separate
    /// Command_Toggle gizmos on the bed itself, already reachable through gizmo navigation.
    /// Rows come from <see cref="AssignRowCapture"/>, bracketed to this dialog's own DoWindowContents
    /// pass alongside <see cref="ButtonTextCapture"/> and <see cref="QuickSearchCapture"/>: Enter on
    /// an assigned/candidate row requests a click on its captured button, so vanilla's own
    /// TryAssignPawn/TryUnassignPawn, click sound and single-slot auto-close all run untouched. A
    /// successful single-owner assign closes the dialog and pops this scope before its next
    /// OnGuiPass, so the pending-action confirmation never fires and the mirror's own refocus
    /// announcement is all that is spoken.
    /// Because the rows arrive from the draw, the content region is EMPTY until the dialog has drawn
    /// once — hence the always-navigable region and the deferred entry announcement in
    /// <see cref="OnFocus"/>.
    /// </summary>
    public sealed class AssignScope : ScreenScope
    {
        private static readonly AccessTools.FieldRef<Dialog_AssignBuildingOwner, CompAssignableToPawn> assignableField =
            AccessTools.FieldRefAccess<Dialog_AssignBuildingOwner, CompAssignableToPawn>("assignable");
        private static readonly AccessTools.FieldRef<Dialog_AssignBuildingOwner, Vector2> scrollPositionField =
            AccessTools.FieldRefAccess<Dialog_AssignBuildingOwner, Vector2>("scrollPosition");

        /// <summary>
        /// The label vanilla's own assignment gizmo carries for this comp — virtual, so a grave answers
        /// "Assign colonist" where a bed answers "Set owner". Read through it rather than hardcoding
        /// the base key, so the region is named the way the player's own click was.
        /// </summary>
        private static readonly MethodInfo assignmentGizmoLabelMethod =
            AccessTools.Method(typeof(CompAssignableToPawn), "GetAssignmentGizmoLabel");

        private readonly Dialog_AssignBuildingOwner dialog;
        private readonly CompAssignableToPawn assignable;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<CapturedAssignRow> rows = new List<CapturedAssignRow>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private Rect lastInRect;
        private bool pendingAnnounce;
        private Pawn pendingActionPawn;
        private bool pendingActionIsAssign;
        private QuickSearchWidget capturedSearch;
        private bool searchRowPresent;

        public AssignScope(Dialog_AssignBuildingOwner dialog)
        {
            this.dialog = dialog;
            assignable = assignableField(dialog);
            RegisterPopTeardown(session.CancelIfActive);

            Claim(SharedMenuGrammar.Info, OnInfo);
            // Cancel is claimed only to re-enter vanilla's own Escape router (see the class remarks).
            // Registered after the chassis's typeahead Cancel claim, so a live search clears instead.
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "assign-owner"; }
        }

        /// <summary>Mechanism 2: a scopeless real window stacked above ours (the ideo info window mid-transition is scopeless) takes input.</summary>
        public override bool IsModal
        {
            get { return !ForeignWindowAbove(); }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// True only while a hosting mod's own search box is actually filtering: that filter is a
        /// substring test, so the prefix tiers alone cannot reach every row it leaves drawn. Answered
        /// from the captured widget's live state — the vanilla dialog has no filter of its own.
        /// </summary>
        protected override bool TypeaheadSubstringFallback
        {
            get { return capturedSearch != null && capturedSearch.filter.Active; }
        }

        /// <summary>The pawn rows draw Widgets.ButtonText of their own, so scraping the window's buttons would present every row's Assign button twice.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>
        /// The live scope owning <paramref name="window"/>, from anywhere on the focus stack. See
        /// <see cref="TextDialogShared.ScopeOwning{T}"/> for why the top of the stack is wrong.
        /// </summary>
        internal static AssignScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<AssignScope>(
                window, delegate(AssignScope s, Window w) { return s.Owns(w); });
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        /// <summary>Same shape as OptionsScope/others: a scopeless real window above ours counts as foreign; tooltips (ImmediateWindow) never do.</summary>
        private bool ForeignWindowAbove()
        {
            IList<Window> windows = Find.WindowStack.Windows;
            bool above = false;
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (ReferenceEquals(window, dialog))
                {
                    above = true;
                    continue;
                }
                if (!above || window is ImmediateWindow)
                {
                    continue;
                }
                if (!ScopeForWindow.HasAttachedScope(window))
                {
                    return true;
                }
            }
            return false;
        }

        // Row model

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)assignmentGizmoLabelMethod.Invoke(assignable, null);
        }

        protected override int ContentItemCount(int region)
        {
            return rows.Count + SearchRowOffset;
        }

        /// <summary>Re-syncs the row list against the most recent draw pass; the capture keeps its items until the next BeginPass.</summary>
        protected override void RefreshContent()
        {
            rows.Clear();
            IReadOnlyList<CapturedAssignRow> captured = AssignRowCapture.Items;
            for (int i = 0; i < captured.Count; i++)
            {
                rows.Add(captured[i]);
            }
            searchRowPresent = capturedSearch != null;
        }

        /// <summary>1 while a hosting mod's search box is captured (it draws above the list, so it is row 0), else 0.</summary>
        private int SearchRowOffset
        {
            get { return searchRowPresent ? 1 : 0; }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (IsSearchRow(index))
            {
                return DescribeSearchRow();
            }
            CapturedAssignRow row = RowAt(index);
            return row == null ? new ElementDescription() : DescribeRow(row);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (IsSearchRow(index))
            {
                BeginSearchEdit();
                return;
            }
            CapturedAssignRow row = RowAt(index);
            if (row == null)
            {
                return;
            }
            switch (row.Kind)
            {
                case AssignRowKind.Assigned:
                    RequestRowClick(row, isAssign: false);
                    break;
                case AssignRowKind.Candidate:
                    RequestRowClick(row, isAssign: true);
                    break;
                default:
                    // IdeoForbidden/Rejected: vanilla draws no button Enter could click either —
                    // re-announce the reason/forbid state, never silent.
                    AnnounceCurrentItem();
                    break;
            }
        }

        /// <summary>Rows match on the pawn's identity alone; ButtonWord/ForbidReason are status, not something a player types.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (IsSearchRow(row))
            {
                return "RimWorldAccess.Search.Prompt".Loc().ToString();
            }
            CapturedAssignRow captured = RowAt(row);
            return captured == null ? "" : captured.PawnLabel;
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "CloseButton".Translate().ToString(),
                    delegate { dialog.Close(); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private bool IsSearchRow(int index)
        {
            return searchRowPresent && index == 0;
        }

        private CapturedAssignRow RowAt(int index)
        {
            int row = index - SearchRowOffset;
            return row >= 0 && row < rows.Count ? rows[row] : null;
        }

        /// <summary>The row index under the cursor for the draw pass's focus ring, or -1 when the cursor is elsewhere.</summary>
        private int FocusedRowIndex()
        {
            if (Model.RegionIndex != 0)
            {
                return -1;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return -1;
            }
            return IsSearchRow(region.Index) ? -1 : region.Index - SearchRowOffset;
        }

        private bool SearchRowFocused()
        {
            ListModel region = Model.CurrentRegion;
            return Model.RegionIndex == 0 && region != null && !region.IsEmpty && IsSearchRow(region.Index);
        }

        // Per-GUI-pass work, driven by the dialog's own draw

        /// <summary>Runs from the DoWindowContents prefix: arms the capture engines for this pass.</summary>
        internal void BeginDrawPass(Rect inRect)
        {
            lastInRect = inRect;
            QuickSearchCapture.BeginPass(SearchRowFocused());
            AssignRowCapture.BeginPass(FocusedRowIndex());
            ButtonTextCapture.BeginPass();
        }

        internal static void EndCapturePasses()
        {
            ButtonTextCapture.EndPass();
            AssignRowCapture.EndPass();
            QuickSearchCapture.EndPass();
        }

        /// <summary>Runs from the DoWindowContents finalizer, after vanilla's own scroll view has already closed.</summary>
        internal void OnGuiPass()
        {
            capturedSearch = QuickSearchCapture.Items.Count > 0 ? QuickSearchCapture.Items[0].Widget : null;
            session.MirrorLive();

            RefreshModel();
            AutoScrollToFocused();

            if (pendingActionPawn != null)
            {
                Pawn actedPawn = pendingActionPawn;
                bool wasAssign = pendingActionIsAssign;
                pendingActionPawn = null;
                string key = wasAssign ? "RimWorldAccess.UI.Assign.Assigned" : "RimWorldAccess.UI.Assign.Unassigned";
                TolkHelper.Speak(key.Loc(actedPawn.LabelShort));
            }
            else if (pendingAnnounce && rows.Count > 0 && !ForeignWindowAbove()
                && !ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                pendingAnnounce = false;
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// The rows arrive from the dialog's own draw one pass after the push, so the chassis's entry
        /// announcement would speak an empty region. Stand it down and let <see cref="OnGuiPass"/>
        /// speak the landing once the rows are really there.
        /// </summary>
        public override void OnFocus()
        {
            if (rows.Count == 0)
            {
                SuppressNextEntryAnnouncement();
                pendingAnnounce = true;
            }
            base.OnFocus();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.UI.Assign.SlotsSummary".Translate(
                assignable.parent.LabelCap,
                assignable.AssignedPawnsForReading.Count,
                assignable.TotalSlots);
        }

        /// <summary>
        /// Keeps the focused row visible when the list scrolls. The visible band is vanilla's own
        /// inRect minus its fixed 20f top / 40f bottom margins; Widgets.AdjustRectsForScrollView only
        /// ever trims width, never height, so this stays accurate without reflecting into the dialog's
        /// scroll rect.
        /// </summary>
        private void AutoScrollToFocused()
        {
            int index = FocusedRowIndex();
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            Rect rect = rows[index].Rect;
            if (rect.width <= 0f)
            {
                return;
            }
            float visibleHeight = lastInRect.height - 60f;
            ref Vector2 scroll = ref scrollPositionField(dialog);
            if (rect.y < scroll.y)
            {
                scroll.y = rect.y;
            }
            else if (rect.yMax > scroll.y + visibleHeight)
            {
                scroll.y = rect.yMax - visibleHeight;
            }
        }

        // Action handlers

        /// <summary>
        /// Routes Escape through vanilla's own handling (vehicle A), guarded on the window still being
        /// on the stack so the ordinary click route — where the window's earlier GUI pass already
        /// closed it — cannot make Notify_PressedCancel close whatever window is now on top. Stamp
        /// AFTER the call, never before: the stamp is a this-frame global the router checks first.
        /// </summary>
        private void OnCancel(KeyEventSnapshot e)
        {
            if (Find.WindowStack.Windows.Contains(dialog))
            {
                Find.WindowStack.Notify_PressedCancel();
            }
            ShellFrameStamps.MarkCancelConsumed();
        }

        /// <summary>
        /// The widget's own <c>filter.Text</c> setter is the vanilla write path (it trims and drops the
        /// match cache), and the hosting mod re-filters from its next draw, so the list shrinks as the
        /// player types. minLength 0 is deliberate: committing an empty buffer clears the filter.
        /// </summary>
        private void BeginSearchEdit()
        {
            QuickSearchWidget widget = capturedSearch;
            if (widget == null)
            {
                AnnounceCurrentItem();
                return;
            }
            session.EnterEdit(
                widget.filter.Text,
                new TextFieldSpec(
                    labelKey: "RimWorldAccess.TextInput.LabelDefault",
                    maxLength: widget.maxSearchTextLength,
                    minLength: 0),
                "RimWorldAccess.Search.Prompt".Loc().ToString(),
                delegate(string text) { widget.filter.Text = text; },
                AnnounceCurrentItem);
        }

        private void RequestRowClick(CapturedAssignRow row, bool isAssign)
        {
            if (row.ButtonCaptureIndex < 0)
            {
                // Defensive only: vanilla always draws a button for these two row kinds.
                AnnounceCurrentItem();
                return;
            }
            ButtonTextCapture.RequestClick(row.ButtonCaptureIndex);
            // The actual TryAssignPawn/TryUnassignPawn (and single-slot auto-close) run on the NEXT
            // armed pass, when the injected click reaches vanilla's inline handling — confirm and
            // speak from OnGuiPass once that has happened.
            pendingActionPawn = row.Pawn;
            pendingActionIsAssign = isAssign;
        }

        private void OnInfo(KeyEventSnapshot e)
        {
            CapturedAssignRow row = FocusedRow();
            if (row == null || row.Kind != AssignRowKind.IdeoForbidden || row.IdeoIconAction == null)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            // Invoke the SAME delegate vanilla's own click handler would run (OpenIdeoInfo then
            // Close) — parity, including the dialog closing as a side effect.
            row.IdeoIconAction();
        }

        private CapturedAssignRow FocusedRow()
        {
            int index = FocusedRowIndex();
            return index >= 0 && index < rows.Count ? rows[index] : null;
        }

        // Descriptions

        private ElementDescription DescribeSearchRow()
        {
            ElementDescription d = new ElementDescription();
            d.Label = "RimWorldAccess.Search.Prompt".Loc().ToString();
            d.Role = ElementRole.TextField;
            QuickSearchFilter filter = capturedSearch != null ? capturedSearch.filter : null;
            string text = filter != null ? filter.Text : "";
            if (string.IsNullOrEmpty(text))
            {
                d.ValueBlank = true;
            }
            else if (capturedSearch.noResultsMatched && filter.Active)
            {
                d.Value = "RimWorldAccess.Search.NoMatches".Loc(text).ToString();
            }
            else
            {
                d.Value = text;
            }
            return d;
        }

        /// <summary>
        /// Vanilla draws the pawn's name and its button/reason text as two widgets in one row; this
        /// shell treats a row as one focusable element, so both are spoken as one label with the pawn
        /// name first, as vanilla's own reason-baked-into-the-label formula already does for Rejected
        /// rows.
        /// </summary>
        private static ElementDescription DescribeRow(CapturedAssignRow row)
        {
            ElementDescription d = new ElementDescription();
            switch (row.Kind)
            {
                case AssignRowKind.Assigned:
                case AssignRowKind.Candidate:
                    d.Label = row.PawnLabel + ". " + row.ButtonWord;
                    d.Role = ElementRole.Button;
                    break;
                case AssignRowKind.IdeoForbidden:
                    d.Label = row.PawnLabel + ". " + row.ForbidReason;
                    d.Role = ElementRole.None;
                    break;
                default: // Rejected
                    d.Label = row.PawnLabel;
                    d.Role = ElementRole.None;
                    d.Disabled = true;
                    break;
            }
            return d;
        }
    }

    /// <summary>
    /// Brackets AssignRowCapture, ButtonTextCapture and QuickSearchCapture to the dialog's own draw
    /// and drives the scope's per-pass refresh, auto-scroll and deferred announcements, all inside
    /// the window's own GUI pass.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_AssignBuildingOwner), "DoWindowContents")]
    public static class AssignScopeDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_AssignBuildingOwner __instance, Rect inRect, out AssignScope __state)
        {
            __state = null;
            try
            {
                AssignScope scope = AssignScope.OwningScope(__instance);
                if (scope != null)
                {
                    __state = scope;
                    scope.BeginDrawPass(inRect);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Assign dialog draw pass error", ex);
            }
        }

        // A finalizer runs after every postfix, including a third party's that draws its own
        // search widget, and still closes the passes when the body throws.
        [HarmonyFinalizer]
        public static void Finalizer(Exception __exception, AssignScope __state)
        {
            if (__state != null)
            {
                CapturePasses.Finish(__state, FocusStack.Contains(__state), __exception,
                    () => AssignScope.EndCapturePasses(), s => s.OnGuiPass(), "Assign dialog draw pass error");
            }
        }
    }
}
