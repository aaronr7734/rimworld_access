using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared sorting logic for PawnColumnDef-based tabular menus.
    /// Centralizes the SortStable + Worker.Compare pattern used by
    /// AnimalsMenuHelper, WildlifeMenuHelper, and AssignMenuHelper.
    /// </summary>
    public static class PawnColumnSortHelper
    {
        /// <summary>
        /// Sorts a pawn list using the PawnColumnDef at the given index.
        /// Returns the original list unchanged if the index is out of range or the def is null.
        /// </summary>
        public static List<Pawn> SortByColumnDef(
            List<Pawn> pawns,
            IList<PawnColumnDef> columnDefs,
            int columnIndex,
            bool descending)
        {
            if (columnDefs == null || columnIndex < 0 || columnIndex >= columnDefs.Count)
                return pawns;

            var columnDef = columnDefs[columnIndex];
            if (columnDef == null)
                return pawns;

            var sorted = new List<Pawn>(pawns);
            if (descending)
                sorted.SortStable(columnDef.Worker.Compare);
            else
                sorted.SortStable((Pawn a, Pawn b) => columnDef.Worker.Compare(b, a));
            return sorted;
        }

        /// <summary>
        /// Returns whether the PawnColumnDef at the given index supports sorting.
        /// </summary>
        public static bool IsColumnSortable(
            IList<PawnColumnDef> columnDefs,
            int columnIndex)
        {
            if (columnDefs == null || columnIndex < 0 || columnIndex >= columnDefs.Count)
                return false;
            return columnDefs[columnIndex]?.sortable ?? false;
        }
    }
}
