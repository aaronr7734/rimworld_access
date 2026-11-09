using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class CharacterEditorScope
    {
        // ------------------------------------------------------------------
        // Health section: Summary, Conditions, Actions. Summary reuses
        // HealthTabHelper's own capacity/pain/bleeding readers rather than re-deriving them --
        // BlockHealth itself
        // draws the untouched vanilla HealthCardUtility.DrawHealthSummary to the left of its
        // custom listing, which this section's Summary subsection mirrors in tree form).
        // Conditions groups hediffs by body part head-to-toe exactly as BlockHealth's own
        // listing does (CharEditorCompat.VisibleHediffsForListing plus
        // HealthTabHelper.GetHediffListPriority -- the identical grouping/ordering formula).
        // ------------------------------------------------------------------

        private void BuildHealthSection(InspectionTreeItem section, Pawn pawn)
        {
            if (!CharEditorCompat.HealthReady)
            {
                InspectNodeFactory.DetailLine(section, "RimWorldAccess.CharEd.NoPawn".Translate());
                return;
            }
            BuildHealthSummarySubsection(section, pawn);
            BuildHealthConditionsSubsection(section, pawn);
            BuildHealthActionsSubsection(section, pawn);
        }

        /// <summary>Read-only rows: every visible capacity (label, level), then pain, matching the vanilla panel BlockHealth draws unmodified to the left of its own listing.</summary>
        private static void BuildHealthSummarySubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Health.SummarySection".Translate());
            int added = 0;
            foreach (HealthTabHelper.CapacityInfo capacity in HealthTabHelper.GetCapacities(pawn))
            {
                InspectNodeFactory.DetailLine(sub,
                    "RimWorldAccess.CharEd.LabelledValue".Translate(capacity.Label, capacity.LevelLabel));
                added++;
            }
            string pain = HealthTabHelper.GetPainLabel(pawn);
            if (!pain.NullOrEmpty())
            {
                InspectNodeFactory.DetailLine(sub, pain);
                added++;
            }
            if (added == 0)
            {
                InspectNodeFactory.DetailLine(sub, "RimWorldAccess.CharEd.Health.NoCapacities".Translate());
            }
        }

        /// <summary>Hediffs grouped by body part (head to toe), then by the mod's own "x2"-style UIGroupKey within each part; a trailing bleeding-rate row when bleeding, matching BlockHealth's own layout exactly.</summary>
        private void BuildHealthConditionsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Health.ConditionsSection".Translate());
            bool showHidden = CharEditorCompat.ShowHiddenHediffs(window);
            List<Hediff> hediffs = CharEditorCompat.VisibleHediffsForListing(pawn, showHidden);

            var byPart = hediffs
                .GroupBy(h => h.Part)
                .OrderByDescending(g => HealthTabHelper.GetHediffListPriority(g.Key));

            int totalRows = 0;
            foreach (var partGroup in byPart)
            {
                InspectionTreeItem partNode = AddSubsection(sub, partGroup.Key != null
                    ? partGroup.Key.LabelCap.ToString()
                    : "WholeBody".Translate().ToString());
                foreach (var uiGroup in partGroup.GroupBy(h => h.UIGroupKey))
                {
                    List<Hediff> groupList = uiGroup.ToList();
                    string label = groupList[0].LabelCap;
                    if (groupList.Count != 1)
                    {
                        label += " x" + groupList.Count;
                    }
                    AddRow(partNode, HealthRowKind.ConditionEntry, label, groupList);
                    totalRows++;
                }
            }

            if (totalRows == 0)
            {
                InspectNodeFactory.DetailLine(sub, "NoHealthConditions".Translate());
            }

            string bleeding = HealthTabHelper.GetBleedingLabel(pawn);
            if (!bleeding.NullOrEmpty())
            {
                InspectNodeFactory.DetailLine(sub, bleeding);
            }
        }

        /// <summary>Add/Copy/Paste/Random/Show-hidden always present; the lower action row splits on Dead exactly as BlockHealth.DrawLower does (Resurrect only when dead, everything else only when alive).</summary>
        private void BuildHealthActionsSubsection(InspectionTreeItem section, Pawn pawn)
        {
            InspectionTreeItem sub = AddSubsection(section, "RimWorldAccess.CharEd.Actions".Translate());
            AddRow(sub, HealthRowKind.ActionAddCondition, "RimWorldAccess.CharEd.Health.AddCondition".Translate());
            AddRow(sub, HealthRowKind.ActionCopyHealth, "Copy".Translate());
            AddRow(sub, HealthRowKind.ActionPasteHealth, "Paste".Translate());
            if (CharEditorCompat.CreationMode)
            {
                AddRow(sub, HealthRowKind.ActionRandomHealth, "Randomize".Translate());
            }
            AddRow(sub, HealthRowKind.ActionShowHidden, "RimWorldAccess.CharEd.Health.ShowHidden".Translate());

            if (pawn.Dead)
            {
                AddRow(sub, HealthRowKind.ActionResurrect, "RimWorldAccess.CharEd.Health.Resurrect".Translate());
                return;
            }
            AddRow(sub, HealthRowKind.ActionFullHeal, "RimWorldAccess.CharEd.Health.FullHeal".Translate());
            // Same underlying vanilla call as the Actions section's own QuickRestore Alt branch
            // (HealthUtility.HealNonPermanentInjuriesAndRestoreLegs) -- reusing its wording keeps
            // one phrase for one operation across both entry points.
            AddRow(sub, HealthRowKind.ActionInstantFullHeal, "RimWorldAccess.CharEd.Actions.HealInjuriesRestoreLegs".Translate());
            AddRow(sub, HealthRowKind.ActionMedicate, "RimWorldAccess.CharEd.Actions.Medicate".Translate());
            AddRow(sub, HealthRowKind.ActionAnaesthetize, "RimWorldAccess.CharEd.Actions.Anaesthetize".Translate());
            AddRow(sub, HealthRowKind.ActionHurt, "RimWorldAccess.CharEd.Health.Hurt".Translate());
            // Mirrors BlockHealth.AHurt's own gate exactly: its non-Alt branch reads
            // `Event.current.alt || InStartingScreen`, so in world-gen this branch is
            // unreachable even by a sighted mouse click -- parity says offer none there.
            if (!CharEditorCompat.InStartingScreen)
            {
                AddRow(sub, HealthRowKind.ActionDamageUntilDeath, "RimWorldAccess.CharEd.Actions.DamageUntilDeath".Translate());
            }
        }

        /// <summary>
        /// Rebuilds a whole top-level section in place -- Health, Needs and Social all need the
        /// identical shape. A single full rebuild rather than several targeted ones, since a
        /// mutation can change which rows even
        /// exist; cheap, matching RefreshCharacterSectionAfterChildDialog's own "always safe to
        /// run" reasoning). Every expanded subsection at ANY depth is re-expanded by its stable
        /// identity label; the cursor falls back to its old FLATTENED index, clamped -- the same
        /// acceptable approximation RebuildTraitsAfterMutation already accepts for add/remove.
        /// <paramref name="silent"/> true is the OnFocus/child-dialog-return convention (no
        /// utterance, matching RefreshCharacterSectionAfterChildDialog); false speaks
        /// <paramref name="outcomeKey"/> when given, else re-describes wherever the cursor landed.
        /// </summary>
        private void RefreshSectionInPlace(SectionKind kind, Action<InspectionTreeItem, Pawn> builder, bool silent, string outcomeKey = null)
        {
            InspectionTreeItem section = FindTopLevelSection(kind);
            if (section == null || !section.IsExpanded)
            {
                staleSections.Add(kind);
                return;
            }
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn == null)
            {
                return;
            }

            var expandedLabels = new HashSet<string>();
            CollectExpandedLabels(section, expandedLabels);
            InspectionTreeItem current = CurrentTreeItem();
            int cursorIndex = current != null ? IndexOfVisible(Tree.Visible, current) : -1;

            section.Children.Clear();
            builder(section, pawn);
            ApplyExpandedLabels(section, expandedLabels);
            Tree.Reflatten();
            ResetBoundaryTracking();
            RefreshModel();

            // Only when the cursor really was in the tree: a silent refresh triggered from the
            // pawn-selection strip or the toolbar leaves cursorIndex at -1, and clamping that to 0
            // would drag the tree's selection to its first row unseen (desync class 453660a).
            if (cursorIndex >= 0 && Tree.Visible.Count > 0)
            {
                int clamped = Math.Min(cursorIndex, Tree.Visible.Count - 1);
                Tree.SetSelectedIndex(clamped);
                SyncRegionFromCurrentTree();
            }

            if (silent)
            {
                return;
            }
            if (outcomeKey != null)
            {
                TolkHelper.SpeakData(outcomeKey.Translate().ToString());
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        private void RefreshHealthSectionInPlace(bool silent, string outcomeKey = null) =>
            RefreshSectionInPlace(SectionKind.Health, BuildHealthSection, silent, outcomeKey);

        private void RefreshNeedsSectionInPlace(bool silent, string outcomeKey = null) =>
            RefreshSectionInPlace(SectionKind.Needs, BuildNeedsSection, silent, outcomeKey);

        private void RefreshSocialSectionInPlace(bool silent, string outcomeKey = null) =>
            RefreshSectionInPlace(SectionKind.Social, BuildSocialSection, silent, outcomeKey);

        /// <summary>Records every currently-expanded node's stable identity label, at any depth under <paramref name="root"/>.</summary>
        private static void CollectExpandedLabels(InspectionTreeItem root, HashSet<string> into)
        {
            foreach (InspectionTreeItem child in root.Children)
            {
                if (child.IsExpanded)
                {
                    string label = child.ExpandedLabel ?? child.Label;
                    if (!label.NullOrEmpty())
                    {
                        into.Add(label);
                    }
                    CollectExpandedLabels(child, into);
                }
            }
        }

        /// <summary>Re-expands every node under <paramref name="root"/> whose stable identity label was previously recorded.</summary>
        private static void ApplyExpandedLabels(InspectionTreeItem root, HashSet<string> labels)
        {
            foreach (InspectionTreeItem child in root.Children)
            {
                string label = child.ExpandedLabel ?? child.Label;
                if (!label.NullOrEmpty() && labels.Contains(label))
                {
                    child.IsExpanded = true;
                }
                ApplyExpandedLabels(child, labels);
            }
        }

        // ---- Describe/Activate/Adjust dispatch. ----

        private void DescribeHealthRow(RegionRow<HealthRowKind> row, ElementDescription d)
        {
            switch (row.Kind)
            {
                case HealthRowKind.ConditionEntry:
                {
                    Pawn pawn = CharEditorCompat.CurrentPawn;
                    var group = row.Payload as List<Hediff>;
                    Hediff exemplar = !group.NullOrEmpty() ? group[0] : null;
                    string label = exemplar?.LabelCap ?? "";
                    if (group != null && group.Count > 1)
                    {
                        label += " x" + group.Count;
                    }
                    d.Label = label;
                    d.Role = ElementRole.ComboBox;
                    d.Extras = exemplar != null && pawn != null
                        ? RedundantLabelPrefix.AfterTitle(exemplar.GetTooltip(pawn, Prefs.DevMode).StripTags(), exemplar.LabelCap.StripTags())
                        : null;
                    break;
                }
                case HealthRowKind.ActionAddCondition:
                    d.Label = "RimWorldAccess.CharEd.Health.AddCondition".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionCopyHealth:
                    d.Label = "Copy".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionPasteHealth:
                {
                    d.Label = "Paste".Translate();
                    d.Role = ElementRole.Button;
                    bool has = CharEditorCompat.HasHealthClipboard(window);
                    d.Disabled = !has;
                    d.Extras = has ? null : "RimWorldAccess.CharEd.Character.NothingToPaste".Translate().ToString();
                    break;
                }
                case HealthRowKind.ActionRandomHealth:
                    d.Label = "Randomize".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionShowHidden:
                    d.Label = "RimWorldAccess.CharEd.Health.ShowHidden".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorCompat.ShowHiddenHediffs(window) ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case HealthRowKind.ActionFullHeal:
                    d.Label = "RimWorldAccess.CharEd.Health.FullHeal".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionInstantFullHeal:
                    d.Label = "RimWorldAccess.CharEd.Actions.HealInjuriesRestoreLegs".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionMedicate:
                    d.Label = "RimWorldAccess.CharEd.Actions.Medicate".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionAnaesthetize:
                    d.Label = "RimWorldAccess.CharEd.Actions.Anaesthetize".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionHurt:
                    d.Label = "RimWorldAccess.CharEd.Health.Hurt".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionDamageUntilDeath:
                    d.Label = "RimWorldAccess.CharEd.Actions.DamageUntilDeath".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case HealthRowKind.ActionResurrect:
                    d.Label = "RimWorldAccess.CharEd.Health.Resurrect".Translate();
                    d.Role = ElementRole.Button;
                    break;
            }
        }

        private void ActivateHealthRow(RegionRow<HealthRowKind> row, InspectionTreeItem item)
        {
            switch (row.Kind)
            {
                case HealthRowKind.ConditionEntry:
                {
                    var group = row.Payload as List<Hediff>;
                    if (!group.NullOrEmpty())
                    {
                        CharEditorCompat.EditHediff(window, group[0]);
                    }
                    // Dialog opened; OnFocus's silent RefreshHealthSectionInPlace covers the return.
                    break;
                }
                case HealthRowKind.ActionAddCondition:
                    CharEditorCompat.AddHediff(window);
                    break;
                case HealthRowKind.ActionCopyHealth:
                    CharEditorCompat.CopyHealth(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Health.HealthCopied".Translate().ToString());
                    break;
                case HealthRowKind.ActionPasteHealth:
                    if (!CharEditorCompat.HasHealthClipboard(window))
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        AnnounceCurrentItem();
                        break;
                    }
                    CharEditorCompat.PasteHealth(window);
                    RefreshHealthSectionInPlace(silent: false);
                    break;
                case HealthRowKind.ActionRandomHealth:
                    CharEditorCompat.RandomHealth(window);
                    RefreshHealthSectionInPlace(silent: false);
                    break;
                case HealthRowKind.ActionShowHidden:
                {
                    bool newState = !CharEditorCompat.ShowHiddenHediffs(window);
                    CharEditorCompat.ToggleShowHidden(window);
                    RefreshHealthSectionInPlace(silent: true);
                    var stateDesc = new ElementDescription
                    {
                        Label = "RimWorldAccess.CharEd.Health.ShowHidden".Translate(),
                        Role = ElementRole.Checkbox,
                        Check = newState ? CheckState.Checked : CheckState.Unchecked,
                    };
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(stateDesc, TranslatedShellVocabulary.Instance));
                    break;
                }
                case HealthRowKind.ActionFullHeal:
                    CharEditorCompat.OpenFullHeal(window);
                    // Dialog opened; OnFocus's silent RefreshHealthSectionInPlace covers the return.
                    break;
                case HealthRowKind.ActionInstantFullHeal:
                    CharEditorCompat.InstantFullHeal(window);
                    RefreshHealthSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Actions.RestoreApplied");
                    break;
                case HealthRowKind.ActionMedicate:
                    CharEditorCompat.Medicate(window);
                    RefreshHealthSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Health.Medicated");
                    break;
                case HealthRowKind.ActionAnaesthetize:
                    CharEditorCompat.Anaesthetize(window);
                    RefreshHealthSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Health.Anaesthetized");
                    break;
                case HealthRowKind.ActionHurt:
                    CharEditorCompat.HurtPawn(window);
                    RefreshHealthSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Health.HurtApplied");
                    break;
                case HealthRowKind.ActionDamageUntilDeath:
                    CharEditorCompat.DamageUntilDeath(window);
                    RefreshHealthSectionInPlace(silent: false, outcomeKey: "RimWorldAccess.CharEd.Health.DamagedUntilDeath");
                    break;
                case HealthRowKind.ActionResurrect:
                    CharEditorCompat.Resurrect(window);
                    RefreshHealthSectionInPlace(silent: true);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Health.Resurrected".Translate(DescribePawnValue()).ToString());
                    break;
            }
        }

        /// <summary>Only the AddHediff dialog's Severity/Level/Pain/Duration parameter rows are adjustable; every Health tree row here is a button, checkbox, or the condition combo (whose own value is edited through DialogAddHediff, not stepped in place) -- so this always falls through.</summary>
        private bool AdjustHealthRow(RegionRow<HealthRowKind> row, InspectionTreeItem item, int direction)
        {
            return false;
        }
    }
}
