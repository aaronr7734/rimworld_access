using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Rows the hospitality suite's tab adapters (guest, restaurant, store) share on top of
    /// <see cref="InspectRows"/>: the mod's own keyed labels and the register tabs' common controls.
    /// </summary>
    internal static class HotelTreeRows
    {
        /// <summary>A Listing_Standard.LabelDouble row: the mod's own label key beside its value.</summary>
        public static string StatLine(string labelKey, string value)
        {
            return InspectRows.StatLine(CompatText.ModText(labelKey), value);
        }

        /// <summary>A button. A refusal with no click target keeps the row navigable and says why.</summary>
        public static InspectionTreeItem Button(InspectionTreeItem parent, string label, Func<string> tip,
            Action onActivate, InspectionMode mode, string disabledReason = null)
        {
            return InspectRows.Button(parent, label,
                () => CompatText.JoinSentences(new List<string> { disabledReason, tip == null ? null : tip() }),
                onActivate, mode, disabledReason != null);
        }

        /// <summary>The rename icon beside a register tab's name, opening the mod's own rename dialog.</summary>
        public static InspectionTreeItem RenameButton(InspectionTreeItem parent, string tipKey, Type dialogType,
            object target, InspectionMode mode)
        {
            InspectionTreeItem item = Button(parent, CompatText.Flatten(CompatText.ModText(tipKey)), null,
                () => Find.WindowStack.Add((Window)Activator.CreateInstance(dialogType, target)), mode);
            item.OpensOverlayMenu = true;
            return item;
        }

        /// <summary>A register tab's right column: one row opening its filter in the shared thing-filter screen.</summary>
        public static void FilterRow(InspectionTreeItem parent, string label, object data, Action open, InspectionMode mode)
        {
            if (mode == InspectionMode.ReadOnly)
            {
                return;
            }
            InspectNodeFactory.ActionRow(parent, label, data, open, opensOverlayMenu: true);
        }

        /// <summary>The register tabs' earned-yesterday and earned-today lines.</summary>
        public static void IncomeRows(InspectionTreeItem parent, object owner, FieldInfo yesterday, FieldInfo today)
        {
            InspectRows.Info(parent, () => StatLine("TabRegisterEarnedYesterday", ((float)yesterday.GetValue(owner)).ToStringMoney()));
            InspectRows.Info(parent, () => StatLine("TabRegisterEarnedToday", ((float)today.GetValue(owner)).ToStringMoney()));
        }

        /// <summary>The pawns behind a controller's pawn-list property.</summary>
        public static List<Pawn> Pawns(PropertyInfo property, object owner)
        {
            return property.GetValue(owner, null) is IEnumerable pawns ? pawns.OfType<Pawn>().ToList() : new List<Pawn>();
        }

        /// <summary>A pawn-list count whose tooltip names the pawns.</summary>
        public static void PawnCountRow(InspectionTreeItem parent, string labelKey, PropertyInfo property, object owner)
        {
            InspectRows.Info(parent, () => StatLine(labelKey, Pawns(property, owner).Count.ToString()),
                () => Names(Pawns(property, owner)));
        }

        public static string Names(List<Pawn> pawns)
        {
            return pawns.Select(p => p.LabelShort).ToCommaList();
        }
    }
}
