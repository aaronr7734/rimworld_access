using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class WildlifeMenuHelper
    {
        // Column type enumeration for wildlife menu
        public enum ColumnType
        {
            Name,
            Predator,
            Gender,
            LifeStage,
            Hunt,
            ManhunterOnDamage,
            Tame,
            ManhunterOnTameFail
        }

        private static int totalColumns = 8;

        // Get total column count
        public static int GetTotalColumnCount()
        {
            return totalColumns;
        }

        // Get column name by index (using RimWorld's localized strings)
        public static string GetColumnName(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= totalColumns)
                return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();

            ColumnType type = (ColumnType)columnIndex;
            switch (type)
            {
                case ColumnType.Name: return "RimWorldAccess.Animals.Wildlife.Column.Name".Translate().ToString();
                case ColumnType.Predator: return "RimWorldAccess.Animals.Wildlife.Column.Predator".Translate().ToString();
                case ColumnType.Gender: return "Sex".Translate().Resolve();
                case ColumnType.LifeStage: return "LifeStage".Translate().Resolve();
                case ColumnType.Hunt: return "DesignatorHunt".Translate().Resolve();
                case ColumnType.ManhunterOnDamage: return "HarmedRevengeChance".Translate().Resolve();
                case ColumnType.Tame: return "DesignatorTame".Translate().Resolve();
                case ColumnType.ManhunterOnTameFail: return "TameFailedRevengeChance".Translate().Resolve();
                default: return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
            }
        }

        // Get column value for a pawn
        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= totalColumns)
                return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();

            ColumnType type = (ColumnType)columnIndex;
            switch (type)
            {
                case ColumnType.Name: return GetAnimalNameWithActivity(pawn);
                case ColumnType.Predator: return GetPredatorStatus(pawn);
                case ColumnType.Gender: return GetGender(pawn);
                case ColumnType.LifeStage: return GetLifeStage(pawn);
                case ColumnType.Hunt: return GetHuntStatus(pawn);
                case ColumnType.ManhunterOnDamage: return GetManhunterOnDamageChance(pawn);
                case ColumnType.Tame: return GetTameStatus(pawn);
                case ColumnType.ManhunterOnTameFail: return GetManhunterOnTameFailChance(pawn);
                default: return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
            }
        }

        // Check if column is interactive (can be changed with Enter key)
        public static bool IsColumnInteractive(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= totalColumns)
                return false;

            ColumnType type = (ColumnType)columnIndex;
            return type == ColumnType.Name || type == ColumnType.Hunt || type == ColumnType.Tame;
        }

        // Get column tooltip (shown only on column navigation, not row navigation)
        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= totalColumns)
                return null;

            ColumnType type = (ColumnType)columnIndex;
            switch (type)
            {
                case ColumnType.Predator:
                    return "IsPredator".Translate().Resolve();
                case ColumnType.Hunt:
                    return DefDatabase<PawnColumnDef>.GetNamedSilentFail("Hunt")?.headerTip;
                case ColumnType.ManhunterOnDamage:
                    return DefDatabase<PawnColumnDef>.GetNamedSilentFail("ManhunterOnDamageChance")?.headerTip;
                case ColumnType.Tame:
                    return DefDatabase<PawnColumnDef>.GetNamedSilentFail("Tame")?.headerTip;
                case ColumnType.ManhunterOnTameFail:
                    return DefDatabase<PawnColumnDef>.GetNamedSilentFail("ManhunterOnTameFailChance")?.headerTip;
                default:
                    return null;
            }
        }

        // === Column Accessors ===

        /// <summary>
        /// Gets the basic animal name without activity (used for row labels).
        /// </summary>
        public static string GetAnimalName(Pawn pawn)
        {
            // Wild animals typically don't have individual names, just species
            return pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
        }

        /// <summary>
        /// Gets the animal name with current activity (used for Name column value).
        /// </summary>
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
            // Use RimWorld's localized gender labels
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

        public static string GetTameStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();
            if (!pawn.RaceProps.Animal) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            // Check if the animal is tameable (wildness stat >= 1 means untameable)
            float wildness = pawn.GetStatValue(StatDefOf.Wildness);
            if (wildness >= 1f)
            {
                return "MessageMustDesignateTameable".Translate().Resolve();
            }

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame);
            string status = designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();

            // Append wildness and min handling skill
            string wildnessLabel = StatDefOf.Wildness.LabelCap.Resolve();
            string minHandlingLabel = StatDefOf.MinimumHandlingSkill.LabelCap.Resolve();

            List<string> infoParts = new List<string>();
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

        // === Designation Toggles ===

        public static bool ToggleHuntDesignation(Pawn pawn)
        {
            if (pawn.Map == null) return false;

            Designation existing = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt);

            if (existing != null)
            {
                pawn.Map.designationManager.RemoveDesignation(existing);
                return false; // Now unmarked
            }
            else
            {
                pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Hunt));
                // Show warnings (manhunter risk, no hunters, etc.) - same as vanilla Wildlife tab
                Designator_Hunt.ShowDesignationWarnings(pawn);
                return true; // Now marked
            }
        }

        public static bool? ToggleTameDesignation(Pawn pawn)
        {
            if (pawn.Map == null) return null;
            if (!pawn.RaceProps.Animal) return null;

            // Check if the animal is tameable (wildness stat >= 1 means untameable)
            if (pawn.GetStatValue(StatDefOf.Wildness) >= 1f)
            {
                return null; // Cannot tame
            }

            Designation existing = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame);

            if (existing != null)
            {
                pawn.Map.designationManager.RemoveDesignation(existing);
                return false; // Now unmarked
            }
            else
            {
                pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Tame));
                // Show warnings (manhunter risk, no handlers, etc.) - same as vanilla Wildlife tab
                TameUtility.ShowDesignationWarnings(pawn);
                return true; // Now marked
            }
        }

        // === Painting Support ===

        /// <summary>
        /// Checks if a column supports painting (drag-to-apply).
        /// Uses runtime PawnColumnDef.paintable lookup.
        /// </summary>
        public static bool CanPaintColumn(int columnIndex)
        {
            if (columnDefs == null || columnIndex < 0 || columnIndex >= totalColumns)
                return false;
            return columnDefs[columnIndex]?.paintable == true;
        }

        /// <summary>
        /// Gets the current boolean value of a paintable column for a pawn.
        /// </summary>
        public static bool GetPaintableValue(Pawn pawn, int columnIndex)
        {
            ColumnType type = (ColumnType)columnIndex;
            switch (type)
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
        /// Sets a paintable column to a specific value (not toggle).
        /// Returns false if the animal can't accept the value or is already in the desired state.
        /// </summary>
        public static bool SetPaintableValue(Pawn pawn, int columnIndex, bool value)
        {
            ColumnType type = (ColumnType)columnIndex;
            switch (type)
            {
                case ColumnType.Hunt:
                    if (pawn.Map == null) return false;
                    var huntDes = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt);
                    if (value && huntDes == null)
                    {
                        pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Hunt));
                        return true;
                    }
                    if (!value && huntDes != null)
                    {
                        pawn.Map.designationManager.RemoveDesignation(huntDes);
                        return true;
                    }
                    return false;

                case ColumnType.Tame:
                    if (pawn.Map == null) return false;
                    if (pawn.GetStatValue(StatDefOf.Wildness) >= 1f) return false;
                    var tameDes = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame);
                    if (value && tameDes == null)
                    {
                        pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Tame));
                        return true;
                    }
                    if (!value && tameDes != null)
                    {
                        pawn.Map.designationManager.RemoveDesignation(tameDes);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets the appropriate sound for painting a column.
        /// </summary>
        public static SoundDef GetPaintSound(int columnIndex, bool value)
        {
            return value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff;
        }

        /// <summary>
        /// Gets the display label for a paint value (e.g., "checked", "unchecked").
        /// </summary>
        public static string GetPaintValueLabel(int columnIndex, bool value)
        {
            return value
                ? "RimWorldAccess.Animals.Paint.Checked".Translate().ToString()
                : "RimWorldAccess.Animals.Paint.Unchecked".Translate().ToString();
        }

        // === Column Defs (for sorting via game logic) ===

        private static readonly string[] columnDefNames = new string[]
        {
            "LabelWithIcon",             // Name
            "Predator",                  // Predator
            "Gender",                    // Gender
            "LifeStage",                 // LifeStage
            "Hunt",                      // Hunt
            "ManhunterOnDamageChance",   // ManhunterOnDamage
            "Tame",                      // Tame
            "ManhunterOnTameFailChance"  // ManhunterOnTameFail
        };

        private static PawnColumnDef[] columnDefs;

        public static void InitColumnDefs()
        {
            columnDefs = new PawnColumnDef[totalColumns];
            for (int i = 0; i < totalColumns; i++)
            {
                columnDefs[i] = DefDatabase<PawnColumnDef>.GetNamedSilentFail(columnDefNames[i]);
            }
        }

        public static bool IsColumnSortable(int columnIndex)
            => PawnColumnSortHelper.IsColumnSortable(columnDefs, columnIndex);

        // === Sorting ===

        public static List<Pawn> SortWildlifeByColumn(List<Pawn> wildlife, int columnIndex, bool descending)
            => PawnColumnSortHelper.SortByColumnDef(wildlife, columnDefs, columnIndex, descending);

    }
}
