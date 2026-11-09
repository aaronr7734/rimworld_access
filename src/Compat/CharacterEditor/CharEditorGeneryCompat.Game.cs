using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.DialogGenery</c>, backing <c>GeneryAdapter</c> in
    /// browse mode only: the extended GeneDef editor pane stays unbound, so neither
    /// <c>DrawParameter</c> nor any of the dozens of fields it touches is resolved here.
    /// <c>DialogGenery : DialogTemplate&lt;GeneDef&gt;</c>, the same shared base <c>DialogObjects</c>
    /// extends -- a live <c>search</c> instance whose <c>modName</c> the base's own
    /// <c>ASelectedModName</c> reads and writes, an <c>lDefs</c> filtered-result field, and
    /// <c>selectedDef</c>.
    ///
    /// OPENED by <see cref="RimWorldAccess.CharEditorXenoGenesCompat.OpenAddGeneDialog"/>, never by
    /// this facade; only the resulting window is read and written here.
    ///
    /// FILTERS: mod name (the base class's own row, drawn for every subclass, <c>DrawDropdownModname</c>
    /// -> <c>ASelectedModName</c>) and category (<c>DialogGenery</c>'s OWN <c>DrawCustomFilter</c>
    /// override, a <c>GeneCategoryDef</c> float menu over <c>lCat</c> -- a set built once at
    /// construction, always including a leading <c>null</c> "All" entry -- invoking
    /// <c>ASelectedCategoryDef</c>, which sets both <c>search.ofilter1</c> AND <c>selGeneCategoryDef</c>
    /// then rebuilds <c>lDefs</c> filtered by that category, mirroring <see cref="AddTraitAdapter"/>'s
    /// own category-filter shape). RESULTS are the base class's own <c>lDefs</c> (a live
    /// <c>HashSet&lt;GeneDef&gt;</c>, matching <c>DialogObjects.Results</c>'s exact idiom); SELECTION
    /// rides the base's own <c>selectedDef</c> field, no setter method to ride (MUTATION-C, matching
    /// the base-class ListView's own ref-bound write DialogTemplate.cs:131 documents).
    ///
    /// CONFIRM is the base class's own <c>OnAcceptKeyPressed</c> override
    /// (<c>DialogTemplate&lt;T&gt;.DoAndClose</c> calls <c>OnAccept()</c> only when
    /// <c>selectedDef != null</c>, then unconditionally <c>Close()</c>).
    /// <c>DialogGenery.OnAccept</c> adds <c>selectedDef</c> to <c>CEditor.API.Pawn</c>'s gene set via
    /// <c>Pawn.AddGeneAsFirst(selectedDef, bIsXeno)</c>, and <c>bIsXeno</c> is captured at the
    /// dialog's own construction -- so which gene set OK writes to needs no plumbing here.
    ///
    /// RANDOM PICK: <c>ARandomDef()</c> is private and declared on the shared generic base, but
    /// resolving it against the concrete <c>DialogGenery</c> type works -- private INSTANCE members
    /// declared on a base type ARE returned by <c>Type.GetMethod</c> on a derived type without
    /// <c>DeclaredOnly</c>. Harmony's inherited-method gotcha is about PATCHING, not invocation.
    /// </summary>
    internal static class CharEditorGeneryCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorGeneryCompat", Resolve);

        private static Type dialogType;

        private static FieldInfo searchField;
        private static FieldInfo lModsField;
        private static FieldInfo lDefsField;
        private static FieldInfo selectedDefField;
        private static FieldInfo bIsXenoField;
        private static FieldInfo lCatField;
        private static FieldInfo selGeneCategoryDefField;
        private static MethodInfo aSelectedCategoryDefMethod;
        private static MethodInfo aSelectedModNameMethod;
        private static MethodInfo aRandomDefMethod;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get { gate.Ensure(); return gate.Ensure(); }
        }

        public static Type DialogType
        {
            get { gate.Ensure(); return dialogType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            dialogType = surface.Type("CharacterEditor.DialogGenery");

            searchField = surface.Field(dialogType, "search");
            lModsField = surface.Field(dialogType, "lMods");
            lDefsField = surface.Field(dialogType, "lDefs");
            selectedDefField = surface.Field(dialogType, "selectedDef");
            bIsXenoField = surface.Field(dialogType, "bIsXeno");
            lCatField = surface.Field(dialogType, "lCat");
            selGeneCategoryDefField = surface.Field(dialogType, "selGeneCategoryDef");
            aSelectedCategoryDefMethod = surface.Method(dialogType, "ASelectedCategoryDef", new[] { typeof(GeneCategoryDef) });
            aSelectedModNameMethod = surface.Method(dialogType, "ASelectedModName", new[] { typeof(string) });
            aRandomDefMethod = surface.Method(dialogType, "ARandomDef", Type.EmptyTypes);

            return surface.Ready && CharEditorCompat.Search.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorGeneryCompat." + member + " failed: " + ex.Message);
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

        /// <summary>Which gene set Confirm adds to; captured at the dialog's own construction, never written here.</summary>
        public static bool IsXeno(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                return (bool)bIsXenoField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("IsXeno", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Mod-name filter (DialogTemplate<T>'s own base-class row).
        // ------------------------------------------------------------------

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

        /// <summary>Vehicle A: DialogTemplate&lt;T&gt;'s own ASelectedModName -- sets search.modName AND rebuilds lDefs.</summary>
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

        // ------------------------------------------------------------------
        // Category filter (DialogGenery's own DrawCustomFilter override).
        // ------------------------------------------------------------------

        /// <summary>DialogGenery.lCat -- a HashSet&lt;GeneCategoryDef&gt; built once at construction, always including a leading null ("All").</summary>
        public static List<GeneCategoryDef> CategoryCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<GeneCategoryDef>();
            try
            {
                var set = lCatField.GetValue(dlg) as HashSet<GeneCategoryDef>;
                return set != null ? set.ToList() : new List<GeneCategoryDef>();
            }
            catch (Exception ex)
            {
                Fail("CategoryCandidates", ex);
                return new List<GeneCategoryDef>();
            }
        }

        public static GeneCategoryDef Category(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                // ASelectedCategoryDef writes BOTH search.ofilter1 and selGeneCategoryDef to the
                // same value (DialogGenery.ASelectedCategoryDef); reading the dialog's own field is
                // simpler than re-deriving the shared SearchTool base's ofilter1 slot.
                return selGeneCategoryDefField.GetValue(dlg) as GeneCategoryDef;
            }
            catch (Exception ex)
            {
                Fail("Category", ex);
                return null;
            }
        }

        /// <summary>Vehicle A: DialogGenery.ASelectedCategoryDef -- sets search.ofilter1 AND selGeneCategoryDef, then rebuilds lDefs filtered by category.</summary>
        public static void SetCategory(Window dlg, GeneCategoryDef category)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aSelectedCategoryDefMethod.Invoke(dlg, new object[] { category });
            }
            catch (Exception ex)
            {
                Fail("SetCategory", ex);
            }
        }

        // ------------------------------------------------------------------
        // Results and selection.
        // ------------------------------------------------------------------

        public static List<GeneDef> Results(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<GeneDef>();
            try
            {
                var set = lDefsField.GetValue(dlg) as HashSet<GeneDef>;
                return set != null ? set.ToList() : new List<GeneDef>();
            }
            catch (Exception ex)
            {
                Fail("Results", ex);
                return new List<GeneDef>();
            }
        }

        public static GeneDef Selected(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return selectedDefField.GetValue(dlg) as GeneDef;
            }
            catch (Exception ex)
            {
                Fail("Selected", ex);
                return null;
            }
        }

        /// <summary>MUTATION-C: mirrors the base class's own SZWidgets.ListView ref-bound selection write (DialogTemplate.cs:131) -- no setter method exists to ride, matching ObjectsAdapter's own SetSelected precedent.</summary>
        public static void SetSelected(Window dlg, GeneDef def)
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

        // ------------------------------------------------------------------
        // Random pick.
        // ------------------------------------------------------------------

        /// <summary>Vehicle A: DialogTemplate&lt;T&gt;.ARandomDef() -- a random pick from the currently filtered lDefs.</summary>
        public static void Randomize(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aRandomDefMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("Randomize", ex);
            }
        }
    }
}
