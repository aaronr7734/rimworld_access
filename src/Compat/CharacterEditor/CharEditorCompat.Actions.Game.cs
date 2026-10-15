using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The Actions region's mutation vehicles: <c>BlockPerson</c>'s creation toolbar
    /// (bplus2/bminus2/brandom/breplace/bgender/barmory/bskills/bresurrect/bclone, the
    /// move-up/move-down world-gen buttons), the preset save/load/clear slot machinery
    /// (<c>PresetPawn</c>, <c>MessageTool</c>, <c>CEditor.GetSlot</c>/<c>SetSlot</c>/
    /// <c>NumSlots</c>), and the utility row handlers (gender cycle via
    /// <c>NameTool.SetPawnGender</c>, nude/hats toggles, portrait rotate, jump to pawn). Gated by
    /// its own <see cref="ActionsReady"/> so a rename here cannot take down the sibling slices.
    ///
    /// MUTATION VEHICLES:
    /// <list type="bullet">
    /// <item>The non-branching creation-toolbar buttons invoke <c>BlockPerson</c>'s own no-arg
    /// handler verbatim (vehicle A). <see cref="RemovePawn"/> invokes <c>ARemovePawn</c> — still
    /// vehicle A, since it is the mod's own named delegate — AFTER the caller has shown our confirm,
    /// the mod's own button carrying none.</item>
    /// <item>Modifier-branching handlers read <c>Event.current.alt/shift/control/capsLock</c> inline
    /// to pick a branch, and every chord becomes a discrete option.
    /// <see cref="InvokeActionWithSimulatedModifiers"/> sets <c>Event.current.modifiers</c> to the
    /// single flag the option represents, invokes, and restores; it does not reuse the sibling
    /// slice's copy, which is gated on <c>AppearanceReady</c> and would couple this slice's actions
    /// to Appearance's binding success. CapsLock lives in the same bitmask, so those branches need
    /// no separate case.</item>
    /// <item><see cref="RandomizeEquip"/>'s options are NOT symmetric: Alt-alone and Shift-alone
    /// isolate Redress-only and Reequip-only (<c>bool flag = !alt &amp;&amp; !shift;</c>), but
    /// Control-alone leaves <c>flag</c> true, so it redresses and reequips IN ADDITION to
    /// recoloring — a mod quirk, not a binding bug. <see cref="RecolorApparelOnly"/> is therefore
    /// MUTATION-C: it mirrors only the inner recolor loop, since no A/B vehicle isolates it.</item>
    /// <item>Presets: <see cref="SaveToSlot"/> mirrors <c>ASavePawn</c>'s per-slot delegate at the
    /// call level (MUTATION-C — the real delegate is a compiler-generated closure, unreachable by
    /// reflection): set <c>iRemSlot</c> as the delegate does, then either invoke
    /// <c>AConfirmSavePawn</c> directly for an empty slot or open the mod's OWN overwrite dialog via
    /// <c>MessageTool.ShowCustomDialog</c> for an occupied one. <see cref="LoadFromSlot"/> and
    /// <see cref="PeekSlotMods"/> call the exact <c>PresetPawn</c> methods that delegate calls.
    /// <see cref="ClearSlot"/> calls <c>CEditor.SetSlot</c>, the same vehicle the mod's Ctrl-click
    /// clear uses, after the caller's confirm.</item>
    /// <item><see cref="SetGender"/> invokes the standalone <c>NameTool.SetPawnGender</c> extension:
    /// the gender toggle has NO named handler at all (three raw ButtonImage branches inline in
    /// <c>BlockPerson.DrawMainIcons</c>), so this is the only stable vehicle, and it already carries
    /// the mod's head-regeneration-with-rollback logic.</item>
    /// </list>
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static bool actionsInitialized;
        private static bool actionsReady;

        private static Type presetPawnType;
        private static Type messageToolType;
        private static Type capturerType;
        private static Type nameToolType;
        private static Type colorToolType;

        // BlockPerson: creation toolbar, no-arg or dummy-Color-arg instance methods.
        private static MethodInfo bpAddPawnMethod;
        private static MethodInfo bpRemovePawnMethod;
        private static MethodInfo bpClonePawnMethod;
        private static MethodInfo bpRandomizePawnMethod;
        private static MethodInfo bpRandomizePawnKeepRaceMethod;
        private static MethodInfo bpRandomizeBodyPartsMethod;
        private static MethodInfo bpRandomizeEquipMethod;
        private static MethodInfo bpRandomizeBioMethod;
        private static MethodInfo bpQuickResurrectMethod;
        private static MethodInfo bpMoveUpMethod;
        private static MethodInfo bpMoveDownMethod;
        private static MethodInfo bpToggleNudeMethod;
        private static MethodInfo bpToggleHatsMethod;
        private static FieldInfo bpShowClothesField;
        private static FieldInfo bpShowHatField;
        private static MethodInfo bpRotateMethod;
        private static MethodInfo bpJumpToPawnMethod;

        // BlockPerson: presets.
        private static MethodInfo bpSlotLabelMethod;
        private static MethodInfo bpConfirmSavePawnMethod;
        private static FieldInfo bpRemSlotField;

        // CEditor: slots and the Capturer container.
        private static MethodInfo apiGetSlotMethod;
        private static MethodInfo apiSetSlotMethod;
        private static MethodInfo apiNumSlotsGetter;
        private static MethodInfo apiGetCapturerMethod;
        private static object eTypeCapturer;
        private static FieldInfo capturerRotationField;

        // PresetPawn.
        private static ConstructorInfo presetPawnCtor;
        private static MethodInfo presetPawnLoadMethod;
        private static MethodInfo presetPawnLoadPawnMethod;
        private static MethodInfo presetPawnSavePawnMethod;
        private static MethodInfo presetPawnShowPawnModsMethod;

        // MessageTool and Label.
        private static MethodInfo messageToolShowCustomDialogMethod;
        private static MethodInfo messageToolShowMethod;
        private static FieldInfo labelOverwriteExistingField;

        // NameTool and ColorTool.
        private static MethodInfo nameToolSetPawnGenderMethod;
        private static MethodInfo colorToolRandomAlphaColorGetter;

        /// <summary>True when every member this slice's Actions section rows need resolved.</summary>
        public static bool ActionsReady
        {
            get
            {
                EnsureInit();
                return actionsReady;
            }
        }

        /// <summary>Runs after <see cref="BindAppearance"/>, reusing the types it bound and the shared label table.</summary>
        private static void BindActions()
        {
            if (actionsInitialized)
                return;
            actionsInitialized = true;

            var surface = new ReflectionSurface("CharEditorCompat actions");
            surface.Supplied("CharacterEditor.CEditor", ceditorType);
            surface.Supplied("EditorUI.BlockPerson", blockPersonType);
            surface.Supplied("CharacterEditor.EType", eTypeType);
            surface.Supplied("CharacterEditor.Label", Labels.LabelType);

            bpAddPawnMethod = surface.Method(blockPersonType, "AAddPawn", Type.EmptyTypes);
            bpRemovePawnMethod = surface.Method(blockPersonType, "ARemovePawn", Type.EmptyTypes);
            bpClonePawnMethod = surface.Method(blockPersonType, "AClonePawn", Type.EmptyTypes);
            bpRandomizePawnMethod = surface.Method(blockPersonType, "ARandomizePawn", Type.EmptyTypes);
            bpRandomizePawnKeepRaceMethod = surface.Method(blockPersonType, "ARandomizePawnKeepRace", Type.EmptyTypes);
            bpRandomizeBodyPartsMethod = surface.Method(blockPersonType, "ARandomizeBodyParts", Type.EmptyTypes);
            bpRandomizeEquipMethod = surface.Method(blockPersonType, "ARandomizeEquip", Type.EmptyTypes);
            bpRandomizeBioMethod = surface.Method(blockPersonType, "ARandomizeBio", Type.EmptyTypes);
            bpQuickResurrectMethod = surface.Method(blockPersonType, "AQuickResurrect", Type.EmptyTypes);
            bpMoveUpMethod = surface.Method(blockPersonType, "AMoveUp", Type.EmptyTypes);
            bpMoveDownMethod = surface.Method(blockPersonType, "AMoveDown", Type.EmptyTypes);
            bpToggleNudeMethod = surface.Method(blockPersonType, "AToggleNude", new[] { typeof(Color) });
            bpToggleHatsMethod = surface.Method(blockPersonType, "AToggleHats", new[] { typeof(Color) });
            bpShowClothesField = surface.Field(blockPersonType, "bShowClothes");
            bpShowHatField = surface.Field(blockPersonType, "bShowHat");
            bpRotateMethod = surface.Method(blockPersonType, "ARotate", new[] { typeof(Color) });
            bpJumpToPawnMethod = surface.Method(blockPersonType, "AJumpToPawn", Type.EmptyTypes);
            bpSlotLabelMethod = surface.Method(blockPersonType, "SlotLabel", new[] { typeof(int), typeof(bool) });
            bpConfirmSavePawnMethod = surface.Method(blockPersonType, "AConfirmSavePawn", Type.EmptyTypes);
            bpRemSlotField = surface.Field(blockPersonType, "iRemSlot");

            apiGetSlotMethod = surface.Method(ceditorType, "GetSlot", new[] { typeof(int) });
            apiSetSlotMethod = surface.Method(ceditorType, "SetSlot", new[] { typeof(int), typeof(string), typeof(bool) });
            apiNumSlotsGetter = surface.Property(ceditorType, "NumSlots")?.GetGetMethod(true);

            capturerType = surface.Type("CharacterEditor.Capturer");
            apiGetCapturerMethod = surface.Required("CEditor.Get<Capturer>(EType) closed",
                CloseGeneric(ceditorType, "Get", capturerType));
            capturerRotationField = surface.Field(capturerType, "iCurrentRotation");
            eTypeCapturer = surface.Required("EType.Capturer boxed", EnumValue(eTypeType, "Capturer"));

            presetPawnType = surface.Type("CharacterEditor.PresetPawn");
            presetPawnCtor = surface.Constructor(presetPawnType, Type.EmptyTypes);
            presetPawnLoadMethod = surface.Method(presetPawnType, "Load", new[] { typeof(int), typeof(string) });
            presetPawnLoadPawnMethod = surface.Method(presetPawnType, "LoadPawn", new[] { typeof(int), typeof(bool), typeof(string) });
            presetPawnSavePawnMethod = surface.Method(presetPawnType, "SavePawn", new[] { typeof(Pawn), typeof(int) });
            presetPawnShowPawnModsMethod = surface.Method(presetPawnType, "ShowPawnMods", Type.EmptyTypes);

            messageToolType = surface.Type("CharacterEditor.MessageTool");
            messageToolShowCustomDialogMethod = surface.Method(messageToolType, "ShowCustomDialog",
                new[] { typeof(string), typeof(string), typeof(Action), typeof(Action), typeof(Action) });
            messageToolShowMethod = surface.Method(messageToolType, "Show", new[] { typeof(string), typeof(MessageTypeDef) });

            labelOverwriteExistingField = surface.Field(Labels.LabelType, "OVERWRITE_EXISTING");

            nameToolType = surface.Type("CharacterEditor.NameTool");
            nameToolSetPawnGenderMethod = surface.Method(nameToolType, "SetPawnGender", new[] { typeof(Pawn), typeof(Gender) });

            colorToolType = surface.Type("CharacterEditor.ColorTool");
            colorToolRandomAlphaColorGetter = surface.Property(colorToolType, "RandomAlphaColor")?.GetGetMethod(true);

            actionsReady = surface.Ready && EditorCore.Ready && Labels.Ready;
        }

        // Simulated-modifier invocation, scoped to this slice's own readiness — deliberately not
        // the sibling copy, which gates on AppearanceReady.

        private static void InvokeActionWithSimulatedModifiers(MethodInfo method, object instance, EventModifiers modifiers, string caller)
        {
            if (!ActionsReady || method == null)
                return;
            Event evt = Event.current;
            EventModifiers original = evt != null ? evt.modifiers : EventModifiers.None;
            try
            {
                if (evt != null)
                    KeyboardHelper.SetEventModifiers(evt, modifiers);
                method.Invoke(instance, null);
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

        private static object ActionsBlockPerson(Window editorUI)
        {
            return Block(editorUI, getBlockPerson, "BlockPerson");
        }

        private static void InvokeNoArg(Window editorUI, MethodInfo method, string caller)
        {
            if (!ActionsReady || method == null)
                return;
            try
            {
                object block = ActionsBlockPerson(editorUI);
                if (block != null)
                    method.Invoke(block, null);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        private static void InvokeDummyColor(Window editorUI, MethodInfo method, string caller)
        {
            if (!ActionsReady || method == null)
                return;
            try
            {
                object block = ActionsBlockPerson(editorUI);
                if (block != null)
                    method.Invoke(block, new object[] { Color.white });
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // Creation subsection.

        /// <summary>Vehicle A: BlockPerson's own bplus2 handler, which reads whatever the Pawns region's creation-mode dropdowns hold.</summary>
        public static void AddPawn(Window editorUI) => InvokeNoArg(editorUI, bpAddPawnMethod, "AddPawn");

        /// <summary>Vehicle A, called by the caller AFTER our own confirm (the mod's bminus2 carries none).</summary>
        public static void RemovePawn(Window editorUI) => InvokeNoArg(editorUI, bpRemovePawnMethod, "RemovePawn");

        public static void ClonePawn(Window editorUI) => InvokeNoArg(editorUI, bpClonePawnMethod, "ClonePawn");

        public static void RandomizePawn(Window editorUI) => InvokeNoArg(editorUI, bpRandomizePawnMethod, "RandomizePawn");

        public static void RandomizeBio(Window editorUI) => InvokeNoArg(editorUI, bpRandomizeBioMethod, "RandomizeBio");

        /// <summary>Alt = force female, CapsLock = force male, None = any gender: the mod's own three branches.</summary>
        public static void RandomizePawnKeepRace(Window editorUI, EventModifiers modifiers)
        {
            if (!ActionsReady) return;
            object block = ActionsBlockPerson(editorUI);
            if (block != null)
                InvokeActionWithSimulatedModifiers(bpRandomizePawnKeepRaceMethod, block, modifiers, "RandomizePawnKeepRace");
        }

        /// <summary>Alt = female, CapsLock = male, None = random.</summary>
        public static void RandomizeBodyParts(Window editorUI, EventModifiers modifiers)
        {
            if (!ActionsReady) return;
            object block = ActionsBlockPerson(editorUI);
            if (block != null)
                InvokeActionWithSimulatedModifiers(bpRandomizeBodyPartsMethod, block, modifiers, "RandomizeBodyParts");
        }

        /// <summary>
        /// Alt = apparel only, Shift = weapons only, none = both. Recolor is not offered here: see
        /// <see cref="RecolorApparelOnly"/> for why Control-alone cannot isolate it.
        /// </summary>
        public static void RandomizeEquip(Window editorUI, EventModifiers modifiers)
        {
            if (!ActionsReady) return;
            object block = ActionsBlockPerson(editorUI);
            if (block != null)
                InvokeActionWithSimulatedModifiers(bpRandomizeEquipMethod, block, modifiers, "RandomizeEquip");
        }

        /// <summary>
        /// MUTATION-C: mirrors ONLY ARandomizeEquip's inner Ctrl-branch recolor loop — every worn
        /// Apparel takes a fresh <c>ColorTool.RandomAlphaColor</c>, then the mod's own
        /// <c>UpdateGraphics()</c> refreshes the portrait.
        /// </summary>
        public static void RecolorApparelOnly(Pawn pawn)
        {
            if (!ActionsReady || pawn?.apparel == null || pawn.apparel.WornApparelCount <= 0)
                return;
            try
            {
                object colorObj = colorToolRandomAlphaColorGetter.Invoke(null, null);
                if (!(colorObj is Color color))
                    return;
                foreach (Apparel item in pawn.apparel.WornApparel)
                {
                    item.DrawColor = color;
                }
                CallUpdateGraphics();
            }
            catch (Exception ex)
            {
                Fail("RecolorApparelOnly", ex);
            }
        }

        /// <summary>None = resurrect and heal, Alt = heal injuries and restore legs, Shift = medicate, Control = anaesthetize, CapsLock = damage until death.</summary>
        public static void QuickRestore(Window editorUI, EventModifiers modifiers)
        {
            if (!ActionsReady) return;
            object block = ActionsBlockPerson(editorUI);
            if (block != null)
                InvokeActionWithSimulatedModifiers(bpQuickResurrectMethod, block, modifiers, "QuickRestore");
        }

        /// <summary>World-gen only (the mod's own gate: <c>if (InStartingScreen)</c> around both buttons).</summary>
        public static void MoveUpInList(Window editorUI) => InvokeNoArg(editorUI, bpMoveUpMethod, "MoveUpInList");

        public static void MoveDownInList(Window editorUI) => InvokeNoArg(editorUI, bpMoveDownMethod, "MoveDownInList");

        // Presets subsection.

        public static int NumSlots
        {
            get
            {
                if (!ActionsReady) return 0;
                try
                {
                    object api = Api();
                    return api != null ? (int)apiNumSlotsGetter.Invoke(api, null) : 0;
                }
                catch (Exception ex)
                {
                    Fail("NumSlots", ex);
                    return 0;
                }
            }
        }

        /// <summary>CEditor.GetSlot: the raw stored slot string, empty when the slot holds nothing.</summary>
        public static string GetSlot(int index)
        {
            if (!ActionsReady) return "";
            try
            {
                object api = Api();
                return api != null ? apiGetSlotMethod.Invoke(api, new object[] { index }) as string ?? "" : "";
            }
            catch (Exception ex)
            {
                Fail("GetSlot", ex);
                return "";
            }
        }

        /// <summary>BlockPerson.SlotLabel: the mod's own formatted slot label, truncated exactly as its picker shows it.</summary>
        public static string SlotDisplayLabel(Window editorUI, int index, bool isSave)
        {
            if (!ActionsReady) return "";
            try
            {
                object block = ActionsBlockPerson(editorUI);
                return block != null ? bpSlotLabelMethod.Invoke(block, new object[] { index, isSave }) as string ?? "" : "";
            }
            catch (Exception ex)
            {
                Fail("SlotDisplayLabel", ex);
                return "";
            }
        }

        /// <summary>
        /// MUTATION-C (call-level mirror of ASavePawn's per-slot delegate). An empty slot saves
        /// directly; an occupied slot opens the mod's OWN overwrite-confirm dialog, which the mod's
        /// "always skip" option bypasses on its own.
        /// </summary>
        public static void SaveToSlot(Window editorUI, int index)
        {
            if (!ActionsReady) return;
            try
            {
                object block = ActionsBlockPerson(editorUI);
                object api = Api();
                if (block == null || api == null)
                    return;
                // MUTATION-C: mirrors ASavePawn's per-slot delegate setting its own
                // `iRemSlot = i2;` before branching (the field AConfirmSavePawn reads below).
                bpRemSlotField.SetValue(block, index);
                string existing = apiGetSlotMethod.Invoke(api, new object[] { index }) as string ?? "";
                if (existing.Length == 0)
                {
                    bpConfirmSavePawnMethod.Invoke(block, null);
                    return;
                }
                Action onConfirm = () => bpConfirmSavePawnMethod.Invoke(block, null);
                string title = labelOverwriteExistingField.GetValue(null) as string ?? "";
                messageToolShowCustomDialogMethod.Invoke(null, new object[] { "Data: " + existing, title, null, onConfirm, null });
            }
            catch (Exception ex)
            {
                Fail("SaveToSlot", ex);
            }
        }

        /// <summary>PresetPawn.LoadPawn: the mod's own plain-click load path.</summary>
        public static Pawn LoadFromSlot(int index)
        {
            if (!ActionsReady) return null;
            try
            {
                object preset = presetPawnCtor.Invoke(null);
                return presetPawnLoadPawnMethod.Invoke(preset, new object[] { index, true, "" }) as Pawn;
            }
            catch (Exception ex)
            {
                Fail("LoadFromSlot", ex);
                return null;
            }
        }

        /// <summary>
        /// PresetPawn.Load plus ShowPawnMods: the mod's own Alt-click peek path, loading the preset
        /// into a throwaway object without spawning anything and reporting its mod dependencies.
        /// Posts the mod's own MessageTool.Show for visual parity; the returned string lets the
        /// caller speak immediately rather than wait on the message-announcement patch.
        /// </summary>
        public static string PeekSlotMods(int index)
        {
            if (!ActionsReady) return "";
            try
            {
                object preset = presetPawnCtor.Invoke(null);
                presetPawnLoadMethod.Invoke(preset, new object[] { index, "" });
                string mods = presetPawnShowPawnModsMethod.Invoke(preset, null) as string ?? "";
                messageToolShowMethod.Invoke(null, new object[] { mods, null });
                return mods;
            }
            catch (Exception ex)
            {
                Fail("PeekSlotMods", ex);
                return "";
            }
        }

        /// <summary>CEditor.SetSlot: the SAME vehicle the mod's Ctrl-click clear uses, called after our own confirm, which its gesture lacks.</summary>
        public static void ClearSlot(Window editorUI, int index)
        {
            if (!ActionsReady) return;
            try
            {
                object api = Api();
                if (api != null)
                    apiSetSlotMethod.Invoke(api, new object[] { index, "", true });
            }
            catch (Exception ex)
            {
                Fail("ClearSlot", ex);
            }
        }

        // Utilities subsection.

        /// <summary>NameTool.SetPawnGender: the only stable seam, and it already carries the mod's head-regeneration-with-rollback logic.</summary>
        public static void SetGender(Pawn pawn, Gender gender)
        {
            if (!ActionsReady || pawn == null) return;
            try
            {
                nameToolSetPawnGenderMethod.Invoke(null, new object[] { pawn, gender });
            }
            catch (Exception ex)
            {
                Fail("SetGender", ex);
            }
        }

        public static void ToggleNude(Window editorUI) => InvokeDummyColor(editorUI, bpToggleNudeMethod, "ToggleNude");

        public static void ToggleHats(Window editorUI) => InvokeDummyColor(editorUI, bpToggleHatsMethod, "ToggleHats");

        /// <summary>BlockPerson.bShowClothes: true when clothes are worn, so the Nude row's checked state is its negation.</summary>
        public static bool ClothesShown(Window editorUI) => ReadBlockPersonBool(editorUI, bpShowClothesField, defaultValue: true, "ClothesShown");

        /// <summary>BlockPerson.bShowHat: true when head-layer apparel is worn.</summary>
        public static bool HatsShown(Window editorUI) => ReadBlockPersonBool(editorUI, bpShowHatField, defaultValue: true, "HatsShown");

        private static bool ReadBlockPersonBool(Window editorUI, FieldInfo field, bool defaultValue, string caller)
        {
            if (!ActionsReady || field == null)
                return defaultValue;
            try
            {
                object block = ActionsBlockPerson(editorUI);
                return block != null ? (bool)field.GetValue(block) : defaultValue;
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
                return defaultValue;
            }
        }

        /// <summary>
        /// Invokes the mod's own ARotate, then reads Capturer's private iCurrentRotation index
        /// (South, West, North, East — the mod's fixed cycle order) and returns the resulting facing
        /// through vanilla's Rot4.ToStringHuman().
        /// </summary>
        public static string RotatePortrait(Window editorUI)
        {
            if (!ActionsReady) return "";
            try
            {
                object block = ActionsBlockPerson(editorUI);
                if (block == null)
                    return "";
                bpRotateMethod.Invoke(block, new object[] { Color.white });

                object api = Api();
                if (api == null)
                    return "";
                object capturer = apiGetCapturerMethod.Invoke(api, new[] { eTypeCapturer });
                if (capturer == null)
                    return "";
                int index = (int)capturerRotationField.GetValue(capturer);
                Rot4 facing;
                switch (index)
                {
                    case 1:
                        facing = Rot4.West;
                        break;
                    case 2:
                        facing = Rot4.North;
                        break;
                    case 3:
                        facing = Rot4.East;
                        break;
                    default:
                        facing = Rot4.South;
                        break;
                }
                return facing.ToStringHuman();
            }
            catch (Exception ex)
            {
                Fail("RotatePortrait", ex);
                return "";
            }
        }

        public static void JumpToPawn(Window editorUI) => InvokeNoArg(editorUI, bpJumpToPawnMethod, "JumpToPawn");

        private static void CallUpdateGraphics()
        {
            EditorCore.UpdateGraphics();
        }
    }
}
