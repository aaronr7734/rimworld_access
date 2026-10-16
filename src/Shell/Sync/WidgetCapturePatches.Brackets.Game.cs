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
    /// <summary>
    /// Brackets Listing_Standard.ButtonTextLabeledPct so its inner Widgets.ButtonText records with
    /// DropdownOpener set, the same signal a real Widgets.Dropdown opener carries. The non-Pct
    /// ButtonTextLabeled overload delegates straight into this one, so one bracket covers both public
    /// overloads. Concrete and non-generic, so it patches through the attribute.
    /// </summary>
    [HarmonyPatch(typeof(Listing_Standard), "ButtonTextLabeledPct",
        new Type[] { typeof(string), typeof(string), typeof(float), typeof(TextAnchor), typeof(string), typeof(string), typeof(Texture2D) })]
    internal static class WidgetCaptureLabeledButtonBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterLabeledButton();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitLabeledButton();
        }
    }

    // -----------------------------------------------------------------------
    // Numeric text-field family brackets. NONE of these record a row — every member funnels through
    // the already-patched Widgets.TextField/TextArea/Label primitives, so a recorder here would
    // double-capture. Each bracket only stashes the call site's own context (label, int-ness,
    // min/max, percent/vector-ness) so the inner primitive records itself enriched.
    // Listing_Standard's TextEntry/TextFieldNumeric wrappers are thin GetRect+delegate forwards over
    // these same Widgets methods and need nothing of their own.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Shared closed-generic resolver for the TextFieldNumeric family: reflection over the generic
    /// method definition, then MakeGenericMethod, applied once per value-type instantiation because
    /// Mono shares compiled bodies across reference types only. int and float are the whole legal
    /// universe — vanilla's ResolveParseNow&lt;T&gt; (Widgets.cs:1880) Log.Errors on any other T.
    /// NOT the open-generic technique, which silently skips value-type instantiations.
    /// </summary>
    internal static class NumericFieldBracketTargets
    {
        internal static MethodBase Closed(string name, Type valueType)
        {
            foreach (MethodInfo method in typeof(Widgets).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == name && method.IsGenericMethodDefinition)
                {
                    return method.MakeGenericMethod(valueType);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Widgets.TextFieldNumeric&lt;int&gt; — the body is one Widgets.TextField call plus re-parse and
    /// clamp (Widgets.cs:1862-1877), so the row is already captured; the bracket stashes int-ness and
    /// the call's own min/max. TargetMethod because the by-ref parameters rule out the attribute
    /// Type[] form (CS0182); the prefix binds min/max by name.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureTextFieldNumericIntBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return NumericFieldBracketTargets.Closed("TextFieldNumeric", typeof(int));
        }

        [HarmonyPrefix]
        public static void Prefix(float min, float max)
        {
            WidgetCapture.EnterNumericField(isInt: true, min: min, max: max);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitNumericField();
        }
    }

    /// <summary>Widgets.TextFieldNumeric&lt;float&gt; — the float sibling of the int bracket above (value types never share a compiled body).</summary>
    [HarmonyPatch]
    internal static class WidgetCaptureTextFieldNumericFloatBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return NumericFieldBracketTargets.Closed("TextFieldNumeric", typeof(float));
        }

        [HarmonyPrefix]
        public static void Prefix(float min, float max)
        {
            WidgetCapture.EnterNumericField(isInt: false, min: min, max: max);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitNumericField();
        }
    }

    /// <summary>
    /// Widgets.TextFieldNumericLabeled&lt;int|float&gt; — body is Label + TextFieldNumeric
    /// (Widgets.cs:2027-2036); the inner numeric bracket supplies int-ness and bounds, so one class
    /// with plural TargetMethods covers both closed forms. The caption is suppressed as an orphan
    /// Label row and carried on the field's row as FieldLabel: capture-side and deterministic, where
    /// the scope's geometric adjacency fusion is a heuristic.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureTextFieldNumericLabeledBracketPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return NumericFieldBracketTargets.Closed("TextFieldNumericLabeled", typeof(int));
            yield return NumericFieldBracketTargets.Closed("TextFieldNumericLabeled", typeof(float));
        }

        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            WidgetCapture.EnterFieldLabel(label);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.ExitFieldLabel();
        }
    }

    /// <summary>
    /// Widgets.TextFieldPercent — body is Label("%") + TextFieldNumeric&lt;float&gt; over val*100
    /// with min*100/max*100 (Widgets.cs:2038-2049), so the inner bracket harvests bounds already in
    /// the percent domain the player types in. The "%" caption is suppressed; the row's own
    /// NumericPercent flag speaks it. TargetMethod because of the by-ref parameters.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureTextFieldPercentBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "TextFieldPercent",
                new Type[] { typeof(Rect), typeof(float).MakeByRefType(), typeof(string).MakeByRefType(), typeof(float), typeof(float) });
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterPercentField();
            WidgetCapture.EnterSelfCaptioned("%");
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.ExitPercentField();
        }
    }

    /// <summary>
    /// Widgets.TextEntryLabeled — body is Label + TextField or TextArea by rect height
    /// (Widgets.cs:1792-1805). Both branches reach RecordTextField, so one bracket carries the caption
    /// into either; suppression and carry exactly as the NumericLabeled bracket above.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "TextEntryLabeled",
        new Type[] { typeof(Rect), typeof(string), typeof(string) })]
    internal static class WidgetCaptureTextEntryLabeledBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            WidgetCapture.EnterFieldLabel(label);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.ExitFieldLabel();
        }
    }

    /// <summary>
    /// Widgets.TextFieldVector — body is three TextFieldNumeric&lt;float&gt; calls in x, y, z order
    /// (Widgets.cs:1845-1860), otherwise three indistinguishable unlabeled fields; the bracket names
    /// them per-axis via the numeric bracket's axis counter. TargetMethod because of the by-refs.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureTextFieldVectorBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "TextFieldVector",
                new Type[] { typeof(Rect), typeof(Vector3).MakeByRefType(), typeof(string[]).MakeByRefType(), typeof(float), typeof(float) });
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterVectorField();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitVectorField();
        }
    }

    // -----------------------------------------------------------------------
    // Stepper brackets. Same doctrine as the numeric text-field brackets: NO recorder — the
    // composites' ButtonText calls and IntEntry's center TextFieldNumeric already record through
    // their own taps — the bracket only marks those records stepper members so GenericWindowScope
    // fuses the run into one Stepper row.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Widgets.IntEntry — body is four ButtonText calls captioned ±(10×multiplier)/±multiplier, each
    /// mutating by that amount times GenUI.CurrentAdjustmentMultiplier(), plus a center
    /// TextFieldNumeric (Widgets.cs:2186-2214). TargetMethod because of the by-ref parameters.
    /// Listing_Standard.IntEntry is a GetRect+delegate wrapper over it and needs nothing of its own.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureIntEntryBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "IntEntry",
                new Type[] { typeof(Rect), typeof(int).MakeByRefType(), typeof(string).MakeByRefType(), typeof(int) });
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterIntEntry();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitIntEntry();
        }
    }

    /// <summary>
    /// Listing_Standard.IntAdjuster — body is two ButtonText calls captioned ∓countChange, each
    /// mutating by countChange times GenUI.CurrentAdjustmentMultiplier() and re-clamping to its own
    /// min after either (Listing_Standard.cs:394-418); it draws no value of its own. TargetMethod
    /// because of the by-ref int parameter.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureIntAdjusterBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Listing_Standard), "IntAdjuster",
                new Type[] { typeof(int).MakeByRefType(), typeof(int), typeof(int) });
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterIntAdjuster();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitIntAdjuster();
        }
    }

    // -----------------------------------------------------------------------
    // Labeled-composite brackets. Same doctrine: NO recorder — every member (the caption Label, the
    // ButtonText/ButtonImage, the ButtonInvisible click targets, the FillableBar) already records
    // through its own tap — the bracket only suppresses, folds and stamps so GenericWindowScope can
    // present the ONE control a sighted player sees. The exception is FillableBarChangeArrows, which
    // annotates a bar row rather than adding one.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Widgets.FillableBarLabeled — body is Label + FillableBar (Widgets.cs:2578-2595), otherwise an
    /// orphan caption row followed by an unnamed percentage row. The caption is suppressed and
    /// carried on the bar's own row as FieldLabel.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "FillableBarLabeled",
        new Type[] { typeof(Rect), typeof(float), typeof(int), typeof(string) })]
    internal static class WidgetCaptureFillableBarLabeledBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            WidgetCapture.EnterFillableBarLabel(label);
            WidgetCapture.EnterSelfCaptioned(label);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelfCaptioned();
            WidgetCapture.ExitFillableBarLabel();
        }
    }

    /// <summary>
    /// Widgets.FillableBarChangeArrows' int core — the rising/falling cue vanilla draws beside a bar,
    /// the only rendering of a need's rate a sighted player gets. The float overload scales and
    /// truncates into this one (Widgets.cs:2597), so one tap covers both. Prefix: the body reassigns
    /// its own clamped copy of changeRate, and the rect is only read.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "FillableBarChangeArrows",
        new Type[] { typeof(Rect), typeof(int) })]
    internal static class WidgetCaptureFillableBarChangeArrowsPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect barRect, int changeRate)
        {
            WidgetCapture.RecordBarChangeArrows(barRect, changeRate);
        }
    }

    /// <summary>
    /// Listing_Standard.LabelDouble — body is an optional TipRegion over the whole row plus two Labels
    /// (Listing_Standard.cs:132-150), i.e. two captured rows for one "name: value" pair. The bracket
    /// folds the right half into the left half's row; the tip needs no work, since vanilla registers
    /// it in the same clip context the left half draws in.
    /// </summary>
    [HarmonyPatch(typeof(Listing_Standard), "LabelDouble",
        new Type[] { typeof(string), typeof(string), typeof(string) })]
    internal static class WidgetCaptureLabelDoubleBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string rightLabel)
        {
            WidgetCapture.EnterLabelDouble(rightLabel);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitLabelDouble();
        }
    }

    /// <summary>
    /// Widgets.DefLabelWithIcon — body is TipRegion(rect, def.description), then BeginGroup, DefIcon
    /// and Label inside that group (Widgets.cs:1072-1089). The Label already records; what it cannot
    /// reach is its own tooltip, registered one clip context OUT from the draw where
    /// TipIndex.QueryAtScreen requires context equality. The bracket carries the description onto the
    /// row.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "DefLabelWithIcon",
        new Type[] { typeof(Rect), typeof(Def), typeof(float), typeof(float) })]
    internal static class WidgetCaptureDefLabelWithIconBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Def def)
        {
            WidgetCapture.EnterDefLabelIcon(def != null ? def.description : null);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitDefLabelIcon();
        }
    }

    /// <summary>
    /// Widgets.HyperlinkWithIcon — body draws the icon, the link text through ButtonText with
    /// <c>active: false</c>, then a ButtonInvisible over the SAME rect calling ActivateHyperlink
    /// (Widgets.cs:1119-1160): otherwise a dimmed text row plus a dropped orphan hotspot. The bracket
    /// stamps both halves so presentation emits one live Button row.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "HyperlinkWithIcon",
        new Type[]
        {
            typeof(Rect), typeof(Dialog_InfoCard.Hyperlink), typeof(string), typeof(float),
            typeof(float), typeof(Color?), typeof(bool), typeof(string),
        })]
    internal static class WidgetCaptureHyperlinkWithIconBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_InfoCard.Hyperlink hyperlink)
        {
            WidgetCapture.EnterHyperlink(hyperlink.IsHidden);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitHyperlink();
        }
    }

    /// <summary>
    /// Widgets.CheckboxLabeledSelectable — body is Label + a row-select ButtonInvisible drawn only
    /// while unselected + CheckboxDraw + a 24-pixel check-toggle ButtonInvisible
    /// (Widgets.cs:1268-1313). All four already record; the bracket stamps them so presentation emits
    /// one checkbox row that also reports whether the row itself is selected. TargetMethod because of
    /// the two by-ref bools (CS0182); Listing_Standard's wrapper needs nothing of its own.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureCheckboxLabeledSelectableBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "CheckboxLabeledSelectable",
                new Type[]
                {
                    typeof(Rect), typeof(string), typeof(bool).MakeByRefType(),
                    typeof(bool).MakeByRefType(), typeof(Texture2D), typeof(float),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(ref bool selected)
        {
            WidgetCapture.EnterSelectableRow(selected);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelectableRow();
        }
    }

    /// <summary>
    /// Listing_Standard.SelectableDef — body is Label + a delete ButtonImage when a callback was
    /// supplied + a full-row ButtonInvisible (Listing_Standard.cs:497-521). The label and the row
    /// hotspot already fuse into one Button row; the bracket names the delete icon after the row it
    /// belongs to and stamps the hotspot so the row can carry the delete affordance.
    /// </summary>
    [HarmonyPatch(typeof(Listing_Standard), "SelectableDef",
        new Type[] { typeof(string), typeof(bool), typeof(Action) })]
    internal static class WidgetCaptureSelectableDefBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string name)
        {
            WidgetCapture.EnterSelectableDef(name);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitSelectableDef();
        }
    }

    /// <summary>
    /// Widgets.InfoCardButtonWorker — the private worker all nine public InfoCardButton overloads
    /// funnel through, body TipRegionByKey "DefInfoTip" + ButtonImage (Widgets.cs:3309-3316). The
    /// button already records and its tip already resolves; only the NAME was a texture asset path.
    /// The (float, float) worker forwards to this Rect one (:3304), so one tap covers both.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureInfoCardButtonWorkerPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "InfoCardButtonWorker", new Type[] { typeof(Rect) });
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            WidgetCapture.EnterInfoCardButton();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitInfoCardButton();
        }
    }

    // -----------------------------------------------------------------------
    // Listing_Tree row brackets. Stash-only: a tree row's expander (a ButtonImage) and its caption (a
    // Label) both already record AND are already operable — the expander's own click calls
    // node.SetOpen — so these two only add the structure a flat capture cannot see, the node's depth
    // and its open/closed state. Both members are PROTECTED and NON-VIRTUAL (Listing_Tree.cs:36/:59),
    // so patching the declaring type reaches every subclass; the inherited-method trap needs an
    // override to bite. AccessTools.Method resolves non-public members, hence TargetMethod.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Listing_Tree.LabelLeft — body is DrawHighlightIfMouseover + an optional TipRegion +
    /// Widgets.Label(rect, label.Truncate(rect.width)) (Listing_Tree.cs:36-57). The Label already
    /// records and its tip resolves; the bracket adds the node's depth and the untruncated caption.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureListingTreeLabelBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Listing_Tree), "LabelLeft",
                new Type[]
                {
                    typeof(string), typeof(string), typeof(int), typeof(float),
                    typeof(Color?), typeof(float),
                });
        }

        [HarmonyPrefix]
        public static void Prefix(string label, string tipText, int indentLevel)
        {
            WidgetCapture.EnterListingTreeLabel(label, tipText, indentLevel);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitListingTreeLabel();
        }
    }

    /// <summary>
    /// Listing_Tree.OpenCloseWidget — returns immediately for a non-openable node, else draws
    /// ButtonImage with TexButton.Collapse or TexButton.Reveal and calls node.SetOpen on its own click
    /// (Listing_Tree.cs:59-83). The button already records and is injectable; the bracket adds depth,
    /// and the ButtonImage tap hands the drawn texture to
    /// <see cref="WidgetCapture.NoteTreeExpanderTexture"/> for the branch state. A leaf draws nothing,
    /// so nothing is stamped — the distinction presentation needs to leave it a leaf.
    /// </summary>
    [HarmonyPatch]
    internal static class WidgetCaptureListingTreeOpenCloseBracketPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Listing_Tree), "OpenCloseWidget",
                new Type[] { typeof(TreeNode), typeof(int), typeof(int) });
        }

        [HarmonyPrefix]
        public static void Prefix(int indentLevel)
        {
            WidgetCapture.EnterListingTreeExpander(indentLevel);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            WidgetCapture.ExitListingTreeExpander();
        }
    }
}
