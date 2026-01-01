using HarmonyLib;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Page_ChooseIdeoPreset to add keyboard accessibility.
    /// Only applies when Ideology DLC is active.
    /// </summary>
    [HarmonyPatch]
    public static class IdeologySelectionPatch
    {
        private static bool patchActive = false;
        private static bool hasAnnouncedTitle = false;

        /// <summary>
        /// Only patch if Ideology DLC is active and the class exists.
        /// </summary>
        static bool Prepare()
        {
            return ModsConfig.IdeologyActive;
        }

        /// <summary>
        /// Target the DoWindowContents method.
        /// </summary>
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.Page_ChooseIdeoPreset");
            if (type == null)
            {
                Log.Warning("[RimWorld Access] Page_ChooseIdeoPreset not found - Ideology DLC may not be installed");
                return null;
            }
            return AccessTools.Method(type, "DoWindowContents");
        }

        /// <summary>
        /// Prefix: Initialize state and handle keyboard input.
        /// IMPORTANT: Parameter must be named "inRect" to match the original method signature.
        /// </summary>
        static void Prefix(object __instance, Rect inRect)
        {
            try
            {
                // Initialize navigation state
                IdeologyNavigationState.Initialize();

                // Announce window title and initial selection once
                if (!hasAnnouncedTitle)
                {
                    string pageTitle = "Choose Your Ideoligion";
                    string instructions = "Use Up/Down arrows to navigate, Tab to browse presets, Enter to confirm";
                    TolkHelper.Speak($"{pageTitle}. {instructions}");

                    // Announce the first option after a brief delay
                    hasAnnouncedTitle = true;

                    // Announce initial selection
                    System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                    {
                        string description = IdeoPresetCategoryDefOf.Classic.description;
                        TolkHelper.Speak($"Play Classic - {description}");
                    });
                }

                // Handle keyboard input
                if (Event.current.type == EventType.KeyDown)
                {
                    KeyCode keyCode = Event.current.keyCode;

                    if (keyCode == KeyCode.Tab)
                    {
                        // Toggle between main options and preset browsing
                        IdeologyNavigationState.TogglePresetBrowsing();
                        UpdatePageSelection(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.UpArrow)
                    {
                        IdeologyNavigationState.NavigateUp();
                        UpdatePageSelection(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                    else if (keyCode == KeyCode.DownArrow)
                    {
                        IdeologyNavigationState.NavigateDown();
                        UpdatePageSelection(__instance);
                        Event.current.Use();
                        patchActive = true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeologySelectionPatch Prefix: {ex}");
            }
        }

        /// <summary>
        /// Postfix: Draw visual indicator of keyboard mode.
        /// </summary>
        static void Postfix(object __instance, Rect inRect)
        {
            try
            {
                if (!patchActive) return;

                // Draw a simple indicator at the top
                Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, 300f, 30f);
                string modeText = "[Keyboard Navigation Active]";

                // Draw semi-transparent background
                Widgets.DrawBoxSolid(indicatorRect, new Color(0.2f, 0.2f, 0.2f, 0.8f));

                // Draw text
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(indicatorRect, modeText);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in IdeologySelectionPatch Postfix: {ex}");
            }
        }

        /// <summary>
        /// Updates the page's internal selection state using reflection.
        /// </summary>
        private static void UpdatePageSelection(object instance)
        {
            try
            {
                var instanceType = instance.GetType();

                // Get the private fields
                FieldInfo presetSelectionField = AccessTools.Field(instanceType, "presetSelection");
                FieldInfo selectedIdeoField = AccessTools.Field(instanceType, "selectedIdeo");

                if (presetSelectionField == null || selectedIdeoField == null)
                {
                    Log.Warning("[RimWorld Access] Could not find required fields in Page_ChooseIdeoPreset");
                    return;
                }

                // Get the selection type
                string selectionType = IdeologyNavigationState.GetCurrentSelectionTypeString();

                // Get the enum type and parse the value
                System.Type presetSelectionEnumType = presetSelectionField.FieldType;
                object presetSelectionValue = System.Enum.Parse(presetSelectionEnumType, selectionType);

                // Update the page's presetSelection field
                presetSelectionField.SetValue(instance, presetSelectionValue);

                // Update the selectedIdeo field
                if (selectionType == "Preset")
                {
                    IdeoPresetDef selectedPreset = IdeologyNavigationState.GetSelectedPreset();
                    selectedIdeoField.SetValue(instance, selectedPreset);
                }
                else
                {
                    selectedIdeoField.SetValue(instance, null);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error updating page selection: {ex}");
            }
        }

        public static void ResetAnnouncement()
        {
            hasAnnouncedTitle = false;
            patchActive = false;
        }
    }

    /// <summary>
    /// Patch to reset state when the ideology selection page opens.
    /// </summary>
    [HarmonyPatch]
    public static class IdeologySelectionPatch_PostOpen
    {
        static bool Prepare()
        {
            return ModsConfig.IdeologyActive;
        }

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.Page_ChooseIdeoPreset");
            if (type == null) return null;
            return AccessTools.Method(type, "PostOpen");
        }

        [HarmonyPostfix]
        static void Postfix()
        {
            IdeologySelectionPatch.ResetAnnouncement();
            IdeologyNavigationState.Reset();
        }
    }
}
