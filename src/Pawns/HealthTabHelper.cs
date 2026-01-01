using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for health tab data extraction and interactions.
    /// Provides methods for medical settings, capacities, operations, and hediff information.
    /// </summary>
    public static class HealthTabHelper
    {
        /// <summary>
        /// Represents a capacity with its level and breakdown.
        /// </summary>
        public class CapacityInfo
        {
            public PawnCapacityDef Def { get; set; }
            public string Label { get; set; }
            public float Level { get; set; }
            public string LevelLabel { get; set; }
            public string DetailedBreakdown { get; set; }
        }

        /// <summary>
        /// Represents a body part with its hediffs.
        /// </summary>
        public class BodyPartInfo
        {
            public BodyPartRecord Part { get; set; }
            public string Label { get; set; }
            public float Health { get; set; }
            public float MaxHealth { get; set; }
            public float Efficiency { get; set; }
            public List<HediffInfo> Hediffs { get; set; }

            public BodyPartInfo()
            {
                Hediffs = new List<HediffInfo>();
            }
        }

        /// <summary>
        /// Represents a hediff (health condition).
        /// </summary>
        public class HediffInfo
        {
            public Hediff Hediff { get; set; }
            public string Label { get; set; }
            public string DetailedInfo { get; set; }
        }

        /// <summary>
        /// Represents a medical operation.
        /// </summary>
        public class OperationInfo
        {
            public RecipeDef Recipe { get; set; }
            public BodyPartRecord BodyPart { get; set; }
            public string Label { get; set; }
            public string Requirements { get; set; }
            public bool IsAvailable { get; set; }
            public string UnavailableReason { get; set; }
        }

        #region Medical Settings

        /// <summary>
        /// Gets the current food restriction for a pawn.
        /// </summary>
        public static string GetCurrentFoodRestriction(Pawn pawn)
        {
            if (pawn?.foodRestriction?.CurrentFoodPolicy == null)
                return "None";

            return pawn.foodRestriction.CurrentFoodPolicy.label;
        }

        /// <summary>
        /// Gets all available food restrictions.
        /// </summary>
        public static List<FoodPolicy> GetAvailableFoodRestrictions()
        {
            if (Current.Game?.foodRestrictionDatabase == null)
                return new List<FoodPolicy>();

            return Current.Game.foodRestrictionDatabase.AllFoodRestrictions.ToList();
        }

        /// <summary>
        /// Sets the food restriction for a pawn.
        /// </summary>
        public static bool SetFoodRestriction(Pawn pawn, FoodPolicy restriction)
        {
            try
            {
                if (pawn?.foodRestriction == null)
                    return false;

                pawn.foodRestriction.CurrentFoodPolicy = restriction;
                TolkHelper.Speak($"Food restriction set to: {restriction.label}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error setting food restriction: {ex}");
                TolkHelper.Speak("Error setting food restriction", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Gets the current medical care quality for a pawn.
        /// </summary>
        public static string GetCurrentMedicalCare(Pawn pawn)
        {
            if (pawn?.playerSettings == null)
                return "None";

            return pawn.playerSettings.medCare.GetLabel();
        }

        /// <summary>
        /// Gets all available medical care levels.
        /// </summary>
        public static List<MedicalCareCategory> GetAvailableMedicalCare()
        {
            return Enum.GetValues(typeof(MedicalCareCategory))
                .Cast<MedicalCareCategory>()
                .ToList();
        }

        /// <summary>
        /// Sets the medical care quality for a pawn.
        /// </summary>
        public static bool SetMedicalCare(Pawn pawn, MedicalCareCategory care)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;

                pawn.playerSettings.medCare = care;
                TolkHelper.Speak($"Medical care set to: {care.GetLabel()}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error setting medical care: {ex}");
                TolkHelper.Speak("Error setting medical care", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Gets whether self-tend is enabled for a pawn.
        /// </summary>
        public static bool GetSelfTendEnabled(Pawn pawn)
        {
            if (pawn?.playerSettings == null)
                return false;

            return pawn.playerSettings.selfTend;
        }

        /// <summary>
        /// Toggles self-tend for a pawn.
        /// </summary>
        public static bool ToggleSelfTend(Pawn pawn)
        {
            try
            {
                if (pawn?.playerSettings == null)
                    return false;

                pawn.playerSettings.selfTend = !pawn.playerSettings.selfTend;
                string status = pawn.playerSettings.selfTend ? "enabled" : "disabled";
                TolkHelper.Speak($"Self-tend {status}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error toggling self-tend: {ex}");
                TolkHelper.Speak("Error toggling self-tend", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion

        #region Capacities

        /// <summary>
        /// Gets all capacity information for a pawn, sorted by level (lowest/most impaired first).
        /// </summary>
        public static List<CapacityInfo> GetCapacities(Pawn pawn)
        {
            var capacities = new List<CapacityInfo>();

            if (pawn?.health?.capacities == null)
                return capacities;

            // Get key capacities
            var keyCapacities = new List<PawnCapacityDef>
            {
                PawnCapacityDefOf.Consciousness,
                PawnCapacityDefOf.Sight,
                PawnCapacityDefOf.Hearing,
                PawnCapacityDefOf.Moving,
                PawnCapacityDefOf.Manipulation,
                PawnCapacityDefOf.Talking,
                PawnCapacityDefOf.Breathing,
                PawnCapacityDefOf.BloodFiltration,
                PawnCapacityDefOf.BloodPumping
            };

            foreach (var capacityDef in keyCapacities)
            {
                if (capacityDef == null || !pawn.health.capacities.CapableOf(capacityDef))
                    continue;

                float level = pawn.health.capacities.GetLevel(capacityDef);
                string label = capacityDef.LabelCap.ToString().StripTags();
                string levelLabel = GetCapacityLevelLabel(level);

                capacities.Add(new CapacityInfo
                {
                    Def = capacityDef,
                    Label = label,
                    Level = level,
                    LevelLabel = levelLabel,
                    DetailedBreakdown = GetCapacityBreakdown(pawn, capacityDef)
                });
            }

            // Sort by level (lowest first = most impaired/urgent)
            capacities = capacities.OrderBy(c => c.Level).ToList();

            return capacities;
        }

        /// <summary>
        /// Gets a human-readable label for a capacity level.
        /// </summary>
        private static string GetCapacityLevelLabel(float level)
        {
            if (level <= 0f)
                return "None (0%)";
            if (level < 0.4f)
                return $"Very Poor ({level:P0})";
            if (level < 0.7f)
                return $"Poor ({level:P0})";
            if (level < 1.0f)
                return $"Weakened ({level:P0})";
            if (level < 1.3f)
                return $"Good ({level:P0})";
            return $"Enhanced ({level:P0})";
        }

        /// <summary>
        /// Gets a detailed breakdown of what affects a capacity.
        /// </summary>
        private static string GetCapacityBreakdown(Pawn pawn, PawnCapacityDef capacity)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{capacity.LabelCap.ToString().StripTags()}:");
            sb.AppendLine();

            // Get capacity breakdown using PawnCapacityUtility
            var impactors = new List<PawnCapacityUtility.CapacityImpactor>();
            float level = PawnCapacityUtility.CalculateCapacityLevel(
                pawn.health.hediffSet,
                capacity,
                impactors
            );

            sb.AppendLine($"Current level: {level:P0}");
            sb.AppendLine();

            if (impactors != null && impactors.Count > 0)
            {
                sb.AppendLine("Factors:");
                foreach (var impactor in impactors)
                {
                    string readable = impactor.Readable(pawn);
                    if (!string.IsNullOrEmpty(readable))
                    {
                        sb.AppendLine($"  {readable}");
                    }
                }
            }
            else
            {
                sb.AppendLine("No factors affecting this capacity");
            }

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Operations

        /// <summary>
        /// Gets all queued operations for a pawn.
        /// </summary>
        public static List<Bill> GetQueuedOperations(Pawn pawn)
        {
            if (pawn?.BillStack == null)
                return new List<Bill>();

            return pawn.BillStack.Bills.ToList();
        }

        /// <summary>
        /// Gets all available recipe types (operations) for a pawn, without body part specifics.
        /// </summary>
        public static List<RecipeDef> GetAvailableRecipes(Pawn pawn)
        {
            if (pawn?.health == null)
                return new List<RecipeDef>();

            // Get all medical recipes that can be performed on this pawn
            var recipes = DefDatabase<RecipeDef>.AllDefs
                .Where(r => r.AllRecipeUsers != null &&
                           r.AllRecipeUsers.Contains(pawn.def) &&
                           r.AvailableNow)
                .ToList();

            return recipes;
        }

        /// <summary>
        /// Gets all body parts that a recipe can be applied to for a pawn.
        /// Returns an empty list if the recipe doesn't target specific body parts.
        /// </summary>
        public static List<BodyPartRecord> GetPartsForRecipe(Pawn pawn, RecipeDef recipe)
        {
            var parts = new List<BodyPartRecord>();

            if (pawn?.health == null || recipe == null)
                return parts;

            // Use the recipe's Worker to get valid parts (this handles all the complex logic)
            if (recipe.Worker != null)
            {
                var validParts = recipe.Worker.GetPartsToApplyOn(pawn, recipe);
                if (validParts != null)
                {
                    parts.AddRange(validParts);
                }
            }

            return parts;
        }

        /// <summary>
        /// Gets all available operations for a pawn.
        /// </summary>
        public static List<OperationInfo> GetAvailableOperations(Pawn pawn)
        {
            var operations = new List<OperationInfo>();

            if (pawn?.health == null)
                return operations;

            // Get all medical recipes
            var recipes = DefDatabase<RecipeDef>.AllDefs
                .Where(r => r.AllRecipeUsers != null &&
                           r.AllRecipeUsers.Contains(pawn.def) &&
                           r.AvailableNow)
                .ToList();

            foreach (var recipe in recipes)
            {
                // Check if recipe applies to whole body or specific parts
                if (recipe.appliedOnFixedBodyParts != null && recipe.appliedOnFixedBodyParts.Count > 0)
                {
                    // Recipe applies to specific body parts
                    foreach (var part in pawn.health.hediffSet.GetNotMissingParts())
                    {
                        if (recipe.appliedOnFixedBodyParts.Contains(part.def))
                        {
                            var opInfo = CreateOperationInfo(pawn, recipe, part);
                            operations.Add(opInfo);
                        }
                    }
                }
                else
                {
                    // Recipe applies to whole body
                    var opInfo = CreateOperationInfo(pawn, recipe, null);
                    operations.Add(opInfo);
                }
            }

            return operations;
        }

        private static OperationInfo CreateOperationInfo(Pawn pawn, RecipeDef recipe, BodyPartRecord part)
        {
            var opInfo = new OperationInfo
            {
                Recipe = recipe,
                BodyPart = part,
                Label = recipe.LabelCap.ToString().StripTags()
            };

            if (part != null)
            {
                opInfo.Label += $" ({part.Label})";
            }

            // Check availability
            var violations = new List<string>();
            if (!recipe.Worker.AvailableOnNow(pawn, part))
            {
                opInfo.IsAvailable = false;
                opInfo.UnavailableReason = "Not available on this pawn";
            }
            else
            {
                opInfo.IsAvailable = true;
            }

            // Get requirements
            var reqSb = new StringBuilder();
            if (recipe.ingredients != null && recipe.ingredients.Count > 0)
            {
                reqSb.Append("Requires: ");
                foreach (var ingredient in recipe.ingredients)
                {
                    reqSb.Append($"{ingredient.Summary}, ");
                }
                opInfo.Requirements = reqSb.ToString().TrimEnd(',', ' ');
            }

            return opInfo;
        }

        /// <summary>
        /// Adds an operation to a pawn's bill stack.
        /// </summary>
        public static bool AddOperation(Pawn pawn, RecipeDef recipe, BodyPartRecord part)
        {
            try
            {
                if (pawn?.BillStack == null)
                    return false;

                // Create bill using proper constructor with recipe and uniqueIngredients
                // Pass null for uniqueIngredients as we're not pre-selecting ingredients
                Bill_Medical bill = new Bill_Medical(recipe, null);

                // Add bill to stack FIRST
                pawn.BillStack.AddBill(bill);

                // THEN set the body part (must be done after adding to stack)
                bill.Part = part;

                TolkHelper.Speak($"Added operation: {recipe.LabelCap.ToString().StripTags()}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error adding operation: {ex}");
                TolkHelper.Speak("Error adding operation", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Removes an operation from a pawn's bill stack.
        /// </summary>
        public static bool RemoveOperation(Pawn pawn, Bill bill)
        {
            try
            {
                if (pawn?.BillStack == null || bill == null)
                    return false;

                pawn.BillStack.Delete(bill);
                TolkHelper.Speak($"Removed operation: {bill.LabelCap.ToString().StripTags()}");
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error removing operation: {ex}");
                TolkHelper.Speak("Error removing operation", SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        #endregion

        #region Body Parts & Hediffs

        /// <summary>
        /// Gets all body parts with their hediffs organized, sorted by severity (most damaged first).
        /// Uses health percentage (lower = more urgent) and prioritizes core/vital parts.
        /// </summary>
        public static List<BodyPartInfo> GetBodyPartsWithHediffs(Pawn pawn)
        {
            var parts = new List<BodyPartInfo>();

            if (pawn?.health?.hediffSet == null)
                return parts;

            // First, get whole-body hediffs
            var wholeBodyHediffs = pawn.health.hediffSet.hediffs
                .Where(h => h.Part == null && h.Visible)
                .OrderByDescending(h => h.Severity) // Most severe first
                .ToList();

            if (wholeBodyHediffs.Count > 0)
            {
                var wholeBodyPart = new BodyPartInfo
                {
                    Part = null,
                    Label = "Whole Body",
                    Health = 0,
                    MaxHealth = 0,
                    Efficiency = 1.0f
                };

                foreach (var hediff in wholeBodyHediffs)
                {
                    wholeBodyPart.Hediffs.Add(CreateHediffInfo(hediff));
                }

                parts.Add(wholeBodyPart);
            }

            // Then get hediffs for each body part
            foreach (var part in pawn.health.hediffSet.GetNotMissingParts())
            {
                var hediffs = pawn.health.hediffSet.hediffs
                    .Where(h => h.Part == part && h.Visible)
                    .OrderByDescending(h => h.Severity) // Most severe first within each part
                    .ToList();

                if (hediffs.Count == 0)
                    continue;

                float partHealth = pawn.health.hediffSet.GetPartHealth(part);
                float maxHealth = part.def.GetMaxHealth(pawn);
                float efficiency = PawnCapacityUtility.CalculatePartEfficiency(pawn.health.hediffSet, part);

                var partInfo = new BodyPartInfo
                {
                    Part = part,
                    Label = part.Label,
                    Health = partHealth,
                    MaxHealth = maxHealth,
                    Efficiency = efficiency
                };

                foreach (var hediff in hediffs)
                {
                    partInfo.Hediffs.Add(CreateHediffInfo(hediff));
                }

                parts.Add(partInfo);
            }

            // Sort parts: core/vital parts first, then by health percentage (lowest first = most damaged)
            parts = parts
                .OrderByDescending(p => p.Part?.IsCorePart ?? true) // Core parts first (whole body counts as core)
                .ThenBy(p => p.MaxHealth > 0 ? p.Health / p.MaxHealth : 1f) // Lowest health % first
                .ThenBy(p => p.Efficiency) // Lowest efficiency first
                .ToList();

            return parts;
        }

        private static HediffInfo CreateHediffInfo(Hediff hediff)
        {
            return new HediffInfo
            {
                Hediff = hediff,
                Label = hediff.LabelCap.ToString().StripTags(),
                DetailedInfo = GetHediffDetailedInfo(hediff)
            };
        }

        private static string GetHediffDetailedInfo(Hediff hediff)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{hediff.LabelCap.ToString().StripTags()}:");
            sb.AppendLine();

            // Part affected (show mechanical effects first)
            if (hediff.Part != null)
            {
                // Show part efficiency if available
                string partInfo = $"Affects: {hediff.Part.Label}";
                if (hediff.pawn != null)
                {
                    float efficiency = PawnCapacityUtility.CalculatePartEfficiency(hediff.pawn.health.hediffSet, hediff.Part);
                    if (efficiency < 0.999f) // Only show if less than 100%
                    {
                        partInfo += $" (part at {efficiency:P0})";
                    }
                }
                sb.AppendLine(partInfo);
                sb.AppendLine();
            }

            // Get all mechanical effects from RimWorld's TipStringExtra
            string tipExtra = hediff.TipStringExtra;
            if (!string.IsNullOrEmpty(tipExtra))
            {
                sb.AppendLine(tipExtra.Trim());
                sb.AppendLine();
            }

            // Severity/Stage (if not already in TipStringExtra)
            if (hediff.def.stages != null && hediff.def.stages.Count > 0)
            {
                sb.AppendLine($"Stage: {hediff.CurStageIndex + 1} of {hediff.def.stages.Count}");
                sb.AppendLine($"Severity: {hediff.Severity:F2}");
                sb.AppendLine();
            }

            // Immunity
            if (hediff.TryGetComp<HediffComp_Immunizable>() is HediffComp_Immunizable immunizable)
            {
                if (hediff.pawn != null && hediff.pawn.health?.immunity != null)
                {
                    float immunity = hediff.pawn.health.immunity.GetImmunity(hediff.def);
                    sb.AppendLine($"Immunity: {immunity:P0}");
                    sb.AppendLine();
                }
            }

            // Bleeding
            if (hediff.Bleeding)
            {
                sb.AppendLine($"Bleeding: {hediff.BleedRate:F2} per day");
                sb.AppendLine();
            }

            // Pain
            float pain = hediff.PainOffset;
            if (pain > 0.01f)
            {
                sb.AppendLine($"Pain: +{pain:P0}");
                sb.AppendLine();
            }

            // Tendable
            if (hediff.TendableNow())
            {
                sb.AppendLine("Can be tended");
                sb.AppendLine();
            }

            // Description (show after mechanical effects)
            if (!string.IsNullOrEmpty(hediff.def.description))
            {
                sb.AppendLine(hediff.def.description);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets comprehensive effect information for a hediff, focusing on functional impacts.
        /// </summary>
        public static string GetComprehensiveHediffEffects(Hediff hediff, Pawn pawn)
        {
            if (hediff == null)
                return string.Empty;

            var sb = new StringBuilder();
            var stage = hediff.CurStage;
            bool hasAnyEffect = false;

            // Life-threatening status (show first as most critical)
            if (hediff.IsCurrentlyLifeThreatening)
            {
                sb.AppendLine("⚠ LIFE THREATENING");
                hasAnyEffect = true;
            }

            // Bleeding
            if (hediff.Bleeding)
            {
                sb.AppendLine($"Bleeding: {hediff.BleedRate:F2} per day");
                hasAnyEffect = true;
            }

            // Pain
            float pain = hediff.PainOffset;
            if (pain > 0.01f)
            {
                sb.AppendLine($"Pain: +{pain:P0}");
                hasAnyEffect = true;
            }

            // Capacity impacts with percentages
            if (hediff.CapMods != null && hediff.CapMods.Count > 0)
            {
                foreach (var capMod in hediff.CapMods)
                {
                    if (capMod.capacity == null)
                        continue;

                    string capacityName = capMod.capacity.LabelCap.ToString().StripTags();
                    float currentLevel = 0f;

                    // Get current capacity level if pawn is available
                    if (pawn?.health?.capacities != null && pawn.health.capacities.CapableOf(capMod.capacity))
                    {
                        currentLevel = pawn.health.capacities.GetLevel(capMod.capacity);
                    }

                    // Format the impact
                    var impactParts = new List<string>();

                    if (capMod.offset != 0f)
                    {
                        string sign = capMod.offset > 0 ? "+" : "";
                        impactParts.Add($"{sign}{capMod.offset:P0}");
                    }

                    if (capMod.postFactor != 1f)
                    {
                        float percentChange = (capMod.postFactor - 1f) * 100f;
                        string sign = percentChange > 0 ? "+" : "";
                        impactParts.Add($"{sign}{percentChange:F0}%");
                    }

                    if (capMod.SetMaxDefined)
                    {
                        float maxValue = capMod.setMax;
                        impactParts.Add($"max {maxValue:P0}");
                    }

                    string impactStr = string.Join(", ", impactParts);

                    if (pawn != null && currentLevel > 0f)
                    {
                        sb.AppendLine($"{capacityName}: {impactStr} (current: {currentLevel:P0})");
                    }
                    else
                    {
                        sb.AppendLine($"{capacityName}: {impactStr}");
                    }

                    hasAnyEffect = true;
                }
            }

            // Note: Removed generic part efficiency - it's not specific to this condition
            // and confuses users. CapMods above shows the actual impact of this hediff.

            // Stage-based effects
            if (stage != null)
            {
                // Work restrictions
                if (stage.disabledWorkTags != WorkTags.None)
                {
                    sb.AppendLine($"Disables work: {stage.disabledWorkTags}");
                    hasAnyEffect = true;
                }

                // Need modifications
                if (stage.hungerRateFactor != 1f || stage.hungerRateFactorOffset != 0f)
                {
                    float totalFactor = stage.hungerRateFactor + stage.hungerRateFactorOffset;
                    sb.AppendLine($"Hunger rate: x{totalFactor:F2}");
                    hasAnyEffect = true;
                }

                if (stage.restFallFactor != 1f || stage.restFallFactorOffset != 0f)
                {
                    float totalFactor = stage.restFallFactor + stage.restFallFactorOffset;
                    sb.AppendLine($"Rest fall rate: x{totalFactor:F2}");
                    hasAnyEffect = true;
                }

                if (stage.fertilityFactor != 1f && stage.fertilityFactor >= 0f)
                {
                    sb.AppendLine($"Fertility: x{stage.fertilityFactor:F2}");
                    hasAnyEffect = true;
                }

                // Enabled/disabled needs
                if (stage.enablesNeeds != null && stage.enablesNeeds.Count > 0)
                {
                    foreach (var need in stage.enablesNeeds)
                    {
                        sb.AppendLine($"Enables need: {need.LabelCap.ToString().StripTags()}");
                        hasAnyEffect = true;
                    }
                }

                if (stage.disablesNeeds != null && stage.disablesNeeds.Count > 0)
                {
                    foreach (var need in stage.disablesNeeds)
                    {
                        sb.AppendLine($"Disables need: {need.LabelCap.ToString().StripTags()}");
                        hasAnyEffect = true;
                    }
                }

                // Activity restrictions
                if (stage.blocksSleeping)
                {
                    sb.AppendLine("Prevents sleeping");
                    hasAnyEffect = true;
                }

                if (stage.blocksMentalBreaks)
                {
                    sb.AppendLine("Prevents mental breaks");
                    hasAnyEffect = true;
                }

                if (stage.blocksInspirations)
                {
                    sb.AppendLine("Prevents inspirations");
                    hasAnyEffect = true;
                }

                // Mental health effects
                if (stage.mentalBreakMtbDays > 0f)
                {
                    sb.AppendLine($"Mental break risk: every {stage.mentalBreakMtbDays:F1} days (avg)");
                    hasAnyEffect = true;
                }

                if (stage.forgetMemoryThoughtMtbDays > 0f)
                {
                    sb.AppendLine($"Memory loss: every {stage.forgetMemoryThoughtMtbDays:F1} days (avg)");
                    hasAnyEffect = true;
                }

                // Progression effects
                if (stage.vomitMtbDays > 0f)
                {
                    sb.AppendLine($"Vomiting: every {stage.vomitMtbDays:F1} days (avg)");
                    hasAnyEffect = true;
                }

                if (stage.deathMtbDays > 0f)
                {
                    sb.AppendLine($"Death risk: every {stage.deathMtbDays:F1} days (avg)");
                    hasAnyEffect = true;
                }

                // Healing effects
                if (stage.naturalHealingFactor != -1f && stage.naturalHealingFactor != 1f)
                {
                    if (stage.naturalHealingFactor == 0f)
                    {
                        sb.AppendLine("Prevents natural healing");
                    }
                    else
                    {
                        sb.AppendLine($"Natural healing: x{stage.naturalHealingFactor:F2}");
                    }
                    hasAnyEffect = true;
                }

                if (stage.regeneration > 0f)
                {
                    sb.AppendLine($"Regeneration: +{stage.regeneration:F2} HP/day");
                    hasAnyEffect = true;
                }
            }

            // Tend status and quality
            var tendComp = hediff.TryGetComp<HediffComp_TendDuration>();
            if (tendComp != null)
            {
                if (tendComp.IsTended)
                {
                    sb.AppendLine($"Tended: {tendComp.tendQuality:P0} quality");
                    hasAnyEffect = true;
                }
                else if (hediff.TendableNow())
                {
                    sb.AppendLine("Needs tending");
                    hasAnyEffect = true;
                }
            }
            else if (hediff.TendableNow())
            {
                sb.AppendLine("Needs tending");
                hasAnyEffect = true;
            }

            // Permanence status - only show if already permanent
            var permanentComp = hediff.TryGetComp<HediffComp_GetsPermanent>();
            if (permanentComp != null && permanentComp.IsPermanent)
            {
                sb.AppendLine("Permanent scar");
                hasAnyEffect = true;
            }

            // Immunity progress
            var immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
            if (immunizable != null && pawn?.health?.immunity != null)
            {
                float immunity = pawn.health.immunity.GetImmunity(hediff.def);
                sb.AppendLine($"Immunity: {immunity:P0}");
                hasAnyEffect = true;
            }

            // Severity info
            if (hediff.def.lethalSeverity > 0f || hediff.def.stages != null)
            {
                string severityInfo = $"Severity: {hediff.Severity:F2}";

                if (hediff.def.lethalSeverity > 0f)
                {
                    severityInfo += $" / {hediff.def.lethalSeverity:F2} (lethal)";
                }
                else if (hediff.def.maxSeverity < 999f)
                {
                    severityInfo += $" / {hediff.def.maxSeverity:F2} (max)";
                }

                if (hediff.def.stages != null && hediff.def.stages.Count > 0)
                {
                    severityInfo += $" [Stage {hediff.CurStageIndex + 1}/{hediff.def.stages.Count}]";
                }

                sb.AppendLine(severityInfo);
                hasAnyEffect = true;
            }

            // If no specific effects found, show a generic message
            if (!hasAnyEffect)
            {
                sb.AppendLine("No significant mechanical effects");
            }

            return sb.ToString().TrimEnd();
        }

        #endregion
    }
}
