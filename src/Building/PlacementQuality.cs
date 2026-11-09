namespace RimWorldAccess
{
    /// <summary>
    /// How well a building would work at one cell and facing, as a <see cref="PlacementUsefulnessRegistry"/>
    /// rule judges it: a score in [0, 1], where 0 means it would not work at all, and an optional
    /// localized phrase saying why.
    /// </summary>
    internal readonly struct PlacementQuality
    {
        public static readonly PlacementQuality Full = new PlacementQuality(PlacementSpotRanking.FullQuality, null);

        public readonly float Score;
        public readonly string Phrase;

        public PlacementQuality(float score, string phrase)
        {
            Score = score;
            Phrase = phrase;
        }
    }
}
