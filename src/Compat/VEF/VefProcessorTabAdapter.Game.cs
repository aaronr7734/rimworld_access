using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Vanilla Expanded Framework's PipeSystem processor tab, a
    /// bill-stack-style editor: Add-process and settings float menus, copy and paste of the whole
    /// process list, a current-process progress line with cancel, and one section per queued Process
    /// carrying its reorder, repeat-mode, count, quality, suspend and delete controls.
    ///
    /// The float-menu actions use the tab's own option lists and PipeSystem's own delegates, opened
    /// as WINDOWLESS menus because a real FloatMenu self-closes on vanishIfMouseDistant for a
    /// keyboard activation and inspection action rows are not an armed DialogInterceptionPatch
    /// redirect source. Those handlers speak nothing and never rebuild the tree: the menu's own
    /// delegates perform the mutation, and the tree rebuilds when its category is next opened.
    /// Copying into ProcessUtility.Clipboard mutates UI state rather than game state, so it carries
    /// no MUTATION-C marker.
    ///
    /// Deviations: keyboard count adjustment always steps by 1, since vanilla's adjustment
    /// multiplier reads live chord state a keyboard-activated row does not hold; mutation outcomes
    /// are announced, where vanilla communicates them visually only; and "Settings" is a baked
    /// accessibility name, the icon button having no vanilla label or tooltip. Deliberately not
    /// covered: the add-process menu's hover info window and the UIHighlighter pulse.
    ///
    /// PipeSystem ships in a Workshop mod invisible to the l10n checker, so its strings are baked
    /// into this mod's own keys; core keys and the repeat-mode labels are live game data and pass
    /// through unmodified.
    /// </summary>
    internal sealed class VefProcessorTabAdapter : InspectNodeAdapter
    {
        private readonly Type compType;
        private readonly Type compPropertiesType;
        private readonly Type processStackType;
        private readonly Type processType;
        private readonly Type processDefType;
        private readonly Type processUtilityType;

        private readonly PropertyInfo processStackProperty;
        private readonly PropertyInfo processesOptionsProperty;
        private readonly PropertyInfo settingsProperty;
        private readonly PropertyInfo propsProperty;
        private readonly FieldInfo overclockMultiplierField;
        private readonly MethodInfo getNotInRoomRoleFactorMethod;

        private readonly FieldInfo hideSettingsField;

        private readonly PropertyInfo processesProperty;
        private readonly PropertyInfo firstCanDoProperty;
        private readonly MethodInfo addProcessMethod;
        private readonly MethodInfo deleteMethod;
        private readonly MethodInfo reorderMethod;
        private readonly MethodInfo indexOfMethod;
        private readonly MethodInfo notifyProcessChangeMethod;

        private readonly PropertyInfo defProperty;
        private readonly PropertyInfo progressProperty;
        private readonly PropertyInfo repeatInfoTextProperty;
        private readonly PropertyInfo repeatLabelProperty;
        private readonly PropertyInfo optionsProperty;
        private readonly PropertyInfo qualitySelectionsProperty;
        private readonly PropertyInfo missingIngredientsProperty;
        // Resolved for the Ready gate only, never rendered: the tab does not surface it visually
        // either, so a blind player gets exactly what a sighted one does.
        private readonly PropertyInfo ruinedByTempProperty;
        private readonly MethodInfo resetProcessMethod;
        private readonly MethodInfo shouldDoNowMethod;
        private readonly MethodInfo notifyStartWorkingSoundMethod;
        private readonly MethodInfo notifyStopWorkingSoundMethod;
        private readonly FieldInfo suspendedField;
        private readonly FieldInfo targetCountField;
        private readonly FieldInfo processCountField;
        private readonly FieldInfo repeatModeField;
        private readonly FieldInfo cachedInitialTicksField;
        private readonly FieldInfo qualityToOutputField;

        private readonly FieldInfo ticksQualityField;
        private readonly FieldInfo sustainerWhenWorkingField;
        private readonly FieldInfo sustainerDefField;

        private readonly FieldInfo clipboardField;

        private readonly bool ready;
        internal InspectTabBase sharedTab;

        public override bool Ready => ready;

        public VefProcessorTabAdapter()
        {
            var surface = new ReflectionSurface("VefProcessorTabAdapter");

            compType = surface.Type("PipeSystem.CompAdvancedResourceProcessor");
            compPropertiesType = surface.Type("PipeSystem.CompProperties_AdvancedResourceProcessor");
            processStackType = surface.Type("PipeSystem.ProcessStack");
            processType = surface.Type("PipeSystem.Process");
            processDefType = surface.Type("PipeSystem.ProcessDef");
            processUtilityType = surface.Type("PipeSystem.ProcessUtility");

            processStackProperty = surface.Property(compType, "ProcessStack");
            processesOptionsProperty = surface.Property(compType, "ProcessesOptions");
            settingsProperty = surface.Property(compType, "Settings");
            propsProperty = surface.Property(compType, "Props");
            overclockMultiplierField = surface.Field(compType, "overclockMultiplier");
            getNotInRoomRoleFactorMethod = surface.Method(compType, "GetNotInRoomRoleFactor");

            hideSettingsField = surface.Field(compPropertiesType, "hideSettings");

            processesProperty = surface.Property(processStackType, "Processes");
            firstCanDoProperty = surface.Property(processStackType, "FirstCanDo");
            addProcessMethod = processDefType != null
                ? surface.Method(processStackType, "AddProcess",
                    new[] { processDefType, typeof(ThingWithComps), typeof(BillRepeatModeDef), typeof(int), typeof(QualityCategory) })
                : null;
            deleteMethod = processType != null ? surface.Method(processStackType, "Delete", new[] { processType }) : null;
            reorderMethod = processType != null ? surface.Method(processStackType, "Reorder", new[] { processType, typeof(int) }) : null;
            indexOfMethod = processType != null ? surface.Method(processStackType, "IndexOf", new[] { processType }) : null;
            notifyProcessChangeMethod = surface.Method(processStackType, "Notify_ProcessChange", Type.EmptyTypes);

            defProperty = surface.Property(processType, "Def");
            progressProperty = surface.Property(processType, "Progress");
            repeatInfoTextProperty = surface.Property(processType, "RepeatInfoText");
            repeatLabelProperty = surface.Property(processType, "RepeatLabel");
            optionsProperty = surface.Property(processType, "Options");
            qualitySelectionsProperty = surface.Property(processType, "QualitySelections");
            missingIngredientsProperty = surface.Property(processType, "MissingIngredients");
            ruinedByTempProperty = surface.Property(processType, "RuinedByTemp");
            resetProcessMethod = surface.Method(processType, "ResetProcess");
            shouldDoNowMethod = surface.Method(processType, "ShouldDoNow", Type.EmptyTypes);
            notifyStartWorkingSoundMethod = surface.Method(processType, "Notify_StartWorkingSound", Type.EmptyTypes);
            notifyStopWorkingSoundMethod = surface.Method(processType, "Notify_StopWorkingSound", Type.EmptyTypes);
            suspendedField = surface.Field(processType, "suspended");
            targetCountField = surface.Field(processType, "targetCount");
            processCountField = surface.Field(processType, "processCount");
            repeatModeField = surface.Field(processType, "repeatMode");
            cachedInitialTicksField = surface.Field(processType, "cachedInitialTicks");
            qualityToOutputField = surface.Field(processType, "qualityToOutput");

            ticksQualityField = surface.Field(processDefType, "ticksQuality");
            sustainerWhenWorkingField = surface.Field(processDefType, "sustainerWhenWorking");
            sustainerDefField = surface.Field(processDefType, "sustainerDef");

            clipboardField = surface.Field(processUtilityType, "Clipboard");

            ready = surface.Ready;
        }

        // A stable English dispatch token, never displayed raw.
        public override string CategoryKey => "VEF Processes";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        // The tab's own label, the word a sighted player reads, so this bypasses
        // InspectionCategoryLocalizer rather than adding an entry only PipeSystem ever renders.
        public override string DisplayName(InspectTabBase tab) => "RimWorldAccess.Compat.Vef.ProcessorTab".Translate();

        public override string CategoryDisplayName(object obj) => "RimWorldAccess.Compat.Vef.ProcessorTab".Translate();

        public override bool CanExpand(object obj)
        {
            return ready && ResolveComp(obj) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;
            if (!(obj is ThingWithComps thing))
                return;

            try
            {
                BuildAllSections(categoryItem, thing, mode);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefProcessorTabAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Rebuilds the whole category after a mutation: the Paste row's presence, the
        /// current-process line and every section's controls all move together. Routed through the
        /// framework so the extender and parity-capture passes survive.
        /// </summary>
        private void RebuildCategory(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, obj, mode, this, sharedTab);
        }

        /// <summary>The tab's own cache is a perf shim around exactly this lookup, so the comp is reflected directly.</summary>
        private object ResolveComp(object obj)
        {
            if (!(obj is ThingWithComps twc))
                return null;

            foreach (ThingComp comp in twc.AllComps)
            {
                if (compType.IsInstanceOfType(comp))
                    return comp;
            }
            return null;
        }

        private bool ProcessStillPresent(object stack, object process)
        {
            return (int)indexOfMethod.Invoke(stack, new object[] { process }) >= 0;
        }

        private void BuildAllSections(InspectionTreeItem categoryItem, ThingWithComps thing, InspectionMode mode)
        {
            object comp = ResolveComp(thing);
            if (comp == null)
                return;

            object stack = processStackProperty.GetValue(comp);

            if (mode != InspectionMode.ReadOnly)
            {
                InspectNodeFactory.ActionRow(categoryItem, "RimWorldAccess.Compat.Vef.ProcessorAddProcessAction".Translate(), thing,
                    () => OnAddProcess(comp));

                object props = propsProperty.GetValue(comp);
                bool hideSettings = props != null && (bool)hideSettingsField.GetValue(props);
                if (!hideSettings)
                {
                    InspectNodeFactory.ActionRow(categoryItem, "RimWorldAccess.Compat.Vef.ProcessorSettingsAction".Translate(), thing,
                        () => OnSettings(comp));
                }

                InspectNodeFactory.ActionRow(categoryItem, "RimWorldAccess.Compat.Vef.ProcessorCopyAction".Translate(), thing,
                    () => OnCopy(categoryItem, thing, stack, mode));

                IDictionary clipboard = clipboardField.GetValue(null) as IDictionary;
                if (clipboard != null && clipboard.Count > 0)
                {
                    InspectNodeFactory.ActionRow(categoryItem, "RimWorldAccess.Compat.Vef.ProcessorPasteAction".Translate(), thing,
                        () => OnPaste(categoryItem, thing, stack, mode));
                }
            }

            object firstCanDo = firstCanDoProperty.GetValue(stack);
            if (firstCanDo != null)
            {
                object def = defProperty.GetValue(firstCanDo);
                float progress = (float)progressProperty.GetValue(firstCanDo);
                InspectNodeFactory.DetailLine(categoryItem,
                    "RimWorldAccess.Compat.Vef.ProcessorCurrentLine".Translate(((Def)def).LabelCap, progress.ToStringPercent()));

                if (mode != InspectionMode.ReadOnly)
                {
                    InspectNodeFactory.ActionRow(categoryItem, "RimWorldAccess.Compat.Vef.ProcessorCancelCurrentAction".Translate(), firstCanDo,
                        () => OnCancelCurrent(categoryItem, thing, stack, firstCanDo, mode));
                }
            }
            else
            {
                InspectNodeFactory.DetailLine(categoryItem, "RimWorldAccess.Compat.Vef.ProcessorNoProcess".Translate());
            }

            IList processes = processesProperty.GetValue(stack) as IList;
            if (processes == null)
                return;

            // Snapshot: the loop body can mutate the live list through activation callbacks.
            var workingList = new List<object>();
            foreach (object process in processes)
                workingList.Add(process);
            foreach (object process in workingList)
            {
                BuildProcessSection(categoryItem, thing, comp, stack, process, mode);
            }
        }

        private void BuildProcessSection(InspectionTreeItem categoryItem, ThingWithComps thing, object comp, object stack, object process, InspectionMode mode)
        {
            object def = defProperty.GetValue(process);
            string label = BuildSectionLabel(thing, comp, def, process);

            InspectNodeFactory.Section(categoryItem, label, process, secItem =>
                BuildProcessChildren(categoryItem, secItem, thing, comp, stack, process, mode));
        }

        /// <summary>Mirrors Process.DoInterface's label concat from game data.</summary>
        private string BuildSectionLabel(ThingWithComps thing, object comp, object def, object process)
        {
            string label = ((Def)def).LabelCap;

            List<int> ticksQuality = ticksQualityField.GetValue(def) as List<int>;
            if (!ticksQuality.NullOrEmpty())
            {
                var quality = (QualityCategory)qualityToOutputField.GetValue(process);
                label += " (" + quality.GetLabel().CapitalizeFirst() + ")";
            }

            float overclockMultiplier = (float)overclockMultiplierField.GetValue(comp);
            float notInRoomFactor = (float)getNotInRoomRoleFactorMethod.Invoke(comp, new object[] { thing });
            int cachedInitialTicks = (int)cachedInitialTicksField.GetValue(process);
            int duration = (int)((cachedInitialTicks / overclockMultiplier) / notInRoomFactor);
            label += " (" + duration.ToStringTicksToPeriod() + ")";

            bool suspended = (bool)suspendedField.GetValue(process);
            if (suspended)
            {
                label += ". " + "RimWorldAccess.Compat.Vef.ProcessorSuspendedClause".Translate();
            }
            else if (!(bool)shouldDoNowMethod.Invoke(process, null))
            {
                label += ". " + "RimWorldAccess.Compat.Vef.ProcessorInactiveClause".Translate();
            }

            if ((bool)missingIngredientsProperty.GetValue(process))
                label += ". " + "RimWorldAccess.Compat.Vef.ProcessorMissingIngredientsClause".Translate();

            return label;
        }

        private void BuildProcessChildren(InspectionTreeItem categoryItem, InspectionTreeItem secItem, ThingWithComps thing, object comp, object stack, object process, InspectionMode mode)
        {
            string repeatLabel = (string)repeatLabelProperty.GetValue(process);
            string repeatInfoText = (string)repeatInfoTextProperty.GetValue(process);
            InspectNodeFactory.DetailLine(secItem, "RimWorldAccess.Compat.Vef.ProcessorRepeatLine".Translate(repeatLabel, repeatInfoText));

            object def = defProperty.GetValue(process);
            string description = ((Def)def).description;
            if (!string.IsNullOrEmpty(description))
                InspectNodeFactory.DetailLine(secItem, description);

            if (mode == InspectionMode.ReadOnly)
                return;

            InspectNodeFactory.ActionRow(secItem, repeatLabel, process, () => OnOpenRepeatOptions(process));

            InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Vef.ProcessorIncreaseAction".Translate(), process,
                () => OnIncreaseCount(categoryItem, thing, stack, process, mode));
            InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Vef.ProcessorDecreaseAction".Translate(), process,
                () => OnDecreaseCount(categoryItem, thing, stack, process, mode));

            List<int> ticksQuality = ticksQualityField.GetValue(def) as List<int>;
            if (!ticksQuality.NullOrEmpty())
            {
                InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Vef.ProcessorQualityAction".Translate(), process,
                    () => OnOpenQualitySelect(process));
            }

            bool suspended = (bool)suspendedField.GetValue(process);
            string suspendActionLabel = suspended
                ? "RimWorldAccess.Compat.Vef.ProcessorUnsuspendAction".Translate()
                : "RimWorldAccess.Compat.Vef.ProcessorSuspendAction".Translate();
            InspectNodeFactory.ActionRow(secItem, suspendActionLabel, process,
                () => OnToggleSuspend(categoryItem, thing, stack, process, mode));

            int index = (int)indexOfMethod.Invoke(stack, new object[] { process });
            int count = ((IList)processesProperty.GetValue(stack)).Count;
            if (index > 0)
            {
                InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Vef.ProcessorMoveUpAction".Translate(), process,
                    () => OnMove(categoryItem, thing, stack, process, -1, mode));
            }
            if (index >= 0 && index < count - 1)
            {
                InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Vef.ProcessorMoveDownAction".Translate(), process,
                    () => OnMove(categoryItem, thing, stack, process, 1, mode));
            }

            InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Vef.ProcessorDeleteAction".Translate(), process,
                () => OnDeleteProcess(categoryItem, thing, stack, process, mode));
        }

        private void OnAddProcess(object comp)
        {
            Guarded.Run("VefProcessorTabAdapter add-process action", () =>
            {
                var options = processesOptionsProperty.GetValue(comp) as List<FloatMenuOption>;
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnSettings(object comp)
        {
            Guarded.Run("VefProcessorTabAdapter settings action", () =>
            {
                var options = settingsProperty.GetValue(comp) as List<FloatMenuOption>;
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnOpenRepeatOptions(object process)
        {
            Guarded.Run("VefProcessorTabAdapter repeat-options action", () =>
            {
                var options = optionsProperty.GetValue(process) as List<FloatMenuOption>;
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnOpenQualitySelect(object process)
        {
            Guarded.Run("VefProcessorTabAdapter quality-select action", () =>
            {
                var options = qualitySelectionsProperty.GetValue(process) as List<FloatMenuOption>;
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnCopy(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter copy action", () =>
            {
                IDictionary clipboard = clipboardField.GetValue(null) as IDictionary;
                if (clipboard == null)
                    return;

                object processes = processesProperty.GetValue(stack);

                // UI clipboard state, not game state.
                clipboard.Clear();
                clipboard[thing.def] = processes;

                // The Paste row must appear immediately, as vanilla's redraw makes it.
                RebuildCategory(categoryItem, thing, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorCopied".Loc());
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnPaste(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter paste action", () =>
            {
                IDictionary clipboard = clipboardField.GetValue(null) as IDictionary;
                if (clipboard == null || !clipboard.Contains(thing.def))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorNothingToPaste".Loc());
                    return;
                }

                IList sourceProcesses = clipboard[thing.def] as IList;
                IList processes = processesProperty.GetValue(stack) as IList;
                if (sourceProcesses == null || processes == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorActionFailed".Loc());
                    return;
                }

                var toAdd = new List<object>();
                foreach (object sourceProcess in sourceProcesses)
                    toAdd.Add(sourceProcess);

                // MUTATION-C: mirrors ITab_Processor's paste branch; the tab's own body is
                // inline IMGUI with no callable vanilla method.
                processes.Clear();
                foreach (object sourceProcess in toAdd)
                {
                    object sourceDef = defProperty.GetValue(sourceProcess);
                    object sourceRepeatMode = repeatModeField.GetValue(sourceProcess);
                    int sourceTargetCount = (int)targetCountField.GetValue(sourceProcess);
                    object sourceQuality = qualityToOutputField.GetValue(sourceProcess);
                    addProcessMethod.Invoke(stack, new object[] { sourceDef, thing, sourceRepeatMode, sourceTargetCount, sourceQuality });
                }

                foreach (object process in (IList)processesProperty.GetValue(stack))
                {
                    // MUTATION-C: mirrors ITab_Processor's paste branch progress reset; inline IMGUI body with no callable vanilla method.
                    progressProperty.SetValue(process, 0f);
                }

                RebuildCategory(categoryItem, thing, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorPasted".Loc());
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        // ResetProcess and Delete are public ProcessStack/Process methods, invoked unmodified.
        private void OnCancelCurrent(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, object process, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter cancel-current action", () =>
            {
                resetProcessMethod.Invoke(process, new object[] { false });
                deleteMethod.Invoke(stack, new object[] { process });
                SoundDefOf.Click.PlayOneShotOnCamera(null);

                RebuildCategory(categoryItem, thing, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorCancelled".Loc());
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnIncreaseCount(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, object process, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter increase-count action", () =>
            {
                if (!ProcessStillPresent(stack, process))
                {
                    RebuildCategory(categoryItem, thing, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorActionFailed".Loc());
                    return;
                }

                int targetCount = (int)targetCountField.GetValue(process);

                if (targetCount == -1)
                {
                    // MUTATION-C: mirrors Process.DoInterface's Plus branch; inline IMGUI widget body with no callable vanilla method.
                    repeatModeField.SetValue(process, BillRepeatModeDefOf.RepeatCount);
                    processCountField.SetValue(process, 0);
                    targetCountField.SetValue(process, 1);
                }
                else
                {
                    // MUTATION-C: mirrors Process.DoInterface's Plus branch; inline IMGUI widget body with no callable vanilla method.
                    targetCountField.SetValue(process, targetCount + 1);
                }

                SoundDefOf.DragSlider.PlayOneShotOnCamera(null);
                notifyProcessChangeMethod.Invoke(stack, null);

                RebuildCategory(categoryItem, thing, mode);

                string repeatInfoText = (string)repeatInfoTextProperty.GetValue(process);
                TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorCountSet".Loc(repeatInfoText));
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnDecreaseCount(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, object process, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter decrease-count action", () =>
            {
                if (!ProcessStillPresent(stack, process))
                {
                    RebuildCategory(categoryItem, thing, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorActionFailed".Loc());
                    return;
                }

                int targetCount = (int)targetCountField.GetValue(process);

                if (targetCount == -1)
                {
                    // MUTATION-C: mirrors Process.DoInterface's Minus branch; inline IMGUI widget body with no callable vanilla method.
                    repeatModeField.SetValue(process, BillRepeatModeDefOf.RepeatCount);
                    processCountField.SetValue(process, 0);
                    targetCountField.SetValue(process, 1);
                }
                else
                {
                    // MUTATION-C: mirrors Process.DoInterface's Minus branch; inline IMGUI widget body with no callable vanilla method.
                    targetCountField.SetValue(process, Mathf.Max(0, targetCount - 1));
                }

                SoundDefOf.DragSlider.PlayOneShotOnCamera(null);
                notifyProcessChangeMethod.Invoke(stack, null);

                RebuildCategory(categoryItem, thing, mode);

                string repeatInfoText = (string)repeatInfoTextProperty.GetValue(process);
                TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorCountSet".Loc(repeatInfoText));
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnToggleSuspend(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, object process, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter suspend action", () =>
            {
                if (!ProcessStillPresent(stack, process))
                {
                    RebuildCategory(categoryItem, thing, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorActionFailed".Loc());
                    return;
                }

                // MUTATION-C: mirrors Process.DoInterface's suspend toggle; inline IMGUI
                // widget body with no callable vanilla method.
                bool suspended = !(bool)suspendedField.GetValue(process);
                suspendedField.SetValue(process, suspended);

                if (suspended)
                {
                    notifyStopWorkingSoundMethod.Invoke(process, null);
                }
                else
                {
                    object def = defProperty.GetValue(process);
                    bool sustainerWhenWorking = (bool)sustainerWhenWorkingField.GetValue(def);
                    object sustainerDef = sustainerDefField.GetValue(def);
                    if (sustainerWhenWorking && sustainerDef != null)
                        notifyStartWorkingSoundMethod.Invoke(process, null);
                }

                SoundDefOf.Click.PlayOneShotOnCamera(null);
                notifyProcessChangeMethod.Invoke(stack, null);

                RebuildCategory(categoryItem, thing, mode);

                TolkHelper.Speak(suspended
                    ? "RimWorldAccess.Compat.Vef.ProcessorSuspended".Loc()
                    : "RimWorldAccess.Compat.Vef.ProcessorResumed".Loc());
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnMove(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, object process, int offset, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter move action", () =>
            {
                if (!ProcessStillPresent(stack, process))
                {
                    RebuildCategory(categoryItem, thing, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorActionFailed".Loc());
                    return;
                }

                reorderMethod.Invoke(stack, new object[] { process, offset });
                if (offset < 0)
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                else
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);

                RebuildCategory(categoryItem, thing, mode);

                TolkHelper.Speak(offset < 0
                    ? "RimWorldAccess.Compat.Vef.ProcessorMovedUp".Loc()
                    : "RimWorldAccess.Compat.Vef.ProcessorMovedDown".Loc());
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }

        private void OnDeleteProcess(InspectionTreeItem categoryItem, ThingWithComps thing, object stack, object process, InspectionMode mode)
        {
            Guarded.Run("VefProcessorTabAdapter delete action", () =>
            {
                if (!ProcessStillPresent(stack, process))
                {
                    RebuildCategory(categoryItem, thing, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorActionFailed".Loc());
                    return;
                }

                resetProcessMethod.Invoke(process, new object[] { false });
                deleteMethod.Invoke(stack, new object[] { process });
                SoundDefOf.Click.PlayOneShotOnCamera(null);

                RebuildCategory(categoryItem, thing, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Vef.ProcessorDeleted".Loc());
            }, "RimWorldAccess.Compat.Vef.ProcessorActionFailed");
        }
    }
}
