using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Drafted Auto-Combat marks abilities for auto-cast only through an icon drawn on the ability
    /// gizmo, toggled by right-clicking it (DraftGizmos.GizmoOnGUIPostfix, on GizmoState.OpenedFloatMenu).
    /// This speaks the mark as the gizmo's status and offers the toggle as a right-bracket row.
    /// </summary>
    internal sealed class AutoCombatAbilityHandler : GizmoHandlerBase
    {
        // Mirrors the postfix's own preconditions: exact type (subclasses are excluded), drafted player pawn.
        private static bool Applies(Gizmo gizmo, out Command_Ability command, out object data)
        {
            command = null;
            data = null;
            if (gizmo.GetType() != typeof(Command_Ability))
                return false;
            command = (Command_Ability)gizmo;
            if (command.Ability?.def == null || !AutoCombatReflection.IsDraftedPlayerPawn(command.Pawn))
                return false;
            data = AutoCombatReflection.GetData(command.Pawn);
            return data != null;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            try
            {
                if (!Applies(gizmo, out Command_Ability command, out object data)
                    || !AutoCombatReflection.AutoCastFor(data, command.Ability.def))
                {
                    return false;
                }
                status = "RimWorldAccess.Compat.AutoCombat.AutoCastOn".Translate();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("AutoCombatAbilityHandler.TryGetStatus", ex);
                return false;
            }
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            // Command.GizmoOnGUIInt rejects clicks on a disabled command before the postfix sees a right-click.
            if (gizmo.Disabled)
                return false;
            try
            {
                if (!Applies(gizmo, out Command_Ability command, out object data))
                    return false;
                AbilityDef def = command.Ability.def;
                options.Add(new CheckboxFloatMenuOption(
                    "RimWorldAccess.Compat.AutoCombat.AutoCastOption".Translate(),
                    () => Toggle(data, def),
                    () => ReadState(data, def)));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("AutoCombatAbilityHandler.TryGetExtraOptions", ex);
                return false;
            }
        }

        // The same call the postfix's right-click branch makes.
        private static void Toggle(object data, AbilityDef def)
        {
            try
            {
                AutoCombatReflection.ToggleAutoCastFor(data, def);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("AutoCombatAbilityHandler.Toggle", ex);
            }
        }

        private static bool ReadState(object data, AbilityDef def)
        {
            try
            {
                return AutoCombatReflection.AutoCastFor(data, def);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("AutoCombatAbilityHandler.ReadState", ex);
                return false;
            }
        }
    }
}
