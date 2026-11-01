using System;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The mouse pointer as a second map cursor: crossing onto another cell reads that cell with
    /// the grammar, the settings gates and the movement sound the keyboard cursor uses. Gated by
    /// the shared <c>HoverSpeech</c> setting (default off) and, like the widget half, it does not
    /// wait for the pointer to settle.
    ///
    /// While a designator is selected the reader also speaks the ghost the sighted player sees:
    /// the placement verdict for the hovered cell and, when it changes, the ghost's rotation. The
    /// click itself stays pure vanilla (<c>DesignatorManager.ProcessInputEvents</c>).
    ///
    /// Announce-only with one deliberate exception: while a shape is half-drawn the pointer
    /// stretches it, so the reader writes <see cref="MapNavigationState.CurrentCursorPosition"/>
    /// and drives <see cref="ShapePlacementState"/> for that case alone. It never calls
    /// <c>CameraDriver</c>, never writes <see cref="MapNavigationState.LastAnnouncedInfo"/> and
    /// never consumes the event.
    /// </summary>
    internal static class MapHoverSpeech
    {
        private static IntVec3 spokenCell = IntVec3.Invalid;
        private static int spokenMapId = -1;
        private static Designator spokenDesignator;
        private static Rot4 spokenRot = Rot4.North;
        private static bool spokenOffSurface;

        /// <summary>
        /// Called once per map OnGUI pass, after vanilla's camera work.
        /// </summary>
        internal static void Evaluate()
        {
            try
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                if (settings == null || !settings.HoverSpeech)
                {
                    Reset();
                    return;
                }
                // The several IMGUI events per frame collapse to one evaluation on Repaint.
                if (Event.current == null || Event.current.type != EventType.Repaint)
                {
                    return;
                }
                // Re-armed here rather than beside the announcement below, so a pointer that comes
                // back over a window or a piece of chrome still re-arms and a second departure is
                // announced again.
                if (PointerSurface.PointerInside)
                {
                    spokenOffSurface = false;
                }
                if (!MapScope.MapSurfaceLive())
                {
                    Reset();
                    return;
                }
                // A live menu owns the surface even when the map is drawn behind it. Placement is
                // the exception: its scope owns input without covering the map, and the pointer is
                // how a sighted player aims the ghost.
                Designator designator = ActiveDesignator();
                if (designator == null && ShellGuards.MenuOwnsInput())
                {
                    Reset();
                    return;
                }
                if (ShellGuards.NonImmediateWindowUnderPointer() || PointerOwnedByChrome())
                {
                    Reset();
                    return;
                }
                // A drag owns the speech channel for as long as it is in flight: the extent it is
                // growing is the only thing the player is asking about, and a cell read between two
                // extents buries the number they were waiting for.
                // Deliberately not a Reset: the drag ends on the cell it ends on, and forgetting it
                // would re-read that cell on top of the drag's own result.
                if (DragInFlight())
                {
                    return;
                }

                // A stationary pointer never speaks, even when the cell changes under it.
                if (!PointerMotion.MovedThisFrame())
                {
                    return;
                }
                // A pointer that has left the game's surface is not hovering anything, but the
                // camera ray still resolves it to an in-bounds cell — see PointerSurface. Say so
                // once, because the presses made out there never reach the game either, and a
                // silent reader is indistinguishable from a mod that stopped working.
                if (!PointerSurface.PointerInside)
                {
                    if (!spokenOffSurface)
                    {
                        spokenOffSurface = true;
                        TolkHelper.SpeakData("RimWorldAccess.Input.Cursor.PointerOffWindow".Loc().ToString(),
                            SpeechPriority.High);
                    }
                    Reset();
                    return;
                }

                Map map = Find.CurrentMap;
                // UI.MouseCell is patched to answer with the keyboard cursor under a dev-tool
                // override, which would make the pointer read a cell it is not over.
                IntVec3 cell = UI.UIToMapPosition(UI.MousePositionOnUI).ToIntVec3();
                if (!cell.InBounds(map))
                {
                    spokenCell = IntVec3.Invalid;
                    return;
                }
                Rot4 rot = Rot4.North;
                bool rotates = designator != null && PlacementDescriber.TryGetPlacingRot(designator, out rot);
                // Rotation is supplementary, so it rides along only when its context moved. The
                // rotate key speaks for itself; this is for the pointer re-entering a rotated ghost.
                bool rotationContextChanged = designator != spokenDesignator || rot != spokenRot;

                if (map.uniqueID == spokenMapId && cell == spokenCell && !rotationContextChanged)
                {
                    return;
                }

                TerrainAudioHelper.PlayCellAudio(cell, map, 0.5f);
                AnnouncementBuilder builder = new AnnouncementBuilder();
                if (rotates && rotationContextChanged)
                {
                    builder.Add(PlacementDescriber.DescribeRotation(designator, rot));
                }
                // A half-drawn shape is the one case where the pointer drives rather than reads:
                // it is how a sighted player stretches the rectangle, so the extent has to follow
                // the pointer and be spoken with the cell it now ends on, exactly as the arrow keys
                // do. Writing the keyboard cursor too keeps the two
                // cursors from disagreeing about where the shape ends the moment an arrow follows.
                bool drivingShape = ShapePlacementState.ShouldUpdatePreviewOnMove();
                if (drivingShape)
                {
                    MapNavigationState.CurrentCursorPosition = cell;
                }
                builder.Add(MapArrowKeyHandler.ComposePositionAnnouncement(cell, map, null, allowStateWrites: drivingShape));
                if (designator != null)
                {
                    builder.Add(PlacementDescriber.DescribeValidity(designator, cell));
                }
                string text = builder.Build();
                if (!string.IsNullOrEmpty(text))
                {
                    TolkHelper.SpeakData(text, SpeechPriority.High);
                }
                spokenCell = cell;
                spokenMapId = map.uniqueID;
                spokenDesignator = designator;
                spokenRot = rot;
                FlightRecorder.Record("map-hover", cell.ToString() + " -> " + text);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Map hover speech error", ex);
            }
        }

        /// <summary>True while vanilla's dragger is building a selection from the pointer.</summary>
        private static bool DragInFlight()
        {
            DesignatorManager manager = Find.DesignatorManager;
            return manager != null && manager.Dragger.Dragging;
        }

        /// <summary>
        /// True when the pointer rests on a piece of screen chrome the map does not own: a
        /// colonist bar entry, a command gizmo, a main button, or one of the opaque panels
        /// vanilla floats over the map for the live designator. Vanilla's own click never
        /// reaches a cell under any of them — <c>Selector</c> consults
        /// <c>ColonistOrCorpseAt</c> before the map (decompiled RimWorld/Selector.cs:519), a
        /// gizmo's or a button's <c>ButtonInvisible</c> uses the event long before
        /// <c>MapInterface.HandleMapClicks</c> runs, and a panel drawn as a window has its
        /// mouse-down taken by <c>GUI.Window</c> itself (decompiled Verse/Window.cs:202, :224).
        /// The chrome hover channel reads all of those, and a cell read on top of them buries
        /// the utterance it interrupts — so what the click would hit is what gets announced.
        /// </summary>
        internal static bool PointerOwnedByChrome()
        {
            Vector2 pos = UI.MousePositionOnUIInverted;
            if (MainButtonRectRegistry.TryHitTest(pos, out _, out _))
            {
                return true;
            }
            if (GizmoRectRegistry.TryHitTest(pos, out _, out _))
            {
                return true;
            }
            if (ShellGuards.OpaqueImmediatePanelUnderPointer())
            {
                return true;
            }
            ColonistBar bar = Find.ColonistBar;
            return bar != null && bar.ColonistOrCorpseAt(pos) != null;
        }

        /// <summary>
        /// The designator the pointer is aiming, or null. A modal surface above the map owns the
        /// pointer whatever is selected underneath it.
        /// </summary>
        private static Designator ActiveDesignator()
        {
            if (FocusStack.AnyLiveModal)
            {
                return null;
            }
            return Find.DesignatorManager != null ? Find.DesignatorManager.SelectedDesignator : null;
        }

        /// <summary>
        /// Forgets the spoken cell, so re-entering the map surface re-announces the cell under
        /// the pointer on the next movement.
        /// </summary>
        private static void Reset()
        {
            spokenDesignator = null;
            spokenRot = Rot4.North;
            spokenCell = IntVec3.Invalid;
            spokenMapId = -1;
            // spokenOffSurface is deliberately NOT cleared here: Reset runs on every pass the
            // pointer is off the surface, and clearing it would repeat the announcement each pass.
        }
    }
}
