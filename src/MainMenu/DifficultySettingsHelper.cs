using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds the custom difficulty sections, shared by in-game storyteller selection and chargen.
    /// </summary>
    public static class DifficultySettingsHelper
    {
        /// <summary>
        /// Builds every custom difficulty section for <paramref name="difficulty"/>.
        /// </summary>
        /// <param name="isCharGen">Vanilla exposes the Anomaly playstyle picker and its
        /// override-fraction slider at chargen only, so both are omitted when false; the
        /// inactive/active/study sliders stay editable mid-game.</param>
        public static List<DifficultySection> BuildSections(
            Difficulty difficulty,
            Action<DifficultyDef> onReset = null,
            Action onAnomalyPlaystyleChanged = null,
            bool isCharGen = true)
        {
            var sections = new List<DifficultySection>();

            // Left column, mirroring DrawCustomLeft.
            var threats = new DifficultySection("DifficultyThreatSection".Translate());
            threats.Settings.Add(new DifficultySliderSetting("threatScale", () => difficulty.threatScale, v => difficulty.threatScale = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            threats.Settings.Add(new DifficultyCheckboxSetting("allowBigThreats", () => difficulty.allowBigThreats, v => difficulty.allowBigThreats = v));
            threats.Settings.Add(new DifficultyCheckboxSetting("allowViolentQuests", () => difficulty.allowViolentQuests, v => difficulty.allowViolentQuests = v));
            threats.Settings.Add(new DifficultyCheckboxSetting("allowIntroThreats", () => difficulty.allowIntroThreats, v => difficulty.allowIntroThreats = v));
            threats.Settings.Add(new DifficultyCheckboxSetting("predatorsHuntHumanlikes", () => difficulty.predatorsHuntHumanlikes, v => difficulty.predatorsHuntHumanlikes = v));
            threats.Settings.Add(new DifficultyCheckboxSetting("allowExtremeWeatherIncidents", () => difficulty.allowExtremeWeatherIncidents, v => difficulty.allowExtremeWeatherIncidents = v));
            if (ModsConfig.BiotechActive)
            {
                threats.Settings.Add(new DifficultySliderSetting("wastepackInfestationChanceFactor", () => difficulty.wastepackInfestationChanceFactor, v => difficulty.wastepackInfestationChanceFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            }
            sections.Add(threats);

            var economy = new DifficultySection("DifficultyEconomySection".Translate());
            economy.Settings.Add(new DifficultySliderSetting("cropYieldFactor", () => difficulty.cropYieldFactor, v => difficulty.cropYieldFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("mineYieldFactor", () => difficulty.mineYieldFactor, v => difficulty.mineYieldFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("butcherYieldFactor", () => difficulty.butcherYieldFactor, v => difficulty.butcherYieldFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            if (ModsConfig.IsActive("ludeon.rimworld.odyssey"))
            {
                economy.Settings.Add(new DifficultySliderSetting("fishingYieldFactor", () => difficulty.fishingYieldFactor, v => difficulty.fishingYieldFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            }
            economy.Settings.Add(new DifficultySliderSetting("researchSpeedFactor", () => difficulty.researchSpeedFactor, v => difficulty.researchSpeedFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("questRewardValueFactor", () => difficulty.questRewardValueFactor, v => difficulty.questRewardValueFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("raidLootPointsFactor", () => difficulty.raidLootPointsFactor, v => difficulty.raidLootPointsFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("tradePriceFactorLoss", () => difficulty.tradePriceFactorLoss, v => difficulty.tradePriceFactorLoss = v, 0f, 0.5f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("maintenanceCostFactor", () => difficulty.maintenanceCostFactor, v => difficulty.maintenanceCostFactor = v, 0.01f, 1f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("scariaRotChance", () => difficulty.scariaRotChance, v => difficulty.scariaRotChance = v, 0f, 1f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("enemyDeathOnDownedChanceFactor", () => difficulty.enemyDeathOnDownedChanceFactor, v => difficulty.enemyDeathOnDownedChanceFactor = v, 0f, 1f, 0.01f, ToStringStyle.PercentZero));
            economy.Settings.Add(new DifficultySliderSetting("nomadicMineableResourcesFactor", () => difficulty.nomadicMineableResourcesFactor, v => difficulty.nomadicMineableResourcesFactor = v, 0f, 2f, 0.01f, ToStringStyle.PercentZero));
            sections.Add(economy);

            if (ModsConfig.IdeologyActive)
            {
                var ideology = new DifficultySection("DifficultyIdeologySection".Translate());
                ideology.Settings.Add(new DifficultySliderSetting("lowPopConversionBoost", () => difficulty.lowPopConversionBoost, v => difficulty.lowPopConversionBoost = v, 1f, 5f, 1f, ToStringStyle.Integer, ToStringNumberSense.Factor));
                sections.Add(ideology);
            }

            // Vanilla gates the whole Anomaly section on the playstyle's enableAnomalyContent, so
            // the Disabled playstyle shows no section at all. AnomalyPlaystyleDef's getter falls
            // back to Standard when null, so this is never a null ref.
            if (ModsConfig.AnomalyActive && difficulty.AnomalyPlaystyleDef.enableAnomalyContent)
            {
                var anomaly = new DifficultySection("DifficultyAnomalySection".Translate());

                if (isCharGen)
                {
                    // MUTATION-C: setter mirrors Dialog_AnomalySettings's Accept branch
                    // (`difficulty.AnomalyPlaystyleDef = anomalyPlaystyleDef;`, decompiled
                    // ~line 80); no gated setter exists. onTransitionToOverride's 0.15f
                    // default mirrors Dialog_AnomalySettings.DefaultOverrideThreatFraction,
                    // the game's own const for the same fallback.
                    anomaly.Settings.Add(new AnomalyPlaystyleSetting(
                        getter: () => difficulty.AnomalyPlaystyleDef,
                        setter: v => difficulty.AnomalyPlaystyleDef = v,
                        onTransitionToOverride: () =>
                        {
                            if (!difficulty.overrideAnomalyThreatsFraction.HasValue)
                                difficulty.overrideAnomalyThreatsFraction = 0.15f;
                        },
                        onChanged: onAnomalyPlaystyleChanged));
                }

                // The override-fraction slider only matters for override-style playstyles, which can
                // only be picked at chargen.
                anomaly.Settings.AddRange(BuildAnomalySliders(
                    playstyleGetter: () => difficulty.AnomalyPlaystyleDef,
                    overrideGetter: () => difficulty.overrideAnomalyThreatsFraction ?? 0.15f,
                    // MUTATION-C: mirrors Dialog_AnomalySettings.cs:170
                    // (`overrideAnomalyThreatsFraction = listing.Slider(..., 0f, 1f);`);
                    // vanilla writes the field bare, no gated setter exists.
                    overrideSetter: v => difficulty.overrideAnomalyThreatsFraction = v,
                    inactiveGetter: () => difficulty.anomalyThreatsInactiveFraction,
                    // MUTATION-C: mirrors Dialog_AnomalySettings.cs:161
                    // (`anomalyThreatsInactiveFraction = listing.Slider(..., 0f, 1f);`);
                    // vanilla writes the field bare, no gated setter exists.
                    inactiveSetter: v => difficulty.anomalyThreatsInactiveFraction = v,
                    activeGetter: () => difficulty.anomalyThreatsActiveFraction,
                    // MUTATION-C: mirrors Dialog_AnomalySettings.cs:164's bare-field write
                    // (`anomalyThreatsActiveFraction = listing.Slider(..., 0f, 1f);`) and
                    // StorytellerUI.cs:247's DrawCustomLeft bare-field write; no gated setter
                    // exists. The per-caller floor below (activeFractionFloor) supplies the
                    // vanilla-matching bound for whichever draw site this call mirrors.
                    activeSetter: v => difficulty.anomalyThreatsActiveFraction = v,
                    // MUTATION-C: floor mirrors StorytellerUI.cs:247's DrawCustomLeft
                    // slider (0.1f), distinct from Dialog_AnomalySettings.cs:164's popup
                    // floor (0f) -- vanilla itself disagrees between its two draw sites
                    // for this field. This BuildSections path (both isCharGen: true and
                    // isCharGen: false callers) always renders through DrawCustomLeft's
                    // persistent custom-difficulty section, never the popup, so 0.1f is
                    // correct for both.
                    activeFractionFloor: 0.1f,
                    studyGetter: () => difficulty.studyEfficiencyFactor,
                    // MUTATION-C: mirrors Dialog_AnomalySettings.cs:175
                    // (`studyEfficiencyFactor = listing.Slider(..., 0f, 5f);`);
                    // vanilla writes the field bare, no gated setter exists.
                    studySetter: v => difficulty.studyEfficiencyFactor = v,
                    useEnabledConditions: true,
                    includeOverride: isCharGen));

                if (anomaly.Settings.Count > 0)
                    sections.Add(anomaly);
            }

            if (ModsConfig.BiotechActive)
            {
                var children = new DifficultySection("DifficultyChildrenSection".Translate());
                children.Settings.Add(new DifficultyCheckboxSetting("noBabiesOrChildren", () => difficulty.noBabiesOrChildren, v => difficulty.noBabiesOrChildren = v));
                children.Settings.Add(new DifficultyCheckboxSetting("babiesAreHealthy", () => difficulty.babiesAreHealthy, v => difficulty.babiesAreHealthy = v));
                children.Settings.Add(new DifficultyCheckboxSetting("childRaidersAllowed", () => difficulty.childRaidersAllowed, v => difficulty.childRaidersAllowed = v, () => !difficulty.noBabiesOrChildren));
                if (ModsConfig.AnomalyActive)
                {
                    children.Settings.Add(new DifficultyCheckboxSetting("childShamblersAllowed", () => difficulty.childShamblersAllowed, v => difficulty.childShamblersAllowed = v, () => !difficulty.noBabiesOrChildren));
                }
                children.Settings.Add(new DifficultySliderSetting("childAgingRate", () => difficulty.childAgingRate, v => difficulty.childAgingRate = v, 1f, 6f, 1f, ToStringStyle.Integer, ToStringNumberSense.Factor));
                children.Settings.Add(new DifficultySliderSetting("adultAgingRate", () => difficulty.adultAgingRate, v => difficulty.adultAgingRate = v, 1f, 6f, 1f, ToStringStyle.Integer, ToStringNumberSense.Factor));
                sections.Add(children);
            }

            // Right column, mirroring DrawCustomRight.
            var general = new DifficultySection("DifficultyGeneralSection".Translate());
            general.Settings.Add(new DifficultySliderSetting("colonistMoodOffset", () => difficulty.colonistMoodOffset, v => difficulty.colonistMoodOffset = v, -20f, 20f, 1f, ToStringStyle.Integer, ToStringNumberSense.Offset));
            general.Settings.Add(new DifficultySliderSetting("foodPoisonChanceFactor", () => difficulty.foodPoisonChanceFactor, v => difficulty.foodPoisonChanceFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            general.Settings.Add(new DifficultySliderSetting("manhunterChanceOnDamageFactor", () => difficulty.manhunterChanceOnDamageFactor, v => difficulty.manhunterChanceOnDamageFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            general.Settings.Add(new DifficultySliderSetting("playerPawnInfectionChanceFactor", () => difficulty.playerPawnInfectionChanceFactor, v => difficulty.playerPawnInfectionChanceFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            general.Settings.Add(new DifficultySliderSetting("diseaseIntervalFactor", () => difficulty.diseaseIntervalFactor, v => difficulty.diseaseIntervalFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero, ToStringNumberSense.Absolute, true, 100f));
            general.Settings.Add(new DifficultySliderSetting("enemyReproductionRateFactor", () => difficulty.enemyReproductionRateFactor, v => difficulty.enemyReproductionRateFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            general.Settings.Add(new DifficultySliderSetting("deepDrillInfestationChanceFactor", () => difficulty.deepDrillInfestationChanceFactor, v => difficulty.deepDrillInfestationChanceFactor = v, 0f, 5f, 0.01f, ToStringStyle.PercentZero));
            general.Settings.Add(new DifficultySliderSetting("friendlyFireChanceFactor", () => difficulty.friendlyFireChanceFactor, v => difficulty.friendlyFireChanceFactor = v, 0f, 1f, 0.01f, ToStringStyle.PercentZero));
            general.Settings.Add(new DifficultySliderSetting("allowInstantKillChance", () => difficulty.allowInstantKillChance, v => difficulty.allowInstantKillChance = v, 0f, 1f, 0.01f, ToStringStyle.PercentZero));
            general.Settings.Add(new DifficultyCheckboxSetting("peacefulTemples", () => difficulty.peacefulTemples, v => difficulty.peacefulTemples = v, null, true));
            general.Settings.Add(new DifficultyCheckboxSetting("allowCaveHives", () => difficulty.allowCaveHives, v => difficulty.allowCaveHives = v));
            general.Settings.Add(new DifficultyCheckboxSetting("unwaveringPrisoners", () => difficulty.unwaveringPrisoners, v => difficulty.unwaveringPrisoners = v));
            sections.Add(general);

            var playerTools = new DifficultySection("DifficultyPlayerToolsSection".Translate());
            playerTools.Settings.Add(new DifficultyCheckboxSetting("allowTraps", () => difficulty.allowTraps, v => difficulty.allowTraps = v));
            playerTools.Settings.Add(new DifficultyCheckboxSetting("allowTurrets", () => difficulty.allowTurrets, v => difficulty.allowTurrets = v));
            playerTools.Settings.Add(new DifficultyCheckboxSetting("allowMortars", () => difficulty.allowMortars, v => difficulty.allowMortars = v));
            playerTools.Settings.Add(new DifficultyCheckboxSetting("classicMortars", () => difficulty.classicMortars, v => difficulty.classicMortars = v));
            sections.Add(playerTools);

            var adaptation = new DifficultySection("DifficultyAdaptationSection".Translate());
            adaptation.Settings.Add(new DifficultySliderSetting("adaptationGrowthRateFactorOverZero", () => difficulty.adaptationGrowthRateFactorOverZero, v => difficulty.adaptationGrowthRateFactorOverZero = v, 0f, 1f, 0.01f, ToStringStyle.PercentZero));
            adaptation.Settings.Add(new DifficultySliderSetting("adaptationEffectFactor", () => difficulty.adaptationEffectFactor, v => difficulty.adaptationEffectFactor = v, 0f, 1f, 0.01f, ToStringStyle.PercentZero));
            adaptation.Settings.Add(new DifficultyCheckboxSetting("fixedWealthMode", () => difficulty.fixedWealthMode, v => difficulty.fixedWealthMode = v));
            adaptation.Settings.Add(new DifficultySliderSetting(
                "fixedWealthTimeFactor",
                () => Mathf.Round(12f / Mathf.Max(0.01f, difficulty.fixedWealthTimeFactor)),
                v => difficulty.fixedWealthTimeFactor = 12f / Mathf.Max(1f, v),
                1f, 20f, 1f, ToStringStyle.Integer, ToStringNumberSense.Absolute, false, 0f,
                () => difficulty.fixedWealthMode));
            sections.Add(adaptation);

            if (onReset != null)
            {
                var resetSection = new DifficultySection("DifficultyReset".Translate(), (string)"RimWorldAccess.CustomDifficulty.ItemsLabelPlaystyles".Translate());
                foreach (DifficultyDef def in DefDatabase<DifficultyDef>.AllDefs)
                {
                    if (!def.isCustom)
                    {
                        DifficultyDef localDef = def;
                        resetSection.Settings.Add(new DifficultyResetSetting(
                            localDef.LabelCap,
                            (localDef.description ?? "").StripTags(),
                            () => onReset(localDef)));
                    }
                }
                sections.Add(resetSection);
            }

            return sections;
        }

        /// <summary>
        /// The four conditional anomaly sliders (override, inactive, active, study), shared by the
        /// Custom Difficulty Anomaly section and the Dialog_AnomalySettings popup. The caller wires
        /// the getter/setter delegates, so the same builder drives either direct writes to a
        /// Difficulty or writes to local copies committed on Accept.
        /// With <paramref name="useEnabledConditions"/> true, every slider is returned but disables
        /// itself when its condition fails, for the section that shows one continuous list; with
        /// false, only the currently-relevant sliders come back, for the popup that rebuilds its
        /// list whenever the playstyle changes.
        /// </summary>
        public static List<DifficultySetting> BuildAnomalySliders(
            Func<AnomalyPlaystyleDef> playstyleGetter,
            Func<float> overrideGetter, Action<float> overrideSetter,
            Func<float> inactiveGetter, Action<float> inactiveSetter,
            Func<float> activeGetter, Action<float> activeSetter,
            float activeFractionFloor,
            Func<float> studyGetter, Action<float> studySetter,
            bool useEnabledConditions,
            bool includeOverride = true)
        {
            var result = new List<DifficultySetting>();

            bool overrideVisible() => playstyleGetter()?.overrideThreatFraction == true;
            bool fractionSlidersVisible() => playstyleGetter()?.displayThreatFractionSliders == true
                                             && playstyleGetter()?.overrideThreatFraction != true;
            bool studyVisible() => playstyleGetter()?.displayStudyFactorSlider == true;

            void AddSlider(DifficultySliderSetting s, Func<bool> visibility)
            {
                if (useEnabledConditions || visibility())
                    result.Add(s);
            }

            // MUTATION-C: overrideGetter/Setter's 0f-1f bounds mirror both call sites
            // identically -- Dialog_AnomalySettings.cs:170 (`listing.Slider(..., 0f, 1f)`)
            // and StorytellerUI.cs:240 (`DrawCustomDifficultySlider(..., 0f, 1f)`); no
            // gated setter exists for the bare Difficulty field either writes.
            if (includeOverride)
            {
                AddSlider(new DifficultySliderSetting(
                    "Difficulty_AnomalyThreats_Label".Translate(),
                    "Difficulty_AnomalyThreats_Info".Translate(),
                    overrideGetter, overrideSetter,
                    0f, 1f, 0.01f, ToStringStyle.PercentZero,
                    enabledCondition: useEnabledConditions ? overrideVisible : (Func<bool>)null,
                    coveredField: "overrideAnomalyThreatsFraction"),
                    overrideVisible);
            }

            // MUTATION-C: inactiveGetter/Setter's 0f-1f bounds mirror both call sites
            // identically -- Dialog_AnomalySettings.cs:161 and StorytellerUI.cs:245
            // (both `..., 0f, 1f)`); no gated setter, vanilla writes the field bare.
            AddSlider(new DifficultySliderSetting(
                "Difficulty_AnomalyThreatsInactive_Label".Translate(),
                "Difficulty_AnomalyThreatsInactive_Info".Translate(),
                inactiveGetter, inactiveSetter,
                0f, 1f, 0.01f, ToStringStyle.PercentZero,
                enabledCondition: useEnabledConditions ? fractionSlidersVisible : (Func<bool>)null,
                coveredField: "anomalyThreatsInactiveFraction"),
                fractionSlidersVisible);

            // Vanilla embeds the current and 1.5x values in the tooltip, so it must be
            // re-evaluated on every announcement.
            // MUTATION-C: floor is activeFractionFloor, threaded per-caller -- vanilla's
            // two draw sites for this field disagree (Dialog_AnomalySettings.cs:164 uses
            // 0f; StorytellerUI.cs:247's DrawCustomLeft uses 0.1f), so the shared helper
            // can't hardcode one floor for both callers. See callers for the citation.
            AddSlider(new DifficultySliderSetting(
                "Difficulty_AnomalyThreatsActive_Label".Translate(),
                tooltipFunc: () => "Difficulty_AnomalyThreatsActive_Info".Translate(
                    Mathf.Clamp01(activeGetter()).ToStringPercent(),
                    Mathf.Clamp01(activeGetter() * 1.5f).ToStringPercent()),
                activeGetter, activeSetter,
                activeFractionFloor, 1f, 0.01f, ToStringStyle.PercentZero,
                enabledCondition: useEnabledConditions ? fractionSlidersVisible : (Func<bool>)null,
                coveredField: "anomalyThreatsActiveFraction"),
                fractionSlidersVisible);

            // MUTATION-C: studyGetter/Setter's 0f-5f bounds mirror both call sites
            // identically -- Dialog_AnomalySettings.cs:175 and StorytellerUI.cs:249
            // (both `..., 0f, 5f)`); no gated setter, vanilla writes the field bare.
            AddSlider(new DifficultySliderSetting(
                "Difficulty_StudyEfficiency_Label".Translate(),
                "Difficulty_StudyEfficiency_Info".Translate(),
                studyGetter, studySetter,
                0f, 5f, 0.01f, ToStringStyle.PercentZero,
                enabledCondition: useEnabledConditions ? studyVisible : (Func<bool>)null,
                coveredField: "studyEfficiencyFactor"),
                studyVisible);

            return result;
        }
    }

    /// <summary>One section of difficulty settings, such as Threats or Economy.</summary>
    public class DifficultySection
    {
        public string Name { get; }
        public List<DifficultySetting> Settings { get; }
        public string ItemsLabel { get; }

        public DifficultySection(string name, string itemsLabel = null)
        {
            Name = name;
            Settings = new List<DifficultySetting>();
            ItemsLabel = itemsLabel ?? (string)"RimWorldAccess.CustomDifficulty.ItemsLabelSettings".Translate();
        }
    }

    /// <summary>Base class for difficulty settings.</summary>
    public abstract class DifficultySetting
    {
        public string Label { get; protected set; }
        public string Tooltip { get; protected set; }
        /// <summary>
        /// The single <see cref="Difficulty"/> field this setting reads and writes, null for entries
        /// that map to none. Read only by the DEBUG catalog self-audit, never by the player.
        /// </summary>
        public string CoveredField { get; protected set; }
        protected Func<bool> enabledCondition;

        public bool IsEnabled => enabledCondition == null || enabledCondition();

        public virtual string GetAdjustmentAnnouncement() => GetAnnouncement();
        public abstract string GetAnnouncement();
        public abstract void Toggle();
        public abstract void Adjust(int direction);
    }

    /// <summary>
    /// Turns any <see cref="DifficultySetting"/> subclass into a role-bearing announcement, so the
    /// same setting speaks identically in every difficulty-editing surface.
    /// <see cref="ExtractSettingValue"/> lives here because it depends on
    /// <see cref="DifficultySetting.GetAnnouncement"/>'s "{label}. {value}. {tooltip}" format,
    /// which this file also composes.
    /// </summary>
    public static class DifficultySettingAdapter
    {
        /// <summary>Fills one setting's row: Reset a Button (no Value slot), Checkbox a Checkbox,
        /// AnomalyPlaystyle a ComboBox, everything else a Slider.</summary>
        public static void FillRow(ElementDescription d, DifficultySetting s)
        {
            if (s is DifficultyResetSetting)
            {
                d.Label = s.Label;
                d.Role = ElementRole.Button;
                d.Extras = s.Tooltip;
                return;
            }
            if (s is DifficultyCheckboxSetting checkbox)
            {
                FillSettingRow(d, s, ElementRole.Checkbox);
                // Check, not Value: the composer speaks the standard checked/not-checked grammar.
                if (!d.Disabled)
                {
                    d.Check = checkbox.DisplayChecked ? CheckState.Checked : CheckState.Unchecked;
                    d.Value = null;
                }
            }
            else if (s is AnomalyPlaystyleSetting)
                FillSettingRow(d, s, ElementRole.ComboBox);
            else
                FillSettingRow(d, s, ElementRole.Slider);
        }

        /// <summary>
        /// Presents a setting with a control role. <see cref="DifficultySetting.GetAnnouncement"/> is
        /// the only value source, so the value is sliced out of that string using the setting's OWN
        /// label, never a translated-label guess.
        /// </summary>
        private static void FillSettingRow(ElementDescription d, DifficultySetting s, ElementRole role)
        {
            d.Label = s.Label;
            d.Role = role;
            d.Extras = s.Tooltip;
            if (!s.IsEnabled)
            {
                d.Disabled = true;
                return;
            }
            d.Value = ExtractSettingValue(s);
        }

        /// <summary>
        /// Slices the value out of "{label}. {value}. {tooltip}": strip the leading label, then take
        /// everything up to the first ". ". No difficulty value ever contains ". " itself, so that
        /// boundary is always the value/tooltip split.
        /// </summary>
        public static string ExtractSettingValue(DifficultySetting s)
        {
            string ann = s.GetAnnouncement() ?? "";
            string label = s.Label ?? "";
            if (label.Length > 0 && ann.StartsWith(label, System.StringComparison.Ordinal))
                ann = ann.Substring(label.Length);
            ann = ann.TrimStart('.', ':', ' ');
            int boundary = ann.IndexOf(". ", System.StringComparison.Ordinal);
            if (boundary > 0)
                ann = ann.Substring(0, boundary);
            ann = ann.Trim();
            return ann.Length > 0 ? ann : null;
        }
    }

    /// <summary>Boolean difficulty setting.</summary>
    public class DifficultyCheckboxSetting : DifficultySetting
    {
        private readonly Func<bool> getter;
        private readonly Action<bool> setter;
        private readonly bool invert;

        public DifficultyCheckboxSetting(string optionName, Func<bool> getter, Action<bool> setter, Func<bool> enabledCondition = null, bool invert = false)
        {
            this.getter = getter;
            this.setter = setter;
            this.enabledCondition = enabledCondition;
            this.invert = invert;
            CoveredField = optionName;

            string invertSuffix = invert ? "_Inverted" : "";
            string capitalizedName = optionName.CapitalizeFirst();
            Label = $"Difficulty_{capitalizedName}{invertSuffix}_Label".Translate();
            Tooltip = $"Difficulty_{capitalizedName}{invertSuffix}_Info".Translate();
        }

        /// <summary>The state as shown: an inverted setting displays the opposite of its backing field.</summary>
        public bool DisplayChecked
        {
            get { return invert ? !getter() : getter(); }
        }

        public override string GetAnnouncement()
        {
            if (!IsEnabled)
                return $"{Label}: {(string)"Disabled".Translate()}";

            string valueStr = DisplayChecked ? (string)"On".Translate() : (string)"Off".Translate();
            return $"{Label}. {valueStr}. {Tooltip}";
        }

        public override void Toggle()
        {
            if (!IsEnabled) return;
            setter(!getter());
        }

        public override void Adjust(int direction)
        {
            Toggle();
        }
    }

    /// <summary>Float difficulty setting.</summary>
    public class DifficultySliderSetting : DifficultySetting
    {
        private readonly Func<float> getter;
        private readonly Action<float> setter;
        private readonly float min;
        private readonly float max;
        private readonly float step;
        private readonly ToStringStyle style;
        private readonly ToStringNumberSense numberSense;
        private readonly bool reciprocate;
        private readonly float reciprocalCutoff;
        // Re-evaluated each call, for the vanilla tooltips that embed the current value.
        private readonly Func<string> tooltipFunc;

        public DifficultySliderSetting(string optionName, Func<float> getter, Action<float> setter,
            float min, float max, float step, ToStringStyle style,
            ToStringNumberSense numberSense = ToStringNumberSense.Absolute,
            bool reciprocate = false, float reciprocalCutoff = 1000f,
            Func<bool> enabledCondition = null)
        {
            this.getter = getter;
            this.setter = setter;
            this.min = min;
            this.max = max;
            this.step = step;
            this.style = style;
            this.numberSense = numberSense;
            this.reciprocate = reciprocate;
            this.reciprocalCutoff = reciprocalCutoff;
            this.enabledCondition = enabledCondition;
            CoveredField = optionName;

            string invertSuffix = reciprocate ? "_Inverted" : "";
            string capitalizedName = optionName.CapitalizeFirst();
            Label = $"Difficulty_{capitalizedName}{invertSuffix}_Label".Translate();
            Tooltip = $"Difficulty_{capitalizedName}{invertSuffix}_Info".Translate();
        }

        public DifficultySliderSetting(string label, string tooltip, Func<float> getter, Action<float> setter,
            float min, float max, float step, ToStringStyle style,
            ToStringNumberSense numberSense = ToStringNumberSense.Absolute,
            bool reciprocate = false, float reciprocalCutoff = 1000f,
            Func<bool> enabledCondition = null, string coveredField = null)
        {
            this.getter = getter;
            this.setter = setter;
            this.min = min;
            this.max = max;
            this.step = step;
            this.style = style;
            this.numberSense = numberSense;
            this.reciprocate = reciprocate;
            this.reciprocalCutoff = reciprocalCutoff;
            this.enabledCondition = enabledCondition;
            CoveredField = coveredField;

            Label = label;
            Tooltip = tooltip;
        }

        public DifficultySliderSetting(string label, Func<string> tooltipFunc, Func<float> getter, Action<float> setter,
            float min, float max, float step, ToStringStyle style,
            ToStringNumberSense numberSense = ToStringNumberSense.Absolute,
            bool reciprocate = false, float reciprocalCutoff = 1000f,
            Func<bool> enabledCondition = null, string coveredField = null)
        {
            this.getter = getter;
            this.setter = setter;
            this.min = min;
            this.max = max;
            this.step = step;
            this.style = style;
            this.numberSense = numberSense;
            this.reciprocate = reciprocate;
            this.reciprocalCutoff = reciprocalCutoff;
            this.enabledCondition = enabledCondition;
            this.tooltipFunc = tooltipFunc;
            CoveredField = coveredField;

            Label = label;
            Tooltip = tooltipFunc?.Invoke() ?? "";
        }

        public float WidgetValue
        {
            get { return reciprocate ? Reciprocal(getter(), reciprocalCutoff) : getter(); }
        }

        public override string GetAnnouncement()
        {
            if (!IsEnabled)
                return $"{Label}: {(string)"Disabled".Translate()}";

            float value = WidgetValue;
            string valueStr = value.ToStringByStyle(style, numberSense);
            string tooltipStr = tooltipFunc != null ? tooltipFunc() : Tooltip;
            return $"{Label}. {valueStr}. {tooltipStr}";
        }

        public override void Toggle()
        {
            Adjust(1);
        }

        public override void Adjust(int direction)
        {
            if (!IsEnabled) return;

            float current = getter();
            if (reciprocate)
                current = Reciprocal(current, reciprocalCutoff);

            float newValue = Mathf.Clamp(current + (step * direction), min, max);
            newValue = GenMath.RoundTo(newValue, step);

            if (reciprocate)
                newValue = Reciprocal(newValue, reciprocalCutoff);

            setter(newValue);
        }

        public void SetToMin()
        {
            if (!IsEnabled) return;
            float newValue = min;
            if (reciprocate)
                newValue = Reciprocal(newValue, reciprocalCutoff);
            setter(newValue);
        }

        public void SetToMax()
        {
            if (!IsEnabled) return;
            float newValue = max;
            if (reciprocate)
                newValue = Reciprocal(newValue, reciprocalCutoff);
            setter(newValue);
        }

        /// <summary>Adjusts the slider by a fraction of its total discrete positions, at least one step.</summary>
        public void AdjustByPercentOfPositions(float percent)
        {
            if (!IsEnabled) return;

            float current = getter();
            if (reciprocate)
            {
                current = Reciprocal(current, reciprocalCutoff);
            }

            float range = max - min;
            int totalPositions = Mathf.Max(1, Mathf.RoundToInt(range / step));

            int stepsToMove = Mathf.Max(1, Mathf.RoundToInt(totalPositions * Mathf.Abs(percent)));
            if (percent < 0) stepsToMove = -stepsToMove;

            float adjustment = step * stepsToMove;
            float newValue = Mathf.Clamp(current + adjustment, min, max);
            newValue = GenMath.RoundTo(newValue, step);

            if (reciprocate)
            {
                newValue = Reciprocal(newValue, reciprocalCutoff);
            }

            setter(newValue);
        }

        private static float Reciprocal(float f, float cutOff)
        {
            cutOff *= 10f;
            if (Mathf.Abs(f) < 0.01f)
                return cutOff;
            if (f >= 0.99f * cutOff)
                return 0f;
            return 1f / f;
        }
    }

    /// <summary>Resets every difficulty setting to a preset.</summary>
    public class DifficultyResetSetting : DifficultySetting
    {
        private readonly Action executeAction;

        public DifficultyResetSetting(string label, string tooltip, Action executeAction)
        {
            Label = label;
            Tooltip = string.IsNullOrEmpty(tooltip) ? (string)"RimWorldAccess.CustomDifficulty.ResetTooltip".Translate() : tooltip;
            this.executeAction = executeAction;
        }

        public override string GetAnnouncement()
        {
            return $"{Label}. {Tooltip}";
        }

        public override void Toggle()
        {
            executeAction?.Invoke();
        }

        public override void Adjust(int direction)
        {
            // Reset settings don't adjust
        }
    }

    /// <summary>Selects an AnomalyPlaystyleDef.</summary>
    public class AnomalyPlaystyleSetting : DifficultySetting
    {
        private readonly Func<AnomalyPlaystyleDef> getter;
        private readonly Action<AnomalyPlaystyleDef> setter;
        private readonly List<AnomalyPlaystyleDef> options;
        private readonly Action onChanged;
        private readonly Action onTransitionToOverride;

        public AnomalyPlaystyleSetting(
            Func<AnomalyPlaystyleDef> getter,
            Action<AnomalyPlaystyleDef> setter,
            Action onTransitionToOverride = null,
            Action onChanged = null)
        {
            this.getter = getter;
            this.setter = setter;
            this.onTransitionToOverride = onTransitionToOverride;
            this.onChanged = onChanged;
            Label = "ChooseAnomalyPlaystyle".Translate();
            Tooltip = (string)"RimWorldAccess.CustomDifficulty.AnomalyPlaystyleTooltip".Translate();
            options = DefDatabase<AnomalyPlaystyleDef>.AllDefs.ToList();
        }

        public override string GetAnnouncement()
        {
            var current = getter();
            string description = current?.description?.StripTags() ?? "";
            return $"{Label}. {current?.LabelCap ?? (string)"None".Translate()}. {description}";
        }

        public override string GetAdjustmentAnnouncement()
        {
            var current = getter();
            string description = current?.description?.StripTags() ?? "";
            return $"{current?.LabelCap ?? (string)"None".Translate()}. {description}";
        }

        public override void Toggle() => OpenPicker();

        /// <summary>
        /// Deliberately a no-op: a combo-box row never steps on Left/Right, so
        /// <see cref="OpenPicker"/> is the only way in.
        /// </summary>
        public override void Adjust(int direction)
        {
        }

        /// <summary>
        /// Opens the playstyle list this ComboBox row promises. The options mirror vanilla's own
        /// <c>standardAnomalyPlaystyleOnly</c> gate, with its DisabledByScenario refusal for
        /// everything but Standard, rather than letting a keyboard user reach a value vanilla's UI
        /// refuses. <paramref name="onPicked"/> lets the caller speak the new state once the
        /// deferred pick lands.
        /// </summary>
        public void OpenPicker(Action onPicked = null)
        {
            if (options.Count == 0) return;

            var current = getter();
            var menuOptions = new List<FloatMenuOption>(options.Count);
            foreach (AnomalyPlaystyleDef def in options)
            {
                AnomalyPlaystyleDef captured = def;
                string label = captured.LabelCap;
                string description = captured.description?.StripTags();
                if (!string.IsNullOrEmpty(description))
                {
                    label = label + ". " + description;
                }
                if (Find.Scenario != null && Find.Scenario.standardAnomalyPlaystyleOnly
                    && captured != AnomalyPlaystyleDefOf.Standard)
                {
                    label = $"{label}. {(string)"DisabledByScenario".Translate()}: {Find.Scenario.name}";
                    menuOptions.Add(new FloatMenuOption(label, null) { Disabled = true });
                    continue;
                }
                menuOptions.Add(new FloatMenuOption(label, delegate
                {
                    Apply(captured);
                    onPicked?.Invoke();
                }));
            }
            int startIndex = Mathf.Max(0, options.IndexOf(current));
            WindowlessFloatMenuState.Open(
                menuOptions, colonistOrders: false, startIndex: startIndex, announceSelection: false);
        }

        private void Apply(AnomalyPlaystyleDef newValue)
        {
            var current = getter();
            if (current != null && !current.overrideThreatFraction && newValue.overrideThreatFraction)
            {
                onTransitionToOverride?.Invoke();
            }

            setter(newValue);
            onChanged?.Invoke();
        }
    }
}
