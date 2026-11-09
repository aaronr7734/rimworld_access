using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over Character Editor's browser-style dialogs (<c>DialogAddTrait</c>,
    /// <c>DialogChangeBackstory</c> and siblings), following <see cref="CharEditorCompat"/>:
    /// presence decided once, members resolved behind <see cref="Ready"/>, degrade-not-throw.
    /// Opening invokes a dialog's own constructor and adds it at <c>WindowLayer.Dialog</c>, what
    /// <c>WindowTool.Open</c> does (WindowTool.cs:88-92).
    /// Selection writes are raw field access: the dialogs bind selection into
    /// <c>SZWidgets.ListView</c> by <c>ref</c> and expose no setter. Filter changes must go through
    /// the <c>AChanged*</c>/<c>A*Changed</c> handlers, which also rebuild the filtered result list.
    /// <see cref="ToggleNoBlockingSkills"/> is the exception: that checkbox's diff-detect lives
    /// inline in <c>DoWindowContents</c>, so it reproduces the sequence synchronously.
    /// </summary>
    internal static class CharEditorBrowserCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorBrowserCompat", Resolve);

        private static Type dialogAddTraitType;
        private static Type dialogChangeBackstoryType;
        private static Type dialogAddAbilityType;
        private static Type dialogChangeRaceType;
        private static Type dialogChangeFactionType;
        private static Type dialogFindPawnType;

        private static ConstructorInfo addTraitCtor;
        private static ConstructorInfo changeBackstoryCtor;
        private static ConstructorInfo addAbilityCtor;
        private static ConstructorInfo changeRaceCtor;
        private static ConstructorInfo changeFactionCtor;
        private static ConstructorInfo findPawnCtor;

        // DialogAddTrait members.
        private static FieldInfo addTraitResultsField;
        private static FieldInfo addTraitSelectedField;
        private static FieldInfo addTraitSearchField;
        private static FieldInfo addTraitStatModifiersField;
        private static FieldInfo addTraitCategoriesField;
        private static MethodInfo addTraitChangedModNameMethod;
        private static MethodInfo addTraitChangedStatModifierMethod;
        private static MethodInfo addTraitChangedCategoryMethod;
        private static MethodInfo addTraitRandomMethod;

        // DialogChangeBackstory members.
        private static FieldInfo backstoryResultsField;
        private static FieldInfo backstorySelectedField;
        private static FieldInfo backstoryIsFilteredField;
        private static FieldInfo backstoryIsFilteredOldField;
        private static FieldInfo backstoryCategoryField;
        private static FieldInfo backstoryFilterField;
        private static FieldInfo backstoryFilter2Field;
        private static FieldInfo backstoryCategoriesField;
        private static FieldInfo backstoryFilterDictField;
        private static FieldInfo backstoryFilter2ListField;
        private static MethodInfo backstoryCategoryChangedMethod;
        private static MethodInfo backstoryFilterChangedMethod;
        private static MethodInfo backstoryFilter2ChangedMethod;
        private static MethodInfo backstoryUpdateDictionaryMethod;
        private static MethodInfo backstoryRemoveMethod;
        private static MethodInfo backstoryRandomMethod;

        // DialogAddAbility members.
        private static FieldInfo addAbilityResultsField;
        private static FieldInfo addAbilitySelectedField;
        private static FieldInfo addAbilityModNameField;
        private static FieldInfo addAbilityStufeField;
        private static FieldInfo addAbilityStufeCandidatesField;
        private static MethodInfo addAbilityChangedModNameMethod;
        private static MethodInfo addAbilityChangedStufeMethod;
        private static MethodInfo addAbilityRandomMethod;

        // DialogChangeRace members. Race-specific dress has no field of its own: it lives in the
        // same SearchTool.ofilter1 slot AddTrait's stat-modifier filter uses.
        private static FieldInfo changeRaceRacesField;
        private static FieldInfo changeRaceRaceDefField;
        private static FieldInfo changeRacePkdField;
        private static FieldInfo changeRaceSelectedPkdField;
        private static FieldInfo changeRaceSearchField;
        private static MethodInfo changeRaceChangedRaceMethod;
        private static MethodInfo changeRaceRedressMethod;
        private static MethodInfo changeRaceDoAndCloseMethod;

        // DialogChangeFaction members.
        private static FieldInfo changeFactionFactionsField;
        private static FieldInfo changeFactionSelectedField;
        private static FieldInfo changeFactionPawnField;
        private static MethodInfo changeFactionDoAndCloseMethod;

        // DialogFindPawn members. Its result list is CharEditorCompat.PawnList(), the same live
        // container the dialog reads, so no field binding is needed.
        private static MethodInfo findPawnSelectPawnMethod;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get
            {
                return gate.Ensure();
            }
        }

        public static Type AddTraitDialogType
        {
            get { gate.Ensure(); return dialogAddTraitType; }
        }

        public static Type ChangeBackstoryDialogType
        {
            get { gate.Ensure(); return dialogChangeBackstoryType; }
        }

        public static Type AddAbilityDialogType
        {
            get { gate.Ensure(); return dialogAddAbilityType; }
        }

        public static Type ChangeRaceDialogType
        {
            get { gate.Ensure(); return dialogChangeRaceType; }
        }

        public static Type ChangeFactionDialogType
        {
            get { gate.Ensure(); return dialogChangeFactionType; }
        }

        public static Type FindPawnDialogType
        {
            get { gate.Ensure(); return dialogFindPawnType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            dialogAddTraitType = surface.Type("CharacterEditor.DialogAddTrait");
            dialogChangeBackstoryType = surface.Type("CharacterEditor.DialogChangeBackstory");
            dialogAddAbilityType = surface.Type("CharacterEditor.DialogAddAbility");
            dialogChangeRaceType = surface.Type("CharacterEditor.DialogChangeRace");
            dialogChangeFactionType = surface.Type("CharacterEditor.DialogChangeFaction");
            dialogFindPawnType = surface.Type("CharacterEditor.DialogFindPawn");

            addTraitCtor = surface.Constructor(dialogAddTraitType, new[] { typeof(Trait) });
            addTraitResultsField = surface.Field(dialogAddTraitType, "lOfTraits");
            addTraitSelectedField = surface.Field(dialogAddTraitType, "selectedTrait");
            addTraitSearchField = surface.Field(dialogAddTraitType, "search");
            addTraitStatModifiersField = surface.Field(dialogAddTraitType, "lOfSM");
            addTraitCategoriesField = surface.Field(dialogAddTraitType, "lOfFilters");
            addTraitChangedModNameMethod = surface.Method(dialogAddTraitType, "AChangedModName", new[] { typeof(string) });
            addTraitChangedStatModifierMethod = surface.Method(dialogAddTraitType, "AChangedSM", new[] { typeof(StatModifier) });
            addTraitChangedCategoryMethod = surface.Method(dialogAddTraitType, "AChangedCategory", new[] { typeof(string) });
            addTraitRandomMethod = surface.Method(dialogAddTraitType, "ARandomTrait", Type.EmptyTypes);

            changeBackstoryCtor = surface.Constructor(dialogChangeBackstoryType, new[] { typeof(bool) });
            backstoryResultsField = surface.Field(dialogChangeBackstoryType, "lOfBackstories");
            backstorySelectedField = surface.Field(dialogChangeBackstoryType, "selectedBackstory");
            backstoryIsFilteredField = surface.Field(dialogChangeBackstoryType, "isFiltered");
            backstoryIsFilteredOldField = surface.Field(dialogChangeBackstoryType, "isFilteredOld");
            backstoryCategoryField = surface.Field(dialogChangeBackstoryType, "selectedCategory");
            backstoryFilterField = surface.Field(dialogChangeBackstoryType, "selectedFilter");
            backstoryFilter2Field = surface.Field(dialogChangeBackstoryType, "selectedFilter2");
            backstoryCategoriesField = surface.Field(dialogChangeBackstoryType, "lOfCategories");
            backstoryFilterDictField = surface.Field(dialogChangeBackstoryType, "dicOfFilters");
            backstoryFilter2ListField = surface.Field(dialogChangeBackstoryType, "lOfFilter2");
            backstoryCategoryChangedMethod = surface.Method(dialogChangeBackstoryType, "ACategoryChanged", new[] { typeof(string) });
            backstoryFilterChangedMethod = surface.Method(dialogChangeBackstoryType, "AFilterChanged", new[] { typeof(string) });
            backstoryFilter2ChangedMethod = surface.Method(dialogChangeBackstoryType, "AFilter2Changed", new[] { typeof(string) });
            backstoryUpdateDictionaryMethod = surface.Method(dialogChangeBackstoryType, "UpdateDictionary", Type.EmptyTypes);
            backstoryRemoveMethod = surface.Method(dialogChangeBackstoryType, "DoRemoveAndClose", Type.EmptyTypes);
            backstoryRandomMethod = surface.Method(dialogChangeBackstoryType, "ARandomBackstory", Type.EmptyTypes);

            addAbilityCtor = surface.Constructor(dialogAddAbilityType, Type.EmptyTypes);
            addAbilityResultsField = surface.Field(dialogAddAbilityType, "lOfAbilities");
            addAbilitySelectedField = surface.Field(dialogAddAbilityType, "selectedAbility");
            addAbilityModNameField = surface.Field(dialogAddAbilityType, "selectedModName");
            addAbilityStufeField = surface.Field(dialogAddAbilityType, "selectedStufe");
            addAbilityStufeCandidatesField = surface.Field(dialogAddAbilityType, "lOfStufen");
            addAbilityChangedModNameMethod = surface.Method(dialogAddAbilityType, "ASelectedModName", new[] { typeof(string) });
            addAbilityChangedStufeMethod = surface.Method(dialogAddAbilityType, "ASelectedStufe", new[] { typeof(string) });
            addAbilityRandomMethod = surface.Method(dialogAddAbilityType, "ARandomAbility", Type.EmptyTypes);

            changeRaceCtor = surface.Constructor(dialogChangeRaceType, new[] { typeof(Pawn) });
            changeRaceRacesField = surface.Field(dialogChangeRaceType, "lraces");
            changeRaceRaceDefField = surface.Field(dialogChangeRaceType, "raceDef");
            changeRacePkdField = surface.Field(dialogChangeRaceType, "lpkd");
            changeRaceSelectedPkdField = surface.Field(dialogChangeRaceType, "selectedPKD");
            changeRaceSearchField = surface.Field(dialogChangeRaceType, "search");
            changeRaceChangedRaceMethod = surface.Method(dialogChangeRaceType, "AChangeRace", new[] { typeof(ThingDef) });
            changeRaceRedressMethod = surface.Method(dialogChangeRaceType, "ARedress", new[] { typeof(bool) });
            changeRaceDoAndCloseMethod = surface.Method(dialogChangeRaceType, "DoAndClose", Type.EmptyTypes);

            changeFactionCtor = surface.Constructor(dialogChangeFactionType, Type.EmptyTypes);
            changeFactionFactionsField = surface.Field(dialogChangeFactionType, "lOfFactions");
            changeFactionSelectedField = surface.Field(dialogChangeFactionType, "selectedFaction");
            changeFactionPawnField = surface.Field(dialogChangeFactionType, "pawn");
            changeFactionDoAndCloseMethod = surface.Method(dialogChangeFactionType, "DoAndClose", Type.EmptyTypes);

            findPawnCtor = surface.Constructor(dialogFindPawnType, Type.EmptyTypes);
            findPawnSelectPawnMethod = surface.Method(dialogFindPawnType, "ASelectPawn", new[] { typeof(Pawn) });

            return surface.Ready && CharEditorCompat.Search.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorBrowserCompat." + member + " failed: " + ex.Message);
        }

        // Opening.

        /// <summary>Opens DialogAddTrait, pre-loaded for editing when <paramref name="editing"/> is non-null (DialogAddTrait.cs:34-62).</summary>
        public static void OpenAddTrait(Trait editing)
        {
            if (!Ready)
                return;
            try
            {
                var window = (Window)addTraitCtor.Invoke(new object[] { editing });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail("OpenAddTrait", ex);
            }
        }

        /// <summary>Opens DialogChangeBackstory for one slot (DialogChangeBackstory.cs:94-150). Its constructor posts a debug message with the raw defName; a mod quirk sighted players get too.</summary>
        public static void OpenChangeBackstory(bool isChildhood)
        {
            if (!Ready)
                return;
            try
            {
                var window = (Window)changeBackstoryCtor.Invoke(new object[] { isChildhood });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail("OpenChangeBackstory", ex);
            }
        }

        // DialogAddTrait.

        public static List<KeyValuePair<TraitDef, TraitDegreeData>> AddTraitResults(Window dlg)
        {
            return FieldOrDefault(addTraitResultsField, dlg, new List<KeyValuePair<TraitDef, TraitDegreeData>>());
        }

        public static KeyValuePair<TraitDef, TraitDegreeData> AddTraitSelected(Window dlg)
        {
            if (!Ready || dlg == null)
                return default;
            try
            {
                return (KeyValuePair<TraitDef, TraitDegreeData>)addTraitSelectedField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("AddTraitSelected", ex);
                return default;
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors DialogAddTrait's own selection assignment -- SZWidgets.ListView
        /// binds the dialog's private selection field by ref and writes it on row click
        /// (DialogAddTrait.cs, ListView call site); no setter method exists to ride.
        /// </summary>
        public static void AddTraitSetSelected(Window dlg, KeyValuePair<TraitDef, TraitDegreeData> value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                addTraitSelectedField.SetValue(dlg, value);
            }
            catch (Exception ex)
            {
                Fail("AddTraitSetSelected", ex);
            }
        }

        public static string AddTraitModName(Window dlg)
        {
            return CharEditorCompat.Search.ModName(SearchInstance(dlg));
        }

        public static StatModifier AddTraitStatModifier(Window dlg)
        {
            return CharEditorCompat.Search.OFilter1(SearchInstance(dlg)) as StatModifier;
        }

        public static string AddTraitCategory(Window dlg)
        {
            return CharEditorCompat.Search.Filter1(SearchInstance(dlg));
        }

        private static object SearchInstance(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return addTraitSearchField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("AddTraitSearch", ex);
                return null;
            }
        }

        /// <summary>The stat-modifier candidates fixed at dialog-open time (DialogAddTrait.cs:40); the mod does not recompute them on mod-name change, and neither does this.</summary>
        public static List<StatModifier> AddTraitStatModifierCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<StatModifier>();
            try
            {
                var set = addTraitStatModifiersField.GetValue(dlg) as HashSet<StatModifier>;
                return set != null ? set.ToList() : new List<StatModifier>();
            }
            catch (Exception ex)
            {
                Fail("AddTraitStatModifierCandidates", ex);
                return new List<StatModifier>();
            }
        }

        public static List<string> AddTraitCategoryCandidates(Window dlg)
        {
            return FieldOrDefault(addTraitCategoriesField, dlg, new List<string>());
        }

        public static void AddTraitSetModName(Window dlg, string value)
        {
            InvokeOn(dlg, addTraitChangedModNameMethod, new object[] { value }, "AddTraitSetModName");
        }

        public static void AddTraitSetStatModifier(Window dlg, StatModifier value)
        {
            InvokeOn(dlg, addTraitChangedStatModifierMethod, new object[] { value }, "AddTraitSetStatModifier");
        }

        public static void AddTraitSetCategory(Window dlg, string value)
        {
            InvokeOn(dlg, addTraitChangedCategoryMethod, new object[] { value }, "AddTraitSetCategory");
        }

        /// <summary>The dialog's own random pick from the filtered list (DialogAddTrait.cs:138-142); not BlockBio's per-row random.</summary>
        public static void AddTraitRandomize(Window dlg)
        {
            InvokeOn(dlg, addTraitRandomMethod, null, "AddTraitRandomize");
        }

        // DialogChangeBackstory.

        public static List<BackstoryDef> BackstoryResults(Window dlg)
        {
            return FieldOrDefault(backstoryResultsField, dlg, new List<BackstoryDef>());
        }

        public static BackstoryDef BackstorySelected(Window dlg)
        {
            return FieldOrDefault(backstorySelectedField, dlg, (BackstoryDef)null);
        }

        /// <summary>
        /// MUTATION-C: mirrors DialogChangeBackstory's own selection assignment -- SZWidgets.ListView
        /// binds the private selection field by ref and writes it on row click; no setter method
        /// exists to ride.
        /// </summary>
        public static void BackstorySetSelected(Window dlg, BackstoryDef value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                backstorySelectedField.SetValue(dlg, value);
            }
            catch (Exception ex)
            {
                Fail("BackstorySetSelected", ex);
            }
        }

        public static bool BackstoryNoBlockingSkills(Window dlg)
        {
            return FieldOrDefault(backstoryIsFilteredField, dlg, false);
        }

        /// <summary>
        /// MUTATION-C: mirrors DialogChangeBackstory.DoWindowContents' own inline checkbox
        /// handling -- vanilla Widgets.Checkbox writes the field by ref, then the dialog's
        /// diff-check against its Old twin triggers UpdateDictionary; both fields and the
        /// refresh call are reproduced here in one pass because no toggle method exists to ride.
        /// </summary>
        public static void ToggleNoBlockingSkills(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                bool next = !(bool)backstoryIsFilteredField.GetValue(dlg);
                backstoryIsFilteredField.SetValue(dlg, next);
                backstoryIsFilteredOldField.SetValue(dlg, next);
                backstoryUpdateDictionaryMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("ToggleNoBlockingSkills", ex);
            }
        }

        public static string BackstoryCategory(Window dlg)
        {
            return FieldOrDefault(backstoryCategoryField, dlg, (string)null);
        }

        public static List<string> BackstoryCategoryCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<string>();
            try
            {
                var set = backstoryCategoriesField.GetValue(dlg) as HashSet<string>;
                return set != null ? set.ToList() : new List<string>();
            }
            catch (Exception ex)
            {
                Fail("BackstoryCategoryCandidates", ex);
                return new List<string>();
            }
        }

        public static void BackstorySetCategory(Window dlg, string value)
        {
            InvokeOn(dlg, backstoryCategoryChangedMethod, new object[] { value }, "BackstorySetCategory");
        }

        /// <summary>The skill-gain-threshold filter's current key ("All", or "&lt;Skill&gt; +/++/+++"); the value half is looked up from <see cref="BackstoryFilterKeyCandidates"/>'s dictionary by the dialog itself.</summary>
        public static string BackstoryFilterKey(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                var pair = (KeyValuePair<string, SkillDef>)backstoryFilterField.GetValue(dlg);
                return pair.Key;
            }
            catch (Exception ex)
            {
                Fail("BackstoryFilterKey", ex);
                return null;
            }
        }

        public static List<string> BackstoryFilterKeyCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<string>();
            try
            {
                var dict = backstoryFilterDictField.GetValue(dlg) as System.Collections.IDictionary;
                var result = new List<string>();
                if (dict != null)
                {
                    foreach (object key in dict.Keys)
                        result.Add((string)key);
                }
                return result;
            }
            catch (Exception ex)
            {
                Fail("BackstoryFilterKeyCandidates", ex);
                return new List<string>();
            }
        }

        public static void BackstorySetFilterKey(Window dlg, string key)
        {
            InvokeOn(dlg, backstoryFilterChangedMethod, new object[] { key }, "BackstorySetFilterKey");
        }

        public static string BackstorySumFilter(Window dlg)
        {
            return FieldOrDefault(backstoryFilter2Field, dlg, (string)null);
        }

        public static List<string> BackstorySumFilterCandidates(Window dlg)
        {
            return FieldOrDefault(backstoryFilter2ListField, dlg, new List<string>());
        }

        public static void BackstorySetSumFilter(Window dlg, string value)
        {
            InvokeOn(dlg, backstoryFilter2ChangedMethod, new object[] { value }, "BackstorySetSumFilter");
        }

        /// <summary>The under-3-years gate DoWindowContents checks before drawing the Remove button (DialogChangeBackstory.cs:300).</summary>
        public static bool BackstoryCanRemove(Pawn pawn, bool isChildhood)
        {
            if (pawn?.ageTracker == null)
                return !isChildhood;
            return (isChildhood && pawn.ageTracker.AgeBiologicalYears < 3) || !isChildhood;
        }

        public static void BackstoryRemove(Window dlg)
        {
            InvokeOn(dlg, backstoryRemoveMethod, null, "BackstoryRemove");
        }

        /// <summary>The dialog's own "random pick from the current filtered list" dice (DialogChangeBackstory.cs:351-355).</summary>
        public static void BackstoryRandomize(Window dlg)
        {
            InvokeOn(dlg, backstoryRandomMethod, null, "BackstoryRandomize");
        }

        // DialogAddAbility.

        /// <summary>Opens DialogAddAbility. Single no-arg constructor: there is no edit-existing overload.</summary>
        public static void OpenAddAbility()
        {
            if (!Ready)
                return;
            try
            {
                var window = (Window)addAbilityCtor.Invoke(null);
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail("OpenAddAbility", ex);
            }
        }

        public static List<AbilityDef> AddAbilityResults(Window dlg)
        {
            return FieldOrDefault(addAbilityResultsField, dlg, new List<AbilityDef>());
        }

        public static AbilityDef AddAbilitySelected(Window dlg)
        {
            return FieldOrDefault(addAbilitySelectedField, dlg, (AbilityDef)null);
        }

        /// <summary>MUTATION-C: mirrors DialogAddAbility's own SZWidgets.ListView ref-bound selection write -- no setter method exists to ride.</summary>
        public static void AddAbilitySetSelected(Window dlg, AbilityDef value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                addAbilitySelectedField.SetValue(dlg, value);
            }
            catch (Exception ex)
            {
                Fail("AddAbilitySetSelected", ex);
            }
        }

        public static string AddAbilityModName(Window dlg)
        {
            return FieldOrDefault(addAbilityModNameField, dlg, (string)null);
        }

        public static void AddAbilitySetModName(Window dlg, string value)
        {
            InvokeOn(dlg, addAbilityChangedModNameMethod, new object[] { value }, "AddAbilitySetModName");
        }

        /// <summary>The level-filter's current key: "All" or a "Level N" string.</summary>
        public static string AddAbilityLevelFilter(Window dlg)
        {
            return FieldOrDefault(addAbilityStufeField, dlg, (string)null);
        }

        /// <summary>The level-filter candidates fixed at dialog-open time ("All" then "Level 0".."Level 6").</summary>
        public static List<string> AddAbilityLevelCandidates(Window dlg)
        {
            return FieldOrDefault(addAbilityStufeCandidatesField, dlg, new List<string>());
        }

        public static void AddAbilitySetLevelFilter(Window dlg, string value)
        {
            InvokeOn(dlg, addAbilityChangedStufeMethod, new object[] { value }, "AddAbilitySetLevelFilter");
        }

        /// <summary>The dialog's own "random pick from the current filtered list" dice (DialogAddAbility.cs:156-160).</summary>
        public static void AddAbilityRandomize(Window dlg)
        {
            InvokeOn(dlg, addAbilityRandomMethod, null, "AddAbilityRandomize");
        }

        // DialogChangeRace.

        /// <summary>Opens DialogChangeRace(pawn); the mod exposes no other opener for it.</summary>
        public static void OpenChangeRace(Pawn pawn)
        {
            if (!Ready || pawn == null)
                return;
            try
            {
                var window = (Window)changeRaceCtor.Invoke(new object[] { pawn });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail("OpenChangeRace", ex);
            }
        }

        public static List<ThingDef> ChangeRaceCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<ThingDef>();
            try
            {
                var set = changeRaceRacesField.GetValue(dlg) as HashSet<ThingDef>;
                return set != null ? set.ToList() : new List<ThingDef>();
            }
            catch (Exception ex)
            {
                Fail("ChangeRaceCandidates", ex);
                return new List<ThingDef>();
            }
        }

        public static ThingDef ChangeRaceSelected(Window dlg)
        {
            return FieldOrDefault(changeRaceRaceDefField, dlg, (ThingDef)null);
        }

        /// <summary>Vehicle A: AChangeRace also rebuilds the pawn-kind list for the new race.</summary>
        public static void ChangeRaceSetRace(Window dlg, ThingDef race)
        {
            InvokeOn(dlg, changeRaceChangedRaceMethod, new object[] { race }, "ChangeRaceSetRace");
        }

        public static List<PawnKindDef> ChangeRaceKindCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<PawnKindDef>();
            try
            {
                var set = changeRacePkdField.GetValue(dlg) as HashSet<PawnKindDef>;
                return set != null ? set.ToList() : new List<PawnKindDef>();
            }
            catch (Exception ex)
            {
                Fail("ChangeRaceKindCandidates", ex);
                return new List<PawnKindDef>();
            }
        }

        public static PawnKindDef ChangeRaceSelectedKind(Window dlg)
        {
            return FieldOrDefault(changeRaceSelectedPkdField, dlg, (PawnKindDef)null);
        }

        /// <summary>MUTATION-C: mirrors the dialog's own SZWidgets.ListView ref-bound kind selection write -- no setter method exists to ride.</summary>
        public static void ChangeRaceSetSelectedKind(Window dlg, PawnKindDef kind)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                changeRaceSelectedPkdField.SetValue(dlg, kind);
            }
            catch (Exception ex)
            {
                Fail("ChangeRaceSetSelectedKind", ex);
            }
        }

        /// <summary>The race-specific-dress checkbox, backed by this dialog's own SearchTool.ofilter1
        /// slot. Absent or unreadable reads as checked, the mod's default.</summary>
        public static bool ChangeRaceRaceSpecificDress(Window dlg)
        {
            object v = CharEditorCompat.Search.OFilter1(ChangeRaceSearchInstance(dlg));
            return !(v is bool b) || b;
        }

        /// <summary>Toggles race-specific dress through the dialog's own ARedress(bool) handler.</summary>
        public static void ChangeRaceSetRaceSpecificDress(Window dlg, bool value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                changeRaceRedressMethod.Invoke(dlg, new object[] { value });
            }
            catch (Exception ex)
            {
                Fail("ChangeRaceSetRaceSpecificDress", ex);
            }
        }

        private static object ChangeRaceSearchInstance(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return changeRaceSearchField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("ChangeRaceSearchInstance", ex);
                return null;
            }
        }

        /// <summary>Invokes the dialog's own DoAndClose directly: it has no OnAcceptKeyPressed
        /// override to route through.</summary>
        public static bool ChangeRaceConfirm(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                changeRaceDoAndCloseMethod.Invoke(dlg, null);
                return !Find.WindowStack.IsOpen(dlg);
            }
            catch (Exception ex)
            {
                Fail("ChangeRaceConfirm", ex);
                return false;
            }
        }

        // DialogChangeFaction.

        public static void OpenChangeFaction()
        {
            if (!Ready)
                return;
            try
            {
                var window = (Window)changeFactionCtor.Invoke(null);
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail("OpenChangeFaction", ex);
            }
        }

        /// <summary>All factions plus a leading null ("None"), in the dialog's defName-descending order, not alphabetical by label.</summary>
        public static List<Faction> ChangeFactionResults(Window dlg)
        {
            return FieldOrDefault(changeFactionFactionsField, dlg, new List<Faction>());
        }

        public static Faction ChangeFactionSelected(Window dlg)
        {
            return FieldOrDefault(changeFactionSelectedField, dlg, (Faction)null);
        }

        /// <summary>MUTATION-C: mirrors the dialog's own inline RadioButton click handler (`selectedFaction = lOfFaction;`) -- no setter method exists to ride.</summary>
        public static void ChangeFactionSetSelected(Window dlg, Faction faction)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                changeFactionSelectedField.SetValue(dlg, faction);
            }
            catch (Exception ex)
            {
                Fail("ChangeFactionSetSelected", ex);
            }
        }

        /// <summary>The pawn the dialog captured at construction, not the live edited pawn.</summary>
        public static Pawn ChangeFactionPawn(Window dlg)
        {
            return FieldOrDefault(changeFactionPawnField, dlg, (Pawn)null);
        }

        /// <summary>Invokes the dialog's own DoAndClose; it has no OnAcceptKeyPressed override either.</summary>
        public static bool ChangeFactionConfirm(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                changeFactionDoAndCloseMethod.Invoke(dlg, null);
                return !Find.WindowStack.IsOpen(dlg);
            }
            catch (Exception ex)
            {
                Fail("ChangeFactionConfirm", ex);
                return false;
            }
        }

        // DialogFindPawn.

        public static void OpenFindPawn()
        {
            if (!Ready)
                return;
            try
            {
                var window = (Window)findPawnCtor.Invoke(null);
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail("OpenFindPawn", ex);
            }
        }

        /// <summary>The dialog's own ASelectPawn, which switches the edited pawn IMMEDIATELY on
        /// selection, not deferred like the other browsers' SelectResult.</summary>
        public static void FindPawnSelect(Window dlg, Pawn pawn)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                findPawnSelectPawnMethod.Invoke(dlg, new object[] { pawn });
            }
            catch (Exception ex)
            {
                Fail("FindPawnSelect", ex);
            }
        }

        // Shared helpers.

        private static T FieldOrDefault<T>(FieldInfo field, Window dlg, T fallback)
        {
            if (!Ready || dlg == null)
                return fallback;
            try
            {
                object value = field.GetValue(dlg);
                return value is T typed ? typed : fallback;
            }
            catch (Exception ex)
            {
                Fail(field?.Name ?? "field", ex);
                return fallback;
            }
        }

        private static void InvokeOn(Window dlg, MethodInfo method, object[] args, string caller)
        {
            if (!Ready || dlg == null || method == null)
                return;
            try
            {
                method.Invoke(dlg, args);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }
    }
}
