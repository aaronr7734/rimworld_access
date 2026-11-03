using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A runtime column def carrying the skill its worker draws. <see cref="PawnColumnDef"/> has no
    /// payload slot and builds its worker from <c>workerClass</c> through Activator, so the def is
    /// the only place a worker can read which skill it belongs to.
    /// </summary>
    internal sealed class PawnColumnDef_AccessSkill : PawnColumnDef
    {
        public SkillDef skill;

        /// <summary>The scope's column index (0 is the pawn name), so one comparer serves both callers.</summary>
        public int columnIndex;
    }

    /// <summary>
    /// Draws one skill cell the way <c>SkillUI</c> does: passion icon left of the level, vanilla's
    /// dash and grey for a disabled skill, vanilla's own tooltip. Sorting delegates to the single
    /// <see cref="PawnSkillsTableHelper"/> comparer the keyboard sort cycle also uses.
    /// </summary>
    internal sealed class PawnColumnWorker_AccessSkill : PawnColumnWorker
    {
        // SkillUI's own conventions: the disabled colour and dash, the 24px passion icon, the
        // aptitude colouring at level 0. DisabledSkillColor is private there, so it is repeated here.
        private static readonly Color DisabledSkillColor = new Color(1f, 1f, 1f, 0.5f);
        private const float PassionIconSize = 24f;
        private const int CellContentWidth = 42;
        private const int CellMaxWidth = 120;

        /// <summary>Vanilla's own skill tooltip, whose builder is private to <c>SkillUI</c>.</summary>
        internal static readonly Func<SkillRecord, string> SkillDescription = BuildSkillDescription();

        private PawnColumnDef_AccessSkill SkillColumn
        {
            get { return (PawnColumnDef_AccessSkill)def; }
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            SkillRecord record = pawn != null && pawn.skills != null
                ? pawn.skills.GetSkill(SkillColumn.skill)
                : null;
            if (record == null)
            {
                return;
            }

            string label;
            if (record.TotallyDisabled)
            {
                GUI.color = DisabledSkillColor;
                label = "-";
            }
            else
            {
                if (record.passion > Passion.None)
                {
                    Texture2D icon = record.passion == Passion.Major
                        ? SkillUI.PassionMajorIcon
                        : SkillUI.PassionMinorIcon;
                    GUI.DrawTexture(new Rect(rect.x + 2f, rect.center.y - PassionIconSize / 2f,
                        PassionIconSize, PassionIconSize), icon);
                }
                int level = record.GetLevel();
                if ((ModsConfig.BiotechActive || ModsConfig.AnomalyActive) && level == 0 && record.Aptitude != 0)
                {
                    GUI.color = record.Aptitude > 0 ? ColorLibrary.BrightGreen : ColorLibrary.RedReadable;
                }
                label = level.ToStringCached();
            }

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (Mouse.IsOver(rect) && SkillDescription != null)
            {
                TooltipHandler.TipRegion(rect, SkillDescription(record));
            }
        }

        public override int Compare(Pawn a, Pawn b)
        {
            return PawnSkillsTableHelper.CompareByColumn(a, b, SkillColumn.columnIndex);
        }

        public override int GetMinWidth(PawnTable table)
        {
            return Mathf.Max(base.GetMinWidth(table), CellContentWidth);
        }

        public override int GetOptimalWidth(PawnTable table)
        {
            return Mathf.Clamp(base.GetMinWidth(table) + 12, GetMinWidth(table), GetMaxWidth(table));
        }

        public override int GetMaxWidth(PawnTable table)
        {
            return Mathf.Max(GetMinWidth(table), CellMaxWidth);
        }

        private static Func<SkillRecord, string> BuildSkillDescription()
        {
            MethodInfo method = AccessTools.Method(typeof(SkillUI), "GetSkillDescription", new[] { typeof(SkillRecord) });
            return method != null ? AccessTools.MethodDelegate<Func<SkillRecord, string>>(method) : null;
        }
    }

    /// <summary>
    /// The visible body for the Alt+P skills table: a REAL vanilla <see cref="PawnTable"/>
    /// — header sort arrows, column sizing, zebra rows, the label column's click-to-jump,
    /// tooltips, all vanilla's own code — hosted in an ImmediateWindow twin. The twin
    /// mechanism and its safety contract are <see cref="FloatMenuTwin"/>'s, documented in
    /// that class's header and shared with <see cref="TreeTwinWindow"/>: an ImmediateWindow
    /// cannot steal Escape/Enter, never attracts a ScopeForWindow attachment, is exempt
    /// from every ForeignWindowAbove guard and never reaches DialogInterceptionPatch, so
    /// <see cref="PawnSkillsTableScope"/>'s lifecycle, modality and Escape symmetry are
    /// exactly what they were while the screen drew nothing.
    ///
    /// The table is also the SORT AUTHORITY once it exists: the scope's sort cycle calls
    /// <see cref="PawnTable.SortBy"/> and <see cref="PawnSkillsTableState"/> reads the row order back
    /// out of <see cref="PawnTable.PawnsListForReading"/>, so a mouse click on a column header
    /// reorders the keyboard's rows too and the two can never diverge.
    /// </summary>
    internal static class PawnSkillsTableTwin
    {
        private const int TwinWindowID = 74112612;

        /// <summary>Beneath <see cref="FloatMenuTwin"/>'s WindowLayer.Super, as <see cref="TreeTwinWindow"/> is.</summary>
        private const WindowLayer TwinLayer = WindowLayer.Dialog;

        private const float TitleGap = 8f;

        /// <summary>Screen space the window chrome needs around the table itself.</summary>
        private const int WidthReserve = 100;
        private const int HeightReserve = 200;

        private static PawnSkillsTableScope scope;
        private static PawnTable table;
        private static Rect windowRect;
        private static float titleHeight;
        private static int sizedForWidth;
        private static int sizedForHeight;

        /// <summary>Whether the mounted scope holds the keyboard, resolved in <see cref="Request"/> for the draw pass that follows it.</summary>
        private static bool scopeIsTop;

        // Cached so the immediate window's identity check does not thrash.
        private static readonly Action DrawContentsAction = DrawContents;

        internal static void Mount(PawnSkillsTableScope newScope)
        {
            scope = newScope;
            table = BuildTable();
            if (table != null)
            {
                PawnSkillsTableState.SetRowOrderSource(ReadRowOrder);
            }
        }

        internal static void Unmount(PawnSkillsTableScope oldScope)
        {
            if (oldScope == null || !ReferenceEquals(scope, oldScope))
            {
                return;
            }
            PawnSkillsTableState.SetRowOrderSource(null);
            scope = null;
            table = null;
            windowRect = default(Rect);
            sizedForWidth = 0;
            sizedForHeight = 0;
            scopeIsTop = false;
        }

        /// <summary>
        /// Runs the scope's sort cycle through the table, vanilla's own vehicle. A column index below
        /// zero is the cleared state: SortBy with a null column drops the sort.
        /// </summary>
        internal static void SortBy(int columnIndex, bool descending)
        {
            if (table == null)
            {
                return;
            }
            List<PawnColumnDef> columns = table.Columns;
            PawnColumnDef column = columnIndex >= 0 && columnIndex < columns.Count ? columns[columnIndex] : null;
            table.SortBy(column, descending);
        }

        private static IReadOnlyList<Pawn> ReadRowOrder()
        {
            return table != null ? table.PawnsListForReading : null;
        }

        internal static void Request()
        {
            try
            {
                // UIRootOnGUI runs during early startup, before the stack exists.
                if (Find.WindowStack == null || scope == null || table == null) return;

                // Body and focus presentation are gated apart: the table stays on screen while the
                // scope is anywhere on the live stack, but the ring, the selection wash and the
                // pointer follow belong to whoever holds the keyboard.
                if (!FocusStack.Contains(scope))
                {
                    windowRect = default(Rect);
                    return;
                }
                scopeIsTop = ReferenceEquals(FocusStack.Top, scope);

                EnsureRect();
                Find.WindowStack.ImmediateWindow(TwinWindowID, windowRect, TwinLayer, DrawContentsAction,
                    doBackground: true, absorbInputAroundWindow: false);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Pawn skills twin draw error", ex);
            }
        }

        /// <summary>
        /// The window wraps whatever size the table computed for itself. A resolution change only
        /// re-clamps the table's bounds — <see cref="PawnTable.SetMinMaxSize"/> sets the dirty flag
        /// itself, so nothing needs rebuilding.
        /// </summary>
        private static void EnsureRect()
        {
            if (sizedForWidth != UI.screenWidth || sizedForHeight != UI.screenHeight)
            {
                sizedForWidth = UI.screenWidth;
                sizedForHeight = UI.screenHeight;
                table.SetMinMaxSize(0, UI.screenWidth - WidthReserve, 0, UI.screenHeight - HeightReserve);
            }

            Text.Font = GameFont.Medium;
            titleHeight = Text.LineHeight;
            Text.Font = GameFont.Small;

            Vector2 size = table.Size;
            float width = size.x + Window.StandardMargin * 2f;
            float height = size.y + Window.StandardMargin * 2f + titleHeight + TitleGap * 2f;
            windowRect = new Rect((int)((UI.screenWidth - width) / 2f), (int)((UI.screenHeight - height) / 2f),
                width, height);
        }

        private static void DrawContents()
        {
            if (table == null) return;

            // ImmediateWindow calls its draw func with no inRect and its Margin is 0f, so the
            // standard window margin is ours to apply.
            Rect inner = windowRect.AtZero().ContractedBy(Window.StandardMargin);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, titleHeight), "Skills".Translate());
            Text.Font = GameFont.Small;
            float listY = inner.y + titleHeight + TitleGap;
            Widgets.DrawLineHorizontal(inner.x, listY, inner.width);
            listY += TitleGap;

            Vector2 origin = new Vector2(inner.x, listY);
            if (scopeIsTop)
            {
                ScrollFocusedCellIntoView(origin);
            }
            table.PawnTableOnGUI(origin);
            if (scopeIsTop && Event.current.type == EventType.Repaint)
            {
                DrawFocusedCell(origin);
            }
        }

        /// <summary>
        /// Keyboard scroll-follow: presentation state, written before the table draws so the focused
        /// cell is visible in the same pass. Top-aligning the row matches PawnTableFocusDriver.
        /// </summary>
        private static void ScrollFocusedCellIntoView(Vector2 origin)
        {
            Pawn pawn;
            PawnColumnDef column;
            bool headerRow;
            if (!TryGetFocus(out pawn, out column, out headerRow))
            {
                return;
            }
            Rect cell;
            float scrollToShow;
            if (!PawnTableFocusDriver.TryGetCellRect(table, origin, pawn, column, headerRow, out cell, out scrollToShow)
                && scrollToShow >= 0f)
            {
                PawnTableFocusDriver.SetScrollY(table, scrollToShow);
            }
        }

        private static void DrawFocusedCell(Vector2 origin)
        {
            Pawn pawn;
            PawnColumnDef column;
            bool headerRow;
            if (!TryGetFocus(out pawn, out column, out headerRow))
            {
                return;
            }
            Rect cell;
            float scrollToShow;
            if (!PawnTableFocusDriver.TryGetCellRect(table, origin, pawn, column, headerRow, out cell, out scrollToShow))
            {
                return;
            }
            Widgets.DrawHighlightSelected(cell);
            FocusRing.Draw(cell.ContractedBy(1f));
            // The twin is the surface, so its draw pass publishes the focused rect: a scope with no
            // OwnedWindow can never use FocusedContentRect.
            UiPointerFollow.NotifyFocusedRect(GuiSpace.VisibleScreenRect(cell));
        }

        /// <summary>
        /// The scope's cursor translated into the table's own decision objects. Scope column i is
        /// table column i: both come from <see cref="PawnSkillsTableHelper"/>'s ordering, asserted in
        /// <see cref="BuildTable"/>.
        /// </summary>
        private static bool TryGetFocus(out Pawn pawn, out PawnColumnDef column, out bool headerRow)
        {
            pawn = null;
            column = null;
            headerRow = false;

            int row;
            int columnIndex;
            if (scope == null || !scope.TryGetTwinFocus(out row, out columnIndex))
            {
                return false;
            }

            List<PawnColumnDef> columns = table.Columns;
            column = columnIndex >= 0 && columnIndex < columns.Count ? columns[columnIndex] : null;
            headerRow = row < 0;
            if (headerRow)
            {
                return true;
            }

            List<Pawn> rows = table.PawnsListForReading;
            pawn = row < rows.Count ? rows[row] : null;
            return pawn != null;
        }

        /// <summary>
        /// Builds the runtime table. The def and its columns are constructed in code and NEVER
        /// registered in any DefDatabase: <see cref="PawnTable"/> reads the fields directly and
        /// resolves nothing by name, and defs are never scribed, so nothing reaches a save file.
        /// Null simply means the window does not appear; the keyboard screen is unaffected.
        /// </summary>
        private static PawnTable BuildTable()
        {
            PawnColumnDef labelColumn = DefDatabase<PawnColumnDef>.GetNamed("Label", errorOnFail: false);
            if (labelColumn == null)
            {
                return null;
            }

            PawnTableDef tableDef = new PawnTableDef
            {
                defName = "RWA_PawnSkills",
                // Vanilla's own content-width tables set this to 0; the default would stretch a
                // narrow skill table across the screen.
                minWidth = 0,
                columns = new List<PawnColumnDef> { labelColumn },
            };

            List<SkillDef> skills = PawnSkillsTableHelper.Skills;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillDef skill = skills[i];
                tableDef.columns.Add(new PawnColumnDef_AccessSkill
                {
                    defName = "RWA_Skill_" + skill.defName,
                    label = (!skill.skillLabel.NullOrEmpty() ? skill.skillLabel : skill.label).CapitalizeFirst(),
                    workerClass = typeof(PawnColumnWorker_AccessSkill),
                    sortable = true,
                    skill = skill,
                    columnIndex = i + 1,
                });
            }

