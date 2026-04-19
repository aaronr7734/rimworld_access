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
                return "RimWorldAccess.Guard.NoPawnSelected".Translate();

            if (pawn.guest == null)
                return "RimWorldAccess.Prisoner.Guest.NoTracker".Translate(pawn.LabelShort);

            if (!pawn.IsPrisonerOfColony)
                return "RimWorldAccess.Prisoner.Guest.NotPrisoner".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Prisoner.Info.HeaderPrisoner".Translate(pawn.LabelShort));

            bool wildMan = pawn.IsWildMan();

            // Prison Break MTB
            StringBuilder prisonBreakExplanation = new StringBuilder();
            int prisonBreakMtb = (int)PrisonBreakUtility.InitiatePrisonBreakMtbDays(pawn, prisonBreakExplanation, ignoreAsleep: true);

            if (PrisonBreakUtility.IsPrisonBreaking(pawn))
            {
                sb.AppendLine();
                sb.AppendLine("RimWorldAccess.Prisoner.Info.PrisonBreakInProgress".Translate());
            }
            else if (prisonBreakMtb < 0)
            {
                sb.AppendLine();
                sb.AppendLine("RimWorldAccess.Prisoner.Info.PrisonBreakNever".Translate());
                if (PrisonBreakUtility.GenePreventsPrisonBreaking(pawn, out var gene))
                {
                    sb.AppendLine("RimWorldAccess.Prisoner.Info.PrisonBreakPreventedByGene".Translate(gene.def.LabelCap));
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("RimWorldAccess.Prisoner.Info.PrisonBreakDays".Translate(prisonBreakMtb));
            }

            if (!wildMan)
            {
                // Recruitment Resistance
                if (pawn.guest.Recruitable)
                {
                    float resistance = (pawn.guest.resistance > 0f) ? System.Math.Max(0.1f, pawn.guest.resistance) : 0f;
                    sb.AppendLine("RimWorldAccess.Prisoner.Info.Resistance".Translate(resistance.ToString("F1")));

                    // Add resistance range info
                    var resistanceRange = pawn.kindDef.initialResistanceRange;
                    if (resistanceRange != null)
                    {
                        sb.AppendLine("RimWorldAccess.Prisoner.Info.ResistanceInitialRange".Translate(resistanceRange.Value.min, resistanceRange.Value.max));
                    }
                }
                else
                {
                    sb.AppendLine("RimWorldAccess.Prisoner.Info.NonRecruitable".Translate());
                }

                // Will Level (Ideology DLC)
                if (ModsConfig.IdeologyActive)
                {
                    sb.AppendLine("RimWorldAccess.Prisoner.Info.WillLevel".Translate(pawn.guest.will.ToString("F1")));
                    if (!pawn.guest.EverEnslaved)
                    {
                        var willRange = pawn.kindDef.initialWillRange;
                        if (willRange != null)
                        {
                            sb.AppendLine("RimWorldAccess.Prisoner.Info.WillInitialRange".Translate(willRange.Value.min, willRange.Value.max));
                        }
                    }
                }
            }

            // Slave Price
            float marketValue = pawn.GetStatValue(StatDefOf.MarketValue);
            sb.AppendLine("RimWorldAccess.Prisoner.Info.SlavePrice".Translate(marketValue.ToString("F0")));

            // Study Info (Anomaly DLC)
            if (IsStudiable(pawn))
            {
                var compStudiable = pawn.TryGetComp<CompStudiable>();
                if (compStudiable != null)
                {
                    sb.AppendLine("RimWorldAccess.Prisoner.Info.StudiableYes".Translate());
                }
            }

            // Release Potential Relations
            string releaseRelations = GetReleaseRelationGainsText(pawn);
            sb.AppendLine("RimWorldAccess.Prisoner.Info.ReleaseRelationGains".Translate(releaseRelations));

            // Guilty Status
            if (pawn.guilt.IsGuilty)
            {
                if (!pawn.InAggroMentalState)
                {
                    string timeUntilInnocent = pawn.guilt.TicksUntilInnocent.ToStringTicksToPeriod();
                    sb.AppendLine("RimWorldAccess.Prisoner.Info.GuiltyUntilInnocent".Translate(timeUntilInnocent));
                }
                else
                {
                    sb.AppendLine("RimWorldAccess.Prisoner.Info.GuiltyNoTimer".Translate(pawn.MentalStateDef.label));
                }
            }

            // Ideology Conversion Target
            if (ModsConfig.IdeologyActive && pawn.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Convert) && pawn.guest.ideoForConversion != null)
            {
                sb.AppendLine("RimWorldAccess.Prisoner.Info.IdeoConversionTarget".Translate(pawn.guest.ideoForConversion.name));
            }

            // Last Recruitment Stats
            if (pawn.guest.finalResistanceInteractionData != null)
            {
                var data = pawn.guest.finalResistanceInteractionData;
                sb.AppendLine();
                sb.AppendLine("RimWorldAccess.Prisoner.Info.LastRecruitmentHeader".Translate());
                sb.AppendLine("RimWorldAccess.Prisoner.Info.LastRecruitmentResistance".Translate(data.resistanceReduction.ToString("F2")));
                sb.AppendLine("RimWorldAccess.Prisoner.Info.LastRecruitmentRecruiter".Translate(data.initiatorName));
                sb.AppendLine("RimWorldAccess.Prisoner.Info.LastRecruitmentMoodFactor".Translate(data.recruiteeMoodFactor.ToString("F2")));
                sb.AppendLine("RimWorldAccess.Prisoner.Info.LastRecruitmentNegotiation".Translate(data.initiatorNegotiationAbilityFactor.ToString("F2")));
                sb.AppendLine("RimWorldAccess.Prisoner.Info.LastRecruitmentOpinion".Translate(data.recruiterOpinionFactor.ToString("F2")));
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets comprehensive slave information including suppression, terror, and rebellion likelihood.
        /// </summary>
        public static string GetSlaveInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Guard.NoPawnSelected".Translate();

            if (pawn.guest == null)
                return "RimWorldAccess.Prisoner.Guest.NoTracker".Translate(pawn.LabelShort);

            if (!pawn.IsSlaveOfColony)
                return "RimWorldAccess.Prisoner.Guest.NotSlave".Translate(pawn.LabelShort);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("RimWorldAccess.Prisoner.Info.HeaderSlave".Translate(pawn.LabelShort));

            // Suppression
            if (pawn.needs.TryGetNeed(out Need_Suppression suppressionNeed))
            {
                float suppressionLevel = suppressionNeed.CurLevel;
                sb.AppendLine();
                sb.AppendLine("RimWorldAccess.Prisoner.Slave.Suppression".Translate(suppressionLevel.ToString("P0")));
            }

            // Suppression Fall Rate
            float fallRate = pawn.GetStatValue(StatDefOf.SlaveSuppressionFallRate);
            sb.AppendLine("RimWorldAccess.Prisoner.Slave.SuppressionFallRate".Translate(fallRate.ToString("P1")));

            // Terror
            float terror = pawn.GetStatValue(StatDefOf.Terror);
            sb.AppendLine("RimWorldAccess.Prisoner.Slave.Terror".Translate(terror.ToString("P0")));

            // Slave Rebellion MTB
            float rebellionMtb = SlaveRebellionUtility.InitiateSlaveRebellionMtbDays(pawn);
            if (!pawn.Awake())
            {
                sb.AppendLine("RimWorldAccess.Prisoner.Slave.RebellionAsleep".Translate());
            }
            else if (rebellionMtb < 0f)
            {
                sb.AppendLine("RimWorldAccess.Prisoner.Slave.RebellionNever".Translate());
            }
            else
            {
                string period = ((int)(rebellionMtb * 60000f)).ToStringTicksToPeriod();
                sb.AppendLine("RimWorldAccess.Prisoner.Slave.RebellionPeriod".Translate(period));
            }

            // Slave Price
            float marketValue = pawn.GetStatValue(StatDefOf.MarketValue);
            sb.AppendLine("RimWorldAccess.Prisoner.Info.SlavePrice".Translate(marketValue.ToString("F0")));

            // Release Potential Relations
            string releaseRelations = GetSlaveReleaseRelationGainsText(pawn);
            sb.AppendLine("RimWorldAccess.Prisoner.Info.ReleaseRelationGains".Translate(releaseRelations));

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
                    return "RimWorldAccess.Prisoner.MedicalCare.NoCare".Translate();
                case MedicalCareCategory.NoMeds:
                    return "RimWorldAccess.Prisoner.MedicalCare.NoMeds".Translate();
                case MedicalCareCategory.HerbalOrWorse:
                    return "RimWorldAccess.Prisoner.MedicalCare.HerbalOrWorse".Translate();
                case MedicalCareCategory.NormalOrWorse:
                    return "RimWorldAccess.Prisoner.MedicalCare.NormalOrWorse".Translate();
                case MedicalCareCategory.Best:
                    return "RimWorldAccess.Prisoner.MedicalCare.Best".Translate();
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
                description += "RimWorldAccess.Prisoner.Warning.NoEnslavementWarden".Translate();
            }

            if (mode == PrisonerInteractionModeDefOf.Execution && pawn.MapHeld != null && !ColonyHasAnyWardenCapableOfViolence(pawn.MapHeld))
            {
                description += "RimWorldAccess.Prisoner.Warning.NoViolenceWarden".Translate();
            }

            if (mode == PrisonerInteractionModeDefOf.Convert && pawn.guest.ideoForConversion != null && pawn.MapHeld != null && !ColonyHasAnyWardenOfIdeo(pawn.guest.ideoForConversion, pawn.MapHeld))
            {
                description += "RimWorldAccess.Prisoner.Warning.NoIdeoWarden".Translate(pawn.guest.ideoForConversion.name);
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
                    description += "RimWorldAccess.Prisoner.Emancipate.BecomeColonist".Translate();
                }
                else if (pawn.SlaveFaction == null)
                {
                    description += "RimWorldAccess.Prisoner.Emancipate.BecomeFreeNoFaction".Translate();
                }
                else
                {
                    description += "RimWorldAccess.Prisoner.Emancipate.ReturnToFaction".Translate(pawn.SlaveFaction.Name);
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
                return "RimWorldAccess.Prisoner.Release.None".Translate();
            }

            bool isHealthy;
            bool isInMentalState;
            int goodwillChange = pawn.Faction.CalculateAdjustedGoodwillChange(
                Faction.OfPlayer,
                pawn.Faction.GetGoodwillGainForExit(pawn, freed: true, out isHealthy, out isInMentalState));

            if (isHealthy && !isInMentalState)
            {
                return "RimWorldAccess.Prisoner.Release.FactionChange".Translate(pawn.Faction.Name, goodwillChange.ToString("+0;-0"));
            }
            else if (!isHealthy)
            {
                return "RimWorldAccess.Prisoner.Release.NoneUntended".Translate();
            }
            else
            {
                return "RimWorldAccess.Prisoner.Release.NoneMentalState".Translate(pawn.MentalState.InspectLine);
            }
        }

        private static string GetSlaveReleaseRelationGainsText(Pawn pawn)
        {
            Faction faction = pawn.SlaveFaction ?? pawn.Faction;

            if (faction == null || faction.IsPlayer || !faction.CanChangeGoodwillFor(Faction.OfPlayer, 1))
            {
                return "RimWorldAccess.Prisoner.Release.None".Translate();
            }

            bool isHealthy;
            bool isInMentalState;
            int goodwillChange = faction.CalculateAdjustedGoodwillChange(
                Faction.OfPlayer,
                faction.GetGoodwillGainForExit(pawn, freed: true, out isHealthy, out isInMentalState));

            if (isHealthy && !isInMentalState)
            {
                return "RimWorldAccess.Prisoner.Release.FactionChange".Translate(faction.Name, goodwillChange.ToString("+0;-0"));
            }
            else if (!isHealthy)
            {
                return "RimWorldAccess.Prisoner.Release.NoneUntended".Translate();
            }
            else
            {
                return "RimWorldAccess.Prisoner.Release.NoneMentalState".Translate(pawn.MentalState.InspectLine);
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
