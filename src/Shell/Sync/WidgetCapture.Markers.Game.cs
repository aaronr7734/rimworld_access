using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public static partial class WidgetCapture
    {
        internal static int ExitCustomButtonAndRecord(Rect rect, string label, bool disabled)
        {
            ExitSelfCaptioned();
            if (!passOpen)
            {
                return -1;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            MaybeArmMatch(WidgetKind.Button, label ?? "", index);
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Button,
                Label = label ?? "",
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                Disabled = disabled,
            });
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        /// <summary>Widgets.DrawOptionBackground, from its tap's postfix. A marker, not a row: the invisible button over the box is the click target; Selected is what tab-strip promotion reads.</summary>
        internal static void RecordOptionBackground(Rect rect, bool selected)
        {
            if (!passOpen || (!detachedPass && ClipUnseen()))
            {
                return;
            }
            optionBackgroundMarkers.Add(new OptionBackgroundMarker
            {
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                Selected = selected,
            });
        }

        /// <summary>Listing.NewColumn, from its tap's postfix: rows recorded from here on belong to the next column.</summary>
        internal static void RecordListingColumnBreak()
        {
            if (!passOpen)
            {
                return;
            }
            listingColumnBreaks.Add(CurrentSink.Count);
        }

        // Composite-widget vocabulary: the ButtonInvisible/CheckboxMulti/CheckTexMarker/GapLine taps.

        /// <summary>
        /// Widgets.ButtonInvisible, from its prefix. Suppressed (returns -1) inside a widget that
        /// already draws its own caption/control row — the self-caption, tab-strip and
        /// checkbox-multi brackets — since recording there would double-count a click target that
        /// row already carries. Every other caller records; composite callers are stamped with their
        /// <see cref="CompositeMember"/> role. This layer never decides fusion versus orphan: it
        /// records whenever unsuppressed and leaves that to the owning scope's presentation pass.
        /// </summary>
        internal static int RecordInvisibleButton(Rect rect)
        {
            if (!passOpen || selfCaptionDepth > 0 || tabStripDepth > 0 || checkboxMultiDepth > 0)
            {
                return -1;
            }
            if (pendingTipTextureValid && pendingTipTextureRect == rect && pendingTipTextureClip.Equals(GuiSpace.CurrentClip())
                && CurrentSink.Count == pendingTipTextureSinkIndex)
            {
                // Third leg of the TipRegion, DrawTexture, ButtonInvisible shape on one rect, whose
                // texture RecordCheckTexMarker deferred rather than mint a placeholder row.
                // Recording straight through RecordButton keeps this row's kind and label identity
                // stable from the first pass it exists in, which is what the activation channels
                // match by; a post-hoc rename would strand a pending request forever.
                pendingTipTextureValid = false;
                string tipLabel = pendingTipTextureResolve();
                if (!string.IsNullOrEmpty(tipLabel))
                {
                    return RecordButton(rect, tipLabel, false);
                }
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            // A color swatch is the one hotspot here with a name of its own, and it must be
            // resolved BEFORE the descriptor match: the live channel counts by (kind, label,
            // ordinal), so naming the row afterwards would address a label no tap ever counts.
            string label = colorBoxDepth > 0 ? ColorSwatchLabel(colorBoxColor) : "";
            MedicalCareCategory medicalCare = (MedicalCareCategory)medicalCareOrdinal;
            if (medicalCareDepth > 0)
            {
                label = MedicalCareChoiceLabel(medicalCare);
                medicalCareOrdinal++;
            }
            MaybeLiveActivateMatch(WidgetKind.InvisibleButton, label, index);
            CapturedWidget row = new CapturedWidget { Kind = WidgetKind.InvisibleButton, Label = label, Rect = rect, ScreenRect = GuiSpace.ToScreen(rect), VisibleScreenRect = GuiSpace.VisibleScreenRect(rect), Clip = GuiSpace.CurrentClip(), DropdownOpener = dropdownDepth > 0 };
            if (colorBoxDepth > 0)
            {
                // Widgets.ColorBox's own hotspot: its click assigns the caller's ref Color with no
                // gate at all (Verse/Widgets.cs:2937-2942), so the row is already the live control;
                // only its identity was missing, vanilla drawing a swatch as a filled square.
                row.Composite = CompositeMember.ColorSwatch;
                row.Selected = colorBoxSelected;
            }
            else if (medicalCareDepth > 0)
            {
                row.Composite = CompositeMember.MedicalCareChoice;
                row.Selected = medicalCare == medicalCareCurrent;
            }
            else if (hyperlinkDepth > 0)
            {
                row.Composite = CompositeMember.HyperlinkTarget;
            }
            else if (selectableRowDepth > 0)
            {
                // Draw order decides which target this is: vanilla draws the row-select hotspot
                // first and only while unselected, then always the check toggle
                // (Verse/Widgets.cs:1287-1296).
                row.Composite = (!selectableRowSelected && selectableRowOrdinal == 0)
                    ? CompositeMember.SelectableRowSelect
                    : CompositeMember.SelectableRowCheck;
                selectableRowOrdinal++;
            }
            else if (selectableDefDepth > 0)
            {
                row.Composite = CompositeMember.SelectableDefRow;
            }
            sink.Add(row);
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        /// <summary>Widgets.CheckboxMulti's self-caption-style bracket — see <see cref="RecordInvisibleButton"/>.</summary>
        internal static void EnterCheckboxMulti()
        {
            checkboxMultiDepth++;
        }

        internal static void ExitCheckboxMulti()
        {
            if (checkboxMultiDepth > 0)
            {
                checkboxMultiDepth--;
            }
        }

        /// <summary>
        /// Start of a Widgets.Dropdown body: the opener button it draws records with DropdownOpener
        /// set. The guarded decrement tolerates an imbalance from a thrown menuGenerator, and the
        /// pass reset clears any single-pass leak — the shape every bracket here follows.
        /// </summary>
        internal static void EnterDropdown()
        {
            dropdownDepth++;
        }

        internal static void ExitDropdown()
        {
            if (dropdownDepth > 0)
            {
                dropdownDepth--;
            }
        }

        /// <summary>Listing_Standard.ButtonTextLabeledPct's bracket — see labeledButtonDepth.</summary>
        internal static void EnterLabeledButton()
        {
            labeledButtonDepth++;
        }

        internal static void ExitLabeledButton()
        {
            if (labeledButtonDepth > 0)
            {
                labeledButtonDepth--;
            }
        }

        // Numeric text-field family brackets.

        /// <summary>
        /// Start of a Widgets.TextFieldNumeric&lt;int|float&gt; body: stashes the instantiation's
        /// int-ness and the call site's min/max for the inner TextField's row. Inside a
        /// TextFieldVector it also names the field with the next axis in draw order (x, y, z).
        /// </summary>
        internal static void EnterNumericField(bool isInt, float min, float max)
        {
            numericFieldDepth++;
            numericFieldIsInt = isInt;
            numericFieldMin = min;
            numericFieldMax = max;
            if (vectorFieldDepth > 0)
            {
                vectorAxisLabel = VectorAxisName(vectorAxisIndex);
                vectorAxisIndex++;
            }
        }

        internal static void ExitNumericField()
        {
            if (numericFieldDepth > 0)
            {
                numericFieldDepth--;
            }
        }

        /// <summary>Start of a labeled text-field wrapper's body (TextFieldNumericLabeled, TextEntryLabeled): the caption to carry on the inner field's row as FieldLabel.</summary>
        internal static void EnterFieldLabel(string label)
        {
            fieldLabelDepth++;
            fieldLabelText = label;
        }

        internal static void ExitFieldLabel()
        {
            if (fieldLabelDepth > 0)
            {
                fieldLabelDepth--;
            }
        }

        /// <summary>Start of a Widgets.TextFieldPercent body — see CapturedWidget.NumericPercent's remarks for the domain rule.</summary>
        internal static void EnterPercentField()
        {
            percentFieldDepth++;
        }

        internal static void ExitPercentField()
        {
            if (percentFieldDepth > 0)
            {
                percentFieldDepth--;
            }
        }

        /// <summary>Start of a Widgets.TextFieldVector body: resets the axis counter its three inner numeric fields are named from.</summary>
        internal static void EnterVectorField()
        {
            vectorFieldDepth++;
            vectorAxisIndex = 0;
        }

        internal static void ExitVectorField()
        {
            if (vectorFieldDepth > 0)
            {
                vectorFieldDepth--;
            }
        }

        /// <summary>Start of a Widgets.IntEntry body (stepper bracket): resets the ordinal its four +/- buttons are identified by; the center numeric box marks itself the value field.</summary>
        internal static void EnterIntEntry()
        {
            intEntryDepth++;
            stepperButtonOrdinal = 0;
        }

        internal static void ExitIntEntry()
        {
            if (intEntryDepth > 0)
            {
                intEntryDepth--;
            }
        }

        /// <summary>Start of a Listing_Standard.IntAdjuster body: its two +/- buttons are identified by the same ordinal counter (no value field exists).</summary>
        internal static void EnterIntAdjuster()
        {
            intAdjusterDepth++;
            stepperButtonOrdinal = 0;
        }

        internal static void ExitIntAdjuster()
        {
            if (intAdjusterDepth > 0)
            {
                intAdjusterDepth--;
            }
        }

        // Labeled-composite brackets; none records a row of its own.

        /// <summary>Start of a Widgets.FillableBarLabeled body: the caption to carry on the bar's own row (its Label draw is suppressed by the self-caption bracket).</summary>
        internal static void EnterFillableBarLabel(string label)
        {
            fillableBarLabelDepth++;
            fillableBarLabelText = label;
        }

        internal static void ExitFillableBarLabel()
        {
            if (fillableBarLabelDepth > 0)
            {
                fillableBarLabelDepth--;
            }
        }

        /// <summary>Start of a Listing_Standard.LabelDouble body: the right half, folded into the left half's row (see RecordLabel).</summary>
        internal static void EnterLabelDouble(string rightLabel)
        {
            labelDoubleDepth++;
            labelDoubleRightText = rightLabel;
            labelDoubleSlot = 0;
        }

        internal static void ExitLabelDouble()
        {
            if (labelDoubleDepth > 0)
            {
                labelDoubleDepth--;
            }
        }

        /// <summary>Start of a Widgets.DefLabelWithIcon body: the def description to carry on its Label row, which vanilla's own grouping puts out of the tooltip lookup's reach (see RecordLabel).</summary>
        internal static void EnterDefLabelIcon(string tip)
        {
            defLabelIconDepth++;
            defLabelIconTip = tip;
        }

        internal static void ExitDefLabelIcon()
        {
            if (defLabelIconDepth > 0)
            {
                defLabelIconDepth--;
            }
        }

        /// <summary>Start of a Widgets.HyperlinkWithIcon body; <paramref name="hidden"/> is the link's only real gate (see RecordButton).</summary>
        internal static void EnterHyperlink(bool hidden)
        {
            hyperlinkDepth++;
            hyperlinkHidden = hidden;
        }

        internal static void ExitHyperlink()
        {
            if (hyperlinkDepth > 0)
            {
                hyperlinkDepth--;
            }
        }

        /// <summary>Start of a Widgets.CheckboxLabeledSelectable body: resets the ordinal its two click targets are told apart by, and stashes the row-selected state its caption Label carries.</summary>
        internal static void EnterSelectableRow(bool selected)
        {
            selectableRowDepth++;
            selectableRowSelected = selected;
            selectableRowOrdinal = 0;
        }

        internal static void ExitSelectableRow()
        {
            if (selectableRowDepth > 0)
            {
                selectableRowDepth--;
            }
        }

        /// <summary>Start of a Listing_Standard.SelectableDef body: the row name its delete icon is named after (see ImageButtonLabel).</summary>
        internal static void EnterSelectableDef(string name)
        {
            selectableDefDepth++;
            selectableDefName = name;
        }

        internal static void ExitSelectableDef()
        {
            if (selectableDefDepth > 0)
            {
                selectableDefDepth--;
            }
        }

        /// <summary>Start of a Widgets.InfoCardButtonWorker body — see ImageButtonLabel.</summary>
        internal static void EnterInfoCardButton()
        {
            infoCardDepth++;
        }

        internal static void ExitInfoCardButton()
        {
            if (infoCardDepth > 0)
            {
                infoCardDepth--;
            }
        }

        /// <summary>
        /// Start of a Verse.Listing_Tree.OpenCloseWidget body: the expander ButtonImage it draws for
        /// an openable node carries this depth and the branch state
        /// <see cref="NoteTreeExpanderTexture"/> reads off the drawn texture.
        /// </summary>
        internal static void EnterListingTreeExpander(int indentLevel)
        {
            listingTreeExpanderDepth++;
            listingTreeIndentLevel = indentLevel;
            listingTreeExpanderOpen = false;
        }

        internal static void ExitListingTreeExpander()
        {
            if (listingTreeExpanderDepth > 0)
            {
                listingTreeExpanderDepth--;
            }
        }

        /// <summary>
        /// The expander's branch state, read from the texture vanilla is about to draw
        /// (Listing_Tree.cs:68-69). Taken from the texture rather than re-invoking
        /// Listing_Tree.IsOpen, which is virtual and IS overridden
        /// (Verse/Listing_TreeThingFilter.cs:353): the icon is both what the subclass decided and
        /// what a sighted player sees. No-op outside the bracket.
        /// </summary>
        internal static void NoteTreeExpanderTexture(Texture2D tex)
        {
            if (listingTreeExpanderDepth > 0)
            {
                listingTreeExpanderOpen = tex != null && tex == TexButton.Collapse;
            }
        }

        /// <summary>Start of a Verse.Listing_Tree.LabelLeft body: the node's depth and its untruncated label, both carried on the Label row it draws (see RecordLabel).</summary>
        internal static void EnterListingTreeLabel(string label, string tipText, int indentLevel)
        {
            listingTreeLabelDepth++;
            listingTreeLabelText = label;
            listingTreeIndentLevel = indentLevel;
            listingTreeTipText = tipText;
        }

        internal static void ExitListingTreeLabel()
        {
            if (listingTreeLabelDepth > 0)
            {
                listingTreeLabelDepth--;
            }
        }

        // Range brackets. The range prefix ALSO records its own row (see RecordRange); the bracket
        // exists only to stamp the "min - max" Label the core draws inside it.

        /// <summary>Start of a Widgets.IntRange/FloatRange/QualityRange body — see rangeDepth's remarks.</summary>
        internal static void EnterRange()
        {
            rangeDepth++;
        }

        internal static void ExitRange()
        {
            if (rangeDepth > 0)
            {
                rangeDepth--;
            }
        }

        /// <summary>Start of a Widgets.FloatRangeWithTypeIn body: resets the ordinal its two type-in boxes are named from (see RecordTextField).</summary>
        internal static void EnterRangeTypeIn()
        {
            rangeTypeInDepth++;
            rangeTypeInOrdinal = 0;
        }

        internal static void ExitRangeTypeIn()
        {
            if (rangeTypeInDepth > 0)
            {
                rangeTypeInDepth--;
            }
        }

        // Four stashing brackets (LabelEllipses, WidgetRow.DefIcon, WidgetRow.ToggleableIcon,
        // ColorBox) plus RecordIcon, which records rather than enriches: a def/thing icon draws no
        // captured primitive at all.

        /// <summary>Start of a Widgets.LabelEllipses body: the caller's UNTRUNCATED text, substituted onto the row its inner Widgets.Label records (see RecordLabel).</summary>
        internal static void EnterLabelEllipses(string label)
        {
            labelEllipsesDepth++;
            labelEllipsesText = label;
        }

        internal static void ExitLabelEllipses()
        {
            if (labelEllipsesDepth > 0)
            {
                labelEllipsesDepth--;
            }
        }

        /// <summary>
        /// A standalone def/thing icon — the one widget here reaching no captured primitive
        /// (Verse/Widgets.cs:393/:472/:524), so nothing exists for its caller's TipRegion to resolve
        /// against and the control would render silent. Recorded as a read-only Label row.
        /// Suppressed wherever a captured row already names the same def: the three cores delegate
        /// into each other, DefLabelWithIcon and HyperlinkWithIcon draw their own captions beside
        /// the icon, a tab strip's captions are already Tab rows, and a self-captioning widget
        /// carries its caption on the control's row. A nameless, tooltip-less icon records nothing.
        /// KNOWN IMPRECISION: DefIcon's dispatch and ThingIcon(Rect, Thing) can both draw nothing
        /// for a def with no resolvable icon and expose no readable gate to mirror;
        /// ThingIcon(Rect, ThingDef) does, and its tap applies vanilla's own test.
        /// </summary>
        internal static void RecordIcon(Rect rect, string label, string tip)
        {
            if (!passOpen || iconDepth > 0 || defLabelIconDepth > 0 || hyperlinkDepth > 0
                || tabStripDepth > 0 || selfCaptionDepth > 0)
            {
                return;
            }
            // The caller's own pairing when it has one, else whatever the geometric tooltip
            // channel resolves later.
            string exactTip = !string.IsNullOrEmpty(tip)
                ? tip
                : (widgetRowDefIconDepth > 0 ? widgetRowDefIconTip : null);
            string text = label ?? "";
            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrEmpty(exactTip))
            {
                return;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Label,
                Label = text,
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                Composite = CompositeMember.Icon,
                Tip = exactTip,
                TipIsExact = !string.IsNullOrEmpty(exactTip),
            });
            DrawFocusRingIfFocused(index, rect);
        }

        /// <summary>Start of a Widgets.DefIcon/ThingIcon body — see RecordIcon for why only the outermost of the three records.</summary>
        internal static void EnterIcon()
        {
            iconDepth++;
        }

        internal static void ExitIcon()
        {
            if (iconDepth > 0)
            {
                iconDepth--;
            }
        }

        /// <summary>Start of a Verse.WidgetRow.DefIcon body: the tooltip its own caller paired with the icon, carried onto the row the inner Widgets.DefIcon records.</summary>
        internal static void EnterWidgetRowDefIcon(string tip)
        {
            widgetRowDefIconDepth++;
            widgetRowDefIconTip = tip;
        }

        internal static void ExitWidgetRowDefIcon()
        {
            if (widgetRowDefIconDepth > 0)
            {
                widgetRowDefIconDepth--;
            }
        }

        /// <summary>
        /// Start of a Verse.WidgetRow.ToggleableIcon body: its tooltip names the inner ButtonImage's
        /// row, whose index is pinned here — the ButtonImage is the very next thing the body draws
        /// (Verse/WidgetRow.cs:176-178), so the current sink length IS that index.
        /// <paramref name="tex"/> is stamped on as <see cref="CapturedWidget.IconTexName"/>, since
        /// the tooltip alone cannot tell two toggles in one run apart.
        /// </summary>
        internal static void EnterToggleableIcon(string tooltip, Texture2D tex)
        {
            toggleableIconDepth++;
            toggleableIconTip = tooltip;
            toggleableIconTexName = tex != null ? tex.name : null;
            toggleableIconRow = passOpen ? CurrentSink.Count : -1;
        }

        /// <summary>
        /// End of a Verse.WidgetRow.ToggleableIcon body: stamps the POST-click state onto the row
        /// its ButtonImage recorded. Post, not pre — an injected activation flips the caller's ref
        /// bool inside this same call (:191-202), and the row a pass records carries the state that
        /// pass ENDS with.
        /// </summary>
        internal static void ExitToggleableIcon(bool toggled)
        {
            if (toggleableIconDepth > 0)
            {
                toggleableIconDepth--;
            }
            int index = toggleableIconRow;
            toggleableIconRow = -1;
            if (!passOpen || index < 0)
            {
                return;
            }
            List<CapturedWidget> sink = CurrentSink;
            if (index < sink.Count && sink[index].Composite == CompositeMember.ToggleableIcon)
            {
                sink[index].Checked = toggled;
            }
        }

        /// <summary>Start of a Widgets.ColorBox body: the swatch's color and vanilla's own chosen-or-not decision (see RecordInvisibleButton).</summary>
        internal static void EnterColorBox(Color boxColor, bool selected)
        {
            colorBoxDepth++;
            colorBoxColor = boxColor;
            colorBoxSelected = selected;
        }

        internal static void ExitColorBox()
        {
            if (colorBoxDepth > 0)
            {
                colorBoxDepth--;
            }
        }

        /// <summary>Start of a MedicalCareUtility.MedicalCareSetter body: the category vanilla boxes as chosen (see RecordInvisibleButton).</summary>
        internal static void EnterMedicalCare(MedicalCareCategory current)
        {
            medicalCareDepth++;
            medicalCareCurrent = current;
            medicalCareOrdinal = 0;
        }

        internal static void ExitMedicalCare()
        {
            if (medicalCareDepth > 0)
            {
                medicalCareDepth--;
            }
        }

        /// <summary>A care icon's captured label: vanilla's own hover tooltip text for it.</summary>
        internal static string MedicalCareChoiceLabel(MedicalCareCategory category)
        {
            return category.GetLabel().CapitalizeFirst();
        }

        /// <summary>A color swatch's spoken identity: the nearest named ColorDef (hex only when nothing is close), never a raw RGB string.</summary>
        private static string ColorSwatchLabel(Color color)
        {
            return "RimWorldAccess.UI.GenericWindow.ColorSwatch"
                .Translate(ColorNameHelper.NameForColor(color)).ToString();
        }

        /// <summary>
        /// Widgets.HSVColorWheel draws via raw GUI.DrawTexture and mutates color from mouse-drag
        /// arithmetic (Verse/Widgets.cs:2987-3021) with no captured primitive inside it and no
        /// hotspot to bracket, so this records a standalone read-only presence row. The wheel sets
        /// no color unreachable through the same dialog's already-operable H/S/V/R/G/B fields, so
        /// the row reports its current value rather than making the drag gesture operable. The hex
        /// text rides Tip/TipIsExact, the channel RecordIcon uses for non-hover supplementary text.
        /// </summary>
        internal static void RecordColorWheel(Rect rect, Color color)
        {
            if (!passOpen)
            {
                return;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Label,
                Label = "RimWorldAccess.UI.GenericWindow.ColorWheel".Translate().ToString(),
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                Tip = ColorSwatchLabel(color),
                TipIsExact = true,
            });
            DrawFocusRingIfFocused(index, rect);
        }

        private static string VectorAxisName(int axisIndex)
        {
            switch (axisIndex)
            {
                case 0:
                    return "RimWorldAccess.UI.GenericWindow.VectorAxisX".Translate();
                case 1:
                    return "RimWorldAccess.UI.GenericWindow.VectorAxisY".Translate();
                case 2:
                    return "RimWorldAccess.UI.GenericWindow.VectorAxisZ".Translate();
                default:
                    return "";
            }
        }
    }
}
