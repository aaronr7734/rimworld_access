using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class CoverHelper
    {
        public static bool ShouldShowCover(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
                return false;

            // Drafted player pawns
            if (pawn.Faction == Faction.OfPlayer && pawn.Drafted)
                return true;

            // Hostile faction pawns
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
                return true;

            return false;
        }

        public static string GetCoverInfo(Pawn pawn)
        {
            if (!ShouldShowCover(pawn))
                return null;

            Map map = pawn.Map;
            IntVec3 pos = pawn.Position;

            Thing bestCover = null;
            float bestBlockChance = 0f;

            for (int i = 0; i < 8; i++)
            {
                IntVec3 adjCell = pos + GenAdj.AdjacentCells[i];
                if (!adjCell.InBounds(map))
                    continue;

                Thing cover = adjCell.GetCover(map);
                if (cover == null)
                    continue;

                float blockChance = cover.BaseBlockChance();
                if (blockChance > bestBlockChance)
                {
                    bestBlockChance = blockChance;
                    bestCover = cover;
                }
            }

            if (bestCover == null)
                return "NoCoverLower".Translate();

            return $"behind {bestCover.def.label} (blocks {bestBlockChance.ToStringPercent()})";
        }
    }
}
