using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation state for baby gene inspection (ITab_GenesPregnancy).
    /// Provides tree-based navigation through genes and their details.
    /// Uses TreeNavigationHelper for standard treeview keyboard navigation.
    /// </summary>
    public static class GeneInspectionState
    {
        public static bool IsActive { get; private set; } = false;

        private static Pawn currentPawn = null;
        private static HediffWithParents currentPregnancy = null;
        private static GeneSetHolderBase currentHolder = null;

        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("GeneInspection");
        public static TypeaheadSearchHelper Typeahead => treeNav.Typeahead;

        static GeneInspectionState()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnBeforeExpand = item =>
            {
                if (item.OnActivate != null && item.Children.Count == 0)
                    item.OnActivate();
            };
        }

        /// <summary>
        /// Opens the gene inspection accessibility state for a pregnant pawn.
        /// </summary>
        /// <param name="pawn">The pregnant pawn to inspect</param>
        public static void Open(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                    return;

                // Find the pregnancy hediff
                var pregnancy = pawn.health?.hediffSet?.hediffs
                    .OfType<HediffWithParents>()
                    .FirstOrDefault();

                if (pregnancy == null || pregnancy.geneSet == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.NoPregnancy".Translate(), SpeechPriority.High);
                    return;
                }

                currentPawn = pawn;
                currentPregnancy = pregnancy;
                IsActive = true;

                // Get parent names for context
                string motherName = pregnancy.Mother?.LabelShort;
                string fatherName = pregnancy.Father?.LabelShort;

                // Build the tree
                var rootItem = GeneTreeBuilder.BuildTree(pregnancy.geneSet, motherName, fatherName);
                treeNav.Initialize(rootItem);

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening();
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error opening: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// Opens the gene inspection accessibility state for a GeneSetHolderBase item
        /// (embryo, genepack, or xenogerm).
        /// </summary>
        public static void OpenForGeneSetHolder(GeneSetHolderBase holder)
        {
            try
            {
                if (holder == null || holder.GeneSet == null)
                    return;

                currentPawn = null;
                currentPregnancy = null;
                currentHolder = holder;
                IsActive = true;

                // Get parent names for embryos
                string motherName = null;
                string fatherName = null;
                if (holder is HumanEmbryo embryo)
                {
                    try
                    {
                        motherName = embryo.Mother?.LabelShort;
                        fatherName = embryo.Father?.LabelShort;
                    }
                    catch { /* CompHasPawnSources may not be available */ }
                }

                // Build the tree
                var rootItem = GeneTreeBuilder.BuildTree(holder.GeneSet, motherName, fatherName);

                // Override root label based on item type
                int geneCount = holder.GeneSet.GenesListForReading?.Count ?? 0;
                string countStr = GeneTreeBuilder.GeneCountSuffix(geneCount);
                string xenotype = holder.GeneSet.Label;
                bool hasXenotype = !string.IsNullOrEmpty(xenotype) && xenotype != "ERR";

                if (holder is HumanEmbryo)
                    rootItem.Label = hasXenotype
                        ? "RimWorldAccess.Biotech.Gene.RootEmbryoGenesWithXenotype".Translate(xenotype, countStr).ToString()
                        : "RimWorldAccess.Biotech.Gene.RootEmbryoGenes".Translate(countStr).ToString();
                else if (holder is Xenogerm xg && !string.IsNullOrEmpty(xg.xenotypeName))
                    rootItem.Label = "RimWorldAccess.Biotech.Gene.RootXenogermGenes".Translate(xg.xenotypeName, countStr).ToString();
                else if (holder is Genepack)
                    rootItem.Label = hasXenotype
                        ? "RimWorldAccess.Biotech.Gene.RootGenepackWithXenotype".Translate(xenotype, countStr).ToString()
                        : "RimWorldAccess.Biotech.Gene.RootGenepackGenes".Translate(countStr).ToString();
                else
                    rootItem.Label = hasXenotype
                        ? "RimWorldAccess.Biotech.Gene.RootGenesWithXenotype".Translate(xenotype, countStr).ToString()
                        : "RimWorldAccess.Biotech.Gene.RootGenes".Translate(countStr).ToString();

                treeNav.Initialize(rootItem);

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening();
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error opening for GeneSetHolder: {ex.Message}");
                Close();
            }
        }

        /// <summary>
        /// Closes the gene inspection accessibility state.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentPawn = null;
            currentPregnancy = null;
            currentHolder = null;
            treeNav.Reset();
        }

        /// <summary>
        /// Jumps to the next gene header (Page Down).
        /// </summary>
        public static void JumpToNextGene()
        {
            if (!IsActive || treeNav.Count == 0)
                return;

            treeNav.Typeahead.ClearSearch();
            var visibleItems = treeNav.VisibleItems;
            int selectedIndex = treeNav.SelectedIndex;

            // Search forward from current position for next gene (Item type with GeneDef data)
            for (int i = selectedIndex + 1; i < visibleItems.Count; i++)
            {
                var item = visibleItems[i];
                if (item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef)
                {
                    treeNav.SetSelectedIndex(i);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    treeNav.ReannounceCurrentItem();
                    return;
                }
                // Also stop at SubCategory (like "Biostats Summary")
                if (item.Type == InspectionTreeItem.ItemType.SubCategory)
                {
                    treeNav.SetSelectedIndex(i);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    treeNav.ReannounceCurrentItem();
                    return;
                }
            }

            // Wrap to beginning (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                for (int i = 0; i <= selectedIndex; i++)
                {
                    var item = visibleItems[i];
                    if ((item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef) ||
                        item.Type == InspectionTreeItem.ItemType.SubCategory)
                    {
                        treeNav.SetSelectedIndex(i);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        treeNav.ReannounceCurrentItem();
                        return;
                    }
                }
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Jumps to the previous gene header (Page Up).
        /// </summary>
        public static void JumpToPreviousGene()
        {
            if (!IsActive || treeNav.Count == 0)
                return;

            treeNav.Typeahead.ClearSearch();
            var visibleItems = treeNav.VisibleItems;
            int selectedIndex = treeNav.SelectedIndex;

            // Search backward from current position
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                var item = visibleItems[i];
                if ((item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef) ||
                    item.Type == InspectionTreeItem.ItemType.SubCategory)
                {
                    treeNav.SetSelectedIndex(i);
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    treeNav.ReannounceCurrentItem();
                    return;
                }
            }

            // Wrap to end (if enabled)
            if (RimWorldAccessMod_Settings.Settings?.WrapNavigation == true)
            {
                for (int i = visibleItems.Count - 1; i >= selectedIndex; i--)
                {
                    var item = visibleItems[i];
                    if ((item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef) ||
                        item.Type == InspectionTreeItem.ItemType.SubCategory)
                    {
                        treeNav.SetSelectedIndex(i);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        treeNav.ReannounceCurrentItem();
                        return;
                    }
                }
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Closes the gene inspection (Escape key).
        /// </summary>
        public static void CloseInspection()
        {
            if (!IsActive)
                return;

            // Also close the visual tab if open
            if (currentPawn != null)
            {
                var pane = Find.MainTabsRoot?.OpenTab?.TabWindow as MainTabWindow_Inspect;
                if (pane != null)
                {
                    // Close the genes tab
                    pane.CloseOpenTab();
                }
            }

            Close();
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.Closed".Translate());
        }

        /// <summary>
        /// Handles keyboard input for gene inspection.
        /// Returns true if input was handled.
        /// Called from UnifiedKeyboardPatch which handles Event.current.Use().
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Escape - always close (don't delegate to treeNav which only clears search)
            if (key == KeyCode.Escape)
            {
                if (treeNav.HasActiveSearch)
                {
                    treeNav.Typeahead.ClearSearchAndAnnounce();
                    treeNav.ReannounceCurrentItem();
                    return true;
                }
                CloseInspection();
                return true;
            }

            // Page Down - jump to next gene (custom behavior, not in TreeNavigationHelper)
            if (key == KeyCode.PageDown)
            {
                JumpToNextGene();
                return true;
            }

            // Page Up - jump to previous gene (custom behavior, not in TreeNavigationHelper)
            if (key == KeyCode.PageUp)
            {
                JumpToPreviousGene();
                return true;
            }

            // Left arrow - intercept to handle GeneDef label restoration on collapse
            if (key == KeyCode.LeftArrow)
            {
                HandleLeftArrow();
                return true;
            }

            // Delegate all other input to TreeNavigationHelper
            return treeNav.HandleInput(ev);
        }

        #region Private Methods

        /// <summary>
        /// Handles left arrow with GeneDef label restoration on collapse.
        /// When collapsing a GeneDef node, restores the rich label from Description.
        /// </summary>
        private static void HandleLeftArrow()
        {
            var item = treeNav.SelectedItem;
            if (item == null)
                return;

            // If this is an expanded GeneDef node, restore the rich label before collapsing
            if (item.IsExpandable && item.IsExpanded && item.Data is GeneDef && !string.IsNullOrEmpty(item.Description))
            {
                item.Label = item.Description;
            }

            // Delegate to TreeNavigationHelper for the actual collapse/drill-up
            treeNav.CollapseOrDrillUp();
        }

        /// <summary>
        /// Announces the opening of the gene inspection.
        /// </summary>
        private static void AnnounceOpening()
        {
            if (treeNav.RootItem == null)
                return;

            string rootLabel = treeNav.RootItem.Label.StripTags();

            // Build opening announcement with first item
            var sb = new System.Text.StringBuilder();
            sb.Append(rootLabel);
            sb.Append(". ");

            // Announce the first item
            if (treeNav.Count > 0)
            {
                var firstItem = treeNav.VisibleItems[0];
                string firstLabel = firstItem.Label.StripTags();
                string state = TreeNavigationHelper.GetExpansionStateWord(firstItem);
                sb.Append("RimWorldAccess.Biotech.GeneInspection.FirstGene".Translate(firstLabel));
                if (!string.IsNullOrEmpty(state))
                    sb.Append($" {state}");
                sb.Append(". ");
            }

            sb.Append("RimWorldAccess.Biotech.GeneInspection.NavHint".Translate());
            TolkHelper.Speak(sb.ToString());
        }

        #endregion

        #region Announcement Formatters

        /// <summary>
        /// Formats item announcement matching the original GeneInspectionState format:
        /// "{label stripped}{space+expanded/collapsed}.{levelSuffix} {position}."
        /// </summary>
        private static string FormatItemAnnouncement(InspectionTreeItem item)
        {
            try
            {
                // Strip XML tags from label
                string label = item.Label.StripTags().TrimEnd('.', '!', '?');

                // Build state indicator (only for expandable items)
                string stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);

                // Get sibling position
                var (position, total) = treeNav.GetSiblingPosition(item);

                // Build level suffix if level changed (skipLevelOne: false for gene inspection)
                string levelSuffix = MenuHelper.GetLevelSuffix("GeneInspection", item.IndentLevel, skipLevelOne: false);

                // Build full announcement (respects AnnouncePosition setting)
                string positionPart = MenuHelper.FormatPosition(position - 1, total);
                string announcement = string.IsNullOrEmpty(positionPart)
                    ? $"{label}{stateIndicator}.{levelSuffix}"
                    : $"{label}{stateIndicator}.{levelSuffix} {positionPart}.";

                return announcement;
            }
            catch (Exception ex)
            {
                Log.Error($"[GeneInspectionState] Error formatting announcement: {ex.Message}");
                return item.Label.StripTags();
            }
        }

        /// <summary>
        /// Formats search announcement matching the original GeneInspectionState format:
        /// "{label stripped}{space+expanded/collapsed}, {N} of {M} matches for '{search}'"
        /// </summary>
        private static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            string label = item.Label.StripTags();

            string stateIndicator = TreeNavigationHelper.FormatExpansionSpaceSuffix(item);

            return typeahead.BuildItemAnnouncement($"{label}{stateIndicator}");
        }

        #endregion

        #region Custom Actions

        /// <summary>
        /// Handles Enter key activation. For expandable items that are already expanded,
        /// shows a reject message. For collapsed expandable items, expands them.
        /// For GeneDef leaf items (shouldn't normally occur), opens InfoCard.
        /// </summary>
        private static bool HandleActivate(InspectionTreeItem item)
        {
            // Handle expandable items
            if (item.IsExpandable)
            {
                if (item.IsExpanded)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.AlreadyExpanded".Translate());
                    return true;
                }
                // Collapsed: let treeNav expand it via ExpandOrDrillDown
                treeNav.ExpandOrDrillDown();
                return true;
            }

            // For gene items, open InfoCard
            if (item.Data is GeneDef gene)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(gene));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }

            // Otherwise, nothing to do
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.NoAction".Translate());
            return true;
        }

        #endregion
    }
}
