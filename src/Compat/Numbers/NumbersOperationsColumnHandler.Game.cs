using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' per-row Add bill button. Enter opens the worker's own surgery option list
    /// (<c>RecipeOptionsMaker</c>, built from vanilla's GenerateSurgeryOption), so the cell owns its
    /// button rather than leaving it to surface as a stray extra.
    /// </summary>
    internal sealed class NumbersOperationsColumnHandler : PawnColumnHandler
    {
        private readonly MethodInfo recipeOptionsMaker;

        private NumbersOperationsColumnHandler(MethodInfo recipeOptionsMaker)
        {
            this.recipeOptionsMaker = recipeOptionsMaker;
        }

        public static void Register()
        {
            var surface = new ReflectionSurface("NumbersOperationsColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_OperationDropDown");
            MethodInfo optionsMaker = surface.Method(workerType, "RecipeOptionsMaker", new[] { typeof(Pawn) });
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersOperationsColumnHandler(optionsMaker));
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return "AddBill".Translate().ToString();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            var options = recipeOptionsMaker.Invoke(null, new object[] { pawn }) as List<FloatMenuOption>;
            if (options == null || options.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData("RimWorldAccess.Compat.Numbers.NoOperations".Loc().ToString());
                return PawnColumnActivation.OpenedUI;
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }
}
