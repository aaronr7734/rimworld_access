using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
        private static readonly MethodInfo getSkillDescription = AccessTools.Method(typeof(SkillUI), "GetSkillDescription");

        private readonly NumbersColumnExtension extension;

        private NumbersSkillColumnHandler(NumbersColumnExtension extension)
        {
            this.extension = extension;
        }

        public static void Register(NumbersColumnExtension extension)
        {
            var surface = new ReflectionSurface("NumbersSkillColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_Skill");
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersSkillColumnHandler(extension));
            }
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
            var parts = new List<string>
            {
                skill.TotallyDisabled
                    ? "DisabledLower".Translate().ToString()
                    : skill.Level.ToStringCached(),
            };
            if (skill.passion == Passion.Major)
            {
                parts.Add("PassionMajor".Translate().ToString());
            }
            else if (skill.passion == Passion.Minor)
            {
                parts.Add("PassionMinor".Translate().ToString());
            }
            return string.Join(", ", parts);
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            SkillRecord skill = SkillFor(def, pawn);
            if (skill == null || getSkillDescription == null)
            {
                return null;
            }
            string tip = getSkillDescription.Invoke(null, new object[] { skill }) as string;
            return string.IsNullOrEmpty(tip) ? null : SpeechFlatten.ToSentences(tip.StripTags());
        }
    }
}
