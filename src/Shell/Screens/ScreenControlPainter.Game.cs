using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>The filter checkbox column and declared-action button row shared by the overview and log windows.</summary>
    internal static class ScreenControlPainter
    {
        private const float CheckboxRowHeight = 28f;
        private const float CheckboxRowGap = 2f;
        private const float ButtonSpacing = 10f;

        /// <summary>
        /// One vanilla labeled checkbox per index; a click goes through <paramref name="click"/> so the
        /// keyboard cursor follows it. Records each row's screen rect into <paramref name="screenRects"/>.
        /// </summary>
        internal static void DrawCheckboxColumn(Rect rect, Rect[] screenRects, int count, Func<int, string> label,
            Func<int, bool> value, Func<int, string> tooltip, Action<int> click, int focusedIndex)
        {
            float y = rect.y;
            for (int i = 0; i < count; i++)
            {
                Rect row = new Rect(rect.x, y, rect.width, CheckboxRowHeight);
                screenRects[i] = GuiSpace.VisibleScreenRect(row);
                bool on = value(i);
                bool before = on;
                string tip = tooltip?.Invoke(i);
                if (tip != null)
                {
                    TooltipHandler.TipRegion(row, tip);
                }
                Widgets.CheckboxLabeled(row, label(i), ref on);
                if (on != before)
                {
                    click(i);
                }
                if (i == focusedIndex)
                {
                    FocusRing.Draw(row);
                }
                y += CheckboxRowHeight + CheckboxRowGap;
            }
        }

        /// <summary>One button per declared action, greyed out while the action is disabled.</summary>
        internal static void DrawActionRow(Rect rect, IReadOnlyList<ScreenAction> actions, int focusedIndex, float maxButtonWidth)
        {
            if (actions.Count == 0)
            {
                return;
            }
            float width = Mathf.Min(maxButtonWidth, (rect.width - ButtonSpacing * (actions.Count - 1)) / actions.Count);
            float x = rect.x;
            for (int i = 0; i < actions.Count; i++)
            {
                ScreenAction action = actions[i];
                Rect r = new Rect(x, rect.y, width, rect.height);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = !action.Disabled;
                if (Widgets.ButtonText(r, action.Label))
                {
                    action.Activate();
                }
                GUI.enabled = wasEnabled;
                if (i == focusedIndex)
                {
                    FocusRing.Draw(r);
                }
                x += width + ButtonSpacing;
            }
        }
    }
}
