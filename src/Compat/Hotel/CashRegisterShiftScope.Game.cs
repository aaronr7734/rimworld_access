using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// CashRegister's Shifts tab, level two: one shift's 24 hours as a single-row TABLE on
    /// <see cref="ScheduleScope"/>'s hour grammar and chords, opened over
    /// <see cref="CashRegisterShiftsScope"/>. It opens on the map's current hour, as the Schedule
    /// tab does. The Buttons region holds the row's own Assign and discard actions, and OK; Escape
    /// or OK returns to the list on the same shift.
    ///
    /// The mod has two states and no brush, so a paint chord carries what the mod's own
    /// press-and-drag would: a stroke begins by flipping the cell under the cursor (the press), and
    /// continues with that value while each chord starts on the cell the previous flip or paint
    /// landed on. Plain Left/Right do not report a landing, so stepping away and back resumes it.
    /// </summary>
    internal sealed class CashRegisterShiftScope : ScreenScope
    {
        private const int HourCount = CashRegisterShiftsCompat.HourCount;

        // Registered in ShellActionInventory.Part3; this scope only claims them.
        private const string ActPaintLeft = "cashRegisterShift.paintLeft";
        private const string ActPaintRight = "cashRegisterShift.paintRight";
        private const string ActPaintToFirstHour = "cashRegisterShift.paintToFirstHour";
        private const string ActPaintToLastHour = "cashRegisterShift.paintToLastHour";
        private const string ActAssignStaff = "cashRegisterShift.assignStaff";

        private static readonly SoundDef BulkPaintSound = SoundDefOf.Designate_DragStandard_Changed_NoCam;

        private Thing register;
        private object shift;
        private int shiftNumber;
        private int seenEditSerial = -1;

        private int strokeHour = -1;
        private bool strokeValue;

        public CashRegisterShiftScope()
        {
            Claim(ActPaintLeft, e => CashRegisterShiftsCompat.RunAction(() => PaintMoveHour(-1)), when: OnHourColumn);
            Claim(ActPaintRight, e => CashRegisterShiftsCompat.RunAction(() => PaintMoveHour(1)), when: OnHourColumn);
            Claim(ActPaintToFirstHour, e => CashRegisterShiftsCompat.RunAction(() => PaintHourRange(0)), when: OnHourColumn);
            Claim(ActPaintToLastHour, e => CashRegisterShiftsCompat.RunAction(() => PaintHourRange(HourCount - 1)), when: OnHourColumn);
            Claim(ActAssignStaff, e => CashRegisterShiftsCompat.AssignStaff(register, shift));
            Claim(SharedMenuGrammar.Cancel, e => Done());
        }

        public override string Name
        {
            get { return "cash-register-shift"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Windowless: no capture pass ever runs, so actions are declared, never captured.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return ShiftLabel();
        }

        protected override void RefreshContent()
        {
            register = RimWorldAccess.CashRegisterShiftsState.Register;
            object editing = RimWorldAccess.CashRegisterShiftsState.EditingShift;
            if (editing != null)
            {
                shift = editing;
            }
            shiftNumber = CashRegisterShiftsCompat.DisplayNumber(register, shift);
        }

        protected override int ContentItemCount(int region)
        {
            return shift == null ? 0 : 1;
        }

        protected override int ContentColumnCount(int region)
        {
            return HourCount;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo("RimWorldAccess.Compat.CashRegisterShifts.HourColumn".Translate(column));
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return new ElementDescription { Label = ShiftLabel() };
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (shift == null || column < 0 || column >= HourCount)
            {
                return "";
            }
            bool on = CashRegisterShiftsCompat.GetHour(shift, column);
            // TimetableUtility.DoTimeAssignment highlights the map's current hour.
            bool current = register?.Map != null && GenLocalDate.HourOfDay(register.Map) == column;
            string key = on
                ? (current ? "RimWorldAccess.Compat.CashRegisterShifts.OnDutyCurrentHour" : "RimWorldAccess.Compat.CashRegisterShifts.OnDuty")
                : (current ? "RimWorldAccess.Compat.CashRegisterShifts.OffDutyCurrentHour" : "RimWorldAccess.Compat.CashRegisterShifts.OffDuty");
            return key.Translate();
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (shift != null && column >= 0 && column < HourCount)
            {
                CashRegisterShiftsCompat.RunAction(() => FlipHour(column));
            }
            return true;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            AnnounceCurrentItem();
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                return new[]
                {
                    new ScreenAction("RimWorldAccess.Compat.CashRegisterShifts.AssignStaffAction".Translate(),
                        () => CashRegisterShiftsCompat.AssignStaff(register, shift),
                        actionId: ActAssignStaff),
                    new ScreenAction("RimWorldAccess.Compat.CashRegisterShifts.DeleteShiftAction".Translate(),
                        () => CashRegisterShiftsCompat.RunAction(DeleteShift)),
                    new ScreenAction("OK".Translate(), Done),
                };
            }
        }

        private const int AssignAction = 0;
        private const int DeleteAction = 1;

        public override void OnPush()
        {
            base.OnPush();
            if (seenEditSerial == RimWorldAccess.CashRegisterShiftsState.EditSerial)
            {
                return;
            }
            seenEditSerial = RimWorldAccess.CashRegisterShiftsState.EditSerial;
            shift = null;
            ResetOpenAnnouncement();
            ClearStroke();
            RefreshModel();
            if (Model.RegionIndex != 0)
            {
                Model.MoveToRegion(0);
            }
            if (HasActionsRegion())
            {
                Model.Region(ActionsRegionIndex())?.MoveTo(0);
            }
            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                table.Rows.MoveTo(1);
                table.MoveToColumn(register?.Map != null ? GenLocalDate.HourOfDay(register.Map) : 0);
            }
        }

        /// <summary>The singleton outlives the game; dropping the shift keeps it from pinning its map.</summary>
        public override void OnPop()
        {
            base.OnPop();
            if (RimWorldAccess.CashRegisterShiftsState.EditingShift == null)
            {
                shift = null;
                register = null;
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return CashRegisterShiftsCompat.RowLabel(register, shift, shiftNumber);
        }

        protected override void OnCursorSettled(int region, int index)
        {
            ClearStroke();
        }

        private string ShiftLabel()
        {
            return CashRegisterShiftsCompat.ShiftName(shiftNumber);
        }

        private void Done()
        {
            RimWorldAccess.CashRegisterShiftsState.EndEdit();
        }

        private void DeleteShift()
        {
            if (CashRegisterShiftsCompat.DeleteShiftAndSpeak(register, shift))
            {
                Done();
            }
        }

        private int CurrentHour()
        {
            TableModel table = Model.CurrentTable;
            if (table == null || Model.RegionIndex != 0 || table.Rows.Index != 1)
            {
                return -1;
            }
            int hour = table.ColumnIndex;
            return hour >= 0 && hour < HourCount ? hour : -1;
        }

        private bool OnHourColumn()
        {
            return CurrentHour() >= 0;
        }

        // Painting. Every write is TimetableBool.SetAssignment, the mod's own drag mutator.

        /// <summary>The value a paint chord starting here carries: the live stroke's, else the flip of this cell.</summary>
        private bool StrokeValueAt(int hour)
        {
            bool current = CashRegisterShiftsCompat.GetHour(shift, hour);
            bool continues = hour == strokeHour && current == strokeValue;
            return continues ? strokeValue : !current;
        }

        private void Anchor(int hour, bool value)
        {
            strokeHour = hour;
            strokeValue = value;
        }

        private void ClearStroke()
        {
            strokeHour = -1;
        }

        /// <summary>Space/Enter on an hour: the mod's click, which always flips and starts a stroke.</summary>
        private void FlipHour(int hour)
        {
            bool value = !CashRegisterShiftsCompat.GetHour(shift, hour);
            CashRegisterShiftsCompat.SetHour(shift, hour, value);
            Anchor(hour, value);
            SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.HourSet".Loc(hour, CashRegisterShiftsCompat.DutyWord(value)));
        }

        /// <summary>Shift+Left/Right: paint this hour and the next one, landing on the next.</summary>
        private void PaintMoveHour(int delta)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            int hour = CurrentHour();
            if (table == null || hour < 0)
            {
                return;
            }
            bool value = StrokeValueAt(hour);
            int target = (hour + delta + HourCount) % HourCount;
            var changed = new List<int>();
            if (CashRegisterShiftsCompat.SetHour(shift, hour, value))
            {
                changed.Add(hour);
            }
            if (CashRegisterShiftsCompat.SetHour(shift, target, value))
            {
                changed.Add(target);
            }
            table.MoveToColumn(target);
            Anchor(target, value);
            if (changed.Count > 0)
            {
                SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
            }

            string state = CashRegisterShiftsCompat.DutyWord(value);
            if (changed.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.HourAlreadySet".Loc(target, state));
            }
            else if (changed.Count == 1 && changed[0] == target)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.HourSet".Loc(target, state));
            }
            else
            {
                changed.Sort();
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.PaintedHours".Loc(state, ScheduleScope.FormatChangedHourRanges(changed)));
            }
        }

        /// <summary>Shift+Home/End: paint from the focused hour to hour 0 or 23.</summary>
        private void PaintHourRange(int boundaryHour)
        {
            RefreshModel();
            TableModel table = Model.CurrentTable;
            int startHour = CurrentHour();
            if (table == null || startHour < 0)
            {
                return;
            }
            bool value = StrokeValueAt(startHour);
            int step = boundaryHour >= startHour ? 1 : -1;
            var changed = new List<int>();
            for (int h = startHour; h != boundaryHour + step; h += step)
            {
                if (CashRegisterShiftsCompat.SetHour(shift, h, value))
                {
                    changed.Add(h);
                }
            }
            table.MoveToColumn(boundaryHour);
            Anchor(boundaryHour, value);
            BulkSoundQueue.Queue(changed.Count, BulkPaintSound);

            string state = CashRegisterShiftsCompat.DutyWord(value);
            if (changed.Count > 0)
            {
                changed.Sort();
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.PaintedHours".Loc(state, ScheduleScope.FormatChangedHourRanges(changed)));
                return;
            }
            TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.HoursAlreadySet".Loc(ScheduleScope.FormatEdgeHourRange(startHour, boundaryHour), state));
        }

        // Focus ring, drawn from CashRegisterShiftsRingPatch inside the tab's own draw.

        /// <summary>DoCell's postfix on this shift's strip: the focused hour, or the toolbar action's own button.</summary>
        internal void OnCellDrawn(Rect rect, object timetable)
        {
            if (shift == null || !ReferenceEquals(CashRegisterShiftsCompat.Timetable(shift), timetable))
            {
                return;
            }
            int hour = CurrentHour();
            if (hour >= 0)
            {
                // DoCell's own slicing, floored per cell.
                float width = Mathf.Floor(rect.width / HourCount);
                FocusRing.Draw(new Rect(rect.x + hour * width, rect.y, width, rect.height).ContractedBy(1f));
                return;
            }
            // The only other region is the toolbar, whose declared actions are its rows.
            int action = Model.RegionIndex != 0 ? Model.CurrentRegion.Index : -1;
            if (action == AssignAction)
            {
                FocusRing.Draw(new Rect(rect.x - CashRegisterShiftsScope.AssignWidth, rect.y, CashRegisterShiftsScope.AssignWidth, rect.height));
            }
            else if (action == DeleteAction)
            {
                FocusRing.Draw(new Rect(rect.x - CashRegisterShiftsScope.RowLeadWidth, rect.y,
                    CashRegisterShiftsScope.ButtonSize, CashRegisterShiftsScope.ButtonSize).ContractedBy(2f));
            }
            else
            {
                // OK and the header row have no widget of their own: ring the whole shift.
                FocusRing.Draw(new Rect(rect.x - CashRegisterShiftsScope.RowLeadWidth, rect.y,
                    CashRegisterShiftsScope.RowLeadWidth + rect.width, rect.height));
            }
        }
    }
}
