using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    internal static partial class CmrCompat
    {
        /// <summary>
        /// The shortcut rows ("All", "Predators", "Stone"...) above every def list in the Hunting,
        /// Foraging, Forestry, Mining and Production tabs, read from the mod's own decision objects:
        /// each tab's <c>ShortcutCategory&lt;T&gt;</c> array, <c>ManagerTab.IsShortcutRowVisible</c>
        /// for which rows are drawn, and <c>ManagerTab.ApplyShortcutClick&lt;T&gt;</c> for what a
        /// press does in each modifier mode. Its own surface, so a rename here costs only the
        /// shortcut rows, never a tab's other controls.
        ///
        /// MUTATION VEHICLE. <see cref="Apply{T}"/> invokes <c>ApplyShortcutClick</c>, the row's own
        /// click body (doctrine A), with the per-option setter the tab hands it.
        /// </summary>
        internal static class Shortcuts
        {
            private static readonly Type categoryOpenType;
            private static readonly Type clickModeType;
            private static readonly MethodInfo isRowVisibleMethod;
            private static readonly MethodInfo applyClickDefinition;
            private static readonly MethodInfo groupBuildingsMethod;
            private static readonly MethodInfo isVeneratedMethod;

            private static readonly FieldInfo animalShortcutsField;
            private static readonly FieldInfo plantShortcutsField;
            private static readonly FieldInfo treeShortcutsField;
            private static readonly FieldInfo clearAreaTreeShortcutsField;
            private static readonly FieldInfo mineralShortcutsField;

            private static readonly Dictionary<Type, MethodInfo> applyClickByType = new Dictionary<Type, MethodInfo>();

            private static readonly bool ready;

            static Shortcuts()
            {
                var surface = new ReflectionSurface("CmrCompat.Shortcuts");

                Type managerTabType = surface.Type("ColonyManagerRedux.ManagerTab");
                categoryOpenType = surface.Type("ColonyManagerRedux.ShortcutCategory`1");
                Type huntingTabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Hunting");
                Type foragingTabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Foraging");
                Type forestryTabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Forestry");
                Type miningTabType = surface.Type("ColonyManagerRedux.Managers.ManagerTab_Mining");

                clickModeType = surface.Required("ManagerTab.ShortcutClickMode",
                    managerTabType != null ? AccessTools.Inner(managerTabType, "ShortcutClickMode") : null);
                foreach (string mode in Enum.GetNames(typeof(CmrShortcutClick)))
                {
                    surface.Required("ShortcutClickMode." + mode,
                        clickModeType != null && Enum.IsDefined(clickModeType, mode) ? mode : null);
                }

                isRowVisibleMethod = surface.Method(managerTabType, "IsShortcutRowVisible",
                    new[] { typeof(int), typeof(int), typeof(bool) });
                applyClickDefinition = surface.Required("ManagerTab.ApplyShortcutClick",
                    managerTabType == null ? null : AccessTools.GetDeclaredMethods(managerTabType)
                        .FirstOrDefault(m => m.Name == "ApplyShortcutClick" && m.IsGenericMethodDefinition));
                groupBuildingsMethod = surface.Method(miningTabType, "GroupBuildingsByDesignationCategory",
                    new[] { typeof(IEnumerable<ThingDef>) });
                isVeneratedMethod = surface.Method(huntingTabType, "IsVenerated", new[] { typeof(PawnKindDef) });

                animalShortcutsField = surface.Field(huntingTabType, "_animalShortcuts");
                plantShortcutsField = surface.Field(foragingTabType, "_plantShortcuts");
                treeShortcutsField = surface.Field(forestryTabType, "_treeShortcuts");
                clearAreaTreeShortcutsField = surface.Field(forestryTabType, "_clearAreaTreeShortcuts");
                mineralShortcutsField = surface.Field(miningTabType, "_mineralShortcuts");

                ready = surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>
            /// The Hunting animal shortcuts, then Venerated, which the tab appends at draw time
            /// (ManagerTab_Hunting.DrawAnimalShortcuts) bound to the tab instance.
            /// </summary>
            public static List<CmrShortcutCategory<PawnKindDef>> Hunting(object tab)
            {
                List<CmrShortcutCategory<PawnKindDef>> categories = Read<PawnKindDef>(animalShortcutsField);
                if (ready && tab != null)
                {
                    try
                    {
                        var venerated = (Func<PawnKindDef, bool>)Delegate.CreateDelegate(
                            typeof(Func<PawnKindDef, bool>), tab, isVeneratedMethod);
                        categories.Add(new CmrShortcutCategory<PawnKindDef>(
                            CompatText.ModText("ColonyManagerRedux.Hunting.Venerated"),
                            CompatText.ModText("ColonyManagerRedux.Hunting.Venerated.Tip"), venerated));
                    }
                    catch (Exception ex)
                    {
                        Fail("Hunting", ex);
                    }
                }
                return categories;
            }

            public static List<CmrShortcutCategory<ThingDef>> Foraging()
            {
                return Read<ThingDef>(plantShortcutsField);
            }

            /// <summary>The tree shortcuts every Forestry job draws, or the extra set only a clear-area job draws below them.</summary>
            public static List<CmrShortcutCategory<ThingDef>> Forestry(bool clearArea)
            {
                return Read<ThingDef>(clearArea ? clearAreaTreeShortcutsField : treeShortcutsField);
            }

            public static List<CmrShortcutCategory<ThingDef>> Minerals()
            {
                return Read<ThingDef>(mineralShortcutsField);
            }

            /// <summary>
            /// The Mining building list's per-category rows, grouped by the mod's own grouping and
            /// ordered as it orders them (ManagerTab_Mining.DrawAllowedBuildingsShortcuts).
            /// </summary>
            public static List<KeyValuePair<DesignationCategoryDef, List<ThingDef>>> BuildingCategories(List<ThingDef> buildings)
            {
                var result = new List<KeyValuePair<DesignationCategoryDef, List<ThingDef>>>();
                if (!ready || buildings == null)
                {
                    return result;
                }
                try
                {
                    var groups = groupBuildingsMethod.Invoke(null, new object[] { buildings })
                        as IEnumerable<IGrouping<DesignationCategoryDef, ThingDef>>;
                    if (groups == null)
                    {
                        return result;
                    }
                    foreach (IGrouping<DesignationCategoryDef, ThingDef> group in groups
                        .OrderBy(g => g.Key.LabelCap.Resolve(), StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(new KeyValuePair<DesignationCategoryDef, List<ThingDef>>(group.Key, group.ToList()));
                    }
                }
                catch (Exception ex)
                {
                    Fail("BuildingCategories", ex);
                }
                return result;
            }

            /// <summary>Whether the mod draws a row toggling <paramref name="matchCount"/> of <paramref name="optionCount"/> options.</summary>
            public static bool RowVisible(int matchCount, int optionCount, bool isAllRow)
            {
                if (!ready)
                {
                    return false;
                }
                try
                {
                    return (bool)isRowVisibleMethod.Invoke(null, new object[] { matchCount, optionCount, isAllRow });
                }
                catch (Exception ex)
                {
                    Fail("RowVisible", ex);
                    return false;
                }
            }

            /// <summary>
            /// One press of a shortcut row in <paramref name="mode"/>. <paramref name="plainTurnsOn"/>
            /// is which of the toggle's two callbacks a plain press fires: on unless every option
            /// in the row is already allowed (Utilities.DrawToggle).
            /// </summary>
            public static void Apply<T>(List<T> matching, List<T> allOptions, Action<T, bool> setAllowed,
                CmrShortcutClick mode, bool plainTurnsOn)
            {
                if (!ready)
                {
                    return;
                }
                try
                {
                    MethodInfo apply;
                    if (!applyClickByType.TryGetValue(typeof(T), out apply))
                    {
                        apply = applyClickDefinition.MakeGenericMethod(typeof(T));
                        applyClickByType[typeof(T)] = apply;
                    }
                    apply.Invoke(null, new object[]
                    {
                        matching, allOptions, setAllowed, Enum.Parse(clickModeType, mode.ToString()), plainTurnsOn,
                    });
                }
                catch (Exception ex)
                {
                    Fail("Apply", ex);
                }
            }

            private static List<CmrShortcutCategory<T>> Read<T>(FieldInfo arrayField)
            {
                var result = new List<CmrShortcutCategory<T>>();
                if (!ready)
                {
                    return result;
                }
                try
                {
                    Type closed = categoryOpenType.MakeGenericType(typeof(T));
                    PropertyInfo labelKey = closed.GetProperty("LabelKey");
                    PropertyInfo tipKey = closed.GetProperty("TipKey");
                    PropertyInfo predicate = closed.GetProperty("Predicate");
                    var categories = arrayField.GetValue(null) as IEnumerable;
                    if (categories == null || labelKey == null || tipKey == null || predicate == null)
                    {
                        return result;
                    }
                    foreach (object category in categories)
                    {
                        result.Add(new CmrShortcutCategory<T>(
                            CompatText.ModText((string)labelKey.GetValue(category, null)),
                            CompatText.ModText((string)tipKey.GetValue(category, null)),
                            (Func<T, bool>)predicate.GetValue(category, null)));
                    }
                }
                catch (Exception ex)
                {
                    Fail("Read(" + arrayField.Name + ")", ex);
                }
                return result;
            }

            private static readonly HashSet<string> loggedFailures = new HashSet<string>();

            private static void Fail(string member, Exception ex)
            {
                if (loggedFailures.Add(member))
                {
                    ModLogger.Error("CmrCompat.Shortcuts." + member + " failed: " + ex.Message);
                }
            }
        }
    }
}
