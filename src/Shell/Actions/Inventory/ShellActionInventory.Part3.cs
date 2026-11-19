using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 3: the big flat/table menus — trade, sellable items, save/load,
    /// pause, extra menus, History, in-game storyteller, options, schedule, research detail,
    /// entity codex, styling station.
    /// </summary>
    internal static class ShellActionInventoryPart3
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- Trade screen -> TradeScope ----
            c.Register(InputAction.ForScreen("trade", "trade.item.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete), KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.accept",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.resetAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, shift: true, alt: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.giftMode.toggle",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            // Tab is the region-cycle key on ScreenScopes, so the breakdown keeps only Alt+P.
            c.Register(InputAction.ForScreen("trade", "trade.priceBreakdown",
                new List<KeyChord> { KeyChord.Of(KeyCode.P, alt: true) }));
            // Vanilla's show-sellable-items button, previously mouse-only.
            c.Register(InputAction.ForScreen("trade", "trade.showSellableItems",
                new List<KeyChord> { KeyChord.Of(KeyCode.L, alt: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.balance",
                new List<KeyChord> { KeyChord.Of(KeyCode.B, alt: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.quantity.decrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus) }));
            // Shifted twins: "+" on a US-style keyboard IS Shift+Equals, and KeyChord matching is
            // exact-modifier, so a bare KeyChord.Of(KeyCode.Equals) never matches a Shift+Equals
            // press. Decrement needs no twin (there is no shifted "-"). Same on autoSlaughter,
            // caravanFormation, splitCaravan and transportPodLoading.
            c.Register(InputAction.ForScreen("trade", "trade.quantity.increase",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus), KeyChord.Of(KeyCode.Equals),
                    KeyChord.Of(KeyCode.Plus, shift: true), KeyChord.Of(KeyCode.KeypadPlus, shift: true), KeyChord.Of(KeyCode.Equals, shift: true)
                }));
            // Larger steps and the range ends, in the row's primary direction; Ctrl+Up/Down are
            // free here because trade rows never reorder.
            c.Register(InputAction.ForScreen("trade", "trade.quantity.increaseTen",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.quantity.decreaseTen",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.quantity.increaseHundred",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.quantity.decreaseHundred",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.quantity.max",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("trade", "trade.quantity.min",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            // Both trade views claim this: classic <-> table on the open dialog (TradeViewOpener).
            c.Register(InputAction.ForScreen("trade", "trade.swapView",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, ctrl: true), KeyChord.Of(KeyCode.Tab, ctrl: true, shift: true) }));

            // ---- Sellable items dialog -> SellableItemsScope ----
            // Its category tabs are regions, cycled by Tab/Shift+Tab like every ScreenScope.
            c.Register(InputAction.ForScreen("sellableItems", "sellableItems.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // ---- save/load menu -> FileListScope ----
            // Drives the real Dialog_SaveFileList_Save/_Load. The save-name field is a modal
            // TextFieldEditSession, so the TextInputController owns the clipboard and caret keys
            // while editing and no per-scope chords are needed.
            c.Register(InputAction.ForScreen("saveMenu", "saveMenu.deleteSave",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("saveMenu", "saveMenu.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- Manage Areas dialog -> ManageAreasScope ----
            c.Register(InputAction.ForScreen("manageAreas", "manageAreas.newArea",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));

            // ---- The text-entry dialog family: RenameScope / GiveNameScope / NamePawnScope.
            // Rename and NamePawn run native fields (vanilla owns typing, caret, clipboard, IME);
            // GiveName mirrors through TextInputController and so needs its own field clipboard
            // chords. Activation is Space, since vanilla's own Enter poll submits the whole dialog
            // and stays unclaimed; Alt+R randomizes the focused name row. ----
            c.Register(InputAction.ForScreen("renameDialog", "renameDialog.activateFocused",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            // VF's dialog has a second button (Remove Name) with no vanilla equivalent, and
            // VfRenameDialogScope claims Enter everywhere, so Space is an alternate activation.
            c.Register(InputAction.ForScreen("vfRenameDialog", "vfRenameDialog.activateFocused",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            // Rename's field is a modal TextFieldEditSession, so no per-scope clipboard chords.
            c.Register(InputAction.ForScreen("nameDialog", "nameDialog.randomizeField",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            // Tab/Shift+Tab walk NamePawnScope's unified BROWSE-mode element cycle, and the scope
            // consumes Tab there so vanilla's editable-fields-only FocusNextControl never runs from
            // a browse-mode press. Inside EDIT mode the focused field owns Tab and the scope
            // re-syncs to follow.
            c.Register(InputAction.ForScreen("nameDialog", "nameDialog.nextElement",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("nameDialog", "nameDialog.previousElement",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("giveNameDialog", "giveNameDialog.activateFocused",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            // GiveName's field is a modal TextFieldEditSession, so no per-scope clipboard chords.

            // ---- RimTalk's CustomDialogueWindow -> RimTalkChatScope ----
            // The scope claims Enter everywhere, so Space is an alternate button activation.
            c.Register(InputAction.ForScreen("rimTalkChatDialog", "rimTalkChatDialog.activateFocused",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- The RimTalk TTS addon's voice picker -> RimTalkTtsVoiceSelectionScope ----
            // Space is the same alternate button activation.
            c.Register(InputAction.ForScreen("rimTalkTtsVoiceSelection", "rimTalkTtsVoiceSelection.activateFocused",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- PersonaDirector's two PawnRosterList windows: the row icon's info card ----
            c.Register(InputAction.ForScreen("rimTalkPersonaDirectorBatch", "rimTalkPersonaDirectorBatch.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("rimTalkPersonaDirectorAutoEvolve", "rimTalkPersonaDirectorAutoEvolve.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // The pause menu registers nothing: pure shared menu grammar.

            // ---- extra menus -> map.menu.extras (opens a real FloatMenu) ----

            // ---- History tab ----
            // Tab-level switching lives in HistoryScope; the Alt filters and actions are claimed by
            // HistoryStatsScope/HistoryMessagesScope, stacked above it. HistoryScope itself claims
            // only the Graph-tab ids, plus Tab/Shift+Tab unconditionally.
            c.Register(InputAction.ForScreen("history", "history.nextTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("history", "history.previousTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("history", "history.messages.toggleLettersFilter",
                new List<KeyChord> { KeyChord.Of(KeyCode.L, alt: true) }));
            c.Register(InputAction.ForScreen("history", "history.messages.toggleMessagesFilter",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("history", "history.messages.togglePin",
                new List<KeyChord> { KeyChord.Of(KeyCode.P, alt: true) }));
            c.Register(InputAction.ForScreen("history", "history.messages.jumpToLocation",
                new List<KeyChord> { KeyChord.Of(KeyCode.J, alt: true) }));
            // Graph sub-tab: mirrors vanilla's "Select graph" button and its four date-range
            // buttons.
            c.Register(InputAction.ForScreen("history", "history.graph.selectGroup",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("history", "history.graph.cycleDateRange",
                new List<KeyChord> { KeyChord.Of(KeyCode.D, alt: true) }));

            // ---- storyteller selection -> StorytellerScopeBase ----
            // Claimed by both StorytellerScreenScope (pre-game) and StorytellerInGameScope, which
            // answer these same ids. Setting adjustments, toggles and reset apply only within the
            // CustomDifficulty region; see that base's claim guards.
            //
            // DORMANT, kept registered so existing rebinds survive:
            // storyteller.setting.decrease/.increase, superseded by the shared ScreenScope
            // Left/Right claim, and storyteller.switchLevel, superseded by the region-cycling
            // claims once the flattened CustomDifficulty region replaced the old two-level
            // sub-navigation.
            c.Register(InputAction.ForScreen("storyteller", "storyteller.resetToPreset",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            // Alt+N, the mod-wide "Next" toolbar chord.
            c.Register(InputAction.ForScreen("storyteller", "storyteller.next",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.setMinimum",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.setMaximum",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.decreaseLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.decreaseMedium",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.decrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.increaseLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.increaseMedium",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.increase",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.switchLevel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.setting.toggle",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            // CustomDifficulty region only: jump to the first row of the adjacent settings section.
            c.Register(InputAction.ForScreen("storyteller", "storyteller.section.previous",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("storyteller", "storyteller.section.next",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));

            // ---- options menu -> WindowlessOptionsMenuState ----
            // Left/Right adjust the current setting only at the SettingsList level.
            c.Register(InputAction.ForScreen("options", "options.setting.decrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("options", "options.setting.increase",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));

            // ---- Configure Spoken Announcements dialog -> AnnouncementConfigScope ----
            // Reorder rides the shared Ctrl+Up/Down grammar, and Enter and Space are both shared
            // activate grammar. Save is also a captured button, so this id is the memorized chord
            // CapturedButtonHotkey teaches on that row.
            c.Register(InputAction.ForScreen("announceConfig", "announceConfig.save",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            // Also a captured button, same as Save above.
            c.Register(InputAction.ForScreen("announceConfig", "announceConfig.resetToDefaults",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));

            // ---- Schedule tab -> ScheduleScope ----
            // Two regions (Schedule grid, Areas) reuse many chords with region-dependent meaning.
            // Global keys, working in both regions, come first.
            c.Register(InputAction.ForScreen("schedule", "schedule.copy",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.paste",
                new List<KeyChord> { KeyChord.Of(KeyCode.V, ctrl: true) }));
            // Digits 1-9 then 0 pick the timetable brush at that positional index in the
            // TimeAssignmentDef list, which is dynamic and DLC-dependent.
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush1",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha1) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush2",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha2) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush3",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha3) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush4",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha4) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush5",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha5) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush6",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha6) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush7",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha7) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush8",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha8) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush9",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha9) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.selectBrush10",
                new List<KeyChord> { KeyChord.Of(KeyCode.Alpha0) }));
            // Jump-to-pawn works in both columns; bare Home/End are repurposed, so first/last-pawn
            // list navigation moves onto Ctrl+Home/End.
            c.Register(InputAction.ForScreen("schedule", "schedule.jumpToFirstPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.jumpToLastPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));
            // Areas region.
            c.Register(InputAction.ForScreen("schedule", "schedule.areas.applyAbove",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.areas.applyBelow",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.areas.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.areas.paintToFirstPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.areas.paintToLastPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.areas.paintToAllTowardFirst",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.areas.paintToAllTowardLast",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true, shift: true) }));
            // Schedule grid region.
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintLeft",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintRight",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.applyBrush",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space), KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintToFirstHour",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintToLastHour",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintToFirstPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("schedule", "schedule.grid.paintToLastPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true, shift: true) }));

            // ---- CashRegister Shifts tab -> CashRegisterShiftsScope (list), CashRegisterShiftScope (one shift's hours) ----
            c.Register(InputAction.ForScreen("cashRegisterShifts", "cashRegisterShifts.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("cashRegisterShifts", "cashRegisterShifts.addShift",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));
            c.Register(InputAction.ForScreen("cashRegisterShifts", "cashRegisterShifts.assignStaff",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("cashRegisterShift", "cashRegisterShift.assignStaff",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("cashRegisterShift", "cashRegisterShift.paintLeft",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            c.Register(InputAction.ForScreen("cashRegisterShift", "cashRegisterShift.paintRight",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            c.Register(InputAction.ForScreen("cashRegisterShift", "cashRegisterShift.paintToFirstHour",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("cashRegisterShift", "cashRegisterShift.paintToLastHour",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));

            // ---- Research detail view -> ResearchDetailScope ----
            // Tree navigation is shared ScreenScope/TreeRegionScope grammar.
            c.Register(InputAction.ForScreen("researchDetail", "researchDetail.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // The shared tree.* ids are registered exactly ONCE, in Part7.cs's "tree" block: ids
            // are globally unique in ActionCatalog and dispatch resolves claims by bare id.

            // ---- entity codex dialog -> EntityCodexState ----
            // Modal: consumes all other keys.
            c.Register(InputAction.ForScreen("entityCodex", "entityCodex.drillIn",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // DEV-mode debug controls on the shared RightBracket context-menu chord.
            c.Register(InputAction.ForScreen("entityCodex", "entityCodex.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- Dialogue Log opener ----
            // A reserved id with no default binding: the F12 extras hub is the only opener for now,
            // and the whole open path is one method behind this id. ActionCategory.Global rather
            // than Screen, since it opens the screen rather than living inside it.
            c.Register(new InputAction("narrative.openDialogueLog",
                ActionCategory.Global,
                new List<KeyChord>()));

            // ---- styling station dialog -> StylingStationScope ----
            // Enter opens the current item's color picker through the shared activate grammar, so
            // it needs no id of its own.
            c.Register(InputAction.ForScreen("styling", "styling.apply",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("styling", "styling.nextTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("styling", "styling.previousTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("styling", "styling.resetAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            // DEV-mode "Show all" toggle. Its own id because RightBracket is already
            // styling.resetAll here; the scope offers this dev-guarded claim first and its menu
            // leads with Reset, so nothing is lost.
            c.Register(InputAction.ForScreen("styling", "styling.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
        }
    }
}
