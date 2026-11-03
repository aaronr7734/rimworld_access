using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Swaps vanilla's mouse instructions in a stat explanation for their keyboard equivalent,
    /// so the on-screen text stays vanilla's. The source sentence is read from the live def, in
    /// the player's language, never matched as a literal.
    /// </summary>
    internal static class SpokenHintRewrites
    {
        internal static string Apply(string explanation)
        {
            // Def descriptions carry their translation only once loading has finished.
            if (string.IsNullOrEmpty(explanation) || !PlayDataLoader.Loaded)
            {
                return explanation;
            }
            string hitChanceMouseHint = CutMouseHint(DefDatabase<StatDef>.GetNamedSilentFail("ShootingAccuracyPawn")?.description);
            if (hitChanceMouseHint == null || explanation.IndexOf(hitChanceMouseHint, System.StringComparison.Ordinal) < 0)
            {
                return explanation;
            }
            string chord = ActionChords.Display("map.tileInfo.itemsAndPawns");
            return chord == null
                ? explanation
                : explanation.Replace(hitChanceMouseHint, "RimWorldAccess.Stat.ShootingAccuracy.HitChanceHint".Translate(chord));
        }

        /// <summary>ShootingAccuracyPawn's last paragraph is the mouse-over sentence on its own; the turret stat's description repeats it.</summary>
        private static string CutMouseHint(string description)
        {
            int lastBreak = description?.LastIndexOf("\n\n", System.StringComparison.Ordinal) ?? -1;
            return lastBreak < 0 ? null : description.Substring(lastBreak + 2).Trim();
        }
    }
}
