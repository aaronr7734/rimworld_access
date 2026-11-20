using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared region-building core for the two storyteller/difficulty screens,
    /// <see cref="StorytellerScreenScope"/> (pre-game) and <see cref="StorytellerInGameScope"/>.
    ///
    /// Three regions live here unconditionally: Storytellers, Difficulty, and CustomDifficulty (gated
    /// on the chosen difficulty being <c>isCustom</c>) built from
    /// <see cref="DifficultySettingsHelper.BuildSections"/>. Its sections take no row of their own:
    /// each setting row carries its section name, spoken as a landing prefix on section crossings
    /// (<see cref="AnnouncePrefix"/>), and PageUp/PageDown jump to the adjacent section's first row.
    /// Three further regions are PRE-GAME ONLY — SaveMode, AnomalyPlaystyle and AnomalySettings,
    /// which vanilla's own DrawStorytellerSelectionInterface gates behind
    /// <c>ProgramState.Entry</c> — so subclasses insert them through
    /// <see cref="BuildAdditionalRegions"/> and the matching hooks. Everything else (region dispatch,
    /// describe, activate, adjust, typeahead, the captured-extras diff, the arrival title) is sealed
    /// here so the two twins cannot drift apart.
    ///
    /// The richer setting-adjustment keys (percent steps, Shift+Home/End set-min/max, Space toggle,
    /// Alt+R reset-to-preset) are claimed HERE so both twins carry them. Plain ±1 Left/Right rides
    /// the base <see cref="ScreenScope"/> claim through <see cref="ResolveAdjustableSetting"/> and
    /// needs no action id of its own.
    /// </summary>
    public abstract class StorytellerScopeBase : ScreenScope, IListingRingClient
    {
        protected enum RegionKind { Storytellers, Difficulty, SaveMode, AnomalyPlaystyle, AnomalySettings, CustomDifficulty }

        protected readonly List<RegionKind> ActiveRegions = new List<RegionKind>();
        protected readonly List<StorytellerDef> Storytellers = new List<StorytellerDef>();
        protected readonly List<DifficultyDef> Difficulties = new List<DifficultyDef>();
        private readonly List<CustomRow> CustomRows = new List<CustomRow>();

        private bool announcedOpen;

        /// <summary>Section name last spoken as a landing prefix (see <see cref="AnnouncePrefix"/>); Reset re-arms it.</summary>
        private readonly SectionPrefixTracker sectionPrefix = new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        /// <summary>Storyteller and difficulty names (and setting rows) are worth searching.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Surface anything vanilla or a mod draws that the typed regions do not present (page notes, mod widgets).</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>True for the pre-game twin: threaded into
        /// <see cref="DifficultySettingsHelper.BuildSections"/> so the Anomaly playstyle picker row
        /// appears only where vanilla itself allows picking one.</summary>
        protected virtual bool IsCharGen
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // Per-twin storage. Pre-game reads and writes Page_SelectStoryteller's private fields, since
        // chargen has no live Storyteller yet; in-game uses Current.Game.storyteller directly.
        // ------------------------------------------------------------------

        protected abstract StorytellerDef CurrentStorytellerDef { get; }
        protected abstract DifficultyDef CurrentDifficultyDef { get; }
        protected abstract Difficulty CurrentDifficultyValues { get; }

        /// <summary>Commit a new storyteller choice (the raw write + whatever side effects this twin's context needs).</summary>
        protected abstract void ApplyStorytellerDef(StorytellerDef def);

        /// <summary>Commit a new difficulty choice (the raw write + CopyFrom/Rough-seed logic each twin's storage needs).</summary>
        protected abstract void ApplyDifficultyDef(DifficultyDef def, DifficultyDef previous);

        // ------------------------------------------------------------------
        // Content model.
        // ------------------------------------------------------------------

        protected override void RefreshContent()
        {
            DifficultyDef chosenDifficulty = CurrentDifficultyDef;
            Difficulty dv = CurrentDifficultyValues;

            Storytellers.Clear();
            Storytellers.AddRange(DefDatabase<StorytellerDef>.AllDefs.Where(d => d.listVisible).OrderBy(d => d.listOrder));

            Difficulties.Clear();
            Difficulties.AddRange(DefDatabase<DifficultyDef>.AllDefs);

            BuildCustomRows(dv, chosenDifficulty);

            ActiveRegions.Clear();
            ActiveRegions.Add(RegionKind.Storytellers);
            ActiveRegions.Add(RegionKind.Difficulty);
            if (chosenDifficulty != null && chosenDifficulty.isCustom)
                ActiveRegions.Add(RegionKind.CustomDifficulty);
            BuildAdditionalRegions(dv, chosenDifficulty);
        }

        /// <summary>Pre-game-only hook: appends SaveMode and (DLC + difficulty chosen) AnomalyPlaystyle + AnomalySettings after CustomDifficulty.</summary>
        protected virtual void BuildAdditionalRegions(Difficulty dv, DifficultyDef chosenDifficulty)
        {
        }

        private void BuildCustomRows(Difficulty dv, DifficultyDef chosenDifficulty)
        {
            CustomRows.Clear();
            if (dv == null || chosenDifficulty == null || !chosenDifficulty.isCustom)
                return;

            List<DifficultySection> sections = DifficultySettingsHelper.BuildSections(
                dv, onReset: OnResetToPreset, onAnomalyPlaystyleChanged: null, isCharGen: IsCharGen);
            foreach (DifficultySection section in sections)
            {
                foreach (DifficultySetting setting in section.Settings)
                    CustomRows.Add(new CustomRow { Setting = setting, Section = section.Name });
            }
        }

        /// <summary>
        /// The one reset-to-preset vehicle, shared by the per-row Reset button and the Alt+R float
        /// menu. CopyFrom is vanilla's own method, so this is a vanilla vehicle.
        /// </summary>
        protected virtual void OnResetToPreset(DifficultyDef preset)
        {
            Difficulty dv = CurrentDifficultyValues;
            dv.CopyFrom(preset);
            TolkHelper.Speak("RimWorldAccess.CustomDifficulty.AllSettingsSetTo".Loc(preset.LabelCap));
        }

        /// <summary>Alt+R: reset ALL custom difficulty settings to a chosen preset via an accessible windowless float menu.</summary>
        private void OpenResetToPresetMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (DifficultyDef def in DefDatabase<DifficultyDef>.AllDefs)
            {
                if (def.isCustom)
                    continue;
                DifficultyDef localDef = def;
                options.Add(new FloatMenuOption(def.LabelCap, () => OnResetToPreset(localDef)));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            TolkHelper.Speak("RimWorldAccess.Storyteller.ResetSelectPreset".Loc());
        }

        protected sealed override int ContentRegionCount
        {
            get { return ActiveRegions.Count; }
        }

        protected sealed override string ContentRegionName(int region)
        {
            switch (ActiveRegions[region])
            {
                case RegionKind.Storytellers: return "RimWorldAccess.Storyteller.StorytellersRegion".Translate();
                case RegionKind.Difficulty: return "RimWorldAccess.Storyteller.DifficultyRegion".Translate();
                case RegionKind.CustomDifficulty: return "RimWorldAccess.Storyteller.CustomDifficultyRegion".Translate();
                default: return AdditionalRegionName(ActiveRegions[region]);
            }
        }

        protected virtual string AdditionalRegionName(RegionKind kind)
        {
            return "";
        }

        protected sealed override int ContentItemCount(int region)
        {
            switch (ActiveRegions[region])
            {
                case RegionKind.Storytellers: return Storytellers.Count;
                case RegionKind.Difficulty: return Difficulties.Count;
                case RegionKind.CustomDifficulty: return CustomRows.Count;
                default: return AdditionalRegionItemCount(ActiveRegions[region]);
            }
        }

        protected virtual int AdditionalRegionItemCount(RegionKind kind)
        {
            return 0;
        }

        protected sealed override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (ActiveRegions[region])
            {
                case RegionKind.Storytellers: return DescribeStoryteller(index);
                case RegionKind.Difficulty: return DescribeDifficulty(index);
                case RegionKind.CustomDifficulty: return DescribeCustom(index);
                default: return DescribeAdditionalRegionItem(ActiveRegions[region], index);
            }
        }

        protected virtual ElementDescription DescribeAdditionalRegionItem(RegionKind kind, int index)
        {
            return new ElementDescription();
        }

        private ElementDescription DescribeStoryteller(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= Storytellers.Count)
                return d;
            StorytellerDef def = Storytellers[index];
            d.Label = def.LabelCap;
            d.Role = ElementRole.RadioButton;
            d.Selected = ReferenceEquals(CurrentStorytellerDef, def);
            d.Extras = def.description.StripTags();
            return d;
        }

        private ElementDescription DescribeDifficulty(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= Difficulties.Count)
                return d;
            DifficultyDef def = Difficulties[index];
            string label = def.LabelCap;
            if (def.isCustom)
                label += "...";
            d.Label = label;
            d.Role = ElementRole.RadioButton;
            d.Selected = ReferenceEquals(CurrentDifficultyDef, def);
            d.Extras = ((string)def.description.ResolveTags()).StripTags();
            if (def.isCustom)
                d.Hint = "RimWorldAccess.Storyteller.CustomDifficultyRevealHint".Translate();
            return d;
        }

        private ElementDescription DescribeCustom(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= CustomRows.Count)
                return d;
            CustomRow row = CustomRows[index];
            // The one home for setting-to-announcement, shared with AnomalySettingsDialogState.
            DifficultySettingAdapter.FillRow(d, row.Setting);
            return d;
        }

        protected sealed override bool CanAdjustContentItem(int region, int index)
        {
            DifficultySetting s = ResolveAdjustableSetting(region, index);
            return s != null && s.IsEnabled && IsAdjustable(s);
        }

        /// <summary>
        /// Only sliders adjust with Left/Right. The playstyle row is a ComboBox — a dropdown,
        /// not a slider — so it reports unadjustable and Enter/Space open its picker instead.
        /// </summary>
        private static bool IsAdjustable(DifficultySetting s)
        {
            return s is DifficultySliderSetting;
        }

        protected sealed override void AdjustContentItem(int region, int index, int direction)
        {
            DifficultySetting s = ResolveAdjustableSetting(region, index);
            if (s == null)
                return;
            s.Adjust(direction);
            AnnounceSettingStateChange(region, index);
        }

        protected sealed override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            value = 0f;
            high = false;
            if (region < 0 || region >= ActiveRegions.Count || ActiveRegions[region] != RegionKind.CustomDifficulty
                || index < 0 || index >= CustomRows.Count)
                return false;
            if (!(CustomRows[index].Setting is DifficultySliderSetting slider) || !slider.IsEnabled)
                return false;
            value = slider.WidgetValue;
            return true;
        }

        /// <summary>
        /// Opt-in gate for the radio-group contract, where arrowing onto a choice selects it. True
        /// pre-game, where the picks are chargen configuration; false in-game, where
        /// <see cref="ApplyDifficultyDef"/> copies a preset straight into the RUNNING colony's
        /// <see cref="Difficulty"/> and browsing would overwrite the player's custom values.
        /// </summary>
        protected virtual bool AutoSelectRadioOnSettle
        {
            get { return false; }
        }

        /// <summary>
        /// Per-region refinement of <see cref="AutoSelectRadioOnSettle"/>: whether
        /// arrowing onto a choice in THIS region selects it. Defaults to the
        /// screen-wide flag. A radio group whose selection rewrites the screen
        /// underneath it — adding, removing or repopulating other controls —
        /// overrides false for that region: browsing must not silently reconfigure
        /// the page, and the controls a choice reveals must stay reachable while the
        /// player is still deciding. Those groups take Space/Enter to select, the
        /// shared activation grammar every other control already answers to.
        /// </summary>
        protected virtual bool RegionAutoSelectsRadio(int region)
        {
            return AutoSelectRadioOnSettle;
        }

        /// <summary>
        /// Storyteller and difficulty rows select on arrival; both ride the same
        /// <see cref="ApplyStorytellerDef"/>/<see cref="ApplyDifficultyDef"/> vehicles Enter
        /// uses, silently (the landing announcement speaks "selected"). A subclass with radio
        /// regions of its own overrides this and calls base first.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            ScrollPortraitIntoView(region, index);
            if (region < 0 || region >= ActiveRegions.Count || !RegionAutoSelectsRadio(region))
                return;
            if (ActiveRegions[region] == RegionKind.Storytellers)
            {
                if (index < 0 || index >= Storytellers.Count)
                    return;
                if (ReferenceEquals(CurrentStorytellerDef, Storytellers[index]))
                    return;
                ApplyStorytellerDef(Storytellers[index]);
                return;
            }
            if (ActiveRegions[region] == RegionKind.Difficulty)
            {
                if (index < 0 || index >= Difficulties.Count)
                    return;
                DifficultyDef def = Difficulties[index];
                if (ReferenceEquals(CurrentDifficultyDef, def))
                    return;
                ApplyDifficultyDef(def, CurrentDifficultyDef);
            }
        }

        /// <summary>Resolves the DifficultySetting (if any) an adjustable content item maps to. Pre-game additionally resolves the AnomalySettings region's slider rows.</summary>
        protected virtual DifficultySetting ResolveAdjustableSetting(int region, int index)
        {
            if (region < 0 || region >= ActiveRegions.Count)
                return null;
            if (ActiveRegions[region] == RegionKind.CustomDifficulty && index >= 0 && index < CustomRows.Count)
                return CustomRows[index].Setting;
            return null;
        }

        /// <summary>
        /// Changed-state-only announcement for a setting row ("119 percent",
        /// "checked") — the description already spoke on focus and must not
        /// repeat on every step.
        /// </summary>
        private void AnnounceSettingStateChange(int region, int index)
        {
            ElementDescription d = DescribeContentItem(region, index);
            if (d == null)
                return;
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>
        /// Speaks the section name once, folded into the same utterance as the row that
        /// crossed into it (the generic reader's ApplySectionPrefix idiom). Leaving the
        /// region re-arms the prefix, so tabbing away and back re-orients.
        /// </summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            string boundary = base.AnnouncePrefix(region, index);
            if (region < 0 || region >= ActiveRegions.Count
                || ActiveRegions[region] != RegionKind.CustomDifficulty)
            {
                sectionPrefix.Reset();
                return boundary;
            }
            if (index < 0 || index >= CustomRows.Count)
                return boundary;
            string section = sectionPrefix.Cross(CustomRows[index].Section);
            if (section == null)
                return boundary;
            return string.IsNullOrEmpty(boundary) ? section : boundary + ". " + section;
        }

        protected sealed override void ActivateContentItem(int region, int index)
        {
            switch (ActiveRegions[region])
            {
                case RegionKind.Storytellers:
                    ActivateStoryteller(index);
                    break;
                case RegionKind.Difficulty:
                    ActivateDifficulty(index);
                    break;
                case RegionKind.CustomDifficulty:
                    ActivateCustom(index);
                    break;
                default:
                    ActivateAdditionalRegionItem(ActiveRegions[region], index);
                    break;
            }
        }

        protected virtual void ActivateAdditionalRegionItem(RegionKind kind, int index)
        {
        }

        private void ActivateStoryteller(int index)
        {
            if (index < 0 || index >= Storytellers.Count)
                return;
            ApplyStorytellerDef(Storytellers[index]);
            AnnounceCurrentItem();
        }

        private void ActivateDifficulty(int index)
        {
            if (index < 0 || index >= Difficulties.Count)
                return;
            // Selecting custom difficulty reveals the CustomDifficulty region on the next refresh;
            // re-announce this row only, since its Hint says the tab appeared.
            DifficultyDef def = Difficulties[index];
            ApplyDifficultyDef(def, CurrentDifficultyDef);
            AnnounceCurrentItem();
        }

        private void ActivateCustom(int index)
        {
            if (index < 0 || index >= CustomRows.Count)
                return;
            CustomRow row = CustomRows[index];
            DifficultySetting s = row.Setting;
            if (s is DifficultyResetSetting)
            {
                // Runs OnResetToPreset, which copies the values in and announces once.
                s.Toggle();
                return;
            }
            if (s is DifficultySliderSetting)
            {
                // Sliders adjust with Left/Right; Enter just re-reads.
                AnnounceCurrentItem();
                return;
            }
            int customRegion = ActiveRegions.IndexOf(RegionKind.CustomDifficulty);
            if (s is AnomalyPlaystyleSetting playstyle)
            {
                // A combo box: Enter/Space open the playstyle list rather than stepping through it.
                playstyle.OpenPicker(() => AnnounceSettingStateChange(customRegion, index));
                return;
            }
            // Checkbox toggles.
            s.Toggle();
            AnnounceSettingStateChange(customRegion, index);
        }

        // ------------------------------------------------------------------
        // Percent adjust, min/max, Space toggle, Alt+R reset-to-preset — available to any content
        // item ResolveAdjustableSetting resolves (CustomDifficulty on both twins, plus Anomaly
        // pre-game).
        // ------------------------------------------------------------------

        protected StorytellerScopeBase()
        {
            Claim("storyteller.setting.setMinimum", e => SetCurrentToMin(), when: CurrentAdjustableClaimable);
            Claim("storyteller.setting.setMaximum", e => SetCurrentToMax(), when: CurrentAdjustableClaimable);
            Claim("storyteller.setting.decreaseLarge", e => AdjustCurrentByPercent(-0.25f), when: CurrentAdjustableClaimable);
            Claim("storyteller.setting.decreaseMedium", e => AdjustCurrentByPercent(-0.1f), when: CurrentAdjustableClaimable);
            Claim("storyteller.setting.increaseLarge", e => AdjustCurrentByPercent(0.25f), when: CurrentAdjustableClaimable);
            Claim("storyteller.setting.increaseMedium", e => AdjustCurrentByPercent(0.1f), when: CurrentAdjustableClaimable);
            Claim("storyteller.setting.toggle", e => ToggleCurrentCustomSetting(), when: CurrentTogglableClaimable);
            Claim("storyteller.resetToPreset", e => OpenResetToPresetMenu(), when: InCustomDifficultyRegionClaimable);
            Claim("storyteller.section.previous", e => JumpToAdjacentSection(-1), when: InCustomDifficultyRegionClaimable);
            Claim("storyteller.section.next", e => JumpToAdjacentSection(1), when: InCustomDifficultyRegionClaimable);
        }

        /// <summary>PageUp/PageDown: the adjacent section's first row, clamped at the outer sections with the reject sound.</summary>
        private void JumpToAdjacentSection(int direction)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            int index = region.Index;
            if (index < 0 || index >= CustomRows.Count)
                return;
            int target = SectionNavigation.FindAdjacentSectionStart(
                CustomRows.Count, index, i => CustomRows[i].Section, direction > 0);
            if (target < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            region.MoveTo(target);
            AnnounceCurrentItem();
        }

        private bool CurrentAdjustableClaimable()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return false;
            return CanAdjustContentItem(Model.RegionIndex, region.Index);
        }

        private void AdjustCurrentByPercent(float percent)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            // Sliders are the only adjustable rows and these chords share that gate, so there is
            // nothing to fall back to.
            if (!(ResolveAdjustableSetting(Model.RegionIndex, region.Index) is DifficultySliderSetting slider))
                return;
            slider.AdjustByPercentOfPositions(percent);
            AnnounceSettingStateChange(Model.RegionIndex, region.Index);
        }

        private void SetCurrentToMin()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            if (!(ResolveAdjustableSetting(Model.RegionIndex, region.Index) is DifficultySliderSetting slider))
                return;
            slider.SetToMin();
            AnnounceSettingStateChange(Model.RegionIndex, region.Index);
        }

        private void SetCurrentToMax()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            if (!(ResolveAdjustableSetting(Model.RegionIndex, region.Index) is DifficultySliderSetting slider))
                return;
            slider.SetToMax();
            AnnounceSettingStateChange(Model.RegionIndex, region.Index);
        }

        /// <summary>Space toggle is CustomDifficulty-only; any row carrying a setting qualifies, not just adjustable ones.</summary>
        private bool CurrentTogglableClaimable()
        {
            RefreshModel();
            if (Model.RegionIndex < 0 || Model.RegionIndex >= ActiveRegions.Count)
                return false;
            if (ActiveRegions[Model.RegionIndex] != RegionKind.CustomDifficulty)
                return false;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return false;
            int index = region.Index;
            return index >= 0 && index < CustomRows.Count && CustomRows[index].Setting != null;
        }

        private void ToggleCurrentCustomSetting()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return;
            int index = region.Index;
            if (index < 0 || index >= CustomRows.Count)
                return;
            DifficultySetting s = CustomRows[index].Setting;
            if (s == null)
                return;
            if (s is AnomalyPlaystyleSetting playstyle)
            {
                // The same combo-box treatment Enter gets (ActivateCustom).
                int regionIndex = Model.RegionIndex;
                playstyle.OpenPicker(() => AnnounceSettingStateChange(regionIndex, index));
                return;
            }
            s.Toggle();
            AnnounceSettingStateChange(Model.RegionIndex, index);
        }

        private bool InCustomDifficultyRegionClaimable()
        {
            RefreshModel();
            if (Model.RegionIndex < 0 || Model.RegionIndex >= ActiveRegions.Count)
                return false;
            return ActiveRegions[Model.RegionIndex] == RegionKind.CustomDifficulty;
        }

        // ------------------------------------------------------------------
        // Captured-extras suppression: the Storytellers region already lists every storyteller, so a
        // portrait ButtonImage's raw texture name must not double as an extra. Pre-game additionally
        // suppresses its chargen-only "AnomalySettings..." button.
        // ------------------------------------------------------------------

        protected sealed override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                for (int i = 0; i < Storytellers.Count; i++)
                {
                    string texName = Storytellers[i].portraitTinyTex != null ? Storytellers[i].portraitTinyTex.name : null;
                    if (!string.IsNullOrEmpty(texName))
                        yield return texName;
                }
                foreach (string extra in AdditionalOwnPresentedTexts)
                    yield return extra;
            }
        }

        protected virtual IEnumerable<string> AdditionalOwnPresentedTexts
        {
            get { yield break; }
        }

        // ------------------------------------------------------------------
        // Arrival announcement. Both pages' PageTitle is "ChooseAIStoryteller".
        // ------------------------------------------------------------------

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            string tabCount = TabCountFragment();
            string title = (string)"ChooseAIStoryteller".Translate();
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title);
            AnnounceCurrentItem();
        }

        /// <summary>Re-reads the current row (the Anomaly-dialog return handoff — StorytellerSelectionPatch.ReturnToStorytellerMode).</summary>
        internal void ReannounceCurrent()
        {
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // Focus ring. Both twins' rows come out of one vanilla method, so the mapping from a focused
        // model row to the identity DifficultyRowKeyTaps marked it with lives here once. Rows with no
        // listing row on this page return None: the portraits (their own ring is below), the section
        // headers, the per-row Reset, and the Anomaly playstyle radios vanilla draws in its own dialog.
        // ------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Vector2> storytellerScrollPosition =
            AccessTools.StaticFieldRefAccess<Vector2>(AccessTools.Field(typeof(StorytellerUI), "scrollPosition"));

        /// <summary>
        /// The portraits are the one region with no listing row behind them: vanilla draws a bare
        /// column of option backgrounds, which <see cref="OptionBackgroundRowCapture"/> records in
        /// storyteller order. <see cref="RowFocus"/> returns None here, so the two ring paths can
        /// never both answer.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            if (Model.RegionIndex < 0 || Model.RegionIndex >= ActiveRegions.Count
                || ActiveRegions[Model.RegionIndex] != RegionKind.Storytellers)
                return default(Rect);
            ListModel list = Model.CurrentRegion;
            if (list == null || list.IsEmpty)
                return default(Rect);
            IReadOnlyList<Rect> portraits =
                OptionBackgroundRowCapture.ScreenRects(OptionBackgroundRowCapture.Consumer.StorytellerPortraits);
            if (portraits.Count != Storytellers.Count || list.Index < 0 || list.Index >= portraits.Count)
                return default(Rect);
            return portraits[list.Index];
        }

        /// <summary>
        /// Keeps the focused portrait inside the column's scroll view. The recorded rects are already
        /// in the view space vanilla laid them out in; only the visible height is unavailable from
        /// them, so the bracket takes it from the interface rect. An unrecorded pass skips, and the
        /// next settle self-heals.
        /// </summary>
        private void ScrollPortraitIntoView(int region, int index)
        {
            if (region < 0 || region >= ActiveRegions.Count || ActiveRegions[region] != RegionKind.Storytellers)
                return;
            IReadOnlyList<Rect> portraits =
                OptionBackgroundRowCapture.RawRects(OptionBackgroundRowCapture.Consumer.StorytellerPortraits);
            if (portraits.Count != Storytellers.Count || index < 0 || index >= portraits.Count)
                return;
            float viewHeight = OptionBackgroundRowCapture.ViewHeight(OptionBackgroundRowCapture.Consumer.StorytellerPortraits);
            Rect portrait = portraits[index];
            if (viewHeight < portrait.height)
                return;
            ref Vector2 scroll = ref storytellerScrollPosition();
            scroll.y = Mathf.Clamp(scroll.y, portrait.yMax - viewHeight, portrait.y);
        }

        public override void OnPush()
        {
            base.OnPush();
            ScreenScopeDrawPatch.RegisterListingClient(this);
            RefreshModel();
            // Rows select on arrival: land the first visit on vanilla's own fallback (Rough).
            if (CurrentDifficultyDef == null)
            {
                int region = ActiveRegions.IndexOf(RegionKind.Difficulty);
                int index = Difficulties.IndexOf(DifficultyDefOf.Rough);
                if (region >= 0 && index >= 0 && index < (Model.Region(region)?.Count ?? 0))
                    Model.Region(region).MoveTo(index);
            }
        }

        public override void OnPop()
        {
            ScreenScopeDrawPatch.UnregisterListingClient(this);
            base.OnPop();
        }

        Window IListingRingClient.ListingRingWindow
        {
            get { return OwnedWindow; }
        }

        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            ListModel list = Model.CurrentRegion;
            if (list == null || list.IsEmpty)
                return ListingRingFocus.None;
            return RowFocus(Model.RegionIndex, list.Index);
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            for (int region = 0; region < ActiveRegions.Count; region++)
            {
                int count = ContentItemCount(region);
                for (int index = 0; index < count; index++)
                {
                    ListingRowCapture.AddRouteCandidates(RowFocus(region, index), region, index, candidates, targets);
                }
            }
        }

        /// <summary>The listing row one content item stands on, or None when it has none.</summary>
        private ListingRingFocus RowFocus(int regionIndex, int index)
        {
            if (regionIndex < 0 || regionIndex >= ActiveRegions.Count)
                return ListingRingFocus.None;
            switch (ActiveRegions[regionIndex])
            {
                case RegionKind.Difficulty:
                    if (index < 0 || index >= Difficulties.Count)
                        return ListingRingFocus.None;
                    return new ListingRingFocus { RowObject = Difficulties[index] };
                case RegionKind.SaveMode:
                    return SaveModeFocus(index);
                case RegionKind.AnomalySettings:
                    // Many-to-one: every row of this region edits what a sighted player reaches
                    // through the single "AnomalySettings..." button.
                    return new ListingRingFocus
                    {
                        RowKey = DifficultyRowKeys.AnomalyButtonSite,
                        LabelTripwire = (string)"AnomalySettings".Translate() + "...",
                    };
                case RegionKind.CustomDifficulty:
                    return CustomRowFocus(index);
                default:
                    return ListingRingFocus.None;
            }
        }

        private static ListingRingFocus SaveModeFocus(int index)
        {
            if (index != 0 && index != 1)
                return ListingRingFocus.None;
            bool reload = index == 0;
            return new ListingRingFocus
            {
                RowKey = reload ? DifficultyRowKeys.ReloadAnytimeMode : DifficultyRowKeys.CommitmentMode,
                LabelTripwire = reload
                    ? (string)"ReloadAnytimeMode".Translate()
                    : (string)"CommitmentMode".TranslateWithBackup("PermadeathMode"),
            };
        }

        private ListingRingFocus CustomRowFocus(int index)
        {
            if (index < 0 || index >= CustomRows.Count)
                return ListingRingFocus.None;
            DifficultySetting setting = CustomRows[index].Setting;
            string covered = setting == null ? null : setting.CoveredField;
            if (covered == null)
                return ListingRingFocus.None;
            string anomalyKey = DifficultyRowKeys.AnomalySliderKey(covered);
            if (anomalyKey != null)
                return new ListingRingFocus { RowKey = anomalyKey };
            // CoveredField IS the optionName vanilla passes its own helpers, so the token matches by
            // construction. Only checkbox rows carry a tripwire: vanilla appends the formatted value
            // to a slider's label, leaving no stable string to compare.
            return new ListingRingFocus
            {
                RowKey = covered,
                LabelTripwire = setting is DifficultyCheckboxSetting ? setting.Label : null,
            };
        }

        private sealed class CustomRow
        {
            /// <summary>The setting behind this row (slider/checkbox/reset/playstyle).</summary>
            public DifficultySetting Setting;

            /// <summary>The vanilla section this setting belongs to; sections take no row of their own.</summary>
            public string Section;
        }
    }
}
