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
            if (!GuardHelper.RequireInGame()) return;
            if (!GuardHelper.RequireMap()) return;

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

            if (!GuardHelper.RequirePawn(pawn)) return;

            // Check if battle log exists
            if (Find.BattleLog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Combat.Log.NoBattleLog".Loc());
                return;
            }

            // Build combat log information
            // First, collect all entries with their timestamps and battle names
            var allEntries = new List<(int ageTicks, string battleName, string entryText)>();

            // Iterate through all battles to collect entries
            foreach (Battle battle in Find.BattleLog.Battles)
            {
                // Skip battles that don't involve this pawn
                if (!battle.Concerns(pawn))
                    continue;

                // Get battle name for grouping
                string battleName = battle.GetName().StripTags();

                // Iterate through entries in this battle
                foreach (LogEntry entry in battle.Entries)
                {
                    // Skip entries that don't involve this pawn
                    if (!entry.Concerns(pawn))
                        continue;

                    // Get the entry text from this pawn's point of view and strip color tags
                    string entryText = entry.ToGameStringFromPOV(pawn).StripTags();
                    allEntries.Add((entry.Age, battleName, entryText));
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Combat.Log.Header".Translate(pawn.LabelShort) + ".");

            if (allEntries.Count == 0)
            {
                sb.AppendLine("RimWorldAccess.Combat.Log.NoEntries".Translate());
            }
            else
            {
                // Sort by age ascending (lowest age = most recent) and take first 10
                var recentEntries = allEntries
                    .OrderBy(e => e.ageTicks)
                    .Take(10)
                    .ToList();

                string currentBattleName = null;

                // Build output for the last 10 entries
                foreach (var (ageTicks, battleName, entryText) in recentEntries)
                {
                    // Add battle header if it changed
                    if (battleName != currentBattleName)
                    {
                        if (currentBattleName != null)
                            sb.AppendLine(); // Add spacing between battles

                        sb.AppendLine($"-- {battleName} --");
                        currentBattleName = battleName;
                    }

                    sb.AppendLine(entryText);
                }

                sb.AppendLine();
                if (allEntries.Count > 10)
                {
                    sb.AppendLine("RimWorldAccess.Combat.Log.ShowingLastN".Translate(allEntries.Count));
                }
                else
                {
                    sb.AppendLine("RimWorldAccess.Combat.Log.Total".Translate(allEntries.Count));
                }
            }

            TolkHelper.Speak(sb.ToString().TrimEnd());
        }
    }
}
