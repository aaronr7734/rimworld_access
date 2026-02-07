using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class AnimalsMenuHelper
    {
        // Column type enumeration matching vanilla PawnTables.xml order
        public enum ColumnType
        {
            // Fixed columns before training
            Name,           // LabelWithIcon
            Gender,
            Age,
            LifeStage,
            Pregnant,
            // Dynamic training columns inserted here (index 5+)
            // Fixed columns after training (starting at fixedColumnsBeforeTraining + trainable count)
            SpecialTrainable, // Odyssey DLC - race-specific abilities (TerrorRoar, Comfort, etc.)
            FollowDrafted,
            FollowFieldwork,
            AnimalDig,      // Odyssey DLC - behavior toggle
            AnimalForage,   // Odyssey DLC - behavior toggle
            Master,
            MentalState,
            Bond,
            Sterile,
            Slaughter,
            MedicalCare,
            ReleaseToWild,
            AllowedArea
        }

        private static List<TrainableDef> cachedTrainables = null;
        private static int fixedColumnsBeforeTraining = 5; // Name through Pregnant

        // DLC detection
        private static bool IsOdysseyActive => ModsConfig.IsActive("Ludeon.RimWorld.Odyssey");

        // Get the list of column types after training, filtering out Odyssey-only columns when DLC isn't active
        private static List<ColumnType> GetColumnsAfterTraining()
        {
            var columns = new List<ColumnType>();

            if (IsOdysseyActive)
                columns.Add(ColumnType.SpecialTrainable);

            columns.Add(ColumnType.FollowDrafted);
            columns.Add(ColumnType.FollowFieldwork);

            if (IsOdysseyActive)
            {
                columns.Add(ColumnType.AnimalDig);
                columns.Add(ColumnType.AnimalForage);
            }

            columns.Add(ColumnType.Master);
            columns.Add(ColumnType.MentalState);
            columns.Add(ColumnType.Bond);
            columns.Add(ColumnType.Sterile);
            columns.Add(ColumnType.Slaughter);
            columns.Add(ColumnType.MedicalCare);
            columns.Add(ColumnType.ReleaseToWild);
            columns.Add(ColumnType.AllowedArea);

            return columns;
        }

        // Check if any colony animal has learned Dig
        private static bool AnyAnimalHasLearnedDig()
        {
            if (!IsOdysseyActive || Find.CurrentMap == null) return false;
            foreach (Pawn animal in Find.CurrentMap.mapPawns.ColonyAnimals)
            {
                if (animal.training?.HasLearned(TrainableDefOf.Dig) == true)
                    return true;
            }
            return false;
        }

        // Check if any colony animal has learned Forage
        private static bool AnyAnimalHasLearnedForage()
        {
            if (!IsOdysseyActive || Find.CurrentMap == null) return false;
            foreach (Pawn animal in Find.CurrentMap.mapPawns.ColonyAnimals)
            {
                if (animal.training?.HasLearned(TrainableDefOf.Forage) == true)
                    return true;
            }
            return false;
        }

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

        // Get total column count (fixed + dynamic training columns + fixed after training)
        public static int GetTotalColumnCount()
        {
            return fixedColumnsBeforeTraining + GetAllTrainables().Count + GetColumnsAfterTraining().Count;
        }

        // Get column name by index (using RimWorld's localized strings)
        public static string GetColumnName(int columnIndex)
        {
            if (columnIndex < fixedColumnsBeforeTraining)
            {
                // Fixed columns before training
                ColumnType type = (ColumnType)columnIndex;
                switch (type)
                {
                    case ColumnType.Name: return "Name";
                    case ColumnType.Gender: return "Sex".Translate().Resolve();
                    case ColumnType.Age: return "Age";
                    case ColumnType.LifeStage: return "LifeStage".Translate().Resolve();
                    case ColumnType.Pregnant: return HediffDefOf.Pregnant.LabelCap.Resolve();
                    default: return type.ToString();
                }
            }
            else if (columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count)
            {
                // Training columns - already localized via LabelCap
                int trainableIndex = columnIndex - fixedColumnsBeforeTraining;
                return GetAllTrainables()[trainableIndex].LabelCap;
            }
            else
            {
                // Fixed columns after training - use dynamic list
                var columnsAfterTraining = GetColumnsAfterTraining();
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                if (fixedIndex < 0 || fixedIndex >= columnsAfterTraining.Count)
                    return "Unknown";

                ColumnType type = columnsAfterTraining[fixedIndex];
                return GetColumnNameForType(type);
            }
        }

        // Helper to get column name for a ColumnType
        private static string GetColumnNameForType(ColumnType type)
        {
            switch (type)
            {
                case ColumnType.SpecialTrainable: return "SpecialTraining".Translate().Resolve();
                case ColumnType.FollowDrafted: return "CreatureFollowDrafted".Translate().Resolve();
                case ColumnType.FollowFieldwork: return "CreatureFollowFieldwork".Translate().Resolve();
                case ColumnType.AnimalDig: return "DigEnabled".Translate().Resolve();
                case ColumnType.AnimalForage: return "ForageEnabled".Translate().Resolve();
                case ColumnType.Master: return "Master".Translate().Resolve();
                case ColumnType.MentalState: return "MentalState".Translate().Resolve();
                case ColumnType.Bond: return "BondInfo".Translate().Resolve();
                case ColumnType.Sterile: return "Sterile".Translate().Resolve();
                case ColumnType.Slaughter: return "DesignatorSlaughter".Translate().Resolve();
                case ColumnType.MedicalCare: return "MedicalCare".Translate().Resolve();
                case ColumnType.ReleaseToWild: return "DesignatorReleaseAnimalToWild".Translate().Resolve();
                case ColumnType.AllowedArea: return "AllowedArea".Translate().Resolve();
                default: return type.ToString().Replace("_", " ");
            }
        }

        // Get column tooltip (shown only on column navigation, not row navigation)
        public static string GetColumnTooltip(int columnIndex)
        {
            // Fixed columns before training — no tooltips
            if (columnIndex < fixedColumnsBeforeTraining)
                return null;
            // Training columns — no tooltips (descriptions already in column value)
            if (columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count)
                return null;
            // Fixed columns after training
            var columnsAfterTraining = GetColumnsAfterTraining();
            int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
            if (fixedIndex < 0 || fixedIndex >= columnsAfterTraining.Count)
                return null;

            ColumnType type = columnsAfterTraining[fixedIndex];
            switch (type)
            {
                case ColumnType.FollowDrafted:
                    return DefDatabase<PawnColumnDef>.GetNamedSilentFail("FollowDrafted")?.headerTip;
                case ColumnType.FollowFieldwork:
                    return DefDatabase<PawnColumnDef>.GetNamedSilentFail("FollowFieldwork")?.headerTip;
                case ColumnType.Slaughter:
                    return "DesignatorSlaughterDesc".Translate().Resolve();
                case ColumnType.Sterile:
                    return "SterilizeAnimal".Translate().Resolve();
                case ColumnType.ReleaseToWild:
                    return "DesignatorReleaseAnimalToWildDesc".Translate().Resolve();
                default:
                    return null;
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
                        return GetAnimalNameWithActivity(pawn);
                    case ColumnType.Gender:
                        return GetGender(pawn);
                    case ColumnType.Age:
                        return GetAge(pawn);
                    case ColumnType.LifeStage:
                        return GetLifeStage(pawn);
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
                // Fixed columns after training - use dynamic list
                var columnsAfterTraining = GetColumnsAfterTraining();
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                if (fixedIndex < 0 || fixedIndex >= columnsAfterTraining.Count)
                    return "Unknown";

                ColumnType type = columnsAfterTraining[fixedIndex];
                return GetColumnValueForType(pawn, type);
            }
            return "Unknown";
        }

        // Helper to get column value for a ColumnType
        private static string GetColumnValueForType(Pawn pawn, ColumnType type)
        {
            switch (type)
            {
                case ColumnType.SpecialTrainable:
                    return GetSpecialTrainableStatus(pawn);
                case ColumnType.FollowDrafted:
                    return GetFollowDrafted(pawn);
                case ColumnType.FollowFieldwork:
                    return GetFollowFieldwork(pawn);
                case ColumnType.AnimalDig:
                    return GetAnimalDigStatus(pawn);
                case ColumnType.AnimalForage:
                    return GetAnimalForageStatus(pawn);
                case ColumnType.Master:
                    return GetMasterName(pawn);
                case ColumnType.MentalState:
                    return GetMentalState(pawn);
                case ColumnType.Bond:
                    return GetBondStatus(pawn);
                case ColumnType.Sterile:
                    return GetSterileStatus(pawn);
                case ColumnType.Slaughter:
                    return GetSlaughterStatus(pawn);
                case ColumnType.MedicalCare:
                    return GetMedicalCare(pawn);
                case ColumnType.ReleaseToWild:
                    return GetReleaseToWildStatus(pawn);
                case ColumnType.AllowedArea:
                    return GetAllowedArea(pawn);
                default:
                    return "Unknown";
            }
        }

        // Check if column is interactive (can be changed with Enter key)
        public static bool IsColumnInteractive(int columnIndex)
        {
            if (columnIndex < fixedColumnsBeforeTraining)
            {
                // Name column is interactive (jumps to animal on map)
                ColumnType type = (ColumnType)columnIndex;
                return type == ColumnType.Name;
            }
            else if (columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count)
            {
                return true; // All training columns are interactive
            }
            else
            {
                // Fixed columns after training - use dynamic list
                var columnsAfterTraining = GetColumnsAfterTraining();
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                if (fixedIndex < 0 || fixedIndex >= columnsAfterTraining.Count)
                    return false;

                ColumnType type = columnsAfterTraining[fixedIndex];
                // Interactive columns after training
                return type == ColumnType.SpecialTrainable ||
                       type == ColumnType.FollowDrafted ||
                       type == ColumnType.FollowFieldwork ||
                       type == ColumnType.AnimalDig ||
                       type == ColumnType.AnimalForage ||
                       type == ColumnType.Master ||
                       type == ColumnType.Sterile ||  // Checkbox to schedule sterilization (not interactive if already sterilized)
                       type == ColumnType.Slaughter ||
                       type == ColumnType.MedicalCare ||
                       type == ColumnType.ReleaseToWild ||
                       type == ColumnType.AllowedArea;
                // MentalState, Bond are display-only
            }
        }

        /// <summary>
        /// Gets the ColumnType for a column index after training columns.
        /// Returns null if the index is not in the after-training section.
        /// </summary>
        public static ColumnType? GetColumnTypeAfterTraining(int columnIndex)
        {
            var columnsAfterTraining = GetColumnsAfterTraining();
            int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
            if (fixedIndex < 0 || fixedIndex >= columnsAfterTraining.Count)
                return null;
            return columnsAfterTraining[fixedIndex];
        }

        // === Fixed Column Accessors ===

        /// <summary>
        /// Gets the basic animal name without activity (used for row labels).
        /// </summary>
        public static string GetAnimalName(Pawn pawn)
        {
            string name = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
            return $"{name} ({pawn.def.LabelCap})";
        }

        /// <summary>
        /// Gets the animal name with current activity (used for Name column value).
        /// </summary>
        public static string GetAnimalNameWithActivity(Pawn pawn)
        {
            string baseName = GetAnimalName(pawn);
            string activity = PawnHelper.GetPawnActivity(pawn);
            return activity != null ? $"{baseName} - {activity}" : baseName;
        }

        public static string GetGender(Pawn pawn)
        {
            // Use RimWorld's localized gender labels
            return pawn.gender.GetLabel(animal: true).CapitalizeFirst();
        }

        public static string GetAge(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "Unknown";
            // Use RimWorld's localized age string
            return pawn.ageTracker.AgeNumberString;
        }

        public static string GetLifeStage(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "Unknown";
            return pawn.ageTracker.CurLifeStage.label.CapitalizeFirst();
        }

        public static string GetPregnancyStatus(Pawn pawn)
        {
            if (pawn.gender != Gender.Female) return "N/A";
            if (pawn.health?.hediffSet == null) return "None".Translate().Resolve();

            Hediff_Pregnant pregnancy = (Hediff_Pregnant)pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            if (pregnancy != null)
            {
                // Use hediff's localized label and progress
                return $"{pregnancy.LabelCap} ({pregnancy.GestationProgress.ToStringPercent()})";
            }
            return "None".Translate().Resolve();
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
                // Add the reason why they can't train (already localized by RimWorld)
                if (!string.IsNullOrEmpty(canTrain.Reason))
                {
                    statusText += " - " + canTrain.Reason;
                }
            }
            else
            {
                bool wanted = pawn.training.GetWanted(trainable);
                bool hasLearned = pawn.training.HasLearned(trainable);

                // Get current training steps using reflection
                int steps = 0;
                var getStepsMethod = typeof(Pawn_TrainingTracker).GetMethod("GetSteps",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (getStepsMethod != null)
                {
                    steps = (int)getStepsMethod.Invoke(pawn.training, new object[] { trainable });
                }

                if (hasLearned)
                {
                    // Animal has completed training at some point
                    if (wanted)
                    {
                        statusText = $"Maintaining ({steps}/{trainable.steps})";
                    }
                    else
                    {
                        statusText = $"Not maintaining ({steps}/{trainable.steps})";
                    }
                }
                else
                {
                    // Animal has never completed training
                    if (wanted)
                    {
                        if (steps > 0)
                        {
                            statusText = $"Training ({steps}/{trainable.steps})";
                        }
                        else
                        {
                            statusText = "Waiting to train";
                        }
                    }
                    else
                    {
                        statusText = "Will not train";
                    }

                    // Add prerequisite information if not learned and has prerequisites
                    if (trainable.prerequisites != null && trainable.prerequisites.Count > 0)
                    {
                        foreach (var prereq in trainable.prerequisites)
                        {
                            if (!pawn.training.HasLearned(prereq))
                            {
                                statusText += " - " + "TrainingNeedsPrerequisite".Translate(prereq.LabelCap).Resolve();
                                break; // Only show first missing prerequisite to keep it concise
                            }
                        }
                    }
                }
            }

            // Add training description (already localized)
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

        // === Follow Settings (require Obedience/Guard training) ===

        public static string GetFollowDrafted(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            // Check if animal has learned Obedience (Guard)
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                return "Requires".Translate().Resolve() + " " + TrainableDefOf.Obedience.LabelCap;
            }

            return pawn.playerSettings.followDrafted ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        public static string GetFollowFieldwork(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            // Check if animal has learned Obedience (Guard)
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                return "Requires".Translate().Resolve() + " " + TrainableDefOf.Obedience.LabelCap;
            }

            return pawn.playerSettings.followFieldwork ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        // === Odyssey DLC: Special Trainables (race-specific abilities) ===

        /// <summary>
        /// Gets the list of special trainables for an animal (e.g., TerrorRoar for alpha thrumbo).
        /// </summary>
        public static List<TrainableDef> GetSpecialTrainables(Pawn pawn)
        {
            if (!IsOdysseyActive) return new List<TrainableDef>();
            if (pawn.RaceProps?.specialTrainables == null) return new List<TrainableDef>();
            return pawn.RaceProps.specialTrainables;
        }

        /// <summary>
        /// Gets the status of special trainables for an animal.
        /// Each animal has at most one special trainable (e.g., TerrorRoar, Comfort, Dig).
        /// </summary>
        public static string GetSpecialTrainableStatus(Pawn pawn)
        {
            if (!IsOdysseyActive) return "None available";

            var specialTrainables = GetSpecialTrainables(pawn);
            if (specialTrainables.Count == 0) return "None available";
            if (pawn.training == null) return "None available";

            // Animals have exactly one special trainable
            var trainable = specialTrainables[0];
            string abilityName = trainable.LabelCap;
            string status;

            bool wanted = pawn.training.GetWanted(trainable);
            bool hasLearned = pawn.training.HasLearned(trainable);

            // Get current training steps using reflection
            int steps = 0;
            var getStepsMethod = typeof(Pawn_TrainingTracker).GetMethod("GetSteps",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (getStepsMethod != null)
            {
                steps = (int)getStepsMethod.Invoke(pawn.training, new object[] { trainable });
            }

            if (hasLearned)
            {
                // Animal has completed training at some point
                if (wanted)
                {
                    status = $"Maintaining ({steps}/{trainable.steps})";
                }
                else
                {
                    status = $"Not maintaining ({steps}/{trainable.steps})";
                }
            }
            else
            {
                // Animal has never completed training
                if (wanted)
                {
                    if (steps > 0)
                    {
                        status = $"Training ({steps}/{trainable.steps})";
                    }
                    else
                    {
                        status = "Waiting to train";
                    }
                }
                else
                {
                    status = "Will not train";
                }
            }

            // Build result with ability name and status
            string result = $"{abilityName}: {status}";

            // Add description if available
            if (!string.IsNullOrEmpty(trainable.description))
            {
                result += " - " + trainable.description;
            }

            return result;
        }

        // === Odyssey DLC: Animal Dig/Forage (behavior toggles) ===

        public static string GetAnimalDigStatus(Pawn pawn)
        {
            if (!IsOdysseyActive) return "N/A";
            if (pawn.training?.HasLearned(TrainableDefOf.Dig) != true) return "N/A";

            return pawn.playerSettings.animalDig
                ? "Enabled".Translate().Resolve()
                : "Disabled".Translate().Resolve();
        }

        public static string GetAnimalForageStatus(Pawn pawn)
        {
            if (!IsOdysseyActive) return "N/A";
            if (pawn.training?.HasLearned(TrainableDefOf.Forage) != true) return "N/A";

            return pawn.playerSettings.animalForage
                ? "Enabled".Translate().Resolve()
                : "Disabled".Translate().Resolve();
        }

        // === Master (requires Obedience/Guard training) ===

        public static string GetMasterName(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            // Check if animal has learned Obedience (Guard)
            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                return "Requires".Translate().Resolve() + " " + TrainableDefOf.Obedience.LabelCap;
            }

            if (pawn.playerSettings.Master == null)
            {
                return "None".Translate().Resolve();
            }
            return pawn.playerSettings.Master.LabelShort;
        }

        // === Mental State ===

        public static string GetMentalState(Pawn pawn)
        {
            // Vanilla shows nothing (empty cell) when not in mental state,
            // but for screen readers we say "Normal" for clarity
            if (pawn.MentalState == null)
                return "Normal";
            return pawn.MentalState.def.LabelCap;
        }

        // === Bond Status ===

        public static string GetBondStatus(Pawn pawn)
        {
            if (pawn.relations == null) return "None".Translate().Resolve();

            Pawn bondedPawn = pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Bond);
            if (bondedPawn != null)
            {
                // Check if bond is "broken" (has master but master is not the bonded pawn)
                bool hasMaster = pawn.playerSettings?.Master != null;
                bool bondBroken = hasMaster && pawn.playerSettings.Master != bondedPawn;

                string bondText = "BondedTo".Translate().Resolve() + " " + bondedPawn.LabelShort;
                if (bondBroken)
                {
                    bondText += " (" + "BondBroken".Translate().Resolve() + ")";
                }
                return bondText;
            }
            return "None".Translate().Resolve();
        }

        // === Sterile Status ===

        /// <summary>
        /// Checks if the animal is already sterilized (has the Sterilized hediff).
        /// </summary>
        public static bool IsAnimalSterilized(Pawn pawn)
        {
            return pawn.health?.hediffSet?.HasHediff(HediffDefOf.Sterilized) == true;
        }

        /// <summary>
        /// Checks if a sterilization operation is currently scheduled for this animal.
        /// </summary>
        public static bool HasSterilizationScheduled(Pawn pawn)
        {
            if (pawn.BillStack == null) return false;
            return pawn.BillStack.Bills.Any(b => b.recipe == RecipeDefOf.Sterilize);
        }

        public static string GetSterileStatus(Pawn pawn)
        {
            // Already sterilized
            if (IsAnimalSterilized(pawn))
            {
                return "Yes".Translate().Resolve();
            }

            // Sterilization scheduled (interactive - can cancel)
            if (HasSterilizationScheduled(pawn))
            {
                return "Scheduled".Translate().Resolve();
            }

            // Not scheduled (interactive - can schedule)
            return "No".Translate().Resolve();
        }

        /// <summary>
        /// Checks if the Sterile column is interactive for this animal.
        /// Not interactive if already sterilized.
        /// </summary>
        public static bool IsSterileInteractive(Pawn pawn)
        {
            return !IsAnimalSterilized(pawn);
        }

        // === Slaughter ===

        public static string GetSlaughterStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter);
            string markedLabel = DesignationDefOf.Slaughter.label.CapitalizeFirst();
            string notMarkedLabel = "None".Translate().Resolve();
            return designation != null ? markedLabel : notMarkedLabel;
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

        // === Release to Wild ===

        public static string GetReleaseToWildStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "N/A";

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild);
            string markedLabel = DesignationDefOf.ReleaseAnimalToWild.label.CapitalizeFirst();
            string notMarkedLabel = "None".Translate().Resolve();
            return designation != null ? markedLabel : notMarkedLabel;
        }

        // === Area Restriction ===

        public static string GetAllowedArea(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "N/A";

            Area area = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
            if (area == null)
            {
                return "Unrestricted".Translate().Resolve();
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

        // === Master Assignment ===

        public static List<Pawn> GetAvailableColonists()
        {
            if (Find.CurrentMap == null) return new List<Pawn>();

            return Find.CurrentMap.mapPawns.FreeColonistsSpawned
                .Where(p => !p.Dead && !p.Downed)
                .OrderBy(p => p.LabelShort)
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
                    case ColumnType.Gender:
                        sorted = animals.OrderBy(p => p.gender);
                        break;
                    case ColumnType.Age:
                        sorted = animals.OrderBy(p => p.ageTracker.AgeBiologicalYearsFloat);
                        break;
                    case ColumnType.LifeStage:
                        sorted = animals.OrderBy(p => p.ageTracker.CurLifeStageIndex);
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
                // Fixed columns after training - use dynamic list
                var columnsAfterTraining = GetColumnsAfterTraining();
                int fixedIndex = columnIndex - fixedColumnsBeforeTraining - GetAllTrainables().Count;
                if (fixedIndex < 0 || fixedIndex >= columnsAfterTraining.Count)
                {
                    sorted = animals;
                }
                else
                {
                    ColumnType type = columnsAfterTraining[fixedIndex];
                    // Sort by column value
                    sorted = animals.OrderBy(p => GetColumnValueForType(p, type));
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
