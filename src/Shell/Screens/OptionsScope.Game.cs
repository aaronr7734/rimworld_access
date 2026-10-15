using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The real vanilla <see cref="Dialog_Options"/> on the shared screen chassis, attached through
    /// the ScopeForWindow mirror. Vanilla's category sidebar is functionally a tab strip — selecting
    /// a category swaps the settings pane inside the SAME window — so every category is a CONTENT
    /// REGION and Tab/Shift+Tab cycle them. Vanilla draws only ONE pane, so an unselected category
    /// has no rows; content regions stay navigable while empty (the shell default), so Tab lands
    /// there, <see cref="OnRegionChanged"/> selects the category in the dialog, and the next draw
    /// pass captures the rows.
    ///
    /// Categories come from vanilla's own def list through the same filter DoWindowContents applies
    /// (<see cref="RebuildCategories"/>), plus one synthetic "RimWorld Access" row
    /// (OptionsRwaCategory.Game.cs owns its drawing and click handling). Selecting one writes the
    /// real public Dialog_Options.selectedCategory/selectedMod fields, exactly as a mouse click on
    /// a rail row does, and the selection is read back from those fields every pass, so a mouse
    /// click leaves the keyboard where the mouse put it. Pane rows come from ListingRowCapture for
    /// every category except Mods, whose grid is hand-rolled and is read from the private
    /// cachedModsWithSettings/modFilter fields instead. Since the pane list exists only after
    /// vanilla has drawn it, a category switch arms a deferred announcement
    /// (<see cref="pendingAnnounce"/>) rather than speaking in the same frame.
    ///
    /// A slider's vanilla label already embeds the formatted current value, so the FOCUS
    /// announcement omits a Value fragment; the state-change announcement reformats one (see
    /// <see cref="FormatSliderStateValue"/>).
    ///
    /// Escape and OwnsAccept are the chassis defaults; accept ownership is load-bearing, because
    /// Dialog_Options re-tests the Accept binding in its own deferred GUI pass where the
    /// dispatcher's Event.Use() is invisible, so an unowned Enter would also close the dialog.
    /// Typeahead spans the CURRENT pane plus the Buttons region: cross-pane search would need every
    /// pane populated at once, which vanilla's one-pane draw model cannot do.
    /// </summary>
    public sealed class OptionsScope : ScreenScope
    {
        private enum PaneRowSource
        {
            Captured,
            ModEntry,
        }

        private sealed class PaneRow
        {
            public PaneRowSource Source;
            public CapturedListingRow Captured;
            public Mod ModEntry;
            public string Label = "";
        }

        private static readonly AccessTools.FieldRef<Dialog_Options, float> optionsViewRectHeightField =
            AccessTools.FieldRefAccess<Dialog_Options, float>("optionsViewRectHeight");
        internal static readonly AccessTools.FieldRef<Dialog_Options, Vector2> OptionsScrollPositionField =
            AccessTools.FieldRefAccess<Dialog_Options, Vector2>("optionsScrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_Options, IEnumerable<Mod>> cachedModsWithSettingsField =
            AccessTools.FieldRefAccess<Dialog_Options, IEnumerable<Mod>>("cachedModsWithSettings");
        private static readonly AccessTools.FieldRef<Dialog_Options, string> modFilterField =
            AccessTools.FieldRefAccess<Dialog_Options, string>("modFilter");
        private static readonly AccessTools.FieldRef<Dialog_Options, bool> hasModSettingsField =
            AccessTools.FieldRefAccess<Dialog_Options, bool>("hasModSettings");

        /// <summary>
        /// Master enable flag for the RimWorld Access category injection (rail row plus pane, in
        /// OptionsRwaCategory.Game.cs), independent of this scope's own registration. Defaults
        /// false: the injection is dormant by construction until explicitly enabled.
        /// </summary>
        public static bool InjectionEnabled = false;

        /// <summary>
        /// Arms the live (or next-attached) scope to select the injected RimWorld Access
        /// category; set by RwaModSettingsRedirectPatch, consumed on the next OnGuiPass.
        /// </summary>
        internal static bool PendingRwaSelection;

        /// <summary>Exposed for OptionsRwaCategory.Game.cs's DoOptions prefix, which needs the same private view-height field to replicate vanilla's scroll chrome.</summary>
        internal static float GetOptionsViewRectHeight(Dialog_Options instance)
        {
            return optionsViewRectHeightField(instance);
        }

        internal static void SetOptionsViewRectHeight(Dialog_Options instance, float value)
        {
            ref float height = ref optionsViewRectHeightField(instance);
            height = value;
        }

        private readonly Dialog_Options dialog;

        /// <summary>The category rail in vanilla's own draw order, one entry per content region; a null entry is the synthetic RimWorld Access row.</summary>
        private readonly List<OptionCategoryDef> categories = new List<OptionCategoryDef>();
        private readonly List<PaneRow> paneRows = new List<PaneRow>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        /// <summary>Real vanilla categories in <see cref="categories"/>, which is also the rail row index the injected row draws at.</summary>
        private int realCategoryCount;

        private bool rwaCategorySelected;

        /// <summary>The category <see cref="paneRows"/> was captured for — the one vanilla drew on the last pass, which is one pass behind a fresh Tab.</summary>
        private OptionCategoryDef paneCategory;
        private bool paneIsRwa;

        // The selection already mirrored into the region cursor, so a change made by anything else
        // (a mouse click on a rail row) is told apart from the cursor resting elsewhere.
        private OptionCategoryDef syncedCategory;
        private bool syncedRwa;
        private bool syncedAny;

        private int pendingStateChangeIndex = -1;

        /// <summary>
        /// Armed whenever the screen must speak a category landing: on focus, and on every
        /// Tab/Shift+Tab onto a pane vanilla has not drawn yet. Deferred because the pane's rows do
        /// not exist until vanilla draws the newly selected category, one pass later.
        /// </summary>
        private bool pendingAnnounce;

        /// <summary>
        /// Set instead of <see cref="pendingStateChangeIndex"/> when the activated row was a
        /// raw-captured button: that click mutates state inline with nothing opening to
        /// self-announce, and the row itself is gone from the next pass, so the announcement
        /// re-reads whatever the pane cursor lands on next.
        /// </summary>
        private bool pendingRawActionReannounce;

        public OptionsScope(Dialog_Options dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "options"; }
        }

        /// <summary>
        /// Foreign-window guard: several scopeless windows can layer above Options without closing
        /// it (the storyteller page, Dialog_ModSettings, Dialog_KeyBindings, Dialog_DefineBinding,
        /// Dialog_AddPreferredName); a window with a scope of its own masks this one through
        /// ordinary stack modality. Not a plain "top window != ours" test — the WindowStack is
        /// layer-sorted and every TOOLTIP is a Super-layer ImmediateWindow in it
        /// (Verse/ActiveTip.cs:81), so a resting mouse would mute the whole screen. Delegates to
        /// <see cref="TextDialogShared.ForeignWindowAbove"/> so its dev-tools EditWindow exemption
        /// holds here too.
        /// </summary>
        private bool ForeignWindowAbove()
        {
            return TextDialogShared.ForeignWindowAbove(dialog);
        }

        /// <summary>
        /// Fully inert while a scopeless window sits above the dialog: the chassis claims every
        /// navigation chord unconditionally, so the guard must stand the whole scope down rather
        /// than mute claims one by one. A non-live scope is skipped by the dispatcher and drops out
        /// of ShellGuards.MenuOwnsInput, so the foreign window receives its own keys.
        /// </summary>
        public override bool IsLive
        {
            get { return base.IsLive && !ForeignWindowAbove(); }
        }

        public override bool IsModal
        {
            get { return !ForeignWindowAbove(); }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// The panes draw their buttons through Listing_Standard, which delegates to
        /// Widgets.ButtonText, so capturing window buttons would present every combo box and action
        /// row twice. The dialog's one real footer button is declared instead.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get { return actions; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        /// <summary>Read by OptionsRwaCategory's DoOptions prefix to decide whether to draw the injected pane instead of vanilla's (empty) body.</summary>
        internal bool RwaCategorySelected
        {
            get { return rwaCategorySelected; }
        }

        public override void OnPush()
        {
            base.OnPush();
            // No TooltipCapture arming needed: Listing_Standard reaches TooltipHandler.TipRegion
            // unconditionally for any non-empty tooltip string (Mouse.IsOver only gates the visual
            // highlight), so the tap's own tooltip parameter is a complete channel.
        }

        /// <summary>
        /// Vanilla's PreClose already runs Prefs.Save(), so only the mod's own settings need an
        /// explicit write. Reopening the pause menu (and re-announcing the main menu at Entry) is a
        /// deliberate keyboard-only deviation: vanilla's mouse flow leaves the player on the bare
        /// map, which offers a keyboard user nothing to land on.
        /// </summary>
        public override void OnPop()
        {
            base.OnPop();
            RimWorldAccessMod_Settings.Settings?.Write();
            if (Current.ProgramState == ProgramState.Playing)
            {
                PauseMenuScope.OpenRealMenu();
            }
            else if (Current.ProgramState == ProgramState.Entry
                && Find.WindowStack.WindowOfType<Page>() == null)
            {
                // The Page guard: the mod-settings redirect can open this dialog over the mods
                // page, where that page's own refocus announcement is the right one.
                AnnounceMainMenuReturn();
            }
        }

        public override void OnFocus()
        {
            // The first utterance waits for the dialog's own draw pass: the selected category's rows
            // are capture-driven, so the chassis entry announcement would speak an empty region.
            SuppressNextEntryAnnouncement();
            base.OnFocus();
            SyncSelectionFromDialog();
            pendingAnnounce = true;
        }

        /// <summary>
        /// Re-reads the main-menu row the player lands back on, through the same roleless
        /// label-plus-position grammar MainMenuScope uses, so returning from Options sounds
        /// identical to arrowing onto that row.
        /// </summary>
        private static void AnnounceMainMenuReturn()
        {
            ListableOption selected = MenuNavigationState.GetCurrentSelection();
            if (selected == null)
            {
                return;
            }
            ElementDescription d = new ElementDescription();
            d.Label = selected.label;
            // SelectedIndex is 0-based; the datum's position fields are 1-based.
            d.PositionIndex = MenuNavigationState.SelectedIndex + 1;
            d.PositionCount = MenuNavigationState.GetCurrentColumnLabels().Count;
            TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(
                d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
        }

        // Regions: one per category, plus the declared footer button.

        protected override int ContentRegionCount
        {
            get { return categories.Count; }
        }

        protected override string ContentRegionName(int region)
        {
            if (region < 0 || region >= categories.Count)
            {
                return "";
            }
            OptionCategoryDef def = categories[region];
            return def == null ? OptionsRwaCategory.CategoryLabel : def.label.CapitalizeFirst();
        }

        protected override int ContentItemCount(int region)
        {
            return region == PaneRegion ? paneRows.Count : 0;
        }

        protected override void RefreshContent()
        {
            RebuildCategories();
            RebuildActions();
        }

        /// <summary>
        /// The live category rail: every OptionCategoryDef DoWindowContents would paint a row for,
        /// in DefDatabase order through vanilla's own filter, plus the injected row. Read from the
        /// defs rather than the rail's draw because the regions must exist BEFORE vanilla's first
        /// pass — the model needs its tabs at focus, one frame before any row is captured.
        /// </summary>
        private void RebuildCategories()
        {
            categories.Clear();
            foreach (OptionCategoryDef def in DefDatabase<OptionCategoryDef>.AllDefs)
            {
                if ((Prefs.DevMode || !def.isDev)
                    && def.modContentPack.IsOfficialMod
                    && (def != OptionCategoryDefOf.Mods || hasModSettingsField(dialog)))
                {
                    categories.Add(def);
                }
            }
            realCategoryCount = categories.Count;
            if (InjectionEnabled)
            {
                categories.Add(null);
            }
        }

        /// <summary>
        /// The dialog's one footer button, rebuilt per refresh because the General pane can change
        /// language while the dialog is open. Activation runs the button's own Close().
        /// </summary>
        private void RebuildActions()
        {
            actions.Clear();
            actions.Add(new ScreenAction("OK".Translate(), () => dialog.Close()));
        }

        /// <summary>The region whose rows <see cref="paneRows"/> holds, or -1 when nothing this rail shows was drawn.</summary>
        private int PaneRegion
        {
            get { return RegionOf(paneCategory, paneIsRwa); }
        }

        /// <summary>The region vanilla will draw next — the dialog's own current selection.</summary>
        private int SelectedRegion
        {
            get { return RegionOf(dialog.selectedCategory, rwaCategorySelected); }
        }

        private int RegionOf(OptionCategoryDef def, bool isRwa)
        {
            if (isRwa)
            {
                return InjectionEnabled && categories.Count > realCategoryCount ? realCategoryCount : -1;
            }
            return def == null ? -1 : categories.IndexOf(def);
        }

        /// <summary>
        /// A region IS a category, so landing on one performs the same public-field write
        /// DoCategoryRow's click handler does; vanilla draws the new pane and the next pass captures
        /// its rows. The Buttons region leaves the selection alone.
        /// </summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            int region = Model.RegionIndex;
            if (region < 0 || region >= categories.Count)
            {
                return;
            }
            OptionCategoryDef def = categories[region];
            rwaCategorySelected = def == null;
            dialog.selectedCategory = def;
            dialog.selectedMod = null;
            syncedCategory = def;
            syncedRwa = rwaCategorySelected;
            syncedAny = true;
        }

        /// <summary>
        /// A landing on a pane vanilla has not drawn yet arms the utterance for the draw postfix
        /// instead of announcing the region as empty.
        /// </summary>
        protected override void AnnounceRegion()
        {
            int region = Model.RegionIndex;
            if (region >= 0 && region < categories.Count && region != PaneRegion)
            {
                pendingAnnounce = true;
                return;
            }
            base.AnnounceRegion();
        }

        /// <summary>
        /// Mirrors the dialog's selection onto the region cursor, so a mouse click on a rail row is
        /// picked up like a Tab. Only an unmirrored selection moves the cursor — otherwise every
        /// pass would drag it out of the Buttons region. Silent by design.
        /// </summary>
        private void SyncSelectionFromDialog()
        {
            if (rwaCategorySelected && dialog.selectedCategory != null)
            {
                // Selecting the injected row clears selectedCategory, so a non-null value here can
                // only mean a vanilla DoCategoryRow click moved to a real category.
                rwaCategorySelected = false;
            }
            if (syncedAny && dialog.selectedCategory == syncedCategory && rwaCategorySelected == syncedRwa)
            {
                return;
            }
            syncedCategory = dialog.selectedCategory;
            syncedRwa = rwaCategorySelected;
            syncedAny = true;
            int region = SelectedRegion;
            if (region < 0 || region == Model.RegionIndex)
            {
                return;
            }
            if (Model.MoveToRegion(region).Changed)
            {
                // The match list belonged to the pane the player just left.
                TypeaheadReset();
            }
        }

        // Per-GUI-pass work, driven by the window's own draw.

        /// <summary>Runs from the DoWindowContents prefix: arms the capture engine for this pass.</summary>
        internal void BeginDrawPass()
        {
            ListingRowCapture.BeginPass(PaneRingIndex());
        }

        /// <summary>
        /// The pane row this pass should ring, or -1 for none. Row 0 while the cursor's category is
        /// undrawn — the row repopulating parks on — so a Tab switch never blinks the ring off.
        /// </summary>
        private int PaneRingIndex()
        {
            if (SelectedRegion != Model.RegionIndex)
            {
                return -1;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null)
            {
                return -1;
            }
            return region.Index < 0 ? 0 : region.Index;
        }

        /// <summary>
        /// Runs from the DoWindowContents postfix, inside the window's own GUI pass: snapshots the
        /// pane vanilla just drew, draws the injected rail row, re-derives the model, and fires any
        /// pending deferred announcement. The focus ring is drawn INLINE by ListingRowCapture's
        /// inner taps — DoOptions closes its scroll group before this postfix, so a rect captured
        /// inside it would be in the wrong coordinate space here.
        /// </summary>
        internal void OnGuiPass()
        {
            ListingRowCapture.EndPass();
            // Before the injected row's own click handling, so this pass's rows stay labelled with
            // the category vanilla actually drew them for.
            CapturePaneRows();
            if (InjectionEnabled)
            {
                DrawInjectedRailRow();
            }
            if (InjectionEnabled && PendingRwaSelection)
            {
                PendingRwaSelection = false;
                SelectRwaCategory();
            }
            else if (InjectionEnabled && dialog.selectedMod is RimWorldAccessMod_Settings)
            {
                // The Dialog_Options(initialMod) ctor path; same one-screen rule as the redirect.
                SelectRwaCategory();
            }
            RefreshModel();
            SyncSelectionFromDialog();

            if (pendingRawActionReannounce)
            {
                pendingRawActionReannounce = false;
                AnnounceCurrentItem();
            }
            else if (pendingStateChangeIndex >= 0)
            {
                int index = pendingStateChangeIndex;
                pendingStateChangeIndex = -1;
                if (index < paneRows.Count && paneRows[index].Source == PaneRowSource.Captured)
                {
                    AnnounceStateChange(paneRows[index].Captured);
                }
            }
            else if (pendingAnnounce && !ForeignWindowAbove() && !ShellDispatcherPatch.LegacyKeyboardOverlayActive())
            {
                pendingAnnounce = false;
                AnnounceRegion();
            }
        }

        /// <summary>Selects the injected category as its rail-row click does; the announcement waits for the pane's draw.</summary>
        private void SelectRwaCategory()
        {
            rwaCategorySelected = true;
            dialog.selectedCategory = null;
            dialog.selectedMod = null;
            pendingAnnounce = true;
        }

        /// <summary>
        /// The synthetic rail row, drawn right after vanilla's own rows with DoCategoryRow's
        /// geometry. While <see cref="InjectionEnabled"/> is off, rwaCategorySelected can never
        /// become true and OptionsRwaPanePatch's redirect never triggers — the injection is fully
        /// absent, not merely hidden.
        /// </summary>
        private void DrawInjectedRailRow()
        {
            Rect rect = new Rect(0f, realCategoryCount * 50f, 160f, 48f).ContractedBy(4f);
            if (OptionsRwaCategory.DrawRailRowAndHandleClick(dialog, rect, rwaCategorySelected))
            {
                rwaCategorySelected = true;
            }
        }

        // Pane content: ListingRowCapture for every category except Mods, whose hand-rolled grid is
        // invisible to that engine and gets its own reader.

        /// <summary>Snapshots the rows of the pane vanilla drew this pass, tagged with the category they belong to.</summary>
        private void CapturePaneRows()
        {
            paneIsRwa = rwaCategorySelected;
            paneCategory = paneIsRwa ? null : dialog.selectedCategory;
            paneRows.Clear();
            if (!paneIsRwa && paneCategory == OptionCategoryDefOf.Mods)
            {
                BuildModsPaneRows();
            }
            else
            {
                BuildCapturedPaneRows();
            }
        }

        private void BuildCapturedPaneRows()
        {
            IReadOnlyList<CapturedListingRow> captured = ListingRowCapture.Items;
            for (int i = 0; i < captured.Count; i++)
            {
                paneRows.Add(new PaneRow
                {
                    Source = PaneRowSource.Captured,
                    Captured = captured[i],
                    Label = captured[i].Label,
                });
            }
        }

        /// <summary>
        /// The mod grid is a hand-rolled ButtonInvisible/Label layout, invisible to
        /// ListingRowCapture, so its ring rides vanilla's per-box DrawOptionBackground call instead
        /// (<see cref="ModsGridRingOrdinal"/>) and its rows are read from the same private
        /// cachedModsWithSettings/modFilter fields Dialog_Options populates, so a mouse-driven
        /// filter is respected here too. Search stays this screen's typeahead rather than the real
        /// QuickSearchWidget's progressive filter.
        /// </summary>
        private void BuildModsPaneRows()
        {
            IEnumerable<Mod> mods = cachedModsWithSettingsField(dialog);
            if (mods == null)
            {
                return;
            }
            string filterLower = (modFilterField(dialog) ?? "").ToLowerInvariant();
            foreach (Mod mod in mods)
            {
                string category = mod.SettingsCategory();
                if (filterLower.Length > 0
                    && category.ToLowerInvariant().IndexOf(filterLower, StringComparison.Ordinal) < 0
                    && mod.Content.Name.ToLowerInvariant().IndexOf(filterLower, StringComparison.Ordinal) < 0)
                {
                    continue;
                }
                paneRows.Add(new PaneRow { Source = PaneRowSource.ModEntry, ModEntry = mod, Label = category });
            }
        }

        /// <summary>
        /// The mod box vanilla's next <c>DoModOptions</c> pass should ring, as an ordinal into its
        /// own draw order, or -1 for none; <see cref="OptionBackgroundRowCapture"/> draws it inline
        /// because the hand-rolled grid's rects exist only inside vanilla's draw. The ordinal maps
        /// to a pane row only while the previous pass's grid and this pane list agree on count — the
        /// filter changes between passes, and a stale count would ring a neighbour.
        /// </summary>
        internal int ModsGridRingOrdinal()
        {
            if (rwaCategorySelected || dialog.selectedCategory != OptionCategoryDefOf.Mods)
            {
                return -1;
            }
            int index = PaneRingIndex();
            if (index < 0 || index >= paneRows.Count || paneRows[index].Source != PaneRowSource.ModEntry)
            {
                return -1;
            }
            int drawn = OptionBackgroundRowCapture.ScreenRects(
                OptionBackgroundRowCapture.Consumer.OptionsModsGrid).Count;
            return drawn == paneRows.Count ? index : -1;
        }

        // Describing and operating a pane row.

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region != PaneRegion || index < 0 || index >= paneRows.Count)
            {
                return new ElementDescription();
            }
            PaneRow row = paneRows[index];
            ElementDescription d = new ElementDescription();
            if (row.Source == PaneRowSource.ModEntry)
            {
                d.Label = row.Label;
                d.Role = ElementRole.Button;
                return d;
            }

            CapturedListingRow captured = row.Captured;
            d.Label = captured.Label;
            d.Extras = captured.Tooltip;
            switch (captured.Kind)
            {
                case ListingRowKind.Checkbox:
                    d.Role = ElementRole.Checkbox;
                    d.Check = captured.Checked ? CheckState.Checked : CheckState.Unchecked;
                    d.Disabled = captured.Disabled;
                    break;
                case ListingRowKind.Slider:
                    d.Role = ElementRole.Slider;
                    // Value deliberately omitted: every SliderLabeled call site here builds its
                    // label as "Name: value[%]", so a Value fragment would double-speak the number.
                    d.AtMinimum = captured.SliderValue <= captured.SliderMin + 0.0001f;
                    d.AtMaximum = captured.SliderValue >= captured.SliderMax - 0.0001f;
                    break;
                case ListingRowKind.ComboBox:
                    d.Role = ElementRole.ComboBox;
                    d.Value = captured.Value;
                    break;
                case ListingRowKind.Button:
                    d.Role = ElementRole.Button;
                    break;
                default:
                    d.Role = ElementRole.None;
                    break;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region != PaneRegion || index < 0 || index >= paneRows.Count)
            {
                return;
            }
            PaneRow row = paneRows[index];
            if (row.Source == PaneRowSource.ModEntry)
            {
                // No opening announcement: the settings window's own scope announces itself on push.
                Window settingsWindow;
                if (!HugsLibCompat.TryGetSettingsWindow(row.ModEntry, out settingsWindow))
                {
                    settingsWindow = new Dialog_ModSettings(row.ModEntry);
                }
                Find.WindowStack.Add(settingsWindow);
                return;
            }

            CapturedListingRow captured = row.Captured;
            switch (captured.Kind)
            {
                case ListingRowKind.Checkbox:
                    if (captured.Disabled)
                    {
                        AnnounceCurrentItem();
                        break;
                    }
                    ListingRowCapture.RequestActivate(index);
                    pendingStateChangeIndex = index;
                    break;
                case ListingRowKind.Slider:
                    // A plain slider owns Left/Right and nothing else, so Enter re-reads the row.
                    AnnounceCurrentItem();
                    break;
                case ListingRowKind.ComboBox:
                case ListingRowKind.Button:
                    if (IsStorytellerRow(region, index) && !TutorSystem.AllowAction("ChooseStoryteller"))
                    {
                        // Vanilla's own click handler short-circuits silently behind this same
                        // AllowAction guard; a keyboard user gets a spoken rejection instead.
                        TolkHelper.Speak("RimWorldAccess.UI.Options.CannotChangeStoryteller".Loc(), SpeechPriority.High);
                        return;
                    }
                    // Silence otherwise: whatever the injected click opens announces itself. A
                    // RawSource button opens nothing and mutates Prefs inline, so this scope
                    // announces for it instead (see pendingRawActionReannounce).
                    if (captured.RawSource)
                    {
                        pendingRawActionReannounce = true;
                    }
                    ListingRowCapture.RequestActivate(index);
                    break;
                default: // Label: nothing to activate.
                    AnnounceCurrentItem();
                    break;
            }
        }

        /// <summary>
        /// Structural (never string-matched) identification of the "Change Storyteller" row:
        /// vanilla draws it only as DoGameplayOptions' first row, and only while Playing.
        /// </summary>
        private bool IsStorytellerRow(int region, int index)
        {
            return index == 0
                && region >= 0
                && region < categories.Count
                && categories[region] == OptionCategoryDefOf.Gameplay
                && Current.ProgramState == ProgramState.Playing;
        }

        /// <summary>
        /// Left/Right belong to a slider row and nothing else; every other row kind declines them so
        /// they fall through rather than being answered with a re-read of the row.
        /// </summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (region != PaneRegion || index < 0 || index >= paneRows.Count)
            {
                return false;
            }
            PaneRow row = paneRows[index];
            return row.Source == PaneRowSource.Captured && row.Captured.Kind == ListingRowKind.Slider;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            ListingRowCapture.RequestAdjust(index, direction);
            pendingStateChangeIndex = index;
        }

        /// <summary>
        /// The state-change-only announcement: an activation speaks either the state change or the
        /// re-composed element, never both. Only Checkbox/Slider rows reach this.
        /// </summary>
        private void AnnounceStateChange(CapturedListingRow row)
        {
            ElementDescription d = new ElementDescription();
            switch (row.Kind)
            {
                case ListingRowKind.Checkbox:
                    d.Check = row.Checked ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case ListingRowKind.Slider:
                    d.Value = FormatSliderStateValue(row);
                    d.AtMinimum = row.SliderValue <= row.SliderMin + 0.0001f;
                    d.AtMaximum = row.SliderValue >= row.SliderMax - 0.0001f;
                    break;
                default:
                    return;
            }
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>
        /// Reformats a slider's value for the state-change announcement, independently of vanilla's
        /// per-call display choice. min/max/roundTo carry no percentage signal, so the test is
        /// whether the captured label contains '%': percentage-styled sliders build their labels via
        /// ToStringPercent, which emits a literal '%' in every language and is plain "value*100"
        /// with no range normalization. Labels without '%' format as a plain number.
        /// </summary>
        private static string FormatSliderStateValue(CapturedListingRow row)
        {
            bool percentStyle = row.Label != null && row.Label.IndexOf('%') >= 0;
            if (percentStyle)
            {
                return Mathf.RoundToInt(row.SliderValue * 100f) + "%";
            }
            bool wholeNumber = Mathf.Approximately(row.SliderValue, Mathf.Round(row.SliderValue));
            return wholeNumber ? row.SliderValue.ToString("F0") : row.SliderValue.ToString("F1");
        }
    }

    /// <summary>
    /// Brackets ListingRowCapture to the dialog's own draw and drives the scope's per-pass refresh,
    /// focus ring, and deferred announcements inside the window's own GUI pass.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Options), "DoWindowContents")]
    public static class OptionsDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Dialog_Options __instance)
        {
            try
            {
                OptionsScope scope = FocusStack.Top as OptionsScope;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Options draw pass error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Dialog_Options __instance)
        {
            try
            {
                OptionsScope scope = FocusStack.Top as OptionsScope;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.OnGuiPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Options draw pass error", ex);
            }
        }
    }
}
