using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Reflection-only surface for Character Development (WantsAndQuirks).</summary>
    internal static class WqCompat
    {
        private static readonly Type stateType;
        private static readonly Type rewardNodeType;
        private static readonly Type rewardDefType;
        private static readonly Type rewardWorkerType;
        private static readonly Type mainTabType;
        private static readonly Type modType;
        private static readonly Type settingsType;
        private static readonly Type utilityType;
        private static readonly Type wantsDataType;
        private static readonly Type activeWantType;
        private static readonly Type wantDefType;
        private static readonly Type quirkType;
        private static readonly Type defsOfType;
        private static readonly Type itabType;

        private static readonly FieldInfo rewardNodesField;
        private static readonly FieldInfo rewardPointsField;
        private static readonly FieldInfo characterPointsField;

        private static readonly FieldInfo nodeDefField;
        private static readonly PropertyInfo nodeLabelCapProp;
        private static readonly PropertyInfo nodeDescriptionProp;
        private static readonly FieldInfo defRarityField;
        private static readonly PropertyInfo rewardWorkerProp;
        private static readonly MethodInfo workerOnRemovedMethod;

        private static readonly MethodInfo claimRewardMethod;
        private static readonly FieldInfo modSettingsField;
        private static readonly FieldInfo pawnSpecificPointsField;
        private static readonly FieldInfo charactersMenuEnabledField;
        private static readonly FieldInfo rerollsPerWantField;

        private static readonly MethodInfo canHaveWantsMethod;
        private static readonly MethodInfo getWantsDataMethod;
        private static readonly FieldInfo activeWantsField;
        private static readonly FieldInfo quirksField;
        private static readonly FieldInfo pawnRewardPointsField;
        private static readonly FieldInfo pawnCharacterPointsField;
        private static readonly MethodInfo globalPointsNeededMethod;
        private static readonly MethodInfo pawnPointsNeededMethod;
        private static readonly MethodInfo rerollWantMethod;

        private static readonly PropertyInfo wantLabelCapProp;
        private static readonly PropertyInfo wantDescriptionProp;
        private static readonly FieldInfo wantDefField;
        private static readonly FieldInfo wantRerollCountField;
        private static readonly FieldInfo wantRewardField;
        private static readonly FieldInfo wantMentalBreakField;

        private static readonly PropertyInfo quirkLabelCapProp;
        private static readonly PropertyInfo quirkDescriptionProp;
        private static readonly FieldInfo quirkDefField;

        private static readonly FieldInfo wantViewedField;
        private static readonly FieldInfo wantGlowField;
        private static readonly FieldInfo nodeViewedField;
        private static readonly FieldInfo nodeGlowField;
        private static readonly float wantGlowSeconds;
        private static readonly float nodeGlowSeconds;
        private static readonly PropertyInfo wantWorkerProp;
        private static readonly MethodInfo tryGetProgressMethod;

        private static readonly FieldInfo charactersMenuDefField;
        private static readonly FieldInfo rerollSoundField;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type MainTabType => mainTabType;
        public static Type InspectTabType => itabType;

        static WqCompat()
        {
            var surface = new ReflectionSurface("WqCompat");

            stateType = surface.Type("WantsAndQuirks.State");
            rewardNodeType = surface.Type("WantsAndQuirks.RewardNode");
            rewardDefType = surface.Type("WantsAndQuirks.RewardDef");
            rewardWorkerType = surface.Type("WantsAndQuirks.RewardWorker");
            mainTabType = surface.Type("WantsAndQuirks.MainTabWindow_Characters");
            modType = surface.Type("WantsAndQuirks.WantsAndQuirksMod");
            settingsType = surface.Type("WantsAndQuirks.WantsAndQuirksSettings");
            utilityType = surface.Type("WantsAndQuirks.WantsAndQuirksUtility");
            wantsDataType = surface.Type("WantsAndQuirks.PawnWantsData");
            activeWantType = surface.Type("WantsAndQuirks.ActiveWant");
            wantDefType = surface.Type("WantsAndQuirks.WantDef");
            quirkType = surface.Type("WantsAndQuirks.Quirk");
            defsOfType = surface.Type("WantsAndQuirks.DefsOf");
            itabType = surface.Type("WantsAndQuirks.ITab_Pawn_WantsAndQuirks");

            rewardNodesField = surface.Field(stateType, "rewardNodes");
            rewardPointsField = surface.Field(stateType, "rewardPoints");
            characterPointsField = surface.Field(stateType, "characterPoints");

            nodeDefField = surface.Field(rewardNodeType, "def");
            nodeLabelCapProp = surface.Property(rewardNodeType, "LabelCap");
            nodeDescriptionProp = surface.Property(rewardNodeType, "Description");
            defRarityField = surface.Field(rewardDefType, "rarity");
            rewardWorkerProp = surface.Property(rewardDefType, "Worker");
            workerOnRemovedMethod = surface.Method(rewardWorkerType, "OnRemoved");

            claimRewardMethod = surface.Method(mainTabType, "ClaimReward");
            modSettingsField = surface.Field(modType, "settings");
            pawnSpecificPointsField = surface.Field(settingsType, "pawnSpecificRewardPoints");
            charactersMenuEnabledField = surface.Field(settingsType, "enableCharactersMenu");
            rerollsPerWantField = surface.Field(settingsType, "rerollsPerWant");

            canHaveWantsMethod = surface.Method(utilityType, "CanHaveWants");
            getWantsDataMethod = surface.Method(utilityType, "GetWantsData");
            activeWantsField = surface.Field(wantsDataType, "activeWants");
            quirksField = surface.Field(wantsDataType, "quirks");
            pawnRewardPointsField = surface.Field(wantsDataType, "rewardPoints");
            pawnCharacterPointsField = surface.Field(wantsDataType, "characterPoints");
            globalPointsNeededMethod = surface.Method(utilityType, "GetGlobalCharacterPointsNeeded");
            pawnPointsNeededMethod = surface.Method(utilityType, "GetPawnCharacterPointsNeeded");
            rerollWantMethod = surface.Method(utilityType, "RerollWant");

            wantLabelCapProp = surface.Property(activeWantType, "LabelCap");
            wantDescriptionProp = surface.Property(activeWantType, "Description");
            wantDefField = surface.Field(activeWantType, "def");
            wantRerollCountField = surface.Field(activeWantType, "rerollCount");
            wantRewardField = surface.Field(wantDefType, "reward");
            wantMentalBreakField = surface.Field(wantDefType, "isMentalBreakWant");

            quirkLabelCapProp = surface.Property(quirkType, "LabelCap");
            quirkDescriptionProp = surface.Property(quirkType, "Description");
            quirkDefField = surface.Field(quirkType, "def");

            charactersMenuDefField = surface.Field(defsOfType, "WQ_CharactersMenu");
            rerollSoundField = surface.Field(defsOfType, "WQ_RerollSound");

            ready = surface.Ready;

            // Version-optional: the first-view glow and the per-want progress bar.
            wantViewedField = ReflectionSurface.TryFieldOrProperty(activeWantType, "hasBeenViewed") as FieldInfo;
            wantGlowField = ReflectionSurface.TryFieldOrProperty(activeWantType, "glowStartRealTime") as FieldInfo;
            nodeViewedField = ReflectionSurface.TryFieldOrProperty(rewardNodeType, "hasBeenViewed") as FieldInfo;
            nodeGlowField = ReflectionSurface.TryFieldOrProperty(rewardNodeType, "glowStartRealTime") as FieldInfo;
            wantGlowSeconds = ConstSeconds(itabType, "NewWantGlowDuration");
            nodeGlowSeconds = ConstSeconds(mainTabType, "NewRewardGlowDuration");
            wantWorkerProp = ReflectionSurface.TryFieldOrProperty(wantDefType, "Worker") as PropertyInfo;
            tryGetProgressMethod = wantWorkerProp == null ? null
                : AccessTools.Method(wantWorkerProp.PropertyType, "TryGetProgress");
        }

        private static float ConstSeconds(Type type, string name)
        {
            FieldInfo field = type == null ? null : AccessTools.Field(type, name);
            return field != null && field.IsLiteral ? (float)field.GetRawConstantValue() : 0f;
        }

        public static int RewardPoints() => ready ? (int)rewardPointsField.GetValue(null) : 0;
        public static int CharacterPoints() => ready ? (int)characterPointsField.GetValue(null) : 0;

        /// <summary>Reward points are earned per pawn, not colony-wide. Default ON.</summary>
        public static bool PawnSpecificRewardPoints() => SettingFlag(pawnSpecificPointsField);

        public static bool CharactersMenuEnabled() => SettingFlag(charactersMenuEnabledField);

        public static int RerollsPerWant()
        {
            object settings = modSettingsField.GetValue(null);
            return settings == null ? 0 : (int)rerollsPerWantField.GetValue(settings);
        }

        private static bool SettingFlag(FieldInfo field)
        {
            object settings = modSettingsField.GetValue(null);
            return settings != null && (bool)field.GetValue(settings);
        }

        /// <summary>The mod's own escalating target, not the raw setting it starts from.</summary>
        public static int GlobalPointsNeeded()
        {
            return ready ? (int)globalPointsNeededMethod.Invoke(null, new object[0]) : 0;
        }

        public static object WantsData(Pawn pawn)
        {
            return ready ? getWantsDataMethod.Invoke(null, new object[] { pawn }) : null;
        }

        public static int PawnPointsNeeded(Pawn pawn)
        {
            object data = WantsData(pawn);
            return data == null ? 0 : DataPointsNeeded(data);
        }

        public static int PawnRewardPoints(Pawn pawn)
        {
            object data = WantsData(pawn);
            return data == null ? 0 : DataRewardPoints(data);
        }

        public static int DataPointsNeeded(object data) => (int)pawnPointsNeededMethod.Invoke(null, new[] { data });
        public static int DataRewardPoints(object data) => (int)pawnRewardPointsField.GetValue(data);
        public static int DataCharacterPoints(object data) => (int)pawnCharacterPointsField.GetValue(data);

        public static List<object> RewardNodes() => ready ? Snapshot(rewardNodesField.GetValue(null)) : new List<object>();
        public static List<object> Wants(object data) => Snapshot(activeWantsField.GetValue(data));
        public static List<object> Quirks(object data) => Snapshot(quirksField.GetValue(data));

        private static List<object> Snapshot(object source)
        {
            var result = new List<object>();
            if (source is IEnumerable items)
            {
                foreach (object item in items)
                {
                    result.Add(item);
                }
            }
            return result;
        }

        public static string NodeLabel(object node) => nodeLabelCapProp.GetValue(node) as string ?? "";
        public static string NodeDescription(object node) => nodeDescriptionProp.GetValue(node) as string ?? "";

        /// <summary>RewardRarity ordinal: 0 common, 1 uncommon, 2 rare, 3 legendary.</summary>
        public static int NodeRarity(object node)
        {
            object def = nodeDefField.GetValue(node);
            return def == null ? 0 : (int)defRarityField.GetValue(def);
        }

        public static string WantLabel(object want) => wantLabelCapProp.GetValue(want) as string ?? "";
        public static string WantDescription(object want) => wantDescriptionProp.GetValue(want) as string ?? "";
        public static int WantRerollCount(object want) => (int)wantRerollCountField.GetValue(want);
        public static int WantReward(object want) => (int)wantRewardField.GetValue(wantDefField.GetValue(want));
        public static bool WantIsMentalBreak(object want) => (bool)wantMentalBreakField.GetValue(wantDefField.GetValue(want));

        /// <summary>The tab's progress bar, drawn only for a positive target.</summary>
        public static bool TryWantProgress(Pawn pawn, object want, out float fraction)
        {
            fraction = 0f;
            if (tryGetProgressMethod == null)
            {
                return false;
            }
            object worker = wantWorkerProp.GetValue(wantDefField.GetValue(want), null);
            var args = new object[] { pawn, want, 0f, 0f };
            if (worker == null || !(bool)tryGetProgressMethod.Invoke(worker, args))
            {
                return false;
            }
            float target = (float)args[3];
            if (target <= 0f)
            {
                return false;
            }
            fraction = Mathf.Clamp01((float)args[2] / target);
            return true;
        }

        /// <summary>Whether the first-view glow is on this want; stamps it viewed, since this tree can be read without the tab drawing.</summary>
        public static bool TakeWantIsNew(object want)
        {
            if (wantViewedField == null || wantGlowField == null || wantGlowSeconds <= 0f)
            {
                return false;
            }
            if (!(bool)wantViewedField.GetValue(want))
            {
                // MUTATION-C: mirrors ITab_Pawn_WantsAndQuirks.DrawWants's first-draw stamp; no method behind it.
                wantViewedField.SetValue(want, true);
                wantGlowField.SetValue(want, Time.realtimeSinceStartup);
                return true;
            }
            return IsGlowing((float)wantGlowField.GetValue(want), wantGlowSeconds);
        }

        public static bool NodeIsNew(object node)
        {
            if (nodeViewedField == null || nodeGlowField == null || nodeGlowSeconds <= 0f)
            {
                return false;
            }
            return !(bool)nodeViewedField.GetValue(node)
                || IsGlowing((float)nodeGlowField.GetValue(node), nodeGlowSeconds);
        }

        private static bool IsGlowing(float glowStart, float seconds)
        {
            return glowStart >= 0f && Time.realtimeSinceStartup - glowStart < seconds;
        }

        public static string QuirkLabel(object quirk) => quirkLabelCapProp.GetValue(quirk) as string ?? "";
        public static string QuirkDescription(object quirk) => quirkDescriptionProp.GetValue(quirk) as string ?? "";

        public static MainButtonDef CharactersMenuDef => charactersMenuDefField.GetValue(null) as MainButtonDef;
        public static SoundDef RerollSound => rerollSoundField.GetValue(null) as SoundDef;

        /// <summary>The tab's own gated claim: its reject messages, then the recipient picker.</summary>
        public static void ClaimReward(Window mainTab, object node)
        {
            claimRewardMethod.Invoke(mainTab, new[] { node });
        }

        public static void RerollWant(Pawn pawn, object data, object want)
        {
            rerollWantMethod.Invoke(null, new object[] { pawn, data, want });
        }

        // MUTATION-C: mirrors ITab_Pawn_WantsAndQuirks.DrawWants's X button, an ungated list
        // removal written inline with no callable method behind it.
        public static void RemoveWant(object data, object want)
        {
            (activeWantsField.GetValue(data) as IList)?.Remove(want);
        }

        // MUTATION-C: mirrors ITab_Pawn_WantsAndQuirks.DrawQuirks's confirmed x button, whose
        // worker callback and list removal are written inline with no callable method behind them.
        public static void RemoveQuirk(Pawn pawn, object data, object quirk)
        {
            object def = quirkDefField.GetValue(quirk);
            object worker = def == null ? null : rewardWorkerProp.GetValue(def);
            if (worker != null)
            {
                workerOnRemovedMethod.Invoke(worker, new object[] { pawn, quirk });
            }
            (quirksField.GetValue(data) as IList)?.Remove(quirk);
        }

        public static bool CanHaveWants(Pawn pawn)
        {
            return ready && (bool)canHaveWantsMethod.Invoke(null, new object[] { pawn });
        }

        public static int ActiveWantCount(Pawn pawn)
        {
            object data = WantsData(pawn);
            return data == null ? 0 : (activeWantsField.GetValue(data) as IList)?.Count ?? 0;
        }
    }
}
