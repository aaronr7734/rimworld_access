using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Profile;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(MainMenuDrawer), "DoMainMenuControls")]
    public static class MainMenuAccessibilityPatch
    {
        private static bool initialized = false;
        private static bool announcedMainMenu = false;
        private static ProgramState lastAnnouncedState = ProgramState.Entry;
        private static List<ListableOption> cachedColumn0 = new List<ListableOption>();
        private static List<ListableOption> cachedColumn1 = new List<ListableOption>();

        [HarmonyPrefix]
        public static void Prefix(Rect rect, bool anyMapFiles)
        {
            // Rebuild menu structure manually (since we can't intercept the original lists)
            cachedColumn0.Clear();
            cachedColumn1.Clear();

            // Build column 0 - main menu options
            if (Current.ProgramState == ProgramState.Entry)
            {
                string tutorialLabel = ("Tutorial".CanTranslate() ? "Tutorial".Translate() : "LearnToPlay".Translate());
                cachedColumn0.Add(new ListableOption(tutorialLabel, delegate {
                    // Display accessibility message instead of launching tutorial
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "The tutorial is currently not accessible. This will be added soon.",
                        null, // buttonAText (OK button, default)
                        null, // buttonAAction (close on OK, default)
                        null, // buttonBText (no second button)
                        null, // buttonBAction
                        null, // title
                        false, // destructive
                        null, // acceptAction
                        null  // cancelAction
                    ));
                }));

                cachedColumn0.Add(new ListableOption("NewColony".Translate(), delegate {
                    Find.WindowStack.Add(new Page_SelectScenario());
                }));

                if (Prefs.DevMode)
                {
                    cachedColumn0.Add(new ListableOption("DevQuickTest".Translate(), delegate {
                        LongEventHandler.QueueLongEvent(delegate {
                            Root_Play.SetupForQuickTestPlay();
                            PageUtility.InitGameStart();
                        }, "GeneratingMap", doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
                    }));
                }
            }

            if (Current.ProgramState == ProgramState.Playing && !GameDataSaveLoader.SavingIsTemporarilyDisabled && !Current.Game.Info.permadeathMode)
            {
                cachedColumn0.Add(new ListableOption("Save".Translate(), delegate {
                    var method = AccessTools.Method(typeof(MainMenuDrawer), "CloseMainTab");
                    method.Invoke(null, null);
                    Find.WindowStack.Add(new Dialog_SaveFileList_Save());
                }));
            }

            if (anyMapFiles && (Current.ProgramState != ProgramState.Playing || !Current.Game.Info.permadeathMode))
            {
                cachedColumn0.Add(new ListableOption("LoadGame".Translate(), delegate {
                    var method = AccessTools.Method(typeof(MainMenuDrawer), "CloseMainTab");
                    method.Invoke(null, null);
                    WindowlessSaveMenuState.Open(SaveLoadMode.Load);
                }));
            }

            if (Current.ProgramState == ProgramState.Playing)
            {
                cachedColumn0.Add(new ListableOption("ReviewScenario".Translate(), delegate {
                    Find.WindowStack.Add(new Dialog_MessageBox(Find.Scenario.GetFullInformationText(), null, null, null, null, Find.Scenario.name) {
                        layer = WindowLayer.Super
                    });
                }));
            }

            cachedColumn0.Add(new ListableOption("Options".Translate(), delegate {
                var method = AccessTools.Method(typeof(MainMenuDrawer), "CloseMainTab");
                method.Invoke(null, null);
                WindowlessOptionsMenuState.Open();
            }, "MenuButton-Options"));

            if (Current.ProgramState == ProgramState.Entry)
            {
                cachedColumn0.Add(new ListableOption("Mods".Translate(), delegate {
                    Find.WindowStack.Add(new Page_ModsConfig());
                }));

                if (Prefs.DevMode && LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage && LanguageDatabase.activeLanguage.anyError)
                {
                    cachedColumn0.Add(new ListableOption("SaveTranslationReport".Translate(), LanguageReportGenerator.SaveTranslationReport));
                }

                cachedColumn0.Add(new ListableOption("Credits".Translate(), delegate {
                    Find.WindowStack.Add(new Screen_Credits());
                }));
            }

            if (Current.ProgramState == ProgramState.Playing)
            {
                if (Current.Game.Info.permadeathMode && !GameDataSaveLoader.SavingIsTemporarilyDisabled)
                {
                    cachedColumn0.Add(new ListableOption("SaveAndQuitToMainMenu".Translate(), delegate {
                        LongEventHandler.QueueLongEvent(delegate {
                            GameDataSaveLoader.SaveGame(Current.Game.Info.permadeathModeUniqueName);
                            MemoryUtility.ClearAllMapsAndWorld();
                        }, "Entry", "SavingLongEvent", doAsynchronously: false, null, showExtraUIInfo: false);
                    }));

                    cachedColumn0.Add(new ListableOption("SaveAndQuitToOS".Translate(), delegate {
                        LongEventHandler.QueueLongEvent(delegate {
                            GameDataSaveLoader.SaveGame(Current.Game.Info.permadeathModeUniqueName);
                            LongEventHandler.ExecuteWhenFinished(Root.Shutdown);
                        }, "SavingLongEvent", doAsynchronously: false, null, showExtraUIInfo: false);
                    }));
                }
                else
                {
                    cachedColumn0.Add(new ListableOption("QuitToMainMenu".Translate(), delegate {
                        if (GameDataSaveLoader.CurrentGameStateIsValuable)
                        {
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmQuit".Translate(), GenScene.GoToMainMenu, destructive: true, null, WindowLayer.Super));
                        }
                        else
                        {
                            GenScene.GoToMainMenu();
                        }
                    }));

                    cachedColumn0.Add(new ListableOption("QuitToOS".Translate(), delegate {
                        if (GameDataSaveLoader.CurrentGameStateIsValuable)
                        {
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmQuit".Translate(), Root.Shutdown, destructive: true, null, WindowLayer.Super));
                        }
                        else
                        {
                            Root.Shutdown();
                        }
                    }));
                }
            }
            else
            {
                cachedColumn0.Add(new ListableOption("QuitToOS".Translate(), Root.Shutdown));
            }

            // Build column 1 - web links (these open URLs, so we keep them simpler)
            cachedColumn1.Add(new ListableOption("FictionPrimer".Translate(), delegate { Application.OpenURL("https://rimworldgame.com/backstory"); }));
            cachedColumn1.Add(new ListableOption("LudeonBlog".Translate(), delegate { Application.OpenURL("https://ludeon.com/blog"); }));
            cachedColumn1.Add(new ListableOption("Subreddit".Translate(), delegate { Application.OpenURL("https://www.reddit.com/r/RimWorld/"); }));
            cachedColumn1.Add(new ListableOption("OfficialWiki".Translate(), delegate { Application.OpenURL("https://rimworldwiki.com"); }));
            cachedColumn1.Add(new ListableOption("TynansX".Translate(), delegate { Application.OpenURL("https://x.com/TynanSylvester"); }));
            cachedColumn1.Add(new ListableOption("TynansDesignBook".Translate(), delegate { Application.OpenURL("https://tynansylvester.com/book"); }));
            cachedColumn1.Add(new ListableOption("HelpTranslate".Translate(), delegate { Application.OpenURL("https://rimworldgame.com/helptranslate"); }));
            cachedColumn1.Add(new ListableOption("BuySoundtrack".Translate(), delegate {
                // Soundtrack submenu
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("BuySoundtrack_Classic".Translate(), delegate { Application.OpenURL("https://store.steampowered.com/app/990430/RimWorld_Soundtrack/"); }),
                    new FloatMenuOption("BuySoundtrack_Royalty".Translate(), delegate { Application.OpenURL("https://store.steampowered.com/app/1244270/RimWorld__Royalty_Soundtrack/"); }),
                    new FloatMenuOption("BuySoundtrack_Anomaly".Translate(), delegate { Application.OpenURL("https://store.steampowered.com/app/2914900/RimWorld__Anomaly_Soundtrack/"); }),
                    new FloatMenuOption("BuySoundtrack_Odyssey".Translate(), delegate { Application.OpenURL("https://store.steampowered.com/app/3689230/RimWorld__Odyssey_Soundtrack/"); })
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }));
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect, bool anyMapFiles)
        {
            // Initialize menu navigation state with our rebuilt lists
            if (cachedColumn0.Count > 0 && cachedColumn1.Count > 0)
            {
                if (!initialized)
                {
                    MenuNavigationState.Initialize(cachedColumn0, cachedColumn1);
                    MenuNavigationState.Reset();
                    initialized = true;
                }
                else
                {
                    MenuNavigationState.Initialize(cachedColumn0, cachedColumn1);
                }

                // Announce main menu when first appearing or when returning from a game
                if (Current.ProgramState == ProgramState.Entry && (!announcedMainMenu || lastAnnouncedState != ProgramState.Entry))
                {
                    announcedMainMenu = true;
                    lastAnnouncedState = ProgramState.Entry;
                    TolkHelper.Speak("Main menu", SpeechPriority.Normal);
                }
                else if (Current.ProgramState == ProgramState.Playing)
                {
                    // Reset announcement flag when in-game so it triggers again on return
                    announcedMainMenu = false;
                    lastAnnouncedState = ProgramState.Playing;
                }
            }

            // Handle keyboard input
            HandleKeyboardInput();

            // Draw highlight on selected item
            DrawSelectionHighlight(rect);
        }

        private static void HandleKeyboardInput()
        {
            if (Event.current.type != EventType.KeyDown)
                return;

            KeyCode key = Event.current.keyCode;
            var typeahead = MenuNavigationState.Typeahead;

            // Handle Home - jump to first
            if (key == KeyCode.Home)
            {
                MenuNavigationState.JumpToFirst();
                Event.current.Use();
                return;
            }

            // Handle End - jump to last
            if (key == KeyCode.End)
            {
                MenuNavigationState.JumpToLast();
                Event.current.Use();
                return;
            }

            // Handle Escape - clear search first, then close
            if (key == KeyCode.Escape)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    MenuNavigationState.AnnounceWithSearch();
                    Event.current.Use();
                    return;
                }
                // Default escape handling (if any) - let it pass through
                return;
            }

            // Handle Backspace for search
            if (key == KeyCode.Backspace && typeahead.HasActiveSearch)
            {
                var labels = MenuNavigationState.GetCurrentColumnLabels();
                if (typeahead.ProcessBackspace(labels, out int newIndex))
                {
                    if (newIndex >= 0) MenuNavigationState.SetSelectedIndex(newIndex);
                    MenuNavigationState.AnnounceWithSearch();
                }
                Event.current.Use();
                return;
            }

            // Handle * key - consume to prevent passthrough (reserved for future)
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            if (key == KeyCode.KeypadMultiply || (Event.current.shift && key == KeyCode.Alpha8))
            {
                Event.current.Use();
                return;
            }

            // Handle typeahead characters
            // Use KeyCode instead of Event.current.character (which is empty in Unity IMGUI)
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;

            if (isLetter || isNumber)
            {
                char c = isLetter ? (char)('a' + (key - KeyCode.A)) : (char)('0' + (key - KeyCode.Alpha0));
                var labels = MenuNavigationState.GetCurrentColumnLabels();
                if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
                {
                    if (newIndex >= 0)
                    {
                        MenuNavigationState.SetSelectedIndex(newIndex);
                        MenuNavigationState.AnnounceWithSearch();
                    }
                }
                else
                {
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'");
                }
                Event.current.Use();
                return;
            }

            // Handle Up/Down arrows with typeahead support (only navigate matches when there ARE matches)
            if (key == KeyCode.UpArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int prev = typeahead.GetPreviousMatch(MenuNavigationState.SelectedIndex);
                    if (prev >= 0)
                    {
                        MenuNavigationState.SetSelectedIndex(prev);
                        MenuNavigationState.AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    MenuNavigationState.MoveUp();
                }
                Event.current.Use();
                return;
            }

            if (key == KeyCode.DownArrow)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                {
                    // Navigate through matches only when there ARE matches
                    int next = typeahead.GetNextMatch(MenuNavigationState.SelectedIndex);
                    if (next >= 0)
                    {
                        MenuNavigationState.SetSelectedIndex(next);
                        MenuNavigationState.AnnounceWithSearch();
                    }
                }
                else
                {
                    // Navigate normally (either no search active, OR search with no matches)
                    MenuNavigationState.MoveDown();
                }
                Event.current.Use();
                return;
            }

            // Handle Left/Right arrows - switch column (clears search)
            if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow)
            {
                MenuNavigationState.SwitchColumn();
                Event.current.Use();
                return;
            }

            // Handle Enter - execute selected item
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                ExecuteSelectedMenuItem();
                Event.current.Use();
                return;
            }
        }

        private static void ExecuteSelectedMenuItem()
        {
            ListableOption selected = MenuNavigationState.GetCurrentSelection();
            if (selected != null && selected.action != null)
            {
                selected.action();
            }
        }

        private static void DrawSelectionHighlight(Rect menuRect)
        {
            int column = MenuNavigationState.CurrentColumn;
            int selectedIndex = MenuNavigationState.SelectedIndex;

            List<ListableOption> currentList = (column == 0) ? cachedColumn0 : cachedColumn1;

            if (selectedIndex < 0 || selectedIndex >= currentList.Count)
                return;

            // Calculate vertical position
            float yOffset = 0f;
            for (int i = 0; i < selectedIndex; i++)
            {
                yOffset += currentList[i].minHeight + 7f;
            }

            // Calculate column offset
            float xOffset = (column == 0) ? 0f : (170f + 17f);
            float width = (column == 0) ? 170f : 145f;
            float height = currentList[selectedIndex].minHeight;

            // Create highlight rect relative to menu rect
            Rect highlightRect = new Rect(
                menuRect.x + xOffset,
                menuRect.y + yOffset + 17f,
                width,
                height
            );

            // Draw highlight
            Widgets.DrawHighlight(highlightRect);
        }
    }
}
