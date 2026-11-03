using System;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The gate ladder every pointer-as-cursor surface (map, planet) runs before resolving what
    /// the pointer is over. Each surface supplies its own liveness, chrome and drag tests and its
    /// own Reset; the off-surface announcement is one latch shared by all of them.
    /// </summary>
    internal sealed class PointerHoverGate
    {
        private static bool spokenOffSurface;

        private readonly Func<bool> surfaceLive;
        private readonly Func<bool> pointerOverChrome;
        private readonly Func<bool> dragInFlight;
        private readonly Action reset;

        internal PointerHoverGate(Func<bool> surfaceLive, Func<bool> pointerOverChrome,
            Func<bool> dragInFlight, Action reset)
        {
            this.surfaceLive = surfaceLive;
            this.pointerOverChrome = pointerOverChrome;
            this.dragInFlight = dragInFlight;
            this.reset = reset;
        }

        /// <summary>True when the pointer moved over the live surface this pass and the caller should resolve its target.</summary>
        internal bool ShouldResolve(bool enabled)
        {
            if (!enabled)
            {
                reset();
                return false;
            }
            // The several IMGUI events per frame collapse to one evaluation on Repaint.
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return false;
            }
            // Re-armed here rather than beside the announcement below, so a pointer that comes
            // back over a window or a piece of chrome still re-arms and a second departure is
            // announced again.
            if (PointerSurface.PointerInside)
            {
                spokenOffSurface = false;
            }
            if (!surfaceLive())
            {
                reset();
                return false;
            }
            if (ShellGuards.NonImmediateWindowUnderPointer() || pointerOverChrome())
            {
                reset();
                return false;
            }
            // Deliberately not a Reset: the drag ends on the target it ends on, and forgetting it
            // would re-read that target on top of the drag's own result.
            if (dragInFlight())
            {
                return false;
            }
            // A stationary pointer never speaks, even when the target changes under it.
            if (!PointerMotion.MovedThisFrame())
            {
                return false;
            }
            // A pointer that has left the game's surface is not hovering anything, but the
            // camera ray still resolves it to an in-bounds target; see PointerSurface. Said once,
            // because presses made out there never reach the game either, and a silent reader is
            // indistinguishable from a mod that stopped working.
            if (!PointerSurface.PointerInside)
            {
                if (!spokenOffSurface)
                {
                    spokenOffSurface = true;
                    TolkHelper.SpeakData("RimWorldAccess.Input.Cursor.PointerOffWindow".Loc().ToString(),
                        SpeechPriority.High);
                }
                // Reset runs on every pass out here, so it must never clear the latch.
                reset();
                return false;
            }
            return true;
        }
    }
}
