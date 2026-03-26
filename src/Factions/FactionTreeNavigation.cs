using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared tree navigation logic for faction screens.
    /// Used by both FactionTabState (in-game, windowless) and
    /// FactionLandingState (pre-game, dialog-based).
    /// Wraps TreeNavigationHelper with faction-specific behavior.
    /// </summary>
    internal class FactionTreeNavigation
    {
        private readonly TreeNavigationHelper treeNav = new TreeNavigationHelper("FactionTree");

        public bool HasActiveSearch => treeNav.HasActiveSearch;

        public FactionTreeNavigation()
        {
            treeNav.OnInfo = HandleInfoCard;
        }

        /// <summary>
        /// Initializes the tree from a faction list. Builds tree, flattens,
        /// resets selection to 0, and announces opening.
        /// </summary>
        public void Initialize(List<Faction> factions)
        {
            var rootItem = FactionHelper.BuildFactionTree(factions);
            treeNav.Initialize(rootItem);
            AnnounceOpening(factions.Count);
        }

        /// <summary>
        /// Resets all tree state.
        /// </summary>
        public void Reset()
        {
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

        #region Announcements

        private void AnnounceOpening(int factionCount)
        {
            if (factionCount > 0 && treeNav.Count > 0)
            {
                var sb = new StringBuilder($"Faction relations, {factionCount} factions");
                var firstItem = treeNav.VisibleItems[0];
                FactionHelper.AppendSentence(sb, firstItem.Label);

                if (firstItem.IsExpandable)
                {
                    string state = firstItem.IsExpanded ? "expanded" : "collapsed";
                    sb.Append($", {state}, {firstItem.Children.Count} {(firstItem.Children.Count == 1 ? "item" : "items")}");
                }

                var (pos, total) = treeNav.GetSiblingPosition(firstItem);
                string position = MenuHelper.FormatPosition(pos - 1, total);
                if (!string.IsNullOrEmpty(position))
                    FactionHelper.AppendSentence(sb, position);

                TolkHelper.Speak(sb.ToString());
            }
            else
            {
                TolkHelper.Speak("Faction relations. No factions.");
            }
        }

        #endregion

        #region Info Card

        private bool HandleInfoCard(InspectionTreeItem item)
        {
            Faction faction = GetSelectedFaction();
            if (faction == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("No faction selected.");
                return true;
            }
            Find.WindowStack.Add(new Dialog_InfoCard(faction));
            return true;
        }

        /// <summary>
        /// Finds the Faction object for the current selection by walking up the tree
        /// until a node with Data of type Faction is found.
        /// </summary>
        private Faction GetSelectedFaction()
        {
            var item = treeNav.SelectedItem;
            while (item != null)
            {
                if (item.Data is Faction f) return f;
                item = item.Parent;
            }
            return null;
        }

        #endregion
    }
}
