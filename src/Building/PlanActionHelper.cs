using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Surfaces a plan's gizmos for the accessible G-key navigation. This is driven by the plan's
    /// own <see cref="Plan.GetGizmos"/> — so the real, game-localized gizmos (and any a mod adds)
    /// flow through unchanged — and only patches the few that aren't keyboard-usable as-is:
    /// the change-color command (vanilla opens a label-less swatch grid) gets our accessible color
    /// picker, the mouse-bound copy / copy-selection designators are dropped in favour of a keyboard
    /// clipboard copy, and a Rename action (vanilla has none — it lives on the inspect pane) is
    /// appended. The hide / expand / shrink / delete gizmos keep the game's own behaviour and tooltips.
    /// </summary>
    public static class PlanActionHelper
    {
        private static Texture2D changeColorIcon;

        public static List<Gizmo> BuildGizmos(Plan plan)
        {
            if (changeColorIcon == null)
                changeColorIcon = ContentFinder<Texture2D>.Get("UI/Designators/PlanChangeColor");

            var gizmos = new List<Gizmo>();
            foreach (var gizmo in plan.GetGizmos())
            {
                if (gizmo == null)
                    continue;

                // Mouse-bound copy tools (they paste at UI.MouseCell): replaced by clipboard copy below.
                if (gizmo is Designator_Plan_Copy || gizmo is Designator_Plan_CopySelection)
                    continue;

                // Hide is a Command_Toggle that flips THIS plan; the nav announces its on/off state.
                // Plan.GetGizmos news up a fresh instance, so we can safely swap its verbose,
                // mouse-referencing description for a clean one.
                if (gizmo is Command_Hide_Plans hide)
                {
                    hide.defaultDesc = "RimWorldAccess.Building.Plan.Desc.ToggleVisibility".Translate();
                    gizmos.Add(hide);
                    continue;
                }

                // Expand's gizmo is the shared architect designator with a verbose inherited
                // description we must not mutate; use a fresh expand designator (same behaviour,
                // pre-set to this plan's color) with a clean description instead. It keeps the game's
                // Misc6 hotkey, which it shares with Shrink — that's the game's own assignment, and
                // the gizmo navigation activates the first match, so the clash is harmless.
                if (gizmo is Designator_Plan_Expand)
                {
                    var expand = new Designator_Plan_Expand();
                    expand.Initialize(plan.Color);
                    expand.defaultDesc = "RimWorldAccess.Building.Plan.Desc.Expand".Translate();
                    gizmos.Add(expand);
                    continue;
                }

                // Change color: keep the game's label/description/icon, but swap the label-less
                // swatch-grid action for our accessible named picker. (Fresh Command_Action instance.)
                if (gizmo is Command_Action colorCmd && colorCmd.icon == changeColorIcon)
                {
                    colorCmd.action = () => PlanColorHelper.OpenColorPickerForPlan(plan);
                    gizmos.Add(colorCmd);
                    continue;
                }

                // Delete: keep the game's label/description/icon, but route through our version so the
                // result is announced to the screen reader.
                if (gizmo is Command_Action deleteCmd && deleteCmd.icon == TexButton.Delete)
                {
                    deleteCmd.action = () => Delete(plan);
                    gizmos.Add(deleteCmd);
                    continue;
                }

                // Shrink and anything a mod added: keep verbatim.
                gizmos.Add(gizmo);
            }

            // Keyboard copy (clipboard) using the game's own copy label/description plus our paste
            // hint, then a Rename action (no vanilla gizmo for it).
            gizmos.Add(new Command_Action
            {
                defaultLabel = "CommandCopyPlanLabel".Translate(),
                defaultDesc = "CommandCopyPlanDesc".Translate().ToString() + " "
                    + "RimWorldAccess.Building.Plan.PasteHint".Translate().ToString() + " "
                    + "RimWorldAccess.Building.Plan.PasteRelativeHint".Translate().ToString(),
                action = () => PlanClipboard.Copy(plan)
            });
            gizmos.Add(new Command_Action
            {
                defaultLabel = "RimWorldAccess.Inspection.CategoryName.PlanRename".Translate(),
                defaultDesc = "RimWorldAccess.Building.Plan.Desc.Rename".Translate(),
                action = () => PlanRenameState.Open(plan)
            });
            return gizmos;
        }

        /// <summary>Deletes the plan entirely and announces it.</summary>
        public static void Delete(Plan plan)
        {
            if (plan == null) return;
            string name = plan.RenamableLabel;
            plan.Delete();
            TolkHelper.Speak("RimWorldAccess.Building.Plan.Deleted".Loc(name), SpeechPriority.High);
        }
    }
}
