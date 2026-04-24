using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for building custom difficulty settings sections.
    /// Used by both in-game storyteller selection and character creation.
    /// </summary>
    public static class DifficultySettingsHelper
    {
        /// <summary>
        /// Builds all custom difficulty sections for the given Difficulty object.
        /// </summary>
        /// <param name="difficulty">The Difficulty object to read/write values from</param>
        /// <param name="onReset">Optional callback when a reset preset is selected</param>
        /// <param name="onAnomalyPlaystyleChanged">Optional callback when anomaly playstyle changes</param>
        /// <param name="isCharGen">True when called from chargen UI, false when called from the
        /// in-game storyteller change UI. Vanilla only exposes the Anomaly playstyle picker
        /// (and its override-fraction slider) at chargen — see StorytellerUI.cs:117 where the
        /// "AnomalySettings..." button is gated on ProgramState.Entry. When false, the playstyle
        /// row and the override slider are omitted to match vanilla behavior; the
        /// inactive/active/study sliders remain editable mid-game.</param>
        /// <returns>List of difficulty sections</returns>
        public static List<DifficultySection> BuildSections(
            Difficulty difficulty,
            Action<DifficultyDef> onReset = null,
            Action onAnomalyPlaystyleChanged = null,
            bool isCharGen = true)
        {
            var sections = new List<DifficultySection>();

            // ===== LEFT COLUMN SECTIONS (from DrawCustomLeft) =====

            // Threats Section
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

            // Economy Section
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

            // Ideology Section (DLC)
            if (ModsConfig.IdeologyActive)
            {
                var ideology = new DifficultySection("DifficultyIdeologySection".Translate());
                ideology.Settings.Add(new DifficultySliderSetting("lowPopConversionBoost", () => difficulty.lowPopConversionBoost, v => difficulty.lowPopConversionBoost = v, 1f, 5f, 1f, ToStringStyle.Integer, ToStringNumberSense.Factor));
                sections.Add(ideology);
            }

            // Anomaly Section (DLC)
            if (ModsConfig.AnomalyActive)
            {
                var anomaly = new DifficultySection("DifficultyAnomalySection".Translate());

                if (isCharGen)
                {
                    // Playstyle selector — chargen-only to match vanilla. A cycle-style row
                    // (Left/Right adjusts). Shared with AnomalySettingsDialogState.
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

                // Conditional anomaly sliders. The override-fraction slider is only meaningful
                // for override-style playstyles (e.g., AmbientHorror), which can only be picked
                // at chargen — so omit it in-game.
                anomaly.Settings.AddRange(BuildAnomalySliders(
                    playstyleGetter: () => difficulty.AnomalyPlaystyleDef,
                    overrideGetter: () => difficulty.overrideAnomalyThreatsFraction ?? 0.15f,
                    overrideSetter: v => difficulty.overrideAnomalyThreatsFraction = v,
                    inactiveGetter: () => difficulty.anomalyThreatsInactiveFraction,
                    inactiveSetter: v => difficulty.anomalyThreatsInactiveFraction = v,
                    activeGetter: () => difficulty.anomalyThreatsActiveFraction,
                    activeSetter: v => difficulty.anomalyThreatsActiveFraction = v,
                    studyGetter: () => difficulty.studyEfficiencyFactor,
                    studySetter: v => difficulty.studyEfficiencyFactor = v,
                    useEnabledConditions: true,
                    includeOverride: isCharGen));

                if (anomaly.Settings.Count > 0)
                    sections.Add(anomaly);
            }

            // Children Section (Biotech DLC)
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

            // ===== RIGHT COLUMN SECTIONS (from DrawCustomRight) =====

            // General Section
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

            // Player Tools Section
            var playerTools = new DifficultySection("DifficultyPlayerToolsSection".Translate());
            playerTools.Settings.Add(new DifficultyCheckboxSetting("allowTraps", () => difficulty.allowTraps, v => difficulty.allowTraps = v));
            playerTools.Settings.Add(new DifficultyCheckboxSetting("allowTurrets", () => difficulty.allowTurrets, v => difficulty.allowTurrets = v));
            playerTools.Settings.Add(new DifficultyCheckboxSetting("allowMortars", () => difficulty.allowMortars, v => difficulty.allowMortars = v));
            playerTools.Settings.Add(new DifficultyCheckboxSetting("classicMortars", () => difficulty.classicMortars, v => difficulty.classicMortars = v));
            sections.Add(playerTools);

            // Adaptation Section
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

            // Reset All Settings to Preset Section
            if (onReset != null)
            {
                var resetSection = new DifficultySection("DifficultyReset".Translate(), "playstyles");
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
        /// Builds the 4 conditional anomaly sliders (override / inactive / active / study)
        /// that appear identically in:
        ///   - the Custom Difficulty Anomaly section (this helper, via BuildSections)
        ///   - the Dialog_AnomalySettings popup (AnomalySettingsDialogState)
        ///
        /// Both call sites previously duplicated label/info keys, min/max ranges, and the
        /// conditional visibility logic. Now they share a single source of truth.
        ///
        /// Caller wires getter/setter delegates so the same builder can drive either:
        ///   - direct writes to a Difficulty (custom-difficulty path), or
        ///   - writes to local copies committed atomically on Accept (popup path).
        ///
        /// When useEnabledConditions is true, sliders are always returned but disable
        /// themselves when their condition fails (used by the custom-difficulty section,
        /// which displays the section as one continuous list). When false, only the
        /// currently-relevant sliders are returned (used by the popup, which rebuilds
        /// its slider list whenever the playstyle changes).
        /// </summary>
        public static List<DifficultySetting> BuildAnomalySliders(
            Func<AnomalyPlaystyleDef> playstyleGetter,
            Func<float> overrideGetter, Action<float> overrideSetter,
            Func<float> inactiveGetter, Action<float> inactiveSetter,
            Func<float> activeGetter, Action<float> activeSetter,
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

            // Override threat fraction slider — only relevant when an override-style playstyle
            // is selected (e.g., AmbientHorror). Skip entirely in-game where those playstyles
            // can't be picked.
            if (includeOverride)
            {
                AddSlider(new DifficultySliderSetting(
                    "Difficulty_AnomalyThreats_Label".Translate(),
                    "Difficulty_AnomalyThreats_Info".Translate(),
                    overrideGetter, overrideSetter,
                    0f, 1f, 0.01f, ToStringStyle.PercentZero,
                    enabledCondition: useEnabledConditions ? overrideVisible : (Func<bool>)null),
                    overrideVisible);
            }

            // Separate inactive/active threat fraction sliders.
            AddSlider(new DifficultySliderSetting(
                "Difficulty_AnomalyThreatsInactive_Label".Translate(),
                "Difficulty_AnomalyThreatsInactive_Info".Translate(),
                inactiveGetter, inactiveSetter,
                0f, 1f, 0.01f, ToStringStyle.PercentZero,
                enabledCondition: useEnabledConditions ? fractionSlidersVisible : (Func<bool>)null),
                fractionSlidersVisible);

            // Active threats: vanilla embeds the current and 1.5× values into the tooltip
            // text, so the tooltip must be re-evaluated each announcement.
            AddSlider(new DifficultySliderSetting(
                "Difficulty_AnomalyThreatsActive_Label".Translate(),
                tooltipFunc: () => "Difficulty_AnomalyThreatsActive_Info".Translate(
                    Mathf.Clamp01(activeGetter()).ToStringPercent(),
                    Mathf.Clamp01(activeGetter() * 1.5f).ToStringPercent()),
                activeGetter, activeSetter,
                0.1f, 1f, 0.01f, ToStringStyle.PercentZero,
                enabledCondition: useEnabledConditions ? fractionSlidersVisible : (Func<bool>)null),
                fractionSlidersVisible);

            // Study efficiency slider.
            AddSlider(new DifficultySliderSetting(
                "Difficulty_StudyEfficiency_Label".Translate(),
                "Difficulty_StudyEfficiency_Info".Translate(),
                studyGetter, studySetter,
                0f, 5f, 0.01f, ToStringStyle.PercentZero,
                enabledCondition: useEnabledConditions ? studyVisible : (Func<bool>)null),
                studyVisible);

            return result;
        }
    }

    /// <summary>
    /// Represents a section of difficulty settings (e.g., Threats, Economy).
    /// </summary>
    public class DifficultySection
    {
        public string Name { get; }
        public List<DifficultySetting> Settings { get; }
        public string ItemsLabel { get; }

        public DifficultySection(string name, string itemsLabel = "settings")
        {
            Name = name;
            Settings = new List<DifficultySetting>();
            ItemsLabel = itemsLabel;
        }
    }

    /// <summary>
    /// Base class for difficulty settings.
    /// </summary>
    public abstract class DifficultySetting
    {
        public string Label { get; protected set; }
        public string Tooltip { get; protected set; }
        protected Func<bool> enabledCondition;

        public bool IsEnabled => enabledCondition == null || enabledCondition();

        public virtual string GetAdjustmentAnnouncement() => GetAnnouncement();
        public abstract string GetAnnouncement();
        public abstract void Toggle();
        public abstract void Adjust(int direction);
    }

    /// <summary>
    /// Checkbox (boolean) difficulty setting.
    /// </summary>
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

            string invertSuffix = invert ? "_Inverted" : "";
            string capitalizedName = optionName.CapitalizeFirst();
            Label = $"Difficulty_{capitalizedName}{invertSuffix}_Label".Translate();
            Tooltip = $"Difficulty_{capitalizedName}{invertSuffix}_Info".Translate();
        }

        public override string GetAnnouncement()
        {
            if (!IsEnabled)
                return $"{Label}: Disabled";

            bool displayValue = invert ? !getter() : getter();
            string valueStr = displayValue ? "On" : "Off";
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

    /// <summary>
    /// Slider (float) difficulty setting.
    /// </summary>
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
        // Optional dynamic tooltip — re-evaluated each call so it reflects the current
        // slider value (vanilla parity: e.g. AnomalyThreats_Active info embeds the
        // current and 1.5× values into its translated text).
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

            string invertSuffix = reciprocate ? "_Inverted" : "";
            string capitalizedName = optionName.CapitalizeFirst();
            Label = $"Difficulty_{capitalizedName}{invertSuffix}_Label".Translate();
            Tooltip = $"Difficulty_{capitalizedName}{invertSuffix}_Info".Translate();
        }

        public DifficultySliderSetting(string label, string tooltip, Func<float> getter, Action<float> setter,
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

            Label = label;
            Tooltip = tooltip;
        }

        public DifficultySliderSetting(string label, Func<string> tooltipFunc, Func<float> getter, Action<float> setter,
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
            this.tooltipFunc = tooltipFunc;

            Label = label;
            Tooltip = tooltipFunc?.Invoke() ?? "";
        }

        public override string GetAnnouncement()
        {
            if (!IsEnabled)
                return $"{Label}: Disabled";

            float value = getter();
            if (reciprocate)
                value = Reciprocal(value, reciprocalCutoff);
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

        /// <summary>
        /// Adjusts the slider by a percentage of its total possible positions.
        /// </summary>
        /// <param name="percent">Percentage of total positions to move (0.1 = 10%, 0.25 = 25%)</param>
        public void AdjustByPercentOfPositions(float percent)
        {
            if (!IsEnabled) return;

            float current = getter();
            if (reciprocate)
            {
                current = Reciprocal(current, reciprocalCutoff);
            }

            // Calculate total number of discrete positions
            float range = max - min;
            int totalPositions = Mathf.Max(1, Mathf.RoundToInt(range / step));

            // Calculate how many steps to move (at least 1)
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

    /// <summary>
    /// Special setting for resetting all difficulty settings to a preset.
    /// </summary>
    public class DifficultyResetSetting : DifficultySetting
    {
        private readonly Action executeAction;

        public DifficultyResetSetting(string label, string tooltip, Action executeAction)
        {
            Label = label;
            Tooltip = string.IsNullOrEmpty(tooltip) ? "Resets all custom difficulty settings to this preset" : tooltip;
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

    /// <summary>
    /// Setting for selecting an AnomalyPlaystyleDef.
    /// </summary>
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
            Tooltip = "Select an anomaly playstyle";
            options = DefDatabase<AnomalyPlaystyleDef>.AllDefs.ToList();
        }

        public override string GetAnnouncement()
        {
            var current = getter();
            string description = current?.description?.StripTags() ?? "";
            return $"{Label}. {current?.LabelCap ?? "None"}. {description}";
        }

        public override string GetAdjustmentAnnouncement()
        {
            var current = getter();
            string description = current?.description?.StripTags() ?? "";
            return $"{current?.LabelCap ?? "None"}. {description}";
        }

        public override void Toggle() => Adjust(1);

        public override void Adjust(int direction)
        {
            if (options.Count == 0) return;

            var current = getter();
            int currentIndex = options.IndexOf(current);
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = (currentIndex + direction + options.Count) % options.Count;
            var newValue = options[newIndex];

            if (current != null && !current.overrideThreatFraction && newValue.overrideThreatFraction)
            {
                onTransitionToOverride?.Invoke();
            }

            setter(newValue);
            onChanged?.Invoke();
        }
    }
}
