using System;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class BookmarkHelper
    {
        private static int lastPeekedSlot = -1;
        private static float lastPeekTime = -1f;
        private const float DoubleTapThreshold = 0.5f;

        public static void PeekOrJumpToBookmark(int slot)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (lastPeekedSlot == slot && now - lastPeekTime <= DoubleTapThreshold)
            {
                lastPeekedSlot = -1;
                lastPeekTime = -1f;
                JumpToBookmark(slot);
            }
            else
            {
                lastPeekedSlot = slot;
                lastPeekTime = now;
                PeekAtBookmark(slot);
            }
        }

        public static void SetBookmark(int slot)
        {
            var component = GetComponent();
            if (component == null)
                return;

            var map = Find.CurrentMap;
            var position = MapNavigationState.CurrentCursorPosition;
            component.SetBookmark(slot, position);

            string tileSummary = TileInfoHelper.GetTileSummary(position, map);
            TolkHelper.Speak($"Bookmark {slot} set: {tileSummary}");
        }

        public static void JumpToBookmark(int slot)
        {
            var component = GetComponent();
            if (component == null)
                return;

            if (!component.IsBookmarkSet(slot))
            {
                TolkHelper.Speak($"Bookmark {slot} not set");
                return;
            }

            var map = Find.CurrentMap;
            var bookmarkPos = component.GetBookmark(slot);

            if (!bookmarkPos.InBounds(map))
            {
                TolkHelper.Speak($"Bookmark {slot} invalid");
                return;
            }

            MapNavigationState.CurrentCursorPosition = bookmarkPos;
            Find.CameraDriver.JumpToCurrentMapLoc(bookmarkPos);
            MapNavigationState.CurrentCameraMode = CameraFollowMode.Cursor;

            // Play terrain audio feedback (matches arrow key movement behavior)
            TerrainDef terrain = bookmarkPos.GetTerrain(map);
            TerrainAudioHelper.PlayTerrainAudio(terrain, 0.5f);

            // Reset last announced info so AnnouncePosition always speaks
            MapNavigationState.LastAnnouncedInfo = "";
            MapArrowKeyHandler.AnnouncePosition(bookmarkPos, map);
        }

        public static void PeekAtBookmark(int slot)
        {
            var component = GetComponent();
            if (component == null)
                return;

            if (!component.IsBookmarkSet(slot))
            {
                TolkHelper.Speak($"Bookmark {slot} not set");
                return;
            }

            var map = Find.CurrentMap;
            var bookmarkPos = component.GetBookmark(slot);

            if (!bookmarkPos.InBounds(map))
            {
                TolkHelper.Speak($"Bookmark {slot} invalid");
                return;
            }

            // Pan camera without moving cursor
            Find.CameraDriver.JumpToCurrentMapLoc(bookmarkPos);

            var cursorPos = MapNavigationState.CurrentCursorPosition;
            string tileSummary = TileInfoHelper.GetTileSummary(bookmarkPos, map);

            float distance = (bookmarkPos - cursorPos).LengthHorizontal;
            if (distance < 0.5f)
            {
                TolkHelper.Speak($"{tileSummary}, here");
            }
            else
            {
                string direction = GetDirection(cursorPos, bookmarkPos);
                TolkHelper.Speak($"{tileSummary}, {distance:F0} tiles {direction}");
            }
        }

        private static string GetDirection(IntVec3 from, IntVec3 to)
        {
            IntVec3 offset = to - from;

            double angle = Math.Atan2(offset.x, offset.z) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            if (angle >= 337.5 || angle < 22.5) return "North";
            if (angle >= 22.5 && angle < 67.5) return "Northeast";
            if (angle >= 67.5 && angle < 112.5) return "East";
            if (angle >= 112.5 && angle < 157.5) return "Southeast";
            if (angle >= 157.5 && angle < 202.5) return "South";
            if (angle >= 202.5 && angle < 247.5) return "Southwest";
            if (angle >= 247.5 && angle < 292.5) return "West";
            return "Northwest";
        }

        private static BookmarkMapComponent GetComponent()
        {
            return Find.CurrentMap?.GetComponent<BookmarkMapComponent>();
        }
    }
}
