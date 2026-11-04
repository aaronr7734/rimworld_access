using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The bill menu rows for the mod's "X per Tag" repeat mode, in the order <c>Panel_BillConfig.Draw</c>
    /// draws them: the target-tag button, then (once a tag is set) the owned-versus-desired count, the
    /// "( colonists + extra copies ) * per colonist" formula as three rows, the copy-from-tag button, and
    /// the vanilla filter rows the panel redraws.
    /// </summary>
    internal static class ClBillRows
    {
        private const string Site = "Panel_BillConfig.Draw#"; // l10n-exempt: ring token

        internal const string TagButtonKey = Site + "tagButton"; // l10n-exempt: ring token
        internal const string CurrentlyHaveKey = Site + "currentlyHave"; // l10n-exempt: ring token
        internal const string FormulaKey = Site + "formula"; // l10n-exempt: ring token
        internal const string CopyFromTagKey = Site + "copyFromTag"; // l10n-exempt: ring token

        // Widgets.TextFieldNumeric's defaults, which the panel's two count boxes keep; plain text boxes, so steps are 1.
        private const int CountMin = 0;
        private const int CountMax = 1000000000;

        public static IEnumerable<BillConfigExtraRow> RowsFor(Bill_Production bill)
        {
            if (!ClCompat.Bills.IsPerTag(bill))
            {
                yield break;
            }

            object tag = ClCompat.Bills.TagFor(bill);
            yield return TagButton(bill, tag);
            if (tag == null)
            {
                yield break;
            }

            yield return new BillConfigExtraRow
            {
                Label = () => BillConfigState.CurrentlyHaveLabel(ClCompat.Bills.DesiredTargetCount(bill).ToString()),
                RingKey = CurrentlyHaveKey,
                RingTripwire = () => DrawnCurrentlyHave(bill),
            };
            yield return new BillConfigExtraRow
            {
                Label = () => "RimWorldAccess.Compat.CompositableLoadouts.Bills.ColonistCount".Translate(
                    ClCompat.Bills.ColonistCountFor(bill)).ToString(),
                Description = CompatText.ModTip("CompositableLoadouts.NumTagsDesc"),
                RingKey = FormulaKey,
                RingTripwire = () => BillConfigRowKeys.RawBand,
            };
            yield return CountRow(
                "RimWorldAccess.Compat.CompositableLoadouts.Bills.ExtraCopies",
                "CompositableLoadouts.ExtraCopiesDesc",
                () => bill.repeatCount,
                // MUTATION-C: mirrors Panel_BillConfig.cs:84's TextFieldNumeric on repeatCount (min 0); the box is raw IMGUI with no setter.
                value => bill.repeatCount = value);
            yield return CountRow(
                "RimWorldAccess.Compat.CompositableLoadouts.Bills.PerColonist",
                "CompositableLoadouts.ItemRepetitionDesc",
                () => bill.targetCount,
                // MUTATION-C: mirrors Panel_BillConfig.cs:86's TextFieldNumeric on targetCount (min 0); the box is raw IMGUI with no setter.
                value => bill.targetCount = value);

            ThingDef produced = bill.recipe.ProducedThingDef;
            if (produced == null)
            {
                yield break;
            }

            object item = ClCompat.Bills.ItemFor(tag, produced);
            if (item != null)
            {
                yield return CopyFromTag(bill, tag, item, produced);
            }
            if (produced.IsWeapon || produced.IsApparel)
            {
                yield return BillConfigExtraRow.Reuse(BillConfigExtraRow.StandardRow.IncludeEquipped);
            }
            if (produced.IsApparel && produced.apparel.careIfWornByCorpse)
            {
                yield return BillConfigExtraRow.Reuse(BillConfigExtraRow.StandardRow.IncludeTainted);
            }
            if (bill.recipe.products.Any(p => p.thingDef.useHitPoints))
            {
                yield return BillConfigExtraRow.Reuse(BillConfigExtraRow.StandardRow.HpRange);
            }
            if (produced.HasComp(typeof(CompQuality)))
            {
                yield return BillConfigExtraRow.Reuse(BillConfigExtraRow.StandardRow.QualityRange);
            }
            if (produced.MadeFromStuff)
            {
                yield return BillConfigExtraRow.Reuse(BillConfigExtraRow.StandardRow.LimitToAllowedStuff);
            }
        }

        private static BillConfigExtraRow TagButton(Bill_Production bill, object tag)
        {
            Func<string> label = () => tag == null
                ? CompatText.ModText("CompositableLoadouts.PickTargetTag")
                : CompatText.ModArgs("CompositableLoadouts.SwitchTargetTag", ClCompat.TagName(tag));
            return new BillConfigExtraRow
            {
                Label = label,
                Role = ElementRole.Button,
                Editable = true,
                Execute = () => OpenTagSelector(bill, tag),
                RingKey = TagButtonKey,
                RingTripwire = () => BillConfigRowKeys.RawBand,
            };
        }

        /// <summary>
        /// The mod's own picker, offered every tag but the current one. The bill dialog's scope stands
        /// down while it is open and re-announces the tag row when it closes.
        /// </summary>
        private static void OpenTagSelector(Bill_Production bill, object current)
        {
            List<object> tags = ClCompat.AllTags();
            tags.Remove(current);
            ClCompat.OpenTagSelector(tags, selected =>
            {
                // MUTATION-C: mirrors Panel_BillConfig.cs:25-29; the selector's callback is a lambda inside the mouse-only button body.
                if (current == null && bill.suspended)
                {
                    bill.suspended = false;
                }
                ClCompat.Bills.SetTagForBill(bill, selected);
            }, false, null);
        }

        private static BillConfigExtraRow CountRow(string nameKey, string tipKey,
            Func<int> get, Action<int> set)
        {
            return new BillConfigExtraRow
            {
                Label = () => "RimWorldAccess.Inspection.BillConfig.Label.LabelWithValue".Translate(
                    nameKey.Translate(), get()).ToString(),
                SearchLabel = () => nameKey.Translate().ToString(),
                Description = CompatText.ModTip(tipKey),
                Role = ElementRole.Stepper,
                Editable = true,
                Adjust = (direction, multiplier) => AdjustCount(get, set, direction * multiplier),
                JumpMin = () => JumpCountToMin(get, set),
                JumpMax = () => TolkHelper.Speak("RimWorldAccess.Inspection.BillConfig.Action.NoMaximumLimit".Loc()),
                NumericText = () => get().ToString(),
                ApplyNumeric = value => set(Mathf.Clamp(value, CountMin, CountMax)),
                RingKey = FormulaKey,
                RingTripwire = () => BillConfigRowKeys.RawBand,
            };
        }

        private static void AdjustCount(Func<int> get, Action<int> set, int delta)
        {
            int old = get();
            int value = Mathf.Clamp(old + delta, CountMin, CountMax);
            if (value == old)
            {
                NumericStepperHelper.SpeakBoundary(Math.Sign(delta));
                return;
            }
            set(value);
            if (value == CountMin)
            {
                NumericStepperHelper.SpeakValueAtMinimum(value.ToString());
            }
            else
            {
                BillConfigState.SpeakValueChange(value.ToString());
            }
        }

        private static void JumpCountToMin(Func<int> get, Action<int> set)
        {
            if (get() == CountMin)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Minimum);
                return;
            }
            set(CountMin);
            NumericStepperHelper.SpeakValueAtMinimum(CountMin.ToString());
        }

        private static BillConfigExtraRow CopyFromTag(Bill_Production bill, object tag, object item, ThingDef produced)
        {
            Func<string> label = () => CompatText.ModArgs("CompositableLoadouts.CopyFromTag", produced.LabelCap);
            return new BillConfigExtraRow
            {
                Label = label,
                Role = ElementRole.Button,
                Editable = true,
                Execute = () =>
                {
                    // MUTATION-C: mirrors Panel_BillConfig.cs:97-98; the button body is inline in the panel's draw.
                    ClCompat.Bills.CopyItemTo(item, bill);
                    TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.Bills.CopiedFromTag".Loc(
                        produced.LabelCap.ToString(), ClCompat.TagName(tag)));
                },
                RingKey = CopyFromTagKey,
                RingTripwire = label,
            };
        }

        /// <summary>The caption the panel draws above its formula (Panel_BillConfig.cs:39-42), the ring's tripwire.</summary>
        private static string DrawnCurrentlyHave(Bill_Production bill)
        {
            string text = $"{"CurrentlyHave".Translate()}: {bill.recipe.WorkerCounter.CountProducts(bill)} / {ClCompat.Bills.DesiredTargetCount(bill)}";
            string products = bill.recipe.WorkerCounter.ProductsDescription(bill);
            if (!products.NullOrEmpty())
            {
                text += "\n" + "CountingProducts".Translate() + ": " + products.CapitalizeFirst();
            }
            return text;
        }
    }
}
