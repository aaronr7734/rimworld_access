using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>Which section/state a captured Dialog_AssignBuildingOwner row represents.</summary>
    internal enum AssignRowKind
    {
        /// <summary>An already-assigned pawn; draws an Unassign button.</summary>
        Assigned,
        /// <summary>An eligible candidate; draws an Assign or Reassign button.</summary>
        Candidate,
        /// <summary>Accepted but ideoligion-forbidden; draws a label plus a separate ideo-info icon, no button.</summary>
        IdeoForbidden,
        /// <summary>Rejected by CanAssignTo; dimmed row, no button, reason baked into the label.</summary>
        Rejected,
    }

    /// <summary>One row Dialog_AssignBuildingOwner actually drew, in draw order.</summary>
    internal sealed class CapturedAssignRow
    {
        public AssignRowKind Kind;
        public Pawn Pawn;
        public Rect Rect;

        /// <summary>The pawn label the row drew, so a mod's rewrite of it (a relation suffix) is heard; Rejected rows carry vanilla's baked "(reason)".</summary>
        public string PawnLabel = "";

        /// <summary>"Unassign"/"Assign"/"Reassign", translated. Empty for IdeoForbidden/Rejected.</summary>
        public string ButtonWord = "";

        /// <summary>"Ideoligion forbids", translated. Only set for IdeoForbidden.</summary>
        public string ForbidReason = "";

        /// <summary>Index into ButtonTextCapture.Items for the same pass, or -1 when the row drew no button.</summary>
        public int ButtonCaptureIndex = -1;

        /// <summary>
        /// IdeoForbidden only: the delegate vanilla's ideo-icon click would invoke. Calling it
        /// directly reproduces the click without a result-injection scheme for ButtonInvisible.
        /// </summary>
        public Action IdeoIconAction;
    }

    /// <summary>
    /// Prefix/postfix taps on Dialog_AssignBuildingOwner's private
    /// DrawAssignedRow/DrawUnassignedRow, bracketed to AssignScope's DoWindowContents pass. Row
    /// identity is RE-DERIVED from the public CompAssignableToPawn API vanilla consults; only the
    /// spoken pawn label is the text the row drew.
    ///
    /// ROW RECT: both methods take the running cursor as a `ref float y` and advance it by the row
    /// height, or leave it untouched when DrawUnassignedRow's early return skips an already-assigned
    /// candidate. The prefix stashes the pre-call value in __state and the postfix reads the
    /// post-call value off the same ref parameter, so the delta is both the row height and the
    /// drawn/skipped signal (delta &lt;= 0 means skipped).
    ///
    /// IDEO ICON: IdeoUIUtility.DoIdeoIcon is not a Widgets.ButtonText call, so
    /// ButtonTextCapture's result-injection cannot reach it. A dedicated prefix records its
    /// extraAction delegate while the currently-drawing row is being evaluated, and AssignScope's
    /// Alt+I handler invokes that delegate directly rather than forcing a click result.
    /// </summary>
    internal static class AssignRowCapture
    {
        private const float FocusRingExpand = 2f;
        private static readonly Color FocusRingColor = new Color(0.45f, 0.78f, 1f);

        private static readonly AccessTools.FieldRef<Dialog_AssignBuildingOwner, CompAssignableToPawn> assignableField =
            AccessTools.FieldRefAccess<Dialog_AssignBuildingOwner, CompAssignableToPawn>("assignable");

        private static bool passOpen;
        private static readonly List<CapturedAssignRow> items = new List<CapturedAssignRow>();
        private static int focusedIndex = -1;

        // Bridges DrawUnassignedRow's bracket to the nested IdeoUIUtility.DoIdeoIcon call it makes
        // for the ideo-forbidden branch only.
        private static Pawn currentUnassignedRowPawn;
        private static Action pendingIdeoIconAction;

        // The last Widgets.LabelEllipses inside the open row: both row methods draw the pawn label last.
        private static bool rowOpen;
        private static string drawnRowLabel;

        /// <summary>Rows recorded by the current/most recent pass, in draw order.</summary>
        public static IReadOnlyList<CapturedAssignRow> Items
        {
            get { return items; }
        }

        /// <summary>
        /// Starts recording, clearing the previous pass. Called from AssignScope's DoWindowContents
        /// prefix with the element index that should carry the focus ring this pass; an index
        /// outside the row range simply draws none.
        /// </summary>
        public static void BeginPass(int focusedIndex)
        {
            items.Clear();
            AssignRowCapture.focusedIndex = focusedIndex;
            currentUnassignedRowPawn = null;
            pendingIdeoIconAction = null;
            passOpen = true;
        }

        /// <summary>Stop recording. Call from AssignScope's own DoWindowContents postfix.</summary>
        public static void EndPass()
        {
            passOpen = false;
            focusedIndex = -1;
            currentUnassignedRowPawn = null;
            pendingIdeoIconAction = null;
        }

        private static void DrawFocusRingIfFocused(int index, Rect rect)
        {
            if (index != focusedIndex || rect.width <= 0f)
            {
                return;
            }
            Rect expanded = rect.ExpandedBy(FocusRingExpand);
            Color previous = GUI.color;
            GUI.color = FocusRingColor;
            Widgets.DrawBox(expanded, 2);
            GUI.color = previous;
        }

        // DrawAssignedRow tap.

        internal static void BeginRow()
        {
            rowOpen = passOpen;
            drawnRowLabel = null;
        }

        internal static void OnRowLabelDrawn(string label)
        {
            if (rowOpen)
            {
                drawnRowLabel = label;
            }
        }

        private static string TakeDrawnLabel(string fallback)
        {
            string drawn = drawnRowLabel;
            rowOpen = false;
            drawnRowLabel = null;
            return string.IsNullOrEmpty(drawn) ? fallback : drawn.StripTags();
        }

        internal static void RecordAssignedRow(Dialog_AssignBuildingOwner dialog, Pawn pawn, float yBefore, float yAfter, float rowWidth, int buttonCountBefore)
        {
            string drawnLabel = TakeDrawnLabel(pawn.LabelCap);
            if (!passOpen)
            {
                return;
            }
            float height = yAfter - yBefore;
            if (height <= 0f)
            {
                return;
            }
            Rect rect = new Rect(0f, yBefore, rowWidth, height);
            CapturedAssignRow row = new CapturedAssignRow
            {
                Kind = AssignRowKind.Assigned,
                Pawn = pawn,
                Rect = rect,
                PawnLabel = drawnLabel,
                ButtonWord = "BuildingUnassign".Translate(),
                ButtonCaptureIndex = ButtonTextCapture.Items.Count > buttonCountBefore ? buttonCountBefore : -1,
            };
            items.Add(row);
            DrawFocusRingIfFocused(items.Count - 1, rect);
        }

        // DrawUnassignedRow tap, plus the nested ideo-icon tap it may trigger.

        internal static void BeginUnassignedRow(Pawn pawn)
        {
            if (!passOpen)
            {
                return;
            }
            currentUnassignedRowPawn = pawn;
            pendingIdeoIconAction = null;
            BeginRow();
        }

        internal static void RecordUnassignedRow(Dialog_AssignBuildingOwner dialog, Pawn pawn, float yBefore, float yAfter, float rowWidth, int buttonCountBefore)
        {
            Action ideoIconAction = pendingIdeoIconAction;
            string drawnLabel = TakeDrawnLabel(null);
            currentUnassignedRowPawn = null;
            pendingIdeoIconAction = null;
            if (!passOpen)
            {
                return;
            }
            float height = yAfter - yBefore;
            if (height <= 0f)
            {
                // Vanilla's AssignedPawnsForReading.Contains early return: already shown above.
                return;
            }
            Rect rect = new Rect(0f, yBefore, rowWidth, height);
            CompAssignableToPawn assignable = assignableField(dialog);
            AcceptanceReport report = assignable.CanAssignTo(pawn);
            CapturedAssignRow row = new CapturedAssignRow { Pawn = pawn, Rect = rect };

            if (!report.Accepted)
            {
                row.Kind = AssignRowKind.Rejected;
                row.PawnLabel = drawnLabel ?? pawn.LabelCap + " (" + report.Reason.StripTags() + ")";
            }
            else if (!Find.IdeoManager.classicMode && assignable.IdeoligionForbids(pawn))
            {
                row.Kind = AssignRowKind.IdeoForbidden;
                row.PawnLabel = drawnLabel ?? pawn.LabelCap;
                row.ForbidReason = "IdeoligionForbids".Translate();
                row.IdeoIconAction = ideoIconAction;
            }
            else
            {
                row.Kind = AssignRowKind.Candidate;
                row.PawnLabel = drawnLabel ?? pawn.LabelCap;
                row.ButtonWord = assignable.AssignedAnything(pawn) ? "BuildingReassign".Translate() : "BuildingAssign".Translate();
                row.ButtonCaptureIndex = ButtonTextCapture.Items.Count > buttonCountBefore ? buttonCountBefore : -1;
            }

            items.Add(row);
            DrawFocusRingIfFocused(items.Count - 1, rect);
        }

        internal static void OnIdeoIconDrawn(Action extraAction)
        {
            if (!passOpen || currentUnassignedRowPawn == null)
            {
                return;
            }
            pendingIdeoIconAction = extraAction;
        }
    }

    // Harmony taps.

    /// <summary>DrawAssignedRow(Pawn, ref float, Rect, int). TargetMethod is required: a by-ref parameter type is not a compile-time constant (CS0182).</summary>
    [HarmonyPatch]
    internal static class AssignedRowTapPatch
    {
        internal struct State
        {
            public float YBefore;
            public int ButtonCountBefore;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Dialog_AssignBuildingOwner), "DrawAssignedRow",
                new Type[] { typeof(Pawn), typeof(float).MakeByRefType(), typeof(Rect), typeof(int) });
        }

        [HarmonyPrefix]
        public static void Prefix(ref float y, out State __state)
        {
            __state.YBefore = y;
            __state.ButtonCountBefore = ButtonTextCapture.Items.Count;
            AssignRowCapture.BeginRow();
        }

        [HarmonyPostfix]
        public static void Postfix(Dialog_AssignBuildingOwner __instance, Pawn pawn, ref float y, Rect viewRect, State __state)
        {
            AssignRowCapture.RecordAssignedRow(__instance, pawn, __state.YBefore, y, viewRect.width, __state.ButtonCountBefore);
        }
    }

    /// <summary>DrawUnassignedRow(Pawn, ref float, Rect, int) -- see AssignedRowTapPatch for the TargetMethod/CS0182 note.</summary>
    [HarmonyPatch]
    internal static class UnassignedRowTapPatch
    {
        internal struct State
        {
            public float YBefore;
            public int ButtonCountBefore;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Dialog_AssignBuildingOwner), "DrawUnassignedRow",
                new Type[] { typeof(Pawn), typeof(float).MakeByRefType(), typeof(Rect), typeof(int) });
        }

        [HarmonyPrefix]
        public static void Prefix(Pawn pawn, ref float y, out State __state)
        {
            __state.YBefore = y;
            __state.ButtonCountBefore = ButtonTextCapture.Items.Count;
            AssignRowCapture.BeginUnassignedRow(pawn);
        }

        [HarmonyPostfix]
        public static void Postfix(Dialog_AssignBuildingOwner __instance, Pawn pawn, ref float y, Rect viewRect, State __state)
        {
            AssignRowCapture.RecordUnassignedRow(__instance, pawn, __state.YBefore, y, viewRect.width, __state.ButtonCountBefore);
        }
    }

    /// <summary>
    /// Not a Widgets.ButtonText call, so ButtonTextCapture never sees it: this records the delegate
    /// a real ideo-icon click would invoke, for AssignScope's Alt+I handler. Gated by
    /// AssignRowCapture's pass/row tracking, so it stays inert for every other DoIdeoIcon call.
    /// </summary>
    [HarmonyPatch(typeof(Widgets), "LabelEllipses", new Type[] { typeof(Rect), typeof(string) })]
    internal static class AssignRowLabelTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string label)
        {
            AssignRowCapture.OnRowLabelDrawn(label);
        }
    }

    [HarmonyPatch(typeof(IdeoUIUtility), "DoIdeoIcon")]
    internal static class AssignIdeoIconTapPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Action extraAction)
        {
            AssignRowCapture.OnIdeoIconDrawn(extraAction);
        }
    }
}
