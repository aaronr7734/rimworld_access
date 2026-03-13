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

        public static void Open(PawnFilter filter)
        {
            filterToSave = filter;

            TextInputHelper.SetText("MyPreset");

            ReloadPresets();

            selectedIndex = 0;
            isTypingName = true;
            IsActive = true;

            TolkHelper.Speak($"Save filter preset. Type name or press Down to select existing preset to overwrite. Current name: {TextInputHelper.CurrentText}");
        }

        public static void Close()
        {
            IsActive = false;
            filterToSave = null;
            existingPresets.Clear();
            TextInputHelper.Clear();
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
                TolkHelper.Speak($"Save as: {TextInputHelper.CurrentText} ({MenuHelper.FormatPosition(0, TotalCount)})");
            }
            else if (selectedIndex > 0 && selectedIndex <= existingPresets.Count)
            {
                string presetName = existingPresets[selectedIndex - 1];
                TolkHelper.Speak($"Overwrite: {presetName} ({MenuHelper.FormatPosition(selectedIndex, TotalCount)})");
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
                string name = TextInputHelper.CurrentText;
                if (string.IsNullOrWhiteSpace(name))
                {
                    TolkHelper.Speak("Name cannot be empty");
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
                        TolkHelper.Speak($"Preset {name} overwritten");
                    }
                    else
                    {
                        PawnFilterPresetSerializer.SavePreset(filterToSave, name);
                        Close();
                        TolkHelper.Speak($"Preset saved as {name}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error saving preset: {ex}");
                    TolkHelper.Speak($"Error saving: {ex.Message}");
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
                    TolkHelper.Speak($"Preset {name} overwritten");
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimWorld Access] Error saving preset: {ex}");
                    TolkHelper.Speak($"Error saving: {ex.Message}");
                }
            }
            else
            {
                TolkHelper.Speak("Invalid selection");
            }
        }

        #endregion

        #region Input Handling

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!IsActive) return false;

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
                    TolkHelper.Speak("Cancelled");
                    return true;

                case KeyCode.Backspace:
                    if (isTypingName && selectedIndex == 0)
                    {
                        TextInputHelper.HandleBackspace();
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
                    TextInputHelper.HandleCharacter(character);
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
