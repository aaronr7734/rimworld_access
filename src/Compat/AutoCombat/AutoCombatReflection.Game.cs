using System;
using System.Collections;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Drafted Auto-Combat's per-pawn auto-cast state: DraftedActionHolder.GetData(pawn) and the
    /// DraftedActionData members its GizmoOnGUIInt postfix reads and writes.
    /// </summary>
    internal static class AutoCombatReflection
    {
        private static MethodInfo isDraftedPlayerPawnMethod;
        private static MethodInfo getDataMethod;
        private static MethodInfo autoCastForMethod;
        private static MethodInfo toggleAutoCastForMethod;
        private static FieldInfo autocastAbilitiesField;

        internal static Type ToggleWithRClickType { get; private set; }

        internal static bool Resolve()
        {
            var surface = new ReflectionSurface("AutoCombatReflection");
            Type draftGizmosType = surface.Type("BigAndSmall.DraftGizmos");
            Type holderType = surface.Type("BigAndSmall.DraftedActionHolder");
            Type dataType = surface.Type("BigAndSmall.DraftedActionData");
            ToggleWithRClickType = surface.Type("BigAndSmall.Command_ToggleWithRClick");

            isDraftedPlayerPawnMethod = surface.Method(draftGizmosType, "IsDraftedPlayerPawn", new[] { typeof(Pawn) });
            getDataMethod = surface.Method(holderType, "GetData", new[] { typeof(Pawn) });
            autoCastForMethod = surface.Method(dataType, "AutoCastFor", new[] { typeof(AbilityDef) });
            toggleAutoCastForMethod = surface.Method(dataType, "ToggleAutoCastFor", new[] { typeof(AbilityDef) });
            autocastAbilitiesField = surface.Field(dataType, "autocastAbilities");
            return surface.Ready;
        }

        internal static bool IsDraftedPlayerPawn(Pawn pawn)
        {
            return pawn != null && (bool)isDraftedPlayerPawnMethod.Invoke(null, new object[] { pawn });
        }

        internal static object GetData(Pawn pawn)
        {
            return getDataMethod.Invoke(null, new object[] { pawn });
        }

        internal static bool AutoCastFor(object data, AbilityDef def)
        {
            return (bool)autoCastForMethod.Invoke(data, new object[] { def });
        }

        internal static void ToggleAutoCastFor(object data, AbilityDef def)
        {
            toggleAutoCastForMethod.Invoke(data, new object[] { def });
        }

        internal static int AutoCastCount(object data)
        {
            return (autocastAbilitiesField.GetValue(data) as ICollection)?.Count ?? 0;
        }
    }
}
