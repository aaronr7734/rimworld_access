using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Ordered resolution for gizmo execution and announcement. Three tiers: exact runtime type then
    /// its base types (<see cref="TypeChainResolver{T}"/>, a handler list per type, core first), then
    /// a <c>GetType().Name</c> match for the few non-public types, then the generic fallback
    /// (<c>ProcessInput</c> verbatim), tried whenever the earlier tiers' candidates all decline.
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
        // A list per type and per name: several mods may each add a facet to the same vanilla type.
        private static readonly TypeChainResolver<List<IGizmoHandler>> byType = new TypeChainResolver<List<IGizmoHandler>>();
        private static readonly Dictionary<string, List<IGizmoHandler>> byTypeName = new Dictionary<string, List<IGizmoHandler>>();
        private static readonly List<IGizmoHandler> noHandlers = new List<IGizmoHandler>();
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

        /// <summary>Adds a handler for an exact gizmo type and its subtypes, after any already registered for that type.</summary>
        public static void Register(Type gizmoType, IGizmoHandler handler)
        {
            EnsureInitialized();
            if (!byType.TryResolve(gizmoType, out List<IGizmoHandler> handlers, out Type matched) || matched != gizmoType)
            {
                handlers = new List<IGizmoHandler>();
                byType.Register(gizmoType, handlers);
            }
            handlers.Add(handler);
        }

        /// <summary>Adds a handler for gizmos matched by GetType().Name, after any already registered for that name.</summary>
        public static void RegisterByTypeName(string typeName, IGizmoHandler handler)
        {
            EnsureInitialized();
            if (!byTypeName.TryGetValue(typeName, out List<IGizmoHandler> handlers))
            {
                handlers = new List<IGizmoHandler>();
                byTypeName[typeName] = handlers;
            }
            handlers.Add(handler);
        }

        private static List<IGizmoHandler> NameHandlers(Type runtimeType)
        {
            return byTypeName.TryGetValue(runtimeType.Name, out List<IGizmoHandler> handlers) ? handlers : noHandlers;
        }

        private static IEnumerable<IGizmoHandler> Candidates(Type runtimeType)
        {
            foreach (List<IGizmoHandler> handlers in byType.ResolveChain(runtimeType))
            {
                foreach (IGizmoHandler handler in handlers)
                    yield return handler;
            }
            foreach (IGizmoHandler handler in NameHandlers(runtimeType))
                yield return handler;
        }

        /// <summary>The most-derived registered type whose handlers implement the facet, then the name tier.</summary>
        private static IEnumerable<IGizmoHandler> OwningCandidates(Type runtimeType, string facet)
        {
            foreach (List<IGizmoHandler> handlers in byType.ResolveChain(runtimeType))
            {
                if (!handlers.Exists(h => Implements(h, facet)))
                    continue;
                foreach (IGizmoHandler handler in handlers)
                    yield return handler;
                break;
            }
            foreach (IGizmoHandler handler in NameHandlers(runtimeType))
                yield return handler;
        }

        /// <summary>Executes through the handler chain (type, name, fallback), skipping declining candidates;
        /// returns whether GizmoNavigationState's shared post-execution epilogue should run.</summary>
        public static bool Execute(Gizmo gizmo, GizmoHandlerContext ctx)
        {
            EnsureInitialized();
            Shell.ScopeForWindow.ArmDeliberateGenericAttach();
            foreach (IGizmoHandler handler in OwningCandidates(gizmo.GetType(), nameof(IGizmoHandler.TryExecute)))
            {
                if (handler.TryExecute(gizmo, ctx, out bool runEpilogue))
                    return runEpilogue;
            }

            fallback.TryExecute(gizmo, ctx, out bool runEpilogueFallback);
            return runEpilogueFallback;
        }

        /// <summary>Calls TryDescribe in the same tiered order as <see cref="Execute"/>, so description precedence matches execution.</summary>
        public static void Describe(Gizmo gizmo, GizmoDescriptionFragments fragments)
        {
            EnsureInitialized();
            foreach (IGizmoHandler handler in OwningCandidates(gizmo.GetType(), nameof(IGizmoHandler.TryDescribe)))
            {
                if (handler.TryDescribe(gizmo, fragments))
                    return;
            }
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
            foreach (IGizmoHandler handler in Candidates(gizmo.GetType()))
            {
                if (handler.TryGetSliderAdapter(gizmo, out adapter))
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
            foreach (IGizmoHandler handler in Candidates(gizmo.GetType()))
            {
                if (Implements(handler, nameof(IGizmoHandler.TryExecute)))
                    return true;
            }
            return false;
        }

        private static readonly Dictionary<(Type, string), bool> facetOverrides = new Dictionary<(Type, string), bool>();
        private static readonly Dictionary<Type, System.Reflection.MethodInfo> processInputs = new Dictionary<Type, System.Reflection.MethodInfo>();

        private static bool Implements(IGizmoHandler handler, string facet)
        {
            var key = (handler.GetType(), facet);
            if (facetOverrides.TryGetValue(key, out bool cached))
                return cached;

            System.Reflection.MethodInfo method = key.Item1.GetMethod(facet);
            bool implemented = method != null && method.DeclaringType != typeof(GizmoHandlerBase);
            facetOverrides[key] = implemented;
            return implemented;
        }

        private static bool OverridesProcessInput(Type gizmoType)
        {
            System.Reflection.MethodInfo method = FindProcessInput(gizmoType);
            return method != null && method.DeclaringType != typeof(Gizmo);
        }

        // No game ProcessInput reads the event's button, so only an override declared outside the
        // game assembly can make a right-click without float menu options differ from a left-click.
        public static bool MayBranchOnRightClick(Gizmo gizmo)
        {
            if (gizmo == null)
                return false;
            System.Reflection.MethodInfo method = FindProcessInput(gizmo.GetType());
            return method != null && method.DeclaringType.Assembly != typeof(Gizmo).Assembly;
        }

        private static System.Reflection.MethodInfo FindProcessInput(Type gizmoType)
        {
            if (!processInputs.TryGetValue(gizmoType, out System.Reflection.MethodInfo method))
            {
                method = gizmoType.GetMethod(
                    "ProcessInput",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                    null,
                    new Type[] { typeof(UnityEngine.Event) },
                    null);
                processInputs[gizmoType] = method;
            }
            return method;
        }

        /// <summary>
        /// Whether the gizmo type resolves to any registered handler, i.e. announcing an instance would
        /// not fall back to the cleaned-up type name. The DEBUG self-audit's probe; the tier walk
        /// mirrors <see cref="ResolveFacet"/>'s.
        /// </summary>
        internal static bool HasNonFallbackHandler(Type gizmoType)
        {
            EnsureInitialized();
            foreach (IGizmoHandler _ in Candidates(gizmoType))
                return true;
            return false;
        }

        /// <summary>
        /// Accumulates keyboard-accessible extra options from EVERY handler along the type chain
        /// (unlike <see cref="ResolveFacet"/>'s first-non-null-wins walk) plus every byTypeName handler:
        /// a base handler and a derived one may each contribute independent options.
        /// </summary>
        public static bool CollectExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            EnsureInitialized();
            bool any = false;
            foreach (IGizmoHandler handler in Candidates(gizmo.GetType()))
            {
                if (handler.TryGetExtraOptions(gizmo, options))
                    any = true;
            }
            return any;
        }

        /// <summary>Snapshots, from every handler along the chain that claims the gizmo, the state a replayed right-click may change unseen.</summary>
        internal static List<KeyValuePair<IGizmoHandler, object>> CaptureRightClickState(Gizmo gizmo, ISelectable owner)
        {
            EnsureInitialized();
            var captured = new List<KeyValuePair<IGizmoHandler, object>>();
            foreach (IGizmoHandler handler in Candidates(gizmo.GetType()))
            {
                try
                {
                    if (handler.TryCaptureRightClickState(gizmo, owner, out object snapshot))
                        captured.Add(new KeyValuePair<IGizmoHandler, object>(handler, snapshot));
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("GizmoHandlerRegistry.CaptureRightClickState", ex);
                }
            }
            return captured;
        }

        internal static void AppendRightClickChanges(List<KeyValuePair<IGizmoHandler, object>> captured,
            Gizmo gizmo, ISelectable owner, List<string> changes)
        {
            foreach (KeyValuePair<IGizmoHandler, object> entry in captured)
            {
                try
                {
                    string change = entry.Key.DescribeRightClickChange(gizmo, owner, entry.Value);
                    if (!string.IsNullOrEmpty(change))
                        changes.Add(change);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("GizmoHandlerRegistry.AppendRightClickChanges", ex);
                }
            }
        }

        private static string ResolveFacet(Gizmo gizmo, Func<IGizmoHandler, Gizmo, string> facet)
        {
            EnsureInitialized();
            foreach (IGizmoHandler handler in Candidates(gizmo.GetType()))
            {
                string value = facet(handler, gizmo);
                if (value != null)
                    return value;
            }
            return null;
        }
    }
}
