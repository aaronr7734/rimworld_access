using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for vanilla's <see cref="Dialog_MedicalDefaults"/>. One TABLE region
    /// mirroring vanilla's grid: a row per group the dialog draws, recorded from its own DoRow so
    /// DLC-gated groups follow vanilla, and a RadioButton column per care icon. Enter posts a live
    /// activation on that icon's captured hotspot, so vanilla's own press branch writes the
    /// setting and plays its sound.
    /// </summary>
    public sealed class MedicalDefaultsScope : ScreenScope
    {
        private sealed class GroupRow
        {
            public string LabelKey;
            public string TipKey;
            public MedicalCareCategory Care;
        }

        private static readonly MedicalCareCategory[] Levels =
            (MedicalCareCategory[])Enum.GetValues(typeof(MedicalCareCategory));

        private readonly Dialog_MedicalDefaults dialog;

        // Upserted, never cleared per pass: a key handler can run while a draw pass is half done.
        private readonly List<GroupRow> groups = new List<GroupRow>();

        internal static MedicalDefaultsScope Current { get; private set; }

        public MedicalDefaultsScope(Dialog_MedicalDefaults dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "medical-defaults"; }
        }

        // The widget pass is what Enter's activation and the focus ring ride on; the care icons
        // themselves never reach the extras region, since the column headers mirror them.
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return GizmoTextUtility.FlattenNewlines("DefaultMedicineSettingsDesc".Translate());
        }

        internal void RecordRow(Dialog_MedicalDefaults instance, string labelKey, string tipKey, MedicalCareCategory care)
        {
            if (!ReferenceEquals(instance, dialog))
            {
                return;
            }
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].LabelKey == labelKey)
                {
                    groups[i].Care = care;
                    return;
                }
            }
            groups.Add(new GroupRow { LabelKey = labelKey, TipKey = tipKey, Care = care });
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "DefaultMedicineSettings".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return groups.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < groups.Count)
            {
                d.Label = groups[index].LabelKey.Translate().ToString();
            }
            return d;
        }

        protected override int ContentColumnCount(int region)
        {
            return Levels.Length;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo(WidgetCapture.MedicalCareChoiceLabel(Levels[column]), cellRole: ElementRole.RadioButton);
        }

        protected override bool? ContentCellSelected(int region, int row, int column)
        {
            if (row < 0 || row >= groups.Count)
            {
                return null;
            }
            return groups[row].Care == Levels[column];
        }

        protected override string ContentRowTip(int region, int row)
        {
            if (row < 0 || row >= groups.Count)
            {
                return null;
            }
            return GizmoTextUtility.FlattenNewlines(groups[row].TipKey.Translate());
        }

        // Each group draws one strip, in row order, so the row index is the icon's ordinal among
        // same-labelled hotspots.
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= groups.Count)
            {
                return false;
            }
            WidgetCapture.RequestActivate(WidgetKind.InvisibleButton, WidgetCapture.MedicalCareChoiceLabel(Levels[column]), row);
            var d = new ElementDescription { Selected = true };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
            return true;
        }

        /// <summary>Every cell handles its own Enter; a row has no separate default action.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
        }

        protected internal override Rect FocusedContentRect()
        {
            TableModel table = Model.CurrentTable;
            if (table == null)
            {
                return default(Rect);
            }
            int row = table.Rows.Index - 1;
            if (row < 0 || row >= groups.Count)
            {
                return default(Rect);
            }
            return FindCapturedWidgetRect(WidgetKind.InvisibleButton,
                WidgetCapture.MedicalCareChoiceLabel(Levels[table.ColumnIndex]), row);
        }

        public override void OnPush()
        {
            base.OnPush();
            groups.Clear();
            Current = this;
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }
    }
}
