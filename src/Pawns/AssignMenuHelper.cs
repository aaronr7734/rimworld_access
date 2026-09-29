using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The colonist assignment table's cell read/write layer, built on vanilla's own
    /// <see cref="PawnTableDefOf.Assign"/> and the shared <see cref="PawnColumnHandlerRegistry"/>:
    /// column set, order, headers and every cell's value/tooltip/menu come from the workers
    /// themselves, and every write rides the worker's own vehicle (see
    /// <see cref="IGeneratedMenuSource"/> for the dropdown-shaped columns' paint vehicle). A modded
    /// column whose worker subclasses a type the registry knows works for free; a wholly unknown
    /// worker degrades to a navigable, sortable, honestly-"not readable" cell.
    /// </summary>
    public static class AssignMenuHelper
    {
        public enum PolicyAction { New, Rename, Copy, Delete, Edit }

        /// <summary>What <see cref="ActivateCell"/> did, so the scope picks the right announcement.</summary>
        public enum ActivationOutcome
        {
            /// <summary>No handler action — the scope falls back to its own row default (Name jump, Ideo/Xenotype re-announce).</summary>
            NotHandled,
            /// <summary>A value changed in place — speak the new state.</summary>
            StateChanged,
            /// <summary>The handler opened UI (a float menu) that announces itself.</summary>
            OpenedUI,
            /// <summary>Vanilla itself blocks this cell (e.g. a quest lodger's Outfit) — speak the reason.</summary>
            ReadOnly,
            /// <summary>The pawn has no tracker for this column (e.g. no playerSettings/inventoryStock) — speak "not applicable".</summary>
            NotApplicable,
        }

        // Vanilla's own Assign columns, in vanilla's order, filtered by Worker.VisibleCurrently:
        // Ideo/Xenotype are absent from the def when their DLC is inactive, and VisibleCurrently
        // covers the runtime gates (e.g. Ideo excludes itself under Ideology's classic mode).
        private static readonly List<PawnColumnDef> columns = new List<PawnColumnDef>();

        public static void BuildActiveColumns()
        {
            columns.Clear();
            List<PawnColumnDef> defs = PawnTableDefOf.Assign?.columns;
            if (defs == null)
                return;
            foreach (PawnColumnDef def in defs)
            {
                if (def == null)
                    continue;
                try
                {
                    IPawnColumnHandler handler = PawnColumnHandlerRegistry.Resolve(def);
                    if (handler.SkipColumn(def))
                        continue; // GapTiny et al: no content a sighted player reads either.
                    if (!def.Worker.VisibleCurrently)
                        continue;
                }
                catch (Exception ex)
                {
                    // A broken (typically modded) worker must not drop the whole table.
                    Log.Warning("[RimWorld Access] Assign column '" + def.defName + "' failed visibility resolution and was skipped: " + ex);
                    continue;
                }
                columns.Add(def);
            }
        }

        public static int GetColumnCount()
        {
            return columns.Count;
        }

        public static PawnColumnDef GetColumnDef(int index)
        {
            return index >= 0 && index < columns.Count ? columns[index] : null;
        }

        /// <summary>Name column: Enter jumps to the pawn, handled by the scope itself rather than the registry so the menu closes first.</summary>
        public static bool IsNameColumn(int index)
        {
            return GetColumnDef(index)?.Worker is PawnColumnWorker_Label;
        }

        /// <summary>Ideo/Xenotype: display-only icon columns, so Enter falls back to the row default.</summary>
        public static bool IsDisplayOnlyColumn(int index)
        {
            return GetColumnDef(index)?.Worker is PawnColumnWorker_Icon;
        }

        // === Header text ===

        /// <summary>
        /// The column header word: the def's own label, else its headerTip, else — for the two icon
        /// columns XML gives neither — vanilla's "Ideo"/"Xenotype" Keyed translations, matched by
        /// worker TYPE rather than a defName string; else the bare defName.
        /// </summary>
        public static string GetColumnName(int index)
        {
            PawnColumnDef def = GetColumnDef(index);
            if (def == null)
                return "RimWorldAccess.Common.Unknown".Translate();

            if (!def.label.NullOrEmpty())
                return def.LabelCap.ToString();
            if (!def.headerTip.NullOrEmpty())
                return def.headerTip;
            if (def.Worker is PawnColumnWorker_Ideo)
                return "Ideo".Translate().Resolve();
            if (def.Worker is PawnColumnWorker_Xenotype)
                return "Xenotype".Translate().Resolve();
            return def.defName;
        }

        /// <summary>The header's own tooltip, suppressed when it would just repeat the label already spoken.</summary>
        public static string GetColumnHeaderTooltip(int index)
        {
            PawnColumnDef def = GetColumnDef(index);
            if (def == null || def.headerTip.NullOrEmpty())
                return null;
            string label = GetColumnName(index);
            return def.headerTip == label ? null : def.headerTip;
        }

        /// <summary>The worker's own per-cell tooltip, via the registry.</summary>
        public static string GetCellTip(Pawn pawn, int index)
        {
            PawnColumnDef def = GetColumnDef(index);
            if (def == null)
                return null;
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellTip(def, pawn);
            }
            catch (Exception ex)
            {
                LogCellFailure(def, ex);
                return null;
            }
        }

        // === Cell value ===

        public static string GetColumnValue(Pawn pawn, int index)
        {
            PawnColumnDef def = GetColumnDef(index);
            if (def == null)
                return "RimWorldAccess.Common.Unknown".Translate();
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellText(def, pawn);
            }
            catch (Exception ex)
            {
                LogCellFailure(def, ex);
                return "RimWorldAccess.Shell.Generic.CellNotReadable".Loc().ToString();
            }
        }

        private static readonly HashSet<Type> loggedCellFailures = new HashSet<Type>();

        private static void LogCellFailure(PawnColumnDef def, Exception ex)
        {
            Type workerType = def.workerClass;
            if (workerType != null && loggedCellFailures.Add(workerType))
            {
                Log.Warning("[RimWorld Access] Assign cell handler failed for column '"
                    + def.defName + "' (" + workerType.FullName + "): " + ex);
            }
        }

        // === Sorting ===

        public static bool IsColumnSortable(int columnIndex)
            => PawnColumnSortHelper.IsColumnSortable(columns, columnIndex);

        public static List<Pawn> SortByColumn(List<Pawn> pawns, int columnIndex, bool descending)
            => PawnColumnSortHelper.SortByColumnDef(pawns, columns, columnIndex, descending);

        // === Read-only gating ===

        /// <summary>
        /// Mirrors PawnColumnWorker_Outfit.DoCell: a quest lodger's outfit cell draws the
        /// "Unchangeable" label, never the dropdown, so vanilla blocks the mutation by never offering
        /// the control. Every path that writes a cell value honors this first.
        /// </summary>
        public static bool CanAssignValue(Pawn pawn, int colIndex)
        {
            PawnColumnDef def = GetColumnDef(colIndex);
            if (def == null)
                return true;
            return !(def.Worker is PawnColumnWorker_Outfit && pawn.IsQuestLodger());
        }

        /// <summary>Why a cell blocked by <see cref="CanAssignValue"/> can't be changed, in vanilla's own words.</summary>
        public static string GetReadOnlyReason(int colIndex)
        {
            PawnColumnDef def = GetColumnDef(colIndex);
            if (def != null && def.Worker is PawnColumnWorker_Outfit)
                return "Unchangeable".Translate() + ". " + "QuestRelated_Outfit".Translate();
            return "Unchangeable".Translate();
        }

        // === Cell activation (Enter) ===

        /// <summary>
        /// Enter on an interactive cell: honors the read-only and not-applicable gates, then
        /// dispatches to the column's registry handler, which opens vanilla's own menu generator.
        /// </summary>
        public static ActivationOutcome ActivateCell(Pawn pawn, int colIndex, PawnTable table, out string blockedReason)
        {
            blockedReason = null;
            PawnColumnDef def = GetColumnDef(colIndex);
            if (def == null)
                return ActivationOutcome.NotHandled;

            if (!CanAssignValue(pawn, colIndex))
            {
                blockedReason = GetReadOnlyReason(colIndex);
                return ActivationOutcome.ReadOnly;
            }
            if (def.Worker is PawnColumnWorker_Carry && pawn.inventoryStock == null)
                return ActivationOutcome.NotApplicable;
            if ((def.Worker is PawnColumnWorker_HostilityResponse || def.Worker is PawnColumnWorker_MedicalCare)
                && pawn.playerSettings == null)
                return ActivationOutcome.NotApplicable;

            PawnColumnActivation result;
            try
            {
                result = PawnColumnHandlerRegistry.Resolve(def).ActivateCell(def, pawn, table);
            }
            catch (Exception ex)
            {
                LogCellFailure(def, ex);
                return ActivationOutcome.NotHandled;
            }
            switch (result)
            {
                case PawnColumnActivation.StateChanged:
                    return ActivationOutcome.StateChanged;
                case PawnColumnActivation.OpenedUI:
                    return ActivationOutcome.OpenedUI;
                default:
                    return ActivationOutcome.NotHandled;
            }
        }

        // === Paint support ===

        public static bool CanPaintColumn(int colIndex)
        {
            PawnColumnDef def = GetColumnDef(colIndex);
            if (def == null)
                return false;
            return !(def.Worker is PawnColumnWorker_Label) && !(def.Worker is PawnColumnWorker_Icon);
        }

        /// <summary>
        /// Applies the source pawn's current value to the target pawn. The dropdown-shaped columns
        /// ride <see cref="IGeneratedMenuSource"/>, so each write is vanilla's own generated option
        /// for THAT target: painting HostilityResponse "Attack" onto a Violence-disabled pawn no-ops
        /// rather than writing a state vanilla never offers it. Carry has no gate to bypass, so it
        /// stays a direct copy. Returns whether the write took; callers must speak that rather than
        /// assume success.
        /// </summary>
        public static bool ApplyValueToPawn(Pawn sourcePawn, Pawn targetPawn, int colIndex)
        {
            PawnColumnDef def = GetColumnDef(colIndex);
            if (def == null)
                return false;

            if (def.Worker is PawnColumnWorker_Outfit)
            {
                return ApplyGeneratedPayload(def, sourcePawn.outfits?.CurrentApparelPolicy, targetPawn);
            }
            if (def.Worker is PawnColumnWorker_FoodRestriction)
            {
                return ApplyGeneratedPayload(def, sourcePawn.foodRestriction?.CurrentFoodPolicy, targetPawn);
            }
            if (def.Worker is PawnColumnWorker_DrugPolicy)
            {
                return ApplyGeneratedPayload(def, sourcePawn.drugs?.CurrentPolicy, targetPawn);
            }
            if (def.Worker is PawnColumnWorker_Reading)
            {
                return ApplyGeneratedPayload(def, sourcePawn.reading?.CurrentPolicy, targetPawn);
            }
            if (def.Worker is PawnColumnWorker_CombatPolicy)
            {
                // No policy is a real value here: the menu's first entry, None, carries a null
                // payload, so the first-match apply lands on it rather than on Edit.
                return PawnColumnHandlerRegistry.Resolve(def) is IGeneratedMenuSource source
                    && source.TryApplyPayloadToTarget(def, targetPawn, PawnColumnWorker_CombatPolicy.CurrentPolicy(sourcePawn));
            }
            if (def.Worker is PawnColumnWorker_HuntPolicy)
            {
                // Unassigned pawns follow the default policy, so the effective one is what paints.
                return ApplyGeneratedPayload(def, CombatAutopilotComponent.EffectiveHuntPolicy(sourcePawn), targetPawn);
            }
            if (def.Worker is PawnColumnWorker_HostilityResponse)
            {
                return sourcePawn.playerSettings != null
                    && ApplyGeneratedPayload(def, sourcePawn.playerSettings.hostilityResponse, targetPawn);
            }
            if (def.Worker is PawnColumnWorker_MedicalCare)
            {
                return sourcePawn.playerSettings != null
                    && ApplyGeneratedPayload(def, sourcePawn.playerSettings.medCare, targetPawn);
            }
            if (def.Worker is PawnColumnWorker_Carry)
            {
                // MUTATION-C: mirrors PawnColumnWorker_Carry's own
                // SetThingForGroup/SetCountForGroup (no Can*/Try* gate exists
                // for this write — audit-confirmed), so a direct copy of the
                // desired thing/count carries no bypassed check.
                if (sourcePawn.inventoryStock == null || targetPawn.inventoryStock == null)
                    return false;
                InventoryStockGroupDef group = InventoryStockGroupDefOf.Medicine;
                if (group == null)
                    return false;
                int count = sourcePawn.inventoryStock.GetDesiredCountForGroup(group);
                ThingDef thing = sourcePawn.inventoryStock.GetDesiredThingForGroup(group);
                targetPawn.inventoryStock.SetThingForGroup(group, thing);
                targetPawn.inventoryStock.SetCountForGroup(group, count);
                return true;
            }
            return false;
        }

        private static bool ApplyGeneratedPayload(PawnColumnDef def, object payload, Pawn targetPawn)
        {
            if (payload == null)
                return false;
            return PawnColumnHandlerRegistry.Resolve(def) is IGeneratedMenuSource source
                && source.TryApplyPayloadToTarget(def, targetPawn, payload);
        }

        // === Policy management (context menu, Alt+N/R/C/E, Delete) ===

        private static bool IsPolicyWorker(PawnColumnDef def)
        {
            return def.Worker is PawnColumnWorker_Outfit
                || def.Worker is PawnColumnWorker_FoodRestriction
                || def.Worker is PawnColumnWorker_DrugPolicy
                || def.Worker is PawnColumnWorker_Reading
                || def.Worker is PawnColumnWorker_CombatPolicy
                || def.Worker is PawnColumnWorker_HuntPolicy;
        }

        public static bool IsColumnPolicyType(int index)
        {
            PawnColumnDef def = GetColumnDef(index);
            return def != null && IsPolicyWorker(def);
        }

        public static bool HasContextMenu(int index)
        {
            PawnColumnDef def = GetColumnDef(index);
            if (def == null)
                return false;
            return IsPolicyWorker(def) || def.Worker is PawnColumnWorker_MedicalCare;
        }

        public static List<FloatMenuOption> GetContextMenuOptions(
            int colIndex, Pawn pawn, Action refreshCallback, Action editCallback, Policy policyOverride = null)
        {
            PawnColumnDef def = GetColumnDef(colIndex);
            if (def == null)
                return null;

            if (def.Worker is PawnColumnWorker_Outfit)
            {
                var db = Current.Game?.outfitDatabase;
                if (db == null) return null;
                var policy = policyOverride ?? pawn.outfits?.CurrentApparelPolicy;
                List<FloatMenuOption> extras = null;
                if (policyOverride == null && pawn.outfits?.forcedHandler?.SomethingIsForced == true)
                {
                    extras = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("ClearForcedApparel".Translate(), () =>
                        {
                            pawn.outfits.forcedHandler.Reset();
                            refreshCallback?.Invoke();
                            TolkHelper.Speak("ClearForcedApparel".Loc());
                        })
                    };
                }
                return BuildPolicyContextMenu(
                    policy,
                    () => db.MakeNewOutfit(),
                    p => db.TryDelete((ApparelPolicy)p),
                    db.DefaultOutfit(),
                    p => db.SetDefault((ApparelPolicy)p),
                    refreshCallback, editCallback, extras);
            }

            if (def.Worker is PawnColumnWorker_FoodRestriction)
            {
                var db = Current.Game?.foodRestrictionDatabase;
                if (db == null) return null;
                var policy = policyOverride ?? pawn.foodRestriction?.CurrentFoodPolicy;
                return BuildPolicyContextMenu(
                    policy,
                    () => db.MakeNewFoodRestriction(),
                    p => db.TryDelete((FoodPolicy)p),
                    db.DefaultFoodRestriction(),
                    p => db.SetDefault((FoodPolicy)p),
                    refreshCallback, editCallback);
            }

            if (def.Worker is PawnColumnWorker_DrugPolicy)
            {
                var db = Current.Game?.drugPolicyDatabase;
                if (db == null) return null;
                var policy = policyOverride ?? pawn.drugs?.CurrentPolicy;
                return BuildPolicyContextMenu(
                    policy,
                    () => db.MakeNewDrugPolicy(),
                    p => db.TryDelete((DrugPolicy)p),
                    db.DefaultDrugPolicy(),
                    p => db.SetDefault((DrugPolicy)p),
                    refreshCallback, editCallback);
            }

            if (def.Worker is PawnColumnWorker_Reading)
            {
                var db = Current.Game?.readingPolicyDatabase;
                if (db == null) return null;
                var policy = policyOverride ?? pawn.reading?.CurrentPolicy;
                return BuildPolicyContextMenu(
                    policy,
                    () => db.MakeNewReadingPolicy(),
                    p => db.TryDelete((ReadingPolicy)p),
                    db.DefaultReadingPolicy(),
                    p => db.SetDefault((ReadingPolicy)p),
                    refreshCallback, editCallback);
            }

            if (def.Worker is PawnColumnWorker_CombatPolicy)
            {
                var policy = policyOverride ?? PawnColumnWorker_CombatPolicy.CurrentPolicy(pawn);
                return BuildPolicyContextMenu(
                    policy,
                    () => CombatAutopilotComponent.MakeNewPolicy(),
                    p => CombatAutopilotComponent.TryDeletePolicy((CombatPolicy)p),
                    CombatAutopilotComponent.DefaultPolicy(),
                    p => CombatAutopilotComponent.SetDefaultPolicy((CombatPolicy)p),
                    refreshCallback, editCallback);
            }

            if (def.Worker is PawnColumnWorker_HuntPolicy)
            {
                var policy = policyOverride ?? CombatAutopilotComponent.EffectiveHuntPolicy(pawn);
                return BuildPolicyContextMenu(
                    policy,
                    () => CombatAutopilotComponent.MakeNewHuntingPolicy(),
                    p => CombatAutopilotComponent.TryDeleteHuntingPolicy((HuntingPolicy)p),
                    CombatAutopilotComponent.DefaultHuntingPolicy(),
                    p => CombatAutopilotComponent.SetDefaultHuntingPolicy((HuntingPolicy)p),
                    refreshCallback, editCallback);
            }

            if (def.Worker is PawnColumnWorker_MedicalCare)
                return BuildMedicalCareContextMenu();

            return null;
        }

        /// <summary>
        /// Deletes a policy through vanilla's own confirm gate (Dialog_ManagePolicies&lt;T&gt;):
        /// held Ctrl skips straight to delete, mirroring the vanilla Ctrl+click on the trash icon;
        /// otherwise the real vanilla Dialog_Confirm gates the mutation, read by ConfirmDialogScope
        /// while AssignMenuScopeMirror stands down.
        /// </summary>
        internal static void DeletePolicyWithConfirm(Policy policy, Func<Policy, AcceptanceReport> tryDelete, Action refreshCallback)
        {
            Action doDelete = () =>
            {
                AcceptanceReport result = tryDelete(policy);
                if (!result.Accepted)
                    TolkHelper.SpeakData(result.Reason);
                else
                {
                    refreshCallback?.Invoke();
                    TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PolicyDeleted".Loc());
                }
            };

            if (KeyboardHelper.IsCtrlHeld)
            {
                doDelete();
                return;
            }

            TaggedString title = "DeletePolicyConfirm".Translate(policy.label);
            TaggedString confirmLabel = "DeletePolicyConfirmButton".Translate();
            Find.WindowStack.Add(new Dialog_Confirm(title, confirmLabel, doDelete));
        }

        private static List<FloatMenuOption> BuildPolicyContextMenu(
            Policy currentPolicy,
            Func<Policy> createNew,
            Func<Policy, AcceptanceReport> tryDelete,
            Policy defaultPolicy,
            Action<Policy> setDefault,
            Action refreshCallback,
            Action editCallback,
            List<FloatMenuOption> extraOptions = null)
        {
            var options = new List<FloatMenuOption>();

            options.Add(new FloatMenuOption($"{"NewPolicy".Translate()} (Alt+N)", () =>
            {
                var newPolicy = createNew();
                refreshCallback?.Invoke();
                TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PolicyCreated".Loc(newPolicy.label));
            }));

            if (currentPolicy != null)
            {
                options.Add(new FloatMenuOption(
                    $"{"Rename".Translate()}: {currentPolicy.label} (Alt+R)", () =>
                    {
                        Find.WindowStack.Add(new Dialog_RenamePolicy(currentPolicy));
                    }));

                options.Add(new FloatMenuOption(
                    $"{"Copy".Translate()}: {currentPolicy.label} (Alt+C)", () =>
                    {
                        var newPolicy = createNew();
                        newPolicy.CopyFrom(currentPolicy);
                        refreshCallback?.Invoke();
                        TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PolicyDuplicated".Loc(newPolicy.label));
                    }));

                options.Add(new FloatMenuOption(
                    $"{"Delete".Translate()}: {currentPolicy.label} (Delete)", () =>
                    {
                        DeletePolicyWithConfirm(currentPolicy, tryDelete, refreshCallback);
                    }));

                if (defaultPolicy != currentPolicy)
                {
                    options.Add(new FloatMenuOption(
                        $"{"Default".Translate()}: {currentPolicy.label}", () =>
                        {
                            setDefault(currentPolicy);
                            TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PolicySetAsDefault".Loc(currentPolicy.label));
                        }));
                }
            }

            if (editCallback != null)
            {
                options.Add(new FloatMenuOption(
                    $"{"AssignTabEdit".Translate()}: {currentPolicy.label} (Alt+E)", () =>
                    {
                        editCallback.Invoke();
                    }));
            }

            if (extraOptions != null)
            {
                options.AddRange(extraOptions);
            }

            return options;
        }

        // === Direct policy action execution (keyboard shortcuts) ===

        public static bool ExecutePolicyAction(PolicyAction action, int colIndex, Pawn pawn,
            Action refreshCallback, Action editCallback, Policy policyOverride = null)
        {
            PawnColumnDef def = GetColumnDef(colIndex);
            if (def == null)
                return false;

            if (def.Worker is PawnColumnWorker_Outfit)
            {
                var db = Current.Game?.outfitDatabase;
                if (db == null) return false;
                var policy = policyOverride ?? pawn.outfits?.CurrentApparelPolicy;
                return ExecuteAction(action, policy,
                    () => db.MakeNewOutfit(),
                    p => db.TryDelete((ApparelPolicy)p),
                    refreshCallback, editCallback);
            }
            if (def.Worker is PawnColumnWorker_FoodRestriction)
            {
                var db = Current.Game?.foodRestrictionDatabase;
                if (db == null) return false;
                var policy = policyOverride ?? pawn.foodRestriction?.CurrentFoodPolicy;
                return ExecuteAction(action, policy,
                    () => db.MakeNewFoodRestriction(),
                    p => db.TryDelete((FoodPolicy)p),
                    refreshCallback, editCallback);
            }
            if (def.Worker is PawnColumnWorker_DrugPolicy)
            {
                var db = Current.Game?.drugPolicyDatabase;
                if (db == null) return false;
                var policy = policyOverride ?? pawn.drugs?.CurrentPolicy;
                return ExecuteAction(action, policy,
                    () => db.MakeNewDrugPolicy(),
                    p => db.TryDelete((DrugPolicy)p),
                    refreshCallback, editCallback);
            }
            if (def.Worker is PawnColumnWorker_Reading)
            {
                var db = Current.Game?.readingPolicyDatabase;
                if (db == null) return false;
                var policy = policyOverride ?? pawn.reading?.CurrentPolicy;
                return ExecuteAction(action, policy,
                    () => db.MakeNewReadingPolicy(),
                    p => db.TryDelete((ReadingPolicy)p),
                    refreshCallback, editCallback);
            }
            if (def.Worker is PawnColumnWorker_CombatPolicy)
            {
                var policy = policyOverride ?? PawnColumnWorker_CombatPolicy.CurrentPolicy(pawn);
                return ExecuteAction(action, policy,
                    () => CombatAutopilotComponent.MakeNewPolicy(),
                    p => CombatAutopilotComponent.TryDeletePolicy((CombatPolicy)p),
                    refreshCallback, editCallback);
            }
            if (def.Worker is PawnColumnWorker_HuntPolicy)
            {
                var policy = policyOverride ?? CombatAutopilotComponent.EffectiveHuntPolicy(pawn);
                return ExecuteAction(action, policy,
                    () => CombatAutopilotComponent.MakeNewHuntingPolicy(),
                    p => CombatAutopilotComponent.TryDeleteHuntingPolicy((HuntingPolicy)p),
                    refreshCallback, editCallback);
            }
            return false;
        }

        private static bool ExecuteAction(PolicyAction action, Policy policy,
            Func<Policy> createNew, Func<Policy, AcceptanceReport> tryDelete,
            Action refreshCallback, Action editCallback)
        {
            switch (action)
            {
                case PolicyAction.New:
                    var newPolicy = createNew();
                    refreshCallback?.Invoke();
                    TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PolicyCreated".Loc(newPolicy.label));
                    return true;

                case PolicyAction.Rename:
                    if (policy == null) return false;
                    Find.WindowStack.Add(new Dialog_RenamePolicy(policy));
                    return true;

                case PolicyAction.Copy:
                    if (policy == null) return false;
                    var copied = createNew();
                    copied.CopyFrom(policy);
                    refreshCallback?.Invoke();
                    TolkHelper.Speak("RimWorldAccess.Pawns.AssignMenu.PolicyDuplicated".Loc(copied.label));
                    return true;

                case PolicyAction.Delete:
                    if (policy == null) return false;
                    DeletePolicyWithConfirm(policy, tryDelete, refreshCallback);
                    return true;

                case PolicyAction.Edit:
                    if (editCallback == null) return false;
                    editCallback.Invoke();
                    return true;

                default:
                    return false;
            }
        }

        private static List<FloatMenuOption> BuildMedicalCareContextMenu()
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("ChangeDefaults".Translate(), () =>
            {
                Find.WindowStack.Add(new Dialog_MedicalDefaults());
            }));
            return options;
        }
    }
}
