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
            var surface = new ReflectionSurface("NumbersPrisonerInteractionColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_PrisonerInteraction");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersPrisonerInteractionColumnHandler());
            }
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
                Action apply = mode.isNonExclusiveInteraction ? (Action)null : () => Apply(pawn, chosen);
                options.Add(new FloatMenuOption(mode.LabelCap, apply));
            }
            int current = modes.IndexOf(pawn.guest.ExclusiveInteractionMode);
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: Math.Max(0, current),
                titleText: def.LabelCap.ToString());
            return PawnColumnActivation.OpenedUI;
        }

        private static void Apply(Pawn pawn, PrisonerInteractionModeDef mode)
        {
            if (pawn.guest == null)
            {
                return;
            }
            pawn.guest.SetExclusiveInteraction(mode);
            if (ModsConfig.IdeologyActive && pawn.guest.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.Convert
                && pawn.guest.ideoForConversion == null)
            {
                // MUTATION-C: mirrors Numbers PawnColumnWorker_PrisonerInteraction.DrawInteractionRadioButton; the write sits inline in a mouse-polled IMGUI branch with no callable method.
                pawn.guest.ideoForConversion = Faction.OfPlayer.ideos.PrimaryIdeo;
            }
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
