using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Contents category on transporter buildings (pods,
    /// shuttles, etc.). Shows items still to load and the items already
    /// contained, each as an expandable section.
    /// </summary>
    internal sealed class TransporterContentsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Contents";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Contents expands only for things carrying a transporter comp
        /// (verbatim from InspectionTreeBuilder.IsExpandableCategory, D3).
        /// Non-transporter Contents tabs resolve to the BasicInspectString
        /// fallback via ITab_ContentsBase and never consult this.
        /// </summary>
        public override bool CanExpand(object obj)
        {
            return obj is Thing contentsThing && contentsThing.TryGetComp<CompTransporter>() != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building building) || building.TryGetComp<CompTransporter>() == null)
                return;
            BuildContentsTransporterChildren(categoryItem, building, mode);
        }

        /// <summary>
        /// Builds children for the Contents (Transporter) tab.
        /// Two sections: items to load and contained items.
        /// </summary>
        private static void BuildContentsTransporterChildren(InspectionTreeItem parentItem, Building building, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            var transporter = building.TryGetComp<CompTransporter>();
            if (transporter == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // Items to Load section
            string toLoadHeader = "ItemsToLoad".Translate();
            var toLoadSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = toLoadHeader,
                ExpandedLabel = toLoadHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            toLoadSection.OnActivate = () =>
            {
                if (toLoadSection.Children.Count > 0) return;
                int childIndent = toLoadSection.IndentLevel + 1;

                if (transporter.leftToLoad != null)
                {
                    foreach (var transferable in transporter.leftToLoad)
                    {
                        if (transferable.CountToTransfer <= 0 || !transferable.HasAnyThing)
                            continue;

                        string itemLabel = $"{transferable.ThingDef.LabelCap} x{transferable.CountToTransfer}";
                        InspectNodeFactory.Attach(toLoadSection, new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = itemLabel,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        });
                    }
                }

                if (toLoadSection.Children.Count == 0)
                {
                    InspectNodeFactory.Attach(toLoadSection, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "(" + "NoneLower".Translate() + ")",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
            };
            InspectNodeFactory.Attach(parentItem, toLoadSection);

            // Contained Items section
            string containedHeader = "ContainedItems".Translate();
            var containedSection = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = containedHeader,
                ExpandedLabel = containedHeader,
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false
            };
            containedSection.OnActivate = () =>
            {
                if (containedSection.Children.Count > 0) return;
                int childIndent = containedSection.IndentLevel + 1;

                if (transporter.innerContainer != null && transporter.innerContainer.Count > 0)
                {
                    foreach (var thing in transporter.innerContainer.ToList())
                    {
                        var localThing = thing;
                        string itemLabel = localThing is Pawn p ? p.LabelShortCap : localThing.LabelCap;
                        if (localThing.stackCount > 1)
                            itemLabel += $" x{localThing.stackCount}";

                        var containedItem = new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = itemLabel,
                            Data = localThing,
                            IndentLevel = childIndent,
                            IsExpandable = false
                        };

                        if (!isReadOnly)
                        {
                            containedItem.OnDelete = () =>
                            {
                                // Drop through the tab's own OnDropThing — the
                                // vanilla vehicle, so subclass bookkeeping runs
                                // (ITab_ContentsTransporter also pulls an ejected
                                // pawn out of the loading lord, which the old
                                // hand-rolled SplitOff+Notify pair missed).
                                if (!ContentsTabDrop.TryDrop(building, localThing, localThing.stackCount))
                                    return;
                                TolkHelper.SpeakData(itemLabel);
                                SoundDefOf.Click.PlayOneShotOnCamera();

                                InspectionTreeBuilder.RebuildBranchInPlace(containedSection, () =>
                                {
                                    containedSection.Children.Clear();
                                    containedSection.OnActivate();
                                });
                            };
                        }

                        InspectNodeFactory.Attach(containedSection, containedItem);
                    }
                }

                if (containedSection.Children.Count == 0)
                {
                    InspectNodeFactory.Attach(containedSection, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = "(" + "NoneLower".Translate() + ")",
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
            };
            InspectNodeFactory.Attach(parentItem, containedSection);
        }
    }

    /// <summary>
    /// Adapter for the Contents category on bookcases. Lists the books
    /// currently held, each with an eject-to-adjacent-cell action.
    /// </summary>
    internal sealed class BookcaseContentsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Books";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building_Bookcase bookcase))
                return;
            BuildContentsBooksChildren(categoryItem, bookcase, mode);
        }

        /// <summary>
        /// Builds children for the Contents (Books) tab on bookcases.
        /// Lists books with eject action.
        /// </summary>
        private static void BuildContentsBooksChildren(InspectionTreeItem parentItem, Building_Bookcase bookcase, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            var books = bookcase.GetDirectlyHeldThings()?.OfType<Book>().ToList();
            if (books == null || books.Count == 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "(" + "NoneLower".Translate() + ")",
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            foreach (var book in books)
            {
                var localBook = book;
                // Book.DescriptionDetailed is the generated blurb, title and quality included
                // (Verse/Book.cs:75-81) — the same text ITab_ContentsBooks.DoRow hovers.
                string label = SpeechFlatten.ToSentences(localBook.DescriptionDetailed.StripTags())
                    ?? localBook.LabelCap;

                var bookItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    Data = localBook,
                    IndentLevel = indent,
                    IsExpandable = false
                };

                if (!isReadOnly)
                {
                    bookItem.OnDelete = () =>
                    {
                        // Eject book to adjacent walkable cell
                        IntVec3 dropCell = bookcase.Position;
                        if (bookcase.Spawned)
                        {
                            foreach (var cell in bookcase.OccupiedRect().AdjacentCells)
                            {
                                if (cell.Walkable(bookcase.Map))
                                {
                                    dropCell = cell;
                                    break;
                                }
                            }
                        }

                        bookcase.GetDirectlyHeldThings().TryDrop(localBook, dropCell, bookcase.Map, ThingPlaceMode.Near, 1, out var dropped);
                        if (dropped?.TryGetComp<CompForbiddable>() is CompForbiddable forbiddable)
                            forbiddable.Forbidden = true;

                        TolkHelper.Speak("RimWorldAccess.Inspection.Tree.BookEjected".Loc(
                            "EjectBookTooltip".Translate(), localBook.LabelCap));
                        SoundDefOf.Click.PlayOneShotOnCamera();

                        InspectionTreeBuilder.RebuildBranchInPlace(parentItem, () =>
                        {
                            parentItem.Children.Clear();
                            BuildContentsBooksChildren(parentItem, bookcase, mode);
                        });
                    };
                }

                InspectNodeFactory.Attach(parentItem, bookItem);
            }
        }
    }

    /// <summary>
    /// Adapter for the Contents category on outfit stands. Lists the apparel
    /// held with the detailed description vanilla shows on hover, each with an
    /// eject-to-adjacent-cell action.
    /// </summary>
    internal sealed class OutfitStandContentsAdapter : InspectNodeAdapter
    {
        /// <summary>Its own dispatch key — expansion resolves adapters by category, and the shared "Contents" belongs to the transporter. Displays as the same Contents wording.</summary>
        public override string CategoryKey => "Outfit Stand Contents";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building_OutfitStand stand))
                return;
            BuildContentsApparelChildren(categoryItem, stand, mode);
        }

        private static void BuildContentsApparelChildren(InspectionTreeItem parentItem, Building_OutfitStand stand, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            List<Thing> items = stand.HeldItems?.Where(t => t != null).ToList();
            if (items == null || items.Count == 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "(" + "NoneLower".Translate() + ")",
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            foreach (var thing in items)
            {
                var localThing = thing;
                // ITab_ContentsOutfitStand.DoRow labels the row with LabelCap and hovers
                // DescriptionDetailed, which for apparel carries no label of its own.
                string label = localThing.LabelCap;
                string detail = SpeechFlatten.ToSentences(localThing.DescriptionDetailed.StripTags());
                if (!string.IsNullOrEmpty(detail))
                    label = label + ". " + detail;

                var apparelItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    Data = localThing,
                    IndentLevel = indent,
                    IsExpandable = false
                };

                if (!isReadOnly)
                {
                    apparelItem.OnDelete = () =>
                    {
                        // The row's own drop button, not ITab_ContentsBase.OnDropThing: the
                        // outfit stand's DoRow drops through Building_OutfitStand.TryDrop and
                        // forbids the result, which the base drop does neither of.
                        IntVec3 dropCell = stand.Position;
                        if (stand.Spawned)
                        {
                            foreach (var cell in stand.OccupiedRect().AdjacentCells)
                            {
                                if (cell.Walkable(stand.Map))
                                {
                                    dropCell = cell;
                                    break;
                                }
                            }
                        }

                        if (!stand.TryDrop(localThing, dropCell, ThingPlaceMode.Near, 1, out var dropped))
                            return;
                        if (dropped?.TryGetComp<CompForbiddable>() is CompForbiddable forbiddable)
                            forbiddable.Forbidden = true;

                        TolkHelper.Speak("RimWorldAccess.Inspection.Tree.ApparelEjected".Loc(
                            "EjectApparelTooltip".Translate(), localThing.LabelCap));
                        SoundDefOf.Click.PlayOneShotOnCamera();

                        InspectionTreeBuilder.RebuildBranchInPlace(parentItem, () =>
                        {
                            parentItem.Children.Clear();
                            BuildContentsApparelChildren(parentItem, stand, mode);
                        });
                    };
                }

                InspectNodeFactory.Attach(parentItem, apparelItem);
            }
        }
    }

    /// <summary>
    /// Ejects a contained thing through the holder's own
    /// <c>ITab_ContentsBase.OnDropThing</c> — the vanilla drop vehicle
    /// (the mutation doctrine, CLAUDE.md), so subclass bookkeeping runs exactly
    /// as a mouse drop would: the base drops via SplitOff+TryDropSpawn, and
    /// ITab_ContentsTransporter additionally removes an ejected pawn from the
    /// loading lord and calls Notify_ThingRemoved.
    /// </summary>
    internal static class ContentsTabDrop
    {
        public static bool TryDrop(Thing holder, Thing thing, int count)
        {
            var tab = holder.GetInspectTabs()?.OfType<ITab_ContentsBase>().FirstOrDefault();
            return TryDrop(tab, holder, thing, count);
        }

        public static bool TryDrop(ITab_ContentsBase tab, Thing holder, Thing thing, int count)
        {
            if (tab == null)
            {
                return false;
            }
            // OnDropThing positions the drop off SelThing, which the tab reads
            // from the selector.
            if (Find.Selector.SingleSelectedThing != holder)
            {
                Find.Selector.Select(holder, playSound: false, forceDesignatorDeselect: false);
            }
            HarmonyLib.AccessTools.Method(tab.GetType(), "OnDropThing")
                ?.Invoke(tab, new object[] { thing, count });
            return true;
        }
    }
}
