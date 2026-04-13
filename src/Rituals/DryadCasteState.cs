using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard accessibility state for Dialog_ChangeDryadCaste.
    /// Provides flat menu navigation through available dryad castes.
    /// </summary>
    public static class DryadCasteState
    {
        private static bool isActive;
        private static Dialog_ChangeDryadCaste currentDialog;
        private static List<GauranlenTreeModeDef> allModes = new List<GauranlenTreeModeDef>();
        private static int selectedIndex = 0;

        // Cached reflection handles
        private static System.Reflection.FieldInfo selectedModeField;
        private static System.Reflection.FieldInfo currentModeField;
        private static System.Reflection.FieldInfo allModesField;
        private static System.Reflection.FieldInfo treeConnectionField;
        private static System.Reflection.FieldInfo connectedPawnField;
        private static System.Reflection.MethodInfo meetsRequirementsMethod;
        private static System.Reflection.MethodInfo meetsMemeRequirementsMethod;
        private static System.Reflection.MethodInfo startChangeMethod;

        public static bool IsActive => isActive;

        public static void Open(Dialog_ChangeDryadCaste dialog)
        {
            if (dialog == null)
                return;

            try
            {
                CacheReflection();

                currentDialog = dialog;
                allModes = (allModesField?.GetValue(dialog) as List<GauranlenTreeModeDef>) ?? new List<GauranlenTreeModeDef>();
                selectedIndex = 0;
                isActive = true;

                Pawn connectedPawn = connectedPawnField?.GetValue(dialog) as Pawn;
                CompTreeConnection treeConnection = treeConnectionField?.GetValue(dialog) as CompTreeConnection;
                GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;
                
                string connectedPawnLabel = connectedPawn?.LabelShortCap ?? "connected pawn";
                string treeLabel = treeConnection?.parent?.LabelCap ?? "tree";
                string currentModeLabel = currentMode?.LabelCap ?? "none";
                
                // Announce initial instruction text (same as what sighted users see)
                string upgradeInfo = "";
                try
                {
                    var dryadCocoonProps = ThingDefOf.DryadCocoon?.GetCompProperties<CompProperties_DryadCocoon>();
                    float daysToComplete = dryadCocoonProps?.daysToComplete ?? 0f;
                    upgradeInfo = "ChooseProductionModeInitialDesc".Translate(
                        connectedPawn.Named("PAWN"), 
                        treeConnection.parent.Named("TREE"), 
                        ((int)daysToComplete).Named("UPGRADEDURATION"));
                }
                catch { }

                TolkHelper.Speak(
                    $"Dryad castes for {connectedPawnLabel}. Current mode: {currentModeLabel}. {upgradeInfo} Use Up and Down arrow keys to navigate, Enter to change caste, Escape to close.");

                AnnounceCurrentSelection();
            }
            catch (Exception ex)
            {
                Log.Error($"[DryadCasteState] Error opening: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            allModes.Clear();
            selectedIndex = 0;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive || currentDialog == null)
                return false;

            if (ctrl || alt)
                return true; // modal: block unrelated shortcuts

            switch (key)
            {
                case KeyCode.UpArrow:
                    NavigateUp();
                    return true;

                case KeyCode.DownArrow:
                    NavigateDown();
                    return true;

                case KeyCode.Home:
                    NavigateHome();
                    return true;

                case KeyCode.End:
                    NavigateEnd();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    TrySelectCaste();
                    return true;

                case KeyCode.Escape:
                    if (!shift)
                    {
                        currentDialog.Close(doCloseSound: false);
                        TolkHelper.Speak("Dryad caste dialog closed.");
                        return true;
                    }
                    break;
            }

            return true; // modal window: consume everything else
        }

        private static void NavigateUp()
        {
            if (allModes.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, allModes.Count);
            AnnounceCurrentSelection();
        }

        private static void NavigateDown()
        {
            if (allModes.Count == 0)
                return;

            selectedIndex = MenuHelper.SelectNext(selectedIndex, allModes.Count);
            AnnounceCurrentSelection();
        }

        private static void NavigateHome()
        {
            if (allModes.Count == 0)
                return;

            selectedIndex = 0;
            AnnounceCurrentSelection();
        }

        private static void NavigateEnd()
        {
            if (allModes.Count == 0)
                return;

            selectedIndex = allModes.Count - 1;
            AnnounceCurrentSelection();
        }

        private static void AnnounceCurrentSelection()
        {
            if (allModes.Count == 0)
                return;

            GauranlenTreeModeDef mode = allModes[selectedIndex];
            string announcement = FormatModeAnnouncement(mode);
            string position = MenuHelper.FormatPosition(selectedIndex, allModes.Count);
            TolkHelper.Speak($"{announcement}{position}", SpeechPriority.Normal);
        }

        private static string FormatModeAnnouncement(GauranlenTreeModeDef mode)
        {
            if (mode == null)
                return "Unknown caste";

            GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;
            bool isCurrent = mode == currentMode;

            // Check requirements
            bool meetsMemes = (bool)(meetsMemeRequirementsMethod?.Invoke(currentDialog, new object[] { mode }) ?? false);
            bool meetsAll = (bool)(meetsRequirementsMethod?.Invoke(currentDialog, new object[] { mode }) ?? false);

            string status;
            if (isCurrent)
            {
                status = "current mode";
            }
            else if (meetsAll)
            {
                status = "available";
            }
            else
            {
                if (!meetsMemes)
                {
                    status = "locked, missing required memes";
                }
                else if (mode.previousStage != null && currentMode != mode.previousStage)
                {
                    status = $"locked, requires {mode.previousStage.LabelCap}";
                }
                else
                {
                    status = "locked";
                }
            }

            // Build announcement with name, status, and description
            var parts = new List<string> { $"{mode.LabelCap}, {status}" };

            // Add description if available
            if (!string.IsNullOrEmpty(mode.description))
            {
                parts.Add(mode.description);
            }

            // Add required memes info if needed
            Pawn connectedPawn = connectedPawnField?.GetValue(currentDialog) as Pawn;
            if (connectedPawn != null && !Find.IdeoManager.classicMode && !mode.requiredMemes.NullOrEmpty())
            {
                var unavailableMemes = new List<string>();
                
                foreach (var memeDef in mode.requiredMemes)
                {
                    if (!connectedPawn.Ideo.HasMeme(memeDef))
                        unavailableMemes.Add(memeDef.LabelCap);
                }

                if (unavailableMemes.Count > 0)
                {
                    parts.Add($"Required memes: {string.Join(", ", unavailableMemes)} (not available)");
                }
            }

            // Add previous stage requirement if present
            if (mode.previousStage != null && currentMode != mode.previousStage)
            {
                parts.Add($"Requires prior caste: {mode.previousStage.pawnKindDef.LabelCap}");
            }

            // Add displayed stats if available
            if (mode.displayedStats != null && mode.displayedStats.Count > 0)
            {
                var statsLines = new List<string>();
                PawnKindDef selectedKind = mode.pawnKindDef;
                
                if (selectedKind != null)
                {
                    foreach (var statDef in mode.displayedStats)
                    {
                        try
                        {
                            string statValue = statDef.ValueToString(
                                selectedKind.race.GetStatValueAbstract(statDef),
                                statDef.toStringNumberSense);
                            statsLines.Add($"{statDef.LabelCap}: {statValue}");
                        }
                        catch
                        {
                            // Skip if stat calculation fails
                        }
                    }
                }
                
                if (statsLines.Count > 0)
                {
                    parts.Add("Stats: " + string.Join(", ", statsLines));
                }
            }

            return string.Join(". ", parts);
        }

        private static void TrySelectCaste()
        {
            if (allModes.Count == 0 || selectedIndex < 0 || selectedIndex >= allModes.Count)
            {
                TolkHelper.Speak("No caste selected.");
                return;
            }

            GauranlenTreeModeDef mode = allModes[selectedIndex];
            GauranlenTreeModeDef currentMode = currentModeField?.GetValue(currentDialog) as GauranlenTreeModeDef;

            if (mode == currentMode)
            {
                TolkHelper.Speak("Already selected.");
                return;
            }

            bool meetsRequirements = (bool)(meetsRequirementsMethod?.Invoke(currentDialog, new object[] { mode }) ?? false);
            if (!meetsRequirements)
            {
                bool meetsMemeRequirements = (bool)(meetsMemeRequirementsMethod?.Invoke(currentDialog, new object[] { mode }) ?? false);
                if (!meetsMemeRequirements)
                {
                    TolkHelper.Speak("Cannot apply. Missing required memes.");
                    return;
                }

                if (mode.previousStage != null && currentMode != mode.previousStage)
                {
                    TolkHelper.Speak($"Cannot apply. Missing required prior caste: {mode.previousStage.LabelCap}.");
                    return;
                }

                TolkHelper.Speak("Cannot apply. Caste is locked.");
                return;
            }

            CompTreeConnection treeConnection = treeConnectionField?.GetValue(currentDialog) as CompTreeConnection;
            Pawn connectedPawn = connectedPawnField?.GetValue(currentDialog) as Pawn;
            if (treeConnection == null || connectedPawn == null)
            {
                TolkHelper.Speak("Cannot apply caste due to missing tree data.");
                return;
            }

            // Update the dialog's selectedMode field so the UI reflects the selection
            selectedModeField?.SetValue(currentDialog, mode);
            SoundDefOf.Click.PlayOneShotOnCamera();

            // Show confirmation dialog
            string confirmText = "GauranlenModeChangeDescFull".Translate(
                treeConnection.parent.Named("TREE"),
                connectedPawn.Named("CONNECTEDPAWN"),
                ThingDefOf.DryadCocoon.GetCompProperties<CompProperties_DryadCocoon>().daysToComplete.Named("DURATION"));

            Dialog_MessageBox confirm = Dialog_MessageBox.CreateConfirmation(confirmText, () =>
            {
                startChangeMethod?.Invoke(currentDialog, null);
            });

            Find.WindowStack.Add(confirm);
            TolkHelper.Speak($"Confirm changing to {mode.LabelCap}. Press Enter to confirm or Escape to cancel.");
        }

        private static void CacheReflection()
        {
            if (selectedModeField == null)
                selectedModeField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "selectedMode");
            if (currentModeField == null)
                currentModeField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "currentMode");
            if (allModesField == null)
                allModesField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "allDryadModes");
            if (treeConnectionField == null)
                treeConnectionField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "treeConnection");
            if (connectedPawnField == null)
                connectedPawnField = AccessTools.Field(typeof(Dialog_ChangeDryadCaste), "connectedPawn");

            if (meetsRequirementsMethod == null)
                meetsRequirementsMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "MeetsRequirements");
            if (meetsMemeRequirementsMethod == null)
                meetsMemeRequirementsMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "MeetsMemeRequirements");
            if (startChangeMethod == null)
                startChangeMethod = AccessTools.Method(typeof(Dialog_ChangeDryadCaste), "StartChange");
        }
    }
}
