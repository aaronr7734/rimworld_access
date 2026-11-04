namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The action inventory: every keyboard shortcut the mod handles, registered with its
    /// current keys as defaults. Parts 1–6 are ordered by the legacy dispatch priority
    /// (highest first); parts 7–8 cover actions registered per screen family (Part7:
    /// in-game/map/building; Part8: pre-game flow and Ideology builder — see each file's own
    /// header for the exact boundary). The shared menu grammar (arrows, Enter, Escape,
    /// Home/End, typeahead backspace) registers once as Menus-category actions via
    /// <see cref="SharedMenuGrammar"/> — never per-screen.
    ///
    /// Scopes dispatch the grammar and their own screen actions from the registry. This
    /// inventory is the single registration point, the collision test bed, and later the
    /// rebind UI's catalog.
    /// </summary>
    public static class ShellActionInventory
    {
        public static void RegisterAll(ActionCatalog catalog)
        {
            SharedMenuGrammar.Register(catalog);
            ShellActionInventoryPart1.Register(catalog);
            ShellActionInventoryPart2.Register(catalog);
            ShellActionInventoryPart3.Register(catalog);
            ShellActionInventoryPart4.Register(catalog);
            ShellActionInventoryPart5.Register(catalog);
            ShellActionInventoryPart6.Register(catalog);
            ShellActionInventoryPart7.Register(catalog);
            ShellActionInventoryPart8.Register(catalog);
            ShellActionInventoryCompositableLoadouts.Register(catalog);
        }
    }
}
