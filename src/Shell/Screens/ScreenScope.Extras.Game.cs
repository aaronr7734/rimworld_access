using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public abstract partial class ScreenScope : FocusScope
    {
        /// <summary>
        /// Called by the draw bracket after the owned window's pass completes; snapshots the
        /// captured buttons so the Buttons region stays valid while an overlay pauses the passes.
        /// </summary>
        internal void OnButtonPassCompleted()
        {
            capturedButtons.Clear();
            capturedButtonSourceIndex.Clear();
            IReadOnlyList<ButtonTextCapture.CapturedButton> pass = ButtonTextCapture.Items;
            for (int i = 0; i < pass.Count; i++)
            {
                if (!KeepCapturedButton(pass[i].Label))
                {
                    continue;
                }
                capturedButtons.Add(pass[i]);
                // Clicks and the focus ring address the RAW capture stream, so a filtered region
                // index must translate back through its source position.
                capturedButtonSourceIndex.Add(i);
            }
            buttonPassSinceFocus = true;
            if (awaitingEntryAnnouncement)
            {
                // The Buttons region populates from a later capture pass, so re-derive region
                // counts against the snapshot just taken and speak the entry item the moment it is
                // no longer empty, rather than waiting for the first navigation key.
                RefreshModel();
                TryAnnounceEntryIfPending();
            }
        }

        /// <summary>Bracket gate: only scopes that want captured buttons arm the capture.</summary>
        internal bool WantsButtonCapture
        {
            get { return IncludeActionsRegion && CaptureWindowButtons; }
        }

        /// <summary>The capture index the ring should highlight this pass, or -1.</summary>
        internal int FocusedCaptureIndex()
        {
            if (!InActionsRegion())
                return -1;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0 || region.Index >= capturedButtons.Count)
                return -1;
            return capturedButtonSourceIndex[region.Index];
        }

        /// <summary>
        /// The focused CONTENT element's on-screen rect in absolute UI points, clipped to what is
        /// visible (empty when scrolled out), or default off a visual content row. The Buttons and
        /// extras regions are not reported here: their capture engines already ring them.
        /// </summary>
        protected internal virtual UnityEngine.Rect FocusedContentRect()
        {
            return default(UnityEngine.Rect);
        }

        // Captured-extras widget pass; the opt-in gate is on ScreenScope itself.

        /// <summary>The stream index the widget-capture ring should highlight this pass, or -1.</summary>
        internal int FocusedWidgetCaptureIndex()
        {
            if (!InExtrasRegion())
                return -1;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.Index < 0 || region.Index >= extrasRows.Count)
                return -1;
            InteractiveMember member = extrasRows[region.Index].Member;
            if (member == null)
                return -1;
            return RimWorldAccess.CaptureDescriptor.FindIndex(widgetSnapshot, member.Kind, member.RawLabel, member.Ordinal);
        }

        /// <summary>
        /// The same (kind, label, ordinal) lookup <see cref="FocusedWidgetCaptureIndex"/> uses,
        /// exposed for typed content regions mirroring a vanilla widget the presented-set diff has
        /// already excluded. Absolute UI points, empty when not captured this pass.
        /// </summary>
        protected internal UnityEngine.Rect FindCapturedWidgetRect(WidgetKind kind, string label, int ordinal)
        {
            int index = RimWorldAccess.CaptureDescriptor.FindIndex(widgetSnapshot, kind, label, ordinal);
            return index >= 0 ? widgetSnapshot[index].VisibleScreenRect : default(UnityEngine.Rect);
        }

        /// <summary>
        /// Called by the draw bracket after the owned window's widget-capture pass completes:
        /// snapshots the stream, rebuilds the extras rows against it, and resolves any deferred
        /// state-change announcement armed by the last Enter/Left/Right on an extras row. Keeps the
        /// previous snapshot when the pass drew nothing while a foreign window sits above.
        /// </summary>
        internal void OnWidgetPassCompleted()
        {
            if (extrasEditSession.Editing)
            {
                // Re-post the live buffer as this pass's forced TextField result, per
                // TextFieldEditSession's per-pass contract.
                extrasEditSession.MirrorLive();
            }
            IReadOnlyList<CapturedWidget> pass = WidgetCapture.Items;
            if (pass.Count == 0 && TextDialogShared.ForeignWindowAbove(OwnedWindow))
            {
                return;
            }
            widgetSnapshot.Clear();
            extrasFieldNames.Clear();
            bool[] admitted = new bool[pass.Count];
            int placeholderNamedField = -1;
            for (int i = 0; i < pass.Count; i++)
            {
                int field = PlaceholderFieldIndex(pass, i);
                if (field >= 0)
                {
                    if (admitted[field] && field != placeholderNamedField && IsBlankField(pass[field]))
                    {
                        extrasFieldNames[pass[field]] = pass[i].PlainLabel;
                        placeholderNamedField = field;
                    }
                    continue;
                }
                if (ExcludeFromCapturedExtras(pass[i]) || IsRedundantCloseX(pass[i]))
                {
                    continue;
                }
                admitted[i] = true;
                widgetSnapshot.Add(pass[i]);
                string caption = CaptionAboveBlankField(pass, i);
                if (caption != null)
                {
                    extrasFieldNames[pass[i]] = caption;
                }
            }
            RebuildExtrasRows();
            if (extrasPendingChange.HasValue)
            {
                ResolveExtrasPendingChange();
            }
            widgetPassSinceFocus = true;
            if (awaitingEntryAnnouncement)
            {
                // Runs after OnButtonPassCompleted in the same postfix, so both snapshots are fresh.
                RefreshModel();
                TryAnnounceEntryIfPending();
            }
        }

        internal void OnWindowPassCompleted()
        {
            if (buttonClickPassesLeft > 0)
            {
                buttonClickPassesLeft--;
                OnCapturedButtonPass();
            }
            // The pass that runs the click still draws the old caption, so the relabel shows a pass later.
            if (relabelWatchPasses > 0)
            {
                relabelWatchPasses--;
                if (relabelWatchIndex < extrasRows.Count && extrasRows[relabelWatchIndex].Member != null
                    && extrasRows[relabelWatchIndex].Label != relabelWatchLabel)
                {
                    relabelWatchPasses = 0;
                    TolkHelper.SpeakData(extrasRows[relabelWatchIndex].Label);
                }
            }
        }

        /// <summary>Runs on both passes after a captured button click posts; the click lands in one, per QA R6.</summary>
        protected virtual void OnCapturedButtonPass()
        {
        }

        /// <summary>
        /// Per-widget veto over the captured-extras region, judged BEFORE folding so a vetoed
        /// control's whole cluster drops together. The presented-set diff can only exclude by TEXT,
        /// which a captionless widget defeats. Default false: everything captured stays eligible.
        /// </summary>
        protected virtual bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return false;
        }

        /// <summary>A label drawn over an empty text field just after it is that field's placeholder: the index of its field, else -1.</summary>
        private static int PlaceholderFieldIndex(IReadOnlyList<CapturedWidget> pass, int labelIndex)
        {
            CapturedWidget label = pass[labelIndex];
            if (label.Kind != WidgetKind.Label || string.IsNullOrWhiteSpace(label.Label))
            {
                return -1;
            }
            for (int j = labelIndex - 1; j >= 0 && j >= labelIndex - 2; j--)
            {
                CapturedWidget field = pass[j];
                if (field.Kind == WidgetKind.TextField && string.IsNullOrEmpty(field.Text) && field.Clip.Equals(label.Clip)
                    && BlankFieldCaption.PlaceholderInside(field.Box(), label.Box()))
                {
                    return j;
                }
            }
            return -1;
        }

        private static bool IsBlankField(CapturedWidget field)
        {
            return field.Kind == WidgetKind.TextField && string.IsNullOrEmpty(field.Label) && string.IsNullOrEmpty(field.FieldLabel);
        }

        /// <summary>An uncaptioned field's caption: a label drawn just before it, directly above.</summary>
        private static string CaptionAboveBlankField(IReadOnlyList<CapturedWidget> pass, int fieldIndex)
        {
            CapturedWidget field = pass[fieldIndex];
            if (fieldIndex == 0 || !IsBlankField(field))
            {
                return null;
            }
            CapturedWidget above = pass[fieldIndex - 1];
            if (above.Kind != WidgetKind.Label || string.IsNullOrWhiteSpace(above.Label) || above.Dimmed
                || !above.Clip.Equals(field.Clip) || !BlankFieldCaption.CaptionAbove(field.Box(), above.Box())
                || !CaptionNote.NamesControl(above.PlainLabel))
            {
                return null;
            }
            return above.PlainLabel;
        }

        /// <summary>The corner X is a second close control when vanilla also draws its bottom close button, which the Buttons region already holds.</summary>
        private bool IsRedundantCloseX(CapturedWidget widget)
        {
            Window window = OwnedWindow;
            return widget.Kind == WidgetKind.Button && widget.CloseX
                && window != null && window.doCloseX && window.doCloseButton;
        }

        /// <summary>One extras row's runtime state: the expanded row itself plus (when operable) the captured member driving activation/adjustment.</summary>
        private sealed class ExtrasRow : CapturedExtraRow
        {
            /// <summary>Null for a read-only row (no interactive control on the folded row).</summary>
            public InteractiveMember Member;
        }

        private struct ExtrasPendingChange
        {
            public int Index;
            public CheckState? Check;
            public bool? Selected;
            public float SliderValue;
            public int PassesLeft;
        }

        /// <summary>
        /// Folds the latest widget snapshot, keeps only rows unmirrored by this scope's own
        /// content/actions rows, and expands each survivor through <see cref="CapturedExtrasRows"/>.
        /// </summary>
        private void RebuildExtrasRows()
        {
            extrasRows.Clear();
            if (widgetSnapshot.Count == 0)
            {
                return;
            }
            List<List<int>> bands;
            List<RimWorldAccess.FoldedRow> folded = RimWorldAccess.CapturedRowFolder.Fold(widgetSnapshot, out bands);
            if (folded.Count == 0)
            {
                return;
            }
            PresentedHaystacks presented = BuildPresentedHaystacks();
            for (int i = 0; i < folded.Count; i++)
            {
                RimWorldAccess.FoldedRow row = folded[i];
                if (row.Interactives == null || row.Interactives.Count == 0)
                {
                    // An OPERABILITY net, not a second reading of the screen: a captured row with
                    // nothing operable is presentation, which belongs to the typed content regions.
                    // A control this scope does not model still comes through, because it has
                    // interactives. ExtrasIncludeReadOnlyRows flips this for a scope whose typed
                    // regions cannot model the window's free-form informational text.
                    if (ExtrasIncludeReadOnlyRows && !string.IsNullOrEmpty(row.Text)
                        && RowIsUnmirrored(row, presented))
                    {
                        extrasRows.Add(new ExtrasRow
                        {
                            Label = row.Text.StripTags(),
                            Role = ElementRole.None,
                            Tip = row.Tip,
                            ReadOnly = true,
                            Member = null,
                        });
                    }
                    continue;
                }
                if (!RowIsUnmirrored(row, presented))
                {
                    continue;
                }
                AppendExtrasRows(row, row.BandIndex >= 0 && row.BandIndex < bands.Count ? bands[row.BandIndex] : null, presented);
            }
        }

        /// <summary>
        /// A row with interactive members is judged by their RAW labels, since fragments bake in role and
        /// state wording no typed row's label matches. A pure-text row falls back to the folded row text;
        /// a row whose interactives are ALL unlabeled falls back to <see cref="RemainderIsUnmirrored"/>.
        /// </summary>
        private bool RowIsUnmirrored(RimWorldAccess.FoldedRow row, PresentedHaystacks presented)
        {
            if (row.Interactives == null || row.Interactives.Count == 0)
            {
                return CaptureTextNormalization.IsUnmirrored(row.Text, presented.All, s => s.StripTags());
            }
            bool judgedAny = false;
            for (int i = 0; i < row.Interactives.Count; i++)
            {
                string memberText = PlainMemberText(row.Interactives[i]);
                if (string.IsNullOrEmpty(memberText))
                {
                    continue;
                }
                string normalized = CaptureTextNormalization.NormalizeForContainment(memberText.StripTags());
                if (normalized.Length == 0)
                {
                    continue;
                }
                judgedAny = true;
                if (!MemberIsMirrored(memberText, normalized, presented))
                {
                    return true;
                }
            }
            if (judgedAny)
            {
                return false;
            }
            return RemainderIsUnmirrored(row, presented.All);
        }

        /// <summary>Below this, a normalized label is too short for containment to mean anything.</summary>
        private const int TrustedContainmentLength = 8;

        /// <summary>
        /// Whether this scope really does say the member elsewhere. Containment is only trustworthy
        /// for a long fragment: a short label lands inside an unrelated sentence ("Skill" inside
        /// "skill focus is missing"), and one under three characters yields no comparable line at
        /// all, so the shared test can return no verdict. A short label therefore counts as mirrored
        /// only when it matches a WHOLE presented entry, and containment only counts in a name, not
        /// in a reason that mentions it. Losing a control is the costly error, so inconclusive keeps it.
        /// </summary>
        private static bool MemberIsMirrored(string memberText, string normalized, PresentedHaystacks presented)
        {
            if (presented.All.Contains("\n" + normalized + "\n"))
            {
                return true;
            }
            return normalized.Length >= TrustedContainmentLength
                && !CaptureTextNormalization.IsUnmirrored(memberText, presented.Names, s => s.StripTags());
        }

        /// <summary>
        /// Fallback for a row whose every interactive was itself unlabeled, the common shape for a
        /// bare uncaptioned TextField or Slider. <c>row.Text</c> for such a member is NOT the
        /// widget's raw value: RenderFragment composes it through the same AnnouncementComposer
        /// vocabulary a live focus announcement uses, so a blank TextField contributes the spoken
        /// phrase, never the empty string, and judging that unstripped text is a permanent false
        /// positive for a scope that already mirrors the control by its adjacent label.
        ///
        /// So strip each interactive's own composed
        /// <see cref="RimWorldAccess.InteractiveMember.Fragment"/> out of <c>row.Text</c> and judge
        /// the residue: a non-empty residue is a real adjacent label, judged on its own; an empty
        /// residue means a standalone unlabeled control, which falls back to the full row text —
        /// a composed phrase that essentially never matches presented text, so such a control still
        /// surfaces.
        ///
        /// DO NOT SIMPLIFY: this is the load-bearing safety property for every scope with
        /// <see cref="IncludeCapturedExtrasRegion"/> on. It only ever narrows a false positive,
        /// never widens a false negative; do not change it without re-checking both directions.
        /// </summary>
        private static bool RemainderIsUnmirrored(RimWorldAccess.FoldedRow row, string presented)
        {
            string residue = row.Text;
            for (int i = 0; i < row.Interactives.Count; i++)
            {
                string fragment = row.Interactives[i].Fragment;
                if (!string.IsNullOrEmpty(fragment))
                {
                    residue = residue.Replace(fragment, "");
                }
            }
            if (!string.IsNullOrWhiteSpace(residue))
            {
                return CaptureTextNormalization.IsUnmirrored(residue, presented, s => s.StripTags());
            }
            return CaptureTextNormalization.IsUnmirrored(row.Text, presented, s => s.StripTags());
        }

        /// <summary>
        /// The name a member is SPOKEN by: the tooltip name the folder resolved for an icon-only
        /// button, else the member's own capture-time label. Both the extras row's label and the
        /// presented-set diff read this rather than the raw label, because the diff must subtract on
        /// what the user would hear and a texture asset name matches nothing a scope presents.
        /// </summary>
        internal static string SpokenMemberLabel(InteractiveMember member)
        {
            CapturedWidget source = member.Source;
            if (source != null && !string.IsNullOrEmpty(source.TipName))
            {
                return source.TipName;
            }
            return member.RawLabel;
        }

        /// <summary><see cref="SpokenMemberLabel"/>, or for an uncaptioned field the name its placeholder or caption above gives it.</summary>
        private string ExtrasMemberName(InteractiveMember member)
        {
            string spoken = SpokenMemberLabel(member);
            string fieldName;
            return string.IsNullOrEmpty(spoken) && member.Source != null && extrasFieldNames.TryGetValue(member.Source, out fieldName)
                ? fieldName
                : spoken;
        }

        /// <summary>
        /// The undecorated text a member is judged by: its spoken label, else its source widget's
        /// label, else its value text — for a valueless name, value identity IS mirroring.
        /// </summary>
        private string PlainMemberText(InteractiveMember member)
        {
            string spoken = ExtrasMemberName(member);
            if (!string.IsNullOrEmpty(spoken))
            {
                return spoken;
            }
            CapturedWidget source = member.Source;
            if (source == null)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(source.Label))
            {
                return source.Label;
            }
            if (!string.IsNullOrEmpty(source.Text))
            {
                return source.Text;
            }
            return source.SliderValueText;
        }

        /// <summary>
        /// The presented set the extras diff subtracts against. Names omits the prose fields
        /// (Extras, Hint, cell tips), which can mention a control without presenting it.
        /// </summary>
        private struct PresentedHaystacks
        {
            public string All;
            public string Names;
        }

        private PresentedHaystacks BuildPresentedHaystacks()
        {
            var all = new StringBuilder();
            var names = new StringBuilder();
            int content = ContentRegionCount;
            for (int region = 0; region < content; region++)
            {
                int columns = ContentColumnCount(region);
                if (columns > 0)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        TableColumnInfo info = ContentColumnInfo(region, c);
                        if (info != null)
                        {
                            AppendName(all, names, info.Label);
                        }
                    }
                    int rows = ContentItemCount(region);
                    for (int r = 0; r < rows; r++)
                    {
                        AppendPresented(all, names, DescribeContentItem(region, r));
                        for (int c = 0; c < columns; c++)
                        {
                            AppendName(all, names, ContentCellText(region, r, c));
                            AppendPresented(all, ContentCellTip(region, r, c));
                        }
                    }
                }
                else
                {
                    int items = ContentItemCount(region);
                    for (int idx = 0; idx < items; idx++)
                    {
                        AppendPresented(all, names, DescribeContentItem(region, idx));
                    }
                }
            }
            for (int i = 0; i < capturedButtons.Count; i++)
            {
                AppendName(all, names, capturedButtons[i].Label);
            }
            IReadOnlyList<ScreenAction> declared = ResolvedDeclaredActions();
            if (declared != null)
            {
                for (int i = 0; i < declared.Count; i++)
                {
                    AppendName(all, names, declared[i].Label);
                }
            }
            IEnumerable<string> extra = AdditionalPresentedTexts;
            if (extra != null)
            {
                foreach (string text in extra)
                {
                    AppendName(all, names, text);
                }
            }
            return new PresentedHaystacks { All = NormalizeHaystack(all), Names = NormalizeHaystack(names) };
        }

        /// <summary>Per entry and newline-delimited, so a short label can match a WHOLE entry.</summary>
        private static string NormalizeHaystack(StringBuilder sb)
        {
            var normalized = new StringBuilder("\n");
            foreach (string entry in sb.ToString().StripTags().Split('\n'))
            {
                string line = CaptureTextNormalization.NormalizeForContainment(entry);
                if (line.Length > 0)
                {
                    normalized.Append(line).Append('\n');
                }
            }
            return normalized.ToString();
        }

        private static void AppendPresented(StringBuilder all, StringBuilder names, ElementDescription d)
        {
            if (d == null)
            {
                return;
            }
            AppendName(all, names, d.Label);
            AppendName(all, names, d.Value);
            AppendPresented(all, d.Extras);
            AppendPresented(all, d.Hint);
            // Vanilla settings surfaces draw combined read-out labels beside widgets the typed rows
            // model as separate Label and Value; the composite line lets the diff subtract those.
            if (!string.IsNullOrEmpty(d.Label) && !string.IsNullOrEmpty(d.Value))
            {
                AppendName(all, names, d.Label + ": " + d.Value);
            }
        }

        private static void AppendName(StringBuilder all, StringBuilder names, string text)
        {
            AppendPresented(all, text);
            AppendPresented(names, text);
        }

        private static void AppendPresented(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            sb.Append(text).Append('\n');
        }

        /// <summary>
        /// Folds one unmirrored captured row into one or more extras rows, pairing each expansion
        /// result with its source InteractiveMember by index. Only rows carrying interactives reach
        /// here; <see cref="RebuildExtrasRows"/> drops pure-text rows.
        /// </summary>
        private void AppendExtrasRows(RimWorldAccess.FoldedRow row, List<int> band, PresentedHaystacks presented)
        {
            var pureMembers = new List<CapturedExtraMember>(row.Interactives.Count);
            for (int i = 0; i < row.Interactives.Count; i++)
            {
                CapturedExtraMember pure = ToPureMember(row.Interactives[i]);
                pure.RawLabel = ExtrasMemberName(row.Interactives[i]);
                pure.CaptionLeads = CaptionLeadsMember(band, row.Interactives[i].Source);
                pureMembers.Add(pure);
            }
            // Whatever a banded row says beyond its controls' own fragments is a two-column form's
            // caption. It names every unlabeled control and each labeled one it leads ("Class hours",
            // 8 and 15); one that names nothing reads as its own line. A row of bare controls has none.
            var fragments = new List<string>(row.Interactives.Count);
            for (int i = 0; i < row.Interactives.Count; i++)
            {
                fragments.Add(row.Interactives[i].Fragment);
            }
            string caption = RimWorldAccess.CapturedRowResidue.HasContext(row.Text, fragments)
                ? RimWorldAccess.CapturedRowResidue.Strip(row.Text, fragments)
                : null;
            if (CapturedExtrasRows.CaptionLeftOver(pureMembers, caption)
                && CaptureTextNormalization.IsUnmirrored(caption, presented.All, s => s.StripTags()))
            {
                extrasRows.Add(new ExtrasRow
                {
                    Label = CapturedExtrasRows.TrimCaption(caption).StripTags(),
                    Role = ElementRole.None,
                    ReadOnly = true,
                    Member = null,
                });
            }
            List<CapturedExtraRow> expanded =
                CapturedExtrasRows.Expand(row.Text, row.Tip, pureMembers, caption);
            for (int i = 0; i < expanded.Count && i < row.Interactives.Count; i++)
            {
                CapturedExtraRow pr = expanded[i];
                extrasRows.Add(new ExtrasRow
                {
                    Label = pr.Label,
                    Role = pr.Role,
                    Tip = pr.Tip,
                    Disabled = pr.Disabled,
                    Check = pr.Check,
                    Selected = pr.Selected,
                    Value = pr.Value,
                    ValueBlank = pr.ValueBlank,
                    AtMinimum = pr.AtMinimum,
                    AtMaximum = pr.AtMaximum,
                    ReadOnly = pr.ReadOnly,
                    Member = row.Interactives[i],
                });
                PublishSliderName(row.Interactives[i]);
            }
        }

        /// <summary>Some plain label on the control's band ends left of it; text only to its right never names it.</summary>
        private bool CaptionLeadsMember(List<int> band, CapturedWidget control)
        {
            if (band == null || control == null)
            {
                return true;
            }
            for (int k = 0; k < band.Count; k++)
            {
                CapturedWidget label = widgetSnapshot[band[k]];
                if (label.Kind == WidgetKind.Label && !string.IsNullOrWhiteSpace(label.Label)
                    && control.Box().GapRight(label.Box()) >= -LineBox.EdgeSlack)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Hands a folded slider's name to the mouse-drag reader under vanilla's own drag identity.
        /// Only a member with a real caption of its own is published: the other label source is the
        /// whole folded row's text, which bakes in the slider's value and would be a pass stale.
        /// </summary>
        private static void PublishSliderName(InteractiveMember member)
        {
            if (member.Kind != WidgetKind.Slider || member.Source == null || member.Source.SliderControlId == 0)
            {
                return;
            }
            // The same label the extras row speaks, so the two readers never name one slider twice.
            string name = SpokenMemberLabel(member);
            if (!string.IsNullOrEmpty(name))
            {
                SliderCaptionIndex.Publish(member.Source.SliderControlId, name, carriesValue: false);
            }
        }

        internal static CapturedExtraMember ToPureMember(InteractiveMember member)
        {
            var pure = new CapturedExtraMember
            {
                Kind = MapCapturedKind(member.Kind),
                RawLabel = SpokenMemberLabel(member),
                Fragment = member.Fragment,
            };
            CapturedWidget source = member.Source;
            if (source == null)
            {
                return pure;
            }
            pure.Disabled = source.Disabled;
            switch (member.Kind)
            {
                case WidgetKind.Checkbox:
                    pure.Check = source.TriState == MultiCheckboxState.Partial
                        ? CheckState.PartiallyChecked
                        : (source.Checked ? CheckState.Checked : CheckState.Unchecked);
                    break;
                case WidgetKind.RadioButton:
                case WidgetKind.Tab:
                    pure.Selected = source.Selected;
                    break;
                case WidgetKind.Slider:
                    pure.SliderValueText = source.SliderValueText;
                    pure.SliderValue = source.SliderValue;
                    pure.SliderMin = source.SliderMin;
                    pure.SliderMax = source.SliderMax;
                    break;
                case WidgetKind.TextField:
                    pure.TextValue = source.Text;
                    pure.ValueBlank = string.IsNullOrEmpty(source.Text);
                    break;
                case WidgetKind.FillableBar:
                    pure.FillPercent = source.FillPercent;
                    break;
            }
            return pure;
        }

        internal static CapturedExtraKind MapCapturedKind(WidgetKind kind)
        {
            switch (kind)
            {
                case WidgetKind.Label: return CapturedExtraKind.Label;
                case WidgetKind.Checkbox: return CapturedExtraKind.Checkbox;
                case WidgetKind.RadioButton: return CapturedExtraKind.RadioButton;
                case WidgetKind.Button: return CapturedExtraKind.Button;
                case WidgetKind.Slider: return CapturedExtraKind.Slider;
                case WidgetKind.TextField: return CapturedExtraKind.TextField;
                case WidgetKind.Tab: return CapturedExtraKind.Tab;
                case WidgetKind.FillableBar: return CapturedExtraKind.FillableBar;
                default: return CapturedExtraKind.InvisibleButton;
            }
        }

    }
}
