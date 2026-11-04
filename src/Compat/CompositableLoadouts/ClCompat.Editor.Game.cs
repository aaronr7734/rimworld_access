using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class ClCompat
    {
        /// <summary>
        /// The loadout editor (<c>Dialog_LoadoutEditor</c>) and the model reads its panels draw from:
        /// the "view as if" state, the pawn stats and coverage panels, the weight and stat offset
        /// statistics, the apparel slot grid and the prosthetic placements.
        /// </summary>
        internal static class Editor
        {
            private static readonly FieldInfo pawnField;
            private static readonly FieldInfo shownStateField;
            private static readonly FieldInfo coveragePanelField;
            private static readonly FieldInfo pawnStatPanelField;
            private static readonly MethodInfo statesGenerator;
            private static readonly MethodInfo doWindowContents;

            private static readonly MethodInfo coverageShouldDrawGetter;
            private static readonly MethodInfo statsShouldDrawGetter;
            private static readonly PropertyInfo coverageTabProperty;
            private static readonly object apparelTab;
            private static readonly object bodyPartsTab;

            private static readonly MethodInfo wornApparelMethod;
            private static readonly MethodInfo prostheticsMethod;
            private static readonly MethodInfo carriedItemsWithMethod;
            private static readonly MethodInfo elementsWithMethod;

            private static readonly MethodInfo unboundedEncumbranceMethod;
            private static readonly MethodInfo massMethod;
            private static readonly MethodInfo capacityMethod;

            private static readonly MethodInfo itemDefsGetter;

            private static readonly MethodInfo slotsConflictingMethod;
            private static readonly MethodInfo slotsConflictsGetter;
            private static readonly MethodInfo slotsPlacementsOnMethod;
            private static readonly MethodInfo slotsBlockedOnMethod;
            private static readonly FieldInfo placementItemField;
            private static readonly FieldInfo placementTagField;
            private static readonly FieldInfo placementDefField;
            private static readonly FieldInfo placementUseField;
            private static readonly MethodInfo installRecipesForMethod;

            private static readonly MethodInfo bodyPartGroupForMethod;
            private static readonly MethodInfo apparelCanFitMethod;
            private static readonly FieldInfo groupDefField;
            private static readonly FieldInfo groupLayersField;
            private static readonly FieldInfo groupChildrenField;
            private static readonly FieldInfo groupCategoriesField;
            private static readonly MethodInfo slotCreateMethod;
            private static readonly MethodInfo slotIntersectsMethod;

            private static readonly bool ready;

            static Editor()
            {
                var surface = new ReflectionSurface("ClCompat.Editor");
                surface.Supplied("Inventory.Dialog_LoadoutEditor", loadoutEditorType);
                surface.Supplied("Inventory.Loadout", loadoutType);
                Type coveragePanelType = surface.Type("Inventory.Panel_ShowCoverage");
                Type statsPanelType = surface.Type("Inventory.Panel_PawnStats");
                Type slotsType = surface.Type("Inventory.ProstheticSlots");
                Type prostheticUtilityType = surface.Type("Inventory.ProstheticUtility");
                Type apparelUtilityType = surface.Type("Inventory.ApparelUtility");
                Type groupType = surface.Type("Inventory.BodyPartGroup");
                Type slotMakerType = surface.Type("Inventory.ApparelSlotMaker");
                Type slotType = surface.Type("Inventory.ApparelSlot");
                Type placementType = surface.Supplied("Inventory.ProstheticSlots+Placement",
                    slotsType?.GetNestedType("Placement", BindingFlags.Public | BindingFlags.NonPublic));
                Type tabType = surface.Supplied("Inventory.Panel_ShowCoverage+CoverageTab",
                    coveragePanelType?.GetNestedType("CoverageTab", BindingFlags.Public | BindingFlags.NonPublic));

                pawnField = surface.Field(loadoutEditorType, "pawn");
                shownStateField = surface.Field(loadoutEditorType, "shownState");
                coveragePanelField = surface.Field(loadoutEditorType, "coveragePanel");
                pawnStatPanelField = surface.Field(loadoutEditorType, "pawnStatPanel");
                doWindowContents = surface.Method(loadoutEditorType, "DoWindowContents", new[] { typeof(UnityEngine.Rect) });
                if (loadoutEditorType != null)
                {
                    statesGenerator = surface.Method(loadoutEditorType, "States", new[] { loadoutEditorType });
                }

                coverageShouldDrawGetter = surface.Property(coveragePanelType, "ShouldDraw")?.GetGetMethod(true);
                statsShouldDrawGetter = surface.Property(statsPanelType, "ShouldDraw")?.GetGetMethod(true);
                coverageTabProperty = surface.Property(coveragePanelType, "Tab");
                surface.Required("Panel_ShowCoverage.Tab setter", coverageTabProperty?.GetSetMethod(true));
                if (tabType != null)
                {
                    apparelTab = surface.Required("CoverageTab.Apparel", ParseEnum(tabType, "Apparel"));
                    bodyPartsTab = surface.Required("CoverageTab.BodyParts", ParseEnum(tabType, "BodyParts"));
                }

                itemDefsGetter = surface.Property(itemType, "Defs")?.GetGetMethod(true);

                slotsConflictsGetter = surface.Property(slotsType, "Conflicts")?.GetGetMethod(true);
                slotsPlacementsOnMethod = surface.Method(slotsType, "PlacementsOn", new[] { typeof(BodyPartRecord) });
                slotsBlockedOnMethod = surface.Method(slotsType, "BlockedOn", new[] { typeof(BodyPartRecord) });
                placementItemField = surface.Field(placementType, "item");
                placementTagField = surface.Field(placementType, "tag");
                placementDefField = surface.Field(placementType, "def");
                placementUseField = surface.Field(placementType, "use");
                installRecipesForMethod = surface.Method(prostheticUtilityType, "InstallRecipesFor", new[] { typeof(ThingDef) });

                bodyPartGroupForMethod = surface.Method(apparelUtilityType, "GetBodyPartGroupFor", new[] { typeof(BodyDef) });
                apparelCanFitMethod = surface.Method(apparelUtilityType, "ApparelCanFitOnBody",
                    new[] { typeof(BodyDef), typeof(List<ThingDef>) });
                groupDefField = surface.Field(groupType, "def");
                groupLayersField = surface.Field(groupType, "layers");
                groupChildrenField = surface.Field(groupType, "children");
                groupCategoriesField = surface.Field(groupType, "categories");
                slotCreateMethod = surface.Method(slotMakerType, "Create", new[] { typeof(BodyDef), typeof(ThingDef) });
                slotIntersectsMethod = surface.Method(slotType, "Intersects", new[] { typeof(BodyPartGroupDef), typeof(ApparelLayerDef) });

                if (stateType != null && itemType != null)
                {
                    Type itemList = typeof(List<>).MakeGenericType(itemType);
                    wornApparelMethod = surface.Method(loadoutType, "HypotheticalWornApparel", new[] { stateType, typeof(BodyDef) });
                    prostheticsMethod = surface.Method(loadoutType, "HypotheticalProsthetics", new[] { stateType, typeof(BodyDef) });
                    carriedItemsWithMethod = surface.Method(loadoutType, "CarriedItemsWith", new[] { stateType });
                    elementsWithMethod = surface.Method(loadoutType, "ElementsWith", new[] { stateType });
                    unboundedEncumbranceMethod = surface.Method(utilityType, "HypotheticalUnboundedEncumberancePercent",
                        new[] { typeof(Pawn), itemList });
                    massMethod = surface.Method(utilityType, "HypotheticalGearAndInventoryMass", new[] { typeof(Pawn), itemList });
                    capacityMethod = surface.Method(utilityType, "HypotheticalCapacity", new[] { typeof(Pawn), itemList });
                    slotsConflictingMethod = surface.Method(slotsType, "Conflicting", new[] { itemType });
                }

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            public static MethodInfo DoWindowContents
            {
                get { return doWindowContents; }
            }

            // ---- The window ----

            public static Pawn PawnOf(Window editor)
            {
                return Guarded.FieldOf<Pawn>(pawnField, editor, "ClCompat.Editor.Pawn");
            }

            /// <summary>The state the statistics and coverage are shown for, null for the default ("any") state.</summary>
            public static object ShownState(Window editor)
            {
                return Guarded.FieldOf<object>(shownStateField, editor, "ClCompat.Editor.ShownState");
            }

            /// <summary>The "view as if" dropdown's generator; static, taking the editor.</summary>
            public static MethodInfo StatesGenerator
            {
                get { return ready ? statesGenerator : null; }
            }

            public static bool PawnStatsShown(Window editor)
            {
                object panel = Guarded.FieldOf<object>(pawnStatPanelField, editor, "ClCompat.Editor.PawnStatPanel");
                return Guarded.Get(statsShouldDrawGetter, panel, "ClCompat.Editor.PawnStatsShown", false);
            }

            public static bool CoverageShown(Window editor)
            {
                return Guarded.Get(coverageShouldDrawGetter, CoveragePanel(editor), "ClCompat.Editor.CoverageShown", false);
            }

            public static bool CoverageOnBodyParts(Window editor)
            {
                object panel = CoveragePanel(editor);
                return ready && panel != null && Equals(coverageTabProperty.GetValue(panel), bodyPartsTab);
            }

            public static void SetCoverageOnBodyParts(Window editor, bool bodyParts)
            {
                object panel = CoveragePanel(editor);
                if (ready && panel != null)
                {
                    coverageTabProperty.SetValue(panel, bodyParts ? bodyPartsTab : apparelTab); // MUTATION-C: mirrors Panel_ShowCoverage.cs:72-73; each TabRecord's click lambda is inline in Draw
                }
            }

            private static object CoveragePanel(Window editor)
            {
                return Guarded.FieldOf<object>(coveragePanelField, editor, "ClCompat.Editor.CoveragePanel");
            }

            /// <summary>Swaps an element with its neighbour, as one step of the tag list's drag does.</summary>
            public static bool SwapElements(object loadout, int index, int direction)
            {
                IList elements = Guarded.FieldOf<IList>(loadoutElementsField, loadout, "ClCompat.Editor.Elements");
                int other = index + direction;
                if (elements == null || index < 0 || other < 0 || index >= elements.Count || other >= elements.Count)
                {
                    return false;
                }
                // MUTATION-C: mirrors Dialog_LoadoutEditor.cs:265-273; the drag swap is inline in DraggableTags, mouse-only.
                object moved = elements[index];
                elements[index] = elements[other];
                elements[other] = moved;
                return true;
            }

            // ---- The loadout as it would be in the shown state ----

            public static List<object> ElementsWith(object loadout, object state)
            {
                return ToList(Guarded.Call(elementsWithMethod, loadout, "ClCompat.Editor.ElementsWith", state) as IEnumerable);
            }

            public static List<object> CarriedItemsWith(object loadout, object state)
            {
                return ToList(Guarded.Call(carriedItemsWithMethod, loadout, "ClCompat.Editor.CarriedItemsWith", state) as IEnumerable);
            }

            /// <summary>The apparel the shown state would have worn: (item, its tag, the def chosen), by priority.</summary>
            public static List<WornApparel> HypotheticalWornApparel(object loadout, object state, BodyDef body)
            {
                var result = new List<WornApparel>();
                foreach (object tuple in ToList(Guarded.Call(wornApparelMethod, loadout, "ClCompat.Editor.WornApparel", state, body) as IEnumerable))
                {
                    result.Add(new WornApparel(TupleItem(tuple, "Item1"), TupleItem(tuple, "Item2"), TupleItem(tuple, "Item3") as ThingDef));
                }
                return result;
            }

            public static object HypotheticalProsthetics(object loadout, object state, BodyDef body)
            {
                return Guarded.Call(prostheticsMethod, loadout, "ClCompat.Editor.Prosthetics", state, body);
            }

            public static float UnboundedEncumbrance(Pawn pawn, IList items)
            {
                return Guarded.Call(unboundedEncumbranceMethod, null, "ClCompat.Editor.Encumbrance", pawn, items) is float value ? value : 0f;
            }

            public static float Mass(Pawn pawn, IList items)
            {
                return Guarded.Call(massMethod, null, "ClCompat.Editor.Mass", pawn, items) is float value ? value : 0f;
            }

            public static float Capacity(Pawn pawn, IList items)
            {
                return Guarded.Call(capacityMethod, null, "ClCompat.Editor.Capacity", pawn, items) is float value ? value : 0f;
            }

            public static IList ItemList(IEnumerable<object> items)
            {
                return TypedList(itemType, items);
            }

            public static List<ThingDef> ItemDefs(object item)
            {
                var defs = new List<ThingDef>();
                foreach (object def in ToList(Guarded.Get<IEnumerable>(itemDefsGetter, item, "ClCompat.Editor.ItemDefs")))
                {
                    if (def is ThingDef thingDef)
                    {
                        defs.Add(thingDef);
                    }
                }
                return defs;
            }

            // ---- Prosthetic placements (ProstheticSlots) ----

            public static bool Conflicting(object slots, object item)
            {
                return Guarded.Call(slotsConflictingMethod, slots, "ClCompat.Editor.Conflicting", item) is bool conflicting && conflicting;
            }

            /// <summary>The tag whose prosthetic is in the way of a conflicting item, or null when the body has too few parts.</summary>
            public static object ConflictBlocker(object slots, object item)
            {
                IDictionary conflicts = Guarded.Get<IDictionary>(slotsConflictsGetter, slots, "ClCompat.Editor.Conflicts");
                return conflicts != null && item != null && conflicts.Contains(item) ? conflicts[item] : null;
            }

            public static List<Placement> PlacementsOn(object slots, BodyPartRecord part)
            {
                var result = new List<Placement>();
                foreach (object placement in ToList(Guarded.Call(slotsPlacementsOnMethod, slots, "ClCompat.Editor.PlacementsOn", part) as IEnumerable))
                {
                    object use = Guarded.FieldOf<object>(placementUseField, placement, "ClCompat.Editor.PlacementUse");
                    result.Add(new Placement(
                        Guarded.FieldOf<object>(placementItemField, placement, "ClCompat.Editor.PlacementItem"),
                        Guarded.FieldOf<object>(placementTagField, placement, "ClCompat.Editor.PlacementTag"),
                        Guarded.FieldOf<ThingDef>(placementDefField, placement, "ClCompat.Editor.PlacementDef"),
                        use?.ToString() ?? ""));
                }
                return result;
            }

            /// <summary>The prosthetics, with the tags asking for them, which would go on the part but conflict with another.</summary>
            public static List<WornApparel> BlockedOn(object slots, BodyPartRecord part)
            {
                var result = new List<WornApparel>();
                foreach (object tuple in ToList(Guarded.Call(slotsBlockedOnMethod, slots, "ClCompat.Editor.BlockedOn", part) as IEnumerable))
                {
                    result.Add(new WornApparel(TupleItem(tuple, "Item1"), TupleItem(tuple, "Item2"), null));
                }
                return result;
            }

            /// <summary>The hediff the prosthetic's first install recipe adds, the one BodyPartCoverage compares against.</summary>
            public static HediffDef InstalledHediff(ThingDef def)
            {
                foreach (object recipe in ToList(Guarded.Call(installRecipesForMethod, null, "ClCompat.Editor.InstallRecipesFor", def) as IEnumerable))
                {
                    return (recipe as RecipeDef)?.addsHediff;
                }
                return null;
            }

            // ---- The apparel slot grid (ApparelUtility's body part group tree) ----

            /// <summary>The body's groups split into the grid's categories: each a list of top-level groups sharing layers.</summary>
            public static List<List<object>> GroupCategories(BodyDef body)
            {
                var result = new List<List<object>>();
                object root = Guarded.Call(bodyPartGroupForMethod, null, "ClCompat.Editor.BodyPartGroupFor", body);
                foreach (object category in ToList(Guarded.FieldOf<IEnumerable>(groupCategoriesField, root, "ClCompat.Editor.Categories")))
                {
                    result.Add(ToList(category as IEnumerable));
                }
                return result;
            }

            public static BodyPartGroupDef GroupDef(object group)
            {
                return Guarded.FieldOf<BodyPartGroupDef>(groupDefField, group, "ClCompat.Editor.GroupDef");
            }

            public static List<ApparelLayerDef> GroupLayers(object group)
            {
                return Layers(Guarded.FieldOf<IEnumerable>(groupLayersField, group, "ClCompat.Editor.GroupLayers"));
            }

            public static List<object> GroupChildren(object group)
            {
                return ToList(Guarded.FieldOf<IEnumerable>(groupChildrenField, group, "ClCompat.Editor.GroupChildren"));
            }

            /// <summary>Whether the apparel covers the group on the layer: the grid's own slot test.</summary>
            public static bool SlotCovers(BodyDef body, ThingDef apparel, BodyPartGroupDef group, ApparelLayerDef layer)
            {
                object slot = Guarded.Call(slotCreateMethod, null, "ClCompat.Editor.SlotCreate", body, apparel);
                return Guarded.Call(slotIntersectsMethod, slot, "ClCompat.Editor.SlotIntersects", group, layer) is bool covers && covers;
            }

            /// <summary>Every apparel def that fits alongside the given worn apparel.</summary>
            public static List<ThingDef> ApparelCanFitOnBody(BodyDef body, List<ThingDef> worn)
            {
                var result = new List<ThingDef>();
                foreach (object def in ToList(Guarded.Call(apparelCanFitMethod, null, "ClCompat.Editor.ApparelCanFit", body, worn) as IEnumerable))
                {
                    if (def is ThingDef thingDef)
                    {
                        result.Add(thingDef);
                    }
                }
                return result;
            }

            private static List<ApparelLayerDef> Layers(IEnumerable source)
            {
                var result = new List<ApparelLayerDef>();
                foreach (object layer in ToList(source))
                {
                    if (layer is ApparelLayerDef layerDef && !result.Contains(layerDef))
                    {
                        result.Add(layerDef);
                    }
                }
                return result;
            }

            private static object TupleItem(object tuple, string name)
            {
                return tuple?.GetType().GetProperty(name)?.GetValue(tuple);
            }

            /// <summary>An (item, tag, def) triple the mod hands back as a <c>Tuple</c>.</summary>
            internal sealed class WornApparel
            {
                public readonly object Item;
                public readonly object Tag;
                public readonly ThingDef Def;

                public WornApparel(object item, object tag, ThingDef def)
                {
                    Item = item;
                    Tag = tag;
                    Def = def;
                }
            }

            /// <summary>One <c>ProstheticSlots.Placement</c>; Use is the PartUse name.</summary>
            internal sealed class Placement
            {
                public readonly object Item;
                public readonly object Tag;
                public readonly ThingDef Def;
                public readonly string Use;

                public Placement(object item, object tag, ThingDef def, string use)
                {
                    Item = item;
                    Tag = tag;
                    Def = def;
                    Use = use;
                }

                public bool Installed
                {
                    get { return Use == "Installed"; }
                }

                public bool Replaced
                {
                    get { return Use == "Replaced"; }
                }
            }
        }
    }
}
