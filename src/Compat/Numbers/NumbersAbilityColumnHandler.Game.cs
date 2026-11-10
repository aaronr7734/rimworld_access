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
            CompatRegistration.PawnColumnHandler("Numbers.PawnColumnWorker_Ability", _ => new NumbersAbilityColumnHandler(extension));
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            AbilityDef abilityDef = extension.Ability(def);
            return abilityDef != null && pawn.abilities?.GetAbility(abilityDef) != null
                ? abilityDef.LabelCap.ToString()
                : "";
        }
    }
}
