using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    internal sealed class AutoCombatModule : CompatModule
    {
        public override string TargetPackageId => "RedMattis.AutoCombat";

        public override void Activate(Harmony harmony)
        {
            if (!AutoCombatReflection.Resolve())
            {
                return;
            }
            GizmoHandlerRegistry.Register(typeof(Command_Ability), new AutoCombatAbilityHandler());
            // On the vanilla base, not the mod's subtype: the most-derived registration owns execution,
            // and ToggleGizmoHandler must keep executing and describing the toggle.
            GizmoHandlerRegistry.Register(typeof(Command_Toggle), new AutoCombatRightClickHandler());
        }
    }
}
