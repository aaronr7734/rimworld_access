using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One stat-allocation window's private surface. Window_StatsAttribution (pawns) and
    /// Window_CreatureStats (ranked creatures) are the same window twice, down to the field
    /// names; only the component they read differs, so one scope drives both through this.
    /// </summary>
    internal sealed class IsekaiStatWindowAdapter
    {
        private static readonly string[] PendingFieldNames =
            { "pendingSTR", "pendingDEX", "pendingVIT", "pendingINT", "pendingWIS", "pendingCHA" };

        internal readonly bool IsCreature;
        private readonly string typeName;
        private readonly string componentFieldName;

        private Type windowType;
        private FieldInfo pawnField;
        private FieldInfo componentField;
        private FieldInfo pointsSpentField;
        private readonly FieldInfo[] pendingFields = new FieldInfo[6];
        private MethodInfo recalculateMethod;
        private MethodInfo canDecreaseMethod;
        private MethodInfo originalStatMethod;
        private MethodInfo applyMethod;
        private bool resolved;
        private bool ready;

        internal IsekaiStatWindowAdapter(string typeName, string componentFieldName, bool isCreature)
        {
            this.typeName = typeName;
            this.componentFieldName = componentFieldName;
            IsCreature = isCreature;
        }

        internal bool Ready
        {
            get
            {
                if (resolved)
                    return ready;
                resolved = true;
                if (!IsekaiCompat.CoreGate.Ensure())
                    return false;

                var surface = new ReflectionSurface("IsekaiStatWindowAdapter:" + typeName);
                windowType = surface.Type(typeName);
                pawnField = surface.Field(windowType, "pawn");
                componentField = surface.Field(windowType, componentFieldName);
                pointsSpentField = surface.Field(windowType, "pointsSpent");
                for (int i = 0; i < PendingFieldNames.Length; i++)
                    pendingFields[i] = surface.Field(windowType, PendingFieldNames[i]);
                recalculateMethod = surface.Method(windowType, "RecalculatePointsSpent", Type.EmptyTypes);
                canDecreaseMethod = surface.Method(windowType, "CanDecrease", new[] { IsekaiCompat.StatTypeEnum });
                originalStatMethod = surface.Method(windowType, "GetOriginalStat", new[] { IsekaiCompat.StatTypeEnum });
                applyMethod = surface.Method(windowType, "ApplyChanges", Type.EmptyTypes);
                ready = surface.Ready;
                return ready;
            }
        }

        internal Pawn PawnOf(Window window) => pawnField.GetValue(window) as Pawn;
        internal object ComponentOf(Window window) => componentField.GetValue(window);

        internal object StatsOf(Window window)
        {
            object comp = ComponentOf(window);
            if (comp == null)
                return null;
            return IsCreature ? IsekaiCompat.MobStats(comp) : IsekaiCompat.StatsOf(comp);
        }

        internal int LevelOf(Window window)
        {
            object comp = ComponentOf(window);
            return IsCreature ? IsekaiCompat.MobLevel(comp) : IsekaiCompat.Level(comp);
        }

        internal int XpOf(Window window)
        {
            object comp = ComponentOf(window);
            return IsCreature ? IsekaiCompat.MobXp(comp) : IsekaiCompat.CurrentXp(comp);
        }

        internal int XpToNextOf(Window window)
        {
            object comp = ComponentOf(window);
            return IsCreature ? IsekaiCompat.MobXpToNext(comp) : IsekaiCompat.XpToNext(comp);
        }

        /// <summary>Level and rank as the window's header shows them: the ladder rank for pawns, the ranked creature's own tier otherwise.</summary>
        internal string LevelRankLine(Window window)
        {
            object comp = ComponentOf(window);
            if (comp == null)
                return "";
            if (!IsCreature)
                return IsekaiCompat.LevelRankLine(IsekaiCompat.Level(comp));
            string line = Shell.CompatText.ModArgs("Isekai_LevelRankDisplay", IsekaiCompat.MobLevel(comp), IsekaiCompat.MobRankString(comp));
            if (!IsekaiCompat.MobIsElite(comp))
                return line;
            return Shell.CompatText.JoinSentences(new List<string> { line, IsekaiCompat.EliteTooltip() });
        }

        internal int Pending(Window window, int ordinal) => (int)pendingFields[ordinal].GetValue(window);
        internal int PointsSpent(Window window) => (int)pointsSpentField.GetValue(window);
        internal bool CanDecrease(Window window, int ordinal) => (bool)canDecreaseMethod.Invoke(window, new[] { IsekaiCompat.StatEnum(ordinal) });
        internal int OriginalStat(Window window, int ordinal) => (int)originalStatMethod.Invoke(window, new[] { IsekaiCompat.StatEnum(ordinal) });

        internal void SetPending(Window window, int ordinal, int value)
        {
            // MUTATION-C: mirrors the window's own +/- handlers (Window_StatsAttribution.DrawStatRow,
            // Window_CreatureStats.DrawStatRow): the pending value is the window's input-surrogate
            // state, written inline by those handlers and followed by RecalculatePointsSpent.
            pendingFields[ordinal].SetValue(window, value);
            recalculateMethod.Invoke(window, null);
        }

        /// <summary>Vehicle A: the window's own Apply, exactly what its confirm button runs.</summary>
        internal void Apply(Window window) => applyMethod.Invoke(window, null);
    }

    /// <summary>Reflection surface over the mod's windows: the two stat windows, the constellation window, the mastery window, and the openers the status tab uses.</summary>
    internal static class IsekaiWindowCompat
    {
        internal static readonly IsekaiStatWindowAdapter PawnStatWindow =
            new IsekaiStatWindowAdapter("IsekaiLeveling.UI.Window_StatsAttribution", "comp", false);

        internal static readonly IsekaiStatWindowAdapter CreatureStatWindow =
            new IsekaiStatWindowAdapter("IsekaiLeveling.UI.Window_CreatureStats", "rankComp", true);

        internal static Type SkillTreeWindowType;
        private static FieldInfo treePawnField;
        private static FieldInfo treeCompField;
        private static FieldInfo currentTreeField;
        private static FieldInfo selectedClassField;
        private static FieldInfo selectedNodeIdField;
        private static FieldInfo panOffsetField;
        private static FieldInfo zoomField;
        private static FieldInfo allClassesField;
        private static MethodInfo loadTreeMethod;
        private static MethodInfo tryUnlockWithChainMethod;
        private static MethodInfo formatBonusMethod;
        private static MethodInfo nodeTypeLabelMethod;
        private static MethodInfo bonusTypeNameMethod;
        private static MethodInfo isInvertedStatMethod;

        private static FieldInfo masteryPawnField;
        private static MethodInfo allWeaponDefsMethod;

        private static MethodInfo openAuraMenuMethod;

        internal static readonly LazyReflectionGate SkillTreeGate =
            new LazyReflectionGate("Isekai constellation window", surface =>
            {
                if (!IsekaiTreeCompat.Gate.Ensure())
                    return false;
                Type window = surface.Type("IsekaiLeveling.UI.Window_SkillTree");
                FieldInfo pawn = surface.Field(window, "pawn");
                FieldInfo comp = surface.Field(window, "comp");
                FieldInfo currentTree = surface.Field(window, "currentTree");
                FieldInfo selectedClass = surface.Field(window, "selectedClass");
                FieldInfo selectedNodeId = surface.Field(window, "selectedNodeId");
                FieldInfo panOffset = surface.Field(window, "panOffset");
                FieldInfo zoom = surface.Field(window, "zoom");
                FieldInfo allClasses = surface.Field(window, "ALL_CLASSES");
                MethodInfo loadTree = surface.Method(window, "LoadTree", new[] { typeof(string) });
                MethodInfo tryUnlock = surface.Method(window, "TryUnlockWithChain", new[] { IsekaiTreeCompat.NodeType });
                MethodInfo formatBonus = surface.Method(window, "FormatBonus", new[] { surface.Type("IsekaiLeveling.SkillTree.PassiveBonus") });
                MethodInfo nodeTypeLabel = surface.Method(window, "GetNodeTypeLabel", new[] { IsekaiTreeCompat.NodeTypeEnum });
                MethodInfo bonusTypeName = surface.Method(window, "GetBonusTypeName", new[] { IsekaiTreeCompat.BonusTypeEnum });
                MethodInfo isInverted = surface.Method(window, "IsInvertedStat", new[] { IsekaiTreeCompat.BonusTypeEnum });
                if (!surface.Ready)
                    return false;

                SkillTreeWindowType = window;
                treePawnField = pawn;
                treeCompField = comp;
                currentTreeField = currentTree;
                selectedClassField = selectedClass;
                selectedNodeIdField = selectedNodeId;
                panOffsetField = panOffset;
                zoomField = zoom;
                allClassesField = allClasses;
                loadTreeMethod = loadTree;
                tryUnlockWithChainMethod = tryUnlock;
                formatBonusMethod = formatBonus;
                nodeTypeLabelMethod = nodeTypeLabel;
                bonusTypeNameMethod = bonusTypeName;
                isInvertedStatMethod = isInverted;
                return true;
            });

        internal static readonly LazyReflectionGate MasteryGate =
            new LazyReflectionGate("Isekai mastery window", surface =>
            {
                Type window = surface.Type("IsekaiLeveling.UI.Window_Mastery");
                FieldInfo pawn = surface.Field(window, "pawn");
                MethodInfo allWeapons = surface.Method(window, "GetAllWeaponDefs", Type.EmptyTypes);
                if (!surface.Ready)
                    return false;
                masteryPawnField = pawn;
                allWeaponDefsMethod = allWeapons;
                return true;
            });

        internal static readonly LazyReflectionGate AuraMenuGate =
            new LazyReflectionGate("Isekai aura menu", surface =>
            {
                if (!IsekaiCompat.CoreGate.Ensure())
                    return false;
                Type tab = surface.Type("IsekaiLeveling.UI.ITab_IsekaiStats");
                MethodInfo open = surface.Method(tab, "OpenAuraMenu", new[] { IsekaiCompat.ComponentType });
                if (!surface.Ready)
                    return false;
                openAuraMenuMethod = open;
                return true;
            });

        // ---- Constellation window ----

        internal static Pawn TreePawn(Window w) => treePawnField.GetValue(w) as Pawn;
        internal static object TreeComponent(Window w) => treeCompField.GetValue(w);
        internal static object CurrentTree(Window w) => currentTreeField.GetValue(w);
        internal static string SelectedClass(Window w) => selectedClassField.GetValue(w) as string;
        internal static string SelectedNodeId(Window w) => selectedNodeIdField.GetValue(w) as string;
        internal static string[] AllClasses() => allClassesField.GetValue(null) as string[];

        internal static void SelectClass(Window w, string treeClass)
        {
            // MUTATION-C: mirrors the class-tab click in Window_SkillTree.DrawClassPanel
            // (selectedClass = cls; LoadTree(cls)); both are the window's own view state.
            selectedClassField.SetValue(w, treeClass);
            loadTreeMethod.Invoke(w, new object[] { treeClass });
        }

        internal static void SelectNode(Window w, string nodeId)
        {
            // MUTATION-C: mirrors the node click in Window_SkillTree.DrawAllNodes (selectedNodeId =
            // node.nodeId), the window's own view state that drives its detail panel.
            selectedNodeIdField.SetValue(w, nodeId);
        }

        /// <summary>
        /// Pans the tree so the node sits at the container's centre; GridToScreen is
        /// centre + grid * GRID_SCALE * zoom + panOffset with Y inverted, so the inverse offset
        /// lands the node exactly there.
        /// </summary>
        internal static void PanToNode(Window w, float gridX, float gridY)
        {
            // MUTATION-C: mirrors Window_SkillTree.HandlePanZoom's drag write of panOffset; the
            // keyboard has no drag, and a viewer must see the focused node.
            const float gridScale = 70f;
            float zoom = (float)zoomField.GetValue(w);
            panOffsetField.SetValue(w, new Vector2(-gridX * gridScale * zoom, gridY * gridScale * zoom));
        }

        /// <summary>Vehicle A: the window's one unlock entry point (direct unlock, else chain), messages and sounds included.</summary>
        internal static void TryUnlockWithChain(Window w, object node) => tryUnlockWithChainMethod.Invoke(w, new[] { node });

        internal static string FormatBonus(Window w, object bonus) => formatBonusMethod.Invoke(w, new[] { bonus }) as string;
        internal static string NodeTypeLabel(Window w, object nodeType) => nodeTypeLabelMethod.Invoke(w, new[] { nodeType }) as string;
        internal static string BonusTypeName(Window w, object bonusType) => bonusTypeNameMethod.Invoke(w, new[] { bonusType }) as string;
        internal static bool IsInvertedStat(Window w, object bonusType) => (bool)isInvertedStatMethod.Invoke(w, new[] { bonusType });

        // ---- Mastery window ----

        internal static Pawn MasteryPawn(Window w) => masteryPawnField.GetValue(w) as Pawn;
        internal static IList AllWeaponDefs(Window w) => allWeaponDefsMethod.Invoke(w, null) as IList;

        // ---- Openers (vehicle A: the same constructor the tab's buttons call) ----

        internal static void OpenPawnWindow(string typeName, Pawn pawn)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null || pawn == null)
                return;
            Window window = Activator.CreateInstance(type, pawn) as Window;
            if (window == null)
                return;
            // The Character Forge absorbs no input, so the generic reader attaches only under the
            // deliberate-open watch the guard arms; the bespoke scopes are unaffected.
            RimWorldAccess.Shell.ScopeDelegateGuard.Run(delegate
            {
                Find.WindowStack.Add(window);
            });
        }

        internal static void OpenAuraMenu(object comp)
        {
            if (!AuraMenuGate.Ensure())
                return;
            RimWorldAccess.Shell.ScopeDelegateGuard.Run(delegate
            {
                openAuraMenuMethod.Invoke(null, new[] { comp });
            });
        }
    }
}
