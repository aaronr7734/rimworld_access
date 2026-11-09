using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.DialogObjects</c> (Apparel and Weapon modes),
    /// backing <see cref="ObjectsAdapter"/>. Follows <see cref="CharEditorBrowserCompat"/>'s idiom
    /// — presence via <see cref="CharEditorCompat.ModPresent"/>, every member resolved behind
    /// <see cref="Ready"/>, degrade-not-throw — but stands alone: <c>DialogObjects</c> is a
    /// <c>DialogTemplate&lt;ThingDef&gt;</c>, and its live selection lives on a separate internal
    /// type reached through <c>ThingTool.SelectedThing</c>.
    /// Opening rides <c>BlockPerson.AOnTextureApparel</c>/<c>AOnTextureWeapon</c> in the Appearance
    /// facade; this file only reads and writes an already-open dialog.
    /// The mod-name filter has a named vehicle (<c>ASelectedModName</c>, which sets
    /// <c>search.modName</c> and rebuilds <c>lDefs</c> in one call); the mode-specific filters do
    /// not — their handlers are anonymous delegates, hence the MUTATION-C members below.
    /// The parameter pane (quality/stuff/style/stack) is deliberately reachable here even though
    /// <c>DialogTemplate.DoWindowContents</c> draws it only while the session-global
    /// <c>CEditor.IsExtendedUI</c> width toggle is on: that toggle is one click away for a mouse
    /// player, and these are core equip-time choices. Every write reproduces the field and method
    /// calls the mod's own NavSelector widgets make, never the draw calls.
    /// <c>ABuy</c> reads <c>UI.MouseCell()</c> synchronously for its affordability check, so
    /// <see cref="ArmBuy"/> wraps it in
    /// <see cref="Shell.DevToolTargeting.WithCursorOverride{T}"/>; it also closes the editor and
    /// this dialog itself, unlike <see cref="ArmDestroy"/> and the placing-mode Confirm path.
    /// Placing mode pins the dialog through <c>mInPlacingMode</c>, an internal field on the BASE
    /// class, and <c>DialogTemplate.DoAndClose</c> then declines to close while pinned — so
    /// <see cref="ObjectsAdapter.Confirm"/> closes the dialog itself, but only once a tool armed.
    /// </summary>
    internal static class CharEditorObjectsCompat
    {
        /// <summary>Local mirror of the internal <c>CharacterEditor.DialogType</c> enum, round-tripped by name.</summary>
        internal enum ObjectsMode { Object, Weapon, Apparel }

        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorObjectsCompat", Resolve);

        private static Type dialogObjectsType;
        private static Type dialogTypeEnumType;
        private static Type weaponTypeEnumType;
        private static Type dialogCapsuleUiType;
        private static Type searchToolType;
        private static Type selectedType;
        private static Type thingToolType;
        private static Type weaponToolType;
        private static Type labelType;

        // DialogObjects / DialogTemplate<ThingDef> base fields.
        private static FieldInfo searchField;
        private static FieldInfo lModsField;
        private static FieldInfo lDefsField;
        private static FieldInfo selectedDefField;
        private static FieldInfo mDialogTypeField;
        private static MethodInfo tListMethod;
        private static MethodInfo aSelectedModNameMethod;
        private static MethodInfo onSelectionChangedMethod;

        // The creature pane's private faction scratch, and the 4-arg constructor that opens the
        // dialog preloaded for editing an existing thing. No named handler exists for that, so
        // constructing the mod's own dialog type is the vehicle.
        private static FieldInfo dialogTempFactionField;
        private static FieldInfo dialogLFactionsField;
        private static ConstructorInfo dialogObjectsEditCtor;

        // Placing mode / Buy / Destroy. mInPlacingMode is declared on the base DialogTemplate<T>,
        // so it resolves via dialogObjectsType like the base's other fields above.
        private static MethodInfo aPlacingModeMethod;
        private static MethodInfo aRotatePlacingMethod;
        private static MethodInfo aDestroyMethod;
        private static MethodInfo aBuyMethod;
        private static FieldInfo mInPlacingModeField;

        // SearchTool fields. The shared mod-name slot lives in CharEditorCompat.Search.
        private static FieldInfo searchWeaponTypeField;
        private static FieldInfo searchApparelLayerField;
        private static FieldInfo searchBodyPartGroupField;
        // Object mode's two filters. Verse.ThingCategory is a public vanilla enum, nameable here.
        private static FieldInfo searchThingCategoryDefField;
        private static FieldInfo searchThingCategoryField;

        // ThingTool / WeaponTool statics.
        private static PropertyInfo selectedThingProperty;
        private static PropertyInfo allWeaponTypeProperty;
        private static PropertyInfo allApparelLayerDefProperty;
        private static PropertyInfo allBodyPartGroupDefProperty;
        private static PropertyInfo allQualityCategoryProperty;
        private static PropertyInfo allThingCategoryDefProperty;
        private static PropertyInfo allThingCategoryProperty;
        private static MethodInfo getNameForWeaponTypeMethod;

        // Selected instance members.
        private static FieldInfo selThingDefField;
        private static FieldInfo selStuffField;
        private static FieldInfo selStyleField;
        private static FieldInfo selQualityField;
        private static FieldInfo selStackValField;
        private static FieldInfo selBuyPriceField;
        private static FieldInfo selLOfStuffField;
        private static FieldInfo selLOfStyleField;
        private static PropertyInfo selHasQualityProperty;
        private static PropertyInfo selHasStackProperty;
        private static MethodInfo selSetStuffDefMethod;     // SetStuff(ThingDef)
        private static MethodInfo selSetStyleDefMethod;     // SetStyle(ThingStyleDef)
        private static MethodInfo selUpdateBuyPriceMethod;
        // Object mode's creature pane (gender/age/faction), gated on Selected.HasRace.
        private static FieldInfo selGenderField;
        private static FieldInfo selAgeField;
        private static PropertyInfo selHasRaceProperty;

        // The mod's own label table, read live rather than re-translated.
        private static FieldInfo labelLayerField;
        private static FieldInfo labelBodyPartGroupsField;
        private static FieldInfo labelQualityField;
        private static FieldInfo labelStuffField;
        private static FieldInfo labelStyleField;
        private static FieldInfo labelCountField;
        private static FieldInfo labelBuyField;
        private static FieldInfo labelPriceField;
        private static FieldInfo labelWeaponField;
        private static FieldInfo labelObjectField;
        private static FieldInfo labelNoneField;
        private static FieldInfo labelAllField;
        private static FieldInfo labelDestroyField;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get { gate.Ensure(); return gate.Ensure(); }
        }

        public static Type DialogType
        {
            get { gate.Ensure(); return dialogObjectsType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            dialogObjectsType = surface.Type("CharacterEditor.DialogObjects");
            dialogTypeEnumType = surface.Type("CharacterEditor.DialogType");
            weaponTypeEnumType = surface.Type("CharacterEditor.WeaponType");
            dialogCapsuleUiType = surface.Type("CharacterEditor.DialogCapsuleUI");
            searchToolType = surface.Supplied("CharacterEditor.SearchTool", CharEditorCompat.Search.SearchToolType);
            selectedType = surface.Type("CharacterEditor.Selected");
            thingToolType = surface.Type("CharacterEditor.ThingTool");
            weaponToolType = surface.Type("CharacterEditor.WeaponTool");
            labelType = surface.Supplied("CharacterEditor.Label", CharEditorCompat.Labels.LabelType);

            searchField = surface.Field(dialogObjectsType, "search");
            lModsField = surface.Field(dialogObjectsType, "lMods");
            lDefsField = surface.Field(dialogObjectsType, "lDefs");
            selectedDefField = surface.Field(dialogObjectsType, "selectedDef");
            mDialogTypeField = surface.Field(dialogObjectsType, "mDialogType");
            tListMethod = surface.Method(dialogObjectsType, "TList", Type.EmptyTypes);
            aSelectedModNameMethod = surface.Method(dialogObjectsType, "ASelectedModName", new[] { typeof(string) });
            onSelectionChangedMethod = surface.Method(dialogObjectsType, "OnSelectionChanged", Type.EmptyTypes);
            dialogTempFactionField = surface.Field(dialogObjectsType, "tempFaction");
            dialogLFactionsField = surface.Field(dialogObjectsType, "lFactions");

            aPlacingModeMethod = surface.Method(dialogObjectsType, "APlacingMode", Type.EmptyTypes);
            aRotatePlacingMethod = surface.Method(dialogObjectsType, "ARotate", Type.EmptyTypes);
            aDestroyMethod = surface.Method(dialogObjectsType, "ADestroy", Type.EmptyTypes);
            aBuyMethod = surface.Method(dialogObjectsType, "ABuy", Type.EmptyTypes);
            mInPlacingModeField = surface.Field(dialogObjectsType, "mInPlacingMode"); // declared on the DialogTemplate<T> base

            // Both signatures name a mod-internal type, so they resolve only after it does.
            dialogObjectsEditCtor = dialogTypeEnumType == null || dialogCapsuleUiType == null ? null
                : surface.Constructor(dialogObjectsType,
                    new[] { dialogTypeEnumType, dialogCapsuleUiType, typeof(ThingWithComps), typeof(bool) });
            getNameForWeaponTypeMethod = weaponTypeEnumType == null ? null
                : surface.Method(weaponToolType, "GetNameForWeaponType", new[] { weaponTypeEnumType });

            searchWeaponTypeField = surface.Field(searchToolType, "weaponType");
            searchApparelLayerField = surface.Field(searchToolType, "apparelLayerDef");
            searchBodyPartGroupField = surface.Field(searchToolType, "bodyPartGroupDef");
            searchThingCategoryDefField = surface.Field(searchToolType, "thingCategoryDef");
            searchThingCategoryField = surface.Field(searchToolType, "thingCategory");

            selectedThingProperty = surface.Property(thingToolType, "SelectedThing");
            allWeaponTypeProperty = surface.Property(thingToolType, "AllWeaponType");
            allApparelLayerDefProperty = surface.Property(thingToolType, "AllApparelLayerDef");
            allBodyPartGroupDefProperty = surface.Property(thingToolType, "AllBodyPartGroupDef");
            allQualityCategoryProperty = surface.Property(thingToolType, "AllQualityCategory");
            allThingCategoryDefProperty = surface.Property(thingToolType, "AllThingCategoryDef");
            allThingCategoryProperty = surface.Property(thingToolType, "AllThingCategory");

            selThingDefField = surface.Field(selectedType, "thingDef");
            selStuffField = surface.Field(selectedType, "stuff");
            selStyleField = surface.Field(selectedType, "style");
            selQualityField = surface.Field(selectedType, "quality");
            selStackValField = surface.Field(selectedType, "stackVal");
            selBuyPriceField = surface.Field(selectedType, "buyPrice");
            selLOfStuffField = surface.Field(selectedType, "lOfStuff");
            selLOfStyleField = surface.Field(selectedType, "lOfStyle");
            selHasQualityProperty = surface.Property(selectedType, "HasQuality");
            selHasStackProperty = surface.Property(selectedType, "HasStack");
            selSetStuffDefMethod = surface.Method(selectedType, "SetStuff", new[] { typeof(ThingDef) });
            selSetStyleDefMethod = surface.Method(selectedType, "SetStyle", new[] { typeof(ThingStyleDef) });
            selUpdateBuyPriceMethod = surface.Method(selectedType, "UpdateBuyPrice", Type.EmptyTypes);
            selGenderField = surface.Field(selectedType, "gender");
            selAgeField = surface.Field(selectedType, "age");
            selHasRaceProperty = surface.Property(selectedType, "HasRace");

            labelLayerField = surface.Field(labelType, "LAYER");
            labelBodyPartGroupsField = surface.Field(labelType, "BODYPARTGROUPS");
            labelQualityField = surface.Field(labelType, "QUALITY");
            labelStuffField = surface.Field(labelType, "STUFF");
            labelStyleField = surface.Field(labelType, "STYLE");
            labelCountField = surface.Field(labelType, "COUNT");
            labelBuyField = surface.Field(labelType, "BUY");
            labelPriceField = surface.Field(labelType, "PRICE");
            labelWeaponField = surface.Field(labelType, "WEAPON");
            labelObjectField = surface.Field(labelType, "OBJECT");
            labelNoneField = surface.Field(labelType, "NONE");
            labelAllField = surface.Field(labelType, "ALL");
            labelDestroyField = surface.Field(labelType, "DESTROY");

            return surface.Ready && CharEditorCompat.Search.Ready && CharEditorCompat.Labels.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorObjectsCompat." + member + " failed: " + ex.Message);
        }

        // Live labels (Label.* read live).

        public static string LayerLabel => LabelValue(labelLayerField);
        public static string BodyPartGroupsLabel => LabelValue(labelBodyPartGroupsField);
        public static string QualityLabel => LabelValue(labelQualityField);
        public static string StuffLabel => LabelValue(labelStuffField);
        public static string StyleLabel => LabelValue(labelStyleField);
        public static string CountLabel => LabelValue(labelCountField);
        public static string BuyLabel => LabelValue(labelBuyField);
        public static string PriceLabel => LabelValue(labelPriceField);
        public static string WeaponModeLabel => LabelValue(labelWeaponField);
        public static string ObjectModeLabel => LabelValue(labelObjectField);
        public static string NoneLabel => LabelValue(labelNoneField);
        public static string AllLabel => LabelValue(labelAllField);
        public static string DestroyLabel => LabelValue(labelDestroyField);

        private static string LabelValue(FieldInfo field)
        {
            if (!Ready || field == null)
                return "";
            try
            {
                return field.GetValue(null) as string ?? "";
            }
            catch (Exception ex)
            {
                Fail("Label:" + field.Name, ex);
                return "";
            }
        }

        // Mode (read-only presence row).

        /// <summary>Reads DialogObjects.mDialogType live, round-tripped by name: the field is a boxed CharacterEditor-internal enum.</summary>
        public static ObjectsMode CurrentMode(Window dlg)
        {
            if (!Ready || dlg == null)
                return ObjectsMode.Apparel;
            try
            {
                object v = mDialogTypeField.GetValue(dlg);
                return v != null && Enum.TryParse(v.ToString(), out ObjectsMode m) ? m : ObjectsMode.Apparel;
            }
            catch (Exception ex)
            {
                Fail("CurrentMode", ex);
                return ObjectsMode.Apparel;
            }
        }

        // Placing mode / Buy / Destroy. Every mutation rides the mod's own DialogObjects
        // instance method (vehicle A); the class remarks cover the arming and closing rules.

        /// <summary>Mirrors DrawLowerButtons' own `!CEditor.InStartingScreen` gate: this row family is drawn only outside world-gen.</summary>
        public static bool PlacingAvailable => Ready && !CharEditorCompat.InStartingScreen;

        /// <summary>DialogTemplate&lt;T&gt;.mInPlacingMode -- true once ActivatePlacingMode has pinned this dialog for placement.</summary>
        public static bool InPlacingMode(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                return (bool)mInPlacingModeField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("InPlacingMode", ex);
                return false;
            }
        }

        /// <summary>Vehicle A: APlacingMode() pins the dialog, resets rotation to North, and closes the editor. Arms no DebugTool.</summary>
        public static void ActivatePlacingMode(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aPlacingModeMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("ActivatePlacingMode", ex);
            }
        }

        /// <summary>Vehicle A: ARotate() cycles PlacingTool.rotation N/E/S/W (DialogObjects.cs:473-495).</summary>
        public static void RotatePlacing(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aRotatePlacingMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("RotatePlacing", ex);
            }
        }

        /// <summary>Vehicle A: ADestroy() unconditionally arms PlacingTool.Destroy(), which clears the roof and every destroyable thing on the targeted cell.</summary>
        public static void ArmDestroy(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aDestroyMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("ArmDestroy", ex);
            }
        }

        /// <summary>
        /// Vehicle A: ABuy(). Its handoff reads UI.MouseCell() synchronously for an affordability
        /// check, so it runs under the same keyboard-cursor override
        /// DevToolTargeting.FireAtKeyboardCursor uses. The purchase confirmation it then opens is
        /// a real Dialog_MessageBox, and ABuy closes the editor and this dialog itself.
        /// </summary>
        public static void ArmBuy(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                DevToolTargeting.WithCursorOverride(MapNavigationState.CurrentCursorPosition, () =>
                {
                    aBuyMethod.Invoke(dlg, null);
                    return true;
                });
            }
            catch (Exception ex)
            {
                Fail("ArmBuy", ex);
            }
        }

        // Opening, preloaded for editing (the Inventory section's row-click path).

        /// <summary>
        /// Opens DialogObjects preloaded with <paramref name="thing"/>, the vehicle DrawThingRow's
        /// own delegate uses. Constructing the mod's dialog type is vehicle A here: no named
        /// handler exists. The add reproduces <c>WindowTool.Open</c> exactly — set
        /// <c>layer = WindowLayer.Dialog</c>, then <c>Find.WindowStack.Add</c>.
        /// </summary>
        public static void OpenForEdit(ThingWithComps thing, ObjectsMode mode)
        {
            if (!Ready || thing == null || dialogObjectsEditCtor == null)
                return;
            try
            {
                object boxedType = Enum.Parse(dialogTypeEnumType, mode.ToString());
                var window = (Window)dialogObjectsEditCtor.Invoke(new object[] { boxedType, null, thing, false });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail("OpenForEdit", ex);
            }
        }

        // Mod-name filter (DialogTemplate<T>'s base-class row; every mode draws it).

        public static string ModName(Window dlg)
        {
            return CharEditorCompat.Search.ModName(SearchInstance(dlg));
        }

        public static List<string> ModNameCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<string>();
            try
            {
                var set = lModsField.GetValue(dlg) as HashSet<string>;
                return set != null ? set.ToList() : new List<string>();
            }
            catch (Exception ex)
            {
                Fail("ModNameCandidates", ex);
                return new List<string>();
            }
        }

        /// <summary>Vehicle A: ASelectedModName sets search.modName and rebuilds lDefs in one call.</summary>
        public static void SetModName(Window dlg, string value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aSelectedModNameMethod.Invoke(dlg, new object[] { value });
            }
            catch (Exception ex)
            {
                Fail("SetModName", ex);
            }
        }

        private static object SearchInstance(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return searchField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("SearchInstance", ex);
                return null;
            }
        }

        // Weapon-mode filter. WeaponType is CharacterEditor-internal, so values stay boxed.

        public static object WeaponTypeFilter(Window dlg)
        {
            object search = SearchInstance(dlg);
            if (search == null)
                return null;
            try
            {
                return searchWeaponTypeField.GetValue(search);
            }
            catch (Exception ex)
            {
                Fail("WeaponTypeFilter", ex);
                return null;
            }
        }

        /// <summary>ThingTool.AllWeaponType, boxed element-by-element: WeaponType cannot be named here.</summary>
        public static List<object> WeaponTypeCandidates()
        {
            var result = new List<object>();
            if (!Ready)
                return result;
            try
            {
                object set = allWeaponTypeProperty.GetValue(null);
                if (set is System.Collections.IEnumerable enumerable)
                {
                    foreach (object v in enumerable)
                        result.Add(v);
                }
            }
            catch (Exception ex)
            {
                Fail("WeaponTypeCandidates", ex);
            }
            return result;
        }

        /// <summary>WeaponTool.GetNameForWeaponType: the mod's own live label for a WeaponType value.</summary>
        public static string WeaponTypeLabel(object weaponType)
        {
            if (!Ready || weaponType == null)
                return "";
            try
            {
                return getNameForWeaponTypeMethod.Invoke(null, new[] { weaponType }) as string ?? "";
            }
            catch (Exception ex)
            {
                Fail("WeaponTypeLabel", ex);
                return "";
            }
        }

        /// <summary>MUTATION-C: reproduces DrawCustomFilter's inline Weapon-mode delegate body (DialogObjects.cs:297-301) -- no named setter to ride.</summary>
        public static void SetWeaponTypeFilter(Window dlg, object weaponType)
        {
            object search = SearchInstance(dlg);
            if (!Ready || dlg == null || search == null)
                return;
            try
            {
                searchWeaponTypeField.SetValue(search, weaponType);
                object result = tListMethod.Invoke(dlg, null);
                lDefsField.SetValue(dlg, result);
            }
            catch (Exception ex)
            {
                Fail("SetWeaponTypeFilter", ex);
            }
        }

        // Apparel-mode filters: layer, body part group (both public vanilla Defs).

        public static ApparelLayerDef ApparelLayerFilter(Window dlg)
        {
            object search = SearchInstance(dlg);
            if (search == null)
                return null;
            try
            {
                return searchApparelLayerField.GetValue(search) as ApparelLayerDef;
            }
            catch (Exception ex)
            {
                Fail("ApparelLayerFilter", ex);
                return null;
            }
        }

        public static List<ApparelLayerDef> ApparelLayerCandidates()
        {
            if (!Ready)
                return new List<ApparelLayerDef>();
            try
            {
                var set = allApparelLayerDefProperty.GetValue(null) as HashSet<ApparelLayerDef>;
                return set != null ? set.ToList() : new List<ApparelLayerDef>();
            }
            catch (Exception ex)
            {
                Fail("ApparelLayerCandidates", ex);
                return new List<ApparelLayerDef>();
            }
        }

        /// <summary>MUTATION-C: reproduces DrawCustomFilter's inline Apparel-mode layer delegate body (DialogObjects.cs:306-310).</summary>
        public static void SetApparelLayerFilter(Window dlg, ApparelLayerDef layer)
        {
            object search = SearchInstance(dlg);
            if (!Ready || dlg == null || search == null)
                return;
            try
            {
                searchApparelLayerField.SetValue(search, layer);
                object result = tListMethod.Invoke(dlg, null);
                lDefsField.SetValue(dlg, result);
            }
            catch (Exception ex)
            {
                Fail("SetApparelLayerFilter", ex);
            }
        }

        public static BodyPartGroupDef BodyPartGroupFilter(Window dlg)
        {
            object search = SearchInstance(dlg);
            if (search == null)
                return null;
            try
            {
                return searchBodyPartGroupField.GetValue(search) as BodyPartGroupDef;
            }
            catch (Exception ex)
            {
                Fail("BodyPartGroupFilter", ex);
                return null;
            }
        }

        public static List<BodyPartGroupDef> BodyPartGroupCandidates()
        {
            if (!Ready)
                return new List<BodyPartGroupDef>();
            try
            {
                var set = allBodyPartGroupDefProperty.GetValue(null) as HashSet<BodyPartGroupDef>;
                return set != null ? set.ToList() : new List<BodyPartGroupDef>();
            }
            catch (Exception ex)
            {
                Fail("BodyPartGroupCandidates", ex);
                return new List<BodyPartGroupDef>();
            }
        }

        /// <summary>MUTATION-C: reproduces DrawCustomFilter's inline Apparel-mode body-part-group delegate body (DialogObjects.cs:311-315).</summary>
        public static void SetBodyPartGroupFilter(Window dlg, BodyPartGroupDef group)
        {
            object search = SearchInstance(dlg);
            if (!Ready || dlg == null || search == null)
                return;
            try
            {
                searchBodyPartGroupField.SetValue(search, group);
                object result = tListMethod.Invoke(dlg, null);
                lDefsField.SetValue(dlg, result);
            }
            catch (Exception ex)
            {
                Fail("SetBodyPartGroupFilter", ex);
            }
        }

        // Object-mode filters. Verse.ThingCategory is a public vanilla enum: no boxing needed.

        public static ThingCategoryDef ThingCategoryDefFilter(Window dlg)
        {
            object search = SearchInstance(dlg);
            if (search == null)
                return null;
            try
            {
                return searchThingCategoryDefField.GetValue(search) as ThingCategoryDef;
            }
            catch (Exception ex)
            {
                Fail("ThingCategoryDefFilter", ex);
                return null;
            }
        }

        public static List<ThingCategoryDef> ThingCategoryDefCandidates()
        {
            if (!Ready)
                return new List<ThingCategoryDef>();
            try
            {
                var set = allThingCategoryDefProperty.GetValue(null) as HashSet<ThingCategoryDef>;
                return set != null ? set.ToList() : new List<ThingCategoryDef>();
            }
            catch (Exception ex)
            {
                Fail("ThingCategoryDefCandidates", ex);
                return new List<ThingCategoryDef>();
            }
        }

        /// <summary>MUTATION-C: reproduces DrawCustomFilter's inline Object-mode category delegate body (DialogObjects.cs:320-324).</summary>
        public static void SetThingCategoryDefFilter(Window dlg, ThingCategoryDef category)
        {
            object search = SearchInstance(dlg);
            if (!Ready || dlg == null || search == null)
                return;
            try
            {
                // MUTATION-C: mirrors DrawCustomFilter's Object-mode category delegate (DialogObjects.cs:320-324).
                searchThingCategoryDefField.SetValue(search, category);
                object result = tListMethod.Invoke(dlg, null);
                lDefsField.SetValue(dlg, result);
            }
            catch (Exception ex)
            {
                Fail("SetThingCategoryDefFilter", ex);
            }
        }

        public static ThingCategory ThingCategoryFilter(Window dlg)
        {
            object search = SearchInstance(dlg);
            if (search == null)
                return ThingCategory.None;
            try
            {
                return (ThingCategory)searchThingCategoryField.GetValue(search);
            }
            catch (Exception ex)
            {
                Fail("ThingCategoryFilter", ex);
                return ThingCategory.None;
            }
        }

        public static List<ThingCategory> ThingCategoryCandidates()
        {
            if (!Ready)
                return new List<ThingCategory>();
            try
            {
                var set = allThingCategoryProperty.GetValue(null) as HashSet<ThingCategory>;
                return set != null ? set.ToList() : new List<ThingCategory>();
            }
            catch (Exception ex)
            {
                Fail("ThingCategoryCandidates", ex);
                return new List<ThingCategory>();
            }
        }

        /// <summary>MUTATION-C: reproduces DrawCustomFilter's inline Object-mode type delegate body (DialogObjects.cs:325-329).</summary>
        public static void SetThingCategoryFilter(Window dlg, ThingCategory category)
        {
            object search = SearchInstance(dlg);
            if (!Ready || dlg == null || search == null)
                return;
            try
            {
                // MUTATION-C: mirrors DrawCustomFilter's Object-mode type delegate (DialogObjects.cs:325-329).
                searchThingCategoryField.SetValue(search, category);
                object result = tListMethod.Invoke(dlg, null);
                lDefsField.SetValue(dlg, result);
            }
            catch (Exception ex)
            {
                Fail("SetThingCategoryFilter", ex);
            }
        }

        /// <summary>Mirrors FLabel.EnumNameAndAll&lt;T&gt; (FLabel.cs:224-227): "None" reads as the mod's "All" label.</summary>
        public static string ThingCategoryLabel(ThingCategory category)
        {
            if (category == ThingCategory.None)
            {
                string all = AllLabel;
                return all.NullOrEmpty() ? "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString() : all;
            }
            return category.ToString();
        }

        // Object-mode creature pane, present only when the selected def has a race —
        // DrawNavSelectors' own gate.

        /// <summary>Selected.HasRace: the creature pane's presence gate (Selected.cs:57).</summary>
        public static bool SelectedHasRace()
        {
            object s = CurrentSelected();
            if (s == null)
                return false;
            try
            {
                return (bool)selHasRaceProperty.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("SelectedHasRace", ex);
                return false;
            }
        }

        public static Gender SelectedGender()
        {
            object s = CurrentSelected();
            if (s == null)
                return Gender.None;
            try
            {
                return (Gender)selGenderField.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("SelectedGender", ex);
                return Gender.None;
            }
        }

        /// <summary>MUTATION-C: reproduces DrawNavSelectors' own inline gender write (DialogObjects.cs:730-732, `ThingTool.SelectedThing.gender = (Gender)gender;` -- the local scratch int the slider ref-binds is not persisted state, only Selected.gender is).</summary>
        public static void SetSelectedGender(Gender gender)
        {
            object s = CurrentSelected();
            if (s == null)
                return;
            try
            {
                // MUTATION-C: mirrors DrawNavSelectors' inline gender write (DialogObjects.cs:730-732).
                selGenderField.SetValue(s, gender);
            }
            catch (Exception ex)
            {
                Fail("SetSelectedGender", ex);
            }
        }

        public static int SelectedAge()
        {
            object s = CurrentSelected();
            if (s == null)
                return 1;
            try
            {
                return (int)selAgeField.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("SelectedAge", ex);
                return 1;
            }
        }

        /// <summary>MUTATION-C: reproduces DrawNavSelectors' own inline age slider ref-write (DialogObjects.cs:734, `ref ThingTool.SelectedThing.age`), range 1-100 matching the slider's own bounds.</summary>
        public static void SetSelectedAge(int age)
        {
            object s = CurrentSelected();
            if (s == null)
                return;
            try
            {
                // MUTATION-C: mirrors DrawNavSelectors' inline age slider write (DialogObjects.cs:734).
                selAgeField.SetValue(s, Mathf.Clamp(age, 1, 100));
            }
            catch (Exception ex)
            {
                Fail("SetSelectedAge", ex);
            }
        }

        /// <summary>The dialog's own private tempFaction scratch, NOT Selected; applied on Confirm for a placed pawn.</summary>
        public static string DialogFaction(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return dialogTempFactionField.GetValue(dlg) as string;
            }
            catch (Exception ex)
            {
                Fail("DialogFaction", ex);
                return null;
            }
        }

        /// <summary>The picker's faction names: the dialog's lFactions, fixed at construction.</summary>
        public static List<string> DialogFactionCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<string>();
            try
            {
                var set = dialogLFactionsField.GetValue(dlg) as HashSet<string>;
                return set != null ? set.ToList() : new List<string>();
            }
            catch (Exception ex)
            {
                Fail("DialogFactionCandidates", ex);
                return new List<string>();
            }
        }

        /// <summary>MUTATION-C: reproduces the inline NonDefSelectorSimple delegate body (DialogObjects.cs:736-739, `tempFaction = s;`) -- no setter method exists to ride.</summary>
        public static void SetDialogFaction(Window dlg, string faction)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                // MUTATION-C: mirrors the inline NonDefSelectorSimple delegate body (DialogObjects.cs:736-739, `tempFaction = s;`).
                dialogTempFactionField.SetValue(dlg, faction);
            }
            catch (Exception ex)
            {
                Fail("SetDialogFaction", ex);
            }
        }

        // Results (the filtered candidate list) and selection.

        public static List<ThingDef> Results(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<ThingDef>();
            try
            {
                var set = lDefsField.GetValue(dlg) as HashSet<ThingDef>;
                return set != null ? set.ToList() : new List<ThingDef>();
            }
            catch (Exception ex)
            {
                Fail("Results", ex);
                return new List<ThingDef>();
            }
        }

        public static ThingDef Selected(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return selectedDefField.GetValue(dlg) as ThingDef;
            }
            catch (Exception ex)
            {
                Fail("Selected", ex);
                return null;
            }
        }

        /// <summary>MUTATION-C: mirrors the base class's own SZWidgets.ListView ref-bound selection write (DialogTemplate.cs:131) -- no setter method exists to ride. Selecting also triggers the base's own OnSelectionChanged on its NEXT DoWindowContents pass (a diff-check against oldSelectedDef the mod itself runs, not reproduced here), which rebuilds ThingTool.SelectedThing; this facade instead rebuilds it directly and immediately (see <see cref="RebuildSelection"/>) since our own model needs the fresh Selected synchronously, not on the next GUI frame.</summary>
        public static void SetSelected(Window dlg, ThingDef def)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                selectedDefField.SetValue(dlg, def);
            }
            catch (Exception ex)
            {
                Fail("SetSelected", ex);
            }
        }

        /// <summary>
        /// Vehicle A: the dialog's own OnSelectionChanged, which rebuilds ThingTool.SelectedThing,
        /// plays the def's soundInteract, and resolves the turret-def cache. Called directly so the
        /// fresh Selected is available synchronously, not a GUI frame later.
        /// </summary>
        public static void RebuildSelection(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                onSelectionChangedMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("RebuildSelection", ex);
            }
        }

        // The live Selected template (ThingTool.SelectedThing): quality/stuff/style/stack.

        private static object CurrentSelected()
        {
            if (!Ready)
                return null;
            try
            {
                return selectedThingProperty.GetValue(null);
            }
            catch (Exception ex)
            {
                Fail("CurrentSelected", ex);
                return null;
            }
        }

        public static bool HasQuality()
        {
            object s = CurrentSelected();
            if (s == null)
                return false;
            try
            {
                return (bool)selHasQualityProperty.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("HasQuality", ex);
                return false;
            }
        }

        public static QualityCategory Quality()
        {
            object s = CurrentSelected();
            if (s == null)
                return QualityCategory.Normal;
            try
            {
                return (QualityCategory)(int)selQualityField.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("Quality", ex);
                return QualityCategory.Normal;
            }
        }

        public static List<QualityCategory> QualityCandidates()
        {
            if (!Ready)
                return new List<QualityCategory>();
            try
            {
                var set = allQualityCategoryProperty.GetValue(null) as HashSet<QualityCategory>;
                return set != null ? set.ToList() : new List<QualityCategory>();
            }
            catch (Exception ex)
            {
                Fail("QualityCandidates", ex);
                return new List<QualityCategory>();
            }
        }

        /// <summary>MUTATION-C: mirrors SZWidgets.NavSelectorQuality's own inline write (SZWidgets.cs:1590-1613, verified here) -- a raw int field write plus the mod's own UpdateBuyPrice(), no setter method exists to ride.</summary>
        public static void SetQuality(QualityCategory quality)
        {
            object s = CurrentSelected();
            if (s == null)
                return;
            try
            {
                selQualityField.SetValue(s, (int)quality);
                selUpdateBuyPriceMethod.Invoke(s, null);
            }
            catch (Exception ex)
            {
                Fail("SetQuality", ex);
            }
        }

        public static bool MadeFromStuff(ThingDef def) => def != null && def.MadeFromStuff;

        public static ThingDef Stuff()
        {
            object s = CurrentSelected();
            if (s == null)
                return null;
            try
            {
                return selStuffField.GetValue(s) as ThingDef;
            }
            catch (Exception ex)
            {
                Fail("Stuff", ex);
                return null;
            }
        }

        public static List<ThingDef> StuffCandidates()
        {
            object s = CurrentSelected();
            if (s == null)
                return new List<ThingDef>();
            try
            {
                var set = selLOfStuffField.GetValue(s) as HashSet<ThingDef>;
                return set != null ? set.ToList() : new List<ThingDef>();
            }
            catch (Exception ex)
            {
                Fail("StuffCandidates", ex);
                return new List<ThingDef>();
            }
        }

        /// <summary>Vehicle A: Selected.SetStuff, the label-click FloatMenu's delegate (Selected.cs:164-171).</summary>
        public static void SetStuff(ThingDef stuff)
        {
            object s = CurrentSelected();
            if (s == null)
                return;
            try
            {
                selSetStuffDefMethod.Invoke(s, new object[] { stuff });
            }
            catch (Exception ex)
            {
                Fail("SetStuff", ex);
            }
        }

        public static bool CanBeStyled(ThingDef def) => def != null && def.CanBeStyled();

        public static ThingStyleDef Style()
        {
            object s = CurrentSelected();
            if (s == null)
                return null;
            try
            {
                return selStyleField.GetValue(s) as ThingStyleDef;
            }
            catch (Exception ex)
            {
                Fail("Style", ex);
                return null;
            }
        }

        public static List<ThingStyleDef> StyleCandidates()
        {
            object s = CurrentSelected();
            if (s == null)
                return new List<ThingStyleDef>();
            try
            {
                var set = selLOfStyleField.GetValue(s) as HashSet<ThingStyleDef>;
                return set != null ? set.ToList() : new List<ThingStyleDef>();
            }
            catch (Exception ex)
            {
                Fail("StyleCandidates", ex);
                return new List<ThingStyleDef>();
            }
        }

        /// <summary>Vehicle A: Selected.SetStyle(ThingStyleDef) (Selected.cs:139-145).</summary>
        public static void SetStyle(ThingStyleDef style)
        {
            object s = CurrentSelected();
            if (s == null)
                return;
            try
            {
                selSetStyleDefMethod.Invoke(s, new object[] { style });
            }
            catch (Exception ex)
            {
                Fail("SetStyle", ex);
            }
        }

        public static bool HasStack()
        {
            object s = CurrentSelected();
            if (s == null)
                return false;
            try
            {
                return (bool)selHasStackProperty.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("HasStack", ex);
                return false;
            }
        }

        public static int StackVal()
        {
            object s = CurrentSelected();
            if (s == null)
                return 1;
            try
            {
                return (int)selStackValField.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("StackVal", ex);
                return 1;
            }
        }

        /// <summary>MUTATION-C: mirrors DrawNavSelectors' own inline stack write (DialogObjects.cs:743-751, `ref ThingTool.SelectedThing.stackVal`) -- a raw field write, no setter method exists to ride.</summary>
        public static void SetStackVal(int value)
        {
            object s = CurrentSelected();
            if (s == null)
                return;
            try
            {
                selStackValField.SetValue(s, value);
            }
            catch (Exception ex)
            {
                Fail("SetStackVal", ex);
            }
        }

        public static int BuyPrice()
        {
            object s = CurrentSelected();
            if (s == null)
                return 0;
            try
            {
                return (int)selBuyPriceField.GetValue(s);
            }
            catch (Exception ex)
            {
                Fail("BuyPrice", ex);
                return 0;
            }
        }

        /// <summary>Selected.thingDef, for the quality/stuff/style gates.</summary>
        public static ThingDef SelectedThingDef()
        {
            object s = CurrentSelected();
            if (s == null)
                return null;
            try
            {
                return selThingDefField.GetValue(s) as ThingDef;
            }
            catch (Exception ex)
            {
                Fail("SelectedThingDef", ex);
                return null;
            }
        }

        // Def/style/stuff labels: plain formatting, no reflection needed.

        public static string StuffOrStyleLabel<T>(T def) where T : Def
        {
            if (def == null)
                return NoneLabel.NullOrEmpty() ? "None".Translate().ToString() : NoneLabel;
            return def.label.NullOrEmpty() ? def.defName : def.label.CapitalizeFirst();
        }
    }
}
