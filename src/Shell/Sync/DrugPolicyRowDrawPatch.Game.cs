using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>The editable cells of one drug row, in vanilla's own draw order.</summary>
    internal enum DrugRowCell
    {
        TakeToInventory,
        AllowForAddiction,
        AllowForJoy,
        AllowScheduled,
        Frequency,
        MoodThreshold,
        JoyThreshold,
    }

    /// <summary>
    /// Records the geometry of the drug row <see cref="DrugPolicyDialogScope"/>'s cursor sits on,
    /// for the ring, and reports its focused slider to <see cref="SliderTypeInFocus"/>.
    /// <c>DoEntryRow</c> (Dialog_ManageDrugPolicies.cs:167) carries the row's entry and rect; cell
    /// widths come from the dialog's private <c>CalculateColumnsWidths</c> (:111), called rather
    /// than transcribed, and only the x-accumulation is mirrored (<see cref="CellGuiRect"/>).
    /// </summary>
    internal static class DrugPolicyRowDrawPatch
    {
        private static readonly MethodInfo CalculateColumnsWidths =
            AccessTools.Method(typeof(Dialog_ManageDrugPolicies), "CalculateColumnsWidths");

        private static int recordedFrame = -1;
        private static Dialog_ManageDrugPolicies recordedDialog;
        private static DrugPolicyEntry recordedEntry;
        private static Rect rowGuiRect;
        private static Rect rowScreenRect;
        private static GuiSpace.ClipKey rowClip;

        private static DrugPolicyDialogScope liveScope;
        private static int focusedSliderOrdinal = -1;
        private static int sliderOrdinal;
        private static DrugPolicyEntry rowEntry;
        private static bool drawingFocusedSlider;
        private static int sliderValueFrame = -1;
        private static int sliderValueOrdinal = -1;
        private static DrugPolicyEntry sliderValueEntry;
        private static float sliderValue;

        internal static bool DrawingFocusedSlider
        {
            get { return drawingFocusedSlider; }
        }

        internal static void Register(DrugPolicyDialogScope scope)
        {
            liveScope = scope;
        }

        internal static void Unregister(DrugPolicyDialogScope scope)
        {
            if (ReferenceEquals(liveScope, scope))
            {
                liveScope = null;
            }
        }

        /// <summary>DoEntryRow draws its sliders in a fixed order (Frequency, mood, joy; :204-208), so the n-th drawn is the n-th slider column.</summary>
        internal static void EnterRow(Dialog_ManageDrugPolicies dialog, DrugPolicyEntry entry, int index)
        {
            DrugPolicyDialogScope scope = liveScope;
            focusedSliderOrdinal = scope != null && scope.Owns(dialog) && scope.ShowsSliderFocus
                && index == scope.FocusedDrugIndex ? scope.FocusedSliderOrdinal : -1;
            sliderOrdinal = 0;
            rowEntry = entry;
        }

        internal static void ExitRow()
        {
            focusedSliderOrdinal = -1;
            drawingFocusedSlider = false;
            rowEntry = null;
        }

        internal static void EnterSlider(float value)
        {
            if (focusedSliderOrdinal < 0)
            {
                return;
            }
            int ordinal = sliderOrdinal++;
            drawingFocusedSlider = ordinal == focusedSliderOrdinal;
            if (drawingFocusedSlider)
            {
                sliderValueFrame = Time.frameCount;
                sliderValueOrdinal = ordinal;
                sliderValueEntry = rowEntry;
                sliderValue = value;
            }
        }

        internal static void ExitSlider()
        {
            drawingFocusedSlider = false;
        }

        internal static void ForgetSliderValue()
        {
            sliderValueFrame = -1;
        }

        /// <summary>The raw value the focused slider was last drawn with, while it is still drawing.</summary>
        internal static bool TryGetFocusedSliderValue(DrugPolicyEntry entry, int ordinal, out float value)
        {
            value = sliderValue;
            return sliderValueFrame >= 0 && Time.frameCount - sliderValueFrame <= 2
                && sliderValueOrdinal == ordinal && ReferenceEquals(sliderValueEntry, entry);
        }

        internal static void Record(Dialog_ManageDrugPolicies dialog, DrugPolicyEntry entry, Rect rect)
        {
            recordedFrame = Time.frameCount;
            recordedDialog = dialog;
            recordedEntry = entry;
            rowGuiRect = rect;
            rowScreenRect = GuiSpace.ToScreen(rect);
            rowClip = GuiSpace.CurrentClip();
        }

        /// <summary>
        /// The focused row's visible rect in absolute UI points, or empty when nothing was
        /// recorded recently. The ring arm and the row's own draw can straddle a frame, so the
        /// previous frame's record still counts.
        /// </summary>
        internal static Rect RowRect()
        {
            if (!Fresh())
            {
                return default(Rect);
            }
            return GuiSpace.VisibleScreenRectFrom(rowGuiRect, rowScreenRect, rowClip, rowGuiRect);
        }

        /// <summary>
        /// The recorded row's <paramref name="cell"/> in absolute UI points, or empty when
        /// vanilla draws no widget there this frame (an addiction or recreation box on a drug
        /// that has neither, the schedule cells of an unscheduled drug) — the caller falls back
        /// to the whole row.
        /// </summary>
        internal static Rect CellRect(DrugRowCell cell)
        {
            if (!Fresh() || recordedDialog == null || recordedEntry == null || recordedEntry.drug == null)
            {
                return default(Rect);
            }
            Rect guiRect = CellGuiRect(cell);
            if (guiRect.width <= 0f || guiRect.height <= 0f)
            {
                return default(Rect);
            }
            return GuiSpace.VisibleScreenRectFrom(rowGuiRect, rowScreenRect, rowClip, guiRect);
        }

        private static bool Fresh()
        {
            return recordedFrame >= 0 && Time.frameCount - recordedFrame <= 1;
        }

        /// <summary>
        /// Mirrors <c>DoEntryRow</c>'s own x-accumulation and per-cell rect shapes
        /// (Dialog_ManageDrugPolicies.cs:175-214) over the widths vanilla itself computes for
        /// this row. Every column advances x whether or not its widget is drawn, exactly as
        /// vanilla does, so a skipped widget shifts nothing.
        /// </summary>
        private static Rect CellGuiRect(DrugRowCell cell)
        {
            float addictionWidth;
            float allowJoyWidth;
            float scheduledWidth;
            float drugIconWidth;
            float drugNameWidth;
            float frequencyWidth;
            float moodThresholdWidth;
            float joyThresholdWidth;
            float takeToInventoryWidth;
            if (!ColumnWidths(rowGuiRect, out addictionWidth, out allowJoyWidth, out scheduledWidth,
                out drugIconWidth, out drugNameWidth, out frequencyWidth, out moodThresholdWidth,
                out joyThresholdWidth, out takeToInventoryWidth))
            {
                return default(Rect);
            }

            DrugPolicyEntry entry = recordedEntry;
            float x = rowGuiRect.x + drugIconWidth + drugNameWidth;
            if (cell == DrugRowCell.TakeToInventory)
            {
                return new Rect(x, rowGuiRect.y, takeToInventoryWidth, rowGuiRect.height).ContractedBy(4f);
            }

            x += takeToInventoryWidth;
            if (cell == DrugRowCell.AllowForAddiction)
            {
                return entry.drug.IsAddictiveDrug ? CheckboxRect(x) : default(Rect);
            }

            x += addictionWidth;
            if (cell == DrugRowCell.AllowForJoy)
            {
                return entry.drug.IsPleasureDrug ? CheckboxRect(x) : default(Rect);
            }

            x += allowJoyWidth;
            if (cell == DrugRowCell.AllowScheduled)
            {
                return CheckboxRect(x);
            }

            x += scheduledWidth;
            if (!entry.allowScheduled)
            {
                return default(Rect);
            }
            if (cell == DrugRowCell.Frequency)
            {
                return SliderRect(x, frequencyWidth);
            }

            x += frequencyWidth;
            if (cell == DrugRowCell.MoodThreshold)
            {
                return SliderRect(x, moodThresholdWidth);
            }

            x += moodThresholdWidth;
            if (cell == DrugRowCell.JoyThreshold)
            {
                return SliderRect(x, joyThresholdWidth);
            }
            return default(Rect);
        }

        /// <summary>The 24px box <c>Widgets.Checkbox(x, y, ..., 24f, ...)</c> draws (Verse/Widgets.cs:1203).</summary>
        private static Rect CheckboxRect(float x)
        {
            return new Rect(x, rowGuiRect.y + 2f, 24f, 24f);
        }

        private static Rect SliderRect(float x, float width)
        {
            return new Rect(x, rowGuiRect.y + 2f, width, rowGuiRect.height).ContractedBy(4f);
        }

        private static bool ColumnWidths(Rect rect, out float addictionWidth, out float allowJoyWidth,
            out float scheduledWidth, out float drugIconWidth, out float drugNameWidth,
            out float frequencyWidth, out float moodThresholdWidth, out float joyThresholdWidth,
            out float takeToInventoryWidth)
        {
            addictionWidth = 0f;
            allowJoyWidth = 0f;
            scheduledWidth = 0f;
            drugIconWidth = 0f;
            drugNameWidth = 0f;
            frequencyWidth = 0f;
            moodThresholdWidth = 0f;
            joyThresholdWidth = 0f;
            takeToInventoryWidth = 0f;
            if (CalculateColumnsWidths == null)
            {
                return false;
            }
            object[] args = new object[] { rect, null, null, null, null, null, null, null, null, null };
            CalculateColumnsWidths.Invoke(recordedDialog, args);
            addictionWidth = (float)args[1];
            allowJoyWidth = (float)args[2];
            scheduledWidth = (float)args[3];
            drugIconWidth = (float)args[4];
            drugNameWidth = (float)args[5];
            frequencyWidth = (float)args[6];
            moodThresholdWidth = (float)args[7];
            joyThresholdWidth = (float)args[8];
            takeToInventoryWidth = (float)args[9];
            return true;
        }
    }

    /// <summary>
    /// Records the focused drug row as vanilla draws it. In the settings sub-mode the focused
    /// row is the drug being edited, not the cursor's row, so the cell math always has its base
    /// rect (<see cref="DrugPolicyDialogScope.FocusedDrugIndex"/>).
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ManageDrugPolicies), "DoEntryRow")]
    internal static class DrugPolicyEntryRowPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_ManageDrugPolicies __instance, DrugPolicyEntry entry, int index)
        {
            DrugPolicyRowDrawPatch.EnterRow(__instance, entry, index);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            DrugPolicyRowDrawPatch.ExitRow();
        }

        [HarmonyPostfix]
        public static void Postfix(Dialog_ManageDrugPolicies __instance, Rect rect, DrugPolicyEntry entry, int index)
        {
            try
            {
                DrugPolicyDialogScope scope = FocusStack.Top as DrugPolicyDialogScope;
                if (scope == null || !scope.Owns(__instance) || index != scope.FocusedDrugIndex)
                {
                    return;
                }
                DrugPolicyRowDrawPatch.Record(__instance, entry, rect);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Drug policy row capture error", ex);
            }
        }
    }

    /// <summary>Finalizer, not postfix: the flag must span a type-in mod's own postfix on the same call.</summary>
    [HarmonyPatch(typeof(Widgets), "HorizontalSlider",
        new Type[] { typeof(Rect), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(float) })]
    internal static class DrugPolicySliderBracketPatch
    {
        [HarmonyPrefix]
        public static void Prefix(float value)
        {
            DrugPolicyRowDrawPatch.EnterSlider(value);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            DrugPolicyRowDrawPatch.ExitSlider();
        }
    }
}
