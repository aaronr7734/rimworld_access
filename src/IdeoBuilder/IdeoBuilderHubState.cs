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
    /// Light hub for Page_ConfigureIdeo / Page_ConfigureFluidIdeo.
    ///
    /// Presents the live ideoligion as a flat menu of editable sections (name, structure
    /// meme, normal memes, deities, precepts, roles, rituals, etc.). Each row shows the
    /// current value pulled directly from the Ideo. Enter on a row opens that section's
    /// dedicated editor state (wired up in later phases). Alt+S advances to the next page,
    /// Escape goes back, both via reflection into the Page base class so we stay in sync
    /// with vanilla's validation and lifecycle.
    /// </summary>
    public static class IdeoBuilderHubState
    {
        public static bool IsActive { get; private set; }

        private static Ideo currentIdeo;
        private static List<IdeoBuilderHelper.HubSection> sections = new List<IdeoBuilderHelper.HubSection>();
        private static int selectedIndex;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static bool hasAnnouncedOpening;

        public static Ideo CurrentIdeo => currentIdeo;
        public static bool HasAnnouncedOpening => hasAnnouncedOpening;
        public static int SelectedIndex => selectedIndex;
        public static IdeoBuilderHelper.HubSection SelectedSection =>
            (selectedIndex >= 0 && selectedIndex < sections.Count) ? sections[selectedIndex] : null;

        #region Lifecycle

        /// <summary>
        /// Ensures the hub is initialized for the given ideo. If the ideo reference changes
        /// (e.g., the page swaps in a randomized one), the section list is rebuilt and the
        /// selection is reset.
        /// </summary>
        public static void EnsureOpen(Ideo ideo)
        {
            if (!IsActive)
            {
                IsActive = true;
                currentIdeo = ideo;
                selectedIndex = 0;
                typeahead.ClearSearch();
                hasAnnouncedOpening = false;
                RebuildSections();
            }
            else if (!System.Object.ReferenceEquals(currentIdeo, ideo))
            {
                currentIdeo = ideo;
                selectedIndex = 0;
                typeahead.ClearSearch();
                hasAnnouncedOpening = false;
                RebuildSections();
            }
        }

        public static void Close()
        {
            IsActive = false;
            currentIdeo = null;
            sections.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
            hasAnnouncedOpening = false;
        }

        public static void RebuildSections()
        {
            sections = IdeoBuilderHelper.BuildSections(currentIdeo);
            if (selectedIndex >= sections.Count)
                selectedIndex = System.Math.Max(0, sections.Count - 1);
        }

        public static void AnnounceOpeningIfNeeded()
        {
            if (hasAnnouncedOpening || currentIdeo == null) return;
            hasAnnouncedOpening = true;

            var sb = new StringBuilder();
            sb.Append(IdeoBuilderHelper.BuildOpeningAnnouncement(currentIdeo));
            sb.Append(". ");
            sb.Append(BuildCurrentSectionAnnouncement());
            TolkHelper.Speak(sb.ToString());
        }

        #endregion

        #region Navigation

        public static void NavigateUp()
        {
            if (sections.Count == 0) return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prev = typeahead.GetPreviousMatch(selectedIndex);
                if (prev >= 0)
                {
                    selectedIndex = prev;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
                return;
            }

            int newIndex = MenuHelper.SelectPrevious(selectedIndex, sections.Count);
            if (newIndex != selectedIndex)
            {
                selectedIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentSection();
        }

        public static void NavigateDown()
        {
            if (sections.Count == 0) return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int next = typeahead.GetNextMatch(selectedIndex);
                if (next >= 0)
                {
                    selectedIndex = next;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
                return;
            }

            int newIndex = MenuHelper.SelectNext(selectedIndex, sections.Count);
            if (newIndex != selectedIndex)
            {
                selectedIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentSection();
        }

        public static void NavigateHome()
        {
            if (sections.Count == 0) return;
            typeahead.ClearSearch();
            selectedIndex = 0;
            AnnounceCurrentSection();
        }

        public static void NavigateEnd()
        {
            if (sections.Count == 0) return;
            typeahead.ClearSearch();
            selectedIndex = sections.Count - 1;
            AnnounceCurrentSection();
        }

        #endregion

        #region Typeahead

        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        public static bool HandleTypeaheadChar(char c)
        {
            var labels = sections.Select(s => s.Label).ToList();
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
            return true;
        }

        public static bool HandleBackspace()
        {
            if (!typeahead.HasActiveSearch) return false;

            var labels = sections.Select(s => s.Label).ToList();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0) selectedIndex = newIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceWithSearch();
            }
            return true;
        }

        public static void ClearSearch()
        {
            typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentSection();
        }

        #endregion

        #region Activation

        /// <summary>
        /// Activates the currently selected section. In Phase 1, each section announces
        /// that its editor is not yet implemented. Phases 2-6 replace each branch with
        /// the actual editor open call (meme picker, precept picker, text input, etc.).
        /// </summary>
        public static void ActivateSelected()
        {
            var section = SelectedSection;
            if (section == null) return;

            if (section.Disabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak(string.IsNullOrEmpty(section.DisabledReason) ? "Unavailable" : section.DisabledReason);
                return;
            }

            // Never let an editor-open failure escape: the caller consumes the Enter key only after
            // this returns, so an exception here would leave Enter unconsumed and fall through to the
            // page's Next button (DoNext), advancing the player out of the builder.
            try
            {
                IdeoBuilderSectionActions.Activate(currentIdeo, section.Kind);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error opening editor for section {section.Kind}: {ex}");
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        #endregion

        #region Announcements

        private static string BuildCurrentSectionAnnouncement()
        {
            var section = SelectedSection;
            if (section == null) return "";

            var sb = new StringBuilder();
            sb.Append(section.Label);
            if (!string.IsNullOrEmpty(section.ValueSummary))
                sb.Append(": ").Append(section.ValueSummary);

            string position = MenuHelper.FormatPosition(selectedIndex, sections.Count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);

            return sb.ToString();
        }

        public static void AnnounceCurrentSection()
        {
            string text = BuildCurrentSectionAnnouncement();
            if (!string.IsNullOrEmpty(text))
                TolkHelper.Speak(text);
        }

        private static void AnnounceWithSearch()
        {
            var section = SelectedSection;
            if (section == null) return;

            string searchInfo = $", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            TolkHelper.Speak($"{section.Label}: {section.ValueSummary}{searchInfo}");
        }

        public static void AnnounceValidationOrImpact()
        {
            if (currentIdeo == null) return;
            string err = IdeoBuilderHelper.BuildValidationSummary(currentIdeo);
            if (!string.IsNullOrEmpty(err))
            {
                TolkHelper.Speak(err, SpeechPriority.High);
                return;
            }

            var sb = new StringBuilder();
            var normals = currentIdeo.memes.Where(m => m.category == MemeCategory.Normal).ToList();
            if (normals.Count > 0)
            {
                int impact = IdeoBuilderHelper.ImpactOf(normals);
                string impactLabel = IdeoImpactUtility.OverallImpactLabel(impact);
                sb.Append($"{"IdeoImpact".Translate()}: {impactLabel}.");
            }

            // Non-blocking precept warning (yellow warning vanilla shows near the continue button).
            string warning = IdeoBuilderHelper.BuildPlayerWarning(currentIdeo);
            if (!string.IsNullOrEmpty(warning))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(warning);
            }

            if (sb.Length > 0)
                TolkHelper.Speak(sb.ToString());
        }

        #endregion

        #region Context menu (] key): save, ritual sound preview

        private static readonly TextInputController saveController = new TextInputController();
        private static Sustainer ritualPreviewSustainer;

        /// <summary>Opens the builder context menu (']' key): save to file, preview ritual sound.</summary>
        public static void OpenContextMenu()
        {
            if (currentIdeo == null) return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Save".Translate() + " " + "Ideoligion".Translate().ToString().ToLower(), SaveIdeoligion),
            };

            if (currentIdeo.SoundOngoingRitual != null)
            {
                bool playing = ritualPreviewSustainer != null && !ritualPreviewSustainer.Ended;
                options.Add(new FloatMenuOption(
                    (playing ? "Stop" : "Preview") + " ritual sound", ToggleRitualPreview));
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void SaveIdeoligion()
        {
            if (currentIdeo == null) return;
            saveController.Begin(currentIdeo.name ?? "", TextFieldSpec.Unrestricted("Name"),
                text =>
                {
                    string fileName = GenFile.SanitizedFileName(text.Trim());
                    if (string.IsNullOrEmpty(fileName))
                    {
                        TolkHelper.Speak("NeedAName".Translate(), SpeechPriority.High);
                        return;
                    }
                    string absPath = GenFilePaths.AbsPathForIdeo(fileName);
                    LongEventHandler.QueueLongEvent(
                        () => GameDataSaveLoader.SaveIdeo(currentIdeo, absPath),
                        "SavingLongEvent", doAsynchronously: false, null);
                    TolkHelper.Speak("SavedAs".Translate(fileName), SpeechPriority.High);
                });
        }

        private static void ToggleRitualPreview()
        {
            if (ritualPreviewSustainer != null && !ritualPreviewSustainer.Ended)
            {
                StopRitualPreview();
                TolkHelper.Speak("RitualAmbienceSound".Translate().Resolve() + ", stopped.");
                return;
            }
            var sound = currentIdeo?.SoundOngoingRitual;
            if (sound == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            // Mirror the in-game viewer's working preview: force on-camera playback so the
            // sustainer is actually audible. MaintainRitualPreview then ducks the game music.
            var info = SoundInfo.OnCamera(MaintenanceType.PerFrame);
            info.forcedPlayOnCamera = true;
            info.testPlay = true;
            ritualPreviewSustainer = sound.TrySpawnSustainer(info);
            TolkHelper.Speak("RitualAmbienceSound".Translate().Resolve() + ", playing.");
        }

        /// <summary>
        /// Keeps the ritual-sound preview alive; called every frame from the hub patch. Wrapped so a
        /// sound-system failure can never propagate and stall the hub's input handling.
        /// </summary>
        public static void MaintainRitualPreview()
        {
            if (ritualPreviewSustainer == null) return;
            try
            {
                if (ritualPreviewSustainer.Ended)
                {
                    ritualPreviewSustainer = null;
                    return;
                }
                ritualPreviewSustainer.Maintain();
                // ForceSilenceFor lives only on MusicManagerPlay, and Find.MusicManagerPlay casts
                // Current.Root to Root_Play — which throws pre-game (the main-menu builder runs in
                // a Root_Entry). Only duck the music when actually in a running game.
                if (Current.ProgramState == ProgramState.Playing)
                    Find.MusicManagerPlay?.ForceSilenceFor(0.1f);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimWorld Access] Ritual sound preview stopped after an error: {ex.Message}");
                StopRitualPreview();
            }
        }

        private static void StopRitualPreview()
        {
            if (ritualPreviewSustainer != null)
            {
                if (!ritualPreviewSustainer.Ended) ritualPreviewSustainer.End();
                ritualPreviewSustainer = null;
            }
        }

        #endregion
    }
}
