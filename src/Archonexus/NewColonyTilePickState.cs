using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard support for the Archonexus "choose new colony site" world-tile
    /// pick (MoveColonyUtility.PickNewColonyTile → Find.TilePicker.StartTargeting
    /// with allowEscape: false). Navigation comes from WorldNavigationState as
    /// usual; this state announces the pick, gates Enter through the game's own
    /// TilePicker validator + tileChosen callbacks (so an invalid tile is rejected
    /// by the vanilla rules), and explains on Escape that the player cannot back
    /// out of this stage of the quest.
    /// </summary>
    public static class NewColonyTilePickState
    {
        public static bool IsActive { get; private set; }

        private static FieldInfo validatorField;
        private static FieldInfo tileChosenField;

        public static void Open()
        {
            if (IsActive) return;
            IsActive = true;
            TolkHelper.Speak(
                "ChooseNextColonySite".Translate()
                + ". Use arrow keys to navigate, PageUp and PageDown for the scanner, Enter to confirm. You cannot cancel — you must choose a valid tile.",
                SpeechPriority.High);
        }

        public static void Close()
        {
            IsActive = false;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

            // If the TilePicker stopped (tile chosen elsewhere, or quest cancelled
            // by some other path), tear down our state.
            if (Find.TilePicker == null || !Find.TilePicker.Active)
            {
                Close();
                return false;
            }

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                ConfirmCurrentTile();
                return true;
            }

            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                // PickNewColonyTile sets allowEscape: false; the player genuinely
                // cannot cancel. Make that audible instead of silent.
                TolkHelper.Speak("You must choose a valid tile to continue.", SpeechPriority.High);
                return true;
            }

            return false;
        }

        private static void ConfirmCurrentTile()
        {
            if (!WorldNavigationState.IsActive)
            {
                TolkHelper.Speak("World navigation not active", SpeechPriority.High);
                return;
            }

            PlanetTile tile = WorldNavigationState.CurrentSelectedTile;
            if (!tile.Valid)
            {
                WorldObject selected = Find.WorldSelector?.SingleSelectedObject;
                if (selected != null && selected.Tile.Valid)
                    tile = selected.Tile;
                else
                {
                    TolkHelper.Speak("No valid tile selected", SpeechPriority.High);
                    return;
                }
            }

            try
            {
                if (validatorField == null) validatorField = AccessTools.Field(typeof(TilePicker), "validator");
                if (tileChosenField == null) tileChosenField = AccessTools.Field(typeof(TilePicker), "tileChosen");

                var validator = validatorField?.GetValue(Find.TilePicker) as Func<PlanetTile, bool>;
                var tileChosen = tileChosenField?.GetValue(Find.TilePicker) as Action<PlanetTile>;
                if (validator == null || tileChosen == null)
                {
                    TolkHelper.Speak("Cannot access tile picker callbacks", SpeechPriority.High);
                    return;
                }

                // Validator runs TileFinder.IsValidTileForNewSettlement; on failure
                // it raises a Messages.Message which the mod already announces.
                if (!validator(tile))
                    return;

                var stopIntMethod = AccessTools.Method(typeof(TilePicker), "StopTargetingInt");
                stopIntMethod?.Invoke(Find.TilePicker, null);

                tileChosen(tile);

                // Tile committed; the picker is done. Close now so the state doesn't linger into
                // the reform-ideoligion screen or the freshly loaded map (where it would otherwise
                // only self-clear on the next keystroke).
                Close();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Error confirming new colony tile: {ex}");
                TolkHelper.Speak("Error selecting tile", SpeechPriority.High);
            }
        }
    }

    /// <summary>
    /// Activates NewColonyTilePickState whenever the game opens the Archonexus
    /// new-colony tile picker. Patching MoveColonyUtility.PickNewColonyTile as a
    /// Postfix is the cleanest entry point: it runs immediately after
    /// Find.TilePicker.StartTargeting returns, so the picker is already Active
    /// and uniquely identifies this caller (we don't have to sniff labels).
    /// </summary>
    [HarmonyPatch(typeof(MoveColonyUtility), nameof(MoveColonyUtility.PickNewColonyTile))]
    public static class NewColonyTilePickPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            NewColonyTilePickState.Open();
        }
    }
}
