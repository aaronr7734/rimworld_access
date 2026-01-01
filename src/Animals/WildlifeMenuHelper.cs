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
            Gender,
            LifeStage,
            Age,
            BodySize,
            Health,
            Pregnant,
            Hunt,
            Tame
        }

        private static int totalColumns = 9;

        // Get total column count
        public static int GetTotalColumnCount()
        {
            return totalColumns;
        }

        // Get column name by index
        public static string GetColumnName(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= totalColumns)
                return "Unknown";

            ColumnType type = (ColumnType)columnIndex;
            switch (type)
            {
                case ColumnType.Name: return "Name";
                case ColumnType.Gender: return "Gender";
                case ColumnType.LifeStage: return "Life Stage";
                case ColumnType.Age: return "Age";
                case ColumnType.BodySize: return "Body Size";
                case ColumnType.Health: return "Health";
                case ColumnType.Pregnant: return "Pregnant";
                case ColumnType.Hunt: return "Hunt";
                case ColumnType.Tame: return "Tame";
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
                case ColumnType.Name: return GetAnimalName(pawn);
                case ColumnType.Gender: return GetGender(pawn);
                case ColumnType.LifeStage: return GetLifeStage(pawn);
                case ColumnType.Age: return GetAge(pawn);
                case ColumnType.BodySize: return GetBodySize(pawn);
                case ColumnType.Health: return GetHealth(pawn);
                case ColumnType.Pregnant: return GetPregnancyStatus(pawn);
                case ColumnType.Hunt: return GetHuntStatus(pawn);
                case ColumnType.Tame: return GetTameStatus(pawn);
                default: return "Unknown";
            }
        }

        // Check if column is interactive (can be changed with Enter key)
        public static bool IsColumnInteractive(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= totalColumns)
                return false;

            ColumnType type = (ColumnType)columnIndex;
            return type == ColumnType.Hunt || type == ColumnType.Tame;
        }

        // === Column Accessors ===

        public static string GetAnimalName(Pawn pawn)
        {
            // Wild animals typically don't have individual names, just species
            string name = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
            return name;
        }

        public static string GetGender(Pawn pawn)
        {
            return pawn.gender.ToString();
        }

        public static string GetLifeStage(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "Unknown";
            return pawn.ageTracker.CurLifeStage.label.CapitalizeFirst();
        }

        public static string GetAge(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "Unknown";
            return pawn.ageTracker.AgeBiologicalYearsFloat.ToString("F1") + " years";
        }

        public static string GetBodySize(Pawn pawn)
        {
            if (pawn.RaceProps == null) return "Unknown";
            return pawn.RaceProps.baseBodySize.ToString("F2");
        }

        public static string GetHealth(Pawn pawn)
        {
            if (pawn.health == null) return "Unknown";

            float healthPercent = pawn.health.summaryHealth.SummaryHealthPercent;
            string healthText = $"{(healthPercent * 100f):F0}%";

            // Add injury/condition info if not at full health
            if (healthPercent < 1f)
            {
                var hediffs = pawn.health.hediffSet.hediffs
                    .Where(h => h.Visible && h.Label != null)
                    .Take(3)
                    .Select(h => h.Label);

                if (hediffs.Any())
                {
                    healthText += " (" + string.Join(", ", hediffs) + ")";
                }
            }

            return healthText;
        }

        public static string GetPregnancyStatus(Pawn pawn)
        {
            if (pawn.gender != Gender.Female) return "N/A";
            if (pawn.health?.hediffSet == null) return "Not pregnant";

            Hediff_Pregnant pregnancy = (Hediff_Pregnant)pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            if (pregnancy != null)
            {
                return $"Pregnant ({pregnancy.GestationProgress.ToStringPercent()} complete)";
            }
            return "Not pregnant";
        }

        public static string GetHuntStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Hunt);

            // Get manhunter on damage chance
            float manhunterChance = PawnUtility.GetManhunterOnDamageChance(pawn);
            string manhunterInfo = manhunterChance > 0f ? $", revenge chance: {manhunterChance.ToStringPercent()}" : "";

            if (designation != null)
            {
                return $"Marked for hunting{manhunterInfo}";
            }
            else
            {
                return manhunterChance > 0f ? $"Not marked (revenge chance: {manhunterChance.ToStringPercent()})" : "Not marked";
            }
        }

        public static string GetTameStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";
            if (!pawn.RaceProps.Animal) return "N/A";

            // Check if the animal is tameable (wildness stat >= 1 means untameable)
            float wildness = pawn.GetStatValue(StatDefOf.Wildness);
            if (wildness >= 1f)
            {
                return "Cannot tame (too wild)";
            }

            // Get minimum handling skill required
            int minSkill = (int)pawn.GetStatValue(StatDefOf.MinimumHandlingSkill);

            // Get manhunter on tame fail chance
            float manhunterChance = PawnUtility.GetManhunterOnTameFailChance(pawn);

            // Build info string
            List<string> infoParts = new List<string>();
            infoParts.Add($"wildness: {wildness.ToStringPercent()}");
            if (minSkill > 0)
            {
                infoParts.Add($"requires skill {minSkill}");
            }
            if (manhunterChance > 0f)
            {
                infoParts.Add($"manhunter on fail: {manhunterChance.ToStringPercent()}");
            }
            string infoString = string.Join(", ", infoParts);

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame);
            if (designation != null)
            {
                return $"Marked for taming ({infoString})";
            }

            return $"Not marked ({infoString})";
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
                    case ColumnType.Gender:
                        sorted = wildlife.OrderBy(p => p.gender);
                        break;
                    case ColumnType.LifeStage:
                        sorted = wildlife.OrderBy(p => p.ageTracker?.CurLifeStageIndex ?? 0);
                        break;
                    case ColumnType.Age:
                        sorted = wildlife.OrderBy(p => p.ageTracker?.AgeBiologicalYearsFloat ?? 0);
                        break;
                    case ColumnType.BodySize:
                        sorted = wildlife.OrderBy(p => p.RaceProps?.baseBodySize ?? 0);
                        break;
                    case ColumnType.Health:
                        sorted = wildlife.OrderBy(p => p.health?.summaryHealth.SummaryHealthPercent ?? 0);
                        break;
                    case ColumnType.Pregnant:
                        sorted = wildlife.OrderBy(p => GetPregnancyStatus(p));
                        break;
                    case ColumnType.Hunt:
                        sorted = wildlife.OrderBy(p => GetHuntStatus(p));
                        break;
                    case ColumnType.Tame:
                        sorted = wildlife.OrderBy(p => GetTameStatus(p));
                        break;
                }
            }

            if (descending)
            {
                sorted = sorted.Reverse();
            }

            return sorted.ToList();
        }

        // Default sort: by body size descending, then by label (matches PawnTable_Wildlife)
        public static List<Pawn> DefaultSort(List<Pawn> wildlife)
        {
            return wildlife
                .OrderByDescending(p => p.RaceProps?.baseBodySize ?? 0)
                .ThenBy(p => p.def.label)
                .ToList();
        }
    }
}
