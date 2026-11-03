using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    public static class AnimalsMenuHelper
    {
        /// <summary>
        /// What a column DOES, derived once per <see cref="InitColumnDefs"/> from its worker's own
        /// runtime TYPE rather than its position, so a modded subclass of a known worker inherits
        /// the right behavior. Kept local rather than routed through
        /// <see cref="PawnColumnHandlerRegistry"/> because Master/AllowedArea/MedicalCare open a
        /// bespoke submenu region reusing the table's own row cursor, not the registry's
        /// FloatMenu-opening <c>ActivateCell</c>.
        /// </summary>
        public enum ColumnType
        {
            Name,
            Gender,
            Age,
            LifeStage,
            Pregnant,
            Trainable,
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
            AllowedArea,
            /// <summary>A modded/DLC column with no bespoke reader here: read generically through <see cref="PawnColumnHandlerRegistry"/> when its worker derives from a known vanilla base, honestly unavailable otherwise.</summary>
            Unknown
        }

        // Column defs are the single source of truth for the column set, order and names; the
        // per-index classification comes from each def's own worker type.
        private static List<PawnColumnDef> columnDefs = new List<PawnColumnDef>();
        private static List<ColumnType> columnKinds = new List<ColumnType>();

        /// <summary>
        /// The resolved column list, straight from vanilla's <see cref="PawnTableDefOf.Animals"/>
        /// def: its order, its DLC and mod gating (an inactive DLC's columns are simply absent from
        /// <c>PawnTableDef.columns</c> via the XML's own MayRequire), and any column a mod adds.
        /// Spacer columns are skipped through the same <see cref="PawnColumnHandlerRegistry"/> test
        /// the generic pawn-table tier applies.
        /// </summary>
        public static void InitColumnDefs()
        {
            columnDefs = new List<PawnColumnDef>();
            columnKinds = new List<ColumnType>();

            List<PawnColumnDef> defs = PawnTableDefOf.Animals?.columns;
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
                    columnDefs.Add(def);
                    columnKinds.Add(ClassifyColumn(def));
                }
                catch (Exception ex)
                {
                    // A broken (typically modded) worker must not drop the whole table.
                    Log.Warning("[RimWorld Access] Animals column '" + def.defName + "' failed to resolve and was skipped: " + ex);
                }
            }
        }

        /// <summary>Classifies a column by its worker's own runtime type (is-checks, so a modded subclass inherits the base behavior for free).</summary>
        private static ColumnType ClassifyColumn(PawnColumnDef def)
        {
            PawnColumnWorker worker = def.Worker;
            if (worker is PawnColumnWorker_Label) return ColumnType.Name;
            if (worker is PawnColumnWorker_Gender) return ColumnType.Gender;
            if (worker is PawnColumnWorker_Age) return ColumnType.Age;
            if (worker is PawnColumnWorker_LifeStage) return ColumnType.LifeStage;
            if (worker is PawnColumnWorker_Pregnant) return ColumnType.Pregnant;
            // Trainable_Special does not derive from Trainable, so this order is not load-bearing.
            if (worker is PawnColumnWorker_Trainable_Special) return ColumnType.SpecialTrainable;
            if (worker is PawnColumnWorker_Trainable) return ColumnType.Trainable;
            if (worker is PawnColumnWorker_FollowDrafted) return ColumnType.FollowDrafted;
            if (worker is PawnColumnWorker_FollowFieldwork) return ColumnType.FollowFieldwork;
            if (worker is PawnColumnWorker_AnimalDig) return ColumnType.AnimalDig;
            if (worker is PawnColumnWorker_AnimalForage) return ColumnType.AnimalForage;
            if (worker is PawnColumnWorker_Master) return ColumnType.Master;
            if (worker is PawnColumnWorker_MentalState) return ColumnType.MentalState;
            if (worker is PawnColumnWorker_Bond) return ColumnType.Bond;
            if (worker is PawnColumnWorker_Sterilize) return ColumnType.Sterile;
            if (worker is PawnColumnWorker_Slaughter) return ColumnType.Slaughter;
            if (worker is PawnColumnWorker_MedicalCare) return ColumnType.MedicalCare;
            if (worker is PawnColumnWorker_ReleaseAnimalToWild) return ColumnType.ReleaseToWild;
            if (worker is PawnColumnWorker_AllowedArea) return ColumnType.AllowedArea; // covers AllowedAreaWide too.
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

        /// <summary>
        /// A column's name. The def's own <c>LabelCap</c> wins when present; a Trainable_ column's is
        /// empty by design (vanilla draws only its icon), so it falls through to its headerTip,
        /// which <see cref="PawnColumnDefGenerator"/> sets to the TrainableDef's LabelCap.
        /// Follow and SpecialTrainable keep a short curated key rather than their paragraph-length
        /// headerTip, which stays available through <see cref="GetColumnTooltip"/>.
        /// Gender/Pregnant/MentalState carry neither label nor headerTip in vanilla's XML, so they
        /// fall to a per-classification key before the bare defName.
        /// </summary>
        public static string GetColumnName(int columnIndex)
        {
            PawnColumnDef def = GetDef(columnIndex);
            if (def == null)
                return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();

            ColumnType kind = GetColumnType(columnIndex);
            switch (kind)
            {
                case ColumnType.SpecialTrainable: return "RimWorldAccess.Animals.Column.SpecialTraining".Translate().Resolve();
                case ColumnType.FollowDrafted: return "CreatureFollowDrafted".Translate().Resolve();
                case ColumnType.FollowFieldwork: return "CreatureFollowFieldwork".Translate().Resolve();
            }

            if (!def.label.NullOrEmpty())
                return def.LabelCap.ToString();
            if (!def.headerTip.NullOrEmpty())
                return def.headerTip;

            switch (kind)
            {
                case ColumnType.Gender: return "Sex".Translate().Resolve();
                case ColumnType.Pregnant: return HediffDefOf.Pregnant.LabelCap.Resolve();
                case ColumnType.MentalState: return "RimWorldAccess.Animals.Column.MentalState".Translate().Resolve();
                default: return PawnColumnHandlerRegistry.Resolve(def).HeaderLabel(def) ?? def.defName;
            }
        }

        /// <summary>A column's tooltip, shown on column navigation only; an allowlist keyed by classification.</summary>
        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
        {
            if (GetDef(columnIndex) == null)
                return null;

            switch (GetColumnType(columnIndex))
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
                case ColumnType.Age:
                    return GetAge(pawn);
                case ColumnType.LifeStage:
                    return GetLifeStage(pawn);
                case ColumnType.Pregnant:
                    return GetPregnancyStatus(pawn);
                case ColumnType.Trainable:
                {
                    TrainableDef trainable = GetTrainableAtColumn(columnIndex);
                    return trainable != null
                        ? GetTrainingStatus(pawn, trainable)
                        : "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
                }
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

        // Whether Enter can change the column.
        public static bool IsColumnInteractive(int columnIndex)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Name:
                case ColumnType.Trainable:
                case ColumnType.SpecialTrainable:
                case ColumnType.FollowDrafted:
                case ColumnType.FollowFieldwork:
                case ColumnType.AnimalDig:
                case ColumnType.AnimalForage:
                case ColumnType.Master:
                case ColumnType.Sterile:
                case ColumnType.Slaughter:
                case ColumnType.MedicalCare:
                case ColumnType.ReleaseToWild:
                case ColumnType.AllowedArea:
                    return true;
                default:
                    // These are display-only; Unknown has no bespoke reader here.
                    return false;
            }
        }

        // === Fixed Column Accessors ===

        /// <summary>The bare animal name, without activity, for row labels.</summary>
        public static string GetNameWithKind(Pawn pawn)
        {
            return pawn.Name != null
                ? "RimWorldAccess.Animals.NameWithKind".Translate(pawn.Name.ToStringShort, pawn.def.LabelCap).ToString()
                : pawn.def.LabelCap.ToString();
        }

        /// <summary>The animal name with its current activity, for the Name column value.</summary>
        public static string GetAnimalNameWithActivity(Pawn pawn)
        {
            string baseName = GetNameWithKind(pawn);
            string activity = PawnHelper.GetPawnActivity(pawn);
            return activity != null ? $"{baseName} - {activity}" : baseName;
        }

        public static string GetGender(Pawn pawn)
        {
            return pawn.gender.GetLabel(animal: true).CapitalizeFirst();
        }

        public static string GetAge(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
            return pawn.ageTracker.AgeNumberString;
        }

        public static string GetLifeStage(Pawn pawn)
        {
            if (pawn.ageTracker == null) return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
            return pawn.ageTracker.CurLifeStage.label.CapitalizeFirst();
        }

        public static string GetPregnancyStatus(Pawn pawn)
        {
            if (pawn.gender != Gender.Female) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();
            if (pawn.health?.hediffSet == null) return "None".Translate().Resolve();

            Hediff_Pregnant pregnancy = (Hediff_Pregnant)pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            if (pregnancy != null)
            {
                return $"{pregnancy.LabelCap} ({pregnancy.GestationProgress.ToStringPercent()})";
            }
            return "None".Translate().Resolve();
        }

        // === Training Column Accessors ===

        public static string GetTrainingStatus(Pawn pawn, TrainableDef trainable)
        {
            if (pawn.training == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(trainable);

            string statusText = "";

            if (!canTrain.Accepted)
            {
                statusText = "RimWorldAccess.Animals.Training.CannotTrain".Translate().ToString();
                // The reason is already localized by RimWorld.
                if (!string.IsNullOrEmpty(canTrain.Reason))
                {
                    statusText += " - " + canTrain.Reason;
                }
            }
            else
            {
                bool wanted = pawn.training.GetWanted(trainable);
                bool hasLearned = pawn.training.HasLearned(trainable);

                int steps = 0;
                var getStepsMethod = typeof(Pawn_TrainingTracker).GetMethod("GetSteps",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (getStepsMethod != null)
                {
                    steps = (int)getStepsMethod.Invoke(pawn.training, new object[] { trainable });
                }

                if (hasLearned)
                {
                    if (wanted)
                    {
                        statusText = "RimWorldAccess.Animals.Training.Maintaining".Translate(steps, trainable.steps).ToString();
                    }
                    else
                    {
                        statusText = "RimWorldAccess.Animals.Training.NotMaintaining".Translate(steps, trainable.steps).ToString();
                    }
                }
                else
                {
                    if (wanted)
                    {
                        if (steps > 0)
                        {
                            statusText = "RimWorldAccess.Animals.Training.InProgress".Translate(steps, trainable.steps).ToString();
                        }
                        else
                        {
                            statusText = "RimWorldAccess.Animals.Training.WaitingToTrain".Translate().ToString();
                        }
                    }
                    else
                    {
                        statusText = "RimWorldAccess.Animals.Training.WillNotTrain".Translate().ToString();
                    }

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

            if (!string.IsNullOrEmpty(trainable.description))
            {
                statusText += " - " + trainable.description;
            }

            return statusText;
        }

        /// <summary>The TrainableDef backing a Trainable column at this index, read straight off the column's own def (set by <see cref="PawnColumnDefGenerator"/>) — null for every other classification.</summary>
        public static TrainableDef GetTrainableAtColumn(int columnIndex)
        {
            return GetDef(columnIndex)?.trainable;
        }

        // === Follow Settings (require Obedience/Guard training) ===

        public static string GetFollowDrafted(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                return "Requires".Translate().Resolve() + " " + TrainableDefOf.Obedience.LabelCap;
            }

            return pawn.playerSettings.followDrafted ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        public static string GetFollowFieldwork(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            if (pawn.training?.HasLearned(TrainableDefOf.Obedience) != true)
            {
                return "Requires".Translate().Resolve() + " " + TrainableDefOf.Obedience.LabelCap;
            }

            return pawn.playerSettings.followFieldwork ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        // === Odyssey DLC: Special Trainables ===
        // No runtime IsOdysseyActive guard is needed: the SpecialTrainable column only exists in
        // columnDefs when Odyssey is active, so these accessors are unreachable without it.

        /// <summary>An animal's special trainables, such as TerrorRoar for an alpha thrumbo.</summary>
        public static List<TrainableDef> GetSpecialTrainables(Pawn pawn)
        {
            if (pawn.RaceProps?.specialTrainables == null) return new List<TrainableDef>();
            return pawn.RaceProps.specialTrainables;
        }

        /// <summary>The status of an animal's special trainable; each animal has at most one.</summary>
        public static string GetSpecialTrainableStatus(Pawn pawn)
        {
            var specialTrainables = GetSpecialTrainables(pawn);
            if (specialTrainables.Count == 0) return "RimWorldAccess.Animals.Training.NoSpecialAbility".Translate().ToString();
            if (pawn.training == null) return "RimWorldAccess.Animals.Training.NoSpecialAbility".Translate().ToString();

            var trainable = specialTrainables[0];
            string abilityName = trainable.LabelCap;
            string status;

            bool wanted = pawn.training.GetWanted(trainable);
            bool hasLearned = pawn.training.HasLearned(trainable);

            int steps = 0;
            var getStepsMethod = typeof(Pawn_TrainingTracker).GetMethod("GetSteps",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (getStepsMethod != null)
            {
                steps = (int)getStepsMethod.Invoke(pawn.training, new object[] { trainable });
            }

            if (hasLearned)
            {
                if (wanted)
                {
                    status = "RimWorldAccess.Animals.Training.Maintaining".Translate(steps, trainable.steps).ToString();
                }
                else
                {
                    status = "RimWorldAccess.Animals.Training.NotMaintaining".Translate(steps, trainable.steps).ToString();
                }
            }
            else
            {
                if (wanted)
                {
                    if (steps > 0)
                    {
                        status = "RimWorldAccess.Animals.Training.InProgress".Translate(steps, trainable.steps).ToString();
                    }
                    else
                    {
                        status = "RimWorldAccess.Animals.Training.WaitingToTrain".Translate().ToString();
                    }
                }
                else
                {
                    status = "RimWorldAccess.Animals.Training.WillNotTrain".Translate().ToString();
                }
            }

            string result = $"{abilityName}: {status}";

            if (!string.IsNullOrEmpty(trainable.description))
            {
                result += " - " + trainable.description;
            }

            return result;
        }

        // === Odyssey DLC: Animal Dig/Forage (behavior toggles) ===

        public static string GetAnimalDigStatus(Pawn pawn)
        {
            if (pawn.training?.HasLearned(TrainableDefOf.Dig) != true) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            return pawn.playerSettings.animalDig
                ? "Enabled".Translate().Resolve()
                : "Disabled".Translate().Resolve();
        }

        public static string GetAnimalForageStatus(Pawn pawn)
        {
            if (pawn.training?.HasLearned(TrainableDefOf.Forage) != true) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            return pawn.playerSettings.animalForage
                ? "Enabled".Translate().Resolve()
                : "Disabled".Translate().Resolve();
        }

        // === Master (requires Obedience/Guard training) ===

        public static string GetMasterName(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

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
            // Vanilla leaves the cell empty outside a mental state; say "Normal" instead.
            if (pawn.MentalState == null)
                return "RimWorldAccess.Animals.Value.Normal".Translate().ToString();
            return pawn.MentalState.def.LabelCap;
        }

        // === Bond Status ===

        public static string GetBondStatus(Pawn pawn)
        {
            if (pawn.relations == null) return "None".Translate().Resolve();

            Pawn bondedPawn = pawn.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Bond);
            if (bondedPawn != null)
            {
                // A bond is broken when the animal has a master other than its bonded pawn.
                bool hasMaster = pawn.playerSettings?.Master != null;
                bool bondBroken = hasMaster && pawn.playerSettings.Master != bondedPawn;

                string bondText = "BondedTo".Translate().Resolve() + " " + bondedPawn.LabelShort;
                if (bondBroken)
                {
                    bondText += " (" + "RimWorldAccess.Animals.Value.BondBroken".Translate().Resolve() + ")";
                }
                return bondText;
            }
            return "None".Translate().Resolve();
        }

        // === Sterile Status ===

        /// <summary>Whether the animal already carries the Sterilized hediff.</summary>
        public static bool IsAnimalSterilized(Pawn pawn)
        {
            return pawn.health?.hediffSet?.HasHediff(HediffDefOf.Sterilized) == true;
        }

        /// <summary>Whether a sterilization operation is scheduled for this animal.</summary>
        public static bool HasSterilizationScheduled(Pawn pawn)
        {
            if (pawn.BillStack == null) return false;
            return pawn.BillStack.Bills.Any(b => b.recipe == RecipeDefOf.Sterilize);
        }

        public static string GetSterileStatus(Pawn pawn)
        {
            if (IsAnimalSterilized(pawn))
            {
                return "Yes".Translate().Resolve();
            }

            if (HasSterilizationScheduled(pawn))
            {
                return "RimWorldAccess.Animals.Value.Scheduled".Translate().Resolve();
            }

            return "No".Translate().Resolve();
        }

        /// <summary>Whether the Sterile column is interactive; an already-sterilized animal is not.</summary>
        public static bool IsSterileInteractive(Pawn pawn)
        {
            return !IsAnimalSterilized(pawn);
        }

        // === Slaughter ===

        public static string GetSlaughterStatus(Pawn pawn)
        {
            if (pawn.Map == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter);
            return designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        // === Medical Care ===

        public static string GetMedicalCare(Pawn pawn)
        {
            if (pawn.playerSettings == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

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
            if (pawn.Map == null) return "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();

            Designation designation = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild);
            return designation != null ? "Yes".Translate().Resolve() : "No".Translate().Resolve();
        }

        // === Area Restriction ===

        public static string GetAllowedArea(Pawn pawn)
        {
            if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn))
            {
                return PawnColumnMutationHelper.AllowedAreaNotice(pawn)
                    ?? "RimWorldAccess.Animals.Value.NotApplicable".Translate().ToString();
            }

            Area area = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
            if (area == null)
            {
                return "NoAreaAllowed".Translate().Resolve();
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
        // The candidate list and CanBeMaster gate come from
        // PawnColumnMutationHelper.GetMasterMenuElements, which reflects vanilla's own
        // TrainableUtility.MasterSelectButton_GenerateMenu.

        // === Painting Support ===

        /// <summary>
        /// Whether a column supports drag-to-apply painting. AllowedArea/Master/MedicalCare are
        /// always paintable regardless of their def's <c>paintable</c> flag, since their paint
        /// semantics live in AnimalsScope rather than vanilla's drag-paint code; every other
        /// classification reads the def's own flag.
        /// </summary>
        public static bool CanPaintColumn(int columnIndex)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Trainable:
                case ColumnType.AllowedArea:
                case ColumnType.Master:
                case ColumnType.MedicalCare:
                    return true;
                case ColumnType.Name:
                case ColumnType.Gender:
                case ColumnType.Age:
                case ColumnType.LifeStage:
                case ColumnType.Pregnant:
                    return false;
                default:
                    return GetDef(columnIndex)?.paintable == true;
            }
        }

        /// <summary>A paintable column's current boolean value, used as the brush value.</summary>
        public static bool GetPaintableValue(Pawn pawn, int columnIndex)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Trainable:
                {
                    if (pawn.training == null) return false;
                    var trainable = GetTrainableAtColumn(columnIndex);
                    return trainable != null && pawn.training.GetWanted(trainable);
                }
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

        /// <summary>Sets a paintable column to a value rather than toggling; false if the animal cannot accept it or already holds it.</summary>
        public static bool SetPaintableValue(Pawn pawn, int columnIndex, bool value)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Trainable:
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
                {
                    if (pawn.Map == null) return false;
                    bool current = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.Slaughter) != null;
                    if (current == value) return false;
                    PawnColumnDef slaughterDef = GetColumnDef(ColumnType.Slaughter);
                    // Paint applies to a whole run of animals in one gesture, and stacking one
                    // blocking confirmation dialog per painted animal is unusable, so skip instead.
                    // The Enter-key toggle below still raises vanilla's own per-pawn dialog.
                    if (value && PawnColumnMutationHelper.DesignatorWouldRequireConfirm(slaughterDef, pawn))
                        return false;
                    PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Animals);
                    PawnColumnMutationHelper.SetDesignatorValue(slaughterDef, pawn, value, table, afterward: null);
                    return true;
                }

                case ColumnType.Sterile:
                {
                    if (IsAnimalSterilized(pawn)) return false;
                    bool scheduled = HasSterilizationScheduled(pawn);
                    if (value == scheduled) return false;
                    // Same paint-vs-blocking-dialog reasoning as Slaughter above.
                    if (value && PawnColumnMutationHelper.SterilizeWouldRequireConfirm(pawn))
                        return false;
                    PawnColumnMutationHelper.SetSterilizeValue(pawn, value, afterward: null);
                    return true;
                }

                case ColumnType.ReleaseToWild:
                {
                    if (pawn.Map == null) return false;
                    bool current = pawn.Map.designationManager.DesignationOn(pawn, DesignationDefOf.ReleaseAnimalToWild) != null;
                    if (current == value) return false;
                    PawnColumnDef releaseDef = GetColumnDef(ColumnType.ReleaseToWild);
                    // Same paint-vs-blocking-dialog reasoning as Slaughter above.
                    if (value && PawnColumnMutationHelper.DesignatorWouldRequireConfirm(releaseDef, pawn))
                        return false;
                    PawnTable table = PawnColumnMutationHelper.CreateDetachedTable(PawnTableDefOf.Animals);
                    PawnColumnMutationHelper.SetDesignatorValue(releaseDef, pawn, value, table, afterward: null);
                    return true;
                }

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

        /// <summary>The sound for painting a column.</summary>
        public static SoundDef GetPaintSound(int columnIndex, bool value)
        {
            ColumnType kind = GetColumnType(columnIndex);
            if (kind == ColumnType.AllowedArea)
                return SoundDefOf.Designate_DragStandard_Changed_NoCam;
            if (kind == ColumnType.Master)
                return SoundDefOf.Click;
            if (kind == ColumnType.MedicalCare)
                return SoundDefOf.Tick_High;
            // Training and every other checkbox column use Checkbox sounds.
            return value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff;
        }

        /// <summary>The display label for a paint value, such as "checked".</summary>
        public static string GetPaintValueLabel(int columnIndex, bool value)
        {
            return value
                ? "RimWorldAccess.Animals.Paint.Checked".Translate().ToString()
                : "RimWorldAccess.Animals.Paint.Unchecked".Translate().ToString();
        }

        // === Sorting ===

        public static List<Pawn> SortAnimalsByColumn(List<Pawn> animals, int columnIndex, bool descending)
            => PawnColumnSortHelper.SortByColumnDef(animals, columnDefs, columnIndex, descending);
    }
}
