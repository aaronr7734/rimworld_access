using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The vanilla handler set behind <see cref="PawnColumnHandlerRegistry"/>. Cell values come from
    /// the game's OWN accessors — the worker's protected GetTextFor/GetValue/GetIconTip and the pawn's
    /// live trackers — via cached reflection, never re-derived display logic. MethodInfo.Invoke on a
    /// virtual method dispatches virtually, so one base-declared MethodInfo serves every subclass.
    /// </summary>
    internal abstract class PawnColumnHandler : IPawnColumnHandler
    {
        public virtual bool SkipColumn(PawnColumnDef def)
        {
            return false;
        }

        public virtual string HeaderLabel(PawnColumnDef def)
        {
            return null;
        }

        public virtual string CellText(PawnColumnDef def, Pawn pawn)
        {
            return "";
        }

        public virtual string CellTip(PawnColumnDef def, Pawn pawn)
        {
            return null;
        }

        public virtual string CellHint(PawnColumnDef def, Pawn pawn)
        {
            return null;
        }

        public virtual PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            return PawnColumnActivation.NotHandled;
        }

        public virtual bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return false;
        }

        public virtual bool CanAdjustCell(PawnColumnDef def)
        {
            return false;
        }

        public virtual PawnColumnActivation AdjustCell(PawnColumnDef def, Pawn pawn, int direction, PawnTable table)
        {
            return PawnColumnActivation.NotHandled;
        }

        /// <summary>
        /// Default TRUE: a typed handler fully reads and drives its own cell kind, so the cell's
        /// incidental captured hotspots (label cells' jump-to-pawn ButtonInvisible, checkbox cells'
        /// toggle targets) are noise that makes every cell parrot its column header. Only the fallback
        /// and text handlers keep extras visible; their cells can hide a widget behind the text.
        /// </summary>
        public virtual bool SuppressCapturedExtras(PawnColumnDef def)
        {
            return true;
        }

        protected static string CheckStateWord(bool value)
        {
            return (value ? "RimWorldAccess.Shell.State.Checked" : "RimWorldAccess.Shell.State.Unchecked").Loc().ToString();
        }
    }

    /// <summary>Spacer columns (Gap, RemainingSpace): no content a sighted player reads either — excluded from the grid.</summary>
    internal sealed class SkipColumnHandler : PawnColumnHandler
    {
        public override bool SkipColumn(PawnColumnDef def)
        {
            return true;
        }
    }

    /// <summary>Unknown (typically modded) workers: navigable and sortable via the worker's own Compare, cell value honestly unavailable.</summary>
    internal sealed class FallbackColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return "RimWorldAccess.Shell.Generic.CellNotReadable".Loc().ToString();
        }

        public override bool SuppressCapturedExtras(PawnColumnDef def)
        {
            return false;
        }
    }

    /// <summary>PawnColumnWorker_Text subclasses: the cell IS GetTextFor(pawn); GetTip is the vanilla tooltip channel.</summary>
    internal sealed class TextColumnHandler : PawnColumnHandler
    {
        private static readonly MethodInfo getTextFor = AccessTools.Method(typeof(PawnColumnWorker_Text), "GetTextFor");
        private static readonly MethodInfo getTip = AccessTools.Method(typeof(PawnColumnWorker_Text), "GetTip");

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            string text = (string)getTextFor.Invoke(def.Worker, new object[] { pawn });
            return text != null ? text.StripTags() : "";
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            string tip = (string)getTip.Invoke(def.Worker, new object[] { pawn });
            return string.IsNullOrEmpty(tip) ? null : tip.StripTags();
        }

        public override bool SuppressCapturedExtras(PawnColumnDef def)
        {
            return false;
        }
    }

    /// <summary>
    /// The pawn-name column. Cell text is the worker's own private GetLabel — reflected, not
    /// transcribed, so the named-non-humanlike "Name, kind" branch and the def.useLabelShort switch
    /// stay vanilla's. Enter mirrors the cell click.
    /// </summary>
    internal class LabelColumnHandler : PawnColumnHandler
    {
        // Table-free ActivateCells opt into the windowless tabs' null-table activation; CheckboxColumnHandler stays gated (Designator.SetValue dereferences the table).
        private static readonly MethodInfo getLabel = AccessTools.Method(typeof(PawnColumnWorker_Label), "GetLabel");

        /// <summary>The pawn the worker's DoCell actually labels, which is not always the row's own pawn.</summary>
        protected virtual Pawn CellSubject(Pawn pawn)
        {
            return pawn;
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            Pawn subject = CellSubject(pawn);
            if (subject == null)
            {
                return "";
            }
            object label = getLabel.Invoke(def.Worker, new object[] { subject });
            return label != null ? label.ToString().StripTags() : "";
        }

        /// <summary>Vanilla's hover text (RimWorld/PawnColumnWorker_Label.cs:82-84) minus the mouse instruction
        /// <see cref="CellHint"/> replaces and the LabelCap every PawnTooltip* template opens with.</summary>
        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            Pawn subject = CellSubject(pawn);
            if (subject == null)
            {
                return null;
            }
            string tip = subject.GetTooltip().text ?? "";
            string name = subject.LabelCap;
            if (!string.IsNullOrEmpty(name) && tip.StartsWith(name, StringComparison.Ordinal))
            {
                tip = tip.Substring(name.Length);
            }
            tip = SpeechFlatten.ToSentences(tip.StripTags().Trim());
            return string.IsNullOrEmpty(tip) ? null : tip;
        }

        public override string CellHint(PawnColumnDef def, Pawn pawn)
        {
            string label = CellSubject(pawn) != null ? CellText(def, pawn) : "";
            return string.IsNullOrEmpty(label) ? null : "RimWorldAccess.Shell.Table.JumpToPawnHint".Translate(label).ToString();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            Pawn subject = CellSubject(pawn);
            if (subject == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            CameraJumper.TryJumpAndSelect(subject);
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }

    /// <summary>
    /// The overseer column. Vanilla's worker labels the mech's OVERSEER, not the row's mech
    /// (RimWorld/PawnColumnWorker_Overseer.cs:10-17), so text, tooltip and jump target follow it.
    /// </summary>
    internal sealed class OverseerColumnHandler : LabelColumnHandler
    {
        protected override Pawn CellSubject(Pawn pawn)
        {
            return pawn != null ? pawn.GetOverseer() : null;
        }
    }

    /// <summary>
    /// PawnColumnWorker_Checkbox and its whole subtree (Designator columns, Sterilize,
    /// FollowDrafted/Fieldwork, ...): state is GetValue, Enter is SetValue(!value) — the worker's own
    /// setter, so designation side effects and confirmation dialogs run unmodified.
    /// </summary>
    internal sealed class CheckboxColumnHandler : PawnColumnHandler
    {
        private static readonly MethodInfo getValue = AccessTools.Method(typeof(PawnColumnWorker_Checkbox), "GetValue");
        private static readonly MethodInfo setValue = AccessTools.Method(typeof(PawnColumnWorker_Checkbox), "SetValue");
        private static readonly MethodInfo hasCheckbox = AccessTools.Method(typeof(PawnColumnWorker_Checkbox), "HasCheckbox");
        private static readonly MethodInfo getTip = AccessTools.Method(typeof(PawnColumnWorker_Checkbox), "GetTip");

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (!(bool)hasCheckbox.Invoke(def.Worker, new object[] { pawn }))
            {
                return "";
            }
            return CheckStateWord((bool)getValue.Invoke(def.Worker, new object[] { pawn }));
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            string tip = (string)getTip.Invoke(def.Worker, new object[] { pawn });
            return string.IsNullOrEmpty(tip) ? null : tip.StripTags();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            PawnColumnWorker worker = def.Worker;
            if (!(bool)hasCheckbox.Invoke(worker, new object[] { pawn }))
            {
                return PawnColumnActivation.NotHandled;
            }
            bool value = (bool)getValue.Invoke(worker, new object[] { pawn });
            setValue.Invoke(worker, new object[] { pawn, !value, table });
            (!value ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            return PawnColumnActivation.StateChanged;
        }
    }

    /// <summary>
    /// Icon columns (Gender, LifeStage, Ideo, Xenotype, Bond, ...): the only text the game attaches
    /// is GetIconTip, so that is the cell value; Enter runs the worker's own ClickedIcon.
    /// </summary>
    internal sealed class IconColumnHandler : PawnColumnHandler
    {
        private static readonly MethodInfo getIconTip = AccessTools.Method(typeof(PawnColumnWorker_Icon), "GetIconTip");
        private static readonly MethodInfo clickedIcon = AccessTools.Method(typeof(PawnColumnWorker_Icon), "ClickedIcon");

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            string tip = (string)getIconTip.Invoke(def.Worker, new object[] { pawn });
            return string.IsNullOrEmpty(tip) ? "" : tip.StripTags();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            clickedIcon.Invoke(def.Worker, new object[] { pawn });
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }

    /// <summary>
    /// Training columns: wanted state from the pawn's own training tracker; Enter toggles via
    /// SetWantedRecursive as TrainingCardUtility.DoTrainableCheckbox does; an untrainable
    /// animal's cell carries the game's own refusal reason as its tip.
    /// </summary>
    internal sealed class TrainableColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.training == null || def.trainable == null)
            {
                return "";
            }
            bool visible;
            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(def.trainable, out visible);
            if (!visible)
            {
                return "";
            }
            if (!canTrain.Accepted)
            {
                return "RimWorldAccess.Shell.Generic.Incapable".Loc().ToString();
            }
            if (pawn.training.HasLearned(def.trainable))
            {
                return CheckStateWord(true) + ", " + "RimWorldAccess.Shell.Generic.Trained".Loc();
            }
            return CheckStateWord(pawn.training.GetWanted(def.trainable));
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.training == null || def.trainable == null)
            {
                return null;
            }
            bool visible;
            AcceptanceReport canTrain = pawn.training.CanAssignToTrain(def.trainable, out visible);
            if (visible && !canTrain.Accepted && !string.IsNullOrEmpty(canTrain.Reason))
            {
                return canTrain.Reason.StripTags();
            }
            return null;
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.training == null || def.trainable == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            bool visible;
            if (!pawn.training.CanAssignToTrain(def.trainable, out visible).Accepted || !visible)
            {
                return PawnColumnActivation.NotHandled;
            }
            bool wanted = pawn.training.GetWanted(def.trainable);
            pawn.training.SetWantedRecursive(def.trainable, !wanted);
            (!wanted ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            return PawnColumnActivation.StateChanged;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }

    /// <summary>
    /// Work-priority columns: value and click behavior mirror WidgetsWork.WorkBoxOnGUI verbatim —
    /// manual-priorities Enter steps the priority down (1 is highest, 0 disables, wrapping to 4),
    /// checkbox mode toggles 0/3, and the low-skill crunch and ideology-opposed warnings still fire.
    /// </summary>
    internal sealed class WorkPriorityColumnHandler : PawnColumnHandler
    {
        public override string HeaderLabel(PawnColumnDef def)
        {
            return def.workType != null ? def.workType.labelShort.CapitalizeFirst() : null;
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return PriorityCellText(def.workType, pawn);
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            return PriorityCellTip(def.workType, pawn);
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            return ActivatePriority(def.workType, pawn);
        }

        public override bool CanAdjustCell(PawnColumnDef def)
        {
            return def.workType != null;
        }

        public override PawnColumnActivation AdjustCell(PawnColumnDef def, Pawn pawn, int direction, PawnTable table)
        {
            return AdjustPriority(def.workType, pawn, direction);
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }

        // Def-less halves for columns carrying their work type outside the def (Colony Manager Redux).

        public static string PriorityCellText(WorkTypeDef workType, Pawn pawn)
        {
            if (pawn.workSettings == null || workType == null)
            {
                return "";
            }
            if (pawn.WorkTypeIsDisabled(workType))
            {
                return "RimWorldAccess.Shell.Generic.Incapable".Loc().ToString();
            }
            int priority = pawn.workSettings.GetPriority(workType);
            if (Find.PlaySettings != null && Find.PlaySettings.useWorkPriorities)
            {
                return priority.ToString();
            }
            return CheckStateWord(priority > 0);
        }

        public static string PriorityCellTip(WorkTypeDef workType, Pawn pawn)
        {
            if (pawn.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return null;
            }
            return WidgetsWork.TipForPawnWorker(pawn, workType, incapableBecauseOfCapacities: false).StripTags();
        }

        public static PawnColumnActivation ActivatePriority(WorkTypeDef workType, Pawn pawn)
        {
            if (pawn.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return PawnColumnActivation.NotHandled;
            }
            bool wasActive = pawn.workSettings.WorkIsActive(workType);
            if (Find.PlaySettings != null && Find.PlaySettings.useWorkPriorities)
            {
                int next = pawn.workSettings.GetPriority(workType) - 1;
                if (next < 0)
                {
                    next = 4;
                }
                pawn.workSettings.SetPriority(workType, next);
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }
            else if (pawn.workSettings.GetPriority(workType) > 0)
            {
                pawn.workSettings.SetPriority(workType, 0);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                pawn.workSettings.SetPriority(workType, 3);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }
            PlayNewlyActiveWarnings(pawn, workType, wasActive);
            return PawnColumnActivation.StateChanged;
        }

        /// <summary>
        /// Left/Right: the vanilla HeaderClicked priority cycle. Unlike <see cref="ActivatePriority"/>,
        /// a no-op at a bound re-announces the unchanged cell rather than wrapping, so it is audible.
        /// </summary>
        public static PawnColumnActivation AdjustPriority(WorkTypeDef workType, Pawn pawn, int direction)
        {
            if (pawn.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return PawnColumnActivation.NotHandled;
            }
            bool decrease = direction < 0;
            int current = pawn.workSettings.GetPriority(workType);
            int next;
            if (!TryComputeCycleTarget(current, decrease, out next))
            {
                return PawnColumnActivation.StateChanged;
            }
            bool wasActive = pawn.workSettings.WorkIsActive(workType);
            pawn.workSettings.SetPriority(workType, next);
            if (Find.PlaySettings != null && Find.PlaySettings.useWorkPriorities)
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }
            else
            {
                (next > 0 ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            }
            PlayNewlyActiveWarnings(pawn, workType, wasActive);
            return PawnColumnActivation.StateChanged;
        }

        /// <summary>The low-skill Crunch sound and the ideology-opposed message, played only when this edit just turned the work type active.</summary>
        private static void PlayNewlyActiveWarnings(Pawn pawn, WorkTypeDef workType, bool wasActive)
        {
            if (wasActive || !pawn.workSettings.WorkIsActive(workType))
            {
                return;
            }
            if (workType.relevantSkills.Any() && pawn.skills != null && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
            {
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            }
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(pawn, workType.gerundLabel),
                    pawn, MessageTypeDefOf.CautionInput, historical: false);
            }
        }

        /// <summary>
        /// Vanilla HeaderClicked priority cycle: decrease skips priority 1, else priority-1 wrapping
        /// 0-&gt;4; increase skips priority 0, else priority+1 wrapping 4-&gt;0. In basic mode decrease
        /// sets 0-&gt;3 and increase sets &gt;0-&gt;0. Returns false when the step would be a no-op.
        /// </summary>
        public static bool TryComputeCycleTarget(int current, bool decrease, out int next)
        {
            if (!Find.PlaySettings.useWorkPriorities)
            {
                if (decrease)
                {
                    if (current == 0) { next = 3; return true; }
                    next = current; return false;
                }
                else
                {
                    if (current > 0) { next = 0; return true; }
                    next = current; return false;
                }
            }

            if (decrease)
            {
                if (current == 1) { next = 1; return false; }
                int n = current - 1;
                if (n < 0) n = 4;
                next = n;
                return true;
            }
            else
            {
                if (current == 0) { next = 0; return false; }
                int n = current + 1;
                if (n > 4) n = 0;
                next = n;
                return true;
            }
        }
    }

    /// <summary>The 24-hour timetable column, run-length encoded into ranges. Read-only; ScheduleScope edits.</summary>
    internal sealed class TimetableColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return TimetableReadout.Describe(pawn);
        }
    }

    /// <summary>
    /// The allowed-area column, branched like vanilla's DoCell. Enter rides
    /// <see cref="AreaUtility.MakeAllowedAreaListFloatMenu"/>, gated on <see cref="PawnColumnMutationHelper.CanEditAllowedArea"/>.
    /// </summary>
    internal sealed class AllowedAreaColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (PawnColumnMutationHelper.CanEditAllowedArea(pawn))
            {
                return AreaUtility.AreaAllowedLabel(pawn)?.StripTags() ?? "";
            }
            return PawnColumnMutationHelper.AllowedAreaNotice(pawn) ?? "";
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.playerSettings == null || pawn.MapHeld == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            if (!PawnColumnMutationHelper.CanEditAllowedArea(pawn))
            {
                return PawnColumnActivation.NotHandled;
            }
            Map map = pawn.MapHeld;

            // The generator pushes its FloatMenu synchronously, so the menu redirected
            // below is guaranteed to be the one it just built.
            // MUTATION-C: the selAction callback below is the exact bare-field-write shape
            // vanilla's own callers pass to this generator (InspectPaneFiller.cs:160,
            // Designator_AreaAllowed.cs:45) — AreaRestrictionInPawnCurrentMap has no gated
            // setter, so even vanilla's own call sites write it bare inside this delegate.
            int windowCountBefore = Find.WindowStack.Windows.Count;
            AreaUtility.MakeAllowedAreaListFloatMenu(
                selArea => pawn.playerSettings.AreaRestrictionInPawnCurrentMap = selArea,
                addNullAreaOption: true,
                addManageOption: true,
                map);

            if (Find.WindowStack.Windows.Count > windowCountBefore
                && Find.WindowStack.Windows[Find.WindowStack.Windows.Count - 1] is FloatMenu spawnedMenu)
            {
                List<FloatMenuOption> options = PawnColumnMutationHelper.ExtractFloatMenuOptions(spawnedMenu);
                Find.WindowStack.TryRemove(spawnedMenu, doCloseSound: false);
                if (options != null)
                {
                    // A real FloatMenu spawned far from the mouse closes itself within frames
                    // (vanilla's distance fade) and never reaches the keyboard. playOpenSound is
                    // false: FloatMenu's constructor already played SoundDefOf.FloatMenu_Open.
                    WindowlessFloatMenuState.Open(options, spawnedMenu.givesColonistOrders, playOpenSound: false);
                    return PawnColumnActivation.OpenedUI;
                }
            }
            return PawnColumnActivation.NotHandled;
        }
    }

    /// <summary>Medical-care column: the enum's translated label; Enter serves MedicalCareUtility's OWN menu generator.</summary>
    internal sealed class MedicalCareColumnHandler : PawnColumnHandler, IGeneratedMenuSource
    {
        private static readonly MethodInfo generateMenu =
            AccessTools.Method(typeof(MedicalCareUtility), "MedicalCareSelectButton_GenerateMenu");

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.playerSettings == null)
            {
                return "";
            }
            return pawn.playerSettings.medCare.GetLabel().CapitalizeFirst();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.playerSettings == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            return DropdownColumnHandler.OpenGeneratedMenu(generateMenu, null, pawn)
                ? PawnColumnActivation.OpenedUI
                : PawnColumnActivation.NotHandled;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }

        public bool TryApplyPayloadToTarget(PawnColumnDef def, Pawn targetPawn, object payload)
        {
            return targetPawn.playerSettings != null
                && PawnColumnMutationHelper.TryApplyGeneratedOption(generateMenu, null, targetPawn, payload);
        }
    }

    /// <summary>Copy/paste columns: Enter opens a two-option menu driving the worker's own protected CopyFrom/PasteTo (paste disabled while the clipboard is empty).</summary>
    internal sealed class CopyPasteColumnHandler : PawnColumnHandler
    {
        /// <summary>The def carries neither label nor headerTip (vanilla draws two icon buttons); the game's own Copy/Paste words name it.</summary>
        public override string HeaderLabel(PawnColumnDef def)
        {
            return "Copy".Translate() + " / " + "Paste".Translate();
        }

        private static readonly MethodInfo copyFrom = AccessTools.Method(typeof(PawnColumnWorker_CopyPaste), "CopyFrom");
        private static readonly MethodInfo pasteTo = AccessTools.Method(typeof(PawnColumnWorker_CopyPaste), "PasteTo");
        private static readonly PropertyInfo anythingInClipboard =
            AccessTools.Property(typeof(PawnColumnWorker_CopyPaste), "AnythingInClipboard");

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            PawnColumnWorker worker = def.Worker;
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("Copy".Translate(), delegate
            {
                copyFrom.Invoke(worker, new object[] { pawn });
            }));
            bool canPaste = (bool)anythingInClipboard.GetValue(worker, null);
            options.Add(canPaste
                ? new FloatMenuOption("Paste".Translate(), delegate
                {
                    pasteTo.Invoke(worker, new object[] { pawn });
                })
                : new FloatMenuOption("Paste".Translate(), null));
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }
    }

    /// <summary>The info-card button column: Enter does what the cell's button does.</summary>
    internal sealed class InfoColumnHandler : PawnColumnHandler
    {
        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
            return PawnColumnActivation.OpenedUI;
        }
    }

    /// <summary>
    /// One vanilla Widgets.DropdownMenuElement&lt;T&gt; entry, unwrapped by reflection: the real
    /// FloatMenuOption plus the boxed payload — the policy/enum/def the option represents, null for a
    /// structural entry like vanilla's trailing "Edit..." button.
    /// </summary>
    internal readonly struct GeneratedMenuOption
    {
        public readonly FloatMenuOption Option;
        public readonly object Payload;

        public GeneratedMenuOption(FloatMenuOption option, object payload)
        {
            Option = option;
            Payload = payload;
        }
    }

    /// <summary>
    /// Implemented by every dropdown-shaped column handler: applies a SOURCE pawn's current value to
    /// a TARGET pawn by matching the entry in the target's own freshly-generated menu and invoking
    /// its real FloatMenuOption action. Kept off <see cref="IPawnColumnHandler"/> because it is
    /// meaningless for non-dropdown columns.
    /// </summary>
    internal interface IGeneratedMenuSource
    {
        bool TryApplyPayloadToTarget(PawnColumnDef def, Pawn targetPawn, object payload);
    }

    /// <summary>
    /// Shared shape for vanilla's Widgets.Dropdown columns (outfit, food, drug and reading policy,
    /// hostility response): the cell value is the pawn's current assignment; Enter reflects the
    /// worker's own menu-generator method — or, where DoCell delegates to an external static utility,
    /// that utility's generator via <paramref name="generatorOwner"/> — and opens its options verbatim.
    /// </summary>
    internal class DropdownColumnHandler : PawnColumnHandler, IGeneratedMenuSource
    {
        private static readonly Dictionary<Type, MethodInfo> menuMethods = new Dictionary<Type, MethodInfo>();

        private readonly string menuMethodName;
        private readonly Func<Pawn, string> currentValue;
        private readonly Type generatorOwner;
        private readonly Func<Pawn, bool> cellExists;

        public DropdownColumnHandler(string menuMethodName, Func<Pawn, string> currentValue, Type generatorOwner = null,
            Func<Pawn, bool> cellExists = null)
        {
            this.menuMethodName = menuMethodName;
            this.currentValue = currentValue;
            this.generatorOwner = generatorOwner;
            this.cellExists = cellExists;
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (cellExists != null && !cellExists(pawn))
            {
                return "";
            }
            string value = currentValue(pawn);
            return value != null ? value.StripTags() : "";
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (cellExists != null && !cellExists(pawn))
            {
                return PawnColumnActivation.NotHandled;
            }
            return OpenGeneratedMenu(ResolveGenerator(def.Worker), GeneratorInstance(def.Worker), pawn)
                ? PawnColumnActivation.OpenedUI
                : PawnColumnActivation.NotHandled;
        }

        /// <summary>The generators take only the pawn, so the windowless tabs (Animals, Mechs, Wildlife) may activate too.</summary>
        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }

        protected MethodInfo ResolveGenerator(PawnColumnWorker worker)
        {
            Type ownerType = generatorOwner ?? worker.GetType();
            MethodInfo method;
            if (!menuMethods.TryGetValue(ownerType, out method))
            {
                method = AccessTools.Method(ownerType, menuMethodName);
                menuMethods[ownerType] = method;
            }
            return method;
        }

        /// <summary>The reflection invocation target: null when the generator is a static utility method.</summary>
        protected object GeneratorInstance(PawnColumnWorker worker)
        {
            return generatorOwner != null ? null : worker;
        }

        public virtual bool TryApplyPayloadToTarget(PawnColumnDef def, Pawn targetPawn, object payload)
        {
            PawnColumnWorker worker = def.Worker;
            return PawnColumnMutationHelper.TryApplyGeneratedOption(ResolveGenerator(worker), GeneratorInstance(worker), targetPawn, payload);
        }

        /// <summary>Invokes a Widgets.DropdownMenuElement&lt;T&gt; generator and opens its options as a FloatMenu.</summary>
        internal static bool OpenGeneratedMenu(MethodInfo generator, object instance, Pawn pawn)
        {
            List<GeneratedMenuOption> elements = GenerateMenuOptions(generator, instance, pawn);
            if (elements.Count == 0)
            {
                return false;
            }
            var options = new List<FloatMenuOption>(elements.Count);
            foreach (GeneratedMenuOption element in elements)
            {
                options.Add(element.Option);
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return true;
        }

        /// <summary>Unwraps every entry's real FloatMenuOption plus its boxed payload, without opening anything. Non-generic enumeration because T differs per worker.</summary>
        internal static List<GeneratedMenuOption> GenerateMenuOptions(MethodInfo generator, object instance, Pawn pawn)
        {
            var result = new List<GeneratedMenuOption>();
            if (generator == null)
            {
                return result;
            }
            var elements = generator.Invoke(instance, new object[] { pawn }) as IEnumerable;
            if (elements == null)
            {
                return result;
            }
            foreach (object element in elements)
            {
                if (element == null)
                {
                    continue;
                }
                Type elementType = element.GetType();
                FieldInfo optionField = AccessTools.Field(elementType, "option");
                FieldInfo payloadField = AccessTools.Field(elementType, "payload");
                FloatMenuOption option = optionField != null ? optionField.GetValue(element) as FloatMenuOption : null;
                if (option == null)
                {
                    continue;
                }
                object payload = payloadField != null ? payloadField.GetValue(element) : null;
                result.Add(new GeneratedMenuOption(option, payload));
            }
            return result;
        }
    }

    /// <summary>
    /// The Outfit column's one divergence from the shared dropdown shape: a quest lodger's cell draws
    /// vanilla's "Unchangeable" LABEL, never the dropdown (PawnColumnWorker_Outfit.DoCell:41-51). The
    /// gate lives here, not at the call site, so every table showing the column gets the same cell.
    /// </summary>
    internal sealed class OutfitColumnHandler : DropdownColumnHandler
    {
        public OutfitColumnHandler()
            : base("Button_GenerateMenu",
                p => p.outfits != null && p.outfits.CurrentApparelPolicy != null ? p.outfits.CurrentApparelPolicy.label : null)
        {
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            return pawn.IsQuestLodger() ? "Unchangeable".Translate().Resolve() : base.CellText(def, pawn);
        }

        /// <summary>Vanilla's "QuestRelated_Outfit" explanation, not a repeat of the cell's own "Unchangeable" value.</summary>
        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            return pawn.IsQuestLodger() ? "QuestRelated_Outfit".Translate().Resolve() : base.CellTip(def, pawn);
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.IsQuestLodger())
            {
                return PawnColumnActivation.NotHandled;
            }
            return base.ActivateCell(def, pawn, table);
        }

        /// <summary>A quest lodger offers no dropdown to search a match in.</summary>
        public override bool TryApplyPayloadToTarget(PawnColumnDef def, Pawn targetPawn, object payload)
        {
            return !targetPawn.IsQuestLodger() && base.TryApplyPayloadToTarget(def, targetPawn, payload);
        }
    }

    /// <summary>
    /// Hostility-response column: the enum's translated label; Enter serves
    /// HostilityResponseModeUtility's OWN generator, where vanilla excludes Attack for a pacifist.
    /// </summary>
    internal sealed class HostilityResponseColumnHandler : PawnColumnHandler, IGeneratedMenuSource
    {
        private static readonly MethodInfo generateMenu =
            AccessTools.Method(typeof(HostilityResponseModeUtility), "DrawResponseButton_GenerateMenu");

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.playerSettings == null)
            {
                return "";
            }
            return pawn.playerSettings.hostilityResponse.GetLabel().CapitalizeFirst();
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            return pawn.playerSettings == null ? null : "HostilityReponseTip".Translate().Resolve();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.playerSettings == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            return DropdownColumnHandler.OpenGeneratedMenu(generateMenu, null, pawn)
                ? PawnColumnActivation.OpenedUI
                : PawnColumnActivation.NotHandled;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }

        public bool TryApplyPayloadToTarget(PawnColumnDef def, Pawn targetPawn, object payload)
        {
            return targetPawn.playerSettings != null
                && PawnColumnMutationHelper.TryApplyGeneratedOption(generateMenu, null, targetPawn, payload);
        }
    }

    /// <summary>
    /// Medicine-carry column: vanilla draws TWO dropdowns per cell (which medicine, how many), each
    /// backed by a private generator taking an extra InventoryStockGroupDef argument that doesn't fit
    /// the shared single-Pawn generator shape. The write is gate-free in vanilla — no Can*/Try*
    /// exists — so collapsing them into one "{thing} x{count}" picker discards no gate.
    /// </summary>
    internal sealed class CarryColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.inventoryStock == null)
            {
                return "";
            }
            InventoryStockGroupDef group = InventoryStockGroupDefOf.Medicine;
            if (group == null)
            {
                return "";
            }
            int count = pawn.inventoryStock.GetDesiredCountForGroup(group);
            if (count == 0)
            {
                return "None".Translate().Resolve();
            }
            ThingDef thing = pawn.inventoryStock.GetDesiredThingForGroup(group);
            return thing != null ? thing.LabelCap + " x" + count : "None".Translate().Resolve();
        }

        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            if (pawn.inventoryStock == null)
            {
                return PawnColumnActivation.NotHandled;
            }
            InventoryStockGroupDef group = InventoryStockGroupDefOf.Medicine;
            if (group == null || group.thingDefs.NullOrEmpty())
            {
                return PawnColumnActivation.NotHandled;
            }

            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("None".Translate(), delegate
            {
                pawn.inventoryStock.SetCountForGroup(group, 0);
            }));
            // MUTATION-C: mirrors PawnColumnWorker_Carry's own field-driven
            // range (group.thingDefs / group.min / group.max — no separate
            // gated vehicle exists for this collapsed picker; see class doc).
            // A zero count is offered once as "None" above rather than once per
            // medicine type, so the loop starts at the greater of group.min and 1.
            int startCount = Math.Max(group.min, 1);
            foreach (ThingDef medicineDef in group.thingDefs)
            {
                ThingDef capturedDef = medicineDef;
                for (int count = startCount; count <= group.max; count++)
                {
                    int capturedCount = count;
                    options.Add(new FloatMenuOption(capturedDef.LabelCap + " x" + capturedCount, delegate
                    {
                        pawn.inventoryStock.SetThingForGroup(group, capturedDef);
                        pawn.inventoryStock.SetCountForGroup(group, capturedCount);
                    }));
                }
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }
    }

    /// <summary>
    /// The mech control group's work mode. Read-only: <c>MechsScope</c> owns the picker on the Mechs
    /// table, and this handler keeps the cell readable wherever else a table mounts the column.
    /// </summary>
    internal sealed class WorkModeColumnHandler : PawnColumnHandler
    {
        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            MechanitorControlGroup group = pawn != null ? pawn.GetMechControlGroup() : null;
            return group != null ? group.WorkMode.LabelCap.Resolve() : "";
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            return CellTipFor(pawn);
        }

        /// <summary>
        /// Vanilla's hover text, composed as DoCell does (RimWorld/PawnColumnWorker_WorkMode.cs:28-38).
        /// DoCell REPLACES the pawn tooltip's text, so none of it survives.
        /// </summary>
        public static string CellTipFor(Pawn pawn)
        {
            MechanitorControlGroup group = pawn != null ? pawn.GetMechControlGroup() : null;
            if (group == null)
            {
                return null;
            }
            string tip = "ClickToChangeWorkMode".Translate();
            AcceptanceReport canControlMechs = group.Tracker.CanControlMechs;
            if (!canControlMechs && !canControlMechs.Reason.NullOrEmpty())
            {
                tip = tip + "\n\n" + "DisabledCommand".Translate() + ": " + canControlMechs.Reason;
            }
            return SpeechFlatten.ToSentences(tip.StripTags());
        }
    }

    /// <summary>
    /// Shared write vehicle for the bespoke, non-<see cref="GenericPawnTableScope"/> Animals/Wildlife
    /// scopes: they read the map's animals directly and never open the real MainTabWindow, so they
    /// have no live <see cref="PawnTable"/> to hand a worker's own SetValue. The Designator family's
    /// SetValue reads the table only for its sort-dirty repaint hint (dead weight off-render), so a
    /// fresh <see cref="CreateDetachedTable"/> satisfies it exactly.
    /// </summary>
    internal static class PawnColumnMutationHelper
    {
        private static readonly MethodInfo designatorGetValue = AccessTools.Method(typeof(PawnColumnWorker_Checkbox), "GetValue");
        private static readonly MethodInfo designatorSetValue = AccessTools.Method(typeof(PawnColumnWorker_Checkbox), "SetValue");
        private static readonly MethodInfo shouldConfirmDesignation = AccessTools.Method(typeof(PawnColumnWorker_Designator), "ShouldConfirmDesignation");
        private static readonly MethodInfo designationConfirmed = AccessTools.Method(typeof(PawnColumnWorker_Designator), "DesignationConfirmed");
        private static readonly MethodInfo masterGenerateMenu = AccessTools.Method(typeof(TrainableUtility), "MasterSelectButton_GenerateMenu");
        private static readonly FieldInfo floatMenuOptionsField = AccessTools.Field(typeof(FloatMenu), "options");

        /// <summary>Builds a real, unrendered PawnTable via the same Activator.CreateInstance call MainTabWindow_PawnTable.CreateTable makes; never PawnTableOnGUI'd, so only the cheap constructor is paid for.</summary>
        internal static PawnTable CreateDetachedTable(PawnTableDef def)
        {
            return (PawnTable)Activator.CreateInstance(def.workerClass, def, (Func<IEnumerable<Pawn>>)(() => Enumerable.Empty<Pawn>()), 0, 0);
        }

        /// <summary>Reflects a just-spawned FloatMenu's own protected options list, so a caller can ride a vanilla generator that pushes straight to Find.WindowStack.Add without returning its options.</summary>
        internal static List<FloatMenuOption> ExtractFloatMenuOptions(FloatMenu menu)
        {
            return floatMenuOptionsField?.GetValue(menu) as List<FloatMenuOption>;
        }

        /// <summary>
        /// Vanilla's compound allowed-area gate (PawnColumnWorker_AllowedArea.DoCell:32-34), never
        /// SupportsAllowedAreas alone. The Faction test is the CURRENT faction, not HomeFaction.
        /// </summary>
        internal static bool CanEditAllowedArea(Pawn pawn)
        {
            return PassesAllowedAreaCellGate(pawn) && pawn.playerSettings.SupportsAllowedAreas;
        }

        /// <summary>
        /// What DoCell:38-51 draws in place of the area selector: AnimalPenGUI.DoAllowedAreaMessage's
        /// pen status, tooltip included, or the dryad notice. Null where it draws a selector or nothing.
        /// </summary>
        internal static string AllowedAreaNotice(Pawn pawn)
        {
            if (!PassesAllowedAreaCellGate(pawn) || pawn.playerSettings.SupportsAllowedAreas)
            {
                return null;
            }
            if (AnimalPenUtility.NeedsToBeManagedByRope(pawn))
            {
                CompAnimalPenMarker pen = AnimalPenUtility.GetCurrentPenOf(pawn, allowUnenclosedPens: false);
                if (pen != null)
                {
                    return "InPen".Translate() + ": " + pen.label;
                }
                return "Unpenned".Translate().CapitalizeFirst() + ". " + "UnpennedTooltip".Translate();
            }
            if (pawn.RaceProps.Dryad)
            {
                return "CannotAssignAllowedAreaToDryad".Translate();
            }
            return null;
        }

        private static bool PassesAllowedAreaCellGate(Pawn pawn)
        {
            return pawn.Faction == Faction.OfPlayer
                && (!pawn.IsMutant || pawn.mutant.Def.respectsAllowedArea)
                && (!pawn.RaceProps.IsMechanoid || pawn.GetOverseer() != null)
                && pawn.playerSettings != null;
        }

        /// <summary>
        /// Paint support for the dropdown-shaped column handlers: applies the source pawn's current
        /// value by finding, in the TARGET's own freshly-generated menu, the entry whose payload
        /// matches, and invoking THAT entry's real FloatMenuOption action. Matching against the
        /// target's menu is what keeps per-target gates honest — HostilityResponseModeUtility omits
        /// Attack for a pacifist target, so painting "Attack" onto one finds no match.
        /// </summary>
        internal static bool TryApplyGeneratedOption(MethodInfo generator, object instance, Pawn targetPawn, object payload)
        {
            foreach (GeneratedMenuOption element in DropdownColumnHandler.GenerateMenuOptions(generator, instance, targetPawn))
            {
                if (!Equals(element.Payload, payload))
                {
                    continue;
                }
                if (element.Option.action == null)
                {
                    return false; // vanilla drew this entry disabled for this target.
                }
                element.Option.action();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Toggles a PawnColumnWorker_Designator-backed column (Hunt, Tame, Slaughter,
        /// ReleaseAnimalToWild) through the worker's own
        /// GetValue/ShouldConfirmDesignation/DesignationConfirmed, so mutual exclusion,
        /// bonded/venerated warnings and the non-player-faction confirm all ride virtual-dispatched
        /// vanilla code. The add branch is split out of SetValue so <paramref name="afterward"/>
        /// always follows the true result: immediately when no confirm is needed, else from the real
        /// Dialog_Confirm's accept delegate, matching vanilla's deferred-until-Yes timing.
        /// </summary>
        internal static void SetDesignatorValue(PawnColumnDef columnDef, Pawn pawn, bool value, PawnTable table, Action afterward)
        {
            PawnColumnWorker worker = columnDef.Worker;
            bool current = (bool)designatorGetValue.Invoke(worker, new object[] { pawn });
            if (value == current)
            {
                afterward?.Invoke();
                return;
            }
            if (!value)
            {
                designatorSetValue.Invoke(worker, new object[] { pawn, false, table });
                afterward?.Invoke();
                return;
            }
            object[] confirmArgs = { pawn, null };
            bool needsConfirm = (bool)shouldConfirmDesignation.Invoke(worker, confirmArgs);
            if (!needsConfirm)
            {
                designatorSetValue.Invoke(worker, new object[] { pawn, true, table });
                afterward?.Invoke();
                return;
            }
            string title = (string)confirmArgs[1];
            Find.WindowStack.Add(new Dialog_Confirm(title, delegate
            {
                designationConfirmed.Invoke(worker, new object[] { pawn });
                afterward?.Invoke();
            }));
        }

        /// <summary>
        /// Read-only: would setting this Designator column to true pop one of vanilla's own
        /// Dialog_Confirm windows for this pawn? Bulk paint uses this to SKIP a pawn rather than
        /// stack one dialog per painted animal; the dialogs still fire via the single-cell Enter
        /// toggle. Covers two independent gates, both reflected so a modded override is honored:
        /// ShouldConfirmDesignation (the blocking pre-add confirm), and
        /// PawnColumnWorker_ReleaseAnimalToWild.Notify_DesignationAdded's own Faction-keyed
        /// Dialog_Confirm, fired after the designation is added — HomeFaction==player animals under
        /// a different current Faction hit only that second gate.
        /// </summary>
        internal static bool DesignatorWouldRequireConfirm(PawnColumnDef columnDef, Pawn pawn)
        {
            object[] args = { pawn, null };
            if ((bool)shouldConfirmDesignation.Invoke(columnDef.Worker, args))
                return true;
            if (columnDef.Worker is PawnColumnWorker_ReleaseAnimalToWild && pawn.Faction != Faction.OfPlayer)
                return true;
            return false;
        }

        internal struct MasterMenuOption
        {
            public Pawn Colonist;
            public string Label;
            public Action Apply;
        }

        /// <summary>
        /// Reflects TrainableUtility.MasterSelectButton_GenerateMenu, the same private generator
        /// vanilla's Master dropdown calls, so the candidate list (all maps' colonists, not just this
        /// map's), the CanBeMaster enable/disable with its "skill too low" annotation, and the write
        /// are all vanilla's. Element 0 is always "(None)"; a null Apply means the entry is disabled.
        /// </summary>
        internal static List<MasterMenuOption> GetMasterMenuElements(Pawn animal)
        {
            var result = new List<MasterMenuOption>();
            var elements = masterGenerateMenu.Invoke(null, new object[] { animal }) as IEnumerable;
            if (elements == null)
            {
                return result;
            }
            foreach (object boxed in elements)
            {
                var element = (Widgets.DropdownMenuElement<Pawn>)boxed;
                if (element.option == null)
                {
                    continue;
                }
                result.Add(new MasterMenuOption
                {
                    Colonist = element.payload,
                    Label = element.option.Label,
                    Apply = element.option.action
                });
            }
            return result;
        }

        /// <summary>The generated entry for a candidate master on a specific animal (CanBeMaster is per-animal: bond overrides skill), or null if that colonist isn't offered.</summary>
        internal static MasterMenuOption? FindMasterMenuOption(Pawn animal, Pawn candidateMaster)
        {
            foreach (MasterMenuOption option in GetMasterMenuElements(animal))
            {
                if (option.Colonist == candidateMaster)
                {
                    return option;
                }
            }
            return null;
        }

        /// <summary>
        /// PawnColumnWorker_Sterilize overrides PawnColumnWorker_Checkbox.SetValue directly rather
        /// than using the Designator ShouldConfirmDesignation/DesignationConfirmed split, so no
        /// isolated vehicle exists for its confirm-gated add branch. The mutations below call the
        /// same public HealthCardUtility.CreateSurgeryBill and BillStack.Delete vanilla does; only
        /// the confirm-required predicate is a hand copy.
        /// </summary>
        internal static void SetSterilizeValue(Pawn pawn, bool value, Action afterward)
        {
            if (pawn.health?.hediffSet?.HasHediff(HediffDefOf.Sterilized) == true)
            {
                afterward?.Invoke();
                return;
            }
            bool current = pawn.BillStack != null && pawn.BillStack.Bills.Any(b => b.recipe == RecipeDefOf.Sterilize);
            if (value == current)
            {
                afterward?.Invoke();
                return;
            }
            if (!value)
            {
                foreach (Bill bill in pawn.BillStack.Bills.Where(b => b.recipe == RecipeDefOf.Sterilize).ToList())
                {
                    pawn.BillStack.Delete(bill);
                }
                afterward?.Invoke();
                return;
            }
            if (SterilizeWouldRequireConfirm(pawn))
            {
                // MUTATION-C: mirrors PawnColumnWorker_Sterilize.SetValue's own
                // HomeFaction confirm gate and title (RimWorld/PawnColumnWorker_Sterilize.cs
                // SetValue, "AnimalSterilizeConfirm" branch) — see class doc above.
                TaggedString title = "AnimalSterilizeConfirm".Translate(pawn.Named("PAWN"), pawn.HomeFaction.Named("FACTION"));
                Find.WindowStack.Add(new Dialog_Confirm(title, delegate
                {
                    HealthCardUtility.CreateSurgeryBill(pawn, RecipeDefOf.Sterilize, null);
                    afterward?.Invoke();
                }));
            }
            else
            {
                HealthCardUtility.CreateSurgeryBill(pawn, RecipeDefOf.Sterilize, null);
                afterward?.Invoke();
            }
        }

        /// <summary>Mirrors PawnColumnWorker_Sterilize.SetValue's HomeFaction check; read-only, used by the toggle path and by paint's skip-rather-than-stack-dialogs decision.</summary>
        internal static bool SterilizeWouldRequireConfirm(Pawn pawn)
        {
            return pawn.HomeFaction != Faction.OfPlayer && pawn.HomeFaction != null;
        }
    }
}
