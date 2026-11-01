using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// The data facade for Dialog_InfoCard's accessible tree (Stats, Character, Health, Records,
    /// Permits): the dialog lifecycle, the nested-card save stack, the multi-hyperlink float-menu
    /// ownership flag, and the statics the rest of the mod calls. Keyboard navigation, typeahead
    /// and announcement composition live in <see cref="Shell.InfoCardScope"/>.
    /// The tree ROOT lives here because the Window.PostOpen patch builds it before the
    /// WindowStack.Add postfix has attached a scope to hold it; the scope seeds itself from
    /// <see cref="CurrentTreeRoot"/> in OnPush and owns the cursor from then on. Every later tree
    /// operation reaches the live instance through <c>Shell.InfoCardScope.Live</c>, which resolves
    /// <see cref="CurrentDialog"/>, so a nested card's close restores the OUTER card's tree.
    /// </summary>
    public static class InfoCardState
    {
        public static bool IsActive { get; private set; } = false;

        private static Dialog_InfoCard currentDialog = null;

        // Seeds for the live scope, which owns the cursor once it is pushed.
        private static InspectionTreeItem currentRoot = null;
        private static int currentIndex = 0;

        /// <summary>The card the state currently tracks, so the scope registry can resolve the live instance.</summary>
        internal static Dialog_InfoCard CurrentDialog => currentDialog;

        /// <summary>Seed tree root for <c>InfoCardScope.OnPush</c>.</summary>
        internal static InspectionTreeItem CurrentTreeRoot => currentRoot;

        /// <summary>Seed cursor index for <c>InfoCardScope.OnPush</c>.</summary>
        internal static int CurrentTreeIndex => currentIndex;

        private class SavedCardState
        {
            public Dialog_InfoCard dialog;
            public InspectionTreeItem rootItem;
            public int selectedIndex;
        }
        private static Stack<SavedCardState> cardStack = new Stack<SavedCardState>();

        // Keeps PostClose out of the way while CloseInfoCard is driving.
        private static bool closingFromAccessibility = false;

        // Whether this state opened the current float menu; a menu from another context must keep
        // handling its own input.
        private static bool ownsFloatMenu = false;

        /// <summary>
        /// Whether the open windowless float menu is the card's own multi-hyperlink picker. Read
        /// only by FloatMenuOverlayScope's Cancel claim, so the picker's Escape announces the
        /// info-card "returning to card" wording rather than the generic one. That claim pairs the
        /// flag with WindowlessFloatMenuState.IsActive, so a value outliving the menu is inert.
        /// </summary>
        internal static bool OwnsFloatMenu => ownsFloatMenu;

        /// <summary>Clears the ownsFloatMenu flag once the picker closes through the scope's Escape claim.</summary>
        internal static void ReleaseFloatMenu()
        {
            ownsFloatMenu = false;
        }

        public static bool IsClosingFromAccessibility => closingFromAccessibility;
        public static bool HasSavedState => cardStack.Count > 0;

        /// <summary>
        /// Hands the current tree root to the live scope if one is attached yet. Open() runs from
        /// Window.PostOpen, which vanilla calls before the WindowStack.Add postfix attaches the
        /// scope, so a null here is normal: OnPush seeds itself.
        /// </summary>
        private static void ApplyTreeToScope()
        {
            Shell.InfoCardScope live = Shell.InfoCardScope.Live;
            if (live != null)
            {
                live.LoadTree(currentRoot, currentIndex);
            }
        }

        /// <summary>Opens the accessible state for a dialog; <paramref name="announceOpening"/> false delays the announcement until stats load.</summary>
        public static void Open(Dialog_InfoCard dialog, bool announceOpening = true)
        {
            try
            {
                if (dialog == null)
                    return;

                currentDialog = dialog;
                IsActive = true;

                // Enter and Escape are ours, for navigation.
                dialog.closeOnAccept = false;
                dialog.closeOnCancel = false;

                // Multiple cards must coexist for nested navigation: RimWorld's default
                // onlyOneOfTypeAllowed makes WindowStack.Add remove the outer card.
                dialog.onlyOneOfTypeAllowed = false;

                // Stats may not be populated on the first frame.
                currentRoot = InfoCardTreeBuilder.BuildTree(dialog);
                currentIndex = 0;
                ApplyTreeToScope();

                ownsFloatMenu = false;

                if (announceOpening)
                {
                    SoundDefOf.TabOpen.PlayOneShotOnCamera();
                    AnnounceOpening();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardState] Error opening: {ex.Message}");
                Close();
            }
        }

        /// <summary>Rebuilds the tree and announces opening, once stats have loaded.</summary>
        public static void RebuildAndAnnounce()
        {
            if (!IsActive || currentDialog == null)
                return;

            try
            {
                currentRoot = InfoCardTreeBuilder.BuildTree(currentDialog);
                currentIndex = 0;
                ApplyTreeToScope();

                SoundDefOf.TabOpen.PlayOneShotOnCamera();
                AnnounceOpening();
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardState] Error rebuilding: {ex.Message}");
            }
        }

        /// <summary>Closes the accessible state.</summary>
        public static void Close()
        {
            IsActive = false;
            currentDialog = null;
            currentRoot = null;
            currentIndex = 0;
            cardStack.Clear();
            ownsFloatMenu = false;
        }

        /// <summary>Closes the current card, restoring the outer card's state when nested.</summary>
        public static void CloseInfoCard()
        {
            if (!IsActive)
                return;

            if (cardStack.Count > 0)
            {
                // TryRemove rather than Dialog_InfoCard.Close(), which would clear RimWorld's
                // static history; the card's own close sound still plays.
                closingFromAccessibility = true;
                if (currentDialog != null)
                    Find.WindowStack.TryRemove(currentDialog, doCloseSound: true);
                closingFromAccessibility = false;

                var saved = cardStack.Pop();
                currentDialog = saved.dialog;
                currentRoot = saved.rootItem;
                currentIndex = saved.selectedIndex;
                ApplyTreeToScope();
                Shell.InfoCardScope.Live?.AnnounceCurrentRow();
            }
            else
            {
                closingFromAccessibility = true;
                if (currentDialog != null)
                    currentDialog.Close();
                closingFromAccessibility = false;

                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                InspectionReturnHelper.AnnounceParentOrFallback(
                    "RimWorldAccess.Inspection.InfoCard.InfoCardClosed".Translate());
            }
        }

        /// <summary>Closes every stacked card, nested ones first, and tears the state down without announcing.</summary>
        private static void CloseAllCards()
        {
            var cards = Find.WindowStack.Windows.OfType<Dialog_InfoCard>().ToList();
            closingFromAccessibility = true;
            try
            {
                for (int i = cards.Count - 1; i > 0; i--)
                    Find.WindowStack.TryRemove(cards[i], doCloseSound: false);
                if (cards.Count > 0)
                    cards[0].Close();
            }
            finally
            {
                closingFromAccessibility = false;
            }
            Close();
        }

        /// <summary>Restores state from the stack for a dialog, for PostClose's external-closure path.</summary>
        public static void RestoreFromStack(Dialog_InfoCard remainingCard)
        {
            if (cardStack.Count > 0)
            {
                var saved = cardStack.Pop();
                currentDialog = saved.dialog;
                currentRoot = saved.rootItem;
                currentIndex = saved.selectedIndex;
                ApplyTreeToScope();
            }
            else
            {
                // Fallback: re-initialize from scratch.
                Open(remainingCard, announceOpening: false);
            }
        }

        /// <summary>Clears the saved state stack.</summary>
        public static void ClearStack()
        {
            cardStack.Clear();
        }

        /// <summary>
        /// Every inspectable hyperlink on a tree item, kept as vanilla's own
        /// <c>Dialog_InfoCard.Hyperlink</c> struct rather than collapsed to a bare Def, so every
        /// shape vanilla's hover system carries survives to <see cref="ActivateHyperlink"/>. Only
        /// stat rows can have hyperlinks: a vanilla <see cref="StatDrawEntry"/> or a pre-resolved
        /// link list from a compat reader (<see cref="HasLinkPayload"/>). A non-stat row walks up to
        /// its nearest such ancestor, so children inherit their stat's hyperlinks. Hyperlinks
        /// vanilla itself no-ops when hidden are filtered out, so an undiscovered item's real name
        /// cannot leak through a nested card's title.
        /// </summary>
        private static List<Dialog_InfoCard.Hyperlink> GetInspectableHyperlinks(InspectionTreeItem item, bool walkUpToParent)
        {
            var result = new List<Dialog_InfoCard.Hyperlink>();
            if (item == null) return result;

            // Direct Def data (gene nodes under a xenotype card).
            if (item.Data is Def directDef)
            {
                result.Add(new Dialog_InfoCard.Hyperlink(directDef));
                return FilterHidden(result);
            }

            // A list of Defs (a Genes parent node).
            if (item.Data is IReadOnlyList<Def> defList && defList.Count > 0)
            {
                foreach (var d in defList)
                    result.Add(new Dialog_InfoCard.Hyperlink(d));
                return FilterHidden(result);
            }

            var target = item;
            if (!HasLinkPayload(target) && walkUpToParent)
            {
                // Walk up to the nearest ancestor carrying links.
                target = target.Parent;
                while (target != null && !HasLinkPayload(target))
                    target = target.Parent;
            }

            if (target?.Data is StatDrawEntry statEntry)
            {
                try
                {
                    var hyperlinks = statEntry.GetHyperlinks(StatRequest.ForEmpty());
                    if (hyperlinks != null)
                        result.AddRange(hyperlinks);
                }
                catch { }
            }
            else if (target?.Data is IReadOnlyList<Dialog_InfoCard.Hyperlink> resolvedLinks)
            {
                result.AddRange(resolvedLinks);
            }
            return FilterHidden(result);
        }

        /// <summary>
        /// Whether a row carries hyperlinks of its own: a vanilla stat entry, or already-resolved
        /// links from a reader with no <see cref="StatDrawEntry"/> to hand over.
        /// </summary>
        private static bool HasLinkPayload(InspectionTreeItem item)
        {
            return item.Data is StatDrawEntry || item.Data is IReadOnlyList<Dialog_InfoCard.Hyperlink>;
        }

        private static List<Dialog_InfoCard.Hyperlink> FilterHidden(List<Dialog_InfoCard.Hyperlink> links)
        {
            return links.Where(l => !l.IsHidden).ToList();
        }

        /// <summary>Whether an item has hyperlinks directly, without walking up; the "Inspectable" hint uses this.</summary>
        public static bool HasInspectableHyperlink(InspectionTreeItem item)
        {
            return GetInspectableHyperlinks(item, walkUpToParent: false).Count > 0;
        }

        /// <summary>Opens a card for a Def, supplying default stuff for stuff-requiring ThingDefs as Hyperlink.ActivateHyperlink does.</summary>
        public static void OpenInfoCardForDef(Def def)
        {
            if (def is ThingDef thingDef && thingDef.MadeFromStuff)
            {
                var defaultStuff = GenStuff.DefaultStuffFor(thingDef);
                if (defaultStuff != null)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(thingDef, defaultStuff));
                    return;
                }
            }
            Find.WindowStack.Add(new Dialog_InfoCard(def));
        }

        /// <summary>
        /// Opens a card for a Def, or announces the fallback message and plays the reject sound when
        /// it is null; returns whether a card opened.
        /// </summary>
        public static bool TryOpenInfoCardForDef(Def def, string fallbackAnnouncement = null)
        {
            if (def == null)
            {
                SpeakNoInfoCardAvailable(fallbackAnnouncement);
                return false;
            }
            OpenInfoCardForDef(def);
            return true;
        }

        /// <summary>Announces that no info card is available and plays the reject sound.</summary>
        public static void SpeakNoInfoCardAvailable(string fallbackAnnouncement = null)
        {
            string announcement = fallbackAnnouncement
                ?? "RimWorldAccess.InfoCard.Unavailable".Translate().ToString();
            TolkHelper.SpeakData(announcement);
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Saves the live scope's cursor and tree state to the stack. The seed fields are only
        /// authoritative between PostOpen and the Add postfix; after that the scope owns the cursor.
        /// </summary>
        private static void PushCurrentState()
        {
            Shell.InfoCardScope live = Shell.InfoCardScope.Live;
            cardStack.Push(new SavedCardState
            {
                dialog = currentDialog,
                rootItem = live != null ? live.TreeRoot : currentRoot,
                selectedIndex = live != null ? live.TreeSelectedIndex : currentIndex
            });
        }

        /// <summary>Saves current state and opens a nested card for a Def.</summary>
        private static void PushStateAndOpenDef(Def def)
        {
            PushCurrentState();
            OpenInfoCardForDef(def);
        }

        /// <summary>Saves current state and opens a nested card for a world object.</summary>
        private static void PushStateAndOpenWorldObject(WorldObject worldObject)
        {
            PushCurrentState();
            Find.WindowStack.Add(new Dialog_InfoCard(worldObject));
        }

        /// <summary>Saves current state and opens a nested card for a royal title.</summary>
        private static void PushStateAndOpenTitle(RoyalTitleDef titleDef, Faction faction)
        {
            PushCurrentState();
            Find.WindowStack.Add(new Dialog_InfoCard(titleDef, faction));
        }

        /// <summary>
        /// Saves current state and opens a nested card for a faction, mirroring vanilla's own
        /// faction-hyperlink branch: it also switches the background Factions tab and scrolls it to
        /// this faction, a visual side effect with no accessible content to skip.
        /// </summary>
        private static void PushStateAndOpenFaction(Faction faction)
        {
            PushCurrentState();
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Factions);
            (Find.MainTabsRoot.OpenTab?.TabWindow as MainTabWindow_Factions)?.ScrollToFaction(faction);
            Find.WindowStack.Add(new Dialog_InfoCard(faction));
        }

        /// <summary>Rebuilds the visible items and clamps the selection after a tree structure change.</summary>
        public static void RefreshVisibleListAndAnnounce()
        {
            Shell.InfoCardScope.Live?.RefreshTreeAndAnnounce();
        }

        /// <summary>Announces the card opening.</summary>
        private static void AnnounceOpening()
        {
            if (currentRoot == null)
                return;

            string rootLabel = currentRoot.Label.StripTags();

            // Children are either tabs (Category) or, in the single-tab case, direct content.
            bool hasTabs = currentRoot.Children.Count > 0 &&
                           currentRoot.Children[0].Type == InspectionTreeItem.ItemType.Category;

            string announcement;
            if (hasTabs)
            {
                int tabCount = currentRoot.Children.Count;
                string key = tabCount == 1
                    ? "RimWorldAccess.Inspection.InfoCard.OpeningWithTabsOne"
                    : "RimWorldAccess.Inspection.InfoCard.OpeningWithTabsMany";
                announcement = key.Translate(rootLabel, tabCount);
            }
            else
            {
                int itemCount = currentRoot.Children.Count;
                string key = itemCount == 1
                    ? "RimWorldAccess.Inspection.InfoCard.OpeningFlatOne"
                    : "RimWorldAccess.Inspection.InfoCard.OpeningFlatMany";
                announcement = key.Translate(rootLabel, itemCount);
            }

            TolkHelper.SpeakData(announcement);
        }

        /// <summary>
        /// Alt+I on a card row: one hyperlink activates directly, several present the picker float
        /// menu, none speaks the canonical "no info card available" feedback. The nested card's own
        /// PostOpen pushes a fresh scope and the save stack restores this one on close.
        /// </summary>
        internal static void OpenNestedCardFor(InspectionTreeItem item)
        {
            var links = GetInspectableHyperlinks(item, walkUpToParent: true);
            if (links.Count == 1)
            {
                ActivateHyperlinkForInspection(links[0]);
            }
            else if (links.Count > 1)
            {
                var options = new List<FloatMenuOption>();
                foreach (var link in links)
                {
                    var capturedLink = link;
                    string label = InfoCardDataExtractor.GetHyperlinkLabel(link)?.CapitalizeFirst() ?? "???";
                    options.Add(new FloatMenuOption(label, () => ActivateHyperlinkForInspection(capturedLink)));
                }
                TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
                ownsFloatMenu = true;
                WindowlessFloatMenuState.Open(options, false);
            }
            else
            {
                SpeakNoInfoCardAvailable();
            }
        }

        /// <summary>
        /// Activates one resolved hyperlink, riding the same shape ladder vanilla's
        /// <c>Hyperlink.ActivateHyperlink</c> does. A shape with a real info card of its own opens a
        /// NESTED card through this class's save/restore stack, so Escape backs out to the card the
        /// player came from. A shape vanilla never gives a card (ideo, quest) closes every stacked
        /// card and rides vanilla's own ActivateHyperlink. The gene-owner shape has no accessible
        /// surface unless the pawn is selectable on a map
        /// (vanilla's own gate), so it speaks the resolved label rather than doing nothing.
        /// </summary>
        private static void ActivateHyperlinkForInspection(Dialog_InfoCard.Hyperlink link)
        {
            if (link.HasGeneOwnerThing)
            {
                if (ThingSelectionUtility.SelectableByMapClick(link.thing))
                {
                    link.ActivateHyperlink();
                    // The Genes tab opens beneath the cards, which keep the keyboard.
                    if (GeneInspectionState.IsActive)
                        CloseAllCards();
                }
                else
                    TolkHelper.SpeakData(InfoCardDataExtractor.GetHyperlinkLabel(link));
                return;
            }
            if (link.ideo != null || link.quest != null)
            {
                CloseAllCards();
                link.ActivateHyperlink();
                return;
            }
            if (link.researchProject != null)
            {
                PushStateAndOpenDef(link.researchProject);
                return;
            }
            if (link.worldObject != null)
            {
                PushStateAndOpenWorldObject(link.worldObject);
                return;
            }
            if (link.titleDef != null)
            {
                PushStateAndOpenTitle(link.titleDef, link.faction);
                return;
            }
            if (link.def != null)
            {
                PushStateAndOpenDef(link.def);
                return;
            }
            if (link.thing?.def != null)
            {
                PushStateAndOpenDef(link.thing.def);
                return;
            }
            if (link.faction != null)
            {
                PushStateAndOpenFaction(link.faction);
                return;
            }
            SpeakNoInfoCardAvailable();
        }
    }
}
