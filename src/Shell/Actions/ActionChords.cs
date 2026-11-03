namespace RimWorldAccess.Shell
{
    internal static class ActionChords
    {
        /// <summary>The player's current chord for an action id, spoken-form ("Alt+S"), or null when unbound.</summary>
        internal static string Display(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return null;
            InputAction action;
            if (!ActionRegistry.Catalog.TryGet(actionId, out action) || action.Bindings.Count == 0)
                return null;
            return action.Bindings[0].DisplayLabel;
        }
    }
}
