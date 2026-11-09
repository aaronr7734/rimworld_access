using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers the inspection-tree tab adapters for Vanilla Expanded Framework's PipeSystem
    /// processor tab and animal Genes tab.
    /// </summary>
    internal static class VefProcessorTabCompat
    {
        public static void RegisterTabAdapters()
        {
            CompatRegistration.TabAdapter("PipeSystem.ITab_Processor",
                t =>
                {
                    var adapter = new VefProcessorTabAdapter();
                    adapter.sharedTab = InspectTabManager.GetSharedInstance(t);
                    return adapter;
                },
                "VEF processor tab compat");

            CompatRegistration.TabAdapter("VEF.AnimalGenes.ITab_AnimalGenes",
                t => new VefAnimalGenesTabAdapter(t),
                "VEF animal genes tab compat");
        }
    }
}
