using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers Vanilla Expanded Framework's own dialogs against their scopes (
    /// <c>VEF.Planet.Dialog_Hire</c> via <see cref="VefHireCompat"/>/<see cref="VefHireScope"/>,
    /// <c>VEF.Storyteller.Window_Contracts</c> via <see cref="VefContractsCompat"/>/
    /// <see cref="VefContractsScope"/>, and <c>VEF.Graphics.Dialog_GraphicCustomization</c> via
    /// <see cref="VefGraphicCustomizationCompat"/>/<see cref="VefGraphicCustomizationScope"/>,
    /// and <c>VEF.Memes.Dialog_FloatMenuOptions</c> via <see cref="VefPreceptOptionsCompat"/>/
    /// <see cref="VefPreceptOptionsScope"/>), mirroring <see cref="VfDialogCompat"/>'s hub for
    /// Vehicle Framework.
    /// </summary>
    internal static class VefDialogCompat
    {
        public static void RegisterDialogScopes()
        {
            if (VefHireCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefHireCompat.DialogHireType, delegate (Window w)
                {
                    return new VefHireScope(w);
                });
            }

            if (VefContractsCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefContractsCompat.WindowType, delegate (Window w)
                {
                    return new VefContractsScope(w);
                });
            }

            if (VefGraphicCustomizationCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefGraphicCustomizationCompat.DialogType, delegate (Window w)
                {
                    return new VefGraphicCustomizationScope(w);
                });
            }

            if (VefCreateOutpostCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefCreateOutpostCompat.DialogType, delegate (Window w)
                {
                    return new VefCreateOutpostScope(w);
                });
            }

            if (VefPreceptOptionsCompat.Ready)
            {
                ScopeForWindow.RegisterHierarchy(VefPreceptOptionsCompat.DialogType, delegate (Window w)
                {
                    return new VefPreceptOptionsScope(w);
                });
            }
        }
    }
}
