using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State class for displaying combat log information of the selected pawn.
    /// Triggered by Alt+B key combination.
    /// </summary>
    public static class CombatLogState
    {
        /// <summary>
        /// Displays combat log information for the currently selected pawn.
        /// Shows all battle entries involving this pawn.
        /// </summary>
        public static void DisplayCombatLog()
        {
            // Check if we're in-game
            if (Current.ProgramState != ProgramState.Playing)
            {
                TolkHelper.Speak("Not in game");
                return;
            }

            // Check if there's a current map
            if (Find.CurrentMap == null)
            {
                TolkHelper.Speak("No map loaded");
                return;
            }

            // Try pawn at cursor first
            Pawn pawn = null;
            if (MapNavigationState.IsInitialized)
            {
                IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;
                if (cursorPosition.IsValid && cursorPosition.InBounds(Find.CurrentMap))
                {
                    pawn = Find.CurrentMap.thingGrid.ThingsListAt(cursorPosition)
                        .OfType<Pawn>().FirstOrDefault();
                }
            }

            // Fall back to selected pawn
            if (pawn == null)
                pawn = Find.Selector?.FirstSelectedObject as Pawn;

            if (pawn == null)
            {
                TolkHelper.Speak("No pawn selected");
                return;
            }

            // Check if battle log exists
            if (Find.BattleLog == null)
            {
                TolkHelper.Speak("No battle log available");
                return;
            }

            // Build combat log information
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"=== {pawn.LabelShort} Combat Log ===");

            int entryCount = 0;
            string currentBattleName = null;

            // Iterate through all battles
            foreach (Battle battle in Find.BattleLog.Battles)
            {
                // Skip battles that don't involve this pawn
                if (!battle.Concerns(pawn))
                    continue;

                // Get battle name for grouping
                string battleName = battle.GetName();

                // Iterate through entries in this battle
                foreach (LogEntry entry in battle.Entries)
                {
                    // Skip entries that don't involve this pawn
                    if (!entry.Concerns(pawn))
                        continue;

                    // Add battle header if it changed
                    if (battleName != currentBattleName)
                    {
                        if (currentBattleName != null)
                            sb.AppendLine(); // Add spacing between battles

                        sb.AppendLine($"-- {battleName.StripTags()} --");
                        currentBattleName = battleName;
                    }

                    // Get the entry text from this pawn's point of view and strip color tags
                    string entryText = entry.ToGameStringFromPOV(pawn).StripTags();
                    sb.AppendLine(entryText);
                    entryCount++;
                }
            }

            if (entryCount == 0)
            {
                sb.AppendLine("No combat entries found");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"Total: {entryCount} entries");
            }

            TolkHelper.Speak(sb.ToString().TrimEnd());
        }
    }
}
