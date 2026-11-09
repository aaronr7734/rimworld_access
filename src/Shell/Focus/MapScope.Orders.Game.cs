using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Ambient map claims: draft toggle (R) and the two bracket order shortcuts
    /// ('[' executes the top option, ']' opens the orders float menu). R yields to an active
    /// targeting session through TargetingScope's stack position; nothing else in the family
    /// claims either bracket.
    ///
    /// Each guard carries its handler's own gate plus two modal-guard terms —
    /// !ShellGuards.MenuOwnsInput() and !StatBreakdownState.IsActive, the latter because the
    /// stat breakdown consumes R/[/] through TreeNavigationHelper's catch-all tail. There is
    /// deliberately no !FocusStack.AnyLiveModal term: a live modal scope already masks these
    /// claims structurally in FocusStackCore.Dispatch.
    ///
    /// Shift+[ is a real chord, not a modifier-blind match: it selects queued rather than
    /// immediate execution, so map.order.topOption is registered on both the bare and
    /// Shift+LeftBracket chords and the handler reads the snapshot's Shift bit.
    ///
    /// map.draft.toggle is registered on <see cref="WorldScope"/> as well, with a
    /// byte-identical guard and handler: Find.CurrentMap stays non-null while the planet is
    /// rendered, so a pawn selected before opening world view keeps R reachable there. The
    /// bracket claims stay MapScope-exclusive — BracketOrdersLive denies on the planet view.
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterOrderClaims()
        {
            RegisterDraftClaim(this);
            Claim("map.order.topOption", OnExecuteTopOption, when: BracketOrdersLive);
            Claim("map.order.menu", OnOpenOrdersMenu, when: BracketOrdersLive);
            // The C-key reform-caravan handler's map-side half. Its explicit
            // !WorldNavigationState.IsActive term is load-bearing rather than a modal guard:
            // Find.CurrentMap stays non-null on the planet view, and the term is what lets the
            // world map's own C branch fall through instead of both firing.
            Claim("map.caravan.reform", OnReformCaravan, when: ReformCaravanLive);
        }

        /// <summary>
        /// Shared registrar for map.draft.toggle, called once from MapScope's constructor and
        /// once from WorldScope's; guard and handler are identical either way.
        /// </summary>
        internal static void RegisterDraftClaim(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.draft.toggle", OnToggleDraft, when: DraftToggleLive);
        }

        /// <summary>The draft gate plus the two modal-guard terms from the class remarks.</summary>
        private static bool DraftToggleLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && Find.Selector != null && Find.Selector.NumSelected > 0
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// Shared gate for both bracket claims: world-view, no-map, camera-blocked and
        /// uninitialized map-nav checks plus the two modal-guard terms. Cursor validity, the
        /// selected-pawns check and the no-options case stay inline in each handler — they
        /// produce runtime announcements rather than deciding whether the key is claimed.
        /// </summary>
        private static bool BracketOrdersLive()
        {
            return (Find.World == null || Find.World.renderer == null
                    || Find.World.renderer.wantedMode != WorldRenderMode.Planet)
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && MapNavigationState.IsInitialized
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// The reform-caravan gate, including its !WorldNavigationState.IsActive term (see the
        /// registration call site), plus the two standard modal-guard terms.
        /// </summary>
        private static bool ReformCaravanLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && !WorldNavigationState.IsActive
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>Reform caravan with C: temporary maps only, and TriggerReformation speaks its own failures.</summary>
        private static void OnReformCaravan(KeyEventSnapshot e)
        {
            CaravanFormationState.TriggerReformation();
        }

        /// <summary>
        /// The pawn's own draft toggle gizmo — the instance vanilla's sidebar renders and clicks,
        /// carrying its baked-in Disabled/disabledReason for Downed, Deathresting and mech
        /// control groups. <see cref="Pawn_DraftController.GetGizmos"/> is internal, so the scan
        /// runs over the public <see cref="Pawn.GetGizmos"/> and matches the vanilla draft hotkey.
        /// </summary>
        private static Command_Toggle GetDraftGizmo(Pawn pawn)
        {
            foreach (Gizmo gizmo in pawn.GetGizmos())
            {
                if (gizmo is Command_Toggle toggle && toggle.hotKey == KeyBindingDefOf.Command_ColonistDraft)
                    return toggle;
            }
            return null;
        }

        /// <summary>
        /// Toggles draft with R. Multi-select follows vanilla's InheritInteractionsFrom shape,
        /// toggling only pawns that share the first pawn's current drafted state.
        ///
        /// Both branches go through each pawn's own draft Command_Toggle
        /// (<see cref="GetDraftGizmo"/> plus Gizmo.ProcessInput) rather than writing
        /// pawn.drafter.Drafted, so vanilla's Disabled gate, its DraftOn/DraftOff sound and its
        /// tutor bookkeeping all apply exactly as they would on a sidebar click.
        /// </summary>
        private static void OnToggleDraft(KeyEventSnapshot e)
        {
            if (MultiSelectState.IsMultiSelectActive)
            {
                // The gizmo scan alone decides draftability, so overseen mechs and modded
                // draftables count. Resolving it up front also means the Disabled gate below
                // reads the same instance that gets invoked.
                var gizmoByPawn = new Dictionary<Pawn, Command_Toggle>();
                var pawns = new List<Pawn>();
                foreach (var p in Find.Selector.SelectedPawns)
                {
                    var g = GetDraftGizmo(p);
                    if (g != null)
                    {
                        gizmoByPawn[p] = g;
                        pawns.Add(p);
                    }
                }

                if (pawns.Count == 0)
                    return;

                // Vanilla InheritInteractionsFrom: only pawns sharing the first pawn's state.
                bool firstPawnDrafted = gizmoByPawn[pawns[0]].isActive();
                bool newState = !firstPawnDrafted;

                Event fakeEvent = new Event();
                fakeEvent.type = EventType.Used;

                foreach (var p in pawns)
                {
                    Command_Toggle g = gizmoByPawn[p];
                    // Disabled pawns are skipped, as GizmoGridDrawer's own !other.Disabled
                    // group-propagation check does; they fall through to the "except" branch.
                    if (g.isActive() == firstPawnDrafted && !g.Disabled)
                    {
                        g.ProcessInput(fakeEvent);
                    }
                }

                // Pawns already in the desired state count as successes. State is read back from
                // each pawn's own toggle, not the drafter, so non-drafter draftables report right.
                var inDesiredState = pawns.Where(p => gizmoByPawn[p].isActive() == newState)
                    .Select(p => p.LabelShort).ToList();
                var notInDesiredState = pawns.Where(p => gizmoByPawn[p].isActive() != newState)
                    .Select(p => p.LabelShort).ToList();

                string everyone = ((string)"ConfirmAbandonHomeNegativeThoughts_Everyone".Translate()).TrimEnd(':', ' ');
                string status = newState
                    ? "RimWorldAccess.Input.Drafting.StatusDrafted".Translate().ToString()
                    : "RimWorldAccess.Input.Drafting.StatusUndrafted".Translate().ToString();

                if (notInDesiredState.Count == 0)
                    TolkHelper.Speak("RimWorldAccess.Input.Drafting.EveryoneStatus".Loc(everyone, status));
                else if (notInDesiredState.Count <= inDesiredState.Count)
                {
                    string exceptNames = MenuHelper.FormatNameList(notInDesiredState);
                    TolkHelper.Speak("RimWorldAccess.Input.Drafting.EveryoneExceptStatus".Loc(everyone, exceptNames, status));
                }
                else
                {
                    string onlyNames = MenuHelper.FormatNameList(inDesiredState);
                    TolkHelper.Speak("RimWorldAccess.Input.Drafting.OnlyStatus".Loc(onlyNames, status));
                }
            }
            else
            {
                // The gizmo scan is again the sole draftability test.
                Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;
                if (selectedPawn == null)
                    return;

                Command_Toggle gizmo = GetDraftGizmo(selectedPawn);
                if (gizmo == null)
                    return;

                if (gizmo.Disabled)
                {
                    // Vanilla's own refusal, in its "DisabledCommand: <reason>" shape — the
                    // exact message a sidebar click on this disabled gizmo would raise.
                    string reason = gizmo.disabledReason;
                    if (string.IsNullOrEmpty(reason))
                        reason = "RimWorldAccess.Inspection.Gizmo.DisabledExecuteFallback".Translate();
                    TolkHelper.SpeakData("DisabledCommand".Translate() + ": " + reason);
                    return;
                }

                Event fakeEvent = new Event();
                fakeEvent.type = EventType.Used;
                gizmo.ProcessInput(fakeEvent);

                string status = gizmo.isActive()
                    ? "RimWorldAccess.Input.Drafting.StatusDraftedTitle".Translate().ToString()
                    : "RimWorldAccess.Input.Drafting.StatusUndraftedTitle".Translate().ToString();
                TolkHelper.Speak("RimWorldAccess.Input.Drafting.SinglePawnStatus".Loc(selectedPawn.LabelShort, status));
            }
        }

        /// <summary>
        /// The row a mouse user sees at the top of the ']' menu.
        /// <see cref="FloatMenuMakerMap.GetOptions"/> returns provider order, while every display
        /// path sorts by Priority then orderInPriority descending and a disabled option's
        /// Priority collapses to the lowest rank — so the raw list can lead with the row the menu
        /// shows last.
        /// </summary>
        private static FloatMenuOption TopMenuOption(List<FloatMenuOption> options)
        {
            return options
                .OrderByDescending(o => o.Priority)
                .ThenByDescending(o => o.orderInPriority)
                .First();
        }

        /// <summary>
        /// Bare '[' — executes the top context-menu option at the cursor: the same
        /// FloatMenuMakerMap options the ']' menu and a mouse click use, taken in the menu's own
        /// sort order (<see cref="TopMenuOption"/>) and invoked through Chosen.
        /// PendingTargetingContext carries the option's label so a targeting session the action
        /// opens announces the real order rather than a generic fallback. Shift is vanilla's
        /// QueueOrder flag.
        /// </summary>
        private static void OnExecuteTopOption(KeyEventSnapshot e)
        {
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;
            if (!cursor.IsValid || !cursor.InBounds(map))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.InvalidPosition".Loc());
                return;
            }

            if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
                return;
            }

            List<Pawn> pawns = Find.Selector.SelectedPawns.ToList();
            Vector3 clickPos = cursor.ToVector3Shifted();
            List<FloatMenuOption> options = BuildOrderOptions(pawns, clickPos, out FloatMenuContext _);
            TraceOrderOptions("map.order.topOption", cursor, pawns, options);

            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.NoAvailableActions".Loc());
                return;
            }

            bool queueing = e.Shift;
            bool multiFeedback = MultiSelectState.IsMultiSelectActive && pawns.Count > 1;

            // Multi-select wraps every option so the invoked action announces per-pawn
            // success/failure; the single-pawn case is announced below instead.
            if (multiFeedback)
                WrapOptionsForMultiSelectFeedback(options, pawns);

            FloatMenuOption top = TopMenuOption(options);

            if (top.Disabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                string singlePrefix = pawns.Count == 1 ? pawns[0].LabelShort + ": " : "";
                TolkHelper.Speak("RimWorldAccess.Input.Order.TopOptionUnavailable".Loc(singlePrefix, top.Label));
                return;
            }

            // The snapshot's Shift stays intact so KeyBindingDefOf.QueueOrder.IsDownEvent
            // evaluates true when queueing; the sound is played here rather than by the action.
            SoundDefOf.ColonistOrdered.PlayOneShotOnCamera();
            // The label lets a Targeter.BeginTargeting call inside the action announce it as the
            // second-phase prompt.
            PendingTargetingContext.Set(top.Label);
            try
            {
                top.Chosen(false, null);
            }
            finally
            {
                PendingTargetingContext.Clear();
            }

            // The wrapped action already announced per-pawn feedback.
            if (!multiFeedback)
            {
                string prefix = pawns[0].LabelShort;
                if (queueing)
                    TolkHelper.Speak("RimWorldAccess.Input.Order.QueuedAction".Loc(prefix, top.Label, "Queued".Translate()));
                else
                    TolkHelper.Speak("RimWorldAccess.Input.Order.Action".Loc(prefix, top.Label));
            }
        }

        /// <summary>
        /// The option list for both brackets, built as vanilla builds it for a right-click:
        /// <see cref="FloatMenuMakerMap.GetOptions"/> inside vanilla's own try/catch. A mod
        /// postfixed onto the provider walk can throw, and vanilla answers that with an empty
        /// menu rather than a dead key, so the keyboard path answers it the same way.
        /// </summary>
        private static List<FloatMenuOption> BuildOrderOptions(
            List<Pawn> pawns, Vector3 clickPos, out FloatMenuContext context)
        {
            context = null;
            List<FloatMenuOption> replaced = OrderOptionOverrides.TryGet(pawns, clickPos.ToIntVec3());
            if (replaced != null)
            {
                return replaced;
            }
            try
            {
                return FloatMenuMakerMap.GetOptions(pawns, clickPos, out context);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Order options error", ex);
                return null;
            }
        }

        /// <summary>
        /// ']' — opens the colonist orders float menu at the cursor. Same option list as '[',
        /// with multi-select additionally wrapped for feedback and given a synthetic "Form and
        /// send in a line" option right after GoHere.
        /// </summary>
        private static void OnOpenOrdersMenu(KeyEventSnapshot e)
        {
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;
            if (!cursor.IsValid || !cursor.InBounds(map))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.InvalidPosition".Loc());
                return;
            }

            if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoPawnSelected".Loc());
                return;
            }

            List<Pawn> pawns = Find.Selector.SelectedPawns.ToList();
            Vector3 clickPos = cursor.ToVector3Shifted();
            List<FloatMenuOption> options = BuildOrderOptions(pawns, clickPos, out FloatMenuContext context);
            TraceOrderOptions("map.order.menu", cursor, pawns, options);

            if (options == null || options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.NoAvailableActions".Loc());
                return;
            }

            if (MultiSelectState.IsMultiSelectActive && pawns.Count > 1)
            {
                // The formation option injected below stays unwrapped: it enters placement mode
                // and issues its jobs later, in LineFormationState.Confirm.
                WrapOptionsForMultiSelectFeedback(options, pawns);

                // Vanilla marks "Go here" with FloatMenuOption.isGoto rather than a subclass, so
                // that field is the structural identity signal instead of the label.
                int goHereIndex = options.FindIndex(o => o.isGoto);

                if (goHereIndex >= 0)
                {
                    var formationPawns = pawns.ToList();
                    string goHereLabel = "GoHere".Translate();
                    var formationOption = new FloatMenuOption(
                        "RimWorldAccess.Input.Order.FormationOption".Translate(goHereLabel),
                        () => LineFormationState.Activate(formationPawns));
                    options.Insert(goHereIndex + 1, formationOption);
                }
            }

            // A real, visible FloatMenu anchored at the cursor cell; FloatMenuScope attaches
            // through the ScopeForWindow mirror and announces the first option on first draw.
            KeyboardFloatMenu.Open(options, givesColonistOrders: true, anchorCell: cursor);
        }

        /// <summary>
        /// Wraps each option's action so it announces per-pawn feedback after invocation
        /// ("Everyone X", "No one could X", "Everyone except A, B X", "Only A, B X"). Used by
        /// both brackets while multi-select is active.
        /// </summary>
        private static void WrapOptionsForMultiSelectFeedback(List<FloatMenuOption> options, List<Pawn> selectedPawns)
        {
            var capturedPawns = selectedPawns.ToList();
            string everyone = ((string)"ConfirmAbandonHomeNegativeThoughts_Everyone".Translate()).TrimEnd(':', ' ');
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (opt.Disabled || opt.action == null)
                    continue;

                var originalAction = opt.action;
                var optLabel = opt.Label;
                opt.action = () =>
                {
                    var jobsBefore = new Dictionary<Pawn, Verse.AI.Job>();
                    var queueCountsBefore = new Dictionary<Pawn, int>();
                    foreach (var p in capturedPawns)
                    {
                        jobsBefore[p] = p.jobs?.curJob;
                        queueCountsBefore[p] = p.jobs?.jobQueue?.Count ?? 0;
                    }

                    // Vanilla's targeter or an external one (Vehicle Framework's TurretTargeter):
                    // either means a new targeting session, so both get the same phrasing.
                    bool targeterWasActive = ExternalMapTargeting.MapTargetingActive;

                    originalAction.Invoke();

                    bool targeterNowActive = ExternalMapTargeting.MapTargetingActive;
                    if (!targeterWasActive && targeterNowActive)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelectOrder.EveryoneDoes".Loc(everyone, optLabel), SpeechPriority.Low);
                        return;
                    }

                    var succeeded = capturedPawns
                        .Where(p =>
                            p.jobs?.curJob != jobsBefore[p] ||
                            (p.jobs?.jobQueue?.Count ?? 0) > queueCountsBefore[p])
                        .ToList();
                    var unchanged = capturedPawns
                        .Where(p =>
                            p.jobs?.curJob == jobsBefore[p] &&
                            (p.jobs?.jobQueue?.Count ?? 0) <= queueCountsBefore[p])
                        .ToList();

                    if (unchanged.Count == 0)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelectOrder.EveryoneDoes".Loc(everyone, optLabel), SpeechPriority.Low);
                    }
                    else if (succeeded.Count == 0)
                    {
                        TolkHelper.Speak("RimWorldAccess.Input.MultiSelectOrder.NoOneCould".Loc(optLabel), SpeechPriority.Low);
                    }
                    else if (unchanged.Count <= succeeded.Count)
                    {
                        string names = MenuHelper.FormatNameList(
                            unchanged.Select(p => p.LabelShort).ToList());
                        TolkHelper.Speak(
                            "RimWorldAccess.Input.MultiSelectOrder.EveryoneExcept".Loc(everyone, names, optLabel),
                            SpeechPriority.Low);
                    }
                    else
                    {
                        string names = MenuHelper.FormatNameList(
                            succeeded.Select(p => p.LabelShort).ToList());
                        TolkHelper.Speak(
                            "RimWorldAccess.Input.MultiSelectOrder.OnlyDoes".Loc(names, optLabel),
                            SpeechPriority.Low);
                    }
                };
            }
        }

        /// <summary>
        /// QA flight recorder line for the bracket keys: the cell, the pawns and their drafted
        /// state, the cell's contents, and every option vanilla produced with its disabled flag.
        /// </summary>
        private static void TraceOrderOptions(string action, IntVec3 cursor, List<Pawn> pawns,
            List<FloatMenuOption> options)
        {
            if (!FlightRecorder.Active)
            {
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append(action).Append(" at ").Append(cursor).Append(" for ");
            for (int i = 0; i < pawns.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(pawns[i].LabelShort)
                  .Append(pawns[i].Drafted ? " (drafted)" : " (undrafted)"); // l10n-exempt: QA trace line, never spoken or shown
            }

            sb.Append("; cell holds ");
            int shown = 0;
            foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(cursor))
            {
                if (shown == TraceThingCap)
                {
                    sb.Append(", ...");
                    break;
                }
                if (shown > 0)
                    sb.Append(", ");
                sb.Append(thing.LabelShort);
                if (thing is Pawn p && p.Downed)
                    sb.Append(" (downed)"); // l10n-exempt: QA trace line, never spoken or shown
                shown++;
            }
            if (shown == 0)
                sb.Append("nothing"); // l10n-exempt: QA trace line, never spoken or shown

            int count = options == null ? 0 : options.Count;
            sb.Append(" -> ").Append(count).Append(" options");
            for (int i = 0; i < count; i++)
            {
                sb.Append(" | ").Append(options[i].Label);
                if (options[i].Disabled)
                    sb.Append(" [disabled]"); // l10n-exempt: QA trace line, never spoken or shown
            }

            FlightRecorder.Record("floatmenu", sb.ToString());
        }

        // A cell under a stockpile can hold dozens of things; one trace line stays one line.
        private const int TraceThingCap = 8;
    }
}
