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
        /// When true, terrain names are spoken during map navigation
        /// (arrow keys, scanner Home jump, bookmark jumps, Go To coordinate).
        /// The terrain sound effect plays independently of this setting.
        /// Default: true.
        /// </summary>
        public bool AnnounceTerrain = true;

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

        /// <summary>
        /// When true, treeviews use submenu-style navigation where expanded parents
        /// are hidden and only their children are shown in the navigation list.
        /// Default: false (standard treeview navigation).
        /// </summary>
        public bool SubmenuTreeNavigation = false;

        /// <summary>
        /// Which work menu view F1 opens by default. Ctrl+Tab (Option+Tab on macOS)
        /// in either view switches and updates this setting so the chosen view is remembered.
        /// </summary>
        public WorkMenuView DefaultWorkMenuView = WorkMenuView.Focused;

        /// <summary>
        /// When true, announces messages when the game forces Normal speed due to threats
        /// ("Game slowed down by presence of threat." / "Threat passed. Game speed resumed.").
        /// Default: false (silent).
        /// </summary>
        public bool AnnounceForcedSlowdowns = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref WrapNavigation, "WrapNavigation", false);
            Scribe_Values.Look(ref AnnouncePosition, "AnnouncePosition", true);
            Scribe_Values.Look(ref ShowPawnActivityOnMap, "ShowPawnActivityOnMap", true);
            Scribe_Values.Look(ref ShowCoverInfo, "ShowCoverInfo", true);
            Scribe_Values.Look(ref AnnounceLevels, "AnnounceLevels", true);
            Scribe_Values.Look(ref SubmenuTreeNavigation, "SubmenuTreeNavigation", false);
            Scribe_Values.Look(ref AnnounceTerrain, "AnnounceTerrain", true);
            Scribe_Values.Look(ref DefaultWorkMenuView, "DefaultWorkMenuView", WorkMenuView.Focused);
            Scribe_Values.Look(ref AnnounceForcedSlowdowns, "AnnounceForcedSlowdowns", false);
            base.ExposeData();
        }
    }

    /// <summary>
    /// Which work menu layout F1 opens by default.
    /// Focused: priority-grouped per-pawn view (default; lower-verbosity).
    /// Table: pawn rows by work-type columns (mirrors vanilla; for power users).
    /// </summary>
    public enum WorkMenuView
    {
        Focused,
        Table
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
            return "RimWorldAccess.Core.Settings.Category".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.WrapNavigation.Label".Translate(), ref Settings.WrapNavigation);
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.AnnouncePosition.Label".Translate(), ref Settings.AnnouncePosition);
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.ShowPawnActivityOnMap.Label".Translate(), ref Settings.ShowPawnActivityOnMap);
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.ShowCoverInfo.Label".Translate(), ref Settings.ShowCoverInfo);
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.AnnounceTerrain.Label".Translate(), ref Settings.AnnounceTerrain);
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.AnnounceLevels.Label".Translate(), ref Settings.AnnounceLevels);
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.SubmenuTreeNavigation.Label".Translate(), ref Settings.SubmenuTreeNavigation,
                "RimWorldAccess.Core.Settings.SubmenuTreeNavigation.Tooltip".Translate());

            listing.End();
        }
    }
}
