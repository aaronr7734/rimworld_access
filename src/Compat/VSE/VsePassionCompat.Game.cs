using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Vanilla Skills Expanded's passion effects under each skill in the Skills inspection tree:
    /// the learn and forget rates VSE adds to the skill tooltip, with the passion's description one
    /// level down. Passion names themselves come from vanilla's <c>SkillUI.GetLabel</c>, which VSE
    /// prefixes, so the shared passion rows already speak them.
    /// </summary>
    internal static class VsePassionCompat
    {
        private static readonly FieldInfo passionsField;
        private static readonly FieldInfo learnRateField;
        private static readonly FieldInfo forgetRateField;
        private static readonly FieldInfo learnRateOtherField;
        private static readonly bool ready;

        static VsePassionCompat()
        {
            var surface = new ReflectionSurface("VsePassionCompat");
            Type managerType = surface.Type("VSE.Passions.PassionManager");
            Type passionDefType = surface.Type("VSE.Passions.PassionDef");
            passionsField = surface.Field(managerType, "Passions");
            learnRateField = surface.Field(passionDefType, "learnRateFactor");
            forgetRateField = surface.Field(passionDefType, "forgetRateFactor");
            learnRateOtherField = surface.Field(passionDefType, "learnRateFactorOther");
            ready = surface.Ready;
        }

        public static void Register()
        {
            if (ready)
                PawnSkillsAdapter.RegisterDetailExtender(BuildPassionRow);
        }

        private static void BuildPassionRow(InspectionTreeItem skillItem, Pawn pawn, SkillRecord skill)
        {
            // VSE's own tooltip addition skips the no-passion entry too (VSE_SkillUI_GetSkillDescription_Patch).
            if (skill == null || skill.passion == Passion.None)
                return;

            try
            {
                Def def = PassionDef(skill.passion);
                if (def == null)
                    return;

                string learn = ((float)learnRateField.GetValue(def)).ToStringPercent("F0");
                string forget = ((float)forgetRateField.GetValue(def)).ToStringPercent("F0");
                float other = (float)learnRateOtherField.GetValue(def);
                string label = Mathf.Approximately(other, 1f)
                    ? "RimWorldAccess.Compat.Vse.PassionRates".Translate(learn, forget).ToString()
                    : "RimWorldAccess.Compat.Vse.PassionRatesOther".Translate(learn, forget, other.ToStringPercent("F0")).ToString();

                var item = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    ExpandedLabel = label,
                    IndentLevel = skillItem.IndentLevel + 1,
                    IsExpandable = true,
                    IsExpanded = false
                };
                InspectNodeFactory.DetailLines(item, def.description);
                item.IsExpandable = item.Children.Count > 0;
                InspectNodeFactory.Attach(skillItem, item);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("VsePassionCompat.BuildPassionRow", ex);
            }
        }

        private static Def PassionDef(Passion passion)
        {
            var passions = passionsField.GetValue(null) as Array;
            int index = (int)passion;
            return passions != null && index < passions.Length ? passions.GetValue(index) as Def : null;
        }
    }
}
