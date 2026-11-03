using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>What Enter on a body cell did, so the scope picks the right announcement.</summary>
    public enum PawnColumnActivation
    {
        /// <summary>The handler has no cell action — fall through to the row default.</summary>
        NotHandled,
        /// <summary>A value changed in place — speak the cell's new state (toggle doctrine: state only, never the whole cell).</summary>
        StateChanged,
        /// <summary>The handler opened UI (a float menu, a dialog) that announces itself — stay silent.</summary>
        OpenedUI,
    }

    /// <summary>
    /// Reads and drives ONE pawn-table column kind for the generic pawn-table
    /// tier (the table-model generic tier). A handler is registered against a
    /// <see cref="PawnColumnWorker"/> TYPE and serves every column whose worker
    /// is that type or derives from it — a mod's custom checkbox column that
    /// subclasses <c>PawnColumnWorker_Checkbox</c> resolves to the checkbox
    /// handler with no per-mod code, the same teach-the-TYPE philosophy as
    /// <see cref="GizmoHandlerRegistry"/>.
    /// </summary>
    public interface IPawnColumnHandler
    {
        /// <summary>Purely visual columns (spacer gaps, remaining-space filler) are excluded from the table entirely.</summary>
        bool SkipColumn(PawnColumnDef def);

        /// <summary>A header label for a column whose def carries neither label nor headerTip (null = no opinion; the scope falls back to the defName).</summary>
        string HeaderLabel(PawnColumnDef def);

        /// <summary>The cell's readable value — always from the game's own state/decision objects, never re-derived.</summary>
        string CellText(PawnColumnDef def, Pawn pawn);

        /// <summary>The cell's tooltip, when the worker exposes one; null otherwise.</summary>
        string CellTip(PawnColumnDef def, Pawn pawn);

        string CellHint(PawnColumnDef def, Pawn pawn);

        /// <summary>Enter on the cell. See <see cref="PawnColumnActivation"/> for the announcement contract.</summary>
        PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table);

        /// <summary>
        /// True when <see cref="ActivateCell"/> never dereferences its table, so the windowless
        /// bespoke tabs (no live <see cref="PawnTable"/>) may pass null. Vanilla checkbox workers
        /// DO read the table (SortingBy, PawnsListForReading), so the default is false.
        /// </summary>
        bool ActivatesCellWithoutTable(PawnColumnDef def);

        /// <summary>Whether <see cref="AdjustCell"/> does anything for this column — the claim guard for the bracket chords.</summary>
        bool CanAdjustCell(PawnColumnDef def);

        /// <summary>
        /// Directional adjust of the cell's value (Left/Right bracket; -1 steps the priority
        /// down, +1 up, the work-table grammar). NotHandled for columns with no value axis.
        /// </summary>
        PawnColumnActivation AdjustCell(PawnColumnDef def, Pawn pawn, int direction, PawnTable table);

        /// <summary>
        /// True when this handler's own presentation of a cell already covers
        /// everything a sighted player would see there, so the generic
        /// pawn-table tier's geometry-scoped captured-extras router
        /// (<see cref="GenericPawnTableScope"/>) must not ALSO surface raw
        /// widgets it finds inside that cell's screen rect — e.g. a column
        /// whose handler replaces a hand-rolled mouse-drag strip with a real
        /// float-menu picker no longer wants the strip's own N option labels
        /// re-appearing as unmirrored extras. Most handlers have nothing to
        /// hide, so the default is false.
        /// </summary>
        bool SuppressCapturedExtras(PawnColumnDef def);
    }

    /// <summary>
    /// Resolves the <see cref="IPawnColumnHandler"/> for a column's worker type
    /// via the shared <see cref="TypeChainResolver{TValue}"/> (exact type first,
    /// then base types), falling back to a read-only handler that keeps unknown
    /// mod columns navigable and sortable while honestly announcing that the
    /// cell value is not readable.
    /// </summary>
    public static class PawnColumnHandlerRegistry
    {
        private static readonly TypeChainResolver<IPawnColumnHandler> resolver = new TypeChainResolver<IPawnColumnHandler>();
        private static readonly IPawnColumnHandler fallback = new FallbackColumnHandler();
        private static readonly HashSet<Type> loggedFailures = new HashSet<Type>();
        private static bool initialized;

        public static IPawnColumnHandler Resolve(PawnColumnDef def)
        {
            EnsureInitialized();
            Type workerType = WorkerTypeOf(def);
            if (workerType == null)
            {
                return fallback;
            }
            IPawnColumnHandler handler;
            return resolver.TryResolve(workerType, out handler) ? handler : fallback;
        }

        /// <summary>
        /// The runtime worker type. <see cref="PawnColumnDef.Worker"/> lazily
        /// instantiates <c>workerClass</c>; a broken mod's worker can throw in
        /// its constructor, so the failure degrades to the fallback handler
        /// (logged once per type) instead of killing the whole table.
        /// </summary>
        private static Type WorkerTypeOf(PawnColumnDef def)
        {
            if (def == null)
            {
                return null;
            }
            try
            {
                PawnColumnWorker worker = def.Worker;
                return worker != null ? worker.GetType() : def.workerClass;
            }
            catch (Exception ex)
            {
                if (def.workerClass != null && loggedFailures.Add(def.workerClass))
                {
                    Log.Warning("[RimWorld Access] Pawn column worker '" + def.workerClass.FullName
                        + "' failed to instantiate; column '" + def.defName + "' falls back to read-only. " + ex.Message);
                }
                return def.workerClass;
            }
        }

        /// <summary>
        /// Registers (or replaces) the handler for a worker type and its
        /// subtypes — the same shape as <see cref="GizmoHandlerRegistry.Register"/>,
        /// letting a compat shim teach the table a mod column type by
        /// resolving the mod's worker <see cref="Type"/> (typically via
        /// <c>AccessTools.TypeByName</c>, since the mod assembly is never
        /// referenced at compile time) and handing it a handler instance.
        /// No name-keyed tier is needed here: unlike gizmos, a mod's worker
        /// type is always a real, reflectable <see cref="Type"/> once
        /// resolved by name, so callers register against that Type object
        /// directly rather than a string.
        /// </summary>
        public static void Register(Type workerType, IPawnColumnHandler handler)
        {
            EnsureInitialized(); // built-ins first, so a compat registration wins
            resolver.Register(workerType, handler);
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;

            var skip = new SkipColumnHandler();
            resolver.Register(typeof(PawnColumnWorker_Gap), skip);
            resolver.Register(typeof(PawnColumnWorker_RemainingSpace), skip);

            resolver.Register(typeof(PawnColumnWorker_Text), new TextColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_Label), new LabelColumnHandler());
            // A Label subclass that labels a different pawn than the row's, so it
            // needs its own entry ahead of the Label chain walk.
            resolver.Register(typeof(PawnColumnWorker_Overseer), new OverseerColumnHandler());
            // Covers PawnColumnWorker_Designator and every other checkbox
            // subclass (Sterilize, FollowDrafted, Hunt/Tame/Slaughter, ...)
            // through the type-chain walk.
            resolver.Register(typeof(PawnColumnWorker_Checkbox), new CheckboxColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_Icon), new IconColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_Trainable), new TrainableColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_WorkPriority), new WorkPriorityColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_Timetable), new TimetableColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_AllowedArea), new AllowedAreaColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_MedicalCare), new MedicalCareColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_CopyPaste), new CopyPasteColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_Info), new InfoColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_HostilityResponse), new HostilityResponseColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_Carry), new CarryColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_WorkMode), new WorkModeColumnHandler());

            // Vanilla's own dropdown columns: the current value is the pawn's
            // assigned policy; Enter serves the worker's OWN menu generator
            // (Outfit, FoodRestriction) or, when DoCell delegates to an
            // external static utility instead of a method on the worker
            // itself (DrugPolicy -> DrugPolicyUIUtility, Reading ->
            // ReadingColumnUIUtility), that utility's own generator — so the
            // options (including the trailing "Edit..." manage entry) are
            // vanilla's decision objects verbatim.
            resolver.Register(typeof(PawnColumnWorker_Outfit), new OutfitColumnHandler());
            resolver.Register(typeof(PawnColumnWorker_FoodRestriction),
                new DropdownColumnHandler("Button_GenerateMenu",
                    p => p.foodRestriction != null && p.foodRestriction.CurrentFoodPolicy != null ? p.foodRestriction.CurrentFoodPolicy.label : null));
            resolver.Register(typeof(PawnColumnWorker_DrugPolicy),
                new DropdownColumnHandler("Button_GenerateMenu",
                    p => p.drugs != null && p.drugs.CurrentPolicy != null ? p.drugs.CurrentPolicy.label : null,
                    typeof(DrugPolicyUIUtility)));
            resolver.Register(typeof(PawnColumnWorker_Reading),
                new DropdownColumnHandler("Button_GenerateMenu",
                    p => p.reading != null && p.reading.CurrentPolicy != null ? p.reading.CurrentPolicy.label : null,
                    typeof(ReadingColumnUIUtility)));
            resolver.Register(typeof(PawnColumnWorker_CombatPolicy),
                new DropdownColumnHandler("Button_GenerateMenu",
                    p => PawnColumnWorker_CombatPolicy.AppliesTo(p) ? PawnColumnWorker_CombatPolicy.CurrentLabel(p) : null));
            resolver.Register(typeof(PawnColumnWorker_HuntPolicy),
                new DropdownColumnHandler("Button_GenerateMenu",
                    p => PawnColumnWorker_HuntPolicy.AppliesTo(p)
                        ? CombatAutopilotComponent.EffectiveHuntPolicy(p).label
                        : null));
            // The Animals/Mechs variants only draw cells for draftable rows.
            resolver.Register(typeof(PawnColumnWorker_CombatPolicyDraftGated),
                new DropdownColumnHandler("Button_GenerateMenu",
                    p => PawnColumnWorker_CombatPolicy.AppliesTo(p) ? PawnColumnWorker_CombatPolicy.CurrentLabel(p) : null,
                    cellExists: PawnColumnWorker_CombatPolicyDraftGated.Draftable));
        }
    }
}
