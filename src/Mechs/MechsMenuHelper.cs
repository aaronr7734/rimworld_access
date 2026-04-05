using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    public static class MechsMenuHelper
    {
        public enum ColumnType
        {
            Name,           // LabelWithIcon
            Energy,         // Energy bar
            Draft,          // DraftMech checkbox
            AutoRepair,     // Auto-repair checkbox
            Overseer,       // Mechanitor name
            ControlGroup,   // Dropdown for control group
            WorkMode,       // Work mode label
            AllowedArea     // Area restriction
        }

        private const int TotalColumns = 8;

        // === Column Defs (for sorting via game logic) ===
        private static List<PawnColumnDef> columnDefs;

        private static readonly Dictionary<ColumnType, string> columnTypeToDefName = new Dictionary<ColumnType, string>
        {
            { ColumnType.Name, "LabelWithIcon" },
            { ColumnType.Energy, "Energy" },
            { ColumnType.Draft, "DraftMech" },
            { ColumnType.AutoRepair, "AutoRepair" },
            { ColumnType.Overseer, "Overseer" },
            { ColumnType.ControlGroup, "ControlGroup" },
            { ColumnType.WorkMode, "WorkMode" },
            { ColumnType.AllowedArea, "AllowedAreaMech" },
        };

        public static void InitColumnDefs()
        {
            columnDefs = new List<PawnColumnDef>();
            foreach (ColumnType ct in System.Enum.GetValues(typeof(ColumnType)))
            {
                columnDefs.Add(DefDatabase<PawnColumnDef>.GetNamedSilentFail(columnTypeToDefName[ct]));
            }
        }

        public static bool IsColumnSortable(int columnIndex)
            => PawnColumnSortHelper.IsColumnSortable(columnDefs, columnIndex);

        public static int GetTotalColumnCount() => TotalColumns;

        // === Row Label (for typeahead search) ===

        public static string GetMechName(Pawn pawn)
        {
            string name = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.LabelCap.ToString();
            return $"{name} ({pawn.def.LabelCap})";
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
            if (columnIndex < 0 || columnIndex >= TotalColumns)
                return "Unknown";

            ColumnType type = (ColumnType)columnIndex;
            switch (type)
            {
                case ColumnType.Name:
                    return "Name".Translate().Resolve();
                case ColumnType.AllowedArea:
                    return "AllowedArea".Translate().Resolve();
                default:
                    // Use the PawnColumnDef label (already localized by the game)
                    if (columnDefs != null && columnIndex < columnDefs.Count && columnDefs[columnIndex] != null)
                        return columnDefs[columnIndex].label.CapitalizeFirst();
                    return type.ToString();
            }
        }

        // === Column Values ===

        public static string GetColumnValue(Pawn pawn, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= TotalColumns)
                return "Unknown";

            switch ((ColumnType)columnIndex)
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
                    return "Unknown";
            }
        }

        // === Column Tooltips ===

        public static string GetColumnTooltip(Pawn pawn, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= TotalColumns)
                return null;

            switch ((ColumnType)columnIndex)
            {
                case ColumnType.Draft:
                    AcceptanceReport canDraft = MechanitorUtility.CanDraftMech(pawn);
                    if (!canDraft && !canDraft.Reason.NullOrEmpty())
                        return canDraft.Reason;
                    return null;
                case ColumnType.AutoRepair:
                    return "CommandAutoRepairDesc".Translate().Resolve();
                case ColumnType.WorkMode:
                    return "ClickToChangeWorkMode".Translate().Resolve();
                case ColumnType.AllowedArea:
                    // From AllowedAreaMech XML headerTip
                    var def = columnDefs != null && columnIndex < columnDefs.Count ? columnDefs[columnIndex] : null;
                    return def?.headerTip;
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
            if (columnIndex < 0 || columnIndex >= TotalColumns)
                return false;

            switch ((ColumnType)columnIndex)
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
            if (columnIndex < 0 || columnIndex >= TotalColumns)
                return false;

            switch ((ColumnType)columnIndex)
            {
                case ColumnType.Draft:       // paintable=true in XML
                case ColumnType.AutoRepair:  // paintable=true in XML
                case ColumnType.ControlGroup: // paintable=true in XML
                case ColumnType.AllowedArea: // mod lastAppliedArea mechanism
                    return true;
                default:
                    return false;
            }
        }

        public static bool GetPaintableValue(Pawn pawn, int columnIndex)
        {
            switch ((ColumnType)columnIndex)
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
            switch ((ColumnType)columnIndex)
            {
                case ColumnType.Draft:
                    if (!ModsConfig.BiotechActive || !pawn.IsColonyMechPlayerControlled || !pawn.Spawned)
                        return false;
                    AcceptanceReport canDraft = MechanitorUtility.CanDraftMech(pawn);
                    if (!canDraft) return false;
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
            switch ((ColumnType)columnIndex)
            {
                case ColumnType.Draft:
                    return value
                        ? "CommandDraftLabel".Translate().Resolve()
                        : "CommandUndraftLabel".Translate().Resolve();
                case ColumnType.AutoRepair:
                    return value ? "On".Translate().Resolve() : "Off".Translate().Resolve();
                default:
                    return value ? "checked" : "unchecked";
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
                return "N/A";

            return $"{Mathf.RoundToInt(pawn.needs.energy.CurLevel)} / {Mathf.RoundToInt(pawn.needs.energy.MaxLevel)}";
        }

        private static string GetDraftStatus(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || !pawn.IsColonyMechPlayerControlled || !pawn.Spawned)
                return "N/A";

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
                return "N/A";
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
                return "N/A";

            return group.WorkMode.LabelCap.Resolve();
        }

        private static string GetAllowedAreaValue(Pawn pawn)
        {
            if (pawn.playerSettings == null)
                return "N/A";

            Area area = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
            return area?.Label ?? "Unrestricted".Translate().Resolve();
        }
    }
}
