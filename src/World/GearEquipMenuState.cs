using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages the gear equip/swap menu for caravan gear management.
    /// Shows list of pawns with their current equipment in the relevant slot.
    /// </summary>
    public static class GearEquipMenuState
    {
        public static bool IsActive { get; private set; } = false;

        private static Caravan currentCaravan = null;
        private static Thing itemToEquip = null;
        private static Pawn sourceOwner = null; // Pawn who currently has the item (null if from inventory)
        private static bool isWeapon = false;
        private static List<PawnEquipOption> options = new List<PawnEquipOption>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        /// <summary>
        /// Represents a pawn option in the equip menu
        /// </summary>
        private class PawnEquipOption
        {
            public Pawn Pawn { get; set; }
            public bool CanEquip { get; set; }
            public string CantEquipReason { get; set; }
            public string CurrentEquipmentLabel { get; set; }
            public Thing CurrentEquipment { get; set; } // For swap functionality
            public bool IsUnequipOption { get; set; } // Special "Unequip to inventory" option

            public string GetDisplayLabel()
            {
                if (IsUnequipOption)
                {
                    return "RimWorldAccess.Gear.UnequipToInventory".Translate();
                }
                if (!CanEquip)
                {
                    return "RimWorldAccess.Gear.PawnCantEquip".Translate(Pawn.LabelShortCap, CantEquipReason);
                }
                return "RimWorldAccess.Gear.PawnWithCurrent".Translate(Pawn.LabelShortCap, CurrentEquipmentLabel);
            }
        }

        /// <summary>
        /// Opens the equip menu for a gear item.
        /// </summary>
        /// <param name="caravan">The caravan</param>
        /// <param name="item">The item to equip</param>
        /// <param name="currentOwner">The pawn who currently has the item equipped (null if from inventory)</param>
        public static void Open(Caravan caravan, Thing item, Pawn currentOwner)
        {
            if (caravan == null || item == null)
            {
                TolkHelper.Speak("RimWorldAccess.Gear.CannotOpenEquipMenu".Loc(), SpeechPriority.High);
                return;
            }

            currentCaravan = caravan;
            itemToEquip = item;
            sourceOwner = currentOwner;
            isWeapon = item.def.IsWeapon;

            BuildOptions();

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Gear.NoPawnsAvailable".Loc(), SpeechPriority.High);
                return;
            }

            selectedIndex = 0;
            typeahead.ClearSearch();
            IsActive = true;
            SoundDefOf.TabOpen.PlayOneShotOnCamera();

            // Build header announcement
            string itemName = item.LabelCap;
            string actionType = currentOwner != null
                ? "RimWorldAccess.Gear.ActionGive".Translate()
                : "RimWorldAccess.Gear.ActionEquip".Translate();
            TolkHelper.Speak("RimWorldAccess.Gear.OpenInstructions".Loc(actionType, itemName));

            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Builds the list of pawn options for equipping.
        /// </summary>
        private static void BuildOptions()
        {
            options.Clear();

            // If item is currently equipped, add "Unequip to inventory" as first option
            if (sourceOwner != null)
            {
                options.Add(new PawnEquipOption
                {
                    IsUnequipOption = true,
                    CanEquip = true
                });
            }

            var canEquipList = new List<PawnEquipOption>();
            var cantEquipList = new List<PawnEquipOption>();

            // Get all humanlike pawns in the caravan
            var pawns = currentCaravan.PawnsListForReading
                .Where(p => p.RaceProps.Humanlike && !p.Dead && !p.Downed)
                .OrderBy(p => p.LabelShortCap);

            foreach (Pawn pawn in pawns)
            {
                // Skip the source owner (can't give to yourself)
                if (pawn == sourceOwner)
                    continue;

                var option = new PawnEquipOption { Pawn = pawn };

                // Check if pawn can equip this item
                if (!EquipmentUtility.CanEquip(itemToEquip, pawn, out string cantReason))
                {
                    option.CanEquip = false;
                    option.CantEquipReason = cantReason ?? "unknown";
                    cantEquipList.Add(option);
                    continue;
                }

                // Additional checks for weapons
                if (isWeapon)
                {
                    if (pawn.guest?.IsPrisoner == true)
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = "prisoner";
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = "pacifist";
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (pawn.WorkTagIsDisabled(WorkTags.Shooting) && itemToEquip.def.IsRangedWeapon)
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = "can't shoot";
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = "can't manipulate";
                        cantEquipList.Add(option);
                        continue;
                    }
                }

                // Check for apparel-specific issues
                if (itemToEquip is Apparel apparel)
                {
                    if (!ApparelUtility.HasPartsToWear(pawn, apparel.def))
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = "missing body parts";
                        cantEquipList.Add(option);
                        continue;
                    }
                    if (pawn.apparel?.WouldReplaceLockedApparel(apparel) == true)
                    {
                        option.CanEquip = false;
                        option.CantEquipReason = "would replace locked apparel";
                        cantEquipList.Add(option);
                        continue;
                    }
                }

                // Pawn can equip - find what they currently have in this slot
                option.CanEquip = true;
                Thing currentEquip;
                string currentLabel;
                GetCurrentEquipmentInSlot(pawn, out currentEquip, out currentLabel);
                option.CurrentEquipment = currentEquip;
                option.CurrentEquipmentLabel = currentLabel;
                canEquipList.Add(option);
            }

            // Add can-equip pawns first, then can't-equip pawns
            options.AddRange(canEquipList);
            options.AddRange(cantEquipList);
        }

        /// <summary>
        /// Gets the current equipment a pawn has in the slot that would be used by the item.
        /// </summary>
        private static void GetCurrentEquipmentInSlot(Pawn pawn, out Thing currentEquipment, out string label)
        {
            currentEquipment = null;
            label = "";

            if (isWeapon)
            {
                // For weapons, check primary weapon
                var weapon = pawn.equipment?.Primary;
                if (weapon != null)
                {
                    currentEquipment = weapon;
                    label = weapon.LabelCap;
                }
                else
                {
                    label = "unarmed";
                }
            }
            else if (itemToEquip is Apparel apparel)
            {
                // For apparel, find conflicting apparel
                var conflicting = GetConflictingApparel(pawn, apparel);
                if (conflicting != null && conflicting.Count > 0)
                {
                    currentEquipment = conflicting[0];
                    if (conflicting.Count == 1)
                    {
                        label = conflicting[0].LabelCap;
                    }
                    else
                    {
                        label = $"{conflicting[0].LabelCap} +{conflicting.Count - 1} more";
                    }
                }
                else
                {
                    label = "none";
                }
            }
        }

        /// <summary>
        /// Gets apparel that would conflict with equipping the given apparel.
        /// </summary>
        private static List<Apparel> GetConflictingApparel(Pawn pawn, Apparel newApparel)
        {
            var conflicting = new List<Apparel>();
            if (pawn.apparel?.WornApparel == null)
                return conflicting;

            foreach (var worn in pawn.apparel.WornApparel)
            {
                if (!ApparelUtility.CanWearTogether(newApparel.def, worn.def, pawn.RaceProps.body))
                {
                    conflicting.Add(worn);
                }
            }
            return conflicting;
        }

        /// <summary>
        /// Closes the equip menu.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentCaravan = null;
            itemToEquip = null;
            sourceOwner = null;
            options.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
        }

        /// <summary>
        /// Handles typeahead character input from the layout-aware dispatcher.
        /// </summary>
        public static void HandleTypeahead(char c)
        {
            if (!IsActive) return;

            var labels = GetItemLabels();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    selectedIndex = newIndex;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        /// <summary>
        /// Selects the next option.
        /// </summary>
        public static void SelectNext()
        {
            if (options.Count == 0) return;
            selectedIndex = MenuHelper.SelectNext(selectedIndex, options.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Selects the previous option.
        /// </summary>
        public static void SelectPrevious()
        {
            if (options.Count == 0) return;
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, options.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Executes the currently selected option.
        /// </summary>
        public static void ExecuteSelected()
        {
            if (options.Count == 0 || selectedIndex < 0 || selectedIndex >= options.Count)
                return;

            var option = options[selectedIndex];

            if (!option.CanEquip)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Gear.CannotEquipReason".Loc(option.CantEquipReason));
                return;
            }

            string itemName = itemToEquip.LabelCap;

            try
            {
                // Handle "Unequip to inventory" option
                if (option.IsUnequipOption)
                {
                    PerformUnequipToInventory();
                    TolkHelper.Speak("RimWorldAccess.Gear.UnequippedToInventory".Loc(itemName));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                else
                {
                    // Perform the equip/swap
                    bool isSwap = option.CurrentEquipment != null;
                    string targetName = option.Pawn.LabelShortCap;

                    PerformEquip(option.Pawn, option.CurrentEquipment);

                    // Announce result
                    if (isSwap)
                    {
                        string swappedItem = option.CurrentEquipment?.LabelCap ?? "item";
                        if (sourceOwner != null)
                        {
                            // Both pawns now have each other's gear
                            TolkHelper.Speak("RimWorldAccess.Gear.SwappedTwoOwners".Loc(targetName, itemName, sourceOwner.LabelShortCap, swappedItem));
                        }
                        else
                        {
                            // Item was from inventory, target's old item goes to inventory
                            TolkHelper.Speak("RimWorldAccess.Gear.SwappedToInventory".Loc(targetName, itemName, swappedItem));
                        }
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Gear.EquippedTo".Loc(itemName, targetName));
                    }

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error equipping gear: {ex}");
                TolkHelper.Speak("RimWorldAccess.Gear.FailedToEquip".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }

            // Close menu and refresh caravan tree
            Close();
            CaravanInspectState.RefreshTree();
        }

        /// <summary>
        /// Unequips the item from source owner to caravan inventory.
        /// </summary>
        private static void PerformUnequipToInventory()
        {
            if (sourceOwner == null)
                return;

            if (isWeapon)
            {
                ThingWithComps weapon = itemToEquip as ThingWithComps;
                if (weapon != null && sourceOwner.equipment?.Primary == weapon)
                {
                    sourceOwner.equipment.Remove(weapon);
                    // Move to inventory
                    Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(weapon, currentCaravan.PawnsListForReading, null);
                    if (carrier != null)
                    {
                        carrier.inventory.innerContainer.TryAdd(weapon);
                    }
                }
            }
            else if (itemToEquip is Apparel apparel)
            {
                if (sourceOwner.apparel?.WornApparel?.Contains(apparel) == true)
                {
                    sourceOwner.apparel.Remove(apparel);
                    // Move to inventory
                    Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(apparel, currentCaravan.PawnsListForReading, null);
                    if (carrier != null)
                    {
                        carrier.inventory.innerContainer.TryAdd(apparel);
                    }
                }
            }
        }

        /// <summary>
        /// Performs the actual equip/swap operation.
        /// </summary>
        private static void PerformEquip(Pawn targetPawn, Thing targetCurrentEquipment)
        {
            if (isWeapon)
            {
                PerformWeaponEquip(targetPawn);
            }
            else if (itemToEquip is Apparel apparel)
            {
                PerformApparelEquip(targetPawn, apparel);
            }
        }

        /// <summary>
        /// Equips a weapon to the target pawn.
        /// </summary>
        private static void PerformWeaponEquip(Pawn targetPawn)
        {
            ThingWithComps weaponToEquip = itemToEquip as ThingWithComps;
            if (weaponToEquip == null) return;

            // If source owner has the item equipped, unequip it first
            if (sourceOwner != null && sourceOwner.equipment?.Primary == weaponToEquip)
            {
                sourceOwner.equipment.Remove(weaponToEquip);
            }
            // If item is in someone's inventory, remove it
            else
            {
                foreach (Pawn p in currentCaravan.PawnsListForReading)
                {
                    if (p.inventory?.innerContainer?.Contains(weaponToEquip) == true)
                    {
                        p.inventory.innerContainer.Remove(weaponToEquip);
                        break;
                    }
                }
            }

            // If target has a weapon, move it to inventory (or to source for swap)
            ThingWithComps targetWeapon = targetPawn.equipment?.Primary;
            if (targetWeapon != null)
            {
                targetPawn.equipment.Remove(targetWeapon);

                // If this is a swap (source had a weapon), give old weapon to source
                if (sourceOwner != null)
                {
                    sourceOwner.equipment.AddEquipment(targetWeapon);
                }
                else
                {
                    // Move to inventory
                    Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(targetWeapon, currentCaravan.PawnsListForReading, null);
                    if (carrier != null)
                    {
                        carrier.inventory.innerContainer.TryAdd(targetWeapon);
                    }
                }
            }

            // Equip the weapon to target
            targetPawn.equipment.AddEquipment(weaponToEquip);
        }

        /// <summary>
        /// Equips apparel to the target pawn.
        /// </summary>
        private static void PerformApparelEquip(Pawn targetPawn, Apparel apparel)
        {
            Apparel apparelToEquip = apparel;

            // If source owner has the apparel worn, remove it first
            if (sourceOwner != null && sourceOwner.apparel?.WornApparel?.Contains(apparel) == true)
            {
                // Check if apparel is locked (Issue 6: Locked apparel check)
                if (sourceOwner.apparel.IsLocked(apparel))
                {
                    TolkHelper.Speak("RimWorldAccess.Gear.CannotRemoveLockedApparel".Loc());
                    return;
                }
                sourceOwner.apparel.Remove(apparel);
            }
            // If item is in someone's inventory, remove it
            else
            {
                foreach (Pawn p in currentCaravan.PawnsListForReading)
                {
                    if (p.inventory?.innerContainer?.Contains(apparel) == true)
                    {
                        // Use SplitOff(1) pattern to handle stacked items correctly
                        // (Issue 5: Matches game's WITab_Caravan_Gear.cs behavior)
                        apparelToEquip = (Apparel)apparel.SplitOff(1);
                        break;
                    }
                }
            }

            // Remove conflicting apparel from target
            var conflicting = GetConflictingApparel(targetPawn, apparelToEquip);
            foreach (var worn in conflicting)
            {
                // Check if conflicting apparel on target is locked
                if (targetPawn.apparel.IsLocked(worn))
                {
                    TolkHelper.Speak("RimWorldAccess.Gear.CannotRemoveLockedFromPawn".Loc(worn.LabelCap, targetPawn.LabelShortCap));
                    // If we split off an item, put it back
                    if (apparelToEquip != apparel)
                    {
                        apparel.TryAbsorbStack(apparelToEquip, respectStackLimit: true);
                    }
                    return;
                }
                targetPawn.apparel.Remove(worn);

                // If this is a swap and source can wear it, give to source
                if (sourceOwner != null && ApparelUtility.HasPartsToWear(sourceOwner, worn.def))
                {
                    // Check if source already has conflicting apparel
                    bool sourceCanWear = true;
                    if (sourceOwner.apparel != null)
                    {
                        foreach (var sourceWorn in sourceOwner.apparel.WornApparel)
                        {
                            if (!ApparelUtility.CanWearTogether(worn.def, sourceWorn.def, sourceOwner.RaceProps.body))
                            {
                                sourceCanWear = false;
                                break;
                            }
                        }
                    }

                    if (sourceCanWear)
                    {
                        sourceOwner.apparel.Wear(worn, dropReplacedApparel: false);
                        continue;
                    }
                }

                // Otherwise move to inventory
                Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(worn, currentCaravan.PawnsListForReading, null);
                if (carrier != null)
                {
                    carrier.inventory.innerContainer.TryAdd(worn);
                }
            }

            // Wear the apparel (using SplitOff result if from inventory)
            targetPawn.apparel.Wear(apparelToEquip, dropReplacedApparel: false);

            // Force the apparel so it doesn't get auto-removed
            if (targetPawn.outfits != null)
            {
                targetPawn.outfits.forcedHandler.SetForced(apparel, forced: true);
            }
        }

        /// <summary>
        /// Gets labels for typeahead search.
        /// </summary>
        private static List<string> GetItemLabels()
        {
            return options.Select(o => o.GetDisplayLabel()).ToList();
        }

        /// <summary>
        /// Announces the current selection.
        /// </summary>
        private static void AnnounceCurrentSelection()
        {
            if (options.Count == 0 || selectedIndex < 0 || selectedIndex >= options.Count)
                return;

            var option = options[selectedIndex];
            string label = option.GetDisplayLabel();
            string position = MenuHelper.FormatPosition(selectedIndex, options.Count);

            TolkHelper.Speak("RimWorldAccess.Gear.LabelPosition".Loc(label, position));
        }

        /// <summary>
        /// Announces with search context.
        /// </summary>
        private static void AnnounceWithSearch()
        {
            if (options.Count == 0 || selectedIndex < 0 || selectedIndex >= options.Count)
                return;

            var option = options[selectedIndex];
            string label = option.GetDisplayLabel();

            if (typeahead.HasActiveSearch)
            {
                TolkHelper.Speak(typeahead.BuildItemAnnouncement(label));
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }

        /// <summary>
        /// Handles keyboard input.
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive)
                return false;

            if (ev.type != EventType.KeyDown)
                return false;

            KeyCode key = ev.keyCode;

            // Handle Escape
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrentSelection();
                    ev.Use();
                    return true;
                }
                Close();
                SoundDefOf.Click.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
                ev.Use();
                return true;
            }

            // Handle Backspace
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = GetItemLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) selectedIndex = newIndex;
                    AnnounceWithSearch();
                }
                ev.Use();
                return true;
            }

            // Handle Up arrow
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int prevIndex = typeahead.GetPreviousMatch(selectedIndex);
                    if (prevIndex >= 0)
                    {
                        selectedIndex = prevIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectPrevious();
                }
                ev.Use();
                return true;
            }

            // Handle Down arrow
            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    int nextIndex = typeahead.GetNextMatch(selectedIndex);
                    if (nextIndex >= 0)
                    {
                        selectedIndex = nextIndex;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        AnnounceWithSearch();
                    }
                }
                else
                {
                    SelectNext();
                }
                ev.Use();
                return true;
            }

            // Handle Enter
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ExecuteSelected();
                ev.Use();
                return true;
            }

            // Handle typeahead
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                ev.Use();
                return true;
            }

            return false;
        }
    }
}
