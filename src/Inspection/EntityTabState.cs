using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard navigation for ITab_Entity (Anomaly DLC). Replaces the BasicInspectString
    /// fallback (which only dumped GetInspectString) with a full menu surfacing every
    /// interactive control on the tab: containment mode, extract bioferrite, allow medicine,
    /// plus all read-only stats (containment strength, escape MTB, study interval, knowledge gain).
    ///
    /// Visible whenever a held entity (Pawn on HoldingPlatform) is selected.
    /// </summary>
    public static class EntityTabState
    {
        private enum RowKind
        {
            ContainmentStrength,    // read-only stat
            EscapeMtb,              // read-only stat
            StudyInterval,          // read-only (only if CompStudiable.Props.frequencyTicks > 0)
            KnowledgeGain,          // read-only
            AllowMedicine,          // interactive: cycle MedicalCareCategory
            ContainmentMode,        // interactive: 1-of-4 cycle
            ExtractBioferrite,      // interactive: checkbox
        }

        private class Row
        {
            public RowKind Kind;
            public bool Interactive;
            public bool DisabledForInteraction;
            public string DisabledReason;
        }

        private static bool isActive;
        private static Pawn heldPawn;            // the entity itself
        private static Thing platformThing;      // the Building_HoldingPlatform (or other holder)
        private static List<Row> rows = new List<Row>();
        private static int selectedIndex;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        // ===== ENTRY =====

        /// <summary>
        /// Opens the entity menu for the given target. The target may be either a held Pawn
        /// or a Building_HoldingPlatform (or anything that has a HeldPawn via reflection).
        /// </summary>
        public static void Open(Thing target)
        {
            if (target == null) return;

            heldPawn = ResolveHeldPawn(target);
            if (heldPawn == null)
            {
                Log.Warning("[EntityTabState] No held pawn found for target.");
                return;
            }

            // Cache the platform Thing for CompEntityHolder lookups (containment strength etc.)
            platformThing = heldPawn.ParentHolder as Thing ?? target;

            BuildRows();
            selectedIndex = 0;
            typeahead.ClearSearch();
            isActive = true;

            TolkHelper.Speak($"{"TabEntity".Translate()}. {heldPawn.LabelShortCap}");
            AnnounceCurrent();
        }

        public static void Close()
        {
            isActive = false;
            heldPawn = null;
            platformThing = null;
            rows.Clear();
            typeahead.ClearSearch();
        }

        private static Pawn ResolveHeldPawn(Thing target)
        {
            // Building_HoldingPlatform exposes HeldPawn directly.
            // Use reflection so we can compile without a hard reference to Anomaly types
            // (the file still compiles when Anomaly DLC isn't installed; method just no-ops).
            if (target is Pawn p && p.IsOnHoldingPlatform) return p;
            var heldPawnProp = AccessTools.Property(target.GetType(), "HeldPawn");
            return heldPawnProp?.GetValue(target) as Pawn;
        }

        // ===== ROW BUILDER =====

        private static void BuildRows()
        {
            rows.Clear();
            var compStudiable = heldPawn.TryGetComp<CompStudiable>();
            var compHpTarget = heldPawn.TryGetComp<CompHoldingPlatformTarget>();

            rows.Add(new Row { Kind = RowKind.ContainmentStrength });
            rows.Add(new Row { Kind = RowKind.EscapeMtb });

            if (compStudiable?.Props != null && compStudiable.Props.frequencyTicks > 0)
                rows.Add(new Row { Kind = RowKind.StudyInterval });
            if (compStudiable != null)
                rows.Add(new Row { Kind = RowKind.KnowledgeGain });

            // Allow Medicine — every held pawn has playerSettings.
            rows.Add(new Row { Kind = RowKind.AllowMedicine, Interactive = true });

            if (compHpTarget != null)
            {
                rows.Add(new Row { Kind = RowKind.ContainmentMode, Interactive = true });

                // Bioferrite extraction has two disable conditions (mirrors ITab_Entity.FillTab).
                string disableReason = null;
                if (!ResearchProjectDefOf.BioferriteExtraction.IsFinished)
                    disableReason = "RequiresBioferriteExtraction".Translate();
                else
                {
                    var heldPlatform = compHpTarget.HeldPlatform;
                    if (heldPlatform != null && heldPlatform.HasAttachedBioferriteHarvester)
                        disableReason = "BioferriteHarvesterAttached".Translate();
                }

                rows.Add(new Row
                {
                    Kind = RowKind.ExtractBioferrite,
                    Interactive = true,
                    DisabledForInteraction = disableReason != null,
                    DisabledReason = disableReason,
                });
            }
        }

        // ===== KEYBOARD =====

        public static bool HandleInput(Event evt)
        {
            if (!isActive) return false;
            if (evt.type != EventType.KeyDown) return false;

            var key = evt.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;

            // Alt+I → info card on the held entity (matches universal Alt+I drill-in convention).
            if (alt && key == KeyCode.I)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(heldPawn));
                return true;
            }
            // Alt+H/M/N/G → standard pawn deep-dives, mirrors ritual pawn picker behavior.
            if (alt && key == KeyCode.H) { TolkHelper.Speak(PawnInfoHelper.GetHealthInfo(heldPawn)); return true; }
            if (alt && key == KeyCode.M) { TolkHelper.Speak(PawnInfoHelper.GetMoodInfo(heldPawn)); return true; }
            if (alt && key == KeyCode.N) { TolkHelper.Speak(PawnInfoHelper.GetNeedsInfo(heldPawn)); return true; }
            if (alt && key == KeyCode.G) { TolkHelper.Speak(PawnInfoHelper.GetGearInfo(heldPawn)); return true; }

            switch (key)
            {
                case KeyCode.Escape:
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        AnnounceCurrent();
                        return true;
                    }
                    Close();
                    TolkHelper.Speak($"{"TabEntity".Translate()} closed.");
                    return true;

                case KeyCode.UpArrow:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                        selectedIndex = typeahead.GetPreviousMatch(selectedIndex);
                    else
                        selectedIndex = MenuHelper.SelectPrevious(selectedIndex, rows.Count);
                    AnnounceCurrent();
                    return true;

                case KeyCode.DownArrow:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                        selectedIndex = typeahead.GetNextMatch(selectedIndex);
                    else
                        selectedIndex = MenuHelper.SelectNext(selectedIndex, rows.Count);
                    AnnounceCurrent();
                    return true;

                case KeyCode.Home:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                        selectedIndex = typeahead.GetFirstMatch();
                    else
                        selectedIndex = 0;
                    AnnounceCurrent();
                    return true;

                case KeyCode.End:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                        selectedIndex = typeahead.GetLastMatch();
                    else
                        selectedIndex = rows.Count - 1;
                    AnnounceCurrent();
                    return true;

                case KeyCode.LeftArrow:
                    AdjustCurrent(-1);
                    return true;

                case KeyCode.RightArrow:
                    AdjustCurrent(1);
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    ToggleCurrent();
                    return true;

                default:
                    return true; // modal: consume all unhandled keys; typeahead routed via TypeaheadDispatcher.
            }
        }

        /// <summary>
        /// Layout-aware typeahead character entry; called by <see cref="TypeaheadDispatcher"/>.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;

            var labels = rows.Select(r => GetRowLabel(r)).ToList();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIdx) && newIdx >= 0)
            {
                selectedIndex = newIdx;
                AnnounceCurrent();
            }
            else
            {
                TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
            }
        }

        // ===== INTERACTIONS =====

        private static void ToggleCurrent()
        {
            if (rows.Count == 0 || selectedIndex < 0 || selectedIndex >= rows.Count) return;
            var row = rows[selectedIndex];
            if (!row.Interactive)
            {
                AnnounceCurrent();
                return;
            }
            switch (row.Kind)
            {
                case RowKind.AllowMedicine:
                    CycleMedicalCare(1);
                    break;
                case RowKind.ContainmentMode:
                    CycleContainmentMode(1);
                    break;
                case RowKind.ExtractBioferrite:
                    ToggleExtractBioferrite();
                    break;
            }
        }

        private static void AdjustCurrent(int direction)
        {
            if (rows.Count == 0 || selectedIndex < 0 || selectedIndex >= rows.Count) return;
            var row = rows[selectedIndex];
            if (!row.Interactive) return;
            switch (row.Kind)
            {
                case RowKind.AllowMedicine:
                    CycleMedicalCare(direction);
                    break;
                case RowKind.ContainmentMode:
                    CycleContainmentMode(direction);
                    break;
                case RowKind.ExtractBioferrite:
                    ToggleExtractBioferrite();
                    break;
            }
        }

        private static void CycleMedicalCare(int direction)
        {
            if (heldPawn?.playerSettings == null) return;
            var values = (MedicalCareCategory[])Enum.GetValues(typeof(MedicalCareCategory));
            int current = Array.IndexOf(values, heldPawn.playerSettings.medCare);
            if (current < 0) current = 0;
            int next = (current + direction + values.Length) % values.Length;
            heldPawn.playerSettings.medCare = values[next];
            TolkHelper.Speak($"{"AllowMedicine".Translate()}: {heldPawn.playerSettings.medCare.GetLabel()}");
        }

        private static void CycleContainmentMode(int direction)
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (comp == null) return;
            var values = (EntityContainmentMode[])Enum.GetValues(typeof(EntityContainmentMode));
            int current = Array.IndexOf(values, comp.containmentMode);
            if (current < 0) current = 0;

            // Skip "Execute" mode if this entity can't be executed (matches vanilla disable).
            int attempts = 0;
            int next = current;
            do
            {
                next = (next + direction + values.Length) % values.Length;
                attempts++;
            }
            while (values[next] == EntityContainmentMode.Execute && !comp.Props.canBeExecuted && attempts < values.Length);

            if (values[next] == EntityContainmentMode.Execute && !comp.Props.canBeExecuted)
            {
                TolkHelper.Speak("CantBeExecuted".Translate().Resolve());
                return;
            }

            comp.containmentMode = values[next];
            TolkHelper.Speak($"{("EntityStudyMode_" + comp.containmentMode).Translate()}");
        }

        private static void ToggleExtractBioferrite()
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (comp == null) return;
            var row = rows[selectedIndex];
            if (row.DisabledForInteraction)
            {
                TolkHelper.Speak(row.DisabledReason);
                return;
            }
            comp.extractBioferrite = !comp.extractBioferrite;
            string status = comp.extractBioferrite ? "On" : "Off";
            TolkHelper.Speak($"{"EntityStudyMode_Extract".Translate()}: {status}");
        }

        // ===== ANNOUNCEMENTS =====

        private static void AnnounceCurrent()
        {
            if (rows.Count == 0 || selectedIndex < 0 || selectedIndex >= rows.Count) return;
            var row = rows[selectedIndex];
            string text = BuildRowAnnouncement(row);
            string position = MenuHelper.FormatPosition(selectedIndex, rows.Count);
            string suffix = string.IsNullOrEmpty(position) ? "" : $" ({position})";
            TolkHelper.Speak($"{text}{suffix}");
        }

        private static string GetRowLabel(Row row)
        {
            switch (row.Kind)
            {
                case RowKind.ContainmentStrength: return StatDefOf.ContainmentStrength.LabelCap;
                case RowKind.EscapeMtb: return "HoldingPlatformEscapeMTBDays".Translate();
                case RowKind.StudyInterval: return "StudyInterval".Translate();
                case RowKind.KnowledgeGain: return "StudyKnowledgeGain".Translate();
                case RowKind.AllowMedicine: return "AllowMedicine".Translate();
                case RowKind.ContainmentMode: return "EntityStudyMode_Study".Translate();
                case RowKind.ExtractBioferrite: return "EntityStudyMode_Extract".Translate();
                default: return "";
            }
        }

        private static string BuildRowAnnouncement(Row row)
        {
            switch (row.Kind)
            {
                case RowKind.ContainmentStrength: return BuildContainmentStrengthAnnouncement();
                case RowKind.EscapeMtb: return BuildEscapeMtbAnnouncement();
                case RowKind.StudyInterval: return BuildStudyIntervalAnnouncement();
                case RowKind.KnowledgeGain: return BuildKnowledgeGainAnnouncement();
                case RowKind.AllowMedicine: return BuildAllowMedicineAnnouncement();
                case RowKind.ContainmentMode: return BuildContainmentModeAnnouncement();
                case RowKind.ExtractBioferrite: return BuildExtractBioferriteAnnouncement();
                default: return "";
            }
        }

        private static string BuildContainmentStrengthAnnouncement()
        {
            var stat = StatDefOf.ContainmentStrength;
            var comp = platformThing?.TryGetComp<CompEntityHolder>();
            float value = comp?.ContainmentStrength ?? 0f;
            // Vanilla tooltip = stat.description + GetExplanationFull(...) (the full breakdown
            // of what's contributing to the score). Surface both so blind users get the same
            // diagnostic detail mouseover users see.
            string description = stat.description ?? "";
            string explanation = "";
            if (platformThing != null)
            {
                try
                {
                    explanation = stat.Worker.GetExplanationFull(
                        StatRequest.For(platformThing), stat.toStringNumberSense, value);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EntityTabState] ContainmentStrength explanation failed: {ex.Message}");
                }
            }
            return CombineLabelTooltip(
                $"{stat.LabelCap}: {stat.ValueToString(value)}",
                description,
                explanation);
        }

        private static string BuildEscapeMtbAnnouncement()
        {
            var sb = new StringBuilder();
            float days = ContainmentUtility.InitiateEscapeMtbDays(heldPawn, sb);
            string label = "HoldingPlatformEscapeMTBDays".Translate();
            string headline = days < 0f
                ? $"{label}: {"Never".Translate()}"
                : $"{label}: {Mathf.FloorToInt(days * 60000f).ToStringTicksToPeriod()}";
            // Vanilla tooltip = "HoldingPlatformEscapeMTBDaysDesc" + (sb if any reasons).
            string description = "HoldingPlatformEscapeMTBDaysDesc".Translate();
            string reasons = sb.Length > 0 ? sb.ToString() : "";
            return CombineLabelTooltip(headline, description, reasons);
        }

        private static string BuildStudyIntervalAnnouncement()
        {
            var comp = heldPawn.TryGetComp<CompStudiable>();
            int ticks = comp?.Props?.frequencyTicks ?? 0;
            // Vanilla tooltip = "StudyIntervalDesc".
            return CombineLabelTooltip(
                $"{"StudyInterval".Translate()}: {ticks.ToStringTicksToPeriod()}",
                "StudyIntervalDesc".Translate(),
                null);
        }

        private static string BuildKnowledgeGainAnnouncement()
        {
            var comp = heldPawn.TryGetComp<CompStudiable>();
            if (comp == null) return "";
            string label = "StudyKnowledgeGain".Translate();
            string amount = (comp.AdjustedAnomalyKnowledgePerStudy * 5f).ToStringDecimalIfSmall();
            string category = comp.KnowledgeCategory?.label ?? "";
            string headline = $"{label}: {amount} ({category})";

            // Vanilla tooltip = "StudyKnowledgeGainDesc" + factor breakdown
            // (FactorContainmentStrength, FactorElectroharvester if attached, FactorActivity).
            string description = "StudyKnowledgeGainDesc".Translate();
            var factors = new StringBuilder();
            var compHpTarget = comp.Pawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (compHpTarget != null && compHpTarget.CurrentlyHeldOnPlatform)
            {
                var holderComp = compHpTarget.HeldPlatform?.GetComp<CompEntityHolder>();
                float strengthMult = ContainmentUtility.GetStudyKnowledgeAmountMultiplier(comp.Pawn, holderComp);
                factors.Append($"{"FactorContainmentStrength".Translate()}: x{strengthMult:F1}");
                if (compHpTarget.HeldPlatform != null && compHpTarget.HeldPlatform.HasAttachedElectroharvester)
                {
                    factors.Append($". {"FactorElectroharvester".Translate()}: x{0.5f:F1}");
                }
            }
            if (comp.Pawn != null && comp.Pawn.TryGetComp<CompActivity>(out var activityComp))
            {
                if (factors.Length > 0) factors.Append(". ");
                factors.Append($"{"FactorActivity".Translate()}: x{activityComp.ActivityResearchFactor:F1}");
            }
            return CombineLabelTooltip(headline, description, factors.ToString());
        }

        private static string BuildAllowMedicineAnnouncement()
        {
            string label = "AllowMedicine".Translate();
            string value = heldPawn?.playerSettings?.medCare.GetLabel() ?? "";
            // Vanilla tooltip = "MedicineQualityDescriptionEntity" (TipRegionByKey on the row).
            return CombineLabelTooltip(
                $"{label}: {value}",
                "MedicineQualityDescriptionEntity".Translate(),
                null);
        }

        /// <summary>
        /// Joins a row headline with its tooltip(s) into one announcement, stripping
        /// embedded newlines (vanilla tooltips use \n\n as paragraph separators which
        /// make screen-reader output sound like run-on sentences).
        /// </summary>
        private static string CombineLabelTooltip(string headline, string tooltip, string detail)
        {
            var sb = new StringBuilder(headline);
            if (!string.IsNullOrEmpty(tooltip)) sb.Append(". ").Append(SanitizeTooltip(tooltip));
            if (!string.IsNullOrEmpty(detail)) sb.Append(". ").Append(SanitizeTooltip(detail));
            return sb.ToString();
        }

        private static string SanitizeTooltip(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\n\n", ". ").Replace("\n", " ").Trim();
        }

        private static string BuildContainmentModeAnnouncement()
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (comp == null) return "";
            string modeKey = "EntityStudyMode_" + comp.containmentMode;
            string descKey = modeKey + "Desc";
            string suffix = "";
            if (!comp.Props.canBeExecuted)
                suffix = $". {"CantBeExecuted".Translate()}";
            return $"{modeKey.Translate()}. {descKey.Translate()}{suffix}";
        }

        private static string BuildExtractBioferriteAnnouncement()
        {
            var comp = heldPawn?.TryGetComp<CompHoldingPlatformTarget>();
            if (comp == null) return "";
            string label = "EntityStudyMode_Extract".Translate();
            string status = comp.extractBioferrite ? "On" : "Off";
            string description = "EntityStudyMode_ExtractDesc".Translate();
            string row = rows[selectedIndex].DisabledReason;
            string disabledNote = string.IsNullOrEmpty(row) ? "" : $". {row}";
            return $"{label}: {status}. {description}{disabledNote}";
        }
    }
}
