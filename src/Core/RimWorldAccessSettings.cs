using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Mod settings that persist between sessions.
    /// </summary>
    public class RimWorldAccessSettings : ModSettings
    {
        /// <summary>Speak terrain names during map navigation; the terrain sound effect is independent of this.</summary>
        public bool AnnounceTerrain = true;

        public bool WrapNavigation = false;

        /// <summary>Include position info like "3 of 7" in announcements.</summary>
        public bool AnnouncePosition = true;

        /// <summary>Speak pawn activity with the map cursor ("Devin (sleeping), 129, 114").</summary>
        public bool ShowPawnActivityOnMap = true;

        /// <summary>Speak cover info for drafted and hostile pawns ("behind sandbag (good cover)").</summary>
        public bool ShowCoverInfo = true;

        /// <summary>Include treeview heading level changes ("level 2").</summary>
        public bool AnnounceLevels = true;

        /// <summary>Submenu-style treeviews: an expanded parent is hidden and only its children are listed.</summary>
        public bool SubmenuTreeNavigation = false;

        /// <summary>
        /// Which work menu view F1 opens. Ctrl+Tab (Option+Tab on macOS) switches view and writes
        /// this setting, so the chosen view is remembered.
        /// </summary>
        public WorkMenuView DefaultWorkMenuView = WorkMenuView.Focused;

        /// <summary>
        /// Which view the trade dialog opens in. Ctrl+Tab (Option+Tab on macOS) switches view on
        /// the open dialog and writes this setting, so the chosen view is remembered.
        /// </summary>
        public TradeView DefaultTradeView = TradeView.Classic;

        /// <summary>One view for the caravan formation, split and transport pod screens; Ctrl+Tab writes it.</summary>
        public TransferView DefaultTransferView = TransferView.Classic;

        /// <summary>Announce a table region's shape on entry ("table, 5 columns, 8 rows").</summary>
        public bool AnnounceTableDimensions = true;

        /// <summary>Announce how many tabs a screen has on entry ("5 tabs").</summary>
        public bool AnnounceTabCount = true;

        /// <summary>Include "row 3 of 8" / "column 2 of 5" fragments in table navigation.</summary>
        public bool AnnounceRowColumnPosition = true;

        /// <summary>Announce the game forcing Normal speed for a threat, and the resume that follows.</summary>
        public bool AnnounceForcedSlowdowns = false;

        /// <summary>
        /// How many times the Learning Helper hint has ridden a new-lesson announcement; it stops
        /// after 3. Not surfaced in the settings UI.
        /// </summary>
        public int LearningHintShownCount = 0;

        /// <summary>
        /// Concept defNames whose knowledge has been reset once so our re-authored documentation gets
        /// taught. A concept the player completed long ago keeps its "learned" flag, which would
        /// suppress our overridden version forever; DocsTeacher clears it on first contextual teach and
        /// records it here so the reset happens exactly once per player.
        /// </summary>
        public List<string> RetaughtOverriddenConcepts = new List<string>();

        /// <summary>
        /// Versions of the "What's New" announcements the player has read. Anything in
        /// <see cref="WhatsNewCatalog"/> not listed here counts as unread, which drives both the
        /// on-update popup and "jump to next unread". Not surfaced in the settings UI.
        /// </summary>
        public List<string> ReadAnnouncementVersions = new List<string>();

        /// <summary>
        /// Open "What's New" automatically on the main menu after an update. When false the player
        /// hears a brief spoken notice instead and can still open it from the menu; the re-enable path
        /// lives in the accessible Options menu.
        /// </summary>
        public bool ShowWhatsNewOnUpdate = true;

        /// <summary>
        /// User key rebinds for the shell action registry, one "actionId=Chord;Chord" line per rebound
        /// action — deltas from defaults only, so default improvements reach everyone who has not moved
        /// that action. Owned by Shell.ShellBindingPersistence / BindingOverrideSet; the rebind screen
        /// edits it.
        /// </summary>
        public List<string> ShellBindingOverrideLines = new List<string>();

        /// <summary>
        /// Per-fragment enable switches for the Configure Spoken Announcements screen, each gating one
        /// <see cref="RimWorldAccess.Shell.AnnouncementPart"/> in the shared composer. Position,
        /// tree-level and hint fragments reuse <see cref="AnnouncePosition"/>,
        /// <see cref="AnnounceLevels"/> and <see cref="AnnounceInteractionHints"/> rather than
        /// duplicating them, so the settings panel and the screen can never disagree. There is no
        /// AnnounceLabelPart: the Name part can never be silenced, so it is composed unconditionally.
        /// </summary>
        public bool AnnounceHotkeyPart = true;
        public bool AnnounceRolePart = true;
        public bool AnnounceStatePart = true;
        public bool AnnounceExtrasPart = true;

        /// <summary>Include beginner interaction hints ("Press Enter to select") in announcements.</summary>
        public bool AnnounceInteractionHints = true;

        /// <summary>
        /// Announce a captured widget when the mouse pointer rests on it, without moving the keyboard
        /// cursor. Off by default: coverage is partial (the map, the command bar and pawn-table body
        /// cells are not captured widgets).
        /// </summary>
        public bool HoverSpeech = false;

        /// <summary>
        /// Warp the OS mouse pointer onto the keyboard cursor's tile on every cursor move, so mouse
        /// exploration always starts where the keyboard is. The warp stands down while the hand owns
        /// the pointer (a held button, a drag, a shape being stretched). Toggled in play with Alt+Shift+K.
        /// </summary>
        public bool PointerFollowsKeyboard = true;

        /// <summary>
        /// Player-chosen order of announcement fragments (<see cref="RimWorldAccess.Shell.AnnouncementPart"/>
        /// enum names, one per entry). Null or empty means
        /// RimWorldAccess.Shell.AnnouncementFormat.DefaultOrder.
        /// </summary>
        public List<string> AnnouncementPartOrder = new List<string>();

        /// <summary>Master toggle for the Narrative Feed's auto-announcer of pawn dialogue lines.</summary>
        public bool AnnouncePawnDialogue = true;

        /// <summary>
        /// Announce vanilla interaction bubbles. Separate from <see cref="AnnouncePawnDialogue"/> so a
        /// chatty colony can keep story lines but drop chitchat.
        /// </summary>
        public bool AnnounceVanillaInteractionBubbles = true;

        /// <summary>
        /// Announce a line even while RimTalk's TTS addon is voicing it. When false the feed backs off
        /// and lets the pawn's TTS voice speak it, while the Dialogue Log still records it. Inert when
        /// the TTS addon is not installed.
        /// </summary>
        public bool TtsAnnounceAnyway = false;

        /// <summary>Saved-recording dialog on flight recorder stop; off speaks the path instead.</summary>
        public bool ShowRecordingSavedDialog = true;

        /// <summary>Master switch for the combat autopilot: gizmos, think-tree brain, autocast menu entries.
        /// The hunting rules live on per-pawn hunting policies, not here.</summary>
        public bool EnableCombatAutopilot = true;

        /// <summary>Undrafting clears the search-and-destroy and hunt-animals checkboxes, so a redrafted pawn holds position until told otherwise.</summary>
        public bool UndraftClearsStandingOrders = true;

        /// <summary>Speak the followed pawn's new jobs and the named rooms or zones it enters.</summary>
        public bool AnnounceSelectedPawnActivity = false;

        /// <summary>Scanner auto-jump (map and world scanners alike); the in-play toggles write it, so it survives restarts.</summary>
        public bool ScannerAutoJump = false;

        /// <summary>A new game starts paused, silently, so the player can get their bearings first.</summary>
        public bool PauseOnGameStart = true;

        /// <summary>Choosing the research screen from a research-finished dialog leaves the game paused.</summary>
        public bool StayPausedAfterResearch = true;

        /// <summary>Closing the trade screen leaves the game paused until the player resumes.</summary>
        public bool StayPausedAfterTrade = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref WrapNavigation, "WrapNavigation", false);
            Scribe_Values.Look(ref AnnouncePosition, "AnnouncePosition", true);
            Scribe_Values.Look(ref ShowPawnActivityOnMap, "ShowPawnActivityOnMap", true);
            Scribe_Values.Look(ref ShowCoverInfo, "ShowCoverInfo", true);
            Scribe_Values.Look(ref AnnounceLevels, "AnnounceLevels", true);
            Scribe_Values.Look(ref SubmenuTreeNavigation, "SubmenuTreeNavigation", false);
            Scribe_Values.Look(ref AnnounceTerrain, "AnnounceTerrain", true);
            Scribe_Values.Look(ref DefaultWorkMenuView, "DefaultWorkMenuView", WorkMenuView.Focused);
            Scribe_Values.Look(ref DefaultTradeView, "DefaultTradeView", TradeView.Classic);
            Scribe_Values.Look(ref DefaultTransferView, "DefaultTransferView", TransferView.Classic);
            Scribe_Values.Look(ref AnnounceTableDimensions, "AnnounceTableDimensions", true);
            Scribe_Values.Look(ref AnnounceTabCount, "AnnounceTabCount", true);
            Scribe_Values.Look(ref AnnounceRowColumnPosition, "AnnounceRowColumnPosition", true);
            Scribe_Values.Look(ref AnnounceForcedSlowdowns, "AnnounceForcedSlowdowns", false);
            Scribe_Values.Look(ref LearningHintShownCount, "LearningHintShownCount", 0);
            Scribe_Values.Look(ref ShowWhatsNewOnUpdate, "ShowWhatsNewOnUpdate", true);
            Scribe_Collections.Look(ref RetaughtOverriddenConcepts, "RetaughtOverriddenConcepts", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && RetaughtOverriddenConcepts == null)
                RetaughtOverriddenConcepts = new List<string>();
            Scribe_Collections.Look(ref ReadAnnouncementVersions, "ReadAnnouncementVersions", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && ReadAnnouncementVersions == null)
                ReadAnnouncementVersions = new List<string>();
            Scribe_Collections.Look(ref ShellBindingOverrideLines, "ShellBindingOverrideLines", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && ShellBindingOverrideLines == null)
                ShellBindingOverrideLines = new List<string>();
            Scribe_Values.Look(ref AnnounceHotkeyPart, "AnnounceHotkeyPart", true);
            // The combined type+state toggle was split into separate parts; seed both from the
            // legacy value so a save that had silenced them stays silenced.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                bool legacyRoleState = true;
                Scribe_Values.Look(ref legacyRoleState, "AnnounceRoleStatePart", true);
                AnnounceRolePart = legacyRoleState;
                AnnounceStatePart = legacyRoleState;
            }
            Scribe_Values.Look(ref AnnounceRolePart, "AnnounceRolePart", AnnounceRolePart);
            Scribe_Values.Look(ref AnnounceStatePart, "AnnounceStatePart", AnnounceStatePart);
            Scribe_Values.Look(ref AnnounceExtrasPart, "AnnounceExtrasPart", true);
            Scribe_Values.Look(ref AnnounceInteractionHints, "AnnounceInteractionHints", true);
            Scribe_Values.Look(ref HoverSpeech, "HoverSpeech", false);
            Scribe_Values.Look(ref PointerFollowsKeyboard, "PointerFollowsKeyboard", true);
            Scribe_Collections.Look(ref AnnouncementPartOrder, "AnnouncementPartOrder", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && AnnouncementPartOrder == null)
                AnnouncementPartOrder = new List<string>();
            Scribe_Values.Look(ref AnnouncePawnDialogue, "AnnouncePawnDialogue", true);
            Scribe_Values.Look(ref AnnounceVanillaInteractionBubbles, "AnnounceVanillaInteractionBubbles", true);
            Scribe_Values.Look(ref TtsAnnounceAnyway, "TtsAnnounceAnyway", false);
            Scribe_Values.Look(ref ShowRecordingSavedDialog, "ShowRecordingSavedDialog", true);
            Scribe_Values.Look(ref EnableCombatAutopilot, "EnableCombatAutopilot", true);
            Scribe_Values.Look(ref UndraftClearsStandingOrders, "UndraftClearsStandingOrders", true);
            Scribe_Values.Look(ref AnnounceSelectedPawnActivity, "AnnounceSelectedPawnActivity", false);
            Scribe_Values.Look(ref ScannerAutoJump, "ScannerAutoJump", false);
            Scribe_Values.Look(ref PauseOnGameStart, "PauseOnGameStart", true);
            Scribe_Values.Look(ref StayPausedAfterResearch, "StayPausedAfterResearch", true);
            Scribe_Values.Look(ref StayPausedAfterTrade, "StayPausedAfterTrade", true);
            base.ExposeData();
        }
    }

    /// <summary>
    /// Which work menu layout F1 opens: Focused is the priority-grouped per-pawn view, Table is
    /// pawn rows by work-type columns as vanilla draws them.
    /// </summary>
    public enum WorkMenuView
    {
        Focused,
        Table
    }

    /// <summary>
    /// Which trade dialog layout opens: Classic is three flat lists (the trader's goods, the pending
    /// deal, your goods), Table is vanilla's one sortable list with a column cursor.
    /// </summary>
    public enum TradeView
    {
        Classic,
        Table
    }

    /// <summary>Classic is one flat list per tab, Left/Right switching tabs; Table is each tab as a sortable table.</summary>
    public enum TransferView
    {
        Classic,
        Table
    }

    /// <summary>
    /// Mod class for RimWorld Access; registers the settings and draws the vanilla settings panel.
    /// </summary>
    public class RimWorldAccessMod_Settings : Mod
    {
        public static RimWorldAccessSettings Settings { get; private set; }

        public RimWorldAccessMod_Settings(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimWorldAccessSettings>();
        }

        public override string SettingsCategory()
        {
            return "RimWorldAccess.Core.Settings.Category".Translate();
        }

        /// <summary>
        /// Normally unreachable (RwaModSettingsRedirectPatch routes every mod-settings path onto
        /// the Options pane), but any surface that still draws this shows that exact pane.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            Shell.OptionsRwaCategory.DrawSettings(listing);
            listing.End();
        }
    }
}
