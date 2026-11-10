using System;
using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree rows shared by the hospitality suite's tab adapters (guest, restaurant, store).
    /// Every row re-reads live state on each announcement, so an edit made by another row, a dialog
    /// or the mod itself is what the next announcement speaks. Rows that write are built only in
    /// <see cref="InspectionMode.Full"/>; read-only inspection gets the same rows marked read only.
    /// </summary>
    internal static class HotelTreeRows
    {
        /// <summary>A text line with the tooltip the mod attaches to it, both read live.</summary>
        public static InspectionTreeItem Info(InspectionTreeItem parent, Func<string> label, Func<string> tip = null)
        {
            InspectionTreeItem item = InspectNodeFactory.LiveDetailLine(parent, label);
            if (tip != null)
            {
                item.DescribeElement = () => new ElementDescription
                {
                    Label = CompatText.Flatten(label()),
                    Extras = CompatText.Flatten(tip()),
                };
            }
            return item;
        }

        /// <summary>A Listing_Standard.LabelDouble row: the mod's own label key beside its value.</summary>
        public static string StatLine(string labelKey, string value)
        {
            return "RimWorldAccess.Compat.CashRegister.StatLine".Translate(CompatText.ModText(labelKey), value);
        }

        /// <summary>"Label: value", for a row whose label names what the value is.</summary>
        public static string LabeledValue(string label, string value)
        {
            return "RimWorldAccess.Compat.CashRegister.LabeledValue".Translate(label, value);
        }

        /// <summary>One choice of a radio strip.</summary>
        internal struct RadioChoice
        {
            public string Label;
            public Func<bool> IsSelected;
            public Action Select;
        }

        /// <summary>
        /// A mod's strip of mutually exclusive cells (an area selector) as a section whose collapsed
        /// label carries the current choice and whose children are one radio row per cell, in the
        /// strip's order. The choices are re-read each time the section is built.
        /// </summary>
        public static InspectionTreeItem RadioGroup(InspectionTreeItem parent, string label, Func<string> value,
            Func<string> tip, Func<List<RadioChoice>> choices, InspectionMode mode)
        {
            Func<string> composite = () => LabeledValue(label, value());
            InspectionTreeItem section = InspectNodeFactory.LiveSection(parent, composite, null, sectionItem =>
            {
                List<RadioChoice> list = choices();
                for (int i = 0; i < list.Count; i++)
                {
                    RadioChoice choice = list[i];
                    int position = i + 1;
                    InspectionTreeItem row = InspectNodeFactory.ActionRow(sectionItem, choice.Label, null,
                        mode == InspectionMode.ReadOnly ? null : choice.Select);
                    row.DescribeElement = () => new ElementDescription
                    {
                        Label = choice.Label,
                        Role = ElementRole.RadioButton,
                        Selected = choice.IsSelected(),
                        ReadOnly = mode == InspectionMode.ReadOnly,
                        PositionIndex = position,
                        PositionCount = list.Count,
                    };
                }
            }, label);
            if (tip != null)
            {
                section.DescribeElement = () => new ElementDescription
                {
                    Label = section.IsExpanded ? label : composite(),
                    Extras = CompatText.Flatten(tip()),
                };
            }
            return section;
        }

        /// <summary>A checkbox; the mod's own widget plays its toggle sound, so this does too.</summary>
        public static InspectionTreeItem Checkbox(InspectionTreeItem parent, string label, Func<string> tip,
            Func<bool> get, Action<bool> set, InspectionMode mode)
        {
            bool readOnly = mode == InspectionMode.ReadOnly;
            InspectionTreeItem item = InspectNodeFactory.ActionRow(parent, label, null, null);
            item.DescribeElement = () => new ElementDescription
            {
                Label = label,
                Role = ElementRole.Checkbox,
                Check = get() ? CheckState.Checked : CheckState.Unchecked,
                ReadOnly = readOnly,
                Extras = tip == null ? null : CompatText.Flatten(tip()),
            };
            if (!readOnly)
            {
                item.OnActivate = () =>
                {
                    bool next = !get();
                    set(next);
                    (next ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
                };
            }
            return item;
        }

        /// <summary>A button. A refusal with no click target keeps the row navigable and says why.</summary>
        public static InspectionTreeItem Button(InspectionTreeItem parent, string label, Func<string> tip,
            Action onActivate, InspectionMode mode, string disabledReason = null)
        {
            bool inert = mode == InspectionMode.ReadOnly || disabledReason != null;
            InspectionTreeItem item = InspectNodeFactory.ActionRow(parent, label, null, inert ? null : onActivate);
            item.DescribeElement = () =>
            {
                var parts = new List<string> { disabledReason, tip == null ? null : tip() };
                return new ElementDescription
                {
                    Label = label,
                    Role = ElementRole.Button,
                    Disabled = disabledReason != null,
                    ReadOnly = mode == InspectionMode.ReadOnly,
                    Extras = CompatText.Flatten(CompatText.JoinSentences(parts)),
                };
            };
            return item;
        }

        /// <summary>A picker whose Enter opens a windowless menu of the options the mod's own control offers.</summary>
        public static InspectionTreeItem Combo(InspectionTreeItem parent, string label, Func<string> value,
            Func<string> tip, Func<List<FloatMenuOption>> options, InspectionMode mode)
        {
            bool readOnly = mode == InspectionMode.ReadOnly;
            // The composite label is the search text and what the capture parity diff matches the
            // mod's own button caption (the value) against; speech uses the structured datum.
            Func<string> composite = () => LabeledValue(label, value());
            InspectionTreeItem item = InspectNodeFactory.ActionRow(parent, composite(), null, null, opensOverlayMenu: !readOnly);
            item.LabelProvider = composite;
            item.DescribeElement = () => new ElementDescription
            {
                Label = label,
                Role = ElementRole.ComboBox,
                Value = value(),
                ReadOnly = readOnly,
                Extras = tip == null ? null : CompatText.Flatten(tip()),
            };
            if (!readOnly)
            {
                item.OnActivate = () => WindowlessFloatMenuState.Open(options(), colonistOrders: false);
            }
            return item;
        }

        /// <summary>
        /// A Left/Right slider over [min, max] in fixed steps, mirroring a mod's Widgets.HorizontalSlider
        /// whose label carries the value ("Price for guests: 120%"), so the label is the value.
        /// Driven here rather than through the widget, so a slider-replacing mod never sees the edit.
        /// </summary>
        public static InspectionTreeItem Slider(InspectionTreeItem parent, Func<string> label,
            Func<string> tip, Func<float> get, Action<float> set, float min, float max, float step, InspectionMode mode)
        {
            bool readOnly = mode == InspectionMode.ReadOnly;
            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = label(),
                LabelProvider = label,
                IndentLevel = parent.IndentLevel + 1,
            };
            Func<ElementDescription> describe = () =>
            {
                float current = get();
                return new ElementDescription
                {
                    Label = label(),
                    Role = ElementRole.Slider,
                    AtMinimum = current <= min,
                    AtMaximum = current >= max,
                    ReadOnly = readOnly,
                    Extras = tip == null ? null : CompatText.Flatten(tip()),
                };
            };
            item.DescribeElement = describe;
            if (!readOnly)
            {
                item.OnAdjust = direction =>
                {
                    float current = get();
                    float next = Mathf.Clamp(GenMath.RoundTo(current + direction * step, step), min, max);
                    if (Mathf.Approximately(next, current))
                    {
                        NumericStepperHelper.SpeakBoundary(direction);
                        return true;
                    }
                    set(next);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    TolkHelper.SpeakData(label());
                    return true;
                };
            }
            InspectNodeFactory.Attach(parent, item);
            return item;
        }
    }
}
