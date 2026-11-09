using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Mod windows that switch pages with a row of plain text buttons sized to their captions,
    /// drawing the current page's button inactive. Geometry cannot tell that row from a toolbar
    /// with one greyed action, so <see cref="GenericWindowScope"/> promotes it to a tab strip only
    /// when a compat module registers the window type with a reader of the current page's caption,
    /// taken from the mod's own page field.
    /// </summary>
    public static class WindowPageButtonRegistry
    {
        private static readonly Dictionary<Type, Func<Window, string>> readers = new Dictionary<Type, Func<Window, string>>();

        /// <summary><paramref name="currentCaption"/> returns the drawn caption of the page showing, or null when unknown.</summary>
        public static void Register(Type windowType, Func<Window, string> currentCaption)
        {
            readers[windowType] = currentCaption;
        }

        /// <summary>The current page's caption for a registered window, else null.</summary>
        public static string CurrentCaption(Window window)
        {
            Func<Window, string> reader;
            if (window == null || !readers.TryGetValue(window.GetType(), out reader))
            {
                return null;
            }
            try
            {
                return reader(window);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Window page-button reader", ex);
                return null;
            }
        }
    }
}
