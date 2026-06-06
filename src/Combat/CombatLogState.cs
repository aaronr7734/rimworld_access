using System.Collections.Generic;
using System.Linq;
using System.Text;
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

            // Collect references to relevant entries without rendering them.
            // ToGameStringFromPOV is expensive (grammar interpolation), so we
            // defer it until after we've picked the 10 we'll actually display.
            var allEntries = new List<(int ageTicks, string battleName, LogEntry entry)>();

            foreach (Battle battle in Find.BattleLog.Battles)
            {
                if (!battle.Concerns(pawn))
                    continue;

                string battleName = battle.GetName().StripTags();

                foreach (LogEntry entry in battle.Entries)
                {
                    if (!entry.Concerns(pawn))
                        continue;

                    allEntries.Add((entry.Age, battleName, entry));
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
                // Sort by age ascending (lowest age = most recent) and take first 10,
                // then render only those 10.
                var recentEntries = allEntries
                    .OrderBy(e => e.ageTicks)
                    .Take(10)
                    .Select(e => (e.battleName, entryText: e.entry.ToGameStringFromPOV(pawn).StripTags()))
                    .ToList();

                string currentBattleName = null;

                foreach (var (battleName, entryText) in recentEntries)
                {
                    if (battleName != currentBattleName)
                    {
                        if (currentBattleName != null)
                            sb.AppendLine();

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

            TolkHelper.SpeakData(sb.ToString().TrimEnd());
        }
    }
}
