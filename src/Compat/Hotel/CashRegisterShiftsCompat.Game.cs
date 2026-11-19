using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// CashRegister's Shifts tab (ITab_Register_Shifts), the register's only staff scheduling
    /// surface: its reflection view, the tab's own mutation vehicles, and the text the shift list
    /// (<see cref="CashRegisterShiftsScope"/>) and editor speak. The tab is invisible to capture, since each
    /// hour cell is a bare GUI.DrawTexture drag-painted through Mouse.IsOver.
    /// </summary>
    internal static class CashRegisterShiftsCompat
    {
        // ITab_Register_Shifts.FillTab's own cap.
        internal const int ShiftCap = 50;
        internal const int HourCount = 24;

        private static Type buildingType;
        private static Type shiftType;
        private static Type compShiftsType;
        private static FieldInfo shiftsField;
        private static MethodInfo markDirtyMethod;
        private static MethodInfo isAvailableMethod;
        private static PropertyInfo shiftActive;
        private static FieldInfo assignedField;
        private static FieldInfo mapField;
        private static FieldInfo timetableField;
        private static MethodInfo getAssignmentMethod;
        private static MethodInfo setAssignmentMethod;
        private static MethodInfo setAssignedMethod;

        internal static bool Ready { get; private set; }

        internal static void Register(Harmony harmony)
        {
            Type tabType = AccessTools.TypeByName("CashRegister.Shifts.ITab_Register_Shifts");
            if (tabType == null)
            {
                return;
            }
            try
            {
                var surface = new ReflectionSurface("CashRegisterShiftsCompat");
                surface.Supplied("CashRegister.Shifts.ITab_Register_Shifts", tabType);
                buildingType = surface.Type("CashRegister.Building_CashRegister");
                shiftType = surface.Type("CashRegister.Shifts.Shift");
                compShiftsType = surface.Type("CashRegister.Shifts.CompAssignableToPawn_Shifts");
                Type timetableType = surface.Type("CashRegister.Timetable.TimetableBool");
                Type utilityType = AccessTools.TypeByName("CashRegister.Timetable.TimetableUtility");

                shiftsField = surface.Field(buildingType, "shifts");
                markDirtyMethod = surface.Method(buildingType, "MarkDirty", Type.EmptyTypes);
                isAvailableMethod = surface.Method(buildingType, "IsAvailable", new[] { typeof(Pawn) });
                shiftActive = surface.Property(shiftType, "IsActive");
                assignedField = surface.Field(shiftType, "assigned");
                mapField = surface.Field(shiftType, "map");
                timetableField = surface.Field(shiftType, "timetable");
                getAssignmentMethod = surface.Method(timetableType, "GetAssignment", new[] { typeof(int) });
                setAssignmentMethod = surface.Method(timetableType, "SetAssignment", new[] { typeof(int), typeof(bool) });
                setAssignedMethod = surface.Method(compShiftsType, "SetAssigned", new[] { typeof(List<Pawn>) });

                Ready = surface.Ready;
                if (!Ready)
                {
                    return;
                }
                CashRegisterShiftsAdapter.TryRegister();

                // Ring-only targets: optional, so a reshaped draw costs the ring, never the screens.
                Type floatRef = typeof(float).MakeByRefType();
                CashRegisterShiftsRingPatch.Install(harmony,
                    AccessTools.Method(utilityType, "DoHeader", new[] { typeof(Rect), floatRef }),
                    AccessTools.Method(utilityType, "DoCell", new[] { typeof(Rect), timetableType, typeof(Map) }),
                    AccessTools.Method(tabType, "DrawShift", new[] { shiftType, floatRef, typeof(float) }));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegister shifts compat registration failed: {ex.Message}");
            }
        }

        internal static bool IsRegister(object obj)
        {
            return Ready && buildingType.IsInstanceOfType(obj);
        }

        internal static IList Shifts(Thing register)
        {
            return register == null ? null : (IList)shiftsField.GetValue(register);
        }

        /// <summary>The shifts the tab draws, in order: it skips null entries.</summary>
        internal static void LiveShifts(Thing register, List<object> into)
        {
            into.Clear();
            IList shifts = Shifts(register);
            if (shifts == null)
            {
                return;
            }
            foreach (object shift in shifts)
            {
                if (shift != null)
                {
                    into.Add(shift);
                }
            }
        }

        /// <summary>The shift's 1-based position among the shifts the tab draws, or 0 when it is gone.</summary>
        internal static int DisplayNumber(Thing register, object shift)
        {
            IList shifts = Shifts(register);
            if (shifts == null || shift == null)
            {
                return 0;
            }
            int number = 0;
            foreach (object candidate in shifts)
            {
                if (candidate == null)
                {
                    continue;
                }
                number++;
                if (ReferenceEquals(candidate, shift))
                {
                    return number;
                }
            }
            return 0;
        }

        internal static bool AtShiftCap(Thing register)
        {
            IList shifts = Shifts(register);
            return shifts != null && shifts.Count >= ShiftCap;
        }

        internal static string ShiftName(int displayIndex)
        {
            return "RimWorldAccess.Compat.CashRegisterShifts.ShiftName".Translate(displayIndex);
        }

        internal static void RunAction(Action body)
        {
            Guarded.Run("CashRegister shifts action", body, "RimWorldAccess.Compat.CashRegisterShifts.ActionFailed");
        }

        /// <summary>The tab's Assign sequence for <paramref name="shift"/>, failures spoken.</summary>
        internal static void AssignStaff(Thing register, object shift)
        {
            RunAction(() =>
            {
                if (!OpenAssign(register, shift))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
                }
            });
        }

        /// <summary>The tab's discard for <paramref name="shift"/>, its outcome spoken; true when it was removed.</summary>
        internal static bool DeleteShiftAndSpeak(Thing register, object shift)
        {
            bool deleted = DeleteShift(register, shift);
            TolkHelper.Speak(deleted
                ? "RimWorldAccess.Compat.CashRegisterShifts.ShiftDeleted".Loc()
                : "RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
            return deleted;
        }

        internal static object Timetable(object shift)
        {
            return timetableField.GetValue(shift);
        }

        internal static bool GetHour(object shift, int hour)
        {
            object timetable = Timetable(shift);
            return timetable != null && HourOn(timetable, hour);
        }

        private static bool HourOn(object timetable, int hour)
        {
            return (bool)getAssignmentMethod.Invoke(timetable, new object[] { hour });
        }

        /// <summary>Vehicle A: the mutator DoTimeAssignment's own drag branch calls. False when the cell already held the value.</summary>
        internal static bool SetHour(object shift, int hour, bool value)
        {
            object timetable = Timetable(shift);
            if (timetable == null || HourOn(timetable, hour) == value)
            {
                return false;
            }
            setAssignmentMethod.Invoke(timetable, new object[] { hour, value });
            return true;
        }

        /// <summary>
        /// Vehicle A: the tab's own Assign button loads the shift roster into the register's single
        /// shared assignment comp, then opens the vanilla dialog. The dialog edits the comp's list in
        /// place, which is the same List&lt;Pawn&gt; as the shift's, so nothing is copied back.
        /// </summary>
        internal static bool OpenAssign(Thing register, object shift)
        {
            object comp = FindShiftsComp(register);
            if (comp == null)
            {
                return false;
            }
            setAssignedMethod.Invoke(comp, new object[] { assignedField.GetValue(shift) });
            Find.WindowStack.Add(new Dialog_AssignBuildingOwner((CompAssignableToPawn)comp));
            return true;
        }

        private static bool DeleteShift(Thing register, object shift)
        {
            IList shifts = Shifts(register);
            int index = shifts.IndexOf(shift);
            if (index < 0)
            {
                return false;
            }
            // MUTATION-C: mirrors ITab_Register_Shifts.DrawDiscardButton; its body is inline
            // list surgery on Building_CashRegister.shifts, with no callable removal method.
            shifts.RemoveAt(index);
            markDirtyMethod.Invoke(register, null);
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            return true;
        }

        internal static void AddShift(Thing register)
        {
            IList shifts = Shifts(register);
            object newShift = Activator.CreateInstance(shiftType);

            // MUTATION-C: mirrors ITab_Register_Shifts.DrawAddButton's inline
            // `new Shift {map = Register.Map}`; Shift exposes only the field.
            mapField.SetValue(newShift, register.Map);

            shifts.Add(newShift);
            markDirtyMethod.Invoke(register, null);
            SoundDefOf.Click.PlayOneShotOnCamera(null);
        }

        private static object FindShiftsComp(Thing register)
        {
            if (!(register is ThingWithComps twc))
            {
                return null;
            }
            foreach (ThingComp comp in twc.AllComps)
            {
                if (compShiftsType.IsInstanceOfType(comp))
                {
                    return comp;
                }
            }
            return null;
        }

        /// <summary>The shift and its staff, marking those the tab greys as unavailable.</summary>
        internal static string RowLabel(Thing register, object shift, int displayIndex)
        {
            return "RimWorldAccess.Compat.CashRegisterShifts.ShiftRowLabel"
                .Translate(displayIndex, StaffText(register, (List<Pawn>)assignedField.GetValue(shift)));
        }

        /// <summary>The row label, then the shift's duty state now and its hours.</summary>
        internal static string ShiftSummary(Thing register, object shift, int displayIndex)
        {
            string key = (bool)shiftActive.GetValue(shift, null)
                ? "RimWorldAccess.Compat.CashRegisterShifts.ShiftSummaryOnDuty"
                : "RimWorldAccess.Compat.CashRegisterShifts.ShiftSummaryOffDuty";
            return key.Translate(RowLabel(register, shift, displayIndex), HoursSummary(shift));
        }

        private static string StaffText(Thing register, List<Pawn> assigned)
        {
            var names = new List<string>();
            if (assigned != null)
            {
                foreach (Pawn pawn in assigned)
                {
                    if (pawn == null)
                    {
                        continue;
                    }
                    string name = pawn.Name?.ToStringShort ?? pawn.LabelShort;
                    names.Add((bool)isAvailableMethod.Invoke(register, new object[] { pawn })
                        ? name
                        : "RimWorldAccess.Compat.CashRegisterShifts.PawnUnavailable".Translate(name).ToString());
                }
            }
            if (names.Count == 0)
            {
                return CompatText.ResolveOrFallback("TabRegisterShiftsEmpty", "RimWorldAccess.Compat.CashRegisterShifts.NoOne");
            }
            return names.ToCommaList();
        }

        private static string HoursSummary(object shift)
        {
            object timetable = Timetable(shift);
            var hourLabels = new List<string>(HourCount);
            bool anyOn = false;
            bool allOn = true;
            for (int hour = 0; hour < HourCount; hour++)
            {
                bool on = timetable != null && HourOn(timetable, hour);
                hourLabels.Add(on ? "on" : "");
                anyOn |= on;
                allOn &= on;
            }
            if (!anyOn)
            {
                return "RimWorldAccess.Compat.CashRegisterShifts.HoursNone".Translate();
            }
            if (allOn)
            {
                return "RimWorldAccess.Compat.CashRegisterShifts.HoursAllDay".Translate();
            }

            // Wraparound stays two runs: HourRangeSegments is linear RLE over 0-23, matching
            // how the same helper reads for ScheduleScope.
            var parts = new List<string>();
            foreach (HourRangeSegment segment in HourRangeSegments.Compute(hourLabels))
            {
                if (segment.Label != "on")
                {
                    continue;
                }
                parts.Add(segment.Start == segment.End
                    ? (string)"RimWorldAccess.Compat.CashRegisterShifts.HourSingleSpan".Translate(segment.Start)
                    : (string)"RimWorldAccess.Compat.CashRegisterShifts.HourRangeSpan".Translate(segment.Start, segment.End));
            }
            return string.Join(", ", parts);
        }

        internal static string DutyWord(bool on)
        {
            return on
                ? "RimWorldAccess.Compat.CashRegisterShifts.OnDuty".Translate().ToString()
                : "RimWorldAccess.Compat.CashRegisterShifts.OffDuty".Translate().ToString();
        }

        internal static string TabName()
        {
            return CompatText.ResolveOrFallback("TabRegisterShifts", "RimWorldAccess.Compat.CashRegisterShifts.TabName");
        }
    }
}
