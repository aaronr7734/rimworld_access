using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Pure mirror of <c>WidgetCapture.WidgetKind</c> (Shell/Sync, game-coupled
    /// via <c>CapturedWidget</c>'s UnityEngine fields) — the captured-extras
    /// region's own vocabulary so its row-expansion logic can link into the
    /// test project. ScreenScope.Game.cs maps the real WidgetKind onto this
    /// one 1:1.
    /// </summary>
    public enum CapturedExtraKind
    {
        Label,
        Checkbox,
        RadioButton,
        Button,
        Slider,
        TextField,
        Tab,
        FillableBar,
        InvisibleButton,
    }

    /// <summary>
    /// Pure snapshot of one interactive control folded from a captured visual
    /// row, carrying just enough state to build the row's
    /// <see cref="CapturedExtraRow"/> without touching CapturedWidget/
    /// UnityEngine. Populated 1:1 from an <c>InteractiveMember</c>/
    /// <c>CapturedWidget</c> pair by ScreenScope.Game.cs.
    /// </summary>
    public sealed class CapturedExtraMember
    {
        public CapturedExtraKind Kind;

        /// <summary>
        /// The widget's own undecorated label — the same string the activation
        /// descriptor matches on. Preferred as the row label so role/state
        /// wording comes only from the element vocabulary, never baked into
        /// the label text.
        /// </summary>
        public string RawLabel = "";

        public string Fragment = "";
        public bool Disabled;
        public CheckState? Check;
        public bool? Selected;
        public string SliderValueText;
        public float SliderValue;
        public float SliderMin;
        public float SliderMax;
        public string TextValue;
        public bool ValueBlank;
        public float FillPercent;

        /// <summary>Every caption fragment on the member's row sits left of it, the way a form labels its controls.</summary>
        public bool CaptionLeads = true;
    }

    /// <summary>One navigable row the captured-extras region presents.</summary>
    public class CapturedExtraRow
    {
        public string Label = "";
        public ElementRole Role = ElementRole.None;

        /// <summary>The folded row's tooltip text, if any — spoken as the description's verbose tail.</summary>
        public string Tip;

        public bool Disabled;
        public bool ReadOnly;
        public CheckState? Check;
        public bool? Selected;
        public string Value;
        public bool ValueBlank;
        public bool AtMinimum;
        public bool AtMaximum;
    }

    /// <summary>
    /// Pure expansion engine behind ScreenScope's captured-extras region (new-
    /// game retrofit Slice 0): maps a captured widget kind to its
    /// <see cref="ElementRole"/>, and expands one folded visual row (its
    /// composed text/tooltip plus zero or more interactive members) into the
    /// one-or-many <see cref="CapturedExtraRow"/> entries the region
    /// navigates. A row with no interactive members becomes a single
    /// read-only line (the folded text, the folded tip as its own tooltip); a
    /// row with members becomes one row per member, labeled by that member's
    /// own rendered fragment (falling back to the row's whole text when the
    /// fragment is blank) — mirroring how a multi-control captured row (e.g.
    /// a labeled slider next to a reset button) already reads as several
    /// separate elements everywhere else in this engine.
    /// </summary>
    public static class CapturedExtrasRows
    {
        private const float SliderBoundsEpsilon = 0.0001f;

        /// <summary>The standard role for a captured widget kind, mod-wide.</summary>
        public static ElementRole RoleFor(CapturedExtraKind kind)
        {
            switch (kind)
            {
                case CapturedExtraKind.Checkbox:
                    return ElementRole.Checkbox;
                case CapturedExtraKind.RadioButton:
                    return ElementRole.RadioButton;
                case CapturedExtraKind.Button:
                case CapturedExtraKind.InvisibleButton:
                    return ElementRole.Button;
                case CapturedExtraKind.Slider:
                    return ElementRole.Slider;
                case CapturedExtraKind.TextField:
                    return ElementRole.TextField;
                case CapturedExtraKind.Tab:
                    return ElementRole.Tab;
                default: // Label, FillableBar: plain read-only content, no role word.
                    return ElementRole.None;
            }
        }

        /// <summary>
        /// The standard element description for one expanded extras row — the
        /// single mapping every reader of these rows speaks through (the
        /// keyboard's extras region and the hover service alike). The row's
        /// tooltip is the description's verbose tail.
        /// </summary>
        public static ElementDescription Describe(CapturedExtraRow row)
        {
            if (row == null)
            {
                return new ElementDescription();
            }
            return new ElementDescription
            {
                Label = row.Label,
                Role = row.Role,
                Extras = row.Tip,
                Disabled = row.Disabled,
                ReadOnly = row.ReadOnly,
                Check = row.Check,
                Selected = row.Selected,
                Value = row.Value,
                ValueBlank = row.ValueBlank,
                AtMinimum = row.AtMinimum,
                AtMaximum = row.AtMaximum,
            };
        }

        /// <summary>
        /// Expands one folded row into its navigable extras rows, in the same
        /// order as <paramref name="members"/> (index-aligned — a caller that
        /// also needs each member's own activation descriptor zips the input
        /// list against this method's output by index when
        /// <paramref name="members"/> is non-empty).
        /// </summary>
        public static List<CapturedExtraRow> Expand(string rowText, string rowTip,
            IReadOnlyList<CapturedExtraMember> members, string caption = null)
        {
            var rows = new List<CapturedExtraRow>();
            if (members == null || members.Count == 0)
            {
                rows.Add(new CapturedExtraRow
                {
                    Label = rowText ?? "",
                    Role = ElementRole.None,
                    Tip = rowTip,
                    ReadOnly = true,
                });
                return rows;
            }

            // The folded row's tip is the WHOLE row's tooltip context — kept on
            // only the first expanded member row so a multi-control row (e.g. a
            // labeled slider next to a reset button) does not repeat it once per
            // control.
            for (int i = 0; i < members.Count; i++)
            {
                CapturedExtraRow row = ExpandMember(rowText, members[i], CaptionNames(members[i], caption) ? caption : null);
                row.Tip = i == 0 ? rowTip : null;
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>A row caption names an unlabeled control; a labeled one only when it leads it and is no counter line.</summary>
        public static bool CaptionNames(CapturedExtraMember member, string caption)
        {
            if (member == null || string.IsNullOrWhiteSpace(caption))
            {
                return false;
            }
            if (string.IsNullOrEmpty(member.RawLabel))
            {
                return true;
            }
            return member.CaptionLeads && !CaptionNote.IsMultiValueReadout(caption);
        }

        /// <summary>The row caption no control took: still on screen, so it reads as a line of its own.</summary>
        public static bool CaptionLeftOver(IReadOnlyList<CapturedExtraMember> members, string caption)
        {
            if (string.IsNullOrWhiteSpace(caption) || members == null)
            {
                return false;
            }
            for (int i = 0; i < members.Count; i++)
            {
                if (CaptionNames(members[i], caption))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>A caption trimmed of the row joiner's punctuation.</summary>
        public static string TrimCaption(string caption)
        {
            return string.IsNullOrEmpty(caption) ? "" : caption.Trim(CaptionTrim);
        }

        private static CapturedExtraRow ExpandMember(string rowText, CapturedExtraMember member,
            string caption)
        {
            string context = TrimCaption(caption);
            var row = new CapturedExtraRow
            {
                // RawLabel first: the fragment already says role and state. A blank field's or
                // slider's row text IS that role phrase ("edit box, blank"), so it falls back to
                // nothing rather than speak the phrase twice.
                Label = !string.IsNullOrEmpty(member.RawLabel)
                    ? member.RawLabel
                    : (member.Kind == CapturedExtraKind.TextField
                        || member.Kind == CapturedExtraKind.Slider ? "" : (rowText ?? "")),
                Role = RoleFor(member.Kind),
                Disabled = member.Disabled,
                Check = member.Check,
                Selected = member.Selected,
                ValueBlank = member.ValueBlank,
            };
            switch (member.Kind)
            {
                case CapturedExtraKind.Slider:
                    row.Value = !string.IsNullOrEmpty(member.SliderValueText)
                        ? member.SliderValueText
                        : SliderCaption.FormatValue(member.SliderValue);
                    row.AtMinimum = member.SliderValue <= member.SliderMin + SliderBoundsEpsilon;
                    row.AtMaximum = member.SliderValue >= member.SliderMax - SliderBoundsEpsilon;
                    break;
                case CapturedExtraKind.TextField:
                    row.Value = member.ValueBlank ? null : member.TextValue;
                    break;
                case CapturedExtraKind.FillableBar:
                    row.Role = ElementRole.None;
                    row.ReadOnly = true;
                    row.Value = FormatPercent(member.FillPercent);
                    break;
                case CapturedExtraKind.Label:
                    row.ReadOnly = true;
                    break;
            }
            if (context.Length > 0)
            {
                // A two-column form draws the name beside the control, so the control's own text is
                // its value and the caption is its name. Without this the caption is dropped and a
                // bare field reaches the player with no name at all.
                if (string.IsNullOrEmpty(row.Value) && !row.ValueBlank
                    && !string.IsNullOrEmpty(row.Label) && row.Label != context)
                {
                    row.Value = row.Label;
                }
                row.Label = context;
            }
            return row;
        }

        /// <summary>The row joiner's own punctuation, trimmed off a caption's ends.</summary>
        private static readonly char[] CaptionTrim = { ' ', '\t', '\n', '\r', ',', '.', ':', ';' };

        private static string FormatPercent(float value)
        {
            return Math.Round(value * 100f).ToString("F0") + "%";
        }
    }
}
