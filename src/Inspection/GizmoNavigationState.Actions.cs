using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Sound;
using RimWorld;
using RimWorld.Planet;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess
{
    public static partial class GizmoNavigationState
    {
        /// <summary>Opens the windowless material picker for a Designator_Build gizmo.</summary>
        internal static void HandleBuildDesignatorMaterialSelection(Designator_Build buildDesignator, BuildableDef buildable)
        {
            MaterialHarvestOutcome outcome = MaterialMenuHarvest.TryBuildOptions(
                buildDesignator,
                (material, vanillaAction) => OnGizmoMaterialSelected(buildDesignator, material, vanillaAction),
                out List<FloatMenuOption> options,
                out System.Action vanillaOnClose);

            if (outcome == MaterialHarvestOutcome.Menu)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.SelectMaterialFor".Loc(buildable.label));
                WindowlessFloatMenuState.Open(options, false, playOpenSound: false,
                    onClose: _ => vanillaOnClose?.Invoke());
                Close();
                return;
            }

            if (outcome == MaterialHarvestOutcome.NoMenu)
            {
                // Vanilla already messaged; stay in the gizmo menu like the empty case below.
                return;
            }

            List<FloatMenuOption> legacy = ArchitectHelper.CreateMaterialOptions(
                buildable,
                (material) => OnGizmoMaterialSelected(buildDesignator, material, null));

            if (legacy.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoMaterialsForBuildable".Loc(buildable.label));
                return;
            }

            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.SelectMaterialFor".Loc(buildable.label));
            WindowlessFloatMenuState.Open(legacy, false);
            Close();
        }

        /// <summary>
        /// Applies a chosen material. <paramref name="vanillaAction"/>, when given, is the harvested
        /// option's own delegate; null takes the direct path, setting the material and selecting
        /// the designator.
        /// </summary>
        private static void OnGizmoMaterialSelected(Designator_Build designator, ThingDef material, System.Action vanillaAction)
        {
            try
            {
                if (vanillaAction != null)
                {
                    // The harvested delegate carries Select, stuffDef and writeStuff;
                    // DesignatorManagerPatch routes its Select to accessible placement.
                    vanillaAction();
                }
                else
                {
                    designator.SetStuffDef(material);
                    // Mirrors Designator_Build.ProcessInput's stuff menu; SetStuffDef alone leaves the label in pre-material form.
                    BuildingReflection.SetWriteStuff(designator, true);
                    Find.DesignatorManager.Select(designator);
                }

                if (material != null)
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.MaterialSelected".Loc(material.LabelCap));
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in gizmo material selection: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorWithMessage".Loc(ex.Message), SpeechPriority.High);
            }
        }

        // ===== Right-click float menu support =====

        /// <summary>The Designator behind a gizmo: the gizmo itself, or the reverse designator it was made from.</summary>
        private static Designator DesignatorBehind(Gizmo gizmo)
        {
            Designator direct = gizmo as Designator;
            if (direct != null)
                return direct;
            Designator source;
            return reverseGizmoSources.TryGetValue(gizmo, out source) ? source : null;
        }

        /// <summary>Whether a gizmo offers any right-click float menu options.</summary>
        private static bool HasRightClickOptions(Gizmo gizmo)
        {
            try
            {
                if (gizmo.RightClickFloatMenuOptions.Any())
                    return true;
            }
            catch
            {
                return false;
            }

            Designator behind = DesignatorBehind(gizmo);
            if (behind != null && DesignatorContextMenuRouter.HasOptions(behind))
                return true;

            // Registry-contributed extras (hover widgets vanilla exposes only through checkboxes
            // or right-click branches) also arm the hint.
            var extras = new List<FloatMenuOption>();
            return GizmoHandlerRegistry.CollectExtraOptions(gizmo, extras);
        }


        /// <summary>Right-click options from the gizmo and its grouped gizmos, mirroring GizmoGridDrawer.</summary>
        private static List<FloatMenuOption> CollectRightClickOptions(Gizmo gizmo)
        {
            var options = new List<FloatMenuOption>();

            try
            {
                foreach (var opt in gizmo.RightClickFloatMenuOptions)
                    options.Add(opt);

                if (gizmoGroups.TryGetValue(gizmo, out var group))
                {
                    for (int i = 0; i < group.Count; i++)
                    {
                        Gizmo other = group[i];
                        if (other == gizmo || other.Disabled || !gizmo.InheritFloatMenuInteractionsFrom(other))
                            continue;

                        foreach (var opt in other.RightClickFloatMenuOptions)
                        {
                            var existing = options.FirstOrDefault(o => o.Label == opt.Label);
                            if (existing == null)
                            {
                                options.Add(opt);
                            }
                            else if (!opt.Disabled && existing.Disabled)
                            {
                                int idx = options.IndexOf(existing);
                                options[idx] = opt;
                            }
                            else if (!opt.Disabled && !existing.Disabled)
                            {
                                System.Action prevAction = existing.action;
                                System.Action localAction = opt.action;
                                existing.action = delegate { prevAction(); localAction(); };
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception collecting right-click options: {ex.Message}");
            }

            GizmoHandlerRegistry.CollectExtraOptions(gizmo, options);

            return options;
        }

        /// <summary>Opens the selected gizmo's right-click options in the accessible WindowlessFloatMenuState.</summary>
        public static void ExecuteRightClick()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            if (gizmo.Disabled)
            {
                // Vanilla right-click options are unreachable on a disabled command, but a
                // handler's extra options may deliberately survive disable where the drawn widget
                // does — Vehicle Framework's turret quota button is a sighted player's only way to
                // arm an empty, and therefore disabled, turret. Handlers self-gate what parity
                // forbids; when nothing survives, speak the disabled reason.
                var disabledExtras = new List<FloatMenuOption>();
                GizmoHandlerRegistry.CollectExtraOptions(gizmo, disabledExtras);
                if (disabledExtras.Count == 0)
                {
                    string reason = gizmo.disabledReason;
                    if (string.IsNullOrEmpty(reason))
                        reason = "RimWorldAccess.Inspection.Gizmo.DisabledExecuteFallback".Translate();
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.DisabledSuffix".Loc(reason));
                    return;
                }
                WindowlessFloatMenuState.Open(disabledExtras, colonistOrders: false);
                return;
            }

            var options = CollectRightClickOptions(gizmo);

            if (options.Count > 0)
            {
                WindowlessFloatMenuState.Open(options, colonistOrders: false);
                return;
            }

            if (GizmoHandlerRegistry.MayBranchOnRightClick(gizmo))
            {
                DispatchRightButtonInput(gizmo);
                return;
            }

            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoAdditionalOptions".Loc());
        }

        /// <summary>GizmoGridDrawer.ProcessGizmoState treats a right-click with no float menu options as an
        /// interaction: the right-button event rides the left-click ProcessInput chain. Every changed
        /// toggle in the menu is spoken, else what a registered outcome describer reports.</summary>
        private static void DispatchRightButtonInput(Gizmo gizmo)
        {
            var toggles = new List<(Command_Toggle toggle, ISelectable owner, bool wasOn)>();
            foreach (Gizmo candidate in availableGizmos)
            {
                if (candidate is Command_Toggle toggle && TryReadToggle(toggle, out bool isOn))
                {
                    gizmoOwners.TryGetValue(candidate, out ISelectable owner);
                    toggles.Add((toggle, owner, isOn));
                }
            }
            gizmoOwners.TryGetValue(gizmo, out ISelectable gizmoOwner);
            var outcomes = RightClickOutcomeRegistry.Capture(gizmo, gizmoOwner);
            int windowsBefore = Find.WindowStack.Count;

            ExecuteSelected(1);

            if (LastExecutionOpenedMenu || Find.WindowStack.Count != windowsBefore)
                return;

            var changes = new List<string>();
            foreach (var entry in toggles)
            {
                if (TryReadToggle(entry.toggle, out bool isOn) && isOn != entry.wasOn)
                    changes.Add(ComposeGizmoStateChange(entry.toggle, entry.owner, includeLabel: true));
            }
            if (changes.Count == 0)
                RightClickOutcomeRegistry.AppendChanges(outcomes, gizmo, gizmoOwner, changes);

            if (changes.Count > 0)
                TolkHelper.SpeakData(JoinSentences(changes));
        }

        private static bool TryReadToggle(Command_Toggle toggle, out bool isOn)
        {
            try
            {
                isOn = toggle.isActive?.Invoke() ?? false;
                return true;
            }
            catch
            {
                isOn = false;
                return false;
            }
        }

        // ===== Slider adjustment support =====

        /// <summary>
        /// Adjusts the adapter's value by step * multiplier, writing through the adapter and
        /// speaking its readout. Stateless: both callers resolve a fresh adapter per press, since
        /// some adapters capture the current value in their write closure.
        /// </summary>
        private static void AdjustSliderValue(GizmoSliderAdapter adapter, int direction, int multiplier)
        {
            float min = adapter.Min;
            float max = adapter.Max;
            float step = adapter.Step > 0f ? adapter.Step : (max - min) / 20f;

            float oldValue = adapter.Value;
            float value = Mathf.Clamp(oldValue + direction * step * multiplier, min, max);

            // Snap to the nearest step to avoid floating-point drift.
            float stepsFromMin = Mathf.Round((value - min) / step);
            value = Mathf.Clamp(min + stepsFromMin * step, min, max);

            if (Mathf.Approximately(value, oldValue))
            {
                NumericStepperHelper.SpeakBoundary(direction);
                return;
            }

            SoundDefOf.DragSlider.PlayOneShotOnCamera();

            try
            {
                adapter.Write(value);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception writing slider value: {ex.Message}");
            }

            string valueText = null;
            try
            {
                valueText = adapter.DescribeValue?.Invoke(value);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception describing slider value: {ex.Message}");
            }
            if (string.IsNullOrEmpty(valueText))
                valueText = "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetPercent"
                    .Translate((value * 100).ToString("F0"));

            // One announcement per action, in the shared state-change grammar: the row's own
            // refreshed readout, never an invented raw float, plus the bound word once the value
            // lands on an end of the range.
            var d = new ElementDescription
            {
                Role = ElementRole.Slider,
                Value = valueText,
                AtMinimum = Mathf.Approximately(value, min),
                AtMaximum = Mathf.Approximately(value, max),
            };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>
        /// Activates a gizmo whose hotkey matches the key, searching both the current selection and
        /// the cursor-tile objects. Always consumes the event: one match activates, several open a
        /// WindowlessFloatMenuState to disambiguate, none plays the rejection sound and announces
        /// that no command is bound so the player knows the press was received.
        /// Cursor-tile gizmos are collected with the same temp-select-then-restore pattern as
        /// OpenAtCursor, because some gizmos only expose themselves when their Thing is selected.
        /// </summary>
        public static bool TryHotkeyActivate(KeyCode key)
        {
            if (key == KeyCode.None)
                return false;
            if (Find.Selector == null || Find.CurrentMap == null)
                return false;

            var collected = CollectHotkeyCandidates(key);

            // Group matching gizmos through vanilla's GroupsWith/MergeWith so identical gizmos from
            // several selected pawns collapse into one entry.
            var rawGizmos = collected.Select(c => c.gizmo).ToList();
            var rawOwners = new Dictionary<Gizmo, ISelectable>();
            foreach (var (gizmo, owner) in collected)
            {
                if (!rawOwners.ContainsKey(gizmo))
                    rawOwners[gizmo] = owner;
            }

            var groups = new List<List<Gizmo>>();
            foreach (var gizmo in rawGizmos)
            {
                bool grouped = false;
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i][0].GroupsWith(gizmo))
                    {
                        groups[i].Add(gizmo);
                        groups[i][0].MergeWith(gizmo);
                        grouped = true;
                        break;
                    }
                }
                if (!grouped)
                    groups.Add(new List<Gizmo> { gizmo });
            }

            var representatives = groups
                .Select(g => g[0])
                .OrderBy(g => g.Order)
                .ToList();

            if (representatives.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.NoCommandForShiftPlus".Loc(key.ToStringReadable()));
                return true;
            }

            if (representatives.Count == 1)
            {
                Gizmo only = representatives[0];
                ISelectable owner = rawOwners.TryGetValue(only, out var o) ? o : null;
                List<Gizmo> group = groups.First(g => g[0] == only);
                ActivateSingleGizmo(only, owner, group);
                return true;
            }

            // Several matches: open a menu so the player hears the familiar open sound and can
            // pick. Each option's Label is the full rich gizmo announcement, so it carries the same
            // information the G menu would.
            var options = new List<FloatMenuOption>();
            foreach (var rep in representatives)
            {
                ISelectable owner = rawOwners.TryGetValue(rep, out var o) ? o : null;
                List<Gizmo> group = groups.First(g => g[0] == rep);
                string label = BuildGizmoMenuLabel(rep, owner);

                if (rep.Disabled)
                {
                    options.Add(new FloatMenuOption(label, null) { Disabled = true });
                    continue;
                }

                Gizmo capturedGizmo = rep;
                ISelectable capturedOwner = owner;
                List<Gizmo> capturedGroup = group;
                options.Add(new FloatMenuOption(label, () =>
                {
                    ActivateSingleGizmo(capturedGizmo, capturedOwner, capturedGroup);
                }));
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return true;
        }

        /// <summary>
        /// Seeds the state with a single gizmo and invokes ExecuteSelected so every existing
        /// activation path applies unchanged, then closes the state so no phantom single-item G
        /// menu is left behind.
        /// </summary>
        private static void ActivateSingleGizmo(Gizmo gizmo, ISelectable owner, List<Gizmo> group)
        {
            availableGizmos.Clear();
            gizmoOwners.Clear();
            gizmoGroups.Clear();
            reverseGizmoSources.Clear();
            availableGizmos.Add(gizmo);
            if (owner != null)
                gizmoOwners[gizmo] = owner;
            gizmoGroups[gizmo] = group ?? new List<Gizmo> { gizmo };
            selectedGizmoIndex = 0;
            isActive = true;
            // No opener built this one-shot list, so a toggle must not try to refresh it.
            menuSource = MenuSource.None;
            typeahead.ClearSearch();
            lastAnnouncedOwner = null;
            pawnJustSelected = false;

            ExecuteSelected();

            // ExecuteSelected leaves toggle, verb-target and target paths open for further
            // navigation; in hotkey context the player is done.
            if (isActive)
                Close();
        }

        /// <summary>
        /// The rich announcement for a FloatMenuOption row when Shift+hotkey has several matches:
        /// the unified composer in MenuLabel mode, owner suffix on every row and no interaction hints.
        /// </summary>
        private static string BuildGizmoMenuLabel(Gizmo gizmo, ISelectable owner)
        {
            // The hotkey collector swaps selection per thing and restores it afterward, so lazy
            // properties evaluated here would otherwise resolve against the stale selection.
            return WithGizmoOwnerSelected(gizmo, owner,
                () => BuildGizmoMenuLabelInner(gizmo, owner));
        }

        /// <summary>
        /// Every visible gizmo whose hotkey is live-bound to the key in either slot, from the current
        /// selection and the cursor tile. The cursor tile uses OpenAtCursor's temporary-selection
        /// pattern so lazy gizmos become visible.
        /// </summary>
        private static List<(Gizmo gizmo, ISelectable owner)> CollectHotkeyCandidates(KeyCode key)
        {
            var results = new List<(Gizmo, ISelectable)>();
            var seenGizmos = new HashSet<Gizmo>();
            var ownersAlreadyProcessed = new HashSet<ISelectable>();

            // Current selection: these objects' gizmos are already live.
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (!(obj is ISelectable selectable))
                    continue;
                ownersAlreadyProcessed.Add(selectable);
                foreach (var gizmo in selectable.GetGizmos())
                {
                    if (gizmo == null || !gizmo.Visible || ShouldSkipGizmo(gizmo))
                        continue;
                    if (!MatchesHotkey(gizmo, key))
                        continue;
                    if (seenGizmos.Add(gizmo))
                        results.Add((gizmo, selectable));
                }

                // Thing.GetGizmos() omits reverse designators, which vanilla's InspectGizmoGrid
                // combines in at render time. Mirrored here so hotkeys keep working once a
                // selection lands on the Thing.
                if (selectable is Thing selectedThing)
                {
                    List<Designator> selectedReverseDesignators = Find.ReverseDesignatorDatabase.AllDesignators;
                    for (int i = 0; i < selectedReverseDesignators.Count; i++)
                    {
                        Command_Action reverseGizmo = selectedReverseDesignators[i].CreateReverseDesignationGizmo(selectedThing);
                        if (reverseGizmo == null || ShouldSkipGizmo(reverseGizmo))
                            continue;
                        if (!MatchesHotkey(reverseGizmo, key))
                            continue;
                        if (seenGizmos.Add(reverseGizmo))
                            results.Add((reverseGizmo, selectable));
                    }
                }
            }

            // Cursor tile: temp-select each thing so lazy gizmos appear, then restore.
            if (!MapNavigationState.IsInitialized)
                return results;

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;
            if (!cursor.IsValid || !cursor.InBounds(map))
                return results;

            var previousSelection = Find.Selector.SelectedObjects.ToList();
            try
            {
                var sortedThings = cursor.GetThingList(map)
                    .Where(t => !(t is Mote) && t.def.category != ThingCategory.Mote)
                    .Where(t => !HiddenPawns.IsHidden(t))
                    .OrderByDescending(t => (int)t.def.altitudeLayer)
                    .ToList();

                foreach (ISelectable selectable in sortedThings.OfType<ISelectable>())
                {
                    if (ownersAlreadyProcessed.Contains(selectable))
                        continue;

                    Find.Selector.ClearSelection();
                    Find.Selector.Select(selectable, playSound: false, forceDesignatorDeselect: false);

                    foreach (var gizmo in selectable.GetGizmos())
                    {
                        if (gizmo == null || !gizmo.Visible || ShouldSkipGizmo(gizmo))
                            continue;
                        if (!MatchesHotkey(gizmo, key))
                            continue;
                        if (seenGizmos.Add(gizmo))
                            results.Add((gizmo, selectable));
                    }

                    if (selectable is Thing thing)
                    {
                        List<Designator> reverseDesignators = Find.ReverseDesignatorDatabase.AllDesignators;
                        for (int i = 0; i < reverseDesignators.Count; i++)
                        {
                            Command_Action reverseGizmo = reverseDesignators[i].CreateReverseDesignationGizmo(thing);
                            if (reverseGizmo == null || ShouldSkipGizmo(reverseGizmo))
                                continue;
                            if (!MatchesHotkey(reverseGizmo, key))
                                continue;
                            if (seenGizmos.Add(reverseGizmo))
                                results.Add((reverseGizmo, selectable));
                        }
                    }
                }

                Zone zone = cursor.GetZone(map);
                if (zone != null && !ownersAlreadyProcessed.Contains(zone))
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(zone, playSound: false, forceDesignatorDeselect: false);

                    foreach (var gizmo in zone.GetGizmos())
                    {
                        if (gizmo == null || !gizmo.Visible || ShouldSkipGizmo(gizmo))
                            continue;
                        if (!MatchesHotkey(gizmo, key))
                            continue;
                        if (seenGizmos.Add(gizmo))
                            results.Add((gizmo, zone));
                    }
                }
            }
            finally
            {
                Find.Selector.ClearSelection();
                foreach (var obj in previousSelection.OfType<ISelectable>())
                {
                    Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                }
            }

            return results;
        }

        private static bool MatchesHotkey(Gizmo gizmo, KeyCode key)
        {
            if (!(gizmo is Command command))
                return false;
            if (command.hotKey == null)
                return false;
            if (GizmoHotkeyShiftPatch.IsShiftExempt(command.hotKey))
                return false;
            return VanillaBindings.IsBoundTo(command.hotKey, key);
        }

        /// <summary>
        /// How many designations a reverse-designation gizmo actually produced, measured across its
        /// execution and propagation. Vanilla's reverse gizmo is a bare Command_Action whose only
        /// feedback is the designation appearing on the map. Inert for every other kind of gizmo,
        /// and silent when the designator or the mod already emitted a message of its own.
        /// </summary>
        private readonly struct ReverseDesignationOutcome
        {
            private readonly string label;
            private readonly Map map;
            private readonly DesignationDef designation;
            private readonly bool applicable;
            private readonly int countBefore;
            private readonly long messagesBefore;

            private ReverseDesignationOutcome(string label, Map map, DesignationDef designation,
                bool applicable, int countBefore, long messagesBefore)
            {
                this.label = label;
                this.map = map;
                this.designation = designation;
                this.applicable = applicable;
                this.countBefore = countBefore;
                this.messagesBefore = messagesBefore;
            }

            public static ReverseDesignationOutcome Begin(Gizmo gizmo, string label)
            {
                Designator source = DesignatorBehind(gizmo);
                if (source == null)
                    return default(ReverseDesignationOutcome);

                Map map = Find.CurrentMap;
                DesignationDef designation = map == null ? null : TileInfoHelper.GetDesignationDef(source);
                int before = designation == null ? 0 : CountDesignations(map, designation);
                return new ReverseDesignationOutcome(label, map, designation, true, before,
                    NotificationAccessibilityPatch.MessageEmissionCount);
            }

            public void Announce()
            {
                if (!applicable)
                    return;
                // The designator or the mod spoke for itself; ours would be the second voice.
                if (NotificationAccessibilityPatch.MessageEmissionCount != messagesBefore)
                    return;

                if (designation == null)
                {
                    // A reverse designator with no designation def of its own (Allow Tool's Select
                    // Similar changes the selection instead). Nothing to count, but the player
                    // still needs to know the command ran.
                    TolkHelper.SpeakData("RimWorldAccess.Inspection.Gizmo.CommandApplied".Translate(label));
                    return;
                }

                int designated = CountDesignations(map, designation) - countBefore;
                TolkHelper.SpeakData(designated > 0
                    ? "RimWorldAccess.Inspection.Gizmo.DesignatedCount".Translate(designated, label)
                    : "RimWorldAccess.Inspection.Gizmo.DesignatedNone".Translate(label));
            }

            private static int CountDesignations(Map map, DesignationDef def)
            {
                return map.designationManager.SpawnedDesignationsOfDef(def).Count();
            }
        }
    }
}
