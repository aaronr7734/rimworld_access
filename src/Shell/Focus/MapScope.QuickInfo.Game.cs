using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Ambient map claims: the Alt+letter quick-info cluster, the pawn skills table, the
    /// area-assignment picker, the rename-pawn opener, and the full-menu openers (F1-F7, F12, L).
    /// These are always-available actions with no mode of their own, so they sit directly on
    /// <see cref="MapScope"/> beside the cursor hotkeys.
    ///
    /// Every when-guard here carries two terms that are easy to mistake for redundancy:
    /// <see cref="ShellGuards.MenuOwnsInput"/>, because an ambient claim has no position in a
    /// dispatch order and so needs the explicit stand-down a positioned handler got for free (this
    /// also keeps quick-info silent during gizmo browse); and !StatBreakdownState.IsActive, which is
    /// NOT a member of that predicate yet does consume Alt+letter chords — its HandleInput forwards
    /// unmatched keys to a TreeNavigationHelper whose catch-all tail is
    /// <c>key != KeyCode.None</c>. The other consume-all overlays each fall through unmatched keys
    /// explicitly, so they need no term of their own.
    ///
    /// Every claim registered here is ALSO registered on <see cref="WorldScope"/>, which is why this
    /// method exists as the single registrar rather than a duplicated list:
    /// AmbientScopeSelector.Reconcile swaps the focus-stack base to WorldScope whenever the planet is
    /// rendered, removing MapScope from the stack, so a MapScope-only registration would be dead on
    /// the world view. Each opener below therefore carries its own world hide-dance
    /// (CameraJumper.TryHideWorld + MapNavigationState.RestoreCursorForCurrentMap). Claims gated on
    /// WorldRendererUtility.DrawingMap or on !WorldNavigationState.IsActive stay MapScope-exclusive,
    /// as does Alt+C: the world binds that chord to world.caravan.jumpToSelected, and two claims on
    /// different ScopeKeys never collide in ActionRegistry.CanCoexist, so registering both on one
    /// scope would silently resolve in registration order.
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterQuickInfoClaims()
        {
            RegisterQuickInfoClaims(this);
            // Alt+C stays MapScope-exclusive — see the class remarks.
            Claim("map.jumpToSelectedPawn", OnJumpToSelectedPawn, when: JumpToSelectedPawnLive);
        }

        /// <summary>
        /// The shared registrar, called once from MapScope's constructor and once from WorldScope's.
        /// Guards and handlers are identical either way; add no world-specific branching here.
        /// </summary>
        internal static void RegisterQuickInfoClaims(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.info.mood",
                delegate (KeyEventSnapshot e) { MoodState.DisplayMoodInfo(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.info.health",
                delegate (KeyEventSnapshot e) { HealthState.DisplayHealthInfo(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.info.needs",
                delegate (KeyEventSnapshot e) { NeedsState.DisplayNeedsInfo(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.info.combatLog",
                delegate (KeyEventSnapshot e) { CombatLogState.DisplayCombatLog(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.openGlobalLog",
                delegate (KeyEventSnapshot e) { GlobalLogWindow.Open(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.info.gear",
                delegate (KeyEventSnapshot e) { GearState.DisplayGearInfo(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.info.skills",
                delegate (KeyEventSnapshot e) { SkillsState.DisplaySkillsInfo(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.menu.skillsTable",
                delegate (KeyEventSnapshot e) { PawnSkillsTableState.Open(); }, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.pawn.assignArea", OnAssignArea, when: QuickInfoLive);
            scope.RegisterExternalClaim("map.pawn.rename", OnRenamePawn, when: RenamePawnLive);

            // F4 opens Animals/Mechs, or a picker when both are on the map.
            scope.RegisterExternalClaim("map.menu.animalsMechs", OnOpenAnimalsMechs, when: AnimalsMechsOpenerLive);

            // F1 opens the work menu, focused or table view per the persisted default-view setting.
            scope.RegisterExternalClaim("map.menu.work", OnOpenWorkMenu, when: WorkMenuOpenerLive);

            // F3 opens the assign menu.
            scope.RegisterExternalClaim("map.menu.assign", OnOpenAssignMenu, when: AssignMenuOpenerLive);

            // F12 opens the extras menu; its hide-dance lives in AmbientScopes.Game.cs.
            scope.RegisterExternalClaim("map.menu.extras", OnOpenExtraMenus, when: ExtraMenusLive);

            // L opens the notification menu (messages, letters, alerts). It carries no
            // !FocusStack.AnyLiveModal term: once the menu is a live MODAL scope,
            // FocusStackCore.Dispatch's structural masking already stops an unclaimed L from
            // reaching this claim.
            scope.RegisterExternalClaim("map.menu.notifications", OnOpenNotifications, when: NotificationsOpenerLive);

            // F7 opens the quest menu.
            scope.RegisterExternalClaim("map.menu.quests", OnOpenQuestMenu, when: QuestMenuOpenerLive);

            // F2 opens the shell-driven Schedule tab.
            scope.RegisterExternalClaim("map.menu.schedule", OnOpenScheduleMenu, when: ScheduleMenuOpenerLive);

            // F6 opens the research menu (ResearchMenuScope).
            scope.RegisterExternalClaim("map.menu.research", OnOpenResearchMenu, when: ResearchMenuOpenerLive);
        }

        /// <summary>The shared gate for the quick-info cluster — see the class remarks.</summary>
        private static bool QuickInfoLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// Alt+R's gate. Its explicit ArchitectState/ViewingMode/ShapePlacement terms overlap
        /// ShellGuards.MenuOwnsInput and are kept deliberately, not simplified away.
        /// </summary>
        private static bool RenamePawnLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ArchitectState.IsInPlacementMode
                && !ViewingModeState.IsActive
                && !ShapePlacementState.IsActive
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        private static void OnAssignArea(KeyEventSnapshot e)
        {
            Pawn pawn = null;
            if (MapNavigationState.IsInitialized)
            {
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                if (cursorPosition.IsValid && cursorPosition.InBounds(Find.CurrentMap))
                {
                    pawn = Find.CurrentMap.thingGrid.ThingsListAt(cursorPosition)
                        .OfType<Pawn>().FirstOrDefault(p => !HiddenPawns.IsHidden(p));
                }
            }

            if (pawn == null)
                pawn = Find.Selector?.FirstSelectedObject as Pawn;

            // Open handles a null or unsupported pawn internally.
            PawnAreaMenuState.Open(pawn);
        }

        private static void OnRenamePawn(KeyEventSnapshot e)
        {
            Pawn pawn = null;
            if (MapNavigationState.IsInitialized)
            {
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                if (cursorPosition.IsValid && cursorPosition.InBounds(Find.CurrentMap))
                {
                    pawn = Find.CurrentMap.thingGrid.ThingsListAt(cursorPosition)
                        .OfType<Pawn>().FirstOrDefault(p => !HiddenPawns.IsHidden(p));
                }
            }

            if (pawn == null)
                pawn = Find.Selector?.SingleSelectedObject as Pawn;

            if (pawn == null)
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.NoPawnAtCursor".Loc());
                return;
            }

            if (!PawnRenameHelper.CanRename(pawn))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.PawnCannotBeRenamed".Loc());
                return;
            }

            Find.WindowStack.Add(pawn.NamePawnDialog());
        }

        /// <summary>
        /// Alt+C's gate. The MenuOwnsInput term is deliberate: without it, whether Alt+C fires while
        /// a menu is open would be registration-order-defined.
        /// </summary>
        private static bool JumpToSelectedPawnLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput();
        }

        private static void OnJumpToSelectedPawn(KeyEventSnapshot e)
        {
            HandleJumpToSelectedPawn();
        }

        /// <summary>Jumps the camera to the selected pawn, or opens a picker while multi-select is on.</summary>
        private static void HandleJumpToSelectedPawn()
        {
            if (MultiSelectState.IsMultiSelectActive)
            {
                MultiSelectState.ValidateAndCleanupSelection();
                var options = new List<FloatMenuOption>();
                foreach (var pawn in MultiSelectState.SelectedPawns)
                {
                    string task = pawn.GetJobReport();
                    if (string.IsNullOrEmpty(task)) task = "RimWorldAccess.Pawns.MultiSelect.Idle".Translate();
                    string label = "RimWorldAccess.Pawns.Info.JumpPickerRow".Translate(pawn.LabelShort, task);
                    var p = pawn;
                    options.Add(new FloatMenuOption(label, () =>
                    {
                        JumpToPawn(p);
                    }));
                }
                WindowlessFloatMenuState.Open(options, false);
                return;
            }

            Pawn selectedPawn = GetSelectedPawn();
            if (selectedPawn == null)
                return;

            JumpToPawn(selectedPawn);
        }

        /// <summary>Jumps the camera and cursor to a pawn, and leaves the cursor riding it.</summary>
        private static void JumpToPawn(Pawn pawn)
        {
            CameraDriver cameraDriver = Find.CameraDriver;
            if (cameraDriver == null)
                return;

            IntVec3 pawnPosition = pawn.Position;
            cameraDriver.JumpToCurrentMapLoc(pawnPosition);

            MapNavigationState.CurrentCursorPosition = pawnPosition;
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;
            FollowedPawnAnnouncer.ArmTether(pawn);

            MapNavigationState.SpeakJumpedTo(pawn.LabelShort);
        }

        /// <summary>The selected pawn, or null (with a spoken reason) when there is none.</summary>
        private static Pawn GetSelectedPawn()
        {
            if (Find.Selector == null || Find.Selector.NumSelected == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Info.NoPawnSelected".Loc());
                return null;
            }

            Pawn selectedPawn = Find.Selector.FirstSelectedObject as Pawn;
            if (selectedPawn == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Info.NotAPawn".Loc());
                return null;
            }

            return selectedPawn;
        }

        /// <summary>F4's gate — same shape as <see cref="QuickInfoLive"/>.</summary>
        private static bool AnimalsMechsOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// Opens Animals and/or Mechs: a windowless float menu when both are on the map, straight to
        /// whichever is present otherwise.
        /// </summary>
        private static void OnOpenAnimalsMechs(KeyEventSnapshot e)
        {
            if (WorldNavigationState.IsActive)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            bool hasAnimals = Find.CurrentMap.mapPawns.ColonyAnimals.Any();
            bool hasMechs = ModsConfig.BiotechActive &&
                Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer)
                    .Any(p => p.RaceProps.IsMechanoid && p.OverseerSubject != null);

            if (hasAnimals && hasMechs)
            {
                string animalsLabel = DefDatabase<MainButtonDef>.GetNamed("Animals", false)?.label?.CapitalizeFirst()
                    ?? "RimWorldAccess.Input.AnimalsMenu.AnimalsFallbackLabel".Translate().ToString();
                string mechsLabel = DefDatabase<MainButtonDef>.GetNamed("Mechs", false)?.label?.CapitalizeFirst()
                    ?? "RimWorldAccess.Input.AnimalsMenu.MechsFallbackLabel".Translate().ToString();
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(animalsLabel, () =>
                    {
                        AnimalsMenuState.Open();
                        if (AnimalsMenuState.IsActive) MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Animals);
                    }),
                    new FloatMenuOption(mechsLabel, () =>
                    {
                        MechsMenuState.Open();
                        if (MechsMenuState.IsActive) MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Mechs);
                    })
                };
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }
            else if (hasAnimals)
            {
                AnimalsMenuState.Open();
                if (AnimalsMenuState.IsActive) MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Animals);
            }
            else if (hasMechs)
            {
                MechsMenuState.Open();
                if (MechsMenuState.IsActive) MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Mechs);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.AnimalsMenu.NoAnimalsOrMechs".Loc());
            }
        }

        /// <summary>F1's gate — <see cref="QuickInfoLive"/> plus the two work-menu state terms.</summary>
        private static bool WorkMenuOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !WorkMenuState.IsActive
                && !WorkTableState.IsActive
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// Opens the work menu for the selected pawn, falling back to the first free colonist, in the
        /// view the DefaultWorkMenuView setting names.
        /// </summary>
        private static void OnOpenWorkMenu(KeyEventSnapshot e)
        {
            if (WorldNavigationState.IsActive)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            Pawn targetPawn = null;
            if (Find.Selector != null && Find.Selector.NumSelected > 0)
            {
                targetPawn = Find.Selector.FirstSelectedObject as Pawn;
            }
            if (targetPawn == null && Find.CurrentMap.mapPawns.FreeColonists.Any())
            {
                targetPawn = Find.CurrentMap.mapPawns.FreeColonists.First();
            }

            if (targetPawn != null)
            {
                // This opener resolves its target pawn differently from the window prefix, so
                // EnsureTabOpen alone would silently change which pawn the menu opens on.
                WorkMenuOpener.OpenDefaultView(targetPawn);
                if (WorkMenuState.IsActive || WorkTableState.IsActive)
                    MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Work);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Input.WorkMenu.NoColonistsAvailable".Loc());
            }
        }

        /// <summary>F3's gate — same shape as <see cref="QuickInfoLive"/>.</summary>
        private static bool AssignMenuOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>Opens the assign menu; AssignMenuState.Open picks the pawn itself.</summary>
        private static void OnOpenAssignMenu(KeyEventSnapshot e)
        {
            if (WorldNavigationState.IsActive)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            AssignMenuState.Open();
            if (AssignMenuState.IsActive) MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Assign);
        }

        /// <summary>L's gate — same shape as <see cref="QuickInfoLive"/>; see the registration site for why no AnyLiveModal term is needed.</summary>
        private static bool NotificationsOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// Opens the notification menu. Alone among the openers here it does NOT hide the world view
        /// first: the menu's own jump-to-target actions branch on whether the target is a world
        /// object, a map cell or a map thing, so nothing depends on which view is rendered.
        /// </summary>
        private static void OnOpenNotifications(KeyEventSnapshot e)
        {
            NotificationMenuState.Open();
        }

        /// <summary>F7's gate — same shape as <see cref="AssignMenuOpenerLive"/>.</summary>
        private static bool QuestMenuOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>Opens the quest menu, hiding the world view first.</summary>
        private static void OnOpenQuestMenu(KeyEventSnapshot e)
        {
            if (WorldNavigationState.IsActive)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            QuestMenuState.Open();
            MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Quests);
        }

        /// <summary>F2's gate — same shape as <see cref="QuickInfoLive"/>.</summary>
        private static bool ScheduleMenuOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// Opens the real Schedule tab, hiding the world view first. SetCurrentTab is idempotent, so
        /// a repeat press is a no-op.
        /// </summary>
        private static void OnOpenScheduleMenu(KeyEventSnapshot e)
        {
            if (WorldNavigationState.IsActive)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            Find.MainTabsRoot.SetCurrentTab(DefDatabase<MainButtonDef>.GetNamed("Schedule"));
        }

        /// <summary>F6's gate — same shape as <see cref="ScheduleMenuOpenerLive"/>.</summary>
        private static bool ResearchMenuOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput()
                && !StatBreakdownState.IsActive;
        }

        /// <summary>Opens the research menu (ResearchMenuScope drives it), hiding the world view first.</summary>
        private static void OnOpenResearchMenu(KeyEventSnapshot e)
        {
            if (WorldNavigationState.IsActive)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            WindowlessResearchMenuState.Open();
            MainTabWindowLink.EnsureTabOpen(MainTabWindowLink.Research);
        }
    }
}
