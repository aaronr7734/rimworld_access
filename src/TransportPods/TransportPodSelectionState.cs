using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State for transport pod selection mode.
    /// Uses the map cursor for navigation (like building placement).
    /// Space toggles pod selection at cursor, Enter confirms group.
    /// Uses the game's Find.Selector for actual selection.
    /// </summary>
    public static class TransportPodSelectionState
    {
        /// <summary>
        /// Whether pod selection mode is currently active.
        /// </summary>
        public static bool IsActive { get; private set; }

        /// <summary>
        /// The map where selection is happening.
        /// </summary>
        private static Map currentMap;

        /// <summary>
        /// The source pod that initiated selection mode.
        /// </summary>
        private static CompTransporter sourcePod;

        /// <summary>
        /// Set of pods that can be grouped with the source (determined by flood fill at Open time).
        /// </summary>
        private static HashSet<CompTransporter> groupablePods;

        /// <summary>
        /// Opens pod selection mode for grouping multiple pods.
        /// If the source pod has no adjacent pods to group with, skips selection and loads directly.
        /// </summary>
        public static void Open(CompTransporter sourceTransporter)
        {
            if (!GuardHelper.RequireMap(SpeechPriority.High)) return;

            if (sourceTransporter == null)
            {
                TolkHelper.Speak("No pod selected", SpeechPriority.High);
                return;
            }

            currentMap = Find.CurrentMap;
            sourcePod = sourceTransporter;

            // Check if there are any pods that can be grouped with this one (adjacent launchers)
            var groupableList = TransportPodHelper.GetGroupablePodsFor(sourceTransporter, currentMap);

            if (groupableList.Count == 0)
            {
                // No adjacent pods to group with - skip selection and load directly
                sourcePod = null;
                LoadSinglePod(sourceTransporter);
                return;
            }

            // Store the groupable set (includes source pod) for validation during selection
            groupablePods = new HashSet<CompTransporter>(groupableList);
            groupablePods.Add(sourceTransporter);

            // There are adjacent pods - enter selection mode
            // Pre-select the source pod
            Find.Selector.ClearSelection();
            Find.Selector.Select(sourceTransporter.parent);

            IsActive = true;

            int totalGroupable = groupablePods.Count;
            TolkHelper.Speak($"Pod selection mode. {totalGroupable} pods can be grouped. Arrow to pods, Space to select, Enter to load.", SpeechPriority.High);
        }

        /// <summary>
        /// Loads a single pod directly without entering selection mode.
        /// </summary>
        private static void LoadSinglePod(CompTransporter transporter)
        {
            if (transporter?.parent == null)
                return;

            // Select just this pod
            Find.Selector.ClearSelection();
            Find.Selector.Select(transporter.parent);

            // Find and execute the load gizmo
            foreach (var gizmo in transporter.CompGetGizmosExtra())
            {
                if (gizmo is Command_LoadToTransporter loadCommand)
                {
                    loadCommand.ProcessInput(null);
                    return;
                }
            }

            TolkHelper.Speak("Could not find load command", SpeechPriority.High);
            Find.Selector.ClearSelection();
        }

        /// <summary>
        /// Closes pod selection mode without grouping.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentMap = null;
            sourcePod = null;
            groupablePods = null;

            // Clear the game's selection
            Find.Selector.ClearSelection();

            TolkHelper.Speak("Pod selection cancelled", SpeechPriority.Normal);
        }

        /// <summary>
        /// Handles keyboard input for pod selection mode.
        /// Returns true if the input was handled.
        /// </summary>
        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive)
                return false;

            // Space - toggle pod selection at cursor
            if (key == KeyCode.Space && !shift && !ctrl && !alt)
            {
                TogglePodAtCursor();
                return true;
            }

            // Enter - confirm and open loading dialog
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                ConfirmSelection();
                return true;
            }

            // Escape - cancel selection mode
            if (key == KeyCode.Escape)
            {
                Close();
                return true;
            }

            // Let arrow keys pass through to map navigation
            // but announce pod info after movement
            if (key == KeyCode.UpArrow || key == KeyCode.DownArrow ||
                key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                // Don't handle - let MapNavigationPatch handle the movement
                // We'll announce pod info via a postfix or the next frame
                return false;
            }

            return false;
        }

        /// <summary>
        /// Toggles selection of any transport pod at the current cursor position.
        /// </summary>
        private static void TogglePodAtCursor()
        {
            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            if (!cursorPos.InBounds(currentMap))
            {
                TolkHelper.Speak("Invalid position", SpeechPriority.Normal);
                return;
            }

            // Find transport pod at cursor
            var pods = TransportPodHelper.GetTransportPodsAt(cursorPos, currentMap);

            if (pods.Count == 0)
            {
                TolkHelper.Speak("No transport pod here", SpeechPriority.Normal);
                return;
            }

            // Toggle the first pod found at this position
            var pod = pods[0];
            if (pod?.parent == null)
                return;

            Thing podThing = pod.parent;

            if (Find.Selector.IsSelected(podThing))
            {
                Find.Selector.Deselect(podThing);
                int selectedCount = GetSelectedPodCount();
                TolkHelper.Speak($"Pod deselected. {selectedCount} selected", SpeechPriority.Normal);
            }
            else
            {
                // Check if pod is in the groupable set (connected via adjacent launchers)
                if (groupablePods == null || !groupablePods.Contains(pod))
                {
                    TolkHelper.Speak("This pod's launcher is not adjacent to the group.", SpeechPriority.Normal);
                    return;
                }

                // Check if pod is available (not already loading)
                if (pod.LoadingInProgressOrReadyToLaunch)
                {
                    TolkHelper.Speak("Pod is already loading or ready to launch", SpeechPriority.Normal);
                    return;
                }

                Find.Selector.Select(podThing);
                int selectedCount = GetSelectedPodCount();
                TolkHelper.Speak($"Pod selected. {selectedCount} selected", SpeechPriority.Normal);
            }
        }

        /// <summary>
        /// Gets the count of currently selected transport pods.
        /// </summary>
        private static int GetSelectedPodCount()
        {
            int count = 0;
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing && thing.TryGetComp<CompTransporter>() != null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Confirms selection and opens the loading dialog via the game's gizmo.
        /// </summary>
        private static void ConfirmSelection()
        {
            int selectedCount = GetSelectedPodCount();

            if (selectedCount == 0)
            {
                TolkHelper.Speak("No pods selected. Arrow to pods and press Space to select.", SpeechPriority.High);
                return;
            }

            // Get the first selected pod to access its gizmo
            CompTransporter firstTransporter = null;
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing)
                {
                    var transporter = thing.TryGetComp<CompTransporter>();
                    if (transporter != null)
                    {
                        firstTransporter = transporter;
                        break;
                    }
                }
            }

            if (firstTransporter == null)
            {
                TolkHelper.Speak("Selected pods are no longer available", SpeechPriority.High);
                Close();
                return;
            }

            IsActive = false;
            currentMap = null;

            // Find the load gizmo from the first transporter
            Command_LoadToTransporter loadCommand = null;
            foreach (var gizmo in firstTransporter.CompGetGizmosExtra())
            {
                if (gizmo is Command_LoadToTransporter cmd)
                {
                    loadCommand = cmd;
                    break;
                }
            }

            if (loadCommand == null)
            {
                TolkHelper.Speak("Could not find load command. Pod may already be loading.", SpeechPriority.High);
                Find.Selector.ClearSelection();
                return;
            }

            // Manually call InheritInteractionsFrom for each other selected pod
            // This populates the gizmo's transporters list (normally done by gizmo grid)
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing && thing != firstTransporter.parent)
                {
                    var otherTransporter = thing.TryGetComp<CompTransporter>();
                    if (otherTransporter != null)
                    {
                        // Get the other transporter's load gizmo
                        foreach (var otherGizmo in otherTransporter.CompGetGizmosExtra())
                        {
                            if (otherGizmo is Command_LoadToTransporter otherLoadCmd)
                            {
                                loadCommand.InheritInteractionsFrom(otherLoadCmd);
                                break;
                            }
                        }
                    }
                }
            }

            // Now ProcessInput will have all selected transporters
            loadCommand.ProcessInput(null);
        }

        /// <summary>
        /// Gets the currently selected transporters from the game's selector (for visual feedback).
        /// </summary>
        public static IEnumerable<CompTransporter> GetSelectedTransporters()
        {
            if (!IsActive)
                yield break;

            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ThingWithComps thing)
                {
                    var transporter = thing.TryGetComp<CompTransporter>();
                    if (transporter != null)
                        yield return transporter;
                }
            }
        }
    }
}
