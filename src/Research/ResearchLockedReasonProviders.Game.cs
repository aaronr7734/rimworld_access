using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Extra locked reasons a mod adds to the Research window's Start button tooltip, which
    /// <see cref="WindowlessResearchDetailHelper.BuildLockedReasons"/> cannot see because it mirrors
    /// that tooltip rather than drawing it. A provider appends whole translated sentences.
    /// </summary>
    internal static class ResearchLockedReasonProviders
    {
        private static readonly List<Action<ResearchProjectDef, List<string>>> providers =
            new List<Action<ResearchProjectDef, List<string>>>();

        public static void Register(Action<ResearchProjectDef, List<string>> provider)
        {
            providers.Add(provider);
        }

        public static void AppendTo(ResearchProjectDef project, List<string> reasons)
        {
            foreach (Action<ResearchProjectDef, List<string>> provider in providers)
            {
                try
                {
                    provider(project, reasons);
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("ResearchLockedReasonProviders", ex);
                }
            }
        }
    }
}
