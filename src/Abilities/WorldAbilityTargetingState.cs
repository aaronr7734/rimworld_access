using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for world map ability targeting (e.g., Farskip).
    /// Follows the pattern from TransportPodLaunchState.
    /// </summary>
    public static class WorldAbilityTargetingState
    {
        private static bool isActive = false;
        private static Ability currentAbility = null;

        /// <summary>
        /// Gets whether world ability targeting mode is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the current ability being targeted.
        /// </summary>
        public static Ability CurrentAbility => currentAbility;

        /// <summary>
        /// Opens world ability targeting state.
        /// </summary>
        public static void Open(Ability ability)
        {
            if (ability == null)
            {
                Log.Warning("RimWorld Access: WorldAbilityTargetingState.Open called with null ability");
                return;
            }

            currentAbility = ability;
            isActive = true;

            // Announce targeting start, including affected pawns for AOE abilities
            // (the AOE is centered on the caster and can't be changed during world targeting)
            string announcement = $"{ability.def.LabelCap} world targeting.";

            if (ability.def.HasAreaOfEffect && ability.pawn?.Map != null)
            {
                var affected = AbilityTargetingHelper.GetAffectedPawns(
                    ability, ability.pawn.Position, ability.pawn.Map);
                if (affected.Count > 0)
                {
                    var names = affected.Select(p => p.LabelShort).ToCommaList(useAnd: true);
                    announcement += $" Bringing {affected.Count}: {names}.";
                }
            }

            announcement += " Select destination tile.";
            TolkHelper.Speak(announcement, SpeechPriority.Normal);
        }

        /// <summary>
        /// Closes world ability targeting state.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentAbility = null;
        }

        /// <summary>
        /// Handles keyboard input during world ability targeting.
        /// Returns true if input was handled.
        /// </summary>
        public static bool HandleInput(UnityEngine.KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive)
                return false;

            // If WorldTargeter stopped, close our state
            if (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting)
            {
                Close();
                return false;
            }

            // Enter - confirm current destination
            if ((key == UnityEngine.KeyCode.Return || key == UnityEngine.KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                // If a float menu is showing, let it handle Enter
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                ConfirmCurrentDestination();
                return true;
            }

            // Escape - cancel targeting
            if (key == UnityEngine.KeyCode.Escape)
            {
                // If a float menu is showing, let it close first
                if (WindowlessFloatMenuState.IsActive)
                    return false;

                CancelTargeting();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Confirms the currently selected world tile as the destination.
        /// </summary>
        private static void ConfirmCurrentDestination()
        {
            if (!WorldNavigationState.IsActive)
            {
                TolkHelper.Speak("World navigation not active", SpeechPriority.High);
                return;
            }

            PlanetTile selectedTile = WorldNavigationState.CurrentSelectedTile;
            if (!selectedTile.Valid)
            {
                TolkHelper.Speak("No valid tile selected", SpeechPriority.High);
                return;
            }

            if (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting)
            {
                TolkHelper.Speak("World targeter not active", SpeechPriority.High);
                return;
            }

            try
            {
                // Get the action field from WorldTargeter using reflection
                var actionField = typeof(WorldTargeter).GetField("action", BindingFlags.NonPublic | BindingFlags.Instance);
                if (actionField == null)
                {
                    TolkHelper.Speak("Cannot access world targeter action", SpeechPriority.High);
                    return;
                }

                var action = actionField.GetValue(Find.WorldTargeter) as Func<GlobalTargetInfo, bool>;
                if (action == null)
                {
                    TolkHelper.Speak("No targeting action available", SpeechPriority.High);
                    return;
                }

                // Create GlobalTargetInfo for the selected tile
                GlobalTargetInfo targetInfo;
                var worldObjects = Find.WorldObjects?.ObjectsAt(selectedTile)?.ToList();
                if (worldObjects != null && worldObjects.Count > 0)
                {
                    // Use the first world object (typically a settlement or site)
                    targetInfo = new GlobalTargetInfo(worldObjects[0]);
                }
                else
                {
                    // Just a tile with no world object
                    targetInfo = new GlobalTargetInfo(selectedTile);
                }

                // Invoke the action callback
                bool completed = action(targetInfo);

                if (completed)
                {
                    // Target was accepted
                    Find.WorldTargeter.StopTargeting();
                    TolkHelper.Speak("Target selected", SpeechPriority.Normal);
                }
                // If not completed, target was rejected or a float menu was created
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Error confirming world ability destination: {ex}");
                TolkHelper.Speak("Error selecting destination", SpeechPriority.High);
            }
        }

        /// <summary>
        /// Cancels world ability targeting and returns to map.
        /// </summary>
        private static void CancelTargeting()
        {
            // Cache the return target
            Pawn caster = currentAbility?.pawn;
            Map returnMap = caster?.Map;

            // Stop world targeting
            if (Find.WorldTargeter != null && Find.WorldTargeter.IsTargeting)
            {
                Find.WorldTargeter.StopTargeting();
            }

            // Close our state
            Close();
            TolkHelper.Speak("Ability targeting cancelled", SpeechPriority.Normal);

            // Return to map view
            if (returnMap != null && caster != null)
            {
                CameraJumper.TryJump(caster);
            }
        }

        /// <summary>
        /// Gets destination info for a world tile.
        /// Uses the ability's WorldMapExtraLabel for validity information.
        /// </summary>
        public static string GetDestinationInfo(int tile)
        {
            if (!isActive || currentAbility == null)
                return null;

            GlobalTargetInfo targetInfo;
            var worldObjects = Find.WorldObjects?.ObjectsAt(tile)?.ToList();
            if (worldObjects != null && worldObjects.Count > 0)
            {
                targetInfo = new GlobalTargetInfo(worldObjects[0]);
            }
            else
            {
                targetInfo = new GlobalTargetInfo(tile);
            }

            // Get the extra label from the ability (e.g., "No ally to skip to" for Farskip)
            var extraLabel = currentAbility.WorldMapExtraLabel(targetInfo);
            if (!extraLabel.NullOrEmpty())
            {
                return extraLabel;
            }

            // Check if the ability can target this tile
            if (!currentAbility.ValidateGlobalTarget(targetInfo))
            {
                return "Invalid target";
            }

            return null;
        }

    }
}
