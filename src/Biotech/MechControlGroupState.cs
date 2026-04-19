using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a two-page accessible interface for mechanitor control groups.
    /// Page 1 (Settings): Work mode, recharge range, select all mechs.
    /// Page 2 (Members): Individual mechs with energy levels, reassignment.
    /// Tab/Shift+Tab switches between pages.
    /// </summary>
    public static class MechControlGroupState
    {
        private enum Page { Settings, Members }

        private enum SettingsItemType { WorkMode, RechargeRange, SelectAll }

        private class SettingsItem
        {
            public SettingsItemType Type;
            public string Label;
            public string Description;
        }

        private static bool isActive = false;
        private static MechanitorControlGroup controlGroup;
        private static Page currentPage = Page.Settings;

        // Settings page
        private static List<SettingsItem> settingsItems = new List<SettingsItem>();
        private static int settingsSelectedIndex = 0;
        private static TypeaheadSearchHelper settingsTypeahead = new TypeaheadSearchHelper();

        // Members page
        private static List<Pawn> memberMechs = new List<Pawn>();
        private static int membersSelectedIndex = 0;
        private static TypeaheadSearchHelper membersTypeahead = new TypeaheadSearchHelper();

        // Inline range editor sub-state
        private static bool isEditingRange = false;
        private static int rangeSelectedOption = 0; // 0 = min, 1 = max
        private static FloatRange editingRange;
        private static FloatRange originalRange;

        // Cached reflection
        private static FieldInfo controlGroupField;
        private static FieldInfo mergedGroupsField;

        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the MechanitorControlGroup from a MechanitorControlGroupGizmo via reflection.
        /// </summary>
        public static MechanitorControlGroup GetControlGroupFromGizmo(Gizmo gizmo)
        {
            if (controlGroupField == null)
            {
                controlGroupField = AccessTools.Field(
                    typeof(MechanitorControlGroupGizmo), "controlGroup");
            }
            return controlGroupField?.GetValue(gizmo) as MechanitorControlGroup;
        }

        /// <summary>
        /// Gets merged control groups from a MechanitorControlGroupGizmo via reflection.
        /// </summary>
        private static List<MechanitorControlGroup> GetMergedGroupsFromGizmo(Gizmo gizmo)
        {
            if (mergedGroupsField == null)
            {
                mergedGroupsField = AccessTools.Field(
                    typeof(MechanitorControlGroupGizmo), "mergedControlGroups");
            }
            return mergedGroupsField?.GetValue(gizmo) as List<MechanitorControlGroup>;
        }

        /// <summary>
        /// Opens the control group detail view starting on the Settings page.
        /// </summary>
        public static void Open(MechanitorControlGroup group)
        {
            if (group == null)
            {
                Log.Error("[RimWorld Access] Cannot open mech control group state: group is null");
                return;
            }

            controlGroup = group;
            isActive = true;
            currentPage = Page.Settings;
            settingsSelectedIndex = 0;
            membersSelectedIndex = 0;
            isEditingRange = false;
            settingsTypeahead.ClearSearch();
            membersTypeahead.ClearSearch();

            BuildSettingsItems();
            BuildMembersList();

            string groupLabel = "ControlGroup".Translate() + " " + controlGroup.Index;
            TolkHelper.Speak("RimWorldAccess.Biotech.Mech.GroupSettingsHeader".Translate(groupLabel));
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Closes the state and reopens gizmo navigation.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            isEditingRange = false;
            controlGroup = null;
            settingsItems.Clear();
            memberMechs.Clear();
            settingsTypeahead.ClearSearch();
            membersTypeahead.ClearSearch();

            // Reopen gizmo navigation so the user returns to the gizmo bar
            GizmoNavigationState.Open();
        }

        /// <summary>
        /// Handles all keyboard input for the control group state.
        /// </summary>
        /// <returns>True if input was handled.</returns>
        public static bool HandleInput()
        {
            if (!isActive || controlGroup == null)
                return false;

            // Let float menus take priority (work mode selection, reassignment)
            if (WindowlessFloatMenuState.IsActive)
                return false;

            if (Event.current.type != EventType.KeyDown)
                return false;

            KeyCode key = Event.current.keyCode;
            bool shift = Event.current.shift;
            bool ctrl = Event.current.control;
            bool alt = KeyboardHelper.IsAltHeld;

            // Range editor sub-state takes priority
            if (isEditingRange)
                return HandleRangeEditorInput(key, shift);

            // Tab / Shift+Tab: switch pages
            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                SwitchPage(!shift);
                Event.current.Use();
                return true;
            }

            // Home - jump to first
            if (key == KeyCode.Home && !ctrl && !alt)
            {
                JumpToFirst();
                Event.current.Use();
                return true;
            }

            // End - jump to last
            if (key == KeyCode.End && !ctrl && !alt)
            {
                JumpToLast();
                Event.current.Use();
                return true;
            }

            // Escape
            if (key == KeyCode.Escape)
            {
                var typeahead = GetCurrentTypeahead();
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentItem();
                    Event.current.Use();
                    return true;
                }
                Close();
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.GroupClosed".Translate());
                Event.current.Use();
                return true;
            }

            // Up arrow
            if (key == KeyCode.UpArrow)
            {
                NavigateUp();
                Event.current.Use();
                return true;
            }

            // Down arrow
            if (key == KeyCode.DownArrow)
            {
                NavigateDown();
                Event.current.Use();
                return true;
            }

            // Enter - execute current item
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ExecuteSelected();
                Event.current.Use();
                return true;
            }

            // ] key - reassign mech (members page only)
            if (key == KeyCode.RightBracket && currentPage == Page.Members)
            {
                OpenReassignMenu();
                Event.current.Use();
                return true;
            }

            // Backspace for search
            if (key == KeyCode.Backspace)
            {
                var typeahead = GetCurrentTypeahead();
                if (typeahead.HasActiveSearch)
                {
                    var labels = GetCurrentLabels();
                    if (typeahead.ProcessBackspace(labels, out int newIndex))
                    {
                        if (newIndex >= 0)
                            SetCurrentIndex(newIndex);
                        AnnounceWithSearch();
                    }
                    Event.current.Use();
                    return true;
                }
                return false;
            }

            // Typeahead characters
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if ((isLetter || isNumber) && !alt)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Public entry point for the unified typeahead dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!IsActive) return;
            var typeahead = GetCurrentTypeahead();
            var labels = GetCurrentLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    SetCurrentIndex(newIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        // ========== Page Switching ==========

        private static void SwitchPage(bool forward)
        {
            var typeahead = GetCurrentTypeahead();
            typeahead.ClearSearch();

            currentPage = (currentPage == Page.Settings) ? Page.Members : Page.Settings;

            if (currentPage == Page.Members)
            {
                BuildMembersList();
                if (memberMechs.Count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Biotech.Mech.Members".Translate() + ". " + "NoMechs".Translate());
                    return;
                }
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.Members".Translate());
            }
            else
            {
                BuildSettingsItems();
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.Settings".Translate());
            }

            AnnounceCurrentItem();
        }

        // ========== Navigation ==========

        private static void NavigateUp()
        {
            var typeahead = GetCurrentTypeahead();
            int count = GetCurrentCount();
            if (count == 0) return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int prevIndex = typeahead.GetPreviousMatch(GetCurrentIndex());
                if (prevIndex >= 0)
                {
                    SetCurrentIndex(prevIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                int newIndex = MenuHelper.SelectPrevious(GetCurrentIndex(), count);
                SetCurrentIndex(newIndex);
                AnnounceCurrentItem();
            }
        }

        private static void NavigateDown()
        {
            var typeahead = GetCurrentTypeahead();
            int count = GetCurrentCount();
            if (count == 0) return;

            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int nextIndex = typeahead.GetNextMatch(GetCurrentIndex());
                if (nextIndex >= 0)
                {
                    SetCurrentIndex(nextIndex);
                    AnnounceWithSearch();
                }
            }
            else
            {
                int newIndex = MenuHelper.SelectNext(GetCurrentIndex(), count);
                SetCurrentIndex(newIndex);
                AnnounceCurrentItem();
            }
        }

        private static void JumpToFirst()
        {
            int count = GetCurrentCount();
            if (count == 0) return;

            GetCurrentTypeahead().ClearSearch();
            SetCurrentIndex(MenuHelper.JumpToFirst());
            AnnounceCurrentItem();
        }

        private static void JumpToLast()
        {
            int count = GetCurrentCount();
            if (count == 0) return;

            GetCurrentTypeahead().ClearSearch();
            SetCurrentIndex(MenuHelper.JumpToLast(count));
            AnnounceCurrentItem();
        }

        // ========== Execution ==========

        private static void ExecuteSelected()
        {
            if (currentPage == Page.Settings)
                ExecuteSettingsItem();
            else
                ExecuteMembersItem();
        }

        private static void ExecuteSettingsItem()
        {
            if (settingsSelectedIndex < 0 || settingsSelectedIndex >= settingsItems.Count)
                return;

            var item = settingsItems[settingsSelectedIndex];

            switch (item.Type)
            {
                case SettingsItemType.WorkMode:
                    OpenWorkModeMenu();
                    break;

                case SettingsItemType.RechargeRange:
                    OpenRangeEditor();
                    break;

                case SettingsItemType.SelectAll:
                    SelectAllMechs();
                    break;
            }
        }

        private static void ExecuteMembersItem()
        {
            if (membersSelectedIndex < 0 || membersSelectedIndex >= memberMechs.Count)
                return;

            Pawn mech = memberMechs[membersSelectedIndex];
            isActive = false;
            isEditingRange = false;
            controlGroup = null;
            settingsItems.Clear();
            memberMechs.Clear();
            settingsTypeahead.ClearSearch();
            membersTypeahead.ClearSearch();

            CameraJumper.TryJumpAndSelect(mech);
            MapNavigationState.SpeakJumpedTo(mech.LabelCap);
        }

        // ========== Work Mode Menu ==========

        private static void OpenWorkModeMenu()
        {
            var options = MechanitorControlGroupGizmo.GetWorkModeOptions(controlGroup).ToList();
            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.NoWorkModes".Translate());
                return;
            }

            // Wrap each option's action to rebuild our settings after selection
            var wrappedOptions = new List<FloatMenuOption>();
            foreach (var opt in options)
            {
                var originalAction = opt.action;
                string label = opt.Label;
                wrappedOptions.Add(new FloatMenuOption(label, delegate
                {
                    originalAction?.Invoke();
                    // Rebuild settings to reflect the change
                    if (isActive)
                    {
                        BuildSettingsItems();
                        TolkHelper.Speak("RimWorldAccess.Biotech.Mech.WorkModeSet".Translate(controlGroup.WorkMode.LabelCap));
                    }
                }, opt.iconThing, opt.iconColor)
                {
                    tooltip = opt.tooltip
                });
            }

            WindowlessFloatMenuState.Open(wrappedOptions, false);
        }

        // ========== Recharge Range Editor ==========

        private static void OpenRangeEditor()
        {
            isEditingRange = true;
            rangeSelectedOption = 0;
            originalRange = controlGroup.mechRechargeThresholds;
            editingRange = controlGroup.mechRechargeThresholds;

            AnnounceRangeSelection();
        }

        private static void CloseRangeEditor(bool save)
        {
            if (save)
            {
                controlGroup.mechRechargeThresholds = editingRange;
                BuildSettingsItems();
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.RangeSaved".Translate(
                    FormatPercent(editingRange.min), FormatPercent(editingRange.max)));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.RangeCancelled".Translate());
            }

            isEditingRange = false;
        }

        private static bool HandleRangeEditorInput(KeyCode key, bool shift)
        {
            // Up/Down: toggle min/max
            if (key == KeyCode.UpArrow || key == KeyCode.DownArrow)
            {
                rangeSelectedOption = (rangeSelectedOption == 0) ? 1 : 0;
                AnnounceRangeSelection();
                Event.current.Use();
                return true;
            }

            // Right: increase value
            if (key == KeyCode.RightArrow)
            {
                float step = shift ? 0.01f : 0.05f;
                AdjustRangeValue(1, step);
                Event.current.Use();
                return true;
            }

            // Left: decrease value
            if (key == KeyCode.LeftArrow)
            {
                float step = shift ? 0.01f : 0.05f;
                AdjustRangeValue(-1, step);
                Event.current.Use();
                return true;
            }

            // Enter: confirm
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                CloseRangeEditor(save: true);
                Event.current.Use();
                return true;
            }

            // Escape: cancel
            if (key == KeyCode.Escape)
            {
                CloseRangeEditor(save: false);
                Event.current.Use();
                return true;
            }

            // Consume other keys to prevent bleed-through
            Event.current.Use();
            return true;
        }

        private static void AdjustRangeValue(int direction, float step)
        {
            float adjustment = step * direction;

            if (rangeSelectedOption == 0) // Min
            {
                float newMin = Mathf.Clamp(editingRange.min + adjustment, 0f, editingRange.max);
                editingRange.min = Mathf.Round(newMin * 100f) / 100f;
            }
            else // Max
            {
                float newMax = Mathf.Clamp(editingRange.max + adjustment, editingRange.min, 1f);
                editingRange.max = Mathf.Round(newMax * 100f) / 100f;
            }

            AnnounceRangeSelection();
        }

        private static void AnnounceRangeSelection()
        {
            string optionName = rangeSelectedOption == 0
                ? "RimWorldAccess.Biotech.Mech.RangeMinimum".Translate().ToString()
                : "RimWorldAccess.Biotech.Mech.RangeMaximum".Translate().ToString();
            float value = rangeSelectedOption == 0 ? editingRange.min : editingRange.max;
            TolkHelper.Speak("RimWorldAccess.Biotech.Mech.RangeSelection".Translate(
                optionName, FormatPercent(value), FormatPercent(editingRange.min), FormatPercent(editingRange.max)));
        }

        // ========== Select All Mechs ==========

        private static void SelectAllMechs()
        {
            var mechs = controlGroup.MechsForReading;
            if (mechs.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.NoMechsInGroup".Translate());
                return;
            }

            // Close state before changing selection
            isActive = false;
            isEditingRange = false;
            controlGroup = null;
            settingsItems.Clear();
            memberMechs.Clear();
            settingsTypeahead.ClearSearch();
            membersTypeahead.ClearSearch();

            Find.Selector.ClearSelection();
            foreach (var mech in mechs)
            {
                Find.Selector.Select(mech, playSound: false, forceDesignatorDeselect: false);
            }

            TolkHelper.Speak(mechs.Count == 1
                ? "RimWorldAccess.Biotech.Mech.SelectedMechOne".Translate(mechs.Count)
                : "RimWorldAccess.Biotech.Mech.SelectedMechMany".Translate(mechs.Count));
        }

        // ========== Reassignment ==========

        private static void OpenReassignMenu()
        {
            if (membersSelectedIndex < 0 || membersSelectedIndex >= memberMechs.Count)
                return;

            Pawn selectedMech = memberMechs[membersSelectedIndex];
            var tracker = controlGroup.Tracker;
            var allGroups = tracker.controlGroups;

            if (allGroups.Count <= 1)
            {
                TolkHelper.Speak("RimWorldAccess.Biotech.Mech.NoOtherGroups".Translate());
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var group in allGroups)
            {
                if (group == controlGroup)
                    continue;

                int groupIndex = group.Index;
                string label = "AssignMechToControlGroup".Translate(groupIndex)
                    + " (" + group.WorkMode.LabelCap + ")";

                var targetGroup = group;
                options.Add(new FloatMenuOption(label, delegate
                {
                    targetGroup.Assign(selectedMech);

                    if (isActive)
                    {
                        BuildMembersList();
                        // Clamp index after the list shrank
                        if (membersSelectedIndex >= memberMechs.Count)
                            membersSelectedIndex = Mathf.Max(0, memberMechs.Count - 1);

                        string announcement = "RimWorldAccess.Biotech.Mech.AssignedToGroup".Translate(
                            selectedMech.LabelCap, "ControlGroup".Translate(), groupIndex);
                        if (memberMechs.Count == 0)
                        {
                            announcement += ". " + "NoMechs".Translate();
                        }
                        TolkHelper.Speak(announcement);

                        if (memberMechs.Count > 0)
                            AnnounceCurrentItem();
                    }
                }));
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        // ========== Data Building ==========

        private static void BuildSettingsItems()
        {
            settingsItems.Clear();

            // Work mode
            settingsItems.Add(new SettingsItem
            {
                Type = SettingsItemType.WorkMode,
                Label = "CurrentMechWorkMode".Translate() + ": " + controlGroup.WorkMode.LabelCap,
                Description = controlGroup.WorkMode.description
            });

            // Recharge range
            var range = controlGroup.mechRechargeThresholds;
            settingsItems.Add(new SettingsItem
            {
                Type = SettingsItemType.RechargeRange,
                Label = "MechRechargeSettingsTitle".Translate()
                    + ": " + FormatPercent(range.min) + " to " + FormatPercent(range.max),
                Description = "MechRechargeSettingsExplanation".Translate()
            });

            // Select all mechs
            int mechCount = controlGroup.MechsForReading.Count;
            string selectLabel = "CommandSelectAllMechs".Translate();
            if (mechCount > 0)
                selectLabel += $" ({mechCount})";
            settingsItems.Add(new SettingsItem
            {
                Type = SettingsItemType.SelectAll,
                Label = selectLabel,
                Description = "CommandSelectAllMechsDesc".Translate()
            });
        }

        private static void BuildMembersList()
        {
            memberMechs.Clear();
            if (controlGroup != null)
            {
                memberMechs.AddRange(controlGroup.MechsForReading);
            }
        }

        // ========== Announcement ==========

        private static void AnnounceCurrentItem()
        {
            int count = GetCurrentCount();
            if (count == 0) return;

            int index = GetCurrentIndex();
            if (index < 0 || index >= count) return;

            string label = GetItemLabel(index);
            string description = GetItemDescription(index);
            string position = MenuHelper.FormatPosition(index, count);

            string announcement = label;
            if (!string.IsNullOrEmpty(description))
                announcement += ". " + description;
            if (!string.IsNullOrEmpty(position))
                announcement += ". " + position;

            TolkHelper.Speak(announcement);
        }

        private static void AnnounceWithSearch()
        {
            int count = GetCurrentCount();
            if (count == 0) return;

            int index = GetCurrentIndex();
            if (index < 0 || index >= count) return;

            var typeahead = GetCurrentTypeahead();
            string label = GetItemLabel(index);

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak(typeahead.BuildItemAnnouncement(label));
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        private static string GetItemLabel(int index)
        {
            if (currentPage == Page.Settings)
            {
                if (index >= 0 && index < settingsItems.Count)
                    return settingsItems[index].Label;
                return "";
            }
            else
            {
                if (index >= 0 && index < memberMechs.Count)
                    return GetMechLabel(memberMechs[index]);
                return "";
            }
        }

        private static string GetItemDescription(int index)
        {
            if (currentPage == Page.Settings)
            {
                if (index >= 0 && index < settingsItems.Count)
                    return settingsItems[index].Description ?? "";
                return "";
            }
            // Members page doesn't have item-level descriptions
            return "";
        }

        private static string GetMechLabel(Pawn mech)
        {
            string label = mech.LabelCap;

            if (mech.needs?.energy != null)
            {
                label += ", " + FormatPercent(mech.needs.energy.CurLevelPercentage) + " " + "EnergyLower".Translate();
            }

            // Check if uncontrolled (not in controlled pawns list)
            if (controlGroup != null
                && !controlGroup.Tracker.ControlledPawns.Contains(mech))
            {
                label += ", " + "RimWorldAccess.Biotech.Mech.Uncontrolled".Translate();
            }

            return label;
        }

        // ========== Gizmo Label Helpers (used by GizmoNavigationState) ==========

        /// <summary>
        /// Gets the accessible label for a MechanitorControlGroupGizmo.
        /// </summary>
        public static string GetGizmoLabel(Gizmo gizmo)
        {
            var group = GetControlGroupFromGizmo(gizmo);
            if (group == null)
                return "RimWorldAccess.Biotech.Mech.GizmoLabelFallback".Translate();

            var mergedGroups = GetMergedGroupsFromGizmo(gizmo);
            int mechCount = group.MechsForReading.Count;

            if (mergedGroups != null && mergedGroups.Count > 0)
            {
                // Merged empty groups: "Control groups 1, 2, no mechs"
                string indices = group.Index.ToString();
                var sorted = mergedGroups.OrderBy(g => g.Index).ToList();
                foreach (var mg in sorted)
                {
                    indices += ", " + mg.Index;
                    mechCount += mg.MechsForReading.Count;
                }

                string label = "Groups".Translate() + " " + indices;
                if (mechCount == 0)
                    label += ", " + "NoMechs".Translate();
                else
                    label += ", " + (mechCount == 1
                        ? "RimWorldAccess.Biotech.Mech.MechCountOne".Translate(mechCount)
                        : "RimWorldAccess.Biotech.Mech.MechCountMany".Translate(mechCount));
                return label;
            }
            else
            {
                // Single group: "Control group 1, Work, 3 mechs"
                string label = "ControlGroup".Translate() + " " + group.Index;
                label += ", " + group.WorkMode.LabelCap;

                if (mechCount == 0)
                    label += ", " + "NoMechs".Translate();
                else
                    label += ", " + (mechCount == 1
                        ? "RimWorldAccess.Biotech.Mech.MechCountOne".Translate(mechCount)
                        : "RimWorldAccess.Biotech.Mech.MechCountMany".Translate(mechCount));

                return label;
            }
        }

        /// <summary>
        /// Gets the status value for a MechanitorControlGroupGizmo.
        /// Groups mechs by type with count and average energy.
        /// </summary>
        public static string GetGizmoStatus(Gizmo gizmo)
        {
            var group = GetControlGroupFromGizmo(gizmo);
            if (group == null)
                return "";

            var mechs = group.MechsForReading;
            if (mechs.Count == 0)
                return "";

            // Group mechs by kindDef
            var grouped = new Dictionary<PawnKindDef, List<Pawn>>();
            foreach (var mech in mechs)
            {
                if (!grouped.ContainsKey(mech.kindDef))
                    grouped[mech.kindDef] = new List<Pawn>();
                grouped[mech.kindDef].Add(mech);
            }

            var entries = new List<string>();
            foreach (var kvp in grouped)
            {
                string typeName = kvp.Key.LabelCap;
                int count = kvp.Value.Count;

                // Calculate average energy
                float totalEnergy = 0f;
                int energyCount = 0;
                foreach (var mech in kvp.Value)
                {
                    if (mech.needs?.energy != null)
                    {
                        totalEnergy += mech.needs.energy.CurLevelPercentage;
                        energyCount++;
                    }
                }

                string entry = typeName + ": " + count;
                if (energyCount > 0)
                {
                    float avgEnergy = totalEnergy / energyCount;
                    entry += ". " + "RimWorldAccess.Biotech.Mech.AverageEnergy".Translate(
                        "EnergyLower".Translate(), FormatPercent(avgEnergy));
                }
                entries.Add(entry);
            }

            return string.Join(". ", entries);
        }

        // ========== Helper Methods ==========

        private static TypeaheadSearchHelper GetCurrentTypeahead()
        {
            return currentPage == Page.Settings ? settingsTypeahead : membersTypeahead;
        }

        private static int GetCurrentCount()
        {
            return currentPage == Page.Settings ? settingsItems.Count : memberMechs.Count;
        }

        private static int GetCurrentIndex()
        {
            return currentPage == Page.Settings ? settingsSelectedIndex : membersSelectedIndex;
        }

        private static void SetCurrentIndex(int index)
        {
            if (currentPage == Page.Settings)
                settingsSelectedIndex = index;
            else
                membersSelectedIndex = index;
        }

        private static List<string> GetCurrentLabels()
        {
            var labels = new List<string>();
            int count = GetCurrentCount();

            for (int i = 0; i < count; i++)
            {
                labels.Add(GetItemLabel(i));
            }

            return labels;
        }

        private static string FormatPercent(float value)
        {
            return Mathf.RoundToInt(value * 100f) + "%";
        }
    }
}
