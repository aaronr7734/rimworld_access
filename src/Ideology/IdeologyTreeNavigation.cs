using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Tree navigation logic for the ideology details panel.
    /// Wraps TreeNavigationHelper with ideology-specific behavior:
    /// smart label truncation, "Inspectable." suffix, ritual sound preview.
    /// </summary>
    internal class IdeologyTreeNavigation
    {
        private readonly TreeNavigationHelper treeNav = new TreeNavigationHelper("IdeologyTree");
        private static Sustainer ritualSoundPreview;

        public bool HasActiveSearch => treeNav.HasActiveSearch;

        public IdeologyTreeNavigation()
        {
            treeNav.FormatItemAnnouncement = FormatItemAnnouncement;
            treeNav.FormatStateChangeAnnouncement = FormatStateChangeAnnouncement;
            treeNav.FormatSearchAnnouncement = FormatSearchAnnouncement;
            treeNav.OnActivate = HandleActivate;
            treeNav.OnInfo = HandleInfoCard;
        }

        /// <summary>
        /// Initializes the tree from an ideology. Builds tree, flattens,
        /// resets selection to 0, and announces opening.
        /// </summary>
        public void Initialize(Ideo ideo)
        {
            var rootItem = IdeologyHelper.BuildIdeologyTree(ideo);
            treeNav.Initialize(rootItem);
            AnnounceOpening(ideo);
        }

        /// <summary>
        /// Resets all tree state.
        /// </summary>
        public void Reset()
        {
            StopRitualSound();
            treeNav.Reset();
        }

        /// <summary>
        /// Handles keyboard input for tree navigation.
        /// Returns true if input was handled.
        /// Returns false for Escape-close (no active search), letting the caller handle it.
        /// </summary>
        public bool HandleInput(Event ev)
        {
            return treeNav.HandleInput(ev);
        }

        // Expose for callers that need it
        public void AnnounceCurrentItem() => treeNav.ReannounceCurrentItem();

        #region Announcement Formatters

        private string FormatItemAnnouncement(InspectionTreeItem item)
        {
            // Smart label: expanded nodes use short name, collapsed/leaf use full label
            string label;
            if (item.IsExpandable && item.IsExpanded)
            {
                int sepIdx = item.Label.IndexOf(". ");
                label = sepIdx > 0 ? item.Label.Substring(0, sepIdx) : item.Label;
            }
            else
            {
                label = item.Label.TrimEnd('.', '!', '?');
            }

            string stateIndicator = TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true);

            var (position, total) = treeNav.GetSiblingPosition(item);
            string positionPart = MenuHelper.FormatPosition(position - 1, total);
            string positionSection = string.IsNullOrEmpty(positionPart)
                ? "" : $". {positionPart}";

            string levelSuffix = MenuHelper.GetLevelSuffix("IdeologyTree", item.IndentLevel);
            string inspectable = GetInspectableDefs().Count > 0 ? " Inspectable." : "";

            return $"{label}{stateIndicator}{positionSection}{levelSuffix}{inspectable}";
        }

        /// <summary>
        /// Short announcement after expand/collapse: just label + state.
        /// </summary>
        private string FormatStateChangeAnnouncement(InspectionTreeItem item)
        {
            int sepIdx = item.Label.IndexOf(". ");
            string shortLabel = sepIdx > 0 ? item.Label.Substring(0, sepIdx) : item.Label.TrimEnd('.', '!', '?');

            return $"{shortLabel}{TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true)}";
        }

        private string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead)
        {
            int searchSepIdx = item.IsExpandable ? item.Label.IndexOf(". ") : -1;
            string label = searchSepIdx > 0 ? item.Label.Substring(0, searchSepIdx) : item.Label.TrimEnd('.', '!', '?');

            string stateIndicator = "";
            if (item.IsExpandable)
                stateIndicator = item.IsExpanded ? ", expanded" : ", collapsed";

            string searchInfo = $", {typeahead.CurrentMatchPosition} of {typeahead.MatchCount} matches for '{typeahead.SearchBuffer}'";
            return $"{label}{stateIndicator}{searchInfo}";
        }

        private void AnnounceOpening(Ideo ideo)
        {
            if (treeNav.Count > 0)
            {
                var firstItem = treeNav.VisibleItems[0];

                string stateIndicator = TreeNavigationHelper.FormatExpansionSuffix(firstItem, includeChildCount: true);

                var (pos, total) = treeNav.GetSiblingPosition(firstItem);
                string position = MenuHelper.FormatPosition(pos - 1, total);

                string inspectable = GetInspectableDefs().Count > 0 ? " Inspectable." : "";
                TolkHelper.Speak($"{firstItem.Label}{stateIndicator}. {position}{inspectable}");
            }
            else
            {
                TolkHelper.Speak(ideo.name + ". " + "NoneLower".Translate());
            }
        }

        #endregion

        #region Custom Actions

        private bool HandleActivate(InspectionTreeItem item)
        {
            // Ritual sound toggle
            if (item.Data is SoundDef soundDef)
            {
                ToggleRitualSound(soundDef);
                return true;
            }
            return false; // Fall back to default expand/collapse toggle
        }

        #endregion

        #region Info Card

        private bool HandleInfoCard(InspectionTreeItem item)
        {
            var defs = GetInspectableDefs();
            if (defs.Count == 0)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return true;
            }

            if (defs.Count == 1)
            {
                InfoCardState.OpenInfoCardForDef(defs[0]);
                return true;
            }

            // Multiple defs — present selection menu
            var options = new List<FloatMenuOption>();
            foreach (var def in defs)
            {
                var captured = def;
                string label = def.label?.CapitalizeFirst() ?? def.defName;
                options.Add(new FloatMenuOption(label, () => InfoCardState.OpenInfoCardForDef(captured)));
            }
            TolkHelper.Speak("Choose item to inspect");
            WindowlessFloatMenuState.Open(options, false);
            return true;
        }

        /// <summary>
        /// Walks up the tree from the current item to find inspectable Defs.
        /// Supports both single Def and List&lt;Def&gt; stored in Data.
        /// </summary>
        private List<Def> GetInspectableDefs()
        {
            var item = treeNav.SelectedItem;
            var rootItem = treeNav.RootItem;
            while (item != null && item != rootItem)
            {
                if (item.Data is Def def && !(def is SoundDef))
                    return new List<Def> { def };
                if (item.Data is List<Def> defs && defs.Count > 0)
                    return defs;
                item = item.Parent;
            }
            return new List<Def>();
        }

        #endregion

        #region Ritual Sound

        private void ToggleRitualSound(SoundDef soundDef)
        {
            if (ritualSoundPreview != null)
            {
                ritualSoundPreview.End();
                ritualSoundPreview = null;
                TolkHelper.Speak("RitualAmbienceSound".Translate().Resolve() + ", stopped.");
            }
            else
            {
                SoundInfo info = SoundInfo.OnCamera(MaintenanceType.PerFrame);
                info.forcedPlayOnCamera = true;
                info.testPlay = true;
                ritualSoundPreview = soundDef.TrySpawnSustainer(info);
                TolkHelper.Speak("RitualAmbienceSound".Translate().Resolve() + ", playing.");
            }
        }

        public static void MaintainRitualSound()
        {
            if (ritualSoundPreview != null)
            {
                if (ritualSoundPreview.Ended)
                {
                    ritualSoundPreview = null;
                    return;
                }
                ritualSoundPreview.Maintain();
                Find.MusicManagerPlay?.ForceSilenceFor(0.1f);
            }
        }

        public static void StopRitualSound()
        {
            if (ritualSoundPreview != null)
            {
                ritualSoundPreview.End();
                ritualSoundPreview = null;
            }
        }

        #endregion
    }
}
