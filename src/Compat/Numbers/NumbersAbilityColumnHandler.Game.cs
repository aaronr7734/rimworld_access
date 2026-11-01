using System;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Numbers' generated ability columns draw the ability's icon when the pawn has it and nothing otherwise; the cell names the ability in the first case.</summary>
    internal sealed class NumbersAbilityColumnHandler : PawnColumnHandler
    {
        private readonly NumbersColumnExtension extension;

        private NumbersAbilityColumnHandler(NumbersColumnExtension extension)
        {
            this.extension = extension;
        }

        public static void Register(NumbersColumnExtension extension)
        {
            var surface = new ReflectionSurface("NumbersAbilityColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_Ability");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersAbilityColumnHandler(extension));
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            AbilityDef abilityDef = extension.Ability(def);
            if (abilityDef == null || pawn.abilities == null)
            {
                return "";
            }
            foreach (Ability ability in pawn.abilities.abilities)
            {
                if (ability.def == abilityDef)
                {
                    return abilityDef.LabelCap.ToString();
                }
            }
            return "";
        }
    }
}
