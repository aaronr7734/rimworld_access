using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patch that intercepts keyboard input while the pawn skills table
    /// is active. Separate from UnifiedKeyboardPatch so input handling runs at
    /// Priority.First and is strictly gated by PawnSkillsTableState.IsActive.
    /// The table is read-only: no priority editing, no painting.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot))]
    [HarmonyPatch("UIRootOnGUI")]
    public static class PawnSkillsTableMenuInputPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            if (!PawnSkillsTableState.IsActive) return;
            if (Event.current.type != EventType.KeyDown) return;

            KeyCode key = Event.current.keyCode;
            bool shift = Event.current.shift;
            bool ctrl = KeyboardHelper.IsCtrlHeld;
            bool alt = KeyboardHelper.IsAltHeld;
            var typeahead = PawnSkillsTableState.Typeahead;

            // Alt+P — toggle close (matches the global opener).
            if (alt && key == KeyCode.P)
            {
                PawnSkillsTableState.Close();
                Event.current.Use();
                return;
            }

            // Escape — clear search first, otherwise close.
            if (key == KeyCode.Escape)
            {
                if (PawnSkillsTableState.ClearSearchIfActive())
                {
                    Event.current.Use();
                    return;
                }
                PawnSkillsTableState.Close();
                Event.current.Use();
                return;
            }

            // Enter — commit a search jump if active, otherwise close.
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                if (typeahead != null && typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearch();
                    PawnSkillsTableState.AnnounceCurrentCell(includePawnName: true);
                }
                else
                {
                    PawnSkillsTableState.Close();
                }
                Event.current.Use();
                return;
            }

            if (key == KeyCode.Backspace)
            {
                if (PawnSkillsTableState.HandleBackspace())
                {
                    Event.current.Use();
                    return;
                }
            }

            if (alt && key == KeyCode.S)
            {
                PawnSkillsTableState.ToggleSortByCurrentColumn();
                Event.current.Use();
                return;
            }

            if (key == KeyCode.UpArrow)
            {
                PawnSkillsTableState.SelectPreviousPawn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.DownArrow)
            {
                PawnSkillsTableState.SelectNextPawn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.LeftArrow)
            {
                PawnSkillsTableState.SelectPreviousColumn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.RightArrow)
            {
                PawnSkillsTableState.SelectNextColumn();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.Home)
            {
                PawnSkillsTableState.JumpToFirst();
                Event.current.Use();
                return;
            }
            if (key == KeyCode.End)
            {
                PawnSkillsTableState.JumpToLast();
                Event.current.Use();
                return;
            }

            // Typeahead character routing handled by TypeaheadDispatcher upstream
            // (see TypeaheadConsumerRegistry). Swallow the keycode-only event here so
            // RimWorld's bindings on KeyCode.A..Z don't fire while the table is open.
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            if (isLetter && !alt && !shift && !ctrl)
            {
                Event.current.Use();
                return;
            }

            // Swallow any remaining printable key events while the table is open
            // so vanilla shortcuts (time speed, architect menu, etc.) don't fire.
            if (!alt && !ctrl &&
                ((key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) ||
                 (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) ||
                 key == KeyCode.Space))
            {
                Event.current.Use();
                return;
            }
        }
    }
}
