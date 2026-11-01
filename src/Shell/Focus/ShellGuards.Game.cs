using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The structural predicate for "does an accessibility menu own the keyboard right now",
    /// serving two roles: the dispatcher's native modal swallow — consume-and-delegate scopes
    /// deliberately under-claim and depend on that swallow eating the rest — and the stand-down
    /// gate for ambient and scope claims that must stay dormant while a menu or a
    /// placement/viewing mode owns input.
    ///
    /// The answer comes from <see cref="FocusStack.AnyLiveInputOwner"/>: any live scope whose
    /// <see cref="FocusScope.OwnsGameInput"/> is true — modal scopes by default, plus the non-modal
    /// input owners that override it (gizmo navigation, viewing mode, placement, and the two
    /// dynamic-modality pages). The only residual hand terms are the four TextInputManager-session
    /// renames, whose TextSessionScope is a shadow. In DEBUG the retired OR-list survives as a
    /// divergence sentinel, so any state the structural predicate misses announces itself in the
    /// log instead of failing silently.
    /// </summary>
    public static class ShellGuards
    {
        /// <summary>
        /// True when ANY accessibility surface owns the keyboard; all unclaimed input then belongs
        /// to that surface rather than the game.
        /// </summary>
        public static bool MenuOwnsInput()
        {
            // Never during loading: Entry is the main menu, Playing is in-game, MapInitializing
            // is a map load.
            if (Current.ProgramState != ProgramState.Playing && Current.ProgramState != ProgramState.Entry)
                return false;

            // MapNavigationState.IsInitialized is deliberately NOT a term: it is always true on
            // the map, so it would block all keyboard input. MapNavigationPatch consumes the arrow
            // keys directly instead.

            bool owns = FocusStack.AnyLiveInputOwner;

#if DEBUG
            CheckDivergence(owns);
#endif
            return owns;
        }

        /// <summary>
        /// True when a real, foreground dialog the shell drives with its own attached scope sits
        /// on the WindowStack. Window-attached scopes are pushed exactly ONCE at Add time and never
        /// re-assert, while <see cref="FocusStackCore.Push"/> re-floats an already-stacked scope to
        /// the top — so a per-frame mirror that keeps pushing while such a dialog is open hoists
        /// its scope back above the dialog every pass and eats the dialog's keys. Mirrors whose
        /// screen can open, or stay live under, a real dialog fold
        /// <c>!ShellGuards.ForeignInputOwningWindowAbove()</c> into their live gate.
        ///
        /// "Foreground dialog" is EITHER of the two flags a dialog uses to claim the screen:
        /// <c>absorbInputAroundWindow</c> (the common modal case) or <c>closeOnClickedOutside</c>
        /// (the membership-editor pattern, which pauses and closes on an outside click but does NOT
        /// absorb). Both require an attached scope, so a window the shell does not drive never trips
        /// this, and windowless overlays add no window at all.
        /// </summary>
        public static bool ForeignInputOwningWindowAbove()
        {
            IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (windows == null)
            {
                return false;
            }
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window is ImmediateWindow)
                {
                    continue;
                }
                if ((window.absorbInputAroundWindow || window.closeOnClickedOutside)
                    && ScopeForWindow.HasAttachedScope(window))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The wider twin of <see cref="ForeignInputOwningWindowAbove"/>, for the windowless SCREEN
        /// mirrors: a real dialog carrying one of the shell's attached scopes owns the keyboard even
        /// when it declares neither modal flag, and a per-frame mirror would otherwise hoist its own
        /// scope back above such a window every pass and eat its keys.
        ///
        /// <see cref="WindowLayer.GameUI"/> is the dividing line vanilla itself draws:
        /// <c>MainTabWindow</c> drops to that layer (RimWorld/MainTabWindow.cs:34) while ordinary
        /// windows default to <c>Dialog</c> (Verse/Window.cs:12), so main tabs stay the business of
        /// <see cref="NonInspectMainTabOpen"/>. The map MODES keep the narrow test deliberately: a
        /// non-absorbing editor window stays open precisely so the map under it remains drivable.
        /// </summary>
        public static bool ForeignDialogWindowAbove()
        {
            IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (windows == null)
            {
                return false;
            }
            for (int i = 0; i < windows.Count; i++)
            {
                Window window = windows[i];
                if (window is ImmediateWindow)
                {
                    continue;
                }
                if ((window.absorbInputAroundWindow || window.closeOnClickedOutside
                        || window.layer != WindowLayer.GameUI)
                    && ScopeForWindow.HasAttachedScope(window))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when a main tab other than Inspect owns the screen. Main tab windows are
        /// neither absorbing nor close-on-outside, so <see cref="ForeignInputOwningWindowAbove"/>
        /// never sees one, and a per-frame mirror buries the scope such a tab attached.</summary>
        public static bool NonInspectMainTabOpen()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.MainTabsRoot == null)
            {
                return false;
            }
            MainButtonDef openTab = Find.MainTabsRoot.OpenTab;
            return openTab != null && openTab != MainButtonDefOf.Inspect;
        }

        /// <summary>
        /// The positional twin of <see cref="ForeignInputOwningWindowAbove"/>: true when a real
        /// window sits under the mouse pointer. Keeps the <see cref="ImmediateWindow"/> skip,
        /// because the overlays vanilla draws that way cover map the player is still looking at.
        /// Topmost-first, matching <c>WindowStack.GetWindowAt</c>, which cannot express the skip.
        /// With <paramref name="below"/>, only windows ABOVE it count, so a window drawing itself
        /// can ask whether anything obscures it at the pointer.
        /// </summary>
        internal static bool NonImmediateWindowUnderPointer(Window below = null)
        {
            IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (windows == null)
            {
                return false;
            }
            // Absolute UI points, the space windowRect lives in.
            // MousePosUIInvertedUseEventIfCan divides the local point by Prefs.UIScale and unclips
            // in unscaled space (Verse/UI.cs:23-31, :68-71), so above 100% scale it disagrees with
            // windowRect — and callers inside a window's GUI pass run under GUI.Window's clip.
            Vector2 pos = UI.MousePositionOnUIInverted;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window window = windows[i];
                if (window == below)
                {
                    return false;
                }
                if (window is ImmediateWindow)
                {
                    continue;
                }
                if (window.windowRect.Contains(pos))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when an OPAQUE immediate panel sits under the pointer — the case
        /// <see cref="NonImmediateWindowUnderPointer"/>'s blanket <see cref="ImmediateWindow"/>
        /// skip is too coarse for. Vanilla draws the designator's rotation and draw-style controls
        /// this way, at bottom-Y = the inspect pane's top edge (Verse/DesignatorUtility.cs:81,
        /// Verse/Designator.cs:373), painting over the left end of the inspect tab strip, so a
        /// reader of the strip must yield to them exactly as it yields to a real window.
        ///
        /// The background flag is the test, not the window layer: it is what makes a panel hide
        /// what it covers (Verse/Window.cs:212), while the transparent immediate-window overlays
        /// leave the surface beneath readable.
        /// </summary>
        internal static bool OpaqueImmediatePanelUnderPointer()
        {
            IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (windows == null)
            {
                return false;
            }
            Vector2 pos = UI.MousePositionOnUIInverted;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window window = windows[i];
                if (window is ImmediateWindow && window.doWindowBackground
                    && window.windowRect.Contains(pos))
                {
                    return true;
                }
            }
            return false;
        }

#if DEBUG
        private static int lastDivergenceFrame = -1;

        /// <summary>
        /// Divergence sentinel: compares the structural predicate against the retired hand-listed
        /// OR kept verbatim below, logging any mismatch once per frame with the active legacy terms
        /// and the scope stack. DEBUG-only.
        /// </summary>
        private static void CheckDivergence(bool structural)
        {
            var activeTerms = new List<string>();
            CollectLegacyTerms(activeTerms);
            bool legacy = FocusStack.AnyLiveModal || activeTerms.Count > 0;
            if (legacy == structural)
                return;
            if (UnityEngine.Time.frameCount == lastDivergenceFrame)
                return;
            lastDivergenceFrame = UnityEngine.Time.frameCount;
            Log.Warning("[RimWorldAccess] MenuOwnsInput divergence (P4-7 sentinel): structural="
                + structural + " legacy=" + legacy
                + "; active legacy terms: [" + string.Join(", ", activeTerms)
                + "]; stack: " + FocusStack.DebugDump());
        }

        /// <summary>The retired OR-list, verbatim and in its original evaluation order. Feeds the sentinel only.</summary>
        private static void CollectLegacyTerms(List<string> active)
        {
            if (WindowlessFloatMenuState.IsActive) active.Add("WindowlessFloatMenuState");
            if (WindowlessInventoryState.IsActive) active.Add("WindowlessInventoryState");
            if (WindowlessInspectionState.IsActive) active.Add("WindowlessInspectionState");
            if (WhatsNewState.IsActive) active.Add("WhatsNewState");
            if (WindowlessResearchMenuState.IsActive) active.Add("WindowlessResearchMenuState");
            if (WindowlessResearchDetailState.IsActive) active.Add("WindowlessResearchDetailState");
            if (CaravanInspectState.IsActive) active.Add("CaravanInspectState");
            if (WorldObjectSelectionState.IsActive) active.Add("WorldObjectSelectionState");
            if (CaravanFormationState.IsActive) active.Add("CaravanFormationState");
            if (LordJobDialogState.IsActive) active.Add("LordJobDialogState");
            if (QuestMenuState.IsActive) active.Add("QuestMenuState");
            if (NotificationMenuState.IsActive) active.Add("NotificationMenuState");
            if (AssignMenuState.IsActive) active.Add("AssignMenuState");
            if (WorkMenuState.IsActive) active.Add("WorkMenuState");
            if (WorkTableState.IsActive) active.Add("WorkTableState");
            if (StorageSettingsMenuState.IsActive) active.Add("StorageSettingsMenuState");
            if (PlantSelectionMenuState.IsActive) active.Add("PlantSelectionMenuState");
            if (MechControlGroupState.IsActive) active.Add("MechControlGroupState");
            if (GizmoNavigationState.IsActive) active.Add("GizmoNavigationState");
            if (TradeNavigationState.IsActive) active.Add("TradeNavigationState");
            if (SellableItemsState.IsActive) active.Add("SellableItemsState");
            if (BillsMenuState.IsActive) active.Add("BillsMenuState");
            if (BillConfigState.IsActive) active.Add("BillConfigState");
            if (FishingZoneMenuState.IsActive) active.Add("FishingZoneMenuState");
            if (RangeEditMenuState.IsActive) active.Add("RangeEditMenuState");
            if (TempControlMenuState.IsActive) active.Add("TempControlMenuState");
            if (ForbidControlState.IsActive) active.Add("ForbidControlState");
            if (DoorControlState.IsActive) active.Add("DoorControlState");
            if (RefuelableComponentState.IsActive) active.Add("RefuelableComponentState");
            if (HealthTabState.IsActive) active.Add("HealthTabState");
            if (PrisonerTabState.IsActive) active.Add("PrisonerTabState");
            if (ThingFilterMenuState.IsActive) active.Add("ThingFilterMenuState");
            if (ArchitectState.IsActive) active.Add("ArchitectState");
            if (ArchitectTreeState.IsActive) active.Add("ArchitectTreeState");
            if (AnimalsMenuState.IsActive) active.Add("AnimalsMenuState");
            if (WildlifeMenuState.IsActive) active.Add("WildlifeMenuState");
            if (PawnSkillsTableState.IsActive) active.Add("PawnSkillsTableState");
            if (MechsMenuState.IsActive) active.Add("MechsMenuState");
            if (ModListState.IsActive) active.Add("ModListState");
            if (StorytellerSelectionState.IsActive) active.Add("StorytellerSelectionState");
            if (PlaySettingsMenuState.IsActive) active.Add("PlaySettingsMenuState");
            if (SplitCaravanState.IsActive) active.Add("SplitCaravanState");
            if (GearEquipMenuState.IsActive) active.Add("GearEquipMenuState");
            if (QuantityMenuState.IsActive) active.Add("QuantityMenuState");
            if (AreaSelectionMenuState.IsActive) active.Add("AreaSelectionMenuState");
            if (PawnAreaMenuState.IsActive) active.Add("PawnAreaMenuState");
            if (HistoryState.IsActive) active.Add("HistoryState");
            if (HistoryStatisticsState.IsActive) active.Add("HistoryStatisticsState");
            if (HistoryMessagesState.IsActive) active.Add("HistoryMessagesState");
            if (ViewingModeState.IsActive) active.Add("ViewingModeState");
            if (ShapePlacementState.IsActive) active.Add("ShapePlacementState");
            if (InfoCardState.IsActive) active.Add("InfoCardState");
            if (GrowthMomentState.IsActive) active.Add("GrowthMomentState");
            if (FactionTabState.IsActive) active.Add("FactionTabState");
            if (LearningHelperState.IsActive) active.Add("LearningHelperState");
        }
#endif
    }
}
