using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The Inventory tab's mutation vehicles: <c>EditorUI+BlockInventory</c>'s per-thing row
    /// handlers (drop/destroy/transfer, ingest, randomize), its four lower buttons, its three
    /// "Add..." buttons, and its four Copy/Paste pairs. Gated by its own
    /// <see cref="InventoryReady"/> so a rename here cannot take down the other tree sections.
    ///
    /// Every member below invokes the mod's own handler, so the mod's clipboard fields stay
    /// shared with a sighted player's Copy/Paste icons and its gates keep applying. Two of those
    /// handlers branch inline on <c>Event.current.alt</c>, which
    /// <see cref="InventoryInvokeWithSimulatedModifiers"/> selects between: simulating Alt always
    /// reaches destroy-outright, simulating nothing reaches the plain-drop branch. That
    /// plain-drop branch exists only in-game — world generation's non-Alt default is already
    /// destroy — so callers must drop <see cref="DropThing"/> during world generation. Undress
    /// is the same shape, its un-simulated call covering both mutually-exclusive non-Alt
    /// branches; the tree presents them as two differently labelled rows so the outcome is known
    /// before choosing.
    ///
    /// The row-click "open <c>DialogObjects</c> preloaded for editing" path is absent here: it is
    /// an anonymous lambda with no named handler to ride, so it lives in
    /// <see cref="CharEditorObjectsCompat.OpenForEdit"/>, constructing the dialog type directly.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static bool inventoryInitialized;
        private static bool inventoryReady;

        private static Type blockInventoryType;
        private static Type inventoryDialogTypeEnumType;

        private static MethodInfo getBlockInventory;

        private static MethodInfo invInterfaceDropMethod;
        private static MethodInfo invInterfaceIngestMethod;
        private static MethodInfo invARandomThingMethod;
        private static MethodInfo invAUndressMethod;
        private static MethodInfo invARedressMethod;
        private static MethodInfo invAReequipMethod;
        private static MethodInfo invAReinventMethod;
        private static MethodInfo invAAddGunMethod;
        private static MethodInfo invAAddApparelMethod;
        private static MethodInfo invAAddItemMethod;
        private static MethodInfo invACopyAllMethod;
        private static MethodInfo invAPasteAllMethod;
        private static MethodInfo invACopyWeaponMethod;
        private static MethodInfo invAPasteWeaponMethod;
        private static MethodInfo invACopyApparelMethod;
        private static MethodInfo invAPasteApparelMethod;
        private static MethodInfo invACopyInvMethod;
        private static MethodInfo invAPasteInvMethod;

        private static FieldInfo invCopyOutfitsField;
        private static FieldInfo invCopyItemsField;
        private static FieldInfo invCopyWeaponsField;

        /// <summary>True when every member this slice's Inventory section needs resolved.</summary>
        public static bool InventoryReady
        {
            get
            {
                EnsureInit();
                return inventoryReady;
            }
        }

        /// <summary>
        /// Called from the main <c>EnsureInit</c> once EditorUI and TabType are known.
        /// </summary>
        private static void BindInventory()
        {
            if (inventoryInitialized)
                return;
            inventoryInitialized = true;

            var surface = new ReflectionSurface("CharEditorCompat inventory");
            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            blockInventoryType = surface.Supplied("EditorUI.BlockInventory", editorUIType.GetNestedType("BlockInventory", NestedFlags));
            inventoryDialogTypeEnumType = surface.Type("CharacterEditor.DialogType");

            getBlockInventory = surface.Required("EditorUI.Get<BlockInventory>(TabType) closed",
                CloseGeneric(editorUIType, "Get", blockInventoryType));

            invInterfaceDropMethod = surface.Method(blockInventoryType, "InterfaceDrop", new[] { typeof(Thing), typeof(bool) });
            invInterfaceIngestMethod = surface.Method(blockInventoryType, "InterfaceIngest", new[] { typeof(Thing) });
            invAUndressMethod = surface.Method(blockInventoryType, "AUndress", Type.EmptyTypes);
            invARedressMethod = surface.Method(blockInventoryType, "ARedress", Type.EmptyTypes);
            invAReequipMethod = surface.Method(blockInventoryType, "AReequip", Type.EmptyTypes);
            invAReinventMethod = surface.Method(blockInventoryType, "AReinvent", Type.EmptyTypes);
            invAAddGunMethod = surface.Method(blockInventoryType, "AAddGun", Type.EmptyTypes);
            invAAddApparelMethod = surface.Method(blockInventoryType, "AAddApparel", Type.EmptyTypes);
            invAAddItemMethod = surface.Method(blockInventoryType, "AAddItem", Type.EmptyTypes);
            invACopyAllMethod = surface.Method(blockInventoryType, "ACopyAll", Type.EmptyTypes);
            invAPasteAllMethod = surface.Method(blockInventoryType, "APasteAll", Type.EmptyTypes);
            invACopyWeaponMethod = surface.Method(blockInventoryType, "ACopyWeapon", Type.EmptyTypes);
            invAPasteWeaponMethod = surface.Method(blockInventoryType, "APasteWeapon", Type.EmptyTypes);
            invACopyApparelMethod = surface.Method(blockInventoryType, "ACopyApparel", Type.EmptyTypes);
            invAPasteApparelMethod = surface.Method(blockInventoryType, "APasteApparel", Type.EmptyTypes);
            invACopyInvMethod = surface.Method(blockInventoryType, "ACopyInv", Type.EmptyTypes);
            invAPasteInvMethod = surface.Method(blockInventoryType, "APasteInv", Type.EmptyTypes);

            invCopyOutfitsField = surface.Field(blockInventoryType, "lOfCopyOutfits");
            invCopyItemsField = surface.Field(blockInventoryType, "lOfCopyItems");
            invCopyWeaponsField = surface.Field(blockInventoryType, "lOfCopyWeapons");

            // The signature names a mod-internal enum, so it binds only once that type resolved.
            invARandomThingMethod = inventoryDialogTypeEnumType == null ? null
                : surface.Method(blockInventoryType, "ARandomThing", new[] { typeof(Thing), inventoryDialogTypeEnumType });

            inventoryReady = surface.Ready;
        }

        private static object InventoryBlock(Window editorUI)
        {
            return Block(editorUI, getBlockInventory, "BlockInventory");
        }

        private static void InvokeOnInventoryBlock(Window editorUI, MethodInfo method, object[] args, string caller)
        {
            if (!InventoryReady || method == null)
                return;
            try
            {
                object block = InventoryBlock(editorUI);
                if (block != null)
                    method.Invoke(block, args);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        /// <summary>Runs a handler with modifiers simulated, to select its inline Alt branch; gated on this slice's own readiness.</summary>
        private static void InventoryInvokeWithSimulatedModifiers(MethodInfo method, object instance, object[] args, EventModifiers modifiers, string caller)
        {
            if (!InventoryReady || method == null)
                return;
            Event evt = Event.current;
            EventModifiers original = evt != null ? evt.modifiers : EventModifiers.None;
            try
            {
                if (evt != null)
                    KeyboardHelper.SetEventModifiers(evt, modifiers);
                method.Invoke(instance, args);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
            finally
            {
                if (evt != null)
                    KeyboardHelper.SetEventModifiers(evt, original);
            }
        }

        // Clipboard reads, for the Paste rows' disabled-with-reason gate.

        public static bool HasWeaponClipboard(Window editorUI) => InventoryClipboardNonEmpty(editorUI, invCopyWeaponsField, "HasWeaponClipboard");

        public static bool HasApparelClipboard(Window editorUI) => InventoryClipboardNonEmpty(editorUI, invCopyOutfitsField, "HasApparelClipboard");

        public static bool HasInventoryClipboard(Window editorUI) => InventoryClipboardNonEmpty(editorUI, invCopyItemsField, "HasInventoryClipboard");

        /// <summary>All three lists at once, matching the Armor separator's combined Copy/Paste.</summary>
        public static bool HasAnyInventoryClipboard(Window editorUI) =>
            HasWeaponClipboard(editorUI) || HasApparelClipboard(editorUI) || HasInventoryClipboard(editorUI);

        private static bool InventoryClipboardNonEmpty(Window editorUI, FieldInfo field, string caller)
        {
            if (!InventoryReady)
                return false;
            try
            {
                object block = InventoryBlock(editorUI);
                var list = block != null ? field.GetValue(block) as System.Collections.ICollection : null;
                return list != null && list.Count > 0;
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
                return false;
            }
        }

        /// <summary>The plain in-game drop path; absent during world generation, see class remarks.</summary>
        public static void DropThing(Window editorUI, Thing thing)
        {
            if (thing == null)
                return;
            InvokeOnInventorySimulated(editorUI, invInterfaceDropMethod, new object[] { thing, false }, EventModifiers.None, "DropThing");
        }

        /// <summary>The destroy-outright path, always reachable.</summary>
        public static void DestroyThing(Window editorUI, Thing thing)
        {
            if (thing == null)
                return;
            InvokeOnInventorySimulated(editorUI, invInterfaceDropMethod, new object[] { thing, false }, EventModifiers.Alt, "DestroyThing");
        }

        /// <summary>Moves a thing between worn/equipped and inventory; this branch never reads Alt.</summary>
        public static void TransferThing(Window editorUI, Thing thing)
        {
            if (thing == null)
                return;
            InvokeOnInventoryBlock(editorUI, invInterfaceDropMethod, new object[] { thing, true }, "TransferThing");
        }

        public static void IngestThing(Window editorUI, Thing thing)
        {
            if (thing == null)
                return;
            InvokeOnInventoryBlock(editorUI, invInterfaceIngestMethod, new object[] { thing }, "IngestThing");
        }

        /// <summary>Replaces <paramref name="thing"/> with a random equivalent of the same <paramref name="mode"/>; creation mode only, per the caller's gate.</summary>
        public static void RandomizeThing(Window editorUI, Thing thing, CharEditorObjectsCompat.ObjectsMode mode)
        {
            if (thing == null || !InventoryReady)
                return;
            try
            {
                object block = InventoryBlock(editorUI);
                object boxedType = EnumValue(inventoryDialogTypeEnumType, mode.ToString());
                if (block != null && boxedType != null)
                    invARandomThingMethod.Invoke(block, new object[] { thing, boxedType });
            }
            catch (Exception ex)
            {
                Fail("RandomizeThing", ex);
            }
        }

        private static void InvokeOnInventorySimulated(Window editorUI, MethodInfo method, object[] args, EventModifiers modifiers, string caller)
        {
            if (!InventoryReady || method == null)
                return;
            object block = InventoryBlock(editorUI);
            if (block != null)
                InventoryInvokeWithSimulatedModifiers(method, block, args, modifiers, caller);
        }

        // Undress (three-way branch, see class remarks) / Redress / Reequip / Reinvent.

        /// <summary>The non-Alt branch: drops in-game, moves to inventory during world generation.</summary>
        public static void Undress(Window editorUI) =>
            InvokeOnInventorySimulated(editorUI, invAUndressMethod, null, EventModifiers.None, "Undress");

        /// <summary>The Alt branch: destroys the randomly chosen worn item outright.</summary>
        public static void UndressDestroy(Window editorUI) =>
            InvokeOnInventorySimulated(editorUI, invAUndressMethod, null, EventModifiers.Alt, "UndressDestroy");

        public static void Redress(Window editorUI) => InvokeOnInventoryBlock(editorUI, invARedressMethod, null, "Redress");

        public static void Reequip(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAReequipMethod, null, "Reequip");

        public static void Reinvent(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAReinventMethod, null, "Reinvent");

        // Add equipment / apparel / item: opens an unloaded DialogObjects in the matching mode.

        public static void AddEquipment(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAAddGunMethod, null, "AddEquipment");

        public static void AddApparel(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAAddApparelMethod, null, "AddApparel");

        public static void AddItem(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAAddItemMethod, null, "AddItem");

        // Copy / paste, per list plus the Armor separator's combined "all three".

        public static void CopyEquipment(Window editorUI) => InvokeOnInventoryBlock(editorUI, invACopyWeaponMethod, null, "CopyEquipment");

        public static void PasteEquipment(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAPasteWeaponMethod, null, "PasteEquipment");

        public static void CopyApparel(Window editorUI) => InvokeOnInventoryBlock(editorUI, invACopyApparelMethod, null, "CopyApparel");

        public static void PasteApparel(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAPasteApparelMethod, null, "PasteApparel");

        public static void CopyInventory(Window editorUI) => InvokeOnInventoryBlock(editorUI, invACopyInvMethod, null, "CopyInventory");

        public static void PasteInventory(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAPasteInvMethod, null, "PasteInventory");

        public static void CopyAllGear(Window editorUI) => InvokeOnInventoryBlock(editorUI, invACopyAllMethod, null, "CopyAllGear");

        public static void PasteAllGear(Window editorUI) => InvokeOnInventoryBlock(editorUI, invAPasteAllMethod, null, "PasteAllGear");
    }
}
