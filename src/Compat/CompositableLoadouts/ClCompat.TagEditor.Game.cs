using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    internal static partial class ClCompat
    {
        /// <summary>
        /// The private state of the tag editor (<c>Dialog_TagEditor</c>) and the mod helpers its
        /// draw calls inline. Its handlers are mouse-only button bodies with no method to invoke, so
        /// each setter writes the same field the body writes and carries the marker naming it.
        /// </summary>
        internal static class TagEditor
        {
            private static readonly Type categoryEnum;
            private static readonly Type showOnlyEnum;
            private static readonly Type sortByEnum;
            private static readonly Type presetType;
            private static readonly Type prostheticUtilityType;
            private static readonly Type prostheticSlotsType;
            private static readonly Type apparelSlotMakerType;
            private static readonly Type apparelSlotType;
            private static readonly Type tagItemTupleType;

            private static readonly MethodInfo drawMethod;
            private static readonly FieldInfo curTagField;
            private static readonly FieldInfo curStateField;
            private static readonly FieldInfo defFilterField;
            private static readonly FieldInfo lastDefFilterField;
            private static readonly FieldInfo lastDefListField;
            private static readonly FieldInfo showOnlyField;
            private static readonly FieldInfo sortedByKindField;
            private static readonly FieldInfo ascendingField;
            private static readonly FieldInfo selectedStatField;
            private static readonly FieldInfo selectedEffectField;
            private static readonly FieldInfo statLengthField;
            private static readonly FieldInfo statCacheField;
            private static readonly FieldInfo invalidStatField;
            private static readonly FieldInfo addingAlternativesField;
            private static readonly FieldInfo collapsedPanelField;
            private static readonly FieldInfo massAssignElementField;
            private static readonly FieldInfo dummySavedElementField;
            private static readonly MethodInfo resetTabState;
            private static readonly MethodInfo colonyDefs;
            private static readonly MethodInfo addDefToTag;
            private static readonly MethodInfo specifiableMember;

            private static readonly FieldInfo defaultHeightField;
            private static readonly FieldInfo tagDefaultEnabledField;
            private static readonly FieldInfo tagDefaultStateField;
            private static readonly FieldInfo tagDefaultConditionField;
            private static readonly FieldInfo itemMembersField;
            private static readonly FieldInfo itemQuantityField;
            private static readonly FieldInfo itemQuantityStrField;
            private static readonly MethodInfo itemDefsGetter;
            private static readonly MethodInfo itemStuffGetter;
            private static readonly MethodInfo itemPasteFilter;
            private static readonly MethodInfo memberListLabelGetter;
            private static readonly FieldInfo copiedItemField;
            private static readonly ConstructorInfo elementCtor;
            private static readonly MethodInfo elementEquivalent;

            private static readonly FieldInfo[] categoryDefsFields;
            private static readonly FieldInfo baseWeaponStatsField;
            private static readonly FieldInfo meleeWeaponStatsField;
            private static readonly FieldInfo rangedWeaponStatsField;
            private static readonly FieldInfo presetMedicineField;
            private static readonly FieldInfo presetMealsField;
            private static readonly MethodInfo presetLabelGetter;
            private static readonly MethodInfo presetDefsGetter;
            private static readonly MethodInfo presetMakeItem;

            private static readonly MethodInfo isProsthetic;
            private static readonly MethodInfo effectsOf;
            private static readonly MethodInfo maxInstallable;
            private static readonly MethodInfo targetPartsLabel;
            private static readonly ConstructorInfo prostheticSlotsCtor;
            private static readonly MethodInfo slotsFits;
            private static readonly MethodInfo slotsConflicts;
            private static readonly MethodInfo slotsOverlap;
            private static readonly MethodInfo apparelSlotForDef;
            private static readonly MethodInfo apparelSlotForDefs;
            private static readonly MethodInfo apparelSlotIntersects;
            private static readonly MethodInfo statsToDraw;
            private static readonly FieldInfo statEntryValueField;

            private static readonly bool ready;

            static TagEditor()
            {
                var surface = new ReflectionSurface("ClCompat.TagEditor");
                surface.Supplied("Inventory.Dialog_TagEditor", tagEditorType);
                categoryEnum = surface.Type("Inventory.Dialog_TagEditor+State");
                showOnlyEnum = surface.Type("Inventory.Dialog_TagEditor+ShowOnly");
                sortByEnum = surface.Type("Inventory.Dialog_TagEditor+SortBy");
                presetType = surface.Type("Inventory.ItemPreset");
                prostheticUtilityType = surface.Type("Inventory.ProstheticUtility");
                prostheticSlotsType = surface.Type("Inventory.ProstheticSlots");
                apparelSlotMakerType = surface.Type("Inventory.ApparelSlotMaker");
                apparelSlotType = surface.Type("Inventory.ApparelSlot");
                Type guiUtilityType = surface.Type("Inventory.GUIUtility");
                Type uicType = surface.Type("Inventory.UIC");

                drawMethod = surface.Method(tagEditorType, "Draw", new[] { typeof(UnityEngine.Rect) });
                curTagField = surface.Field(tagEditorType, "curTag");
                curStateField = surface.Field(tagEditorType, "curState");
                defFilterField = surface.Field(tagEditorType, "defFilter");
                lastDefFilterField = surface.Field(tagEditorType, "lastDefFilter");
                lastDefListField = surface.Field(tagEditorType, "lastDefList");
                showOnlyField = surface.Field(tagEditorType, "showOnly");
                sortedByKindField = surface.Field(tagEditorType, "sortedByKind");
                ascendingField = surface.Field(tagEditorType, "ascending");
                selectedStatField = surface.Field(tagEditorType, "selectedStat");
                selectedEffectField = surface.Field(tagEditorType, "selectedEffect");
                statLengthField = surface.Field(tagEditorType, "statLength");
                statCacheField = surface.Field(tagEditorType, "statCache");
                invalidStatField = surface.Field(tagEditorType, "INVALID_STAT_VALUE");
                addingAlternativesField = surface.Field(tagEditorType, "addingAlternativesTo");
                collapsedPanelField = surface.Field(tagEditorType, "collapsedPanel");
                massAssignElementField = surface.Field(tagEditorType, "massAssignElement");
                dummySavedElementField = surface.Field(tagEditorType, "dummySavedElement");
                resetTabState = surface.Method(tagEditorType, "ResetTabState", Type.EmptyTypes);
                colonyDefs = surface.Method(tagEditorType, "ColonyDefs", Type.EmptyTypes);
                addDefToTag = surface.Method(tagEditorType, "AddDefToTag", new[] { typeof(ThingDef) });

                defaultHeightField = surface.Field(uicType, "DEFAULT_HEIGHT");
                tagDefaultEnabledField = surface.Field(tagType, "defaultEnabled");
                tagDefaultStateField = surface.Field(tagType, "defaultState");
                tagDefaultConditionField = surface.Field(tagType, "defaultCondition");
                itemMembersField = surface.Field(itemType, "members");
                itemQuantityField = surface.Field(itemType, "quantity");
                itemQuantityStrField = surface.Field(itemType, "quantityStr");
                itemDefsGetter = surface.Property(itemType, "Defs")?.GetGetMethod(true);
                itemStuffGetter = surface.Property(itemType, "RepresentativeStuff")?.GetGetMethod(true);
                memberListLabelGetter = surface.Property(itemMemberType, "ListLabel")?.GetGetMethod(true);

                Type utilityStatics = utilityType;
                categoryDefsFields = new[]
                {
                    surface.Field(utilityStatics, "apparelDefs"),
                    surface.Field(utilityStatics, "meleeWeapons"),
                    surface.Field(utilityStatics, "rangedWeapons"),
                    surface.Field(utilityStatics, "medicinalDefs"),
                    surface.Field(utilityStatics, "items"),
                    surface.Field(utilityStatics, "prostheticDefs"),
                };
                baseWeaponStatsField = surface.Field(itemSpecifierType, "baseWeaponStats");
                meleeWeaponStatsField = surface.Field(itemSpecifierType, "meleeWeaponStats");
                rangedWeaponStatsField = surface.Field(itemSpecifierType, "rangedWeaponStats");
                presetMedicineField = surface.Field(presetType, "Medicine");
                presetMealsField = surface.Field(presetType, "Meals");
                presetLabelGetter = surface.Property(presetType, "Label")?.GetGetMethod(true);
                presetDefsGetter = surface.Property(presetType, "Defs")?.GetGetMethod(true);
                presetMakeItem = surface.Method(presetType, "MakeItem", Type.EmptyTypes);

                isProsthetic = surface.Method(prostheticUtilityType, "IsProsthetic", new[] { typeof(ThingDef) });
                effectsOf = surface.Method(prostheticUtilityType, "EffectsOf", new[] { typeof(ThingDef) });
                maxInstallable = surface.Method(prostheticUtilityType, "MaxInstallable", new[] { typeof(BodyDef), typeof(ThingDef) });
                targetPartsLabel = surface.Method(prostheticUtilityType, "TargetPartsLabel", new[] { typeof(BodyDef), typeof(ThingDef) });
                slotsFits = surface.Method(prostheticSlotsType, "Fits", new[] { typeof(ThingDef) });
                slotsConflicts = surface.Property(prostheticSlotsType, "Conflicts")?.GetGetMethod(true);
                slotsOverlap = surface.Method(prostheticSlotsType, "Overlaps", new[] { typeof(BodyDef), typeof(ThingDef), typeof(ThingDef) });
                apparelSlotForDef = surface.Method(apparelSlotMakerType, "Create", new[] { typeof(BodyDef), typeof(ThingDef) });
                apparelSlotForDefs = surface.Method(apparelSlotMakerType, "Create", new[] { typeof(BodyDef), typeof(IEnumerable<ThingDef>) });
                statsToDraw = surface.Method(typeof(StatsReportUtility), "StatsToDraw", new[] { typeof(Def), typeof(ThingDef) });
                statEntryValueField = surface.Field(typeof(StatDrawEntry), "value");

                if (itemType != null && tagType != null && elementType != null && stateType != null
                    && activeConditionType != null && apparelSlotType != null && guiUtilityType != null)
                {
                    tagItemTupleType = typeof(Tuple<,>).MakeGenericType(tagType, itemType);
                    prostheticSlotsCtor = surface.Constructor(prostheticSlotsType,
                        new[] { typeof(BodyDef), typeof(IEnumerable<>).MakeGenericType(tagItemTupleType) });
                    itemPasteFilter = surface.Method(itemType, "PasteFilter", new[] { itemType, itemType });
                    specifiableMember = surface.Method(tagEditorType, "SpecifiableMember", new[] { itemType });
                    elementCtor = surface.Constructor(elementType, new[] { tagType, stateType, activeConditionType });
                    elementEquivalent = surface.Method(elementType, "Equivalent", new[] { elementType });
                    apparelSlotIntersects = surface.Method(apparelSlotType, "Intersects", new[] { apparelSlotType });
                    Type helper = guiUtilityType.GetNestedType("DraggableHelper`1", BindingFlags.Public | BindingFlags.NonPublic);
                    copiedItemField = surface.Field(helper?.MakeGenericType(itemType), "curState");
                }

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The editor's whole draw; the point to post live edit buffers before.</summary>
            public static MethodInfo DrawMethod
            {
                get { return drawMethod; }
            }

            // ---- Window state ----

            public static object CurTag(Window dialog)
            {
                return Guarded.FieldOf<object>(curTagField, dialog, "ClCompat.TagEditor.CurTag");
            }

            public static void SetCurTag(Window dialog, object tag)
            {
                curTagField.SetValue(dialog, tag); // MUTATION-C: mirrors Dialog_TagEditor.cs:236/244/251; the select, create and delete bodies are inline
            }

            /// <summary>The selected category's ordinal: Apparel, Melee, Ranged, Medicinal, Items, Prosthetics.</summary>
            public static int Category(Window dialog)
            {
                return Convert.ToInt32(Guarded.FieldOf<object>(curStateField, dialog, "ClCompat.TagEditor.Category") ?? 0);
            }

            public static int CategoryCount
            {
                get { return categoryDefsFields.Length; }
            }

            /// <summary>The category icon's click body, less its GUI.FocusControl, which would hand IMGUI focus to the raw search field.</summary>
            public static void SelectCategory(Window dialog, int category)
            {
                curStateField.SetValue(dialog, Enum.ToObject(categoryEnum, category)); // MUTATION-C: mirrors Dialog_TagEditor.cs:585; the icon's body is inline
                lastDefFilterField.SetValue(dialog, string.Empty); // MUTATION-C: mirrors Dialog_TagEditor.cs:586
                lastDefListField.SetValue(dialog, null); // MUTATION-C: mirrors Dialog_TagEditor.cs:587
                Guarded.Call(resetTabState, dialog, "ClCompat.TagEditor.ResetTabState");
            }

            public static string DefFilter(Window dialog)
            {
                return Guarded.FieldOf(defFilterField, dialog, "ClCompat.TagEditor.DefFilter", "");
            }

            public static void SetDefFilter(Window dialog, string value)
            {
                defFilterField.SetValue(dialog, value ?? ""); // MUTATION-C: mirrors Dialog_TagEditor.cs:819 (GUI.TextField writes the field each draw)
            }

            /// <summary>The list the draw cached for its current search, or null when the search has changed since.</summary>
            public static List<ThingDef> CachedFilteredDefs(Window dialog)
            {
                string last = Guarded.FieldOf(lastDefFilterField, dialog, "ClCompat.TagEditor.LastDefFilter", "");
                return last == DefFilter(dialog)
                    ? Guarded.FieldOf<List<ThingDef>>(lastDefListField, dialog, "ClCompat.TagEditor.LastDefList")
                    : null;
            }

            /// <summary>0 all, 1 researched, 2 in the colony.</summary>
            public static int ShowOnly(Window dialog)
            {
                return Convert.ToInt32(Guarded.FieldOf<object>(showOnlyField, dialog, "ClCompat.TagEditor.ShowOnly") ?? 0);
            }

            public static void SetShowOnly(Window dialog, int mode)
            {
                showOnlyField.SetValue(dialog, Enum.ToObject(showOnlyEnum, mode)); // MUTATION-C: mirrors Dialog_TagEditor.cs:738; the option's action is a closure built inside the click
            }

            public static bool SortByStat(Window dialog)
            {
                return Convert.ToInt32(Guarded.FieldOf<object>(sortedByKindField, dialog, "ClCompat.TagEditor.SortedByKind") ?? 0) != 0;
            }

            public static bool Ascending(Window dialog)
            {
                return Guarded.FieldOf(ascendingField, dialog, "ClCompat.TagEditor.Ascending", false);
            }

            public static void SetAscending(Window dialog, bool value)
            {
                ascendingField.SetValue(dialog, value); // MUTATION-C: mirrors Dialog_TagEditor.cs:812; the sort icon's left-click body is inline
            }

            public static StatDef SelectedStat(Window dialog)
            {
                return Guarded.FieldOf<StatDef>(selectedStatField, dialog, "ClCompat.TagEditor.SelectedStat");
            }

            public static string SelectedEffect(Window dialog)
            {
                return Guarded.FieldOf<string>(selectedEffectField, dialog, "ClCompat.TagEditor.SelectedEffect");
            }

            /// <summary>The sort menu's options; stat and effect picks fill the static cache the list sorts by.</summary>
            public static void SetSort(Window dialog, bool byStat, StatDef stat, string effect)
            {
                sortedByKindField.SetValue(dialog, Enum.ToObject(sortByEnum, byStat ? 1 : 0)); // MUTATION-C: mirrors Dialog_TagEditor.cs:768/779/798; each option's action is a closure built inside the click
                selectedStatField.SetValue(dialog, stat); // MUTATION-C: mirrors Dialog_TagEditor.cs:769/780/799
                selectedEffectField.SetValue(dialog, effect); // MUTATION-C: mirrors Dialog_TagEditor.cs:770/781/800
                statLengthField.SetValue(dialog, defaultHeightField.GetValue(null)); // MUTATION-C: mirrors Dialog_TagEditor.cs:771/782/801
            }

            public static Dictionary<ThingDef, float> StatCache()
            {
                return Guarded.FieldOf<Dictionary<ThingDef, float>>(statCacheField, null, "ClCompat.TagEditor.StatCache");
            }

            public static float InvalidStatValue
            {
                get { return Guarded.FieldOf(invalidStatField, null, "ClCompat.TagEditor.InvalidStat", -10000f); }
            }

            public static object AddingAlternativesTo(Window dialog)
            {
                return Guarded.FieldOf<object>(addingAlternativesField, dialog, "ClCompat.TagEditor.AddingAlternativesTo");
            }

            public static void SetAddingAlternativesTo(Window dialog, object item)
            {
                addingAlternativesField.SetValue(dialog, item); // MUTATION-C: mirrors Dialog_TagEditor.cs:362/692; the plus icon and Done bodies are inline
            }

            public static bool PanelCollapsed(Window dialog)
            {
                return Guarded.FieldOf(collapsedPanelField, dialog, "ClCompat.TagEditor.CollapsedPanel", true);
            }

            /// <summary>The plus/minus icon's body: opening the panel creates its element on first use.</summary>
            public static void TogglePanel(Window dialog)
            {
                bool collapsed = !PanelCollapsed(dialog);
                collapsedPanelField.SetValue(dialog, collapsed); // MUTATION-C: mirrors Dialog_TagEditor.cs:155; the icon's body is inline
                if (!collapsed && MassAssignElement(dialog) == null)
                {
                    massAssignElementField.SetValue(dialog, NewElement(CurTag(dialog), null, true)); // MUTATION-C: mirrors Dialog_TagEditor.cs:157
                }
            }

            public static object MassAssignElement(Window dialog)
            {
                return Guarded.FieldOf<object>(massAssignElementField, dialog, "ClCompat.TagEditor.MassAssignElement");
            }

            /// <summary>The tag picker's callback: the panel's element is rebuilt and the default-state dummy dropped.</summary>
            public static void AfterTagSelected(Window dialog, object tag)
            {
                massAssignElementField.SetValue(dialog, NewElement(tag, null, true)); // MUTATION-C: mirrors Dialog_TagEditor.cs:237; the callback is a closure built inside the click
                dummySavedElementField.SetValue(dialog, null); // MUTATION-C: mirrors Dialog_TagEditor.cs:238
            }

            /// <summary>The element whose state the default-state dropdown edits; the draw copies it onto the tag each frame.</summary>
            public static object DefaultStateElement(Window dialog)
            {
                return Guarded.FieldOf<object>(dummySavedElementField, dialog, "ClCompat.TagEditor.DummySavedElement");
            }

            public static HashSet<ThingDef> ColonyDefs(Window dialog)
            {
                return Guarded.Call(colonyDefs, dialog, "ClCompat.TagEditor.ColonyDefs") as HashSet<ThingDef> ?? new HashSet<ThingDef>();
            }

            /// <summary>A def-list click: a new item, or an alternative while one is being added to.</summary>
            public static void AddDefToTag(Window dialog, ThingDef def)
            {
                Guarded.Call(addDefToTag, dialog, "ClCompat.TagEditor.AddDefToTag", def);
            }

            /// <summary>The member the Edit icon opens the item specifier on, or null when the icon is not drawn.</summary>
            public static object SpecifiableMember(object item)
            {
                return Guarded.Call(specifiableMember, null, "ClCompat.TagEditor.SpecifiableMember", item);
            }

            // ---- Tags and items ----

            public static bool TagDefaultEnabled(object tag)
            {
                return Guarded.FieldOf(tagDefaultEnabledField, tag, "ClCompat.TagEditor.DefaultEnabled", false);
            }

            public static void SetTagDefaultEnabled(object tag, bool value)
            {
                tagDefaultEnabledField.SetValue(tag, value); // MUTATION-C: mirrors Dialog_TagEditor.cs:281; DraggableCheckbox writes the field by ref
            }

            public static string TagDefaultState(object tag)
            {
                return Guarded.FieldOf<string>(tagDefaultStateField, tag, "ClCompat.TagEditor.DefaultState");
            }

            public static bool TagDefaultActiveWhenStateActive(object tag)
            {
                object condition = Guarded.FieldOf<object>(tagDefaultConditionField, tag, "ClCompat.TagEditor.DefaultCondition");
                return condition == null || Equals(condition, Condition(true));
            }

            public static void SetTagName(object tag, string value)
            {
                tagNameField.SetValue(tag, value ?? ""); // MUTATION-C: mirrors Dialog_TagEditor.cs:293 (GUI.TextField writes the field each draw)
            }

            /// <summary>The tag's live item list, the one the remove and drag bodies edit.</summary>
            public static IList ItemsList(object tag)
            {
                return Guarded.FieldOf<IList>(tagItemsField, tag, "ClCompat.TagEditor.ItemsList");
            }

            /// <summary>The item's live member list, best first, the one the remove and drag bodies edit.</summary>
            public static IList MembersList(object item)
            {
                return Guarded.FieldOf<IList>(itemMembersField, item, "ClCompat.TagEditor.MembersList");
            }

            public static List<ThingDef> ItemDefs(object item)
            {
                var defs = new List<ThingDef>();
                if (Guarded.Get<IEnumerable>(itemDefsGetter, item, "ClCompat.TagEditor.ItemDefs") is IEnumerable raw)
                {
                    foreach (object def in raw)
                    {
                        if (def is ThingDef thingDef)
                        {
                            defs.Add(thingDef);
                        }
                    }
                }
                return defs;
            }

            public static ThingDef ItemRepresentativeStuff(object item)
            {
                return Guarded.Get<ThingDef>(itemStuffGetter, item, "ClCompat.TagEditor.ItemStuff");
            }

            public static string QuantityBuffer(object item)
            {
                return Guarded.FieldOf<string>(itemQuantityStrField, item, "ClCompat.TagEditor.QuantityBuffer");
            }

            /// <summary>The quantity box's per-keystroke parse: a typed number is clamped and its text normalised, a blank box keeps the quantity.</summary>
            public static void SetQuantityText(object item, string text, int max)
            {
                if (int.TryParse(text, out int typed))
                {
                    int clamped = UnityEngine.Mathf.Clamp(typed, 1, max);
                    itemQuantityField.SetValue(item, clamped); // MUTATION-C: mirrors Widgets.TextFieldNumeric/ResolveParseNow (Dialog_TagEditor.cs:381); the box is a raw IMGUI field the shell cannot type into
                    itemQuantityStrField.SetValue(item, clamped.ToString()); // MUTATION-C: mirrors Widgets.ResolveParseNow's buffer rewrite
                }
                else if (string.IsNullOrEmpty(text))
                {
                    itemQuantityStrField.SetValue(item, ""); // MUTATION-C: mirrors Widgets.TextFieldNumeric keeping a blank partial entry
                }
            }

            public static void PasteFilter(object from, object to)
            {
                Guarded.Call(itemPasteFilter, null, "ClCompat.TagEditor.PasteFilter", from, to);
            }

            /// <summary>The copy/paste icons' shared clipboard (<c>GUIUtility.DraggableHelper&lt;Item&gt;.curState</c>).</summary>
            public static object CopiedItem()
            {
                return Guarded.FieldOf<object>(copiedItemField, null, "ClCompat.TagEditor.CopiedItem");
            }

            public static void CopyItem(object item)
            {
                copiedItemField.SetValue(null, item); // MUTATION-C: mirrors GUIUtility.cs:212; the copy icon's body is inline in DraggableCopyPaste
            }

            public static string MemberListLabel(object member)
            {
                return Guarded.Get(memberListLabelGetter, member, "ClCompat.TagEditor.MemberListLabel", "");
            }

            public static object NewElement(object tag, object state, bool activeWhenStateActive)
            {
                return Construct(elementCtor, "ClCompat.TagEditor.NewElement", tag, state, Condition(activeWhenStateActive));
            }

            public static bool ElementsEquivalent(object element, object other)
            {
                return element != null && other != null
                    && Guarded.Call(elementEquivalent, element, "ClCompat.TagEditor.Equivalent", other) is bool same && same;
            }

            // ---- The def browser's sources ----

            public static List<ThingDef> CategoryDefs(int category)
            {
                return category >= 0 && category < categoryDefsFields.Length
                    ? Guarded.FieldOf<List<ThingDef>>(categoryDefsFields[category], null, "ClCompat.TagEditor.CategoryDefs") ?? new List<ThingDef>()
                    : new List<ThingDef>();
            }

            /// <summary>The stats DrawItemColumns adds to the sort menu: the item specifier's weapon stats for Melee and Ranged.</summary>
            public static List<StatDef> CategoryExtraStats(int category)
            {
                var stats = new List<StatDef>();
                if (category != 1 && category != 2)
                {
                    return stats;
                }
                FieldInfo extra = category == 1 ? meleeWeaponStatsField : rangedWeaponStatsField;
                foreach (FieldInfo field in new[] { baseWeaponStatsField, extra })
                {
                    List<StatDef> list = Guarded.FieldOf<List<StatDef>>(field, null, "ClCompat.TagEditor.WeaponStats");
                    if (list == null)
                    {
                        continue;
                    }
                    foreach (StatDef stat in list)
                    {
                        if (!stats.Contains(stat))
                        {
                            stats.Add(stat);
                        }
                    }
                }
                return stats;
            }

            /// <summary>The preset pinned above the list: Medicine for Medicinal, Meals for Items, else null.</summary>
            public static object CategoryPreset(int category)
            {
                FieldInfo field = category == 3 ? presetMedicineField : category == 4 ? presetMealsField : null;
                return field == null ? null : Guarded.FieldOf<object>(field, null, "ClCompat.TagEditor.Preset");
            }

            public static string PresetLabel(object preset)
            {
                return Guarded.Get(presetLabelGetter, preset, "ClCompat.TagEditor.PresetLabel", "");
            }

            public static List<ThingDef> PresetDefs(object preset)
            {
                return Guarded.Get<List<ThingDef>>(presetDefsGetter, preset, "ClCompat.TagEditor.PresetDefs") ?? new List<ThingDef>();
            }

            public static object PresetMakeItem(object preset)
            {
                return Guarded.Call(presetMakeItem, preset, "ClCompat.TagEditor.PresetMakeItem");
            }

            public static List<StatDrawEntry> StatsToDraw(ThingDef def, ThingDef stuff)
            {
                var entries = new List<StatDrawEntry>();
                if (Guarded.Call(statsToDraw, null, "ClCompat.TagEditor.StatsToDraw", def, stuff) is IEnumerable<StatDrawEntry> raw)
                {
                    entries.AddRange(raw);
                }
                return entries;
            }

            public static float StatEntryValue(StatDrawEntry entry)
            {
                return Guarded.FieldOf(statEntryValueField, entry, "ClCompat.TagEditor.StatEntryValue", 0f);
            }

            // ---- Prosthetics and apparel slots ----

            public static bool IsProsthetic(ThingDef def)
            {
                return def != null && Guarded.Call(isProsthetic, null, "ClCompat.TagEditor.IsProsthetic", def) is bool yes && yes;
            }

            public static IEnumerable<KeyValuePair<string, float>> EffectsOf(ThingDef def)
            {
                return Guarded.Call(effectsOf, null, "ClCompat.TagEditor.EffectsOf", def) as IEnumerable<KeyValuePair<string, float>>
                    ?? new List<KeyValuePair<string, float>>();
            }

            public static int MaxInstallable(ThingDef def)
            {
                return Guarded.Call(maxInstallable, null, "ClCompat.TagEditor.MaxInstallable", BodyDefOf.Human, def) is int max ? max : 1;
            }

            public static string TargetPartsLabel(ThingDef def)
            {
                return Guarded.Call(targetPartsLabel, null, "ClCompat.TagEditor.TargetPartsLabel", BodyDefOf.Human, def) as string;
            }

            /// <summary>The tag's prosthetic placements, as the draw builds them from every item in priority order.</summary>
            public static object ProstheticSlots(object tag)
            {
                IList tuples = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tagItemTupleType));
                foreach (object item in TagItems(tag))
                {
                    tuples.Add(Activator.CreateInstance(tagItemTupleType, tag, item));
                }
                return Construct(prostheticSlotsCtor, "ClCompat.TagEditor.ProstheticSlots", BodyDefOf.Human, tuples);
            }

            public static bool SlotsFit(object slots, ThingDef def)
            {
                return slots == null || (Guarded.Call(slotsFits, slots, "ClCompat.TagEditor.SlotsFit", def) is bool fits && fits);
            }

            /// <summary>Whether the item cannot be installed, with the tag in the way (null: the body has too few parts).</summary>
            public static bool SlotsConflict(object slots, object item, out object blocker)
            {
                blocker = null;
                if (!(Guarded.Get<IDictionary>(slotsConflicts, slots, "ClCompat.TagEditor.SlotsConflicts") is IDictionary conflicts)
                    || item == null || !conflicts.Contains(item))
                {
                    return false;
                }
                blocker = conflicts[item];
                return true;
            }

            public static bool ProstheticsOverlap(ThingDef a, ThingDef b)
            {
                return Guarded.Call(slotsOverlap, null, "ClCompat.TagEditor.ProstheticsOverlap", BodyDefOf.Human, a, b) is bool overlap && overlap;
            }

            public static object ApparelSlot(ThingDef def)
            {
                return Guarded.Call(apparelSlotForDef, null, "ClCompat.TagEditor.ApparelSlot", BodyDefOf.Human, def);
            }

            public static object ApparelSlot(List<ThingDef> defs)
            {
                return Guarded.Call(apparelSlotForDefs, null, "ClCompat.TagEditor.ApparelSlots", BodyDefOf.Human, defs);
            }

            public static bool SlotsIntersect(object slot, object other)
            {
                return slot != null && other != null
                    && Guarded.Call(apparelSlotIntersects, slot, "ClCompat.TagEditor.SlotsIntersect", other) is bool hit && hit;
            }
        }
    }
}
