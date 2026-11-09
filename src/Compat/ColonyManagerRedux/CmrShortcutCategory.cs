using System;

namespace RimWorldAccess.Shell
{
    /// <summary>One of the mod's own <c>ShortcutCategory&lt;T&gt;</c> records, its keys already resolved.</summary>
    internal sealed class CmrShortcutCategory<T>
    {
        public readonly string Label;
        public readonly string Tip;
        public readonly Func<T, bool> Predicate;

        public CmrShortcutCategory(string label, string tip, Func<T, bool> predicate)
        {
            Label = label ?? "";
            Tip = tip;
            Predicate = predicate;
        }
    }
}
