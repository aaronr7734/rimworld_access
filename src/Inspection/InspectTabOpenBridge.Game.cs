using System;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <c>InspectPaneUtility.OpenTab</c> is the game's shared "show me this thing's tab" vehicle:
    /// vanilla drives it from death letters, the character card's genes and social links, the
    /// health card's combat log, book contents and growth vats, and a mod with a button that jumps
    /// to its own ITab calls the same method. This shell reads an inspect tab through the
    /// windowless tree rather than the drawn pane, so opening a tab has to open the tree on it.
    /// That is why a mod button of this shape needs no wiring in its compat module.
    ///
    /// Stands down for the gene tabs, which <see cref="GeneInspectionPatch"/> claims for their own
    /// state, and under the info card or any foreground dialog, where the caller is a link inside
    /// a modal the player is still driving. Also stands down for an open fired from inside
    /// <c>Selector.Select</c>: storage mods (Adaptive Storage Framework, LWM Deep Storage) open
    /// their contents tab from a selection postfix, and the shell selects things constantly
    /// (map cursor, tree rows, selection restore on close), so treating those as intent would
    /// hijack the tree on every select. The drawn pane still opens the tab for the viewer.
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), "OpenTab")]
    public static class InspectTabOpenBridgePatch
    {
        private static int selectDepth;

        [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
        private static class SelectScopePatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                selectDepth++;
            }

            [HarmonyFinalizer]
            private static Exception Finalizer(Exception __exception)
            {
                selectDepth--;
                return __exception;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Type inspectTabType, InspectTabBase __result)
        {
            try
            {
                // A null result means no such tab on the selected thing, so nothing opened.
                if (__result == null || inspectTabType == null
                    || selectDepth > 0
                    || Current.ProgramState != ProgramState.Playing
                    || InfoCardState.IsActive
                    || ShellGuards.ForeignDialogWindowAbove())
                {
                    return;
                }

                // Postfix order between two patches on one method is not guaranteed, so the gene
                // tabs are excluded by type rather than by that patch's state flag alone.
                if (typeof(ITab_Genes).IsAssignableFrom(inspectTabType)
                    || typeof(ITab_GenesPregnancy).IsAssignableFrom(inspectTabType)
                    || GeneInspectionState.IsActive)
                {
                    return;
                }

                Thing thing = Find.Selector?.SingleSelectedThing;
                if (thing == null)
                {
                    return;
                }

                if (WindowlessInspectionState.IsActive
                    && WindowlessInspectionState.TryLandOnTab(inspectTabType))
                {
                    return;
                }
                WindowlessInspectionState.OpenForObjectOnTab(thing, inspectTabType);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Inspect tab open bridge", ex);
            }
        }
    }
}
