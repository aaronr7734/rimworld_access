using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Exception-safe cell reads through <see cref="PawnColumnHandlerRegistry"/>, shared by the
    /// generic pawn-table tier and the bespoke tables' Unknown columns. A throwing handler is
    /// logged once per worker type and degrades to the honest not-readable phrase.
    /// </summary>
    internal static class PawnColumnCellReader
    {
        private static readonly HashSet<Type> loggedFailures = new HashSet<Type>();

        /// <summary>The cell's readable value; the not-readable phrase when the handler throws.</summary>
        public static string CellText(PawnColumnDef def, Pawn pawn)
        {
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellText(def, pawn);
            }
            catch (Exception ex)
            {
                LogFailure(def, ex);
                return "RimWorldAccess.Shell.Generic.CellNotReadable".Loc().ToString();
            }
        }

        /// <summary>The cell's tooltip; null when absent or the handler throws.</summary>
        public static string CellTip(PawnColumnDef def, Pawn pawn)
        {
            if (def == null || pawn == null)
            {
                return null;
            }
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellTip(def, pawn);
            }
            catch (Exception ex)
            {
                LogFailure(def, ex);
                return null;
            }
        }

        public static string CellHint(PawnColumnDef def, Pawn pawn)
        {
            if (def == null || pawn == null)
            {
                return null;
            }
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellHint(def, pawn);
            }
            catch (Exception ex)
            {
                LogFailure(def, ex);
                return null;
            }
        }

        /// <summary>
        /// Enter for the windowless bespoke tabs: only handlers declaring
        /// <see cref="IPawnColumnHandler.ActivatesCellWithoutTable"/> are invoked (null table);
        /// everything else is NotHandled rather than risking a null dereference in a worker.
        /// </summary>
        public static PawnColumnActivation ActivateCellTableless(PawnColumnDef def, Pawn pawn)
        {
            if (def == null || pawn == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            try
            {
                IPawnColumnHandler handler = PawnColumnHandlerRegistry.Resolve(def);
                if (!handler.ActivatesCellWithoutTable(def))
                {
                    return PawnColumnActivation.NotHandled;
                }
                return handler.ActivateCell(def, pawn, null);
            }
            catch (Exception ex)
            {
                LogFailure(def, ex);
                return PawnColumnActivation.NotHandled;
            }
        }

        public static void LogFailure(PawnColumnDef def, Exception ex)
        {
            Type workerType = def.workerClass;
            if (workerType != null && loggedFailures.Add(workerType))
            {
                Log.Warning("[RimWorld Access] Pawn column cell handler failed for column '"
                    + def.defName + "' (" + workerType.FullName + "): " + ex);
            }
        }
    }
}
