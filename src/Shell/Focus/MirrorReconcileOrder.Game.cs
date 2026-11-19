using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One entry in the dispatcher's mirror-reconciliation sequence: a name (diagnostics only)
    /// and the parameterless Reconcile call it wraps.
    /// </summary>
    internal readonly struct MirrorReconcileEntry
    {
        public readonly string Name;
        public readonly Action Reconcile;

        public MirrorReconcileEntry(string name, Action reconcile)
        {
            Name = name;
            Reconcile = reconcile;
        }
    }

    /// <summary>
    /// The dispatcher's per-frame mirror-reconciliation sequence, as one explicitly-ordered array.
    /// A flat, hand-maintained total order, not a dependency solver: later entries Push their scope
    /// above earlier ones whenever both are simultaneously live, so an entry's position relative to
    /// its neighbors decides which scope wins the keys they both claim.
    /// Adding an entry: if your scope can be live at the same time as an existing one, place yours on
    /// the side that must win the shared keys (later wins); otherwise state in one line why it can
    /// never coexist ("chain-independent") and place it beside its screen family. Never reorder
    /// existing entries for tidiness without re-deriving each moved entry's justification.
    /// </summary>
    internal static class MirrorReconcileOrder
    {
        public static readonly MirrorReconcileEntry[] Entries =
        {
            // Safety net: detaches any ScopeForWindow-attached scope whose window left
            // Find.WindowStack by a path that bypassed Add/TryRemove's postfixes. First of all, so a
            // stale attachment never shadows this frame's dispatch.
            new MirrorReconcileEntry("ScopeForWindow.ReconcileLiveness", ScopeForWindow.ReconcileLiveness),

            // The attach half of the same sweep: re-attaches the generic-reader scope for an
            // absorbing modal that opened in a context which raced the session-boundary reset and so
            // never kept its Add-time scope. Before every screen mirror below, so the replayed Push
            // lands the modal scope on top exactly as Add-time would.
            new MirrorReconcileEntry("ScopeForWindow.ReconcileAttachments", ScopeForWindow.ReconcileAttachments),

            // Heals window/state drift for the eight main tabs a windowless scope drives (a mouse
            // click closes the tab; a state closes without its window). Before every screen mirror
            // below, so a state closed here has its scope popped in the same pass.
            new MirrorReconcileEntry("MainTabWindowLink", MainTabWindowLink.Reconcile),

            // The inspect pane is the windowless inspection tree's visual twin. After
            // MainTabWindowLink, which settles the main tab this must read, and before every screen
            // mirror below, so the pane state it writes is what this frame draws.
            new MirrorReconcileEntry("InspectPaneLink", InspectPaneLink.Reconcile),

            new MirrorReconcileEntry("AmbientScopeSelector", AmbientScopeSelector.Reconcile),

            // Order-independent: pushes no scope and claims no keys, only announcing the dev-mode
            // debug tool's arm/disarm transitions. Its apply/cancel claims live ambiently on
            // MapScope/WorldScope.
            new MirrorReconcileEntry("DevToolMirror", DevToolTargeting.Reconcile),

            new MirrorReconcileEntry("TextSessionScopeMirror", TextSessionScopeMirror.Reconcile),

            // Before MapOverlayScopeMirror: Go To / colony scanner-search must win the keys they
            // share with targeting whenever both are somehow active.
            new MirrorReconcileEntry("TargetingScopeMirror", TargetingScopeMirror.Reconcile),

            // Before PlacementScopeMirror: both scopes can be live at once (a door placed from a
            // viewing-mode gizmo) and PlacementScope's gate deliberately does not yield, so placement
            // must win the overlapping keys while ViewingModeScope still catches what placement does
            // not claim (e.g. Tab).
            new MirrorReconcileEntry("ViewingModeScopeMirror", ViewingModeScopeMirror.Reconcile),

            // Before MapOverlayScopeMirror: Go To / colony scanner-search re-float above this scope
            // whenever either is live.
            new MirrorReconcileEntry("PlacementScopeMirror", PlacementScopeMirror.Reconcile),

            // Before MapOverlayScopeMirror: RoutePlannerScope's Enter/Escape claims must lose to
            // ScannerSearchScope's whenever a scanner search is active over an open route planner.
            // Only Enter/Escape need this; Space and E/R do not.
            new MirrorReconcileEntry("RoutePlannerScopeMirror", RoutePlannerScopeMirror.Reconcile),

            // Vehicle Framework's standalone route planner, before MapOverlayScopeMirror for the same
            // reason. Order relative to the vanilla planner is irrelevant — the two are mutually
            // exclusive.
            new MirrorReconcileEntry("VfRoutePlannerScopeMirror", VfRoutePlannerScopeMirror.Reconcile),

            new MirrorReconcileEntry("MapOverlayScopeMirror", MapOverlayScopeMirror.Reconcile),

            // After PlacementScopeMirror: opening the shape menu does not clear the underlying
            // designator selection, so this modal scope must re-float above PlacementScope each frame.
            new MirrorReconcileEntry("ShapeSelectionScopeMirror", ShapeSelectionScopeMirror.Reconcile),

            // Chain-independent: each opener is reachable only through the gizmo navigation scope,
            // neither nests inside another overlay, and neither touches
            // Find.Targeter/WorldTargeter/TilePicker.
            new MirrorReconcileEntry("TransportPodSelectionScopeMirror", TransportPodSelectionScopeMirror.Reconcile),

            new MirrorReconcileEntry("ShelfLinkingScopeMirror", ShelfLinkingScopeMirror.Reconcile),

            // Chain-independent: the sole opener is the ambient map.architect.toggle claim, and the
            // tree-to-placement handoff always closes this scope first.
            new MirrorReconcileEntry("ArchitectTreeScopeMirror", ArchitectTreeScopeMirror.Reconcile),

            // Chain-independent of the bills/storage mirrors below.
            new MirrorReconcileEntry("InventoryScopeMirror", InventoryScopeMirror.Reconcile),

            // Chain-independent: never coexists with another migrated menu family.
            new MirrorReconcileEntry("PawnAreaMenuScopeMirror", PawnAreaMenuScopeMirror.Reconcile),

            // Chain-independent: its only opener already excludes every other menu.
            new MirrorReconcileEntry("PawnSkillsTableScopeMirror", PawnSkillsTableScopeMirror.Reconcile),

            // Before GizmoScopeMirror: gizmo must outrank inspection, which is what lets
            // Ctrl+Alt+Enter coexist across the two.
            new MirrorReconcileEntry("InspectionScopeMirror", InspectionScopeMirror.Reconcile),

            // The five building-component/zone drill-ins (TempControl/Refuelable/Door/Forbid/
            // PlantSelection), immediately after InspectionScopeMirror: all five open FROM the
            // inspection tree without closing it, so stack order alone gives them the keyboard. Each
            // scope's own gate additionally stands down for the info card.
            new MirrorReconcileEntry("InspectComponentScopeMirror", InspectComponentScopeMirror.Reconcile),

            // Chain-independent: the shared mod-HUD-toolbar menu's only opener is the pause menu's
            // injected item, which closes the real menu window before Open() runs.
            new MirrorReconcileEntry("ModHudToolbarScopeMirror", ModHudToolbarScopeMirror.Reconcile),

            // The four tab-attached inspect-pane scopes, immediately after InspectionScopeMirror: all
            // four open FROM the inspection tree without closing it, so stack order alone gives them
            // the keyboard. They are mutually exclusive with each other.
            new MirrorReconcileEntry("EntityTabScopeMirror", EntityTabScopeMirror.Reconcile),

            new MirrorReconcileEntry("FishingZoneScopeMirror", FishingZoneScopeMirror.Reconcile),

            new MirrorReconcileEntry("HealthTabScopeMirror", HealthTabScopeMirror.Reconcile),

            new MirrorReconcileEntry("PrisonerTabScopeMirror", PrisonerTabScopeMirror.Reconcile),

            // Same tab-attached family; order relative to the others never matters.
            new MirrorReconcileEntry("GeneInspectionScopeMirror", GeneInspectionScopeMirror.Reconcile),

            new MirrorReconcileEntry("CashRegisterShiftsScopeMirror", CashRegisterShiftsScopeMirror.Reconcile),

            new MirrorReconcileEntry("GizmoScopeMirror", GizmoScopeMirror.Reconcile),

            // After GizmoScopeMirror: a mouse click on the Wildlife main button can open this screen
            // over an active gizmo browse session, and this screen must win.
            new MirrorReconcileEntry("WildlifeScopeMirror", WildlifeScopeMirror.Reconcile),

            // Same rule as Wildlife: after gizmo. Order among Wildlife/Animals/Mechs never matters —
            // only one of these F4-family screens can be open at a time.
            new MirrorReconcileEntry("AnimalsScopeMirror", AnimalsScopeMirror.Reconcile),

            new MirrorReconcileEntry("MechsScopeMirror", MechsScopeMirror.Reconcile),

            // Same family, after gizmo. Order relative to Wildlife/Animals/Mechs and to each other
            // never matters; WorkMenuState/WorkTableState.IsActive are mutually exclusive by
            // construction.
            new MirrorReconcileEntry("WorkMenuScopeMirror", WorkMenuScopeMirror.Reconcile),

            new MirrorReconcileEntry("WorkTableScopeMirror", WorkTableScopeMirror.Reconcile),

            // Same family, after gizmo: assign wins the keyboard when the mouse-click path leaves both
            // simultaneously active.
            new MirrorReconcileEntry("AssignMenuScopeMirror", AssignMenuScopeMirror.Reconcile),

            new MirrorReconcileEntry("BillsScopeMirror", BillsScopeMirror.Reconcile),

            new MirrorReconcileEntry("BillConfigScopeMirror", BillConfigScopeMirror.Reconcile),

            new MirrorReconcileEntry("ThingFilterMenuScopeMirror", ThingFilterMenuScopeMirror.Reconcile),

            new MirrorReconcileEntry("StorageSettingsScopeMirror", StorageSettingsScopeMirror.Reconcile),

            new MirrorReconcileEntry("RangeEditScopeMirror", RangeEditScopeMirror.Reconcile),

            // Chain-independent: the two history sub-tab scopes are mutually exclusive with each other
            // (HistoryState keeps exactly one sub-tab open) and never simultaneously live with any
            // other mirrored scope. The one live-window scope History coexists with is HistoryScope,
            // which this Push always lands above regardless of array position.
            new MirrorReconcileEntry("HistorySubTabScopeMirror", HistorySubTabScopeMirror.Reconcile),

            // Chain-independent: the notification menu is reached only through its own ambient L
            // opener, never nested inside another menu and never a launch point for one.
            new MirrorReconcileEntry("NotificationScopeMirror", NotificationScopeMirror.Reconcile),

            // Chain-independent: the only keyboard openers are the ambient map.menu.research claim and
            // EntityCodexState's drill-in, which EntityCodexScope's own IsLive term stands down for.
            // Menu is pushed first and detail second, so detail lands above when both are active.
            new MirrorReconcileEntry("ResearchScopeMirror", ResearchScopeMirror.Reconcile),

            // Chain-independent: the sole keyboard opener is the ambient map.menu.quests claim, gated
            // off under any live modal scope, and NotificationMenuState's "View Quest" button closes
            // itself before opening this.
            new MirrorReconcileEntry("QuestMenuScopeMirror", QuestMenuScopeMirror.Reconcile),

            // Chain-independent: FactionTab/IdeologyTab open only from their own DoWindowContents
            // hijacks and MechControlGroupState only from GizmoNavigationState's close-before-open
            // handoff; the three are mutually exclusive by construction.
            new MirrorReconcileEntry("FullScreenTabScopeMirror", FullScreenTabScopeMirror.Reconcile),

            // Chain-independent: the sole opener is WorldScope's ambient world.object.select claim,
            // this screen never nests inside another menu, and the single-object auto-open path never
            // sets WorldObjectSelectionState.IsActive at all.
            new MirrorReconcileEntry("WorldObjectSelectionScopeMirror", WorldObjectSelectionScopeMirror.Reconcile),

            // Tail-placed but NOT chain-independent: this mirror's push condition stands down while
            // WindowlessInspectionState is active.
            new MirrorReconcileEntry("CaravanInspectScopeMirror", CaravanInspectScopeMirror.Reconcile),

            // After CaravanInspectScopeMirror: StatBreakdownScope/QuantityMenuScope are opened from
            // within a caravan-cluster dialog, so this later Push re-floats them above whichever
            // parent is open. The window-attached caravan-cluster scopes need no entry here at all —
            // ScopeForWindow pushes each on its window's Add, so the parent is already beneath.
            new MirrorReconcileEntry("CaravanOverlayScopeMirror", CaravanOverlayScopeMirror.Reconcile),

            // The three ideo host scopes need no mirror call: each is window-attached via
            // ScopeForWindow with no per-frame ritual-sound or float-menu bookkeeping of its own
            // (IdeoBuilder's ritual-preview upkeep stays in the residual IdeoBuilderHubPatch prefix).
            // The four windowless IdeoBuilder overlay editors share one mirror across all three hosts:
            // it must land above those hosts (it does, being immediately below them) and below
            // FloatMenuOverlayScopeMirror (it does), so a sub-picker float menu always lands above
            // whichever overlay opened it.
            new MirrorReconcileEntry("IdeoOverlayScopesMirror", IdeoOverlayScopesMirror.Reconcile),

            // Only the three windowless scenario overlays (add-part/save/load) ride a mirror; the
            // editor page itself is the window-attached ScenarioEditorScreenScope. Stands down while
            // any real window sits above the editor page (ScenarioScopeGuards), so it never re-floats
            // above MessageBoxScope on the confirm boxes. Chain-independent otherwise: Entry-only, and
            // no other mirrored state can be active with it.
            new MirrorReconcileEntry("ScenarioOverlayScopeMirror", ScenarioOverlayScopeMirror.Reconcile),

            // The MODAL consume-all add menu on Page_CreateWorldParams's factions section. Real
            // windows CAN stack over it (the page stays mouse-interactive), so the mirror stands down
            // under any real window rather than re-floating above its scope every pass.
            // Chain-independent otherwise; its only ordering need is to float above the
            // window-attached WorldParamsScope, which this later Push satisfies.
            new MirrorReconcileEntry("WorldParamsAddFactionScopeMirror", WorldParamsAddFactionScopeMirror.Reconcile),

            // Pawn-editor overlay family: the filter editor, its three preset overlays and the reroll
            // blackout, pushed filter-lowest and reroll-on-top. The filter-family gates fold the
            // PawnScopeGuards page-drivable walk; reroll is bare IsActive. Chain-independent: the
            // family anchors to the chargen page (Entry) or the wanderers dialog (Playing, absorbing +
            // forcePause), so no map overlay state can be active with it; its only ordering need is to
            // float above the window-attached StartingPawnScope.
            new MirrorReconcileEntry("PawnOverlayScopeMirror", PawnOverlayScopeMirror.Reconcile),

            // The Learning Helper / docs reader, a consume-all overlay, immediately before
            // WhatsNewScopeMirror so it floats above every scope above. Mutually unreachable with
            // What's New.
            new MirrorReconcileEntry("LearningHelperScopeMirror", LearningHelperScopeMirror.Reconcile),

            // Last among the ordinary mirrors: the What's New reader is a consume-all overlay that
            // must re-float above every mirrored and window-attached scope whenever it is open. Runs
            // in Entry AND Playing; its flag is a plain bool, so it is Entry-safe.
            new MirrorReconcileEntry("WhatsNewScopeMirror", WhatsNewScopeMirror.Reconcile),

            // After every screen and overlay mirror, and only before the float-menu keystone below: an
            // armed map tool must outrank the screen it was armed from (Character Editor's editor
            // window stays open while the player aims), but a float menu raised on top of an armed
            // tool is still the thing the player is answering. See MapToolScope.
            new MirrorReconcileEntry("MapToolScopeMirror", MapToolScopeMirror.Reconcile),

            // ABSOLUTE LAST OF ALL MIRRORS (THE KEYSTONE): a windowless float menu wins over
            // everything, and this last Push is what gives it that rank structurally. Neither What's
            // New nor the Learning Helper can be open at the same time as one, so there is no real
            // ordering conflict with either.
            new MirrorReconcileEntry("FloatMenuOverlayScopeMirror", FloatMenuOverlayScopeMirror.Reconcile),
        };
    }
}
