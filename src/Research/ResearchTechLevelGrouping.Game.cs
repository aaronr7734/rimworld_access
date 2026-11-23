namespace RimWorldAccess
{
    /// <summary>
    /// Tech-level grouping in the research menu. Vanilla never advances the player's tech level,
    /// so the layer is offered only by compat modules for mods where finishing a level matters;
    /// the setting row appears only once offered.
    /// </summary>
    internal static class ResearchTechLevelGrouping
    {
        public static bool Offered { get; private set; }

        public static void Offer()
        {
            Offered = true;
        }

        public static bool Active =>
            Offered && (RimWorldAccessMod_Settings.Settings?.GroupResearchByTechLevel ?? true);
    }
}
