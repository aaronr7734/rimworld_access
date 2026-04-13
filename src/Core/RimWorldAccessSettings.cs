using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Stores mod settings that persist between sessions.
    /// </summary>
    public class RimWorldAccessSettings : ModSettings
    {
        public bool WrapNavigation = false;
        public bool AnnouncePosition = true;
        public bool ShowPawnActivityOnMap = true;
        public bool ShowCoverInfo = true;
        public bool AnnounceLevels = true;
        public bool SubmenuTreeNavigation = false;

        // ===== Sound Effects =====
        public bool EnableSoundEffects = true;

        // Footsteps module
        public bool FootstepsEnabled = true;
        public bool FootstepTerrainVariation = true;
        public bool FootstepZoomScaling = true;
        public bool FootstepPerformanceMode = false;

        // Per-category footstep state, keyed by FootstepAudioCategory.ToString().
        // Enabled: is the category producing sound at all (and tracked at all).
        // Volume: current slider value when enabled (0..2 range).
        // LastVolume: remembered volume so toggling off/on restores the prior value.
        public Dictionary<string, bool> FootstepCategoryEnabled = new Dictionary<string, bool>();
        public Dictionary<string, float> FootstepCategoryVolume = new Dictionary<string, float>();
        public Dictionary<string, float> FootstepCategoryLastVolume = new Dictionary<string, float>();

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
            Scribe_Values.Look(ref FootstepTerrainVariation, "FootstepTerrainVariation", true);
            Scribe_Values.Look(ref FootstepZoomScaling, "FootstepZoomScaling", true);
            Scribe_Values.Look(ref FootstepPerformanceMode, "FootstepPerformanceMode", false);

            Scribe_Collections.Look(ref FootstepCategoryEnabled, "FootstepCategoryEnabled", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref FootstepCategoryVolume, "FootstepCategoryVolume", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref FootstepCategoryLastVolume, "FootstepCategoryLastVolume", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (FootstepCategoryEnabled == null) FootstepCategoryEnabled = new Dictionary<string, bool>();
                if (FootstepCategoryVolume == null) FootstepCategoryVolume = new Dictionary<string, float>();
                if (FootstepCategoryLastVolume == null) FootstepCategoryLastVolume = new Dictionary<string, float>();
            }

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
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 500f);
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

            listing.Label("Granular footstep controls live in the in-game Options screen under RimWorld Access > Sound Effects.");

            listing.CheckboxLabeled(
                "Enable Sound Effects",
                ref Settings.EnableSoundEffects,
                "Master toggle for custom sound effects added by RimWorld Access. When off, only vanilla RimWorld sounds play.");

            bool prevGuiEnabled = GUI.enabled;
            GUI.enabled = prevGuiEnabled && Settings.EnableSoundEffects;

            listing.CheckboxLabeled("  Enable footstep audio", ref Settings.FootstepsEnabled,
                "Play footstep sounds when pawns move.");
            listing.CheckboxLabeled("  Terrain variation", ref Settings.FootstepTerrainVariation,
                "Play different sounds based on terrain type.");
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
