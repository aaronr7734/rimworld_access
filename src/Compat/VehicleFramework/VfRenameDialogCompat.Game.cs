using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for <c>Vehicles.Dialog_GiveVehicleName</c>: resolves the type once behind
    /// <see cref="Ready"/> and patches its <c>DoWindowContents</c> to bracket
    /// <see cref="VfRenameDialogScope"/>'s draw pass, mirroring <see cref="RenameDrawPatch"/> minus
    /// the <see cref="TextFieldRawPollGuard"/> lines (this dialog has no raw top-of-body Return
    /// poll to mask -- see the scope's class remarks). DoWindowContents is a concrete (non-generic)
    /// override declared directly on Dialog_GiveVehicleName, so a plain Harmony.Patch on the
    /// declaring type's method suffices; a modded subclass that overrides DoWindowContents without
    /// calling base would still escape this patch, an accepted limitation.
    /// </summary>
    internal static class VfRenameDialogCompat
    {
        private static readonly Type dialogType;
        private static readonly bool ready;

        public static Type DialogType => dialogType;
        public static bool Ready => ready;

        static VfRenameDialogCompat()
        {
            dialogType = AccessTools.TypeByName("Vehicles.Dialog_GiveVehicleName");
            ready = dialogType != null;
        }

        /// <summary>Called from VfDialogCompat.RegisterDialogScopes after the scope's own RegisterHierarchy succeeds. No-op unless Ready.</summary>
        public static void PatchDrawPass()
        {
            if (!ready)
            {
                return;
            }
            try
            {
                MethodInfo target = AccessTools.Method(dialogType, "DoWindowContents");
                if (target == null)
                {
                    ModLogger.Error("VfRenameDialogCompat: could not resolve Dialog_GiveVehicleName.DoWindowContents; declining draw-pass patch.");
                    return;
                }
                RimWorldAccessMod.HarmonyInstance.Patch(target,
                    prefix: new HarmonyMethod(typeof(VfRenameDialogCompat), nameof(DrawPrefix)),
                    finalizer: new HarmonyMethod(typeof(VfRenameDialogCompat), nameof(DrawFinalizer)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRenameDialogCompat.PatchDrawPass failed: {ex.Message}");
            }
        }

        public static void DrawPrefix(object __instance, out VfRenameDialogScope __state)
        {
            __state = null;
            try
            {
                // Resolved from anywhere on the stack, never off its top: a live edit
                // session's own scope sits above this one, and a top-only lookup would skip
                // the pass (and its MirrorLive) for exactly the frames the user is typing.
                VfRenameDialogScope scope = VfRenameDialogScope.OwningScope(__instance as Window);
                if (scope != null)
                {
                    __state = scope;
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("VF rename dialog draw pass", ex);
            }
        }

        public static void DrawFinalizer(Exception __exception, VfRenameDialogScope __state)
        {
            if (__state != null)
            {
                CapturePasses.Finish(__state, FocusStack.Contains(__state), __exception,
                    () => TextDialogCapture.EndPass(), s => s.OnGuiPass(), "VF rename dialog draw pass");
            }
        }
    }
}
