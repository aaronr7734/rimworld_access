using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Installs the tooltip hover gate over the generated vanilla registry
    /// (<see cref="TooltipGateTargets.Names"/>), one transpiler per method that
    /// actually holds a certified site. Mod assemblies get the same treatment
    /// from <see cref="TooltipGateModScan"/>, which has no registry to work
    /// from and discovers its sites instead.
    ///
    /// Analysis runs before patching so startup cost tracks real coverage
    /// rather than registry size, and every method is wrapped on its own: a
    /// type renamed by a game update, or a body Harmony cannot read, costs one
    /// skipped method and not the sweep.
    /// </summary>
    public static class TooltipGateInstaller
    {
        private static bool installed;

        internal static readonly TooltipGateCoverage Coverage = new TooltipGateCoverage();

        public static int TypesResolved { get; private set; }
        public static int MethodsPatched { get { return Coverage.MethodsPatched; } }
        public static int SitesCertified { get { return Coverage.SitesCertified; } }

        public static void Install(Harmony harmony)
        {
            if (installed || harmony == null)
            {
                return;
            }
            installed = true;

            var watch = Stopwatch.StartNew();
            HarmonyMethod transpiler = TooltipGateTranspiler.Patch();
            int methodsScanned = 0;

            string[] names = TooltipGateTargets.Names;
            for (int i = 0; i < names.Length; i++)
            {
                Type type = AccessTools.TypeByName(names[i]);
                if (type == null)
                {
                    ModLogger.Warning("Tooltip gate: type " + names[i] + " not found; skipping.");
                    continue;
                }
                if (RequiresInactiveDlc(names[i]))
                {
                    continue;
                }
                TypesResolved++;
                foreach (Type walked in WithNestedTypes(type))
                {
                    foreach (MethodInfo declared in AccessTools.GetDeclaredMethods(walked))
                    {
                        MethodInfo method = declared.IsGenericMethodDefinition
                            ? CloseOverReferenceTypes(declared)
                            : declared;
                        if (!IsPatchable(method))
                        {
                            continue;
                        }
                        methodsScanned++;
                        InstallOn(harmony, transpiler, method, Coverage);
                    }
                }
            }

            watch.Stop();
#if DEBUG
            ModLogger.Msg("Tooltip gate: " + SitesCertified + " sites certified across " + MethodsPatched
                + " methods in " + TypesResolved + " types (scanned " + methodsScanned
                + ", refused " + Coverage.RefusedTotal + "), " + watch.ElapsedMilliseconds + " ms");
#endif
        }

        // Biotech-only types with eager static texture initializers: resolving them on a
        // Biotech-free install loads absent biostat icons and logs missing-texture errors, and
        // their UI never draws anyway, so they are skipped when Biotech is off.
        private static bool RequiresInactiveDlc(string typeName)
        {
            switch (typeName)
            {
                case "RimWorld.BiostatsTable":
                case "RimWorld.GeneUIUtility":
                    return !Verse.ModsConfig.BiotechActive;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Analyzes one method and, when it holds a certified site, installs the
        /// operand-swap transpiler on it. Returns the number of sites certified.
        /// </summary>
        internal static int InstallOn(Harmony harmony, HarmonyMethod transpiler, MethodBase method,
            TooltipGateCoverage coverage)
        {
            try
            {
                IReadOnlyList<GateSiteResult> sites =
                    TooltipGateTranspiler.Analyze(PatchProcessor.GetOriginalInstructions(method), method);
                int eligible = coverage.Record(sites, TooltipGateTranspiler.Describe(method));
                if (eligible == 0)
                {
                    return 0;
                }
                harmony.Patch(method, transpiler: transpiler);
                coverage.NotePatched(eligible);
                return eligible;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Tooltip gate install (" + TooltipGateTranspiler.Describe(method) + ")", ex);
                return 0;
            }
        }

        private static IEnumerable<Type> WithNestedTypes(Type type)
        {
            yield return type;
            // Tooltip registrations inside inline delegates are lowered into
            // compiler-generated nested classes, so the sweep has to descend.
            foreach (Type nested in type.GetNestedTypes(AccessTools.all))
            {
                foreach (Type inner in WithNestedTypes(nested))
                {
                    yield return inner;
                }
            }
        }

        /// <summary>
        /// A generic method definition closed over the reference type each of
        /// its parameters is constrained to, or null when no such closure
        /// exists. Harmony refuses an open definition outright, and Mono
        /// compiles one shared body for every reference-type instantiation, so
        /// patching the constraint's own instantiation reaches all of them. On
        /// 1.7's CoreCLR a patch on a generic method never runs, so there these
        /// sites stay vanilla, as a value-type instantiation always does.
        /// </summary>
        private static MethodInfo CloseOverReferenceTypes(MethodInfo method)
        {
            Type[] parameters = method.GetGenericArguments();
            var arguments = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if ((parameters[i].GenericParameterAttributes
                        & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                {
                    return null;
                }
                arguments[i] = typeof(object);
                foreach (Type constraint in parameters[i].GetGenericParameterConstraints())
                {
                    if (!constraint.IsInterface && !constraint.IsValueType && !constraint.ContainsGenericParameters)
                    {
                        arguments[i] = constraint;
                    }
                }
            }
            try
            {
                return method.MakeGenericMethod(arguments);
            }
            catch (Exception)
            {
                // An interface or new() constraint object cannot satisfy.
                return null;
            }
        }

        internal static bool IsPatchable(MethodInfo method)
        {
            if (method == null || method.IsAbstract)
            {
                return false;
            }
            try
            {
                // GetParameters canary first: it resolves the signature through mono's
                // CHECKED loader and throws managed for a method compiled against an
                // absent optional assembly, where ContainsGenericParameters' UNCHECKED
                // icall dereferences the null signature and segfaults the whole game.
                method.GetParameters();
                return !method.ContainsGenericParameters
                    && method.GetMethodBody() != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
