using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The <see cref="PolicyDialogScope"/> for <see cref="Dialog_ManageHuntingPolicies"/>:
    /// one filter-tree region shaped like the bill ingredient filter — the revenge-chance
    /// range and spare-venerated rows lead, then Clear all / Allow all, then the animal
    /// tree. Rule state is read directly; rule-row rects come from the dialog's draw.
    /// </summary>
    public sealed class HuntingPolicyDialogScope : PolicyDialogScope
    {
        private const int AnimalsRegion = FirstContentsRegion;
        private const int SliderRow = 0;
        private const int VeneratedRow = 1;

        private readonly TreePanel animalsPanel;
        private HuntingPolicy policy;

        public HuntingPolicyDialogScope(Window dialog) : base(dialog)
        {
            animalsPanel = CreatePanel();
            // Muscle-memory shortcuts to the same activation logic the ClearAll/AllowAll
            // prefix rows use; the single filter region is the target from anywhere.
            Claim("thingFilter.allowAll", e => ActivateFilterShortcut(allow: true));
            Claim("thingFilter.disallowAll", e => ActivateFilterShortcut(allow: false));
        }

        private void ActivateFilterShortcut(bool allow)
        {
            if (policy == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (allow)
            {
                ActivateAllowAllRow(AnimalsRegion);
            }
            else
            {
                ActivateClearAllRow(AnimalsRegion);
            }
        }

        protected override int ContentsRegionCount
        {
            get { return 1; }
        }

        protected override string ContentsRegionName(int region)
        {
            return "RimWorldAccess.Autopilot.HuntPolicyAnimalsRegion".Translate().ToString();
        }

        protected override int TreeRegionIndex
        {
            get { return AnimalsRegion; }
        }

        protected override string TreeRegionLabel
        {
            get { return ContentsRegionName(AnimalsRegion); }
        }

        protected override TreePanel PanelFor(int region)
        {
            return region == AnimalsRegion ? animalsPanel : null;
        }

        protected override ThingFilterSessionCore.FilterContext BuildFilterContext(int region)
        {
            if (region != AnimalsRegion || policy == null)
            {
                return default(ThingFilterSessionCore.FilterContext);
            }
            return new ThingFilterSessionCore.FilterContext
            {
                CurrentFilter = policy.AllowedAnimals,
                ParentFilter = HuntingPolicy.AnimalGlobalFilter,
                DisplayRoot = HuntingPolicy.AnimalGlobalFilter.DisplayRootCategory,
            };
        }

        protected override ThingFilter FilterForRegion(int region)
        {
            return region == AnimalsRegion && policy != null ? policy.AllowedAnimals : null;
        }

        /// <summary>Animals carry neither range; the dialog force-hides hit points and quality never resolves.</summary>
        protected override bool ShowHitPointsRange
        {
            get { return false; }
        }

        protected override bool ShowQualityRange
        {
            get { return false; }
        }

        protected override void RebuildContents()
        {
            policy = Selected as HuntingPolicy;
            SetTreeRoot(animalsPanel, policy == null
                ? ThingFilterSessionCore.EmptyTreeRoot()
                : ThingFilterSessionCore.BuildCategoryTreeRoot(ContextForBuild(AnimalsRegion)));
        }

        // --- The rule rows leading the filter region: revenge-chance range, spare venerated. ---

        protected override int SubclassLeadingRowCountFor(int region)
        {
            if (region != AnimalsRegion || policy == null)
            {
                return 0;
            }
            return Dialog_ManageHuntingPolicies.ShowVeneratedRow ? 2 : 1;
        }

        protected override ElementDescription DescribeSubclassLeadingRow(int index)
        {
            var d = new ElementDescription();
            if (policy == null)
            {
                return d;
            }
            if (index == SliderRow)
            {
                d.Label = "HarmedRevengeChance".Translate().ToString();
                d.Role = ElementRole.Slider;
                d.Value = "RimWorldAccess.Shell.FilterTree.RangeValue".Translate(
                    policy.RevengeChance.min.ToString("P1"), policy.RevengeChance.max.ToString("P1"));
                d.Extras = "RimWorldAccess.Autopilot.HuntRevengeRange.Desc".Translate();
                d.EntersEditOnAccept = true;
            }
            else if (index == VeneratedRow)
            {
                d.Label = "RimWorldAccess.Autopilot.HuntSpareVenerated.Label".Translate();
                d.Extras = "RimWorldAccess.Autopilot.HuntSpareVenerated.Desc".Translate();
                d.Role = ElementRole.Checkbox;
                d.Check = policy.SpareVenerated ? CheckState.Checked : CheckState.Unchecked;
            }
            return d;
        }

        protected override void ActivateSubclassLeadingRow(int index)
        {
            if (policy == null)
            {
                return;
            }
            if (index == SliderRow)
            {
                HuntingPolicy edited = policy;
                RangeEditMenuState.OpenPercentRange(
                    "HarmedRevengeChance".Translate().ToString(),
                    () => edited.RevengeChance,
                    v => edited.RevengeChance = v);
                RangeEditMenuState.OfferTypeInBox(edited);
                return;
            }
            if (index == VeneratedRow)
            {
                policy.SpareVenerated = !policy.SpareVenerated;
                (policy.SpareVenerated ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff)
                    .PlayOneShotOnCamera();
                AnnounceCurrentItem();
            }
        }

        protected internal override Rect FocusedContentRect()
        {
            ListModel rows = Model.RegionIndex == AnimalsRegion ? Model.Region(AnimalsRegion) : null;
            if (rows == null || rows.Index >= SubclassLeadingRowCountFor(AnimalsRegion)
                || !(dialog is Dialog_ManageHuntingPolicies own))
            {
                return base.FocusedContentRect();
            }
            int dialogRow = rows.Index == SliderRow
                ? Dialog_ManageHuntingPolicies.SliderRowIndex
                : Dialog_ManageHuntingPolicies.VeneratedRowIndex;
            Rect rect = own.RowScreenRect(dialogRow);
            return rect.width > 0f ? rect : base.FocusedContentRect();
        }
    }
}
