using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// CashRegister's Shifts tab, level one: the register's shifts as a list, each read with its
    /// staff, duty state now and hours, and Add shift in the Buttons region. Enter opens the shift's
    /// hours in <see cref="CashRegisterShiftScope"/>, stacked above; returning lands back on the same
    /// shift; a new shift opens there at once. RightBracket offers the shift's own Assign and
    /// discard actions without opening it, and the toolbar's assign button and chord assign it. The
    /// tab draws inside the inspect pane, so the scope is windowless and rides
    /// <see cref="CashRegisterShiftsScopeMirror"/>.
    /// </summary>
    internal sealed class CashRegisterShiftsScope : ScreenScope
    {
        // DrawShift lays a row out as discard (30), names (90), Assign (150), then DoCell's strip.
        internal const float RowLeadWidth = 270f;
        internal const float AssignWidth = 150f;
        internal const float ButtonSize = 30f;
        private const float AddButtonX = 10f;

        // Registered in ShellActionInventory.Part3; this scope only claims them.
        private const string ActContextMenu = "cashRegisterShifts.contextMenu";
        private const string ActAddShift = "cashRegisterShifts.addShift";
        private const string ActAssignStaff = "cashRegisterShifts.assignStaff";

        // DeclaredActions order; the windowless toolbar captures nothing, so these are its indices.
        private const int AddActionIndex = 0;
        private const int AssignActionIndex = 1;

        private readonly List<object> rows = new List<object>();
        private Thing register;
        private int seenOpenSerial = -1;

        public CashRegisterShiftsScope()
        {
            Claim(ActContextMenu, e => OpenShiftMenu());
            Claim(ActAddShift, e => CashRegisterShiftsCompat.RunAction(AddShift));
            Claim(ActAssignStaff, e => AssignFocusedShift());
            Claim(SharedMenuGrammar.Cancel, e => OnCancel());
        }

        public override string Name
        {
            get { return "cash-register-shifts"; }
        }

        /// <summary>No window will ever close this screen, so Escape is this scope's own.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
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
            return CashRegisterShiftsCompat.TabName();
        }

        /// <summary>The tab skips null entries when it draws, so they are not rows here either.</summary>
        protected override void RefreshContent()
        {
            register = RimWorldAccess.CashRegisterShiftsState.Register;
            CashRegisterShiftsCompat.LiveShifts(register, rows);
        }

        protected override int ContentItemCount(int region)
        {
            return rows.Count;
        }

        /// <summary>Rows are uniform and costly to describe, and the search matches the shift and its staff, not duty words or hours.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            return row >= 0 && row < rows.Count ? CashRegisterShiftsCompat.RowLabel(register, rows[row], row + 1) : "";
        }

        protected override TypeaheadCandidateKind ContentRowSearchKind(int region, int row)
        {
            return TypeaheadCandidateKind.Item;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return new ElementDescription
            {
                Label = index >= 0 && index < rows.Count ? CashRegisterShiftsCompat.ShiftSummary(register, rows[index], index + 1) : "",
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index >= 0 && index < rows.Count)
            {
                RimWorldAccess.CashRegisterShiftsState.EditShift(rows[index]);
            }
        }

        /// <summary>The tab draws no add button at its cap; the row stays, refusing with the cap.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                bool atCap = CashRegisterShiftsCompat.AtShiftCap(register);
                int row = ListRow();
                return new[]
                {
                    new ScreenAction("RimWorldAccess.Compat.CashRegisterShifts.AddShiftAction".Translate(), () => CashRegisterShiftsCompat.RunAction(AddShift),
                        actionId: ActAddShift,
                        disabled: atCap,
                        disabledReason: atCap ? (string)"RimWorldAccess.Compat.CashRegisterShifts.ShiftCapReached".Translate(CashRegisterShiftsCompat.ShiftCap) : null),
                    new ScreenAction(row >= 0
                            ? (string)"RimWorldAccess.Compat.CashRegisterShifts.AssignStaffToShift".Translate(row + 1)
                            : (string)"RimWorldAccess.Compat.CashRegisterShifts.AssignStaffAction".Translate(),
                        AssignFocusedShift,
                        actionId: ActAssignStaff,
                        disabled: row < 0,
                        disabledReason: row < 0 ? (string)"RimWorldAccess.Compat.CashRegisterShifts.NoShiftHere".Translate() : null),
                };
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            if (seenOpenSerial == RimWorldAccess.CashRegisterShiftsState.OpenSerial)
            {
                return;
            }
            seenOpenSerial = RimWorldAccess.CashRegisterShiftsState.OpenSerial;
            ResetOpenAnnouncement();
            TypeaheadReset();
            RefreshModel();
            LandOnFirstShift();
        }

        protected override string ComposeOpenAnnouncement()
        {
            return CashRegisterShiftsCompat.TabName();
        }

        /// <summary>The singleton outlives the game; dropping the shifts keeps it from pinning their map.</summary>
        public override void OnPop()
        {
            base.OnPop();
            if (!RimWorldAccess.CashRegisterShiftsState.IsActive)
            {
                rows.Clear();
                register = null;
            }
        }

        private void LandOnFirstShift()
        {
            if (Model.RegionIndex != 0 && rows.Count > 0)
            {
                Model.MoveToRegion(0);
            }
            Model.CurrentRegion?.MoveTo(0);
        }

        private void OnCancel()
        {
            string label = CashRegisterShiftsCompat.TabName();
            RimWorldAccess.CashRegisterShiftsState.Close();
            InspectionReturnHelper.AnnounceParentOrFallback("RimWorldAccess.Compat.CashRegisterShifts.Closed".Translate(label));
        }

        private int CurrentRow()
        {
            return Model.RegionIndex == 0 ? ListRow() : -1;
        }

        /// <summary>The list's row, remembered while the cursor is in the toolbar, so a toolbar action knows its shift.</summary>
        private int ListRow()
        {
            // Also read while RefreshModel is still building the regions.
            ListModel list = Model.RegionCount > 0 ? Model.Region(0) : null;
            int row = list == null ? -1 : list.Index;
            return row >= 0 && row < rows.Count ? row : -1;
        }

        private void AssignFocusedShift()
        {
            RefreshModel();
            int row = ListRow();
            if (row < 0)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.NoShiftHere".Loc());
                return;
            }
            CashRegisterShiftsCompat.AssignStaff(register, rows[row]);
        }

        /// <summary>The focused shift's own actions without opening it: the tab's Assign button and discard button.</summary>
        private void OpenShiftMenu()
        {
            RefreshModel();
            int row = CurrentRow();
            if (row < 0)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.NoShiftHere".Loc());
                return;
            }
            object shift = rows[row];
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimWorldAccess.Compat.CashRegisterShifts.AssignStaffAction".Translate(),
                    () => CashRegisterShiftsCompat.AssignStaff(register, shift)),
                new FloatMenuOption("RimWorldAccess.Compat.CashRegisterShifts.DeleteShiftAction".Translate(),
                    () => CashRegisterShiftsCompat.RunAction(() => DeleteShift(shift))),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private void DeleteShift(object shift)
        {
            if (CashRegisterShiftsCompat.DeleteShiftAndSpeak(register, shift))
            {
                RefreshModel();
            }
        }

        /// <summary>The new shift's hours open at once; returning lands on it in the list.</summary>
        private void AddShift()
        {
            if (CashRegisterShiftsCompat.Shifts(register) == null || CashRegisterShiftsCompat.AtShiftCap(register))
            {
                TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ShiftCapReached".Loc(CashRegisterShiftsCompat.ShiftCap));
                return;
            }
            CashRegisterShiftsCompat.AddShift(register);
            RefreshModel();
            if (Model.RegionIndex != 0)
            {
                Model.MoveToRegion(0);
            }
            Model.CurrentRegion?.MoveTo(rows.Count - 1);
            TolkHelper.Speak("RimWorldAccess.Compat.CashRegisterShifts.ShiftAddedAt".Loc(rows.Count));
            RimWorldAccess.CashRegisterShiftsState.EditShift(rows[rows.Count - 1]);
        }

        // Focus ring, drawn from CashRegisterShiftsRingPatch inside the tab's own draw.

        /// <summary>DoCell's postfix: the focused shift's whole row, or its Assign button for the toolbar's assign action.</summary>
        internal void OnCellDrawn(Rect rect, object timetable)
        {
            int row = ListRow();
            if (row < 0 || !ReferenceEquals(CashRegisterShiftsCompat.Timetable(rows[row]), timetable))
            {
                return;
            }
            if (Model.RegionIndex == 0)
            {
                FocusRing.Draw(new Rect(rect.x - RowLeadWidth, rect.y, RowLeadWidth + rect.width, rect.height));
            }
            else if (Model.CurrentRegion?.Index == AssignActionIndex)
            {
                FocusRing.Draw(new Rect(rect.x - AssignWidth, rect.y, AssignWidth, rect.height));
            }
        }

        /// <summary>DoHeader's postfix: <paramref name="curY"/> is past the header, where an empty tab draws its add button.</summary>
        internal void OnHeaderDrawn(float curY)
        {
            if (rows.Count == 0 && Model.RegionIndex != 0)
            {
                RingAddButton(curY);
            }
        }

        /// <summary>DrawShift's postfix: after the last shift, <paramref name="curY"/> is where FillTab draws the add button.</summary>
        internal void OnShiftDrawn(object shift, float curY)
        {
            if (Model.RegionIndex == 0)
            {
                return;
            }
            IList shifts = CashRegisterShiftsCompat.Shifts(register);
            if (shifts != null && shifts.Count > 0 && ReferenceEquals(shifts[shifts.Count - 1], shift))
            {
                RingAddButton(curY);
            }
        }

        /// <summary>Only off the list; the tab draws no add button at its cap.</summary>
        private void RingAddButton(float y)
        {
            if (Model.CurrentRegion?.Index == AddActionIndex && !CashRegisterShiftsCompat.AtShiftCap(register))
            {
                FocusRing.Draw(new Rect(AddButtonX, y, ButtonSize, ButtonSize));
            }
        }
    }
}
