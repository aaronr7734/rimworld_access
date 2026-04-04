using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

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

        // === Column Defs (for sorting and painting via game logic) ===
        private static List<PawnColumnDef> columnDefs;

        // Mapping from ColumnType to PawnColumnDef defName
        private static readonly Dictionary<ColumnType, string> columnTypeToDefName = new Dictionary<ColumnType, string>
        {
            { ColumnType.Name, "LabelWithIcon" },
            { ColumnType.Gender, "Gender" },
            { ColumnType.Age, "Age" },
            { ColumnType.LifeStage, "LifeStage" },
            { ColumnType.Pregnant, "Pregnant" },
            { ColumnType.SpecialTrainable, "SpecialTrainable" },
            { ColumnType.FollowDrafted, "FollowDrafted" },
            { ColumnType.FollowFieldwork, "FollowFieldwork" },
            { ColumnType.AnimalDig, "AnimalDig" },
            { ColumnType.AnimalForage, "AnimalForage" },
            { ColumnType.Master, "Master" },
            { ColumnType.MentalState, "MentalState" },
            { ColumnType.Bond, "Bond" },
            { ColumnType.Sterile, "Sterile" },
            { ColumnType.Slaughter, "Slaughter" },
            { ColumnType.MedicalCare, "MedicalCare" },
            { ColumnType.ReleaseToWild, "ReleaseAnimalToWild" },
            { ColumnType.AllowedArea, "AllowedAreaWide" },
        };

        public static void InitColumnDefs()
        {
            columnDefs = new List<PawnColumnDef>();

            // Fixed columns before training
            ColumnType[] fixedBefore = { ColumnType.Name, ColumnType.Gender, ColumnType.Age, ColumnType.LifeStage, ColumnType.Pregnant };
            foreach (var ct in fixedBefore)
            {
                columnDefs.Add(DefDatabase<PawnColumnDef>.GetNamedSilentFail(columnTypeToDefName[ct]));
            }

            // Dynamic training columns
            foreach (var trainable in GetAllTrainables())
            {
                columnDefs.Add(DefDatabase<PawnColumnDef>.GetNamedSilentFail("Trainable_" + trainable.defName));
            }

            // Fixed columns after training
            foreach (var ct in GetColumnsAfterTraining())
            {
                if (columnTypeToDefName.TryGetValue(ct, out string defName))
                {
                    columnDefs.Add(DefDatabase<PawnColumnDef>.GetNamedSilentFail(defName));
                }
                else
                {
                    columnDefs.Add(null);
                }
            }
        }

        public static bool IsColumnSortable(int columnIndex)
        {
            if (columnDefs == null || columnIndex < 0 || columnIndex >= columnDefs.Count)
                return false;
            return columnDefs[columnIndex]?.sortable ?? false;
        }

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
        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
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
            return designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
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
            return designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
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

        // === Painting Support ===

        /// <summary>
        /// Checks if a column supports painting (drag-to-apply).
        /// Uses the unified columnDefs list for PawnColumnDef.paintable lookup.
        /// AllowedArea and Master/MedicalCare are special cases with mod-specific painting.
        /// </summary>
        public static bool CanPaintColumn(int columnIndex)
        {
            // Fixed columns before training are never paintable
            if (columnIndex < fixedColumnsBeforeTraining)
                return false;

            // Training columns are paintable (PawnColumnWorker_Trainable passes paintable: true)
            if (IsTrainingColumn(columnIndex))
                return true;

            var columnType = GetColumnTypeAfterTraining(columnIndex);
            if (columnType == null)
                return false;

            // AllowedArea is paintable via mod's lastAppliedArea mechanism
            if (columnType == ColumnType.AllowedArea)
                return true;

            // Master and MedicalCare are paintable (workers pass paintable: true to Widgets.Dropdown)
            if (columnType == ColumnType.Master || columnType == ColumnType.MedicalCare)
                return true;

            // Look up PawnColumnDef.paintable at runtime via unified column defs
            if (columnDefs != null && columnIndex >= 0 && columnIndex < columnDefs.Count)
            {
                return columnDefs[columnIndex]?.paintable == true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the column index is in the dynamic training column range.
        /// </summary>
        public static bool IsTrainingColumn(int columnIndex)
        {
            return columnIndex >= fixedColumnsBeforeTraining
                && columnIndex < fixedColumnsBeforeTraining + GetAllTrainables().Count;
        }

        /// <summary>
        /// Gets the current boolean value of a paintable column for a pawn.
        /// Used as the "brush" value when painting.
        /// </summary>
        public static bool GetPaintableValue(Pawn pawn, int columnIndex)
        {
            // Training columns
            if (IsTrainingColumn(columnIndex))
            {
                if (pawn.training == null) return false;
                var trainable = GetTrainableAtColumn(columnIndex);
                return trainable != null && pawn.training.GetWanted(trainable);
            }

            var columnType = GetColumnTypeAfterTraining(columnIndex);
            if (columnType == null) return false;

            switch (columnType.Value)
            {
                case ColumnType.FollowDrafted:
                    return pawn.playerSettings?.followDrafted == true;
                case ColumnType.FollowFieldwork:
                    return pawn.playerSettings?.followFieldwork == true;
                case ColumnType.Slaughter:
                    return pawn.Map?.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter) != null;
                case ColumnType.Sterile:
                    return HasSterilizationScheduled(pawn);
                case ColumnType.ReleaseToWild:
                    return pawn.Map?.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild) != null;
                case ColumnType.SpecialTrainable:
                    var specials = GetSpecialTrainables(pawn);
                    return specials.Count > 0 && pawn.training != null && specials.Any(t => pawn.training.GetWanted(t));
                case ColumnType.AnimalDig:
                    return pawn.playerSettings?.animalDig == true;
                case ColumnType.AnimalForage:
                    return pawn.playerSettings?.animalForage == true;
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
            // Training columns
            if (IsTrainingColumn(columnIndex))
            {
                if (pawn.training == null) return false;
                var trainable = GetTrainableAtColumn(columnIndex);
                if (trainable == null) return false;
                bool visible;
                AcceptanceReport canTrain = pawn.training.CanAssignToTrain(trainable, out visible);
                if (!visible || !canTrain.Accepted) return false;
                if (pawn.training.HasLearned(trainable) && !value) return false; // can't un-train learned
                pawn.training.SetWantedRecursive(trainable, value);
                return true;
            }

            var columnType = GetColumnTypeAfterTraining(columnIndex);
            if (columnType == null) return false;

            switch (columnType.Value)
            {
                case ColumnType.FollowDrafted:
                    if (pawn.playerSettings == null || pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
                        return false;
                    pawn.playerSettings.followDrafted = value;
                    return true;

                case ColumnType.FollowFieldwork:
                    if (pawn.playerSettings == null || pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
                        return false;
                    pawn.playerSettings.followFieldwork = value;
                    return true;

                case ColumnType.Slaughter:
                    if (pawn.Map == null) return false;
                    var slaughterDes = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter);
                    if (value && slaughterDes == null)
                    {
                        pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.Slaughter));
                        return true;
                    }
                    if (!value && slaughterDes != null)
                    {
                        pawn.Map.designationManager.RemoveDesignation(slaughterDes);
                        return true;
                    }
                    return false;

                case ColumnType.Sterile:
                    if (IsAnimalSterilized(pawn)) return false;
                    bool scheduled = HasSterilizationScheduled(pawn);
                    if (value && !scheduled)
                    {
                        HealthCardUtility.CreateSurgeryBill(pawn, RecipeDefOf.Sterilize, null);
                        return true;
                    }
                    if (!value && scheduled)
                    {
                        var bills = pawn.BillStack.Bills.Where(b => b.recipe == RecipeDefOf.Sterilize).ToList();
                        foreach (var bill in bills)
                            pawn.BillStack.Delete(bill);
                        return true;
                    }
                    return false;

                case ColumnType.ReleaseToWild:
                    if (pawn.Map == null) return false;
                    var releaseDes = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild);
                    if (value && releaseDes == null)
                    {
                        pawn.Map.designationManager.AddDesignation(new Designation(pawn, DesignationDefOf.ReleaseAnimalToWild));
                        return true;
                    }
                    if (!value && releaseDes != null)
                    {
                        pawn.Map.designationManager.RemoveDesignation(releaseDes);
                        return true;
                    }
                    return false;

                case ColumnType.SpecialTrainable:
                    var specials = GetSpecialTrainables(pawn);
                    if (specials.Count == 0 || pawn.training == null) return false;
                    foreach (var trainable in specials)
                        pawn.training.SetWantedRecursive(trainable, value);
                    return true;

                case ColumnType.AnimalDig:
                    if (pawn.playerSettings == null || pawn.training?.HasLearned(TrainableDefOf.Dig) != true)
                        return false;
                    pawn.playerSettings.animalDig = value;
                    return true;

                case ColumnType.AnimalForage:
                    if (pawn.playerSettings == null || pawn.training?.HasLearned(TrainableDefOf.Forage) != true)
                        return false;
                    pawn.playerSettings.animalForage = value;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets the appropriate sound for painting a column.
        /// </summary>
        public static SoundDef GetPaintSound(int columnIndex, bool value)
        {
            var columnType = GetColumnTypeAfterTraining(columnIndex);
            if (columnType == ColumnType.AllowedArea)
                return SoundDefOf.Designate_DragStandard_Changed_NoCam;
            if (columnType == ColumnType.Master)
                return SoundDefOf.Click;
            if (columnType == ColumnType.MedicalCare)
                return SoundDefOf.Tick_High;
            // Training columns and all other checkbox columns use Checkbox sounds
            return value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff;
        }

        /// <summary>
        /// Gets the display label for a paint value (e.g., "checked", "unchecked").
        /// </summary>
        public static string GetPaintValueLabel(int columnIndex, bool value)
        {
            return value ? "checked" : "unchecked";
        }

        // === Sorting ===

        public static List<Pawn> SortAnimalsByColumn(List<Pawn> animals, int columnIndex, bool descending)
        {
            if (columnDefs == null || columnIndex < 0 || columnIndex >= columnDefs.Count)
                return animals;

            var columnDef = columnDefs[columnIndex];
            if (columnDef == null)
                return animals;

            var sorted = new List<Pawn>(animals);
            if (descending)
            {
                sorted.SortStable(columnDef.Worker.Compare);
            }
            else
            {
                sorted.SortStable((Pawn a, Pawn b) => columnDef.Worker.Compare(b, a));
            }
            return sorted;
        }
    }
}
