using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reports a Royalty noble's throne-room and bedroom title requirements for a room. The unmet
    /// list comes from <see cref="Pawn_RoyaltyTracker"/>'s own enumerators, the source of the
    /// undignified-room alerts and thoughts, so ascetics, previous-title carry-over and invalid
    /// throne rooms resolve exactly as the game resolves them.
    /// </summary>
    public static class RoomRequirementsHelper
    {
        public static string GetTitleRequirementsInfo(Room room)
        {
            if (!ModsConfig.RoyaltyActive || room == null || !room.ProperRoom)
                return null;

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);
            AppendThroneRequirements(room, builder);
            AppendBedroomRequirements(room, builder);

            string result = builder.Build();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        private static void AppendThroneRequirements(Room room, AnnouncementBuilder builder)
        {
            foreach (Thing thing in room.ContainedAndAdjacentThings)
            {
                if (!(thing is Building_Throne throne) || throne.GetRoom() != room)
                    continue;

                Pawn noble = throne.AssignedPawn;
                if (noble?.royalty == null || noble.ownership?.AssignedThrone != throne)
                    continue;

                RoyalTitle title = noble.royalty.HighestTitleWithThroneRoomRequirements();
                if (title == null)
                    continue;

                builder.Add("RimWorldAccess.Map.Tile.Room.Req.ThroneHeader".Translate(
                    noble.LabelShortCap, title.def.GetLabelCapFor(noble)));

                string invalidReason = RoomRoleWorker_ThroneRoom.Validate(room);
                if (invalidReason != null)
                    builder.Add(invalidReason);

                AppendStatus(title.def.throneRoomRequirements.Count,
                    noble.royalty.GetUnmetThroneroomRequirements().ToList(), builder);
                return;
            }
        }

        private static void AppendBedroomRequirements(Room room, AnnouncementBuilder builder)
        {
            foreach (Pawn owner in room.Owners)
            {
                if (owner?.royalty == null || owner.ownership?.OwnedRoom != room
                    || !owner.royalty.HasPersonalBedroom())
                    continue;

                RoyalTitle title = owner.royalty.HighestTitleWithBedroomRequirements();
                IEnumerable<RoomRequirement> requirements = title?.def.GetBedroomRequirements(owner);
                if (requirements == null)
                    continue;

                builder.Add("RimWorldAccess.Map.Tile.Room.Req.BedroomHeader".Translate(
                    owner.LabelShortCap, title.def.GetLabelCapFor(owner)));
                AppendStatus(requirements.Count(),
                    owner.royalty.GetUnmetBedroomRequirements().ToList(), builder);
                return;
            }
        }

        // Vanilla counts a precept-disabled requirement as met (MetOrDisabled), so the total is
        // the full list and met is whatever the unmet enumerator did not yield.
        private static void AppendStatus(int total, List<string> unmet, AnnouncementBuilder builder)
        {
            if (unmet.Count == 0)
            {
                builder.Add("RimWorldAccess.Map.Tile.Room.Req.AllMet".Translate());
                return;
            }

            builder.Add("RimWorldAccess.Map.Tile.Room.Req.Summary".Translate(
                System.Math.Max(0, total - unmet.Count), total));
            builder.Add("RimWorldAccess.Map.Tile.Room.Req.Missing".Translate(
                unmet.ToCommaList()));
        }
    }
}
