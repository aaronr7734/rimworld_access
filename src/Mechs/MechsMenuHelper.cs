using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class MechsMenuHelper
    {
        /// <summary>
        /// What a column DOES, derived once per <see cref="InitColumnDefs"/>
        /// from its worker's own runtime TYPE — never its position in the
        /// list — mirroring <see cref="AnimalsMenuHelper.ColumnType"/>'s own
        /// classification approach for the sibling Animals table.
        /// </summary>
        public enum ColumnType
        {
            Name,           // LabelWithIcon
            Energy,         // Energy bar
            Draft,          // DraftMech checkbox
            AutoRepair,     // Auto-repair checkbox
            Overseer,       // Mechanitor name
            ControlGroup,   // Dropdown for control group
            WorkMode,       // Work mode label
            AllowedArea,    // Area restriction
            /// <summary>A modded/DLC column with no bespoke reader here: read generically through <see cref="PawnColumnHandlerRegistry"/> when its worker derives from a known vanilla base, honestly unavailable otherwise.</summary>
            Unknown
        }

        // === Column Defs (the single source of truth for the column SET,
        // ORDER, and NAMES) plus the per-index classification derived from
        // each def's own worker type (for dispatch — see ColumnType). ===
        private static List<PawnColumnDef> columnDefs = new List<PawnColumnDef>();
        private static List<ColumnType> columnKinds = new List<ColumnType>();

        /// <summary>
        /// Builds the resolved column list straight from vanilla's own
        /// <see cref="PawnTableDefOf.Mechs"/> def — its order and any column
        /// a mod adds. Spacer/no-content columns (Gap, RemainingSpace) are
        /// skipped via the same <see cref="PawnColumnHandlerRegistry"/> test
        /// <see cref="AnimalsMenuHelper.InitColumnDefs"/> applies.
        /// </summary>
        public static void InitColumnDefs()
        {
            columnDefs = new List<PawnColumnDef>();
            columnKinds = new List<ColumnType>();

            List<PawnColumnDef> defs = PawnTableDefOf.Mechs?.columns;
            if (defs == null)
                return;

            foreach (PawnColumnDef def in defs)
            {
                if (def == null)
                    continue;
                try
                {
                    if (PawnColumnHandlerRegistry.Resolve(def).SkipColumn(def))
                        continue;
                    columnDefs.Add(def);
                    columnKinds.Add(ClassifyColumn(def));
                }
                catch (Exception ex)
                {
                    // A broken (typically modded) worker must not drop the
                    // whole table — matches AnimalsMenuHelper's own
                    // failure-containment rule.
                    Log.Warning("[RimWorld Access] Mechs column '" + def.defName + "' failed to resolve and was skipped: " + ex);
                }
            }
        }

        /// <summary>Classifies a column by its worker's own runtime type (is-checks, so a modded subclass inherits the base behavior for free).</summary>
        private static ColumnType ClassifyColumn(PawnColumnDef def)
        {
            PawnColumnWorker worker = def.Worker;
            // Overseer derives from PawnColumnWorker_Label (it renders the Label
            // cell against the mech's overseer), so it must be tested before the
            // Label check would swallow it and read the mech's own name instead.
            if (worker is PawnColumnWorker_Overseer) return ColumnType.Overseer;
            if (worker is PawnColumnWorker_Label) return ColumnType.Name;
            if (worker is PawnColumnWorker_Energy) return ColumnType.Energy;
            if (worker is PawnColumnWorker_DraftMech) return ColumnType.Draft;
            if (worker is PawnColumnWorker_AutoRepair) return ColumnType.AutoRepair;
            if (worker is PawnColumnWorker_ControlGroup) return ColumnType.ControlGroup;
            if (worker is PawnColumnWorker_WorkMode) return ColumnType.WorkMode;
            if (worker is PawnColumnWorker_AllowedArea) return ColumnType.AllowedArea;
            return ColumnType.Unknown;
        }

        /// <summary>Internal so the focus driver can map the scope's column index onto vanilla's own column def with it.</summary>
        internal static PawnColumnDef GetDef(int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < columnDefs.Count ? columnDefs[columnIndex] : null;
        }

        /// <summary>The classification for a column index (Unknown if out of range or the worker has no bespoke reader here).</summary>
        public static ColumnType GetColumnType(int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < columnKinds.Count ? columnKinds[columnIndex] : ColumnType.Unknown;
        }

        public static bool IsColumnSortable(int columnIndex)
            => PawnColumnSortHelper.IsColumnSortable(columnDefs, columnIndex);

        public static int GetTotalColumnCount() => columnDefs.Count;

        // === Row Label (for typeahead search) ===

        public static string GetMechName(Pawn pawn)
        {
            return pawn.Name != null ? $"{pawn.Name.ToStringShort} ({pawn.def.LabelCap})" : pawn.def.LabelCap.ToString();
        }

        private static string GetMechNameWithActivity(Pawn pawn)
        {
            string baseName = GetMechName(pawn);
            string activity = PawnHelper.GetPawnActivity(pawn);
            return activity != null ? $"{baseName} - {activity}" : baseName;
        }

        // === Column Names (localized) ===

        public static string GetColumnName(int columnIndex)
        {
            PawnColumnDef def = GetDef(columnIndex);
            if (def == null)
                return "RimWorldAccess.Mechs.Value.Unknown".Translate().ToString();

            // Every vanilla Mechs column def carries its own label (LabelWithIcon
            // inherits "name" from its Label parent, AllowedAreaMech inherits
            // "allowed area" from its AllowedArea parent); a label-less mod
            // column falls to its registry handler's name, then the defName.
            if (!def.label.NullOrEmpty())
                return def.LabelCap.ToString();
            return PawnColumnHandlerRegistry.Resolve(def).HeaderLabel(def) ?? def.defName;
        }

        // === Column Values ===

        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (GetDef(columnIndex) == null)
                return "RimWorldAccess.Mechs.Value.Unknown".Translate().ToString();

            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Name:
                    return GetMechNameWithActivity(pawn);
                case ColumnType.Energy:
                    return GetEnergy(pawn);
                case ColumnType.Draft:
                    return GetDraftStatus(pawn);
                case ColumnType.AutoRepair:
                    return GetAutoRepairStatus(pawn);
                case ColumnType.Overseer:
                    return GetOverseerName(pawn);
                case ColumnType.ControlGroup:
                    return GetControlGroupValue(pawn);
                case ColumnType.WorkMode:
                    return GetWorkModeValue(pawn);
                case ColumnType.AllowedArea:
                    return GetAllowedAreaValue(pawn);
                default:
                    // Modded workers derived from a known vanilla base read generically.
                    return PawnColumnCellReader.CellText(GetDef(columnIndex), pawn);
            }
        }

        /// <summary>Per-cell tip for an Unknown-classified column, via the registry; null for classified columns, whose tips are curated.</summary>
        public static string GetUnknownCellTip(Pawn pawn, int columnIndex)
        {
            return GetColumnType(columnIndex) == ColumnType.Unknown
                ? PawnColumnCellReader.CellTip(GetDef(columnIndex), pawn)
                : null;
        }

        // === Column Tooltips ===

        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
        {
            PawnColumnDef def = GetDef(columnIndex);
            if (def == null)
                return null;

            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Draft:
                    AcceptanceReport canDraft = MechanitorUtility.CanDraftMech(pawn);
                    if (!canDraft && !canDraft.Reason.NullOrEmpty())
                        return canDraft.Reason;
                    return null;
                case ColumnType.AutoRepair:
                    return "CommandAutoRepairDesc".Translate().Resolve();
                case ColumnType.WorkMode:
                    return WorkModeColumnHandler.CellTipFor(pawn);
                case ColumnType.AllowedArea:
                    // From AllowedAreaMech XML headerTip
                    return def.headerTip;
                default:
                    return null;
            }
        }

        // === Sorting ===

        public static IList<Pawn> SortMechsByColumn(List<Pawn> mechs, int columnIndex, bool descending)
        {
            return PawnColumnSortHelper.SortByColumnDef(mechs, columnDefs, columnIndex, descending);
        }

        // === Interactivity ===

        public static bool IsColumnInteractive(int columnIndex)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Name:        // Jump to mech
                case ColumnType.Draft:       // Toggle draft
                case ColumnType.AutoRepair:  // Toggle auto-repair
                case ColumnType.ControlGroup: // Submenu
                case ColumnType.WorkMode:    // Submenu
                case ColumnType.AllowedArea: // Submenu
                    return true;
                case ColumnType.Energy:      // Display only
                case ColumnType.Overseer:    // Display only
                default:
                    return false;
            }
        }

        // === Painting ===

        public static bool CanPaintColumn(int columnIndex)
        {
            return GetDef(columnIndex)?.paintable == true;
        }

        public static bool GetPaintableValue(Pawn pawn, int columnIndex)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Draft:
                    return pawn.Drafted;
                case ColumnType.AutoRepair:
                    var comp = pawn.GetComp<CompMechRepairable>();
                    return comp?.autoRepair == true;
                default:
                    return false;
            }
        }

        public static bool SetPaintableValue(Pawn pawn, int columnIndex, bool value)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Draft:
                    if (!ModsConfig.BiotechActive || !pawn.IsColonyMechPlayerControlled || !pawn.Spawned)
                        return false;
                    AcceptanceReport canDraft = MechanitorUtility.CanDraftMech(pawn);
                    if (!canDraft) return false;
                    // MUTATION-C: mirrors PawnColumnWorker_DraftMech.DoCell's own
                    // inline pawn.drafter.Drafted write (RimWorld/PawnColumnWorker_DraftMech.cs
                    // :19-28) — DoCell draws its own checkbox and writes the field
                    // itself on change; it is not a PawnColumnWorker_Checkbox
                    // subclass, so there is no separate SetValue to call instead.
                    pawn.drafter.Drafted = value;
                    return true;
                case ColumnType.AutoRepair:
                    var comp = pawn.GetComp<CompMechRepairable>();
                    if (comp == null) return false;
                    comp.autoRepair = value;
                    return true;
                default:
                    return false;
            }
        }

        public static SoundDef GetPaintSound(int columnIndex, bool value)
        {
            return value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff;
        }

        public static string GetPaintValueLabel(int columnIndex, bool value)
        {
            switch (GetColumnType(columnIndex))
            {
                case ColumnType.Draft:
                    return value
                        ? "CommandDraftLabel".Translate().Resolve()
                        : "CommandUndraftLabel".Translate().Resolve();
                case ColumnType.AutoRepair:
                    return value ? "On".Translate().Resolve() : "Off".Translate().Resolve();
                default:
                    return value
                        ? "RimWorldAccess.Mechs.Paint.Checked".Translate().ToString()
                        : "RimWorldAccess.Mechs.Paint.Unchecked".Translate().ToString();
            }
        }

        // === Available Areas ===

        public static List<Area> GetAvailableAreas()
        {
            if (Find.CurrentMap == null) return new List<Area>();
            return Find.CurrentMap.areaManager.AllAreas
                .Where(a => a.AssignableAsAllowed())
                .ToList();
        }

        // === Individual Column Accessors ===

        private static string GetEnergy(Pawn pawn)
        {
            if (pawn.IsGestating())
                return "Gestating".Translate().Resolve();

            if (pawn.needs?.energy == null)
                return "RimWorldAccess.Mechs.Value.NotApplicable".Translate().ToString();

            return $"{Mathf.RoundToInt(pawn.needs.energy.CurLevel)} / {Mathf.RoundToInt(pawn.needs.energy.MaxLevel)}";
        }

        private static string GetDraftStatus(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || !pawn.IsColonyMechPlayerControlled || !pawn.Spawned)
                return "RimWorldAccess.Mechs.Value.NotApplicable".Translate().ToString();

            AcceptanceReport canDraft = MechanitorUtility.CanDraftMech(pawn);
            if (!canDraft && !canDraft.Reason.NullOrEmpty())
                return canDraft.Reason;

            return pawn.Drafted
                ? "CommandDraftLabel".Translate().Resolve()
                : "CommandUndraftLabel".Translate().Resolve();
        }

        private static string GetAutoRepairStatus(Pawn pawn)
        {
            CompMechRepairable comp = pawn.GetComp<CompMechRepairable>();
            if (comp == null)
                return "RimWorldAccess.Mechs.Value.NotApplicable".Translate().ToString();
            return comp.autoRepair ? "On".Translate().Resolve() : "Off".Translate().Resolve();
        }

        private static string GetOverseerName(Pawn pawn)
        {
            Pawn overseer = pawn.GetOverseer();
            return overseer?.LabelShort ?? "None".Translate().Resolve();
        }

        private static string GetControlGroupValue(Pawn pawn)
        {
            if (pawn.IsGestating())
                return "Gestating".Translate().Resolve();

            MechanitorControlGroup group = pawn.GetMechControlGroup();
            if (group == null)
                return "None".Translate().Resolve();

            return group.LabelIndexWithWorkMode;
        }

        private static string GetWorkModeValue(Pawn pawn)
        {
            MechanitorControlGroup group = pawn.GetMechControlGroup();
            if (group == null)
                return "RimWorldAccess.Mechs.Value.NotApplicable".Translate().ToString();

            return group.WorkMode.LabelCap.Resolve();
        }

        private static string GetAllowedAreaValue(Pawn pawn)
        {
            if (pawn.playerSettings == null)
                return "RimWorldAccess.Mechs.Value.NotApplicable".Translate().ToString();

            Area area = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
            return area?.Label ?? "NoAreaAllowed".Translate().Resolve();
        }
    }
}
