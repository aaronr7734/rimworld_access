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
    /// Uses vanilla translation keys so screen reader output follows the user's language.
    /// </summary>
    public static class PrisonerTabHelper
    {
        /// <summary>
        /// Comprehensive prisoner readout: stats, resistance, prison-break risk,
        /// release goodwill, guilt timer, last-recruitment breakdown.
        /// Uses vanilla translation keys so screen reader output follows the user's language.
        /// </summary>
        public static string GetPrisonerInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Guard.NoPawnSelected".Translate();

            if (pawn.guest == null)
                return "RimWorldAccess.Prisoner.Guest.NoTracker".Translate(pawn.LabelShort);

            if (!pawn.IsPrisonerOfColony)
                return "RimWorldAccess.Prisoner.Guest.NotPrisoner".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Prisoner.Info.HeaderPrisoner".Translate(pawn.LabelShort));

            bool wildMan = pawn.IsWildMan();

            // Prison Break MTB (vanilla uses "PrisonBreakMTBDays" key)
            string prisonBreakLabel = "PrisonBreakMTBDays".Translate();
            if (PrisonBreakUtility.IsPrisonBreaking(pawn))
            {
                ab.Add($"{prisonBreakLabel}: {"CurrentlyPrisonBreaking".Translate()}");
            }
            else
            {
                int prisonBreakMtb = (int)PrisonBreakUtility.InitiatePrisonBreakMtbDays(pawn, null, ignoreAsleep: true);
                if (prisonBreakMtb < 0)
                {
                    ab.Add($"{prisonBreakLabel}: {"Never".Translate()}");
                    if (PrisonBreakUtility.GenePreventsPrisonBreaking(pawn, out var gene))
                    {
                        ab.Add("PrisonBreakingDisabledDueToGene".Translate(gene.def.Named("GENE")).ToString().StripTags());
                    }
                }
                else
                {
                    ab.Add($"{prisonBreakLabel}: {"PeriodDays".Translate(prisonBreakMtb)}");
                }
            }

            if (!wildMan)
            {
                string resistanceLabel = "RecruitmentResistance".Translate();
                if (pawn.guest.Recruitable)
                {
                    float resistance = (pawn.guest.resistance > 0f) ? System.Math.Max(0.1f, pawn.guest.resistance) : 0f;
                    ab.Add($"{resistanceLabel}: {resistance:F1}");

                    var resistanceRange = pawn.kindDef.initialResistanceRange;
                    if (resistanceRange != null)
                    {
                        ab.Add($"{"RecruitmentResistanceFromPawnKind".Translate(pawn.kindDef.LabelCap)}: {resistanceRange.Value.min}~{resistanceRange.Value.max}");
                    }

                    // Royalty title recruitment offset (vanilla ITab_Pawn_Visitor line 227-234)
                    if (pawn.royalty != null)
                    {
                        RoyalTitle mostSeniorTitle = pawn.royalty.MostSeniorTitle;
                        if (mostSeniorTitle != null && mostSeniorTitle.def.recruitmentResistanceOffset != 0f)
                        {
                            string sign = mostSeniorTitle.def.recruitmentResistanceOffset > 0f ? "+" : "";
                            ab.Add($"{"RecruitmentResistanceRoyalTitleOffset".Translate(mostSeniorTitle.Label.CapitalizeFirst())}: {sign}{mostSeniorTitle.def.recruitmentResistanceOffset}");
                        }
                    }
                }
                else
                {
                    ab.Add($"{resistanceLabel}: {"NonRecruitable".Translate()}");
                }

                if (ModsConfig.IdeologyActive)
                {
                    ab.Add($"{"WillLevel".Translate()}: {pawn.guest.will:F1}");
                    if (!pawn.guest.EverEnslaved)
                    {
                        var willRange = pawn.kindDef.initialWillRange;
                        if (willRange != null)
                        {
                            ab.Add($"{"WillFromPawnKind".Translate(pawn.kindDef.LabelCap)}: {willRange.Value.min}~{willRange.Value.max}");
                        }
                    }
                }
            }

            // Slave Price (vanilla DoSlavePriceListing)
            float marketValue = pawn.GetStatValue(StatDefOf.MarketValue);
            ab.Add($"{"SlavePrice".Translate()}: {marketValue.ToStringMoney()}");

            // Study info (Anomaly DLC) — vanilla calls ITab_Entity.DoStudyPeriodListing / DoKnowledgeGainListing
            if (IsStudiable(pawn))
            {
                var compStudiable = pawn.TryGetComp<CompStudiable>();
                if (compStudiable != null)
                {
                    AppendStudyInfo(ab, compStudiable);
                }
            }

            // Release Potential Relations (vanilla "PrisonerReleasePotentialRelationGains")
            ab.Add($"{"PrisonerReleasePotentialRelationGains".Translate()}: {GetReleaseRelationGainsText(pawn)}");

            // Guilty Status (vanilla "ConsideredGuilty" / "ConsideredGuiltyNoTimer")
            if (pawn.guilt.IsGuilty)
            {
                if (!pawn.InAggroMentalState)
                {
                    string timeUntilInnocent = pawn.guilt.TicksUntilInnocent.ToStringTicksToPeriod();
                    ab.Add("ConsideredGuilty".Translate(timeUntilInnocent).ToString().StripTags());
                }
                else
                {
                    ab.Add($"{"ConsideredGuiltyNoTimer".Translate()} ({pawn.MentalStateDef.label})");
                }
            }

            // Ideology Conversion Target (vanilla "IdeoConversionTarget")
            if (ModsConfig.IdeologyActive && pawn.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Convert) && pawn.guest.ideoForConversion != null)
            {
                ab.Add($"{"IdeoConversionTarget".Translate()}: {pawn.guest.ideoForConversion.name}");
            }

            // Last Recruitment Stats (vanilla "LastRecruitment", "Mood", "RecruiterNegotiationAbility", "OpinionOfRecruiter")
            if (pawn.guest.finalResistanceInteractionData != null)
            {
                var data = pawn.guest.finalResistanceInteractionData;
                ab.Add($"{"LastRecruitment".Translate()}: {data.resistanceReduction.ToStringByStyle(ToStringStyle.FloatTwo)}");
                ab.Add(data.initiatorName);
                ab.Add($"{"Mood".Translate()}: x{data.recruiteeMoodFactor.ToStringByStyle(ToStringStyle.FloatTwo)}");
                ab.Add($"{"RecruiterNegotiationAbility".Translate()}: x{data.initiatorNegotiationAbilityFactor.ToStringByStyle(ToStringStyle.FloatTwo)}");
                ab.Add($"{"OpinionOfRecruiter".Translate()}: x{data.recruiterOpinionFactor.ToStringByStyle(ToStringStyle.FloatTwo)}");
            }

            return ab.Build();
        }

        /// <summary>
        /// Comprehensive slave readout: suppression, terror, rebellion-MTB.
        /// Each row mirrors a row in vanilla ITab_Pawn_Visitor.DoSlaveTab and appends that row's
        /// hover tooltip (flattened to one line) so screen reader users hear the same explanation a
        /// sighted player would see on mouseover. Uses vanilla translation keys throughout.
        /// </summary>
        public static string GetSlaveInfo(Pawn pawn)
        {
            if (pawn == null)
                return "RimWorldAccess.Guard.NoPawnSelected".Translate();

            if (pawn.guest == null)
                return "RimWorldAccess.Prisoner.Guest.NoTracker".Translate(pawn.LabelShort);

            if (!pawn.IsSlaveOfColony)
                return "RimWorldAccess.Prisoner.Guest.NotSlave".Translate(pawn.LabelShort);

            var ab = new AnnouncementBuilder()
                .Add("RimWorldAccess.Prisoner.Info.HeaderSlave".Translate(pawn.LabelShort));

            // Suppression (vanilla "Suppression", tooltip "SuppressionDesc")
            if (pawn.needs.TryGetNeed(out Need_Suppression suppressionNeed))
            {
                string suppressionTooltip = FlattenTooltip("SuppressionDesc".Translate());
                string row = $"{"Suppression".Translate()}: {suppressionNeed.CurLevel.ToStringPercent()}";
                if (!string.IsNullOrEmpty(suppressionTooltip))
                    row += $". {suppressionTooltip}";
                ab.Add(row);
            }

            // Suppression Fall Rate (vanilla "SuppressionFallRate", tooltip "SuppressionFallRateDesc" + stat explanation)
            float fallRate = pawn.GetStatValue(StatDefOf.SlaveSuppressionFallRate);
            string fallRateTip = "SuppressionFallRateDesc".Translate(
                0.2f.ToStringPercent(), 0.3f.ToStringPercent(), 0.1f.ToStringPercent(),
                0.15f.ToStringPercent(), 0.15f.ToStringPercent(), 0.05f.ToStringPercent(), 0.15f.ToStringPercent());
            string fallRateExplanation = ((StatWorker_SuppressionFallRate)StatDefOf.SlaveSuppressionFallRate.Worker)
                .GetExplanationForTooltip(StatRequest.For(pawn));
            string fallRateTooltip = FlattenTooltip(fallRateTip + "\n" + fallRateExplanation);
            string fallRateRow = $"{"SuppressionFallRate".Translate()}: {StatDefOf.SlaveSuppressionFallRate.ValueToString(fallRate)}";
            if (!string.IsNullOrEmpty(fallRateTooltip))
                fallRateRow += $". {fallRateTooltip}";
            ab.Add(fallRateRow);

            // Terror (vanilla "Terror", tooltip "TerrorDescription" + fall-rate curve + current terror thoughts)
            float terror = pawn.GetStatValue(StatDefOf.Terror);
            string terrorTip = "TerrorDescription".Translate() + ": " + TerrorUtility.SuppressionFallRateOverTerror.Points
                .Select(p => string.Format("- {0} {1}: {2}", "Terror".Translate(), (p.x / 100f).ToStringPercent(), (p.y / 100f).ToStringPercent()))
                .ToLineList();
            var terrorThoughts = TerrorUtility.GetTerrorThoughts(pawn).OrderByDescending(t => t.intensity).ToList();
            if (terrorThoughts.Any())
            {
                string thoughtsList = terrorThoughts.Select(t => $"{t.LabelCap}: {t.intensity}%").ToLineList("- ", capitalizeItems: true);
                terrorTip += "\n" + "TerrorCurrentThoughts".Translate() + ": " + thoughtsList;
            }
            string terrorTooltip = FlattenTooltip(terrorTip);
            string terrorRow = $"{"Terror".Translate()}: {terror.ToStringPercent()}";
            if (!string.IsNullOrEmpty(terrorTooltip))
                terrorRow += $". {terrorTooltip}";
            ab.Add(terrorRow);

            // Slave Rebellion MTB (vanilla "SlaveRebellionMTBDays", tooltip "SlaveRebellionMTBDaysDescription")
            string rebellionLabel = "SlaveRebellionMTBDays".Translate();
            string rebellionValue;
            if (!pawn.Awake())
            {
                rebellionValue = "NotWhileAsleep".Translate();
            }
            else
            {
                float rebellionMtb = SlaveRebellionUtility.InitiateSlaveRebellionMtbDays(pawn);
                rebellionValue = (rebellionMtb < 0f)
                    ? "Never".Translate().ToString()
                    : ((int)(rebellionMtb * 60000f)).ToStringTicksToPeriod();
            }
            string rebellionTooltip = FlattenTooltip("SlaveRebellionMTBDaysDescription".Translate());
            string rebellionRow = $"{rebellionLabel}: {rebellionValue}";
            if (!string.IsNullOrEmpty(rebellionTooltip))
                rebellionRow += $". {rebellionTooltip}";
            ab.Add(rebellionRow);

            // Slave Price (vanilla "SlavePrice", tooltip "SlavePriceDescription")
            float marketValue = pawn.GetStatValue(StatDefOf.MarketValue);
            string slavePriceTooltip = FlattenTooltip("SlavePriceDescription".Translate());
            string slavePriceRow = $"{"SlavePrice".Translate()}: {marketValue.ToStringMoney()}";
            if (!string.IsNullOrEmpty(slavePriceTooltip))
                slavePriceRow += $". {slavePriceTooltip}";
            ab.Add(slavePriceRow);

            // Release Potential Relations (vanilla "SlaveReleasePotentialRelationGains", tooltip "SlaveReleaseRelationGainsDesc")
            string releaseTooltip = FlattenTooltip("SlaveReleaseRelationGainsDesc".Translate());
            string releaseRow = $"{"SlaveReleasePotentialRelationGains".Translate()}: {GetSlaveReleaseRelationGainsText(pawn)}";
            if (!string.IsNullOrEmpty(releaseTooltip))
                releaseRow += $". {releaseTooltip}";
            ab.Add(releaseRow);

            return ab.Build();
        }

        /// <summary>
        /// Flattens a multi-line game tooltip into a single line so it can be appended to one
        /// navigable info row. Strips formatting tags and turns line breaks into sentence breaks;
        /// SpeechSanitizer collapses any resulting redundant punctuation.
        /// </summary>
        private static string FlattenTooltip(string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip))
                return "";

            return tooltip.StripTags().Replace("\r", " ").Replace("\n", ". ").Trim();
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
        /// Gets the label for a medical care level via the vanilla MedicalCareUtility accessor,
        /// which uses per-level translation keys ("MedicalCareCategory_NoCare", etc.).
        /// </summary>
        public static string GetMedicalCareLabel(MedicalCareCategory category)
        {
            return category.GetLabel();
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
        /// Warnings reuse vanilla message keys so they localize correctly.
        /// </summary>
        public static string GetInteractionModeDescription(Pawn pawn, PrisonerInteractionModeDef mode)
        {
            var ab = new AnnouncementBuilder()
                .Add(mode.description ?? mode.LabelCap);

            if (mode == PrisonerInteractionModeDefOf.Enslave && pawn.MapHeld != null && !ColonyHasAnyWardenCapableOfEnslavement(pawn.MapHeld))
            {
                ab.Add("MessageNoWardenCapableOfEnslavement".Translate());
            }

            if (mode == PrisonerInteractionModeDefOf.Execution && pawn.MapHeld != null && !ColonyHasAnyWardenCapableOfViolence(pawn.MapHeld))
            {
                ab.Add("MessageCantDoExecutionBecauseNoWardenCapableOfViolence".Translate());
            }

            if (mode == PrisonerInteractionModeDefOf.Convert && pawn.guest.ideoForConversion != null && pawn.MapHeld != null && !ColonyHasAnyWardenOfIdeo(pawn.guest.ideoForConversion, pawn.MapHeld))
            {
                ab.Add("NoWardenOfIdeo".Translate(pawn.guest.ideoForConversion.memberName.Named("MEMBERNAME")));
            }

            return ab.Build();
        }

        /// <summary>
        /// Gets a description of the slave interaction mode.
        /// Emancipate tooltips reuse vanilla's EmancipateXxxTooltip keys.
        /// </summary>
        public static string GetSlaveInteractionModeDescription(Pawn pawn, SlaveInteractionModeDef mode)
        {
            var ab = new AnnouncementBuilder()
                .Add(mode.description ?? mode.LabelCap);

            if (mode == SlaveInteractionModeDefOf.Emancipate)
            {
                if (pawn.SlaveFaction == Faction.OfPlayer)
                {
                    ab.Add("EmancipateCololonistTooltip".Translate());
                }
                else if (pawn.SlaveFaction == null)
                {
                    ab.Add("EmancipateNonCololonistWithoutFactionTooltip".Translate());
                }
                else
                {
                    ab.Add("EmancipateNonCololonistWithFactionTooltip".Translate(pawn.SlaveFaction.Name));
                }
            }

            return ab.Build();
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

        /// <summary>
        /// Appends Anomaly DLC study period and knowledge gain info using the same translation
        /// keys vanilla ITab_Entity.DoStudyPeriodListing / DoKnowledgeGainListing use.
        /// </summary>
        private static void AppendStudyInfo(AnnouncementBuilder ab, CompStudiable studiable)
        {
            // Study interval (vanilla "StudyInterval")
            if (studiable.Props.frequencyTicks > 0)
            {
                ab.Add($"{"StudyInterval".Translate()}: {studiable.Props.frequencyTicks.ToStringTicksToPeriod()}");
            }

            // Knowledge gain per study (vanilla "StudyKnowledgeGain" with category label)
            float knowledgePerStudy = studiable.AdjustedAnomalyKnowledgePerStudy * 5f;
            string knowledgeCategoryLabel = studiable.KnowledgeCategory?.label ?? "";
            ab.Add($"{"StudyKnowledgeGain".Translate()}: {knowledgePerStudy.ToStringDecimalIfSmall()} ({knowledgeCategoryLabel})");

            // Multiplier breakdown (containment, electroharvester, activity)
            var compHoldingPlatformTarget = studiable.Pawn.TryGetComp<CompHoldingPlatformTarget>();
            if (compHoldingPlatformTarget != null && compHoldingPlatformTarget.CurrentlyHeldOnPlatform)
            {
                var holderComp = compHoldingPlatformTarget.HeldPlatform.GetComp<CompEntityHolder>();
                float containmentMultiplier = ContainmentUtility.GetStudyKnowledgeAmountMultiplier(studiable.Pawn, holderComp);
                ab.Add($"{"FactorContainmentStrength".Translate()}: x{containmentMultiplier:F1}");

                if (compHoldingPlatformTarget.HeldPlatform.HasAttachedElectroharvester)
                {
                    ab.Add($"{"FactorElectroharvester".Translate()}: x0.5");
                }
            }
            if (studiable.Pawn.TryGetComp<CompActivity>(out var compActivity))
            {
                ab.Add($"{"FactorActivity".Translate()}: x{compActivity.ActivityResearchFactor:F1}");
            }
        }

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
            return FormatReleaseRelationGains(pawn, pawn.Faction);
        }

        private static string GetSlaveReleaseRelationGainsText(Pawn pawn)
        {
            return FormatReleaseRelationGains(pawn, pawn.SlaveFaction ?? pawn.Faction);
        }

        /// <summary>
        /// Formats release relation gain text using vanilla "None" and "UntendedInjury" keys,
        /// matching the logic in ITab_Pawn_Visitor.DoPrisonerTab / DoSlaveTab.
        /// </summary>
        private static string FormatReleaseRelationGains(Pawn pawn, Faction faction)
        {
            string none = "None".Translate();

            if (faction == null || faction.IsPlayer || !faction.CanChangeGoodwillFor(Faction.OfPlayer, 1))
            {
                return none;
            }

            bool isHealthy;
            bool isInMentalState;
            int goodwillChange = faction.CalculateAdjustedGoodwillChange(
                Faction.OfPlayer,
                faction.GetGoodwillGainForExit(pawn, freed: true, out isHealthy, out isInMentalState));

            if (isHealthy && !isInMentalState)
            {
                return $"{faction.Name} {goodwillChange.ToStringWithSign()}";
            }
            if (!isHealthy)
            {
                return $"{none} ({"UntendedInjury".Translate()})";
            }
            return $"{none} ({pawn.MentalState.InspectLine})";
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
