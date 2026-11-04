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
    internal static partial class ClCompat
    {
        /// <summary>
        /// The loadout overview main tab (<c>MainTabWindow_LoadoutOverview</c>): its private pawn list,
        /// selection, sort and view state, the per-row computations its draw uses (shown state, chip
        /// warnings, chip layout), and the members the stats panel reads.
        /// </summary>
        internal static class Overview
        {
            private static readonly FieldInfo pawnsField;
            private static readonly FieldInfo focusedField;
            private static readonly FieldInfo selectedField;
            private static readonly FieldInfo searchField;
            private static readonly FieldInfo sortByField;
            private static readonly FieldInfo descendingField;
            private static readonly FieldInfo showStatsField;
            private static readonly FieldInfo statsPanelField;
            private static readonly FieldInfo rowWarningsField;
            private static readonly FieldInfo elementsField;
            private static readonly AccessTools.FieldRef<object, Vector2> tableScrollRef;
            private static readonly Type sortByType;

            private static readonly MethodInfo findPawns;
            private static readonly MethodInfo shownStateFor;
            private static readonly MethodInfo infoFor;
            private static readonly MethodInfo chipOptions;
            private static readonly MethodInfo removeChip;
            private static readonly MethodInfo stepFocus;
            private static readonly MethodInfo layoutChips;
            private static readonly MethodInfo chipsWidth;
            private static readonly MethodInfo stateGenerator;
            private static readonly MethodInfo insertElement;
            private static readonly ConstructorInfo elementCtor;
            private static readonly MethodInfo hypotheticalWornApparel;
            private static readonly MethodInfo apparelCanFitOnBody;

            private static readonly MethodInfo doWindowContents;
            private static readonly MethodInfo drawToolbar;
            private static readonly MethodInfo drawRow;
            private static readonly MethodInfo drawName;
            private static readonly MethodInfo drawChip;
            private static readonly MethodInfo statsPanelDraw;

            private static readonly bool ready;

            static Overview()
            {
                var surface = new ReflectionSurface("ClCompat.Overview");
                Type window = surface.Supplied("Inventory.MainTabWindow_LoadoutOverview", overviewType);
                Type rowInfoType = surface.Supplied("MainTabWindow_LoadoutOverview.RowInfo",
                    window?.GetNestedType("RowInfo", BindingFlags.NonPublic));
                Type statsPanelType = surface.Type("Inventory.Panel_PawnStats");
                Type columnWorkerType = surface.Type("Inventory.PawnColumnWorker_LoadoutState");
                Type apparelUtilityType = surface.Type("Inventory.ApparelUtility");

                pawnsField = surface.Field(window, "pawns");
                focusedField = surface.Field(window, "focused");
                selectedField = surface.Field(window, "selected");
                searchField = surface.Field(window, "search");
                sortByField = surface.Field(window, "sortBy");
                descendingField = surface.Field(window, "descending");
                showStatsField = surface.Field(window, "showStats");
                statsPanelField = surface.Field(window, "statsPanel");
                FieldInfo tableScroll = surface.Field(window, "tableScroll");
                rowWarningsField = surface.Field(rowInfoType, "warnings");
                elementsField = surface.Field(loadoutType, "elements");
                sortByType = sortByField?.FieldType;

                findPawns = surface.Method(window, "FindPawns", Type.EmptyTypes);
                shownStateFor = surface.Method(window, "ShownStateFor", new[] { typeof(Pawn) });
                infoFor = surface.Method(window, "InfoFor", new[] { typeof(Pawn) });
                stepFocus = surface.Method(window, "StepFocus", new[] { typeof(int) });
                chipsWidth = surface.Method(window, "ChipsWidth", new[] { typeof(float) });
                doWindowContents = surface.Method(window, "DoWindowContents", new[] { typeof(Rect) });
                drawToolbar = surface.Method(window, "DrawToolbar", new[] { typeof(Rect) });
                drawRow = surface.Method(window, "DrawRow", new[] { typeof(Rect), typeof(Pawn), typeof(int) });
                drawName = surface.Method(window, "DrawName", new[] { typeof(Rect), typeof(Pawn) });
                statsPanelDraw = surface.Method(statsPanelType, "Draw", new[] { typeof(Rect) });
                stateGenerator = surface.Method(columnWorkerType, "GenerateMenuFor", new[] { typeof(Pawn) });
                apparelCanFitOnBody = surface.Method(apparelUtilityType, "ApparelCanFitOnBody",
                    new[] { typeof(BodyDef), typeof(List<ThingDef>) });

                if (elementType != null && tagType != null && stateType != null && activeConditionType != null)
                {
                    Type elementList = typeof(List<>).MakeGenericType(elementType);
                    chipOptions = surface.Method(window, "ChipOptions", new[] { typeof(Pawn), elementType });
                    removeChip = surface.Method(window, "RemoveTag", new[] { typeof(Pawn), elementType, typeof(bool) });
                    layoutChips = surface.Method(window, "LayoutChips",
                        new[] { elementList, typeof(float), typeof(float).MakeByRefType() });
                    drawChip = surface.Method(window, "DrawChip",
                        new[] { typeof(Rect), typeof(Pawn), elementType, typeof(bool), typeof(string) });
                    insertElement = surface.Method(componentType, "InsertElement", new[] { elementType, typeof(int) });
                    elementCtor = surface.Constructor(elementType, new[] { tagType, stateType, activeConditionType });
                    hypotheticalWornApparel = surface.Method(loadoutType, "HypotheticalWornApparel",
                        new[] { stateType, typeof(BodyDef) });
                }

                if (tableScroll != null && tableScroll.FieldType == typeof(Vector2))
                {
                    tableScrollRef = AccessTools.FieldRefAccess<Vector2>(window, "tableScroll");
                }
                surface.Required("MainTabWindow_LoadoutOverview.tableScroll ref", tableScrollRef);

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            public static MethodInfo DoWindowContentsMethod { get { return doWindowContents; } }
            public static MethodInfo DrawToolbarMethod { get { return drawToolbar; } }
            public static MethodInfo DrawRowMethod { get { return drawRow; } }
            public static MethodInfo DrawNameMethod { get { return drawName; } }
            public static MethodInfo DrawChipMethod { get { return drawChip; } }
            public static MethodInfo StatsPanelDrawMethod { get { return statsPanelDraw; } }

            /// <summary>The state dropdown's generator, static and taking the pawn.</summary>
            public static MethodInfo StateGenerator { get { return stateGenerator; } }

            // ---- The window's own state ----

            /// <summary>The listed pawns in the window's own filtered, sorted order.</summary>
            public static List<Pawn> Pawns(Window window)
            {
                return Guarded.FieldOf<List<Pawn>>(pawnsField, window, "ClCompat.Overview.Pawns") ?? new List<Pawn>();
            }

            /// <summary>The pawn last clicked: the anchor of a range selection and the stats panel's pawn.</summary>
            public static Pawn Focused(Window window)
            {
                return Guarded.FieldOf<Pawn>(focusedField, window, "ClCompat.Overview.Focused");
            }

            public static void SetFocused(Window window, Pawn pawn)
            {
                if (ready && window != null)
                {
                    focusedField.SetValue(window, pawn); // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:173 (Select's inline write)
                }
            }

            /// <summary>The window's live selection set; the pawns a change to a selected pawn applies to.</summary>
            public static HashSet<Pawn> Selected(Window window)
            {
                return Guarded.FieldOf<HashSet<Pawn>>(selectedField, window, "ClCompat.Overview.Selected");
            }

            public static QuickSearchWidget Search(Window window)
            {
                return Guarded.FieldOf<QuickSearchWidget>(searchField, window, "ClCompat.Overview.Search");
            }

            /// <summary>The SortBy enum as its ordinal: 0 name, 1 state, 2 tags.</summary>
            public static int SortBy(Window window)
            {
                object value = Guarded.FieldOf<object>(sortByField, window, "ClCompat.Overview.SortBy");
                return value == null ? 0 : Convert.ToInt32(value);
            }

            public static bool Descending(Window window)
            {
                return Guarded.FieldOf(descendingField, window, "ClCompat.Overview.Descending", false);
            }

            // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:563-565; the header click is an inline ButtonInvisible body.
            public static void SetSort(Window window, int sortBy, bool descending)
            {
                if (!ready || window == null)
                {
                    return;
                }
                descendingField.SetValue(window, descending); // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:563
                sortByField.SetValue(window, Enum.ToObject(sortByType, sortBy)); // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:564
                FindPawns(window);
            }

            /// <summary>Re-reads, filters and sorts the pawn list, as the window does every 60 frames.</summary>
            public static void FindPawns(Window window)
            {
                Guarded.Call(findPawns, window, "ClCompat.Overview.FindPawns");
            }

            public static bool ShowStats(Window window)
            {
                return Guarded.FieldOf(showStatsField, window, "ClCompat.Overview.ShowStats", false);
            }

            public static void SetShowStats(Window window, bool show)
            {
                if (ready && window != null)
                {
                    showStatsField.SetValue(window, show); // MUTATION-C: mirrors MainTabWindow_LoadoutOverview.cs:396 (the collapse strip's inline click)
                }
            }

            public static object StatsPanel(Window window)
            {
                return Guarded.FieldOf<object>(statsPanelField, window, "ClCompat.Overview.StatsPanel");
            }

            /// <summary>The stats panel's previous/next arrows: moves the focused pawn and selects only it.</summary>
            public static void StepFocus(Window window, int by)
            {
                Guarded.Call(stepFocus, window, "ClCompat.Overview.StepFocus", by);
            }

            public static ref Vector2 TableScroll(Window window)
            {
                return ref tableScrollRef(window);
            }

            // ---- Per-row computations ----

            /// <summary>The state the row's tags are shown in: the view-as override, else the pawn's own.</summary>
            public static object ShownStateFor(Window window, Pawn pawn)
            {
                return Guarded.Call(shownStateFor, window, "ClCompat.Overview.ShownStateFor", pawn);
            }

            /// <summary>The red overlay's reasons for one chip (prosthetic conflicts, outfit refusals), or null.</summary>
            public static string Warning(Window window, Pawn pawn, object element)
            {
                object info = Guarded.Call(infoFor, window, "ClCompat.Overview.InfoFor", pawn);
                IDictionary warnings = Guarded.FieldOf<IDictionary>(rowWarningsField, info, "ClCompat.Overview.Warnings");
                return warnings != null && element != null && warnings.Contains(element) ? warnings[element] as string : null;
            }

            /// <summary>The chip's right-click menu: change state, edit tag, remove, remove from the selected.</summary>
            public static List<FloatMenuOption> ChipOptions(Window window, Pawn pawn, object element)
            {
                var options = new List<FloatMenuOption>();
                if (Guarded.Call(chipOptions, window, "ClCompat.Overview.ChipOptions", pawn, element) is IEnumerable source)
                {
                    foreach (object option in source)
                    {
                        if (option is FloatMenuOption floatOption)
                        {
                            options.Add(floatOption);
                        }
                    }
                }
                return options;
            }

            /// <summary>The chip's remove button.</summary>
            public static bool RemoveChip(Window window, Pawn pawn, object element)
            {
                return ready && Invoke(removeChip, window, "ClCompat.Overview.RemoveChip", pawn, element, false);
            }

            /// <summary>A row's height: one line per wrapped line of chips, from the window's own layout.</summary>
            public static float RowHeight(Pawn pawn, float rowWidth)
            {
                object elements = Guarded.FieldOf<object>(elementsField, LoadoutOf(pawn), "ClCompat.Overview.Elements");
                if (!(Guarded.Call(chipsWidth, null, "ClCompat.Overview.ChipsWidth", rowWidth) is float width) || elements == null)
                {
                    return 0f;
                }
                var args = new object[] { elements, width, 0f };
                Guarded.Call(layoutChips, null, "ClCompat.Overview.LayoutChips", args);
                return args[2] is float height ? height : 0f;
            }

            /// <summary>The loadout's live element list, the one its own drag-reorder edits in place.</summary>
            public static IList ElementList(Pawn pawn)
            {
                return Guarded.FieldOf<IList>(elementsField, LoadoutOf(pawn), "ClCompat.Overview.ElementList");
            }

            public static object NewElement(object tag, object state, bool activeWhenStateActive)
            {
                return Construct(elementCtor, "ClCompat.Overview.NewElement", tag, state, Condition(activeWhenStateActive));
            }

            public static bool InsertElement(Pawn pawn, object element, int index)
            {
                return Invoke(insertElement, GetComponent(pawn), "ClCompat.Overview.InsertElement", element, index);
            }

            // ---- The stats panel ----

            /// <summary>The panel's apparel list: what fits on the body beside the apparel the loadout would have worn.</summary>
            public static List<ThingDef> WearableApparel(Pawn pawn, object state)
            {
                var result = new List<ThingDef>();
                BodyDef body = pawn?.RaceProps?.body;
                var worn = new List<ThingDef>();
                if (Guarded.Call(hypotheticalWornApparel, LoadoutOf(pawn), "ClCompat.Overview.HypotheticalWornApparel", state, body)
                    is IEnumerable tuples)
                {
                    foreach (object tuple in tuples)
                    {
                        if (tuple?.GetType().GetProperty("Item3")?.GetValue(tuple) is ThingDef def)
                        {
                            worn.Add(def);
                        }
                    }
                }
                if (Guarded.Call(apparelCanFitOnBody, null, "ClCompat.Overview.ApparelCanFitOnBody", body, worn) is IEnumerable fits)
                {
                    foreach (object def in fits)
                    {
                        if (def is ThingDef thingDef)
                        {
                            result.Add(thingDef);
                        }
                    }
                }
                return result;
            }
        }
    }
}
