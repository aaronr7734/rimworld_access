using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The ordered checklist of static per-screen resets for the mod's two session boundaries: a
    /// game starting or loading (<see cref="GameStartPatch"/>, driven by <c>Game.FinalizeInit</c> —
    /// the only reliable hook, since a save load fires no dedicated event) and a return to the main
    /// menu (<see cref="MainMenuAccessibilityPatch"/>, gated on <c>ProgramState.Entry</c>).
    /// A new screen with static session-boundary state MUST add its reset here, in whichever
    /// manifest applies, rather than at either patch site; a leaked <c>IsActive</c> re-pushes a
    /// modal scope forever after. Entries run in the listed order and carry a name so a DEBUG pass
    /// can log which reset ran. Deliberately a hand-reviewed manifest, not self-registration: one
    /// place, ordered, reviewable in a single diff.
    /// </summary>
    public static class StateResetRegistry
    {
        public readonly struct Entry
        {
            public readonly string Name;
            public readonly Action Run;

            public Entry(string name, Action run)
            {
                Name = name;
                Run = run;
            }
        }

        /// <summary>
        /// Fires from <c>Game.FinalizeInit</c> — for both a brand-new game and every loaded
        /// save, on both the new-game and mid-flow load paths. See <see cref="GameStartPatch"/>.
        /// </summary>
        public static readonly Entry[] OnGameLoad =
        {
            new Entry("MultiSelectState.Reset", () => MultiSelectState.Reset()),

            // thingIDNumbers are per-save, so a stale one would diff against a stranger.
            new Entry("FollowedPawnAnnouncer.Forget", () => FollowedPawnAnnouncer.Forget()),
            new Entry("LineFormationState.Reset", () => LineFormationState.Reset()),
            new Entry("GizmoNavigationState.Reset", () => GizmoNavigationState.Reset()),
            new Entry("GizmoDrawCapture.Reset", () => GizmoDrawCapture.Reset()),
            new Entry("WindowlessInspectionState.Reset", () => WindowlessInspectionState.Reset()),
            new Entry("Shell.ScreenReturn.Reset", () => RimWorldAccess.Shell.ScreenReturn.Reset()),
            new Entry("DocsTeacher.ResetSession", () => DocsTeacher.ResetSession()),

            // Pending interests close over per-save state a new/loaded save invalidates; Clear()
            // drops them unfired. Registered signals (process-lifetime probes) survive by design.
            new Entry("AsyncTextStability.Reset", () => AsyncTextStability.Reset()),

            // Close() is silent and idempotent, safe unconditionally.
            new Entry("LearningHelperState.Close", () =>
            {
                if (LearningHelperState.IsActive)
                {
                    LearningHelperState.Close();
                }
            }),

            // Neither research state has a deterministic close, and detail's Close() re-opens the
            // previous project when its navigation stack is non-empty. ResetHard() only clears.
            new Entry("WindowlessResearchMenuState.ResetHard", () =>
            {
                if (WindowlessResearchMenuState.IsActive)
                {
                    WindowlessResearchMenuState.ResetHard();
                }
            }),
            new Entry("WindowlessResearchDetailState.ResetHard", () =>
            {
                if (WindowlessResearchDetailState.IsActive)
                {
                    WindowlessResearchDetailState.ResetHard();
                }
            }),

            // None of the six building-component/zone drill-ins has a deterministic close
            // (Escape-only); all six Close() bodies are silent.
            new Entry("TempControlMenuState.Close", () => { if (TempControlMenuState.IsActive) TempControlMenuState.Close(); }),
            new Entry("RefuelableComponentState.Close", () => { if (RefuelableComponentState.IsActive) RefuelableComponentState.Close(); }),
            new Entry("DoorControlState.Close", () => { if (DoorControlState.IsActive) DoorControlState.Close(); }),
            new Entry("ForbidControlState.Close", () => { if (ForbidControlState.IsActive) ForbidControlState.Close(); }),
            new Entry("PlaySettingsMenuState.Close", () => { if (PlaySettingsMenuState.IsActive) PlaySettingsMenuState.Close(); }),
            new Entry("PlantSelectionMenuState.Close", () => { if (PlantSelectionMenuState.IsActive) PlantSelectionMenuState.Close(); }),

            // Another windowless pause-menu drill-in; Close() is silent.
            new Entry("ModHudToolbarState.Close", () => { if (RimWorldAccess.Shell.ModHudToolbarState.IsActive) RimWorldAccess.Shell.ModHudToolbarState.Close(); }),

            // Three windowless map menus; all three Close() bodies are silent.
            new Entry("BillConfigState.Close", () => { if (BillConfigState.IsActive) BillConfigState.Close(); }),
            new Entry("AreaSelectionMenuState.Close", () => { if (AreaSelectionMenuState.IsActive) AreaSelectionMenuState.Close(); }),
            new Entry("ShapeSelectionMenuState.Close", () => { if (ShapeSelectionMenuState.IsActive) ShapeSelectionMenuState.Close(); }),

            // The targeting family normally closes only via a Targeter.StopTargeting patch, which
            // a mid-flow load never fires. All eight Close() bodies are silent. The last two also
            // self-close lazily in TargetingScopeMirror; these entries add boundary determinism.
            new Entry("AbilityTargetingState.Close", () => { if (AbilityTargetingState.IsActive) AbilityTargetingState.Close(); }),
            new Entry("GenericTargetingState.Close", () => { if (GenericTargetingState.IsActive) GenericTargetingState.Close(); }),
            new Entry("ItemTargetingState.Close", () => { if (ItemTargetingState.IsActive) ItemTargetingState.Close(); }),
            new Entry("JumpTargetingState.Close", () => { if (JumpTargetingState.IsActive) JumpTargetingState.Close(); }),
            new Entry("WorldAbilityTargetingState.Close", () => { if (WorldAbilityTargetingState.IsActive) WorldAbilityTargetingState.Close(); }),
            new Entry("GravshipDestinationState.Close", () => { if (GravshipDestinationState.IsActive) GravshipDestinationState.Close(); }),
            new Entry("TransportPodLaunchState.Close", () => { if (TransportPodLaunchState.IsActive) TransportPodLaunchState.Close(); }),
            new Entry("NewColonyTilePickState.Close", () => { if (NewColonyTilePickState.IsActive) NewColonyTilePickState.Close(); }),

            // ClearSearchSilent() is the silent member: it clears the buffer, search mode and
            // active filter with no TolkHelper call.
            new Entry("ScannerSearchState.ClearSearchSilent", () => { if (ScannerSearchState.IsActive) ScannerSearchState.ClearSearchSilent(); }),
            new Entry("GoToState.Close", () => { if (GoToState.IsActive) GoToState.Close(); }),

            // Both speak on Close() (and ShelfLinkingState clears the game selection), so the
            // boundary uses the silent Reset(); Close() delegates to it so the two cannot drift.
            new Entry("WindowlessInventoryState.Reset", () => { if (WindowlessInventoryState.IsActive) WindowlessInventoryState.Reset(); }),
            new Entry("ShelfLinkingState.Reset", () => { if (ShelfLinkingState.IsActive) ShelfLinkingState.Reset(); }),

            // The remaining modal-scope states below all have silent Close() bodies.
            new Entry("StorageSettingsMenuState.Close", () => { if (StorageSettingsMenuState.IsActive) StorageSettingsMenuState.Close(); }),
            new Entry("RangeEditMenuState.Close", () => { if (RangeEditMenuState.IsActive) RangeEditMenuState.Close(); }),
            new Entry("ThingFilterMenuState.Close", () => { if (ThingFilterMenuState.IsActive) ThingFilterMenuState.Close(); }),
            new Entry("BillsMenuState.Close", () => { if (BillsMenuState.IsActive) BillsMenuState.Close(); }),
            new Entry("EntityTabState.Close", () => { if (EntityTabState.IsActive) EntityTabState.Close(); }),
            new Entry("CashRegisterShiftsState.Close", () => { if (CashRegisterShiftsState.IsActive) CashRegisterShiftsState.Close(); }),

            // FactionTab speaks on Close() and has no other deterministic close path, so the
            // silent ResetHard() is used. Mech's Close() reopens gizmo navigation, wrong at a
            // session boundary, so it needs ResetHard() too. Styling's Close() is silent (its
            // announcement lives in StylingStationPatch's PostClose), and PostClose fires on every
            // real close path, so this call is belt-and-suspenders rather than the only guard.
            new Entry("FactionTabState.ResetHard", () => { if (FactionTabState.IsActive) FactionTabState.ResetHard(); }),
            new Entry("MechControlGroupState.ResetHard", () => { if (MechControlGroupState.IsActive) MechControlGroupState.ResetHard(); }),
            new Entry("StylingStationState.Close", () => { if (StylingStationState.IsActive) StylingStationState.Close(); }),

            // StatBreakdownState speaks on Close() and has no other deterministic close, so the
            // silent ResetHard() is used. The two menu states' Close() bodies are already silent.
            // SliderDialogState needs no entry: SliderDialogPatch gives it a deterministic
            // PostClose.
            new Entry("StatBreakdownState.ResetHard", () => { if (StatBreakdownState.IsActive) StatBreakdownState.ResetHard(); }),
            // Without this the skills window redraws the previous session's dead pawns.
            new Entry("PawnSkillsTableState.ResetHard", () => { if (PawnSkillsTableState.IsActive) PawnSkillsTableState.ResetHard(); }),
            new Entry("QuantityMenuState.Close", () => { if (QuantityMenuState.IsActive) QuantityMenuState.Close(); }),
            new Entry("GearEquipMenuState.Close", () => { if (GearEquipMenuState.IsActive) GearEquipMenuState.Close(); }),

            // Every Open() site is call-site-paired with an ExecuteSelected/Cancel, so this is
            // belt-and-suspenders. Close() is the silent member: Cancel() plays a sound and would
            // run a registered onCloseCallback against the previous session's objects.
            new Entry("WindowlessFloatMenuState.Close", () => { if (WindowlessFloatMenuState.IsActive) WindowlessFloatMenuState.Close(); }),

            // Close(announce: false) is fully silent. PendingSelectQuest is cleared
            // unconditionally: a quest link can arm it without the tab ever opening, and a stale
            // request would land the next tab open on a quest that no longer exists.
            new Entry("QuestMenuState.Close", () => { QuestMenuState.PendingSelectQuest = null; if (QuestMenuState.IsActive) QuestMenuState.Close(announce: false); }),

            // ResetHard() rather than Close(), which speaks Selection.Cancelled.
            new Entry("TransportPodSelectionState.ResetHard", () => { if (TransportPodSelectionState.IsActive) TransportPodSelectionState.ResetHard(); }),

            // ResetHard() rather than Close(), which plays TabClose.
            new Entry("WorldObjectSelectionState.ResetHard", () => { if (WorldObjectSelectionState.IsActive) WorldObjectSelectionState.ResetHard(); }),

            // The Archonexus family's only other resets are two Window.PostClose postfixes, which
            // a mid-flow save-load or quit-to-menu bypasses. Both Close() bodies are silent.
            new Entry("ArchonexusColonyState.Close", () => { if (ArchonexusColonyState.IsActive) ArchonexusColonyState.Close(); }),
            new Entry("ArchonexusReformIdeoState.Close", () => { if (ArchonexusReformIdeoState.IsActive) ArchonexusReformIdeoState.Close(); }),

            // The IdeoBuilder hosts close only via their own Window.PostClose postfixes and the
            // overlays only via their own Escape handler, all of which a save-load bypasses; a
            // stale flag misroutes ArchonexusIdeoScreenScope and lets DialogInterceptionPatch
            // hijack every FloatMenu in the game. All seven Close() bodies are silent. The overlay
            // closes go through CloseAllOverlayEditors(), which every host's PostClose also calls.
            new Entry("IdeoBuilderHubState.Close", () => { if (IdeoBuilderHubState.IsActive) IdeoBuilderHubState.Close(); }),
            new Entry("IdeoReformState.Close", () => { if (IdeoReformState.IsActive) IdeoReformState.Close(); }),
            new Entry("IdeoMemeSelectionState.Close", () => { if (IdeoMemeSelectionState.IsActive) IdeoMemeSelectionState.Close(); }),
            new Entry("IdeoLoadState.Close", () => { if (IdeoLoadState.IsActive) IdeoLoadState.Close(); }),
            new Entry("IdeoBuilderOverlays.CloseAllOverlayEditors", () => IdeoBuilderOverlays.CloseAllOverlayEditors()),

            // Loading a save from in-game never passes through the main menu or a
            // CurrentMap == null frame, so no other reset path fires. The old session's Things are
            // never despawned (ClearAllMapsAndWorld just drops them) and still report Spawned, and
            // the reloaded map keeps its saved uniqueID — so stale caches pass every liveness and
            // map-identity check, and the scanner's thing-state hash (a spawn/despawn counter) can
            // collide between two saves of the same colony.
            new Entry("MapNavigationState.Reset", () => MapNavigationState.Reset()), // also invalidates ScannerState + ScannerHelper caches
            new Entry("PawnSelectionState.Reset", () => PawnSelectionState.Reset()),
            new Entry("WorldScannerState.Reset", () => WorldScannerState.Reset()),
            new Entry("PlantTargetingState.Reset", () => PlantTargetingState.Reset()),
            // MapNavigationState.Reset above already dropped the scanner category itself, so this
            // only clears the tracking fields pointing at the old designator and cells.
            new Entry("PlacementSpotScanner.Reset", () => PlacementSpotScanner.Reset()),

            // The focus stack must not carry scopes across a session boundary.
            new Entry("Shell.FocusStack.ClearToBase", () => RimWorldAccess.Shell.FocusStack.ClearToBase(RimWorldAccess.Shell.GameBoundary.GameStart)),
            new Entry("Shell.ScopeForWindow.Reset", () => RimWorldAccess.Shell.ScopeForWindow.Reset()),

            // Inspect-tab capture service: clears the pending armed activation,
            // pending captures, and the cross-session capture cache (keyed on
            // last session's Things/Zones). FinalizeInit is the canonical reset.
            new Entry("InspectTabCaptureService.ResetSession", () => InspectTabCaptureService.ResetSession()),

            // Configure Spoken Announcements screen: the working (unsaved) format session is
            // reachable from the main menu's own Options surface, so it can be live across a
            // "start new game"/"load game" transition too. The dialog's own PreClose is the
            // primary, deterministic clear on every real close path; this is belt-and-suspenders
            // (the WindowlessFloatMenuState.Close precedent above) — Clear() is silent and
            // idempotent.
            new Entry("AnnouncementFormatSession.Clear", () =>
            {
                if (RimWorldAccess.Shell.AnnouncementFormatSession.IsActive)
                {
                    RimWorldAccess.Shell.AnnouncementFormatSession.Clear();
                }
            }),

            // The Narrative Feed's ring buffer and
            // dedupe-key set are static session state with no other reset path --
            // a stale dedupe key surviving a save-load could silently drop the
            // first repeat of a genuinely new line, and stale ring entries would
            // mix two colonies' dialogue in the Dialogue Log. Reset() is silent.
            new Entry("NarrativeFeed.Reset", () => NarrativeFeed.Reset()),

            // RimTalk's ApiHistory (the player-typed-line
            // producer's data source) is wiped by RimTalk itself on every new game/load, but
            // this producer's own "already seen this ApiLog guid" HashSet has no such reset --
            // harmless if left stale (old guids just never match new entries) but explicit per
            // this registry's own doctrine of never relying on incidental correctness.
            new Entry("RimTalkNarrativeCompat.Reset", () => RimTalkNarrativeCompat.Reset()),
        };

        /// <summary>
        /// Fires every frame <c>MainMenuDrawer.DoMainMenuControls</c> draws while
        /// <c>Current.ProgramState == ProgramState.Entry</c>, which covers the main menu screen and
        /// the whole pregame/chargen flow, so every entry re-checks the program state itself.
        /// See <see cref="MainMenuAccessibilityPatch"/>. Entries mirroring an
        /// <see cref="OnGameLoad"/> twin choose the same silent method for the same reason; a save
        /// load never fires this patch, and quitting to the menu never fires that one.
        /// </summary>
        public static readonly Entry[] OnReturnToMainMenu =
        {
            // Returning to the main menu tears down the world, but the world map's
            // navigation/scanner state has no other reset hook (WorldInterface's own stops
            // running), so its keyboard handlers would keep eating main-menu input. Gated on
            // Entry plus InGame because DoMainMenuControls also runs for the in-game Escape
            // overlay, where a live world map must not be disrupted.
            new Entry("WorldNavigationState.Close+WorldScannerState.Reset", () =>
            {
                if (Current.ProgramState == ProgramState.Entry &&
                    WorldNavigationState.IsActive &&
                    WorldNavigationState.Context == WorldNavContext.InGame)
                {
                    WorldNavigationState.Close();
                    WorldScannerState.Reset();
                }
            }),

            new Entry("GizmoNavigationState.Reset", () =>
            {
                if (Current.ProgramState == ProgramState.Entry && GizmoNavigationState.IsActive)
                {
                    GizmoNavigationState.Reset();
                }
            }),
            new Entry("GizmoDrawCapture.Reset", () =>
            {
                if (Current.ProgramState == ProgramState.Entry)
                {
                    GizmoDrawCapture.Reset();
                }
            }),
            new Entry("WindowlessInspectionState.Reset", () =>
            {
                if (Current.ProgramState == ProgramState.Entry && WindowlessInspectionState.IsActive)
                {
                    WindowlessInspectionState.Reset();
                }
            }),
            new Entry("Shell.ScreenReturn.Reset", () =>
            {
                if (Current.ProgramState == ProgramState.Entry)
                {
                    RimWorldAccess.Shell.ScreenReturn.Reset();
                }
            }),

            new Entry("LearningHelperState.Close", () =>
            {
                if (Current.ProgramState == ProgramState.Entry && LearningHelperState.IsActive)
                {
                    LearningHelperState.Close();
                }
            }),

            new Entry("WindowlessResearchMenuState.ResetHard", () =>
            {
                if (Current.ProgramState == ProgramState.Entry && WindowlessResearchMenuState.IsActive)
                {
                    WindowlessResearchMenuState.ResetHard();
                }
            }),
            new Entry("WindowlessResearchDetailState.ResetHard", () =>
            {
                if (Current.ProgramState == ProgramState.Entry && WindowlessResearchDetailState.IsActive)
                {
                    WindowlessResearchDetailState.ResetHard();
                }
            }),

            new Entry("TempControlMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && TempControlMenuState.IsActive) TempControlMenuState.Close(); }),
            new Entry("RefuelableComponentState.Close", () => { if (Current.ProgramState == ProgramState.Entry && RefuelableComponentState.IsActive) RefuelableComponentState.Close(); }),
            new Entry("DoorControlState.Close", () => { if (Current.ProgramState == ProgramState.Entry && DoorControlState.IsActive) DoorControlState.Close(); }),
            new Entry("ForbidControlState.Close", () => { if (Current.ProgramState == ProgramState.Entry && ForbidControlState.IsActive) ForbidControlState.Close(); }),
            new Entry("PlaySettingsMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && PlaySettingsMenuState.IsActive) PlaySettingsMenuState.Close(); }),
            new Entry("PlantSelectionMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && PlantSelectionMenuState.IsActive) PlantSelectionMenuState.Close(); }),
            new Entry("ModHudToolbarState.Close", () => { if (Current.ProgramState == ProgramState.Entry && RimWorldAccess.Shell.ModHudToolbarState.IsActive) RimWorldAccess.Shell.ModHudToolbarState.Close(); }),

            new Entry("BillConfigState.Close", () => { if (Current.ProgramState == ProgramState.Entry && BillConfigState.IsActive) BillConfigState.Close(); }),
            new Entry("AreaSelectionMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && AreaSelectionMenuState.IsActive) AreaSelectionMenuState.Close(); }),
            new Entry("ShapeSelectionMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && ShapeSelectionMenuState.IsActive) ShapeSelectionMenuState.Close(); }),

            new Entry("AbilityTargetingState.Close", () => { if (Current.ProgramState == ProgramState.Entry && AbilityTargetingState.IsActive) AbilityTargetingState.Close(); }),
            new Entry("GenericTargetingState.Close", () => { if (Current.ProgramState == ProgramState.Entry && GenericTargetingState.IsActive) GenericTargetingState.Close(); }),
            new Entry("ItemTargetingState.Close", () => { if (Current.ProgramState == ProgramState.Entry && ItemTargetingState.IsActive) ItemTargetingState.Close(); }),
            new Entry("JumpTargetingState.Close", () => { if (Current.ProgramState == ProgramState.Entry && JumpTargetingState.IsActive) JumpTargetingState.Close(); }),
            new Entry("WorldAbilityTargetingState.Close", () => { if (Current.ProgramState == ProgramState.Entry && WorldAbilityTargetingState.IsActive) WorldAbilityTargetingState.Close(); }),
            new Entry("GravshipDestinationState.Close", () => { if (Current.ProgramState == ProgramState.Entry && GravshipDestinationState.IsActive) GravshipDestinationState.Close(); }),
            new Entry("TransportPodLaunchState.Close", () => { if (Current.ProgramState == ProgramState.Entry && TransportPodLaunchState.IsActive) TransportPodLaunchState.Close(); }),
            new Entry("NewColonyTilePickState.Close", () => { if (Current.ProgramState == ProgramState.Entry && NewColonyTilePickState.IsActive) NewColonyTilePickState.Close(); }),

            new Entry("ScannerSearchState.ClearSearchSilent", () => { if (Current.ProgramState == ProgramState.Entry && ScannerSearchState.IsActive) ScannerSearchState.ClearSearchSilent(); }),
            new Entry("GoToState.Close", () => { if (Current.ProgramState == ProgramState.Entry && GoToState.IsActive) GoToState.Close(); }),

            // Both Close() bodies announce, so the silent Reset() is used instead.
            new Entry("WindowlessInventoryState.Reset", () => { if (Current.ProgramState == ProgramState.Entry && WindowlessInventoryState.IsActive) WindowlessInventoryState.Reset(); }),
            new Entry("ShelfLinkingState.Reset", () => { if (Current.ProgramState == ProgramState.Entry && ShelfLinkingState.IsActive) ShelfLinkingState.Reset(); }),

            new Entry("StorageSettingsMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && StorageSettingsMenuState.IsActive) StorageSettingsMenuState.Close(); }),

            new Entry("RangeEditMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && RangeEditMenuState.IsActive) RangeEditMenuState.Close(); }),
            new Entry("ThingFilterMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && ThingFilterMenuState.IsActive) ThingFilterMenuState.Close(); }),

            new Entry("BillsMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && BillsMenuState.IsActive) BillsMenuState.Close(); }),
            new Entry("EntityTabState.Close", () => { if (Current.ProgramState == ProgramState.Entry && EntityTabState.IsActive) EntityTabState.Close(); }),
            new Entry("CashRegisterShiftsState.Close", () => { if (Current.ProgramState == ProgramState.Entry && CashRegisterShiftsState.IsActive) CashRegisterShiftsState.Close(); }),

            // Same method choices as the OnGameLoad twins: ResetHard() for the two whose
            // Close() speaks or reopens gizmo navigation, Close() for Styling.
            new Entry("FactionTabState.ResetHard", () => { if (Current.ProgramState == ProgramState.Entry && FactionTabState.IsActive) FactionTabState.ResetHard(); }),
            new Entry("MechControlGroupState.ResetHard", () => { if (Current.ProgramState == ProgramState.Entry && MechControlGroupState.IsActive) MechControlGroupState.ResetHard(); }),
            new Entry("StylingStationState.Close", () => { if (Current.ProgramState == ProgramState.Entry && StylingStationState.IsActive) StylingStationState.Close(); }),

            // ResetHard() for StatBreakdownState, which speaks on Close(); the other two are
            // already silent. SliderDialogState needs no entry (deterministic PostClose).
            new Entry("StatBreakdownState.ResetHard", () => { if (Current.ProgramState == ProgramState.Entry && StatBreakdownState.IsActive) StatBreakdownState.ResetHard(); }),
            new Entry("PawnSkillsTableState.ResetHard", () => { if (Current.ProgramState == ProgramState.Entry && PawnSkillsTableState.IsActive) PawnSkillsTableState.ResetHard(); }),
            new Entry("QuantityMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && QuantityMenuState.IsActive) QuantityMenuState.Close(); }),
            new Entry("GearEquipMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && GearEquipMenuState.IsActive) GearEquipMenuState.Close(); }),

            new Entry("WindowlessFloatMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry && WindowlessFloatMenuState.IsActive) WindowlessFloatMenuState.Close(); }),

            new Entry("QuestMenuState.Close", () => { if (Current.ProgramState == ProgramState.Entry) { QuestMenuState.PendingSelectQuest = null; if (QuestMenuState.IsActive) QuestMenuState.Close(announce: false); } }),

            new Entry("TransportPodSelectionState.ResetHard", () => { if (Current.ProgramState == ProgramState.Entry && TransportPodSelectionState.IsActive) TransportPodSelectionState.ResetHard(); }),

            new Entry("WorldObjectSelectionState.ResetHard", () => { if (Current.ProgramState == ProgramState.Entry && WorldObjectSelectionState.IsActive) WorldObjectSelectionState.ResetHard(); }),

            // Quitting to the main menu bypasses the family's two Window.PostClose postfixes,
            // its only other resets. Both Close() bodies are silent.
            new Entry("ArchonexusColonyState.Close", () => { if (Current.ProgramState == ProgramState.Entry && ArchonexusColonyState.IsActive) ArchonexusColonyState.Close(); }),
            new Entry("ArchonexusReformIdeoState.Close", () => { if (Current.ProgramState == ProgramState.Entry && ArchonexusReformIdeoState.IsActive) ArchonexusReformIdeoState.Close(); }),

            // Quitting to the main menu bypasses both the hosts' PostClose postfixes and the
            // overlays' Escape handler. All eight Close() bodies are silent.
            new Entry("IdeoBuilderHubState.Close", () => { if (Current.ProgramState == ProgramState.Entry && IdeoBuilderHubState.IsActive) IdeoBuilderHubState.Close(); }),
            new Entry("IdeoReformState.Close", () => { if (Current.ProgramState == ProgramState.Entry && IdeoReformState.IsActive) IdeoReformState.Close(); }),
            new Entry("IdeoMemeSelectionState.Close", () => { if (Current.ProgramState == ProgramState.Entry && IdeoMemeSelectionState.IsActive) IdeoMemeSelectionState.Close(); }),
            new Entry("IdeoLoadState.Close", () => { if (Current.ProgramState == ProgramState.Entry && IdeoLoadState.IsActive) IdeoLoadState.Close(); }),
            new Entry("IdeoBuilderOverlays.CloseAllOverlayEditors", () => { if (Current.ProgramState == ProgramState.Entry) IdeoBuilderOverlays.CloseAllOverlayEditors(); }),

            new Entry("InspectTabCaptureService.ResetSession", () => { if (Current.ProgramState == ProgramState.Entry) InspectTabCaptureService.ResetSession(); }),

            new Entry("AnnouncementFormatSession.Clear", () =>
            {
                if (Current.ProgramState == ProgramState.Entry && RimWorldAccess.Shell.AnnouncementFormatSession.IsActive)
                {
                    RimWorldAccess.Shell.AnnouncementFormatSession.Clear();
                }
            }),

            new Entry("AsyncTextStability.Reset", () =>
            {
                if (Current.ProgramState == ProgramState.Entry)
                {
                    AsyncTextStability.Reset();
                }
            }),
        };

        public static void RunOnGameLoad()
        {
            for (int i = 0; i < OnGameLoad.Length; i++)
            {
                OnGameLoad[i].Run();
            }
        }

        public static void RunOnReturnToMainMenu()
        {
            for (int i = 0; i < OnReturnToMainMenu.Length; i++)
            {
                OnReturnToMainMenu[i].Run();
            }
        }
    }
}
