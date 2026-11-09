using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Choice Of Psycasts' LearnPsycasts gizmo and the float-menu options it builds. Each psycast
    /// option is a plain FloatMenuOption whose action closure captures the AbilityDef and the
    /// gizmo, so the closure class is the only link from an option back to its psycast.
    /// </summary>
    internal static class CopReflection
    {
        internal static Type LearnPsycastsType;
        internal static FieldInfo LevelField;
        internal static FieldInfo RangeField;
        internal static FieldInfo ParentField;
        internal static FieldInfo RangeLowField;
        internal static FieldInfo RangeHighField;

        /// <summary>Closure class to its captured (AbilityDef, LearnPsycasts) fields.</summary>
        private static Dictionary<Type, KeyValuePair<FieldInfo, FieldInfo>> optionClosures;

        internal static readonly LazyReflectionGate Gate =
            new LazyReflectionGate("Choice Of Psycasts", surface =>
            {
                Type learn = surface.Type("ChoiceOfPsycasts.LearnPsycasts");
                Type range = surface.Type("ChoiceOfPsycasts.LevelRange");
                FieldInfo level = surface.Field(learn, "Level");
                FieldInfo rangeField = surface.Field(learn, "Range");
                FieldInfo parent = surface.Field(learn, "Parent");
                FieldInfo low = surface.Field(range, "low");
                FieldInfo high = surface.Field(range, "high");
                Dictionary<Type, KeyValuePair<FieldInfo, FieldInfo>> closures =
                    learn == null ? null : FindOptionClosures(learn);
                surface.Required("LearnPsycasts option closure", closures);
                if (!surface.Ready)
                    return false;

                LearnPsycastsType = learn;
                LevelField = level;
                RangeField = rangeField;
                ParentField = parent;
                RangeLowField = low;
                RangeHighField = high;
                optionClosures = closures;
                return true;
            });

        /// <summary>
        /// Matched by field types rather than compiler-generated names, which change with every
        /// recompile of the mod.
        /// </summary>
        private static Dictionary<Type, KeyValuePair<FieldInfo, FieldInfo>> FindOptionClosures(Type learn)
        {
            var result = new Dictionary<Type, KeyValuePair<FieldInfo, FieldInfo>>();
            foreach (Type nested in learn.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                FieldInfo ability = null;
                FieldInfo owner = null;
                foreach (FieldInfo field in nested.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.FieldType == typeof(AbilityDef))
                    {
                        ability = field;
                    }
                    else if (field.FieldType == learn)
                    {
                        owner = field;
                    }
                }
                if (ability != null && owner != null)
                {
                    result[nested] = new KeyValuePair<FieldInfo, FieldInfo>(ability, owner);
                }
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>The psycast a menu option teaches and the gizmo that built it; false for any other option.</summary>
        internal static bool TryReadOption(FloatMenuOption option, out AbilityDef ability, out object gizmo)
        {
            ability = null;
            gizmo = null;
            object closure = option?.action?.Target;
            KeyValuePair<FieldInfo, FieldInfo> fields;
            if (closure == null || !Gate.Ensure() || !optionClosures.TryGetValue(closure.GetType(), out fields))
            {
                return false;
            }
            ability = Guarded.FieldOf<AbilityDef>(fields.Key, closure, "Choice Of Psycasts option ability");
            gizmo = Guarded.FieldOf<object>(fields.Value, closure, "Choice Of Psycasts option gizmo");
            return ability != null && gizmo != null;
        }

        internal static Pawn PawnOf(object gizmo)
        {
            return Guarded.FieldOf<Pawn>(ParentField, gizmo, "Choice Of Psycasts gizmo pawn");
        }

        /// <summary>
        /// The psylink levels a gizmo offers. Each constructor leaves the other field at its
        /// default, so a range with a zero top means the single-level form.
        /// </summary>
        internal static void LevelsOf(object gizmo, out int low, out int high)
        {
            object range = Guarded.FieldOf<object>(RangeField, gizmo, "Choice Of Psycasts gizmo range");
            high = Guarded.FieldOf(RangeHighField, range, "Choice Of Psycasts range high", 0);
            if (high > 0)
            {
                low = Guarded.FieldOf(RangeLowField, range, "Choice Of Psycasts range low", 0);
                return;
            }
            low = high = Guarded.FieldOf(LevelField, gizmo, "Choice Of Psycasts gizmo level", 0);
        }
    }
}
