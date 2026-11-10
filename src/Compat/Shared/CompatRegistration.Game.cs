using System;
using HarmonyLib;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The registration ceremony every compat module repeats: probe the mod type, construct, honor
    /// the Ready gate, register, log once, inside a try/catch so a compat failure never breaks startup.
    /// </summary>
    internal static class CompatRegistration
    {
        /// <summary>
        /// Registers an inspection-tab adapter for a mod tab type, plus optional
        /// additional tab types served by the same adapter. Returns the adapter
        /// when registered (for follow-up wiring), else null: mod absent, adapter
        /// not Ready, or a failure (logged).
        /// </summary>
        public static InspectNodeAdapter TabAdapter(string modTabTypeName,
            Func<Type, InspectNodeAdapter> make, string logName, params string[] alsoTabTypeNames)
        {
            try
            {
                Type tabType = AccessTools.TypeByName(modTabTypeName);
                if (tabType == null)
                {
                    return null;
                }
                InspectNodeAdapter adapter = make(tabType);
                if (adapter == null || !adapter.Ready)
                {
                    return null;
                }
                InspectNodeRegistry.Register(tabType, adapter);
                foreach (string alsoName in alsoTabTypeNames)
                {
                    Type alsoType = AccessTools.TypeByName(alsoName);
                    if (alsoType != null)
                    {
                        InspectNodeRegistry.Register(alsoType, adapter);
                    }
                }
                InspectNodeRegistry.RegisterCategory(adapter);
                return adapter;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{logName} registration failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Registers a gizmo handler when the command type is present.
        /// Returns 1 when registered, else 0, so callers can sum a summary count.</summary>
        public static int GizmoHandler(string commandTypeName, Func<Type, IGizmoHandler> make)
        {
            try
            {
                Type commandType = AccessTools.TypeByName(commandTypeName);
                if (commandType == null)
                {
                    return 0;
                }
                GizmoHandlerRegistry.Register(commandType, make(commandType));
                return 1;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Gizmo handler registration for {commandTypeName} failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Registers a pawn-column handler for a present worker type; a null handler declines.</summary>
        public static void PawnColumnHandler(string workerTypeName, Func<Type, IPawnColumnHandler> make)
        {
            try
            {
                Type workerType = AccessTools.TypeByName(workerTypeName);
                if (workerType == null)
                {
                    return;
                }
                IPawnColumnHandler handler = make(workerType);
                if (handler != null)
                {
                    PawnColumnHandlerRegistry.Register(workerType, handler);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Pawn column handler registration for {workerTypeName} failed: {ex.Message}");
            }
        }
    }
}
