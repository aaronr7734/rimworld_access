using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard-accessible driver for the Archonexus relocation selection screen
    /// (Dialog_ChooseThingsForNewColony). Vanilla draws four sections in one
    /// scrolled column — People / Animals / Relics / Items — so we present them
    /// as four tabs: Left/Right (or Tab/Shift+Tab) switches sections, Up/Down
    /// navigates within, and typeahead matches only within the current section.
    /// Empty sections are skipped (matches vanilla's `count &gt; 0` draw gate).
    /// Accept is gated solely by the dialog's own AcceptanceReport so it is
    /// structurally impossible to relocate with more than the allowed number of
    /// any category; on rejection the red reason is announced.
    /// </summary>
    public static class ArchonexusColonyState
    {
        public enum Section { Colonists, Animals, Relics, Items }

        public static bool IsActive { get; private set; }

        /// <summary>
        /// True when a typeahead search is filtering the current tab. The
        /// OnCancelKeyPressed prefix uses this to decide whether to intercept
        /// Escape (clear the search) or let vanilla close the dialog as a
        /// guaranteed exit.
        /// </summary>
        public static bool HasActiveSearch =>
            sections.Count > 0 && CurrentTypeahead.HasActiveSearch;

        private static Dialog_ChooseThingsForNewColony dialog;

        // Tab order is fixed (matches vanilla's draw order). Only non-empty sections appear.
        private static readonly Section[] AllSections = { Section.Colonists, Section.Animals, Section.Relics, Section.Items };
        private static List<Section> sections = new List<Section>();
        private static int currentSectionIdx;

        private static readonly Dictionary<Section, List<Thing>> entries = new Dictionary<Section, List<Thing>>();
        private static readonly Dictionary<Section, int> selectedIndex = new Dictionary<Section, int>();
        private static readonly Dictionary<Section, TypeaheadSearchHelper> typeaheads = new Dictionary<Section, TypeaheadSearchHelper>();

        private static Section CurrentSection => sections[currentSectionIdx];
        private static List<Thing> CurrentEntries => entries[CurrentSection];
        private static int CurrentSelectedIndex
        {
            get => selectedIndex[CurrentSection];
            set => selectedIndex[CurrentSection] = value;
        }
        private static TypeaheadSearchHelper CurrentTypeahead => typeaheads[CurrentSection];

        #region Reflection cache

        private static readonly Type DialogType = typeof(Dialog_ChooseThingsForNewColony);
        private static readonly FieldInfo MaxColonistsField = AccessTools.Field(DialogType, "maxColonists");
        private static readonly FieldInfo MaxAnimalsField = AccessTools.Field(DialogType, "maxAnimals");
        private static readonly FieldInfo MaxRelicsField = AccessTools.Field(DialogType, "maxRelics");
        private static readonly FieldInfo MaxItemsField = AccessTools.Field(DialogType, "maxItems");
        private static readonly FieldInfo ColonistsField = AccessTools.Field(DialogType, "colonists");
        private static readonly FieldInfo AnimalsField = AccessTools.Field(DialogType, "animals");
        private static readonly FieldInfo RelicsField = AccessTools.Field(DialogType, "relics");
        private static readonly FieldInfo ItemsField = AccessTools.Field(DialogType, "items");
        private static readonly FieldInfo SelectedField = AccessTools.Field(DialogType, "selected");
        private static readonly FieldInfo SelectedItemCountField = AccessTools.Field(DialogType, "selectedItemCount");
        private static readonly FieldInfo ItemAllowedStackCountField = AccessTools.Field(DialogType, "itemArchonexusAllowedStackCount");
        private static readonly PropertyInfo AcceptanceReportProp = AccessTools.Property(DialogType, "AcceptanceReport");
        private static readonly PropertyInfo ColonistCountProp = AccessTools.Property(DialogType, "ColonistCount");
        private static readonly PropertyInfo AnimalCountProp = AccessTools.Property(DialogType, "AnimalCount");
        private static readonly PropertyInfo RelicCountProp = AccessTools.Property(DialogType, "RelicCount");
        private static readonly PropertyInfo SlaveCountProp = AccessTools.Property(DialogType, "SlaveCount");
        private static readonly MethodInfo ConfirmConsequencesMethod = AccessTools.Method(DialogType, "ConfirmArchonexusSettlementConsequences");

        #endregion

        #region Lifecycle

        public static void EnsureOpen(Dialog_ChooseThingsForNewColony d)
        {
            // Reference equality alone — see IdeoLoadState.EnsureOpen for the
            // window-stack snapshot-tail rationale. Keying off IsActive would
            // re-announce every time vanilla closes the dialog.
            if (ReferenceEquals(dialog, d))
                return;
            dialog = d;
            IsActive = true;

            // This dialog is the first screen of the relocation chain, reached by accepting the
            // quest from the (windowless) quest menu — which does NOT close on accept, so it
            // lingers active through the whole chain. Later screens that are NOT protected by the
            // UnifiedKeyboardPatch EXCLUSIVE block (notably the world-tile pick) would then route
            // arrows to the lingering quest menu instead of the world map. Clear it here, silently,
            // at the head of the chain. (Screens 1 and 4 are exclusive-protected regardless; this
            // is what frees Screen 3.)
            if (QuestMenuState.IsActive)
                QuestMenuState.Close(announce: false);

            RebuildAll();
            AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            sections.Clear();
            entries.Clear();
            selectedIndex.Clear();
            typeaheads.Clear();
            currentSectionIdx = 0;
            // dialog reference is intentionally retained — see EnsureOpen.
        }

        private static void RebuildAll()
        {
            sections.Clear();
            entries.Clear();
            selectedIndex.Clear();
            typeaheads.Clear();
            currentSectionIdx = 0;

            foreach (Section s in AllSections)
            {
                var list = GetSourceList(s);
                if (list == null || list.Count == 0)
                    continue; // matches vanilla's `count > 0` draw gate
                sections.Add(s);
                entries[s] = list;
                selectedIndex[s] = 0;
                typeaheads[s] = new TypeaheadSearchHelper();
            }
        }

        private static List<Thing> GetSourceList(Section s)
        {
            FieldInfo f;
            switch (s)
            {
                case Section.Colonists: f = ColonistsField; break;
                case Section.Animals: f = AnimalsField; break;
                case Section.Relics: f = RelicsField; break;
                case Section.Items: f = ItemsField; break;
                default: return null;
            }
            return f.GetValue(dialog) as List<Thing>;
        }

        #endregion

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;
            bool shift = ev.shift;

            if (sections.Count == 0)
                return HandleEmpty(key, alt, ctrl);

            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (CurrentTypeahead.HasActiveSearch)
                {
                    CurrentTypeahead.ClearSearchAndAnnounce();
                    AnnounceCurrent(includeSectionHeader: false);
                    return true;
                }
                // Let vanilla OnCancelKeyPressed close the dialog and fire the cancel
                // callback (the questline's outSignalCancelled). Returning false here
                // and the OnCancel prefix gate together restore the game's escape hatch.
                return false;
            }

            // Tab navigation: Left/Right (and Tab/Shift+Tab as an alias).
            if (key == KeyCode.LeftArrow && !alt && !ctrl) { SwitchSection(-1); return true; }
            if (key == KeyCode.RightArrow && !alt && !ctrl) { SwitchSection(1); return true; }
            if (key == KeyCode.Tab && !alt && !ctrl) { SwitchSection(shift ? -1 : 1); return true; }

            if (key == KeyCode.UpArrow) { Move(-1); return true; }
            if (key == KeyCode.DownArrow) { Move(1); return true; }
            if (key == KeyCode.Home) { CurrentTypeahead.ClearSearch(); CurrentSelectedIndex = 0; AnnounceCurrent(includeSectionHeader: false); return true; }
            if (key == KeyCode.End) { CurrentTypeahead.ClearSearch(); CurrentSelectedIndex = CurrentEntries.Count - 1; AnnounceCurrent(includeSectionHeader: false); return true; }

            // Space and Enter both toggle the current row's checkbox (parity with the
            // caravan formation screen); sending the colony off is Alt+S.
            if (key == KeyCode.Space && !alt && !ctrl) { ToggleCurrent(); return true; }
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !alt && !ctrl) { ToggleCurrent(); return true; }

            if (key == KeyCode.S && alt && !ctrl) { AttemptAccept(); return true; }

            // Alt+H/M/N/G/K read the focused pawn's health / mood / needs / gear / skills,
            // exactly like the caravan formation screen. Only the People and Animals tabs
            // hold pawns; on Relics/Items these keys fall through.
            if (alt && !ctrl && !shift)
            {
                Pawn selectedPawn = GetSelectedPawn();
                if (selectedPawn != null &&
                    CaravanInputHelper.HandlePawnInfoShortcuts(key, selectedPawn, alt, shift, ctrl))
                    return true;
            }

            if (key == KeyCode.I && alt && !ctrl) { OpenInfoCard(); return true; }

            if (key == KeyCode.T && !alt && !ctrl) { AnnounceStatus(); return true; }

            if (key == KeyCode.Backspace)
            {
                if (CurrentTypeahead.HasActiveSearch && CurrentTypeahead.ProcessBackspace(LabelsFor(CurrentSection), out int ni))
                {
                    if (ni >= 0) CurrentSelectedIndex = ni;
                    AnnounceCurrent(includeSectionHeader: false);
                }
                return true;
            }

            char c = ev.character;
            if (!alt && !ctrl && c != '\0' && char.IsLetterOrDigit(c))
            {
                if (CurrentTypeahead.ProcessCharacterInput(c, LabelsFor(CurrentSection), out int ni))
                {
                    CurrentSelectedIndex = ni;
                    AnnounceCurrent(includeSectionHeader: false);
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    CurrentTypeahead.SpeakNoMatches();
                }
                return true;
            }

            return false;
        }

        private static bool HandleEmpty(KeyCode key, bool alt, bool ctrl)
        {
            // No eligible things in any section. Escape falls through to vanilla so the
            // user can still cancel the relocation.
            if (key == KeyCode.Escape && !alt && !ctrl)
                return false;
            return true; // swallow other keys
        }

        #endregion

        #region Navigation

        private static void SwitchSection(int delta)
        {
            if (sections.Count <= 1) return;
            // Always wrap around — tabs are a fixed small set; staying put on Left at
            // index 0 would be confusing ("did I press it?"). MenuHelper.SelectNext/Previous
            // honors the user's WrapNavigation setting which is for long lists, not tabs.
            int next = (currentSectionIdx + delta + sections.Count) % sections.Count;
            if (next == currentSectionIdx) return;
            currentSectionIdx = next;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            AnnounceCurrent(includeSectionHeader: true);
        }

        private static void Move(int delta)
        {
            int n = CurrentEntries.Count;
            if (n == 0) return;

            TypeaheadSearchHelper ta = CurrentTypeahead;
            if (ta.HasActiveSearch && ta.MatchCount > 0)
            {
                // While a search filters the list, arrows step through matches only — so
                // typing "mi" then arrowing visits just the matching pawns, not the whole list.
                int mi = delta > 0 ? ta.GetNextMatch(CurrentSelectedIndex) : ta.GetPreviousMatch(CurrentSelectedIndex);
                if (mi >= 0) CurrentSelectedIndex = mi;
            }
            else
            {
                CurrentSelectedIndex = delta > 0
                    ? MenuHelper.SelectNext(CurrentSelectedIndex, n)
                    : MenuHelper.SelectPrevious(CurrentSelectedIndex, n);
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent(includeSectionHeader: false);
        }

        private static void ToggleCurrent()
        {
            if (CurrentEntries.Count == 0) return;
            Thing t = CurrentEntries[CurrentSelectedIndex];
            var selected = (HashSet<Thing>)SelectedField.GetValue(dialog);
            bool wasSelected = selected.Contains(t);
            if (wasSelected)
            {
                selected.Remove(t);
                if (CurrentSection == Section.Items)
                    SetSelectedItemCount(GetSelectedItemCount() - 1);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                selected.Add(t);
                if (CurrentSection == Section.Items)
                    SetSelectedItemCount(GetSelectedItemCount() + 1);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            // Clear any active typeahead on select so the next keystrokes start a fresh search
            // (parity with other selection screens).
            CurrentTypeahead.ClearSearch();
            AnnounceToggle(!wasSelected);
        }

        private static void AttemptAccept()
        {
            var report = (AcceptanceReport)AcceptanceReportProp.GetValue(dialog);
            if (!report.Accepted)
            {
                TolkHelper.SpeakData(report.Reason, SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            int slaveCount = (int)SlaveCountProp.GetValue(dialog);
            int colonistCount = (int)ColonistCountProp.GetValue(dialog);
            bool onlySlavesSelected = slaveCount > 0 && slaveCount == colonistCount;
            var selectedList = ((HashSet<Thing>)SelectedField.GetValue(dialog)).ToList();
            // ConfirmArchonexusSettlementConsequences opens a Dialog_MessageBox the
            // mod's MessageBoxAccessibilityPatch already announces; on confirm it
            // closes this dialog and fires postAccepted with the selected list.
            ConfirmConsequencesMethod.Invoke(dialog, new object[] { selectedList, onlySlavesSelected });
        }

        private static void OpenInfoCard()
        {
            if (CurrentEntries.Count == 0) return;
            Thing t = CurrentEntries[CurrentSelectedIndex];
            Find.WindowStack.Add(new Dialog_InfoCard(t));
        }

        /// <summary>The focused entry as a Pawn (People/Animals tabs), or null on Relics/Items.</summary>
        private static Pawn GetSelectedPawn()
        {
            if (sections.Count == 0 || CurrentEntries.Count == 0) return null;
            return CurrentEntries[CurrentSelectedIndex] as Pawn;
        }

        #endregion

        #region Reflection accessors

        private static int GetMax(Section s)
        {
            switch (s)
            {
                case Section.Colonists: return (int)MaxColonistsField.GetValue(dialog);
                case Section.Animals: return (int)MaxAnimalsField.GetValue(dialog);
                case Section.Relics: return (int)MaxRelicsField.GetValue(dialog);
                case Section.Items: return (int)MaxItemsField.GetValue(dialog);
                default: return 0;
            }
        }

        private static int GetCount(Section s)
        {
            switch (s)
            {
                case Section.Colonists: return (int)ColonistCountProp.GetValue(dialog);
                case Section.Animals: return (int)AnimalCountProp.GetValue(dialog);
                case Section.Relics: return (int)RelicCountProp.GetValue(dialog);
                case Section.Items: return GetSelectedItemCount();
                default: return 0;
            }
        }

        private static int GetSelectedItemCount() => (int)SelectedItemCountField.GetValue(dialog);
        private static void SetSelectedItemCount(int v) => SelectedItemCountField.SetValue(dialog, v);

        private static int GetItemStackCount(Thing t)
        {
            if (MoveColonyUtility.IsDistinctArchonexusItem(t.def))
                return t.stackCount;
            var map = (Dictionary<Thing, int>)ItemAllowedStackCountField.GetValue(dialog);
            return map.TryGetValue(t, out int n) ? n : t.stackCount;
        }

        private static bool IsSelected(Thing t) =>
            ((HashSet<Thing>)SelectedField.GetValue(dialog)).Contains(t);

        #endregion

        #region Labels and tooltips

        private static List<string> LabelsFor(Section s) =>
            entries[s].Select(t => LabelFor(t, s)).ToList();

        private static string LabelFor(Thing t, Section s)
        {
            if (t is Pawn p && p.RaceProps?.Animal == true)
                return $"{p.LabelCap} ({p.GetGenderLabel()}, {Mathf.FloorToInt(p.ageTracker.AgeBiologicalYearsFloat)})";
            if (s == Section.Items)
                return GenLabel.ThingLabel(t, 1, includeHp: false).CapitalizeFirst();
            return t.LabelCap;
        }

        private static string SectionLabel(Section s)
        {
            // Short tab-style label used in section-switch announcements.
            switch (s)
            {
                case Section.Colonists: return "People".Translate().ToString();
                case Section.Animals: return "AnimalsLower".Translate().ToString().CapitalizeFirst();
                case Section.Relics: return GetMax(s) == 1 ? "RelicLower".Translate().ToString().CapitalizeFirst() : "RelicsLower".Translate().ToString().CapitalizeFirst();
                case Section.Items: return "ItemsLower".Translate().ToString().CapitalizeFirst();
                default: return "";
            }
        }

        private static string SectionHeaderFull(Section s)
        {
            int max = GetMax(s);
            switch (s)
            {
                case Section.Colonists: return "ChoosePeopleDesc".Translate(max).ToString();
                case Section.Animals: return "ChooseThingsDesc".Translate(max, "AnimalsLower".Translate()).ToString();
                case Section.Relics: return "ChooseThingsDesc".Translate(max, max == 1 ? "RelicLower".Translate() : "RelicsLower".Translate()).ToString();
                case Section.Items: return "ChooseThingsDesc".Translate(max, "ItemsLower".Translate()).ToString();
                default: return "";
            }
        }

        #endregion

        #region Announcements

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append("ChooseThingsForNewColonyTitle".Translate());
            sb.Append(". ").Append("ChooseThingsForNewColonyDesc".Translate());
            sb.Append(". ").Append("RimWorldAccess.Archonexus.Colony.OpenInstructions".Translate());
            sb.Append(" ").Append(BuildStatusText());
            if (sections.Count > 0)
                sb.Append(". ").Append(BuildCurrentText(includeSectionHeader: true));
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
        }

        private static void AnnounceCurrent(bool includeSectionHeader)
        {
            if (sections.Count == 0) return;
            string text = BuildCurrentText(includeSectionHeader);
            if (!string.IsNullOrEmpty(text))
                TolkHelper.SpeakData(text);
        }

        private static string BuildCurrentText(bool includeSectionHeader)
        {
            if (sections.Count == 0) return "";
            Section s = CurrentSection;
            var sb = new StringBuilder();
            if (includeSectionHeader)
            {
                // Tab style: "People tab. 3 of 5."
                sb.Append(SectionLabel(s)).Append(" tab. ");
                sb.Append(GetCount(s)).Append(" of ").Append(GetMax(s)).Append(". ");
            }
            if (CurrentEntries.Count == 0)
            {
                sb.Append("NoneLower".Translate());
                return sb.ToString();
            }
            Thing t = CurrentEntries[CurrentSelectedIndex];
            sb.Append(LabelFor(t, s));
            sb.Append(". ").Append(IsSelected(t) ? "selected" : "unselected");
            if (s == Section.Items)
                sb.Append(". ").Append(GetItemStackCount(t));
            string position = MenuHelper.FormatPosition(CurrentSelectedIndex, CurrentEntries.Count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);
            return sb.ToString();
        }

        private static void AnnounceToggle(bool nowSelected)
        {
            // Per the toggle-announcements convention: announce only the changed
            // value plus the running total, not the full label (already known).
            var sb = new StringBuilder();
            sb.Append(nowSelected ? "selected" : "unselected");
            sb.Append(". ").Append(GetCount(CurrentSection)).Append(" of ").Append(GetMax(CurrentSection));
            TolkHelper.SpeakData(sb.ToString());
        }

        private static void AnnounceStatus()
        {
            TolkHelper.SpeakData(BuildStatusText(), SpeechPriority.High);
        }

        private static string BuildStatusText()
        {
            // Status always reports all four categories (the dialog tracks them
            // even when a section has zero entries). Order matches vanilla.
            var sb = new StringBuilder();
            int cMax = (int)MaxColonistsField.GetValue(dialog);
            int aMax = (int)MaxAnimalsField.GetValue(dialog);
            int rMax = (int)MaxRelicsField.GetValue(dialog);
            int iMax = (int)MaxItemsField.GetValue(dialog);
            sb.Append(GetCount(Section.Colonists)).Append(" of ").Append(cMax).Append(" ").Append("People".Translate().ToString().ToLower());
            sb.Append(", ").Append(GetCount(Section.Animals)).Append(" of ").Append(aMax).Append(" ").Append("AnimalsLower".Translate());
            sb.Append(", ").Append(GetCount(Section.Relics)).Append(" of ").Append(rMax).Append(" ").Append(rMax == 1 ? "RelicLower".Translate() : "RelicsLower".Translate());
            sb.Append(", ").Append(GetCount(Section.Items)).Append(" of ").Append(iMax).Append(" ").Append("ItemsLower".Translate());
            var report = (AcceptanceReport)AcceptanceReportProp.GetValue(dialog);
            if (!report.Accepted)
                sb.Append(". ").Append(report.Reason);
            int slaveCount = (int)SlaveCountProp.GetValue(dialog);
            int colonistCount = (int)ColonistCountProp.GetValue(dialog);
            if (report.Accepted && slaveCount > 0 && slaveCount == colonistCount)
                sb.Append(". ").Append("ChooseOnlySlavesInfo".Translate());
            return sb.ToString();
        }

        #endregion
    }
}
