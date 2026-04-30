using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class PawnFilterPresetSaveState
    {
        public static bool IsActive { get; private set; }

        private static PawnFilter filterToSave;
        private static List<string> existingPresets = new List<string>();
        private static int selectedIndex = 0;
        private static bool isTypingName = true;
        private static readonly TextInputController nameController = new TextInputController();
        private static readonly TextFieldSpec nameSpec = new TextFieldSpec(
            labelKey: "RimWorldAccess.TextInput.LabelFilename",
            maxLength: 64,
            minLength: 1,
            mustBeFilename: true);

        public static void Open(PawnFilter filter)
        {
            filterToSave = filter;
            // Embedded controller — Up/Down arrows must reach the surrounding list.
            nameController.Begin("MyPreset", nameSpec, _ => { }, null, replaceOnType: true, modal: false);

            ReloadPresets();

            selectedIndex = 0;
            isTypingName = true;
            IsActive = true;

            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.OpenInstructions".Translate(nameController.CurrentText));
        }

        public static void Close()
        {
            IsActive = false;
            filterToSave = null;
            existingPresets.Clear();
            nameController.Cancel();
        }

        private static void ReloadPresets()
        {
            existingPresets = PawnFilterPresetSerializer.GetPresetNames();
        }

        private static int TotalCount => existingPresets.Count + 1;

        private static void AnnounceCurrentState()
        {
            if (selectedIndex == 0)
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.SaveAs".Translate(nameController.CurrentText, MenuHelper.FormatPosition(0, TotalCount)));
            }
            else if (selectedIndex > 0 && selectedIndex <= existingPresets.Count)
            {
                string presetName = existingPresets[selectedIndex - 1];
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.Overwrite".Translate(presetName, MenuHelper.FormatPosition(selectedIndex, TotalCount)));
            }
        }

        #region Navigation

        private static void SelectNext()
        {
            isTypingName = false;

            if (selectedIndex < existingPresets.Count)
            {
                selectedIndex++;
                AnnounceCurrentState();
            }
        }

        private static void SelectPrevious()
        {
            if (selectedIndex > 0)
            {
                selectedIndex--;
                if (selectedIndex == 0)
                    isTypingName = true;
                AnnounceCurrentState();
            }
        }

        private static void JumpToFirst()
        {
            selectedIndex = 0;
            isTypingName = true;
            AnnounceCurrentState();
        }

        private static void JumpToLast()
        {
            selectedIndex = existingPresets.Count;
            isTypingName = false;
            AnnounceCurrentState();
        }

        #endregion

        #region Actions

        private static void SaveSelected()
        {
            if (selectedIndex == 0)
            {
                string name = nameController.CurrentText;
                if (string.IsNullOrWhiteSpace(name))
                {
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.NameEmpty".Translate());
                    return;
                }

                try
                {
                    // Check for existing preset with same name
                    int existingIndex = existingPresets.FindIndex(n =>
                        string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                    {
                        PawnFilterPresetSerializer.OverwritePreset(filterToSave, name, existingIndex);
                        Close();
                        TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.PresetOverwritten".Translate(name));
                    }
                    else
                    {
                        PawnFilterPresetSerializer.SavePreset(filterToSave, name);
                        Close();
                        TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.PresetSavedAs".Translate(name));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error saving preset: {ex}");
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.ErrorSaving".Translate(ex.Message));
                }
            }
            else if (selectedIndex > 0 && selectedIndex <= existingPresets.Count)
            {
                int presetIndex = selectedIndex - 1;
                string name = existingPresets[presetIndex];

                try
                {
                    PawnFilterPresetSerializer.OverwritePreset(filterToSave, name, presetIndex);
                    Close();
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.PresetOverwritten".Translate(name));
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error saving preset: {ex}");
                    TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.ErrorSaving".Translate(ex.Message));
                }
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetSave.InvalidSelection".Translate());
            }
        }

        #endregion

        #region Input Handling

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

            // Cursor review: Left/Right (with Shift/Ctrl) let the user audit the preset name
            // while they're actually typing. When browsing the existing-preset list
            // (isTypingName == false), arrows aren't used by the state either, so we leave
            // them untouched rather than steal them for a hidden name cursor move.
            if (isTypingName && selectedIndex == 0)
            {
                if (key == KeyCode.LeftArrow)
                {
                    nameController.HandleArrowLeft(shift, ctrl);
                    return true;
                }
                if (key == KeyCode.RightArrow)
                {
                    nameController.HandleArrowRight(shift, ctrl);
                    return true;
                }
            }

            switch (key)
            {
                case KeyCode.UpArrow:
                    SelectPrevious();
                    return true;

                case KeyCode.DownArrow:
                    SelectNext();
                    return true;

                case KeyCode.Home:
                    JumpToFirst();
                    return true;

                case KeyCode.End:
                    JumpToLast();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    SaveSelected();
                    return true;

                case KeyCode.Escape:
                    Close();
                    TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Translate());
                    return true;

                case KeyCode.Backspace:
                    if (isTypingName && selectedIndex == 0)
                    {
                        nameController.HandleBackspace();
                        return true;
                    }
                    break;
                case KeyCode.C:
                    if (ctrl && isTypingName && selectedIndex == 0)
                    {
                        nameController.HandleCopy();
                        return true;
                    }
                    break;
                case KeyCode.V:
                    if (ctrl && isTypingName && selectedIndex == 0)
                    {
                        nameController.HandlePaste();
                        return true;
                    }
                    break;
            }

            return false;
        }

        public static bool HandleCharacterInput(char character)
        {
            if (!IsActive) return false;

            if (selectedIndex == 0 && isTypingName)
            {
                if (char.IsLetterOrDigit(character) || character == ' ' || character == '-' || character == '_')
                {
                    nameController.HandleCharacter(character);
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
