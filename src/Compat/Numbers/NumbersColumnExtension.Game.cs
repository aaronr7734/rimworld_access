using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reads the def each generated Numbers column is about (its need, skill or ability) off the
    /// column def's <c>DefModExtension_PawnColumnDefs</c>, the same source the mod's workers read.
    /// </summary>
    internal sealed class NumbersColumnExtension
    {
        private readonly Type extensionType;
        private readonly FieldInfo needField;
        private readonly FieldInfo skillField;
        private readonly FieldInfo abilityField;

        private NumbersColumnExtension(Type extensionType, FieldInfo needField, FieldInfo skillField, FieldInfo abilityField)
        {
            this.extensionType = extensionType;
            this.needField = needField;
            this.skillField = skillField;
            this.abilityField = abilityField;
        }

        public static NumbersColumnExtension TryResolve()
        {
            var surface = new ReflectionSurface("NumbersColumnExtension");
            Type type = surface.Type("Numbers.DefModExtension_PawnColumnDefs");
            FieldInfo need = surface.Field(type, "need");
            FieldInfo skill = surface.Field(type, "skill");
            FieldInfo ability = surface.Field(type, "ability");
            return surface.Ready ? new NumbersColumnExtension(type, need, skill, ability) : null;
        }

        public NeedDef Need(PawnColumnDef def)
        {
            return Read(def, needField) as NeedDef;
        }

        public SkillDef Skill(PawnColumnDef def)
        {
            return Read(def, skillField) as SkillDef;
        }

        public AbilityDef Ability(PawnColumnDef def)
        {
            return Read(def, abilityField) as AbilityDef;
        }

        private object Read(PawnColumnDef def, FieldInfo field)
        {
            if (def == null || def.modExtensions == null)
            {
                return null;
            }
            foreach (DefModExtension extension in def.modExtensions)
            {
                if (extensionType.IsInstanceOfType(extension))
                {
                    return field.GetValue(extension);
                }
            }
            return null;
        }
    }
}
