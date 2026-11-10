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
        /// <summary>Labels for all gizmos.</summary>
        private static List<string> GetGizmoLabels()
        {
            var labels = new List<string>();
            foreach (var gizmo in availableGizmos)
            {
                // Lazy labels (Designator_Install) need the owner single-selected to resolve.
                Gizmo captured = gizmo;
                labels.Add(WithGizmoOwnerSelected(captured, null, () => GetGizmoLabel(captured)));
            }
            return labels;
        }

        /// <summary>Announces the current selection, with search context when a search is live.</summary>
        private static void AnnounceWithSearch()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            if (typeahead.HasActiveSearch)
            {
                // Lazy gizmo properties (Label, Disabled) check Find.Selector.SingleSelectedThing.
                WithGizmoOwnerSelected(gizmo, null, () =>
                {
                    ISelectable gizmoOwner = null;
                    if (gizmoOwners.Count > 0)
                        gizmoOwners.TryGetValue(gizmo, out gizmoOwner);

                    // Search context rides right after the label. No owner prefix and no "x of y":
                    // the search suffix already carries the match position, and a search jump must
                    // not advance the owner dedup.
                    ElementDescription d = DescribeGizmo(gizmo, gizmoOwner);
                    d.Label += "RimWorldAccess.Search.ContextSuffix".Translate(
                        typeahead.CurrentMatchPosition, typeahead.MatchCount, typeahead.SearchBuffer);
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(
                        d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
                });
            }
            else
            {
                AnnounceCurrentGizmo();
            }
        }

        /// <summary>Announces the selected gizmo through the shared element composer, so it sounds like any other row.</summary>
        private static void AnnounceCurrentGizmo()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            // Lazy gizmo properties (Label, Desc, Disabled) read Find.Selector.SingleSelectedThing.
            WithGizmoOwnerSelected(gizmo, null, () => AnnounceCurrentGizmoInner(gizmo));
        }

        private static void AnnounceCurrentGizmoInner(Gizmo gizmo)
        {
            ISelectable owner = null;
            if (gizmoOwners.Count > 0)
                gizmoOwners.TryGetValue(gizmo, out owner);

            ElementDescription d = DescribeGizmo(gizmo, owner);
            ApplyOwnerPrefix(d, owner);
            d.PositionIndex = selectedGizmoIndex + 1;
            d.PositionCount = availableGizmos.Count;
            TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(
                d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
        }

        /// <summary>
        /// Describes one gizmo as a standard shell element: Command_Toggle becomes a checkbox with
        /// its live state, an adjustable gizmo a slider with its value, an inert readout a
        /// read-only row, everything else a button.
        ///
        /// PURE with respect to the G menu's cursor state — no position fragment, no owner-change
        /// prefix (those belong to <see cref="AnnounceCurrentGizmoInner"/> /
        /// <see cref="ApplyOwnerPrefix"/>) — so the inspect tree's Gizmos node can describe the
        /// same gizmo with its own position without disturbing the menu's owner dedup.
        ///
        /// Callers wrap this in <see cref="WithGizmoOwnerSelected"/> so lazy gizmo properties
        /// resolve against the owning Thing.
        /// </summary>
        public static ElementDescription DescribeGizmo(Gizmo gizmo, ISelectable owner)
        {
            var d = new ElementDescription();
            if (gizmo == null)
                return d;

            d.Label = GetGizmoLabel(gizmo);
            d.Hotkey = GetGizmoHotkey(gizmo);

            // Type-specific fragments resolve through the same handler registry ExecuteSelected
            // uses.
            var fragments = new GizmoDescriptionFragments(GizmoDescriptionMode.Speech);
            GizmoHandlerRegistry.Describe(gizmo, fragments);

            string status = GetGizmoStatusValue(gizmo);
            bool hasSlider = GizmoHandlerRegistry.TryResolveSliderAdapter(gizmo, out GizmoSliderAdapter adapter);
            d.Value = hasSlider
                ? DescribeSliderValue(adapter, status)
                : (string.IsNullOrEmpty(status) ? null : status);

            if (fragments.ToggleState.HasValue)
            {
                // The check state carries ON/OFF; Command.TopRightLabel stays in the value.
                d.Role = ElementRole.Checkbox;
                d.Check = fragments.ToggleState;
            }
            else if (hasSlider)
            {
                d.Role = ElementRole.Slider;
            }
            else if (!string.IsNullOrEmpty(status) && !GizmoHandlerRegistry.HasActivation(gizmo))
            {
                // A pure readout: navigable, and honest about not being operable.
                d.Role = ElementRole.None;
                d.ReadOnly = true;
            }
            else
            {
                d.Role = ElementRole.Button;
            }

            d.Disabled = gizmo.Disabled;
            d.Extras = BuildGizmoExtras(gizmo, owner, fragments);
            if (!gizmo.Disabled)
                d.Hint = BuildGizmoHint(gizmo, adapter);
            return d;
        }

        /// <summary>
        /// The G menu's owner-change prefix over an already-built description. The owner's name is
        /// spoken only when it differs from the last announced one. The dedup state advances on ANY
        /// owner change, labelable or not, so an unlabelable owner still resets it.
        /// </summary>
        private static void ApplyOwnerPrefix(ElementDescription d, ISelectable owner)
        {
            if (owner == null || owner == lastAnnouncedOwner)
                return;

            lastAnnouncedOwner = owner;
            string ownerLabel = GetOwnerLabel(owner);
            if (!string.IsNullOrEmpty(ownerLabel))
                d.Label = "RimWorldAccess.Inspection.Gizmo.OwnerPrefix".Translate(ownerLabel, d.Label);
        }

        /// <summary>The spoken name of a gizmo's owning object, or null when it has none.</summary>
        private static string GetOwnerLabel(ISelectable owner)
        {
            if (owner is Thing thing)
                return thing.LabelCap.StripTags();
            if (owner is WorldObject worldObj)
                return worldObj.LabelCap.StripTags();
            if (owner is Plan plan)
                return plan.RenamableLabel;
            return null;
        }

        /// <summary>
        /// The slider row's spoken value: the adapter's own readout when it has one, else the
        /// gizmo's status text. Never an invented raw float.
        /// </summary>
        private static string DescribeSliderValue(GizmoSliderAdapter adapter, string status)
        {
            if (adapter != null && adapter.DescribeValue != null)
            {
                try
                {
                    string described = adapter.DescribeValue(adapter.Value);
                    if (!string.IsNullOrEmpty(described))
                        return described;
                }
                catch (System.Exception ex)
                {
                    ModLogger.Error($"Exception describing slider value: {ex.Message}");
                }
            }
            return string.IsNullOrEmpty(status) ? null : status;
        }

        /// <summary>
        /// The verbose tail: disabled reason and its extra context first, then an ability's
        /// cost/range/cooldown, the description, and a Command_Target's range suffix.
        /// </summary>
        private static string BuildGizmoExtras(Gizmo gizmo, ISelectable owner, GizmoDescriptionFragments fragments)
        {
            var parts = new List<string>();

            if (gizmo.Disabled)
            {
                string reason = gizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "RimWorldAccess.Inspection.Gizmo.DisabledNotAvailable".Translate();
                parts.Add(reason);
                AppendFragment(parts, GetDisabledGizmoContext(gizmo, owner));
            }

            string description = RedundantLabelPrefix.Strip(GetGizmoDescription(gizmo), GetGizmoLabel(gizmo));
            if (gizmo is Command_Ability && fragments.AbilityInfoAvailable)
            {
                AppendFragment(parts, fragments.AbilityCostInfo);
                AppendFragment(parts, fragments.AbilityRangeInfo);
                AppendFragment(parts, fragments.AbilityCooldownInfo);
            }
            AppendFragment(parts, description);
            AppendFragment(parts, fragments.TargetRangeSuffix);

            return JoinSentences(parts);
        }

        /// <summary>
        /// Interaction hints: a gizmo with right-click options speaks its handler's own hint
        /// first — a slider can carry BOTH a custom Enter action and a right-bracket menu, so the
        /// second must stay discoverable — then the right-bracket hint; a plain adjustable slider
        /// gets the generic arrow-adjust hint. The composer gates the fragment on the
        /// interaction-hints setting.
        /// </summary>
        private static string BuildGizmoHint(Gizmo gizmo, GizmoSliderAdapter adapter)
        {
            var parts = new List<string>();
            if (HasRightClickOptions(gizmo))
            {
                if (adapter != null && !string.IsNullOrEmpty(adapter.InteractionHint))
                    AppendFragment(parts, TrimLeadingSeparator(adapter.InteractionHint));
                AppendFragment(parts, TrimLeadingSeparator(
                    "RimWorldAccess.Inspection.Gizmo.HintRightClickOptions".Translate().ToString()));
            }
            else if (adapter != null)
            {
                AppendFragment(parts, TrimLeadingSeparator(adapter.InteractionHint
                    ?? "RimWorldAccess.Inspection.Gizmo.HintArrowsToAdjust".Translate().ToString()));
            }
            return JoinSentences(parts);
        }

        /// <summary>
        /// Hint keys were authored as concatenation SUFFIXES, so their values open with joining
        /// punctuation. The composer owns separators, so strip a leading punctuation/whitespace run
        /// rather than rewriting every already-translated value.
        /// </summary>
        private static string TrimLeadingSeparator(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            int start = 0;
            while (start < text.Length && (char.IsWhiteSpace(text[start]) || char.IsPunctuation(text[start])))
                start++;
            return start == 0 ? text : text.Substring(start);
        }

        private static void AppendFragment(List<string> parts, string fragment)
        {
            if (!string.IsNullOrEmpty(fragment))
                parts.Add(fragment);
        }

        /// <summary>
        /// Joins fragments the way AnnouncementComposer does: ". ", skipping the period when the
        /// fragment already ends in sentence punctuation, never a newline.
        /// </summary>
        private static string JoinSentences(List<string> parts)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                string fragment = parts[i].Trim();
                if (fragment.Length == 0)
                    continue;
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if (last != '.' && last != '!' && last != '?' && last != ':')
                        sb.Append('.');
                    sb.Append(' ');
                }
                sb.Append(fragment);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Speaks the refreshed state of a gizmo just acted on — one announcement, rebuilt from
        /// the live gizmo AFTER the vanilla vehicle ran. ComposeStateChange's bare-state grammar
        /// presumes the row is still focused — right for a toggle in the open G menu and for
        /// slider adjustments. A one-shot activation (hotkey, inspect tree) closes the menu, so
        /// its caller asks for the row's own label as a prefix ("Draft, checked").
        /// </summary>
        internal static void SpeakGizmoStateChange(Gizmo gizmo, bool includeLabel = true)
        {
            if (gizmo == null)
                return;

            ISelectable owner = null;
            if (gizmoOwners.Count > 0)
                gizmoOwners.TryGetValue(gizmo, out owner);

            TolkHelper.SpeakData(ComposeGizmoStateChange(gizmo, owner, includeLabel));
        }

        private static string ComposeGizmoStateChange(Gizmo gizmo, ISelectable owner, bool includeLabel)
        {
            ElementDescription d = WithGizmoOwnerSelected(gizmo, owner, () => DescribeGizmo(gizmo, owner));
            string state = AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance);
            string label = d.Label;
            if (string.IsNullOrEmpty(state))
                state = label;
            else if (includeLabel && !string.IsNullOrEmpty(label))
                state = label + ", " + state;
            return state;
        }

        /// <summary>
        /// The row string for the Shift+hotkey disambiguation float menu. Menu rows are single
        /// strings with no element grammar, so this composes its own: the owner is suffixed on
        /// EVERY row (each must stand alone, unlike the speech path's dedup prefix) and no
        /// interaction hints are carried. Callers wrap this in
        /// <see cref="WithGizmoOwnerSelected"/>.
        /// </summary>
        private static string BuildGizmoMenuLabelInner(Gizmo gizmo, ISelectable owner)
        {
            string label = GetGizmoLabel(gizmo);
            string description = GetGizmoDescription(gizmo);
            string hotkey = GetGizmoHotkey(gizmo);
            string statusValue = GetGizmoStatusValue(gizmo);

            string announcement = label;
            string ownerLabel = GetOwnerLabel(owner);
            if (!string.IsNullOrEmpty(ownerLabel))
                announcement += "RimWorldAccess.Inspection.Gizmo.MenuOwnerSuffix".Translate(ownerLabel);

            // Hotkey immediately after the title, before any description or stats.
            if (!string.IsNullOrEmpty(hotkey))
                announcement += "RimWorldAccess.Inspection.Gizmo.MenuHotkeySuffix".Translate(hotkey);

            var fragments = new GizmoDescriptionFragments(GizmoDescriptionMode.MenuLabel);
            GizmoHandlerRegistry.Describe(gizmo, fragments);

            // Sighted players see a checkbox, so speak the ON/OFF state.
            if (fragments.ToggleStateSuffix != null)
                announcement += fragments.ToggleStateSuffix;

            if (!string.IsNullOrEmpty(statusValue))
                announcement += "RimWorldAccess.Inspection.Gizmo.MenuStatusSuffix".Translate(statusValue);

            bool isAbility = gizmo is Command_Ability;

            if (!string.IsNullOrEmpty(description) && !isAbility)
            {
                if (gizmo is Command_Toggle || !string.IsNullOrEmpty(statusValue))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + description;
                else
                    announcement += "RimWorldAccess.Inspection.Gizmo.MenuDescriptionSuffix".Translate(description);
            }

            if (fragments.TargetRangeSuffix != null)
            {
                string sep = announcement.EndsWith(".") ? " " : ". ";
                announcement += sep + fragments.TargetRangeSuffix;
            }

            if (isAbility && fragments.AbilityInfoAvailable)
            {
                if (!string.IsNullOrEmpty(fragments.AbilityCostInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + fragments.AbilityCostInfo;

                if (!string.IsNullOrEmpty(fragments.AbilityRangeInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + fragments.AbilityRangeInfo;

                if (!string.IsNullOrEmpty(fragments.AbilityCooldownInfo))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + fragments.AbilityCooldownInfo;

                if (!string.IsNullOrEmpty(description))
                    announcement += (announcement.EndsWith(".") ? " " : ". ") + description;
            }

            if (gizmo.Disabled)
            {
                string reason = gizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "RimWorldAccess.Inspection.Gizmo.DisabledNotAvailable".Translate();
                announcement += " " + "RimWorldAccess.Inspection.Gizmo.DisabledSuffix".Translate(reason);

                string context = GetDisabledGizmoContext(gizmo, owner);
                if (!string.IsNullOrEmpty(context))
                    announcement += $". {context}";
            }

            return announcement;
        }

        /// <summary>
        /// Runs <paramref name="body"/> with the gizmo's owning Thing temporarily set as
        /// <c>Find.Selector.SingleSelectedThing</c>, then restores the previous selection. Many
        /// vanilla gizmos lazy-evaluate Label/Desc/Visible/Disabled against the live selection —
        /// <c>Designator_Install.Label</c> reads "Reinstall at..." when the MinifiedThing is not
        /// selected. Owner comes from <paramref name="explicitOwner"/>, else the gizmoOwners
        /// dictionary; the swap happens only when the owner is a Thing and is not already
        /// single-selected.
        /// </summary>
        private static T WithGizmoOwnerSelected<T>(Gizmo gizmo, ISelectable explicitOwner, System.Func<T> body)
        {
            ISelectable owner = explicitOwner;
            if (owner == null && gizmoOwners != null)
                gizmoOwners.TryGetValue(gizmo, out owner);

            return WithSelectableSelected(owner, body);
        }

        /// <summary>
        /// The selection swap itself, keyed on the owner rather than a gizmo, so a consumer that
        /// already knows what it is inspecting gets the identical lazy-property discipline. Swaps
        /// only when the owner is a Thing and is not already single-selected.
        /// </summary>
        private static T WithSelectableSelected<T>(ISelectable owner, System.Func<T> body)
        {
            if (Find.Selector == null || !(owner is Thing thingOwner))
                return body();

            if (Find.Selector.SingleSelectedThing == thingOwner)
                return body();

            var previousSelection = Find.Selector.SelectedObjects.ToList();
            try
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(thingOwner, playSound: false, forceDesignatorDeselect: false);
                return body();
            }
            finally
            {
                Find.Selector.ClearSelection();
                foreach (var obj in previousSelection.OfType<ISelectable>())
                    Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
            }
        }

        private static void WithGizmoOwnerSelected(Gizmo gizmo, ISelectable explicitOwner, System.Action body)
        {
            WithGizmoOwnerSelected<bool>(gizmo, explicitOwner, () => { body(); return true; });
        }

        /// <summary>
        /// A gizmo's label. Handler-owned labels resolve through the registry; anything
        /// unregistered falls back to a cleaned-up type name.
        /// </summary>
        internal static string GetGizmoLabel(Gizmo gizmo)
        {
            if (GizmoHandlerRegistry.TryResolveLabel(gizmo, out string handlerLabel))
                return handlerLabel;

#if DEBUG
            InspectionSelfAudit.NoteLabelFallback(gizmo.GetType());
#endif
            return CleanupGizmoTypeName(gizmo.GetType().Name);
        }

        /// <summary>Turns "Gizmo_SomethingCamelCase" into "Something Camel Case".</summary>
        private static string CleanupGizmoTypeName(string typeName)
        {
            if (typeName.StartsWith("Gizmo_"))
                typeName = typeName.Substring(6);
            else if (typeName.StartsWith("GeneGizmo_"))
                typeName = typeName.Substring(10);
            else if (typeName.EndsWith("Gizmo"))
                typeName = typeName.Substring(0, typeName.Length - 5);

            var result = new System.Text.StringBuilder();
            for (int i = 0; i < typeName.Length; i++)
            {
                if (i > 0 && char.IsUpper(typeName[i]) && !char.IsUpper(typeName[i - 1]))
                    result.Append(' ');
                result.Append(typeName[i]);
            }

            string label = result.ToString().Trim();
            return string.IsNullOrEmpty(label)
                ? "RimWorldAccess.Inspection.Gizmo.Type.StatusDisplayFallback".Translate().ToString()
                : label;
        }

        /// <summary>
        /// A gizmo's status value ("5 / 12" for bandwidth). Handler-owned; types without a status
        /// facet return empty.
        /// </summary>
        private static string GetGizmoStatusValue(Gizmo gizmo)
        {
            return GizmoHandlerRegistry.TryResolveStatus(gizmo, out string handlerStatus)
                ? handlerStatus
                : "";
        }

        /// <summary>
        /// An ability's psyfocus cost, minimum required band, and neural heat gain, or null when
        /// it has none.
        /// </summary>
        internal static string GetAbilityCostInfo(Ability ability)
        {
            if (ability?.def == null)
                return null;

            var parts = new List<string>();

            float psyfocusCost = ability.def.PsyfocusCost;

            // Band thresholds are 0%, 25%, 50% for bands 0, 1, 2; the band comes from the level.
            int requiredBand = ability.def.RequiredPsyfocusBand;
            float minRequired = 0f;
            if (requiredBand > 0 && requiredBand < Pawn_PsychicEntropyTracker.PsyfocusBandPercentages.Count)
            {
                minRequired = Pawn_PsychicEntropyTracker.PsyfocusBandPercentages[requiredBand];
            }

            if (psyfocusCost > float.Epsilon || minRequired > float.Epsilon)
            {
                if (psyfocusCost > float.Epsilon && minRequired > float.Epsilon)
                {
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.PsyfocusCostAndMin".Translate(
                        (psyfocusCost * 100f).ToString("F0"),
                        (minRequired * 100f).ToString("F0")));
                }
                else if (psyfocusCost > float.Epsilon)
                {
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.PsyfocusCost".Translate(
                        (psyfocusCost * 100f).ToString("F0")));
                }
                else if (minRequired > float.Epsilon)
                {
                    parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.PsyfocusMinRequired".Translate(
                        (minRequired * 100f).ToString("F0")));
                }
            }

            float entropyGain = ability.def.EntropyGain;
            if (entropyGain > float.Epsilon)
            {
                parts.Add("RimWorldAccess.Inspection.Gizmo.Ability.NeuralHeatGain".Translate(
                    entropyGain.ToString("F0")));
            }

            if (parts.Count == 0)
                return null;

            return string.Join(". ", parts);
        }

        /// <summary>An ability gizmo's range line ("Range: N tiles", "touch", "self"), or null.</summary>
        internal static string GetAbilityRangeInfo(Ability ability)
        {
            if (ability?.def == null)
                return null;

            string rangeText = AbilityRangeLine(ability.def, AbilityTargetingHelper.GetRange(ability),
                AbilityTargetingHelper.IsTouchRange(ability));
            if (rangeText == null)
                return null;

            float effectRadius = ability.def.EffectRadius;
            if (effectRadius > 0f)
                rangeText += "RimWorldAccess.Inspection.Gizmo.Ability.EffectRadiusSuffix".Translate(
                    effectRadius.ToString("F0"));

            return rangeText;
        }

        /// <summary>The range ladder alone, for callers that already speak the effect radius.</summary>
        internal static string AbilityRangeLine(AbilityDef def, float range, bool touch)
        {
            if (!def.targetRequired)
                return "RimWorldAccess.Inspection.Gizmo.Ability.RangeSelf".Translate();
            if (def.targetWorldCell)
                return "RimWorldAccess.Inspection.Gizmo.Ability.RangeWorldMap".Translate();
            if (touch)
                return "RimWorldAccess.Inspection.Gizmo.Ability.RangeTouch".Translate();
            if (range > 0f)
                return "RimWorldAccess.Inspection.Gizmo.Ability.RangeTiles".Translate(range.ToString("F0"));
            return null;
        }

        /// <summary>
        /// An ability's cooldown: remaining time while on cooldown, else the base duration. The
        /// disabled section separately announces the full "on cooldown" game text.
        /// </summary>
        internal static string GetAbilityCooldownInfo(Ability ability)
        {
            if (ability?.def == null)
                return null;

            string cooldownLabel = "StatsReport_Cooldown".Translate();

            if (ability.OnCooldown && ability.CooldownTicksRemaining > 0)
            {
                string remaining = ability.CooldownTicksRemaining.ToStringTicksToPeriod();
                return "RimWorldAccess.Inspection.Gizmo.Ability.CooldownLine".Translate(
                    cooldownLabel, remaining);
            }

            // Group abilities use groupDef.cooldownTicks unless overrideGroupCooldown is set;
            // individual abilities use cooldownTicksRange when it is fixed (min == max).
            int baseCooldownTicks = 0;
            if (ability.def.groupDef != null && !ability.def.overrideGroupCooldown
                && ability.def.groupDef.cooldownTicks > 0)
            {
                baseCooldownTicks = ability.def.groupDef.cooldownTicks;
            }
            else if (ability.def.cooldownTicksRange.min == ability.def.cooldownTicksRange.max
                     && ability.def.cooldownTicksRange.min > 0)
            {
                baseCooldownTicks = ability.def.cooldownTicksRange.min;
            }

            if (baseCooldownTicks > 0)
            {
                string baseDuration = baseCooldownTicks.ToStringTicksToPeriod(
                    allowSeconds: true, shortForm: false, canUseDecimals: true, allowYears: false);
                return "RimWorldAccess.Inspection.Gizmo.Ability.CooldownLine".Translate(
                    cooldownLabel, baseDuration);
            }

            return null;
        }

        /// <summary>
        /// A gizmo's description, the hover-tooltip analog. Handler-owned: the Command handler
        /// serves Desc/defaultDesc, Command_Ability pulls ability.def.description (Desc is
        /// render-populated), status gizmos surface their tooltip. Flattened here, the one place
        /// every description passes through, so embedded newlines always read as sentences.
        /// </summary>
        private static string GetGizmoDescription(Gizmo gizmo)
        {
            return GizmoHandlerRegistry.TryResolveDescription(gizmo, out string handlerDescription)
                ? GizmoTextUtility.FlattenNewlines(handlerDescription)
                : "";
        }

        /// <summary>A gizmo's hotkey text.</summary>
        private static string GetGizmoHotkey(Gizmo gizmo)
        {
            if (gizmo is Command cmd && cmd.hotKey != null)
            {
                KeyCode key = VanillaBindings.BoundKey(cmd.hotKey);
                if (key != KeyCode.None)
                {
                    string prefix = GizmoHotkeyShiftPatch.IsShiftExempt(cmd.hotKey)
                        ? ""
                        : GizmoHotkeyShiftPatch.ShiftPrefix;
                    return prefix + key.ToStringReadable();
                }
            }
            return "";
        }

        /// <summary>
        /// Extra context for a disabled gizmo: group mass stats when a Launch gizmo is disabled
        /// for individual pod overload though the group as a whole may be fine.
        /// </summary>
        private static string GetDisabledGizmoContext(Gizmo gizmo, ISelectable owner)
        {
            if (!gizmo.Disabled)
                return null;

            CompTransporter transporter = null;
            if (owner is Thing thing)
            {
                transporter = thing.TryGetComp<CompTransporter>();
            }

            if (transporter == null || transporter.parent?.Map == null)
                return null;

            // The same condition CompLaunchable.FailReason uses to mint the disabled reason —
            // the game's own signal, never the translated reason string.
            if (!transporter.OverMassCapacity)
                return null;

            var transportersInGroup = transporter.TransportersInGroup(transporter.parent.Map)?.ToList();
            if (transportersInGroup == null || transportersInGroup.Count <= 1)
                return null;

            float groupMassUsage = 0f;
            float groupMassCapacity = 0f;
            int overloadedCount = 0;
            int canLaunchCount = 0;

            foreach (var t in transportersInGroup)
            {
                groupMassUsage += t.MassUsage;
                groupMassCapacity += t.MassCapacity;

                if (t.OverMassCapacity)
                    overloadedCount++;

                var launchable = t.Launchable;
                if (launchable != null)
                {
                    var canLaunch = launchable.CanLaunch();
                    if (canLaunch.Accepted)
                        canLaunchCount++;
                }
            }

            var fragments = new List<string>
            {
                "RimWorldAccess.Inspection.Gizmo.Launch.GroupTotal".Translate(
                    groupMassUsage.ToString("F0"), groupMassCapacity.ToString("F0")),
            };

            if (groupMassUsage <= groupMassCapacity && overloadedCount > 0 && canLaunchCount > 0)
            {
                fragments.Add("RimWorldAccess.Inspection.Gizmo.Launch.PodsCanLaunch".Translate(
                    canLaunchCount, transportersInGroup.Count));
                fragments.Add("RimWorldAccess.Inspection.Gizmo.Launch.SelectAnotherPod".Translate());
            }
            else if (groupMassUsage > groupMassCapacity)
            {
                float overBy = groupMassUsage - groupMassCapacity;
                fragments.Add("RimWorldAccess.Inspection.Gizmo.Launch.OverCapacity".Translate(
                    overBy.ToString("F0")));
            }

            return string.Join(". ", fragments);
        }

        /// <summary>Whether a gizmo is skipped, because it does not integrate with cursor-based navigation.</summary>
        // CaravanMergeUtility.MergeCommand builds its Command_Action with this cached texture;
        // ContentFinder returns the same instance every call, so reference identity detects the
        // merge command in any language.
        private static Texture2D mergeCaravansCommandTex;

        private static bool IsMergeCaravansCommand(Gizmo gizmo)
        {
            if (!(gizmo is Command cmd) || cmd.icon == null)
                return false;

            if (mergeCaravansCommandTex == null)
                mergeCaravansCommandTex = ContentFinder<Texture2D>.Get("UI/Commands/MergeCaravans", reportFailure: false);

            return mergeCaravansCommandTex != null && ReferenceEquals(cmd.icon, mergeCaravansCommandTex);
        }

        private static bool ShouldSkipGizmo(Gizmo gizmo)
        {
            // The transporter/launch-group navigation gizmos use CameraJumper.TryJumpAndSelect,
            // which does not integrate with cursor-based navigation. Their delegates point at
            // CompTransporter's group-navigation methods, a language-free signal.
            if (gizmo is Command_Action commandAction && commandAction.action != null)
            {
                var method = commandAction.action.Method;
                if (method != null && method.DeclaringType == typeof(CompTransporter))
                {
                    // Private on CompTransporter, so nameof() cannot reach them.
                    return method.Name == "SelectPreviousInGroup"
                        || method.Name == "SelectAllInGroup"
                        || method.Name == "SelectNextInGroup";
                }
            }

            return false;
        }
    }
}
