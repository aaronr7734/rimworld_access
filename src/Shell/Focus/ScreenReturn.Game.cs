using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One return point: a screen that closed itself to hand the player to a pawn records how to
    /// reopen itself and where its cursor was, and Escape on the destination brings it back.
    ///
    /// The destination decides the point's lifetime. <see cref="Binding.Inspection"/> lives as long
    /// as the windowless inspection it opened: only its Escape (<see cref="WindowlessInspectionState.ClosePanel"/>)
    /// returns, and any other close discards. <see cref="Binding.Map"/> lands on the bare map and
    /// lives for exactly one key: Escape returns ahead of the pause menu, and any other key or a
    /// click discards it. Either kind dies when the current map changes and at a session boundary.
    ///
    /// The reopened screen collects its cursor by name with <see cref="TryTakeCursor{T}"/> when it
    /// next pushes or focuses; an uncollected cursor expires at the next key.
    /// </summary>
    internal static class ScreenReturn
    {
        internal enum Binding
        {
            Inspection,
            Map,
        }

        internal sealed class Point
        {
            public readonly Binding Binding;
            public readonly string Screen;
            public readonly object Cursor;
            public readonly Action Reopen;
            public readonly Map Map;

            public Point(Binding binding, string screen, object cursor, Action reopen, Map map)
            {
                Binding = binding;
                Screen = screen;
                Cursor = cursor;
                Reopen = reopen;
                Map = map;
            }
        }

        private static Point pending;
        private static string cursorScreen;
        private static object cursor;

        internal static void Arm(Binding binding, string screen, object cursor, Action reopen)
        {
            pending = Find.CurrentMap != null ? new Point(binding, screen, cursor, reopen, Find.CurrentMap) : null;
        }

        /// <summary>A main-tab pawn table jumped to <paramref name="pawn"/> on the map; Escape reopens the tab on the same cell.</summary>
        internal static void ArmMainTabTable(string screen, string mainTabDefName, Pawn pawn, TableModel table)
        {
            Arm(Binding.Map, screen, new TableReturnCursor(pawn, table != null ? table.ColumnIndex : -1),
                () => MainTabWindowLink.EnsureTabOpen(mainTabDefName));
        }

        /// <summary>
        /// A main-tab pawn table's jump to <paramref name="pawn"/>: closes the table, moves the map
        /// cursor and camera there, speaks its name (<paramref name="spokenName"/>, else the short
        /// name or the kind), and arms the return.
        /// </summary>
        internal static void JumpToPawn(string screen, string mainTabDefName, TableModel table, Action close,
            Pawn pawn, string notOnMapKey, Func<Pawn, string> spokenName = null)
        {
            if (pawn == null || pawn.Map == null)
            {
                TolkHelper.Speak(notOnMapKey.Loc(), SpeechPriority.High);
                return;
            }
            IntVec3 position = pawn.Position;
            ArmMainTabTable(screen, mainTabDefName, pawn, table);
            close();
            MapNavigationState.CurrentCursorPosition = position;
            Find.CameraDriver?.JumpToCurrentMapLoc(position);
            MapNavigationState.SpeakJumpedTo(spokenName != null ? spokenName(pawn)
                : pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString());
        }

        /// <summary>Detaches the pending point of <paramref name="binding"/>, or null when there is none.</summary>
        internal static Point Take(Binding binding)
        {
            Point point = pending;
            if (point == null || point.Binding != binding)
            {
                return null;
            }
            pending = null;
            return point;
        }

        /// <summary>Puts a point back after its destination changed shape without being dismissed.</summary>
        internal static void Restore(Point point)
        {
            if (point != null)
            {
                pending = point;
            }
        }

        internal static void Discard(Binding binding)
        {
            if (pending != null && pending.Binding == binding)
            {
                pending = null;
            }
        }

        /// <summary>
        /// Escape's half: reopens the point's screen with its cursor queued, and stamps the cancel
        /// so the same Escape cannot close what just opened. False when there is nothing to return to.
        /// </summary>
        internal static bool ReturnOnCancel(Point point)
        {
            if (point == null || point.Map != Find.CurrentMap)
            {
                return false;
            }
            cursorScreen = point.Screen;
            cursor = point.Cursor;
            point.Reopen();
            ShellFrameStamps.MarkCancelConsumed();
            return true;
        }

        internal static bool TryTakeCursor<T>(string screen, out T value) where T : class
        {
            value = null;
            if (cursorScreen != screen)
            {
                return false;
            }
            value = cursor as T;
            cursorScreen = null;
            cursor = null;
            return value != null;
        }

        /// <summary>Lands a reopened pawn table on its saved cell, when <paramref name="screen"/> has one queued.</summary>
        internal static void LandTableCursor(string screen, TableModel table, IList<Pawn> rows)
        {
            if (TryTakeCursor(screen, out TableReturnCursor saved))
            {
                saved.LandOn(table, rows);
            }
        }

        /// <summary>The map point a key is about to meet, read before the key dispatches.</summary>
        internal static Point BeforeKey()
        {
            cursorScreen = null;
            cursor = null;
            return pending != null && pending.Binding == Binding.Map ? pending : null;
        }

        /// <summary>A map point the key neither used nor replaced is spent.</summary>
        internal static void AfterKey(Point before)
        {
            if (before != null && ReferenceEquals(pending, before))
            {
                pending = null;
            }
        }

        internal static void Reset()
        {
            pending = null;
            cursorScreen = null;
            cursor = null;
        }
    }

    /// <summary>A tree screen's cursor: its old tree, expansion flags intact, and the row index into it.</summary>
    internal sealed class TreeReturnCursor
    {
        public readonly InspectionTreeItem Root;
        public readonly int Index;

        public TreeReturnCursor(InspectionTreeItem root, int index)
        {
            Root = root;
            Index = index;
        }
    }

    /// <summary>A list screen's cursor: the row's item, and its old position for when the item is gone.</summary>
    internal sealed class ListReturnCursor
    {
        public readonly object Item;
        public readonly int Index;

        public ListReturnCursor(object item, int index)
        {
            Item = item;
            Index = index;
        }
    }

    /// <summary>A pawn table's cursor: the row's pawn and the column, so a re-sorted roster still finds the row.</summary>
    internal sealed class TableReturnCursor
    {
        public readonly Pawn Pawn;
        public readonly int Column;

        public TableReturnCursor(Pawn pawn, int column)
        {
            Pawn = pawn;
            Column = column;
        }

        /// <summary>Lands <paramref name="table"/> on the pawn's row (row 0 is the header) and the column; false when the pawn is gone.</summary>
        internal bool LandOn(TableModel table, IList<Pawn> rows)
        {
            int row = table != null && rows != null ? rows.IndexOf(Pawn) : -1;
            if (row < 0)
            {
                return false;
            }
            table.Rows.MoveTo(row + 1);
            if (Column >= 0 && Column < table.ColumnCount)
            {
                table.MoveToColumn(Column);
            }
            return true;
        }
    }
}
