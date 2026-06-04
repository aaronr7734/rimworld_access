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
    /// Manages keyboard navigation for the in-game Ideology tab (windowless).
    /// Two-panel interface: Panel 0 = ideology list, Panel 1 = ideology details tree.
    /// Tab/Shift+Tab cycles panels. Escape navigates back through panels then closes.
    /// Opened via F12 > Ideology or any path that activates MainTabWindow_Ideos.
    /// </summary>
    public static class IdeologyTabState
    {
        public static bool IsActive { get; private set; }

        private const int PanelList = 0;
        private const int PanelDetails = 1;

        private static int currentPanel;
        private static List<Ideo> ideologies = new List<Ideo>();
        private static int selectedIdeoIndex;
        private static IdeologyTreeNavigation navigation = new IdeologyTreeNavigation();
        private static TypeaheadSearchHelper listTypeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Opens the ideology tab state, builds ideology list,
        /// selects the player's primary ideology, and initializes the tree.
        /// </summary>
        public static void Open()
        {
            if (IsActive)
                return;

            IsActive = true;
            currentPanel = PanelList;
            ideologies = IdeologyHelper.BuildIdeologyList();
            listTypeahead.ClearSearch();

            // Default to player's primary ideology
            selectedIdeoIndex = 0;
            Ideo playerIdeo = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
            if (playerIdeo != null)
            {
                int idx = ideologies.IndexOf(playerIdeo);
                if (idx >= 0)
                    selectedIdeoIndex = idx;
            }

            AnnounceListOpening();
        }

        /// <summary>
        /// Closes the ideology tab state and resets all fields.
        /// </summary>
        public static void Close()
        {
            IdeologyTreeNavigation.StopRitualSound();
            IsActive = false;
            currentPanel = PanelList;
            ideologies.Clear();
            selectedIdeoIndex = 0;
            navigation.Reset();
            listTypeahead.ClearSearch();
            TolkHelper.Speak("RimWorldAccess.Ideology.Tab.Closed".Loc(MainButtonDefOf.Ideos.LabelCap));
        }

        /// <summary>
        /// Handles keyboard input for the ideology tab.
        /// Returns true if input was handled.
        /// Called from UnifiedKeyboardPatch which handles Event.current.Use().
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || ev.type != EventType.KeyDown)
                return false;

            // Tab / Shift+Tab — switch panels
            if (ev.keyCode == KeyCode.Tab)
            {
                if (ideologies.Count == 0)
                    return true;

                if (ev.shift)
                {
                    // Shift+Tab: go to previous panel
                    if (currentPanel == PanelDetails)
                    {
                        SwitchToListPanel();
                        return true;
                    }
                }
                else
                {
                    // Tab: go to next panel, or wrap back to list from details
                    if (currentPanel == PanelList)
                    {
                        SwitchToDetailsPanel();
                        return true;
                    }
                    else if (currentPanel == PanelDetails)
                    {
                        SwitchToListPanel();
                        return true;
                    }
                }
                return true;
            }

            // Delegate to current panel
            if (currentPanel == PanelList)
                return HandleListInput(ev);
            else
                return HandleDetailsInput(ev);
        }

        #region Panel Switching

        private static void SwitchToDetailsPanel()
        {
            currentPanel = PanelDetails;
            listTypeahead.ClearSearch();
            // Re-initialize tree in case ideology selection changed
            if (ideologies.Count > 0 && selectedIdeoIndex >= 0 && selectedIdeoIndex < ideologies.Count)
                navigation.Initialize(ideologies[selectedIdeoIndex]);
        }

        private static void SwitchToListPanel()
        {
            currentPanel = PanelList;
            navigation.Reset();
            AnnounceCurrentListItem();
        }

        #endregion

        #region List Panel (Panel 0)

        private static void HandleListInput_Navigate(int newIndex)
        {
            if (newIndex != selectedIdeoIndex)
            {
                selectedIdeoIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentListItem();
        }

        private static bool HandleListInput(Event ev)
        {
            KeyCode key = ev.keyCode;

            // Escape — clear search or close
            if (key == KeyCode.Escape)
            {
                if (listTypeahead.HasActiveSearch)
                {
                    listTypeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentListItem();
                    return true;
                }
                Close();
                return true;
            }

            // Up arrow
            if (key == KeyCode.UpArrow)
            {
                if (ideologies.Count == 0) return true;
                if (listTypeahead.HasActiveSearch && !listTypeahead.HasNoMatches)
                {
                    int prev = listTypeahead.GetPreviousMatch(selectedIdeoIndex);
                    if (prev >= 0)
                    {
                        selectedIdeoIndex = prev;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceListWithSearch();
                    }
                }
                else
                {
                    int newIndex = MenuHelper.SelectPrevious(selectedIdeoIndex, ideologies.Count);
                    HandleListInput_Navigate(newIndex);
                }
                return true;
            }

            // Down arrow
            if (key == KeyCode.DownArrow)
            {
                if (ideologies.Count == 0) return true;
                if (listTypeahead.HasActiveSearch && !listTypeahead.HasNoMatches)
                {
                    int next = listTypeahead.GetNextMatch(selectedIdeoIndex);
                    if (next >= 0)
                    {
                        selectedIdeoIndex = next;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceListWithSearch();
                    }
                }
                else
                {
                    int newIndex = MenuHelper.SelectNext(selectedIdeoIndex, ideologies.Count);
                    HandleListInput_Navigate(newIndex);
                }
                return true;
            }

            // Home
            if (key == KeyCode.Home)
            {
                if (ideologies.Count == 0) return true;
                listTypeahead.ClearSearch();
                HandleListInput_Navigate(0);
                return true;
            }

            // End
            if (key == KeyCode.End)
            {
                if (ideologies.Count == 0) return true;
                listTypeahead.ClearSearch();
                HandleListInput_Navigate(ideologies.Count - 1);
                return true;
            }

            // Space — re-announce
            if (key == KeyCode.Space)
            {
                AnnounceCurrentListItem();
                return true;
            }

            // Enter — re-announce (same as Space, no panel switch)
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                AnnounceCurrentListItem();
                return true;
            }

            // Backspace — delete last search character
            if (key == KeyCode.Backspace && listTypeahead.HasActiveSearch)
            {
                var labels = GetIdeoLabels();
                if (listTypeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0)
                        selectedIdeoIndex = newIndex;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    AnnounceListWithSearch();
                }
                return true;
            }

            // Typeahead search
            {
                bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
                bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

                if ((isLetter || isNumber) && !ev.alt && !ev.control)
                {
                    return true;
                }
            }

            // Consume all other keys
            return true;
        }

        private static List<string> GetIdeoLabels()
        {
            return ideologies.Select(i => i.name).ToList();
        }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// Only routes to the list panel; the details panel has its own tree typeahead.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!IsActive) return;
            if (currentPanel != PanelList) return;

            var labels = GetIdeoLabels();
            if (listTypeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                selectedIdeoIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceListWithSearch();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                listTypeahead.SpeakNoMatches();
            }
        }

        #endregion

        #region Details Panel (Panel 1)

        private static bool HandleDetailsInput(Event ev)
        {
            // Delegate to tree navigation first
            if (navigation.HandleInput(ev))
                return true;

            // Escape or Tab with no active search — return to list panel
            if (ev.keyCode == KeyCode.Escape || ev.keyCode == KeyCode.Tab)
            {
                SwitchToListPanel();
                return true;
            }

            return true;
        }

        #endregion

        #region Announcements

        private static void AnnounceListOpening()
        {
            string tabLabel = MainButtonDefOf.Ideos.LabelCap;

            if (ideologies.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Ideology.Tab.Empty".Translate(tabLabel, "NoneLower".Translate()));
                return;
            }

            var sb = new StringBuilder(tabLabel);
            IdeologyHelper.AppendSentence(sb, "RimWorldAccess.Ideology.Tab.ItemCount".Translate(ideologies.Count, tabLabel.ToString().ToLower()));

            string announcement = IdeologyHelper.BuildIdeoListAnnouncement(ideologies[selectedIdeoIndex]);
            IdeologyHelper.AppendSentence(sb, announcement);

            string position = MenuHelper.FormatPosition(selectedIdeoIndex, ideologies.Count);
            if (!string.IsNullOrEmpty(position))
                IdeologyHelper.AppendSentence(sb, position);

            TolkHelper.Speak(sb.ToString());
        }

        private static void AnnounceCurrentListItem()
        {
            if (ideologies.Count == 0 || selectedIdeoIndex < 0 || selectedIdeoIndex >= ideologies.Count)
                return;

            string announcement = IdeologyHelper.BuildIdeoListAnnouncement(ideologies[selectedIdeoIndex]);
            string position = MenuHelper.FormatPosition(selectedIdeoIndex, ideologies.Count);
            string positionSection = string.IsNullOrEmpty(position) ? "" : ". " + position;

            TolkHelper.Speak(announcement + positionSection);
        }

        private static void AnnounceListWithSearch()
        {
            if (ideologies.Count == 0 || selectedIdeoIndex < 0 || selectedIdeoIndex >= ideologies.Count)
                return;

            string name = ideologies[selectedIdeoIndex].name;
            TolkHelper.Speak(listTypeahead.BuildItemAnnouncement(name));
        }

        #endregion
    }
}
