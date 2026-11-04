using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Hooks into the item specifier's own draw: the stuff search box is a raw GUI.TextField, so
    /// the live edit buffer is posted before each draw, and each section's prefix hands the scope
    /// its layout rect so the focus ring lands on the row the cursor is on.
    /// </summary>
    internal static class ClItemSpecifierDrawPatch
    {
        public static void Install(Harmony harmony)
        {
            Patch(harmony, ClCompat.ItemSpecifier.DoWindowContents, nameof(DoWindowContentsPrefix));
            Patch(harmony, ClCompat.ItemSpecifier.DrawMemberSelectorMethod, nameof(MemberSelectorPrefix));
            Patch(harmony, ClCompat.ItemSpecifier.DrawStuffListMethod, nameof(StuffListPrefix));
            Patch(harmony, ClCompat.ItemSpecifier.DrawPreviewQualityMethod, nameof(PreviewQualityPrefix));
            Patch(harmony, ClCompat.ItemSpecifier.DrawStatsMethod, nameof(StatsPrefix));
            Patch(harmony, ClCompat.ItemSpecifier.DrawSlidersMethod, nameof(SlidersPrefix));
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix)
        {
            if (target != null)
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(ClItemSpecifierDrawPatch), prefix));
            }
        }

        private static ClItemSpecifierScope ScopeOf(Window dialog)
        {
            return ScreenScopeDrawPatch.For(dialog) as ClItemSpecifierScope;
        }

        public static void DoWindowContentsPrefix(Window __instance)
        {
            if (Event.current.type != EventType.Layout)
            {
                ScopeOf(__instance)?.BeginDrawPass();
            }
        }

        public static void MemberSelectorPrefix(Window __instance, Rect titleRect)
        {
            ScopeOf(__instance)?.RecordMemberSelector(titleRect);
        }

        public static void StuffListPrefix(Window __instance, Rect outRect, ref Vector2 scrollPosition)
        {
            ScopeOf(__instance)?.RecordStuffList(outRect, scrollPosition);
        }

        public static void PreviewQualityPrefix(Window __instance, Rect rect)
        {
            ScopeOf(__instance)?.RecordPreviewQuality(rect);
        }

        public static void StatsPrefix(Window __instance, Rect rect)
        {
            ScopeOf(__instance)?.RecordStats(rect);
        }

        public static void SlidersPrefix(Window __instance, Rect rect)
        {
            ScopeOf(__instance)?.RecordSliders(rect);
        }
    }
}
