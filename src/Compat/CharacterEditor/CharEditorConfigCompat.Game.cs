using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.CEditor+ModOptions+DialogConfigurate</c> and its
    /// enclosing <c>ModOptions</c>, backing <see cref="Shell.CharEditorConfigScope"/>. Presence via
    /// <see cref="CharEditorCompat.ModPresent"/>, every member resolved once behind
    /// <see cref="Ready"/>, degrade-not-throw.
    ///
    /// <c>DialogConfigurate</c> is a private class three levels of nesting deep, walked here from
    /// <see cref="CharEditorCompat.EditorCore.CEditorType"/>. dicBool/dicInt/dicString/dicSlots are
    /// plain private FIELDS on <c>ModOptions</c>, each a dictionary whose value type exposes public
    /// Title/Descr/Value/Default, read reflectively once per row build.
    ///
    /// OptionI.STACKLIMIT is in the dictionary but <c>DrawNumeric</c> continues past it, so it is
    /// never drawn by the mod and is excluded here too.
    ///
    /// Numeric steppers ride <c>APlusInt</c>/<c>AMinusInt</c> verbatim, unclamped like the mod's own
    /// buttons; exact entry, checkboxes and the string rows are hand-copied because each write is
    /// inlined in the draw method with no setter to invoke. <c>AConfirmDelete</c> opens the mod's OWN
    /// confirm before running <c>ADeleteSlots</c> — ride it whole, never call <c>ADeleteSlots</c>
    /// directly. <c>AResetAll</c> has no confirm despite being fully destructive, so the scope wraps
    /// it in ours. <c>ADeleteCustoms</c> is dead code and deliberately not surfaced: dead code must
    /// not become actionable. The hotkey rows read their binding through plain vanilla
    /// <c>KeyBindingDef</c>/<c>KeyPrefs</c>, and <c>ChangeHotkey</c> opens vanilla
    /// <c>Dialog_DefineBinding</c>, which the shell's generic reader attaches to unaided.
    /// </summary>
    internal static class CharEditorConfigCompat
    {
        private const BindingFlags NestedFlags = BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorConfigCompat", Resolve);

        private static Type modOptionsType;
        private static Type dialogType;
        private static Type bDataType;
        private static Type iDataType;
        private static Type sDataType;
        private static Type optionBType;
        private static Type optionIType;
        private static Type optionSType;

        private static FieldInfo moField;
        private static FieldInfo dicBoolField;
        private static FieldInfo dicIntField;
        private static FieldInfo dicStringField;
        private static FieldInfo dicSlotsField;

        private static FieldInfo bTitleField, bDescrField, bDefaultField, bValueField;
        private static FieldInfo iTitleField, iDescrField, iDefaultField, iValueField;
        private static FieldInfo sTitleField, sDescrField, sDefaultField, sValueField;

        private static MethodInfo aResetAllMethod;
        private static MethodInfo aConfirmDeleteMethod;
        private static MethodInfo aExportSlotsMethod;
        private static MethodInfo aImportSlotsMethod;
        private static MethodInfo aPlusIntMethod;
        private static MethodInfo aMinusIntMethod;
        private static MethodInfo doAndCloseMethod;
        private static MethodInfo changeHotkeyMethod;
        private static MethodInfo removeHotkeyMethod;
        private static MethodInfo boolChangedMethod;
        private static MethodInfo setSlotMethod;
        private static MethodInfo onSettingsChangedMethod;

        private static FieldInfo labelHotkeyEditorField;
        private static FieldInfo labelHotkeyTeleportField;
        private static FieldInfo labelPawnSlotFormatField;
        private static FieldInfo labelToDefaultsField;
        private static FieldInfo labelDeleteSlotsField;
        private static FieldInfo labelExportField;
        private static FieldInfo labelImportField;
        private static FieldInfo labelAllSlotsWillBeClearedField;

        public static bool ModPresent
        {
            get { gate.Ensure(); return CharEditorCompat.ModPresent; }
        }

        public static bool Ready
        {
            get { return gate.Ensure(); }
        }

        /// <summary>The private nested DialogConfigurate window type, for the ScopeForWindow registration.</summary>
        public static Type DialogType
        {
            get { gate.Ensure(); return dialogType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!CharEditorCompat.ModPresent) return false;

            Type ceditorType = surface.Supplied("CharacterEditor.CEditor", CharEditorCompat.EditorCore.CEditorType);
            modOptionsType = surface.Supplied("CEditor.ModOptions", ceditorType?.GetNestedType("ModOptions", NestedFlags));
            dialogType = surface.Supplied("ModOptions.DialogConfigurate", modOptionsType?.GetNestedType("DialogConfigurate", NestedFlags));
            bDataType = surface.Supplied("ModOptions.BData", modOptionsType?.GetNestedType("BData", NestedFlags));
            iDataType = surface.Supplied("ModOptions.IData", modOptionsType?.GetNestedType("IData", NestedFlags));
            sDataType = surface.Supplied("ModOptions.SData", modOptionsType?.GetNestedType("SData", NestedFlags));
            optionBType = surface.Type("CharacterEditor.OptionB");
            optionIType = surface.Type("CharacterEditor.OptionI");
            optionSType = surface.Type("CharacterEditor.OptionS");

            if (modOptionsType == null || dialogType == null || bDataType == null || iDataType == null
                || sDataType == null || optionBType == null || optionIType == null || optionSType == null
                || CharEditorCompat.Labels.LabelType == null)
            {
                return surface.Ready && CharEditorCompat.Labels.Ready;
            }

            moField = surface.Field(dialogType, "mo");
            dicBoolField = surface.Field(modOptionsType, "dicBool");
            dicIntField = surface.Field(modOptionsType, "dicInt");
            dicStringField = surface.Field(modOptionsType, "dicString");
            dicSlotsField = surface.Field(modOptionsType, "dicSlots");

            bTitleField = surface.Field(bDataType, "Title");
            bDescrField = surface.Field(bDataType, "Descr");
            bDefaultField = surface.Field(bDataType, "Default");
            bValueField = surface.Field(bDataType, "Value");
            iTitleField = surface.Field(iDataType, "Title");
            iDescrField = surface.Field(iDataType, "Descr");
            iDefaultField = surface.Field(iDataType, "Default");
            iValueField = surface.Field(iDataType, "Value");
            sTitleField = surface.Field(sDataType, "Title");
            sDescrField = surface.Field(sDataType, "Descr");
            sDefaultField = surface.Field(sDataType, "Default");
            sValueField = surface.Field(sDataType, "Value");

            aResetAllMethod = surface.Method(dialogType, "AResetAll", Type.EmptyTypes);
            aConfirmDeleteMethod = surface.Method(dialogType, "AConfirmDelete", Type.EmptyTypes);
            aExportSlotsMethod = surface.Method(dialogType, "AExportSlots", Type.EmptyTypes);
            aImportSlotsMethod = surface.Method(dialogType, "AImportSlots", Type.EmptyTypes);
            aPlusIntMethod = surface.Method(dialogType, "APlusInt", new[] { typeof(int) });
            aMinusIntMethod = surface.Method(dialogType, "AMinusInt", new[] { typeof(int) });
            doAndCloseMethod = surface.Method(dialogType, "DoAndClose", Type.EmptyTypes);
            changeHotkeyMethod = surface.Method(dialogType, "ChangeHotkey", new[] { typeof(string) });
            removeHotkeyMethod = surface.Method(dialogType, "RemoveHotkey", new[] { typeof(string), optionSType });
            boolChangedMethod = surface.Method(dialogType, "BoolChanged", new[] { optionBType });
            setSlotMethod = surface.Method(modOptionsType, "SetSlot", new[] { typeof(int), typeof(string), typeof(bool) });
            onSettingsChangedMethod = surface.Method(ceditorType, "OnSettingsChanged", new[] { typeof(bool), typeof(bool) });

            Type labelType = CharEditorCompat.Labels.LabelType;
            labelHotkeyEditorField = surface.Field(labelType, "O_HOTKEYEDITOR");
            labelHotkeyTeleportField = surface.Field(labelType, "O_HOTKEYTELEPORT");
            labelPawnSlotFormatField = surface.Field(labelType, "O_PAWNSLOT");
            labelToDefaultsField = surface.Field(labelType, "TODEFAULTS");
            labelDeleteSlotsField = surface.Field(labelType, "DELETE_SLOTS");
            labelExportField = surface.Field(labelType, "EXPORT");
            labelImportField = surface.Field(labelType, "IMPORT");
            labelAllSlotsWillBeClearedField = surface.Field(labelType, "ALLSLOTSWILLBECLEARED");

            // The hotkey descriptions fall back to empty strings, so a miss must not fail here.

            return surface.Ready && CharEditorCompat.Labels.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();
        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorConfigCompat." + member + " failed: " + ex.Message);
        }

        private static object Mo(Window dialog) => moField.GetValue(dialog);
        private static IDictionary Dict(Window dialog, FieldInfo field) => (IDictionary)field.GetValue(Mo(dialog));

        // Booleans: DrawBoolean's dicBool.Keys.

        public static List<object> BoolKeys(Window dialog)
        {
            var result = new List<object>();
            if (!Ready) return result;
            try { foreach (object k in Dict(dialog, dicBoolField).Keys) result.Add(k); }
            catch (Exception ex) { Fail("BoolKeys", ex); }
            return result;
        }

        public static string BoolTitle(Window dialog, object key) => DataField(dialog, dicBoolField, key, bTitleField) as string ?? "";
        public static string BoolDescr(Window dialog, object key) => DataField(dialog, dicBoolField, key, bDescrField) as string ?? "";
        public static bool BoolValue(Window dialog, object key) => (bool)(DataField(dialog, dicBoolField, key, bValueField) ?? false);

        /// <summary>
        /// MUTATION-C: mirrors <c>ModOptions.DrawBoolean</c>'s inline write (CEditor.cs:272-280) --
        /// the checkbox's new state is written straight to <c>dicBool[key].Value</c> with no
        /// separate setter, then <c>BoolChanged(key)</c> runs (the SAME two-step sequence the
        /// mod's own draw loop performs; no vanilla widget/delegate exists to invoke instead since
        /// the write is inlined in the draw method itself, not delegated to a named handler).
        /// </summary>
        public static void SetBool(Window dialog, object key, bool value)
        {
            if (!Ready) return;
            try
            {
                object data = Dict(dialog, dicBoolField)[key];
                // MUTATION-C: mirrors DrawBoolean's own inline write (CEditor.cs:272-280).
                bValueField.SetValue(data, value);
                boolChangedMethod.Invoke(dialog, new[] { key });
            }
            catch (Exception ex) { Fail("SetBool", ex); }
        }

        // Numerics: RESOLUTION, NUMCAPSULESETS and NUMPAWNSLOTS are drawn; VERSION is a dev-only
        // read-only label and STACKLIMIT is never drawn.

        public static List<object> IntKeys(Window dialog)
        {
            var result = new List<object>();
            if (!Ready) return result;
            try
            {
                foreach (object k in Dict(dialog, dicIntField).Keys)
                {
                    if (k.ToString() != "STACKLIMIT") result.Add(k);
                }
            }
            catch (Exception ex) { Fail("IntKeys", ex); }
            return result;
        }

        public static string IntTitle(Window dialog, object key) => DataField(dialog, dicIntField, key, iTitleField) as string ?? "";
        public static string IntDescr(Window dialog, object key) => DataField(dialog, dicIntField, key, iDescrField) as string ?? "";
        public static int IntValue(Window dialog, object key) => (int)(DataField(dialog, dicIntField, key, iValueField) ?? 0);

        /// <summary>The mod's own hardcoded per-row cap: RESOLUTION 1600, else 400.</summary>
        public static int IntMax(object key) => key.ToString() == "RESOLUTION" ? 1600 : 400;

        /// <summary>The exact delegate the row's plus button invokes; unclamped, like that button.</summary>
        public static void IntStepPlus(Window dialog, object key)
        {
            if (!Ready) return;
            try { aPlusIntMethod.Invoke(dialog, new object[] { (int)key }); }
            catch (Exception ex) { Fail("IntStepPlus", ex); }
        }

        /// <summary>The exact delegate the row's minus button invokes; unclamped, like that button.</summary>
        public static void IntStepMinus(Window dialog, object key)
        {
            if (!Ready) return;
            try { aMinusIntMethod.Invoke(dialog, new object[] { (int)key }); }
            catch (Exception ex) { Fail("IntStepMinus", ex); }
        }

        /// <summary>
        /// MUTATION-C: mirrors the exact-entry field's own write (CEditor.cs:322,
        /// <c>SZWidgets.NumericTextField</c> clamped 1..<see cref="IntMax"/> then assigned straight
        /// to <c>dicInt[key].Value</c> -- no setter method exists; the mod's OWN field performs the
        /// identical raw write after its own identical clamp).
        /// </summary>
        public static void SetIntExact(Window dialog, object key, int value)
        {
            if (!Ready) return;
            try
            {
                int clamped = Math.Max(1, Math.Min(IntMax(key), value));
                object data = Dict(dialog, dicIntField)[key];
                // MUTATION-C: mirrors DrawNumeric's own clamped raw write (CEditor.cs:322).
                iValueField.SetValue(data, clamped);
                if (key.ToString() == "RESOLUTION")
                {
                    // CEditor.cs:327-330: a changed RESOLUTION also live-applies the render size.
                    RunOnSettingsChanged(dialog, updateRender: true);
                }
            }
            catch (Exception ex) { Fail("SetIntExact", ex); }
        }

        private static void RunOnSettingsChanged(Window dialog, bool updateRender)
        {
            try
            {
                object api = CharEditorCompat.EditorCore.Api();
                if (api == null) return;
                onSettingsChangedMethod.Invoke(api, new object[] { updateRender, false });
            }
            catch (Exception ex) { Fail("RunOnSettingsChanged", ex); }
        }

        // Pawn-slot strings and the three serialized-modification strings; the two hotkey option
        // strings are excluded, they belong to the hotkey rows below.

        public static List<int> SlotKeys(Window dialog)
        {
            var result = new List<int>();
            if (!Ready) return result;
            try { foreach (object k in Dict(dialog, dicSlotsField).Keys) result.Add((int)k); }
            catch (Exception ex) { Fail("SlotKeys", ex); }
            return result;
        }

        public static string SlotValue(Window dialog, int index)
        {
            if (!Ready) return "";
            try { return Dict(dialog, dicSlotsField)[index] as string ?? ""; }
            catch (Exception ex) { Fail("SlotValue", ex); return ""; }
        }

        public static string SlotLabel(int index) =>
            string.Format((labelPawnSlotFormatField.GetValue(null) as string) ?? "Slot {0}", index.ToString());

        /// <summary>The exact call DrawStrings makes for a changed slot row; persistence is Save-and-close's job.</summary>
        public static void SetSlotText(Window dialog, int index, string value)
        {
            if (!Ready) return;
            try { setSlotMethod.Invoke(Mo(dialog), new object[] { index, value, false }); }
            catch (Exception ex) { Fail("SetSlotText", ex); }
        }

        private static readonly string[] CustomStringKeys = { "CUSTOMGENE", "CUSTOMOBJECT", "CUSTOMLIFESTAGE" };

        public static List<object> CustomStringKeysList(Window dialog)
        {
            var result = new List<object>();
            if (!Ready) return result;
            try
            {
                IDictionary dict = Dict(dialog, dicStringField);
                foreach (object k in dict.Keys)
                {
                    string name = k.ToString();
                    foreach (string wanted in CustomStringKeys)
                    {
                        if (name == wanted) { result.Add(k); break; }
                    }
                }
            }
            catch (Exception ex) { Fail("CustomStringKeysList", ex); }
            return result;
        }

        public static string StringTitle(Window dialog, object key) => DataField(dialog, dicStringField, key, sTitleField) as string ?? "";
        public static string StringDescr(Window dialog, object key) => DataField(dialog, dicStringField, key, sDescrField) as string ?? "";
        public static string StringValue(Window dialog, object key) => DataField(dialog, dicStringField, key, sValueField) as string ?? "";

        /// <summary>
        /// MUTATION-C: mirrors <c>DrawStrings</c>' inline write for CUSTOMGENE/CUSTOMOBJECT/
        /// CUSTOMLIFESTAGE (CEditor.cs:352-357) -- a bare <c>dicString[key].Value = ...</c>
        /// assignment; the mod's own text field performs the identical raw write, no setter method
        /// exists (unlike the pawn-slot rows, which route through <c>SetSlot</c>).
        /// </summary>
        public static void SetCustomString(Window dialog, object key, string value)
        {
            if (!Ready) return;
            try
            {
                object data = Dict(dialog, dicStringField)[key];
                // MUTATION-C: mirrors DrawStrings' own inline write (CEditor.cs:352-357).
                sValueField.SetValue(data, value);
            }
            catch (Exception ex) { Fail("SetCustomString", ex); }
        }

        private static object DataField(Window dialog, FieldInfo dictField, object key, FieldInfo dataField)
        {
            if (!Ready) return null;
            try
            {
                object data = Dict(dialog, dictField)[key];
                return data != null ? dataField.GetValue(data) : null;
            }
            catch (Exception ex) { Fail("DataField", ex); return null; }
        }

        // Hotkeys. Reads ride plain vanilla KeyBindingDef/KeyPrefs; the two mutators ride the mod's
        // own private methods.

        public static string EditorHotkeyLabel => (labelHotkeyEditorField?.GetValue(null) as string) ?? "";
        public static string EditorHotkeyDescr => CharEditorCompat.Labels.Get("O_DESC_HOTKEYEDITOR");
        public static string TeleportHotkeyLabel => (labelHotkeyTeleportField?.GetValue(null) as string) ?? "";
        public static string TeleportHotkeyDescr => CharEditorCompat.Labels.Get("O_DESC_HOTKEYTELEPORT");

        public static KeyBindingDef HotkeyDef(string defName) => DefDatabase<KeyBindingDef>.GetNamedSilentFail(defName);

        public static string HotkeyBoundLabel(string defName)
        {
            KeyBindingDef def = HotkeyDef(defName);
            if (def == null) return Label_None;
            KeyCode code = KeyPrefs.KeyPrefsData.GetBoundKeyCode(def, KeyPrefs.BindingSlot.A);
            return code.ToStringReadable();
        }

        private static string Label_None => "NoneLower".CanTranslate() ? "NoneLower".Translate().ToString() : "None";

        /// <summary>Opens vanilla Dialog_DefineBinding exactly as the mod's own button does.</summary>
        public static void ChangeHotkey(Window dialog, string defName)
        {
            if (!Ready) return;
            try { changeHotkeyMethod.Invoke(dialog, new object[] { defName }); }
            catch (Exception ex) { Fail("ChangeHotkey", ex); }
        }

        /// <summary>The row's own delete-icon delegate: unbinds via KeyPrefs, then writes the mirrored option string.</summary>
        public static void RemoveHotkey(Window dialog, string defName, object optionSKey)
        {
            if (!Ready) return;
            try { removeHotkeyMethod.Invoke(dialog, new[] { defName, optionSKey }); }
            catch (Exception ex) { Fail("RemoveHotkey", ex); }
        }

        public static object OptionS_HotkeyEditor => Enum.Parse(optionSType, "HOTKEYEDITOR");
        public static object OptionS_HotkeyTeleport => Enum.Parse(optionSType, "HOTKEYTELEPORT");


        public static string ResetAllLabel => (labelToDefaultsField?.GetValue(null) as string) ?? "";
        public static string DeleteSlotsLabel => (labelDeleteSlotsField?.GetValue(null) as string) ?? "";
        public static string ExportLabel => (labelExportField?.GetValue(null) as string) ?? "";
        public static string ImportLabel => (labelImportField?.GetValue(null) as string) ?? "";
        public static string ConfirmDeleteAllMessage => (labelAllSlotsWillBeClearedField?.GetValue(null) as string) ?? "";

        /// <summary>The mod offers no confirm here, so the scope wraps this in its own before calling.</summary>
        public static void ResetAllDefaults(Window dialog)
        {
            if (!Ready) return;
            try { aResetAllMethod.Invoke(dialog, null); }
            catch (Exception ex) { Fail("ResetAllDefaults", ex); }
        }

        /// <summary>Rides the mod's OWN confirm dialog; never call ADeleteSlots directly.</summary>
        public static void DeleteAllSlotsWithConfirm(Window dialog)
        {
            if (!Ready) return;
            try { aConfirmDeleteMethod.Invoke(dialog, null); }
            catch (Exception ex) { Fail("DeleteAllSlotsWithConfirm", ex); }
        }

        public static void ExportSlots(Window dialog)
        {
            if (!Ready) return;
            try { aExportSlotsMethod.Invoke(dialog, null); }
            catch (Exception ex) { Fail("ExportSlots", ex); }
        }

        public static void ImportSlots(Window dialog)
        {
            if (!Ready) return;
            try { aImportSlotsMethod.Invoke(dialog, null); }
            catch (Exception ex) { Fail("ImportSlots", ex); }
        }

        /// <summary>The Save button's own delegate — the ONLY path that persists the option files to disk.</summary>
        public static void SaveAndClose(Window dialog)
        {
            if (!Ready) return;
            try { doAndCloseMethod.Invoke(dialog, null); }
            catch (Exception ex) { Fail("SaveAndClose", ex); }
        }
    }
}
