#if DEBUG
using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// DEBUG-only completeness audit for the inspection/gizmo handler registry
    /// (rework plan §5.3; compiled out of Release like the dev bridge). Two
    /// complementary probes:
    ///
    /// 1. Static census on startup: every concrete Gizmo subtype in every
    ///    loaded assembly — vanilla or mod — must resolve to a non-fallback
    ///    handler through the registry tiers, or sit on the explicit whitelist
    ///    with a reason. Failures are logged, not thrown: a mod adding an
    ///    unhandled gizmo type still runs, it just announces by cleaned-up
    ///    type name, and this log is how we find out before a player does.
    ///
    /// 2. Runtime fallback counter: GetGizmoLabel reports every label that
    ///    falls back to the cleaned-up type name. Counts and offending types
    ///    are queryable via the dev bridge
    ///    (RimWorldAccess.InspectionSelfAudit.LabelFallbackCount / Report()).
    ///
    /// 3. Tab census: every concrete InspectTabBase subtype —
    ///    enumerated from InspectTabBase, NOT ITab, so the WITab_* world tabs
    ///    are seen — is listed when it has no InspectNodeRegistry adapter and
    ///    no whitelist entry; such a tab is read by the generic captured
    ///    reader. World tabs are whitelisted by predicate: world objects
    ///    are served by the hand-built caravan/world-info states, not the
    ///    inspection tree (plan §7.4 decision).
    /// </summary>
    [StaticConstructorOnStartup]
    public static class InspectionSelfAudit
    {
        /// <summary>
        /// Gizmo types allowed to lack a non-fallback handler, keyed by
        /// Type.FullName, with the reason they are safe. Keep this list short
        /// and justified — every entry is a type that announces by cleaned-up
        /// type name.
        /// </summary>
        private static readonly Dictionary<string, string> gizmoWhitelist =
            new Dictionary<string, string>
            {
            };

        /// <summary>How many times a gizmo label fell back to the cleaned-up type name.</summary>
        public static int LabelFallbackCount;

        private static readonly HashSet<Type> fallbackTypesSeen = new HashSet<Type>();

        /// <summary>
        /// Tab types allowed to lack an adapter, keyed by Type.FullName, with
        /// the reason they are safe. WITab world tabs are exempted by predicate
        /// in RunTabCensus, not listed here.
        /// </summary>
        private static readonly Dictionary<string, string> tabWhitelist =
            new Dictionary<string, string>
            {
            };

        static InspectionSelfAudit()
        {
            try
            {
                RunGizmoCensus();
                RunTabCensus();
                RunCategoryCensus();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Inspection self-audit failed: {ex}");
            }
        }

        /// <summary>
        /// Probe 4: the category-key dispatch must be complete. (a) Every tab-registered adapter with
        /// RichNavigation behavior must resolve to ITSELF through the category-key index, or its
        /// expanded node silently degrades to the detailed-info fallback. (b) Every category-registered
        /// adapter declaring Action behavior must override ExecuteAction, or its row activates into a
        /// no-op. Returns the failing descriptions (empty when clean) so the dev bridge can re-run it
        /// on demand.
        /// </summary>
        public static List<string> RunCategoryCensus()
        {
            var failures = new List<string>();

            foreach (Type type in SafeTypeSweep.SubclassesNonAbstractSafe(typeof(InspectTabBase), LogSweepFailure))
            {
                if (typeof(RimWorld.Planet.WITab).IsAssignableFrom(type))
                    continue;
                if (!InspectNodeRegistry.TryResolve(type, out InspectNodeAdapter adapter))
                    continue;
                if (adapter.Handler != TabHandlerType.RichNavigation)
                    continue;
                if (!InspectNodeRegistry.TryResolveCategory(adapter.CategoryKey, out InspectNodeAdapter byKey)
                    || !ReferenceEquals(byKey, adapter))
                    failures.Add($"{type.FullName}: rich adapter {adapter.GetType().Name} is not its own "
                        + $"category-key registration for \"{adapter.CategoryKey}\"");
            }

            foreach (InspectNodeAdapter adapter in InspectNodeRegistry.AllCategoryAdapters())
            {
                if (adapter.Handler != TabHandlerType.Action)
                    continue;
                if (adapter.GetType().GetMethod(nameof(InspectNodeAdapter.ExecuteAction)).DeclaringType
                    == typeof(InspectNodeAdapter))
                    failures.Add($"{adapter.GetType().Name} (\"{adapter.CategoryKey}\"): Action handler "
                        + "without an ExecuteAction override — its row activates into a no-op");
            }

            if (failures.Count > 0)
                ModLogger.Warning("[InspectionSelfAudit] category-key dispatch incomplete: "
                    + string.Join("; ", failures));

            return failures;
        }

        /// <summary>
        /// Audits every loaded concrete InspectTabBase subtype against the
        /// inspect-node adapter registry. Returns the unregistered type names
        /// (empty when clean) so the dev bridge can re-run it on demand.
        /// An unregistered tab is read by the generic captured reader, so this is
        /// an inventory; a capture that fails at runtime logs its own error.
        /// </summary>
        public static List<string> RunTabCensus()
        {
            var failures = new List<string>();
            foreach (Type type in SafeTypeSweep.SubclassesNonAbstractSafe(typeof(InspectTabBase), LogSweepFailure))
            {
                // World tabs are served by the caravan/world-info states, not
                // the inspection tree (plan §7.4 decision).
                if (typeof(RimWorld.Planet.WITab).IsAssignableFrom(type))
                    continue;
                if (InspectNodeRegistry.HasAdapter(type))
                    continue;
                if (tabWhitelist.ContainsKey(type.FullName))
                    continue;
                failures.Add(type.FullName);
            }

            if (failures.Count > 0)
                ModLogger.Msg(
                    "[InspectionSelfAudit] inspect tabs without an adapter, read through the generic "
                    + "captured reader: " + string.Join(", ", failures));

            return failures;
        }

        /// <summary>
        /// Audits every loaded concrete Gizmo subtype against the handler
        /// registry. Returns the failing type names (empty when clean) so the
        /// dev bridge can re-run it on demand.
        /// </summary>
        public static List<string> RunGizmoCensus()
        {
            var failures = new List<string>();
            foreach (Type type in SafeTypeSweep.SubclassesNonAbstractSafe(typeof(Gizmo), LogSweepFailure))
            {
                if (GizmoHandlerRegistry.HasNonFallbackHandler(type))
                    continue;
                if (gizmoWhitelist.ContainsKey(type.FullName))
                    continue;
                failures.Add(type.FullName);
            }

            if (failures.Count > 0)
                ModLogger.Warning(
                    "[InspectionSelfAudit] gizmo types resolving only to the generic fallback "
                    + "(they announce by cleaned-up type name — register a handler or whitelist "
                    + "with a reason): " + string.Join(", ", failures));

            return failures;
        }

        /// <summary>
        /// Called by GetGizmoLabel when a label falls back to the cleaned-up
        /// type name. Logs each offending type once.
        /// </summary>
        public static void NoteLabelFallback(Type gizmoType)
        {
            LabelFallbackCount++;
            if (fallbackTypesSeen.Add(gizmoType))
                ModLogger.Warning($"[InspectionSelfAudit] label fallback for gizmo type {gizmoType.FullName}");
        }

        /// <summary>
        /// Reports a type SafeTypeSweep skipped because it could not be
        /// resolved (e.g. a mod assembly with an unresolvable dependency on
        /// this platform) — the census still runs over every OTHER loaded
        /// type instead of aborting.
        /// </summary>
        private static void LogSweepFailure(Type type, Exception ex)
        {
            ModLogger.Warning(
                $"[InspectionSelfAudit] skipping a type that failed to resolve during census "
                + $"({ex.GetType().Name}) — likely an incompatible mod dependency on this platform.");
        }

        /// <summary>Bridge-friendly summary of runtime fallback activity.</summary>
        public static string Report()
        {
            return $"label fallbacks: {LabelFallbackCount}; types: "
                + (fallbackTypesSeen.Count == 0
                    ? "(none)"
                    : string.Join(", ", System.Linq.Enumerable.Select(fallbackTypesSeen, t => t.FullName)));
        }
    }
}
#endif
