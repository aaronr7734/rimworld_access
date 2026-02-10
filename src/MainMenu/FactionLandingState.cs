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
    /// Manages keyboard navigation state for Dialog_FactionDuringLanding.
    /// Provides a flat list of factions with typeahead search, opened via F key during starting site selection.
    /// </summary>
    public static class FactionLandingState
    {
        public static bool IsActive { get; private set; }

        /// <summary>
        /// Frame number when we last handled Escape to close the dialog.
        /// Used by FactionLandingPatch.Page_OnCancelKeyPressed_Patch to block
        /// the game's Cancel handling in the same frame (since Event.current.Use()
        /// does not prevent HandleEventsHighPriority from firing).
        /// </summary>
        internal static int escapeHandledOnFrame = -1;

        private static Dialog_FactionDuringLanding currentDialog;
        private static List<Faction> factions = new List<Faction>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Opens the faction landing state for a dialog.
        /// Called from FactionLandingPatch when Dialog_FactionDuringLanding opens.
        /// </summary>
        public static void Open(Dialog_FactionDuringLanding dialog)
        {
            if (dialog == null)
                return;

            currentDialog = dialog;
            IsActive = true;

            // Prevent RimWorld from closing on Enter/Escape — we handle both
            dialog.closeOnAccept = false;
            dialog.closeOnCancel = false;

            factions = BuildFactionList();
            selectedIndex = 0;
            typeahead.ClearSearch();

            AnnounceOpening();
        }

        /// <summary>
        /// Closes the faction landing state and resets all fields.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentDialog = null;
            factions.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Handles keyboard input for the faction landing dialog.
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

            // Escape — clear search first, then close dialog
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentFaction();
                    return true;
                }
                CloseDialog();
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

            // Enter — consumed (do nothing, prevents dialog close)
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

            // Consume all other keys while dialog is open to prevent pass-through
            return true;
        }

        #region Private Methods

        /// <summary>
        /// Builds the list of visible factions, mirroring FactionUIUtility.DoWindowContents filtering.
        /// Sorted by defeated ascending, then listOrderPriority descending (via AllFactionsInViewOrder).
        /// </summary>
        private static List<Faction> BuildFactionList()
        {
            var result = new List<Faction>();
            foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
            {
                if (!faction.IsPlayer && !faction.Hidden)
                {
                    result.Add(faction);
                }
            }
            return result;
        }

        /// <summary>
        /// Builds the full announcement string for a faction, including all visible data.
        /// Avoids double periods by checking existing punctuation.
        /// </summary>
        private static string BuildFactionAnnouncement(Faction faction)
        {
            var sb = new StringBuilder();

            // Faction name
            sb.Append(faction.Name.CapitalizeFirst());

            // Defeated status
            if (faction.defeated)
                sb.Append(", defeated");

            // Faction type
            AppendSentence(sb, faction.def.LabelCap.Resolve());

            // Leader info
            if (faction.leader != null)
            {
                string leaderTitle = faction.LeaderTitle.CapitalizeFirst();
                string leaderName = faction.leader.Name.ToStringFull;
                AppendSentence(sb, $"{leaderTitle}: {leaderName}");
            }

            // Relation and goodwill
            string relation = faction.PlayerRelationKind.GetLabelCap();
            if (faction.HasGoodwill && !faction.def.permanentEnemy)
            {
                AppendSentence(sb, $"{relation}, goodwill {faction.PlayerGoodwill.ToStringWithSign()}, natural goodwill {faction.NaturalGoodwill.ToStringWithSign()}");
            }
            else if (faction.def.permanentEnemy)
            {
                AppendSentence(sb, $"{relation}, permanent enemy");
            }
            else
            {
                AppendSentence(sb, relation);
            }

            // Ideology (if Ideology DLC active and not classic mode)
            if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && faction.ideos != null)
            {
                if (faction.ideos.PrimaryIdeo != null)
                {
                    AppendSentence(sb, $"Ideology: {faction.ideos.PrimaryIdeo.name}");
                }

                var minor = faction.ideos.IdeosMinorListForReading;
                if (minor != null && minor.Count > 0)
                {
                    var minorNames = minor.Select(i => i.name);
                    AppendSentence(sb, $"Minor ideologies: {string.Join(", ", minorNames)}");
                }
            }

            // Enemy-of list
            var enemies = Find.FactionManager.AllFactionsInViewOrder
                .Where(f => f != faction && f.HostileTo(faction) && !f.IsPlayer && !f.Hidden)
                .ToArray();

            if (enemies.Length > 0)
            {
                var enemyNames = enemies.Select(f => f.Name).ToArray();
                AppendSentence(sb, $"Enemy of: {string.Join(", ", enemyNames)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Appends text as a new sentence, ensuring no double periods.
        /// Adds ". " separator only if the current text doesn't already end with punctuation.
        /// </summary>
        private static void AppendSentence(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (sb.Length > 0)
            {
                char lastChar = sb[sb.Length - 1];
                if (lastChar != '.' && lastChar != '!' && lastChar != '?')
                    sb.Append('.');
                sb.Append(' ');
            }

            sb.Append(text);
        }

        /// <summary>
        /// Announces the dialog opening and the first faction.
        /// </summary>
        private static void AnnounceOpening()
        {
            if (factions.Count > 0)
            {
                var sb = new StringBuilder($"Faction relations, {factions.Count} factions");
                AppendSentence(sb, BuildFactionAnnouncement(factions[0]));

                string position = MenuHelper.FormatPosition(0, factions.Count);
                if (!string.IsNullOrEmpty(position))
                    AppendSentence(sb, position);

                TolkHelper.Speak(sb.ToString());
            }
            else
            {
                TolkHelper.Speak("Faction relations. No factions.");
            }
        }

        /// <summary>
        /// Announces the currently selected faction with full details and position.
        /// </summary>
        private static void AnnounceCurrentFaction()
        {
            if (factions.Count == 0 || selectedIndex < 0 || selectedIndex >= factions.Count)
                return;

            string announcement = BuildFactionAnnouncement(factions[selectedIndex]);
            string position = MenuHelper.FormatPosition(selectedIndex, factions.Count);
            if (!string.IsNullOrEmpty(position))
            {
                var sb = new StringBuilder(announcement);
                AppendSentence(sb, position);
                announcement = sb.ToString();
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Announces the current faction with search context.
        /// </summary>
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

        /// <summary>
        /// Handles typeahead search character input.
        /// </summary>
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

        /// <summary>
        /// Opens a Dialog_InfoCard for the currently selected faction.
        /// InfoCardPatch will auto-activate InfoCardState via PostOpen.
        /// </summary>
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

        /// <summary>
        /// Closes the dialog via WindowStack and announces closure.
        /// Calls Close() directly rather than relying on PostClose patch,
        /// which may not fire reliably for all Window subclasses.
        /// </summary>
        private static void CloseDialog()
        {
            escapeHandledOnFrame = Time.frameCount;
            if (currentDialog != null)
            {
                Find.WindowStack.TryRemove(currentDialog, doCloseSound: false);
            }
            Close();
            TolkHelper.Speak("Faction relations closed.");
        }

        #endregion
    }
}
