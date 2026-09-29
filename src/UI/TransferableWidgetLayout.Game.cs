using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The rows a <see cref="TransferableOneWayWidget"/> actually draws, and the scroll that keeps
    /// one of them on screen. Rows come from each section's cachedTransferables, the list
    /// FillMainRect iterates, so vanilla's search box and sort dropdowns are already applied.
    /// The row geometry mirrors FillMainRect: rows start at 6, each is 30 tall, and a titled
    /// non-empty section spends 30 on its ListSeparator (3 + 20 + 2) plus the 5 after it.
    /// </summary>
    [HarmonyPatch(typeof(TransferableOneWayWidget), "FillMainRect")]
    internal static class TransferableWidgetLayout
    {
        private const float FirstRowY = 6f;
        private const float RowHeight = 30f;
        private const float SectionTitleHeight = 30f;

        internal readonly struct DrawnRow
        {
            public readonly int Section;
            public readonly string SectionTitle;
            public readonly TransferableOneWay Transferable;

            public DrawnRow(int section, string sectionTitle, TransferableOneWay transferable)
            {
                Section = section;
                SectionTitle = sectionTitle;
                Transferable = transferable;
            }
        }

        private sealed class ViewportHeight
        {
            public float Value;
        }

        private static readonly ConditionalWeakTable<TransferableOneWayWidget, ViewportHeight> viewportHeights =
            new ConditionalWeakTable<TransferableOneWayWidget, ViewportHeight>();

        private static readonly FieldInfo sectionsField = AccessTools.Field(typeof(TransferableOneWayWidget), "sections");
        private static readonly Type sectionType =
            sectionsField != null && sectionsField.FieldType.IsGenericType ? sectionsField.FieldType.GetGenericArguments()[0] : null;
        private static readonly FieldInfo titleField = sectionType?.GetField("title");
        private static readonly FieldInfo cachedField = sectionType?.GetField("cachedTransferables");
        private static readonly AccessTools.FieldRef<TransferableOneWayWidget, bool> cachedFlag =
            AccessTools.FieldRefAccess<TransferableOneWayWidget, bool>("transferablesCached");
        private static readonly AccessTools.FieldRef<TransferableOneWayWidget, Vector2> scrollPosition =
            AccessTools.FieldRefAccess<TransferableOneWayWidget, Vector2>("scrollPosition");
        private static readonly MethodInfo cacheMethod = AccessTools.Method(typeof(TransferableOneWayWidget), "CacheTransferables");

        [HarmonyPrefix]
        internal static void Prefix(TransferableOneWayWidget __instance, Rect mainRect)
        {
            viewportHeights.GetOrCreateValue(__instance).Value = mainRect.height;
        }

        /// <summary>The widget's rows in draw order, or null when the widget is null or unreadable.</summary>
        internal static List<DrawnRow> Rows(TransferableOneWayWidget widget)
        {
            if (widget == null || sectionsField == null || titleField == null || cachedField == null)
            {
                return null;
            }
            IEnumerable sections;
            try
            {
                // A tab never drawn yet has not cached; vanilla's own OnGUI does this same fill first.
                if (!cachedFlag(widget))
                {
                    cacheMethod?.Invoke(widget, null);
                }
                sections = sectionsField.GetValue(widget) as IEnumerable;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Transferable widget rows unreadable", ex);
                return null;
            }
            if (sections == null)
            {
                return null;
            }
            var rows = new List<DrawnRow>();
            int sectionIndex = -1;
            foreach (object section in sections)
            {
                sectionIndex++;
                string title = titleField.GetValue(section) as string;
                if (!(cachedField.GetValue(section) is List<TransferableOneWay> cached))
                {
                    continue;
                }
                for (int i = 0; i < cached.Count; i++)
                {
                    rows.Add(new DrawnRow(sectionIndex, title, cached[i]));
                }
            }
            return rows;
        }

        /// <summary>Scrolls the widget the least distance that shows the row for <paramref name="target"/> whole.</summary>
        internal static void ScrollIntoView(TransferableOneWayWidget widget, TransferableOneWay target)
        {
            if (widget == null || target == null || !viewportHeights.TryGetValue(widget, out ViewportHeight viewport))
            {
                return;
            }
            List<DrawnRow> rows = Rows(widget);
            if (rows == null)
            {
                return;
            }
            float y = FirstRowY;
            float rowY = -1f;
            // Scrolling up to a section's first row also shows its title, and to the first row the top margin.
            float revealAbove = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                bool titled = rows[i].SectionTitle != null && (i == 0 || rows[i - 1].Section != rows[i].Section);
                if (titled)
                {
                    y += SectionTitleHeight;
                }
                if (ReferenceEquals(rows[i].Transferable, target))
                {
                    rowY = y;
                    revealAbove = (titled ? SectionTitleHeight : 0f) + (i == 0 ? FirstRowY : 0f);
                    break;
                }
                y += RowHeight;
            }
            if (rowY < 0f)
            {
                return;
            }
            Vector2 scroll = scrollPosition(widget);
            if (rowY < scroll.y)
            {
                scroll.y = Mathf.Max(0f, rowY - revealAbove);
            }
            else if (rowY + RowHeight > scroll.y + viewport.Value)
            {
                scroll.y = Mathf.Max(0f, rowY + RowHeight - viewport.Value);
            }
            else
            {
                return;
            }
            scrollPosition(widget) = scroll;
        }
    }
}
