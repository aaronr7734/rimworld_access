using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Type-keyed registry mapping game inspect-tab types to their
    /// <see cref="InspectNodeAdapter"/>. Resolution walks the tab's inheritance chain
    /// most-derived-first via <see cref="TypeChainResolver{TValue}"/>, so a base registration
    /// (e.g. ITab_ContentsBase) covers subclasses until an exact entry overrides it.
    ///
    /// World tabs (WITab and descendants) are deliberately NOT registered: world objects are served
    /// by the hand-built caravan/world-info states, not by the inspection tree.
    /// </summary>
    public static class InspectNodeRegistry
    {
        private static readonly TypeChainResolver<InspectNodeAdapter> byType =
            new TypeChainResolver<InspectNodeAdapter>();

        /// <summary>
        /// Category-key index for child-building dispatch: the orchestrator's expansion path
        /// resolves by the node's stable English category key, covering both game-tab categories and
        /// the synthetic accessibility ones (Mood, Skills, Job Queue) that have no tab type.
        /// Exactly one adapter builds children per key.
        /// </summary>
        private static readonly Dictionary<string, InspectNodeAdapter> byCategoryKey =
            new Dictionary<string, InspectNodeAdapter>();

        /// <summary>
        /// Category-keyed extenders that run AFTER a category's own adapter builds its children, so
        /// any module — mod-compat shims included — can add a row to any inspect category by its
        /// stable English key without that category's adapter knowing. The extender receives the
        /// built category item and the inspected object and places its row itself.
        /// </summary>
        private static readonly Dictionary<string, List<Action<InspectionTreeItem, object>>> categoryExtenders =
            new Dictionary<string, List<Action<InspectionTreeItem, object>>>();

        private static bool initialized;

        public static void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;

            // Pawn tabs: each registers by tab type for resolution and by category key for the
            // orchestrator's expansion dispatch.
            RegisterTab(typeof(ITab_Pawn_Health), new PawnHealthAdapter());
            RegisterTab(typeof(ITab_Pawn_Needs), new PawnNeedsAdapter());
            RegisterTab(typeof(ITab_Pawn_Character), new PawnCharacterAdapter());
            RegisterTab(typeof(ITab_Pawn_Gear), new PawnGearAdapter());
            RegisterTab(typeof(ITab_Pawn_Social), new PawnSocialAdapter());
            RegisterTab(typeof(ITab_Pawn_Training), new PawnTrainingAdapter());
            RegisterTab(typeof(ITab_Pawn_Log), new PawnLogAdapter());
            // The Visitor base covers ITab_Pawn_Guest; Prisoner/Slave override it.
            RegisterTab(typeof(ITab_Pawn_Visitor), new GuestAdapter());
            RegisterTab(typeof(ITab_Pawn_Prisoner), new PrisonerSlaveAdapter("Prisoner"));
            RegisterTab(typeof(ITab_Pawn_Slave), new PrisonerSlaveAdapter("Slave"));
            RegisterTab(typeof(ITab_Pawn_Feeding), new PawnFeedingAdapter());
            Register(typeof(ITab_Pawn_FormingCaravan), new StaticTabAdapter("Forming Caravan", TabHandlerType.BasicInspectString));

            // Synthetic accessibility categories (no tab type): category-key dispatch only.
            RegisterCategory(new OverviewAdapter());
            RegisterCategory(new GizmosAdapter());
            RegisterCategory(new PawnMoodAdapter());
            RegisterCategory(new PawnSkillsAdapter());
            RegisterCategory(new PawnAppearanceAdapter());
            RegisterCategory(new PawnWorkPrioritiesAdapter());
            RegisterCategory(new PawnJobQueueAdapter());
            RegisterCategory(new LinkedFacilitiesAdapter());
            RegisterCategory(new MeditationFocusAdapter());

            // Synthetic action categories: building/zone-derived actions and component controls.
            RegisterCategory(new RenameAdapter());
            RegisterCategory(new BedAssignmentAdapter());
            RegisterCategory(new OwnerAssignmentAdapter());
            RegisterCategory(new TemperatureAdapter());
            RegisterCategory(new PlantSelectionAdapter());
            RegisterCategory(new RefuelableAdapter());
            RegisterCategory(new DoorControlsAdapter());
            RegisterCategory(new ForbidControlsAdapter());

            // Building tabs.
            RegisterTab(typeof(ITab_Bills), new BillsAdapter());
            // The Storage base covers future subclasses; the biosculpter pod and turret shells
            // override it (both subclass ITab_Storage in vanilla).
            RegisterTab(typeof(ITab_Storage), new StorageAdapter());
            RegisterTab(typeof(ITab_BiosculpterNutritionStorage), new NutritionStorageAdapter());
            RegisterTab(typeof(ITab_Shells), new ShellsAdapter());
            RegisterTab(typeof(ITab_WindTurbineAutoCut), new WindTurbineAutoCutAdapter());
            RegisterTab(typeof(ITab_Art), new ArtAdapter());

            // Contents tabs: the base covers Casket/MapPortal and mod storage tabs; transporter,
            // bookcase, outfit stand and genepack holder override.
            RegisterTab(typeof(ITab_ContentsBase), new ContainerContentsAdapter());
            RegisterTab(typeof(ITab_ContentsTransporter), new TransporterContentsAdapter());
            RegisterTab(typeof(ITab_ContentsBooks), new BookcaseContentsAdapter());
            RegisterTab(typeof(ITab_ContentsOutfitStand), new OutfitStandContentsAdapter());
            Register(typeof(ITab_ContentsGenepackHolder), new StaticTabAdapter("Genepacks", TabHandlerType.BasicInspectString));

            // DLC tabs.
            RegisterTab(typeof(ITab_Genes), new PawnGenesAdapter());
            Register(typeof(ITab_GenesPregnancy), new StaticTabAdapter("Pregnancy Genes", TabHandlerType.BasicInspectString));
            RegisterTab(typeof(ITab_Entity), new EntityAdapter());
            // The StudyNotes base covers the UnnaturalCorpse/VoidMonolith subclasses.
            Register(typeof(ITab_StudyNotes), new StaticTabAdapter("Study Notes", TabHandlerType.BasicInspectString));
            RegisterTab(typeof(ITab_Fishing), new FishingAdapter());
            RegisterTab(typeof(ITab_Book), new BookAdapter());

            // Pen tabs: base fallback plus per-tab behavior.
            Register(typeof(ITab_PenBase), new StaticTabAdapter("Pen", TabHandlerType.BasicInspectString));
            RegisterTab(typeof(ITab_PenAnimals), new PenAnimalsAdapter());
            RegisterTab(typeof(ITab_PenFood), new PenFoodAdapter());
            RegisterTab(typeof(ITab_PenAutoCut), new PenAutoCutAdapter());
        }

        /// <summary>Registers an adapter against the exact tab type. Public for mod-compat shims.</summary>
        public static void Register(Type tabType, InspectNodeAdapter adapter)
        {
            byType.Register(tabType, adapter);
        }

        /// <summary>Registers a child-building tab adapter by tab type and by its category key.</summary>
        private static void RegisterTab(Type tabType, InspectNodeAdapter adapter)
        {
            Register(tabType, adapter);
            RegisterCategory(adapter);
        }

        /// <summary>
        /// Registers an adapter as the child-builder for its category key: synthetic (tab-less)
        /// categories, and tab adapters whose content builds through the category-key path.
        /// </summary>
        public static void RegisterCategory(InspectNodeAdapter adapter)
        {
            byCategoryKey[adapter.CategoryKey] = adapter;
        }

        /// <summary>
        /// Registers an extender that runs after the given category builds its own children.
        /// Public for mod-compat shims.
        /// </summary>
        public static void RegisterCategoryExtender(string categoryKey, Action<InspectionTreeItem, object> extender)
        {
            if (string.IsNullOrEmpty(categoryKey) || extender == null)
                return;
            if (!categoryExtenders.TryGetValue(categoryKey, out var list))
            {
                list = new List<Action<InspectionTreeItem, object>>();
                categoryExtenders[categoryKey] = list;
            }
            list.Add(extender);
        }

        /// <summary>
        /// Runs the registered extenders for a category key (no-op when none). Each is isolated: a
        /// throwing extender is logged and skipped so it can never break the category.
        /// </summary>
        public static void InvokeCategoryExtenders(string categoryKey, InspectionTreeItem categoryItem, object obj)
        {
            if (categoryKey == null || !categoryExtenders.TryGetValue(categoryKey, out var list))
                return;
            foreach (var extender in list)
            {
                try
                {
                    extender(categoryItem, obj);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"Inspection category extender for '{categoryKey}' failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Resolves the child-building adapter for a category key. False for keys served by the
        /// orchestrator's fallback presentations (action categories, detailed-info text, mod tabs).
        /// </summary>
        public static bool TryResolveCategory(string categoryKey, out InspectNodeAdapter adapter)
        {
            EnsureInitialized();
            if (categoryKey != null && byCategoryKey.TryGetValue(categoryKey, out adapter))
                return true;
            adapter = null;
            return false;
        }

        /// <summary>
        /// Resolves the adapter for a tab's runtime type, walking base types most-derived-first.
        /// False for unregistered tabs and for a base-type registration whose concrete type
        /// overrides FillTab (see <see cref="DeclinesBaseRegistration"/>) — such a tab falls through
        /// to the unknown-tab dynamic branch rather than a vanilla adapter describing pixels the mod
        /// no longer draws. An exact-type registration always wins.
        /// </summary>
        public static bool TryResolve(Type tabType, out InspectNodeAdapter adapter)
        {
            EnsureInitialized();
            if (!byType.TryResolve(tabType, out adapter, out Type matchedType))
                return false;
            if (DeclinesBaseRegistration(tabType, matchedType))
            {
                adapter = null;
                return false;
            }
            return true;
        }

        /// <summary>Census probe: does this tab type resolve to any adapter?</summary>
        internal static bool HasAdapter(Type tabType)
        {
            return TryResolve(tabType, out _);
        }

        /// <summary>
        /// True when <paramref name="matchedType"/> is a strict ancestor of
        /// <paramref name="tabType"/> AND that tab's own FillTab override was introduced somewhere
        /// below the match — i.e. the concrete tab draws different pixels than the registered base
        /// type's adapter describes. <c>Type.GetMethod</c> without <c>DeclaredOnly</c> resolves
        /// FillTab the way virtual dispatch would, and its DeclaringType is where the override
        /// lives. Defensive: a reflection failure never declines a registration, since a
        /// possibly-stale adapter beats nothing.
        /// </summary>
        private static bool DeclinesBaseRegistration(Type tabType, Type matchedType)
        {
            if (tabType == matchedType)
                return false; // exact registration always wins

            Type fillTabDeclaringType = ResolveFillTabDeclaringType(tabType);
            if (fillTabDeclaringType == null)
                return false;

            return !TypeHierarchy.IsSameOrAncestor(fillTabDeclaringType, matchedType);
        }

        private static Type ResolveFillTabDeclaringType(Type tabType)
        {
            try
            {
                MethodInfo fillTab = tabType.GetMethod("FillTab",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return fillTab?.DeclaringType;
            }
            catch (AmbiguousMatchException)
            {
                return null;
            }
        }

        /// <summary>Census accessor (DEBUG self-audit): every adapter registered for category-key dispatch.</summary>
        internal static IEnumerable<InspectNodeAdapter> AllCategoryAdapters()
        {
            EnsureInitialized();
            return byCategoryKey.Values;
        }
    }
}
