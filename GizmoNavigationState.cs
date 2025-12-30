using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Maintains the state of gizmo (command button) navigation for accessibility features.
    /// Tracks the currently selected gizmo when browsing available commands with the G key.
    /// </summary>
    public static class GizmoNavigationState
    {
        private static bool isActive = false;
        private static int selectedGizmoIndex = 0;
        private static List<Gizmo> availableGizmos = new List<Gizmo>();
        private static Dictionary<Gizmo, ISelectable> gizmoOwners = new Dictionary<Gizmo, ISelectable>();
        private static ISelectable lastAnnouncedOwner = null;
        private static bool pawnJustSelected = false;

        /// <summary>
        /// Gets whether gizmo navigation is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets or sets whether a pawn was just selected via , or . keys.
        /// This flag is cleared when the user navigates the map with arrow keys.
        /// </summary>
        public static bool PawnJustSelected
        {
            get => pawnJustSelected;
            set => pawnJustSelected = value;
        }

        /// <summary>
        /// Gets the currently selected gizmo index.
        /// </summary>
        public static int SelectedGizmoIndex => selectedGizmoIndex;

        /// <summary>
        /// Gets the list of available gizmos.
        /// </summary>
        public static List<Gizmo> AvailableGizmos => availableGizmos;

        /// <summary>
        /// Opens the gizmo navigation menu by collecting gizmos from selected objects.
        /// </summary>
        public static void Open()
        {
            if (Find.Selector == null || Find.CurrentMap == null)
                return;

            // Collect gizmos from all selected objects
            availableGizmos.Clear();
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is ISelectable selectable)
                {
                    availableGizmos.AddRange(selectable.GetGizmos());
                }
            }

            // Sort by Order property (lower values appear first)
            availableGizmos = availableGizmos
                .Where(g => g != null && g.Visible)
                .OrderBy(g => g.Order)
                .ToList();

            if (availableGizmos.Count == 0)
            {
                TolkHelper.Speak("No commands available");
                return;
            }

            // Start at the first gizmo
            selectedGizmoIndex = 0;
            isActive = true;

            // Announce the first gizmo
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Opens the gizmo navigation menu by collecting gizmos from objects at the cursor position.
        /// </summary>
        public static void OpenAtCursor(IntVec3 cursorPosition, Map map)
        {
            if (map == null)
                return;

            // Validate cursor position
            if (!cursorPosition.IsValid || !cursorPosition.InBounds(map))
            {
                TolkHelper.Speak("Invalid cursor position");
                return;
            }

            // Get all things at the cursor position
            availableGizmos.Clear();
            List<Thing> thingsAtPosition = cursorPosition.GetThingList(map);

            if (thingsAtPosition == null || thingsAtPosition.Count == 0)
            {
                TolkHelper.Speak("No objects at cursor position");
                return;
            }

            // Collect gizmos from all things at this position
            // Important: Temporarily select each thing before getting its gizmos,
            // because some gizmos (like Designator_Install) check if the thing is selected
            // to determine their Visible property
            gizmoOwners.Clear();

            // Store the current selection to restore it later
            var previousSelection = Find.Selector.SelectedObjects.ToList();

            foreach (Thing thing in thingsAtPosition)
            {
                if (thing is ISelectable selectable)
                {
                    // Temporarily select this thing so its gizmos' Visible property works correctly
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);

                    var gizmos = selectable.GetGizmos().ToList();
                    foreach (Gizmo gizmo in gizmos)
                    {
                        availableGizmos.Add(gizmo);
                        gizmoOwners[gizmo] = selectable;
                    }
                }
            }

            // Restore previous selection (or clear if nothing was selected)
            Find.Selector.ClearSelection();
            foreach (var obj in previousSelection)
            {
                if (obj is ISelectable selectableObj)
                {
                    Find.Selector.Select(selectableObj, playSound: false, forceDesignatorDeselect: false);
                }
            }

            // Sort by Order property (lower values appear first)
            availableGizmos = availableGizmos
                .Where(g => g != null && g.Visible)
                .OrderBy(g => g.Order)
                .ToList();

            if (availableGizmos.Count == 0)
            {
                TolkHelper.Speak("No commands available for objects at cursor");
                return;
            }

            // Start at the first gizmo
            selectedGizmoIndex = 0;
            isActive = true;
            lastAnnouncedOwner = null;

            // Announce the first gizmo (will include object name as prefix)
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Closes the gizmo navigation menu.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            selectedGizmoIndex = 0;
            availableGizmos.Clear();
            gizmoOwners.Clear();
            lastAnnouncedOwner = null;
        }

        /// <summary>
        /// Selects the next gizmo in the list.
        /// </summary>
        public static void SelectNext()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            selectedGizmoIndex = (selectedGizmoIndex + 1) % availableGizmos.Count;
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Selects the previous gizmo in the list.
        /// </summary>
        public static void SelectPrevious()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            selectedGizmoIndex = (selectedGizmoIndex - 1 + availableGizmos.Count) % availableGizmos.Count;
            AnnounceCurrentGizmo();
        }

        /// <summary>
        /// Executes the currently selected gizmo.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo selectedGizmo = availableGizmos[selectedGizmoIndex];
            string gizmoLabel = GetGizmoLabel(selectedGizmo);

            // Check if disabled
            if (selectedGizmo.Disabled)
            {
                string reason = selectedGizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "Command not available";
                TolkHelper.Speak($"Disabled: {reason}");
                return;
            }

            // Create a fake event to trigger the gizmo
            Event fakeEvent = new Event();
            fakeEvent.type = EventType.Used;

            // Special handling for different gizmo types

            // 1. Designator (like Reinstall, Copy) - enters placement mode
            if (selectedGizmo is Designator designator)
            {
                // For Designators opened via cursor objects (not selected pawns),
                // we need to ensure the correct object is selected
                // so the Designator has proper context (e.g., Designator_Install needs to know what to reinstall)
                if (!PawnJustSelected && gizmoOwners.ContainsKey(selectedGizmo) && Find.Selector != null)
                {
                    // Select ONLY the specific thing that owns this gizmo
                    ISelectable owner = gizmoOwners[selectedGizmo];
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(owner, playSound: false, forceDesignatorDeselect: false);
                }

                try
                {
                    // Call ProcessInput to let the Designator do its preparation work
                    // (Designator_Install does setup like canceling existing blueprints)
                    selectedGizmo.ProcessInput(fakeEvent);

                    // Validate that the designator was actually selected
                    if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
                    {
                        // Announce placement mode
                        TolkHelper.Speak($"{gizmoLabel} - Use arrow keys to position, R to rotate, Space to place, Escape to cancel");
                    }
                    else
                    {
                        TolkHelper.Speak($"Error: {gizmoLabel} could not be activated. Check if the item can be placed.", SpeechPriority.High);
                    }
                }
                catch (System.Exception ex)
                {
                    ModLogger.Error($"Exception in Designator execution: {ex.Message}");
                    TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                }

                // Close the gizmo menu AFTER announcing
                Close();
                return;
            }

            // 2. Command_Toggle - toggle and announce state
            if (selectedGizmo is Command_Toggle toggle)
            {
                try
                {
                    // Execute the toggle
                    selectedGizmo.ProcessInput(fakeEvent);

                    // Announce the new state
                    bool toggleActive = toggle.isActive?.Invoke() ?? false;
                    string state = toggleActive ? "ON" : "OFF";
                    string label = GetGizmoLabel(toggle);
                    TolkHelper.Speak($"{label}: {state}");
                }
                catch (System.Exception ex)
                {
                    ModLogger.Error($"Exception in Command_Toggle execution: {ex.Message}");
                    TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                }
            }
            else
            {
                // 3. Command_VerbTarget (weapon attacks) - announce targeting mode
                if (selectedGizmo is Command_VerbTarget verbTarget)
                {
                    try
                    {
                        // Execute the command
                        selectedGizmo.ProcessInput(fakeEvent);

                        string weaponName = verbTarget.ownerThing?.LabelCap ?? "weapon";
                        string verbLabel = verbTarget.verb?.ReportLabel ?? "attack";
                        TolkHelper.Speak($"{weaponName} {verbLabel} - Use map navigation to select target, then press Enter");
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception in Command_VerbTarget execution: {ex.Message}");
                        TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                    }
                }
                // 4. Command_Target - announce targeting mode
                else if (selectedGizmo is Command_Target)
                {
                    try
                    {
                        // Execute the command
                        selectedGizmo.ProcessInput(fakeEvent);

                        TolkHelper.Speak($"{gizmoLabel} - Use map navigation to select target, then press Enter");
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception in Command_Target execution: {ex.Message}");
                        TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                    }
                }
                // 5. Generic Command
                else
                {
                    try
                    {
                        // Execute the command
                        selectedGizmo.ProcessInput(fakeEvent);
                    }
                    catch (System.Exception ex)
                    {
                        ModLogger.Error($"Exception in generic Command execution: {ex.Message}");
                        TolkHelper.Speak($"Error executing {gizmoLabel}: {ex.Message}", SpeechPriority.High);
                    }
                }
            }

            // Always close after executing (per user requirement)
            Close();
        }

        /// <summary>
        /// Announces the currently selected gizmo to the user via clipboard.
        /// Format: "Name: Description (Hotkey)" or "Name: Description" if no hotkey.
        /// </summary>
        private static void AnnounceCurrentGizmo()
        {
            if (!isActive || availableGizmos.Count == 0)
                return;

            if (selectedGizmoIndex < 0 || selectedGizmoIndex >= availableGizmos.Count)
                return;

            Gizmo gizmo = availableGizmos[selectedGizmoIndex];

            string label = GetGizmoLabel(gizmo);
            string description = GetGizmoDescription(gizmo);
            string hotkey = GetGizmoHotkey(gizmo);
            string statusValue = GetGizmoStatusValue(gizmo);

            // Add owner prefix when navigating gizmos from multiple objects
            string ownerPrefix = "";
            if (gizmoOwners.Count > 0 && gizmoOwners.TryGetValue(gizmo, out ISelectable owner))
            {
                if (owner != lastAnnouncedOwner)
                {
                    lastAnnouncedOwner = owner;
                    if (owner is Thing thing)
                        ownerPrefix = thing.LabelCap.StripTags() + ": ";
                }
            }

            string announcement = ownerPrefix + label;

            // Add status value for non-Command gizmos (progress bars, etc.)
            if (!string.IsNullOrEmpty(statusValue))
                announcement += $" - {statusValue}";

            if (!string.IsNullOrEmpty(description))
                announcement += $": {description}";

            if (!string.IsNullOrEmpty(hotkey))
                announcement += $" ({hotkey})";

            // Add disabled status if applicable
            if (gizmo.Disabled)
            {
                string reason = gizmo.disabledReason;
                if (string.IsNullOrEmpty(reason))
                    reason = "Not available";
                announcement += $" [DISABLED: {reason}]";
            }

            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Gets the label text for a gizmo.
        /// </summary>
        private static string GetGizmoLabel(Gizmo gizmo)
        {
            // Special handling for Command_VerbTarget (weapon attacks)
            if (gizmo is Command_VerbTarget verbTarget)
            {
                string weaponName = verbTarget.ownerThing?.LabelCap ?? "Unknown weapon";
                string verbLabel = verbTarget.verb?.ReportLabel ?? "attack";
                return $"{weaponName} - {verbLabel}";
            }

            if (gizmo is Command cmd)
            {
                string label = cmd.LabelCap;
                if (string.IsNullOrEmpty(label))
                    label = cmd.defaultLabel;
                if (string.IsNullOrEmpty(label))
                    label = "Unknown Command";
                return label;
            }

            // Handle non-Command gizmos (status displays, bars, etc.)
            return GetNonCommandGizmoLabel(gizmo);
        }

        /// <summary>
        /// Gets a descriptive label for non-Command gizmos (status displays, bars, etc.)
        /// These gizmos don't have a standard Label property, so we identify them by type.
        /// </summary>
        private static string GetNonCommandGizmoLabel(Gizmo gizmo)
        {
            string typeName = gizmo.GetType().Name;

            // Gizmo_Slider and subclasses have a Title property
            if (gizmo is Verse.Gizmo_Slider slider)
            {
                try
                {
                    // Title is a protected abstract property, access via reflection
                    var titleProp = gizmo.GetType().GetProperty("Title",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    if (titleProp != null)
                    {
                        string title = titleProp.GetValue(gizmo) as string;
                        if (!string.IsNullOrEmpty(title))
                            return title;
                    }
                }
                catch { }
            }

            // Handle specific known gizmo types by name
            switch (typeName)
            {
                case "Gizmo_EnergyShieldStatus":
                    return GetEnergyShieldLabel(gizmo);

                case "PsychicEntropyGizmo":
                    return "Psychic Entropy and Psyfocus";

                case "MechanitorBandwidthGizmo":
                    return "Mechanitor Bandwidth";

                case "Gizmo_GrowthTier":
                    return GetGrowthTierLabel(gizmo);

                case "Gizmo_RoomStats":
                    return GetRoomStatsLabel(gizmo);

                case "MechCarrierGizmo":
                    return GetMechCarrierLabel(gizmo);

                case "MechPowerCellGizmo":
                    return "Mech Power Cell";

                case "MechanitorControlGroupGizmo":
                    return "Mechanitor Control Groups";

                case "Gizmo_MechResurrectionCharges":
                    return "Resurrector Charges";

                case "Gizmo_ProjectileInterceptorHitPoints":
                    return "Shield Hit Points";

                case "Gizmo_PruningConfig":
                    return "Pruning Configuration";

                case "GuardianShipGizmo":
                    return "Guardian Ship";

                case "Gizmo_CaravanInfo":
                    return "Caravan Info";

                case "GeneGizmo_DeathrestCapacity":
                    return "Deathrest Capacity";

                case "ActivityGizmo":
                    return GetActivityGizmoLabel(gizmo);

                default:
                    // For unknown gizmos, return a cleaned-up type name
                    return CleanupGizmoTypeName(typeName);
            }
        }

        /// <summary>
        /// Gets the label for an energy shield gizmo by accessing its shield component.
        /// </summary>
        private static string GetEnergyShieldLabel(Gizmo gizmo)
        {
            try
            {
                var shieldField = gizmo.GetType().GetField("shield",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);
                if (shieldField != null)
                {
                    var shield = shieldField.GetValue(gizmo);
                    if (shield != null)
                    {
                        // Check if it's apparel shield or inbuilt
                        var isApparelProp = shield.GetType().GetProperty("IsApparel");
                        bool isApparel = isApparelProp != null && (bool)isApparelProp.GetValue(shield);

                        var parentProp = shield.GetType().GetProperty("parent");
                        var parent = parentProp?.GetValue(shield) as Thing;

                        if (isApparel && parent != null)
                            return $"Shield: {parent.LabelCap}";
                        else
                            return "Shield (Inbuilt)";
                    }
                }
            }
            catch { }
            return "Energy Shield";
        }

        /// <summary>
        /// Gets the label for a growth tier gizmo by accessing the child pawn.
        /// </summary>
        private static string GetGrowthTierLabel(Gizmo gizmo)
        {
            try
            {
                var childField = gizmo.GetType().GetField("child",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                if (childField != null)
                {
                    var child = childField.GetValue(gizmo) as Pawn;
                    if (child != null)
                    {
                        int tier = child.ageTracker?.GrowthTier ?? 0;
                        return $"Growth Tier {tier}";
                    }
                }
            }
            catch { }
            return "Growth Tier";
        }

        /// <summary>
        /// Gets the label for a room stats gizmo by accessing the building and room.
        /// </summary>
        private static string GetRoomStatsLabel(Gizmo gizmo)
        {
            try
            {
                var buildingField = gizmo.GetType().GetField("building",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                if (buildingField != null)
                {
                    var building = buildingField.GetValue(gizmo) as Building;
                    if (building?.GetRoom() != null)
                    {
                        var room = building.GetRoom();
                        string roleName = room.Role?.label ?? "room";
                        return $"Room Stats: {roleName.CapitalizeFirst()}";
                    }
                }
            }
            catch { }
            return "Room Stats";
        }

        /// <summary>
        /// Gets the label for a mech carrier gizmo by accessing the carrier component.
        /// </summary>
        private static string GetMechCarrierLabel(Gizmo gizmo)
        {
            try
            {
                var carrierField = gizmo.GetType().GetField("carrier",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                if (carrierField != null)
                {
                    var carrier = carrierField.GetValue(gizmo);
                    if (carrier != null)
                    {
                        var propsField = carrier.GetType().GetProperty("Props");
                        var props = propsField?.GetValue(carrier);
                        if (props != null)
                        {
                            var ingredientField = props.GetType().GetField("fixedIngredient");
                            var ingredient = ingredientField?.GetValue(props) as Def;
                            if (ingredient != null)
                                return $"Mech Carrier: {ingredient.label?.CapitalizeFirst()}";
                        }
                    }
                }
            }
            catch { }
            return "Mech Carrier";
        }

        /// <summary>
        /// Gets the label for an activity gizmo by accessing its Title property.
        /// </summary>
        private static string GetActivityGizmoLabel(Gizmo gizmo)
        {
            try
            {
                var titleProp = gizmo.GetType().GetProperty("Title",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (titleProp != null)
                {
                    string title = titleProp.GetValue(gizmo) as string;
                    if (!string.IsNullOrEmpty(title))
                        return title;
                }
            }
            catch { }
            return "Activity";
        }

        /// <summary>
        /// Cleans up a gizmo type name to be more readable.
        /// Converts "Gizmo_SomethingCamelCase" to "Something Camel Case".
        /// </summary>
        private static string CleanupGizmoTypeName(string typeName)
        {
            // Remove common prefixes
            if (typeName.StartsWith("Gizmo_"))
                typeName = typeName.Substring(6);
            else if (typeName.StartsWith("GeneGizmo_"))
                typeName = typeName.Substring(10);
            else if (typeName.EndsWith("Gizmo"))
                typeName = typeName.Substring(0, typeName.Length - 5);

            // Add spaces before capital letters
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < typeName.Length; i++)
            {
                if (i > 0 && char.IsUpper(typeName[i]) && !char.IsUpper(typeName[i - 1]))
                    result.Append(' ');
                result.Append(typeName[i]);
            }

            string label = result.ToString().Trim();
            return string.IsNullOrEmpty(label) ? "Status Display" : label;
        }

        /// <summary>
        /// Gets the current status value for non-Command gizmos (progress bars, meters, etc.)
        /// Returns values like "5 / 12" for bandwidth or "75%" for shield energy.
        /// </summary>
        private static string GetGizmoStatusValue(Gizmo gizmo)
        {
            // Only get status for non-Command gizmos
            if (gizmo is Command)
                return "";

            string typeName = gizmo.GetType().Name;

            try
            {
                switch (typeName)
                {
                    case "MechanitorBandwidthGizmo":
                        return GetMechanitorBandwidthStatus(gizmo);

                    case "Gizmo_EnergyShieldStatus":
                        return GetEnergyShieldStatus(gizmo);

                    case "PsychicEntropyGizmo":
                        return GetPsychicEntropyStatus(gizmo);

                    case "Gizmo_GrowthTier":
                        return GetGrowthTierStatus(gizmo);

                    case "MechCarrierGizmo":
                        return GetMechCarrierStatus(gizmo);

                    case "MechPowerCellGizmo":
                        return GetMechPowerCellStatus(gizmo);

                    case "Gizmo_MechResurrectionCharges":
                        return GetMechResurrectionStatus(gizmo);

                    case "Gizmo_ProjectileInterceptorHitPoints":
                        return GetProjectileInterceptorStatus(gizmo);

                    default:
                        // Try to get status from Gizmo_Slider subclasses
                        if (gizmo is Verse.Gizmo_Slider)
                            return GetSliderGizmoStatus(gizmo);
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Gets the bandwidth status (used / total).
        /// </summary>
        private static string GetMechanitorBandwidthStatus(Gizmo gizmo)
        {
            var trackerField = gizmo.GetType().GetField("tracker",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (trackerField == null) return "";

            var tracker = trackerField.GetValue(gizmo);
            if (tracker == null) return "";

            var usedProp = tracker.GetType().GetProperty("UsedBandwidth");
            var totalProp = tracker.GetType().GetProperty("TotalBandwidth");

            if (usedProp != null && totalProp != null)
            {
                int used = (int)usedProp.GetValue(tracker);
                int total = (int)totalProp.GetValue(tracker);
                return $"{used} / {total}";
            }
            return "";
        }

        /// <summary>
        /// Gets the shield energy status (current / max as percentage).
        /// </summary>
        private static string GetEnergyShieldStatus(Gizmo gizmo)
        {
            var shieldField = gizmo.GetType().GetField("shield",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            if (shieldField == null) return "";

            var shield = shieldField.GetValue(gizmo);
            if (shield == null) return "";

            var energyProp = shield.GetType().GetProperty("Energy");
            var parentProp = shield.GetType().GetProperty("parent");

            if (energyProp != null && parentProp != null)
            {
                float energy = (float)energyProp.GetValue(shield);
                var parent = parentProp.GetValue(shield) as Thing;
                if (parent != null)
                {
                    float maxEnergy = parent.GetStatValue(RimWorld.StatDefOf.EnergyShieldEnergyMax);
                    if (maxEnergy > 0)
                    {
                        float percent = (energy / maxEnergy) * 100f;
                        return $"{percent:F0}% ({energy * 100:F0} / {maxEnergy * 100:F0})";
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Gets the psychic entropy status (entropy and psyfocus values).
        /// </summary>
        private static string GetPsychicEntropyStatus(Gizmo gizmo)
        {
            var trackerField = gizmo.GetType().GetField("tracker",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (trackerField == null) return "";

            var tracker = trackerField.GetValue(gizmo);
            if (tracker == null) return "";

            var entropyProp = tracker.GetType().GetProperty("EntropyValue");
            var maxEntropyProp = tracker.GetType().GetProperty("MaxEntropy");
            var psyfocusProp = tracker.GetType().GetProperty("CurrentPsyfocus");

            if (entropyProp != null && maxEntropyProp != null && psyfocusProp != null)
            {
                float entropy = (float)entropyProp.GetValue(tracker);
                float maxEntropy = (float)maxEntropyProp.GetValue(tracker);
                float psyfocus = (float)psyfocusProp.GetValue(tracker);

                return $"Entropy: {entropy:F0} / {maxEntropy:F0}, Psyfocus: {psyfocus * 100:F0}%";
            }
            return "";
        }

        /// <summary>
        /// Gets the growth tier status (current tier and progress).
        /// </summary>
        private static string GetGrowthTierStatus(Gizmo gizmo)
        {
            var childField = gizmo.GetType().GetField("child",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (childField == null) return "";

            var child = childField.GetValue(gizmo) as Pawn;
            if (child?.ageTracker == null) return "";

            int tier = child.ageTracker.GrowthTier;
            float percent = child.ageTracker.PercentToNextGrowthTier;

            if (child.ageTracker.AtMaxGrowthTier)
                return $"Tier {tier} (Max)";
            else
                return $"Tier {tier}, {percent * 100:F0}% to next";
        }

        /// <summary>
        /// Gets the mech carrier status (current / max resources).
        /// </summary>
        private static string GetMechCarrierStatus(Gizmo gizmo)
        {
            var carrierField = gizmo.GetType().GetField("carrier",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (carrierField == null) return "";

            var carrier = carrierField.GetValue(gizmo);
            if (carrier == null) return "";

            var countProp = carrier.GetType().GetProperty("IngredientCount");
            var propsProp = carrier.GetType().GetProperty("Props");

            if (countProp != null && propsProp != null)
            {
                int count = (int)countProp.GetValue(carrier);
                var props = propsProp.GetValue(carrier);
                if (props != null)
                {
                    var maxField = props.GetType().GetField("maxIngredientCount");
                    if (maxField != null)
                    {
                        int max = (int)maxField.GetValue(props);
                        return $"{count} / {max}";
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// Gets the mech power cell status.
        /// </summary>
        private static string GetMechPowerCellStatus(Gizmo gizmo)
        {
            // MechPowerCellGizmo has a mech field
            var mechField = gizmo.GetType().GetField("mech",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (mechField == null) return "";

            var mech = mechField.GetValue(gizmo) as Pawn;
            if (mech?.needs?.energy == null) return "";

            float energy = mech.needs.energy.CurLevel;
            float max = mech.needs.energy.MaxLevel;
            float percent = (energy / max) * 100f;

            return $"{percent:F0}% ({energy:F1} / {max:F1})";
        }

        /// <summary>
        /// Gets the mech resurrection charges status.
        /// </summary>
        private static string GetMechResurrectionStatus(Gizmo gizmo)
        {
            var geneField = gizmo.GetType().GetField("gene",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            if (geneField == null) return "";

            var gene = geneField.GetValue(gizmo);
            if (gene == null) return "";

            var chargesProp = gene.GetType().GetProperty("ChargesRemaining");
            var maxProp = gene.GetType().GetProperty("MaxCharges");

            if (chargesProp != null && maxProp != null)
            {
                int charges = (int)chargesProp.GetValue(gene);
                int max = (int)maxProp.GetValue(gene);
                return $"{charges} / {max} charges";
            }
            return "";
        }

        /// <summary>
        /// Gets the projectile interceptor (shield) hit points status.
        /// </summary>
        private static string GetProjectileInterceptorStatus(Gizmo gizmo)
        {
            var compField = gizmo.GetType().GetField("interceptor",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (compField == null) return "";

            var comp = compField.GetValue(gizmo);
            if (comp == null) return "";

            var hpProp = comp.GetType().GetProperty("currentHitPoints",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            var maxHpProp = comp.GetType().GetProperty("HitPointsMax");

            if (hpProp != null && maxHpProp != null)
            {
                int hp = (int)hpProp.GetValue(comp);
                int maxHp = (int)maxHpProp.GetValue(comp);
                return $"{hp} / {maxHp} HP";
            }
            return "";
        }

        /// <summary>
        /// Gets the status for Gizmo_Slider subclasses using their ValuePercent property.
        /// </summary>
        private static string GetSliderGizmoStatus(Gizmo gizmo)
        {
            var valueProp = gizmo.GetType().GetProperty("ValuePercent",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (valueProp != null)
            {
                float value = (float)valueProp.GetValue(gizmo);
                return $"{value * 100:F0}%";
            }
            return "";
        }

        /// <summary>
        /// Gets the description text for a gizmo.
        /// </summary>
        private static string GetGizmoDescription(Gizmo gizmo)
        {
            if (gizmo is Command cmd)
            {
                string desc = cmd.Desc;
                if (string.IsNullOrEmpty(desc))
                    desc = cmd.defaultDesc;
                return desc ?? "";
            }
            return "";
        }

        /// <summary>
        /// Gets the hotkey text for a gizmo.
        /// </summary>
        private static string GetGizmoHotkey(Gizmo gizmo)
        {
            if (gizmo is Command cmd && cmd.hotKey != null)
            {
                KeyCode key = cmd.hotKey.MainKey;
                if (key != KeyCode.None)
                {
                    return key.ToStringReadable();
                }
            }
            return "";
        }
    }
}
