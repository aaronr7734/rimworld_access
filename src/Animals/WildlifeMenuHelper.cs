using System;
using System.Collections.Generic;
using System.Linq;
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
                return "Unknown";

            ColumnType type = (ColumnType)columnIndex;
            switch (type)
            {
                case ColumnType.Name: return "Name";
                case ColumnType.Predator: return "Predator";
                case ColumnType.Gender: return "Sex".Translate().Resolve();
                case ColumnType.LifeStage: return "LifeStage".Translate().Resolve();
                case ColumnType.Hunt: return "DesignatorHunt".Translate().Resolve();
                case ColumnType.ManhunterOnDamage: return "RevengeChance".Translate().Resolve();
                case ColumnType.Tame: return "DesignatorTame".Translate().Resolve();
                case ColumnType.ManhunterOnTameFail: return "TameFailedManhunterChance".Translate().Resolve();
                default: return "Unknown";
            }
        }

        // Get column value for a pawn
        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= totalColumns)
                return "Unknown";

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
                default: return "Unknown";
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
        public static string GetColumnTooltip(int columnIndex)
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
            if (pawn.RaceProps == null) return "Unknown";
            return pawn.RaceProps.predator ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        public static string GetGender(Pawn pawn)
        {
            // Use RimWorld's localized gender labels
            return pawn.gender.GetLabel(animal: true).CapitalizeFirst();
        }

        public static string GetLifeStage(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "Unknown";
            return pawn.ageTracker.CurLifeStage.label.CapitalizeFirst();
        }

        public static string GetHuntStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt);
            return designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        public static string GetManhunterOnDamageChance(Pawn pawn)
        {
            return PawnUtility.GetManhunterOnDamageChance(pawn).ToStringPercent();
        }

        public static string GetTameStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";
            if (!pawn.RaceProps.Animal) return "N/A";

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

        // === Sorting ===

        public static List<Pawn> SortWildlifeByColumn(List<Pawn> wildlife, int columnIndex, bool descending)
        {
            IEnumerable<Pawn> sorted = wildlife;

            if (columnIndex >= 0 && columnIndex < totalColumns)
            {
                ColumnType type = (ColumnType)columnIndex;
                switch (type)
                {
                    case ColumnType.Name:
                        sorted = wildlife.OrderBy(p => p.def.label);
                        break;
                    case ColumnType.Predator:
                        sorted = wildlife.OrderBy(p => p.RaceProps?.predator == true ? 0 : 1);
                        break;
                    case ColumnType.Gender:
                        sorted = wildlife.OrderBy(p => p.gender);
                        break;
                    case ColumnType.LifeStage:
                        sorted = wildlife.OrderBy(p => p.ageTracker?.CurLifeStageIndex ?? 0);
                        break;
                    case ColumnType.Hunt:
                        sorted = wildlife.OrderBy(p => GetHuntStatus(p));
                        break;
                    case ColumnType.ManhunterOnDamage:
                        sorted = wildlife.OrderBy(p => PawnUtility.GetManhunterOnDamageChance(p));
                        break;
                    case ColumnType.Tame:
                        sorted = wildlife.OrderBy(p => GetTameStatus(p));
                        break;
                    case ColumnType.ManhunterOnTameFail:
                        sorted = wildlife.OrderBy(p => PawnUtility.GetManhunterOnTameFailChance(p));
                        break;
                }
            }

            if (descending)
            {
                sorted = sorted.Reverse();
            }

            return sorted.ToList();
        }

        // Default sort: predators first, then by body size descending, then by label
        public static List<Pawn> DefaultSort(List<Pawn> wildlife)
        {
            return wildlife
                .OrderByDescending(p => p.RaceProps?.predator == true ? 1 : 0)
                .ThenByDescending(p => p.RaceProps?.baseBodySize ?? 0)
                .ThenBy(p => p.def.label)
                .ToList();
        }
    }
}
