using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Swaps vanilla's mouse instructions for their keyboard equivalent in speech only, so the
    /// on-screen text stays vanilla's. The source sentence is read from the live def, in the
    /// player's language, never matched as a literal.
    /// </summary>
    internal static class SpokenHintRewrites
    {
        private static bool built;
        private static string hitChanceMouseHint;
        private static string hitChanceKeyboardHint;

        internal static string Apply(string text)
        {
            if (!built)
            {
                Build();
            }
            if (hitChanceMouseHint == null || text.IndexOf(hitChanceMouseHint, System.StringComparison.Ordinal) < 0)
            {
                return text;
            }
            return text.Replace(hitChanceMouseHint, hitChanceKeyboardHint);
        }

        private static void Build()
        {
            // Def descriptions carry their translation only once loading has finished, and the
            // key catalog exists only after bootstrap; until both hold, try again next utterance.
            if (!PlayDataLoader.Loaded
                || !ActionRegistry.Catalog.TryGet("map.tileInfo.itemsAndPawns", out InputAction action)
                || action.Bindings.Count == 0)
            {
                return;
            }
            built = true;
            // ShootingAccuracyPawn's last paragraph is the mouse-over sentence on its own; the
            // turret stat's description repeats it.
            string description = DefDatabase<StatDef>.GetNamedSilentFail("ShootingAccuracyPawn")?.description;
            int lastBreak = description?.LastIndexOf("\n\n", System.StringComparison.Ordinal) ?? -1;
            if (lastBreak < 0)
            {
                return;
            }
            hitChanceMouseHint = description.Substring(lastBreak + 2).Trim();
            hitChanceKeyboardHint = "RimWorldAccess.Stat.ShootingAccuracy.HitChanceHint".Translate(action.Bindings[0].DisplayLabel);
        }
    }
}
