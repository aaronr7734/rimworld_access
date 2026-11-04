using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compositable Loadouts (packageId <c>Wiri.compositableloadouts</c>, assembly <c>Inventory</c>).
    /// Each feature slice implements one of the partial hooks below in its own file, so slices never
    /// edit this one; every hook runs only once the core <see cref="ClCompat"/> surface is ready.
    /// </summary>
    internal sealed partial class CompositableLoadoutsModule : CompatModule
    {
        public override string TargetPackageId => "Wiri.compositableloadouts";

        public override bool ShouldActivate =>
            ModsConfig.IsActive(TargetPackageId)
            || AccessTools.TypeByName("Inventory.LoadoutComponent") != null;

        public override void Activate(Harmony harmony)
        {
            if (!ClCompat.Ready)
            {
                return;
            }

            if (ClCompat.Columns.Ready)
            {
                PawnColumnHandlerRegistry.Register(ClCompat.Columns.LoadoutStateWorkerType, new ClLoadoutStateColumnHandler());
                PawnColumnHandlerRegistry.Register(ClCompat.Columns.TagSummaryWorkerType, new ClTagSummaryColumnHandler());
            }

            PawnGearSectionRegistry.Register(ClGearSection.Build);

            if (ClCompat.Dialogs.Ready)
            {
                ScopeForWindow.Register(ClCompat.TagSelectorWindowType, w => new ClTagSelectorScope(w));
                ScopeForWindow.Register(ClCompat.SetTagLoadoutStateWindowType, w => new ClTagStatePopupScope(w));
                ScopeForWindow.Register(ClCompat.LoadoutStateEditorWindowType, w => new ClStateEditorScope(w));
                ClTagStatePopupVanishPatch.Install(harmony);
                ClTextFieldMirrorPatch.Install(harmony);
            }

            // A warning list with Confirm and Deny buttons; the generic reader covers it, but its
            // window posture keeps the reader from attaching on its own.
            ScopeForWindow.TryRegisterGenericReaderForWindow("Inventory.Dialog_ConfirmSettings");

            ActivateLoadoutEditor(harmony);
            ActivateTagEditor(harmony);
            ActivateOverview(harmony);
            ActivateBillsAndSettings(harmony);
            ActivateItemSpecifier(harmony);
        }

        partial void ActivateLoadoutEditor(Harmony harmony);

        partial void ActivateTagEditor(Harmony harmony);

        partial void ActivateOverview(Harmony harmony);

        partial void ActivateBillsAndSettings(Harmony harmony);

        partial void ActivateItemSpecifier(Harmony harmony);
    }
}
