using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory, part 4: research/faction/ideology tabs, quest menu, wildlife/animals/mechs
    /// PawnTables, map-ambient scanner + Go To, notification menu, policy content editor.
    /// </summary>
    internal static class ShellActionInventoryPart4
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- Research menu -> ResearchMenuScope ----
            // A TreeRegionScope subclass; the per-screen jumpAbsoluteFirst/jumpAbsoluteLast/
            // expandAllSiblings triplicate is retired in favor of the shared tree.* ids at
            // unchanged chords. research.infoCard/research.contextMenu stay bespoke: the shared
            // tree base does not claim Alt+I, and the dev context menu is Research-only.
            c.Register(InputAction.ForScreen("research", "research.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            // Shared tree.* ids are registered exactly once (Part7.cs "tree" pseudo-screen); ids
            // are globally unique and dispatch resolves claims by bare id.
            // DEV-mode debug actions on the selected project (RightBracket).
            c.Register(InputAction.ForScreen("research", "research.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- faction tab -> FactionTabScope ----
            // Tree grammar comes from the shared SharedMenuGrammar claims (same ids as
            // architectTree in Part7); no list/detail split, unlike Ideology.
            c.Register(InputAction.ForScreen("factionTab", "factionTab.firstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("factionTab", "factionTab.lastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));
            c.Register(InputAction.ForScreen("factionTab", "factionTab.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("factionTab", "factionTab.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            // factionTab.reannounce is RETIRED: Space rides the shared menus.activateAlias claim.
            c.Register(InputAction.ForScreen("factionTab", "factionTab.expandAllSiblings",
                new List<KeyChord> { KeyChord.Of(KeyCode.KeypadMultiply), KeyChord.Of(KeyCode.Alpha8, shift: true) }));
            // DEV-mode "Show all" toggle (RightBracket).
            c.Register(InputAction.ForScreen("factionTab", "factionTab.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- ideology tab -> IdeologyViewerScreenScope ----
            // nextPanel/previousPanel are DORMANT (Tab/Shift+Tab region-cycling is base ScreenScope
            // grammar across three regions: Ideoligions/Details/Editor) but stay registered so their
            // rebindable defaults survive. firstAbsolute/lastAbsolute/jumpToPreviousSection/
            // jumpToNextSection/expandAllSiblings are still claimed, Details-region only. contextMenu
            // is DORMANT: the dev toggles are Buttons-region checkbox rows now.
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.nextPanel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) })); // DORMANT — base region cycling
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.previousPanel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) })); // DORMANT — base region cycling
            // ideologyTab.reannounce is RETIRED: Space rides the shared menus.activateAlias claim.
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.firstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.lastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.expandAllSiblings",
                new List<KeyChord> { KeyChord.Of(KeyCode.KeypadMultiply), KeyChord.Of(KeyCode.Alpha8, shift: true) }));
            // DEV-mode "Show all" / "Edit mode" toggles (RightBracket).
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) })); // DORMANT — see above
            // First-letter mnemonic on "Save".Translate().
            c.Register(InputAction.ForScreen("ideologyTab", "ideologyTab.save",
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));

            // ---- Dialog_IdeosDuringLanding -> IdeosDuringLandingScope ----
            // Same two-panel shape as ideologyTab (this dialog calls the SAME
            // IdeoUIUtility.DoIdeoListAndDetails with identical arguments), given its own ids
            // because it is a real window-attached scope rather than the world tab's windowless
            // mirror; same default chords.
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.nextPanel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.previousPanel",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            // ideosLanding.reannounce is RETIRED: Space rides the shared menus.activateAlias claim.
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.firstAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true) }));
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.lastAbsolute",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, ctrl: true) }));
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.jumpToPreviousSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.jumpToNextSection",
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.expandAllSiblings",
                new List<KeyChord> { KeyChord.Of(KeyCode.KeypadMultiply), KeyChord.Of(KeyCode.Alpha8, shift: true) }));
            // DEV-mode "Show all" / "Edit mode" toggles (RightBracket).
            c.Register(InputAction.ForScreen("ideosLanding", "ideosLanding.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- quest menu -> QuestMenuState ----
            // Handled inline across list / detail / reward-prefs / reward-choice-float sub-modes.
            // The Alt+A/Alt+D/Alt+I checks match with ANY extra modifier, so all four combinations
            // are registered to preserve that laxity exactly.
            c.Register(InputAction.ForScreen("quest", "quest.accept",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.A, alt: true),
                    KeyChord.Of(KeyCode.A, alt: true, shift: true),
                    KeyChord.Of(KeyCode.A, alt: true, ctrl: true),
                    KeyChord.Of(KeyCode.A, alt: true, ctrl: true, shift: true)
                }));
            c.Register(InputAction.ForScreen("quest", "quest.dismiss",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.D, alt: true),
                    KeyChord.Of(KeyCode.D, alt: true, shift: true),
                    KeyChord.Of(KeyCode.D, alt: true, ctrl: true),
                    KeyChord.Of(KeyCode.D, alt: true, ctrl: true, shift: true)
                }));
            c.Register(InputAction.ForScreen("quest", "quest.infoCard",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.I, alt: true),
                    KeyChord.Of(KeyCode.I, alt: true, shift: true),
                    KeyChord.Of(KeyCode.I, alt: true, ctrl: true),
                    KeyChord.Of(KeyCode.I, alt: true, ctrl: true, shift: true)
                }));
            c.Register(InputAction.ForScreen("quest", "quest.nextTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            c.Register(InputAction.ForScreen("quest", "quest.previousTab",
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));

            // ---- wildlife menu -> WildlifeMenuState ----
            // Column paint/sort/info ops below; Enter toggles the current cell. Ctrl+Shift+Home and
            // Ctrl+Shift+End both paint the entire column (towardFirst ignored).
            c.Register(InputAction.ForScreen("wildlife", "wildlife.paintEntireColumn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true, shift: true), KeyChord.Of(KeyCode.End, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("wildlife", "wildlife.paintToFirst",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("wildlife", "wildlife.paintToLast",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("wildlife", "wildlife.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("wildlife", "wildlife.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            // wildlife.sortByColumn RETIRED: sorting is the shared menus.sortColumn Alt+S claim
            // plus Enter on the header row, both from the ScreenScope base.
            c.Register(InputAction.ForScreen("wildlife", "wildlife.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // ---- animals menu -> AnimalsMenuState ----
            // Same paint/sort/info set as wildlife, plus Tab opens vanilla Dialog_AutoSlaughter.
            c.Register(InputAction.ForScreen("animals", "animals.paintEntireColumn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true, shift: true), KeyChord.Of(KeyCode.End, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("animals", "animals.paintToFirst",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("animals", "animals.paintToLast",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("animals", "animals.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("animals", "animals.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            // animals.sortByColumn RETIRED: same as wildlife.sortByColumn above.
            c.Register(InputAction.ForScreen("animals", "animals.autoSlaughter",
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            c.Register(InputAction.ForScreen("animals", "animals.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // ---- mechs menu -> MechsMenuState ----
            c.Register(InputAction.ForScreen("mechs", "mechs.paintEntireColumn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, ctrl: true, shift: true), KeyChord.Of(KeyCode.End, ctrl: true, shift: true) }));
            c.Register(InputAction.ForScreen("mechs", "mechs.paintToFirst",
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, shift: true) }));
            c.Register(InputAction.ForScreen("mechs", "mechs.paintToLast",
                new List<KeyChord> { KeyChord.Of(KeyCode.End, shift: true) }));
            c.Register(InputAction.ForScreen("mechs", "mechs.paintDown",
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, shift: true) }));
            c.Register(InputAction.ForScreen("mechs", "mechs.paintUp",
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, shift: true) }));
            // mechs.sortByColumn RETIRED: same as wildlife.sortByColumn above.
            c.Register(InputAction.ForScreen("mechs", "mechs.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));

            // ---- scanner search opener ----
            // Map-ambient. Z activates scanner search (also on the world map); Ctrl+Z clears the
            // active filter. Both suppress vanilla by nulling the keyCode.
            c.Register(new InputAction("map.scanner.search",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Z) }));
            c.Register(new InputAction("map.scanner.clearFilter",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Z, ctrl: true) }));

            // ---- Go To coordinate opener ----
            // Map-ambient, local map only. Suppresses vanilla by nulling the keyCode.
            c.Register(new InputAction("map.goTo",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.G, ctrl: true) }));

            // ---- scanner navigation keys ----
            // Map-ambient, local map only. PageUp/PageDown/Home/End are scanner controls here, not
            // menu paging; modifiers select the granularity.
            c.Register(new InputAction("map.scanner.nextBulk",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown, alt: true) }));
            c.Register(new InputAction("map.scanner.nextCategory",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown, ctrl: true) }));
            c.Register(new InputAction("map.scanner.nextSubcategory",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown, shift: true) }));
            c.Register(new InputAction("map.scanner.nextItem",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageDown) }));
            c.Register(new InputAction("map.scanner.previousBulk",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp, alt: true) }));
            c.Register(new InputAction("map.scanner.previousCategory",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp, ctrl: true) }));
            c.Register(new InputAction("map.scanner.previousSubcategory",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp, shift: true) }));
            c.Register(new InputAction("map.scanner.previousItem",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.PageUp) }));
            c.Register(new InputAction("map.scanner.toggleAutoJump",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Home, alt: true) }));
            c.Register(new InputAction("map.scanner.jumpToCurrent",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.Home) }));
            c.Register(new InputAction("map.scanner.readDistance",
                ActionCategory.Map,
                new List<KeyChord> { KeyChord.Of(KeyCode.End) }));

            // ---- notification menu -> NotificationMenuState ----
            // The one new id is ] = delete. Every other key reuses a SharedMenuGrammar id but is
            // not plain delegation: several carry detail-view-aware or search-suffix-aware branching
            // in thin wrapper routers on NotificationScope.
            c.Register(InputAction.ForScreen("notifications", "notifications.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));

            // ---- the policy window -> PolicyDialogScope ----
            // One scope claims all of these: the real Dialog_ManagePolicies window presents the
            // policy list, the contents editor and the buttons as regions of a single screen. The
            // ids keep their own scope keys because a screen action's id prefix and its ScopeKey
            // must agree (ShellActionInventoryTests) and renaming would drop every player's existing
            // rebinding, so these are pseudo-screen groupings like the shared tree.*/filterTree.* ids.
            //
            // readingPolicy.switchPanel is RETIRED with no replacement: Book Types and Book Effects
            // are regions now, so plain Tab/Shift+Tab already move between them.
            //
            // thingFilter.allowAll/disallowAll keep their ids and chords: they are commands
            // (shortcuts to the ClearAll/AllowAll rows' activation logic), not rows.
            c.Register(InputAction.ForScreen("thingFilter", "thingFilter.allowAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, ctrl: true) }));
            c.Register(InputAction.ForScreen("thingFilter", "thingFilter.disallowAll",
                new List<KeyChord> { KeyChord.Of(KeyCode.D, ctrl: true) }));
            // Shared tree.*/filterTree.* ids are registered exactly once (Part7.cs).
            // "+"/"-" step the focused numeric or slider cell (Left/Right stay column navigation); the shifted twins are because "+" is Shift+Equals, as in trade.quantity.increase.
            c.Register(InputAction.ForScreen("drugPolicy", "drugPolicy.settingDecrease",
                new List<KeyChord> { KeyChord.Of(KeyCode.Minus), KeyChord.Of(KeyCode.KeypadMinus) }));
            c.Register(InputAction.ForScreen("drugPolicy", "drugPolicy.settingIncrease",
                new List<KeyChord> {
                    KeyChord.Of(KeyCode.Plus), KeyChord.Of(KeyCode.KeypadPlus), KeyChord.Of(KeyCode.Equals),
                    KeyChord.Of(KeyCode.Plus, shift: true), KeyChord.Of(KeyCode.KeypadPlus, shift: true), KeyChord.Of(KeyCode.Equals, shift: true)
                }));
            // Vanilla's DoEntryRow draws a per-drug Widgets.InfoCardButton next to the drug name
            // (Dialog_ManageDrugPolicies.cs:180-186).
            c.Register(InputAction.ForScreen("drugPolicy", "drugPolicy.infoCard",
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
        }
    }
}
