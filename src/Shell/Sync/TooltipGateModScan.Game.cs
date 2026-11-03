using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The mod half of the tooltip hover gate. Vanilla sites come from a
    /// registry generated off the decompiled tree; a mod's cannot, so this
    /// discovers them at startup by walking every running mod's assemblies for
    /// <c>Verse.Mouse::IsOver</c> call sites and handing each holder to the
    /// same <see cref="TooltipGateAnalysis"/> certification and the same
    /// operand-swap transpiler. Mods get identical rules, not looser ones.
    ///
    /// Everything here is best-effort. A poisoned type, an unreadable body, a
    /// method Harmony refuses, or the whole sweep running past its time budget
    /// costs recovered mod tooltips and nothing else: no exception escapes to
    /// startup, and nothing about vanilla coverage depends on this running.
    /// </summary>
    public static class TooltipGateModScan
    {
        private const byte Call = 0x28;
        private const byte Callvirt = 0x6F;
        private const int MemberRefTable = 0x0A;
        private const int MethodDefTable = 0x06;

        /// <summary>
        /// Wall-clock ceiling for the whole sweep. A player with hundreds of
        /// mods gets partial coverage rather than a stalled load; the prefilter
        /// below keeps a normal list far under this.
        /// </summary>
        private const long BudgetMs = 4000;

        private static bool installed;

        internal static readonly TooltipGateCoverage Coverage = new TooltipGateCoverage();

        public static int AssembliesScanned { get; private set; }
        public static int AssembliesFailed { get; private set; }
        public static int MethodsAnalyzed { get; private set; }
        public static bool BudgetExhausted { get; private set; }

        public static void Install(Harmony harmony)
        {
            if (installed || harmony == null)
            {
                return;
            }
            installed = true;

            var watch = Stopwatch.StartNew();
            HarmonyMethod transpiler = TooltipGateTranspiler.Patch();
            Assembly self = Assembly.GetExecutingAssembly();
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            for (int i = 0; i < mods.Count && !BudgetExhausted; i++)
            {
                List<Assembly> loaded = mods[i]?.assemblies?.loadedAssemblies;
                if (loaded == null)
                {
                    continue;
                }
                for (int j = 0; j < loaded.Count; j++)
                {
                    if (loaded[j] == null || loaded[j] == self)
                    {
                        continue;
                    }
                    if (watch.ElapsedMilliseconds > BudgetMs)
                    {
                        BudgetExhausted = true;
                        break;
                    }
                    AssembliesScanned++;
                    ScanAssembly(harmony, transpiler, loaded[j]);
                }
            }

            watch.Stop();
#if DEBUG
            ModLogger.Msg("Tooltip gate (mods): " + Coverage.SitesCertified + " sites certified across "
                + Coverage.MethodsPatched + " methods in " + AssembliesScanned + " assemblies (analyzed "
                + MethodsAnalyzed + ", refused " + Coverage.RefusedTotal + ", unreadable " + AssembliesFailed
                + (BudgetExhausted ? ", BUDGET EXHAUSTED" : string.Empty) + "), " + watch.ElapsedMilliseconds + " ms");
#endif
        }

        private static void ScanAssembly(Harmony harmony, HarmonyMethod transpiler, Assembly assembly)
        {
            Module[] modules;
            try
            {
                modules = assembly.GetModules();
            }
            catch (Exception ex)
            {
                AssembliesFailed++;
                LogScanFailure("Tooltip gate mod scan (" + assembly.FullName + ")", ex);
                return;
            }

            for (int i = 0; i < modules.Length; i++)
            {
                var memo = new Dictionary<int, bool>();
                foreach (Type type in TypesIn(modules[i], assembly))
                {
                    if (type == null)
                    {
                        continue;
                    }
                    try
                    {
                        // GetTypes already returns nested types, so this walks
                        // lambda display classes without descending itself.
                        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                        {
                            if (!TooltipGateInstaller.IsPatchable(method) || !CallsHoverTest(method, memo))
                            {
                                continue;
                            }
                            MethodsAnalyzed++;
                            TooltipGateInstaller.InstallOn(harmony, transpiler, method, Coverage);
                        }
                    }
                    catch (Exception ex)
                    {
                        // A poisoned type resolves far enough to be listed and
                        // then throws on member access (see SafeTypeSweep).
                        LogScanFailure("Tooltip gate mod scan (" + type.FullName + ")", ex);
                    }
                }
            }
        }

        private static IEnumerable<Type> TypesIn(Module module, Assembly assembly)
        {
            try
            {
                return module.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Vanilla's own recovery: the types that did load are usable.
                return ex.Types;
            }
            catch (Exception ex)
            {
                AssembliesFailed++;
                LogScanFailure("Tooltip gate mod scan (" + assembly.FullName + ")", ex);
                return Array.Empty<Type>();
            }
        }

        /// <summary>
        /// Reports a scan failure at the level it deserves. This sweep runs
        /// during startup patching, where vanilla auto-opens the debug log
        /// window for ANY logged error whether or not dev mode is on
        /// (decompiled Verse/Log.cs), so only a real defect may go in as one.
        /// A mod compiled with optional hooks into mods the player does not
        /// have fails to resolve them here as a matter of course — expected,
        /// costing nothing but that mod's recovered tooltips.
        /// </summary>
        private static void LogScanFailure(string context, Exception ex)
        {
            if (ModLogger.IsForeignAssemblyFault(ex))
            {
                ModLogger.LimitedMessage(context, ex);
                return;
            }
            ModLogger.LimitedError(context, ex);
        }

        /// <summary>
        /// Cheap prefilter: does this method body contain a call to
        /// <c>Mouse.IsOver</c>? Reflection has no such query, and reading every
        /// method of every mod assembly through Harmony's body parser costs
        /// seconds of load time, so this scans the raw IL for a call opcode
        /// followed by a token that resolves to the hover test. Tokens are
        /// memoized per module, so an assembly pays one resolve per distinct
        /// call target. Byte positions inside an operand can spell a call
        /// opcode; a false positive costs one real analysis that finds no site.
        /// </summary>
        private static bool CallsHoverTest(MethodInfo method, Dictionary<int, bool> memo)
        {
            byte[] il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception)
            {
                return false;
            }
            if (il == null)
            {
                return false;
            }

            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != Call && il[i] != Callvirt)
                {
                    continue;
                }
                int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                int table = (token >> 24) & 0xFF;
                if (table != MemberRefTable && table != MethodDefTable)
                {
                    continue;
                }
                if (ResolvesToHoverTest(method.Module, token, memo))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ResolvesToHoverTest(Module module, int token, Dictionary<int, bool> memo)
        {
            if (memo.TryGetValue(token, out bool known))
            {
                return known;
            }
            bool hit = false;
            try
            {
                MethodBase target = module.ResolveMethod(token);
                hit = target != null && target.Name == nameof(Mouse.IsOver) && target.DeclaringType == typeof(Mouse);
            }
            catch (Exception)
            {
                // Not a method token, or one this module cannot resolve.
            }
            memo[token] = hit;
            return hit;
        }
    }
}
