using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Ordered resolution for gizmo execution and announcement. Three tiers: exact runtime type then
    /// its base types (<see cref="TypeChainResolver{T}"/>), then a <c>GetType().Name</c> match for the
    /// few non-public types, then the generic fallback (<c>ProcessInput</c> verbatim), tried whenever
    /// the earlier tiers have no candidate or their candidate declines.
    /// Tier order between 1 and 2 cannot change behaviour: every branch tests a distinct,
    /// non-overlapping concrete type, and none of the name-matched types derive from Command.
    /// A candidate may decline (TryExecute/TryDescribe returning false) to fall through —
    /// MechanitorControlGroupGizmo does exactly that when its control group cannot be resolved.
    /// Designator is deliberately NOT resolved here: its branch must run BEFORE the owner-selection
    /// preamble every other branch depends on and uses its own owner-sync (without the multi-select
    /// gate), so GizmoNavigationState.ExecuteSelected keeps an explicit `is Designator` check ahead of
    /// the preamble and calls DesignatorGizmoHandler.Execute directly.
    /// </summary>
    public static class GizmoHandlerRegistry
    {
        private static readonly TypeChainResolver<IGizmoHandler> byType = new TypeChainResolver<IGizmoHandler>();
        private static readonly Dictionary<string, IGizmoHandler> byTypeName = new Dictionary<string, IGizmoHandler>();
        private static IGizmoHandler fallback;
        private static bool initialized;

        /// <summary>Registers the default handler set. Idempotent.</summary>
        public static void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;

            Register(typeof(Command_SetPlantToGrow), new PlantToGrowGizmoHandler());
            Register(typeof(Command_Toggle), new ToggleGizmoHandler());
            Register(typeof(Command_VerbTarget), new VerbTargetGizmoHandler());
            Register(typeof(Command_Target), new TargetGizmoHandler());
            Register(typeof(Command_Ability), new AbilityGizmoHandler());

            // Public game types belong in the byType tier: it resolves first, so a byTypeName entry
            // would be silently shadowed the moment a base type gains a registration. byTypeName is
            // for genuinely non-public types only. The Command base handler supplies label and
            // description for every command subtype whose exact-type handler declines those facets;
            // it has no execution duty, so Execute's fallthrough is unchanged.
            Register(typeof(Command), new CommandGizmoHandler());

            Register(typeof(MechanitorControlGroupGizmo), new MechanitorControlGroupGizmoHandler());
            Register(typeof(PsychicEntropyGizmo), new PsychicEntropyGizmoHandler());
            Register(typeof(GeneGizmo_ResourceHemogen), new HemogenGizmoHandler());
            Register(typeof(ActivityGizmo), new ActivityGizmoHandler());

            // Announcement handlers for the status-display gizmo family. The Gizmo_Slider base
            // handler serves every slider subtype that declines a facet, modded ones included.
            Register(typeof(Verse.Gizmo_Slider), new SliderGizmoHandler());
            Register(typeof(Gizmo_SetFuelLevel), new FuelLevelGizmoHandler());
            Register(typeof(Gizmo_EnergyShieldStatus), new EnergyShieldGizmoHandler());
            Register(typeof(MechanitorBandwidthGizmo), new MechanitorBandwidthGizmoHandler());
            Register(typeof(Gizmo_GrowthTier), new GrowthTierGizmoHandler());
            Register(typeof(Verse.Gizmo_RoomStats), new RoomStatsGizmoHandler());
            Register(typeof(MechCarrierGizmo), new MechCarrierGizmoHandler());
            Register(typeof(MechPowerCellGizmo), new MechPowerCellGizmoHandler());
            Register(typeof(Gizmo_MechResurrectionCharges), new MechResurrectionChargesGizmoHandler());
            Register(typeof(Gizmo_ProjectileInterceptorHitPoints), new ProjectileInterceptorGizmoHandler());
            Register(typeof(Gizmo_PruningConfig), new PruningConfigGizmoHandler());
            Register(typeof(GuardianShipGizmo), new GuardianShipGizmoHandler());
            Register(typeof(RimWorld.Planet.Gizmo_CaravanInfo), new CaravanInfoGizmoHandler());
            Register(typeof(GeneGizmo_DeathrestCapacity), new DeathrestCapacityGizmoHandler());

            fallback = new GenericFallbackGizmoHandler();
        }

        /// <summary>Registers (or replaces) the handler for an exact gizmo type and its subtypes.</summary>
        public static void Register(Type gizmoType, IGizmoHandler handler)
        {
            byType.Register(gizmoType, handler);
        }

        /// <summary>Registers (or replaces) the handler for a gizmo matched by GetType().Name.</summary>
        public static void RegisterByTypeName(string typeName, IGizmoHandler handler)
        {
            byTypeName[typeName] = handler;
        }

        /// <summary>
        /// Executes the gizmo through the resolved handler chain (type, name, fallback), falling
        /// through for a declining candidate. Returns whether GizmoNavigationState's shared
        /// post-execution epilogue should run.
        /// </summary>
        public static bool Execute(Gizmo gizmo, GizmoHandlerContext ctx)
        {
            EnsureInitialized();
            Shell.ScopeForWindow.ArmDeliberateGenericAttach();
            Type runtimeType = gizmo.GetType();

            if (byType.TryResolve(runtimeType, out IGizmoHandler typeHandler)
                && typeHandler.TryExecute(gizmo, ctx, out bool runEpilogueTyped))
            {
                return runEpilogueTyped;
            }

            if (byTypeName.TryGetValue(runtimeType.Name, out IGizmoHandler nameHandler)
                && nameHandler.TryExecute(gizmo, ctx, out bool runEpilogueNamed))
            {
                return runEpilogueNamed;
            }

            fallback.TryExecute(gizmo, ctx, out bool runEpilogueFallback);
            return runEpilogueFallback;
        }

        /// <summary>Calls TryDescribe in the same tiered order as <see cref="Execute"/>, so description precedence matches execution.</summary>
        public static void Describe(Gizmo gizmo, GizmoDescriptionFragments fragments)
        {
            EnsureInitialized();
            Type runtimeType = gizmo.GetType();

            if (byType.TryResolve(runtimeType, out IGizmoHandler typeHandler)
                && typeHandler.TryDescribe(gizmo, fragments))
            {
                return;
            }

            if (byTypeName.TryGetValue(runtimeType.Name, out IGizmoHandler nameHandler))
                nameHandler.TryDescribe(gizmo, fragments);
        }

        /// <summary>Resolves the gizmo's spoken title through the handler tiers; false when no registered handler owns the type's label.</summary>
        public static bool TryResolveLabel(Gizmo gizmo, out string label)
        {
            label = ResolveFacet(gizmo, (h, g) => h.TryGetLabel(g, out string v) ? v : null);
            return label != null;
        }

        /// <summary>Resolves the gizmo's status readout (bar fill, count, meter); false when the type has no status facet.</summary>
        public static bool TryResolveStatus(Gizmo gizmo, out string status)
        {
            status = ResolveFacet(gizmo, (h, g) => h.TryGetStatus(g, out string v) ? v : null);
            return status != null;
        }

        /// <summary>Resolves the gizmo's long description (tooltip analog); false to fall back to the generic resolution.</summary>
        public static bool TryResolveDescription(Gizmo gizmo, out string description)
        {
            description = ResolveFacet(gizmo, (h, g) => h.TryGetDescription(g, out string v) ? v : null);
            return description != null;
        }

        /// <summary>Resolves the gizmo's slider-adjustment adapter through the same chain walk; a resolved adapter means the slider is adjustable right now.</summary>
        public static bool TryResolveSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            EnsureInitialized();
            Type runtimeType = gizmo.GetType();

            foreach (IGizmoHandler typeHandler in byType.ResolveChain(runtimeType))
            {
                if (typeHandler.TryGetSliderAdapter(gizmo, out adapter))
                    return true;
            }

            if (byTypeName.TryGetValue(runtimeType.Name, out IGizmoHandler nameHandler)
                && nameHandler.TryGetSliderAdapter(gizmo, out adapter))
            {
                return true;
            }

            adapter = null;
            return false;
        }

        /// <summary>
        /// Whether activating this gizmo does anything at all — the test separating an operable row from
        /// an inert readout, so a status display can be presented honestly as read-only. Answered here
        /// because it depends only on the gizmo's type and the handler that claims it. Three cases, no
        /// hand-written type list: a <see cref="Command"/> (Designator subtree included) always acts;
        /// any other gizmo reaches the fallback handler's <c>Gizmo.ProcessInput</c>, an EMPTY virtual
        /// in vanilla, so only a type that overrides it acts that way (asked of the runtime type and
        /// cached); and a registered handler can activate a gizmo that overrides nothing, which
        /// <see cref="HasExecutionHandler"/> answers.
        /// </summary>
        public static bool HasActivation(Gizmo gizmo)
        {
            if (gizmo == null)
                return false;
            if (gizmo is Command)
                return true;
            if (OverridesProcessInput(gizmo.GetType()))
                return true;
            return HasExecutionHandler(gizmo);
        }

        /// <summary>
        /// Whether any handler resolved for this gizmo actually implements execution, read off the
        /// handler classes themselves so no caller keeps a list of "gizmos that do something". A
        /// handler leaving <see cref="GizmoHandlerBase.TryExecute"/>'s declining default in place has
        /// no execution duty. The generic fallback is deliberately not consulted: it only forwards to
        /// <c>Gizmo.ProcessInput</c>, which <see cref="HasActivation"/> tests separately.
        /// </summary>
        public static bool HasExecutionHandler(Gizmo gizmo)
        {
            EnsureInitialized();
            Type runtimeType = gizmo.GetType();

            foreach (IGizmoHandler typeHandler in byType.ResolveChain(runtimeType))
            {
                if (ImplementsExecution(typeHandler))
                    return true;
            }

            return byTypeName.TryGetValue(runtimeType.Name, out IGizmoHandler nameHandler)
                && ImplementsExecution(nameHandler);
        }

        private static readonly Dictionary<Type, bool> executionOverrides = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, bool> processInputOverrides = new Dictionary<Type, bool>();

        private static bool ImplementsExecution(IGizmoHandler handler)
        {
            Type handlerType = handler.GetType();
            if (executionOverrides.TryGetValue(handlerType, out bool cached))
                return cached;

            System.Reflection.MethodInfo method = handlerType.GetMethod("TryExecute");
            bool implemented = method != null && method.DeclaringType != typeof(GizmoHandlerBase);
            executionOverrides[handlerType] = implemented;
            return implemented;
        }

        private static bool OverridesProcessInput(Type gizmoType)
        {
            if (processInputOverrides.TryGetValue(gizmoType, out bool cached))
                return cached;

            System.Reflection.MethodInfo method = gizmoType.GetMethod(
                "ProcessInput",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null,
                new Type[] { typeof(UnityEngine.Event) },
                null);
            bool overridden = method != null && method.DeclaringType != typeof(Gizmo);
            processInputOverrides[gizmoType] = overridden;
            return overridden;
        }

        /// <summary>
        /// Whether the gizmo type resolves to any registered handler, i.e. announcing an instance would
        /// not fall back to the cleaned-up type name. The DEBUG self-audit's probe; the tier walk
        /// mirrors <see cref="ResolveFacet"/>'s.
        /// </summary>
        internal static bool HasNonFallbackHandler(Type gizmoType)
        {
            EnsureInitialized();
            foreach (IGizmoHandler _ in byType.ResolveChain(gizmoType))
                return true;
            return byTypeName.ContainsKey(gizmoType.Name);
        }

        /// <summary>
        /// Accumulates keyboard-accessible extra options from EVERY handler along the type chain
        /// (unlike <see cref="ResolveFacet"/>'s first-non-null-wins walk) plus the byTypeName handler:
        /// a base handler and a derived one may each contribute independent options.
        /// </summary>
        public static bool CollectExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            EnsureInitialized();
            Type runtimeType = gizmo.GetType();
            bool any = false;

            foreach (IGizmoHandler typeHandler in byType.ResolveChain(runtimeType))
            {
                if (typeHandler.TryGetExtraOptions(gizmo, options))
                    any = true;
            }

            if (byTypeName.TryGetValue(runtimeType.Name, out IGizmoHandler nameHandler)
                && nameHandler.TryGetExtraOptions(gizmo, options))
            {
                any = true;
            }

            return any;
        }

        private static string ResolveFacet(Gizmo gizmo, Func<IGizmoHandler, Gizmo, string> facet)
        {
            EnsureInitialized();
            Type runtimeType = gizmo.GetType();

            // Offer the facet to every handler along the chain, most derived first: a subtype handler
            // that declines defers to its base type's handler.
            foreach (IGizmoHandler typeHandler in byType.ResolveChain(runtimeType))
            {
                string value = facet(typeHandler, gizmo);
                if (value != null)
                    return value;
            }

            if (byTypeName.TryGetValue(runtimeType.Name, out IGizmoHandler nameHandler))
            {
                string value = facet(nameHandler, gizmo);
                if (value != null)
                    return value;
            }

            return null;
        }
    }
}
