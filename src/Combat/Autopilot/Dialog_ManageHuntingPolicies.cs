using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Hunting-policy manager on vanilla's <see cref="Dialog_ManagePolicies{T}"/> chassis,
    /// shaped like the bill ingredient filter: a revenge-chance range slider and the
    /// spare-venerated checkbox above the allowed-animals filter. Rule-row rects are
    /// recorded at draw for the focus ring.
    /// </summary>
    public class Dialog_ManageHuntingPolicies : Dialog_ManagePolicies<HuntingPolicy>
    {
        public const int SliderRowIndex = 0;
        public const int VeneratedRowIndex = 1;

        private const float RowHeight = 32f;
        private const float RowGap = 6f;
        private const float FilterGap = 10f;
        private const int RuleRowCount = 2;

        private readonly ThingFilterUI.UIState thingFilterState = new ThingFilterUI.UIState();

        private int recordedFrame = -1;
        private readonly Rect[] rowGuiRects = new Rect[RuleRowCount];
        private readonly Rect[] rowScreenRects = new Rect[RuleRowCount];
        private readonly GuiSpace.ClipKey[] rowClips = new GuiSpace.ClipKey[RuleRowCount];

        public Dialog_ManageHuntingPolicies(HuntingPolicy policy) : base(policy)
        {
        }

        /// <summary>Venerated animals exist only under Ideology; without it the rule is inert and hidden.</summary>
        public static bool ShowVeneratedRow
        {
            get { return ModsConfig.IdeologyActive; }
        }

        protected override string TitleKey
        {
            get { return "RimWorldAccess.Autopilot.HuntPolicyTitle"; }
        }

        protected override string TipKey
        {
            get { return "RimWorldAccess.Autopilot.HuntPolicyTip"; }
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(700f, 700f); }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            thingFilterState.quickSearch.Reset();
        }

        protected override HuntingPolicy CreateNewPolicy()
        {
            return CombatAutopilotComponent.MakeNewHuntingPolicy();
        }

        protected override HuntingPolicy GetDefaultPolicy()
        {
            return CombatAutopilotComponent.DefaultHuntingPolicy();
        }

        protected override void SetDefaultPolicy(HuntingPolicy policy)
        {
            CombatAutopilotComponent.SetDefaultHuntingPolicy(policy);
        }

        protected override AcceptanceReport TryDeletePolicy(HuntingPolicy policy)
        {
            return CombatAutopilotComponent.TryDeleteHuntingPolicy(policy);
        }

        protected override List<HuntingPolicy> GetPolicies()
        {
            return CombatAutopilotComponent.AllHuntingPolicies;
        }

        protected override void DoContentsRect(Rect rect)
        {
            HuntingPolicy policy = SelectedPolicy;
            if (policy == null)
            {
                Widgets.DrawMenuSection(rect);
                return;
            }
            recordedFrame = Time.frameCount;
            float y = rect.y;

            var sliderRow = new Rect(rect.x, y, rect.width, RowHeight);
            y += RowHeight + RowGap;
            FloatRange range = policy.RevengeChance;
            // Own key: vanilla's HarmedRevengeChance label has no {0}, which would hide the range.
            bool edited = RangeEditMenuState.EnterRangeDraw(policy, RangeEditMenuState.RangeType.Percent);
            try
            {
                Widgets.FloatRange(sliderRow, GetHashCode(), ref range, 0f, 1f,
                    "RimWorldAccess.Autopilot.HuntRevengeRange.SliderLabel", ToStringStyle.PercentOne);
            }
            finally
            {
                if (edited)
                {
                    RangeEditMenuState.ExitRangeDraw();
                }
            }
            policy.RevengeChance = range;
            RecordRow(SliderRowIndex, sliderRow);

            if (ShowVeneratedRow)
            {
                var veneratedRow = new Rect(rect.x, y, rect.width, RowHeight);
                y += RowHeight + RowGap;
                bool spare = policy.SpareVenerated;
                Widgets.CheckboxLabeled(veneratedRow,
                    "RimWorldAccess.Autopilot.HuntSpareVenerated.Label".Translate(), ref spare);
                policy.SpareVenerated = spare;
                if (Mouse.IsOver(veneratedRow))
                {
                    TooltipHandler.TipRegion(veneratedRow,
                        "RimWorldAccess.Autopilot.HuntSpareVenerated.Desc".Translate());
                }
                RecordRow(VeneratedRowIndex, veneratedRow);
            }

            var filterRect = new Rect(rect.x, y + FilterGap, rect.width, rect.yMax - y - FilterGap);
            ThingFilterUI.DoThingFilterConfigWindow(filterRect, thingFilterState, policy.AllowedAnimals,
                HuntingPolicy.AnimalGlobalFilter, 1, null, null, forceHideHitPointsConfig: true);
        }

        private void RecordRow(int index, Rect row)
        {
            rowGuiRects[index] = row;
            rowScreenRects[index] = GuiSpace.ToScreen(row);
            rowClips[index] = GuiSpace.CurrentClip();
        }

        /// <summary>Rule row rect in absolute UI points for the focus ring; empty when not drawn recently.</summary>
        internal Rect RowScreenRect(int index)
        {
            if (index < 0 || index >= rowGuiRects.Length || Time.frameCount - recordedFrame > 1)
            {
                return default(Rect);
            }
            return GuiSpace.VisibleScreenRectFrom(rowGuiRects[index], rowScreenRects[index], rowClips[index], rowGuiRects[index]);
        }
    }
}
