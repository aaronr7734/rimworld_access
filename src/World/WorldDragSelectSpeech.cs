using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks the extent of the world drag-select box while the button is held — the only thing a
    /// sighted player is shown during that gesture (<c>WorldInterface.WorldInterfaceOnGUI</c>
    /// calls <c>WorldDragBox.DragBoxOnGUI</c>, which draws a rectangle and nothing else).
    ///
    /// The world box lives in UI pixels rather than tiles (<c>WorldDragBox</c> is built from
    /// <c>UI.MousePositionOnUIInverted</c>), so the map reader's "width by height in cells" has no
    /// equivalent here. The extent is spoken as a share of the screen, which is the same rectangle
    /// in a unit that does not change meaning with resolution or UI scale.
    ///
    /// Deliberately no live count of the contents, for the same reason as
    /// <see cref="MapDragSelectSpeech"/>: <c>WorldSelector.SelectInsideDragBox</c> is a category
    /// cascade (colonist-bar caravans, then map colonists, then world objects narrowed to
    /// caravans and then to player caravans, then the tile under the mouse), so a mid-drag count
    /// would not be what the release selects.
    ///
    /// Announce-only, and not gated by the hover-speech setting, like the map half.
    /// </summary>
    internal static class WorldDragSelectSpeech
    {
        private static bool spokenValid;
        private static int spokenWidth;
        private static int spokenHeight;

        /// <summary>Called once per world interface OnGUI pass.</summary>
        internal static void Evaluate()
        {
            try
            {
                if (Event.current == null || Event.current.type != EventType.Repaint)
                {
                    return;
                }
                WorldSelector selector = Find.WorldSelector;
                WorldDragBox box = selector != null ? selector.dragBox : null;
                if (box == null || !box.IsValidAndActive || !WorldScope.WorldSurfaceLive()
                    || UI.screenWidth <= 0 || UI.screenHeight <= 0)
                {
                    spokenValid = false;
                    return;
                }

                int width = Mathf.RoundToInt((box.RightX - box.LeftX) / UI.screenWidth * 100f);
                int height = Mathf.RoundToInt((box.TopZ - box.BotZ) / UI.screenHeight * 100f);
                if (spokenValid && width == spokenWidth && height == spokenHeight)
                {
                    return;
                }
                spokenValid = true;
                spokenWidth = width;
                spokenHeight = height;

                TolkHelper.Speak(
                    "RimWorldAccess.World.Drag.SelectExtent".Loc(width, height), SpeechPriority.High);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("World drag select speech error", ex);
            }
        }
    }

    /// <summary>
    /// Drives <see cref="WorldDragSelectSpeech"/> and <see cref="WorldHoverSpeech"/> off the pass that draws the box, in both UI roots.
    /// Observation only: it consumes nothing.
    /// </summary>
    [HarmonyPatch(typeof(WorldInterface), "WorldInterfaceOnGUI")]
    internal static class WorldDragSelectSpeechPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            WorldDragSelectSpeech.Evaluate();
            WorldHoverSpeech.Evaluate();
        }
    }
}
