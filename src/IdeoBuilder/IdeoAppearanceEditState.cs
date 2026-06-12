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
    /// Windowless overlay for editing an ideoligion's appearance items — the allowed hair, beard
    /// and tattoo styles (vanilla's <see cref="Dialog_EditIdeoStyleItems"/>, reached from the
    /// "Appearance" box in the ideo editor / reform dialog). Opened from the builder hub and the
    /// reform dialog's stage 2.
    ///
    /// Presented as a tree: four item-type groups (hair, beard, face tattoo, body tattoo), each
    /// holding the game's style-item categories, each expandable into individual styles. Every
    /// style carries a frequency (Never … Frequent) controlling how often colonists of this
    /// ideoligion roll it, and — for hair and tattoos — a gender it applies to. Edits are applied
    /// live to <c>ideo.style</c>, mirroring the data the vanilla dialog writes on Done.
    ///
    /// Keys:
    ///   Up/Down/Home/End/Left/Right — tree navigation
    ///   Enter — on a style: choose its frequency; on a category: expand/collapse
    ///   ] — on a style: set its gender (hair/tattoo only); on a category: set the frequency of
    ///       every style in the category at once
    ///   Space — re-announce
    ///   A-Z / 0-9 — typeahead
    ///   Escape — close, return to the hub
    /// </summary>
    public static class IdeoAppearanceEditState
    {
        public static bool IsActive { get; private set; }

        private static Ideo ideo;
        private static TreeNavigationHelper treeNav = new TreeNavigationHelper("IdeoAppearance");
        private static bool configured;

        private enum ItemKind { Hair, Beard, FaceTattoo, BodyTattoo }

        // Marker for a category node so we can resolve its style items + kind for bulk edits and
        // cursor restoration after a rebuild.
        private sealed class CategoryRef
        {
            public ItemKind Kind;
            public StyleItemCategoryDef Category;
        }

        private static readonly StyleItemFrequency[] Frequencies =
            (StyleItemFrequency[])System.Enum.GetValues(typeof(StyleItemFrequency));

        #region Lifecycle

        public static void Open(Ideo targetIdeo)
        {
            if (targetIdeo == null) return;
            ideo = targetIdeo;
            IsActive = true;
            EnsureConfigured();
            RebuildTree();
            AnnounceOpening();
        }

        public static void Close()
        {
            if (IsActive)
            {
                // Vanilla guarantees a non-empty style pool on Done; preserve that invariant.
                ideo?.style?.EnsureAtLeastOneStyleItemAvailable();
            }
            IsActive = false;
            ideo = null;
            treeNav.Reset();
        }

        private static void EnsureConfigured()
        {
            if (configured) return;
            configured = true;
            treeNav.AnnounceChildCounts = false;
            treeNav.FormatItemAnnouncement = FormatItem;
            treeNav.FormatStateChangeAnnouncement = FormatStateChange;
            treeNav.FormatSearchAnnouncement = FormatSearch;
            treeNav.OnActivate = HandleActivate;
        }

        #endregion

        #region Tree

        public static void RebuildTree()
        {
            if (ideo?.style == null) return;

            // Remember the focused node so an edit keeps the cursor in place after the rebuild.
            object focusedKey = treeNav.RootItem != null ? treeNav.SelectedItem?.Data : null;

            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category,
            };

            foreach (ItemKind kind in new[] { ItemKind.Hair, ItemKind.Beard, ItemKind.FaceTattoo, ItemKind.BodyTattoo })
            {
                var groupNode = new InspectionTreeItem
                {
                    Label = SectionLabel(kind),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.Category,
                    Data = kind,
                    Parent = root,
                };

                foreach (var category in DefDatabase<StyleItemCategoryDef>.AllDefs)
                {
                    var items = category.ItemsInCategory.Where(s => CanList(s, kind)).ToList();
                    if (items.Count == 0) continue;

                    var catNode = new InspectionTreeItem
                    {
                        Label = category.LabelCap + ", " + items.Count,
                        ExpandedLabel = category.LabelCap,
                        IndentLevel = 1,
                        IsExpandable = true,
                        IsExpanded = false,
                        Type = InspectionTreeItem.ItemType.SubCategory,
                        Data = new CategoryRef { Kind = kind, Category = category },
                        Parent = groupNode,
                    };

                    foreach (var styleItem in items)
                    {
                        catNode.Children.Add(new InspectionTreeItem
                        {
                            Label = StyleItemLabel(styleItem),
                            IndentLevel = 2,
                            IsExpandable = false,
                            Type = InspectionTreeItem.ItemType.Item,
                            Data = styleItem,
                            LinkedDef = styleItem,
                            Parent = catNode,
                        });
                    }
                    groupNode.Children.Add(catNode);
                }
                if (groupNode.Children.Count > 0)
                    root.Children.Add(groupNode);
            }

            treeNav.Initialize(root);
            RestoreSelection(focusedKey);
        }

        private static void RestoreSelection(object focusedKey)
        {
            if (focusedKey == null) return;
            var items = treeNav.VisibleItems;
            for (int i = 0; i < items.Count; i++)
            {
                if (DataMatches(items[i].Data, focusedKey))
                {
                    treeNav.SetSelectedIndex(i);
                    return;
                }
            }
        }

        private static bool DataMatches(object a, object b)
        {
            if (a is StyleItemDef sa && b is StyleItemDef sb) return ReferenceEquals(sa, sb);
            if (a is CategoryRef ca && b is CategoryRef cb) return ca.Kind == cb.Kind && ca.Category == cb.Category;
            if (a is ItemKind ka && b is ItemKind kb) return ka == kb;
            return false;
        }

        #endregion

        #region Labels

        private static bool CanList(StyleItemDef s, ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Hair: return s is HairDef;
                case ItemKind.Beard: return s is BeardDef;
                case ItemKind.FaceTattoo: return s is TattooDef ft && ft.tattooType == TattooType.Face;
                case ItemKind.BodyTattoo: return s is TattooDef bt && bt.tattooType == TattooType.Body;
                default: return false;
            }
        }

        private static string SectionLabel(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Hair: return "Hair".Translate().CapitalizeFirst();
                case ItemKind.Beard: return "Beard".Translate().CapitalizeFirst();
                case ItemKind.FaceTattoo: return "TattooFace".Translate().CapitalizeFirst();
                case ItemKind.BodyTattoo: return "TattooBody".Translate().CapitalizeFirst();
                default: return kind.ToString();
            }
        }

        // Gender is meaningful for hair and tattoos; beards have none (matches the vanilla dialog).
        private static bool GenderApplies(StyleItemDef s) => !(s is BeardDef);

        /// <summary>"{name}, {frequency}[, {gender}]" — the live setting for one style item.</summary>
        private static string StyleItemLabel(StyleItemDef s)
        {
            var sb = new StringBuilder(s.LabelCap);
            sb.Append(", ").Append(ideo.style.GetFrequency(s).GetLabel().CapitalizeFirst());
            if (GenderApplies(s))
                sb.Append(", ").Append(GenderLabel(ideo.style.GetGender(s)));
            return sb.ToString();
        }

        private static string GenderLabel(StyleGender g)
        {
            switch (g)
            {
                case StyleGender.Male:
                case StyleGender.MaleUsually:
                    return Gender.Male.GetLabel();
                case StyleGender.Female:
                case StyleGender.FemaleUsually:
                    return Gender.Female.GetLabel();
                default:
                    return "MaleAndFemale".Translate();
            }
        }

        #endregion

        #region Formatters

        private static string FormatItem(InspectionTreeItem item)
        {
            // Style-item leaves read as just their text — no level/position chatter mid-list.
            if (item.Type == InspectionTreeItem.ItemType.Item)
                return item.Label;

            var sb = new StringBuilder();
            sb.Append(item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                ? item.ExpandedLabel : item.Label);
            if (item.IsExpandable)
                sb.Append(item.IsExpanded ? ", " + (string)"RimWorldAccess.Tree.StateExpanded".Translate() : ", " + (string)"RimWorldAccess.Tree.StateCollapsed".Translate());

            var (pos, total) = treeNav.GetSiblingPosition(item);
            string position = MenuHelper.FormatPosition(pos - 1, total);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);

            string levelSuffix = MenuHelper.GetLevelSuffix("IdeoAppearance", item.IndentLevel);
            if (!string.IsNullOrEmpty(levelSuffix))
                sb.Append(levelSuffix);

            return sb.ToString();
        }

        private static string FormatStateChange(InspectionTreeItem item)
        {
            string state = (item.IsExpanded ? "RimWorldAccess.Tree.StateExpanded" : "RimWorldAccess.Tree.StateCollapsed").Translate().ToString().CapitalizeFirst();
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            return state + ". " + label;
        }

        private static string FormatSearch(InspectionTreeItem item, TypeaheadSearchHelper t)
        {
            string label = !string.IsNullOrEmpty(item.ExpandedLabel) ? item.ExpandedLabel : item.Label;
            return label + t.BuildSearchContextSuffix();
        }

        #endregion

        #region Activation / edit menus

        private static bool HandleActivate(InspectionTreeItem item)
        {
            if (item?.Data is StyleItemDef styleItem)
            {
                OpenFrequencyMenu(styleItem);
                return true;
            }
            return false; // categories / groups fall through to expand/collapse
        }

        /// <summary>] context action: gender for a single style (hair/tattoo), bulk frequency for a category.</summary>
        private static void OpenContextMenu()
        {
            var item = treeNav.SelectedItem;
            if (item?.Data is StyleItemDef styleItem)
            {
                if (GenderApplies(styleItem))
                    OpenGenderMenu(styleItem);
                else
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
            else if (item?.Data is CategoryRef catRef)
            {
                OpenBulkFrequencyMenu(catRef);
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        private static void OpenFrequencyMenu(StyleItemDef styleItem)
        {
            var current = ideo.style.GetFrequency(styleItem);
            var options = new List<FloatMenuOption>();
            foreach (var freq in Frequencies)
            {
                var captured = freq;
                string label = freq.GetLabel().CapitalizeFirst();
                if (freq == current) label += ", current";
                options.Add(new FloatMenuOption(label, () =>
                {
                    ideo.style.SetFrequency(styleItem, captured);
                }));
            }
            TolkHelper.SpeakData(styleItem.LabelCap);
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void OpenGenderMenu(StyleItemDef styleItem)
        {
            var current = ideo.style.GetGender(styleItem);
            var options = new List<FloatMenuOption>
            {
                GenderOption(styleItem, Gender.Male, current),
                GenderOption(styleItem, Gender.Female, current),
                GenderOption(styleItem, Gender.None, current),
            };
            TolkHelper.Speak("Gender".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static FloatMenuOption GenderOption(StyleItemDef styleItem, Gender gender, StyleGender current)
        {
            var target = ToStyleGender(gender);
            string label = gender == Gender.None ? "MaleAndFemale".Translate().ToString() : gender.GetLabel().CapitalizeFirst();
            if (Matches(current, gender)) label += ", current";
            return new FloatMenuOption(label, () => ideo.style.SetGender(styleItem, target));
        }

        private static void OpenBulkFrequencyMenu(CategoryRef catRef)
        {
            var items = catRef.Category.ItemsInCategory.Where(s => CanList(s, catRef.Kind)).ToList();
            if (items.Count == 0) { SoundDefOf.ClickReject.PlayOneShotOnCamera(); return; }

            var options = new List<FloatMenuOption>();
            foreach (var freq in Frequencies)
            {
                var captured = freq;
                options.Add(new FloatMenuOption(freq.GetLabel().CapitalizeFirst(), () =>
                {
                    foreach (var s in items)
                        ideo.style.SetFrequency(s, captured);
                }));
            }
            TolkHelper.SpeakData(catRef.Category.LabelCap);
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static StyleGender ToStyleGender(Gender g)
        {
            switch (g)
            {
                case Gender.Male: return StyleGender.Male;
                case Gender.Female: return StyleGender.Female;
                default: return StyleGender.Any;
            }
        }

        private static bool Matches(StyleGender current, Gender g)
        {
            switch (g)
            {
                case Gender.Male: return current == StyleGender.Male || current == StyleGender.MaleUsually;
                case Gender.Female: return current == StyleGender.Female || current == StyleGender.FemaleUsually;
                default: return current == StyleGender.Any;
            }
        }

        /// <summary>
        /// Called when the player returns from a frequency / gender float menu. A frequency/gender
        /// change only affects the leaf style items' labels (the tree structure and the parent
        /// labels are unchanged), so we refresh those labels in place rather than rebuilding the
        /// tree — that keeps the player's expansion state and cursor exactly where they were.
        /// </summary>
        public static void NotifyReturnedFromPicker()
        {
            if (!IsActive) return;
            RefreshItemLabels(treeNav.RootItem);
            treeNav.ReannounceCurrentItem();
        }

        private static void RefreshItemLabels(InspectionTreeItem node)
        {
            if (node == null) return;
            foreach (var child in node.Children)
            {
                if (child.Data is StyleItemDef styleItem)
                    child.Label = StyleItemLabel(styleItem);
                RefreshItemLabels(child);
            }
        }

        #endregion

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;

            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (treeNav.HasActiveSearch)
                {
                    treeNav.Typeahead.ClearSearchAndAnnounce();
                    treeNav.ReannounceCurrentItem();
                    return true;
                }
                Close();
                SoundDefOf.TabClose.PlayOneShotOnCamera();
                TolkHelper.Speak("CustomizeIdeoligion".Loc());
                return true;
            }

            // ] — context action (gender for a style, bulk frequency for a category).
            if (key == KeyCode.RightBracket && !alt && !ctrl)
            {
                OpenContextMenu();
                return true;
            }

            return treeNav.HandleInput(ev);
        }

        #endregion

        #region Announcement

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append("Appearance".Translate().CapitalizeFirst());
            sb.Append(". ").Append(IdeoBuilderHelper.AppearanceSummary(ideo));
            if (treeNav.Count > 0)
                sb.Append(". ").Append(treeNav.VisibleItems[0].Label);
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
        }

        #endregion
    }
}
