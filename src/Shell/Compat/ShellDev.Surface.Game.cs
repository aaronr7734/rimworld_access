#if DEBUG
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's screen-dump surface: reads a live scope's whole navigable surface without moving its
    /// cursor or speaking, via
    /// <see cref="FocusScope.DebugDescribeSurface"/>/<see cref="ScreenScope.DebugDescribeSurface"/>.
    /// </summary>
    public static partial class ShellDev
    {
        /// <summary>The top of the focus stack's full surface dump. See <see cref="DumpScreenAt"/> to look beneath an overlay.</summary>
        public static string DumpScreen()
        {
            return DumpScreenAt(0);
        }

        /// <summary>Full surface dump of the scope <paramref name="depthFromTop"/> layers below the top (0 = the top itself).</summary>
        public static string DumpScreenAt(int depthFromTop)
        {
            System.Collections.Generic.IReadOnlyList<FocusScope> scopes = FocusStack.ScopesBottomUp;
            int count = scopes.Count;
            int index = count - 1 - depthFromTop;
            if (index < 0 || index >= count)
            {
                string outOfRange = "(no scope at depth " + depthFromTop + "; stack depth " + count + ")";
                return outOfRange;
            }
            FocusScope scope = scopes[index];
            var sb = new StringBuilder();
            sb.Append("scope=").Append(scope.Name)
              .Append(" depth=").Append(depthFromTop)
              .Append(" stackDepth=").Append(count)
              .Append('\n').Append(scope.DebugDescribeSurface());
            return sb.ToString();
        }

        /// <summary>The raw capture rows, in draw order, behind a generic window scope's <see cref="DumpScreenAt"/> rows.</summary>
        public static string DumpCaptureRows(int depthFromTop = 0)
        {
            System.Collections.Generic.IReadOnlyList<FocusScope> scopes = FocusStack.ScopesBottomUp;
            int index = scopes.Count - 1 - depthFromTop;
            if (index < 0 || index >= scopes.Count)
            {
                string outOfRange = "(no scope at depth " + depthFromTop + "; stack depth " + scopes.Count + ")";
                return outOfRange;
            }
            GenericWindowScope generic = scopes[index] as GenericWindowScope;
            if (generic == null)
            {
                return "(scope " + scopes[index].Name + " is not a generic window scope)";
            }
            return generic.DebugDescribeCaptureRows();
        }

        /// <summary>
        /// The focused scope's current-row composed text alone, or a fallback line when the top
        /// scope isn't a <see cref="ScreenScope"/> (no row model) or the stack is empty. Shared by
        /// <see cref="InjectText"/>'s trailing "what does the row say now" line.
        /// </summary>
        private static string DescribeFocusedRow()
        {
            FocusScope top = FocusStack.Top;
            if (top == null)
            {
                string noScope = "(no scope focused)";
                return noScope;
            }
            ScreenScope screenScope = top as ScreenScope;
            if (screenScope == null)
            {
                return "(scope " + top.Name + " has no row model)";
            }
            return screenScope.DebugDescribeCurrentRow();
        }
    }
}
#endif
