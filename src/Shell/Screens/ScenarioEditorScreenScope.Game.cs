using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the scenario editor page
    /// (<see cref="Page_ScenarioEditor"/>), window-attached via ShellBootstrap;
    /// <see cref="ScenarioBuilderState"/> is purely the row source.
    /// Region 0 is the metadata: the Seed row (present only while the page's private
    /// <c>seedIsValid</c> is true; editing regenerates the ENTIRE scenario per keystroke, as
    /// vanilla's own <c>if (text != seed)</c> branch does), the Edit mode checkbox, and the
    /// Title/Summary/Description fields — gated on <c>editMode</c> for parity, since
    /// <c>ScenarioUI.DrawScenarioEditInterface</c> is the one place vanilla ever draws them.
    /// Region 1 is a <see cref="TreeRegionScope"/> panel per mode: the read-only detail tree with
    /// edit mode off, otherwise the parts tree — part branches over the 3-level shape
    /// <see cref="ScenPartListItemManager"/> models, with field leaves as live controls (typed
    /// node subclasses in the Parts partial). The shared tree carries expansion, submenu mode,
    /// sibling jumps, and typeahead auto-expansion; only a Quantity leaf overrides Left/Right
    /// (<see cref="AdjustContentItem"/>) to step its value instead.
    /// Deletes and reorders ride <c>Scenario.RemovePart</c> (gated <c>PlayerAddRemovable</c>) and
    /// <c>Scenario.CanReorder</c>/<c>Reorder</c>. A Dropdown field is a ComboBox, so Left/Right
    /// never change its value in place.
    /// <see cref="CaptureWindowButtons"/> is false because the page's content draws many
    /// non-toolbar buttons (per-part icons, field pickers), so the toolbar is declared instead.
    /// Next and Back are static <see cref="AdvanceRequested"/>/<see cref="BackRequested"/> flags
    /// around the reflected gates, so vanilla's compatibility check and unsaved-changes prompts
    /// fire; <see cref="ScenarioEditorScreenScopePatch_CanDoNext"/> and its _CanDoBack twin block
    /// the raw keyboard poll while this scope or an overlay is live and the flag unset.
    /// </summary>
    public sealed partial class ScenarioEditorScreenScope : TreeRegionScope, IListingRingClient
    {
        private enum ScenarioRowKind { Seed, EditMode, Title, Summary, Description }

        private static readonly AccessTools.FieldRef<Page_ScenarioEditor, Scenario> curScenField =
            AccessTools.FieldRefAccess<Page_ScenarioEditor, Scenario>("curScen");
        private static readonly AccessTools.FieldRef<Page_ScenarioEditor, string> seedField =
            AccessTools.FieldRefAccess<Page_ScenarioEditor, string>("seed");
        private static readonly AccessTools.FieldRef<Page_ScenarioEditor, bool> seedIsValidField =
            AccessTools.FieldRefAccess<Page_ScenarioEditor, bool>("seedIsValid");
        private static readonly AccessTools.FieldRef<Page_ScenarioEditor, bool> editModeField =
            AccessTools.FieldRefAccess<Page_ScenarioEditor, bool>("editMode");

        private static readonly MethodInfo canDoNextMethod = AccessTools.Method(typeof(Page_ScenarioEditor), "CanDoNext");
        private static readonly MethodInfo doNextMethod = AccessTools.Method(typeof(Page), "DoNext");
        private static readonly MethodInfo canDoBackMethod = AccessTools.Method(typeof(Page), "CanDoBack");
        private static readonly MethodInfo doBackMethod = AccessTools.Method(typeof(Page), "DoBack");
        private static readonly MethodInfo checkAllPartsCompatibleMethod =
            AccessTools.Method(typeof(Page_ScenarioEditor), "CheckAllPartsCompatible");
        private static readonly MethodInfo workshopUploadMethod = AccessTools.Method(typeof(Workshop), "Upload");

        /// <summary>True only while the Next declared action drives the vanilla gate-and-advance (the guard-twin flag).</summary>
        internal static bool AdvanceRequested;

        /// <summary>True only while this scope's own Back invocation runs its vanilla gate (the guard-twin flag).</summary>
        internal static bool BackRequested;

        /// <summary>The one live instance — lets ScenarioBuilderActions notify the scope after Alt+A/Alt+L/Alt+R mutate the tree/scenario out from under it.</summary>
        internal static ScenarioEditorScreenScope Active { get; private set; }

        private readonly Page_ScenarioEditor page;
        private readonly List<ScenarioRowKind> scenarioRows = new List<ScenarioRowKind>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession metadataEditSession = new TextFieldEditSession();

        /// <summary>Edit mode ON: the parts tree.</summary>
        private readonly TreePanel partsPanel;

        /// <summary>Edit mode OFF: the read-only detail tree. Its own panel so each mode keeps its expansion.</summary>
        private readonly TreePanel infoPanel;

        private Scenario lastInfoScenario;
        private ScenarioRowKind editingMetadataRow;
        private bool announcedOpen;

        public ScenarioEditorScreenScope(Page_ScenarioEditor page)
        {
            this.page = page;
            partsPanel = CreatePanel();
            infoPanel = CreatePanel();

            Claim(SharedMenuGrammar.Cancel, e => EscapeBack(), when: () => !TypeaheadHasActiveSearch);
            Claim("scenarioBuilder.next", e => NextAction());
            Claim("scenarioBuilder.load", e => ActivateLoad());
            Claim("scenarioBuilder.save", e => ActivateSave());
            Claim("scenarioBuilder.randomizeSeed", e => ActivateRandomizeSeed());
            Claim("scenarioBuilder.addPart", e => ActivateAddPart(), when: EditModeOn);
            Claim(SharedMenuGrammar.ReorderUp, e => ActivateReorder(-1), when: OnPartRow);
            Claim(SharedMenuGrammar.ReorderDown, e => ActivateReorder(1), when: OnPartRow);
            Claim("scenarioBuilder.deletePart", e => ActivateDelete(), when: PartsRegionLive);
            // Expand-all rides the base's tree.expandAllSiblings claim (identical chords).
            Claim("scenarioBuilder.readFullField", e => ActivateReadFullField(), when: PartsRegionLive);
            // The Quantity stepper's coarse-step chords; the base's own Left/Right claim covers
            // the plain step-1 case.
            Claim("scenarioBuilder.quantityStepLarge", e => AdjustQuantityFromChord(e), when: OnQuantityFieldRow);

            RegisterPopTeardown(metadataEditSession.CancelIfActive);
            RegisterPopTeardown(quantityEditSession.CancelIfActive);
            RegisterPopTeardown(textFieldEditSession.CancelIfActive);
        }

        public override string Name
        {
            get { return "scenario-editor"; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>The page's content also draws non-toolbar buttons (per-part icons, field pickers), so the toolbar is declared instead of captured.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }


        // ------------------------------------------------------------------
        // Region gating.
        // ------------------------------------------------------------------

        private bool EditModeOn()
        {
            return editModeField(page);
        }

        private bool PartsRegionLive()
        {
            RefreshModel();
            return Model.RegionIndex == 1 && !Model.CurrentRegion.IsEmpty;
        }

        private bool OnPartRow()
        {
            return PartsRegionLive() && EditModeOn() && CurrentTreeItem() is PartNode;
        }

        private bool OnQuantityFieldRow()
        {
            ScenarioBuilderState.PartField field = CurrentField();
            return field != null && field.Type == ScenarioBuilderState.FieldType.Quantity;
        }

        private ScenarioBuilderState.PartField CurrentField()
        {
            if (!PartsRegionLive() || !EditModeOn()) return null;
            return CurrentTreeItem() is FieldNode field ? field.Field : null;
        }

        // ------------------------------------------------------------------
        // Content model.
        // ------------------------------------------------------------------

        protected override void RefreshContent()
        {
            base.RefreshContent();
            RefreshScenarioRows();
            // Lazy first build per mode; every mutation path rebuilds explicitly through
            // RebuildRegionTreePreservingState (the IdeoTypedPreceptScreenScope idiom).
            TreePanel panel = ActiveRegionPanel;
            if (panel.Tree.Root == null)
            {
                SetTreeRoot(panel, BuildActiveRegionRoot());
            }
        }

        /// <summary>The panel region 1 presents right now: parts in edit mode, the info tree otherwise.</summary>
        private TreePanel ActiveRegionPanel
        {
            get { return editModeField(page) ? partsPanel : infoPanel; }
        }

        private InspectionTreeItem BuildActiveRegionRoot()
        {
            return editModeField(page) ? BuildPartsRoot() : BuildInfoRoot();
        }

        /// <summary>
        /// Rebuilds region 1's tree from the live scenario, keeping expansion and the cursor's
        /// logical node. Call after any mutation; never announces.
        /// </summary>
        private void RebuildRegionTreePreservingState()
        {
            RefreshModel();
            ListModel region = Model.Region(1);
            int before = region != null && !region.IsEmpty ? region.Index : 0;
            int restored = SetTreeRootPreservingState(ActiveRegionPanel, BuildActiveRegionRoot(), before);
            RefreshModel();
            ListModel after = Model.Region(1);
            if (after != null && !after.IsEmpty)
            {
                after.MoveTo(Mathf.Clamp(restored >= 0 ? restored : before, 0, after.Count - 1));
            }
        }

        private void RefreshScenarioRows()
        {
            scenarioRows.Clear();
            bool editMode = editModeField(page);
            if (editMode && seedIsValidField(page))
            {
                // MUTATION-C: mirrors DoConfigControls' OWN per-frame reset (decompiled
                // :126-128, `if (editMode) { seedIsValid = false; ... }`) — vanilla's real
                // field settles back to false on the very next draw pass regardless of
                // what set it true a moment earlier (e.g. RandomizeSeed's unconditional
                // `seedIsValid = true` a few lines above that same block). Forcing it here
                // too, not just gating presentation below, avoids a transient Seed row
                // that the game's own next frame would already have invalidated.
                seedIsValidField(page) = false;
            }
            if (!editMode && seedIsValidField(page))
            {
                scenarioRows.Add(ScenarioRowKind.Seed);
            }
            scenarioRows.Add(ScenarioRowKind.EditMode);
            if (editMode)
            {
                scenarioRows.Add(ScenarioRowKind.Title);
                scenarioRows.Add(ScenarioRowKind.Summary);
                scenarioRows.Add(ScenarioRowKind.Description);
            }
        }

        /// <summary>The info pane's tree: the same detail root the S2 pattern read, with a
        /// placeholder child when the scenario has nothing beyond its name.</summary>
        private InspectionTreeItem BuildInfoRoot()
        {
            Scenario curScen = curScenField(page);
            lastInfoScenario = curScen;
            InspectionTreeItem root = ScenarioNavigationState.BuildDetailTree(curScen);
            if (root.Children.Count == 0)
            {
                root.Children.Add(new InspectionTreeItem
                {
                    Label = "RimWorldAccess.ScenarioBuilder.NoAdditionalDetails".Translate(),
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root,
                });
            }
            return root;
        }

        // ------------------------------------------------------------------
        // Region wiring: region 0 is all prefix rows (scenario metadata), region 1 the tree.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override int TreeRegionIndex
        {
            get { return 1; }
        }

        protected override TreePanel PanelFor(int region)
        {
            return region == 1 ? ActiveRegionPanel : null;
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.ScenarioBuilder.PartsRegion".Translate(); }
        }

        protected override string ContentRegionName(int region)
        {
            return region == 0
                ? (string)"RimWorldAccess.ScenarioBuilder.ScenarioRegion".Translate()
                : TreeRegionLabel;
        }

        /// <summary>Region 1 has the read-only scenario-name header row above the info tree.</summary>
        protected override int PrefixRowCountFor(int region)
        {
            if (region == 0) return scenarioRows.Count;
            return editModeField(page) ? 0 : 1;
        }

        protected override ElementDescription DescribePrefixRow(int region, int index)
        {
            if (region == 0) return DescribeScenarioRow(index);
            Scenario curScen = curScenField(page);
            return new ElementDescription
            {
                Label = curScen != null ? curScen.name : "",
                ReadOnly = true,
            };
        }

        protected override void ActivatePrefixRow(int region, int index)
        {
            if (region == 0) { ActivateScenarioRow(index); return; }
            AnnounceCurrentItem();
        }

        /// <summary>A quantity field adjusts in place; every other tree row keeps expand/collapse.</summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            TreePanel panel = PanelFor(region);
            if (panel != null)
            {
                int treeIndex = index - PrefixRowCountFor(region);
                if (treeIndex >= 0 && treeIndex < panel.Tree.Count
                    && panel.Tree.Visible[treeIndex] is FieldNode field
                    && field.Field.Type == ScenarioBuilderState.FieldType.Quantity)
                {
                    StepQuantity(field.Field, direction, 1);
                    return;
                }
            }
            base.AdjustContentItem(region, index, direction);
        }

        // ------------------------------------------------------------------
        // Region 0: Scenario (metadata).
        // ------------------------------------------------------------------

        private ElementDescription DescribeScenarioRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= scenarioRows.Count) return d;
            Scenario scen = curScenField(page);
            switch (scenarioRows[index])
            {
                case ScenarioRowKind.Seed:
                    d.Label = (string)"Seed".Translate().CapitalizeFirst();
                    d.Role = ElementRole.TextField;
                    d.Value = seedField(page) ?? "";
                    d.ValueBlank = string.IsNullOrEmpty(d.Value);
                    return d;
                case ScenarioRowKind.EditMode:
                    d.Label = (string)"EditMode".Translate().CapitalizeFirst();
                    d.Role = ElementRole.Checkbox;
                    d.Check = editModeField(page) ? CheckState.Checked : CheckState.Unchecked;
                    return d;
                case ScenarioRowKind.Title:
                    d.Label = (string)"ScenarioTitle".Translate();
                    d.Role = ElementRole.TextField;
                    d.Value = scen?.name ?? "";
                    d.ValueBlank = string.IsNullOrEmpty(d.Value);
                    return d;
                case ScenarioRowKind.Summary:
                    d.Label = (string)"Summary".Translate();
                    d.Role = ElementRole.TextField;
                    d.Value = scen?.summary ?? "";
                    d.ValueBlank = string.IsNullOrEmpty(d.Value);
                    return d;
                default: // Description
                    d.Label = (string)"Description".Translate();
                    d.Role = ElementRole.TextField;
                    d.Value = scen?.description ?? "";
                    d.ValueBlank = string.IsNullOrEmpty(d.Value);
                    return d;
            }
        }

        private void ActivateScenarioRow(int index)
        {
            if (index < 0 || index >= scenarioRows.Count) return;
            ScenarioRowKind kind = scenarioRows[index];
            if (kind == ScenarioRowKind.EditMode)
            {
                ToggleEditMode();
                return;
            }
            BeginMetadataEdit(kind);
        }

        private void ToggleEditMode()
        {
            // MUTATION-C: mirrors Page_ScenarioEditor.DoConfigControls' CheckboxLabeled write
            // (decompiled :125) — no gated vanilla setter exists for the private editMode field.
            bool newValue = !editModeField(page);
            editModeField(page) = newValue;
            if (newValue)
            {
                // Mirrors decompiled :128 — entering edit mode invalidates the seed field.
                seedIsValidField(page) = false;
            }
            // Region 1 swaps panels with the mode; the incoming panel's data may be stale.
            RebuildRegionTreePreservingState();
            var d = new ElementDescription { Check = newValue ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void BeginMetadataEdit(ScenarioRowKind kind)
        {
            Scenario scen = curScenField(page);
            if (scen == null) return;
            editingMetadataRow = kind;

            string current;
            TextFieldSpec spec;
            string displayLabel;
            switch (kind)
            {
                case ScenarioRowKind.Seed:
                    current = seedField(page) ?? "";
                    spec = TextFieldSpec.Unrestricted("RimWorldAccess.TextInput.LabelDefault");
                    displayLabel = (string)"Seed".Translate().CapitalizeFirst();
                    break;
                case ScenarioRowKind.Title:
                    current = scen.name ?? "";
                    // Harvested from Scenario.NameMaxLength (Scenario.cs:49) — the same
                    // constant TrimmedToLength(55) reads in ScenarioUI.cs:44.
                    spec = new TextFieldSpec("RimWorldAccess.TextInput.LabelScenarioTitle", maxLength: Scenario.NameMaxLength, minLength: 0, forbidGrammarSpecials: true);
                    displayLabel = (string)"ScenarioTitle".Translate();
                    break;
                case ScenarioRowKind.Summary:
                    current = scen.summary ?? "";
                    // Harvested from Scenario.SummaryMaxLength (Scenario.cs:51).
                    spec = new TextFieldSpec("RimWorldAccess.TextInput.LabelScenarioSummary", maxLength: Scenario.SummaryMaxLength, minLength: 0, forbidGrammarSpecials: true, multiLine: true);
                    displayLabel = (string)"Summary".Translate();
                    break;
                default: // Description
                    current = scen.description ?? "";
                    // Harvested from Scenario.DescriptionMaxLength (Scenario.cs:53).
                    spec = new TextFieldSpec("RimWorldAccess.TextInput.LabelScenarioDescription", maxLength: Scenario.DescriptionMaxLength, minLength: 0, forbidGrammarSpecials: true, multiLine: true);
                    displayLabel = (string)"Description".Translate();
                    break;
            }
            metadataEditSession.EnterEdit(current, spec, displayLabel, ApplyMetadataEdit, FinishMetadataEdit);
        }

        private void ApplyMetadataEdit(string value)
        {
            Scenario scen = curScenField(page);
            if (scen == null) return;
            switch (editingMetadataRow)
            {
                case ScenarioRowKind.Seed:
                    // MUTATION-C: mirrors Page_ScenarioEditor.DoConfigControls' seed TextEntry
                    // branch (decompiled :113-119) exactly — vanilla checks `text != seed` on
                    // EVERY draw pass and regenerates the whole scenario from the new seed text;
                    // TextFieldEditSession.MirrorLive() calls this same apply once per GUI pass
                    // while editing (see OnHostDrawPass below), reproducing that per-keystroke
                    // regeneration.
                    seedField(page) = value;
                    Scenario regenerated = ScenarioMaker.GenerateNewRandomScenario(value);
                    curScenField(page) = regenerated;
                    ScenarioBuilderState.SetCurrentScenario(regenerated);
                    break;
                case ScenarioRowKind.Title:
                    scen.name = value;
                    break;
                case ScenarioRowKind.Summary:
                    scen.summary = value;
                    break;
                case ScenarioRowKind.Description:
                    scen.description = value;
                    break;
            }
            ScenarioBuilderState.SetDirty();
        }

        private void FinishMetadataEdit()
        {
            // A seed edit regenerated the whole scenario per keystroke; region 1's tree is stale.
            RebuildRegionTreePreservingState();
            AnnounceCurrentItem();
        }

        private void ActivateLoad()
        {
            ScenarioBuilderActions.OpenLoadDialog();
        }

        private void ActivateSave()
        {
            ScenarioBuilderActions.OpenSaveDialog();
        }

        private void ActivateRandomizeSeed()
        {
            ScenarioBuilderActions.RandomizeSeed();
        }

        private void ActivateAddPart()
        {
            ScenarioBuilderActions.OpenAddPartMenu();
        }

        private void ActivateUpload()
        {
            Scenario scen = curScenField(page);
            if (scen == null) return;
            // Vehicle A: the page's upload block is inline inside DoConfigControls, which also
            // draws the Load/Save/RandomizeSeed/EditMode/AddPart buttons, so there is no isolated
            // private method to reflect-invoke. This replicates that block's own sequence of
            // vanilla calls exactly rather than inventing logic.
            if (checkAllPartsCompatibleMethod != null
                && !(bool)checkAllPartsCompatibleMethod.Invoke(null, new object[] { scen }))
            {
                return;
            }
            AcceptanceReport report = scen.TryUploadReport();
            if (!report.Accepted)
            {
                Messages.Message(report.Reason, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmSteamWorkshopUpload".Translate(), delegate
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmContentAuthor".Translate(), delegate
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    // Workshop.Upload is internal in Assembly-CSharp, so it must be
                    // reflect-invoked from this assembly.
                    workshopUploadMethod?.Invoke(null, new object[] { scen });
                }, destructive: true));
            }, destructive: true));
        }

        // ------------------------------------------------------------------
        // Focus ring. The config column and edit pane metadata labels are keyed rows; a scenario part,
        // and a slider field drawn in its rect, ring by its live ScenPart (a GetScenPartRect parameter);
        // route candidates keep the part alone. Rows with no vanilla counterpart return None.
        // ------------------------------------------------------------------

        Window IListingRingClient.ListingRingWindow
        {
            get { return page; }
        }

        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return ListingRingFocus.None;
            FieldNode box = SliderFieldAt(Model.RegionIndex, region.Index);
            if (box != null && box.Parent is PartNode owner)
                return new ListingRingFocus { RowObject = owner.Data };
            return RowFocus(Model.RegionIndex, region.Index);
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            AddRegionRouteCandidates(0, scenarioRows.Count, candidates, targets);
            AddRegionRouteCandidates(1, ContentItemCount(1), candidates, targets);
            int buttons = Model.RegionCount - 1;
            IReadOnlyList<ScreenAction> declared = DeclaredActions;
            if (buttons > 1 && declared != null)
                AddRegionRouteCandidates(buttons, declared.Count, candidates, targets);
        }

        private void AddRegionRouteCandidates(int regionIndex, int count,
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            for (int index = 0; index < count; index++)
            {
                ListingRowCapture.AddRouteCandidates(RowFocus(regionIndex, index), regionIndex, index, candidates, targets);
            }
        }

        /// <summary>The listing row one item stands on, or None when it has none.</summary>
        private ListingRingFocus RowFocus(int regionIndex, int index)
        {
            if (regionIndex == 0)
                return ScenarioRowFocus(index);
            if (regionIndex == 1)
                return editModeField(page) ? PartRowFocus(index) : ListingRingFocus.None;
            // Buttons is always the model's last region; the extras rows before it carry no token.
            return regionIndex == Model.RegionCount - 1 ? ActionRowFocus(index) : ListingRingFocus.None;
        }

        /// <summary>The Seed/Title/Summary/Description text fields are untapped widgets, so each rings vanilla's own label row above it.</summary>
        private ListingRingFocus ScenarioRowFocus(int index)
        {
            if (index < 0 || index >= scenarioRows.Count)
                return ListingRingFocus.None;
            switch (scenarioRows[index])
            {
                case ScenarioRowKind.Seed:
                    return KeyFocus(ScenarioEditorRowKeys.Seed, (string)"Seed".Translate().CapitalizeFirst());
                case ScenarioRowKind.EditMode:
                    return KeyFocus(ScenarioEditorRowKeys.EditMode, (string)"EditMode".Translate().CapitalizeFirst());
                case ScenarioRowKind.Title:
                    return KeyFocus(ScenarioEditorRowKeys.Title, (string)"ScenarioTitle".Translate());
                case ScenarioRowKind.Summary:
                    return KeyFocus(ScenarioEditorRowKeys.Summary, (string)"Summary".Translate());
                default:
                    return KeyFocus(ScenarioEditorRowKeys.Description, (string)"Description".Translate());
            }
        }

        private ListingRingFocus PartRowFocus(int index)
        {
            IReadOnlyList<InspectionTreeItem> visible = partsPanel.Tree.Visible;
            if (index < 0 || index >= visible.Count || !(visible[index] is PartNode part))
                return ListingRingFocus.None;
            return new ListingRingFocus { RowObject = part.Data };
        }

        /// <summary>This scope captures no window buttons, so the Buttons region is <see cref="DeclaredActions"/> alone and the row index addresses it directly.</summary>
        private ListingRingFocus ActionRowFocus(int index)
        {
            IReadOnlyList<ScreenAction> declared = DeclaredActions;
            if (declared == null || index < 0 || index >= declared.Count)
                return ListingRingFocus.None;
            ScreenAction action = declared[index];
            switch (action.ActionId)
            {
                case "scenarioBuilder.load":
                    return KeyFocus(ScenarioEditorRowKeys.Load, action.Label);
                case "scenarioBuilder.save":
                    return KeyFocus(ScenarioEditorRowKeys.Save, action.Label);
                case "scenarioBuilder.randomizeSeed":
                    return KeyFocus(ScenarioEditorRowKeys.RandomizeSeed, action.Label);
                case "scenarioBuilder.addPart":
                    return KeyFocus(ScenarioEditorRowKeys.AddPart, action.Label);
                case null:
                    // The upload button is the only declared action without an id.
                    return KeyFocus(ScenarioEditorRowKeys.UploadSite, action.Label);
                default:
                    // Back and Next are the page's own bottom buttons, drawn raw.
                    return ListingRingFocus.None;
            }
        }

        private static ListingRingFocus KeyFocus(string rowKey, string tripwire)
        {
            return new ListingRingFocus { RowKey = rowKey, LabelTripwire = tripwire };
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            ScreenScopeDrawPatch.RegisterListingClient(this);
            Active = this;
            announcedOpen = false;
            lastInfoScenario = null;
            ResetTree(partsPanel);
            ResetTree(infoPanel);
        }

        public override void OnPop()
        {
            ScreenScopeDrawPatch.UnregisterListingClient(this);
            base.OnPop();
            if (ReferenceEquals(Active, this)) Active = null;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen) return;
            announcedOpen = true;
            string tabCount = TabCountFragment();
            string title = page.PageTitle;
            TolkHelper.SpeakData(string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title);
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Called from <see cref="ScenarioBuilderPatch"/>'s DoWindowContents prefix each GUI pass so
        /// a live edit session's buffer stays mirrored into the backing store — critical for the
        /// Seed field's per-keystroke regeneration, and so an Escape mid-edit keeps what was typed.
        /// </summary>
        internal void OnHostDrawPass()
        {
            ShellTextFocus.ReleaseNativeFocus();
            metadataEditSession.MirrorLive();
            quantityEditSession.MirrorLive();
            textFieldEditSession.MirrorLive();
        }

        /// <summary>Rebuilds the tree and re-announces the current row after an external mutation lands.</summary>
        internal void RefreshAndReannounceAfterExternalChange()
        {
            RebuildRegionTreePreservingState();
            AnnounceCurrentItem();
        }

        /// <summary>Moves the cursor to the newly-added part by ScenPart identity and re-announces it in full.</summary>
        internal void NotifyPartAdded(ScenPart newPart)
        {
            RebuildRegionTreePreservingState();
            Model.MoveToRegion(1);
            InspectionTreeItem node = FindPartNode(newPart);
            if (node != null && TryRevealAndSelect(node))
            {
                SyncRegionFromCurrentTree();
            }
            AnnounceCurrentItem();
        }

        /// <summary>Resets both regions' cursors after Load or RandomizeSeed swaps the whole scenario out.</summary>
        internal void ResetCursorAfterScenarioSwap()
        {
            SetTreeRoot(ActiveRegionPanel, BuildActiveRegionRoot());
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            region?.MoveTo(0);
            AnnounceCurrentItem();
        }
    }

}
