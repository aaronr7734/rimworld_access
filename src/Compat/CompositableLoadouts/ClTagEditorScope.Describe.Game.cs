using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    internal sealed partial class ClTagEditorScope
    {
        private static readonly string[] CategoryKeys =
        {
            "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.CategoryApparel",
            "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.CategoryMelee",
            "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.CategoryRanged",
            "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.CategoryMedicine",
            "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.CategoryItems",
            null,
        };

        private static readonly string[] ShowOnlyKeys =
        {
            "CompositableLoadouts.ShowAllThings", "CompositableLoadouts.ShowResearchedThings",
            "CompositableLoadouts.ShowThingsInColony",
        };

        /// <summary>The icon's tooltip; the mod keys only the prosthetics one.</summary>
        private static string CategoryLabel(int category)
        {
            string key = category >= 0 && category < CategoryKeys.Length ? CategoryKeys[category] : null;
            return key == null ? CompatText.ModText("CompositableLoadouts.Prosthetics") : key.Translate().ToString();
        }

        private string RowLabel(Row row)
        {
            switch (row.Kind)
            {
                case RowKind.SelectTag:
                    return CompatText.ModText("CompositableLoadouts.SelectTag");
                case RowKind.CreateTag:
                    return CompatText.ModText("CompositableLoadouts.CreateNewTag");
                case RowKind.DeleteTag:
                    return CompatText.ModText("CompositableLoadouts.DeleteTag");
                case RowKind.DefaultEnabled:
                    return CompatText.ModText("CompositableLoadouts.EnableTagByDefault").Trim();
                case RowKind.DefaultState:
                case RowKind.PanelState:
                    return CompatText.ModText("CompositableLoadouts.ActiveWhen");
                case RowKind.DefaultCondition:
                case RowKind.PanelCondition:
                    return "RimWorldAccess.Compat.CompositableLoadouts.ConditionRow".Translate();
                case RowKind.Name:
                    return CompatText.ModText("CompositableLoadouts.ChangeTagName");
                case RowKind.Item:
                    return ItemLabel(row.Item);
                case RowKind.Member:
                    return ClCompat.TagEditor.MemberListLabel(row.Member);
                case RowKind.Category:
                    return CategoryLabel(row.Index);
                case RowKind.AlternativesBanner:
                    return CompatText.ModArgs("CompositableLoadouts.AddingAlternativesTo", ClCompat.ItemRepresentativeDef(row.Item)?.LabelCap.ToString() ?? "");
                case RowKind.AlternativesDone:
                    return CompatText.ModText("CompositableLoadouts.Done");
                case RowKind.Search:
                    return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Search".Translate();
                case RowKind.SortBy:
                    return "SortBy".Translate();
                case RowKind.SortOrder:
                    return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.SortOrder".Translate();
                case RowKind.ShowOnly:
                    return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.ShowOnly".Translate();
                case RowKind.Preset:
                    return ClCompat.TagEditor.PresetLabel(row.Preset);
                case RowKind.Thing:
                    return row.Def.LabelCap;
                case RowKind.PanelToggle:
                    return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.PawnList".Translate();
                case RowKind.Pawn:
                    return row.Pawn.LabelShort;
                default:
                    return "";
            }
        }

        /// <summary>The row's own text: a group's "One of:" heading, or the label with "(installed)" for a prosthetic.</summary>
        private static string ItemLabel(object item)
        {
            if (ClCompat.ItemHasAlternatives(item))
            {
                return CompatText.ModText("CompositableLoadouts.OneOfHeading");
            }
            string label = ClCompat.ItemLabel(item);
            return ClCompat.ItemIsProsthetic(item) ? CompatText.ModArgs("CompositableLoadouts.InstalledLabel", label) : label;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            Row row = RowAt(region, index);
            if (row == null)
            {
                return d;
            }
            d.Label = RowLabel(row);
            switch (row.Kind)
            {
                case RowKind.SelectTag:
                case RowKind.CreateTag:
                case RowKind.DeleteTag:
                case RowKind.AlternativesDone:
                    d.Role = ElementRole.Button;
                    break;
                case RowKind.DefaultEnabled:
                    d.Role = ElementRole.Checkbox;
                    d.Check = ClCompat.TagEditor.TagDefaultEnabled(tag) ? CheckState.Checked : CheckState.Unchecked;
                    d.Extras = CompatText.Flatten(CompatText.ModText("CompositableLoadouts.EnableTagByDefaultDesc"));
                    break;
                case RowKind.DefaultState:
                    DescribeStateRow(d, DefaultState());
                    break;
                case RowKind.PanelState:
                    DescribeStateRow(d, ClCompat.ElementState(ClCompat.TagEditor.MassAssignElement(dialog)));
                    break;
                case RowKind.DefaultCondition:
                    DescribeConditionRow(d, DefaultActiveWhenStateActive());
                    break;
                case RowKind.PanelCondition:
                    DescribeConditionRow(d, ClCompat.ElementActiveWhenStateActive(ClCompat.TagEditor.MassAssignElement(dialog)));
                    break;
                case RowKind.Name:
                    DescribeTextRow(d, ClCompat.TagName(tag));
                    break;
                case RowKind.Item:
                    DescribeItemRow(d, row.Item);
                    break;
                case RowKind.Member:
                    DescribeMemberRow(d, row);
                    break;
                case RowKind.Category:
                    d.Role = ElementRole.RadioButton;
                    d.Selected = ClCompat.TagEditor.Category(dialog) == row.Index;
                    break;
                case RowKind.AlternativesBanner:
                    d.ReadOnly = true;
                    break;
                case RowKind.Search:
                    DescribeTextRow(d, ClCompat.TagEditor.DefFilter(dialog));
                    d.Extras = CompatText.Flatten(CompatText.ModText("CompositableLoadouts.SearchBarDesc"));
                    break;
                case RowKind.SortBy:
                    d.Role = ElementRole.ComboBox;
                    d.Value = SortLabel();
                    d.Extras = "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.SortByDesc".Translate().ToString();
                    break;
                case RowKind.SortOrder:
                    d.Role = ElementRole.ComboBox;
                    d.Value = (ClCompat.TagEditor.Ascending(dialog)
                        ? "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Ascending"
                        : "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Descending").Translate();
                    break;
                case RowKind.ShowOnly:
                    d.Role = ElementRole.ComboBox;
                    d.Value = CompatText.ModText(ShowOnlyKeys[ClCompat.TagEditor.ShowOnly(dialog)]);
                    d.Extras = CompatText.Flatten(CompatText.ModText("CompositableLoadouts.ShowOnlyDesc"));
                    break;
                case RowKind.Preset:
                    d.Extras = CompatText.Flatten(CompatText.ModText("CompositableLoadouts.ItemPresetDesc") + "\n\n"
                        + ClCompat.ItemRankedMembersDescription(ClCompat.TagEditor.PresetMakeItem(row.Preset)));
                    break;
                case RowKind.Thing:
                    d.Extras = ThingExtras(row.Def);
                    break;
                case RowKind.PanelToggle:
                    d.Role = ElementRole.Button;
                    d.Value = (ClCompat.TagEditor.PanelCollapsed(dialog)
                        ? "RimWorldAccess.Shell.State.Collapsed"
                        : "RimWorldAccess.Shell.State.Expanded").Translate();
                    break;
                case RowKind.Pawn:
                    d.Role = ElementRole.Checkbox;
                    d.Check = PawnHasTag(row.Pawn) ? CheckState.Checked : CheckState.Unchecked;
                    break;
            }
            return d;
        }

        private static void DescribeTextRow(ElementDescription d, string value)
        {
            d.Role = ElementRole.TextField;
            d.EntersEditOnAccept = true;
            if (string.IsNullOrEmpty(value))
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = value;
            }
        }

        private static void DescribeStateRow(ElementDescription d, object state)
        {
            d.Role = ElementRole.ComboBox;
            d.Value = ClCompat.StateName(state) ?? CompatText.ModText("CompositableLoadouts.DefaultStateNameInUse");
        }

        private static void DescribeConditionRow(ElementDescription d, bool activeWhenStateActive)
        {
            d.Role = ElementRole.Button;
            d.Value = CompatText.ModText(activeWhenStateActive ? "CompositableLoadouts.Active" : "CompositableLoadouts.Inactive");
        }

        /// <summary>The state the default-state dropdown shows: its dummy element's, or before the first draw the tag's own by name.</summary>
        private object DefaultState()
        {
            object element = DefaultStateElement();
            if (element != null)
            {
                return ClCompat.ElementState(element);
            }
            string name = ClCompat.TagEditor.TagDefaultState(tag);
            return ClCompat.AllStates().Find(s => ClCompat.StateName(s) == name);
        }

        private bool DefaultActiveWhenStateActive()
        {
            object element = DefaultStateElement();
            return element != null
                ? ClCompat.ElementActiveWhenStateActive(element)
                : ClCompat.TagEditor.TagDefaultActiveWhenStateActive(tag);
        }

        private void DescribeItemRow(ElementDescription d, object item)
        {
            d.EntersEditOnAccept = true;
            d.Value = "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.QuantityValue".Translate(ClCompat.ItemQuantity(item));
            if (item == addingTo)
            {
                d.Selected = true;
            }
            var extras = new List<string>();
            if (ClCompat.TagEditor.SlotsConflict(prostheticSlots, item, out object blocker))
            {
                extras.Add(blocker == null
                    ? CompatText.ModText("CompositableLoadouts.ProstheticNoRoom")
                    : CompatText.ModArgs("CompositableLoadouts.ProstheticConflict", ClCompat.TagName(blocker)));
            }
            ThingDef def = ClCompat.ItemRepresentativeDef(item);
            if (!ClCompat.ItemHasAlternatives(item) && def != null)
            {
                extras.Add(CompatText.Flatten(def.DescriptionDetailed));
            }
            if (ClCompat.ItemIsProsthetic(item))
            {
                extras.Add(CompatText.ModArgs("CompositableLoadouts.InstalledOn", ClCompat.TagEditor.TargetPartsLabel(def) ?? ""));
            }
            d.Extras = CompatText.JoinSentences(extras);
            d.Hint = ItemHint(item);
        }

        /// <summary>The row's icons, each with its chord and the mod's own tooltip.</summary>
        private string ItemHint(object item)
        {
            var parts = new List<string>
            {
                ChordTip(SharedMenuGrammar.Activate, "CompositableLoadouts.EditQuantity"),
            };
            if (ClCompat.TagEditor.SpecifiableMember(item) != null)
            {
                parts.Add(ChordTip(EditActionId, "CompositableLoadouts.SpecifyElementsToolTip"));
            }
            if (!ClCompat.ItemIsProsthetic(item))
            {
                parts.Add(ChordTip(AddAlternativeActionId, "CompositableLoadouts.AddAlternativeDesc"));
            }
            parts.Add("RimWorldAccess.Compat.CompositableLoadouts.TagEditor.CopyPasteTip".Translate(
                ChordDisplay(CopyActionId) ?? "", ChordDisplay(PasteActionId) ?? "",
                CompatText.Flatten(CompatText.ModText("CompositableLoadouts.CopyPasteExplain"))));
            parts.Add(ChordTip(DeleteActionId, "CompositableLoadouts.RemoveItemFromTag"));
            return CompatText.JoinSentences(parts);
        }

        private static string ChordTip(string actionId, string modTipKey)
        {
            return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.ChordTip".Translate(ChordDisplay(actionId) ?? "", CompatText.Flatten(CompatText.ModText(modTipKey)));
        }

        private void DescribeMemberRow(ElementDescription d, Row row)
        {
            d.Level = 2;
            List<object> members = ClCompat.ItemMembers(row.Item);
            d.PositionIndex = row.Index + 1;
            d.PositionCount = members.Count;
            ThingDef def = ClCompat.MemberDef(row.Member);
            d.Extras = CompatText.JoinSentences(new List<string>
            {
                def == null ? null : CompatText.Flatten(def.DescriptionDetailed),
            });
            // The mod's own text here says to drag; the keyboard reorders with the shared chords.
            d.Hint = CompatText.JoinSentences(new List<string>
            {
                ChordTip(DeleteActionId, "CompositableLoadouts.RemoveAlternative"),
                "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.ReorderAlternativeHint".Translate(
                    ChordDisplay(SharedMenuGrammar.ReorderUp) ?? "", ChordDisplay(SharedMenuGrammar.ReorderDown) ?? "").ToString(),
            });
        }

        private string SortLabel()
        {
            StatDef stat = ClCompat.TagEditor.SelectedStat(dialog);
            string label = stat != null ? stat.LabelCap.ToString() : ClCompat.TagEditor.SelectedEffect(dialog);
            return label ?? "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.SortName".Translate().ToString();
        }

        /// <summary>The stat column, the row's tooltip, and the rows the mouse-over highlight marks.</summary>
        private string ThingExtras(ThingDef def)
        {
            var parts = new List<string>();
            if (ClCompat.TagEditor.SortByStat(dialog)
                && ClCompat.TagEditor.StatCache().TryGetValue(def, out float value)
                && value != ClCompat.TagEditor.InvalidStatValue)
            {
                parts.Add("RimWorldAccess.Compat.CompositableLoadouts.TagEditor.StatValue".Translate(SortLabel(), value.ToString("0.##")));
            }
            bool prosthetic = ClCompat.TagEditor.IsProsthetic(def);
            string tip = def.DescriptionDetailed;
            if (def.IsApparel)
            {
                tip += "\n\n" + CompatText.ModArgs("CompositableLoadouts.ApparelOverlapFlavour", def.LabelCap.ToString());
            }
            else if (prosthetic)
            {
                tip += "\n\n" + CompatText.ModArgs("CompositableLoadouts.InstalledOn", ClCompat.TagEditor.TargetPartsLabel(def) ?? "")
                    + "\n\n" + CompatText.ModArgs("CompositableLoadouts.ProstheticOverlapFlavour", def.LabelCap.ToString());
            }
            parts.Add(CompatText.Flatten(tip));
            if (def.IsApparel || prosthetic)
            {
                List<string> highlighted = ConflictingThings(def);
                if (highlighted.Count > 0)
                {
                    parts.Add("RimWorldAccess.Compat.CompositableLoadouts.TagEditor.Highlighted".Translate(string.Join(", ", highlighted)));
                }
            }
            return CompatText.JoinSentences(parts);
        }

        /// <summary>DrawDefList's hover pass: apparel covering the same layers, or prosthetics on the same parts.</summary>
        private List<string> ConflictingThings(ThingDef hovered)
        {
            var labels = new List<string>();
            object covering = hovered.IsApparel ? ClCompat.TagEditor.ApparelSlot(hovered) : null;
            foreach (ThingDef other in things)
            {
                if (other == hovered)
                {
                    continue;
                }
                bool conflicts = covering != null
                    ? other.IsApparel && ClCompat.TagEditor.SlotsIntersect(covering, ClCompat.TagEditor.ApparelSlot(other))
                    : ClCompat.TagEditor.IsProsthetic(other) && ClCompat.TagEditor.ProstheticsOverlap(hovered, other);
                if (conflicts)
                {
                    labels.Add(other.LabelCap);
                }
            }
            return labels;
        }

        private bool PawnHasTag(Pawn pawn)
        {
            object element = ClCompat.TagEditor.MassAssignElement(dialog);
            foreach (object owned in ClCompat.Elements(ClCompat.LoadoutOf(pawn)))
            {
                if (ClCompat.TagEditor.ElementsEquivalent(owned, element))
                {
                    return true;
                }
            }
            return false;
        }

        private string ThingCountPhrase()
        {
            int count = things.Count;
            if (count == 0)
            {
                return "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.ThingCountNone".Translate();
            }
            return count == 1 ? "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.ThingCountOne".Translate().ToString() : "RimWorldAccess.Compat.CompositableLoadouts.TagEditor.ThingCountMany".Translate(count).ToString();
        }
    }
}
