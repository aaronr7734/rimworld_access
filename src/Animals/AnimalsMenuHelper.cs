using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class AnimalsMenuHelper
    {
        // Column type enumeration for fixed columns
        public enum ColumnType
        {
            Name,
            Bond,
            Master,
            Slaughter,
            Gender,
            LifeStage,
            Age,
            Pregnant,
            // Dynamic training columns inserted here
            FollowDrafted,
            FollowFieldwork,
            AllowedArea,
            MedicalCare,
            FoodRestriction,
            ReleaseToWild
        }

        private static List<TrainableDef> cachedTrainables = null;
        private static int fixedColumnsBeforeTraining = 8; // Name through Pregnant
        private static int fixedColumnsAfterTraining = 6; // FollowDrafted through ReleaseToWild

        // Get all trainable definitions (cached)
        public static List<TrainableDef> GetAllTrainables()
        {
            if (cachedTrainables == null)
            {
                cachedTrainables = DefDatabase<TrainableDef>.AllDefsListForReading
                    .Where(t => !t.specialTrainable)
                    .OrderByDescending(t => t.listPriority)
                    .ToList();
            }
            return cachedTrainables;
        }

        // Get total column count (fixed + dynamic training columns)
        public static int GetTotalColumnCount()
        {
            return fixedColumnsBeforeTraining + GetAllTrainables().Count + fixedColumnsAfterTraining;
        }

        // Get column name by index
        public static string GetColumnName(int columnIndex)
        {
            if (columnIndex < fixedColumnsBeforeTraining)
            {
                // Fixed columns before training
                return ((ColumnType)columnIndex).ToString();
            }
            else if (columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count)
            {
                // Training columns
                int trainableIndex = columnIndex - fixedColumnsBeforeTraining;
                return GetAllTrainables()[trainableIndex].LabelCap;
            }
            else
            {
                // Fixed columns after training
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                ColumnType type = (ColumnType)(fixedColumnsBeforeTraining + fixedIndex);
                return type.ToString().Replace("_", " ");
            }
        }

        // Get column value for a pawn
        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (columnIndex < fixedColumnsBeforeTraining)
            {
                // Fixed columns before training
                switch ((ColumnType)columnIndex)
                {
                    case ColumnType.Name:
                        return GetAnimalName(pawn);
                    case ColumnType.Bond:
                        return GetBondStatus(pawn);
                    case ColumnType.Master:
                        return GetMasterName(pawn);
                    case ColumnType.Slaughter:
                        return GetSlaughterStatus(pawn);
                    case ColumnType.Gender:
                        return GetGender(pawn);
                    case ColumnType.LifeStage:
                        return GetLifeStage(pawn);
                    case ColumnType.Age:
                        return GetAge(pawn);
                    case ColumnType.Pregnant:
                        return GetPregnancyStatus(pawn);
                }
            }
            else if (columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count)
            {
                // Training columns
                int trainableIndex = columnIndex - fixedColumnsBeforeTraining;
                TrainableDef trainable = GetAllTrainables()[trainableIndex];
                return GetTrainingStatus(pawn, trainable);
            }
            else
            {
                // Fixed columns after training
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                ColumnType type = (ColumnType)(fixedColumnsBeforeTraining + fixedIndex);
                switch (type)
                {
                    case ColumnType.FollowDrafted:
                        return GetFollowDrafted(pawn);
                    case ColumnType.FollowFieldwork:
                        return GetFollowFieldwork(pawn);
                    case ColumnType.AllowedArea:
                        return GetAllowedArea(pawn);
                    case ColumnType.MedicalCare:
                        return GetMedicalCare(pawn);
                    case ColumnType.FoodRestriction:
                        return GetFoodRestriction(pawn);
                    case ColumnType.ReleaseToWild:
                        return GetReleaseToWildStatus(pawn);
                }
            }
            return "Unknown";
        }

        // Check if column is interactive (can be changed with Enter key)
        public static bool IsColumnInteractive(int columnIndex)
        {
            if (columnIndex < fixedColumnsBeforeTraining)
            {
                ColumnType type = (ColumnType)columnIndex;
                return type == ColumnType.Master || type == ColumnType.Slaughter;
            }
            else if (columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count)
            {
                return true; // All training columns are interactive
            }
            else
            {
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                ColumnType type = (ColumnType)(fixedColumnsBeforeTraining + fixedIndex);
                return type == ColumnType.FollowDrafted ||
                       type == ColumnType.FollowFieldwork ||
                       type == ColumnType.AllowedArea ||
                       type == ColumnType.MedicalCare ||
                       type == ColumnType.FoodRestriction ||
                       type == ColumnType.ReleaseToWild;
            }
        }

        // === Fixed Column Accessors ===

        public static string GetAnimalName(Pawn pawn)
        {
            string name = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
            return $"{name} ({pawn.def.LabelCap})";
        }

        public static string GetBondStatus(Pawn pawn)
        {
            if (pawn.relations == null) return "No bond";

            Pawn bondedPawn = pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Bond);
            if (bondedPawn != null)
            {
                return $"Bonded to {bondedPawn.Name.ToStringShort}";
            }
            return "No bond";
        }

        public static string GetMasterName(Pawn pawn)
        {
            if (pawn.playerSettings == null || pawn.playerSettings.Master == null)
            {
                return "None";
            }
            return pawn.playerSettings.Master.Name.ToStringShort;
        }

        public static string GetSlaughterStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter);
            return designation != null ? "Marked for slaughter" : "Not marked";
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

        public static string GetPregnancyStatus(Pawn pawn)
        {
            if (pawn.gender != Gender.Female) return "N/A";
            if (pawn.health?.hediffSet == null) return "Not pregnant";

            Hediff_Pregnant pregnancy = (Hediff_Pregnant)pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            if (pregnancy != null)
            {
                int daysLeft = (int)((pregnancy.GestationProgress - pregnancy.GestationProgress) * pawn.RaceProps.gestationPeriodDays);
                return $"Pregnant ({pregnancy.GestationProgress.ToStringPercent()} complete)";
            }
            return "Not pregnant";
        }

        // === Training Column Accessors ===

        public static string GetTrainingStatus(Pawn pawn, TrainableDef trainable)
        {
            if (pawn.training == null) return "N/A";

            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(trainable);

            string statusText = "";

            if (!canTrain.Accepted)
            {
                statusText = "Cannot train";
                // Add the reason why they can't train
                if (!string.IsNullOrEmpty(canTrain.Reason))
                {
                    statusText += " - " + canTrain.Reason;
                }
            }
            else
            {
                bool wanted = pawn.training.GetWanted(trainable);
                if (!wanted)
                {
                    statusText = "Disabled";
                }
                else if (pawn.training.HasLearned(trainable))
                {
                    statusText = "Trained";
                }
                else
                {
                    // Use reflection to access internal GetSteps method
                    var getStepsMethod = typeof(Pawn_TrainingTracker).GetMethod("GetSteps",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (getStepsMethod != null)
                    {
                        int steps = (int)getStepsMethod.Invoke(pawn.training, new object[] { trainable });
                        if (steps > 0)
                        {
                            statusText = $"Learning ({steps}/{trainable.steps})";
                        }
                        else
                        {
                            statusText = "Not started";
                        }
                    }
                    else
                    {
                        statusText = "Not started";
                    }
                }

                // Add prerequisite information if not learned and has prerequisites
                if (!pawn.training.HasLearned(trainable) && trainable.prerequisites != null && trainable.prerequisites.Count > 0)
                {
                    foreach (var prereq in trainable.prerequisites)
                    {
                        if (!pawn.training.HasLearned(prereq))
                        {
                            statusText += $" - Needs {prereq.LabelCap} first";
                            break; // Only show first missing prerequisite to keep it concise
                        }
                    }
                }
            }

            // Add training description
            if (!string.IsNullOrEmpty(trainable.description))
            {
                statusText += " - " + trainable.description;
            }

            return statusText;
        }

        public static TrainableDef GetTrainableAtColumn(int columnIndex)
        {
            if (columnIndex < fixedColumnsBeforeTraining ||
                columnIndex >= fixedColumnsBeforeTraining + GetAllTrainables().Count)
            {
                return null;
            }

            int trainableIndex = columnIndex - fixedColumnsBeforeTraining;
            return GetAllTrainables()[trainableIndex];
        }

        // === Follow Settings ===

        public static string GetFollowDrafted(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            string status = pawn.playerSettings.followDrafted ? "Yes" : "No";
            string description = "Follow master while the master is drafted.";

            return $"{status} - {description}";
        }

        public static string GetFollowFieldwork(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            string status = pawn.playerSettings.followFieldwork ? "Yes" : "No";
            string description = "Follow master while the master is doing field work (hunting or taming animals).";

            return $"{status} - {description}";
        }

        // === Area Restriction ===

        public static string GetAllowedArea(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            Area area = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
            if (area == null)
            {
                return "Unrestricted";
            }
            return area.Label;
        }

        public static List<Area> GetAvailableAreas()
        {
            if (Find.CurrentMap == null) return new List<Area>();

            return Find.CurrentMap.areaManager.AllAreas
                .Where(a => a.AssignableAsAllowed())
                .ToList();
        }

        // === Medical Care ===

        public static string GetMedicalCare(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            MedicalCareCategory category = pawn.playerSettings.medCare;
            return category.GetLabel();
        }

        public static List<MedicalCareCategory> GetMedicalCareLevels()
        {
            return Enum.GetValues(typeof(MedicalCareCategory))
                .Cast<MedicalCareCategory>()
                .ToList();
        }

        // === Food Restriction ===

        public static string GetFoodRestriction(Pawn pawn)
        {
            if (pawn.foodRestriction == null || pawn.foodRestriction.CurrentFoodPolicy == null)
            {
                return "Unrestricted";
            }
            return pawn.foodRestriction.CurrentFoodPolicy.label;
        }

        public static List<FoodPolicy> GetFoodPolicies()
        {
            if (Current.Game == null) return new List<FoodPolicy>();

            return Current.Game.foodRestrictionDatabase.AllFoodRestrictions.ToList();
        }

        // === Release to Wild ===

        public static string GetReleaseToWildStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild);
            return designation != null ? "Marked for release" : "Not marked";
        }

        // === Master Assignment ===

        public static List<Pawn> GetAvailableColonists()
        {
            if (Find.CurrentMap == null) return new List<Pawn>();

            return Find.CurrentMap.mapPawns.FreeColonistsSpawned
                .Where(p => !p.Dead && !p.Downed)
                .OrderBy(p => p.Name.ToStringShort)
                .ToList();
        }

        // === Sorting ===

        public static List<Pawn> SortAnimalsByColumn(List<Pawn> animals, int columnIndex, bool descending)
        {
            IEnumerable<Pawn> sorted = null;

            if (columnIndex < fixedColumnsBeforeTraining)
            {
                ColumnType type = (ColumnType)columnIndex;
                switch (type)
                {
                    case ColumnType.Name:
                        sorted = animals.OrderBy(p => p.Name?.ToStringShort ?? p.def.label);
                        break;
                    case ColumnType.Bond:
                        sorted = animals.OrderBy(p => GetBondStatus(p));
                        break;
                    case ColumnType.Master:
                        sorted = animals.OrderBy(p => GetMasterName(p));
                        break;
                    case ColumnType.Slaughter:
                        sorted = animals.OrderBy(p => GetSlaughterStatus(p));
                        break;
                    case ColumnType.Gender:
                        sorted = animals.OrderBy(p => p.gender);
                        break;
                    case ColumnType.LifeStage:
                        sorted = animals.OrderBy(p => p.ageTracker.CurLifeStageIndex);
                        break;
                    case ColumnType.Age:
                        sorted = animals.OrderBy(p => p.ageTracker.AgeBiologicalYearsFloat);
                        break;
                    case ColumnType.Pregnant:
                        sorted = animals.OrderBy(p => GetPregnancyStatus(p));
                        break;
                    default:
                        sorted = animals;
                        break;
                }
            }
            else if (columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count)
            {
                // Sort by training status
                TrainableDef trainable = GetTrainableAtColumn(columnIndex);
                if (trainable != null)
                {
                    sorted = animals.OrderBy(p => GetTrainingStatus(p, trainable));
                }
                else
                {
                    sorted = animals;
                }
            }
            else
            {
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                ColumnType type = (ColumnType)(fixedColumnsBeforeTraining + fixedIndex);
                switch (type)
                {
                    case ColumnType.FollowDrafted:
                        sorted = animals.OrderBy(p => GetFollowDrafted(p));
                        break;
                    case ColumnType.FollowFieldwork:
                        sorted = animals.OrderBy(p => GetFollowFieldwork(p));
                        break;
                    case ColumnType.AllowedArea:
                        sorted = animals.OrderBy(p => GetAllowedArea(p));
                        break;
                    case ColumnType.MedicalCare:
                        sorted = animals.OrderBy(p => GetMedicalCare(p));
                        break;
                    case ColumnType.FoodRestriction:
                        sorted = animals.OrderBy(p => GetFoodRestriction(p));
                        break;
                    case ColumnType.ReleaseToWild:
                        sorted = animals.OrderBy(p => GetReleaseToWildStatus(p));
                        break;
                    default:
                        sorted = animals;
                        break;
                }
            }

            if (descending)
            {
                sorted = sorted.Reverse();
            }

            return sorted.ToList();
        }
    }
}
