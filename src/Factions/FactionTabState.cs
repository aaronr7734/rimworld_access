using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for the in-game Factions tab (windowless).
    /// Provides a flat list of factions with typeahead search.
    /// Opened via F12 > Factions or any other path that activates MainTabWindow_Factions.
    /// </summary>
    public static class FactionTabState
    {
        public static bool IsActive { get; private set; }

        private static List<Faction> factions = new List<Faction>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Opens the faction tab state and builds the faction list.
        /// Called from FactionTabPatch when MainTabWindow_Factions opens.
        /// </summary>
        public static void Open()
        {
            if (IsActive)
                return;

            IsActive = true;
            factions = FactionHelper.BuildFactionList();
            selectedIndex = 0;
            typeahead.ClearSearch();
            AnnounceOpening();
        }

        /// <summary>
        /// Closes the faction tab state and resets all fields.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            factions.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
            TolkHelper.Speak("Faction relations closed.");
        }

        /// <summary>
        /// Handles keyboard input for the faction tab.
        /// Returns true if input was handled.
        /// Called from UnifiedKeyboardPatch which handles Event.current.Use().
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Alt+I — open info card for selected faction
            if (ev.alt && key == KeyCode.I)
            {
                OpenInfoCard();
                return true;
            }

            // Escape — clear search first, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentFaction();
                    return true;
                }
                Close();
                return true;
            }

            // Up arrow
            if (key == KeyCode.UpArrow)
            {
                if (factions.Count == 0) return true;
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prev = typeahead.GetPreviousMatch(selectedIndex);
                    if (prev >= 0)
                    {
                        selectedIndex = prev;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    selectedIndex = MenuHelper.SelectPrevious(selectedIndex, factions.Count);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentFaction();
                }
                return true;
            }

            // Down arrow
            if (key == KeyCode.DownArrow)
            {
                if (factions.Count == 0) return true;
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int next = typeahead.GetNextMatch(selectedIndex);
                    if (next >= 0)
                    {
                        selectedIndex = next;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    selectedIndex = MenuHelper.SelectNext(selectedIndex, factions.Count);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceCurrentFaction();
                }
                return true;
            }

            // Home — first faction
            if (key == KeyCode.Home)
            {
                if (factions.Count == 0) return true;
                typeahead.ClearSearch();
                selectedIndex = 0;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentFaction();
                return true;
            }

            // End — last faction
            if (key == KeyCode.End)
            {
                if (factions.Count == 0) return true;
                typeahead.ClearSearch();
                selectedIndex = factions.Count - 1;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceCurrentFaction();
                return true;
            }

            // Space — re-announce current faction
            if (key == KeyCode.Space)
            {
                AnnounceCurrentFaction();
                return true;
            }

            // Enter — consumed (read-only, no action)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                return true;
            }

            // Backspace — delete last search character
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = factions.Select(f => f.Name).ToList();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIndex = newIndex;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
                return true;
            }

            // Typeahead search — alphanumeric keys
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !ev.alt && !ev.control)
                {
                    char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                    HandleTypeahead(c);
                    return true;
                }
            }

            // Consume all other keys while menu is open to prevent pass-through
            return true;
        }

        #region Private Methods

        private static void AnnounceOpening()
        {
            if (factions.Count > 0)
            {
                var sb = new StringBuilder($"Faction relations, {factions.Count} factions");
                FactionHelper.AppendSentence(sb, FactionHelper.BuildFactionAnnouncement(factions[0]));

                string position = MenuHelper.FormatPosition(0, factions.Count);
                if (!string.IsNullOrEmpty(position))
                    FactionHelper.AppendSentence(sb, position);

                TolkHelper.Speak(sb.ToString());
            }
            else
            {
                TolkHelper.Speak("Faction relations. No factions.");
            }
        }

        private static void AnnounceCurrentFaction()
        {
            if (factions.Count == 0 || selectedIndex < 0 || selectedIndex >= factions.Count)
                return;

            string announcement = FactionHelper.BuildFactionAnnouncement(factions[selectedIndex]);
            string position = MenuHelper.FormatPosition(selectedIndex, factions.Count);
            if (!string.IsNullOrEmpty(position))
            {
                var sb = new StringBuilder(announcement);
                FactionHelper.AppendSentence(sb, position);
                announcement = sb.ToString();
            }

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceWithSearch()
        {
            if (factions.Count == 0 || selectedIndex < 0 || selectedIndex >= factions.Count)
                return;

            Faction faction = factions[selectedIndex];
            string name = faction.Name.CapitalizeFirst();
            string relation = faction.PlayerRelationKind.GetLabelCap();

            // Shorter announcement during search for readability
            var sb = new StringBuilder();
            sb.Append(name);

            if (faction.HasGoodwill && !faction.def.permanentEnemy)
            {
                sb.Append($", {relation}, goodwill {faction.PlayerGoodwill.ToStringWithSign()}");
            }
            else
            {
                sb.Append($", {relation}");
            }

            sb.Append($", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'");
            TolkHelper.Speak(sb.ToString());
        }

        private static void HandleTypeahead(char c)
        {
            var labels = factions.Select(f => f.Name).ToList();

            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                selectedIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'.");
            }
        }

        private static void OpenInfoCard()
        {
            if (factions.Count == 0 || selectedIndex < 0 || selectedIndex >= factions.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No faction selected.");
                return;
            }

            Faction faction = factions[selectedIndex];
            Find.WindowStack.Add(new Dialog_InfoCard(faction));
        }

        #endregion
    }
}
