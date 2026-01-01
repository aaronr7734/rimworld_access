using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for extracting prisoner and slave management information.
    /// Provides methods to get prisoner stats, interaction modes, and colony capabilities.
    /// </summary>
    public static class PrisonerTabHelper
    {
        /// <summary>
        /// Gets comprehensive prisoner information including stats, resistance, and potential outcomes.
        /// </summary>
        public static string GetPrisonerInfo(Pawn pawn)
        {
            if (pawn == null)
                return "No pawn selected";

            if (pawn.guest == null)
                return $"{pawn.LabelShort}: No guest tracker";

            if (!pawn.IsPrisonerOfColony)
                return $"{pawn.LabelShort}: Not a prisoner";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} - Prisoner");

            bool wildMan = pawn.IsWildMan();

            // Prison Break MTB
            StringBuilder prisonBreakExplanation = new StringBuilder();
            int prisonBreakMtb = (int)PrisonBreakUtility.InitiatePrisonBreakMtbDays(pawn, prisonBreakExplanation, ignoreAsleep: true);

            if (PrisonBreakUtility.IsPrisonBreaking(pawn))
            {
                sb.AppendLine($"\nPrison Break: Currently Prison Breaking!");
            }
            else if (prisonBreakMtb < 0)
            {
                sb.AppendLine($"\nPrison Break MTB: Never");
                if (PrisonBreakUtility.GenePreventsPrisonBreaking(pawn, out var gene))
                {
                    sb.AppendLine($"  (Prevented by gene: {gene.def.LabelCap})");
                }
            }
            else
            {
                sb.AppendLine($"\nPrison Break MTB: {prisonBreakMtb} days");
            }

            if (!wildMan)
            {
                // Recruitment Resistance
                if (pawn.guest.Recruitable)
                {
                    float resistance = (pawn.guest.resistance > 0f) ? System.Math.Max(0.1f, pawn.guest.resistance) : 0f;
                    sb.AppendLine($"Recruitment Resistance: {resistance:F1}");

                    // Add resistance range info
                    var resistanceRange = pawn.kindDef.initialResistanceRange;
                    if (resistanceRange != null)
                    {
                        sb.AppendLine($"  (Initial range: {resistanceRange.Value.min}~{resistanceRange.Value.max})");
                    }
                }
                else
                {
                    sb.AppendLine($"Recruitment: Non-Recruitable");
                }

                // Will Level (Ideology DLC)
                if (ModsConfig.IdeologyActive)
                {
                    sb.AppendLine($"Will Level: {pawn.guest.will:F1}");
                    if (!pawn.guest.EverEnslaved)
                    {
                        var willRange = pawn.kindDef.initialWillRange;
                        if (willRange != null)
                        {
                            sb.AppendLine($"  (Initial range: {willRange.Value.min}~{willRange.Value.max})");
                        }
                    }
                }
            }

            // Slave Price
            float marketValue = pawn.GetStatValue(StatDefOf.MarketValue);
            sb.AppendLine($"Slave Price: {marketValue:F0} silver");

            // Study Info (Anomaly DLC)
            if (IsStudiable(pawn))
            {
                var compStudiable = pawn.TryGetComp<CompStudiable>();
                if (compStudiable != null)
                {
                    sb.AppendLine($"Studiable: Yes");
                }
            }

            // Release Potential Relations
            string releaseRelations = GetReleaseRelationGainsText(pawn);
            sb.AppendLine($"Release Relation Gains: {releaseRelations}");

            // Guilty Status
            if (pawn.guilt.IsGuilty)
            {
                if (!pawn.InAggroMentalState)
                {
                    string timeUntilInnocent = pawn.guilt.TicksUntilInnocent.ToStringTicksToPeriod();
                    sb.AppendLine($"Guilty: {timeUntilInnocent} until innocent");
                }
                else
                {
                    sb.AppendLine($"Guilty: No timer (in {pawn.MentalStateDef.label})");
                }
            }

            // Ideology Conversion Target
            if (ModsConfig.IdeologyActive && pawn.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Convert) && pawn.guest.ideoForConversion != null)
            {
                sb.AppendLine($"Ideo Conversion Target: {pawn.guest.ideoForConversion.name}");
            }

            // Last Recruitment Stats
            if (pawn.guest.finalResistanceInteractionData != null)
            {
                var data = pawn.guest.finalResistanceInteractionData;
                sb.AppendLine($"\nLast Recruitment:");
                sb.AppendLine($"  Resistance Reduction: {data.resistanceReduction:F2}");
                sb.AppendLine($"  Recruiter: {data.initiatorName}");
                sb.AppendLine($"  Mood Factor: x{data.recruiteeMoodFactor:F2}");
                sb.AppendLine($"  Negotiation Ability: x{data.initiatorNegotiationAbilityFactor:F2}");
                sb.AppendLine($"  Opinion Factor: x{data.recruiterOpinionFactor:F2}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets comprehensive slave information including suppression, terror, and rebellion likelihood.
        /// </summary>
        public static string GetSlaveInfo(Pawn pawn)
        {
            if (pawn == null)
                return "No pawn selected";

            if (pawn.guest == null)
                return $"{pawn.LabelShort}: No guest tracker";

            if (!pawn.IsSlaveOfColony)
                return $"{pawn.LabelShort}: Not a slave";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{pawn.LabelShort} - Slave");

            // Suppression
            if (pawn.needs.TryGetNeed(out Need_Suppression suppressionNeed))
            {
                float suppressionLevel = suppressionNeed.CurLevel;
                sb.AppendLine($"\nSuppression: {suppressionLevel:P0}");
            }

            // Suppression Fall Rate
            float fallRate = pawn.GetStatValue(StatDefOf.SlaveSuppressionFallRate);
            sb.AppendLine($"Suppression Fall Rate: {fallRate:P1} per day");

            // Terror
            float terror = pawn.GetStatValue(StatDefOf.Terror);
            sb.AppendLine($"Terror: {terror:P0}");

            // Slave Rebellion MTB
            float rebellionMtb = SlaveRebellionUtility.InitiateSlaveRebellionMtbDays(pawn);
            if (!pawn.Awake())
            {
                sb.AppendLine($"Slave Rebellion MTB: Not while asleep");
            }
            else if (rebellionMtb < 0f)
            {
                sb.AppendLine($"Slave Rebellion MTB: Never");
            }
            else
            {
                string period = ((int)(rebellionMtb * 60000f)).ToStringTicksToPeriod();
                sb.AppendLine($"Slave Rebellion MTB: {period}");
            }

            // Slave Price
            float marketValue = pawn.GetStatValue(StatDefOf.MarketValue);
            sb.AppendLine($"Slave Price: {marketValue:F0} silver");

            // Release Potential Relations
            string releaseRelations = GetSlaveReleaseRelationGainsText(pawn);
            sb.AppendLine($"Release Relation Gains: {releaseRelations}");

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets list of available exclusive interaction modes for the prisoner.
        /// </summary>
        public static List<PrisonerInteractionModeDef> GetAvailableExclusiveInteractionModes(Pawn pawn)
        {
            bool wildMan = pawn.IsWildMan();
            return DefDatabase<PrisonerInteractionModeDef>.AllDefs
                .Where(mode => !mode.isNonExclusiveInteraction && CanUsePrisonerInteractionMode(pawn, mode, wildMan))
                .OrderBy(mode => mode.listOrder)
                .ToList();
        }

        /// <summary>
        /// Gets list of available non-exclusive interaction modes for the prisoner.
        /// </summary>
        public static List<PrisonerInteractionModeDef> GetAvailableNonExclusiveInteractionModes(Pawn pawn)
        {
            bool wildMan = pawn.IsWildMan();
            return DefDatabase<PrisonerInteractionModeDef>.AllDefs
                .Where(mode => mode.isNonExclusiveInteraction && CanUsePrisonerInteractionMode(pawn, mode, wildMan))
                .OrderBy(mode => mode.listOrder)
                .ToList();
        }

        /// <summary>
        /// Gets list of available slave interaction modes.
        /// </summary>
        public static List<SlaveInteractionModeDef> GetAvailableSlaveInteractionModes()
        {
            return DefDatabase<SlaveInteractionModeDef>.AllDefs
                .OrderBy(mode => mode.listOrder)
                .ToList();
        }

        /// <summary>
        /// Gets the label for a medical care level.
        /// </summary>
        public static string GetMedicalCareLabel(MedicalCareCategory category)
        {
            switch (category)
            {
                case MedicalCareCategory.NoCare:
                    return "No Care";
                case MedicalCareCategory.NoMeds:
                    return "No Meds";
                case MedicalCareCategory.HerbalOrWorse:
                    return "Herbal or Worse";
                case MedicalCareCategory.NormalOrWorse:
                    return "Normal or Worse";
                case MedicalCareCategory.Best:
                    return "Best";
                default:
                    return category.ToString();
            }
        }

        /// <summary>
        /// Gets the next medical care level (cycles through all levels).
        /// </summary>
        public static MedicalCareCategory GetNextMedicalCare(MedicalCareCategory current)
        {
            switch (current)
            {
                case MedicalCareCategory.NoCare:
                    return MedicalCareCategory.NoMeds;
                case MedicalCareCategory.NoMeds:
                    return MedicalCareCategory.HerbalOrWorse;
                case MedicalCareCategory.HerbalOrWorse:
                    return MedicalCareCategory.NormalOrWorse;
                case MedicalCareCategory.NormalOrWorse:
                    return MedicalCareCategory.Best;
                case MedicalCareCategory.Best:
                    return MedicalCareCategory.NoCare;
                default:
                    return MedicalCareCategory.Best;
            }
        }

        /// <summary>
        /// Gets the previous medical care level (cycles through all levels).
        /// </summary>
        public static MedicalCareCategory GetPreviousMedicalCare(MedicalCareCategory current)
        {
            switch (current)
            {
                case MedicalCareCategory.NoCare:
                    return MedicalCareCategory.Best;
                case MedicalCareCategory.NoMeds:
                    return MedicalCareCategory.NoCare;
                case MedicalCareCategory.HerbalOrWorse:
                    return MedicalCareCategory.NoMeds;
                case MedicalCareCategory.NormalOrWorse:
                    return MedicalCareCategory.HerbalOrWorse;
                case MedicalCareCategory.Best:
                    return MedicalCareCategory.NormalOrWorse;
                default:
                    return MedicalCareCategory.NoCare;
            }
        }

        /// <summary>
        /// Gets a description of the interaction mode with warnings if needed.
        /// </summary>
        public static string GetInteractionModeDescription(Pawn pawn, PrisonerInteractionModeDef mode)
        {
            string description = mode.description ?? mode.LabelCap;

            // Add warnings
            if (mode == PrisonerInteractionModeDefOf.Enslave && pawn.MapHeld != null && !ColonyHasAnyWardenCapableOfEnslavement(pawn.MapHeld))
            {
                description += "\n[WARNING: No warden capable of enslavement]";
            }

            if (mode == PrisonerInteractionModeDefOf.Execution && pawn.MapHeld != null && !ColonyHasAnyWardenCapableOfViolence(pawn.MapHeld))
            {
                description += "\n[WARNING: No warden capable of violence]";
            }

            if (mode == PrisonerInteractionModeDefOf.Convert && pawn.guest.ideoForConversion != null && pawn.MapHeld != null && !ColonyHasAnyWardenOfIdeo(pawn.guest.ideoForConversion, pawn.MapHeld))
            {
                description += $"\n[WARNING: No warden of {pawn.guest.ideoForConversion.name}]";
            }

            return description;
        }

        /// <summary>
        /// Gets a description of the slave interaction mode.
        /// </summary>
        public static string GetSlaveInteractionModeDescription(Pawn pawn, SlaveInteractionModeDef mode)
        {
            string description = mode.description ?? mode.LabelCap;

            // Add specific emancipation info
            if (mode == SlaveInteractionModeDefOf.Emancipate)
            {
                if (pawn.SlaveFaction == Faction.OfPlayer)
                {
                    description += " (Will become colonist)";
                }
                else if (pawn.SlaveFaction == null)
                {
                    description += " (Will become free, no faction)";
                }
                else
                {
                    description += $" (Will return to {pawn.SlaveFaction.Name})";
                }
            }

            return description;
        }

        /// <summary>
        /// Gets list of all player ideologies for conversion selection.
        /// </summary>
        public static List<Ideo> GetPlayerIdeologies()
        {
            if (!ModsConfig.IdeologyActive || Faction.OfPlayer.ideos == null)
                return new List<Ideo>();

            return Faction.OfPlayer.ideos.AllIdeos.ToList();
        }

        #region Private Helper Methods

        private static bool CanUsePrisonerInteractionMode(Pawn pawn, PrisonerInteractionModeDef mode, bool wildMan)
        {
            if (!pawn.guest.Recruitable && mode.hideIfNotRecruitable)
            {
                return false;
            }
            if (wildMan && !mode.allowOnWildMan)
            {
                return false;
            }
            if (mode.hideIfNoBloodfeeders && pawn.MapHeld != null && !ColonyHasAnyBloodfeeder(pawn.MapHeld))
            {
                return false;
            }
            if (mode.hideOnHemogenicPawns && ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.HasActiveGene(GeneDefOf.Hemogenic))
            {
                return false;
            }
            if (!mode.allowInClassicIdeoMode && Find.IdeoManager.classicMode)
            {
                return false;
            }
            if (ModsConfig.AnomalyActive)
            {
                if (mode.hideIfNotStudiableAsPrisoner && !IsStudiable(pawn))
                {
                    return false;
                }
                if (mode.hideIfGrayFleshNotAppeared && !Find.Anomaly.hasSeenGrayFlesh)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsStudiable(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive)
            {
                return false;
            }
            if (!pawn.TryGetComp<CompStudiable>(out var comp) || !comp.EverStudiable())
            {
                return false;
            }
            if (pawn.kindDef.studiableAsPrisoner)
            {
                return !pawn.everLostEgo;
            }
            return false;
        }

        private static string GetReleaseRelationGainsText(Pawn pawn)
        {
            if (pawn.Faction == null || pawn.Faction.IsPlayer || !pawn.Faction.CanChangeGoodwillFor(Faction.OfPlayer, 1))
            {
                return "None";
            }

            bool isHealthy;
            bool isInMentalState;
            int goodwillChange = pawn.Faction.CalculateAdjustedGoodwillChange(
                Faction.OfPlayer,
                pawn.Faction.GetGoodwillGainForExit(pawn, freed: true, out isHealthy, out isInMentalState));

            if (isHealthy && !isInMentalState)
            {
                return $"{pawn.Faction.Name} {goodwillChange:+0;-0}";
            }
            else if (!isHealthy)
            {
                return "None (Untended Injury)";
            }
            else
            {
                return $"None ({pawn.MentalState.InspectLine})";
            }
        }

        private static string GetSlaveReleaseRelationGainsText(Pawn pawn)
        {
            Faction faction = pawn.SlaveFaction ?? pawn.Faction;

            if (faction == null || faction.IsPlayer || !faction.CanChangeGoodwillFor(Faction.OfPlayer, 1))
            {
                return "None";
            }

            bool isHealthy;
            bool isInMentalState;
            int goodwillChange = faction.CalculateAdjustedGoodwillChange(
                Faction.OfPlayer,
                faction.GetGoodwillGainForExit(pawn, freed: true, out isHealthy, out isInMentalState));

            if (isHealthy && !isInMentalState)
            {
                return $"{faction.Name} {goodwillChange:+0;-0}";
            }
            else if (!isHealthy)
            {
                return "None (Untended Injury)";
            }
            else
            {
                return $"None ({pawn.MentalState.InspectLine})";
            }
        }

        private static bool ColonyHasAnyBloodfeeder(Map map)
        {
            if (!ModsConfig.BiotechActive)
                return false;

            foreach (Pawn colonist in map.mapPawns.FreeColonistsAndPrisonersSpawned)
            {
                if (colonist.IsBloodfeeder())
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ColonyHasAnyWardenCapableOfViolence(Map map)
        {
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.workSettings.WorkIsActive(WorkTypeDefOf.Warden) && !colonist.WorkTagIsDisabled(WorkTags.Violent))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ColonyHasAnyWardenCapableOfEnslavement(Map map)
        {
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.workSettings.WorkIsActive(WorkTypeDefOf.Warden) &&
                    new HistoryEvent(HistoryEventDefOf.EnslavedPrisoner, colonist.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ColonyHasAnyWardenOfIdeo(Ideo ideo, Map map)
        {
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.workSettings.WorkIsActive(WorkTypeDefOf.Warden) && colonist.Ideo == ideo)
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
