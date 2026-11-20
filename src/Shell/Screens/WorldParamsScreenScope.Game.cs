using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for the pre-game world-parameters page
    /// (<see cref="Page_CreateWorldParams"/>), window-attached via ShellBootstrap. The factions
    /// ADD MENU rides the modal <see cref="WorldParamsAddFactionScope"/> mirror above this anchor.
    ///
    /// Both regions are dynamic — <see cref="RefreshContent"/> re-derives the rows every cycle, so
    /// a DLC-gated row is simply absent. Planet holds heterogeneous typed rows: seed (TextField),
    /// randomize seed (Button), planet coverage / map size / starting season (ComboBox, where
    /// Left/Right do NOT change the value), and the sliders. Map size and starting season are
    /// INLINED here in place of vanilla's "Advanced settings… Edit…" button, which is suppressed
    /// from the captured-extras diff. Factions holds one row per visible faction (Delete removes;
    /// scenario-locked rows refuse), an "Add factions" row, then DLC-gated warnings as read-only
    /// rows. <see cref="CaptureWindowButtons"/> is false because the page CONTENT draws its own
    /// ButtonTexts, so the Buttons region declares Back/Generate/Resets itself.
    ///
    /// <b>Wizard advance.</b> Every Enter is scope-routed, so the page's deferred Accept poll —
    /// which for THIS page queues async world generation via CanDoNext — must never fire on an
    /// Enter this scope consumed. <see cref="WorldParamsPatch_CanDoNext"/> blocks that poll while
    /// this scope or one of its windowless overlays is live and Generate has not set
    /// <see cref="AdvanceRequested"/>. Escape reaches vanilla's Back except during a typeahead
    /// search or an overlay (<see cref="WorldParamsPatch_CanDoBack"/>).
    ///
    /// <b>Seed row.</b> Randomize Seed carries no chord: bare R collided with typeahead. Enter on
    /// the row activates it.
    /// </summary>
    public sealed class WorldParamsScreenScope : ScreenScope
    {
        private enum RegionKind { Planet, Factions }

        private enum PlanetRow
        {
            Seed,
            RandomizeSeed,
            PlanetCoverage,
            Rainfall,
            Temperature,
            Population,
            LandmarkDensity,
            Pollution,
            AdvancedSettings,
        }

        private static readonly MethodInfo canDoNextMethod = AccessTools.Method(typeof(Page_CreateWorldParams), "CanDoNext");
        private static readonly MethodInfo doNextMethod = AccessTools.Method(typeof(Page), "DoNext");
        private static readonly MethodInfo canDoBackMethod = AccessTools.Method(typeof(Page), "CanDoBack");
        private static readonly MethodInfo doBackMethod = AccessTools.Method(typeof(Page), "DoBack");

        /// <summary>
        /// True only while the Generate action drives the vanilla gate-and-advance, so
        /// <see cref="WorldParamsPatch_CanDoNext"/> lets that one explicit call through while
        /// still blocking the raw keyboard Accept poll.
        /// </summary>
        internal static bool AdvanceRequested;

        /// <summary>
        /// The single live scope for the open page, so the add-menu overlay can re-announce the
        /// current Factions row on close and the page's draw prefix can mirror the seed buffer.
        /// </summary>
        internal static WorldParamsScreenScope Active { get; private set; }

        private readonly Page_CreateWorldParams page;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession seedSession = new TextFieldEditSession();
        private bool announcedOpen;

        // Rebuilt every RefreshContent so IMGUI-fresh page state and the model never drift.
        private readonly List<PlanetRow> planetRows = new List<PlanetRow>();
        private readonly List<FactionRowInfo> factionRows = new List<FactionRowInfo>();
        private readonly List<string> warnings = new List<string>();

        public WorldParamsScreenScope(Page_CreateWorldParams page)
        {
            this.page = page;

            Claim("worldParams.deleteFaction", e => DeleteCurrentFaction(), when: CurrentIsFactionRow);
            Claim("worldParams.openAddFactionMenu", e => FactionsNavigationState.OpenAddMenu(), when: InFactionsRegion);
            Claim("worldParams.resetAll", e => ResetAll());
            Claim("worldParams.resetFactions", e => ResetFactions());
            // Unguarded like modList.saveChanges/announceConfig.save — Generate is the page's
            // proceed action, always available.
            Claim("worldParams.generate", e => NextAction());
            // Escape = Back must be a dispatcher claim, not a fallthrough to
            // Page.DoBottomButtons' raw Cancel poll: that poll runs in the page's WINDOW pass,
            // which here runs AFTER the dispatcher, so an unclaimed Escape dies in the modal
            // swallow first. The base's search-clear claim wins during a typeahead search.
            Claim(SharedMenuGrammar.Cancel, e => EscapeBack(), when: () => !HasActiveTypeaheadSearch);

            RegisterPopTeardown(seedSession.CancelIfActive);
        }

        public override string Name
        {
            get { return "world-params-page"; }
        }

        /// <summary>Field labels and faction names are worth searching.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>The page content draws its own ButtonTexts (reset buttons, coverage/advanced pickers); declare Back/Generate/Resets instead.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Surface anything vanilla or a mod draws that the typed regions do not present.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>Exposed for the CanDoBack guard: Back must yield to a live search-clear Escape.</summary>
        internal bool HasActiveTypeaheadSearch
        {
            get { return TypeaheadHasActiveSearch; }
        }

        // Content model.

        protected override void RefreshContent()
        {
            // Keep displayed values equal to the live page/GameInitData even when a vanilla mouse
            // path mutated them.
            WorldParamsFieldValues.SyncFromGame();
            WorldParamsFieldValues.SyncAdvancedFromGame();

            planetRows.Clear();
            planetRows.Add(PlanetRow.Seed);
            planetRows.Add(PlanetRow.RandomizeSeed);
            planetRows.Add(PlanetRow.PlanetCoverage);
            planetRows.Add(PlanetRow.Rainfall);
            planetRows.Add(PlanetRow.Temperature);
            planetRows.Add(PlanetRow.Population);
            if (ModsConfig.OdysseyActive)
            {
                planetRows.Add(PlanetRow.LandmarkDensity);
            }
            if (ModsConfig.BiotechActive)
            {
                planetRows.Add(PlanetRow.Pollution);
            }
            planetRows.Add(PlanetRow.AdvancedSettings);

            factionRows.Clear();
            factionRows.AddRange(FactionsNavigationState.GetVisibleFactionRows());

            warnings.Clear();
            warnings.AddRange(FactionsNavigationState.GetCurrentWarnings());
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == 0
                ? "RimWorldAccess.WorldParams.PlanetRegion".Translate()
                : "RimWorldAccess.Factions.Title".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == 0)
            {
                return planetRows.Count;
            }
            return factionRows.Count + 1 + warnings.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return region == 0 ? DescribePlanet(index) : DescribeFaction(index);
        }

        // Planet region.

        private ElementDescription DescribePlanet(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= planetRows.Count)
            {
                return d;
            }
            PlanetRow row = planetRows[index];
            switch (row)
            {
                case PlanetRow.Seed:
                {
                    d.Label = "WorldSeed".Translate();
                    d.Role = ElementRole.TextField;
                    string seed = WorldParamsPageBridge.SeedString;
                    if (string.IsNullOrEmpty(seed))
                    {
                        d.ValueBlank = true;
                    }
                    else
                    {
                        d.Value = seed;
                    }
                    return d;
                }
                case PlanetRow.RandomizeSeed:
                    d.Label = "RandomizeSeed".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                case PlanetRow.PlanetCoverage:
                    d.Label = "PlanetCoverage".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = WorldParamsFieldValues.GetValueString(WorldParamField.PlanetCoverage);
                    d.Extras = ExtrasWith("PlanetCoverageTip".Translate(), WorldParamsFieldValues.GetWarning(WorldParamField.PlanetCoverage));
                    return d;
                case PlanetRow.Rainfall:
                    FillSlider(d, "PlanetRainfall".Translate(), WorldParamField.Rainfall);
                    return d;
                case PlanetRow.Temperature:
                    FillSlider(d, "PlanetTemperature".Translate(), WorldParamField.Temperature);
                    return d;
                case PlanetRow.Population:
                    FillSlider(d, "PlanetPopulation".Translate(), WorldParamField.Population);
                    return d;
                case PlanetRow.LandmarkDensity:
                    FillSlider(d, "PlanetLandmarkDensity".Translate(), WorldParamField.LandmarkDensity);
                    return d;
                case PlanetRow.Pollution:
                    FillSlider(d, "PlanetPollution".Translate(), WorldParamField.Pollution);
                    return d;
                default: // AdvancedSettings
                    // Vanilla's Edit... button shows no values; the dialog itself carries them.
                    d.Label = "AdvancedSettings".Translate();
                    d.Role = ElementRole.Button;
                    return d;
            }
        }

        private static void FillSlider(ElementDescription d, string label, WorldParamField field)
        {
            d.Label = label;
            d.Role = ElementRole.Slider;
            d.Value = WorldParamsFieldValues.GetValueString(field);
            d.Extras = WorldParamsFieldValues.GetWarning(field);
        }

        /// <summary>Joins a tooltip and a (possibly empty) warning into one Extras string.</summary>
        private static string ExtrasWith(string tip, string warning)
        {
            if (string.IsNullOrEmpty(warning))
            {
                return tip;
            }
            if (string.IsNullOrEmpty(tip))
            {
                return warning;
            }
            // GetWarning already carries its leading ". " (WarningPrefix).
            return tip + warning;
        }

        private static WorldParamField PlanetRowField(PlanetRow row)
        {
            switch (row)
            {
                case PlanetRow.PlanetCoverage: return WorldParamField.PlanetCoverage;
                case PlanetRow.Rainfall: return WorldParamField.Rainfall;
                case PlanetRow.Temperature: return WorldParamField.Temperature;
                case PlanetRow.Population: return WorldParamField.Population;
                case PlanetRow.LandmarkDensity: return WorldParamField.LandmarkDensity;
                default: return WorldParamField.Pollution;
            }
        }

        // Factions region.

        private ElementDescription DescribeFaction(int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < factionRows.Count)
            {
                FactionRowInfo row = factionRows[index];
                d.Label = row.Def.LabelCap;
                d.Role = ElementRole.None;
                string desc = row.Def.Description;
                d.Extras = string.IsNullOrEmpty(desc) ? null : desc.StripTags();
                if (row.Locked)
                {
                    d.Hint = "RimWorldAccess.Factions.LockedSuffix".Translate();
                }
                return d;
            }
            int addIndex = factionRows.Count;
            if (index == addIndex)
            {
                d.Label = "RimWorldAccess.WorldParams.AddFactionsRow".Translate();
                d.Role = ElementRole.Button;
                d.Hotkey = ChordDisplay("worldParams.openAddFactionMenu");
                return d;
            }
            int warningIndex = index - addIndex - 1;
            if (warningIndex >= 0 && warningIndex < warnings.Count)
            {
                d.Label = warnings[warningIndex];
                d.Role = ElementRole.None;
                d.ReadOnly = true;
            }
            return d;
        }

        // Left/Right adjust, Planet sliders only: the three ComboBox rows are dropdowns, so
        // Enter/Space open their pickers and Left/Right leave them alone.

        /// <summary>True only for the Slider rows; see the remarks above.</summary>
        private static bool IsSliderRow(PlanetRow row)
        {
            return row == PlanetRow.Rainfall
                || row == PlanetRow.Temperature
                || row == PlanetRow.Population
                || row == PlanetRow.LandmarkDensity
                || row == PlanetRow.Pollution;
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            if (region != 0 || index < 0 || index >= planetRows.Count)
            {
                return false;
            }
            return IsSliderRow(planetRows[index]);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != 0 || index < 0 || index >= planetRows.Count)
            {
                return;
            }
            PlanetRow row = planetRows[index];
            if (!IsSliderRow(row))
            {
                return;
            }
            WorldParamField field = PlanetRowField(row);
            string before = WorldParamsFieldValues.GetValueString(field);
            WorldParamsFieldValues.Modify(field, direction);
            string after = WorldParamsFieldValues.GetValueString(field);

            var d = new ElementDescription { Value = after };
            bool unchanged = before == after;
            d.AtMinimum = unchanged && direction < 0;
            d.AtMaximum = unchanged && direction > 0;
            string change = AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance);
            string warning = WorldParamsFieldValues.GetWarning(field);
            TolkHelper.SpeakData(string.IsNullOrEmpty(warning) ? change : change + warning);
        }

        // Focus ring: resolve each typed row's backing vanilla widget capture. widgetSnapshot
        // already holds it — the presented-set diff hides it from extrasRows, not from the stream.

        protected internal override Rect FocusedContentRect()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return default(Rect);
            }
            if (Model.RegionIndex == 0)
            {
                return region.Index >= 0 && region.Index < planetRows.Count
                    ? ResolvePlanetRowRect(planetRows[region.Index])
                    : default(Rect);
            }
            if (Model.RegionIndex == 1)
            {
                return ResolveFactionRowRect(region.Index);
            }
            return default(Rect);
        }

        private Rect ResolvePlanetRowRect(PlanetRow row)
        {
            switch (row)
            {
                case PlanetRow.Seed:
                    // RecordTextField's contract: Label is always "", ordinal among text fields.
                    return FindCapturedWidgetRect(WidgetKind.TextField, "", 0);
                case PlanetRow.RandomizeSeed:
                    return FindCapturedWidgetRect(WidgetKind.Button, (string)"RandomizeSeed".Translate(), 0);
                case PlanetRow.PlanetCoverage:
                    // The ButtonText caption is the live percent value (:114); reproduce it to
                    // match this pass.
                    return FindCapturedWidgetRect(WidgetKind.Button, WorldParamsPageBridge.PlanetCoverage.ToStringPercent(), 0);
                case PlanetRow.Rainfall:
                case PlanetRow.Temperature:
                case PlanetRow.Population:
                case PlanetRow.LandmarkDensity:
                case PlanetRow.Pollution:
                {
                    string label;
                    int ordinal;
                    SliderDescriptor(row, out label, out ordinal);
                    return FindCapturedWidgetRect(WidgetKind.Slider, label, ordinal);
                }
                default: // AdvancedSettings
                    return FindCapturedWidgetRect(WidgetKind.Button, (string)"Edit".Translate() + "...", 0);
            }
        }

        /// <summary>
        /// The four Overall* sliders share one HorizontalSlider middle-label parameter (:144-158) and
        /// several translate to identical text in English, so the ordinal CaptureDescriptor.FindIndex
        /// needs is how many EARLIER rows in this fixed draw sequence resolve to the SAME text this
        /// pass — never a hardcoded position, so a language where all four differ still resolves.
        /// </summary>
        private static void SliderDescriptor(PlanetRow row, out string label, out int ordinal)
        {
            ordinal = 0;
            if (row == PlanetRow.Pollution)
            {
                // Pollution's caption IS its live percent value (:165), not a fixed Low/Normal/High key.
                label = WorldParamsPageBridge.Pollution.ToStringPercent();
                return;
            }
            string myLabel = SliderMiddleLabel(row);
            label = myLabel;
            foreach (PlanetRow earlier in SliderRowsInDrawOrder())
            {
                if (earlier == row)
                {
                    break;
                }
                if (SliderMiddleLabel(earlier) == myLabel)
                {
                    ordinal++;
                }
            }
        }

        protected override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            value = 0f;
            high = false;
            if (region != 0 || index < 0 || index >= planetRows.Count || !IsSliderRow(planetRows[index]) || !WorldParamsPageBridge.IsBound)
            {
                return false;
            }
            switch (planetRows[index])
            {
                case PlanetRow.Rainfall: value = (float)WorldParamsPageBridge.Rainfall; break;
                case PlanetRow.Temperature: value = (float)WorldParamsPageBridge.Temperature; break;
                case PlanetRow.Population: value = (float)WorldParamsPageBridge.Population; break;
                case PlanetRow.LandmarkDensity: value = (float)WorldParamsPageBridge.LandmarkDensity; break;
                default: value = WorldParamsPageBridge.Pollution; break;
            }
            return true;
        }

        private void ReportFocusedSlider()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != 0 || region == null || region.IsEmpty
                || region.Index < 0 || region.Index >= planetRows.Count || !IsSliderRow(planetRows[region.Index]))
            {
                return;
            }
            string label;
            int ordinal;
            SliderDescriptor(planetRows[region.Index], out label, out ordinal);
            WidgetCapture.SetFocusedContentControl(WidgetKind.Slider, label, ordinal);
        }

        private static string SliderMiddleLabel(PlanetRow row)
        {
            switch (row)
            {
                case PlanetRow.Rainfall: return (string)"PlanetRainfall_Normal".Translate();
                case PlanetRow.Temperature: return (string)"PlanetTemperature_Normal".Translate();
                case PlanetRow.Population: return (string)"PlanetPopulation_Normal".Translate();
                default: return (string)"PlanetLandmarkDensity_Normal".Translate(); // LandmarkDensity
            }
        }

        /// <summary>Fixed vanilla draw order (decompiled Page_CreateWorldParams.cs:142-163); LandmarkDensity only draws under Odyssey, matching RefreshContent's own gate.</summary>
        private static IEnumerable<PlanetRow> SliderRowsInDrawOrder()
        {
            yield return PlanetRow.Rainfall;
            yield return PlanetRow.Temperature;
            yield return PlanetRow.Population;
            if (ModsConfig.OdysseyActive)
            {
                yield return PlanetRow.LandmarkDensity;
            }
        }

        /// <summary>
        /// A Delete ButtonImage per unlocked row — WorldFactionsUIUtility.DoRow short-circuits, so
        /// a scenario-locked row draws none — the "Add..." ButtonText for the add-factions row, and
        /// no widget for warning rows, which vanilla draws as one combined multi-line label.
        /// </summary>
        private Rect ResolveFactionRowRect(int index)
        {
            if (index >= 0 && index < factionRows.Count)
            {
                FactionRowInfo row = factionRows[index];
                if (row.Locked)
                {
                    return default(Rect);
                }
                string deleteLabel = WidgetCapture.ImageButtonLabel(TexButton.Delete, null);
                int ordinal = 0;
                for (int i = 0; i < index; i++)
                {
                    if (!factionRows[i].Locked)
                    {
                        ordinal++;
                    }
                }
                return FindCapturedWidgetRect(WidgetKind.Button, deleteLabel, ordinal);
            }
            if (index == factionRows.Count)
            {
                string addLabel = (string)"Add".Translate().CapitalizeFirst() + "...";
                return FindCapturedWidgetRect(WidgetKind.Button, addLabel, 0);
            }
            return default(Rect); // a warning row
        }

        // Enter.

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == 0)
            {
                ActivatePlanet(index);
            }
            else
            {
                ActivateFaction(index);
            }
        }

        private void ActivatePlanet(int index)
        {
            if (index < 0 || index >= planetRows.Count)
            {
                return;
            }
            switch (planetRows[index])
            {
                case PlanetRow.Seed:
                    BeginSeedEdit();
                    break;
                case PlanetRow.RandomizeSeed:
                    RandomizeSeed();
                    break;
                case PlanetRow.PlanetCoverage:
                    OpenPlanetCoveragePicker();
                    break;
                case PlanetRow.AdvancedSettings:
                    // The Edit... button's own body (decompiled Page_CreateWorldParams.cs:173);
                    // AdvancedGameConfigScope drives the dialog.
                    Find.WindowStack.Add(new Dialog_AdvancedGameConfig());
                    break;
                default:
                    // Sliders adjust with Left/Right; without a type-in box Enter just re-reads.
                    AnnounceCurrentItem();
                    break;
            }
        }

        private void ActivateFaction(int index)
        {
            if (index >= 0 && index < factionRows.Count)
            {
                // Deletion is the worldParams.deleteFaction chord; Enter re-reads the row.
                AnnounceCurrentItem();
                return;
            }
            if (index == factionRows.Count)
            {
                FactionsNavigationState.OpenAddMenu();
                return;
            }
            AnnounceCurrentItem();
        }

        // Seed browse/edit session.

        private void BeginSeedEdit()
        {
            seedSession.EnterEdit(
                WorldParamsPageBridge.SeedString ?? string.Empty,
                TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelWorldSeed"),
                "WorldSeed".Translate(),
                ApplySeedEdit,
                OnSeedEditExit);
        }

        private void ApplySeedEdit(string value)
        {
            WorldParamsPageBridge.SeedString = value;
        }

        private void OnSeedEditExit()
        {
            AnnounceCurrentItem();
        }

        /// <summary>Called by the page's minimal draw prefix each GUI pass so the vanilla seed TextField renders the live buffer and Escape keeps it.</summary>
        internal void OnPageDrawPass()
        {
            // The shell must stay the sole owner of Unity keyboard focus so arrows reach the
            // dispatcher; mirroring the seed buffer is a no-op in browse mode.
            ShellTextFocus.ReleaseNativeFocus();
            seedSession.MirrorLive();
            ReportFocusedSlider();
        }

        private void RandomizeSeed()
        {
            if (!WorldParamsPageBridge.IsBound)
            {
                return;
            }
            // MUTATION-C: mirrors Page_CreateWorldParams's RandomizeSeed button body
            // (decompiled :106-110) — Tick_Tiny + seedString = GenText.RandomSeedString().
            // The private seedString field has no gated setter.
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            string newSeed = GenText.RandomSeedString();
            WorldParamsPageBridge.SeedString = newSeed;
            RefreshModel();
            TolkHelper.Speak("RimWorldAccess.WorldParams.SeedRandomized".Loc(newSeed));
        }

        // Combo pickers: windowless float menus over vanilla's own option sources. Selections
        // write through the same paths Left/Right's Modify uses, then re-sync and re-announce.

        private void OpenPlanetCoveragePicker()
        {
            // MUTATION-C: mirrors Page_CreateWorldParams's planet-coverage FloatMenu
            // (decompiled :114-138), including the PlanetCoverages/PlanetCoveragesDev
            // arrays and the " (dev)" marker. The 100% performance caution reaches the
            // ear through the row's GetWarning-fed Extras (announced on selection),
            // so vanilla's Messages.Message toast is not duplicated.
            float[] coverages = Prefs.DevMode
                ? new[] { 0.3f, 0.5f, 1f, 0.05f }
                : new[] { 0.3f, 0.5f, 1f };
            var options = new List<FloatMenuOption>();
            foreach (float coverage in coverages)
            {
                float c = coverage;
                string label = c.ToStringPercent();
                if (c <= 0.1f)
                {
                    label += " (dev)";
                }
                options.Add(new FloatMenuOption(label, delegate
                {
                    WorldParamsPageBridge.PlanetCoverage = c;
                    WorldParamsFieldValues.SyncFromGame();
                    RefreshModel();
                    AnnounceCurrentItem();
                }));
            }
            int start = Array.FindIndex(coverages, c => Math.Abs(c - WorldParamsPageBridge.PlanetCoverage) < 0.01f);
            OpenPicker(options, Math.Max(0, start));
        }

        private static void OpenPicker(List<FloatMenuOption> options, int startIndex)
        {
            if (options.Count == 0)
            {
                return;
            }
            // announceSelection:false — each option's own action re-announces the row.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex, announceSelection: false);
        }

        // Faction actions; the mutation cores stay on FactionsNavigationState.

        private void DeleteCurrentFaction()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || Model.RegionIndex != 1)
            {
                return;
            }
            int index = region.Index;
            if (index < 0 || index >= factionRows.Count)
            {
                return;
            }
            FactionRowInfo row = factionRows[index];
            FactionDeleteResult result = FactionsNavigationState.DeleteFactionAt(row.ListIndex);
            switch (result)
            {
                case FactionDeleteResult.LockedByScenario:
                    TolkHelper.Speak("RimWorldAccess.Factions.CannotRemoveLocked".Loc(row.Def.LabelCap));
                    return;
                case FactionDeleteResult.TutorialBlocked:
                    TolkHelper.Speak("RimWorldAccess.Factions.CannotModifyTutorial".Loc());
                    return;
                case FactionDeleteResult.NotFound:
                    return;
            }
            RefreshModel();
            int remaining = factionRows.Count;
            TolkHelper.Speak("RimWorldAccess.Factions.RemovedRemaining".Loc(row.Def.LabelCap, remaining));
            if (remaining > 0)
            {
                AnnounceCurrentItem();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Factions.NoneRemaining".Loc());
            }
        }

        private void ResetAll()
        {
            if (!WorldParamsPageBridge.IsBound)
            {
                return;
            }
            seedSession.CancelIfActive();
            // Category A — Page_CreateWorldParams.Reset() (vanilla's "Reset all" button).
            WorldParamsPageBridge.ResetPage();
            WorldParamsFieldValues.SyncFromGame();
            RefreshModel();
            TolkHelper.Speak("RimWorldAccess.WorldParams.ResetAllDone".Loc());
            AnnounceCurrentItem();
        }

        private void ResetFactions()
        {
            FactionsNavigationState.ResetFactions();
            RefreshModel();
            AnnounceCurrentItem();
        }

        // Buttons region: Back / Generate / Resets ride vanilla vehicles.

        /// <summary>Enter double-press proceed (S4c): Generate is this page's default/proceed button.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "worldParams.generate"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction("WorldGenerate".Translate(), NextAction, "worldParams.generate"));
                actions.Add(new ScreenAction("ResetAll".Translate(), ResetAll, "worldParams.resetAll"));
                actions.Add(new ScreenAction("ResetFactions".Translate(), ResetFactions, "worldParams.resetFactions"));
                return actions;
            }
        }

        /// <summary>
        /// True only while this scope's Back invocation runs its vanilla gate, letting that one
        /// explicit CanDoBack through <c>WorldParamsPatch_CanDoBack</c>, which otherwise blocks the
        /// raw Cancel poll outright while the scope is live.
        /// </summary>
        internal static bool BackRequested;

        private void EscapeBack()
        {
            ShellFrameStamps.MarkCancelConsumed();
            BackAction();
        }

        private void BackAction()
        {
            // Mirror Page.DoBottomButtons' Back branch (Page.cs:58-61): gate, then DoBack.
            BackRequested = true;
            try
            {
                if ((bool)canDoBackMethod.Invoke(page, null))
                {
                    doBackMethod.Invoke(page, null);
                }
            }
            finally
            {
                BackRequested = false;
            }
        }

        private void NextAction()
        {
            // Mirror the Next branch (Page.cs:69-71): gate, then DoNext — vehicle B. On this page
            // CanDoNext queues async world generation and returns false, so DoNext never runs; the
            // flag lets this explicit call through while the raw Accept poll stays blocked.
            AdvanceRequested = true;
            try
            {
                if ((bool)canDoNextMethod.Invoke(page, null))
                {
                    doNextMethod.Invoke(page, null);
                }
            }
            finally
            {
                AdvanceRequested = false;
            }
        }

        // Captured-extras suppression.

        /// <summary>
        /// Folds into the captured-extras diff the strings this scope presents another way: the
        /// "Advanced settings" label and its "Edit…" button (map size and season are inlined), the
        /// factions "Add…" ButtonText, and each per-faction delete ButtonImage's texture name.
        /// </summary>
        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                yield return (string)"AdvancedSettings".Translate();
                yield return (string)"Edit".Translate() + "...";
                yield return (string)"Add".Translate().CapitalizeFirst() + "...";
                // The capture engine names TexButton.Delete by the localized "Delete", not its
                // asset name "Dismiss".
                string deleteLabel = WidgetCapture.ImageButtonLabel(TexButton.Delete, null);
                if (!string.IsNullOrEmpty(deleteLabel))
                {
                    yield return deleteLabel;
                }
            }
        }

        /// <summary>A slider's type-in box is presented on its slider row.</summary>
        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return widget.SliderTypeInOwner >= 0;
        }

        // Claim gates.

        private bool InFactionsRegion()
        {
            RefreshModel();
            return Model.RegionIndex == 1;
        }

        private bool CurrentIsFactionRow()
        {
            RefreshModel();
            if (Model.RegionIndex != 1)
            {
                return false;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return false;
            }
            return region.Index >= 0 && region.Index < factionRows.Count;
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            Active = this;
            WorldParamsPageBridge.Bind(page);
            WorldParamsFieldValues.SetupDynamicOptions();
            WorldParamsFieldValues.SyncFromGame();
            WorldParamsFieldValues.SyncAdvancedFromGame();
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(Active, this))
            {
                Active = null;
            }
            WorldParamsPageBridge.Unbind();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            string tabCount = TabCountFragment();
            string title = (string)"CreateWorld".Translate();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title);
            AnnounceCurrentItem();
        }

        /// <summary>Re-reads the current row (the add-menu overlay's close handoff).</summary>
        internal void ReannounceCurrent()
        {
            RefreshModel();
            AnnounceCurrentItem();
        }
    }
}
