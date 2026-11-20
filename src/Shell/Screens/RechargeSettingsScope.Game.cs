using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <see cref="Dialog_RechargeSettings"/>
    /// (Biotech: the mechanitor control-group gizmo's power-icon dialog) — DLC
    /// coverage remediation, finding G1. The dialog is opened only from a
    /// mouse-hover-and-click on the gizmo's power icon
    /// (<c>MechanitorControlGroupGizmo.GizmoOnGUI</c>, decompiled-verified: an
    /// inline <c>Widgets.ButtonInvisible</c> check, not <c>Gizmo.ProcessInput</c>)
    /// — our own accessible gizmo handler
    /// (<see cref="MechanitorControlGroupGizmoHandler"/>) never opens this real
    /// window; it redirects straight to <see cref="MechControlGroupState"/>'s own
    /// bespoke recharge-range editor. This scope exists for the window itself,
    /// reachable by a mouse click (or any other opener, modded included) even
    /// though our own flow never takes that path — full parity means the real
    /// vanilla dialog must be readable and drivable whenever it does appear on
    /// the WindowStack.
    ///
    /// One content region ("Recharge threshold"), two Stepper rows (Minimum,
    /// Maximum) over the dialog's own private <c>range</c> field
    /// (<see cref="Dialog_RechargeSettings.DoWindowContents"/>, decompiled
    /// :48-50: <c>Widgets.FloatRange(..., 0f, 1f, null, ToStringStyle.PercentZero,
    /// 0.05f, ...)</c> then <c>GenMath.RoundTo(range.min/max, 0.01f)</c>). The
    /// Buttons region captures the dialog's own Cancel/Reset/OK
    /// <c>Widgets.ButtonText</c> calls (default <see cref="ScreenScope.CaptureWindowButtons"/>,
    /// no over-capture risk — the range slider is the only other widget) —
    /// clicking OK reads this SAME reflected field and commits
    /// <c>controlGroup.mechRechargeThresholds = range</c> through vanilla's own
    /// click branch (Category A), so the Left/Right step below only has to
    /// mirror the field write the widget itself performs, not the commit.
    /// </summary>
    public sealed class RechargeSettingsScope : ScreenScope
    {
        private const int ThresholdRegion = 0;
        private const int MinRow = 0;
        private const int MaxRow = 1;

        /// <summary>Vanilla's own drag-snap granularity for this widget (decompiled :48's <c>roundTo</c> argument).</summary>
        private const float Step = 0.05f;

        private static readonly AccessTools.FieldRef<Dialog_RechargeSettings, FloatRange> rangeField =
            AccessTools.FieldRefAccess<Dialog_RechargeSettings, FloatRange>("range");
        private static readonly Func<Window, float> MarginOf =
            AccessTools.MethodDelegate<Func<Window, float>>(AccessTools.PropertyGetter(typeof(Window), "Margin"));

        private readonly Dialog_RechargeSettings dialog;
        private bool announcedOpen;

        public RechargeSettingsScope(Dialog_RechargeSettings dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "recharge-settings"; }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData("MechRechargeSettingsExplanation".Translate().ToString());
            AnnounceCurrentItem();
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "MechRechargeSettingsTitle".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return 2;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            FloatRange range = rangeField(dialog);
            bool isMin = index == MinRow;
            float value = isMin ? range.min : range.max;

            ElementDescription d = new ElementDescription();
            d.Label = (isMin
                ? "RimWorldAccess.Biotech.Mech.RangeMinimum"
                : "RimWorldAccess.Biotech.Mech.RangeMaximum").Translate().ToString();
            d.Role = ElementRole.Stepper;
            d.Value = value.ToStringPercent();
            d.AtMinimum = isMin && value <= 0f;
            d.AtMaximum = !isMin && value >= 1f;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            // Flat stepper rows have no separate "activate" beyond adjusting;
            // without a type-in box Enter just re-reads the current value.
            AnnounceCurrentItem();
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return true;
        }

        protected override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            FloatRange range = rangeField(dialog);
            high = index == MaxRow;
            value = high ? range.max : range.min;
            return region == ThresholdRegion && (index == MinRow || high);
        }

        internal static bool CursorOnRange(Dialog_RechargeSettings window)
        {
            RechargeSettingsScope scope = TextDialogShared.ScopeOwning<RechargeSettingsScope>(
                window, delegate(RechargeSettingsScope s, Window w) { return ReferenceEquals(s.dialog, w); });
            ListModel region = scope?.Model.CurrentRegion;
            return region != null && !region.IsEmpty && scope.Model.RegionIndex == ThresholdRegion;
        }

        // MUTATION-C: mirrors Dialog_RechargeSettings.DoWindowContents (decompiled
        // :48-50) — Widgets.FloatRange has no gated setter of its own, vanilla
        // writes range.min/max directly and then clamps each bound against the
        // other. No Can*/Try* vehicle exists; the OK button's own commit
        // (controlGroup.mechRechargeThresholds = range) still runs through the
        // vanilla click branch via ButtonTextCapture (Category A) once this
        // field is written.
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            FloatRange range = rangeField(dialog);
            float delta = Step * direction;
            if (index == MinRow)
            {
                range.min = Mathf.Clamp(GenMath.RoundTo(range.min + delta, 0.01f), 0f, range.max);
            }
            else
            {
                range.max = Mathf.Clamp(GenMath.RoundTo(range.max + delta, 0.01f), range.min, 1f);
            }
            rangeField(dialog) = range;
            AnnounceCurrentItem();
        }

        // One Widgets.FloatRange (:48) rings for either row; vanilla exposes no per-thumb geometry.
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || Model.RegionIndex != ThresholdRegion)
            {
                return default(Rect);
            }

            Rect inRect = WindowContentRect();
            GameFont font = Text.Font;
            Text.Font = GameFont.Small;
            float explanationHeight = Text.CalcHeight(
                "MechRechargeSettingsExplanation".Translate().ToString(), inRect.width);
            Text.Font = font;

            // mirrors Dialog_RechargeSettings.DoWindowContents' y accumulation (decompiled :34-48)
            float y = inRect.y + 30f + 17f + explanationHeight + 17f;
            return GuiSpace.ToScreen(new Rect(inRect.x, y, inRect.width, 30f));
        }

        /// <summary>
        /// The rect InnerWindowOnGUI hands DoWindowContents: the window
        /// contracted by its own Margin. Dialog_RechargeSettings sets no
        /// optionalTitle, so there is no title-bar offset to add.
        /// </summary>
        private Rect WindowContentRect()
        {
            float margin = MarginOf(dialog);
            return new Rect(0f, 0f, dialog.windowRect.width, dialog.windowRect.height)
                .ContractedBy(margin);
        }
    }

    /// <summary>The dialog's body draws only the recharge FloatRange, so it reports that control's focus.</summary>
    [HarmonyPatch(typeof(Dialog_RechargeSettings), nameof(Dialog_RechargeSettings.DoWindowContents))]
    public static class RechargeSettingsSliderFocusPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_RechargeSettings __instance)
        {
            SoleSliderFocus.Enter(RechargeSettingsScope.CursorOnRange(__instance));
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            SoleSliderFocus.Exit();
        }
    }
}
