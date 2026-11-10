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
    /// <summary>
    /// Visible Wealth's reflection surface: the wealth breakdown dialog, the <c>WealthNode</c> tree
    /// it draws, the static settings its toolbar edits, and the three <c>ChartOption</c> shapes
    /// whose own delegates are the toolbar's mutation vehicles. Every number spoken comes from the
    /// mod's own members (<c>Value</c>, <c>TotalWealth</c>, <c>PercentOfUtility.Text</c>).
    /// </summary>
    internal static class VwCompat
    {
        internal enum OptionKind { Choice, Toggle, Button }

        private static readonly Type dialogType;
        private static readonly Type nodeType;
        private static readonly Type nodeListType;
        private static readonly Type genericOptionType;
        private static readonly Type toggleOptionType;
        private static readonly Type buttonOptionType;
        private static readonly Type pieWorkerType;

        private static readonly FieldInfo rootNodesField;
        private static readonly MethodInfo totalWealthGetter;
        private static readonly FieldInfo searchField;
        private static readonly FieldInfo chartTypeOptionField;
        private static readonly AccessTools.FieldRef<Vector2> scrollPositionRef;
        private static readonly MethodInfo openMethod;

        private static readonly MethodInfo textGetter;
        private static readonly MethodInfo childrenGetter;
        private static readonly MethodInfo valueGetter;
        private static readonly MethodInfo openGetter;
        private static readonly MethodInfo openSetter;
        private static readonly MethodInfo isLeafGetter;
        private static readonly MethodInfo sortChildrenGetter;
        private static readonly MethodInfo infoDefGetter;
        private static readonly MethodInfo infoThingGetter;
        private static readonly FieldInfo parentField;
        private static readonly FieldInfo levelField;
        private static readonly MethodInfo matchesSearch;
        private static readonly MethodInfo thisOrAnyChildMatchesSearch;
        private static readonly MethodInfo showInfoCard;
        private static readonly MethodInfo getAllNodes;
        private static readonly FieldInfo drawHeightField;

        private static readonly FieldInfo chartTypeField;
        private static readonly FieldInfo sortByField;
        private static readonly FieldInfo sortAscendingField;
        private static readonly FieldInfo percentOfField;
        private static readonly FieldInfo listStyleField;
        private static readonly FieldInfo pieStyleField;
        private static readonly FieldInfo playSettingsButtonField;
        private static readonly MethodInfo chartWorkerGetter;
        private static readonly MethodInfo workerOptionsGetter;
        private static readonly MethodInfo sorted;
        private static readonly MethodInfo percentText;
        private static readonly MethodInfo flatten;

        private static readonly object listStyleNested;
        private static readonly object pieStyleNested;
        private static readonly object sortByName;
        private static readonly object percentOfTotal;
        private static readonly object percentOfCategory;

        // ChartOption fields are read off each instance's closed type; these only prove the names exist.
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> optionFields =
            new Dictionary<Type, Dictionary<string, FieldInfo>>();

        private static readonly bool ready;

        static VwCompat()
        {
            var surface = new ReflectionSurface("VwCompat");
            dialogType = surface.Type("VisibleWealth.Dialog_WealthBreakdown");
            nodeType = surface.Type("VisibleWealth.WealthNode");
            nodeListType = nodeType != null ? typeof(List<>).MakeGenericType(nodeType) : null;
            Type settingsType = surface.Type("VisibleWealth.VisibleWealthSettings");
            Type chartDefType = surface.Type("VisibleWealth.ChartDef");
            Type workerType = surface.Type("VisibleWealth.ChartWorker");
            pieWorkerType = surface.Type("VisibleWealth.ChartWorker_PieChart");
            Type optionType = surface.Type("VisibleWealth.ChartOption");
            genericOptionType = surface.Type("VisibleWealth.ChartOption`1");
            toggleOptionType = surface.Type("VisibleWealth.ChartOption_Toggle");
            buttonOptionType = surface.Type("VisibleWealth.ChartOption_Button");
            Type sortByUtilityType = surface.Type("VisibleWealth.SortByUtility");
            Type percentOfUtilityType = surface.Type("VisibleWealth.PercentOfUtility");
            Type sortByType = surface.Type("VisibleWealth.SortBy");
            Type percentOfType = surface.Type("VisibleWealth.PercentOf");
            Type listStyleType = surface.Type("VisibleWealth.ListStyle");
            Type pieStyleType = surface.Type("VisibleWealth.PieStyle");

            rootNodesField = surface.Field(dialogType, "rootNodes");
            totalWealthGetter = surface.Property(dialogType, "TotalWealth")?.GetGetMethod();
            searchField = surface.Field(dialogType, "Search");
            chartTypeOptionField = surface.Field(dialogType, "chartTypeOption");
            FieldInfo scrollField = surface.Field(dialogType, "scrollPosition");
            openMethod = surface.Method(dialogType, "Open", Type.EmptyTypes);
            DoWindowContentsMethod = surface.Method(dialogType, "DoWindowContents", new[] { typeof(Rect) });

            textGetter = surface.Property(nodeType, "Text")?.GetGetMethod();
            childrenGetter = surface.Property(nodeType, "Children")?.GetGetMethod();
            valueGetter = surface.Property(nodeType, "Value")?.GetGetMethod();
            PropertyInfo open = surface.Property(nodeType, "Open");
            openGetter = open?.GetGetMethod();
            openSetter = open?.GetSetMethod();
            isLeafGetter = surface.Property(nodeType, "IsLeafNode")?.GetGetMethod();
            sortChildrenGetter = surface.Property(nodeType, "SortChildren")?.GetGetMethod();
            infoDefGetter = surface.Property(nodeType, "InfoDef")?.GetGetMethod(true);
            infoThingGetter = surface.Property(nodeType, "InfoThing")?.GetGetMethod(true);
            parentField = surface.Field(nodeType, "parent");
            levelField = surface.Field(nodeType, "level");
            matchesSearch = surface.Method(nodeType, "MatchesSearch", Type.EmptyTypes);
            thisOrAnyChildMatchesSearch = surface.Method(nodeType, "ThisOrAnyChildMatchesSearch", Type.EmptyTypes);
            showInfoCard = surface.Method(nodeType, "ShowInfoCard", Type.EmptyTypes);
            getAllNodes = surface.Method(nodeType, "GetAllNodes");
            drawHeightField = surface.Field(nodeType, "drawHeight");
            NodeDrawMethod = surface.Method(nodeType, "Draw",
                new[] { typeof(float), typeof(float).MakeByRefType(), typeof(bool) });

            chartTypeField = surface.Field(settingsType, "ChartType");
            sortByField = surface.Field(settingsType, "SortBy");
            sortAscendingField = surface.Field(settingsType, "SortAscending");
            percentOfField = surface.Field(settingsType, "PercentOf");
            listStyleField = surface.Field(settingsType, "ListStyle");
            pieStyleField = surface.Field(settingsType, "PieStyle");
            playSettingsButtonField = surface.Field(settingsType, "PlaySettingsButton");
            chartWorkerGetter = surface.Property(chartDefType, "Worker")?.GetGetMethod();
            workerOptionsGetter = surface.Property(workerType, "Options")?.GetGetMethod();
            sorted = surface.Method(sortByUtilityType, "Sorted");
            percentText = surface.Method(percentOfUtilityType, "Text");
            flatten = surface.Method(pieWorkerType, "Flatten");
            PieDrawMethod = surface.Method(pieWorkerType, "Draw");

            foreach (string name in new[] { "optionGetter", "optionSetter", "optionName", "allOptionsGetter", "labelGetter", "iconGetter" })
            {
                surface.Field(genericOptionType, name);
            }
            foreach (string name in new[] { "optionGetter", "optionSetter", "optionName" })
            {
                surface.Field(toggleOptionType, name);
            }
            foreach (string name in new[] { "action", "enabledGetter", "tooltip" })
            {
                surface.Field(buttonOptionType, name);
            }

            listStyleNested = surface.EnumValue(listStyleType, "Nested");
            pieStyleNested = surface.EnumValue(pieStyleType, "Nested");
            sortByName = surface.EnumValue(sortByType, "Name");
            percentOfTotal = surface.EnumValue(percentOfType, "Total");
            percentOfCategory = surface.EnumValue(percentOfType, "Category");

            if (scrollField != null && scrollField.FieldType == typeof(Vector2))
            {
                scrollPositionRef = AccessTools.StaticFieldRefAccess<Vector2>(scrollField);
            }
            surface.Required("Dialog_WealthBreakdown.scrollPosition ref", scrollPositionRef);

            ready = surface.Ready;
            RowHeight = Guarded.FieldOf(drawHeightField, null, "VwCompat.RowHeight", 0f);
        }

        public static bool Ready => ready;

        public static Type DialogType => dialogType;

        public static MethodInfo DoWindowContentsMethod { get; }

        public static MethodInfo NodeDrawMethod { get; }

        public static MethodInfo PieDrawMethod { get; }

        // ---- The dialog ----

        public static void OpenBreakdown()
        {
            Guarded.Call(openMethod, null, "VwCompat.OpenBreakdown");
        }

        public static IEnumerable<object> RootNodes(Window window)
        {
            return Enumerate(Guarded.FieldOf<IEnumerable>(rootNodesField, window, "VwCompat.RootNodes"));
        }

        public static float TotalWealth(Window window)
        {
            return Guarded.Get(totalWealthGetter, window, "VwCompat.TotalWealth", 0f);
        }

        public static QuickSearchWidget Search()
        {
            return Guarded.FieldOf<QuickSearchWidget>(searchField, null, "VwCompat.Search");
        }

        public static bool SearchActive()
        {
            QuickSearchWidget search = Search();
            return search != null && search.filter.Active;
        }

        public static ref Vector2 ScrollPosition()
        {
            return ref scrollPositionRef();
        }

        // ---- Nodes ----

        public static string Text(object node)
        {
            return Guarded.Get(textGetter, node, "VwCompat.Text", "");
        }

        public static float Value(object node)
        {
            return Guarded.Get(valueGetter, node, "VwCompat.Value", 0f);
        }

        public static bool IsOpen(object node)
        {
            return Guarded.Get(openGetter, node, "VwCompat.IsOpen", false);
        }

        public static void SetOpen(object node, bool open)
        {
            if (openSetter == null || node == null)
            {
                return;
            }
            try
            {
                // MUTATION-C: mirrors WealthNode.Draw's arrow button and ChartWorker_PieChart's slice, ring and arrow clicks; each writes Open inline, with no delegate to invoke.
                openSetter.Invoke(node, new object[] { open });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("VwCompat.SetOpen", ex.InnerException ?? ex);
            }
        }

        public static bool IsLeaf(object node)
        {
            return Guarded.Get(isLeafGetter, node, "VwCompat.IsLeaf", true);
        }

        public static object Parent(object node)
        {
            return Guarded.FieldOf<object>(parentField, node, "VwCompat.Parent");
        }

        public static int Level(object node)
        {
            return Guarded.FieldOf(levelField, node, "VwCompat.Level", 0);
        }

        public static bool MatchesSearch(object node)
        {
            return Guarded.Call(matchesSearch, node, "VwCompat.MatchesSearch") is bool b && b;
        }

        /// <summary>The mod's own draw gate for a list row.</summary>
        public static bool IsListed(object node)
        {
            return Guarded.Call(thisOrAnyChildMatchesSearch, node, "VwCompat.IsListed") is bool b && b;
        }

        /// <summary>The info button's draw gate in <c>WealthNode.Draw</c>.</summary>
        public static bool HasInfoCard(object node)
        {
            return Guarded.Get<object>(infoDefGetter, node, "VwCompat.InfoDef") != null
                || Guarded.Get<object>(infoThingGetter, node, "VwCompat.InfoThing") != null;
        }

        public static void ShowInfoCard(object node)
        {
            Guarded.Call(showInfoCard, node, "VwCompat.ShowInfoCard");
        }

        /// <summary><c>WealthNode.drawHeight</c>, a static readonly row height.</summary>
        public static float RowHeight { get; private set; }

        /// <summary>A node's children in the order <c>WealthNode.Draw</c> draws them.</summary>
        public static IEnumerable<object> DrawOrderChildren(object node)
        {
            object children = Guarded.Get<object>(childrenGetter, node, "VwCompat.Children");
            if (children != null && Guarded.Get(sortChildrenGetter, node, "VwCompat.SortChildren", false))
            {
                children = Guarded.Call(sorted, null, "VwCompat.Sorted",
                    sortByField.GetValue(null), children, SortAscending);
            }
            return Enumerate(children as IEnumerable);
        }

        public static IEnumerable<object> Children(object node)
        {
            return Enumerate(Guarded.Get<object>(childrenGetter, node, "VwCompat.Children") as IEnumerable);
        }

        /// <summary>The flat list's rows: every leaf, sorted by the mod's own comparer, kept where its draw gate passes.</summary>
        public static List<object> FlatRows(Window window)
        {
            IList typed = (IList)Activator.CreateInstance(nodeListType);
            object all = Guarded.Call(getAllNodes, null, "VwCompat.GetAllNodes", Guarded.FieldOf<object>(rootNodesField, window, "VwCompat.RootNodes"));
            foreach (object node in Enumerate(all as IEnumerable))
            {
                if (IsLeaf(node))
                {
                    typed.Add(node);
                }
            }
            var rows = new List<object>();
            object ordered = Guarded.Call(sorted, null, "VwCompat.Sorted", sortByField.GetValue(null), typed, SortAscending);
            foreach (object node in Enumerate(ordered as IEnumerable))
            {
                if (MatchesSearch(node))
                {
                    rows.Add(node);
                }
            }
            return rows;
        }

        /// <summary>The pie's slices in drawing order (clockwise from the top), dropping the zero-width ones the chart never shows.</summary>
        public static List<object> Slices(Window window)
        {
            var slices = new List<object>();
            object all = Guarded.Call(flatten, null, "VwCompat.Flatten", Guarded.FieldOf<object>(rootNodesField, window, "VwCompat.RootNodes"));
            foreach (object node in Enumerate(all as IEnumerable))
            {
                if (Value(node) > 0f)
                {
                    slices.Add(node);
                }
            }
            return slices;
        }

        /// <summary>The percentage the mod prints after a row's value under <paramref name="percentOf"/>, parentheses dropped; empty when it prints none.</summary>
        public static string PercentText(object node, object percentOf)
        {
            string text = Guarded.Call(percentText, null, "VwCompat.PercentText", percentOf, node) as string;
            return string.IsNullOrEmpty(text) ? "" : text.Trim('(', ')');
        }

        public static string PercentOfTotalText(object node)
        {
            return PercentText(node, percentOfTotal);
        }

        // ---- Settings ----

        public static object ChartType => chartTypeField.GetValue(null);

        public static bool IsPieChart
        {
            get
            {
                object def = ChartType;
                return def != null && pieWorkerType.IsInstanceOfType(Guarded.Get<object>(chartWorkerGetter, def, "VwCompat.Worker"));
            }
        }

        public static bool IsNestedList => Equals(listStyleField.GetValue(null), listStyleNested);

        public static bool IsNestedPie => Equals(pieStyleField.GetValue(null), pieStyleNested);

        public static bool SortsByName => Equals(sortByField.GetValue(null), sortByName);

        public static bool SortAscending => sortAscendingField.GetValue(null) is bool b && b;

        public static object PercentOf => percentOfField.GetValue(null);

        public static bool ShowsPercentOfTotal => Equals(PercentOf, percentOfTotal);

        public static bool ShowsPercentOfCategory => Equals(PercentOf, percentOfCategory);

        public static bool PlaySettingsButton => playSettingsButtonField.GetValue(null) is bool b && b;

        // ---- Toolbar options ----

        /// <summary>The strip left to right: the chart type picker, then the current chart's own options.</summary>
        public static List<object> ToolbarOptions()
        {
            var list = new List<object>();
            object chartType = chartTypeOptionField.GetValue(null);
            if (chartType != null)
            {
                list.Add(chartType);
            }
            object def = ChartType;
            object worker = def != null ? Guarded.Get<object>(chartWorkerGetter, def, "VwCompat.Worker") : null;
            object options = worker != null ? Guarded.Get<object>(workerOptionsGetter, worker, "VwCompat.Options") : null;
            list.AddRange(Enumerate(options as IEnumerable));
            return list;
        }

        public static bool IsChartTypeOption(object option)
        {
            return option != null && ReferenceEquals(option, chartTypeOptionField.GetValue(null));
        }

        public static OptionKind KindOf(object option)
        {
            if (buttonOptionType.IsInstanceOfType(option))
            {
                return OptionKind.Button;
            }
            return toggleOptionType.IsInstanceOfType(option) ? OptionKind.Toggle : OptionKind.Choice;
        }

        /// <summary>The name the button's tooltip leads with: <c>optionName</c>, or a button's whole tooltip.</summary>
        public static string OptionName(object option)
        {
            if (KindOf(option) == OptionKind.Button)
            {
                return OptionField(option, "tooltip") is TaggedString tip ? tip.Resolve() : "";
            }
            return OptionField(option, "optionName") as string;
        }

        public static bool ToggleValue(object option)
        {
            return InvokeDelegate(OptionField(option, "optionGetter"), "VwCompat.ToggleValue") is bool b && b;
        }

        public static void SetToggle(object option, bool value)
        {
            InvokeDelegate(OptionField(option, "optionSetter"), "VwCompat.SetToggle", value);
        }

        public static bool ButtonEnabled(object option)
        {
            return InvokeDelegate(OptionField(option, "enabledGetter"), "VwCompat.ButtonEnabled") is bool b && b;
        }

        public static void PressButton(object option)
        {
            InvokeDelegate(OptionField(option, "action"), "VwCompat.PressButton");
        }

        public static object ChoiceValue(object option)
        {
            return InvokeDelegate(OptionField(option, "optionGetter"), "VwCompat.ChoiceValue");
        }

        public static string ChoiceLabel(object option, object choice)
        {
            return InvokeDelegate(OptionField(option, "labelGetter"), "VwCompat.ChoiceLabel", choice) as string ?? "";
        }

        public static Texture2D ChoiceIcon(object option, object choice)
        {
            return InvokeDelegate(OptionField(option, "iconGetter"), "VwCompat.ChoiceIcon", choice) as Texture2D;
        }

        public static IEnumerable<object> Choices(object option)
        {
            return Enumerate(InvokeDelegate(OptionField(option, "allOptionsGetter"), "VwCompat.Choices") as IEnumerable);
        }

        public static void Choose(object option, object choice)
        {
            InvokeDelegate(OptionField(option, "optionSetter"), "VwCompat.Choose", choice);
        }

        private static object OptionField(object option, string name)
        {
            if (option == null)
            {
                return null;
            }
            Type type = option.GetType();
            if (!optionFields.TryGetValue(type, out Dictionary<string, FieldInfo> fields))
            {
                fields = new Dictionary<string, FieldInfo>();
                optionFields[type] = fields;
            }
            if (!fields.TryGetValue(name, out FieldInfo field))
            {
                field = AccessTools.Field(type, name);
                fields[name] = field;
            }
            return Guarded.FieldOf<object>(field, option, "VwCompat.OptionField");
        }

        private static object InvokeDelegate(object target, string context, params object[] args)
        {
            if (!(target is Delegate callback))
            {
                return null;
            }
            try
            {
                return callback.DynamicInvoke(args);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError(context, ex.InnerException ?? ex);
                return null;
            }
        }

        private static IEnumerable<object> Enumerate(IEnumerable source)
        {
            if (source == null)
            {
                yield break;
            }
            foreach (object item in source)
            {
                yield return item;
            }
        }
    }
}
