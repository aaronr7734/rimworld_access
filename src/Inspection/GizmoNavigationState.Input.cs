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
    public static partial class GizmoNavigationState
    {
        /// <summary>
        /// Jumps to the first gizmo in the list.
        /// </summary>
        public static void JumpToFirst()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedGizmoIndex = typeahead.GetFirstMatch();
                AnnounceWithSearch();
                return;
            }

            selectedGizmoIndex = MenuHelper.JumpToFirst();
            typeahead.ClearSearch();
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Jumps to the last gizmo in the list.
        /// </summary>
        public static void JumpToLast()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                selectedGizmoIndex = typeahead.GetLastMatch();
                AnnounceWithSearch();
                return;
            }

            selectedGizmoIndex = MenuHelper.JumpToLast(availableGizmos.Count);
            typeahead.ClearSearch();
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Alt+Shift+J: land the gizmo cursor on whatever the mouse is resting on and
        /// read it exactly as an arrow key would. The bar draws straight into the map UI
        /// with no window and no clip group, so the recorded rects are already in the
        /// pointer's own space. False when nothing routable is under the pointer — the
        /// caller sounds the refusal.
        /// </summary>
        public static bool RouteToPointer(Vector2 pointer)
        {
            if (!isActive || availableGizmos.Count == 0)
                return false;

            if (!GizmoRectRegistry.TryHitTest(pointer, out Gizmo hit, out int _))
                return false;

            int index = IndexOfDrawnGizmo(hit);
            if (index < 0)
                return false;

            selectedGizmoIndex = index;
            typeahead.ClearSearch();
            AnnounceCurrentGizmo();
            return true;
        }

        /// <summary>
        /// The browsed gizmo vanilla drew as <paramref name="drawn"/>, or -1. Reference
        /// identity almost never matches — GetGizmos() builds fresh instances on every
        /// enumeration — so this falls back to vanilla's own sameness test, exactly as
        /// GizmoRectRegistry.TryGet does in the opposite direction.
        /// </summary>
        private static int IndexOfDrawnGizmo(Gizmo drawn)
        {
            int identity = availableGizmos.IndexOf(drawn);
            if (identity >= 0)
                return identity;

            for (int i = 0; i < availableGizmos.Count; i++)
            {
                try
                {
                    if (availableGizmos[i].GroupsWith(drawn) || drawn.GroupsWith(availableGizmos[i]))
                        return i;
                }
                catch
                {
                    continue; // A modded GroupsWith that throws just skips its entry.
                }
            }
            return -1;
        }

        /// <summary>
        /// Handles typeahead character input for the gizmo menu.
        /// Called from GizmoScope's CharSink.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            var labels = GetGizmoLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedGizmoIndex = newIndex;
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
        /// Called from GizmoScope's SearchBackspace claim.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (!typeahead.HasActiveSearch)
                return;

            var labels = GetGizmoLabels();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedGizmoIndex = newIndex;
                }
                AnnounceWithSearch();
            }
        }

        /// <summary>
        /// Gets whether typeahead search is active.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        /// <summary>
        /// Whether the focused gizmo carries an adjustable slider. GizmoScope's
        /// horizontal-arrow claims gate on this, so the arrows only leave the map
        /// cursor while a slider is focused.
        /// </summary>
        public static bool SelectedGizmoHasSlider()
        {
            if (!isActive || availableGizmos.Count == 0)
                return false;
            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return false;
            return GizmoHandlerRegistry.TryResolveSliderAdapter(
                availableGizmos[selectedGizmoIndex], out GizmoSliderAdapter _);
        }

        #region Shell Focus-Scope Router
        // Thin per-action routers claimed by GizmoScope. Each replicates one
        // branch of the legacy HandleInput dispatcher; all domain logic stays
        // in the private methods they call.

        /// <summary>Up arrow: previous search match while a search with matches
        /// is active, otherwise the previous gizmo.</summary>
        public static void NavigatePrevious()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prevIndex = typeahead.GetPreviousMatch(selectedGizmoIndex);
                if (prevIndex >= 0)
                {
                    selectedGizmoIndex = prevIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                SelectPrevious();
            }
        }

        /// <summary>Down arrow: next search match while a search with matches
        /// is active, otherwise the next gizmo.</summary>
        public static void NavigateNext()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int nextIndex = typeahead.GetNextMatch(selectedGizmoIndex);
                if (nextIndex >= 0)
                {
                    selectedGizmoIndex = nextIndex;
                    AnnounceWithSearch();
                }
            }
            else
            {
                SelectNext();
            }
        }

        /// <summary>
        /// Enter: executes the selected gizmo, or points at the arrows when the
        /// gizmo is a slider with nothing to activate (its ProcessInput fallback
        /// would be silent).
        /// </summary>
        public static void ActivateCurrent()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;
            Gizmo enterGizmo = availableGizmos[selectedGizmoIndex];
            if (GizmoHandlerRegistry.TryResolveSliderAdapter(enterGizmo, out GizmoSliderAdapter _)
                && !GizmoHandlerRegistry.HasActivation(enterGizmo))
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.SliderUseArrows".Loc());
                return;
            }
            ExecuteSelected();
        }

        /// <summary>
        /// Escape: clears an active search, and otherwise closes the menu with
        /// the close announcement (previously split between HandleInput and the
        /// ladder rung's fallback).
        /// </summary>
        public static void HandleCancel()
        {
            if (!isActive)
                return;
            if (typeahead.HasActiveSearch)
            {
                typeahead.ClearSearchAndAnnounce();
                AnnounceCurrentGizmo();
                return;
            }
            Close();
            TolkHelper.Speak("RimWorldAccess.Input.Close.GizmoMenuClosed".Loc());
        }

        /// <summary>Alt+I: info card for the selected gizmo.</summary>
        public static void OpenGizmoInfoCard()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;
            OpenInfoCardForCurrentGizmo();
        }

        /// <summary>
        /// Right bracket: right-click options when the gizmo has them, the gizmo's own
        /// right-button input when a mod may handle it there, otherwise the no-options speech.
        /// </summary>
        public static void HandleRightBracket()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;
            Gizmo rbGizmo = availableGizmos[selectedGizmoIndex];

            // A provider builds the merged list itself, vanilla's options included, so
            // CollectRightClickOptions is skipped here. Its grouped-gizmo option merge is
            // not applicable either: GroupIntoRepresentatives never groups a designator
            // with other commands.
            Designator behind = DesignatorBehind(rbGizmo);
            if (behind != null && DesignatorContextMenuRouter.HasOptions(behind))
            {
                if (DesignatorContextMenuRouter.TryOpen(behind))
                    return;
            }

            if (HasRightClickOptions(rbGizmo) || GizmoHandlerRegistry.MayBranchOnRightClick(rbGizmo))
                ExecuteRightClick();
            else
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoAdditionalOptions".Loc());
        }

        /// <summary>
        /// Left/Right arrows on a focused slider gizmo; Shift steps by 5. The
        /// adapter is resolved per press because some adapters capture the
        /// current value in their write closure.
        /// </summary>
        public static void AdjustSlider(int direction, bool bigStep)
        {
            if (!isActive || availableGizmos.Count == 0)
                return;
            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;
            if (!GizmoHandlerRegistry.TryResolveSliderAdapter(
                    availableGizmos[selectedGizmoIndex], out GizmoSliderAdapter adapter))
            {
                return;
            }
            AdjustSliderValue(adapter, direction, bigStep ? 5 : 1);
        }

        #endregion

        /// <summary>
        /// Opens an info card for the currently selected gizmo, if applicable.
        /// Handles Command_Ability (ability def), Designator_Build (building def with optional stuff),
        /// and falls back to the gizmo's owner (Thing or WorldObject).
        /// </summary>
        private static void OpenInfoCardForCurrentGizmo()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            // Command_Ability -> open info card for the ability def
            if (gizmo is Command_Ability cmdAbility && cmdAbility.Ability?.def != null)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(cmdAbility.Ability.def));
                return;
            }

            // Designator_Build -> open info card for the building def (with stuff if applicable)
            if (gizmo is Designator_Build buildDesignator)
            {
                BuildableDef placingDef = buildDesignator.PlacingDef;
                if (placingDef is ThingDef thingDef)
                {
                    ThingDef stuff = buildDesignator.StuffDef;
                    if (stuff != null)
                        Find.WindowStack.Add(new Dialog_InfoCard(thingDef, stuff));
                    else
                        InfoCardState.OpenInfoCardForDef(thingDef);
                    return;
                }
                if (placingDef is TerrainDef terrainDef)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(terrainDef));
                    return;
                }
            }

            // Fallback: use gizmo owner (Thing or WorldObject)
            if (gizmoOwners.TryGetValue(gizmo, out ISelectable owner))
            {
                if (owner is Thing thing)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(thing));
                    return;
                }
                if (owner is WorldObject worldObj)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(worldObj));
                    return;
                }
            }

            // Nothing applicable
            InfoCardState.SpeakNoInfoCardAvailable();
        }
    }
}
