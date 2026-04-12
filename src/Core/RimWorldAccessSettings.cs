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

        /// <summary>
        /// When true, treeviews use submenu-style navigation where expanded parents
        /// are hidden and only their children are shown in the navigation list.
        /// Default: false (standard treeview navigation).
        /// </summary>
        public bool SubmenuTreeNavigation = false;

        // ===== Sound Effects section =====

        /// <summary>
        /// Master toggle for all custom sound effects added by RimWorld Access
        /// (footsteps, and future eating/sleeping sounds). When false, vanilla
        /// sound experience is preserved.
        /// Default: true.
        /// </summary>
        public bool EnableSoundEffects = true;

        // ----- Footsteps module -----
        public bool FootstepsEnabled = true;
        public float FootstepHumanVolume = 1f;
        public float FootstepAnimalVolume = 1f;
        public float FootstepMechVolume = 1f;
        public bool FootstepTerrainVariation = true;
        public bool FootstepStereoPan = true;
        public bool FootstepZoomScaling = true;
        public bool FootstepPerformanceMode = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref WrapNavigation, "WrapNavigation", false);
            Scribe_Values.Look(ref AnnouncePosition, "AnnouncePosition", true);
            Scribe_Values.Look(ref ShowPawnActivityOnMap, "ShowPawnActivityOnMap", true);
            Scribe_Values.Look(ref ShowCoverInfo, "ShowCoverInfo", true);
            Scribe_Values.Look(ref AnnounceLevels, "AnnounceLevels", true);
            Scribe_Values.Look(ref SubmenuTreeNavigation, "SubmenuTreeNavigation", false);
            Scribe_Values.Look(ref EnableSoundEffects, "EnableSoundEffects", true);
            Scribe_Values.Look(ref FootstepsEnabled, "FootstepsEnabled", true);
            Scribe_Values.Look(ref FootstepHumanVolume, "FootstepHumanVolume", 1f);
            Scribe_Values.Look(ref FootstepAnimalVolume, "FootstepAnimalVolume", 1f);
            Scribe_Values.Look(ref FootstepMechVolume, "FootstepMechVolume", 1f);
            Scribe_Values.Look(ref FootstepTerrainVariation, "FootstepTerrainVariation", true);
            Scribe_Values.Look(ref FootstepStereoPan, "FootstepStereoPan", true);
            Scribe_Values.Look(ref FootstepZoomScaling, "FootstepZoomScaling", true);
            Scribe_Values.Look(ref FootstepPerformanceMode, "FootstepPerformanceMode", false);
            base.ExposeData();
        }
    }

    /// <summary>
    /// Mod class for RimWorld Access. Handles settings registration.
    /// </summary>
    public class RimWorldAccessMod_Settings : Mod
    {
        public static RimWorldAccessSettings Settings { get; private set; }
        private static Vector2 settingsScrollPosition;

        public RimWorldAccessMod_Settings(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimWorldAccessSettings>();
            Log.Message("[RimWorld Access] Settings loaded.");
            FootstepSoundBank.Reset();
        }

        public override string SettingsCategory()
        {
            return "RimWorld Access";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 700f);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled("Wrap navigation (loop from end to beginning)", ref Settings.WrapNavigation);
            listing.CheckboxLabeled("Announce position (e.g., '3 of 7')", ref Settings.AnnouncePosition);
            listing.CheckboxLabeled("Show pawn activity on map cursor movement", ref Settings.ShowPawnActivityOnMap);
            listing.CheckboxLabeled("Show cover info for drafted and hostile pawns", ref Settings.ShowCoverInfo);
            listing.CheckboxLabeled("Announce depth levels in treeviews (e.g., 'level 2')", ref Settings.AnnounceLevels);
            listing.CheckboxLabeled("Rashad Hates Treeviews (submenu-style navigation)", ref Settings.SubmenuTreeNavigation,
                "Changes how treeviews work. When you expand a category, it disappears and you navigate only its items. Press Left Arrow to go back. Your position is remembered when you return.");

            listing.GapLine();
            Text.Font = GameFont.Medium;
            listing.Label("Sound Effects");
            Text.Font = GameFont.Small;

            listing.CheckboxLabeled(
                "Enable Sound Effects",
                ref Settings.EnableSoundEffects,
                "Master toggle for custom sound effects added by RimWorld Access. When off, only vanilla RimWorld sounds play.");

            bool prevGuiEnabled = GUI.enabled;
            GUI.enabled = prevGuiEnabled && Settings.EnableSoundEffects;

            listing.Gap(6f);
            listing.Label("  Footsteps");

            listing.CheckboxLabeled("  Enable footstep audio", ref Settings.FootstepsEnabled,
                "Play footstep sounds when pawns move.");

            listing.Label($"  Human volume: {(Settings.FootstepHumanVolume * 100f):F0}%");
            Settings.FootstepHumanVolume = Widgets.HorizontalSlider(
                listing.GetRect(22f), Settings.FootstepHumanVolume, 0f, 2f);

            listing.Label($"  Animal volume: {(Settings.FootstepAnimalVolume * 100f):F0}%");
            Settings.FootstepAnimalVolume = Widgets.HorizontalSlider(
                listing.GetRect(22f), Settings.FootstepAnimalVolume, 0f, 2f);

            listing.Label($"  Mechanoid volume: {(Settings.FootstepMechVolume * 100f):F0}%");
            Settings.FootstepMechVolume = Widgets.HorizontalSlider(
                listing.GetRect(22f), Settings.FootstepMechVolume, 0f, 2f);

            listing.CheckboxLabeled("  Terrain variation", ref Settings.FootstepTerrainVariation,
                "Play different sounds based on terrain type.");
            listing.CheckboxLabeled("  Stereo panning", ref Settings.FootstepStereoPan,
                "Pan footsteps left and right based on the pawn's screen position.");
            listing.CheckboxLabeled("  Wall occlusion", ref Settings.FootstepZoomScaling,
                "Muffle footsteps behind walls, fading with zoom so distant pawns become audible as you zoom out. Off = all footsteps audible regardless of walls.");
            listing.CheckboxLabeled("  Performance mode", ref Settings.FootstepPerformanceMode,
                "Limit footsteps to about 18 highest-priority pawns per tick.");

            GUI.enabled = prevGuiEnabled;

            listing.End();
            Widgets.EndScrollView();
        }
    }
}
