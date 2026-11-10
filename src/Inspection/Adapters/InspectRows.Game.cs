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
    /// Control rows for tab adapters that mirror a tab's own widgets. Every row re-reads live state
    /// on each announcement, so an edit made elsewhere is what the next announcement speaks. In
    /// <see cref="InspectionMode.ReadOnly"/> the same rows are built, marked read only and inert.
    /// </summary>
    internal static class InspectRows
    {
        /// <summary>A text line with a tooltip, both read live.</summary>
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

        /// <summary>A label drawn beside its value, the label carrying its own colon.</summary>
        public static string StatLine(string label, string value)
        {
            return "RimWorldAccess.Inspection.Row.StatLine".Translate(label, value);
        }

        /// <summary>"Label: value", for a row whose label names what the value is.</summary>
        public static string LabeledValue(string label, string value)
        {
            return "RimWorldAccess.Inspection.Row.LabeledValue".Translate(label, value);
        }

        /// <summary>One choice of a radio strip.</summary>
        internal struct RadioChoice
        {
            public string Label;
            public Func<bool> IsSelected;
            public Action Select;
        }

        /// <summary>
        /// A strip of mutually exclusive cells as a section whose collapsed label carries the current
        /// choice and whose children are one radio row per cell, in the strip's order. The choices
        /// are re-read each time the section is built.
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

        /// <summary>A checkbox playing vanilla's toggle sound, as Widgets.Checkbox does.</summary>
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

        /// <summary>A button. A disabled one keeps the row navigable, and has no click target.</summary>
        public static InspectionTreeItem Button(InspectionTreeItem parent, string label, Func<string> extras,
            Action onActivate, InspectionMode mode, bool disabled = false)
        {
            bool inert = mode == InspectionMode.ReadOnly || disabled;
            InspectionTreeItem item = InspectNodeFactory.ActionRow(parent, label, null, inert ? null : onActivate);
            item.DescribeElement = () => new ElementDescription
            {
                Label = label,
                Role = ElementRole.Button,
                Disabled = disabled,
                ReadOnly = mode == InspectionMode.ReadOnly,
                Extras = CompatText.Flatten(extras == null ? null : extras()),
            };
            return item;
        }

        /// <summary>A picker whose Enter opens a windowless menu of the options the control offers.</summary>
        public static InspectionTreeItem Combo(InspectionTreeItem parent, string label, Func<string> value,
            Func<string> tip, Func<List<FloatMenuOption>> options, InspectionMode mode)
        {
            bool readOnly = mode == InspectionMode.ReadOnly;
            // The composite label is the search text and what the capture parity diff matches the
            // control's own caption (the value) against; speech uses the structured datum.
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
        /// A Left/Right slider over [min, max] in fixed steps, mirroring a Widgets.HorizontalSlider
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
            item.DescribeElement = () =>
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
