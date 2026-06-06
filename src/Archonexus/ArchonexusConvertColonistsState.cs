using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard-accessible driver for Dialog_ChooseColonistsForIdeo — the "Assign colonists" sub-dialog
    /// of the Archonexus reform screen, where each colonist can be converted to the new primary
    /// ideoligion or reverted to their previous one. A flat list: Up/Down/Home/End to browse, Enter to
    /// toggle the focused colonist's pending conversion, Escape to close. All actions and labels are the
    /// dialog's own (game) strings; the toggle reuses the dialog's pawnIdeoSetter so the pending-conversion
    /// bookkeeping the reform's "Next" applies stays correct.
    /// </summary>
    public static class ArchonexusConvertColonistsState
    {
        public static bool IsActive { get; private set; }

        private static Dialog_ChooseColonistsForIdeo dialog;
        private static readonly List<Pawn> pawns = new List<Pawn>();
        private static int selectedIndex;
        private static readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        #region Reflection cache

        private static readonly Type DialogType = typeof(Dialog_ChooseColonistsForIdeo);
        private static readonly FieldInfo PawnsField = AccessTools.Field(DialogType, "pawns");
        private static readonly FieldInfo IdeoField = AccessTools.Field(DialogType, "ideo");
        private static readonly FieldInfo CanChangeField = AccessTools.Field(DialogType, "canChangeIdeo");
        private static readonly FieldInfo OriginalIdeoField = AccessTools.Field(DialogType, "originalIdeo");
        private static readonly FieldInfo GetterField = AccessTools.Field(DialogType, "pawnIdeoGetter");
        private static readonly FieldInfo SetterField = AccessTools.Field(DialogType, "pawnIdeoSetter");

        #endregion

        #region Lifecycle

        public static void EnsureOpen(Dialog_ChooseColonistsForIdeo d)
        {
            if (ReferenceEquals(dialog, d))
                return;
            dialog = d;
            IsActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();
            pawns.Clear();
            if (PawnsField.GetValue(dialog) is List<Pawn> p)
                pawns.AddRange(p);
            AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            pawns.Clear();
            typeahead.ClearSearch();
            // dialog reference intentionally retained — see ArchonexusReformIdeoState.EnsureOpen.
        }

        #endregion

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;

            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrent();
                    return true;
                }
                dialog.Close(doCloseSound: false);
                return true;
            }

            if (pawns.Count == 0) return true;

            if (key == KeyCode.UpArrow) { Move(-1); return true; }
            if (key == KeyCode.DownArrow) { Move(1); return true; }
            if (key == KeyCode.Home) { typeahead.ClearSearch(); selectedIndex = 0; AnnounceCurrent(); return true; }
            if (key == KeyCode.End) { typeahead.ClearSearch(); selectedIndex = pawns.Count - 1; AnnounceCurrent(); return true; }

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) && !alt && !ctrl)
            {
                ToggleCurrent();
                return true;
            }

            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch && typeahead.ProcessBackspace(Labels(), out int ni))
                {
                    if (ni >= 0) selectedIndex = ni;
                    AnnounceCurrent();
                }
                return true;
            }

            char c = ev.character;
            if (!alt && !ctrl && c != '\0' && char.IsLetterOrDigit(c))
            {
                if (typeahead.ProcessCharacterInput(c, Labels(), out int ni))
                {
                    selectedIndex = ni;
                    AnnounceCurrent();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    typeahead.SpeakNoMatches();
                }
                return true;
            }

            return true; // own all keys while this modal is up
        }

        private static List<string> Labels() => pawns.Select(p => p.LabelShortCap.ToString()).ToList();

        private static void Move(int delta)
        {
            // While a search filters the list, arrows step through matches only (parity with the
            // colony-selection and reform screens).
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int mi = delta > 0 ? typeahead.GetNextMatch(selectedIndex) : typeahead.GetPreviousMatch(selectedIndex);
                if (mi >= 0) selectedIndex = mi;
            }
            else
            {
                selectedIndex = delta > 0
                    ? MenuHelper.SelectNext(selectedIndex, pawns.Count)
                    : MenuHelper.SelectPrevious(selectedIndex, pawns.Count);
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent();
        }

        private static void ToggleCurrent()
        {
            if (selectedIndex < 0 || selectedIndex >= pawns.Count) return;
            Pawn pawn = pawns[selectedIndex];

            var canChange = CanChangeField.GetValue(dialog) as Func<Pawn, bool>;
            if (canChange != null && !canChange(pawn))
            {
                // Already a follower of the player ideoligion — no toggle available.
                TolkHelper.Speak("ExistingFollowerOfPlayerIdeoligion".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            Ideo target = IdeoField.GetValue(dialog) as Ideo;
            var getter = GetterField.GetValue(dialog) as Func<Pawn, Ideo>;
            var original = OriginalIdeoField.GetValue(dialog) as Func<Pawn, Ideo>;
            var setter = SetterField.GetValue(dialog) as Action<Pawn, Ideo>;

            Ideo currentPending = getter != null ? getter(pawn) : pawn.Ideo;
            Ideo newIdeo = (currentPending == target) ? original?.Invoke(pawn) : target;

            if (setter != null)
                setter(pawn, newIdeo);
            else
                pawn.ideo.SetIdeo(newIdeo);

            // Clear any active search on toggle so the next keystrokes start fresh (parity with
            // the colony-selection screen).
            typeahead.ClearSearch();
            SoundDefOf.Click.PlayOneShotOnCamera();
            // Checkbox toggle: announce the new checked state; when unchecked, name the ideoligion
            // they now keep so the player hears the consequence of leaving them unconverted.
            bool nowConverting = newIdeo == target;
            TolkHelper.Speak(nowConverting
                ? "Selected"
                : (newIdeo != null ? $"Not selected. Currently {newIdeo.name}" : "Not selected"));
        }

        #endregion

        #region Announcements

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append("ChooseColonistsForIdeoTitle".Translate());
            sb.Append(". ").Append("ChooseColonistsForIdeoDesc".Translate());
            sb.Append(". ").Append(pawns.Count);
            // Tell the player how to toggle and how to finish (vanilla's bottom "Close" button is
            // Escape for us).
            sb.Append(". Space or Enter to convert, Escape when finished.");
            if (pawns.Count > 0)
                sb.Append(". ").Append(BuildCurrentText());
            TolkHelper.Speak(sb.ToString(), SpeechPriority.High);
        }

        private static void AnnounceCurrent()
        {
            if (pawns.Count == 0) return;
            TolkHelper.Speak(BuildCurrentText());
        }

        private static string BuildCurrentText()
        {
            if (selectedIndex < 0 || selectedIndex >= pawns.Count) return "";
            Pawn pawn = pawns[selectedIndex];

            Ideo target = IdeoField.GetValue(dialog) as Ideo;
            var canChange = CanChangeField.GetValue(dialog) as Func<Pawn, bool>;
            var getter = GetterField.GetValue(dialog) as Func<Pawn, Ideo>;
            Ideo currentPending = getter != null ? getter(pawn) : pawn.Ideo;

            var sb = new StringBuilder();
            sb.Append(pawn.LabelShortCap);

            // Vanilla renders each colonist as a two-state toggle (a Convert / Revert button), so we
            // present it as a checkbox: "Convert to {primary}: selected / not selected". Colonists who
            // already follow the primary have no toggle (vanilla shows a static "Existing follower").
            if (canChange != null && !canChange(pawn))
            {
                sb.Append(". ").Append("ExistingFollowerOfPlayerIdeoligion".Translate());
            }
            else
            {
                bool converting = currentPending == target;
                sb.Append(". ").Append("ConvertToPlayerIdeoligion".Translate()); // "Convert"
                if (target != null)
                    sb.Append(" ").Append(target.name);
                sb.Append(": ").Append(converting ? "selected" : "not selected");
                // When not converting, name the ideoligion they keep (vanilla shows it as an icon).
                if (!converting && currentPending != null)
                    sb.Append(". ").Append("Currently").Append(" ").Append(currentPending.name);
            }

            string position = MenuHelper.FormatPosition(selectedIndex, pawns.Count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);
            return sb.ToString();
        }

        #endregion
    }

    /// <summary>
    /// Patches for Dialog_ChooseColonistsForIdeo — routes input to ArchonexusConvertColonistsState and
    /// blocks vanilla Enter (which would close the dialog) so Enter can toggle a colonist instead.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ChooseColonistsForIdeo), "DoWindowContents")]
    public static class ArchonexusConvertColonistsPatch
    {
        static bool Prefix(Dialog_ChooseColonistsForIdeo __instance)
        {
            try
            {
                ArchonexusConvertColonistsState.EnsureOpen(__instance);

                if (Find.WindowStack != null && Find.WindowStack.WindowOfType<Dialog_InfoCard>() != null)
                    return true;

                if (Event.current.type == EventType.KeyDown)
                {
                    if (ArchonexusConvertColonistsState.HandleInput(Event.current))
                        Event.current.Use();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ArchonexusConvertColonistsPatch.Prefix: {ex}");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class ArchonexusConvertColonistsPatch_OnAccept
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            if (__instance is Dialog_ChooseColonistsForIdeo && ArchonexusConvertColonistsState.IsActive)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class ArchonexusConvertColonistsPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseColonistsForIdeo)
                Find.WindowStack.Notify_ManuallySetFocus(__instance);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class ArchonexusConvertColonistsPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ChooseColonistsForIdeo)
                ArchonexusConvertColonistsState.Close();
        }
    }
}
