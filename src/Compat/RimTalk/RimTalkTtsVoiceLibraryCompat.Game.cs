using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for <c>RimTalk.TTS.UI.VoiceLibraryWindow</c> and its private nested
    /// <c>VoiceEntry</c> class: resolves the window type, its private <c>searchText</c>/
    /// <c>selectedLanguage</c> instance fields, the static <c>allVoices</c> field, and VoiceEntry's
    /// six public fields once behind <see cref="Ready"/> (missing type or member = the TTS addon
    /// not loaded, or its shipped DLL drifted -- silent decline).
    /// </summary>
    internal static class RimTalkTtsVoiceLibraryCompat
    {
        internal struct VoiceEntry
        {
            public string Name;
            public string Gender;
            public string LanguageDisplay;
            public string Personality;
            public string Category;
        }

        private static readonly Type dialogType;
        private static readonly FieldInfo searchTextField;
        private static readonly FieldInfo selectedLanguageField;
        private static readonly FieldInfo allVoicesField;
        private static readonly Type voiceEntryType;
        private static readonly FieldInfo nameField;
        private static readonly FieldInfo genderField;
        private static readonly FieldInfo languageDisplayField;
        private static readonly FieldInfo personalityField;
        private static readonly FieldInfo categoryField;
        private static readonly bool ready;

        public static bool Ready => ready;

        static RimTalkTtsVoiceLibraryCompat()
        {
            var surface = new ReflectionSurface("RimTalk TTS compat (voice library)");

            dialogType = surface.Type("RimTalk.TTS.UI.VoiceLibraryWindow");
            searchTextField = surface.Field(dialogType, "searchText");
            selectedLanguageField = surface.Field(dialogType, "selectedLanguage");
            allVoicesField = surface.Field(dialogType, "allVoices");

            // VoiceEntry is a private nested type, so it resolves off the window rather than by
            // full name; its absence surfaces through the five fields that then fail to resolve.
            voiceEntryType = dialogType != null ? AccessTools.Inner(dialogType, "VoiceEntry") : null;
            nameField = surface.Field(voiceEntryType, "Name");
            genderField = surface.Field(voiceEntryType, "Gender");
            languageDisplayField = surface.Field(voiceEntryType, "LanguageDisplay");
            personalityField = surface.Field(voiceEntryType, "Personality");
            categoryField = surface.Field(voiceEntryType, "Category");

            ready = surface.Ready && voiceEntryType != null;
        }

        internal static string GetSearchText(Window w)
        {
            return searchTextField != null ? searchTextField.GetValue(w) as string : null;
        }

        internal static void SetSearchText(Window w, string value)
        {
            if (searchTextField != null)
            {
                // MUTATION-C: mirrors DoWindowContents' own return-assign verbatim.
                searchTextField.SetValue(w, value ?? "");
            }
        }

        internal static string GetSelectedLanguage(Window w)
        {
            return selectedLanguageField != null ? selectedLanguageField.GetValue(w) as string : null;
        }

        internal static IList GetAllVoices()
        {
            return allVoicesField != null ? allVoicesField.GetValue(null) as IList : null;
        }

        internal static VoiceEntry ReadEntry(object entry)
        {
            return new VoiceEntry
            {
                Name = nameField.GetValue(entry) as string,
                Gender = genderField.GetValue(entry) as string,
                LanguageDisplay = languageDisplayField.GetValue(entry) as string,
                Personality = personalityField.GetValue(entry) as string,
                Category = categoryField.GetValue(entry) as string,
            };
        }

        public static void Register()
        {
            if (!ready)
            {
                return;
            }

            ScopeForWindow.Register(dialogType, delegate (Window w)
            {
                return new RimTalkTtsVoiceLibraryScope(w);
            });
        }
    }
}
