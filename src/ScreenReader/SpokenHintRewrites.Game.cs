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
        // A language change reloads the defs, so a new description string re-cuts the hint.
        private static string cutFromDescription;
        private static string hitChanceMouseHint;

        internal static string Apply(string text)
        {
            // Def descriptions carry their translation only once loading has finished.
            if (!PlayDataLoader.Loaded)
            {
                return text;
            }
            string description = DefDatabase<StatDef>.GetNamedSilentFail("ShootingAccuracyPawn")?.description;
            if (!ReferenceEquals(description, cutFromDescription))
            {
                cutFromDescription = description;
                hitChanceMouseHint = CutMouseHint(description);
            }
            if (hitChanceMouseHint == null || text.IndexOf(hitChanceMouseHint, System.StringComparison.Ordinal) < 0
                || !ActionRegistry.Catalog.TryGet("map.tileInfo.itemsAndPawns", out InputAction action)
                || action.Bindings.Count == 0)
            {
                return text;
            }
            return text.Replace(hitChanceMouseHint,
                "RimWorldAccess.Stat.ShootingAccuracy.HitChanceHint".Translate(action.Bindings[0].DisplayLabel));
        }

        /// <summary>ShootingAccuracyPawn's last paragraph is the mouse-over sentence on its own; the turret stat's description repeats it.</summary>
        private static string CutMouseHint(string description)
        {
            int lastBreak = description?.LastIndexOf("\n\n", System.StringComparison.Ordinal) ?? -1;
            return lastBreak < 0 ? null : description.Substring(lastBreak + 2).Trim();
        }
    }
}
