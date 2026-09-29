using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 1: housekeeping/text sinks, Learning Helper, What's New,
    /// pawn-filter reroll block, windowless-dialog hotkey block, Dialog_NodeTree/
    /// Dialog_MessageBox early steps, scanner-search and GoTo text sinks, the Biotech
    /// dialog family (auto-slaughter, gene inspection, growth moment, xenogerm, xenotype
    /// editor), faction landing, Dialog_Slider, Anomaly Settings, world object selection,
    /// caravan inspect, settlement browser, caravan destination picking, shape
    /// selection/placement, line formation, viewing mode / stat breakdown, quantity menu,
    /// shelf linking, transport pod selection, area selection menus, caravan formation,
    /// transport pod loading, ritual/lord-job dialog, and dryad caste selection.
    /// </summary>
    internal static class ShellActionInventoryPart1
    {
        internal static void Register(ActionCatalog c)
        {
            // Learning Helper and What's New register nothing: every key they use is shared menu
            // grammar claimed by their scopes.

            // ---- scanner search text input -> ScannerSearchState ----
            // Letters/digits/Backspace are the text-edit-session buffer; only the boundary
            // actions are registered.
            c.Register(InputAction.ForScreen("scannerSearch", "scannerSearch.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("scannerSearch", "scannerSearch.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));
            c.Register(InputAction.ForScreen("scannerSearch", "scannerSearch.backspace",
                new List<KeyChord> { KeyChord.Of(KeyCode.Backspace) }));
            // Consume-only block of the bare letter/digit keycodes while the filter is being
            // typed, so a typed character's keycode twin cannot leak to a bare-letter menu
            // opener; the character itself is captured by the scope's CharSink. Ctrl/Alt
            // combinations are excluded so bookmarks still work.
            c.Register(InputAction.ForScreen("scannerSearch", "scannerSearch.blockChar",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.A), KeyChord.Of(KeyCode.B), KeyChord.Of(KeyCode.C),
                    KeyChord.Of(KeyCode.D), KeyChord.Of(KeyCode.E), KeyChord.Of(KeyCode.F),
                    KeyChord.Of(KeyCode.G), KeyChord.Of(KeyCode.H), KeyChord.Of(KeyCode.I),
                    KeyChord.Of(KeyCode.J), KeyChord.Of(KeyCode.K), KeyChord.Of(KeyCode.L),
                    KeyChord.Of(KeyCode.M), KeyChord.Of(KeyCode.N), KeyChord.Of(KeyCode.O),
                    KeyChord.Of(KeyCode.P), KeyChord.Of(KeyCode.Q), KeyChord.Of(KeyCode.R),
                    KeyChord.Of(KeyCode.S), KeyChord.Of(KeyCode.T), KeyChord.Of(KeyCode.U),
                    KeyChord.Of(KeyCode.V), KeyChord.Of(KeyCode.W), KeyChord.Of(KeyCode.X),
                    KeyChord.Of(KeyCode.Y), KeyChord.Of(KeyCode.Z),
                    KeyChord.Of(KeyCode.Alpha0), KeyChord.Of(KeyCode.Alpha1),
                    KeyChord.Of(KeyCode.Alpha2), KeyChord.Of(KeyCode.Alpha3),
                    KeyChord.Of(KeyCode.Alpha4), KeyChord.Of(KeyCode.Alpha5),
                    KeyChord.Of(KeyCode.Alpha6), KeyChord.Of(KeyCode.Alpha7),
                    KeyChord.Of(KeyCode.Alpha8), KeyChord.Of(KeyCode.Alpha9),
                }));

            // ---- GoTo coordinate text input -> GoToState ----
            c.Register(InputAction.ForScreen("goTo", "goTo.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("goTo", "goTo.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));
            c.Register(InputAction.ForScreen("goTo", "goTo.switchField",
                new List<KeyChord> { KeyChord.Of(KeyCode.Comma), KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("goTo", "goTo.backspace",
                new List<KeyChord> { KeyChord.Of(KeyCode.Backspace) }));
            c.Register(InputAction.ForScreen("goTo", "goTo.plus",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Equals),
                    KeyChord.Of(KeyCode.Equals, shift: true),
                    KeyChord.Of(KeyCode.KeypadPlus),
                }));
            c.Register(InputAction.ForScreen("goTo", "goTo.minus",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Minus),
                    KeyChord.Of(KeyCode.Minus, shift: true),
                    KeyChord.Of(KeyCode.KeypadMinus),
                }));
            // Consume-only block of the bare digit keycodes while Go To is typing, so a typed
            // digit's keycode twin cannot leak to the tile-info digits or vanilla's time-speed
            // bindings. Ctrl/Alt digits are excluded so bookmarks and multi-select still work.
            c.Register(InputAction.ForScreen("goTo", "goTo.blockDigit",
                new List<KeyChord>
                {
                    KeyChord.Of(KeyCode.Alpha0), KeyChord.Of(KeyCode.Alpha1),
                    KeyChord.Of(KeyCode.Alpha2), KeyChord.Of(KeyCode.Alpha3),
                    KeyChord.Of(KeyCode.Alpha4), KeyChord.Of(KeyCode.Alpha5),
                    KeyChord.Of(KeyCode.Alpha6), KeyChord.Of(KeyCode.Alpha7),
                    KeyChord.Of(KeyCode.Alpha8), KeyChord.Of(KeyCode.Alpha9),
                    KeyChord.Of(KeyCode.Keypad0), KeyChord.Of(KeyCode.Keypad1),
                    KeyChord.Of(KeyCode.Keypad2), KeyChord.Of(KeyCode.Keypad3),
                    KeyChord.Of(KeyCode.Keypad4), KeyChord.Of(KeyCode.Keypad5),
                    KeyChord.Of(KeyCode.Keypad6), KeyChord.Of(KeyCode.Keypad7),
                    KeyChord.Of(KeyCode.Keypad8), KeyChord.Of(KeyCode.Keypad9),
                }));

            // ---- Info Card dialog -> InfoCardScope ----
            // Page Up/Down keep their own ids rather than the shared tree.jump*Section pair: a
            // section here is a string field on the node (InspectionTreeItem.Description), not a
            // node flag, and Page Up lands on the FIRST row of the previous section, which
            // TreeRegionScope's IsSectionBoundary walk cannot express. "infoCard.inspect" stays
            // registered but unclaimed; Alt+I comes from SharedMenuGrammar.Info.
            c.Register(InputAction.ForScreen("infoCard", "infoCard.jumpToNextCategory",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("infoCard", "infoCard.jumpToPreviousCategory",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("infoCard", "infoCard.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("infoCard", "infoCard.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            // Left/Right and the tree.* chords come from the shared grammar; the tree ids live
            // once under the "tree" pseudo-screen key (Part7.cs).

            // ---- Auto-Slaughter dialog -> AutoSlaughterState ----
            // Numeric edit mode is a bespoke text-edit session; only the ENTRY is registered.
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.editValue",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.setToZero",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.setToUnlimited",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.decreaseBy10",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.decreaseBy100",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.increaseBy10",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.increaseBy100",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            // Shifted twins are needed because "+" is Shift+Equals; see trade.quantity.increase.
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.increment",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus), KeyChord.Of(KeyCode.Equals),
                    KeyChord.Of(KeyCode.Plus, shift: true), KeyChord.Of(KeyCode.KeypadPlus, shift: true), KeyChord.Of(KeyCode.Equals, shift: true)
                }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.decrement",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.toggleCheckbox",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("autoSlaughter", "autoSlaughter.returnToAnimalsMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab), KeyChord.Of(KeyCode.Tab, shift: true) }));

            // ---- Baby Gene Inspection -> GeneInspectionScope ----
            // PageUp/PageDown stay bespoke: they scan to the next/previous gene row or
            // sub-category, not to an IsSectionBoundary node. Alt+I stays unclaimed (silence,
            // not statBreakdown's "no info card available"): the tree wires no OnInfo.
            c.Register(InputAction.ForScreen("geneInspection", "geneInspection.jumpToNextGene",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("geneInspection", "geneInspection.jumpToPreviousGene",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));

            // ---- Growth Moment dialog -> GrowthMomentState ----
            c.Register(InputAction.ForScreen("growthMoment", "growthMoment.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("growthMoment", "growthMoment.nextTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("growthMoment", "growthMoment.previousTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            c.Register(InputAction.ForScreen("growthMoment", "growthMoment.toggleSelection",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // ---- Xenogerm Creation dialog -> XenogermScope ----
            // Region cycling, expand/collapse and Space come from the shared grammar.
            c.Register(InputAction.ForScreen("xenogerm", "xenogerm.startCombining",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("xenogerm", "xenogerm.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("xenogerm", "xenogerm.jumpToNextGenepack",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("xenogerm", "xenogerm.jumpToPreviousGenepack",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("xenogerm", "xenogerm.firstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("xenogerm", "xenogerm.lastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));

            // ---- Xenotype Editor dialog -> XenotypeEditorScope ----
            // Same shared-grammar split as xenogerm.* above. RightBracket is deliberately not
            // registered: the colonist-orders "]" rung's `!FocusStack.AnyLiveModal` guard already
            // stands down once this scope is live and modal.
            c.Register(InputAction.ForScreen("xenotypeEditor", "xenotypeEditor.saveAndApply",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("xenotypeEditor", "xenotypeEditor.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("xenotypeEditor", "xenotypeEditor.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("xenotypeEditor", "xenotypeEditor.jumpToNextTopLevel",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("xenotypeEditor", "xenotypeEditor.jumpToPreviousTopLevel",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("xenotypeEditor", "xenotypeEditor.firstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("xenotypeEditor", "xenotypeEditor.lastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));

            // ---- Faction Landing dialog -> FactionLandingScope ----
            // Tree chords come from the "tree" block in Part7.cs. Page Up/Down always reject: the
            // faction tree flags no section boundaries. Delete is not registered — the tree wires
            // no OnDelete, so it would be a silent no-op.
            c.Register(InputAction.ForScreen("factionLanding", "factionLanding.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // DEV-mode "Show all" toggle (RightBracket, the shared context-menu chord).
            c.Register(InputAction.ForScreen("factionLanding", "factionLanding.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- Dialog_Slider -> SliderDialogState ----
            // A modal integer picker, not a list: the arrows and Home/End adjust the value.
            c.Register(InputAction.ForScreen("sliderDialog", "sliderDialog.increment",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow) }));
            c.Register(InputAction.ForScreen("sliderDialog", "sliderDialog.decrement",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow) }));
            c.Register(InputAction.ForScreen("sliderDialog", "sliderDialog.incrementLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("sliderDialog", "sliderDialog.decrementLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("sliderDialog", "sliderDialog.jumpToMin",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home) }));
            c.Register(InputAction.ForScreen("sliderDialog", "sliderDialog.jumpToMax",
                new List<KeyChord> { KeyChord.Of(KeyCode.End) }));

            // ---- Anomaly Settings dialog -> AnomalySettingsScope ----
            // A flat single-column list, so Left/Right adjust the current row's value rather than
            // moving a column cursor, and are therefore screen-specific ids.
            c.Register(InputAction.ForScreen("anomalySettings", "anomalySettings.accept",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("anomalySettings", "anomalySettings.setStandardPlaystyle",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("anomalySettings", "anomalySettings.decreaseValue",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            c.Register(InputAction.ForScreen("anomalySettings", "anomalySettings.increaseValue",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("anomalySettings", "anomalySettings.decreaseValueLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, shift: true) }));
            c.Register(InputAction.ForScreen("anomalySettings", "anomalySettings.increaseValueLarge",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, shift: true) }));
            c.Register(InputAction.ForScreen("anomalySettings", "anomalySettings.toggle",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));

            // World object selection registers nothing: it is all shared tree grammar, and Delete
            // would be a no-op (no node wires OnDelete). Tree ids are global in ActionCatalog, so
            // they are registered exactly once, in Part7.cs's "tree" block.

            // ---- caravan inspect screen -> CaravanInspectState ----
            // Alt+I opens a stat breakdown or nested info card and Delete abandons the focused
            // pawn/item, so neither is the tree grammar's no-op. caravanInspect.inspect stays
            // registered but unclaimed, so any existing rebind survives.
            c.Register(InputAction.ForScreen("caravanInspect", "caravanInspect.showMood",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("caravanInspect", "caravanInspect.showNeeds",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("caravanInspect", "caravanInspect.showHealth",
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));
            c.Register(InputAction.ForScreen("caravanInspect", "caravanInspect.showGear",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("caravanInspect", "caravanInspect.showSkills",
                new List<KeyChord> { KeyChord.Of(KeyCode.K, alt: true) }));
            c.Register(InputAction.ForScreen("caravanInspect", "caravanInspect.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("caravanInspect", "caravanInspect.abandonItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));

            // The shape selection menu registers nothing: it is a plain selection list.

            // ---- Viewing Mode (post-placement review) -> ViewingModeState ----
            // A modal map placement-review mode, not a menu. The Tab block is load-bearing on the
            // non-modal ViewingModeScope: an unclaimed Tab would fall through to the ambient
            // map.architect.toggle claim and open the architect menu over viewing mode.
            c.Register(InputAction.ForScreen("viewingMode", "viewingMode.blockTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("viewingMode", "viewingMode.placeAtCursor",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("viewingMode", "viewingMode.removeAtCursor",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space, shift: true) }));
            c.Register(InputAction.ForScreen("viewingMode", "viewingMode.addAnotherShape",
                new List<KeyChord> { KeyChord.Of(KeyCode.Equals), KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus) }));
            c.Register(InputAction.ForScreen("viewingMode", "viewingMode.removeLastSegment",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus) }));
            c.Register(InputAction.ForScreen("viewingMode", "viewingMode.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("viewingMode", "viewingMode.exit",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));

            // ---- Shape Placement (two-point selection) -> ShapePlacementState ----
            // Only 'C' is handled here; ArchitectPlacementPatch handles the rest. One action covers
            // both the paint- and plan-designator cases: one player-visible function, gated on
            // mutually exclusive designator types.
            c.Register(InputAction.ForScreen("shapePlacement", "shapePlacement.reopenColorPicker",
                new List<KeyChord> { KeyChord.Of(KeyCode.C) }));

            // ---- Line Formation Placement -> LineFormationState ----
            // A map targeting mode, not a menu; arrows pass through to map navigation.
            c.Register(InputAction.ForScreen("lineFormation", "lineFormation.placePoint",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("lineFormation", "lineFormation.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("lineFormation", "lineFormation.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));

            // The early-priority inspection routing duplicate registers nothing: the same handler
            // is inventoried under scope "inspection", and a second registration would conflict.

            // ---- stat breakdown -> StatBreakdownState ----
            // Tab is the one bespoke id: it always closes, unlike any standard tree key.
            c.Register(InputAction.ForScreen("statBreakdown", "statBreakdown.close",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));

            // ---- quantity menu -> QuantityMenuState ----
            // A numeric value picker, not a list; same treatment as SliderDialogState.
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.increaseQuantity",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow) }));
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.decreaseQuantity",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow) }));
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.jumpToMin",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home) }));
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.jumpToMax",
                new List<KeyChord> { KeyChord.Of(KeyCode.End) }));
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.increaseTen",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.decreaseTen",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.increaseHundred",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("quantityMenu", "quantityMenu.decreaseHundred",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));

            // ---- shelf linking selection -> ShelfLinkingState ----
            // A map cursor selection mode: Enter/Escape are confirm/cancel, not list grammar, and
            // arrows pass through to map navigation.
            c.Register(InputAction.ForScreen("shelfLinking", "shelfLinking.toggleAtCursor",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("shelfLinking", "shelfLinking.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("shelfLinking", "shelfLinking.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));

            // ---- transport pod selection mode -> TransportPodSelectionState ----
            // Same shape as ShelfLinkingState.
            c.Register(InputAction.ForScreen("transportPodSelection", "transportPodSelection.toggleAtCursor",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("transportPodSelection", "transportPodSelection.confirm",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            c.Register(InputAction.ForScreen("transportPodSelection", "transportPodSelection.cancel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));

            // The area selection and pawn area assignment menus register nothing: plain selection
            // lists on standard grammar.

            // ---- caravan formation dialog -> CaravanFormationState ----
            // The quantity shortcuts come from TransferableQuantityHelper and the Alt+H/M/N/G/K
            // pawn-info shortcuts from CaravanInputHelper, both shared with the sibling caravan
            // split and transport pod loading screens.
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.addMaxOrSelectAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.removeItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.send",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.toggleAutoProvision",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.showHealth",
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.showMood",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.showNeeds",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.showGear",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.showSkills",
                new List<KeyChord> { KeyChord.Of(KeyCode.K, alt: true) }));
            // Shifted twins are needed because "+" is Shift+Equals; see trade.quantity.increase.
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.increment",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus), KeyChord.Of(KeyCode.Equals),
                    KeyChord.Of(KeyCode.Plus, shift: true), KeyChord.Of(KeyCode.KeypadPlus, shift: true), KeyChord.Of(KeyCode.Equals, shift: true)
                }));
            c.Register(InputAction.ForScreen("caravanFormation", "caravanFormation.decrement",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus) }));
            // The modifier-arrow and modifier-Home/End steps are the shared transfer.* family.

            // ---- the three transfer screens, both views -> TransferScreenScope ----
            // Both views claim the swap; only the classic view claims the steps and range ends.
            c.Register(InputAction.ForScreen("transfer", "transfer.swapView",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, ctrl: true), KeyChord.Of(KeyCode.Tab, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("transfer", "transfer.quantity.increaseTen",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            c.Register(InputAction.ForScreen("transfer", "transfer.quantity.decreaseTen",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("transfer", "transfer.quantity.increaseHundred",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("transfer", "transfer.quantity.decreaseHundred",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            c.Register(InputAction.ForScreen("transfer", "transfer.quantity.max",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("transfer", "transfer.quantity.min",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));

            // ---- split caravan dialog -> SplitCaravanState ----
            // Same shape as caravanFormation minus the destination sub-mode and auto-provision.
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.addMaxOrSelectAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.removeItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.split",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.showHealth",
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.showMood",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.showNeeds",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.showGear",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.showSkills",
                new List<KeyChord> { KeyChord.Of(KeyCode.K, alt: true) }));
            // Shifted twins are needed because "+" is Shift+Equals; see trade.quantity.increase.
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.increment",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus), KeyChord.Of(KeyCode.Equals),
                    KeyChord.Of(KeyCode.Plus, shift: true), KeyChord.Of(KeyCode.KeypadPlus, shift: true), KeyChord.Of(KeyCode.Equals, shift: true)
                }));
            c.Register(InputAction.ForScreen("splitCaravan", "splitCaravan.decrement",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus) }));

            // ---- transport pod loading dialog -> TransportPodLoadingState ----
            // Same shape as caravanFormation, sharing the same two helpers.
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.addMaximum",
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.removeItem",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.accept",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.reset",
                new List<KeyChord> { KeyChord.Of(KeyCode.R, alt: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.showHealth",
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.showMood",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.showNeeds",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.showGear",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.showSkills",
                new List<KeyChord> { KeyChord.Of(KeyCode.K, alt: true) }));
            // Shifted twins are needed because "+" is Shift+Equals; see trade.quantity.increase.
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.increment",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus), KeyChord.Of(KeyCode.Equals),
                    KeyChord.Of(KeyCode.Plus, shift: true), KeyChord.Of(KeyCode.KeypadPlus, shift: true), KeyChord.Of(KeyCode.Equals, shift: true)
                }));
            c.Register(InputAction.ForScreen("transportPodLoading", "transportPodLoading.decrement",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus) }));

            // ---- ritual dialog (weddings, funerals, childbirth, conversions) -> LordJobDialogScope ----
            // Three regions: Roles, Pawns, QualityStats. Alt+I is one action across two regions
            // (inspect the current pawn, or open the current row's stat breakdown). Quality Stats
            // is on Alt+Q rather than Tab: Tab belongs to the shared region-cycle grammar, so the
            // same chord here would be a BindingConflict.
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.toggleQualityStats",
                new List<KeyChord> { KeyChord.Of(KeyCode.Q, alt: true) }));
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.toggleExtra",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.start",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.togglePawnAssignment",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.inspect",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // Pawn-info shortcuts, PawnSelection only. No Alt+K (Skills): this screen has none.
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.showHealth",
                new List<KeyChord> { KeyChord.Of(KeyCode.H, alt: true) }));
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.showMood",
                new List<KeyChord> { KeyChord.Of(KeyCode.M, alt: true) }));
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.showNeeds",
                new List<KeyChord> { KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("lordJobDialog", "lordJobDialog.showGear",
                new List<KeyChord> { KeyChord.Of(KeyCode.G, alt: true) }));

            // The dryad caste dialog registers nothing: a plain selection list. Its Ctrl/Alt block
            // is pure suppression of host shortcuts leaking through the modal, not an action.
        }
    }
}