#if DEBUG
            for (int i = 1; i < tableDef.columns.Count; i++)
            {
                SkillDef expected = PawnSkillsTableHelper.SkillForColumn(i);
                SkillDef actual = ((PawnColumnDef_AccessSkill)tableDef.columns[i]).skill;
                if (actual != expected)
                {
                    ModLogger.Error("Pawn skills twin column " + i + " draws " + actual
                        + " but the scope reads " + expected + "; the ring would land on the wrong cell.");
                }
            }
#endif

            return new AccessSkillsPawnTable(tableDef, () => PawnSkillsTableState.DefaultOrder,
                UI.screenWidth - WidthReserve, UI.screenHeight - HeightReserve);
        }

        /// <summary>Keeps the colonist-bar order the state captured as the unsorted order, as <c>PawnTable_PlayerPawns</c> substitutes its own display order.</summary>
        private sealed class AccessSkillsPawnTable : PawnTable
        {
            internal AccessSkillsPawnTable(PawnTableDef def, Func<IEnumerable<Pawn>> pawnsGetter, int uiWidth, int uiHeight)
                : base(def, pawnsGetter, uiWidth, uiHeight)
            {
            }

            protected override IEnumerable<Pawn> LabelSortFunction(IEnumerable<Pawn> input)
            {
                return input;
            }
        }
    }

    /// <summary>Re-requests the twin's ImmediateWindow every frame, on <see cref="FloatMenuTwinRequestPatch"/>'s hook and self-expiring contract.</summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class PawnSkillsTableTwinRequestPatch
    {
        [HarmonyPostfix]
        internal static void Postfix()
        {
            try
            {
                PawnSkillsTableTwin.Request();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Pawn skills twin request patch error", ex);
            }
        }
    }
}
