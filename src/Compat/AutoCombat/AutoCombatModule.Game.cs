using HarmonyLib;

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
            // By name: the byType slot for Command_Ability belongs to the core handler.
            GizmoHandlerRegistry.RegisterByTypeName("Command_Ability", new AutoCombatAbilityHandler());
            RightClickOutcomeRegistry.Register(new AutoCombatRightClickDescriber());
        }
    }
}
