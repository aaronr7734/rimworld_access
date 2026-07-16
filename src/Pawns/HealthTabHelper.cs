using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

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
            public string Description { get; set; }
            public float Level { get; set; }
            public string LevelLabel { get; set; }
            public string DetailedBreakdown { get; set; }
        }

        #region Medical Settings

        /// <summary>
        /// Gets the current food restriction for a pawn.
        /// </summary>
        public static string GetCurrentFoodRestriction(Pawn pawn)
        {
            if (pawn?.foodRestriction?.CurrentFoodPolicy == null)
                return "NoneLower".Translate();

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
            if (pawn?.foodRestriction == null)
                return false;

            pawn.foodRestriction.CurrentFoodPolicy = restriction;
            return true;
        }

        /// <summary>
        /// Gets the current medical care quality for a pawn.
        /// </summary>
        public static string GetCurrentMedicalCare(Pawn pawn)
        {
            if (pawn?.playerSettings == null)
                return "NoneLower".Translate();

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
            if (pawn?.playerSettings == null)
                return false;

            pawn.playerSettings.medCare = care;
            return true;
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
            if (pawn?.playerSettings == null)
                return false;

            pawn.playerSettings.selfTend = !pawn.playerSettings.selfTend;
            return true;
        }

        #endregion

        #region Capacities

        /// <summary>
        /// Gets all capacity information for a pawn, using vanilla's filtering, sorting, and labels.
        /// Filters by pawn type (humanlike/animal/mechanoid/etc.), sorts by vanilla's listOrder,
        /// and uses pawn-type-specific labels (e.g. "Data processing" for mechs).
        /// </summary>
        public static List<CapacityInfo> GetCapacities(Pawn pawn)
        {
            var capacities = new List<CapacityInfo>();

            if (pawn?.health?.capacities == null || pawn.Dead)
                return capacities;

            // Use vanilla's filtering: only show capacities appropriate for this pawn type
            var visibleCapacities = DefDatabase<PawnCapacityDef>.AllDefs
                .Where(cap => cap.CanShowOnPawn(pawn)
                    && PawnCapacityUtility.BodyCanEverDoCapacity(pawn.RaceProps.body, cap))
                .OrderBy(cap => cap.listOrder);

            foreach (var capacityDef in visibleCapacities)
            {
                float level = pawn.health.capacities.GetLevel(capacityDef);
                // Use pawn-type-specific label (e.g. "Data processing" for mechs)
                string label = capacityDef.GetLabelFor(pawn).CapitalizeFirst();
                string levelLabel = GetCapacityLevelLabel(level);

                capacities.Add(new CapacityInfo
                {
                    Def = capacityDef,
                    Label = label,
                    Description = capacityDef.description ?? "",
                    Level = level,
                    LevelLabel = levelLabel,
                    DetailedBreakdown = GetCapacityBreakdown(pawn, capacityDef)
                });
            }

            return capacities;
        }

        /// <summary>
        /// Gets a translatable label for a capacity level using vanilla's EfficiencyEstimate system.
        /// </summary>
        private static string GetCapacityLevelLabel(float level)
        {
            var estimate = HealthCardUtility.EfficiencyValueToEstimate(level);
            string translatedLabel = estimate.ToString().Translate();
            return $"{translatedLabel}, {level:P0}";
        }

        /// <summary>
        /// Gets a detailed breakdown of what affects a capacity,
        /// matching vanilla's GetPawnCapacityTip() format with impactors grouped by type.
        /// </summary>
        private static string GetCapacityBreakdown(Pawn pawn, PawnCapacityDef capacity)
        {
            var impactors = new List<PawnCapacityUtility.CapacityImpactor>();
            PawnCapacityUtility.CalculateCapacityLevel(
                pawn.health.hediffSet,
                capacity,
                impactors
            );

            // Filter out capacities that can't show on this pawn (matches vanilla)
            impactors.RemoveAll(x =>
                x is PawnCapacityUtility.CapacityImpactorCapacity capImpactor
                && !capImpactor.capacity.CanShowOnPawn(pawn));

            if (impactors.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("AffectedBy".Translate().ToString());

            // Group by type like vanilla does: hediffs first, then body parts, then genes, then capacities
            var seenHediffs = new HashSet<Hediff>();
            var seenBodyParts = new HashSet<BodyPartRecord>();
            var seenGenes = new HashSet<object>();

            foreach (var impactor in impactors)
            {
                if (impactor is PawnCapacityUtility.CapacityImpactorHediff hediffImpactor)
                {
                    if (seenHediffs.Add(hediffImpactor.hediff))
                        sb.AppendLine($"  {impactor.Readable(pawn)}");
                }
            }
            foreach (var impactor in impactors)
            {
                if (impactor is PawnCapacityUtility.CapacityImpactorBodyPartHealth bpImpactor)
                {
                    if (seenBodyParts.Add(bpImpactor.bodyPart))
                        sb.AppendLine($"  {impactor.Readable(pawn)}");
                }
            }
            foreach (var impactor in impactors)
            {
                if (impactor is PawnCapacityUtility.CapacityImpactorGene geneImpactor)
                {
                    if (seenGenes.Add(geneImpactor.gene))
                        sb.AppendLine($"  {impactor.Readable(pawn)}");
                }
            }
            foreach (var impactor in impactors)
            {
                if (impactor is PawnCapacityUtility.CapacityImpactorCapacity)
                {
                    sb.AppendLine($"  {impactor.Readable(pawn)}");
                }
            }
            foreach (var impactor in impactors)
            {
                if (impactor is PawnCapacityUtility.CapacityImpactorPain)
                {
                    sb.AppendLine($"  {impactor.Readable(pawn)}");
                }
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
        /// Gets available recipe types (operations) for a pawn, matching vanilla's
        /// dynamic ingredient-aware filtering from HealthCardUtility.DrawMedOperationsTab.
        /// </summary>
        public static List<RecipeDef> GetAvailableRecipes(Pawn pawn)
        {
            if (pawn?.health == null)
                return new List<RecipeDef>();

            var recipes = new List<RecipeDef>();

            foreach (RecipeDef recipe in pawn.def.AllRecipes)
            {
                if (!recipe.AvailableNow)
                    continue;

                AcceptanceReport report = recipe.Worker.AvailableReport(pawn);
                if (!report.Accepted && report.Reason.NullOrEmpty())
                    continue;

                // Match vanilla: hide recipes where required tech hediffs or drugs are missing
                if (pawn.MapHeld != null)
                {
                    var missing = recipe.PotentiallyMissingIngredients(null, pawn.MapHeld);
                    if (missing.Any(x => x.isTechHediff) || missing.Any(x => x.IsDrug))
                        continue;
                    if (missing.Any() && recipe.dontShowIfAnyIngredientMissing)
                        continue;
                }

                // Match vanilla: for non-body-part recipes that add a hediff,
                // hide if pawn already has that hediff
                if (!recipe.targetsBodyPart && recipe.addsHediff != null
                    && pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                    continue;

                recipes.Add(recipe);
            }

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
        /// Adds an operation to a pawn's bill stack.
        /// </summary>
        public static bool AddOperation(Pawn pawn, RecipeDef recipe, BodyPartRecord part)
        {
            if (pawn?.BillStack == null)
                return false;

            Bill_Medical bill = new Bill_Medical(recipe, null);
            pawn.BillStack.AddBill(bill);
            bill.Part = part;
            return true;
        }

        /// <summary>
        /// Removes an operation from a pawn's bill stack.
        /// </summary>
        public static bool RemoveOperation(Pawn pawn, Bill bill)
        {
            if (pawn?.BillStack == null || bill == null)
                return false;

            pawn.BillStack.Delete(bill);
            return true;
        }

        #endregion

        #region Hediff Information

        /// <summary>
        /// Gets comprehensive effect information for a hediff, focusing on functional impacts.
        /// Uses vanilla's TipStringExtra for consistency with what sighted players see, plus
        /// explicit severity and immunity readouts (see below) that TipStringExtra alone does
        /// not reliably surface.
        /// </summary>
        public static string GetComprehensiveHediffEffects(Hediff hediff, Pawn pawn)
        {
            if (hediff == null)
                return string.Empty;

            var sb = new StringBuilder();

            // Life-threatening status (show first as most critical)
            if (hediff.IsCurrentlyLifeThreatening)
            {
                sb.AppendLine("PawnsWithLifeThreateningDisease".Translate().ToString().ToUpper());
            }

            // Severity - vanilla's own formatted severity string (e.g. "54%"). Vanilla only
            // populates this for hediffs that can kill or that opt into always showing it
            // (def.alwaysShowSeverity); it is never included in TipStringExtra, so it must be
            // read directly. This was dropped by PR #62's TipStringExtra-only rewrite.
            string severityLabel = hediff.SeverityLabel;
            if (!string.IsNullOrEmpty(severityLabel))
            {
                sb.AppendLine($"{"RimWorldAccess.Pawns.Health.Severity".Translate()}: {severityLabel}");
            }

            // Immunity progress - read HediffComp_Immunizable directly rather than relying on
            // TipStringExtra's indirect CompTipStringExtra aggregation, which vanilla hides once
            // Hidden, not naturally-developable, or already fully immune. Also dropped by #62.
            string immunityLabel = null;
            var immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
            if (immunizable != null && pawn?.health?.immunity != null)
            {
                immunityLabel = $"{"Immunity".Translate()}: {immunizable.Immunity.ToStringPercent("0.#")}";
                sb.AppendLine(immunityLabel);
            }

            // Use vanilla's TipStringExtra - this is what sighted players see in tooltips
            string tipExtra = hediff.TipStringExtra;
            if (!string.IsNullOrEmpty(tipExtra))
            {
                string cleaned = tipExtra.StripTags().Trim();
                if (immunityLabel != null && !string.IsNullOrEmpty(cleaned))
                {
                    // Drop any redundant immunity line TipStringExtra may already contain -
                    // we announced it explicitly above.
                    string immunityKeyword = "Immunity".Translate();
                    cleaned = string.Join("\n", cleaned
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => !line.Contains(immunityKeyword)));
                }
                if (!string.IsNullOrEmpty(cleaned))
                {
                    sb.AppendLine(cleaned);
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Gets the vanilla pain label (qualitative + percentage) using translation keys.
        /// Returns null if no pain (for flesh pawns) or not applicable.
        /// </summary>
        public static string GetPainLabel(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || !pawn.def.race.IsFlesh)
                return null;

            float painTotal = pawn.health.hediffSet.PainTotal;
            if (Mathf.Approximately(painTotal, 0f))
                return null;

            string qualitative;
            if (painTotal < 0.15f)
                qualitative = "LittlePain".Translate();
            else if (painTotal < 0.4f)
                qualitative = "MediumPain".Translate();
            else if (painTotal < 0.8f)
                qualitative = "SeverePain".Translate();
            else
                qualitative = "ExtremePain".Translate();

            return $"{"PainLevel".Translate()}: {qualitative} ({painTotal:P0})";
        }

        /// <summary>
        /// Gets the bleeding rate label with time-to-death information, using vanilla translation keys.
        /// Returns null if not bleeding.
        /// </summary>
        public static string GetBleedingLabel(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return null;

            float bleedRate = pawn.health.hediffSet.BleedRateTotal;
            if (bleedRate <= 0.01f)
                return null;

            string label = $"{"BleedingRate".Translate()}: {bleedRate.ToStringPercent()}/{"LetterDay".Translate()}";

            // Add time-to-death or safety status
            if (ModsConfig.BiotechActive && pawn.genes != null
                && pawn.genes.HasActiveGene(GeneDefOf.Deathless))
            {
                label += $" ({"Deathless".Translate()})";
            }
            else
            {
                int ticksUntilDeath = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);
                if (ticksUntilDeath >= 60000)
                    label += $" ({"WontBleedOutSoon".Translate()})";
                else
                    label += $" ({"TimeToDeath".Translate(ticksUntilDeath.ToStringTicksToPeriod())})";
            }

            return label;
        }

        /// <summary>
        /// Gets visible hediffs using vanilla's own filtering logic.
        /// Uses GetMissingPartsCommonAncestors() for missing parts (handles bionics correctly)
        /// and all other visible non-MissingPart hediffs.
        /// </summary>
        public static IEnumerable<Hediff> GetVisibleHediffs(Pawn pawn, bool showBloodLoss = true)
        {
            if (pawn?.health?.hediffSet == null)
                yield break;

            // Missing parts via vanilla's smart common-ancestor logic
            // (already filters out bionic-replaced parts)
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            for (int i = 0; i < missingParts.Count; i++)
            {
                yield return missingParts[i];
            }

            // All other visible hediffs (excluding MissingPart to avoid doubles)
            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff is Hediff_MissingPart)
                    continue;
                if (!hediff.Visible)
                    continue;
                if (!showBloodLoss && hediff.def == HediffDefOf.BloodLoss)
                    continue;

                yield return hediff;
            }
        }

        /// <summary>
        /// Gets the sort priority for a body part, matching vanilla's GetListPriority().
        /// Higher priority = shown first. Whole body (null) has highest priority.
        /// </summary>
        public static float GetHediffListPriority(BodyPartRecord rec)
        {
            if (rec == null)
                return 9999999f;
            return (float)((int)rec.height * 10000) + rec.coverageAbsWithChildren;
        }

        #endregion
    }
}
