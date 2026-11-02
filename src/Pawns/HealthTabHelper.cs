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
    /// Health-tab reads and mutations: medical settings, capacities, operations, hediffs.
    /// </summary>
    public static class HealthTabHelper
    {
        /// <summary>One capacity with its level and impactor breakdown.</summary>
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

        /// <summary>The pawn's current food policy label, or "none".</summary>
        public static string GetCurrentFoodRestriction(Pawn pawn)
        {
            if (pawn?.foodRestriction?.CurrentFoodPolicy == null)
                return "NoneLower".Translate();

            return pawn.foodRestriction.CurrentFoodPolicy.label;
        }

        /// <summary>Every food policy in the game's database.</summary>
        public static List<FoodPolicy> GetAvailableFoodRestrictions()
        {
            if (Current.Game?.foodRestrictionDatabase == null)
                return new List<FoodPolicy>();

            return Current.Game.foodRestrictionDatabase.AllFoodRestrictions.ToList();
        }

        /// <summary>Assigns a food policy; false when the pawn has no tracker.</summary>
        public static bool SetFoodRestriction(Pawn pawn, FoodPolicy restriction)
        {
            if (pawn?.foodRestriction == null)
                return false;

            // MUTATION-C: mirrors HealthCardUtility.cs:478's "Allow food" float-menu
            // action; CurrentFoodPolicy is a bare property with no gated setter
            // (Pawn_FoodRestrictionTracker.cs:28).
            pawn.foodRestriction.CurrentFoodPolicy = restriction;
            return true;
        }

        /// <summary>
        /// Vanilla's own condition for drawing the "Allow food" row. Whether the control is
        /// drawn is the validation, so Health Settings must omit this row exactly where a
        /// sighted player never sees it: babies, non-Configurable pawns, and mutants whose
        /// MutantDef disables policies.
        /// </summary>
        public static bool CanConfigureFoodRestriction(Pawn pawn)
        {
            return pawn?.foodRestriction != null
                && pawn.foodRestriction.Configurable
                && !pawn.DevelopmentalStage.Baby()
                && pawn.needs?.food != null
                && (!pawn.IsMutant || !pawn.mutant.Def.disablePolicies);
        }

        /// <summary>The pawn's current medical care label, or "none".</summary>
        public static string GetCurrentMedicalCare(Pawn pawn)
        {
            if (pawn?.playerSettings == null)
                return "NoneLower".Translate();

            return pawn.playerSettings.medCare.GetLabel();
        }

        /// <summary>Every medical care category.</summary>
        public static List<MedicalCareCategory> GetAvailableMedicalCare()
        {
            return Enum.GetValues(typeof(MedicalCareCategory))
                .Cast<MedicalCareCategory>()
                .ToList();
        }

        /// <summary>Assigns medical care; false when the pawn has no player settings.</summary>
        public static bool SetMedicalCare(Pawn pawn, MedicalCareCategory care)
        {
            if (pawn?.playerSettings == null)
                return false;

            pawn.playerSettings.medCare = care;
            return true;
        }

        /// <summary>Whether self-tend is on.</summary>
        public static bool GetSelfTendEnabled(Pawn pawn)
        {
            if (pawn?.playerSettings == null)
                return false;

            return pawn.playerSettings.selfTend;
        }

        /// <summary>Flips self-tend; false when the pawn has no player settings.</summary>
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
        /// Every capacity vanilla would show for this pawn, in its listOrder, with the
        /// pawn-type-specific label ("Data processing" for mechs).
        /// </summary>
        public static List<CapacityInfo> GetCapacities(Pawn pawn)
        {
            var capacities = new List<CapacityInfo>();

            if (pawn?.health?.capacities == null || pawn.Dead)
                return capacities;

            var visibleCapacities = DefDatabase<PawnCapacityDef>.AllDefs
                .Where(cap => cap.CanShowOnPawn(pawn)
                    && PawnCapacityUtility.BodyCanEverDoCapacity(pawn.RaceProps.body, cap))
                .OrderBy(cap => cap.listOrder);

            foreach (var capacityDef in visibleCapacities)
            {
                float level = pawn.health.capacities.GetLevel(capacityDef);
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

        /// <summary>Capacity level as vanilla's efficiency estimate plus the percentage.</summary>
        private static string GetCapacityLevelLabel(float level)
        {
            var estimate = HealthCardUtility.EfficiencyValueToEstimate(level);
            string translatedLabel = estimate.ToString().Translate();
            return $"{translatedLabel}, {level:P0}";
        }

        /// <summary>
        /// What affects a capacity, in vanilla's GetPawnCapacityTip format with impactors
        /// grouped by type.
        /// </summary>
        private static string GetCapacityBreakdown(Pawn pawn, PawnCapacityDef capacity)
        {
            var impactors = new List<PawnCapacityUtility.CapacityImpactor>();
            PawnCapacityUtility.CalculateCapacityLevel(
                pawn.health.hediffSet,
                capacity,
                impactors
            );

            impactors.RemoveAll(x =>
                x is PawnCapacityUtility.CapacityImpactorCapacity capImpactor
                && !capImpactor.capacity.CanShowOnPawn(pawn));

            if (impactors.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("AffectedBy".Translate().ToString());

            // Vanilla's grouping order: hediffs, body parts, genes, capacities, pain.
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

        /// <summary>The pawn's queued medical bills.</summary>
        public static List<Bill> GetQueuedOperations(Pawn pawn)
        {
            if (pawn?.BillStack == null)
                return new List<Bill>();

            return pawn.BillStack.Bills.ToList();
        }

        /// <summary>
        /// The operations vanilla would offer for a pawn, with its own ingredient-aware
        /// filtering from HealthCardUtility.DrawMedOperationsTab.
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

                if (pawn.MapHeld != null)
                {
                    var missing = recipe.PotentiallyMissingIngredients(null, pawn.MapHeld);
                    if (missing.Any(x => x.isTechHediff) || missing.Any(x => x.IsDrug))
                        continue;
                    if (missing.Any() && recipe.dontShowIfAnyIngredientMissing)
                        continue;
                }

                if (!recipe.targetsBodyPart && recipe.addsHediff != null
                    && pawn.health.hediffSet.HasHediff(recipe.addsHediff))
                    continue;

                recipes.Add(recipe);
            }

            return recipes;
        }

        /// <summary>
        /// The body parts a recipe applies to, empty when it targets none.
        /// </summary>
        public static List<BodyPartRecord> GetPartsForRecipe(Pawn pawn, RecipeDef recipe)
        {
            var parts = new List<BodyPartRecord>();

            if (pawn?.health == null || recipe == null)
                return parts;

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

        /// <summary>Queues a medical bill for a recipe and body part.</summary>
        public static bool AddOperation(Pawn pawn, RecipeDef recipe, BodyPartRecord part)
        {
            if (pawn?.BillStack == null)
                return false;

            Bill_Medical bill = new Bill_Medical(recipe, null);
            pawn.BillStack.AddBill(bill);
            bill.Part = part;
            return true;
        }

        /// <summary>Removes a queued medical bill.</summary>
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
        /// Vanilla's health-card hover tooltip for one hediff, split into lines. The header is
        /// always its first line ("Label: severity"); the returned lines are everything after it.
        /// </summary>
        public static List<string> GetHediffTooltipLines(Hediff hediff, Pawn pawn, out string header)
        {
            header = null;
            var lines = new List<string>();
            if (hediff == null)
                return lines;

            if (hediff.IsCurrentlyLifeThreatening)
                lines.Add(LifeThreateningFlag());

            string[] tipLines = hediff.GetTooltip(pawn, false).Replace("\r", "").Split('\n');
            header = tipLines[0].StripTags().Trim();
            for (int i = 1; i < tipLines.Length; i++)
            {
                string line = tipLines[i].StripTags().Trim();
                if (line.Length > 0)
                    lines.Add(line);
            }
            return lines;
        }

        private static string LifeThreateningFlag()
        {
            return "PawnsWithLifeThreateningDisease".Translate().ToString().ToUpper();
        }

        /// <summary>Tooltip header, life-threatening flag, then the immunity and tending comps' tooltip lines.</summary>
        public static List<string> GetHediffSummaryLines(Hediff hediff, Pawn pawn)
        {
            var lines = new List<string>();
            if (hediff == null)
                return lines;

            GetHediffTooltipLines(hediff, pawn, out string header);
            lines.Add(string.IsNullOrEmpty(header) ? hediff.LabelCap.StripTags() : header);
            if (hediff.IsCurrentlyLifeThreatening)
                lines.Add(LifeThreateningFlag());
            AddCompTipLines(lines, hediff.TryGetComp<HediffComp_Immunizable>());
            AddCompTipLines(lines, hediff.TryGetComp<HediffComp_TendDuration>());
            return lines;
        }

        private static void AddCompTipLines(List<string> lines, HediffComp comp)
        {
            string tip = comp?.CompTipStringExtra;
            if (string.IsNullOrEmpty(tip))
                return;
            foreach (string raw in tip.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.StripTags().Trim();
                if (line.Length > 0)
                    lines.Add(line);
            }
        }

        /// <summary>
        /// The hediff's DebugString content — hidden flag, exact severity, tend priority,
        /// subclass detail — as one localized sentence, under vanilla's own health-card debug
        /// gate; null when the gate is closed or there is nothing to show. It does not overlap
        /// TipStringExtra, so callers append it rather than deduplicating.
        /// </summary>
        public static string GetHediffDeveloperInfo(Hediff hediff)
        {
            if (hediff == null || !Prefs.DevMode || Current.ProgramState != ProgramState.Playing)
                return null;

            string debug = hediff.DebugString();
            if (string.IsNullOrWhiteSpace(debug))
                return null;

            string flattened = string.Join(". ", debug
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
            if (string.IsNullOrEmpty(flattened))
                return null;

            return "RimWorldAccess.Dev.Info.Hediff".Translate(flattened);
        }

        /// <summary>
        /// Vanilla's pain label, qualitative plus percentage; null for non-flesh pawns.
        /// Vanilla draws this row for every flesh pawn, "None" included, so zero pain is a
        /// value rather than an absence.
        /// </summary>
        public static string GetPainLabel(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || !pawn.def.race.IsFlesh)
                return null;

            float painTotal = pawn.health.hediffSet.PainTotal;

            string qualitative;
            if (Mathf.Approximately(painTotal, 0f))
                qualitative = "NoPain".Translate();
            else if (painTotal < 0.15f)
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
        /// Bleeding rate with time to death, or null when the pawn is not bleeding.
        /// </summary>
        public static string GetBleedingLabel(Pawn pawn)
        {
            BleedOutKind bleeding = PawnHealthSummary.BleedOut(pawn, out int ticksUntilDeath);
            if (bleeding == BleedOutKind.None)
                return null;

            float bleedRate = pawn.health.hediffSet.BleedRateTotal;
            string label = $"{"BleedingRate".Translate()}: {bleedRate.ToStringPercent()}/{"LetterDay".Translate()}";

            if (bleeding == BleedOutKind.Deathless)
                label += $" ({"Deathless".Translate()})";
            else if (bleeding == BleedOutKind.NotSoon)
                label += $" ({"WontBleedOutSoon".Translate()})";
            else
                label += $" ({"TimeToDeath".Translate(ticksUntilDeath.ToStringTicksToPeriod())})";

            return label;
        }

        /// <summary>
        /// Visible hediffs: missing parts through vanilla's common-ancestor logic, which
        /// already drops bionic-replaced parts, plus every other visible non-MissingPart one.
        /// </summary>
        public static IEnumerable<Hediff> GetVisibleHediffs(Pawn pawn, bool showBloodLoss = true)
        {
            if (pawn?.health?.hediffSet == null)
                yield break;

            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            for (int i = 0; i < missingParts.Count; i++)
            {
                yield return missingParts[i];
            }

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
        /// Vanilla's GetListPriority sort key for a body part; higher sorts first, and the
        /// whole body (null) sorts highest.
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
