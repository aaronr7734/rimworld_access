using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Records each candidate pawn portrait of the <see cref="Dialog_BeginLordJob"/> family
    /// (ritual, psychic ritual, gravship launch, and any modded subclass) for
    /// <see cref="LordJobDialogScope"/>'s focus ring. Every portrait in the widget's grid goes
    /// through one vanilla method carrying both the pawn and its rect —
    /// <c>PawnRoleSelectionWidgetBase&lt;RoleType&gt;.DrawPawnPortraitInternal</c> (decompiled
    /// RimWorld/PawnRoleSelectionWidgetBase.cs:431) — which is all a ring needs.
    /// </summary>
    internal static class RitualPawnRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        internal static void Record(Pawn pawn, Rect tileRect)
        {
            try
            {
                if (!LordJobDialogState.IsActive || pawn == null)
                {
                    return;
                }
                Rows.Record(pawn, tileRect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Ritual pawn row capture error", ex);
            }
        }
    }

    /// <summary>
    /// The one non-generic call inside PawnRoleSelectionWidgetBase&lt;RoleType&gt;.DrawPawnPortraitInternal.
    /// The tile is the portrait plus the name strip beneath it, which is iconSize tall (both are
    /// 20 times the widget's scale).
    /// </summary>
    [HarmonyPatch(typeof(PawnPortraitIconsDrawer), nameof(PawnPortraitIconsDrawer.DrawPawnPortraitIcons))]
    internal static class PawnPortraitDrawPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Rect portraitRect, Pawn p, float iconSize)
        {
            RitualPawnRowDrawPatch.Record(p, new Rect(portraitRect.x, portraitRect.y, portraitRect.width, portraitRect.height + iconSize));
        }
    }
}
