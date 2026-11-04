using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for the windowless bills menu (a crafting station's add/paste/existing
    /// bill rows), backed by the <see cref="RimWorldAccess.BillsMenuState"/> facade rather
    /// than a window, so it rides the focus stack through <see cref="BillsScopeMirror"/>.
    /// One flat content region of <see cref="ElementRole.Button"/> rows; disabled rows stay
    /// navigable and speak their disabled state.
    /// Cursor restore is BY BILL IDENTITY, not index: any mutation that changes the list's
    /// shape or order re-locates via <see cref="RebuildRowsAndFind"/>. Delete and
    /// <see cref="RefreshRowsPreservingIndex"/> instead rely on
    /// <see cref="ListModel.SetCount"/>'s index clamp.
    /// <see cref="BillConfigState"/> opens ON TOP of this menu without closing it, so both
    /// scopes stay pushed; modal masking gives BillConfig the keyboard.
    /// </summary>
    public sealed class BillsScope : ScreenScope, IBillRowFocusSource
    {
        private enum RowKind
        {
            AddBill,
            PasteBill,
            ExistingBill,
            Empty,
        }

        private sealed class BillRow
        {
            public RowKind Kind;
            public string Label;
            public Bill Bill; // ExistingBill only
            public bool Enabled = true;
        }

        // Bill.CanCopy is protected; reflect it rather than hand-copying its per-subclass rules.
        private static readonly PropertyInfo billCanCopy =
            typeof(Bill).GetProperty("CanCopy", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<BillRow> billRows = new List<BillRow>();
        private IBillGiver billGiver;
        private IntVec3 billGiverPos;
        private bool announcedOpen;

        public BillsScope()
        {
            // The base's typeahead-gated Cancel claim registers first and wins while a
            // search is active, so reaching HandleCancelKey means there is none.
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });

            Claim("bills.infoCard", delegate { OpenInfoCard(); });
            Claim(SharedMenuGrammar.ReorderUp, delegate { MoveSelected(-1); });
            Claim(SharedMenuGrammar.ReorderDown, delegate { MoveSelected(1); });
            Claim("bills.delete", delegate { DeleteSelected(); });
            Claim("bills.copy", delegate { CopySelected(); });
        }

        public override string Name
        {
            get { return "bills"; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless menu — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own Bills tab label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            return (string)"TabBills".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return billRows.Count;
        }

        /// <summary>Rebuilds only when the cached row list is empty — never per frame.</summary>
        protected override void RefreshContent()
        {
            if (billRows.Count > 0)
                return;
            BuildRows();
        }

        private void BuildRows()
        {
            billRows.Clear();
            if (billGiver == null)
                return;

            // Vanilla draws no add-bill button past the cap (BillStack.DoListing), so the
            // row is omitted rather than shown disabled.
            if (billGiver.BillStack == null || billGiver.BillStack.Count < BillStack.MaxCount)
            {
                billRows.Add(new BillRow { Kind = RowKind.AddBill, Label = "AddBill".Translate() + "..." });
            }

            if (BillUtility.Clipboard != null)
            {
                Building_WorkTable workTable = billGiver as Building_WorkTable;
                bool canPaste = false;
                string pasteLabel = "PasteBillTip".Translate().CapitalizeFirst().ToString();

                if (workTable != null)
                {
                    if (!workTable.def.AllRecipes.Contains(BillUtility.Clipboard.recipe) ||
                        !BillUtility.Clipboard.recipe.AvailableNow ||
                        !BillUtility.Clipboard.recipe.AvailableOnNow(workTable))
                    {
                        pasteLabel = "ClipboardBillNotAvailableHere".Translate() + ": " + BillUtility.Clipboard.LabelCap;
                        canPaste = false;
                    }
                    else if (billGiver.BillStack.Count >= BillStack.MaxCount)
                    {
                        pasteLabel = "PasteBillTip".Translate().CapitalizeFirst() + " (" + "PasteBillTip_LimitReached".Translate() + "): " + BillUtility.Clipboard.LabelCap;
                        canPaste = false;
                    }
                    else
                    {
                        pasteLabel = "PasteBillTip".Translate().CapitalizeFirst() + ": " + BillUtility.Clipboard.LabelCap;
                        canPaste = true;
                    }
                }

                billRows.Add(new BillRow { Kind = RowKind.PasteBill, Label = pasteLabel, Enabled = canPaste });
            }

            if (billGiver.BillStack != null)
            {
                for (int i = 0; i < billGiver.BillStack.Count; i++)
                {
                    Bill bill = billGiver.BillStack[i];
                    string billLabel = $"{i + 1}. {bill.LabelCap}";

                    string repeatInfo = RepeatInfoText(bill);
                    if (!string.IsNullOrEmpty(repeatInfo))
                        billLabel += $". {repeatInfo}";

                    string costInfo = GetBillCostInfo(bill);
                    if (!string.IsNullOrEmpty(costInfo))
                        billLabel += $". {costInfo}";

                    string description = GetBillDescription(bill);
                    if (!string.IsNullOrEmpty(description))
                        billLabel += $". {description}";

                    if (bill.suspended)
                        billLabel += " (" + "Paused".Translate() + ")";
                    else if (bill is Bill_Production prodBill && prodBill.paused)
                        billLabel += " (" + "Paused".Translate() + ")";

                    billRows.Add(new BillRow { Kind = RowKind.ExistingBill, Label = billLabel, Bill = bill });
                }
            }

            if (billGiver.BillStack == null || billGiver.BillStack.Count == 0)
            {
                billRows.Add(new BillRow
                {
                    Kind = RowKind.Empty,
                    Label = "RimWorldAccess.Inspection.Bills.Empty".Translate(),
                    Enabled = false,
                });
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= billRows.Count)
                return d;
            BillRow row = billRows[index];
            d.Label = row.Label;
            d.Role = ElementRole.Button;
            d.Disabled = !row.Enabled;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= billRows.Count)
                return;
            BillRow row = billRows[index];
            if (!row.Enabled)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.OptionNotAvailable".Loc(), SpeechPriority.High);
                return;
            }

            switch (row.Kind)
            {
                case RowKind.AddBill:
                    OpenAddBillMenu();
                    break;
                case RowKind.PasteBill:
                    PasteBill();
                    break;
                case RowKind.ExistingBill:
                    OpenBillConfig(row.Bill);
                    break;
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            billGiver = RimWorldAccess.BillsMenuState.BillGiver;
            billGiverPos = RimWorldAccess.BillsMenuState.BillGiverPos;
            billRows.Clear();
            TypeaheadReset();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            AnnounceCurrentItem();
        }

        private void HandleCancelKey()
        {
            Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Inspection.Patch.ClosedBillsMenu".Translate());
        }

        private void Close()
        {
            RimWorldAccess.BillsMenuState.Close();
        }

        /// <summary>
        /// Rebuilds <c>billRows</c> from the live BillStack, then re-locates the cursor onto
        /// <paramref name="target"/> by identity; a miss leaves it where the clamp put it.
        /// </summary>
        private void RebuildRowsAndFind(Bill target)
        {
            billRows.Clear();
            RefreshModel();
            if (target == null)
                return;
            int idx = billRows.FindIndex(r => r.Kind == RowKind.ExistingBill && r.Bill == target);
            if (idx >= 0)
            {
                Model.CurrentRegion?.MoveTo(idx);
            }
        }

        private void MoveSelected(int direction)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            BillRow row = billRows[region.Index];
            if (row.Kind != RowKind.ExistingBill || row.Bill == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.CannotReorder".Loc(), SpeechPriority.High);
                return;
            }

            Bill bill = row.Bill;
            int billIndex = billGiver.BillStack.IndexOf(bill);
            if (direction < 0 && billIndex <= 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Top);
                return;
            }
            if (direction > 0 && billIndex >= billGiver.BillStack.Count - 1)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Bottom);
                return;
            }

            billGiver.BillStack.Reorder(bill, direction);

            TypeaheadReset();
            RebuildRowsAndFind(bill);

            string key = direction < 0
                ? "RimWorldAccess.Inspection.Bills.MovedUp"
                : "RimWorldAccess.Inspection.Bills.MovedDown";
            TolkHelper.Speak(key.Loc(bill.LabelCap, billGiver.BillStack.IndexOf(bill) + 1));
        }

        private void DeleteSelected()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            BillRow row = billRows[region.Index];
            if (row.Kind == RowKind.ExistingBill && row.Bill != null)
            {
                Bill bill = row.Bill;
                billGiver.BillStack.Delete(bill);
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.Deleted".Loc(bill.LabelCap));

                TypeaheadReset();
                // The deleted bill can't be re-located by identity — rely on the count clamp.
                billRows.Clear();
                RefreshModel();
                AnnounceCurrentItem();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.CannotDelete".Loc(), SpeechPriority.High);
            }
        }

        private void CopySelected()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.CannotCopy".Loc(), SpeechPriority.High);
                return;
            }
            BillRow row = billRows[region.Index];
            if (row.Kind == RowKind.ExistingBill && row.Bill != null)
            {
                Bill bill = row.Bill;

                // Vanilla only draws the copy button when Bill.CanCopy is true (Bill.cs:354);
                // subclasses override it (Bill_Medical to false).
                if (!(bool)billCanCopy.GetValue(bill))
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Bills.CannotCopy".Loc(), SpeechPriority.High);
                    return;
                }

                // MUTATION-C: mirrors Bill.DoInterface's copy-button branch
                // (decompiled RimWorld/Bill.cs:354-361: gated on CanCopy,
                // `BillUtility.Clipboard = Clone();`) — Clipboard is a plain
                // public static field with no gated setter, so vanilla itself
                // writes it bare; storing a Clone keeps the clipboard
                // independent of the live bill exactly as vanilla does.
                BillUtility.Clipboard = bill.Clone();
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.CopiedToClipboard".Loc(bill.LabelCap));

                TypeaheadReset();
                RebuildRowsAndFind(bill);
                AnnounceCurrentItem();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.CannotCopy".Loc(), SpeechPriority.High);
            }
        }

        /// <summary>Opens the add-bill recipe list as a second-level windowless float menu.</summary>
        private void OpenAddBillMenu()
        {
            Building_WorkTable workTable = billGiver as Building_WorkTable;
            if (workTable == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.CannotAddBills".Loc(), SpeechPriority.High);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // Reproduces vanilla's add-bill FloatMenu order: every option is
            // MenuOptionPriority.Default, so its sort collapses to ascending displayPriority,
            // and the stable sort keeps AllRecipes order on ties.
            foreach (RecipeDef recipe in workTable.def.AllRecipes
                .Where(r => r.AvailableNow && r.AvailableOnNow(workTable))
                .OrderBy(r => r.displayPriority))
            {
                AddRecipeOption(recipe, workTable, options, null);

                if (recipe.ProducedThingDef != null)
                {
                    foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
                    {
                        foreach (Precept_Building precept in ideo.cachedPossibleBuildings)
                        {
                            if (precept.ThingDef == recipe.ProducedThingDef)
                            {
                                AddRecipeOption(recipe, workTable, options, precept);
                            }
                        }
                    }
                }
            }

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.NoRecipes".Loc());
                return;
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        private void AddRecipeOption(RecipeDef recipe, Building_WorkTable workTable, List<FloatMenuOption> options, Precept_ThingStyle precept)
        {
            string label = (precept != null) ? "RecipeMake".Translate(precept.LabelCap).CapitalizeFirst() : recipe.LabelCap;

            string costInfo = GetRecipeCostInfo(recipe);
            if (!string.IsNullOrEmpty(costInfo))
                label += $". {costInfo}";

            string description = GetRecipeDescription(recipe);
            if (!string.IsNullOrEmpty(description))
                label += $". {description}";

            FloatMenuOption option = new FloatMenuOption(label, delegate
            {
                // Vanilla treats a missing mechanitor or skill as a warning, not a refusal
                // (ITab_Bills.OptionsMaker) — ride its dialogs, don't block the bill.
                if (ModsConfig.BiotechActive && recipe.mechanitorOnlyRecipe &&
                    !workTable.Map.mapPawns.FreeColonists.Any(MechanitorUtility.IsMechanitor))
                {
                    Find.WindowStack.Add(new Dialog_MessageBox("RecipeRequiresMechanitor".Translate(recipe.LabelCap)));
                }
                else if (!workTable.Map.mapPawns.FreeColonists.Any((Pawn col) => recipe.PawnSatisfiesSkillRequirements(col)))
                {
                    Bill.CreateNoPawnsWithSkillDialog(recipe);
                }

                Bill bill = recipe.MakeNewBill(precept);
                billGiver.BillStack.AddBill(bill);

                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.AddedBill".Loc(bill.LabelCap));

                TypeaheadReset();
                RebuildRowsAndFind(bill);
                AnnounceCurrentItem();

                if (recipe.conceptLearned != null)
                {
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(recipe.conceptLearned, KnowledgeAmount.Total);
                }
            }, recipe.ProducedThingDef);

            options.Add(option);
        }

        private void PasteBill()
        {
            if (BillUtility.Clipboard == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.ClipboardEmpty".Loc());
                return;
            }

            // ITab_Bills.cs:68's paste sequence: Clone, InitializeAfterClone, then AddBill.
            Bill bill = BillUtility.Clipboard.Clone();
            bill.InitializeAfterClone();
            billGiver.BillStack.AddBill(bill);

            TolkHelper.Speak("RimWorldAccess.Inspection.Bills.PastedBill".Loc(bill.LabelCap));

            TypeaheadReset();
            RebuildRowsAndFind(bill);
            AnnounceCurrentItem();
        }

        private void OpenBillConfig(Bill bill)
        {
            if (bill == null)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.NoBillSelected".Loc());
                return;
            }

            if (bill is Bill_Production productionBill)
            {
                BillConfigState.Open(productionBill, billGiverPos);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Bills.UnsupportedType".Loc(bill.GetType().Name));
            }
        }

        private void OpenInfoCard()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            BillRow row = billRows[region.Index];
            Bill bill = row.Kind == RowKind.ExistingBill ? row.Bill : null;
            InfoCardState.TryOpenInfoCardForDef(bill?.recipe?.ProducedThingDef);
        }

        /// <summary>
        /// The bill under the keyboard cursor, or the bill being edited while
        /// <see cref="BillConfigState"/> is up. Called from inside a vanilla DRAW patch, so
        /// it must NOT <c>RefreshModel()</c>: it reads the model as-is and answers null on a
        /// row/model mismatch rather than ringing a wrong row.
        /// </summary>
        public Bill FocusedBill
        {
            get
            {
                if (BillConfigState.IsActive)
                {
                    return BillConfigState.ConfiguredBill;
                }
                ListModel region = Model.CurrentRegion;
                if (region == null || region.IsEmpty || region.Index < 0 || region.Index >= billRows.Count)
                {
                    return null;
                }
                BillRow row = billRows[region.Index];
                return row.Kind == RowKind.ExistingBill ? row.Bill : null;
            }
        }

        /// <summary>Alt+Shift+J routes off vanilla's own bills tab, which this windowless menu never hides.</summary>
        protected override Window PointerSurface
        {
            get { return BillRowFocusRing.HostWindow; }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            for (int i = 0; i < billRows.Count; i++)
            {
                BillRowFocusRing.AddRouteCandidate(billRows[i].Bill, 0, i, candidates, targets);
            }
        }

        /// <summary>Rebuilds rows for fresh labels, keeping the cursor's INDEX rather than its identity (after a rename).</summary>
        internal void RefreshRowsPreservingIndex()
        {
            billRows.Clear();
            RefreshModel();
        }

        internal void ReannounceCurrent()
        {
            // Called on return from a bill's settings, whose changes the cached labels predate.
            billRows.Clear();
            RefreshModel();
            AnnounceCurrentItem();
        }

        private static string GetRecipeCostInfo(RecipeDef recipe)
        {
            if (recipe == null)
                return "";

            List<string> costs = new List<string>();

            if (recipe.ingredients != null && recipe.ingredients.Count > 0)
            {
                foreach (IngredientCount ingredient in recipe.ingredients)
                {
                    string ingredientName = ingredient.filter.Summary;
                    float amount = ingredient.GetBaseCount();
                    costs.Add($"{amount} {ingredientName}");
                }
            }

            if (recipe.fixedIngredientFilter != null && recipe.fixedIngredientFilter.AllowedThingDefs.Any())
            {
                if (recipe.ingredients == null || recipe.ingredients.Count == 0)
                {
                    costs.Add(recipe.fixedIngredientFilter.Summary);
                }
            }

            if (costs.Count == 0)
                return "";

            return string.Join(", ", costs);
        }

        private static string GetRecipeDescription(RecipeDef recipe)
        {
            if (recipe == null)
                return "";

            List<string> descriptions = new List<string>();

            if (recipe.products != null && recipe.products.Count > 0)
            {
                foreach (ThingDefCountClass product in recipe.products)
                {
                    descriptions.Add("RimWorldAccess.Inspection.Bills.MakesCount".Translate(product.count, product.thingDef.LabelCap));
                }
            }
            else if (recipe.ProducedThingDef != null)
            {
                descriptions.Add("RimWorldAccess.Inspection.Bills.MakesProduct".Translate(recipe.ProducedThingDef.LabelCap));
            }

            float workAmount = recipe.WorkAmountTotal(null);
            if (workAmount > 0f)
            {
                descriptions.Add($"{"WorkAmount".Translate()}: {workAmount.ToStringWorkAmount()}");
            }

            if (!recipe.skillRequirements.NullOrEmpty())
            {
                var reqs = recipe.skillRequirements
                    .Select(r => "RimWorldAccess.Inspection.Bills.SkillRequirement".Translate(r.skill.LabelCap, r.minLevel).ToString());
                descriptions.Add($"{"MinimumSkills".Translate()}: {string.Join(", ", reqs)}");
            }
            else if (recipe.workSkill != null)
            {
                descriptions.Add(recipe.workSkill.LabelCap.ToString());
            }

            // The produced thing's full description carries the flavor text and stat
            // write-up sighted players get from the info card.
            string productDescription = GetProductFullDescription(recipe);
            if (!string.IsNullOrEmpty(productDescription))
                descriptions.Add(productDescription);

            if (descriptions.Count == 0)
                return "";

            return string.Join(", ", descriptions);
        }

        private static string GetProductFullDescription(RecipeDef recipe)
        {
            ThingDef produced = recipe?.ProducedThingDef;
            if (produced == null && recipe?.products != null && recipe.products.Count > 0)
                produced = recipe.products[0].thingDef;
            return produced?.DescriptionDetailed?.Trim();
        }

        private static string GetBillCostInfo(Bill bill)
        {
            if (bill?.recipe == null)
                return "";

            List<string> costs = new List<string>();

            if (bill.recipe.ingredients != null && bill.recipe.ingredients.Count > 0)
            {
                foreach (IngredientCount ingredient in bill.recipe.ingredients)
                {
                    string ingredientName = ingredient.filter.Summary;
                    float amount = ingredient.GetBaseCount();
                    costs.Add($"{amount} {ingredientName}");
                }
            }

            if (bill.recipe.fixedIngredientFilter != null && bill.recipe.fixedIngredientFilter.AllowedThingDefs.Any())
            {
                if (bill.recipe.ingredients == null || bill.recipe.ingredients.Count == 0)
                {
                    costs.Add(bill.recipe.fixedIngredientFilter.Summary);
                }
            }

            if (costs.Count == 0)
                return "";

            return string.Join(", ", costs);
        }

        /// <summary>The count the bill card draws beside its label; mods patch it for their own repeat modes, which vanilla throws on.</summary>
        private static string RepeatInfoText(Bill bill)
        {
            if (!(bill is Bill_Production production))
                return "";
            try
            {
                return production.RepeatInfoText;
            }
            catch (System.InvalidOperationException)
            {
                return "";
            }
        }

        private static string GetBillDescription(Bill bill)
        {
            if (bill?.recipe == null)
                return "";

            List<string> descriptions = new List<string>();

            if (bill.recipe.products != null && bill.recipe.products.Count > 0)
            {
                foreach (ThingDefCountClass product in bill.recipe.products)
                {
                    descriptions.Add("RimWorldAccess.Inspection.Bills.MakesCount".Translate(product.count, product.thingDef.LabelCap));
                }
            }
            else if (bill.recipe.ProducedThingDef != null)
            {
                descriptions.Add("RimWorldAccess.Inspection.Bills.MakesProduct".Translate(bill.recipe.ProducedThingDef.LabelCap));
            }

            float workAmount = bill.recipe.WorkAmountTotal(null);
            if (workAmount > 0f)
            {
                descriptions.Add($"{"WorkAmount".Translate()}: {workAmount.ToStringWorkAmount()}");
            }

            if (!bill.recipe.skillRequirements.NullOrEmpty())
            {
                var reqs = bill.recipe.skillRequirements
                    .Select(r => "RimWorldAccess.Inspection.Bills.SkillRequirement".Translate(r.skill.LabelCap, r.minLevel).ToString());
                descriptions.Add($"{"MinimumSkills".Translate()}: {string.Join(", ", reqs)}");
            }
            else if (bill.recipe.workSkill != null)
            {
                descriptions.Add(bill.recipe.workSkill.LabelCap.ToString());
            }

            string productDescription = GetProductFullDescription(bill.recipe);
            if (!string.IsNullOrEmpty(productDescription))
                descriptions.Add(productDescription);

            if (descriptions.Count == 0)
                return "";

            return string.Join(", ", descriptions);
        }
    }

    /// <summary>
    /// Keeps <see cref="BillsScope"/> in lockstep with
    /// <see cref="RimWorldAccess.BillsMenuState.IsActive"/>.
    /// Pops while an info card is open so the card owns the keyboard and the menu
    /// contributes no AnyLiveModal; the scope holds no state, so re-pushing restores the
    /// cursor. The bills → bill config → ingredient filter chain leaves all three scopes
    /// pushed, so <c>ThingFilterMenuScope</c> must reconcile after this mirror.
    /// </summary>
    internal static class BillsScopeMirror
    {
        private static readonly BillsScope scope = new BillsScope();

        public static void Reconcile()
        {
            // ForeignDialogWindowAbove: the add-recipe path rides vanilla's
            // mechanitor/skill warning Dialog_MessageBoxes while BillsMenuState stays active.
            if (RimWorldAccess.BillsMenuState.IsActive && !InfoCardState.IsActive
                && !ShellGuards.ForeignDialogWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }

        internal static void RefreshRows()
        {
            scope.RefreshRowsPreservingIndex();
        }

        internal static void ReannounceCurrent()
        {
            scope.ReannounceCurrent();
        }
    }
}
