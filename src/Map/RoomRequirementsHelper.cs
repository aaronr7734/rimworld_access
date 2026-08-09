using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reports a Royalty noble's throne-room / bedroom title requirements for a room, with a
    /// met / missing status for each one. Surfaces the same information a sighted player reads
    /// off the throne's requirement checklist, which is otherwise invisible to a screen reader.
    ///
    /// The requirements come straight from the game: <see cref="RoomRequirement.LabelCap"/>
    /// already produces a localized label that includes the numeric progress (e.g. "Brazier
    /// 0 / 2", "Room size: 84 / 24 cells"), and <see cref="RoomRequirement.Met"/> is the game's
    /// own test, so the readout always matches what the game itself considers satisfied. We only
    /// add the localized framing around that list.
    ///
    /// Which title's requirements apply is decided by the game too:
    /// <c>HighestTitleWithThroneRoomRequirements</c> / <c>HighestTitleWithBedroomRequirements</c>
    /// return the title whose requirements are in effect (and null when the noble's title carries
    /// none, e.g. Freeholder or Yeoman), so those rooms produce no readout at all.
    ///
    /// All of these are first-party RimWorld types compiled into the base assembly, so they are
    /// referenced directly; the whole feature is gated on <see cref="ModsConfig.RoyaltyActive"/>
    /// so it stays inert when the Royalty DLC is not installed.
    /// </summary>
    public static class RoomRequirementsHelper
    {
        /// <summary>
        /// Returns a localized met / missing summary of the title requirements that apply to
        /// <paramref name="room"/>, or null when none do (no Royalty DLC, not a proper room, or
        /// no titled noble whose throne or bed is here). Appended to the room-stats readout.
        /// </summary>
        public static string GetTitleRequirementsInfo(Room room)
        {
            if (!ModsConfig.RoyaltyActive)
                return null;

            if (room == null || !room.ProperRoom)
                return null;

            var builder = new AnnouncementBuilder().DefaultSep(Separator.Period);

            AppendThroneRequirements(room, builder);
            AppendBedroomRequirements(room, builder);

            string result = builder.Build();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        /// <summary>
        /// If a throne in the room is assigned to a noble whose title has throne-room
        /// requirements, appends that noble's requirement summary.
        /// </summary>
        private static void AppendThroneRequirements(Room room, AnnouncementBuilder builder)
        {
            foreach (Thing thing in room.ContainedAndAdjacentThings)
            {
                if (!(thing is Building_Throne throne) || throne.GetRoom() != room)
                    continue;

                Pawn noble = throne.AssignedPawn;
                if (noble?.royalty == null)
                    continue;

                RoyalTitle title = noble.royalty.HighestTitleWithThroneRoomRequirements();
                if (title?.def?.throneRoomRequirements == null)
                    continue;

                AppendRequirementBlock(
                    "RimWorldAccess.Map.Tile.Room.Req.ThroneHeader",
                    noble, title, title.def.throneRoomRequirements, room, builder);
                return;
            }
        }

        /// <summary>
        /// If an owner of a bed in the room holds a title with bedroom requirements, appends
        /// that owner's requirement summary.
        /// </summary>
        private static void AppendBedroomRequirements(Room room, AnnouncementBuilder builder)
        {
            foreach (Pawn owner in room.Owners)
            {
                if (owner?.royalty == null)
                    continue;

                RoyalTitle title = owner.royalty.HighestTitleWithBedroomRequirements();
                if (title?.def?.bedroomRequirements == null)
                    continue;

                AppendRequirementBlock(
                    "RimWorldAccess.Map.Tile.Room.Req.BedroomHeader",
                    owner, title, title.def.bedroomRequirements, room, builder);
                return;
            }
        }

        /// <summary>
        /// Builds one requirement block: a header naming the noble and title, then either
        /// "all requirements met" or a met count followed by the list of missing requirements.
        /// Precept-disabled requirements are skipped entirely, matching the game's own display.
        /// </summary>
        private static void AppendRequirementBlock(string headerKey, Pawn pawn, RoyalTitle title,
            List<RoomRequirement> requirements, Room room, AnnouncementBuilder builder)
        {
            string titleLabel = title.def.GetLabelCapFor(pawn);
            builder.Add(headerKey.Translate(pawn.LabelShortCap, titleLabel));

            int total = 0;
            int met = 0;
            var missing = new List<string>();

            foreach (RoomRequirement req in requirements)
            {
                if (req.Disabled(room, pawn))
                    continue;

                total++;
                if (req.Met(room, pawn))
                    met++;
                else
                    missing.Add(req.LabelCap(room));
            }

            if (total == 0)
                return;

            if (missing.Count == 0)
            {
                builder.Add("RimWorldAccess.Map.Tile.Room.Req.AllMet".Translate());
                return;
            }

            builder.Add("RimWorldAccess.Map.Tile.Room.Req.Summary".Translate(met, total));
            builder.Add("RimWorldAccess.Map.Tile.Room.Req.Missing".Translate(string.Join("; ", missing)));
        }
    }
}
