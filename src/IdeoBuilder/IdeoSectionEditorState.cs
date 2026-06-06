using System;
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
    /// Host-independent section editor for a single <see cref="Ideo"/>. Presents the same flat
    /// facet menu the Custom-creation hub and the in-game reform dialog use (name, memes, precepts,
    /// deities, appearance, …) built from <see cref="IdeoBuilderHelper.BuildSections"/>, and
    /// dispatches each row to its editor via <see cref="IdeoBuilderSectionActions.Activate"/>. Edits
    /// apply directly to the supplied ideo.
    ///
    /// Unlike <see cref="IdeoBuilderHubState"/> (which also owns a two-tab shell and an ideoligion
    /// list) and <see cref="IdeoReformState"/> (which is tied to <c>Dialog_ReformIdeo</c>'s staged
    /// flow), this state is just the editor, so any screen that needs to make one ideoligion fully
    /// editable can host it. Currently used by the Archonexus reform screen's detail tab for the
    /// player's newly created/loaded ideoligion — the one facet vanilla's
    /// <c>Dialog_ConfigureIdeo</c> renders in edit mode.
    ///
    /// The host owns Tab (leave the editor) and Alt+S (the host's confirm); this state owns
    /// navigation, typeahead, Enter (open a section's editor) and Escape-clears-search.
    /// </summary>
    public static class IdeoSectionEditorState
    {
        public static bool IsActive { get; private set; }

        private static Ideo ideo;
        private static readonly List<IdeoBuilderHelper.HubSection> sections = new List<IdeoBuilderHelper.HubSection>();
        private static int selectedIndex;
        private static readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        #region Lifecycle

        public static void Open(Ideo target, bool announce = true)
        {
            ideo = target;
            IsActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();
            Rebuild();
            if (announce)
                AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            ideo = null;
            sections.Clear();
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Rebuilds the section value summaries and re-announces the current row. Called after an
        /// edit (returning from a meme picker / overlay / text edit) so the row values stay live.
        /// </summary>
        public static void Refresh()
        {
            if (!IsActive || ideo == null) return;
            IdeoBuilderHelper.SectionKind? keep =
                (selectedIndex >= 0 && selectedIndex < sections.Count) ? sections[selectedIndex].Kind : (IdeoBuilderHelper.SectionKind?)null;
            Rebuild();
            if (keep.HasValue)
            {
                int i = sections.FindIndex(s => s.Kind == keep.Value);
                if (i >= 0) selectedIndex = i;
            }
            if (selectedIndex >= sections.Count)
                selectedIndex = Math.Max(0, sections.Count - 1);
            AnnounceCurrent();
        }

        private static void Rebuild()
        {
            sections.Clear();
            if (ideo != null)
                sections.AddRange(IdeoBuilderHelper.BuildSections(ideo));
        }

        #endregion

        #region Input

        /// <summary>
        /// Handles a key for the section editor. Returns false only for keys the host should act on
        /// (Escape with no active search), so the host can leave the editor; everything else is owned.
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;

            // Alt+R — randomize the entire ideoligion (parity with the worldgen hub's editor).
            if (key == KeyCode.R && alt && !ctrl)
            {
                TryRandomizeAll();
                return true;
            }

            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrent();
                    return true;
                }
                return false; // host leaves the editor (back to its list)
            }

            if (sections.Count == 0) return true;

            if (key == KeyCode.UpArrow) { Move(-1); return true; }
            if (key == KeyCode.DownArrow) { Move(1); return true; }
            if (key == KeyCode.Home) { typeahead.ClearSearch(); selectedIndex = 0; AnnounceCurrent(); return true; }
            if (key == KeyCode.End) { typeahead.ClearSearch(); selectedIndex = sections.Count - 1; AnnounceCurrent(); return true; }

            // ] — editor context menu (save, randomize all, preview ritual sound). Matches the hub.
            if (key == KeyCode.RightBracket && !alt && !ctrl)
            {
                IdeoEditorCommands.OpenContextMenu(ideo, onRandomizeAll: TryRandomizeAll);
                return true;
            }

            // Enter — open the focused section's editor.
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !alt && !ctrl)
            {
                Activate();
                return true;
            }

            // Space — re-announce the current section (parity with the hub; Enter is the activator).
            if (key == KeyCode.Space && !alt && !ctrl)
            {
                AnnounceCurrent();
                return true;
            }

            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch && typeahead.ProcessBackspace(Labels(), out int ni))
                {
                    if (ni >= 0) selectedIndex = ni;
                    AnnounceWithSearch();
                }
                return true;
            }

            char c = ev.character;
            if (!alt && !ctrl && c != '\0' && char.IsLetterOrDigit(c))
            {
                if (typeahead.ProcessCharacterInput(c, Labels(), out int ni))
                {
                    selectedIndex = ni;
                    AnnounceWithSearch();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    typeahead.SpeakNoMatches();
                }
                return true;
            }

            return true; // own all other keys while the editor is up
        }

        private static void Move(int delta)
        {
            if (sections.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int mi = delta > 0 ? typeahead.GetNextMatch(selectedIndex) : typeahead.GetPreviousMatch(selectedIndex);
                if (mi >= 0) selectedIndex = mi;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceWithSearch();
                return;
            }
            selectedIndex = delta > 0
                ? MenuHelper.SelectNext(selectedIndex, sections.Count)
                : MenuHelper.SelectPrevious(selectedIndex, sections.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent();
        }

        /// <summary>
        /// Randomizes the whole ideoligion behind a confirmation (an accidental Alt+R must not wipe
        /// the player's work — vanilla shows no warning). Mirrors the hub's TryRandomizeAll.
        /// </summary>
        private static void TryRandomizeAll()
        {
            if (ideo == null) return;
            // Don't stack confirmations.
            if (WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive) return;

            Action confirm = () =>
            {
                if (IdeoEditorCommands.RandomizeAll(ideo))
                {
                    Rebuild();
                    AnnounceCurrent();
                    AnnounceValidationOrImpact();
                }
            };
            Find.WindowStack.Add(new Dialog_MessageBox(
                "Randomizing will replace the entire ideoligion. Continue?",
                buttonAText: "Continue",
                buttonAAction: confirm,
                buttonBText: "Cancel",
                buttonBAction: null,
                title: null,
                buttonADestructive: true,
                acceptAction: confirm,
                cancelAction: delegate { }));
        }

        private static void Activate()
        {
            if (selectedIndex < 0 || selectedIndex >= sections.Count) return;
            var section = sections[selectedIndex];
            if (section.Disabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                if (!string.IsNullOrEmpty(section.DisabledReason))
                    TolkHelper.Speak(section.DisabledReason, SpeechPriority.High);
                return;
            }
            typeahead.ClearSearch();
            IdeoBuilderSectionActions.Activate(ideo, section.Kind);
        }

        #endregion

        #region Announcements

        private static List<string> Labels() => sections.Select(s => s.Label).ToList();

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            // Same opening as the worldgen hub: title + name + overall impact line.
            sb.Append(IdeoBuilderHelper.BuildOpeningAnnouncement(ideo));
            sb.Append(". ").Append("Tab to return to the list");
            if (sections.Count > 0)
                sb.Append(". ").Append(BuildCurrentText());
            TolkHelper.Speak(sb.ToString(), SpeechPriority.High);
        }

        private static void AnnounceCurrent()
        {
            if (sections.Count == 0) return;
            string text = BuildCurrentText();
            if (!string.IsNullOrEmpty(text))
                TolkHelper.Speak(text);
        }

        /// <summary>Announces the focused section plus the typeahead match position (parity with the hub).</summary>
        private static void AnnounceWithSearch()
        {
            if (selectedIndex < 0 || selectedIndex >= sections.Count) return;
            var s = sections[selectedIndex];
            string value = string.IsNullOrEmpty(s.ValueSummary) ? "" : ": " + s.ValueSummary;
            string searchInfo = $", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            TolkHelper.Speak($"{s.Label}{value}{searchInfo}");
        }

        /// <summary>
        /// After a whole-ideoligion change (randomize), announce the blocking validation error if any,
        /// otherwise the overall impact plus any non-blocking precept warning. Mirrors the hub.
        /// </summary>
        private static void AnnounceValidationOrImpact()
        {
            if (ideo == null) return;
            string err = IdeoBuilderHelper.BuildValidationSummary(ideo);
            if (!string.IsNullOrEmpty(err))
            {
                TolkHelper.Speak(err, SpeechPriority.High);
                return;
            }

            var sb = new StringBuilder();
            var normals = ideo.memes.Where(m => m.category == MemeCategory.Normal).ToList();
            if (normals.Count > 0)
            {
                int impact = IdeoBuilderHelper.ImpactOf(normals);
                sb.Append($"{"IdeoImpact".Translate()}: {IdeoImpactUtility.OverallImpactLabel(impact)}.");
            }

            string warning = IdeoBuilderHelper.BuildPlayerWarning(ideo);
            if (!string.IsNullOrEmpty(warning))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(warning);
            }

            if (sb.Length > 0)
                TolkHelper.Speak(sb.ToString());
        }

        private static string BuildCurrentText()
        {
            if (selectedIndex < 0 || selectedIndex >= sections.Count) return "";
            var s = sections[selectedIndex];
            var sb = new StringBuilder();
            sb.Append(s.Label);
            if (!string.IsNullOrEmpty(s.ValueSummary))
                sb.Append(": ").Append(s.ValueSummary);
            if (s.Disabled && !string.IsNullOrEmpty(s.DisabledReason))
                sb.Append(". ").Append(s.DisabledReason);

            string position = MenuHelper.FormatPosition(selectedIndex, sections.Count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);
            return sb.ToString();
        }

        #endregion
    }
}
