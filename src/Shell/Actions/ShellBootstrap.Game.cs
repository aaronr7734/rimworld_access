using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One-time shell startup: registers the action inventory, applies persisted rebinds, and
    /// installs the platform-aware chord display hooks. Runs after defs load
    /// (StaticConstructorOnStartup) on the main thread.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ShellBootstrap
    {
        static ShellBootstrap()
        {
            FocusStackCore.ScopeTraceSink = delegate (string line)
            {
                FlightRecorder.Record("scope", line);
            };
            FocusStackCore.HandlerErrorSink = delegate (string actionId, System.Exception ex)
            {
                RimWorldAccess.ModLogger.LimitedError("Shell action '" + actionId + "' failed", ex);
                FlightRecorder.Record("key-error", actionId + ": " + ex.Message);
            };
            InstallChordDisplayHooks();

            ShellActionInventory.RegisterAll(ActionRegistry.Catalog);
            ShellBindingPersistence.Load(ActionRegistry.Catalog);

            // Window types whose focus scopes attach through the WindowStack mirror.
            ScopeForWindow.Register(typeof(RimWorld.MainTabWindow_Menu), delegate { return new PauseMenuScope(); });
            // The credits / victory screen: readable rows for everything that scrolls by.
            ScopeForWindow.Register(typeof(RimWorld.Screen_Credits), delegate (Verse.Window w)
            {
                return new CreditsScope((RimWorld.Screen_Credits)w);
            });
            ScopeForWindow.Register(typeof(Verse.Dialog_MessageBox), delegate (Verse.Window w)
            {
                return new MessageBoxScope((Verse.Dialog_MessageBox)w);
            });
            // Dialog_Confirm is a plain Window, NOT a Dialog_MessageBox, so the hierarchy entry
            // never catches it.
            ScopeForWindow.Register(typeof(Verse.Dialog_Confirm), delegate (Verse.Window w)
            {
                return new ConfirmDialogScope((Verse.Dialog_Confirm)w);
            });
            // The whole FloatMenu family, modded subclasses included: GenericReaderEligible
            // refuses every FloatMenu, so an unregistered subclass would reach the stack with no
            // scope and no fallback. Menus DialogInterceptionPatch swallows never enter the stack
            // and keep their windowless path. A subclass overriding DoWindowContents without
            // calling base escapes FloatMenuDrawPatch, keeping navigation but losing its ring and
            // opening announcement.
            ScopeForWindow.RegisterHierarchy(typeof(Verse.FloatMenu), delegate (Verse.Window w)
            {
                return new FloatMenuScope((Verse.FloatMenu)w);
            });
            // The real Schedule tab.
            ScopeForWindow.Register(typeof(RimWorld.MainTabWindow_Schedule), delegate (Verse.Window w)
            {
                return new ScheduleScope((RimWorld.MainTabWindow_Schedule)w);
            });
            // The generic pawn-table tier: any MainTabWindow_PawnTable no bespoke scope claims.
            // Exact-type registrations beat this hierarchy arm by ScopeForWindow precedence, and
            // the factory declines the five intercepted vanilla tabs.
            ScopeForWindow.RegisterHierarchy(typeof(RimWorld.MainTabWindow_PawnTable),
                GenericPawnTableScope.TryCreate);
            // The real save/load dialogs (one scope, mode from the concrete type)
            // and the load-flow mod-mismatch dialog.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_SaveFileList_Save), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_SaveFileList_Load), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            // The mod list's SaveLoadList presets, reusing FileListScope: it reads
            // ShouldDoTypeInField rather than the concrete type, so Save gets the name-field
            // workflow and Load gets list+typeahead. Exact-type — must not capture other
            // Dialog_FileList members.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ModList_Save), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ModList_Load), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            // The xenotype editor's Load custom list: FileListScope in load mode.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_XenotypeList_Load), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            // The scenario editor's Save/Load pickers.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ScenarioList_Save), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ScenarioList_Load), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ModMismatch), delegate (Verse.Window w)
            {
                return new ModMismatchScope((RimWorld.Dialog_ModMismatch)w);
            });
            // The real options dialog, plus the injected RimWorld Access category
            // on its rail (visible to sighted players too).
            ScopeForWindow.Register(typeof(RimWorld.Dialog_Options), delegate (Verse.Window w)
            {
                return new OptionsScope((RimWorld.Dialog_Options)w);
            });
            OptionsScope.InjectionEnabled = true;
            // Configure Spoken Announcements, opened from a button on both settings surfaces.
            ScopeForWindow.Register(typeof(Dialog_ConfigureAnnouncements), delegate (Verse.Window w)
            {
                return new AnnouncementConfigScope((Dialog_ConfigureAnnouncements)w);
            });
            // The Dialogue Log, opened from the F12 extras hub whenever Bubbles or RimTalk is
            // active.
            ScopeForWindow.Register(typeof(DialogueLogWindow), delegate (Verse.Window w)
            {
                return new DialogueLogScope((DialogueLogWindow)w);
            });
            ScopeForWindow.Register(typeof(GlobalLogWindow), delegate (Verse.Window w)
            {
                return new GlobalLogScope((GlobalLogWindow)w);
            });
            // The real owner-assignment dialog (beds, thrones, graves,
            // meditation spots, deathrest caskets).
            ScopeForWindow.Register(typeof(RimWorld.Dialog_AssignBuildingOwner), delegate (Verse.Window w)
            {
                return new AssignScope((RimWorld.Dialog_AssignBuildingOwner)w);
            });
            // Real node-tree dialogs (letters, research completion, ship
            // launch, comms negotiation) and every subclass, driven by NodeTreeScope.
            ScopeForWindow.RegisterHierarchy(typeof(Verse.Dialog_NodeTree), delegate (Verse.Window w)
            {
                return new NodeTreeScope((Verse.Dialog_NodeTree)w);
            });
            // Dialog_MessageBox subclasses reuse MessageBoxScope; the exact-type registration
            // above still wins for the base type.
            ScopeForWindow.RegisterHierarchy(typeof(Verse.Dialog_MessageBox), delegate (Verse.Window w)
            {
                return new MessageBoxScope((Verse.Dialog_MessageBox)w);
            });
            // The text-entry dialog family. Rename covers Dialog_Rename<T>
            // and every closed generic/subclass (native field). GiveName covers
            // the abstract Dialog_GiveName and its subclasses (mirrored field).
            // NamePawn is the exact type (native field + vanilla Tab cycling).
            ScopeForWindow.RegisterGenericHierarchy(typeof(Verse.Dialog_Rename<>), delegate (Verse.Window w)
            {
                return new RenameScope(w);
            });
            // The real policy-management window (apparel/food/drug/reading and any
            // modded closed generic) — see PolicyDialogScope's header.
            ScopeForWindow.RegisterGenericHierarchy(typeof(RimWorld.Dialog_ManagePolicies<>), delegate (Verse.Window w)
            {
                // Mirrors vanilla's own DoContentsRect polymorphism. Matched with `is`, most
                // derived first, so a mod subclassing a vanilla policy dialog inherits the
                // right contents rather than falling through to the list-only scope.
                if (w is Dialog_ManageCombatPolicies)
                {
                    return new CombatPolicyDialogScope(w);
                }
                if (w is Dialog_ManageHuntingPolicies)
                {
                    return new HuntingPolicyDialogScope(w);
                }
                if (w is RimWorld.Dialog_ManageDrugPolicies)
                {
                    return new DrugPolicyDialogScope(w);
                }
                if (FilterPolicyDialogScope.Handles(w))
                {
                    return new FilterPolicyDialogScope(w);
                }
                return new PlainPolicyDialogScope(w);
            });
            ScopeForWindow.RegisterHierarchy(typeof(RimWorld.Dialog_GiveName), delegate (Verse.Window w)
            {
                return new GiveNameScope(w);
            });
            ScopeForWindow.Register(typeof(Verse.Dialog_NamePawn), delegate (Verse.Window w)
            {
                return new NamePawnScope(w);
            });
            // The real Alt+I info card.
            ScopeForWindow.Register(typeof(Verse.Dialog_InfoCard), delegate (Verse.Window w)
            {
                return new InfoCardScope((Verse.Dialog_InfoCard)w);
            });
            // The real auto-slaughter dialog (Tab from Animals).
            ScopeForWindow.Register(typeof(RimWorld.Dialog_AutoSlaughter), delegate (Verse.Window w)
            {
                return new AutoSlaughterScope((RimWorld.Dialog_AutoSlaughter)w);
            });
            // The real manage-areas dialog, reached from every allowed-area picker's Manage row.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ManageAreas), delegate (Verse.Window w)
            {
                return new ManageAreasScope((RimWorld.Dialog_ManageAreas)w);
            });
            // The ideo builder's Edit symbols and Edit narrative dialogs.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ChooseIdeoSymbols), delegate (Verse.Window w)
            {
                return new ChooseIdeoSymbolsScope((RimWorld.Dialog_ChooseIdeoSymbols)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_EditIdeoDescription), delegate (Verse.Window w)
            {
                return new EditIdeoDescriptionScope((RimWorld.Dialog_EditIdeoDescription)w);
            });
            // The world-setup page's Advanced settings dialog (map size / start season).
            ScopeForWindow.Register(typeof(RimWorld.Dialog_AdvancedGameConfig), delegate (Verse.Window w)
            {
                return new AdvancedGameConfigScope(w);
            });
            // The entity codex and dryad caste dialogs.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_EntityCodex), delegate (Verse.Window w)
            {
                return new EntityCodexScope((RimWorld.Dialog_EntityCodex)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ChangeDryadCaste), delegate (Verse.Window w)
            {
                return new DryadCasteScope((RimWorld.Dialog_ChangeDryadCaste)w);
            });
            // The mech accent-colour picker off MainTabWindow_Mechs; no subclasses.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ChooseColor), delegate (Verse.Window w)
            {
                return new ChooseColorScope((RimWorld.Dialog_ChooseColor)w);
            });
            // The palette-plus-wheel picker family (allowed-area colors, glower colors, mod
            // subclasses).
            ScopeForWindow.RegisterHierarchy(typeof(RimWorld.Dialog_ColorPickerBase), delegate (Verse.Window w)
            {
                return new ColorPickerScope((RimWorld.Dialog_ColorPickerBase)w);
            });
            // The biotech gene-editor dialogs.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_CreateXenogerm), delegate (Verse.Window w)
            {
                return new XenogermScope((RimWorld.Dialog_CreateXenogerm)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_CreateXenotype), delegate (Verse.Window w)
            {
                return new XenotypeEditorScope((RimWorld.Dialog_CreateXenotype)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_SelectXenotypeIcon), delegate (Verse.Window w)
            {
                return new XenotypeIconScope((RimWorld.Dialog_SelectXenotypeIcon)w);
            });
            // The read-only gene viewer a pawn's xenotype chip opens. No BiotechActive gate:
            // Dialog_ViewGenes is an unconditional Assembly-CSharp type, the registration runs no
            // Biotech code, and the dialog closes itself in PostOpen when Biotech is off.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ViewGenes), ViewGenesScope.TryCreate);
            // The health-tab xenogerm implant picker; same ungated posture as Dialog_ViewGenes.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_SelectXenogerm), delegate (Verse.Window w)
            {
                return new SelectXenogermScope((RimWorld.Dialog_SelectXenogerm)w);
            });
            // The caravan drug-policy assignment dialog.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_AssignCaravanDrugPolicies), delegate (Verse.Window w)
            {
                return new CaravanDrugPoliciesScope((RimWorld.Dialog_AssignCaravanDrugPolicies)w);
            });
            // Hierarchy: GrowthMomentPatch's PostOpen backup activates GrowthMomentState for any
            // subclass, so the scope must cover the whole family or the modal mask swallows every
            // key on modded dialogs.
            ScopeForWindow.RegisterHierarchy(typeof(RimWorld.Dialog_GrowthMomentChoices), delegate (Verse.Window w)
            {
                return new GrowthMomentScope((RimWorld.Dialog_GrowthMomentChoices)w);
            });
            // Trade (classic or table view per the DefaultTradeView setting) + sellable items.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_Trade), delegate (Verse.Window w)
            {
                return TradeViewOpener.CreateScope((RimWorld.Dialog_Trade)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_SellableItems), delegate (Verse.Window w)
            {
                return new SellableItemsScope((RimWorld.Dialog_SellableItems)w);
            });
            // Caravan formation + split. A suppressed open yields NO scope — see ConsumeSuppression.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_FormCaravan), delegate (Verse.Window w)
            {
                if (CaravanFormationState.ConsumeSuppression())
                    return null;
                return new CaravanFormationScope((RimWorld.Dialog_FormCaravan)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Planet.Dialog_SplitCaravan), delegate (Verse.Window w)
            {
                return new SplitCaravanScope((RimWorld.Planet.Dialog_SplitCaravan)w);
            });
            // Transport pod loading + map portals: one scope class, two window types, mutually
            // exclusive via TransportPodLoadingState.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_LoadTransporters), delegate (Verse.Window w)
            {
                return new TransportPodLoadingScope(w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_EnterPortal), delegate (Verse.Window w)
            {
                return new TransportPodLoadingScope(w);
            });
            // The whole Dialog_BeginLordJob family in one hierarchy registration.
            ScopeForWindow.RegisterHierarchy(typeof(RimWorld.Dialog_BeginLordJob), delegate (Verse.Window w)
            {
                return new LordJobDialogScope(w);
            });
            // The History tab window (Statistics + Messages sub-tabs).
            ScopeForWindow.Register(typeof(RimWorld.MainTabWindow_History), delegate (Verse.Window w)
            {
                return new HistoryScope((RimWorld.MainTabWindow_History)w);
            });
            // The scenario selection and editor pages. Their four windowless overlays
            // (add-part/save/load/delete-confirm) ride ScenarioOverlayScopeMirror above this
            // anchor.
            ScopeForWindow.Register(typeof(RimWorld.Page_SelectScenario), delegate (Verse.Window w)
            {
                return new ScenarioSelectScreenScope((RimWorld.Page_SelectScenario)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Page_ScenarioEditor), delegate (Verse.Window w)
            {
                return new ScenarioEditorScreenScope((RimWorld.Page_ScenarioEditor)w);
            });
            // The pre-game storyteller page — Custom-difficulty and Anomaly editing are inline
            // regions, not modal drill-ins — plus the Anomaly Settings dialog a mod may open.
            ScopeForWindow.Register(typeof(RimWorld.Page_SelectStoryteller), delegate (Verse.Window w)
            {
                return new StorytellerScreenScope((RimWorld.Page_SelectStoryteller)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_AnomalySettings), delegate (Verse.Window w)
            {
                return new AnomalySettingsScope((RimWorld.Dialog_AnomalySettings)w);
            });
            // The world-params page; map size and starting season are inlined from
            // Dialog_AdvancedGameConfig. Its factions add-menu overlay rides
            // WorldParamsAddFactionScopeMirror above this anchor.
            ScopeForWindow.Register(typeof(RimWorld.Page_CreateWorldParams), delegate (Verse.Window w)
            {
                return new WorldParamsScreenScope((RimWorld.Page_CreateWorldParams)w);
            });
            // The world-gen starting-site page (NON-modal over the live planet) and the
            // faction-relations dialog it opens with F.
            ScopeForWindow.Register(typeof(RimWorld.Page_SelectStartingSite), delegate (Verse.Window w)
            {
                return new StartingSiteScreenScope((RimWorld.Page_SelectStartingSite)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_FactionDuringLanding), delegate (Verse.Window w)
            {
                return new FactionLandingScope((RimWorld.Dialog_FactionDuringLanding)w);
            });
            // The ideoligion preset page. Its structure/style pickers stay windowless float
            // menus; Dialog_IdeoList_Load carries its own IdeoLoadScope and masks this one
            // through ordinary stack modality.
            ScopeForWindow.Register(typeof(RimWorld.Page_ChooseIdeoPreset), delegate (Verse.Window w)
            {
                return new IdeoPresetScreenScope((RimWorld.Page_ChooseIdeoPreset)w);
            });
            // The starting-pawn editor's TWO hosts: the pre-game chargen page and the
            // Playing-state wanderers dialog both re-host StartingPawnState. The
            // filter/preset/reroll overlays ride PawnOverlayScopeMirror above this anchor, keyed
            // on the WINDOW type rather than the scope class.
            ScopeForWindow.Register(typeof(RimWorld.Page_ConfigureStartingPawns), delegate (Verse.Window w)
            {
                return new StartingPawnScreenScope(w, PawnEditorContext.GameStart);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ChooseNewWanderers), delegate (Verse.Window w)
            {
                return new StartingPawnScreenScope(w, PawnEditorContext.Wanderer);
            });

            // The mod list page — the pattern for closeOnCancel Pages and the window-pass twin
            // guard.
            ScopeForWindow.Register(typeof(RimWorld.Page_ModsConfig), delegate (Verse.Window w)
            {
                return new ModListScreenScope((RimWorld.Page_ModsConfig)w);
            });
            // The in-game storyteller/difficulty page — closeOnCancel/closeOnAccept BOTH false,
            // the opposite of Page_ModsConfig.
            ScopeForWindow.Register(typeof(RimWorld.Page_SelectStorytellerInGame), delegate (Verse.Window w)
            {
                return new StorytellerInGameScope((RimWorld.Page_SelectStorytellerInGame)w);
            });
            // The styling station (Ideology) — closeOnCancel/closeOnAccept both false, with no
            // override of OnCancelKeyPressed/OnAcceptKeyPressed.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_StylingStation), delegate (Verse.Window w)
            {
                return new StylingStationScope(w);
            });
            // Dialog_Slider — unlike Styling/StorytellerInGame, closeOnCancel/closeOnAccept keep
            // Window's TRUE default, so the base Accept/Cancel bodies are genuinely live.
            ScopeForWindow.Register(typeof(Verse.Dialog_Slider), delegate (Verse.Window w)
            {
                return new SliderDialogScope(w);
            });

            // The relocation people/animals/relics/items selection screen. No ForeignWindowAbove
            // needed: every child it can open is already ScopeForWindow-registered.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ChooseThingsForNewColony), delegate (Verse.Window w)
            {
                return new ArchonexusColonyScope((RimWorld.Dialog_ChooseThingsForNewColony)w);
            });
            // The "assign colonists" sub-dialog of the reform-ideoligion screen.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ChooseColonistsForIdeo), delegate (Verse.Window w)
            {
                return new ArchonexusConvertColonistsScope((RimWorld.Dialog_ChooseColonistsForIdeo)w);
            });
            // The Archonexus endgame's choose/build/edit-ideoligion screen. Exact-type:
            // Dialog_ConfigureIdeo has no subclasses, and worldgen's equivalent flow uses the
            // unrelated Page_ConfigureIdeo, registered separately below.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ConfigureIdeo), delegate (Verse.Window w)
            {
                return new ArchonexusIdeoScreenScope((RimWorld.Dialog_ConfigureIdeo)w);
            });
            // The saved-ideoligion load picker. Exact-type — must NOT capture
            // Dialog_IdeoList_Save or other Dialog_FileList members.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_IdeoList_Load), delegate (Verse.Window w)
            {
                return new IdeoLoadScope((RimWorld.Dialog_IdeoList_Load)w);
            });
            // The saved-ideoligion save dialog: the standard file list in save mode.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_IdeoList_Save), delegate (Verse.Window w)
            {
                return new FileListScope((RimWorld.Dialog_FileList)w);
            });
            // The structure/normal meme picker; no subclasses.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ChooseMemes), delegate (Verse.Window w)
            {
                return new IdeoMemeScreenScope((RimWorld.Dialog_ChooseMemes)w);
            });
            // Vanilla's deity editor, opened from the deity list's per-deity menu; no subclasses.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_EditDeity), delegate (Verse.Window w)
            {
                return new EditDeityDialogScope((RimWorld.Dialog_EditDeity)w);
            });
            // Vanilla's precept editor, opened from every precept's "Edit..." option; no
            // subclasses.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_EditPrecept), delegate (Verse.Window w)
            {
                return new EditPreceptDialogScope((RimWorld.Dialog_EditPrecept)w);
            });
            // Vanilla's appearance-items editor, opened from the builder hub's Appearance section
            // and every hair/beard/tattoo box; no subclasses.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_EditIdeoStyleItems), delegate (Verse.Window w)
            {
                return new StyleItemsDialogScope((RimWorld.Dialog_EditIdeoStyleItems)w);
            });
            // The in-game two-stage fluid-ideoligion reform dialog; no subclasses.
            ScopeForWindow.Register(typeof(RimWorld.Dialog_ReformIdeo), delegate (Verse.Window w)
            {
                return new IdeoReformScreenScope((RimWorld.Dialog_ReformIdeo)w);
            });
            // The worldgen custom-creation hub. Hierarchy: Page_ConfigureFluidIdeo derives from
            // Page_ConfigureIdeo, which an exact-type registration would miss.
            ScopeForWindow.RegisterHierarchy(typeof(RimWorld.Page_ConfigureIdeo), delegate (Verse.Window w)
            {
                return new RimWorldAccess.Shell.IdeoBuilderScreenScope((RimWorld.Page_ConfigureIdeo)w);
            });

            // Four DLC dialogs the generic reader alone used to serve; none has a subclass.
            ScopeForWindow.Register(typeof(Verse.Dialog_RechargeSettings), delegate (Verse.Window w)
            {
                return new RechargeSettingsScope((Verse.Dialog_RechargeSettings)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_RewardPrefsConfig), delegate (Verse.Window w)
            {
                return new RewardPrefsScope((RimWorld.Dialog_RewardPrefsConfig)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_StyleSelection), delegate (Verse.Window w)
            {
                return new StyleSelectionScope((RimWorld.Dialog_StyleSelection)w);
            });
            ScopeForWindow.Register(typeof(RimWorld.Dialog_IdeosDuringLanding), delegate (Verse.Window w)
            {
                return new IdeosDuringLandingScope((RimWorld.Dialog_IdeosDuringLanding)w);
            });
            // In-game F12 Ideology tab.
            ScopeForWindow.Register(typeof(RimWorld.MainTabWindow_Ideos), delegate (Verse.Window w)
            {
                return new IdeologyViewerScreenScope((RimWorld.MainTabWindow_Ideos)w);
            });
            // The bill details editor. Deliberately yields NO scope: BillConfigScope owns this
            // dialog's keyboard for its whole life, and returning null is what keeps the generic
            // reader off it — an UNREGISTERED absorbing window would attach one. The factory's
            // real job is the mouse path, where BillConfigState adopts a dialog the player opened
            // through vanilla's Details button. Hierarchy, so modded subclasses are covered.
            ScopeForWindow.RegisterHierarchy(typeof(RimWorld.Dialog_BillConfig), delegate (Verse.Window w)
            {
                BillConfigState.AdoptOpenDialog(w);
                return null;
            });
            // Dev-mode debug dialog, opened from the F12 extras "Development" entry.
            ScopeForWindow.Register(typeof(LudeonTK.Dialog_Debug), delegate (Verse.Window w)
            {
                return new DevDebugScope((LudeonTK.Dialog_Debug)w);
            });
            // Runtime debug option picker, opened by many dev actions after the debug menu
            // closes. Hierarchy, so mod subclasses of the lister are covered.
            ScopeForWindow.RegisterHierarchy(typeof(LudeonTK.Dialog_DebugOptionListLister), delegate (Verse.Window w)
            {
                return new DevOptionListScope((LudeonTK.Dialog_DebugOptionListLister)w);
            });
            // Dev-mode data table, the sortable grid every "Debug Output" dump opens.
            ScopeForWindow.Register(typeof(LudeonTK.Window_DebugTable), delegate (Verse.Window w)
            {
                return new DevTableScope((LudeonTK.Window_DebugTable)w);
            });
            // Dev-mode debug LOG window. Attaches on EVERY open, auto-opens included: the scope
            // announces itself on focus and Escape dismisses it, so a window taking the arrows
            // over the map stays survivable.
            ScopeForWindow.Register(typeof(LudeonTK.EditWindow_Log), delegate (Verse.Window w)
            {
                return new DevLogScope((LudeonTK.EditWindow_Log)w);
            });
            // Dev-mode tweak-values window, a NON-modal EditWindow: the factory yields a scope
            // ONLY while DevTweakValuesScope is armed by the deliberate F12 > Development opener,
            // so opening it any other way can never mask the surface beneath.
            ScopeForWindow.Register(typeof(LudeonTK.EditWindow_TweakValues), delegate (Verse.Window w)
            {
                return DevTweakValuesScope.Arming ? new DevTweakValuesScope((LudeonTK.EditWindow_TweakValues)w) : null;
            });
            // Dev-mode debug inspector, another NON-modal EditWindow on the same arming rule as
            // the tweak-values window.
            ScopeForWindow.Register(typeof(LudeonTK.EditWindow_DebugInspector), delegate (Verse.Window w)
            {
                return DevInspectorScope.Arming ? new DevInspectorScope((LudeonTK.EditWindow_DebugInspector)w) : null;
            });
            // Dev-mode def editor. NO arming flag, unlike its EditWindow siblings above: this
            // window never auto-opens — only the "Edit effecter..." / "Edit Animation..." debug
            // actions reach it — so every open is invited. EditWindow_DefEditor is internal to
            // Assembly-CSharp, hence the reflective Type.
            if (DevDefEditorScope.WindowType != null)
            {
                ScopeForWindow.Register(DevDefEditorScope.WindowType, delegate (Verse.Window w)
                {
                    return new DevDefEditorScope(w);
                });
            }

            // Dev-mode debug tools run their own map-click session outside Verse.Targeter
            // (DebugTools.curTool); this liveness probe stands the rest of the shell down while
            // one is armed.
            ExternalMapTargeting.Register(() => LudeonTK.DebugTools.curTool != null);

        }

        // Chord words are SPOKEN mid-sentence ("Alt plus Page Down"), so they carry Keyed
        // entries. A KeyCode absent from this table keeps KeyChordFormat's humanized name, which
        // is already right for letters, digits and the function row.
        private static readonly Dictionary<KeyCode, string> SpokenKeyNames = new Dictionary<KeyCode, string>
        {
            { KeyCode.UpArrow, "RimWorldAccess.Shell.Key.UpArrow" },
            { KeyCode.DownArrow, "RimWorldAccess.Shell.Key.DownArrow" },
            { KeyCode.LeftArrow, "RimWorldAccess.Shell.Key.LeftArrow" },
            { KeyCode.RightArrow, "RimWorldAccess.Shell.Key.RightArrow" },
            { KeyCode.Return, "RimWorldAccess.Shell.Key.Return" },
            { KeyCode.PageUp, "RimWorldAccess.Shell.Key.PageUp" },
            { KeyCode.PageDown, "RimWorldAccess.Shell.Key.PageDown" },
            { KeyCode.Escape, "RimWorldAccess.Shell.Key.Escape" },
            { KeyCode.Tab, "RimWorldAccess.Shell.Key.Tab" },
            { KeyCode.Space, "RimWorldAccess.Shell.Key.Space" },
            { KeyCode.Backspace, "RimWorldAccess.Shell.Key.Backspace" },
            { KeyCode.Delete, "RimWorldAccess.Shell.Key.Delete" },
            { KeyCode.Insert, "RimWorldAccess.Shell.Key.Insert" },
            { KeyCode.Home, "RimWorldAccess.Shell.Key.Home" },
            { KeyCode.End, "RimWorldAccess.Shell.Key.End" },
            { KeyCode.BackQuote, "RimWorldAccess.Shell.Key.BackQuote" },
            { KeyCode.LeftBracket, "RimWorldAccess.Shell.Key.LeftBracket" },
            { KeyCode.RightBracket, "RimWorldAccess.Shell.Key.RightBracket" },
            { KeyCode.Comma, "RimWorldAccess.Shell.Key.Comma" },
            { KeyCode.Period, "RimWorldAccess.Shell.Key.Period" },
            { KeyCode.Slash, "RimWorldAccess.Shell.Key.Slash" },
            { KeyCode.Minus, "RimWorldAccess.Shell.Key.Minus" },
            { KeyCode.Plus, "RimWorldAccess.Shell.Key.Plus" },
            { KeyCode.Equals, "RimWorldAccess.Shell.Key.Equals" },
            { KeyCode.KeypadEnter, "RimWorldAccess.Shell.Key.KeypadEnter" },
            { KeyCode.KeypadPlus, "RimWorldAccess.Shell.Key.KeypadPlus" },
            { KeyCode.KeypadMinus, "RimWorldAccess.Shell.Key.KeypadMinus" },
            { KeyCode.KeypadMultiply, "RimWorldAccess.Shell.Key.KeypadMultiply" },
            { KeyCode.KeypadDivide, "RimWorldAccess.Shell.Key.KeypadDivide" },
            { KeyCode.KeypadPeriod, "RimWorldAccess.Shell.Key.KeypadPeriod" },
        };

        // KeyboardHelper alone decides WHICH word the platform uses for Ctrl (macOS substitutes
        // Option); this table only translates the word it picked.
        private static readonly Dictionary<string, string> SpokenModifierNames = new Dictionary<string, string>
        {
            { "Ctrl", "RimWorldAccess.Shell.Key.Ctrl" },
            { "Option", "RimWorldAccess.Shell.Key.Option" },
        };

        private static void InstallChordDisplayHooks()
        {
            KeyChordFormat.CtrlLabel = () => SpokenModifierName(KeyboardHelper.CtrlLabel);
            KeyChordFormat.ShiftLabel = () => "RimWorldAccess.Shell.Key.Shift".Translate().Resolve();
            // The Alt key IS Option on a Mac keyboard, so every Alt chord reads as the key the
            // player actually presses.
            KeyChordFormat.AltLabel = () => (NativeLibraryLoader.IsMacOS
                ? "RimWorldAccess.Shell.Key.Option"
                : "RimWorldAccess.Shell.Key.Alt").Translate().Resolve();
            KeyChordFormat.KeyLabelOverride = SpokenKeyName;
        }

        private static string SpokenModifierName(string platformWord)
        {
            string translationKey;
            if (SpokenModifierNames.TryGetValue(platformWord, out translationKey))
                return translationKey.Translate().Resolve();
            return platformWord;
        }

        private static string SpokenKeyName(KeyCode key)
        {
            string translationKey;
            if (SpokenKeyNames.TryGetValue(key, out translationKey))
                return translationKey.Translate().Resolve();
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                return "RimWorldAccess.Shell.Key.NumpadDigit".Translate((int)(key - KeyCode.Keypad0)).Resolve();
            return null;
        }
    }
}
