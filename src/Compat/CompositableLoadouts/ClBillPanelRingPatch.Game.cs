using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Marks the nine row calls of the panel the mod draws into Dialog_BillConfig, so the bill menu's
    /// listing ring lands on them (<see cref="ClBillRows"/> names the same tokens). The three vanilla
    /// checkboxes carry vanilla's own key literals and the two range bands reuse vanilla's tokens, so
    /// the reused vanilla rows ring with no extra mapping.
    /// </summary>
    internal static class ClBillPanelRingPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[]
            {
                BillConfigRowCalls.ButtonText, BillConfigRowCalls.Label,
                BillConfigRowCalls.CheckboxLabeled, BillConfigRowCalls.GetRect,
            },
            Sites = new[]
            {
                Synthetic(BillConfigRowCalls.GetRect, ClBillRows.TagButtonKey),
                Synthetic(BillConfigRowCalls.Label, ClBillRows.CurrentlyHaveKey),
                Synthetic(BillConfigRowCalls.GetRect, ClBillRows.FormulaKey),
                Synthetic(BillConfigRowCalls.ButtonText, ClBillRows.CopyFromTagKey),
                Harvested(BillConfigRowCalls.CheckboxLabeled, BillConfigRowKeys.IncludeEquipped),
                Harvested(BillConfigRowCalls.CheckboxLabeled, BillConfigRowKeys.IncludeTainted),
                Synthetic(BillConfigRowCalls.GetRect, BillConfigRowKeys.HpRange),
                Synthetic(BillConfigRowCalls.GetRect, BillConfigRowKeys.QualityRange),
                Harvested(BillConfigRowCalls.CheckboxLabeled, BillConfigRowKeys.LimitToAllowedStuff),
            },
        };

        public static void Install(Harmony harmony)
        {
            MethodInfo target = ClCompat.Bills.PanelDraw;
            if (target != null)
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ClBillPanelRingPatch), nameof(Transpiler)));
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }

        private static MarkerSite Harvested(MethodInfo rowCall, string key)
        {
            return new MarkerSite { RowCall = rowCall, Payload = MarkerPayload.HarvestedKey, ExpectedKey = key };
        }

        private static MarkerSite Synthetic(MethodInfo rowCall, string key)
        {
            return new MarkerSite { RowCall = rowCall, Payload = MarkerPayload.SyntheticKey, ExpectedKey = key };
        }
    }
}
