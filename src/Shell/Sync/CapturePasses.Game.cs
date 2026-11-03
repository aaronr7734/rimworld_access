using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared lifecycle for the capture passes a scope brackets to its window's own draw.
    ///
    /// A window can close itself mid-body (OK, or Enter on a captured button), popping its scope
    /// before the bracket's finalizer runs, so Finish always closes the passes and
    /// runs the scope's per-pass work only if the scope is still live.
    ///
    /// <see cref="EndLeaked"/> is the backstop when even the finalizer never ran. A live pass opens
    /// and closes within one draw, so one still open on a later frame proves its EndPass was lost.
    /// Frame inequality is the only signal: a same-frame heuristic would misfire on a pass still
    /// legitimately mid-flight. A leaked pass records everything drawn anywhere and can grow its
    /// list to millions of rows, hence <see cref="Release{T}"/>'s TrimExcess.
    /// </summary>
    internal static class CapturePasses
    {
        /// <summary>Runs once per dispatcher pass, before any scope reads a capture.</summary>
        internal static void EndLeaked()
        {
            WidgetCapture.EndLeakedLivePass();
            ButtonTextCapture.EndLeakedPass();
            TextFieldCapture.EndLeakedPass();
        }

        internal static bool Leaked(bool passOpen, int passOpenFrame)
        {
            return passOpen && Time.frameCount != passOpenFrame;
        }

        internal static void Release<T>(List<T> items)
        {
            items.Clear();
            items.TrimExcess();
        }

        /// <summary>
        /// Call from the bracket's Harmony finalizer with the scope its prefix carried in
        /// <c>__state</c>. <paramref name="live"/> is evaluated before the passes close.
        /// </summary>
        internal static void Finish<T>(T scope, bool live, Exception bodyException,
            Action endPasses, Action<T> onPass, string errorContext)
        {
            try
            {
                endPasses();
                if (live && bodyException == null)
                {
                    onPass(scope);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError(errorContext, ex);
            }
        }

        internal static void Finish<T>(T scope, Rect rect, bool live, Exception bodyException,
            Action endPasses, Action<T, Rect> onPass, string errorContext)
        {
            try
            {
                endPasses();
                if (live && bodyException == null)
                {
                    onPass(scope, rect);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError(errorContext, ex);
            }
        }
    }
}
