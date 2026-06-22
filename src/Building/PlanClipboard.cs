using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// A simple clipboard for duplicating a plan. "Copy" on a plan's gizmo captures that plan's
    /// shape, color, and name; Ctrl+V on the map stamps a duplicate. Placement is cursor-relative:
    /// the cell the cursor was on when copying lands under the cursor on paste, so a keyboard user
    /// always knows exactly where the duplicate will go. This is the keyboard-friendly stand-in for
    /// vanilla's plan copy tools, which place at the mouse cursor.
    /// </summary>
    public static class PlanClipboard
    {
        // Cell offsets relative to the copied shape's minimum corner.
        private static List<IntVec3> offsets;
        // The cursor cell at copy time, also relative to the minimum corner. On paste this cell is
        // placed under the paste cursor so the duplicate appears where the user expects.
        private static IntVec3 cursorOffset;
        private static ColorDef color;
        private static string label;

        public static bool HasContent => offsets != null && offsets.Count > 0;

        /// <summary>Captures a plan's shape, color, and name into the clipboard.</summary>
        public static void Copy(Plan plan)
        {
            if (plan == null || plan.CellCount == 0)
                return;

            var cells = plan.Cells;
            int minX = cells.Min(c => c.x);
            int minZ = cells.Min(c => c.z);
            var min = new IntVec3(minX, 0, minZ);
            offsets = cells.Select(c => c - min).ToList();
            color = plan.Color;
            label = plan.RenamableLabel;

            // Anchor on the cell the cursor is over (it sits on this plan, since the copy gizmo opens
            // at the cursor). If for any reason it isn't on the plan, fall back to the minimum corner.
            IntVec3 cursorCell = MapNavigationState.CurrentCursorPosition;
            cursorOffset = plan.ContainsCell(cursorCell) ? cursorCell - min : IntVec3.Zero;

            TolkHelper.SpeakData(
                "RimWorldAccess.Building.Plan.Copied".Translate(label).ToString() + ". "
                + "RimWorldAccess.Building.Plan.PasteHint".Translate().ToString(),
                SpeechPriority.High);
        }

        /// <summary>
        /// Stamps the copied plan onto the map so the cell that was under the cursor when copied now
        /// sits at <paramref name="cursor"/>. Cells already belonging to another plan are reassigned
        /// (a cell belongs to one plan), and the result runs the contiguity check, matching how the
        /// game treats plan edits.
        /// </summary>
        public static void PasteAt(IntVec3 cursor, Map map)
        {
            if (!HasContent || map == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Plan.ClipboardEmpty".Loc(), SpeechPriority.High);
                return;
            }

            // Place the copied cursor cell back under the cursor: the minimum corner goes here.
            IntVec3 anchor = cursor - cursorOffset;
            var planManager = map.planManager;
            var newPlan = new Plan(color, planManager);
            int placed = 0;
            int replaced = 0;
            foreach (var offset in offsets)
            {
                IntVec3 c = anchor + offset;
                if (!c.InBounds(map) || c.InNoBuildEdgeArea(map))
                    continue;

                var existing = planManager.PlanAt(c);
                if (existing == newPlan)
                    continue;
                if (existing != null)
                {
                    existing.RemoveCell(c);
                    replaced++;
                }

                newPlan.AddCell(c);
                placed++;
            }

            if (placed == 0)
            {
                newPlan.Delete(playSound: false);
                TolkHelper.Speak("RimWorldAccess.Building.Plan.PasteNothing".Loc(), SpeechPriority.High);
                return;
            }

            newPlan.RenamableLabel = label;
            newPlan.CheckContiguous();
            if (replaced > 0)
                TolkHelper.Speak("RimWorldAccess.Building.Plan.PastedReplaced".Loc(label, placed, replaced), SpeechPriority.High);
            else
                TolkHelper.Speak("RimWorldAccess.Building.Plan.Pasted".Loc(label, placed), SpeechPriority.High);
        }
    }
}
