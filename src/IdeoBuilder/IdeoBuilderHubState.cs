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

        // --- Two-tab shell: an ideoligion list (tab 1) and a detail panel (tab 2). The detail panel
        // is the section editor when the selected ideoligion is the one being built, and the
        // read-only viewer (the same tree the in-game Ideology tab uses) for any other ideoligion.
        // This mirrors vanilla's left-list / right-details layout. Tab / Shift+Tab switch tabs.
        public enum BuilderTab { Detail, List }
        private static BuilderTab currentTab = BuilderTab.Detail;
        private static List<Ideo> allIdeos = new List<Ideo>();
        private static int listIndex;
        private static readonly TypeaheadSearchHelper listTypeahead = new TypeaheadSearchHelper();
        private static readonly IdeologyTreeNavigation viewer = new IdeologyTreeNavigation();
        private static bool viewingOther;

        public static bool InListTab => currentTab == BuilderTab.List;
        public static bool ViewingOtherIdeo => currentTab == BuilderTab.Detail && viewingOther;

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
                ResetTabState();
            }
            else if (!System.Object.ReferenceEquals(currentIdeo, ideo))
            {
                currentIdeo = ideo;
                selectedIndex = 0;
                typeahead.ClearSearch();
                hasAnnouncedOpening = false;
                RebuildSections();
                ResetTabState();
            }
        }

        /// <summary>Resets the two-tab shell to the editor detail of the current ideoligion.</summary>
        private static void ResetTabState()
        {
            currentTab = BuilderTab.Detail;
            viewingOther = false;
            viewer.Reset();
            listTypeahead.ClearSearch();
            RebuildIdeoList();
        }

        public static void Close()
        {
            IsActive = false;
            currentIdeo = null;
            sections.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
            hasAnnouncedOpening = false;
            currentTab = BuilderTab.Detail;
            viewingOther = false;
            viewer.Reset();
            listTypeahead.ClearSearch();
            allIdeos.Clear();
            listIndex = 0;
        }

        // Set each frame by the host patch so the on-screen "Next" row and Alt+N can advance the page.
        internal static System.Action ContinueAction;

        public static void RebuildSections()
        {
            sections = IdeoBuilderHelper.BuildSections(currentIdeo);
            // Append an on-screen "Next" (continue) row so advancing is discoverable by navigating the
            // list — not only via the Alt+N shortcut. Builder-only: reform builds its own item list.
            sections.Add(new IdeoBuilderHelper.HubSection
            {
                Kind = IdeoBuilderHelper.SectionKind.Continue,
                Label = "Next".Translate(),
                ValueSummary = "Alt+S",
            });
            if (selectedIndex >= sections.Count)
                selectedIndex = System.Math.Max(0, sections.Count - 1);
        }

        public static void AnnounceOpeningIfNeeded()
        {
            if (hasAnnouncedOpening || currentIdeo == null) return;
            hasAnnouncedOpening = true;

            var sb = new StringBuilder();
            sb.Append(IdeoBuilderHelper.BuildOpeningAnnouncement(currentIdeo));
            // Hint the two-tab shell once on open (only when other ideoligions exist to browse).
            if (allIdeos.Count > 1)
                sb.Append(". ").Append((string)"RimWorldAccess.Ideology.Builder.TabForIdeoList".Translate());
            sb.Append(". ");
            sb.Append(BuildCurrentSectionAnnouncement());
            TolkHelper.SpeakData(sb.ToString());
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

        #region Two-tab shell (ideoligion list ↔ detail)

        /// <summary>Rebuilds the ideoligion list, preserving the focused ideoligion across the rebuild.</summary>
        private static void RebuildIdeoList()
        {
            Ideo keep = (allIdeos != null && listIndex >= 0 && listIndex < allIdeos.Count) ? allIdeos[listIndex] : null;
            allIdeos = IdeologyHelper.BuildIdeologyList();
            if (allIdeos.Count == 0) { listIndex = 0; return; }
            int idx = keep != null ? allIdeos.IndexOf(keep) : -1;
            if (idx < 0 && currentIdeo != null) idx = allIdeos.IndexOf(currentIdeo);
            listIndex = Mathf.Clamp(idx < 0 ? 0 : idx, 0, allIdeos.Count - 1);
        }

        /// <summary>Tab / Shift+Tab: toggle between the list and the detail panel.</summary>
        public static void TogglePanel()
        {
            if (currentTab == BuilderTab.List) EnterDetailForSelection();
            else SwitchToList();
        }

        /// <summary>Switch to the ideoligion list (tab 1).</summary>
        public static void SwitchToList()
        {
            if (viewingOther) viewer.Reset();
            viewingOther = false;
            currentTab = BuilderTab.List;
            listTypeahead.ClearSearch();
            RebuildIdeoList();
            AnnounceCurrentListItem(announceTabContext: true);
        }

        /// <summary>
        /// Open the detail panel for the list's current selection: the section editor when it's the
        /// ideoligion being built, the read-only viewer for any other.
        /// </summary>
        public static void EnterDetailForSelection()
        {
            listTypeahead.ClearSearch();
            if (allIdeos.Count == 0 || listIndex < 0 || listIndex >= allIdeos.Count)
            {
                GoToEditorDetail();
                return;
            }
            var sel = allIdeos[listIndex];
            if (ReferenceEquals(sel, currentIdeo))
            {
                GoToEditorDetail();
            }
            else
            {
                viewingOther = true;
                currentTab = BuilderTab.Detail;
                viewer.Initialize(sel); // builds the read-only tree and announces its first item
            }
        }

        /// <summary>Go to the editor detail of our own ideoligion (selecting ours, or Escape "home").</summary>
        public static void GoToEditorDetail()
        {
            if (viewingOther) viewer.Reset();
            viewingOther = false;
            currentTab = BuilderTab.Detail;
            int idx = currentIdeo != null ? allIdeos.IndexOf(currentIdeo) : -1;
            if (idx >= 0) listIndex = idx;
            AnnounceCurrentSection();
        }

        // --- List navigation ---

        public static bool ListHasActiveSearch => listTypeahead.HasActiveSearch;
        public static void ClearListSearch() { listTypeahead.ClearSearchAndAnnounce(); AnnounceCurrentListItem(false); }

        public static void ListNavigate(int delta)
        {
            if (allIdeos.Count == 0) return;
            if (listTypeahead.HasActiveSearch && !listTypeahead.HasNoMatches)
            {
                int idx = delta > 0 ? listTypeahead.GetNextMatch(listIndex) : listTypeahead.GetPreviousMatch(listIndex);
                if (idx >= 0)
                {
                    listIndex = idx;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceListWithSearch();
                }
                return;
            }
            int newIndex = delta > 0 ? MenuHelper.SelectNext(listIndex, allIdeos.Count) : MenuHelper.SelectPrevious(listIndex, allIdeos.Count);
            if (newIndex != listIndex)
            {
                listIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            AnnounceCurrentListItem(false);
        }

        public static void ListHome() { if (allIdeos.Count == 0) return; listTypeahead.ClearSearch(); listIndex = 0; AnnounceCurrentListItem(false); }
        public static void ListEnd() { if (allIdeos.Count == 0) return; listTypeahead.ClearSearch(); listIndex = allIdeos.Count - 1; AnnounceCurrentListItem(false); }
        public static void ListReannounce() => AnnounceCurrentListItem(false);

        public static bool ListTypeaheadChar(char c)
        {
            if (listTypeahead.ProcessCharacterInput(c, ListLabels(), out int newIndex))
            {
                listIndex = newIndex;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                AnnounceListWithSearch();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                listTypeahead.SpeakNoMatches();
            }
            return true;
        }

        public static bool ListBackspace()
        {
            if (!listTypeahead.HasActiveSearch) return false;
            if (listTypeahead.ProcessBackspace(ListLabels(), out int newIndex))
            {
                if (newIndex >= 0) listIndex = newIndex;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceListWithSearch();
            }
            return true;
        }

        // --- Viewer (read-only detail of another ideoligion) ---

        public static bool RouteViewerInput(Event ev) => viewer.HandleInput(ev);
        public static void ViewerTypeaheadChar(char c) => viewer.HandleTypeaheadCharacter(c);

        // --- List announcements ---

        private static List<string> ListLabels() => allIdeos.Select(i => i.name).ToList();

        private static void AnnounceCurrentListItem(bool announceTabContext)
        {
            if (allIdeos.Count == 0)
            {
                TolkHelper.SpeakData(MainButtonDefOf.Ideos.LabelCap + ". " + (string)"NoneLower".Translate());
                return;
            }
            if (listIndex < 0 || listIndex >= allIdeos.Count) listIndex = 0;
            var ideo = allIdeos[listIndex];

            var sb = new StringBuilder();
            if (announceTabContext)
                sb.Append(MainButtonDefOf.Ideos.LabelCap).Append(". ");
            sb.Append(IdeologyHelper.BuildIdeoListAnnouncement(ideo));
            if (ReferenceEquals(ideo, currentIdeo))
                sb.Append(", yours");
            string position = MenuHelper.FormatPosition(listIndex, allIdeos.Count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);
            TolkHelper.SpeakData(sb.ToString());
        }

        private static void AnnounceListWithSearch()
        {
            if (allIdeos.Count == 0 || listIndex < 0 || listIndex >= allIdeos.Count) return;
            string name = allIdeos[listIndex].name;
            TolkHelper.SpeakData(name + listTypeahead.BuildSearchContextSuffix());
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
                typeahead.SpeakNoMatches();
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
                TolkHelper.SpeakData(string.IsNullOrEmpty(section.DisabledReason)
                    ? (string)"RimWorldAccess.Ideology.Builder.Unavailable".Translate()
                    : section.DisabledReason);
                return;
            }

            // The synthetic "Next" row advances the page (same as Alt+N).
            if (section.Kind == IdeoBuilderHelper.SectionKind.Continue)
            {
                ContinueAction?.Invoke();
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
                TolkHelper.SpeakData(text);
        }

        private static void AnnounceWithSearch()
        {
            var section = SelectedSection;
            if (section == null) return;

            string baseText = string.IsNullOrEmpty(section.ValueSummary)
                ? section.Label
                : $"{section.Label}: {section.ValueSummary}";
            TolkHelper.SpeakData(baseText + typeahead.BuildSearchContextSuffix());
        }

        public static void AnnounceValidationOrImpact()
        {
            if (currentIdeo == null) return;
            string err = IdeoBuilderHelper.BuildValidationSummary(currentIdeo);
            if (!string.IsNullOrEmpty(err))
            {
                TolkHelper.SpeakData(err, SpeechPriority.High);
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
                TolkHelper.SpeakData(sb.ToString());
        }

        #endregion

        #region Context menu (] key): save, ritual sound preview

        // Save / randomize / ritual-sound preview live in the shared IdeoEditorCommands so the worldgen
        // builder and the in-game reform editor behave identically. These are thin pass-throughs on the
        // hub's current ideo; the hub patch's call sites are unchanged.

        /// <summary>Opens the builder context menu (']' key): save, randomize all, preview ritual sound.</summary>
        public static void OpenContextMenu(System.Action onRandomizeAll = null)
            => IdeoEditorCommands.OpenContextMenu(currentIdeo, onRandomizeAll);

        public static void SaveIdeoligion() => IdeoEditorCommands.SaveIdeoligion(currentIdeo);

        /// <summary>Keeps the ritual-sound preview alive; called every frame from the hub patch.</summary>
        public static void MaintainRitualPreview() => IdeoEditorCommands.MaintainRitualPreview();

        #endregion
    }
}
