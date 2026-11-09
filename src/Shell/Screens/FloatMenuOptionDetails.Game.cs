using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compat-registered <see cref="IFloatMenuOptionDetailSource"/>s, consulted by both float-menu
    /// readers after the option's own tooltip and info subject come up empty. Sources run per
    /// keystroke, so a throwing source declines instead of breaking the menu.
    /// </summary>
    internal static class FloatMenuOptionDetails
    {
        private static readonly List<IFloatMenuOptionDetailSource> sources = new List<IFloatMenuOptionDetailSource>();

        internal static void Register(IFloatMenuOptionDetailSource source)
        {
            if (source != null && !sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        internal static string ExtrasFor(FloatMenuOption option)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                try
                {
                    string extras = sources[i].ExtrasFor(option);
                    if (!string.IsNullOrEmpty(extras))
                    {
                        return extras;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Float menu option details (extras)", ex);
                }
            }
            return null;
        }

        internal static Def InfoDefFor(FloatMenuOption option)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                try
                {
                    Def def = sources[i].InfoDefFor(option);
                    if (def != null)
                    {
                        return def;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Float menu option details (info card)", ex);
                }
            }
            return null;
        }
    }
}
