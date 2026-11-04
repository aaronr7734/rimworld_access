using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compositable Loadouts' item specifier (<c>Dialog_ItemSpecifier</c>). Three regions: the item's
    /// settings (the alternative picker, the shared hit point and quality ranges, Set as default,
    /// the preview quality arrows), the stuff list (its search box, then one checkbox per stuff),
    /// and the statistics table (one row per stat, one column per previewed def and stuff). Landing
    /// on a stuff row previews it, as hovering it does, which adds the table's second column.
    /// </summary>
    internal sealed class ClItemSpecifierScope : ScreenScope
    {
        private const int SettingsRegion = 0;
        private const int StuffRegion = 1;
        private const int StatsRegion = 2;

        private enum SettingsRow
        {
            Alternative,
            HitPoints,
            Quality,
            SetDefault,
            PreviewQuality,
        }

        // The mod's InputField caps at 999 characters, which no stuff name approaches.
        private static readonly TextFieldSpec SearchSpec = new TextFieldSpec(labelKey: null, minLength: 0);

        private readonly Window dialog;
        private readonly List<SettingsRow> settingsRows = new List<SettingsRow>();
        private readonly List<ThingDef> stuffs = new List<ThingDef>();
        private readonly List<StatDef> stats = new List<StatDef>();
        private readonly List<ThingDef> columns = new List<ThingDef>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession searchSession = new TextFieldEditSession();

        private ThingDef def;
        private Rect drawingRect;
        private Rect drawnRect;

        public ClItemSpecifierScope(Window dialog)
        {
            this.dialog = dialog;
            Claim(SharedMenuGrammar.Info, e => OpenInfoCard());
            RegisterPopTeardown(searchSession.CancelIfActive);
        }

        public override string Name => "clItemSpecifier";

        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        protected override int ContentRegionCount => 3;

        private bool MadeFromStuff => def != null && def.MadeFromStuff;

        private string Title()
        {
            return "RimWorldAccess.Compat.CompositableLoadouts.ItemSpecifierTitle".Translate(def != null ? def.LabelCap.ToString() : "");
        }

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case SettingsRegion:
                    return Title();
                case StuffRegion:
                    return "Stat_Stuff_Name".Translate();
                default:
                    return CompatText.ModText("CompositableLoadouts.Statistics");
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return Title();
        }

        protected override bool ContentRegionAlwaysNavigable(int region)
        {
            return region == StatsRegion || ContentItemCount(region) > 0;
        }

        protected override void RefreshContent()
        {
            def = ClCompat.ItemSpecifier.Def(dialog);

            // DrawMemberSelector, DrawSliders and DrawPreviewQuality's own gates.
            settingsRows.Clear();
            if (ClCompat.ItemSpecifier.MemberCount(dialog) > 1)
            {
                settingsRows.Add(SettingsRow.Alternative);
            }
            bool hitPoints = def != null && def.useHitPoints;
            bool quality = def != null && def.HasComp(typeof(CompQuality));
            if (hitPoints)
            {
                settingsRows.Add(SettingsRow.HitPoints);
            }
            if (quality)
            {
                settingsRows.Add(SettingsRow.Quality);
            }
            if (hitPoints && quality)
            {
                settingsRows.Add(SettingsRow.SetDefault);
            }
            if (MadeFromStuff)
            {
                settingsRows.Add(SettingsRow.PreviewQuality);
            }

            stuffs.Clear();
            stuffs.AddRange(ClCompat.ItemSpecifier.VisibleStuffs(dialog));

            stats.Clear();
            stats.AddRange(ClCompat.ItemSpecifier.Stats(def));

            // DrawStatTableHeader's columns: the representative stuff, then the previewed one.
            columns.Clear();
            if (MadeFromStuff)
            {
                columns.Add(ClCompat.ItemSpecifier.RepresentativeStuff(dialog));
                ThingDef preview = ClCompat.ItemSpecifier.StuffPreview(dialog);
                if (preview != null)
                {
                    columns.Add(preview);
                }
            }
            else
            {
                columns.Add(null);
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case SettingsRegion:
                    return settingsRows.Count;
                case StuffRegion:
                    // The search box, then the stuffs, or the "No Material" label in their place.
                    return MadeFromStuff ? 1 + System.Math.Max(1, stuffs.Count) : 0;
                default:
                    return stats.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (region)
            {
                case SettingsRegion:
                    return index >= 0 && index < settingsRows.Count ? DescribeSetting(settingsRows[index]) : new ElementDescription();
                case StuffRegion:
                    return DescribeStuffRow(index);
                default:
                    return new ElementDescription { Label = index >= 0 && index < stats.Count ? stats[index].LabelCap.ToString() : "" };
            }
        }

        private ElementDescription DescribeSetting(SettingsRow row)
        {
            var d = new ElementDescription();
            switch (row)
            {
                case SettingsRow.Alternative:
                    d.Label = AlternativeLabel();
                    d.Role = ElementRole.ComboBox;
                    d.Extras = CompatText.Flatten(CompatText.ModText("CompositableLoadouts.EditingAlternativeDesc"));
                    break;
                case SettingsRow.HitPoints:
                    FloatRange hp = ClCompat.ItemSpecifier.HpRange(dialog);
                    d.Label = "RimWorldAccess.Inspection.BillConfig.Label.LabelWithRangePercent".Translate(
                        "HitPointsBasic".Translate().CapitalizeFirst(), hp.min.ToStringPercent(), hp.max.ToStringPercent());
                    d.Role = ElementRole.Button;
                    break;
                case SettingsRow.Quality:
                    QualityRange q = ClCompat.ItemSpecifier.QualityRange(dialog);
                    d.Label = "RimWorldAccess.Inspection.BillConfig.Label.LabelWithRangeQuality".Translate(
                        "Quality".Translate(), q.min.GetLabel(), q.max.GetLabel());
                    d.Role = ElementRole.Button;
                    break;
                case SettingsRow.SetDefault:
                    d.Label = CompatText.ModText("CompositableLoadouts.SetDefault");
                    d.Role = ElementRole.Button;
                    break;
                case SettingsRow.PreviewQuality:
                    QualityCategory preview = ClCompat.ItemSpecifier.PreviewQuality(dialog);
                    d.Label = CompatText.ModText("CompositableLoadouts.PreviewQuality").Trim();
                    d.Role = ElementRole.Stepper;
                    d.Value = preview.GetLabel();
                    d.AtMinimum = preview == QualityCategory.Awful;
                    d.AtMaximum = preview == QualityCategory.Legendary;
                    break;
            }
            return d;
        }

        private string AlternativeLabel()
        {
            return CompatText.ModArgs("CompositableLoadouts.EditingAlternative", def != null ? def.LabelCap.ToString() : "");
        }

        private ElementDescription DescribeStuffRow(int index)
        {
            var d = new ElementDescription();
            if (index == 0)
            {
                d.Label = CompatText.ModText("CompositableLoadouts.StuffFilter");
                d.Role = ElementRole.TextField;
                d.EntersEditOnAccept = true;
                string search = ClCompat.ItemSpecifier.StuffFilter(dialog);
                if (search.NullOrEmpty())
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = search;
                }
                return d;
            }
            ThingDef stuff = StuffAt(index);
            if (stuff == null)
            {
                d.Label = CompatText.ModText("CompositableLoadouts.NoMaterial");
                d.ReadOnly = true;
                return d;
            }
            d.Label = stuff.LabelCap;
            d.Role = ElementRole.Checkbox;
            d.Check = ClCompat.ItemSpecifier.StuffAllowed(dialog, stuff) ? CheckState.Checked : CheckState.Unchecked;
            return d;
        }

        private ThingDef StuffAt(int index)
        {
            int stuffIndex = index - 1;
            return stuffIndex >= 0 && stuffIndex < stuffs.Count ? stuffs[stuffIndex] : null;
        }

        // ---- The statistics table ----

        protected override int ContentColumnCount(int region)
        {
            return region == StatsRegion ? columns.Count : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            ThingDef stuff = column >= 0 && column < columns.Count ? columns[column] : null;
            string label = stuff != null
                ? stuff.LabelAsStuff.CapitalizeFirst()
                : def != null ? def.LabelCap.ToString() : "";
            return new TableColumnInfo(label);
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != StatsRegion || row < 0 || row >= stats.Count || column < 0 || column >= columns.Count)
            {
                return null;
            }
            StatDef stat = stats[row];
            QualityCategory quality = ClCompat.ItemSpecifier.PreviewQuality(dialog);
            if (!ClCompat.ItemSpecifier.TryStatValue(def, columns[column], quality, stat, out bool shown, out float value))
            {
                return null;
            }
            if (!shown)
            {
                return "-";
            }
            string text = stat.Worker.ValueToString(value, true, stat.toStringNumberSense);
            return IsHighest(stat, quality, value)
                ? "RimWorldAccess.Compat.CompositableLoadouts.HighestStatValue".Translate(text).ToString()
                : text;
        }

        /// <summary>DrawStatRows paints every value tied with the largest green once two columns compare.</summary>
        private bool IsHighest(StatDef stat, QualityCategory quality, float value)
        {
            if (columns.Count < 2)
            {
                return false;
            }
            float max = -1f;
            foreach (ThingDef stuff in columns)
            {
                if (ClCompat.ItemSpecifier.TryStatValue(def, stuff, quality, stat, out bool shown, out float other) && shown)
                {
                    max = Mathf.Max(max, other);
                }
            }
            return Mathf.Abs(value - max) < 0.001f;
        }

        protected override string ContentCellTip(int region, int row, int column)
        {
            if (region != StatsRegion || row < 0 || row >= stats.Count || column < 0 || column >= columns.Count)
            {
                return null;
            }
            string tip = ClCompat.ItemSpecifier.StatTip(def, columns[column], ClCompat.ItemSpecifier.PreviewQuality(dialog), stats[row]);
            return tip.NullOrEmpty() ? null : CompatText.Flatten(tip);
        }

        protected override string ContentRowTip(int region, int row)
        {
            return region == StatsRegion && row >= 0 && row < stats.Count && !stats[row].description.NullOrEmpty()
                ? CompatText.Flatten(stats[row].description)
                : null;
        }

        // ---- Activation ----

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == SettingsRegion)
            {
                if (index >= 0 && index < settingsRows.Count)
                {
                    ActivateSetting(settingsRows[index]);
                }
                return;
            }
            if (region != StuffRegion)
            {
                return;
            }
            if (index == 0)
            {
                BeginSearchEdit();
                return;
            }
            ThingDef stuff = StuffAt(index);
            if (stuff == null)
            {
                return;
            }
            bool allowed = ClCompat.ItemSpecifier.ToggleStuff(dialog, stuff);
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                new ElementDescription { Check = allowed ? CheckState.Checked : CheckState.Unchecked },
                TranslatedShellVocabulary.Instance));
        }

        private void ActivateSetting(SettingsRow row)
        {
            switch (row)
            {
                case SettingsRow.Alternative:
                    // DrawMemberSelector draws the window's first text button; its float menu sets the member.
                    ButtonTextCapture.RequestClick(0, this);
                    break;
                case SettingsRow.HitPoints:
                    RangeEditMenuState.OpenHitPointsRange(
                        () => ClCompat.ItemSpecifier.HpRange(dialog),
                        // MUTATION-C: mirrors Dialog_ItemSpecifier.DrawHitPointsSlider; the widget is a mouse drag.
                        v => ClCompat.ItemSpecifier.SetHpRange(dialog, v));
                    break;
                case SettingsRow.Quality:
                    RangeEditMenuState.OpenQualityRange(
                        () => ClCompat.ItemSpecifier.QualityRange(dialog),
                        // MUTATION-C: mirrors Dialog_ItemSpecifier.DrawQualitySlider; the widget is a mouse drag.
                        v => ClCompat.ItemSpecifier.SetQualityRange(dialog, v));
                    break;
                case SettingsRow.SetDefault:
                    // DrawSliders' button follows the alternative picker, the only text button before it.
                    ButtonTextCapture.RequestClick(settingsRows.Contains(SettingsRow.Alternative) ? 1 : 0, this);
                    FloatRange hp = ClCompat.ItemSpecifier.HpRange(dialog);
                    QualityRange q = ClCompat.ItemSpecifier.QualityRange(dialog);
                    TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.DefaultsSet".Loc(
                        hp.min.ToStringPercent(), hp.max.ToStringPercent(), q.min.GetLabel(), q.max.GetLabel()));
                    break;
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == SettingsRegion && index >= 0 && index < settingsRows.Count
                && settingsRows[index] == SettingsRow.PreviewQuality;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (!CanAdjustContentItem(region, index))
            {
                return;
            }
            QualityCategory preview = ClCompat.ItemSpecifier.StepPreviewQuality(dialog, direction);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                new ElementDescription
                {
                    Value = preview.GetLabel(),
                    AtMinimum = preview == QualityCategory.Awful,
                    AtMaximum = preview == QualityCategory.Legendary,
                },
                TranslatedShellVocabulary.Instance));
        }

        /// <summary>Landing on a stuff row previews it, as the mouse over it does.</summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (region == StuffRegion)
            {
                ThingDef stuff = StuffAt(index);
                if (stuff != null)
                {
                    ClCompat.ItemSpecifier.SetStuffPreview(dialog, stuff);
                }
            }
        }

        private void BeginSearchEdit()
        {
            searchSession.EnterEdit(ClCompat.ItemSpecifier.StuffFilter(dialog), SearchSpec,
                CompatText.ModText("CompositableLoadouts.StuffFilter"),
                value => ClCompat.ItemSpecifier.SetStuffFilter(dialog, value),
                () =>
                {
                    RefreshModel();
                    AnnounceCurrentItem();
                });
        }

        /// <summary>The header's info button for the focused column, or the previewed stuff's from its row.</summary>
        private void OpenInfoCard()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            ThingDef stuff = null;
            bool found = false;
            if (Model.RegionIndex == StatsRegion && Model.CurrentTable != null)
            {
                int column = Model.CurrentTable.ColumnIndex;
                found = column >= 0 && column < columns.Count;
                stuff = found ? columns[column] : null;
            }
            else if (Model.RegionIndex == StuffRegion && region != null)
            {
                stuff = StuffAt(region.Index);
                found = stuff != null;
            }
            if (!found || def == null)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            Find.WindowStack.Add(new Dialog_InfoCard(def, stuff));
        }

        // ---- The draw pass: live search text and the focus ring ----

        internal void BeginDrawPass()
        {
            searchSession.MirrorLive();
            drawnRect = drawingRect;
            drawingRect = default(Rect);
        }

        protected internal override Rect FocusedContentRect()
        {
            return drawnRect;
        }

        private bool CursorAt(int region, out int index)
        {
            ListModel current = Model.CurrentRegion;
            index = current != null ? current.Index : -1;
            return Model.RegionIndex == region && index >= 0;
        }

        private bool CursorOnSetting(SettingsRow row)
        {
            return CursorAt(SettingsRegion, out int index) && index < settingsRows.Count && settingsRows[index] == row;
        }

        internal void RecordMemberSelector(Rect titleRect)
        {
            if (CursorOnSetting(SettingsRow.Alternative))
            {
                Rect button = new Rect(titleRect.xMax + GenUI.GapSmall, titleRect.y,
                    AlternativeLabel().GetWidthCached() + ClCompat.ItemSpecifier.RowHeight, titleRect.height);
                Ring(button, button);
            }
        }

        internal void RecordSliders(Rect rect)
        {
            if (!CursorAt(SettingsRegion, out int index) || index >= settingsRows.Count || def == null)
            {
                return;
            }
            // DrawSliders stacks hit points, quality, then Set as default, each a GapTiny below the last.
            float step = rect.height + GenUI.GapTiny;
            int slot;
            switch (settingsRows[index])
            {
                case SettingsRow.HitPoints:
                    slot = 0;
                    break;
                case SettingsRow.Quality:
                    slot = def.useHitPoints ? 1 : 0;
                    break;
                case SettingsRow.SetDefault:
                    slot = 2;
                    break;
                default:
                    return;
            }
            Rect slider = new Rect(rect.x, rect.y + slot * step, rect.width, rect.height);
            Ring(slider, slider);
        }

        internal void RecordPreviewQuality(Rect rect)
        {
            if (CursorOnSetting(SettingsRow.PreviewQuality))
            {
                Ring(rect, rect);
            }
        }

        internal void RecordStuffList(Rect outRect, Vector2 scroll)
        {
            if (!CursorAt(StuffRegion, out int index))
            {
                return;
            }
            float rowHeight = ClCompat.ItemSpecifier.RowHeight;
            if (index == 0)
            {
                Rect search = outRect.TopPartPixels(rowHeight).ContractedBy(2f);
                Ring(search, search);
                return;
            }
            Rect list = new Rect(outRect.x, outRect.y + rowHeight, outRect.width, outRect.height - rowHeight);
            Rect row = stuffs.Count == 0
                ? list.TopPart(0.6f)
                : new Rect(list.x, list.y + (index - 1) * rowHeight - scroll.y, list.width - ClCompat.ItemSpecifier.ScrollWidth, rowHeight);
            Ring(row, list);
        }

        internal void RecordStats(Rect rect)
        {
            TableModel table = Model.CurrentTable;
            if (!CursorAt(StatsRegion, out int index) || table == null || columns.Count == 0)
            {
                return;
            }
            // DrawStats: a header row, then one row per stat; a third of the width names the stat,
            // the rest (less the scroll bar) splits in two value halves.
            float rowHeight = ClCompat.ItemSpecifier.RowHeight;
            float nameWidth = rect.width / 3f;
            float valueWidth = (rect.width - ClCompat.ItemSpecifier.ScrollWidth - nameWidth) / 2f;
            Vector2 scroll = ClCompat.ItemSpecifier.StatScroll(dialog);
            Rect cell = new Rect(rect.x + nameWidth + table.ColumnIndex * valueWidth,
                rect.y + index * rowHeight - scroll.y, valueWidth, rowHeight);
            Ring(cell, rect);
        }

        /// <summary>Publishes <paramref name="rect"/>, cut to the area its scroll view shows, in screen space.</summary>
        private void Ring(Rect rect, Rect clip)
        {
            float xMin = Mathf.Max(rect.xMin, clip.xMin);
            float yMin = Mathf.Max(rect.yMin, clip.yMin);
            float xMax = Mathf.Min(rect.xMax, clip.xMax);
            float yMax = Mathf.Min(rect.yMax, clip.yMax);
            drawingRect = xMax > xMin && yMax > yMin
                ? GuiSpace.VisibleScreenRect(Rect.MinMaxRect(xMin, yMin, xMax, yMax))
                : default(Rect);
        }

        /// <summary>The alternative picker and Set as default are content rows; the close X is an icon.</summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return rawLabel != AlternativeLabel() && rawLabel != CompatText.ModText("CompositableLoadouts.SetDefault");
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
