using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Stores mod settings that persist between sessions.
    /// </summary>
    public class RimWorldAccessSettings : ModSettings
    {
        /// <summary>
        /// When true, navigation wraps from end to beginning and vice versa.
        /// Default: false (stop at boundaries).
        /// </summary>
        public bool WrapNavigation = false;

        /// <summary>
        /// When true, announcements include position info like "3 of 7".
        /// Default: true.
        /// </summary>
        public bool AnnouncePosition = true;

        /// <summary>
        /// When true, pawn activity is shown when moving the map cursor.
        /// Example: "Mikaela (sleeping), 129, 114"
        /// Default: true.
        /// </summary>
        public bool ShowPawnActivityOnMap = true;

        /// <summary>
        /// When true, cover info is shown for drafted and hostile pawns.
        /// Example: "Bob, behind sandbag (good cover), melee attacking"
        /// Default: true.
        /// </summary>
        public bool ShowCoverInfo = true;

        /// <summary>
        /// When true, treeview announcements include heading level changes (e.g., "level 2").
        /// Default: true.
        /// </summary>
        public bool AnnounceLevels = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref WrapNavigation, "WrapNavigation", false);
            Scribe_Values.Look(ref AnnouncePosition, "AnnouncePosition", true);
            Scribe_Values.Look(ref ShowPawnActivityOnMap, "ShowPawnActivityOnMap", true);
            Scribe_Values.Look(ref ShowCoverInfo, "ShowCoverInfo", true);
            Scribe_Values.Look(ref AnnounceLevels, "AnnounceLevels", true);
            base.ExposeData();
        }
    }

    /// <summary>
    /// Mod class for RimWorld Access. Handles settings registration.
    /// </summary>
    public class RimWorldAccessMod_Settings : Mod
    {
        public static RimWorldAccessSettings Settings { get; private set; }

        public RimWorldAccessMod_Settings(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimWorldAccessSettings>();
            Log.Message("[RimWorld Access] Settings loaded.");
        }

        public override string SettingsCategory()
        {
            return "RimWorld Access";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Wrap navigation (loop from end to beginning)", ref Settings.WrapNavigation);
            listing.CheckboxLabeled("Announce position (e.g., '3 of 7')", ref Settings.AnnouncePosition);
            listing.CheckboxLabeled("Show pawn activity on map cursor movement", ref Settings.ShowPawnActivityOnMap);
            listing.CheckboxLabeled("Show cover info for drafted and hostile pawns", ref Settings.ShowCoverInfo);
            listing.CheckboxLabeled("Announce depth levels in treeviews (e.g., 'level 2')", ref Settings.AnnounceLevels);

            listing.End();
        }
    }
}
