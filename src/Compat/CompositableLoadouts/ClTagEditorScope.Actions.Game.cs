using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class ClTagEditorScope
    {
        // Dialog_TagEditor.cs:380 passes this literal as every non-prosthetic quantity's cap.
        private const float UncappedQuantity = 1E+09f;

        /// <summary>Right expands and Left collapses the mass-assign panel, as a tree node does.</summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            Row row = RowAt(region, index);
            return row != null && row.Kind == RowKind.PanelToggle;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (ClCompat.TagEditor.PanelCollapsed(dialog) == direction > 0)
            {
                ClCompat.TagEditor.TogglePanel(dialog);
            }
            AnnounceStateChange();
        }

        protected override void ActivateContentItem(int region, int index)
        {
            Row row = RowAt(region, index);
            if (row == null)
            {
                return;
            }
            switch (row.Kind)
            {
                case RowKind.SelectTag:
                    SelectTag();
                    break;
                case RowKind.CreateTag:
                    CreateTag();
                    break;
                case RowKind.DeleteTag:
                    DeleteTag();
                    break;
                case RowKind.DefaultEnabled:
                    ClCompat.TagEditor.SetTagDefaultEnabled(tag, !ClCompat.TagEditor.TagDefaultEnabled(tag));
                    AnnounceStateChange();
                    break;
                case RowKind.DefaultState:
                    OpenStateMenu(DefaultStateElement());
                    break;
                case RowKind.PanelState:
                    OpenStateMenu(ClCompat.TagEditor.MassAssignElement(dialog));
                    break;
                case RowKind.DefaultCondition:
                    FlipCondition(DefaultStateElement());
                    break;
                case RowKind.PanelCondition:
                    FlipCondition(ClCompat.TagEditor.MassAssignElement(dialog));
                    break;
                case RowKind.Name:
                    object named = tag;
                    edit.EnterEdit(ClCompat.TagName(named), FreeTextSpec, RowLabel(row),
                        value => ClCompat.TagEditor.SetTagName(named, value), AnnounceCurrentItem);
                    break;
                case RowKind.Item:
                    BeginQuantityEdit(row.Item);
                    break;
                case RowKind.Category:
                    ClCompat.TagEditor.SelectCategory(dialog, row.Index);
                    RefreshModel();
                    TolkHelper.SpeakData(CompatText.JoinSentences(new List<string> { RowLabel(row), ThingCountPhrase() }));
                    break;
                case RowKind.AlternativesDone:
                    ClCompat.TagEditor.SetAddingAlternativesTo(dialog, null);
                    pendingPrefix = "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.AlternativesDone".Translate();
                    RefreshModel();
                    AnnounceCurrentItem();
                    break;
                case RowKind.Search:
                    edit.EnterEdit(ClCompat.TagEditor.DefFilter(dialog), FreeTextSpec, RowLabel(row),
                        value => ClCompat.TagEditor.SetDefFilter(dialog, value),
                        () =>
                        {
                            RefreshModel();
                            // The search filters only the selected category, so name it with the count.
                            pendingPrefix = CompatText.JoinSentences(new List<string> { ContentRegionName(ThingsRegion), ThingCountPhrase() });
                        });
                    break;
                case RowKind.SortBy:
                    OpenSortMenu();
                    break;
                case RowKind.SortOrder:
                    ClCompat.TagEditor.SetAscending(dialog, !ClCompat.TagEditor.Ascending(dialog));
                    AnnounceStateChange();
                    break;
                case RowKind.ShowOnly:
                    OpenShowOnlyMenu();
                    break;
                case RowKind.Preset:
                    AddPreset(row.Preset);
                    break;
                case RowKind.Thing:
                    AddThing(row.Def);
                    break;
                case RowKind.PanelToggle:
                    ClCompat.TagEditor.TogglePanel(dialog);
                    AnnounceStateChange();
                    break;
                case RowKind.Pawn:
                    TogglePawn(row.Pawn);
                    break;
            }
        }

        private void AnnounceStateChange()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region != null && !region.IsEmpty)
            {
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                    DescribeContentItem(Model.RegionIndex, region.Index), TranslatedShellVocabulary.Instance));
            }
        }

        // ---- The tag controls ----

        private void SelectTag()
        {
            object current = tag;
            ClCompat.OpenTagSelector(ClCompat.AllTags().Where(t => t != current), picked =>
            {
                ClCompat.TagEditor.SetCurTag(dialog, picked);
                ClCompat.TagEditor.AfterTagSelected(dialog, picked);
                RefreshModel();
                LandCursorAt(TagRegion, RowIndexOf(TagRegion, RowKind.Name), announce: false);
            }, false, null);
        }

        private void CreateTag()
        {
            object created = ClCompat.NewTag(string.Empty);
            // MUTATION-C: mirrors Dialog_TagEditor.cs:244-245; the Create New Tag body is inline.
            if (created == null || !ClCompat.ManagerAddTag(created))
            {
                return;
            }
            ClCompat.TagEditor.SetCurTag(dialog, created);
            RefreshModel();
            pendingPrefix = "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.TagCreated".Translate(ClCompat.TagName(created));
            LandCursorAt(TagRegion, RowIndexOf(TagRegion, RowKind.Name));
        }

        /// <summary>The mod's own confirmation, worded as it composes it; the delete runs only on confirm.</summary>
        private void DeleteTag()
        {
            object doomed = tag;
            string name = ClCompat.TagName(doomed);
            string prompt = CompatText.ModText("CompositableLoadouts.DeleteTag") + " " + name; // l10n-exempt: Dialog_TagEditor.cs:249's own composition, shown verbatim
            Find.WindowStack.Add(new Dialog_Confirm(prompt, () =>
            {
                // MUTATION-C: mirrors Dialog_TagEditor.cs:250-251; the confirm callback is a closure built inside the click.
                ClCompat.ManagerRemoveTag(doomed);
                ClCompat.TagEditor.SetCurTag(dialog, null);
                RefreshModel();
                pendingPrefix = "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.TagDeleted".Translate(name);
                LandCursorAt(TagRegion, 0, announce: false);
            }));
        }

        private static void OpenStateMenu(object element)
        {
            if (element == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            DropdownColumnHandler.OpenGeneratedMenu(ClCompat.Dialogs.PopupStateGenerator, null, element);
        }

        private void FlipCondition(object element)
        {
            if (element == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            // MUTATION-C: mirrors Dialog_SetTagLoadoutState.cs:131-136; the Active/Inactive button body is inline in Draw.
            ClCompat.SetElementState(element, ClCompat.ElementState(element), !ClCompat.ElementActiveWhenStateActive(element));
            AnnounceStateChange();
        }

        // ---- Required items ----

        private void BeginQuantityEdit(object item)
        {
            ThingDef def = ClCompat.ItemRepresentativeDef(item);
            int max = ClCompat.ItemIsProsthetic(item) ? ClCompat.TagEditor.MaxInstallable(def) : (int)UncappedQuantity;
            string seed = ClCompat.TagEditor.QuantityBuffer(item) ?? ClCompat.ItemQuantity(item).ToString();
            edit.EnterEdit(seed, TextFieldSpec.WholeNumber("RimWorldAccess.TextInput.LabelDefault"),
                "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Quantity".Translate(),
                value => ClCompat.TagEditor.SetQuantityText(item, value, max), AnnounceCurrentItem);
        }

        private void RemoveCurrent()
        {
            Row row = CurrentRow();
            if (row == null)
            {
                return;
            }
            string label = RowLabel(row);
            if (row.Kind == RowKind.Item)
            {
                // MUTATION-C: mirrors Dialog_TagEditor.cs:347; the remove icon's body is inline.
                ClCompat.TagEditor.ItemsList(tag)?.Remove(row.Item);
            }
            else
            {
                // MUTATION-C: mirrors Dialog_TagEditor.cs:450; the remove icon's body is inline.
                ClCompat.TagEditor.MembersList(row.Item)?.RemoveAt(row.Index);
            }
            RefreshModel();
            pendingPrefix = "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Removed".Translate(label);
            if (Model.CurrentRegionIsEmpty)
            {
                TolkHelper.SpeakData(pendingPrefix);
                pendingPrefix = null;
                return;
            }
            AnnounceCurrentItem();
        }

        private void CopyCurrent()
        {
            Row row = CurrentRow();
            ClCompat.TagEditor.CopyItem(row.Item);
            TolkHelper.SpeakData("RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Copied".Translate(RowLabel(row)));
        }

        private void PasteCurrent()
        {
            Row row = CurrentRow();
            object copied = ClCompat.TagEditor.CopiedItem();
            if (copied == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.Compat.CompositableLoadouts.TagEditor.NothingCopied".Translate());
                return;
            }
            ClCompat.TagEditor.PasteFilter(copied, row.Item);
            RefreshModel();
            TolkHelper.SpeakData("RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Pasted".Translate(RowLabel(row)));
        }

        private void EditCurrent()
        {
            Row row = CurrentRow();
            ClCompat.OpenItemSpecifier(row.Item, ClCompat.TagEditor.SpecifiableMember(row.Item));
        }

        private void BeginAlternatives()
        {
            Row row = CurrentRow();
            ClCompat.TagEditor.SetAddingAlternativesTo(dialog, row.Item);
            RefreshModel();
            Row banner = rows[BrowserRegion].Find(r => r.Kind == RowKind.AlternativesBanner);
            TolkHelper.SpeakData(banner == null ? "" : RowLabel(banner));
        }

        private void OpenInfoCard()
        {
            Row row = CurrentRow();
            ThingDef def = InfoTarget(row);
            if (def == null)
            {
                return;
            }
            ThingDef stuff = row.Kind == RowKind.Item
                ? ClCompat.TagEditor.ItemRepresentativeStuff(row.Item)
                : def.MadeFromStuff ? GenStuff.AllowedStuffsFor(def).First() : null;
            Find.WindowStack.Add(new Dialog_InfoCard(def, stuff));
        }

        /// <summary>The def behind a row's info icon: a required item's best alternative, or a def-list row.</summary>
        private static ThingDef InfoTarget(Row row)
        {
            if (row == null)
            {
                return null;
            }
            if (row.Kind == RowKind.Item)
            {
                return ClCompat.ItemRepresentativeDef(row.Item);
            }
            return row.Kind == RowKind.Thing ? row.Def : null;
        }

        protected override bool CanReorderContentItem(int region, int index, int direction)
        {
            Row row = RowAt(region, index);
            if (region != ItemsRegion || row == null)
            {
                return false;
            }
            int target = row.Index + direction;
            int count = row.Kind == RowKind.Item
                ? ClCompat.TagItems(tag).Count
                : ClCompat.ItemMembers(row.Item).Count;
            return target >= 0 && target < count;
        }

        protected override int ReorderContentItem(int region, int index, int direction)
        {
            Row row = RowAt(region, index);
            IList list = row.Kind == RowKind.Item ? ClCompat.TagEditor.ItemsList(tag) : ClCompat.TagEditor.MembersList(row.Item);
            object moved = row.Kind == RowKind.Item ? row.Item : row.Member;
            int target = row.Index + direction;
            if (list == null || target < 0 || target >= list.Count)
            {
                return -1;
            }
            // MUTATION-C: mirrors Dialog_TagEditor.cs:482-485 and 536-540; the drag's neighbour swap is inline in DraggableMembers/DraggableItems.
            list[row.Index] = list[target];
            list[target] = moved;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            RefreshContent();
            return rows[ItemsRegion].FindIndex(r => (r.Kind == RowKind.Item ? r.Item : r.Member) == moved
                && r.Kind == row.Kind);
        }

        // ---- The def browser ----

        private void OpenShowOnlyMenu()
        {
            var options = new List<FloatMenuOption>();
            // The colony's things are only known in a game.
            int modes = Current.Game != null ? ShowOnlyKeys.Length : ShowOnlyKeys.Length - 1;
            for (int i = 0; i < modes; i++)
            {
                int mode = i;
                options.Add(new FloatMenuOption(CompatText.ModText(ShowOnlyKeys[i]), () =>
                {
                    ClCompat.TagEditor.SetShowOnly(dialog, mode);
                    RefreshModel();
                    pendingPrefix = ThingCountPhrase();
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>The sort icon's right-click menu: Name, then every stat and prosthetic effect of the listed things by label.</summary>
        private void OpenSortMenu()
        {
            int category = ClCompat.TagEditor.Category(dialog);
            var stats = new HashSet<StatDef>();
            foreach (ThingDef def in things)
            {
                stats.AddRange((def.statBases?.Select(sm => sm.stat) ?? Enumerable.Empty<StatDef>())
                    .Union(def.equippedStatOffsets?.Select(sm => sm.stat) ?? new List<StatDef>()));
            }
            stats.AddRange(ClCompat.TagEditor.CategoryExtraStats(category));

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.Compat.CompositableLoadouts.TagEditor.SortName".Translate(), () => ApplySort(false, null, null)),
            };
            var statOptions = new List<FloatMenuOption>();
            foreach (StatDef stat in stats)
            {
                StatDef picked = stat;
                statOptions.Add(new FloatMenuOption(stat.LabelCap, () => ApplySort(true, picked, null)));
            }
            var effects = new HashSet<string>(things.SelectMany(def => ClCompat.TagEditor.EffectsOf(def).Select(e => e.Key)));
            foreach (string effect in effects)
            {
                string picked = effect;
                statOptions.Add(new FloatMenuOption(effect, () => ApplySort(true, null, picked)));
            }
            options.AddRange(statOptions.OrderBy(o => o.Label));
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>The sort options' bodies, including the stat cache each stat or effect pick fills over the whole category.</summary>
        private void ApplySort(bool byStat, StatDef stat, string effect)
        {
            ClCompat.TagEditor.SetSort(dialog, byStat, stat, effect);
            if (byStat)
            {
                Dictionary<ThingDef, float> cache = ClCompat.TagEditor.StatCache();
                float invalid = ClCompat.TagEditor.InvalidStatValue;
                foreach (ThingDef def in ClCompat.TagEditor.CategoryDefs(ClCompat.TagEditor.Category(dialog)))
                {
                    // MUTATION-C: mirrors Dialog_TagEditor.cs:784-805; the cache fill is inside each option's closure.
                    cache[def] = stat != null ? StatValue(def, stat, invalid) : EffectValue(def, effect, invalid);
                }
            }
            RefreshModel();
        }

        private static float StatValue(ThingDef def, StatDef stat, float invalid)
        {
            ThingDef stuff = GenStuff.AllowedStuffsFor(def).FirstOrDefault();
            List<StatDrawEntry> entries = def.SpecialDisplayStats(StatRequest.For(def, stuff)).ToList();
            entries.AddRange(ClCompat.TagEditor.StatsToDraw(def, stuff).Where(r => r.ShouldDisplay()));
            StatDrawEntry relevant = entries.FirstOrDefault(s => s.stat == stat);
            return relevant != null ? ClCompat.TagEditor.StatEntryValue(relevant) : invalid;
        }

        private static float EffectValue(ThingDef def, string effect, float invalid)
        {
            foreach (KeyValuePair<string, float> pair in ClCompat.TagEditor.EffectsOf(def))
            {
                if (pair.Key == effect)
                {
                    return pair.Value;
                }
            }
            return invalid;
        }

        private void AddPreset(object preset)
        {
            object item = ClCompat.TagEditor.PresetMakeItem(preset);
            IList items = ClCompat.TagEditor.ItemsList(tag);
            if (item == null || items == null)
            {
                return;
            }
            // MUTATION-C: mirrors Dialog_TagEditor.cs:1003; the pinned row's body is inline.
            items.Add(item);
            pendingPrefix = "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Added".Translate(ClCompat.TagEditor.PresetLabel(preset));
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void AddThing(ThingDef def)
        {
            object target = addingTo;
            ClCompat.TagEditor.AddDefToTag(dialog, def);
            pendingPrefix = target == null
                ? "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Added".Translate(def.LabelCap).ToString()
                : "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.AddedAlternative".Translate(def.LabelCap, ClCompat.ItemRepresentativeDef(target)?.LabelCap.ToString() ?? "").ToString();
            RefreshModel();
            if (Model.CurrentRegionIsEmpty)
            {
                TolkHelper.SpeakData(pendingPrefix);
                pendingPrefix = null;
                return;
            }
            AnnounceCurrentItem();
        }

        // ---- Mass assign ----

        private void TogglePawn(Pawn pawn)
        {
            object element = ClCompat.TagEditor.MassAssignElement(dialog);
            // MUTATION-C: mirrors Dialog_TagEditor.cs:216-221; the checkbox's body is inline in DrawPawnList.
            if (PawnHasTag(pawn))
            {
                object owned = ClCompat.Elements(ClCompat.LoadoutOf(pawn)).FirstOrDefault(e => ClCompat.ElementTag(e) == tag);
                if (owned != null)
                {
                    ClCompat.RemoveTag(pawn, owned);
                }
            }
            else
            {
                ClCompat.AddTag(pawn, tag, ClCompat.ElementState(element), ClCompat.ElementActiveWhenStateActive(element), true);
            }
            AnnounceStateChange();
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("CloseButton".Translate().ToString(), () => dialog.Close(),
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }
    }
}
