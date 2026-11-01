using System;
using RimWorld;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A customizable pawn table's own column management (a mod that lets the player remove and
    /// drag-reorder columns), so the generic pawn-table tier can offer the same edits from the
    /// header row. Implementations drive the mod's own removal and reorder code.
    /// </summary>
    public interface IPawnTableColumnEditor
    {
        /// <summary>The header tooltip as presented; lets the mod's mouse-only instructions drop out.</summary>
        string FilterHeaderTip(PawnColumnDef def, string tip);

        void RemoveColumn(MainTabWindow window, PawnTable table, PawnColumnDef def);

        /// <summary>Moves <paramref name="column"/> to the far side of its adjacent navigable <paramref name="neighbor"/>; <paramref name="direction"/> is -1 for left, +1 for right.</summary>
        void MoveColumn(MainTabWindow window, PawnTable table, PawnColumnDef column, PawnColumnDef neighbor, int direction);
    }

    /// <summary>Resolves the column editor for a live <see cref="PawnTable"/> by its runtime type chain.</summary>
    public static class PawnTableColumnEditorRegistry
    {
        private static readonly TypeChainResolver<IPawnTableColumnEditor> resolver = new TypeChainResolver<IPawnTableColumnEditor>();

        public static void Register(Type tableType, IPawnTableColumnEditor editor)
        {
            resolver.Register(tableType, editor);
        }

        public static IPawnTableColumnEditor Resolve(PawnTable table)
        {
            IPawnTableColumnEditor editor;
            return table != null && resolver.TryResolve(table.GetType(), out editor) ? editor : null;
        }
    }
}
