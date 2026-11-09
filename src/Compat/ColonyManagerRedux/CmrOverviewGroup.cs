using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>One of the Overview tab's own job groups; a null <see cref="Header"/> is the ungrouped "all" group, drawn with no header row.</summary>
    internal sealed class CmrOverviewGroup
    {
        public readonly string Key;
        public readonly string Header;
        public readonly List<object> Jobs;

        /// <summary>The mod's own typed job list, handed back to its generic group helpers.</summary>
        public readonly object RawJobs;

        public CmrOverviewGroup(string key, string header, List<object> jobs, object rawJobs)
        {
            Key = key ?? "";
            Header = header;
            Jobs = jobs;
            RawJobs = rawJobs;
        }
    }
}
