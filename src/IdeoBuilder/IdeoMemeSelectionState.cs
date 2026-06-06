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
    /// Keyboard-accessible state wrapping Dialog_ChooseMemes (structure + normal meme picker).
    ///
    /// Tree navigation:
    ///   - Structure: top level is MemeGroupDef ("Other" for ungrouped), then individual memes.
    ///   - Normal: top level is impact tier (Low/Medium/High), then MemeGroupDef, then memes.
    ///
    /// Keys:
    ///   Up/Down/Home/End/Left/Right/Ctrl+Home/Ctrl+End — standard tree navigation
    ///   Enter or Space on a meme node — toggle selection (announces new state + impact)
    ///   Enter on a group node — expand/collapse (default tree behavior)
    ///   Alt+S — accept (TryAccept, with validation errors announced)
    ///   Alt+R — randomize (matches vanilla's Randomize button)
    ///   Escape — Back (matches vanilla's Back button)
    ///   A-Z / 0-9 — typeahead search across visible memes
    /// </summary>
    public static class IdeoMemeSelectionState
    {
        public static bool IsActive { get; private set; }

        private static Dialog_ChooseMemes currentDialog;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("IdeoMemePicker");
        private static bool configured;

        public static Dialog_ChooseMemes CurrentDialog => currentDialog;

        #region Lifecycle

        public static void EnsureOpen(Dialog_ChooseMemes dialog)
        {
            if (IsActive && System.Object.ReferenceEquals(currentDialog, dialog))
                return;

            currentDialog = dialog;
            IsActive = true;
            EnsureConfigured();
            RebuildTree();
            AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            currentDialog = null;
            treeNav.Reset();
        }

        public static void RebuildTree()
        {
            if (currentDialog == null) return;
            var root = IdeoMemeSelectionHelper.BuildTree(currentDialog);
            treeNav.Initialize(root);
        }

        private static void EnsureConfigured()
        {
            if (configured) return;
            configured = true;

            treeNav.AnnounceChildCounts = false; // we put counts in our own labels
            treeNav.FormatItemAnnouncement = FormatItem;
            treeNav.FormatStateChangeAnnouncement = FormatStateChange;
            treeNav.FormatSearchAnnouncement = FormatSearch;
            treeNav.OnActivate = HandleActivate;
        }

        #endregion

        #region Tree formatters / activation

        private static string FormatItem(InspectionTreeItem item)
        {
            // A meme's detail lines (one tooltip line apiece) read as just their text — no
            // position/level chatter, since the user is stepping through one meme's contents.
            if (item.Parent?.Data is MemeDef && !item.IsExpandable)
                return item.Label;

            var sb = new StringBuilder();
            sb.Append(ShortOrFullLabel(item));

            if (item.IsExpandable)
            {
                string state = item.IsExpanded ? "expanded" : "collapsed";
                sb.Append(", ").Append(state);
            }

            var (pos, total) = treeNav.GetSiblingPosition(item);
            string position = MenuHelper.FormatPosition(pos - 1, total);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);

            string levelSuffix = MenuHelper.GetLevelSuffix("IdeoMemePicker", item.IndentLevel);
            if (!string.IsNullOrEmpty(levelSuffix))
                sb.Append(levelSuffix);

            return sb.ToString();
        }

        /// <summary>
        /// Smart label: an expanded meme reads its short form (name [+ "Selected"]) since its
        /// details are now its child nodes; a collapsed meme reads its full inline details.
        /// </summary>
        private static string ShortOrFullLabel(InspectionTreeItem item)
        {
            if (item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel))
                return item.ExpandedLabel;
            return item.Label;
        }

        private static string FormatStateChange(InspectionTreeItem item)
        {
            // After expand/collapse, use the short label when one exists (memes) so the user hears
            // a terse "Expanded. Flesh purity" rather than the whole detail wall on every keypress.
            string state = item.IsExpanded ? "Expanded" : "Collapsed";
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            return state + ". " + label;
        }

        private static string FormatSearch(InspectionTreeItem item, TypeaheadSearchHelper t)
        {
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            string searchInfo = $", {t.CurrentMatchPosition} of {t.MatchCount} matches for '{t.SearchBuffer}'";
            return label + searchInfo;
        }

        private static bool HandleActivate(InspectionTreeItem item)
        {
            var meme = MemeForNode(item);
            if (meme != null)
            {
                // Structure is single-select (radio): selecting both picks the meme and advances
                // to the next screen, rather than just toggling it on. Other categories are
                // multi-select, so this toggles selection in place.
                if (meme.category == MemeCategory.Structure)
                {
                    var newMemes = IdeoMemeSelectionHelper.GetNewMemes(currentDialog);
                    if (newMemes == null || !newMemes.Contains(meme))
                        ToggleMeme(meme, announce: false);
                    Accept();
                    return true;
                }

                ToggleMeme(meme, announce: true);
                return true;
            }
            return false; // fall back to default expand/collapse for category nodes
        }

        /// <summary>
        /// A meme node carries the MemeDef directly; its detail-line children carry it on their
        /// parent. Activating either selects the meme — the detail lines are "part of the meme".
        /// </summary>
        private static MemeDef MemeForNode(InspectionTreeItem item)
        {
            if (item?.Data is MemeDef m) return m;
            if (item?.Parent?.Data is MemeDef pm) return pm;
            return null;
        }

        #endregion

        #region Selection toggle

        /// <summary>
        /// Mirrors the click-handling block in Dialog_ChooseMemes.DrawMeme. We can't call it
        /// directly (it's tied to ButtonInvisible) so we re-implement the same rules: structure
        /// memes are single-select; configuring-new-fluid restricts to one Normal; reforming
        /// fluid limits removals; otherwise toggles freely subject to CanRemoveMeme.
        /// </summary>
        public static void ToggleMeme(MemeDef meme, bool announce)
        {
            if (currentDialog == null || meme == null) return;

            var newMemes = IdeoMemeSelectionHelper.GetNewMemes(currentDialog);
            var category = IdeoMemeSelectionHelper.GetMemeCategory(currentDialog);
            bool configuringNewFluid = IdeoMemeSelectionHelper.GetConfiguringNewFluidIdeo(currentDialog);
            bool reformingFluid = IdeoMemeSelectionHelper.GetReformingFluidIdeo(currentDialog);
            var ideo = IdeoMemeSelectionHelper.GetIdeo(currentDialog);

            bool isSelected = newMemes.Contains(meme);
            var displaced = new List<MemeDef>();

            if (isSelected)
            {
                if (meme.category == MemeCategory.Structure)
                {
                    // Vanilla returns silently here — structure memes can't be deselected;
                    // selecting a different structure swaps them.
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("ChooseStructureMeme".Translate());
                    return;
                }

                var report = IdeoMemeSelectionHelper.CanRemoveMeme(currentDialog, meme);
                if (!report.Accepted)
                {
                    // Vanilla shows a message only for required memes; the one-change-per-reform rule
                    // returns a bare false with no message, so we stay silent there too (the reject
                    // sound conveys "can't") — faithful parity, don't invent a message.
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    if (!string.IsNullOrEmpty(report.Reason))
                        TolkHelper.Speak(report.Reason, SpeechPriority.High);
                    return;
                }
                newMemes.Remove(meme);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                // Single-select modes silently displace other memes; collect them so the player
                // hears what was deselected (e.g. picking a new Normal meme during the fluid initial
                // pick swaps out the previous one).
                if (meme.category == MemeCategory.Structure)
                {
                    // Remove all existing structure memes (single-select).
                    displaced.AddRange(newMemes.Where(m => m.category == MemeCategory.Structure));
                    newMemes.RemoveAll(m => m.category == MemeCategory.Structure);
                }
                else if (configuringNewFluid)
                {
                    // Fluid initial pick allows only one Normal meme; swap.
                    displaced.AddRange(newMemes.Where(m => m.category == MemeCategory.Normal));
                    newMemes.RemoveAll(m => m.category == MemeCategory.Normal);
                }
                else if (reformingFluid)
                {
                    int removeCount = IdeoMemeSelectionHelper.GetNormalMemesRemoveCount(currentDialog);
                    if (removeCount >= 1 && !ideo.memes.Contains(meme))
                    {
                        // Reforming a fluid ideoligion allows only one normal-meme change.
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak("ReformIdeoAddOrRemoveMeme".Translate(), SpeechPriority.High);
                        return;
                    }
                    displaced.AddRange(newMemes.Where(m => !ideo.memes.Contains(m)));
                    newMemes.RemoveAll(m => !ideo.memes.Contains(m));
                }

                newMemes.Add(meme);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            // Toggling never changes tree structure (memes don't move), only selection state.
            // Refresh every meme node's labels in place — single-select modes deselect other
            // memes, so siblings' "Selected" markers can change too — then announce the change.
            RefreshAllMemeLabels(treeNav.RootItem);
            if (announce)
            {
                // Terse confirmation: just the meme name + its new state + the running impact /
                // validation line, NOT the whole description.
                bool nowSelected = newMemes.Contains(meme);
                var sb = new StringBuilder();
                sb.Append(meme.LabelCap.ToString()).Append(", ").Append(nowSelected ? "Selected" : "Removed");
                // Name any meme that was silently swapped out so the player isn't surprised.
                foreach (var d in displaced)
                    sb.Append(". ").Append(d.LabelCap.ToString()).Append(", ").Append("Removed");
                string status = IdeoMemeSelectionHelper.BuildStatusLine(currentDialog);
                if (!string.IsNullOrEmpty(status))
                    sb.Append(". ").Append(status);
                TolkHelper.Speak(sb.ToString());
            }
        }

        private static void RefreshAllMemeLabels(InspectionTreeItem node)
        {
            foreach (var child in node.Children)
            {
                if (child.Data is MemeDef)
                    IdeoMemeSelectionHelper.PopulateMemeNode(currentDialog, child);
                RefreshAllMemeLabels(child);
            }
        }

        #endregion

        #region Randomize / Accept / Back

        public static void Randomize()
        {
            if (currentDialog == null) return;
            try
            {
                var ideo = IdeoMemeSelectionHelper.GetIdeo(currentDialog);
                var category = IdeoMemeSelectionHelper.GetMemeCategory(currentDialog);
                var newMemes = IdeoMemeSelectionHelper.GetNewMemes(currentDialog);
                var range = IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(currentDialog);
                bool configuringNewFluid = IdeoMemeSelectionHelper.GetConfiguringNewFluidIdeo(currentDialog);
                bool reformingFluid = IdeoMemeSelectionHelper.GetReformingFluidIdeo(currentDialog);

                FactionDef forFaction = IdeoUIUtility.FactionForRandomization(ideo);
                List<MemeDef> randomized;
                if (category == MemeCategory.Normal)
                {
                    if (reformingFluid)
                        randomized = IdeoUtility.RandomizeNormalMemesForReforming(range.max, ideo.memes, forFaction);
                    else
                        randomized = IdeoUtility.RandomizeNormalMemes(
                            GenMath.RoundRandom(range.Average), newMemes, forFaction, configuringNewFluid);
                }
                else
                {
                    randomized = IdeoUtility.RandomizeStructureMeme(newMemes, forFaction);
                }

                // Replace newMemes contents (preserves the same list reference the dialog uses)
                newMemes.Clear();
                newMemes.AddRange(randomized);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                RebuildTree();

                // Structure is single-select: name the structure meme that was rolled and move the
                // cursor onto it, so the player knows what they got and can read/keep or re-roll.
                if (category == MemeCategory.Structure)
                {
                    var chosen = newMemes.FirstOrDefault(m => m.category == MemeCategory.Structure);
                    if (chosen != null)
                    {
                        FocusMemeNode(chosen);
                        TolkHelper.Speak("Randomize".Translate() + ". " + chosen.LabelCap + ", Selected");
                        return;
                    }
                }
                // Normal memes: name the memes that were rolled (not just the impact), then the
                // impact/validation status.
                string names = string.Join(", ", newMemes
                    .Where(m => m.category == MemeCategory.Normal)
                    .Select(m => (string)m.LabelCap));
                if (string.IsNullOrEmpty(names)) names = "None".Translate();
                TolkHelper.Speak("Randomize".Translate() + ". " + names + ", Selected. "
                    + IdeoMemeSelectionHelper.BuildStatusLine(currentDialog));
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error randomizing memes: {ex}");
            }
        }

        public static void Accept()
        {
            if (currentDialog == null) return;
            IdeoMemeSelectionHelper.InvokeTryAccept(currentDialog);
        }

        /// <summary>Moves the tree cursor onto the node carrying the given meme, if it's visible.</summary>
        private static void FocusMemeNode(MemeDef meme)
        {
            var items = treeNav.VisibleItems;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i].Data, meme))
                {
                    treeNav.SetSelectedIndex(i);
                    return;
                }
            }
        }

        public static void Back()
        {
            if (currentDialog == null) return;
            // Simulate the Back button (see Dialog_ChooseMemes.DoWindowContents). For initialSelection
            // on Normal, vanilla chains back to the Structure dialog; otherwise close and notify the page.
            var category = IdeoMemeSelectionHelper.GetMemeCategory(currentDialog);
            bool initialSelection = IdeoMemeSelectionHelper.GetInitialSelection(currentDialog);
            var ideo = IdeoMemeSelectionHelper.GetIdeo(currentDialog);

            currentDialog.Close(doCloseSound: false);

            if (category == MemeCategory.Normal && initialSelection)
            {
                // Chain back to structure picker (vanilla behavior).
                Find.WindowStack.Add(new Dialog_ChooseMemes(ideo, MemeCategory.Structure, initialSelection: true));
                return;
            }

            var page = Find.WindowStack.WindowOfType<Page_ConfigureIdeo>();
            page?.Notify_ClosedChooseMemesDialog();

            // Notify_ClosedChooseMemesDialog removes the freshly-made empty ideo when no normal
            // meme was chosen (i.e. the player abandoned the initial structure pick) but leaves
            // page.ideo dangling at that removed, unconfigured ideo — culture-less, name-less, with
            // uninitialized style counts. There is nothing to configure, so leave the builder and
            // return to the previous page (preset selection) rather than stranding the player on a
            // ghost ideo. A configured ideo (still in the manager) keeps the player on the hub.
            if (page != null && (page.ideo == null || !Find.IdeoManager.IdeosListForReading.Contains(page.ideo)))
                IdeoBuilderHubPatch.LeaveBuilderAbandoned(page);
        }

        #endregion

        #region Input dispatch

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool ctrl = ev.control;
            bool alt = KeyboardHelper.IsAltHeld;

            // Alt+S — accept
            if (key == KeyCode.S && alt && !ctrl)
            {
                Accept();
                return true;
            }

            // Alt+R — randomize
            if (key == KeyCode.R && alt && !ctrl)
            {
                Randomize();
                return true;
            }

            // Escape — Back (clear search first if active)
            if (key == KeyCode.Escape)
            {
                if (treeNav.HasActiveSearch)
                {
                    treeNav.Typeahead.ClearSearchAndAnnounce();
                    treeNav.ReannounceCurrentItem();
                    return true;
                }
                Back();
                return true;
            }

            // Space — identical to Enter on a meme (or its detail child): select/deselect, and for
            // single-select structure, select + advance. Non-meme nodes fall through to re-announce.
            if (key == KeyCode.Space && !alt && !ctrl)
            {
                if (HandleActivate(treeNav.SelectedItem))
                    return true;
                // Let TreeNavigationHelper handle (re-announce)
            }

            // Backspace — delete a typeahead search character.
            if (key == KeyCode.Backspace && treeNav.HasActiveSearch)
            {
                treeNav.HandleTypeaheadBackspace();
                return true;
            }

            // Typeahead search. This dialog is driven from a DoWindowContents prefix rather than
            // UnifiedKeyboardPatch, so the layout-aware character dispatcher never runs for us;
            // we read Event.current.character directly off the character half of Unity's key-event
            // pair (same approach as IdeologySelectionPatch / IdeoBuilderHubPatch).
            if (!alt && !ctrl)
            {
                char c = ev.character;
                if (c != '\0' && char.IsLetterOrDigit(c))
                {
                    treeNav.HandleTypeaheadCharacter(c);
                    return true;
                }
            }

            // All other keys — delegate to TreeNavigationHelper (Up/Down/Left/Right, Enter for groups,
            // Home/End, and the keyCode half of letter presses which it consumes to suppress game
            // hotkeys).
            return treeNav.HandleInput(ev);
        }

        #endregion

        #region Opening announcement

        private static void AnnounceOpening()
        {
            if (currentDialog == null) return;

            var sb = new StringBuilder();
            var category = IdeoMemeSelectionHelper.GetMemeCategory(currentDialog);
            bool configuringNewFluid = IdeoMemeSelectionHelper.GetConfiguringNewFluidIdeo(currentDialog);
            bool reformingFluid = IdeoMemeSelectionHelper.GetReformingFluidIdeo(currentDialog);

            string title = category == MemeCategory.Structure
                ? "ChooseStructure".Translate().ToString()
                : (configuringNewFluid ? "ChooseStartingMeme".Translate().ToString() : "ChooseMemes".Translate().ToString());

            sb.Append(title);

            string info;
            if (category == MemeCategory.Structure)
                info = "ChooseStructureMemesInfo".Translate();
            else if (configuringNewFluid)
                info = "ChooseNormalMemesFluidIdeoInfo".Translate(IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(currentDialog).min);
            else if (reformingFluid)
                info = "ChooseOrRemoveMeme".Translate() + " " + "SomeMemesHaveMoreImpact".Translate();
            else
            {
                var range = IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(currentDialog);
                info = "ChooseNormalMemesInfo".Translate(range.min, range.max) + " " + "SomeMemesHaveMoreImpact".Translate();
            }
            sb.Append(". ").Append(info);

            string status = IdeoMemeSelectionHelper.BuildStatusLine(currentDialog);
            if (!string.IsNullOrEmpty(status))
                sb.Append(". ").Append(status);

            // First visible item announcement — use the same smart label as arrow navigation
            // (ShortOrFullLabel): a collapsed meme reads its full inline details, an expanded one
            // reads its short name, so the opening matches what stepping onto the meme would say.
            if (treeNav.Count > 0)
            {
                var first = treeNav.VisibleItems[0];
                sb.Append(". ").Append(ShortOrFullLabel(first));
                if (first.IsExpandable)
                    sb.Append(", ").Append(first.IsExpanded ? "expanded" : "collapsed");
            }

            TolkHelper.Speak(sb.ToString(), SpeechPriority.High);
        }

        #endregion
    }
}
