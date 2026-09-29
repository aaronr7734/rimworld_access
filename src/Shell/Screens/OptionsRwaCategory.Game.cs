using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Injects a "RimWorld Access"
    /// row into the real Dialog_Options rail (so sighted players finally SEE
    /// the mod's own settings, not just the separate Mod Settings panel) and
    /// draws its content pane with the same Listing_Standard widgets every
    /// other category uses, so ListingRowCapture drives keyboard + speech for
    /// it with zero extra wiring. Labels reuse the canonical
    /// RimWorldAccess.Core.Settings.* / RimWorldAccess.UI.Options.* keys the
    /// Mod Settings panel already uses — never forked.
    ///
    /// Split from OptionsScope.Game.cs per the design-lock's file assignment:
    /// this file owns the injected row's drawing/click and the settings
    /// pane's content; OptionsScope owns navigation, keymap, and the shared
    /// draw-pass lifecycle (BeginDrawPass/OnGuiPass) that calls into
    /// DrawRailRowAndHandleClick every pass.
    /// </summary>
    internal static class OptionsRwaCategory
    {
        public static string CategoryLabel
        {
            get { return "RimWorldAccess.Core.Settings.Category".Translate(); }
        }

        /// <summary>
        /// Mirrors DoCategoryRow's own background/click handling exactly
        /// (same background helper, same click sound, same
        /// selectedCategory/selectedMod-clearing contract a real category
        /// row's click uses) so the synthetic row behaves identically to a
        /// real one for both mouse and keyboard. No icon: this mod ships no
        /// standalone category icon texture, so the label sits at the same
        /// 40px indent vanilla's rows use after their own 20x20 icon, keeping
        /// every rail row's label left edge aligned even though this one's
        /// icon slot is blank.
        /// </summary>
        internal static bool DrawRailRowAndHandleClick(Dialog_Options dialog, Rect rect, bool selected)
        {
            Widgets.DrawOptionBackground(rect, selected);
            bool clicked = Widgets.ButtonInvisible(rect);
            if (clicked)
            {
                dialog.selectedCategory = null;
                dialog.selectedMod = null;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 40f, rect.y, rect.width - 40f, rect.height), CategoryLabel);
            Text.Anchor = TextAnchor.UpperLeft;
            return clicked;
        }

        /// <summary>
        /// The mod's settings pane: behavior settings, then the Narrative Feed group (whose
        /// TtsAnnounceAnyway row appears only when RimTalk's TTS addon is installed). Every
        /// announcement-verbosity switch lives in the Configure Spoken Announcements screen
        /// opened below, its single source of truth. Drawn with real Listing_Standard widgets so
        /// ListingRowCapture captures every row exactly like a vanilla category's. WorkMenuView
        /// is the one enum here, and vanilla's convention for those is a ButtonTextLabeledPct
        /// that opens a real FloatMenu, so that is what this builds.
        /// </summary>
        internal static void DrawSettings(Listing_Standard listing)
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
            {
                listing.Label(CategoryLabel);
                return;
            }

            if (listing.ButtonText("RimWorldAccess.Core.Settings.ConfigureAnnouncements.Button".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ConfigureAnnouncements());
            }
            listing.Gap();

            bool wrapNavigation = settings.WrapNavigation;
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.WrapNavigation.Label".Translate(), ref wrapNavigation, null, 30f, 0.6f);
            settings.WrapNavigation = wrapNavigation;

            bool showPawnActivity = settings.ShowPawnActivityOnMap;
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.ShowPawnActivityOnMap.Label".Translate(), ref showPawnActivity, null, 30f, 0.6f);
            settings.ShowPawnActivityOnMap = showPawnActivity;

            bool showCoverInfo = settings.ShowCoverInfo;
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.ShowCoverInfo.Label".Translate(), ref showCoverInfo, null, 30f, 0.6f);
            settings.ShowCoverInfo = showCoverInfo;

            bool announceTerrain = settings.AnnounceTerrain;
            listing.CheckboxLabeled("RimWorldAccess.Core.Settings.AnnounceTerrain.Label".Translate(), ref announceTerrain, null, 30f, 0.6f);
            settings.AnnounceTerrain = announceTerrain;

            bool submenuTreeNavigation = settings.SubmenuTreeNavigation;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.SubmenuTreeNavigation.Label".Translate(),
                ref submenuTreeNavigation,
                "RimWorldAccess.Core.Settings.SubmenuTreeNavigation.Tooltip".Translate(),
                30f, 0.6f);
            settings.SubmenuTreeNavigation = submenuTreeNavigation;

            bool showRecordingSavedDialog = settings.ShowRecordingSavedDialog;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.ShowRecordingSavedDialog.Label".Translate(),
                ref showRecordingSavedDialog,
                "RimWorldAccess.Core.Settings.ShowRecordingSavedDialog.Desc".Translate(FlightRecorder.ToggleChordDisplay()),
                30f, 0.6f);
            settings.ShowRecordingSavedDialog = showRecordingSavedDialog;

            WorkMenuView currentView = settings.DefaultWorkMenuView;
            if (listing.ButtonTextLabeledPct(
                "RimWorldAccess.UI.Options.WorkMenuView.Label".Translate(),
                WorkMenuViewValueLabel(currentView),
                0.6f, TextAnchor.MiddleLeft, null, WorkMenuViewValueTooltip(currentView)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (WorkMenuView value in (WorkMenuView[])Enum.GetValues(typeof(WorkMenuView)))
                {
                    WorkMenuView localValue = value;
                    options.Add(new FloatMenuOption(WorkMenuViewValueLabel(localValue), delegate
                    {
                        if (RimWorldAccessMod_Settings.Settings != null)
                        {
                            RimWorldAccessMod_Settings.Settings.DefaultWorkMenuView = localValue;
                        }
                    })
                    {
                        tooltip = new TipSignal(WorkMenuViewValueTooltip(localValue)),
                    });
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            TradeView currentTradeView = settings.DefaultTradeView;
            if (listing.ButtonTextLabeledPct(
                "RimWorldAccess.UI.Options.TradeView.Label".Translate(),
                TradeViewValueLabel(currentTradeView),
                0.6f, TextAnchor.MiddleLeft, null, TradeViewValueTooltip(currentTradeView)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (TradeView value in (TradeView[])Enum.GetValues(typeof(TradeView)))
                {
                    TradeView localValue = value;
                    options.Add(new FloatMenuOption(TradeViewValueLabel(localValue), delegate
                    {
                        if (RimWorldAccessMod_Settings.Settings != null)
                        {
                            RimWorldAccessMod_Settings.Settings.DefaultTradeView = localValue;
                        }
                    })
                    {
                        tooltip = new TipSignal(TradeViewValueTooltip(localValue)),
                    });
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            TransferView currentTransferView = settings.DefaultTransferView;
            if (listing.ButtonTextLabeledPct(
                "RimWorldAccess.UI.Options.TransferView.Label".Translate(),
                TransferViewValueLabel(currentTransferView),
                0.6f, TextAnchor.MiddleLeft, null, TransferViewValueTooltip(currentTransferView)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (TransferView value in (TransferView[])Enum.GetValues(typeof(TransferView)))
                {
                    TransferView localValue = value;
                    options.Add(new FloatMenuOption(TransferViewValueLabel(localValue), delegate
                    {
                        if (RimWorldAccessMod_Settings.Settings != null)
                        {
                            RimWorldAccessMod_Settings.Settings.DefaultTransferView = localValue;
                        }
                    })
                    {
                        tooltip = new TipSignal(TransferViewValueTooltip(localValue)),
                    });
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            bool forcedSlowdowns = settings.AnnounceForcedSlowdowns;
            listing.CheckboxLabeled(
                "RimWorldAccess.UI.Options.ForcedSlowdowns.Label".Translate(),
                ref forcedSlowdowns,
                "RimWorldAccess.UI.Options.ForcedSlowdowns.Desc".Translate(),
                30f, 0.6f);
            settings.AnnounceForcedSlowdowns = forcedSlowdowns;

            bool pauseOnGameStart = settings.PauseOnGameStart;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.PauseOnGameStart.Label".Translate(),
                ref pauseOnGameStart,
                "RimWorldAccess.Core.Settings.PauseOnGameStart.Desc".Translate(),
                30f, 0.6f);
            settings.PauseOnGameStart = pauseOnGameStart;

            bool stayPausedAfterResearch = settings.StayPausedAfterResearch;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.StayPausedAfterResearch.Label".Translate(),
                ref stayPausedAfterResearch,
                "RimWorldAccess.Core.Settings.StayPausedAfterResearch.Desc".Translate(),
                30f, 0.6f);
            settings.StayPausedAfterResearch = stayPausedAfterResearch;

            bool stayPausedAfterTrade = settings.StayPausedAfterTrade;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.StayPausedAfterTrade.Label".Translate(),
                ref stayPausedAfterTrade,
                "RimWorldAccess.Core.Settings.StayPausedAfterTrade.Desc".Translate(),
                30f, 0.6f);
            settings.StayPausedAfterTrade = stayPausedAfterTrade;

            bool showWhatsNew = settings.ShowWhatsNewOnUpdate;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.ShowWhatsNew.Label".Translate(),
                ref showWhatsNew,
                "RimWorldAccess.Core.Settings.ShowWhatsNew.Desc".Translate(),
                30f, 0.6f);
            settings.ShowWhatsNewOnUpdate = showWhatsNew;

            bool hoverSpeech = settings.HoverSpeech;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.HoverSpeech.Label".Translate(),
                ref hoverSpeech,
                "RimWorldAccess.Core.Settings.HoverSpeech.Desc".Translate(),
                30f, 0.6f);
            // Same re-seed the Alt+Shift+M toggle does: only the next real hand
            // movement may speak after tracking turns on.
            if (hoverSpeech && !settings.HoverSpeech)
                PointerMotion.Rebase();
            settings.HoverSpeech = hoverSpeech;

            bool pointerFollowsKeyboard = settings.PointerFollowsKeyboard;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.PointerFollowsKeyboard.Label".Translate(),
                ref pointerFollowsKeyboard,
                "RimWorldAccess.Core.Settings.PointerFollowsKeyboard.Desc".Translate(
                    ActionChordDisplay("map.cursor.toggleFollowKeyboard")),
                30f, 0.6f);
            settings.PointerFollowsKeyboard = pointerFollowsKeyboard;

            bool scannerAutoJump = settings.ScannerAutoJump;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.ScannerAutoJump.Label".Translate(),
                ref scannerAutoJump,
                "RimWorldAccess.Core.Settings.ScannerAutoJump.Desc".Translate(
                    ActionChordDisplay("map.scanner.toggleAutoJump")),
                30f, 0.6f);
            settings.ScannerAutoJump = scannerAutoJump;

            bool announceSelectedPawnActivity = settings.AnnounceSelectedPawnActivity;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.AnnounceSelectedPawnActivity.Label".Translate(),
                ref announceSelectedPawnActivity,
                "RimWorldAccess.Core.Settings.AnnounceSelectedPawnActivity.Desc".Translate(),
                30f, 0.6f);
            settings.AnnounceSelectedPawnActivity = announceSelectedPawnActivity;

            bool enableCombatAutopilot = settings.EnableCombatAutopilot;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.EnableCombatAutopilot.Label".Translate(),
                ref enableCombatAutopilot,
                "RimWorldAccess.Core.Settings.EnableCombatAutopilot.Desc".Translate(),
                30f, 0.6f);
            settings.EnableCombatAutopilot = enableCombatAutopilot;

            bool undraftClearsStandingOrders = settings.UndraftClearsStandingOrders;
            listing.CheckboxLabeled(
                "RimWorldAccess.Core.Settings.UndraftClearsStandingOrders.Label".Translate(),
                ref undraftClearsStandingOrders,
                "RimWorldAccess.Core.Settings.UndraftClearsStandingOrders.Desc".Translate(),
                30f, 0.6f);
            settings.UndraftClearsStandingOrders = undraftClearsStandingOrders;

            listing.Gap();

            bool announcePawnDialogue = settings.AnnouncePawnDialogue;
            listing.CheckboxLabeled(
                "RimWorldAccess.Narrative.Settings.AnnouncePawnDialogue.Label".Translate(),
                ref announcePawnDialogue,
                "RimWorldAccess.Narrative.Settings.AnnouncePawnDialogue.Desc".Translate(),
                30f, 0.6f);
            settings.AnnouncePawnDialogue = announcePawnDialogue;

            bool announceVanillaInteractionBubbles = settings.AnnounceVanillaInteractionBubbles;
            listing.CheckboxLabeled(
                "RimWorldAccess.Narrative.Settings.AnnounceVanillaInteractionBubbles.Label".Translate(),
                ref announceVanillaInteractionBubbles,
                "RimWorldAccess.Narrative.Settings.AnnounceVanillaInteractionBubbles.Desc".Translate(),
                30f, 0.6f);
            settings.AnnounceVanillaInteractionBubbles = announceVanillaInteractionBubbles;

            // TtsAnnounceAnyway is inert (nothing to back off from) without RimTalk's
            // TTS addon installed, so the row is hidden entirely rather than shown
            // disabled -- consistent with how the rest of this pane only ever shows
            // rows that do something.
            if (ModsConfig.IsActive("nitoritech.rimtalk.tts"))
            {
                bool ttsAnnounceAnyway = settings.TtsAnnounceAnyway;
                listing.CheckboxLabeled(
                    "RimWorldAccess.Narrative.Settings.TtsAnnounceAnyway.Label".Translate(),
                    ref ttsAnnounceAnyway,
                    "RimWorldAccess.Narrative.Settings.TtsAnnounceAnyway.Desc".Translate(),
                    30f, 0.6f);
                settings.TtsAnnounceAnyway = ttsAnnounceAnyway;
            }
        }

        /// <summary>The player's current chord for an action id in spoken form, or "" when unbound.</summary>
        internal static string ActionChordDisplay(string actionId)
        {
            InputAction action;
            if (!ActionRegistry.Catalog.TryGet(actionId, out action) || action.Bindings.Count == 0)
            {
                return "";
            }
            return action.Bindings[0].DisplayLabel;
        }

        private static string WorkMenuViewValueLabel(WorkMenuView view)
        {
            return (view == WorkMenuView.Focused
                ? "RimWorldAccess.UI.Options.WorkMenuView.Focused"
                : "RimWorldAccess.UI.Options.WorkMenuView.Table").Translate();
        }

        private static string WorkMenuViewValueTooltip(WorkMenuView view)
        {
            return (view == WorkMenuView.Focused
                ? "RimWorldAccess.UI.Options.WorkMenuView.FocusedDesc"
                : "RimWorldAccess.UI.Options.WorkMenuView.TableDesc").Translate();
        }

        private static string TradeViewValueLabel(TradeView view)
        {
            return (view == TradeView.Classic
                ? "RimWorldAccess.UI.Options.TradeView.Classic"
                : "RimWorldAccess.UI.Options.TradeView.Table").Translate();
        }

        private static string TradeViewValueTooltip(TradeView view)
        {
            return (view == TradeView.Classic
                ? "RimWorldAccess.UI.Options.TradeView.ClassicDesc"
                : "RimWorldAccess.UI.Options.TradeView.TableDesc").Translate();
        }

        private static string TransferViewValueLabel(TransferView view)
        {
            return (view == TransferView.Classic
                ? "RimWorldAccess.UI.Options.TradeView.Classic"
                : "RimWorldAccess.UI.Options.TradeView.Table").Translate();
        }

        private static string TransferViewValueTooltip(TransferView view)
        {
            return (view == TransferView.Classic
                ? "RimWorldAccess.UI.Options.TransferView.ClassicDesc"
                : "RimWorldAccess.UI.Options.TransferView.TableDesc").Translate();
        }
    }

    /// <summary>
    /// Redirects Dialog_Options.DoOptions to the RimWorld Access pane when
    /// this scope's synthetic rail row is selected — vanilla's own
    /// hardcoded if/else-if chain on OptionCategoryDef reference equality
    /// can never match our injected row (it is not a real
    /// DefDatabase&lt;OptionCategoryDef&gt; entry, deliberately: an
    /// OptionCategoryDef added via our own XML would need
    /// modContentPack.IsOfficialMod to even appear in the rail loop, which a
    /// third-party mod's content pack never satisfies). Replicates the exact
    /// scroll/Listing_Standard chrome DoOptions itself uses, reusing the same
    /// private optionsScrollPosition/optionsViewRectHeight fields (via
    /// OptionsScope's FieldRef accessors) so switching between this category
    /// and a real one carries over the same one-frame-lag content-height
    /// quirk vanilla already has between its own categories, rather than
    /// introducing a new, different inconsistency.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Options), "DoOptions")]
    internal static class OptionsRwaPanePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Dialog_Options __instance, Rect inRect)
        {
            try
            {
                if (!OptionsScope.InjectionEnabled)
                {
                    return true;
                }
                OptionsScope scope = FocusStack.Top as OptionsScope;
                if (scope == null || !scope.Owns(__instance) || !scope.RwaCategorySelected)
                {
                    return true;
                }
                DrawPane(__instance, inRect);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Options RWA pane draw error", ex);
                return true;
            }
        }

        private static void DrawPane(Dialog_Options instance, Rect inRect)
        {
            float viewHeight = OptionsScope.GetOptionsViewRectHeight(instance);
            bool needsScrollbar = viewHeight > inRect.height;
            Rect outRect = new Rect(inRect);
            Rect viewRect = new Rect(outRect.x, outRect.y, outRect.width - (needsScrollbar ? 26f : 0f), viewHeight);

            ref Vector2 scrollPosition = ref OptionsScope.OptionsScrollPositionField(instance);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            Rect rect = new Rect(viewRect.x, viewRect.y, viewRect.width, 999999f);
            listing.Begin(rect);
            listing.verticalSpacing = 5f;
            listing.Gap();
            OptionsRwaCategory.DrawSettings(listing);
            OptionsScope.SetOptionsViewRectHeight(instance, listing.CurHeight);
            listing.End();
            Widgets.EndScrollView();
        }
    }

    /// <summary>
    /// One settings screen: vanilla reaches mod settings exclusively by adding a
    /// Dialog_ModSettings, so an add for THIS mod is redirected onto the injected Options &gt;
    /// RimWorld Access pane — on the already-open Dialog_Options, or on a fresh one.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    internal static class RwaModSettingsRedirectPatch
    {
        private static readonly AccessTools.FieldRef<Dialog_ModSettings, Mod> modField =
            AccessTools.FieldRefAccess<Dialog_ModSettings, Mod>("mod");

        [HarmonyPrefix]
        public static bool Prefix(Window window)
        {
            if (!OptionsScope.InjectionEnabled)
            {
                return true;
            }
            Dialog_ModSettings modSettings = window as Dialog_ModSettings;
            if (modSettings == null || !(modField(modSettings) is RimWorldAccessMod_Settings))
            {
                return true;
            }
            OptionsScope.PendingRwaSelection = true;
            if (Find.WindowStack.WindowOfType<Dialog_Options>() == null)
            {
                Find.WindowStack.Add(new Dialog_Options());
            }
            return false;
        }
    }
}
