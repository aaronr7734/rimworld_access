using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for ISEKAI RPG LEVELING. The mod draws with custom styles, hand-rolled buttons and a
    /// bitmap title font, so nothing is capturable; every surface reads the mod's data objects and
    /// drives its gated methods. This class holds the core surface (component, stats, ranks) and the
    /// registrations; the tree, forge and window surfaces live in their Isekai*Compat siblings.
    /// </summary>
    internal static class IsekaiCompat
    {
        internal static Type ComponentType;
        internal static Type StatTypeEnum;
        internal static Type MobRankComponentType;

        private static MethodInfo getCachedMethod;
        private static FieldInfo currentLevelField;
        private static FieldInfo currentXpField;
        private static FieldInfo statsField;
        private static FieldInfo passiveTreeField;
        private static FieldInfo weaponMasteryField;
        private static FieldInfo autoDistributeField;
        private static FieldInfo auraDisplayModeField;
        private static FieldInfo auraConstellationField;
        private static PropertyInfo xpToNextProperty;
        private static MethodInfo getRankStringMethod;
        private static MethodInfo autoDistributeMethod;
        private static MethodInfo devAddLevelMethod;

        private static FieldInfo availablePointsField;
        private static MethodInfo getStatMethod;
        private static MethodInfo effectiveMaxStatMethod;
        private static MethodInfo bulkAmountMethod;

        private static MethodInfo statNameMethod;
        private static MethodInfo statAbbreviationMethod;
        private static MethodInfo statDescriptionMethod;
        private static MethodInfo statEffectsMethod;
        private static MethodInfo creatureStatNameMethod;
        private static MethodInfo creatureStatDescriptionMethod;
        private static MethodInfo creatureStatEffectsMethod;

        private static MethodInfo rankStringFromLevelMethod;
        private static MethodInfo progressionTitleMethod;
        private static MethodInfo rankStringMethod;
        private static MethodInfo rankTitleMethod;

        private static FieldInfo mobLevelField;
        private static FieldInfo mobXpField;
        private static FieldInfo mobStatsField;
        private static PropertyInfo mobRankProperty;
        private static PropertyInfo mobIsEliteProperty;
        private static PropertyInfo mobXpToNextProperty;

        private static MethodInfo formatNumMethod;
        private static MethodInfo currentTitleNameMethod;
        private static PropertyInfo weaponMasteryEnabledProperty;
        private static PropertyInfo forgeEnabledProperty;
        private static MethodInfo initializePawnStatsMethod;
        private static float? eliteStatFactor;
        private static float? eliteArmorBonus;
        private static float? eliteHealthFactor;

        internal static readonly LazyReflectionGate CoreGate =
            new LazyReflectionGate("Isekai core", surface =>
            {
                Type component = surface.Type("IsekaiLeveling.IsekaiComponent");
                Type allocation = surface.Type("IsekaiLeveling.IsekaiStatAllocation");
                Type statType = surface.Type("IsekaiLeveling.IsekaiStatType");
                Type statInfo = surface.Type("IsekaiLeveling.IsekaiStatInfo");
                Type rankUtility = surface.Type("IsekaiLeveling.MobRanking.MobRankUtility");
                Type rankTier = surface.Type("IsekaiLeveling.MobRanking.MobRankTier");
                Type mobComponent = surface.Type("IsekaiLeveling.MobRanking.MobRankComponent");
                Type numberFormatting = surface.Type("IsekaiLeveling.NumberFormatting");
                Type titleApplier = surface.Type("IsekaiLeveling.IsekaiTitleApplier");
                Type settings = surface.Type("IsekaiLeveling.IsekaiLevelingSettings");
                Type generator = surface.Type("IsekaiLeveling.PawnStatGenerator");

                MethodInfo getCached = surface.Method(component, "GetCached", new[] { typeof(Pawn) });
                FieldInfo currentLevel = surface.Field(component, "currentLevel");
                FieldInfo currentXp = surface.Field(component, "currentXP");
                FieldInfo stats = surface.Field(component, "stats");
                FieldInfo passiveTree = surface.Field(component, "passiveTree");
                FieldInfo weaponMastery = surface.Field(component, "weaponMastery");
                FieldInfo autoDistribute = surface.Field(component, "autoDistributeStats");
                FieldInfo auraMode = surface.Field(component, "auraDisplayMode");
                FieldInfo auraConstellation = surface.Field(component, "auraConstellation");
                PropertyInfo xpToNext = surface.Property(component, "XPToNextLevel");
                MethodInfo getRankString = surface.Method(component, "GetRankString", Type.EmptyTypes);
                MethodInfo autoDistributeByClass = surface.Method(component, "AutoDistributeByClass", Type.EmptyTypes);
                MethodInfo devAddLevel = surface.Method(component, "DevAddLevel", new[] { typeof(int) });

                FieldInfo availablePoints = surface.Field(allocation, "availableStatPoints");
                MethodInfo getStat = statType == null ? null : surface.Method(allocation, "GetStat", new[] { statType });
                MethodInfo effectiveMax = surface.Method(allocation, "GetEffectiveMaxStat", Type.EmptyTypes);
                MethodInfo bulkAmount = surface.Method(allocation, "GetBulkAmount", Type.EmptyTypes);

                MethodInfo statName = statType == null ? null : surface.Method(statInfo, "GetStatName", new[] { statType });
                MethodInfo statAbbreviation = statType == null ? null : surface.Method(statInfo, "GetStatAbbreviation", new[] { statType });
                MethodInfo statDescription = statType == null ? null : surface.Method(statInfo, "GetStatDescription", new[] { statType });
                MethodInfo statEffects = statType == null ? null : surface.Method(statInfo, "GetStatEffects", new[] { statType, typeof(int) });
                MethodInfo creatureName = statType == null ? null : surface.Method(statInfo, "GetCreatureStatName", new[] { statType });
                MethodInfo creatureDescription = statType == null ? null : surface.Method(statInfo, "GetCreatureStatDescription", new[] { statType });
                MethodInfo creatureEffects = statType == null ? null : surface.Method(statInfo, "GetCreatureStatEffects", new[] { statType, typeof(int), typeof(float) });

                MethodInfo rankFromLevel = surface.Method(rankUtility, "RankStringFromLevel", new[] { typeof(int) });
                MethodInfo progressionTitle = surface.Method(rankUtility, "GetProgressionTitle", new[] { typeof(string) });
                MethodInfo rankString = rankTier == null ? null : surface.Method(rankUtility, "GetRankString", new[] { rankTier });
                MethodInfo rankTitle = rankTier == null ? null : surface.Method(rankUtility, "GetRankTitle", new[] { rankTier });

                FieldInfo mobLevel = surface.Field(mobComponent, "currentLevel");
                FieldInfo mobXp = surface.Field(mobComponent, "currentXP");
                FieldInfo mobStats = surface.Field(mobComponent, "stats");
                PropertyInfo mobRank = surface.Property(mobComponent, "Rank");
                PropertyInfo mobIsElite = surface.Property(mobComponent, "IsElite");
                PropertyInfo mobXpToNext = surface.Property(mobComponent, "XPToNextLevel");

                MethodInfo formatNum = surface.Method(numberFormatting, "FormatNum", new[] { typeof(int) });
                MethodInfo currentTitleName = component == null ? null : surface.Method(titleApplier, "CurrentTitleName", new[] { component });
                PropertyInfo masteryEnabled = surface.Property(settings, "EnableWeaponMastery");
                PropertyInfo forgeEnabled = surface.Property(settings, "EnableForgeSystem");
                MethodInfo initializeStats = component == null ? null
                    : surface.Method(generator, "InitializePawnStats", new[] { typeof(Pawn), component });

                if (!surface.Ready)
                    return false;

                ComponentType = component;
                StatTypeEnum = statType;
                MobRankComponentType = mobComponent;
                getCachedMethod = getCached;
                currentLevelField = currentLevel;
                currentXpField = currentXp;
                statsField = stats;
                passiveTreeField = passiveTree;
                weaponMasteryField = weaponMastery;
                autoDistributeField = autoDistribute;
                auraDisplayModeField = auraMode;
                auraConstellationField = auraConstellation;
                xpToNextProperty = xpToNext;
                getRankStringMethod = getRankString;
                autoDistributeMethod = autoDistributeByClass;
                devAddLevelMethod = devAddLevel;
                availablePointsField = availablePoints;
                getStatMethod = getStat;
                effectiveMaxStatMethod = effectiveMax;
                bulkAmountMethod = bulkAmount;
                statNameMethod = statName;
                statAbbreviationMethod = statAbbreviation;
                statDescriptionMethod = statDescription;
                statEffectsMethod = statEffects;
                creatureStatNameMethod = creatureName;
                creatureStatDescriptionMethod = creatureDescription;
                creatureStatEffectsMethod = creatureEffects;
                rankStringFromLevelMethod = rankFromLevel;
                progressionTitleMethod = progressionTitle;
                rankStringMethod = rankString;
                rankTitleMethod = rankTitle;
                mobLevelField = mobLevel;
                mobXpField = mobXp;
                mobStatsField = mobStats;
                mobRankProperty = mobRank;
                mobIsEliteProperty = mobIsElite;
                mobXpToNextProperty = mobXpToNext;
                formatNumMethod = formatNum;
                currentTitleNameMethod = currentTitleName;
                weaponMasteryEnabledProperty = masteryEnabled;
                forgeEnabledProperty = forgeEnabled;
                initializePawnStatsMethod = initializeStats;

                // Version-optional: the elite constants arrived with the creature window's tooltip.
                Type statPatches = AccessTools.TypeByName("IsekaiLeveling.MobRanking.MobRankStatPatches");
                eliteStatFactor = ConstOf(statPatches, "EliteStatFactor");
                eliteArmorBonus = ConstOf(statPatches, "EliteArmorBonus");
                eliteHealthFactor = ConstOf(statPatches, "EliteHealthFactor");
                return true;
            });

        /// <summary>The six stats in the order every mod screen lays them out (STR, VIT, DEX, INT, WIS, CHA), as boxed enum values.</summary>
        internal static readonly int[] StatDisplayOrder = { 0, 2, 1, 3, 4, 5 };

        internal static object StatEnum(int ordinal)
        {
            return Enum.ToObject(StatTypeEnum, ordinal);
        }

        // ---- IsekaiComponent ----

        internal static object ComponentOf(Pawn pawn)
        {
            return pawn == null || !CoreGate.Ensure() ? null : getCachedMethod.Invoke(null, new object[] { pawn });
        }

        internal static int Level(object comp) => (int)currentLevelField.GetValue(comp);
        internal static int CurrentXp(object comp) => (int)currentXpField.GetValue(comp);
        internal static int XpToNext(object comp) => (int)xpToNextProperty.GetValue(comp, null);
        internal static object StatsOf(object comp) => statsField.GetValue(comp);
        internal static object PassiveTreeOf(object comp) => passiveTreeField.GetValue(comp);
        internal static object WeaponMasteryOf(object comp) => weaponMasteryField.GetValue(comp);
        internal static bool AutoDistribute(object comp) => (bool)autoDistributeField.GetValue(comp);
        internal static string RankString(object comp) => getRankStringMethod.Invoke(comp, null) as string;
        internal static string AuraConstellation(object comp) => auraConstellationField.GetValue(comp) as string;
        internal static string AuraDisplayModeName(object comp) => auraDisplayModeField.GetValue(comp)?.ToString();

        internal static void SetAutoDistribute(object comp, bool value)
        {
            // MUTATION-C: mirrors the checkbox handler in ITab_IsekaiStats.DrawStatPanel (Widgets.Checkbox
            // bound to comp.autoDistributeStats, then AutoDistributeByClass when turning on with
            // points available); the mod exposes no setter, the checkbox writes the field directly.
            autoDistributeField.SetValue(comp, value);
            if (value && AvailablePoints(StatsOf(comp)) > 0)
                autoDistributeMethod.Invoke(comp, null);
        }

        internal static void DevAddLevel(object comp, int levels)
        {
            devAddLevelMethod.Invoke(comp, new object[] { levels });
        }

        // ---- IsekaiStatAllocation ----

        internal static int AvailablePoints(object stats) => (int)availablePointsField.GetValue(stats);
        internal static int StatValue(object stats, int ordinal) => (int)getStatMethod.Invoke(stats, new[] { StatEnum(ordinal) });
        internal static int EffectiveMaxStat() => (int)effectiveMaxStatMethod.Invoke(null, null);

        /// <summary>The mod's own Shift=5 / Ctrl=20 / Ctrl+Shift=100 bulk step, read off Event.current's modifiers.</summary>
        internal static int BulkAmount() => (int)bulkAmountMethod.Invoke(null, null);

        internal static void AddAvailablePoints(object stats, int amount)
        {
            // MUTATION-C: mirrors the god-mode "+10 SP" button in ITab_IsekaiStats.DrawStatPanel
            // (comp.stats.availableStatPoints += 10); a raw write in the mod with no method behind it.
            availablePointsField.SetValue(stats, AvailablePoints(stats) + amount);
        }

        // ---- IsekaiStatInfo ----

        internal static string StatName(int ordinal) => statNameMethod.Invoke(null, new[] { StatEnum(ordinal) }) as string;
        internal static string StatAbbreviation(int ordinal) => statAbbreviationMethod.Invoke(null, new[] { StatEnum(ordinal) }) as string;
        internal static string StatDescription(int ordinal) => statDescriptionMethod.Invoke(null, new[] { StatEnum(ordinal) }) as string;
        internal static string StatEffects(int ordinal, int value) => statEffectsMethod.Invoke(null, new[] { StatEnum(ordinal), value }) as string;
        internal static string CreatureStatName(int ordinal) => creatureStatNameMethod.Invoke(null, new[] { StatEnum(ordinal) }) as string;
        internal static string CreatureStatDescription(int ordinal) => creatureStatDescriptionMethod.Invoke(null, new[] { StatEnum(ordinal) }) as string;
        internal static string CreatureStatEffects(int ordinal, int value) => creatureStatEffectsMethod.Invoke(null, new object[] { StatEnum(ordinal), value, 1f }) as string;

        // ---- Ranks ----

        internal static string RankStringForLevel(int level) => rankStringFromLevelMethod.Invoke(null, new object[] { level }) as string;

        /// <summary>The rank letter through the mod's own Isekai_Rank_* keys, as every mod screen shows it.</summary>
        internal static string RankTranslated(string rank) => CompatText.ModText("Isekai_Rank_" + rank);

        internal static string ProgressionTitle(string rank) => progressionTitleMethod.Invoke(null, new object[] { rank }) as string;

        internal static string LevelRankLine(int level)
        {
            return CompatText.ModArgs("Isekai_LevelRankDisplay", level, RankTranslated(RankStringForLevel(level)));
        }

        internal static string XpLine(int current, int toNext)
        {
            return "RimWorldAccess.Compat.Isekai.XpLine".Translate(FormatNum(current), FormatNum(toNext));
        }

        internal static string FormatNum(int value) => formatNumMethod.Invoke(null, new object[] { value }) as string;

        internal static string CurrentTitleName(object comp) => currentTitleNameMethod.Invoke(null, new[] { comp }) as string;

        internal static bool WeaponMasteryEnabled => (bool)weaponMasteryEnabledProperty.GetValue(null, null);

        /// <summary>Vehicle A: the character-creation panel's Reroll button runs exactly this.</summary>
        internal static void RerollStartingStats(Pawn pawn, object comp) => initializePawnStatsMethod.Invoke(null, new[] { pawn, comp });
        internal static bool ForgeEnabled => (bool)forgeEnabledProperty.GetValue(null, null);

        // ---- MobRankComponent ----

        internal static object MobRankOf(Pawn pawn)
        {
            if (pawn == null || !CoreGate.Ensure())
                return null;
            foreach (ThingComp comp in pawn.AllComps)
            {
                if (MobRankComponentType.IsInstanceOfType(comp))
                    return comp;
            }
            return null;
        }

        internal static int MobLevel(object comp) => (int)mobLevelField.GetValue(comp);
        internal static int MobXp(object comp) => (int)mobXpField.GetValue(comp);
        internal static int MobXpToNext(object comp) => (int)mobXpToNextProperty.GetValue(comp, null);
        internal static object MobStats(object comp) => mobStatsField.GetValue(comp);
        internal static bool MobIsElite(object comp) => (bool)mobIsEliteProperty.GetValue(comp, null);
        internal static string MobRankString(object comp) => rankStringMethod.Invoke(null, new[] { mobRankProperty.GetValue(comp, null) }) as string;

        internal static string EliteTooltip()
        {
            if (eliteStatFactor == null || eliteArmorBonus == null || eliteHealthFactor == null)
                return "RimWorldAccess.Compat.Isekai.Elite".Translate();
            return Shell.CompatText.ModArgs("Isekai_CreatureStats_EliteTooltip",
                ((eliteStatFactor.Value - 1f) * 100f).ToString("F0"),
                (eliteArmorBonus.Value * 100f).ToString("F0"),
                ((eliteHealthFactor.Value - 1f) * 100f).ToString("F0"));
        }

        private static float? ConstOf(Type type, string name)
        {
            FieldInfo field = type == null ? null : AccessTools.Field(type, name);
            return field != null && field.IsLiteral ? (float?)(float)field.GetRawConstantValue() : null;
        }

        internal static string MobRankTitle(object comp) => rankTitleMethod.Invoke(null, new[] { mobRankProperty.GetValue(comp, null) }) as string;

        // ---- Registration ----

        public static void RegisterTabAdapters()
        {
            CompatRegistration.TabAdapter("IsekaiLeveling.UI.ITab_IsekaiStats",
                type => new IsekaiStatusTabAdapter(type), "Isekai status tab");
            CompatRegistration.TabAdapter("IsekaiLeveling.UI.ITab_CreatureStats",
                type => new IsekaiCreatureTabAdapter(type), "Isekai creature tab");
        }

        public static void RegisterWindowScopes()
        {
            RegisterScope("IsekaiLeveling.UI.Window_StatsAttribution",
                w => new IsekaiStatWindowScope(w, IsekaiWindowCompat.PawnStatWindow), "stat window");
            RegisterScope("IsekaiLeveling.UI.Window_CreatureStats",
                w => new IsekaiStatWindowScope(w, IsekaiWindowCompat.CreatureStatWindow), "creature stat window");
            RegisterScope("IsekaiLeveling.UI.Window_SkillTree", w => new IsekaiSkillTreeScope(w), "constellation window");
            RegisterScope("IsekaiLeveling.UI.Window_Mastery", w => new IsekaiMasteryScope(w), "mastery window");
            RegisterScope("IsekaiLeveling.Forge.Window_Forge", w => new IsekaiForgeScope(w), "forge window");
            RegisterScope("IsekaiLeveling.Forge.Window_RunicStation", w => new IsekaiRunicStationScope(w), "runic station window");
        }

        private static void RegisterScope(string typeName, Func<Window, FocusScope> factory, string logName)
        {
            try
            {
                Type windowType = AccessTools.TypeByName(typeName);
                if (windowType == null)
                    return;
                ScopeForWindow.RegisterHierarchy(windowType, factory);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Isekai compat (" + logName + ") registration failed: " + ex.Message);
            }
        }

        public static void RegisterCharacterCreation()
        {
            try
            {
                IsekaiCharacterCreationExtender.Register();
            }
            catch (Exception ex)
            {
                ModLogger.Error("Isekai compat (character creation) registration failed: " + ex.Message);
            }
        }

        public static void RegisterAffectedStatsCapture(Harmony harmony)
        {
            try
            {
                IsekaiAffectedStatsCapture.Register(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Isekai compat (affected stats capture) registration failed: " + ex.Message);
            }
        }
    }
}
