using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for the Character Editor mod (packageId
    /// <c>void.charactereditor</c>): <c>CharacterEditor.CEditor</c>, its nested window
    /// <c>EditorUI</c>, and the two editor blocks this file drives. Presence is decided by
    /// <see cref="ModPresent"/>, a ModLister packageId lookup and never a type probe; every member
    /// resolves once behind <see cref="Ready"/>; a resolved type missing a member costs one logged
    /// warning and a graceful decline. Nothing here throws — a partially bound facade degrades
    /// feature by feature, and <see cref="Shell.CharacterEditorScope"/> hides the unbound rows.
    /// <see cref="Ready"/> covers ONLY this file's members; every other block class binds through
    /// its own partial and its own Ready flag, so one renamed member disables one surface.
    ///
    /// Every write below invokes the exact delegate the mod's own button passes to
    /// <c>SZWidgets</c> (vehicle A), so no raw field write exists here and no MUTATION-C marker is
    /// needed. <see cref="SelectPawn"/> assigns <c>CEditor.Pawn</c>, the mod's own list-row click
    /// path; that setter re-runs <c>Find.Selector</c> and <c>UpdateGraphics()</c> on EVERY
    /// assignment, even an unchanged one, so this facade never writes a pawn back over itself.
    ///
    /// The mod runs two translation systems: vanilla <c>"Key".Translate()</c> for most strings
    /// and its own eight-language <c>CharacterEditor.Label</c> table for concepts vanilla has no
    /// key for. Read whichever the mod itself uses for a given string.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        /// <summary>Steam/local packageId of the Character Editor mod, lowercase as ModsConfig stores it.</summary>
        private const string PackageId = "void.charactereditor";

        /// <summary>Nested-type lookup: every type this facade needs is a PRIVATE nested class.</summary>
        private const BindingFlags NestedFlags = BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Method lookup for the generic accessors, which are private instance members.</summary>
        private const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorCompat", Resolve);
        private static bool modPresent;

        private static Type ceditorType;
        private static Type editorUIType;
        private static Type blockPawnListType;
        private static Type blockPersonType;
        private static Type tabTypeType;
        private static Type eTypeType;

        private static MethodInfo pawnGetter;
        private static MethodInfo pawnSetter;
        private static MethodInfo isRandomGetter;
        private static MethodInfo inStartingScreenGetter;
        private static MethodInfo onMapGetter;
        private static MethodInfo listNameGetter;
        private static MethodInfo dicFactionsGetter;
        private static MethodInfo listOfPawnsMethod;

        private static MethodInfo getBlockPawnList;
        private static MethodInfo getBlockPerson;

        private static MethodInfo changeListMethod;
        private static MethodInfo changedOnMapMethod;
        private static MethodInfo choosePrevPawnMethod;
        private static MethodInfo chooseNextPawnMethod;
        private static MethodInfo toggleCreationModeMethod;
        private static MethodInfo tabSwitchMethod;
        private static FieldInfo labelInfoField;
        private static FieldInfo labelColonistsField;

        /// <summary>Boxed <c>EType.Pawns</c>, the container key the mod's own pawn list lives behind.</summary>
        private static object eTypePawns;

        // ------------------------------------------------------------------
        // Presence, readiness, and the one-shot bind.
        // ------------------------------------------------------------------

        /// <summary>True when the Character Editor mod is in the active mod list.</summary>
        public static bool ModPresent
        {
            get
            {
                gate.Ensure();
                return modPresent;
            }
        }

        /// <summary>True when every member this core file needs resolved against the loaded assembly.</summary>
        public static bool Ready
        {
            get
            {
                return gate.Ensure();
            }
        }

        /// <summary>The private nested <c>EditorUI</c> window type, for the ScopeForWindow registration.</summary>
        public static Type EditorUIType
        {
            get
            {
                gate.Ensure();
                return editorUIType;
            }
        }

        /// <summary>
        /// Resolves and caches every member. Idempotent and lazy: the first property touch binds,
        /// later touches are a bool test. Deliberately not a static constructor — binding must
        /// happen after the mod list is populated, and the caller decides when that is.
        /// </summary>
        private static bool Resolve(ReflectionSurface surface)
        {
            // ignorePostfix trims the "_steam" suffix a Workshop install carries.
            modPresent = ModLister.GetActiveModWithIdentifier(PackageId, ignorePostfix: true) != null;
            if (!modPresent)
                return false;

            ceditorType = EditorCore.CEditorType;
            editorUIType = ceditorType != null ? ceditorType.GetNestedType("EditorUI", NestedFlags) : null;
            blockPawnListType = editorUIType != null ? editorUIType.GetNestedType("BlockPawnList", NestedFlags) : null;
            blockPersonType = editorUIType != null ? editorUIType.GetNestedType("BlockPerson", NestedFlags) : null;
            tabTypeType = editorUIType != null ? editorUIType.GetNestedType("TabType", NestedFlags) : null;
            eTypeType = AccessTools.TypeByName("CharacterEditor.EType");

            // Every per-tab binder runs BEFORE the core-types early return below, so a member
            // missing from this file's own set does not take down the independently-gated tab
            // surfaces. Each binder gates its own members behind its own Ready flag. PlacingTool
            // and RecordTool are free-standing statics, bound here only so one install resolves
            // every surface in one pass.
            if (editorUIType != null && tabTypeType != null)
            {
                characterTabGate.Ensure();
                characterExtendedGate.Ensure();
                appearanceGate.Ensure();
                actionsGate.Ensure();
                healthGate.Ensure();
                needsGate.Ensure();
                socialGate.Ensure();
                inventoryGate.Ensure();
                placingGate.Ensure();
                recordsGate.Ensure();
            }

            if (ceditorType == null || editorUIType == null || blockPawnListType == null
                || blockPersonType == null || tabTypeType == null || eTypeType == null || Labels.LabelType == null)
            {
                ModLogger.Warning("CharEditorCompat: Character Editor is installed but its core types "
                    + "(CEditor / EditorUI / BlockPawnList / BlockPerson / TabType / EType / Label) "
                    + "could not be resolved; the bespoke editor screen is disabled.");
                return false;
            }

            // Every core type is non-null here, so the surface anchors on one rather than
            // re-resolving any by name.
            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            PropertyInfo pawnProperty = surface.Property(ceditorType, "Pawn");
            pawnGetter = pawnProperty?.GetGetMethod(true);
            pawnSetter = pawnProperty?.GetSetMethod(true);
            isRandomGetter = surface.Property(ceditorType, "IsRandom")?.GetGetMethod(true);
            inStartingScreenGetter = surface.Property(ceditorType, "InStartingScreen")?.GetGetMethod(true);
            onMapGetter = surface.Property(ceditorType, "OnMap")?.GetGetMethod(true);
            listNameGetter = surface.Property(ceditorType, "ListName")?.GetGetMethod(true);
            dicFactionsGetter = surface.Property(ceditorType, "DicFactions")?.GetGetMethod(true);

            changeListMethod = surface.Method(blockPawnListType, "ChangeList", new[] { typeof(string) });
            changedOnMapMethod = surface.Method(blockPawnListType, "AChangedOnMap", Type.EmptyTypes);
            choosePrevPawnMethod = surface.Method(blockPersonType, "AChoosePrevPawn", Type.EmptyTypes);
            chooseNextPawnMethod = surface.Method(blockPersonType, "AChooseNextPawn", Type.EmptyTypes);
            toggleCreationModeMethod = surface.Method(blockPersonType, "AToggleR", Type.EmptyTypes);
            tabSwitchMethod = surface.Method(editorUIType, "ATabwechsel", new[] { tabTypeType });
            labelInfoField = surface.Field(Labels.LabelType, "INFO");
            // BlockPawnList.IsPlayerFaction compares ListName against this same field, and the
            // starting-count stepper and reserve-pawn tag need the identical comparison outside it.
            labelColonistsField = surface.Field(Labels.LabelType, "COLONISTS");

            // The two generic accessors are closed over the concrete types read here so no generic
            // work happens per call; hand-closed and boxed, so neither is resolvable by the
            // surface's own lookups.
            listOfPawnsMethod = surface.Required("CEditor.ListOf<Pawn>(EType) closed",
                CloseGeneric(ceditorType, "ListOf", typeof(Pawn)));
            getBlockPawnList = surface.Required("EditorUI.Get<BlockPawnList>(TabType) closed",
                CloseGeneric(editorUIType, "Get", blockPawnListType));
            getBlockPerson = surface.Required("EditorUI.Get<BlockPerson>(TabType) closed",
                CloseGeneric(editorUIType, "Get", blockPersonType));
            eTypePawns = surface.Required("EType.Pawns boxed", EnumValue(eTypeType, "Pawns"));

            return surface.Ready && EditorCore.Ready && Labels.Ready;
        }

        /// <summary>Members already reported as throwing, so a per-frame reader logs once, not every pass.</summary>
        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        /// <summary>
        /// Reports a reflection call that threw, once per member per session. Several readers run
        /// from the screen's per-pass model refresh, so an unguarded log would repeat every frame
        /// and bury whatever else went wrong.
        /// </summary>
        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorCompat." + member + " failed: " + ex.Message);
        }

        /// <summary>Finds the single generic method <paramref name="name"/> and closes it over one argument.</summary>
        internal static MethodInfo CloseGeneric(Type declaring, string name, Type argument)
        {
            if (declaring == null || argument == null)
                return null;
            foreach (MethodInfo m in declaring.GetMethods(MemberFlags))
            {
                if (m.Name == name && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
                {
                    return m.MakeGenericMethod(argument);
                }
            }
            return null;
        }

        internal static object EnumValue(Type enumType, string name)
        {
            if (enumType == null || !enumType.IsEnum || !Enum.IsDefined(enumType, name))
                return null;
            return Enum.Parse(enumType, name);
        }

        /// <summary>The mod's singleton, or null when it has not bootstrapped yet.</summary>
        private static object Api()
        {
            return EditorCore.Api();
        }

        private static object Block(Window editorUI, MethodInfo accessor, string tabName)
        {
            if (editorUI == null || accessor == null || !editorUIType.IsInstanceOfType(editorUI))
                return null;
            object tab = EnumValue(tabTypeType, tabName);
            return tab != null ? accessor.Invoke(editorUI, new[] { tab }) : null;
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        /// <summary>The pawn currently loaded in the editor, or null when it holds none.</summary>
        public static Pawn CurrentPawn
        {
            get
            {
                if (!Ready)
                    return null;
                try
                {
                    object api = Api();
                    return api != null ? pawnGetter.Invoke(api, null) as Pawn : null;
                }
                catch (Exception ex)
                {
                    Fail("CurrentPawn", ex);
                    return null;
                }
            }
        }

        /// <summary>Creation mode ("random" mode) -- the dice toggle that gates the mod's add/clone/randomize toolbar.</summary>
        public static bool CreationMode
        {
            get
            {
                if (!Ready)
                    return false;
                try
                {
                    return (bool)isRandomGetter.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Fail("CreationMode", ex);
                    return false;
                }
            }
        }

        /// <summary>True in world generation (and the mod's "game plus" restart flow); false during an active game.</summary>
        public static bool InStartingScreen
        {
            get
            {
                if (!Ready)
                    return false;
                try
                {
                    return (bool)inStartingScreenGetter.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Fail("InStartingScreen", ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// The "only pawns on this map" filter. The mod's own getter forces false while
        /// <see cref="InStartingScreen"/>, so it is meaningful in an active game only.
        /// </summary>
        public static bool OnMapFilter
        {
            get
            {
                if (!Ready)
                    return false;
                try
                {
                    return (bool)onMapGetter.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Fail("OnMapFilter", ex);
                    return false;
                }
            }
        }

        /// <summary>The name of the pawn list currently shown, as the mod's own selector button displays it.</summary>
        public static string ListSourceName
        {
            get
            {
                if (!Ready)
                    return "";
                try
                {
                    return listNameGetter.Invoke(null, null) as string ?? "";
                }
                catch (Exception ex)
                {
                    Fail("ListSourceName", ex);
                    return "";
                }
            }
        }

        /// <summary>
        /// Mirrors <c>BlockPawnList.IsPlayerFaction</c> exactly (<c>ListName == Label.COLONISTS</c>):
        /// the starting-count stepper and the reserve tag are meaningful only for the player's own
        /// "Colonists" list source, matching the mod's own count-row and reserve-render gates.
        /// </summary>
        public static bool IsPlayerFactionList
        {
            get
            {
                if (!Ready)
                    return false;
                try
                {
                    string colonists = labelColonistsField.GetValue(null) as string ?? "";
                    return ListSourceName == colonists;
                }
                catch (Exception ex)
                {
                    Fail("IsPlayerFactionList", ex);
                    return false;
                }
            }
        }

        /// <summary>Every selectable list source in the mod's own order: the keys of the faction dictionary its float menu is built from.</summary>
        public static List<string> ListSources()
        {
            var result = new List<string>();
            if (!Ready)
                return result;
            try
            {
                object api = Api();
                if (api == null)
                    return result;
                var dict = dicFactionsGetter.Invoke(api, null) as IDictionary;
                if (dict == null)
                    return result;
                foreach (object key in dict.Keys)
                {
                    if (key is string s)
                        result.Add(s);
                }
            }
            catch (Exception ex)
            {
                Fail("ListSources", ex);
            }
            return result;
        }

        /// <summary>
        /// The pawns in the editor's current list, live. The same shared container the list rows and
        /// portrait arrows walk, so a picker built from it steps the identical index space.
        /// </summary>
        public static List<Pawn> PawnList()
        {
            if (!Ready)
                return new List<Pawn>();
            try
            {
                object api = Api();
                if (api == null)
                    return new List<Pawn>();
                return listOfPawnsMethod.Invoke(api, new[] { eTypePawns }) as List<Pawn> ?? new List<Pawn>();
            }
            catch (Exception ex)
            {
                Fail("PawnList", ex);
                return new List<Pawn>();
            }
        }

        /// <summary>The mod's own label for its Info tab (its <c>Label</c> table, not a vanilla key).</summary>
        public static string InfoTabLabel
        {
            get
            {
                if (!Ready)
                    return "";
                try
                {
                    return labelInfoField.GetValue(null) as string ?? "";
                }
                catch (Exception ex)
                {
                    Fail("InfoTabLabel", ex);
                    return "";
                }
            }
        }

        // ------------------------------------------------------------------
        // Mutators -- each rides the mod's own handler (vehicle A; see class remarks).
        // ------------------------------------------------------------------

        /// <summary>
        /// Loads a pawn through the mod's own selection path. No-op when the pawn is already the
        /// edited one: the setter re-runs map selection and rebuilds the portrait unconditionally,
        /// so writing a pawn over itself is real work with a visible side effect.
        /// </summary>
        public static void SelectPawn(Pawn pawn)
        {
            if (!Ready)
                return;
            try
            {
                object api = Api();
                if (api == null)
                    return;
                var current = pawnGetter.Invoke(api, null) as Pawn;
                if (ReferenceEquals(current, pawn))
                    return;
                pawnSetter.Invoke(api, new object[] { pawn });
            }
            catch (Exception ex)
            {
                Fail("SelectPawn", ex);
            }
        }

        /// <summary>Steps back one pawn through the portrait arrow's own handler.</summary>
        public static void PreviousPawn(Window editorUI)
        {
            InvokeOnBlockPerson(editorUI, choosePrevPawnMethod, "PreviousPawn");
        }

        /// <summary>Steps forward one pawn through the portrait arrow's own handler.</summary>
        public static void NextPawn(Window editorUI)
        {
            InvokeOnBlockPerson(editorUI, chooseNextPawnMethod, "NextPawn");
        }

        /// <summary>Flips creation mode through the dice toggle's own handler.</summary>
        public static void ToggleCreationMode(Window editorUI)
        {
            InvokeOnBlockPerson(editorUI, toggleCreationModeMethod, "ToggleCreationMode");
        }

        private static void InvokeOnBlockPerson(Window editorUI, MethodInfo method, string caller)
        {
            if (!Ready || method == null)
                return;
            try
            {
                object block = Block(editorUI, getBlockPerson, "BlockPerson");
                if (block != null)
                    method.Invoke(block, null);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        /// <summary>
        /// Switches the pawn list through the selector float menu's own callback, which repopulates
        /// the faction dictionary and pawn container and auto-selects the first pawn when none is
        /// loaded.
        /// </summary>
        public static void ChangeListSource(Window editorUI, string sourceName)
        {
            if (!Ready || string.IsNullOrEmpty(sourceName))
                return;
            try
            {
                object block = Block(editorUI, getBlockPawnList, "BlockPawnList");
                if (block != null)
                    changeListMethod.Invoke(block, new object[] { sourceName });
            }
            catch (Exception ex)
            {
                Fail("ChangeListSource", ex);
            }
        }

        /// <summary>Flips the on-map filter through the globe button's own handler, which also reloads the list.</summary>
        public static void ToggleOnMapFilter(Window editorUI)
        {
            if (!Ready)
                return;
            try
            {
                object block = Block(editorUI, getBlockPawnList, "BlockPawnList");
                if (block != null)
                    changedOnMapMethod.Invoke(block, null);
            }
            catch (Exception ex)
            {
                Fail("ToggleOnMapFilter", ex);
            }
        }

        /// <summary>
        /// True while a Character Editor action is invoking one of the mod's own delegates. Read by
        /// <c>DialogInterceptionPatch</c>: a vanilla <c>FloatMenu</c> spawned inside such an
        /// invocation must ride the windowless menu path, because a real FloatMenu opened far from
        /// the mouse closes itself on the next update and a keyboard user would never see it.
        /// </summary>
        internal static bool DelegateInFlight { get; private set; }

        /// <summary>Runs <paramref name="action"/> with <see cref="DelegateInFlight"/> raised.</summary>
        internal static void RunWithMenuRedirect(Action action)
        {
            DelegateInFlight = true;
            try
            {
                action();
            }
            finally
            {
                DelegateInFlight = false;
            }
        }

        /// <summary>
        /// Points the mod's visual tab strip at a <c>TabType</c> member through the tab buttons' own
        /// delegate, so a sighted spectator's view follows the keyboard user. Declines an unknown
        /// name silently.
        /// </summary>
        public static void SwitchTab(Window editorUI, string tabName)
        {
            if (!Ready || editorUI == null || !editorUIType.IsInstanceOfType(editorUI))
                return;
            try
            {
                object tab = EnumValue(tabTypeType, tabName);
                if (tab != null)
                    tabSwitchMethod.Invoke(editorUI, new[] { tab });
            }
            catch (Exception ex)
            {
                Fail("SwitchTab", ex);
            }
        }
    }
}
