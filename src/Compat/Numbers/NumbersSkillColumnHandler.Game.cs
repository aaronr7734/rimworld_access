using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' generated skill columns: the level or disabled state, the passion
    /// flame, and the vanilla skill tooltip the cell carries.
    /// </summary>
    internal sealed class NumbersSkillColumnHandler : PawnColumnHandler
    {
        private readonly NumbersColumnExtension extension;

        private NumbersSkillColumnHandler(NumbersColumnExtension extension)
        {
            this.extension = extension;
        }

        public static void Register(NumbersColumnExtension extension)
        {
            CompatRegistration.PawnColumnHandler("Numbers.PawnColumnWorker_Skill", _ => new NumbersSkillColumnHandler(extension));
        }

        private SkillRecord SkillFor(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.RaceProps.IsMechanoid || pawn.RaceProps.Animal || pawn.skills == null)
            {
                return null;
            }
            return pawn.skills.GetSkill(extension.Skill(def));
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            SkillRecord skill = SkillFor(def, pawn);
            if (skill == null)
            {
                return "";
            }
            string level = skill.TotallyDisabled
                ? "DisabledLower".Translate().ToString()
                : skill.GetLevelForUI().ToStringCached();
            string passion = PawnSkillsTableHelper.PassionLabel(skill.passion);
            return string.IsNullOrEmpty(passion) ? level : level + ", " + passion;
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            SkillRecord skill = SkillFor(def, pawn);
            if (skill == null || PawnColumnWorker_AccessSkill.SkillDescription == null)
            {
                return null;
            }
            string tip = PawnColumnWorker_AccessSkill.SkillDescription(skill);
            return string.IsNullOrEmpty(tip) ? null : SpeechFlatten.ToSentences(tip.StripTags());
        }
    }
}
