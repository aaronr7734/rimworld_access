using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Ambient base scope for the colony map: cursor arrows, tile-info digits 1-7, and the
    /// Shift+1/2/3 time-speed controls. NON-modal — unclaimed keys fall through to the legacy
    /// ladder and ultimately to vanilla (the sighted-parity guarantee for the bare map) — and it
    /// must never trip AnyLiveModal, which would force SuppressMapNavigation and starve its own
    /// arrow claims. Placement/targeting/pod/shelf/zone modes force SuppressMapNavigation=false so
    /// arrows keep flowing to MapArrowKeyHandler, which is why the arrow guard consults that flag
    /// rather than any per-mode knowledge. Digits 1-3 stay claimable whenever time keys are live
    /// even if tile info is blocked (consume-only), keeping vanilla's bare-digit speed bindings
    /// from firing; every digit handler debounces OS key repeat via Input.GetKeyDown, since the
    /// dispatcher is an OnGUI prefix that repeats with the OS.
    /// </summary>
    public sealed partial class MapScope : FocusScope
    {
        public MapScope()
        {
            // MapArrowKeyHandler re-derives move / jump / jump-mode cycle / preset-distance step
            // from the modifiers, so every arrow claim delegates with the snapshot's modifiers.
            Claim("map.cursor.north", delegate (KeyEventSnapshot e) { Arrow(KeyCode.UpArrow, e); }, when: ArrowsLive);
            Claim("map.cursor.south", delegate (KeyEventSnapshot e) { Arrow(KeyCode.DownArrow, e); }, when: ArrowsLive);
            Claim("map.cursor.west", delegate (KeyEventSnapshot e) { Arrow(KeyCode.LeftArrow, e); }, when: ArrowsLive);
            Claim("map.cursor.east", delegate (KeyEventSnapshot e) { Arrow(KeyCode.RightArrow, e); }, when: ArrowsLive);
            Claim("map.cursor.jumpNorth", delegate (KeyEventSnapshot e) { Arrow(KeyCode.UpArrow, e); }, when: ArrowsLive);
            Claim("map.cursor.jumpSouth", delegate (KeyEventSnapshot e) { Arrow(KeyCode.DownArrow, e); }, when: ArrowsLive);
            Claim("map.cursor.jumpWest", delegate (KeyEventSnapshot e) { Arrow(KeyCode.LeftArrow, e); }, when: ArrowsLive);
            Claim("map.cursor.jumpEast", delegate (KeyEventSnapshot e) { Arrow(KeyCode.RightArrow, e); }, when: ArrowsLive);
            Claim("map.jumpMode.next", delegate (KeyEventSnapshot e) { Arrow(KeyCode.UpArrow, e); }, when: ArrowsLive);
            Claim("map.jumpMode.previous", delegate (KeyEventSnapshot e) { Arrow(KeyCode.DownArrow, e); }, when: ArrowsLive);
            Claim("map.jumpMode.decreaseDistance", delegate (KeyEventSnapshot e) { Arrow(KeyCode.LeftArrow, e); }, when: ArrowsLive);
            Claim("map.jumpMode.increaseDistance", delegate (KeyEventSnapshot e) { Arrow(KeyCode.RightArrow, e); }, when: ArrowsLive);

            // Digits 1-4 stay claimable whenever time keys are live so vanilla's bare-digit speed
            // bindings stay blocked, including the dev-mode-only Ultrafast binding on bare Alpha4
            // (RimWorld/TimeControls.cs:147-154). AnnounceTileInfo's early return when tile info
            // is unavailable is what makes that fallback consume-only.
            Claim("map.tileInfo.itemsAndPawns",
                delegate (KeyEventSnapshot e) { AnnounceTileInfo(e, TileInfoHelper.GetItemsAndPawnsInfo); },
                when: delegate { return TileInfoLive() || TimeKeysLive(); });
            Claim("map.tileInfo.flooring",
                delegate (KeyEventSnapshot e) { AnnounceTileInfo(e, TileInfoHelper.GetFlooringInfo); },
                when: delegate { return TileInfoLive() || TimeKeysLive(); });
            Claim("map.tileInfo.resources",
                delegate (KeyEventSnapshot e) { AnnounceTileInfo(e, TileInfoHelper.GetPlantsInfo); },
                when: delegate { return TileInfoLive() || TimeKeysLive(); });
            Claim("map.tileInfo.brightnessAndTemp",
                delegate (KeyEventSnapshot e) { AnnounceTileInfo(e, TileInfoHelper.GetLightInfo); },
                when: delegate { return TileInfoLive() || TimeKeysLive(); });
            Claim("map.tileInfo.roomStats",
                delegate (KeyEventSnapshot e) { AnnounceTileInfo(e, TileInfoHelper.GetRoomStatsInfo); },
                when: TileInfoLive);
            Claim("map.tileInfo.power",
                delegate (KeyEventSnapshot e) { AnnounceTileInfo(e, TileInfoHelper.GetPowerInfo); },
                when: TileInfoLive);
            Claim("map.tileInfo.areas",
                delegate (KeyEventSnapshot e) { AnnounceTileInfo(e, TileInfoHelper.GetAreasInfo); },
                when: TileInfoLive);

            // Announcement comes from TimeControlAccessibilityPatch's trigger-agnostic
            // CurTimeSpeed hook, so vanilla Space stays pure vanilla and still announces.
            Claim("map.timeSpeed.normal",
                delegate (KeyEventSnapshot e) { SetTimeSpeed(TimeSpeed.Normal, e); }, when: TimeKeysLive);
            Claim("map.timeSpeed.fast",
                delegate (KeyEventSnapshot e) { SetTimeSpeed(TimeSpeed.Fast, e); }, when: TimeKeysLive);
            Claim("map.timeSpeed.superfast",
                delegate (KeyEventSnapshot e) { SetTimeSpeed(TimeSpeed.Superfast, e); }, when: TimeKeysLive);
            // TimeControls.cs:147-154 processes TimeSpeed_Ultrafast only under Prefs.DevMode,
            // so Shift+4 carries the same gate.
            Claim("map.timeSpeed.ultrafast",
                delegate (KeyEventSnapshot e) { SetTimeSpeed(TimeSpeed.Ultrafast, e); },
                when: delegate { return TimeKeysLive() && Prefs.DevMode; });

            RegisterBookmarkClaims();
            RegisterCursorHotkeyClaims();
            RegisterScannerBrowseClaims();
            RegisterMapOverlayOpenerClaims();
            RegisterMultiSelectClaims();
            RegisterColonistBarClaims();
            RegisterLineFormationClaims();
            RegisterQuickInfoClaims();
            RegisterOrderClaims();
            RegisterArchitectClaims();
            RegisterGizmoOpenerClaims();
            LearningHelperOpenerClaims.Register(this);
            RegisterTimeAnnounceClaims();
            // The bottom openers (Ctrl+V plan paste, I inventory, P prisoner tab, Escape pause
            // menu, Enter inspection) are MapScope-exclusive: WorldScope must not register them.
            RegisterEndgameOpenerClaims();
            RegisterInspectClaims();
        }

        public override string Name
        {
            get { return "map"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        private static void Arrow(KeyCode key, KeyEventSnapshot e)
        {
            MapArrowKeyHandler.HandleArrowKey(key, e.Ctrl, e.Shift);
        }

        private static bool ArrowsLive()
        {
            return WorldRendererUtility.DrawingMap
                && Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && MapNavigationState.IsInitialized
                && !MapNavigationState.SuppressMapNavigation;
        }

        /// <summary>
        /// The canonical "an arrow-capturing menu is active" signal, recomputed every frame by
        /// <c>MapNavigationPatch.UpdateSuppressionFlag()</c>. False during placement/cursor modes,
        /// which keep arrows live.
        /// </summary>
        private static bool ArrowCapturingMenuActive => MapNavigationState.SuppressMapNavigation;

        /// <summary>
        /// Overlays that intentionally keep the map cursor (arrow keys) live yet must still block
        /// the tile-info number row: the scanner search field and GoTo coordinate entry consume
        /// the digits themselves.
        /// </summary>
        private static bool NonArrowMapOverlayActive =>
            ScannerSearchState.IsActive ||
            GoToState.IsActive ||
            FishingZoneMenuState.IsActive;

        /// <summary>
        /// Placement / cursor modes that keep the map cursor live. Excluded from the base signal
        /// so tile-info still works during placement; time-speed keys stay suppressed under them
        /// (<see cref="BlockTimeSpeedKeys"/>).
        /// </summary>
        private static bool PlacementModeActive =>
            ArchitectState.IsActive ||
            ShapePlacementState.IsActive ||
            ViewingModeState.IsActive;

        /// <summary>True when the tile-info hotkeys (1-7) must NOT fire because a menu owns input.</summary>
        private static bool BlockMapInfoKeys =>
            ArrowCapturingMenuActive || NonArrowMapOverlayActive;

        /// <summary>
        /// True when the time-speed hotkeys (1/2/3 and Shift+1/2/3) must NOT fire: placement
        /// modes on top of <see cref="BlockMapInfoKeys"/>.
        /// </summary>
        private static bool BlockTimeSpeedKeys =>
            BlockMapInfoKeys || PlacementModeActive;

        private static bool TileInfoLive()
        {
            return WorldRendererUtility.DrawingMap
                && Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && MapNavigationState.IsInitialized
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !BlockMapInfoKeys;
        }

        /// <summary>
        /// True when the map surface is active and readable, i.e. the conditions under which the
        /// tile-info hotkeys describe a cell. Exposed for the mouse hover reader, which describes
        /// cells off the same surface without going through a claim.
        /// </summary>
        internal static bool MapSurfaceLive()
        {
            return TileInfoLive();
        }

        private static bool TimeKeysLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !BlockTimeSpeedKeys;
        }

        private static void AnnounceTileInfo(KeyEventSnapshot e, Func<IntVec3, Map, string> info)
        {
            // OS key repeat re-delivers KeyDown events; a held digit must not re-announce, and
            // must stay consumed so vanilla never sees it. Injected events carry no hardware key
            // state, so the dev-harness override bypasses the check.
            if (!Input.GetKeyDown(e.Key) && !KeyboardHelper.InjectionOverrideActive)
            {
                return;
            }
            // Consume-only: the digit is claimed purely to keep vanilla's speed bindings blocked
            // while tile info is unavailable.
            if (!TileInfoLive())
            {
                return;
            }
            IntVec3 position = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;
            // Fogged tiles surface nothing beyond "Undiscovered", matching vanilla's
            // MouseoverReadout short-circuit.
            TolkHelper.SpeakData(position.Fogged(map)
                ? (string)"Undiscovered".Translate()
                : info(position, map));
        }

        private static void SetTimeSpeed(TimeSpeed speed, KeyEventSnapshot e)
        {
            if (!Input.GetKeyDown(e.Key) && !KeyboardHelper.InjectionOverrideActive)
            {
                return;
            }
            // MUTATION-C: mirrors TimeControls.DoTimeControlsGUI's own bare CurTimeSpeed assignment
            // (RimWorld/TimeControls.cs:113,120,127) — the property setter itself (Verse/TickManager.cs:193-206)
            // carries the PlayerCanControl gate; there is no separate Try*/Can* method to call.
            Find.TickManager.CurTimeSpeed = speed;
            SoundDef sound;
            switch (speed)
            {
                case TimeSpeed.Fast:
                    sound = SoundDefOf.Clock_Fast;
                    break;
                case TimeSpeed.Superfast:
                case TimeSpeed.Ultrafast:
                    sound = SoundDefOf.Clock_Superfast;
                    break;
                case TimeSpeed.Paused:
                    sound = SoundDefOf.Clock_Stop;
                    break;
                default:
                    sound = SoundDefOf.Clock_Normal;
                    break;
            }
            if (sound != null)
            {
                sound.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// The F12 opener's gate. Registered on both MapScope and WorldScope: it carries no
        /// world-view term, so it fires from the planet view too.
        /// </summary>
        private static bool ExtraMenusLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !ShellGuards.MenuOwnsInput();
        }

        /// <summary>
        /// F12 extra-menus launcher, with a three-outcome contract: zero options announces there
        /// is nothing to show; exactly one speaks its label and auto-executes with no menu shown;
        /// two or more open a real FloatMenu, which FloatMenuScope then drives.
        /// </summary>
        private static void OnOpenExtraMenus(KeyEventSnapshot e)
        {
            if (WorldNavigationState.IsActive)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            List<ExtraMenuOption> options = BuildExtraMenuOptions();

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.UI.Extra.None".Loc());
                return;
            }

            if (options.Count == 1)
            {
                TolkHelper.SpeakData(options[0].Label);
                options[0].Action();
                return;
            }

            List<FloatMenuOption> floatOptions = new List<FloatMenuOption>();
            Dictionary<FloatMenuOption, string> hotkeys = new Dictionary<FloatMenuOption, string>();
            for (int i = 0; i < options.Count; i++)
            {
                ExtraMenuOption option = options[i];
                FloatMenuOption floatOption = new FloatMenuOption(option.Label, option.Action);
                // DoGUI turns this into a TipRegion (Verse/FloatMenuOption.cs:321-323), which
                // FloatMenuScope resolves into the row's description.
                if (!string.IsNullOrEmpty(option.Description))
                {
                    floatOption.tooltip = new TipSignal(option.Description);
                }
                if (!string.IsNullOrEmpty(option.Hotkey))
                {
                    hotkeys[floatOption] = option.Hotkey;
                }
                floatOptions.Add(floatOption);
            }
            KeyboardFloatMenu.Open(floatOptions, givesColonistOrders: false, optionHotkeys: hotkeys);
        }

        /// <summary>
        /// A pending extra-menu item as a plain (label, action) pair rather than a
        /// FloatMenuOption, so the single-option auto-execute path can invoke the action directly.
        /// </summary>
        private struct ExtraMenuOption
        {
            public string Label;
            /// <summary>The tab's own blurb, handed to the row as a tooltip so it announces with it.</summary>
            public string Description;
            /// <summary>The chord that also opens this, spoken through the standard Hotkey part.</summary>
            public string Hotkey;
            public Action Action;
        }

        /// <summary>
        /// Main tabs that must never appear in the F12 directory because there is no plain tab
        /// toggle behind them to offer. Inspect is selection-driven and not a visible button;
        /// Menu is the Escape pause menu; Architect's button opens vanilla's panel rather than
        /// the keyboard build tree (Tab is its opener). Every other main tab is listed.
        /// </summary>
        private static readonly HashSet<string> SelfServedMainTabs = new HashSet<string>
        {
            "Inspect", "Architect", "Menu",
            // PersonaDirector's MainButtonDef multiplexes plain/right/shift-click into three
            // unrelated windows inside its own Worker.Activate; the generic per-def entry can
            // only replay the plain-click branch, so three explicit entries are appended below.
            "PersonaDirector",
        };

        /// <summary>
        /// The shell action whose chord opens each main tab the shell serves itself; vanilla's
        /// own <c>MainButtonDef.hotKey</c> covers the rest. Entries name an ACTION ID, not a
        /// chord, so a rebind moves the spoken key with it.
        /// </summary>
        private static readonly Dictionary<string, string> MainTabOpenerActions = new Dictionary<string, string>
        {
            { "Work", "map.menu.work" },
            { "Schedule", "map.menu.schedule" },
            { "Assign", "map.menu.assign" },
            { "Animals", "map.menu.animalsMechs" },
            { "Mechs", "map.menu.animalsMechs" },
            { "Research", "map.menu.research" },
            { "Quests", "map.menu.quests" },
        };

        /// <summary>The chord that opens this tab, or null when nothing but this menu does.</summary>
        private static string HotkeyForMainTab(MainButtonDef def)
        {
            string actionId;
            InputAction action;
            if (MainTabOpenerActions.TryGetValue(def.defName, out actionId)
                && ActionRegistry.Catalog.TryGet(actionId, out action)
                && action.Bindings.Count > 0)
            {
                return action.Bindings[0].DisplayLabel;
            }
            // A non-null KeyBindingDef bound to KeyCode.None opens nothing: some mods
            // (Character Editor's "HotkeyEditor") attach one even with no key assigned.
            string hotkeyLabel = VanillaBindings.HotkeyLabel(def.hotKey);
            if (hotkeyLabel != null)
            {
                return hotkeyLabel;
            }
            return null;
        }

        private static List<ExtraMenuOption> BuildExtraMenuOptions()
        {
            List<ExtraMenuOption> options = new List<ExtraMenuOption>();

            IEnumerable<MainButtonDef> ordered = DefDatabase<MainButtonDef>.AllDefs
                .OrderBy(d => d.order)
                .ThenBy(d => d.defName);

            foreach (MainButtonDef def in ordered)
            {
                MainButtonDef mainButton = def;

                if (SelfServedMainTabs.Contains(mainButton.defName))
                {
                    continue;
                }

                // The game's own visibility decision (covers buttonVisible and the classic-ideo
                // gate). A broken modded worker must not take the whole menu down with it.
                bool visible;
                try
                {
                    visible = mainButton.Worker.Visible;
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"MainButtonDef '{mainButton.defName}' Worker.Visible threw: {ex.Message}");
                    continue;
                }

                if (!visible)
                {
                    continue;
                }

                string label = MainButtonRectRegistry.LabelOf(mainButton);

                // A modded tab's window has no bespoke scope and is often deliberately
                // non-absorbing, so plain activation would open a surface the shell never reads.
                // Official tabs keep the plain path: their windows have bespoke handling.
                bool moddedTab = mainButton.modContentPack != null
                    && !mainButton.modContentPack.IsOfficialMod;
                Action activate;
                if (moddedTab)
                {
                    activate = delegate { OpenModdedMainTab(mainButton); };
                }
                else
                {
                    activate = delegate { mainButton.Worker.Activate(); };
                }
                options.Add(new ExtraMenuOption
                {
                    Label = label,
                    Description = mainButton.description,
                    Hotkey = HotkeyForMainTab(mainButton),
                    Action = activate
                });
            }

            if (ModsConfig.AnomalyActive)
            {
                options.Add(new ExtraMenuOption
                {
                    Label = "ViewEntityCodex".Translate().ToString(),
                    Action = delegate { Find.WindowStack.Add(new Dialog_EntityCodex()); }
                });
            }

            options.Add(new ExtraMenuOption
            {
                Label = "RimWorldAccess.GlobalLog.Title".Translate().ToString(),
                Hotkey = GlobalLogWindow.OpenerHotkey(),
                Action = GlobalLogWindow.Open
            });

            // The Dialogue Log, present whenever either mod that feeds it is active: Bubbles-only
            // installs still get vanilla interaction-bubble lines, and RimTalk hard-depends on
            // Bubbles anyway.
            if (ModsConfig.IsActive("jaxe.bubbles") || ModsConfig.IsActive("cj.rimtalk"))
            {
                options.Add(new ExtraMenuOption
                {
                    Label = "RimWorldAccess.Narrative.DialogueLog.Title".Translate().ToString(),
                    Action = delegate
                    {
                        if (!Find.WindowStack.IsOpen<DialogueLogWindow>())
                        {
                            Find.WindowStack.Add(new DialogueLogWindow());
                        }
                    }
                });
            }

            // PersonaDirector's three-way opener; each entry mirrors one branch of
            // MainButtonWorker_Director.Activate exactly.
            if (ModsConfig.IsActive(PersonaDirectorCompat.PackageId))
            {
                options.Add(new ExtraMenuOption
                {
                    Label = PersonaDirectorCompat.F12NotesLabel(),
                    Action = PersonaDirectorCompat.ToggleNotesWindow
                });
                options.Add(new ExtraMenuOption
                {
                    Label = PersonaDirectorCompat.F12BatchLabel(),
                    Action = PersonaDirectorCompat.ToggleBatchWindow
                });
                options.Add(new ExtraMenuOption
                {
                    Label = PersonaDirectorCompat.F12SettingsLabel(),
                    Action = PersonaDirectorCompat.OpenSettings
                });
            }
            if (Prefs.DevMode)
            {
                options.Add(new ExtraMenuOption
                {
                    Label = "RimWorldAccess.UI.Extra.Development".Translate().ToString(),
                    Action = OpenDevelopmentMenu
                });
            }

            return options;
        }

        /// <summary>
        /// F12 activation for a MODDED main tab: arm <see cref="ScopeForWindow.ArmDeliberateGenericAttach"/>
        /// so the generic reader accepts whatever non-absorbing, scope-less window opens, then
        /// ride the def's own <c>Worker.Activate()</c>. An already-open window is re-attached
        /// first, because a mod may only bring its existing window to front on re-activation,
        /// adding nothing the WindowStack add-hook could see.
        /// </summary>
        private static void OpenModdedMainTab(MainButtonDef def)
        {
            ScopeForWindow.ArmDeliberateGenericAttach();
            AttachExistingScopelessForeignWindows();
            def.Worker.Activate();
        }

        /// <summary>
        /// Replays the scope attach for already-open windows the armed watch would accept.
        /// MainTabWindow subclasses are excluded here (unlike in the watch itself, which needs
        /// them for trampoline bridges): pre-existing ones are the game's own surfaces with
        /// bespoke handling, most notably the inspect pane, which is open whenever something is
        /// selected and must never be grabbed by a generic modal scope.
        /// </summary>
        private static void AttachExistingScopelessForeignWindows()
        {
            IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (windows == null)
            {
                return;
            }
            for (int i = 0; i < windows.Count; i++)
            {
                Window w = windows[i];
                if (w is MainTabWindow
                    || ScopeForWindow.HasAttachedScope(w)
                    || !ScopeForWindow.GenericReaderEligible(w))
                {
                    continue;
                }
                ScopeForWindow.AttachOnDemand(w);
            }
        }

        // ------------------------------------------------------------------
        // Dev-mode debug destinations (F12 > Development). Each entry rides the game's own
        // DebugWindowsOpener method (reflection-invoked, most being private) or mirrors the exact
        // call its vanilla keybinding handler makes. Entry labels name what the entry OPENS, not
        // vanilla's "Toggle ..." keybinding wording. Openers run through
        // DevActionOutcome.RunAndAnnounce so a silent or throwing action still speaks a result;
        // god mode and advance-tick carry their own announcers and stay unwrapped.
        // ------------------------------------------------------------------

        private static readonly MethodInfo ToggleDebugActionsMenuMethod =
            AccessTools.Method(typeof(DebugWindowsOpener), "ToggleDebugActionsMenu");
        private static readonly MethodInfo ToggleDebugSettingsMenuMethod =
            AccessTools.Method(typeof(DebugWindowsOpener), "ToggleDebugSettingsMenu");
        private static readonly MethodInfo ToggleDebugLogMenuMethod =
            AccessTools.Method(typeof(DebugWindowsOpener), "ToggleDebugLogMenu");
        private static readonly MethodInfo ToggleLogWindowMethod =
            AccessTools.Method(typeof(DebugWindowsOpener), "ToggleLogWindow");
        private static readonly MethodInfo ToggleDebugInspectorMethod =
            AccessTools.Method(typeof(DebugWindowsOpener), "ToggleDebugInspector");
        private static readonly MethodInfo ToggleTweakValuesMenuMethod =
            AccessTools.Method(typeof(DebugWindowsOpener), "ToggleTweakValuesMenu");
        private static readonly MethodInfo ToggleGodModeMethod =
            AccessTools.Method(typeof(DebugWindowsOpener), "ToggleGodMode");

        private static void OpenDevelopmentMenu()
        {
            List<FloatMenuOption> options = BuildDevDestinations();
            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.UI.Extra.None".Loc());
                return;
            }
            KeyboardFloatMenu.Open(options, givesColonistOrders: false);
        }

        private static List<FloatMenuOption> BuildDevDestinations()
        {
            var options = new List<FloatMenuOption>();
            bool playing = Current.ProgramState == ProgramState.Playing;

            // Pinned dev-palette actions lead the menu so a keyboard user reaches a favorite in
            // one keystroke, the same speed the sighted Dialog_DevPalette gives.
            AddPinnedPaletteOptions(options);

            options.Add(DevDestination("RimWorldAccess.Dev.Menu.DebugActions",
                delegate { InvokeOpener(ToggleDebugActionsMenuMethod); }));
            options.Add(DevDestination("RimWorldAccess.Dev.Menu.DebugSettings",
                delegate { InvokeOpener(ToggleDebugSettingsMenuMethod); }));
            options.Add(DevDestination("RimWorldAccess.Dev.Menu.DebugOutputs",
                delegate { InvokeOpener(ToggleDebugLogMenuMethod); }));
            options.Add(DevDestination("RimWorldAccess.Dev.Menu.DebugLog", OpenDebugLog));
            options.Add(DevDestination("RimWorldAccess.Dev.Menu.DebugInspector", OpenDebugInspector));
            options.Add(DevDestination("RimWorldAccess.Dev.TweakValues", OpenTweakValues));
            if (playing)
            {
                // The toggle's own Harmony postfix announces the new state, so this entry rides a
                // bare opener invoke with no wrapper to avoid double-speak.
                options.Add(new FloatMenuOption(GodModeLabel(),
                    delegate { InvokeOpener(ToggleGodModeMethod); }));
                // TickOnce speaks its own confirmation, so it too stays unwrapped.
                options.Add(new FloatMenuOption("RimWorldAccess.Dev.Menu.AdvanceTick".Translate().ToString(),
                    TickOnce));
            }

            return options;
        }

        /// <summary>
        /// A destination entry whose activation runs through
        /// <see cref="DevActionOutcome.RunAndAnnounce"/>: silent when the opener's window/scope
        /// announces itself, speaking a result otherwise (including a caught throw).
        /// </summary>
        private static FloatMenuOption DevDestination(string labelKey, Action action)
        {
            string label = labelKey.Translate().ToString();
            return new FloatMenuOption(label,
                delegate { DevActionOutcome.RunAndAnnounce(label, action); });
        }

        /// <summary>The god-mode entry label, naming the state activation will switch TO.</summary>
        private static string GodModeLabel()
        {
            return (DebugSettings.godMode
                ? "RimWorldAccess.Dev.GodModeOff"
                : "RimWorldAccess.Dev.GodModeOn").Translate().ToString();
        }

        /// <summary>
        /// Prepends a FloatMenuOption for each resolvable path in Prefs.DebugActionsPalette.
        /// <c>Dialog_Debug.GetNode</c> calls <c>TrySetupNodeGraph</c> internally, so no separate
        /// setup call is needed. A path that no longer resolves is skipped silently; the prefs
        /// list is NOT mutated here, since pruning stale entries is Dialog_DevPalette's job.
        /// </summary>
        private static void AddPinnedPaletteOptions(List<FloatMenuOption> options)
        {
            List<string> palette = Prefs.DebugActionsPalette;
            if (palette == null)
            {
                return;
            }
            for (int i = 0; i < palette.Count; i++)
            {
                DebugActionNode node = Dialog_Debug.GetNode(palette[i]);
                if (node == null)
                {
                    continue;
                }
                options.Add(PinnedPaletteOption(node));
            }
        }

        /// <summary>
        /// One pinned action as a FloatMenuOption. A checkbox pin (settingsField set) speaks its
        /// current state in the label and, on activation, invokes vanilla's own toggle delegate
        /// then announces the new state. Any other node rides <c>DebugActionNode.Enter</c> with a
        /// null dialog, whose <c>dialog == null</c> branch news up the window itself: it opens a
        /// fresh Dialog_Debug and navigates INTO a node with children, executes a plain leaf, or
        /// arms a debug tool (announced by the DevToolTargeting mirror).
        /// </summary>
        private static FloatMenuOption PinnedPaletteOption(DebugActionNode node)
        {
            if (node.settingsField != null)
            {
                bool on = (bool)node.settingsField.GetValue(null);
                string label = node.LabelNow + ". " + CheckStateWord(on);
                return new FloatMenuOption(label, delegate
                {
                    node.action?.Invoke();
                    node.DirtyLabelCache();
                    bool nowOn = (bool)node.settingsField.GetValue(null);
                    TolkHelper.SpeakData(node.LabelNow + ". " + CheckStateWord(nowOn) + ".");
                });
            }
            return new FloatMenuOption(node.LabelNow, delegate
            {
                node.TrySetupChildren();
                // The wrapper announces whichever outcome the DevDebug/DevTool surfaces do not.
                DevActionOutcome.RunAndAnnounce(node.LabelNow, delegate { node.Enter(null); });
            });
        }

        private static string CheckStateWord(bool on)
        {
            return (on
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
        }

        private static void InvokeOpener(MethodInfo method)
        {
            DebugWindowsOpener opener = Find.UIRoot == null ? null : Find.UIRoot.debugWindowOpener;
            if (opener != null && method != null)
            {
                method.Invoke(opener, null);
            }
        }

        /// <summary>
        /// F12 &gt; Development "Toggle debug log": focus the accessible log reader. This never
        /// toggles the window closed — the log auto-opens on errors, and a user pressing this
        /// while that error pop is up wants to READ it. When the window is closed, vanilla's
        /// opener adds it and the WindowStack add-hook attaches the scope; when it is already
        /// open, <see cref="ScopeForWindow.AttachOnDemand"/> replays the attach.
        /// </summary>
        private static void OpenDebugLog()
        {
            LudeonTK.EditWindow_Log existing =
                Find.WindowStack == null ? null : Find.WindowStack.WindowOfType<LudeonTK.EditWindow_Log>();
            if (existing == null)
            {
                InvokeOpener(ToggleLogWindowMethod);
            }
            else
            {
                ScopeForWindow.AttachOnDemand(existing);
            }
        }

        /// <summary>
        /// F12 &gt; Development "Tweak values": focus the accessible tweak-values editor. Like
        /// <see cref="OpenDebugLog"/> this never toggles the window closed. Arm
        /// <see cref="DevTweakValuesScope"/> and let vanilla's opener add the window, or replay
        /// the attach when it is already open but scopeless. The flag is always cleared in a
        /// finally so a non-armed open can never inherit it.
        /// </summary>
        private static void OpenTweakValues()
        {
            LudeonTK.EditWindow_TweakValues existing =
                Find.WindowStack == null ? null : Find.WindowStack.WindowOfType<LudeonTK.EditWindow_TweakValues>();
            DevTweakValuesScope.Arming = true;
            try
            {
                if (existing == null)
                {
                    InvokeOpener(ToggleTweakValuesMenuMethod);
                }
                else
                {
                    ScopeForWindow.AttachOnDemand(existing);
                }
            }
            finally
            {
                DevTweakValuesScope.Arming = false;
            }
        }

        /// <summary>
        /// F12 &gt; Development "Toggle debug inspector": focus the accessible inspector reader.
        /// Like <see cref="OpenTweakValues"/> this never toggles the window closed, arms
        /// <see cref="DevInspectorScope"/> before vanilla's opener adds the window (or replays
        /// the attach when it is already open but scopeless), and always clears the flag in a
        /// finally so a non-armed open can never inherit it.
        /// </summary>
        private static void OpenDebugInspector()
        {
            LudeonTK.EditWindow_DebugInspector existing =
                Find.WindowStack == null ? null : Find.WindowStack.WindowOfType<LudeonTK.EditWindow_DebugInspector>();
            DevInspectorScope.Arming = true;
            try
            {
                if (existing == null)
                {
                    InvokeOpener(ToggleDebugInspectorMethod);
                }
                else
                {
                    ScopeForWindow.AttachOnDemand(existing);
                }
            }
            finally
            {
                DevInspectorScope.Arming = false;
            }
        }

        /// <summary>
        /// Mirrors TimeControls' Dev_TickOnce handler: a single manual tick only advances while
        /// the game is paused.
        /// </summary>
        private static void TickOnce()
        {
            TickManager tickManager = Find.TickManager;
            if (tickManager == null || tickManager.CurTimeSpeed != TimeSpeed.Paused)
            {
                return;
            }
            tickManager.DoSingleTick();
            SoundDefOf.Clock_Stop.PlayOneShotOnCamera();
            TolkHelper.SpeakData("RimWorldAccess.Dev.TickedOnce".Translate().ToString());
        }
    }

    /// <summary>
    /// Ambient base scope for the world (planet) view; see <see cref="MapScope"/> for the general
    /// ambient-base ground rules. AmbientScopeSelector.Reconcile swaps the focus-stack base to
    /// this scope whenever the planet is rendered, removing MapScope from the stack entirely, so
    /// any shared claim must be re-registered here for parity — and IsLive must be true for those
    /// claims to reach FocusStackCore.Dispatch, since a shadow scope's claims are skipped
    /// outright. Its own claims live in the partials: <see cref="RegisterNavClaims"/> (cursor
    /// arrows, planet-layer cycle, F8 dismiss, tile-info digits, key-blackout consume set, C/']'
    /// side doors) and <see cref="RegisterScannerClaims"/> (world scanner + caravan cycle).
    /// </summary>
    public sealed partial class WorldScope : FocusScope
    {
        /// <summary>
        /// The shared world-map element (cursor arrows, tile-info digits, scanner browse/jump),
        /// registered from one definition this scope shares with the starting-site screen. Each
        /// family carries THIS scope's own gate.
        /// </summary>
        private readonly WorldMapElement map = new WorldMapElement(new WorldMapElementProfile
        {
            CursorLive = ArrowsLive,
            // Deliberate omission: the in-game world view offers no Ctrl+arrow biome jump.
            BiomeJumpLive = null,
            TileInfoLive = TileInfoDigitsLive,
            ScannerLive = WorldScannerBrowseLive,
            // Plain Space in game is vanilla's pause, so the world view must not claim it.
            ReadTileLive = null,
            JumpToNearestCaravanIsSilentNoOp = false,
        });

        public WorldScope()
        {
            MapScope.RegisterQuickInfoClaims(this);
            MapScope.RegisterDraftClaim(this);
            MapScope.RegisterGizmoOpenClaim(this);
            RegisterNavClaims();
            RegisterScannerClaims();
            MapScope.RegisterScannerSearchOpenerClaims(this);
            RegisterRoutePlannerClaims();
            // Shift+R opens Vehicle Framework's standalone route planner selector.
            RegisterVfRoutePlannerClaims();
            // The remapped Learning Helper twin (non-US '?' key) registers here and on
            // StartingSiteScreenScope but NOT MapScope.
            LearningHelperOpenerClaims.Register(this);
            LearningHelperOpenerClaims.RegisterRemapped(this);
            // The four endgame openers (Ctrl+V/I/P/Escape) stay MapScope-exclusive and are
            // deliberately not registered here.
            MapScope.RegisterTimeAnnounceClaims(this);
        }

        public override string Name
        {
            get { return "world"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }
    }

    /// <summary>
    /// Ambient base scope for the entry scene (main menu): a <see cref="ScreenScope"/> with two
    /// content regions ("Menu" = column 0, "Links" = column 1) mirroring vanilla's two-column
    /// <c>MainMenuDrawer.DoMainMenuControls</c> layout, rebuilt every frame from
    /// <see cref="MainMenuAccessibilityPatch"/>'s capture tap via
    /// <see cref="MenuNavigationState"/>. Non-modal: unclaimed keys fall through to vanilla.
    ///
    /// <b>IsLive is dynamic, not the ScreenScope-inherited constant true</b>, and load-bearing:
    /// this scope never leaves the stack (AmbientScopeSelector keeps it as the permanent Entry
    /// base), so nothing else stops its claims from firing on a frame where vanilla ISN'T drawing
    /// the menu — a scopeless Entry page above it, <c>Screen_Credits</c> for one. Because
    /// Dispatch, OfferChar, TopCharSinkScope and WindowKeyRouter all test <c>scope.IsLive</c>
    /// first, a false IsLive is equivalent to this scope being absent for the frame, handing
    /// Escape/Enter and every other key back to vanilla. That is also why <see cref="OwnsCancel"/>
    /// keeps ScreenScope's default with no override: it is only ever consulted on a frame where
    /// IsLive is already true.
    ///
    /// Typeahead rides the shared <see cref="ScreenScope.Typeahead"/> engine: Enter ACTIVATES the
    /// match, Shift+Enter settles on it without running it. Typed characters arrive through the
    /// <see cref="CharSink"/> that engine installs, while the consume-only
    /// <c>mainMenu.blockChar</c> claim swallows the bare letter/digit KEYCODE twins plus the '*'
    /// consume (KeypadMultiply / Shift+Alpha8) — needed because only NON-modal scopes must block
    /// those explicitly; a modal scope gets the dispatcher's modal-swallow backstop for free.
    ///
    /// Left/Right switch regions like Tab/Shift+Tab (with two regions, switch and cycle are the
    /// same operation): ScreenScope's base Left/Right claims never fire here, since no main-menu
    /// row is adjustable and there is no table or Buttons region.
    ///
    /// <see cref="WrapItems"/> is force-true, a deliberate override of the mod-wide
    /// WrapNavigation setting so the columns always wrap.
    /// </summary>
    public sealed class MainMenuScope : ScreenScope
    {
        /// <summary>
        /// The one instance, constructed once by AmbientScopeSelector and never re-created, so
        /// <see cref="MenuNavigationState"/>'s CurrentColumn/SelectedIndex forwarders can read
        /// this scope's live ScreenModel cursor without their callers depending on this class.
        /// </summary>
        internal static MainMenuScope Instance { get; private set; }

        public MainMenuScope()
        {
            Instance = this;
            Claim(SharedMenuGrammar.PreviousHorizontal, delegate { MoveRegion(false); });
            Claim(SharedMenuGrammar.NextHorizontal, delegate { MoveRegion(true); });
            // Consume-only twin/star block; see class remarks. No-op handler.
            Claim("mainMenu.blockChar", delegate { });
        }

        public override string Name
        {
            get { return "main-menu"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return MenuNavigationState.IsActive; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override bool WrapItems
        {
            get { return true; }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == 0
                ? "RimWorldAccess.MainMenu.MenuRegion".Translate().ToString()
                : "RimWorldAccess.MainMenu.LinksRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return MenuNavigationState.ColumnOptions(region).Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            List<ListableOption> options = MenuNavigationState.ColumnOptions(region);
            if (index >= 0 && index < options.Count)
            {
                d.Label = options[index].label;
            }
            return d;
        }

        /// <summary>
        /// Runs the option's own vanilla delegate (vehicle A) via
        /// MenuNavigationState.ActivateSelected; the base's ActivateCurrent already stamped
        /// MarkAcceptConsumed before calling here.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            MenuNavigationState.ActivateSelected();
        }

        /// <summary>A game start's transient refocus must not speak the menu the player just left.</summary>
        public override void AfterFocusDispatch()
        {
            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                return;
            }
            base.AfterFocusDispatch();
        }

        /// <summary>Read by MenuNavigationState's CurrentColumn forwarder.</summary>
        internal int CurrentRegionIndex
        {
            get { return Model.RegionIndex; }
        }

        /// <summary>Read by MenuNavigationState's SelectedIndex forwarder.</summary>
        internal int CurrentRowIndex
        {
            get
            {
                ListModel region = Model.CurrentRegion;
                return region != null ? region.Index : -1;
            }
        }
    }

    /// <summary>
    /// Keeps exactly one ambient base scope at the bottom of the focus stack, chosen from game
    /// state each frame by the shell dispatcher: main menu (Entry), world (Playing with the
    /// planet rendered), or map. Two boundary rules, both load-bearing:
    /// - Map ↔ world swaps within Playing replace the base IN PLACE
    ///   (<see cref="FocusStackCore.SetBase"/>), never clearing the scopes above, so toggling the
    ///   world view does not tear down open overlays.
    /// - Entry ↔ Playing crossings are real teardowns. In-game save loads never cross this
    ///   boundary (Playing → Playing), which is why GameStartPatch also calls ClearToBase from
    ///   Game.FinalizeInit, the canonical in-game reset point.
    /// </summary>
    internal static class AmbientScopeSelector
    {
        private static readonly MapScope map = new MapScope();
        private static readonly WorldScope world = new WorldScope();
        private static readonly MainMenuScope mainMenu = new MainMenuScope();

        private static bool hasSeenState;
        private static ProgramState lastState;

        public static void Reconcile()
        {
            ProgramState state = Current.ProgramState;
            if (state != ProgramState.Entry && state != ProgramState.Playing)
            {
                // Loading/transition states: leave the stack alone; the next recognized state
                // (plus the FinalizeInit hook) sorts it out.
                return;
            }

            if (hasSeenState && state != lastState)
            {
                FocusStack.ClearToBase(state == ProgramState.Entry
                    ? GameBoundary.MainMenu
                    : GameBoundary.GameStart);
            }
            hasSeenState = true;
            lastState = state;

            FocusScope desired;
            if (state == ProgramState.Entry)
            {
                desired = mainMenu;
            }
            else if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                desired = world;
            }
            else
            {
                desired = map;
            }
            FocusStack.SetBase(desired);
        }
    }
}
