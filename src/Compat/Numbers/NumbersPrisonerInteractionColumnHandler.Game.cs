using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' prisoner interaction radio strip: the cell reads the selected mode; Enter offers
    /// every mode the strip draws, in its order, with the cursor on the current one.
    /// </summary>
    internal sealed class NumbersPrisonerInteractionColumnHandler : PawnColumnHandler
    {
        public static void Register()
        {
            NumbersModule.RegisterWorker("Numbers.PawnColumnWorker_PrisonerInteraction", new NumbersPrisonerInteractionColumnHandler());
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.guest == null || pawn.guest.ExclusiveInteractionMode == null)
            {
                return "";
            }
            return pawn.guest.ExclusiveInteractionMode.LabelCap.ToString();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.guest == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            List<PrisonerInteractionModeDef> modes = DefDatabase<PrisonerInteractionModeDef>.AllDefsListForReading
                .OrderBy(m => m.listOrder).ToList();
            var options = new List<FloatMenuOption>(modes.Count);
            foreach (PrisonerInteractionModeDef mode in modes)
            {
                PrisonerInteractionModeDef chosen = mode;
                // SetExclusiveInteraction refuses non-exclusive modes, so the strip's radio for one does nothing.
                Action apply = mode.isNonExclusiveInteraction ? (Action)null : () => PrisonerTabState.SetExclusiveMode(pawn, chosen);
                options.Add(new FloatMenuOption(mode.LabelCap, apply));
            }
            int current = modes.IndexOf(pawn.guest.ExclusiveInteractionMode);
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: Math.Max(0, current),
                titleText: def.LabelCap.ToString());
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
