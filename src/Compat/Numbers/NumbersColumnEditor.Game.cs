using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' header gestures from the keyboard: removal goes through the table's own
    /// EnqueueColumnRemoval, reordering through the mod's drag ReorderAction, and both finish
    /// with the window's RefreshAndStoreSessionInWorldComp so the layout persists as a mouse
    /// edit would. The dispatcher runs before any window draws, so the removal the mod defers
    /// to the end of the table draw is applied at once.
    /// </summary>
    internal sealed class NumbersColumnEditor : IPawnTableColumnEditor
    {
        private const string HeaderTipKey = "Numbers_ColumnHeader_Tooltip"; // l10n-exempt: the host mod's own translation key

        private readonly Type windowType;
        private readonly MethodInfo enqueueRemoval;
        private readonly MethodInfo applyRemoval;
        private readonly MethodInfo refreshAndStore;
        private readonly PropertyInfo tableDefProperty;
        private readonly Action<int, int> reorder;

        private NumbersColumnEditor(Type windowType, MethodInfo enqueueRemoval, MethodInfo applyRemoval,
            MethodInfo refreshAndStore, PropertyInfo tableDefProperty, Action<int, int> reorder)
        {
            this.windowType = windowType;
            this.enqueueRemoval = enqueueRemoval;
            this.applyRemoval = applyRemoval;
            this.refreshAndStore = refreshAndStore;
            this.tableDefProperty = tableDefProperty;
            this.reorder = reorder;
        }

        public static void Register()
        {
            var surface = new ReflectionSurface("NumbersColumnEditor");
            Type tableType = surface.Type("Numbers.PawnTable_NumbersMain");
            Type windowType = surface.Type("Numbers.MainTabWindow_Numbers");
            Type modType = surface.Type("Numbers.Numbers");
            MethodInfo enqueue = surface.Method(tableType, "EnqueueColumnRemoval", new[] { typeof(Predicate<PawnColumnDef>) });
            MethodInfo apply = surface.Method(tableType, "ApplyColumnRemoval");
            MethodInfo refresh = surface.Method(windowType, "RefreshAndStoreSessionInWorldComp");
            PropertyInfo tableDef = surface.Property(tableType, "PawnTableDef");
            FieldInfo reorderField = surface.Field(modType, "ReorderAction");
            Action<int, int> reorder = surface.Required("Numbers.ReorderAction",
                reorderField != null ? reorderField.GetValue(null) as Action<int, int> : null);
            if (surface.Ready)
            {
                PawnTableColumnEditorRegistry.Register(tableType,
                    new NumbersColumnEditor(windowType, enqueue, apply, refresh, tableDef, reorder));
            }
        }

        public string FilterHeaderTip(PawnColumnDef def, string tip)
        {
            string instructions = CompatText.ModText(HeaderTipKey);
            int index = string.IsNullOrEmpty(instructions) ? -1 : tip.IndexOf(instructions, StringComparison.Ordinal);
            if (index < 0)
            {
                return tip;
            }
            string remaining = tip.Remove(index, instructions.Length).Trim();
            return remaining.Length > 0 ? remaining : null;
        }

        public void RemoveColumn(MainTabWindow window, PawnTable table, PawnColumnDef def)
        {
            PawnColumnWorker worker = def.Worker;
            Predicate<PawnColumnDef> match = x => ReferenceEquals(worker, x.Worker);
            enqueueRemoval.Invoke(table, new object[] { match });
            applyRemoval.Invoke(table, null);
            RefreshAndStore(window);
        }

        /// <summary>ReorderAction indexes the full column list, hidden columns included, and inserts before its target index.</summary>
        public void MoveColumn(MainTabWindow window, PawnTable table, PawnColumnDef column, PawnColumnDef neighbor, int direction)
        {
            var tableDef = tableDefProperty.GetValue(table, null) as PawnTableDef;
            List<PawnColumnDef> all = tableDef != null ? tableDef.columns : null;
            int from = all != null ? all.IndexOf(column) : -1;
            int to = all != null ? all.IndexOf(neighbor) : -1;
            if (from < 0 || to < 0)
            {
                return;
            }
            reorder(from, direction < 0 ? to : to + 1);
        }

        private void RefreshAndStore(MainTabWindow window)
        {
            if (window != null && windowType.IsInstanceOfType(window))
            {
                refreshAndStore.Invoke(window, null);
            }
        }
    }
}
