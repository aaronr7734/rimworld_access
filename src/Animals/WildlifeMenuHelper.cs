using System;
using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    public static class WildlifeMenuHelper
    {
        /// <summary>
        /// What a column DOES, derived once per <see cref="InitColumnDefs"/> from its worker's own
        /// runtime TYPE, never its position in the list.
        /// </summary>
        public enum ColumnType
        {
            Name,
            Gender,
            LifeStage,
            Hunt,
            ManhunterOnDamage,
            Tame,
            ManhunterOnTameFail,
            Predator,
            MentalState,
            /// <summary>A modded/DLC column with no bespoke reader here (also covers vanilla's own Info column — see <see cref="InitColumnDefs"/>): read generically through <see cref="PawnColumnHandlerRegistry"/> when its worker derives from a known vanilla base, honestly unavailable otherwise.</summary>
            Unknown
        }

        // === Column defs (the source of truth for the column SET, ORDER and NAMES) plus the
        // per-index classification derived from each def's own worker type. ===
        private static List<PawnColumnDef> columnDefs = new List<PawnColumnDef>();
        private static List<ColumnType> columnKinds = new List<ColumnType>();

        /// <summary>
        /// Builds the resolved column list straight from vanilla's own
        /// <see cref="PawnTableDefOf.Wildlife"/> def — its order, and any column a mod adds.
        /// Spacer/no-content columns (Gap, RemainingSpace) are skipped via the
        /// <see cref="PawnColumnHandlerRegistry"/> test, as is the Info column (a bare info-card
        /// button with no textual cell content of its own).
        /// </summary>
        public static void InitColumnDefs()
        {
            columnDefs = new List<PawnColumnDef>();
            columnKinds = new List<ColumnType>();

            List<PawnColumnDef> defs = PawnTableDefOf.Wildlife?.columns;
            if (defs == null)
                return;

            foreach (PawnColumnDef def in defs)
            {
                if (def == null)
                    continue;
                try
                {
                    if (PawnColumnHandlerRegistry.Resolve(def).SkipColumn(def))
                        continue;
                    if (def.Worker is PawnColumnWorker_Info)
                        continue;
                    columnDefs.Add(def);
                    columnKinds.Add(ClassifyColumn(def));
                }
                catch (Exception ex)
                {
                    // A broken (typically modded) worker must not drop the whole table.
                    Log.Warning("[RimWorld Access] Wildlife column '" + def.defName + "' failed to resolve and was skipped: " + ex);
                }
            }
        }

        /// <summary>Classifies a column by its worker's own runtime type (is-checks, so a modded subclass inherits the base behavior for free).</summary>
        private static ColumnType ClassifyColumn(PawnColumnDef def)
        {
            PawnColumnWorker worker = def.Worker;
            if (worker is PawnColumnWorker_Label) return ColumnType.Name;
            if (worker is PawnColumnWorker_Gender) return ColumnType.Gender;
            if (worker is PawnColumnWorker_LifeStage) return ColumnType.LifeStage;
            // Hunt/Tame both derive from PawnColumnWorker_Designator (a Checkbox subclass), so
            // check the concrete types before anything more general.
            if (worker is PawnColumnWorker_Hunt) return ColumnType.Hunt;
            if (worker is PawnColumnWorker_Tame) return ColumnType.Tame;
            if (worker is PawnColumnWorker_ManhunterOnDamageChance) return ColumnType.ManhunterOnDamage;
            if (worker is PawnColumnWorker_ManhunterOnTameFailChance) return ColumnType.ManhunterOnTameFail;
            if (worker is PawnColumnWorker_Predator) return ColumnType.Predator;
            if (worker is PawnColumnWorker_MentalState) return ColumnType.MentalState;
            return ColumnType.Unknown;
        }

        /// <summary>Internal so the focus driver can map the scope's column index onto vanilla's own column def with it.</summary>
        internal static PawnColumnDef GetDef(int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < columnDefs.Count ? columnDefs[columnIndex] : null;
        }

        /// <summary>The classification for a column index (Unknown if out of range or the worker has no bespoke reader here).</summary>
        public static ColumnType GetColumnType(int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < columnKinds.Count ? columnKinds[columnIndex] : ColumnType.Unknown;
        }

        /// <summary>The live def currently classified as <paramref name="kind"/> (first match) — for a mutation that needs a specific fixed column's own worker independent of its index.</summary>
        private static PawnColumnDef GetColumnDef(ColumnType kind)
        {
            for (int i = 0; i < columnKinds.Count; i++)
            {
                if (columnKinds[i] == kind)
                    return columnDefs[i];
            }
            return null;
        }

        public static bool IsColumnSortable(int columnIndex)
            => PawnColumnSortHelper.IsColumnSortable(columnDefs, columnIndex);

        public static int GetTotalColumnCount()
        {
            return columnDefs.Count;
        }

        // Column name by index, using RimWorld's localized strings where available.
        public static string GetColumnName(int columnIndex)
        {
            PawnColumnDef def = GetDef(columnIndex);
            if (def == null)
                return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();

            // Hunt/Tame and the two Manhunter columns carry only a paragraph-length headerTip, not
            // a usable short header, so their name comes from vanilla's short vocabulary instead.
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Hunt: return "DesignatorHunt".Translate().Resolve();
                case ColumnType.Tame: return "DesignatorTame".Translate().Resolve();
                case ColumnType.ManhunterOnDamage: return "HarmedRevengeChance".Translate().Resolve();
                case ColumnType.ManhunterOnTameFail: return "TameFailedRevengeChance".Translate().Resolve();
            }

            if (!def.label.NullOrEmpty())
                return def.LabelCap.ToString();
            if (!def.headerTip.NullOrEmpty())
                return def.headerTip;

            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Gender: return "Sex".Translate().Resolve();
                case ColumnType.Predator: return "RimWorldAccess.Animals.Wildlife.Column.Predator".Translate().ToString();
                case ColumnType.MentalState: return "RimWorldAccess.Animals.Column.MentalState".Translate().Resolve();
                default: return PawnColumnHandlerRegistry.Resolve(def).HeaderLabel(def) ?? def.defName;
            }
        }

        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (GetDef(columnIndex) == null)
                return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();

            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Name:
                    return GetAnimalNameWithActivity(pawn);
                case ColumnType.Gender:
                    return GetGender(pawn);
                case ColumnType.LifeStage:
                    return GetLifeStage(pawn);
                case ColumnType.Hunt:
                    return GetHuntStatus(pawn);
                case ColumnType.ManhunterOnDamage:
                    return GetManhunterOnDamageChance(pawn);
                case ColumnType.Tame:
                    return GetTameStatus(pawn);
                case ColumnType.ManhunterOnTameFail:
                    return GetManhunterOnTameFailChance(pawn);
                case ColumnType.Predator:
                    return GetPredatorStatus(pawn);
                case ColumnType.MentalState:
                    return AnimalsMenuHelper.GetMentalState(pawn);
                default:
                    // Modded workers derived from a known vanilla base read generically.
                    return PawnColumnCellReader.CellText(GetDef(columnIndex), pawn);
            }
        }

        /// <summary>Per-cell tip for an Unknown-classified column, via the registry; null for classified columns, whose tips are curated.</summary>
        public static string GetUnknownCellTip(Pawn pawn, int columnIndex)
        {
            return GetColumnType(columnIndex) == ColumnType.Unknown
                ? PawnColumnCellReader.CellTip(GetDef(columnIndex), pawn)
                : null;
        }

        // Whether a column is interactive (changeable with Enter).
        public static bool IsColumnInteractive(int columnIndex)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Name:
                case ColumnType.Hunt:
                case ColumnType.Tame:
                    return true;
                default:
                    // Gender/LifeStage/ManhunterOnDamage/ManhunterOnTameFail/Predator/MentalState
                    // are display-only; Unknown has no bespoke reader here.
                    return false;
            }
        }

        // Column tooltip, spoken on column navigation only.
        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
        {
            PawnColumnDef def = GetDef(columnIndex);
            if (def == null)
                return null;

            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Predator:
                    return "IsPredator".Translate().Resolve();
                case ColumnType.Hunt:
                case ColumnType.Tame:
                case ColumnType.ManhunterOnDamage:
                case ColumnType.ManhunterOnTameFail:
                    // Vanilla's own long-form headerTip for these four columns.
                    return def.headerTip;
                default:
                    return null;
            }
        }

        // === Column accessors ===

        /// <summary>The bare animal name without activity, for row labels.</summary>
        public static string GetAnimalName(Pawn pawn)
        {
            // Wild animals typically have no individual name, just a species.
            return pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
        }

        /// <summary>The animal name with current activity, for the Name column value.</summary>
        public static string GetAnimalNameWithActivity(Pawn pawn)
        {
            string name = GetAnimalName(pawn);
            string activity = PawnHelper.GetPawnActivity(pawn);
            return activity != null ? $"{name} - {activity}" : name;
        }

        public static string GetPredatorStatus(Pawn pawn)
        {
            if (pawn.RaceProps == null) return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
            return pawn.RaceProps.predator ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        public static string GetGender(Pawn pawn)
        {
            return pawn.gender.GetLabel(animal: true).CapitalizeFirst();
        }

        public static string GetLifeStage(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
            return pawn.ageTracker.CurLifeStage.label.CapitalizeFirst();
        }

        public static string GetHuntStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt);
            return designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        public static string GetManhunterOnDamageChance(Pawn pawn)
        {
            return PawnUtility.GetManhunterOnDamageChance(pawn).ToStringPercent();
        }

        /// <summary>
        /// Full tameability gate mirroring <see cref="PawnColumnWorker_Tame.HasCheckbox"/>, which
        /// calls <see cref="TameUtility.CanTame"/> — not just the Wildness stat. CanTame also
        /// excludes Dryads, Scaria-infected animals, and animals owned by a humanlike faction.
        /// </summary>
        public static string GetTameStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            if (!TameUtility.CanTame(pawn))
            {
                return "MessageMustDesignateTameable".Translate().Resolve();
            }

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame);
            string status = designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();

            string wildnessLabel = StatDefOf.Wildness.LabelCap.Resolve();
            string minHandlingLabel = StatDefOf.MinimumHandlingSkill.LabelCap.Resolve();

            List<string> infoParts = new List<string>();
            float wildness = pawn.GetStatValue(StatDefOf.Wildness);
            infoParts.Add($"{wildnessLabel}: {wildness.ToStringPercent()}");

            int minSkill = (int)pawn.GetStatValue(StatDefOf.MinimumHandlingSkill);
            if (minSkill > 0)
            {
                infoParts.Add($"{minHandlingLabel}: {minSkill}");
            }

            return $"{status}, {string.Join(", ", infoParts)}";
        }

        public static string GetManhunterOnTameFailChance(Pawn pawn)
        {
            return PawnUtility.GetManhunterOnTameFailChance(pawn).ToStringPercent();
        }

        // === Designation toggles ===

        /// <summary>
        /// Toggles the Hunt designation through PawnColumnWorker_Hunt's own SetValue (via
        /// PawnColumnMutationHelper — this scope has no live vanilla PawnTable to hand it).
        /// Notify_DesignationAdded is what removes a conflicting Tame designation and shows the
        /// warnings, so riding it rather than hand-adding the designation closes both.
        /// </summary>
        public static bool ToggleHuntDesignation(Pawn pawn)
        {
            if (pawn.Map == null) return false;

            bool newValue = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt) == null;
            PawnColumnDef huntDef = GetColumnDef(ColumnType.Hunt);
            PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Wildlife);
            PawnColumnMutationHelper.SetDesignatorValue(huntDef, pawn, newValue, table, afterward: null);
            return newValue;
        }

        /// <summary>
        /// Toggles the Tame designation through PawnColumnWorker_Tame's own SetValue. Gated on the
        /// full <see cref="TameUtility.CanTame"/>, not just Wildness.
        /// </summary>
        public static bool? ToggleTameDesignation(Pawn pawn)
        {
            if (pawn.Map == null) return null;
            if (!TameUtility.CanTame(pawn)) return null;

            bool newValue = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame) == null;
            PawnColumnDef tameDef = GetColumnDef(ColumnType.Tame);
            PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Wildlife);
            PawnColumnMutationHelper.SetDesignatorValue(tameDef, pawn, newValue, table, afterward: null);
            return newValue;
        }

        // === Painting support ===

        /// <summary>Whether a column supports painting, via runtime PawnColumnDef.paintable lookup.</summary>
        public static bool CanPaintColumn(int columnIndex)
        {
            return GetDef(columnIndex)?.paintable == true;
        }

        /// <summary>The current boolean value of a paintable column for a pawn.</summary>
        public static bool GetPaintableValue(Pawn pawn, int columnIndex)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Hunt:
                    return pawn.Map?.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt) != null;
                case ColumnType.Tame:
                    return pawn.Map?.designationManager.DesignationOn(pawn, DesignationDefOf.Tame) != null;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Sets a paintable column to a specific value (not toggle), riding PawnColumnWorker_Hunt/
        /// Tame's own SetValue exactly as the toggle path does. Neither worker overrides
        /// ShouldConfirmDesignation, so there is no blocking dialog here (unlike Slaughter/
        /// ReleaseToWild/Sterilize in AnimalsMenuHelper). False if the animal cannot accept the
        /// value or is already in that state.
        /// </summary>
        public static bool SetPaintableValue(Pawn pawn, int columnIndex, bool value)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Hunt:
                {
                    if (pawn.Map == null) return false;
                    bool current = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt) != null;
                    if (current == value) return false;
                    PawnColumnDef huntDef = GetColumnDef(ColumnType.Hunt);
                    PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Wildlife);
                    PawnColumnMutationHelper.SetDesignatorValue(huntDef, pawn, value, table, afterward: null);
                    return true;
                }

                case ColumnType.Tame:
                {
                    if (pawn.Map == null) return false;
                    if (!TameUtility.CanTame(pawn)) return false;
                    bool current = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame) != null;
                    if (current == value) return false;
                    PawnColumnDef tameDef = GetColumnDef(ColumnType.Tame);
                    PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Wildlife);
                    PawnColumnMutationHelper.SetDesignatorValue(tameDef, pawn, value, table, afterward: null);
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>The sound for painting a column.</summary>
        public static SoundDef GetPaintSound(int columnIndex, bool value)
        {
            return value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff;
        }

        /// <summary>The display label for a paint value ("checked", "unchecked").</summary>
        public static string GetPaintValueLabel(int columnIndex, bool value)
        {
            return value
                ? "RimWorldAccess.Animals.Paint.Checked".Translate().ToString()
                : "RimWorldAccess.Animals.Paint.Unchecked".Translate().ToString();
        }

        // === Sorting ===

        public static List<Pawn> SortWildlifeByColumn(List<Pawn> wildlife, int columnIndex, bool descending)
            => PawnColumnSortHelper.SortByColumnDef(wildlife, columnDefs, columnIndex, descending);
    }
}
