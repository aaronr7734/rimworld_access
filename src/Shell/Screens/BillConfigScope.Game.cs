using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the windowless bill configuration menu (one bill's full settings:
    /// repeat mode, target count, ingredient filters, skill/pawn restriction, rename). The mod owns
    /// no window here — the surface is the <see cref="BillConfigState"/> state machine — so the
    /// scope rides the focus stack through <see cref="BillConfigScopeMirror"/>.
    ///
    /// ONE flat content region, because vanilla draws its settings as a single Listing_Standard
    /// column and grouping them into regions here would be an invention.
    ///
    /// Left/Right ride the chassis (<see cref="CanAdjustContentItem"/>/<see cref="AdjustContentItem"/>),
    /// so <c>billConfig.increase</c>/<c>decrease</c> are retired ids; the eight By10/By100/By1000
    /// steps, the two Shift+Home/End value jumps and Alt+I keep their own ids and chords.
    ///
    /// Enter on one of the six countable fields opens the shared <see cref="TextFieldEditSession"/>
    /// (digits only, clamped by the field's own vanilla-harvested commit). The dispatcher's modal
    /// text-session branch routes every key to the session, so no claim here needs a browse gate.
    ///
    /// <see cref="BillConfigState.Open"/> opens vanilla's own <see cref="Dialog_BillConfig"/> through
    /// <c>Bill_Production.GetBillDialog</c> and removes it when the session closes, so the real
    /// window stands beneath this windowless scope for its whole life and both close directions stay
    /// symmetric. That dialog inherits <c>closeOnAccept</c> and <c>closeOnCancel</c> (decompiled
    /// RimWorld/Dialog_BillConfig.cs:68-77), so either key would otherwise close it out from under a
    /// row activation; <see cref="OwnsCancel"/> plus the base's <c>OwnsAccept</c> route both through
    /// <see cref="ShellKeyOwnership"/> rather than a same-frame stamp, whose ordering the shell does
    /// not guarantee.
    ///
    /// <b>Focus ring.</b> The ring rides the ordinary client registry on that window.
    /// <see cref="BillConfigRowMarkerPatch"/> marks the dialog's rows and
    /// <see cref="BillConfigState.ListingFocusFor"/> names the one the keyboard is on; rows vanilla
    /// draws outside any listing (rename and delete icons, the ingredient pane) stay unringed.
    /// </summary>
    public sealed class BillConfigScope : ScreenScope, IListingRingClient
    {
        private readonly TextFieldEditSession numericEdit = new TextFieldEditSession();

        public BillConfigScope()
        {
            // Alt+I: info card for the bill's product.
            Claim("billConfig.infoCard", e => BillConfigState.OpenInfoCard());

            // Shift/Ctrl/Shift+Ctrl Up/Down step the focused field's value by 10/100/1000;
            // plain Left/Right (step 1) ride the base's own adjust path.
            Claim("billConfig.increaseBy10", e => AdjustFocusedValue(1, 10));
            Claim("billConfig.decreaseBy10", e => AdjustFocusedValue(-1, 10));
            Claim("billConfig.increaseBy100", e => AdjustFocusedValue(1, 100));
            Claim("billConfig.decreaseBy100", e => AdjustFocusedValue(-1, 100));
            Claim("billConfig.increaseBy1000", e => AdjustFocusedValue(1, 1000));
            Claim("billConfig.decreaseBy1000", e => AdjustFocusedValue(-1, 1000));

            // Shift+Home/Shift+End jump the focused field's VALUE to its min/max; plain
            // Home/End jump the menu cursor instead.
            Claim("billConfig.jumpToMin", e => JumpFocusedValue(true));
            Claim("billConfig.jumpToMax", e => JumpFocusedValue(false));

            // The base's typeahead-gated Cancel claim (registered first) clears an active search;
            // reaching this one means no search is live, so it always closes.
            Claim(SharedMenuGrammar.Cancel, e => CloseMenu(), when: () => !TypeaheadHasActiveSearch);

            RegisterPopTeardown(numericEdit.CancelIfActive);
        }

        public override string Name
        {
            get { return "bill-config"; }
        }

        /// <summary>No mod-owned <c>Window</c> exists for this windowless menu — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>
        /// The vanilla dialog <see cref="BillConfigState"/> opens is this scope's surface. Asked
        /// live rather than assumed, so the DEBUG visual tripwire screams if that open regresses.
        /// </summary>
        public override bool HasOnScreenSurface
        {
            get { return Find.WindowStack?.WindowOfType<Dialog_BillConfig>() != null; }
        }

        /// <summary>Shared cross-region typeahead.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Windowless menu: no captured buttons and no declared actions — no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        // ScreenScope content contract: one flat region of the bill's fields.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>
        /// The bill's own label, as vanilla composes it — Dialog_BillConfig draws no title of its
        /// own, so the thing being configured is the honest name for the region.
        /// </summary>
        protected override string ContentRegionName(int region)
        {
            Bill_Production bill = BillConfigState.ConfiguredBill;
            return bill != null ? bill.LabelCap.ToString() : "";
        }

        protected override int ContentItemCount(int region)
        {
            return BillConfigState.RowCount;
        }

        /// <summary>
        /// Re-evaluates which fields are shown on every refresh, so a mutation that reveals or
        /// hides a row reshapes the row list without the caller having to remember to.
        /// </summary>
        protected override void RefreshContent()
        {
            BillConfigState.RefreshRows();
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return BillConfigState.DescribeRow(index);
        }

        /// <summary>Typeahead matches the field NAME alone, never the value fused into its spoken label.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            return BillConfigState.RowSearchText(row);
        }

        /// <summary>Enter: a countable field opens exact numeric entry, every other row runs its own action.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (BillConfigState.RowTakesTypedNumber(index))
            {
                BeginNumericEdit(index);
                return;
            }
            BillConfigState.ExecuteRow(index);
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return BillConfigState.RowAdjustsValue(index);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            BillConfigState.AdjustValue(index, direction);
        }

        protected override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            return BillConfigState.TryGetSliderBox(index, out value, out high);
        }

        protected override bool HasOwnNumberEntry(int region, int index)
        {
            return BillConfigState.RowTakesTypedNumber(index);
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            ScreenScopeDrawPatch.RegisterListingClient(this);
        }

        public override void OnPop()
        {
            ScreenScopeDrawPatch.UnregisterListingClient(this);
            base.OnPop();
        }

        /// <summary>
        /// Called by <see cref="BillConfigState.Open"/> for each fresh session, possibly before the
        /// mirror has pushed this scope — harmless, since none of this depends on FocusStack
        /// membership. Deliberately NOT wired to <see cref="OnPush"/>: this mirror pops and
        /// re-pushes around an open info card, so a reset there would throw away the player's row
        /// every time they looked one up.
        /// </summary>
        internal void OpenFresh()
        {
            TypeaheadReset();
            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
        }

        /// <summary>Rebuilds the rows from live bill data and re-announces the focused one.</summary>
        internal void ReannounceCurrent()
        {
            RefreshModel();
            AnnounceCurrentItem();
        }

        // The screen's own value chords.

        private void AdjustFocusedValue(int direction, int multiplier)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            BillConfigState.AdjustValue(region.Index, direction, multiplier);
        }

        private void JumpFocusedValue(bool toMinimum)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            BillConfigState.JumpToBound(region.Index, toMinimum);
        }

        /// <summary>
        /// Exact numeric entry for one countable field: digits only (what vanilla's
        /// Listing_Standard.IntEntry accepts), the value bound on confirm by that field's own
        /// vanilla-harvested clamp. Escape leaves the field untouched; either exit re-announces the
        /// row, which is why nothing on the commit path speaks.
        /// </summary>
        private void BeginNumericEdit(int index)
        {
            ElementDescription row = BillConfigState.DescribeRow(index);
            numericEdit.EnterEdit(
                BillConfigState.TypedNumberSeed(index),
                TextFieldSpec.WholeNumber("RimWorldAccess.TextInput.LabelDefault"),
                row != null ? row.Label : "",
                value => ApplyTypedNumber(index, value),
                onExit: ReannounceCurrent);
        }

        private static void ApplyTypedNumber(int index, string value)
        {
            if (int.TryParse(value, out int typed))
            {
                BillConfigState.ApplyTypedNumber(index, typed);
            }
        }

        private void CloseMenu()
        {
            BillConfigState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback(
                "RimWorldAccess.Inspection.Patch.ClosedBillConfiguration".Translate());
        }

        // Focus ring on the real dialog underneath.

        /// <summary>
        /// Looked up per pass rather than held: the window's lifetime belongs to
        /// <see cref="BillConfigState"/>, and the registry skips a client whose window is null.
        /// </summary>
        Window IListingRingClient.ListingRingWindow
        {
            get
            {
                WindowStack stack = Find.WindowStack;
                return stack == null ? null : stack.WindowOfType<Dialog_BillConfig>();
            }
        }

        /// <summary>
        /// Runs inside the dialog's own draw, so it reads the model as-is and never refreshes it —
        /// a row/model mismatch rings nothing rather than ringing the wrong row.
        /// </summary>
        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            ListModel region = Model.CurrentRegion;
            return region == null || region.IsEmpty
                ? ListingRingFocus.None
                : BillConfigState.ListingFocusFor(region.Index);
        }
    }

    /// <summary>
    /// Tokens Dialog_BillConfig's row sites are identified by, shared between the manifest below
    /// (which emits them) and <see cref="BillConfigState"/> (which maps a focused menu row onto one).
    /// Only seven sites carry a translation key as the literal nearest their call; every other row
    /// concatenates its live value onto the translated text, leaving display text nearest the call,
    /// so those take a "#" site token — which makes a tripwire mandatory at the ring point.
    /// </summary>
    internal static class BillConfigRowKeys
    {
        private const string Site = "Dialog_BillConfig.DoWindowContents#";

        /// <summary>The tripwire the raw <c>GetRect</c> bands and the radius slider carry. Those run under
        /// no outer Listing_Standard tap, so their DrawnLabel is null and any non-null tripwire passes;
        /// this value is never a label vanilla draws, so a stale one cannot match it either.</summary>
        internal const string RawBand = "<raw band>";

        /// <summary>decompiled RimWorld/Dialog_BillConfig.cs:126, whose caption is the repeat mode's own label.</summary>
        internal const string RepeatMode = Site + "repeatMode";

        /// <summary>:133.</summary>
        internal const string RepeatCount = "RepeatCount";

        /// <summary>:139-148. Vanilla folds the owned count and the target count into this one label, so two menu rows share it.</summary>
        internal const string CurrentlyHave = Site + "currentlyHave";

        /// <summary>:157.</summary>
        internal const string IncludeEquipped = "IncludeEquipped";

        /// <summary>:161.</summary>
        internal const string IncludeTainted = "IncludeTainted";

        /// <summary>:163-165, whose caption picks one of two translation keys.</summary>
        internal const string IncludeSource = Site + "includeSource";

        /// <summary>:185, a raw Widgets.FloatRange band.</summary>
        internal const string HpRange = Site + "hpRange";

        /// <summary>:190, a raw Widgets.QualityRange band.</summary>
        internal const string QualityRange = Site + "qualityRange";

        /// <summary>:195.</summary>
        internal const string LimitToAllowedStuff = "LimitToAllowedStuff";

        /// <summary>:201.</summary>
        internal const string PauseWhenSatisfied = "PauseWhenSatisfied";

        /// <summary>:204.</summary>
        internal const string UnpauseAt = Site + "unpauseAt";

        /// <summary>:216-224.</summary>
        internal const string StoreMode = Site + "storeMode";

        /// <summary>:248, the raw band the worker Widgets.Dropdown fills.</summary>
        internal const string PawnRestriction = Site + "pawnRestriction";

        /// <summary>:251. Vanilla draws one caption for the whole IntRange, so both skill-bound rows share it.</summary>
        internal const string SkillRange = Site + "skillRange";

        /// <summary>:263 and :269 — the two halves of vanilla's suspend button.</summary>
        internal const string Suspended = "Suspended";

        /// <summary>:269.</summary>
        internal const string NotSuspended = "NotSuspended";

        /// <summary>:332, the recipe-description block. Never focused — see the manifest's remarks.</summary>
        internal const string RecipeInfo = Site + "recipeInfo";

        /// <summary>:349, the classic-mode style button. Never focused.</summary>
        internal const string Style = Site + "style";

        /// <summary>:401, the "no styles available" band. Never focused.</summary>
        internal const string NoStyles = Site + "noStyles";

        /// <summary>:515, the radius slider, whose caption vanilla truncates to the pane width.</summary>
        internal const string IngredientRadius = "Dialog_BillConfig.DoIngredientConfigPane#radius";
    }

    /// <summary>
    /// The row calls <c>DoWindowContents</c> makes, resolved by exact signature: plain ButtonText
    /// (decompiled Verse/Listing_Standard.cs:248), both Label overloads (:95 and :100 — a caption
    /// built from a <c>Translate</c> result binds the TaggedString one, a plain <c>string</c> local
    /// the other), the tooltip CheckboxLabeled (:215 — the four checkbox calls pass only
    /// (label, ref field), binding this optional-argument overload rather than the 3-argument tabIn
    /// shape), and <c>Listing.GetRect</c> for the bands vanilla fills with a raw widget
    /// (Verse/Listing.cs:62).
    /// </summary>
    internal static class BillConfigRowCalls
    {
        internal static readonly MethodInfo ButtonText =
            AccessTools.Method(typeof(Listing_Standard), "ButtonText",
                new[] { typeof(string), typeof(string), typeof(float) });

        internal static readonly MethodInfo Label =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(string), typeof(float), typeof(TipSignal?) });

        internal static readonly MethodInfo TaggedLabel =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(TaggedString), typeof(float), typeof(string) });

        internal static readonly MethodInfo CheckboxLabeled =
            AccessTools.Method(typeof(Listing_Standard), "CheckboxLabeled",
                new[] { typeof(string), typeof(bool).MakeByRefType(), typeof(string), typeof(float), typeof(float) });

        internal static readonly MethodInfo GetRect =
            AccessTools.Method(typeof(Listing), "GetRect", new[] { typeof(float), typeof(float) });

        internal static readonly MethodInfo Slider =
            AccessTools.Method(typeof(Listing_Standard), "Slider", new[] { typeof(float), typeof(float), typeof(float) });
    }

    /// <summary>
    /// The nineteen row calls of <c>DoWindowContents</c>, in IL order (decompiled
    /// RimWorld/Dialog_BillConfig.cs:126, :133, :139, :157, :161, :165, :185, :190, :195, :201,
    /// :204, :224, :248, :251, :263, :269, :332, :349, :401). Sites pair positionally, so mutually
    /// exclusive branches both appear here and the unexecuted one emits no marker. The last three
    /// are marked purely to keep that pairing honest: their tokens are "#"-shaped and no focus ever
    /// names them, so they can never ring. IntEntry, IntRange and Slider call <c>GetRect</c> inside
    /// their own bodies, and the float-menu option lambdas live in compiler-generated closures, so
    /// neither shows up here.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_BillConfig), "DoWindowContents")]
    internal static class BillConfigRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[]
            {
                BillConfigRowCalls.ButtonText, BillConfigRowCalls.Label,
                BillConfigRowCalls.TaggedLabel, BillConfigRowCalls.CheckboxLabeled,
                BillConfigRowCalls.GetRect,
            },
            Sites = new[]
            {
                Synthetic(BillConfigRowCalls.ButtonText, BillConfigRowKeys.RepeatMode),
                Harvested(BillConfigRowCalls.TaggedLabel, BillConfigRowKeys.RepeatCount),
                Synthetic(BillConfigRowCalls.Label, BillConfigRowKeys.CurrentlyHave),
                Harvested(BillConfigRowCalls.CheckboxLabeled, BillConfigRowKeys.IncludeEquipped),
                Harvested(BillConfigRowCalls.CheckboxLabeled, BillConfigRowKeys.IncludeTainted),
                Synthetic(BillConfigRowCalls.ButtonText, BillConfigRowKeys.IncludeSource),
                Synthetic(BillConfigRowCalls.GetRect, BillConfigRowKeys.HpRange),
                Synthetic(BillConfigRowCalls.GetRect, BillConfigRowKeys.QualityRange),
                Harvested(BillConfigRowCalls.CheckboxLabeled, BillConfigRowKeys.LimitToAllowedStuff),
                Harvested(BillConfigRowCalls.CheckboxLabeled, BillConfigRowKeys.PauseWhenSatisfied),
                Synthetic(BillConfigRowCalls.TaggedLabel, BillConfigRowKeys.UnpauseAt),
                Synthetic(BillConfigRowCalls.ButtonText, BillConfigRowKeys.StoreMode),
                Synthetic(BillConfigRowCalls.GetRect, BillConfigRowKeys.PawnRestriction),
                Synthetic(BillConfigRowCalls.TaggedLabel, BillConfigRowKeys.SkillRange),
                Harvested(BillConfigRowCalls.ButtonText, BillConfigRowKeys.Suspended),
                Harvested(BillConfigRowCalls.ButtonText, BillConfigRowKeys.NotSuspended),
                Synthetic(BillConfigRowCalls.Label, BillConfigRowKeys.RecipeInfo),
                Synthetic(BillConfigRowCalls.ButtonText, BillConfigRowKeys.Style),
                Synthetic(BillConfigRowCalls.GetRect, BillConfigRowKeys.NoStyles),
            },
        };

        private static MarkerSite Harvested(MethodInfo rowCall, string key)
        {
            return new MarkerSite { RowCall = rowCall, Payload = MarkerPayload.HarvestedKey, ExpectedKey = key };
        }

        private static MarkerSite Synthetic(MethodInfo rowCall, string key)
        {
            return new MarkerSite { RowCall = rowCall, Payload = MarkerPayload.SyntheticKey, ExpectedKey = key };
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>The ingredient pane's one row call (decompiled RimWorld/Dialog_BillConfig.cs:515); Dialog_MechBillConfig draws it through base.</summary>
    [HarmonyPatch(typeof(Dialog_BillConfig), "DoIngredientConfigPane")]
    internal static class BillConfigRadiusMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { BillConfigRowCalls.Slider },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = BillConfigRowCalls.Slider,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = BillConfigRowKeys.IngredientRadius,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>
    /// The close direction the mod does not drive: whichever route removes the dialog ends the
    /// editing session too. Patches the declaring <see cref="Window"/> type with an instance guard,
    /// since <see cref="Dialog_BillConfig"/> overrides no <c>PostClose</c> of its own.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostClose")]
    internal static class BillConfigDialogClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            BillConfigState.NotifyDialogClosed(__instance);
        }
    }

    /// <summary>
    /// Keeps <see cref="BillConfigScope"/> in lockstep with <see cref="BillConfigState.IsActive"/>,
    /// reconciled every OnGUI pass AFTER <see cref="BillsScopeMirror"/> so it lands above the
    /// bills-menu scope when both are active (opening BillConfigState does not close BillsMenuState).
    /// Stands down while an info card, or a scoped dialog a row opened, is up. The ingredient filter submenu
    /// (<see cref="ThingFilterMenuState"/>) needs no yield gate: it opens ON TOP of BillConfigState
    /// without closing it, and ThingFilterMenuScopeMirror reconciles after this one, so modal stack
    /// masking gives the filter the keyboard.
    /// </summary>
    internal static class BillConfigScopeMirror
    {
        private static readonly BillConfigScope scope = new BillConfigScope();

        public static void Reconcile()
        {
            if (BillConfigState.IsActive && !InfoCardState.IsActive
                && !ShellGuards.ForeignDialogWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }

        /// <summary>Forwards <see cref="BillConfigState.Open"/>'s fresh-session reset.</summary>
        internal static void OpenFresh()
        {
            scope.OpenFresh();
        }

        /// <summary>Forwards <see cref="BillConfigState.Reannounce"/> and the state's post-mutation re-reads.</summary>
        internal static void ReannounceCurrent()
        {
            scope.ReannounceCurrent();
        }
    }
}
