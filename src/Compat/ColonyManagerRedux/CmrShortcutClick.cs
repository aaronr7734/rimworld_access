namespace RimWorldAccess.Shell
{
    /// <summary>
    /// How a shortcut row press combines the row's options with the current selection, one per
    /// member of the mod's own <c>ManagerTab.ShortcutClickMode</c>, matched by name.
    /// </summary>
    internal enum CmrShortcutClick
    {
        /// <summary>Plain Enter: allows the row's options unless all are allowed, then disallows them.</summary>
        Toggle,

        /// <summary>Allows the row's options and leaves everything else as it is.</summary>
        Add,

        /// <summary>Disallows the row's options and leaves everything else as it is.</summary>
        Remove,

        /// <summary>Allows the row's options and disallows every other option.</summary>
        Restrict,
    }
}
