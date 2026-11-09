using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Character Development's Characters main tab, registered by
    /// <see cref="WqModule"/>; all reflection lives in <see cref="WqCompat"/>. Its center panel
    /// is a physics canvas of reward bubbles drawn with raw textures and clicked through raw
    /// mouse events, which no capture can see, so this scope presents those nodes as rows. Enter
    /// claims through the tab's own gated <c>ClaimReward</c>, which speaks its rejects and opens
    /// the recipient-picker dialog. Regions: information, rewards, characters (Enter jumps to
    /// the pawn and opens the mod's inspect tab). The <c>pawnSpecificRewardPoints</c> setting,
    /// ON by default, drops the global points tracker and colony reward count, puts point counts
    /// on the character rows, admits a pawn holding points but no wants, and gates a claim on
    /// the recipients' own points.
    /// </summary>
    internal sealed class WqCharactersTabScope : ScreenScope
    {
        private const int InfoRegion = 0;
        private const int RewardsRegion = 1;
        private const int PawnsRegion = 2;

        private readonly Window window;
        private readonly List<string> infoRows = new List<string>();
        private readonly List<object> rewardNodes = new List<object>();
        private readonly List<Pawn> characterRows = new List<Pawn>();
        private readonly HashSet<object> newRewards = new HashSet<object>();
        private bool pawnSpecific;
        private bool anyRecipientHasPoints;

        public WqCharactersTabScope(Window w)
        {
            window = w;
        }

        public override string Name => "wq-characters-tab";

        protected internal override Window OwnedWindow => window;

        protected override bool EnableTypeahead => true;

        /// <summary>The tab draws no ButtonText of its own (only dev-mode icon buttons).</summary>
        protected override bool CaptureWindowButtons => false;

        protected override string ComposeOpenAnnouncement()
        {
            if (pawnSpecific)
            {
                return null;
            }
            return CompatText.JoinSentences(new List<string>
            {
                CompatText.ModText("WQ_CharacterPoints"),
                "RimWorldAccess.Compat.Wq.PointsProgress".Translate(
                    WqCompat.CharacterPoints(), WqCompat.GlobalPointsNeeded()),
                CompatText.ModArgs("WQ_AvailableRewards", WqCompat.RewardPoints()),
            });
        }

        protected override void RefreshContent()
        {
            pawnSpecific = WqCompat.PawnSpecificRewardPoints();

            rewardNodes.Clear();
            rewardNodes.AddRange(WqCompat.RewardNodes());
            for (int i = 0; i < rewardNodes.Count; i++)
            {
                if (WqCompat.NodeIsNew(rewardNodes[i]))
                {
                    newRewards.Add(rewardNodes[i]);
                }
            }

            infoRows.Clear();
            if (!pawnSpecific)
            {
                infoRows.Add("RimWorldAccess.Compat.Wq.PointsProgress".Translate(
                    WqCompat.CharacterPoints(), WqCompat.GlobalPointsNeeded()));
                infoRows.Add(CompatText.Flatten(CompatText.ModText("WQ_ProgressBarDesc")));
                infoRows.Add(CompatText.ModArgs("WQ_AvailableRewards", WqCompat.RewardPoints()));
            }
            infoRows.Add(CompatText.Flatten(CompatText.ModText("WQ_ClaimInstruction")));
            infoRows.Add(CompatText.Flatten(CompatText.ModText("WQ_QuirksInfoText")));

            characterRows.Clear();
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Pawn> pawns = maps[i].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int j = 0; j < pawns.Count; j++)
                {
                    Pawn pawn = pawns[j];
                    if (!WqCompat.CanHaveWants(pawn))
                    {
                        continue;
                    }
                    if (WqCompat.ActiveWantCount(pawn) > 0
                        || (pawnSpecific && WqCompat.PawnRewardPoints(pawn) > 0))
                    {
                        characterRows.Add(pawn);
                    }
                }
            }

            anyRecipientHasPoints = pawnSpecific && AnyRecipientHasPoints();
        }

        /// <summary><c>ClaimReward</c>'s first gate; its pool spans caravans, not just the list.</summary>
        private static bool AnyRecipientHasPoints()
        {
            List<Pawn> candidates = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (WqCompat.CanHaveWants(candidates[i]) && WqCompat.PawnRewardPoints(candidates[i]) > 0)
                {
                    return true;
                }
            }
            return false;
        }

        protected override int ContentRegionCount => 3;

        protected override string ContentRegionName(int region)
        {
            switch (region)
            {
                case InfoRegion:
                    return pawnSpecific
                        ? "RimWorldAccess.Compat.Wq.InfoRegion".Translate().ToString()
                        : CompatText.ModText("WQ_CharacterPoints");
                case RewardsRegion:
                    return "RimWorldAccess.Compat.Wq.RewardsRegion".Translate();
                default:
                    return CompatText.ModText("WQ_CharactersWithWants");
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (region)
            {
                case InfoRegion:
                    return infoRows.Count;
                case RewardsRegion:
                    return rewardNodes.Count;
                default:
                    return characterRows.Count;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == InfoRegion)
            {
                d.ReadOnly = true;
                if (index >= 0 && index < infoRows.Count)
                {
                    d.Label = infoRows[index];
                }
                return d;
            }

            if (region == RewardsRegion)
            {
                if (index < 0 || index >= rewardNodes.Count)
                {
                    return d;
                }
                object node = rewardNodes[index];
                d.Label = WqCompat.NodeLabel(node);
                d.Role = ElementRole.Button;
                d.Value = RarityWord(WqCompat.NodeRarity(node));
                if (newRewards.Contains(node))
                {
                    d.Value = CompatText.JoinSentences(new List<string> { d.Value, "RimWorldAccess.Compat.Wq.New".Translate() });
                }
                d.Extras = CompatText.Flatten(WqCompat.NodeDescription(node));
                string refusal = ClaimRefusal();
                if (refusal != null)
                {
                    d.Disabled = true;
                    d.Extras = CompatText.JoinSentences(new List<string> { refusal, d.Extras });
                }
                return d;
            }

            if (index < 0 || index >= characterRows.Count)
            {
                return d;
            }
            Pawn pawn = characterRows[index];
            d.Label = pawn.LabelShortCap;
            d.Role = ElementRole.Button;
            d.Value = pawnSpecific
                ? CompatText.JoinSentences(new List<string>
                {
                    CompatText.ModArgs("WQ_WantsCount", WqCompat.ActiveWantCount(pawn)),
                    CompatText.ModArgs("WQ_PawnRewardPoints", WqCompat.PawnRewardPoints(pawn)),
                })
                : CompatText.ModArgs("WQ_WantsCount", WqCompat.ActiveWantCount(pawn));
            d.Hint = "RimWorldAccess.Compat.Wq.PawnRowHint".Translate();
            return d;
        }

        /// <summary>The tab's own points gate, in whichever mode it is running.</summary>
        private string ClaimRefusal()
        {
            if (pawnSpecific)
            {
                return anyRecipientHasPoints ? null : CompatText.ModText("WQ_NoPawnRewardPoints");
            }
            return WqCompat.RewardPoints() > 0 ? null : CompatText.ModText("WQ_NotEnoughRewardPoints");
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == RewardsRegion)
            {
                if (index < 0 || index >= rewardNodes.Count)
                {
                    return;
                }
                // The tab's own gated claim: its reject messages speak on failure; on success
                // the recipient dialog opens and this list regenerates.
                WqCompat.ClaimReward(window, rewardNodes[index]);
                RefreshModel();
                return;
            }
            if (region == PawnsRegion && index >= 0 && index < characterRows.Count)
            {
                Pawn pawn = characterRows[index];
                // The tab's own row click, verbatim (MainTabWindow_Characters.cs:324-325). OpenTab
                // switches the main tab itself and routes into the windowless tree through
                // InspectTabOpenBridgePatch, so this needs no shell-specific step of its own.
                CameraJumper.TryJumpAndSelect(pawn);
                InspectPaneUtility.OpenTab(WqCompat.InspectTabType);
            }
        }

        private static string RarityWord(int rarity)
        {
            switch (rarity)
            {
                case 3:
                    return "RimWorldAccess.Compat.Wq.RarityLegendary".Translate();
                case 2:
                    return "RimWorldAccess.Compat.Wq.RarityRare".Translate();
                case 1:
                    return "RimWorldAccess.Compat.Wq.RarityUncommon".Translate();
                default:
                    return "RimWorldAccess.Compat.Wq.RarityCommon".Translate();
            }
        }
    }
}
