using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper for building pawn labels, especially for grouped pawns (animals with numerical names).
    /// Used across multiple modules: World (caravans), TransportPods, Trade.
    /// </summary>
    public static class PawnLabelHelper
    {
        /// <summary>
        /// Builds a label for grouped pawns (animals with numerical names).
        /// Includes gender, life stage, and pregnancy status to distinguish between groups.
        /// Format: "[Gender] [kind]" with optional suffixes like "(juvenile)" or "(pregnant)".
        /// Examples: "Male Ducklings", "Female Chickens (juvenile)", "Female Cows (pregnant)"
        /// </summary>
        /// <param name="pawn">A representative pawn from the group</param>
        /// <param name="count">Total count in the group (used for pluralization)</param>
        /// <returns>A descriptive label for the group</returns>
        public static string BuildGroupedPawnLabel(Pawn pawn, int count)
        {
            // Get the base kind label and pluralize
            string kindLabel = pawn.KindLabel ?? pawn.def.label;
            if (count > 1)
            {
                kindLabel = Find.ActiveLanguageWorker.Pluralize(kindLabel, count);
            }

            // Build the label with gender prefix
            string genderPrefix = "";
            if (pawn.gender == Gender.Male)
            {
                genderPrefix = "RimWorldAccess.Pawns.Group.MalePrefix".Translate();
            }
            else if (pawn.gender == Gender.Female)
            {
                genderPrefix = "RimWorldAccess.Pawns.Group.FemalePrefix".Translate();
            }

            // Collect suffixes (life stage, pregnancy)
            var suffixes = new System.Collections.Generic.List<string>();

            // Add life stage if not the final/adult stage
            if (pawn.ageTracker?.CurLifeStage != null)
            {
                var lifeStages = pawn.RaceProps?.lifeStageAges;
                if (lifeStages != null && lifeStages.Count > 1)
                {
                    // Only add life stage if not the last (adult) stage
                    int currentStageIndex = pawn.ageTracker.CurLifeStageIndex;
                    if (currentStageIndex < lifeStages.Count - 1)
                    {
                        string stageLabel = pawn.ageTracker.CurLifeStage.label;
                        if (!string.IsNullOrEmpty(stageLabel))
                        {
                            suffixes.Add(stageLabel);
                        }
                    }
                }
            }

            // Add pregnancy status if pregnant
            if (pawn.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) == true)
            {
                suffixes.Add("RimWorldAccess.Pawns.Group.Pregnant".Translate());
            }

            // Build the final label
            string suffix = suffixes.Count > 0
                ? "RimWorldAccess.Pawns.Group.SuffixWrap".Translate(string.Join(", ", suffixes)).ToString()
                : "";
            return "RimWorldAccess.Pawns.Group.Label".Translate(genderPrefix, kindLabel.CapitalizeFirst(), suffix);
        }
    }
}
