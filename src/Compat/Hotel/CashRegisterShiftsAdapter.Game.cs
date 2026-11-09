using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for CashRegister's Shifts tab, the register's only staff
    /// scheduling surface (Building_CashRegister.IsActive and HasToWork both read a shift's
    /// TimetableBool). The tab is invisible to capture — each of the 24 hour cells is a bare
    /// GUI.DrawTexture drag-painted through Mouse.IsOver, with no Widgets primitive, TipRegion,
    /// or click target — so this adapter builds a structured tree instead.
    /// One expandable Section per shift, each holding a read-only "Assigned:" line, an "Assign
    /// staff"/"Delete shift" pair, and 24 individually toggled hour rows; a trailing "Add shift"
    /// honors the tab's own 50-shift cap. Every row re-derives from live state on each build.
    /// Assign staff runs the tab's own two-call sequence (SetAssigned then
    /// Dialog_AssignBuildingOwner): the dialog mutates the comp's assignedPawns list in place,
    /// which is the same List&lt;Pawn&gt; reference as shift.assigned, so no copy-back is needed
    /// and no rebuild follows the modal, asynchronous open.
    /// The tab colours each name by whether its shift is on duty now and whether the pawn is
    /// available, and highlights the current hour; both are spoken.
    /// </summary>
    internal sealed class CashRegisterShiftsAdapter : InspectNodeAdapter
    {
        // ITab_Register_Shifts.FillTab's own cap.
        private const int ShiftCap = 50;

        private readonly Type buildingType;
        private readonly Type shiftType;
        private readonly Type timetableType;
        private readonly Type compShiftsType;

        private readonly FieldInfo shiftsField;       // Building_CashRegister.shifts : List<Shift>
        private readonly MethodInfo markDirtyMethod;   // Building_CashRegister.MarkDirty()
        private readonly MethodInfo isAvailableMethod; // Building_CashRegister.IsAvailable(Pawn)
        private readonly PropertyInfo shiftActive;     // Shift.IsActive

        private readonly FieldInfo assignedField;      // Shift.assigned : List<Pawn>
        private readonly FieldInfo mapField;           // Shift.map : Map
        private readonly FieldInfo timetableField;      // Shift.timetable : TimetableBool

        private readonly MethodInfo getAssignmentMethod; // TimetableBool.GetAssignment(int) : bool
        private readonly MethodInfo setAssignmentMethod; // TimetableBool.SetAssignment(int, bool)

        private readonly MethodInfo setAssignedMethod;   // CompAssignableToPawn_Shifts.SetAssigned(List<Pawn>)

        private readonly bool ready;
        private InspectTabBase sharedTab;

        public override bool Ready => ready;

        internal CashRegisterShiftsAdapter()
        {
            var surface = new ReflectionSurface("CashRegisterShiftsAdapter");
            buildingType = surface.Type("CashRegister.Building_CashRegister");
            shiftType = surface.Type("CashRegister.Shifts.Shift");
            timetableType = surface.Type("CashRegister.Timetable.TimetableBool");
            compShiftsType = surface.Type("CashRegister.Shifts.CompAssignableToPawn_Shifts");

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

            ready = surface.Ready;
        }

        /// <summary>
        /// Registers this adapter against the Shifts tab type. A missing type or failed member
        /// resolution declines silently (Ready == false); registration never breaks startup.
        /// </summary>
        public static void TryRegister()
        {
            CompatRegistration.TabAdapter("CashRegister.Shifts.ITab_Register_Shifts",
                t =>
                {
                    var adapter = new CashRegisterShiftsAdapter();
                    adapter.sharedTab = InspectTabManager.GetSharedInstance(t);
                    return adapter;
                },
                "CashRegister shifts tab compat");
        }

        // Dispatch token, never displayed raw.
        public override string CategoryKey => "CashRegister Shifts";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => TabName();

        public override string CategoryDisplayName(object obj) => TabName();

        private static string TabName()
        {
            return CompatText.ResolveOrFallback("TabRegisterShifts", "RimWorldAccess.Compat.CashRegisterShifts.TabName");
        }

        public override bool CanExpand(object obj)
        {
            return ready && buildingType.IsInstanceOfType(obj);
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
            {
                return;
            }
            if (!buildingType.IsInstanceOfType(obj))
            {
                return;
            }

            try
            {
                BuildAllShifts(categoryItem, obj, mode);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegisterShiftsAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        private void BuildAllShifts(InspectionTreeItem categoryItem, object building, InspectionMode mode)
        {
            IList shifts = (IList)shiftsField.GetValue(building);
            if (shifts == null)
            {
                return;
            }

            // An activation callback can mutate the live list, so walk a stable copy.
            var workingList = new List<object>(shifts.Count);
            foreach (object shift in shifts)
            {
                workingList.Add(shift);
            }

            for (int i = 0; i < workingList.Count; i++)
            {
                BuildShiftSection(categoryItem, building, workingList[i], i + 1, mode);
            }

            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(categoryItem, "RimWorldAccess.Compat.CashRegisterShifts.AddShiftAction".Translate(), building,
                    () => OnAddShift(categoryItem, building, mode));
            }
        }

        private void BuildShiftSection(InspectionTreeItem categoryItem, object building, object shift, int displayIndex, InspectionMode mode)
        {
            // Live label, not a snapshot: child rows mutate the roster and the hours in place
            // with no category rebuild. displayIndex is stable, since add/delete rebuild all.
            InspectNodeFactory.LiveSection(categoryItem, () => BuildShiftLabel(building, shift, displayIndex), shift,
                secItem => BuildShiftChildren(categoryItem, building, secItem, shift, mode));
        }

        private void BuildShiftChildren(InspectionTreeItem categoryItem, object building, InspectionTreeItem secItem, object shift, InspectionMode mode)
        {
            // Live line: Dialog_AssignBuildingOwner mutates the assigned list in place after
            // this section is built, and no rebuild follows.
            InspectNodeFactory.LiveDetailLine(secItem, () =>
                "RimWorldAccess.Compat.CashRegisterShifts.AssignedLine"
                    .Translate(ComposeStaffText(building, (List<Pawn>)assignedField.GetValue(shift))).ToString());

            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.CashRegisterShifts.AssignStaffAction".Translate(), shift,
                    () => OnAssignStaff(building, shift));

                InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.CashRegisterShifts.DeleteShiftAction".Translate(), shift,
                    () => OnDeleteShift(categoryItem, building, shift, mode));
            }

            object timetable = timetableField.GetValue(shift);
            if (timetable == null)
            {
                return;
            }

            int indent = secItem.IndentLevel + 1;
            Map map = (Map)mapField.GetValue(shift);
            for (int hour = 0; hour < 24; hour++)
            {
                BuildHourRow(secItem, timetable, map, hour, indent, mode);
            }
        }

        private void BuildHourRow(InspectionTreeItem secItem, object timetable, Map map, int hour, int indent, InspectionMode mode)
        {
            bool on = (bool)getAssignmentMethod.Invoke(timetable, new object[] { hour });
            bool isReadOnly = mode == InspectionMode.ReadOnly;

            var item = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                Label = BuildHourLabel(map, hour, on),
                IndentLevel = indent,
                IsExpandable = false
            };

            if (!isReadOnly)
            {
                item.OnActivate = () => OnToggleHour(item, timetable, map, hour);
            }

            InspectNodeFactory.Attach(secItem, item);
        }

        private void OnToggleHour(InspectionTreeItem item, object timetable, Map map, int hour)
        {
            try
            {
                bool current = (bool)getAssignmentMethod.Invoke(timetable, new object[] { hour });
                bool next = !current;

                // Vehicle A: the mutator DoTimeAssignment's own drag branch calls.
                setAssignmentMethod.Invoke(timetable, new object[] { hour, next });

                item.Label = BuildHourLabel(map, hour, next);
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera(null);
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.HourToggled".Loc(StateWord(next)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegisterShiftsAdapter hour toggle failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
            }
        }

        // Vehicle A: the tab's own Assign button loads the shift roster into the register's
        // single shared assignment comp, then opens the vanilla dialog.
        private void OnAssignStaff(object building, object shift)
        {
            try
            {
                object comp = FindShiftsComp(building);
                if (comp == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
                    return;
                }

                List<Pawn> assigned = (List<Pawn>)assignedField.GetValue(shift);
                setAssignedMethod.Invoke(comp, new object[] { assigned });
                Find.WindowStack.Add(new Dialog_AssignBuildingOwner((CompAssignableToPawn)comp));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegisterShiftsAdapter assign-staff action failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
            }
        }

        private void OnDeleteShift(InspectionTreeItem categoryItem, object building, object shift, InspectionMode mode)
        {
            try
            {
                IList shifts = (IList)shiftsField.GetValue(building);
                int index = shifts.IndexOf(shift);
                if (index < 0)
                {
                    RebuildCategory(categoryItem, building, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
                    return;
                }

                // MUTATION-C: mirrors ITab_Register_Shifts.DrawDiscardButton; its body is inline
                // list surgery on Building_CashRegister.shifts, with no callable removal method.
                shifts.RemoveAt(index);
                markDirtyMethod.Invoke(building, null);
                SoundDefOf.Click.PlayOneShotOnCamera(null);

                RebuildCategory(categoryItem, building, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ShiftDeleted".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegisterShiftsAdapter delete-shift action failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
            }
        }

        private void OnAddShift(InspectionTreeItem categoryItem, object building, InspectionMode mode)
        {
            try
            {
                IList shifts = (IList)shiftsField.GetValue(building);
                if (shifts.Count >= ShiftCap)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ShiftCapReached".Loc(ShiftCap.ToString()));
                    return;
                }

                object newShift = Activator.CreateInstance(shiftType);

                // MUTATION-C: mirrors ITab_Register_Shifts.DrawAddButton's inline
                // `new Shift {map = Register.Map}`; Shift exposes only the field.
                mapField.SetValue(newShift, ((Thing)building).Map);

                shifts.Add(newShift);
                markDirtyMethod.Invoke(building, null);
                SoundDefOf.Click.PlayOneShotOnCamera(null);

                RebuildCategory(categoryItem, building, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ShiftAdded".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegisterShiftsAdapter add-shift action failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ActionFailed".Loc());
            }
        }

        private void RebuildCategory(InspectionTreeItem categoryItem, object building, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, building, mode, this, sharedTab);
        }

        private object FindShiftsComp(object building)
        {
            if (!(building is ThingWithComps twc))
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

        private string BuildShiftLabel(object building, object shift, int displayIndex)
        {
            List<Pawn> assigned = (List<Pawn>)assignedField.GetValue(shift);
            string staffText = ComposeStaffText(building, assigned);
            string hoursText = ComposeHoursSummary(shift);
            string key = (bool)shiftActive.GetValue(shift, null)
                ? "RimWorldAccess.Compat.CashRegisterShifts.ShiftLabelOnDuty"
                : "RimWorldAccess.Compat.CashRegisterShifts.ShiftLabelOffDuty";
            return key.Translate(displayIndex, staffText, hoursText);
        }

        private string ComposeStaffText(object building, List<Pawn> assigned)
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
                    names.Add((bool)isAvailableMethod.Invoke(building, new object[] { pawn })
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

        private string ComposeHoursSummary(object shift)
        {
            object timetable = timetableField.GetValue(shift);
            if (timetable == null)
            {
                return "RimWorldAccess.Compat.CashRegisterShifts.HoursNone".Translate();
            }

            var hourLabels = new List<string>(24);
            bool anyOn = false;
            bool allOn = true;
            for (int hour = 0; hour < 24; hour++)
            {
                bool on = (bool)getAssignmentMethod.Invoke(timetable, new object[] { hour });
                hourLabels.Add(on ? "on" : "");
                if (on)
                {
                    anyOn = true;
                }
                else
                {
                    allOn = false;
                }
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
            List<HourRangeSegment> segments = HourRangeSegments.Compute(hourLabels);
            var parts = new List<string>();
            foreach (HourRangeSegment segment in segments)
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

        // TimetableUtility.DoTimeAssignment highlights the map's current hour.
        private static string BuildHourLabel(Map map, int hour, bool on)
        {
            string key = map != null && GenLocalDate.HourOfDay(map) == hour
                ? "RimWorldAccess.Compat.CashRegisterShifts.HourRowCurrent"
                : "RimWorldAccess.Compat.CashRegisterShifts.HourRow";
            return key.Translate(hour, StateWord(on));
        }

        private static string StateWord(bool on)
        {
            return on ? "On".Translate().ToString() : "Off".Translate().ToString();
        }
    }
}
