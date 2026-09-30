using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 6: the map-ambient heart — multi-select, colonist bar, bookmarks,
    /// Alt+letter info keys, F-key menu openers, gizmos, brackets and local map cursor navigation.
    /// </summary>
    internal static class ShellActionInventoryPart6
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- multi-select pawn commands ----
            c.Register(new InputAction("map.multiSelect.extendSelectionNext",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, shift: true, alt: true) }));
            c.Register(new InputAction("map.multiSelect.extendSelectionPrevious",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, shift: true, alt: true) }));
            c.Register(new InputAction("map.multiSelect.togglePawn",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Space, alt: true) }));
            c.Register(new InputAction("map.multiSelect.toggleAll",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Space, ctrl: true, alt: true) }));
            // Digit-slot family: one save action, chord per slot F1-F4 (gated on active multi-select).
            c.Register(new InputAction("map.multiSelect.saveGroup",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.F1, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.F2, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.F3, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.F4, ctrl: true, shift: true),
                }));
            c.Register(new InputAction("map.multiSelect.recallGroup",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.F1, ctrl: true),
                    KeyChord.Of(KeyCode.F2, ctrl: true),
                    KeyChord.Of(KeyCode.F3, ctrl: true),
                    KeyChord.Of(KeyCode.F4, ctrl: true),
                }));

            // ---- colonist bar navigation ----
            c.Register(new InputAction("map.colonistBar.next",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, alt: true) }));
            c.Register(new InputAction("map.colonistBar.previous",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, alt: true) }));
            c.Register(new InputAction("map.colonistBar.pageDown",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, alt: true) }));
            c.Register(new InputAction("map.colonistBar.pageUp",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, alt: true) }));
            c.Register(new InputAction("map.colonistBar.moveRight",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, ctrl: true, alt: true) }));
            c.Register(new InputAction("map.colonistBar.moveLeft",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, ctrl: true, alt: true) }));
            c.Register(new InputAction("map.colonistBar.moveDown",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true, alt: true) }));
            c.Register(new InputAction("map.colonistBar.moveUp",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true, alt: true) }));
            // Digit-slot family: focus/jump to colonist position 1-10 on the current page (double-tap forces camera jump).
            c.Register(new InputAction("map.colonistBar.focusByNumber",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Alpha1, alt: true),
                    KeyChord.Of(KeyCode.Alpha2, alt: true),
                    KeyChord.Of(KeyCode.Alpha3, alt: true),
                    KeyChord.Of(KeyCode.Alpha4, alt: true),
                    KeyChord.Of(KeyCode.Alpha5, alt: true),
                    KeyChord.Of(KeyCode.Alpha6, alt: true),
                    KeyChord.Of(KeyCode.Alpha7, alt: true),
                    KeyChord.Of(KeyCode.Alpha8, alt: true),
                    KeyChord.Of(KeyCode.Alpha9, alt: true),
                    KeyChord.Of(KeyCode.Alpha0, alt: true),
                }));
            // The reachable chord is Ctrl+Alt+Enter; Unity eats plain Alt+Enter for fullscreen.
            c.Register(new InputAction("map.colonistBar.inspectSelected",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Return, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.KeypadEnter, ctrl: true, alt: true),
                }));
            c.Register(new InputAction("map.colonistBar.infoCardSelected",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.I, ctrl: true, alt: true) }));

            // ---- map bookmarks ----
            // Digit-slot family: one action per operation, chord per slot 0-9.
            c.Register(new InputAction("map.bookmark.set",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Alpha0, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha1, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha2, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha3, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha4, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha5, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha6, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha7, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha8, ctrl: true, alt: true),
                    KeyChord.Of(KeyCode.Alpha9, ctrl: true, alt: true),
                }));
            c.Register(new InputAction("map.bookmark.jump",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Alpha0, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha1, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha2, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha3, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha4, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha5, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha6, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha7, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha8, ctrl: true, shift: true),
                    KeyChord.Of(KeyCode.Alpha9, ctrl: true, shift: true),
                }));
            c.Register(new InputAction("map.bookmark.peekOrJump",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Alpha0, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha1, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha2, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha3, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha4, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha5, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha6, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha7, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha8, ctrl: true),
                    KeyChord.Of(KeyCode.Alpha9, ctrl: true),
                }));

            // ---- mood info ----
            c.Register(new InputAction("map.info.mood",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));

            // ---- health info ----
            c.Register(new InputAction("map.info.health",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));

            // ---- needs info ----
            c.Register(new InputAction("map.info.needs",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));

            // ---- combat log ----
            c.Register(new InputAction("map.info.combatLog",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.B, alt: true) }));

            c.Register(new InputAction("map.openGlobalLog",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.L, alt: true) }));

            // ---- gear info ----
            c.Register(new InputAction("map.info.gear",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));

            // ---- top skills ----
            c.Register(new InputAction("map.info.skills",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.K, alt: true) }));

            // ---- cursor coordinates ----
            c.Register(new InputAction("map.info.cursorCoordinates",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.K) }));

            // ---- assign area ----
            c.Register(new InputAction("map.pawn.assignArea",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));

            // ---- pawn skills table ----
            c.Register(new InputAction("map.menu.skillsTable",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.P, alt: true) }));

            // ---- rename pawn ----
            c.Register(new InputAction("map.pawn.rename",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));

            // ---- unforbid all items ----
            c.Register(new InputAction("map.item.unforbidAll",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F, alt: true) }));

            // ---- reform caravan ----
            c.Register(new InputAction("map.caravan.reform",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.C) }));

            // ---- announce time / performance -> MapScope.TimeAnnounce.Game.cs (registered on both MapScope and WorldScope) ----
            c.Register(new InputAction("map.info.time",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.T) }));
            c.Register(new InputAction("map.info.performance",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.T, alt: true) }));

            // ---- forbid toggle at cursor ----
            c.Register(new InputAction("map.item.forbidToggle",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F) }));

            // ---- open work menu ----
            c.Register(new InputAction("map.menu.work",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F1) }));

            // ---- open animals/mechs menu ----
            c.Register(new InputAction("map.menu.animalsMechs",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F4) }));

            // ---- open schedule menu -> MapScope.QuickInfo.Game.cs's ScheduleMenuOpenerLive/OnOpenScheduleMenu ----
            c.Register(new InputAction("map.menu.schedule",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F2) }));

            // ---- open assign menu ----
            c.Register(new InputAction("map.menu.assign",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F3) }));

            // ---- Shift+<letter> gizmo hotkey activation -> map.gizmo.hotkeyActivate ----
            // One action, 26 chords: the handler reads the matched snapshot's own Key to know which
            // letter fired. Built by loop rather than spelled out, and built here because this file
            // stays PURE (it links into the test project).
            c.Register(new InputAction("map.gizmo.hotkeyActivate",
                ActionCategory.Map,
                BuildShiftLetterGizmoChords()));

            // ---- open gizmo navigation ----
            c.Register(new InputAction("map.gizmo.open",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.G) }));

            // ---- open notification menu ----
            c.Register(new InputAction("map.menu.notifications",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.L) }));

            // ---- focus colonist bar on pawn under cursor ----
            c.Register(new InputAction("map.colonistBar.focusByCursor",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Slash) }));

            // ---- open learning helper -> LearningHelperOpenerClaims ----
            // Claimed on MapScope, WorldScope and StartingSiteScreenScope. '?' is Shift+Slash on US
            // layouts.
            c.Register(new InputAction("map.menu.learningHelper",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Slash, shift: true) }));
            // Non-US layouts deliver the dedicated '?' key as keyCode=None with character='?' and
            // shift NOT held; KeyRemapPatch rewrites it to a bare Slash and stamps
            // KeyboardHelper.WasCharacterRemapped for the frame, which this twin action gates on.
            // Registered on WorldScope and StartingSiteScreenScope only: on the colony map
            // map.colonistBar.focusByCursor holds bare Slash and must keep winning it.
            c.Register(new InputAction("map.menu.learningHelperRemapped",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Slash) }));

            // ---- open quest menu ----
            c.Register(new InputAction("map.menu.quests",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F7) }));

            // ---- open research menu -> MapScope.QuickInfo.Game.cs's ResearchMenuOpenerLive/OnOpenResearchMenu ----
            c.Register(new InputAction("map.menu.research",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F6) }));

            // ---- open extra menus ----
            c.Register(new InputAction("map.menu.extras",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.F12) }));

            // The 'i' inspection-menu opener is dead code (its condition tests KeyCode.None), so no
            // action is registered for it.

            // ---- info card at cursor ----
            c.Register(new InputAction("map.inspect.infoCardAtCursor",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // ---- paste copied plan at cursor ----
            // Only fires when PlanClipboard.HasContent; otherwise Ctrl+V falls through untouched.
            c.Register(new InputAction("map.plan.paste",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.V, ctrl: true) }));

            // ---- open colony inventory menu ----
            // The condition is a bare KeyCode.I with no shift check.
            c.Register(new InputAction("map.menu.inventory",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.I) }));

            // ---- open prisoner tab -> MapScope.EndgameOpeners.Game.cs (prisoner != null lookup folded into the claim's own when-gate) ----
            c.Register(new InputAction("map.menu.prisonerTab",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.P) }));

            // ---- open pause menu -> MapScope.EndgameOpeners.Game.cs ----
            c.Register(new InputAction("map.pause.open",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));

            // ---- open inspection menu at cursor -> MapScope.Inspect.Game.cs ----
            c.Register(new InputAction("map.inspect.open",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Return),
                    KeyChord.Of(KeyCode.KeypadEnter),
                }));

            // ---- execute top context order at cursor -> map.order.topOption ----
            // Bare [ issues immediately, Shift+[ queues via vanilla KeyBindingDefOf.QueueOrder. Both
            // chords bind the SAME id: the handler reads the snapshot's Shift bit to choose. Ctrl+[
            // and Alt+[ no longer reach it — accepted drift, per the exact-chord-matching rule.
            c.Register(new InputAction("map.order.topOption",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftBracket), KeyChord.Of(KeyCode.LeftBracket, shift: true) }));

            // ---- open colonist orders menu at cursor -> map.order.menu ----
            // Bare ] only; the modifier-blind reach of Ctrl/Alt+] is accepted drift, not preserved.
            c.Register(new InputAction("map.order.menu",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- local map cursor navigation ----
            // Delegates to MapArrowKeyHandler.HandleArrowKey.
            c.Register(new InputAction("map.cursor.north",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow) }));
            c.Register(new InputAction("map.cursor.south",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow) }));
            c.Register(new InputAction("map.cursor.west",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(new InputAction("map.cursor.east",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            // Ctrl+arrow performs a jump in the current jump mode (preset distance, impassable, terrain, structure).
            c.Register(new InputAction("map.cursor.jumpNorth",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            c.Register(new InputAction("map.cursor.jumpSouth",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            c.Register(new InputAction("map.cursor.jumpWest",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, ctrl: true) }));
            c.Register(new InputAction("map.cursor.jumpEast",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, ctrl: true) }));
            // Alt+J warps the OS pointer onto the keyboard cursor's tile so mouse scanning can
            // continue from there; Alt+Shift+J is the inverse.
            c.Register(new InputAction("map.cursor.warpPointer",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.J, alt: true) }));
            c.Register(new InputAction("map.cursor.pullFromPointer",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.J, shift: true, alt: true) }));
            // Alt+Shift+M toggles reading what the pointer moves over, Alt+Shift+K the pointer
            // following every cursor move — the NVDA mouse-tracking pair, on the shifted layer
            // because Alt+M and Alt+K are taken.
            c.Register(new InputAction("map.cursor.toggleMouseTracking",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.M, shift: true, alt: true) }));
            c.Register(new InputAction("map.cursor.toggleFollowKeyboard",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.K, shift: true, alt: true) }));

            // Shift+Up/Down cycles the jump mode (no cursor movement).
            c.Register(new InputAction("map.jumpMode.next",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(new InputAction("map.jumpMode.previous",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            // Shift+Left/Right adjusts the preset jump distance and Ctrl+Shift is its coarse step of
            // 10. The handler reads Ctrl for the step size, but KeyChord matching is exact-modifier,
            // so the coarse combination needs its own default on the same action.
            c.Register(new InputAction("map.jumpMode.decreaseDistance",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.LeftArrow, shift: true),
                    KeyChord.Of(KeyCode.LeftArrow, ctrl: true, shift: true),
                }));
            c.Register(new InputAction("map.jumpMode.increaseDistance",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.RightArrow, shift: true),
                    KeyChord.Of(KeyCode.RightArrow, ctrl: true, shift: true),
                }));

            // Dev-mode debug tool targeting: Enter fires the armed tool at the keyboard cursor
            // (repeatable), Escape puts it away. Both are claimed by MapToolScope, which sits on the
            // stack only while a tool is armed and floats above every screen scope while it does, so
            // Apply beats the map inspection opener and Cancel beats the pause menu, both reaching
            // the player even with the screen that armed the tool still open.
            c.Register(new InputAction("map.devtool.apply",
                ActionCategory.Map,
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Return),
                    KeyChord.Of(KeyCode.KeypadEnter),
                }));
            c.Register(new InputAction("map.devtool.cancel",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));
        }

        /// <summary>The 26 Shift+A..Shift+Z chords for map.gizmo.hotkeyActivate.</summary>
        private static List<KeyChord> BuildShiftLetterGizmoChords()
        {
            var chords = new List<KeyChord>();
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++)
            {
                chords.Add(KeyChord.Of(k, shift: true));
            }
            return chords;
        }
    }
}
