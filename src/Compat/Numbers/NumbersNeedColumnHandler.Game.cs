using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' generated need columns: a bare bar with change arrows and an instant-level marker,
    /// read as the fill percent, the arrow's direction, and the marker's level, behind the same
    /// early-outs <c>PawnColumnWorker_Need.DoCell</c> takes.
    /// </summary>
    internal sealed class NumbersNeedColumnHandler : PawnColumnHandler
    {
        private readonly NumbersColumnExtension extension;
        private readonly FieldInfo showMoreInfoField;

        private NumbersNeedColumnHandler(NumbersColumnExtension extension, FieldInfo showMoreInfoField)
        {
            this.extension = extension;
            this.showMoreInfoField = showMoreInfoField;
        }

        public static void Register(NumbersColumnExtension extension)
        {
            var surface = new ReflectionSurface("NumbersNeedColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_Need");
            Type settingsType = surface.Type("Numbers.Numbers_Settings");
            FieldInfo showMoreInfo = surface.Field(settingsType, "showMoreInfoThanVanilla");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersNeedColumnHandler(extension, showMoreInfo));
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.needs == null || pawn.RaceProps.IsMechanoid)
            {
                return "";
            }
            if (!(bool)showMoreInfoField.GetValue(null) && pawn.RaceProps.Animal && pawn.Faction == null)
            {
                return "";
            }
            Need need = pawn.needs.TryGetNeed(extension.Need(def));
            if (need == null)
            {
                return "";
            }
            var parts = new List<string> { need.CurLevelPercentage.ToStringPercent() };
            int arrow = need.GUIChangeArrow;
            if (arrow > 0)
            {
                parts.Add("RimWorldAccess.UI.GenericWindow.BarRising".Loc().ToString());
            }
            else if (arrow < 0)
            {
                parts.Add("RimWorldAccess.UI.GenericWindow.BarFalling".Loc().ToString());
            }
            float instant = need.CurInstantLevelPercentage;
            if (instant >= 0f)
            {
                parts.Add("RimWorldAccess.Compat.Numbers.TargetLevel".Loc(instant.ToStringPercent()).ToString());
            }
            return string.Join(", ", parts);
        }
    }
}
