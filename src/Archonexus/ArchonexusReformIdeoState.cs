using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard-accessible driver for Dialog_ConfigureIdeo (forArchonexusRestart: true) — the
    /// reform-ideoligion step of the Archonexus relocation chain. Presented as the mod's standard
    /// two-tab ideoligion screen (parity with <see cref="IdeoBuilderHubState"/>):
    ///
    ///  - List tab: "Create new" / "Create new fluid" / "Load saved" actions followed by every
    ///    ideoligion. Up/Down/Home/End + typeahead. Enter makes the focused ideoligion the colony's
    ///    primary (or runs the focused action); the current primary is marked.
    ///  - Detail tab (Tab key): the shared read-only <see cref="IdeologyTreeNavigation"/> viewer —
    ///    memes with descriptions, precepts, deities, the full description, and Alt+I info cards.
    ///
    /// Alt+S confirms (the dialog's "Next"): commit the primary, apply any pawn conversions, close,
    /// and run the questline's nextAction. Create/Load delegate to the game's own (already
    /// accessible) Dialog_ChooseMemes / Dialog_IdeoList_Load so editing/loading is faithful.
    /// </summary>
    public static class ArchonexusReformIdeoState
    {
        public static bool IsActive { get; private set; }

        private enum Tab { List, Detail }
        private enum RowKind { CreateNew, CreateFluid, LoadSaved, Ideo, AssignColonists, Confirm }

        private struct Row
        {
            public RowKind Kind;
            public Ideo Ideo;
            public string Label;
        }

        private static Dialog_ConfigureIdeo dialog;
        private static readonly List<Row> rows = new List<Row>();
        private static int selectedIndex;
        private static Tab currentTab = Tab.List;
        // Within the detail tab, the editable custom ideoligion uses the full section editor; every
        // other ideoligion uses the read-only viewer. This tracks which one the detail tab is showing.
        private static bool detailIsEditor;
        private static readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();
        private static readonly IdeologyTreeNavigation viewer = new IdeologyTreeNavigation();

        public static bool HasActiveSearch
        {
            get
            {
                if (currentTab == Tab.Detail)
                    return detailIsEditor ? IdeoSectionEditorState.HasActiveSearch : viewer.HasActiveSearch;
                return typeahead.HasActiveSearch;
            }
        }

        #region Reflection cache

        private static readonly Type DialogType = typeof(Dialog_ConfigureIdeo);
        private static readonly FieldInfo NextActionField = AccessTools.Field(DialogType, "nextAction");
        private static readonly FieldInfo PawnsField = AccessTools.Field(DialogType, "pawns");
        private static readonly FieldInfo PawnConvertToIdeoField = AccessTools.Field(DialogType, "pawnConvertToIdeo");
        private static readonly FieldInfo InitialPrimaryIdeoField = AccessTools.Field(DialogType, "initialPrimaryIdeo");
        private static readonly FieldInfo CustomOrLoadedIdeoField = AccessTools.Field(DialogType, "customOrLoadedIdeo");
        private static readonly PropertyInfo CurrentPrimaryIdeoProp = AccessTools.Property(DialogType, "CurrentPrimaryIdeo");
        private static readonly MethodInfo CheckRemoveAndMakePrimaryMethod = AccessTools.Method(DialogType, "CheckRemoveNewIdeoAndMakePrimary");
        private static readonly MethodInfo CreateNewIdeoMethod = AccessTools.Method(DialogType, "CreateNewIdeo");

        #endregion

        #region Lifecycle

        public static void EnsureOpen(Dialog_ConfigureIdeo d)
        {
            // See ArchonexusColonyState.EnsureOpen / IdeoLoadState.EnsureOpen for the
            // reference-equality (not IsActive) guard rationale.
            if (ReferenceEquals(dialog, d))
                return;
            dialog = d;
            IsActive = true;
            currentTab = Tab.List;
            detailIsEditor = false;
            typeahead.ClearSearch();
            viewer.Reset();
            RebuildRows();

            // Default the cursor onto the current primary so the first announcement is meaningful.
            var primary = (Ideo)CurrentPrimaryIdeoProp.GetValue(dialog);
            int idx = rows.FindIndex(r => r.Kind == RowKind.Ideo && r.Ideo == primary);
            selectedIndex = idx >= 0 ? idx : 0;
            SyncSelected();
            AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            rows.Clear();
            typeahead.ClearSearch();
            viewer.Reset();
            if (IdeoSectionEditorState.IsActive)
                IdeoSectionEditorState.Close();
            detailIsEditor = false;
            currentTab = Tab.List;
            // dialog reference intentionally retained — see EnsureOpen.
        }

        /// <summary>
        /// Called by <see cref="ArchonexusReformIdeoPatch"/> the frame a child window (meme picker,
        /// load picker, per-pawn conversion dialog, info card) closes and focus returns to us. A new
        /// ideoligion may have been created/loaded and made primary, or conversions changed, so we
        /// rebuild and re-announce where the cursor is — context-aware so the announcement matches
        /// the tab the player is actually in.
        /// </summary>
        public static void OnReturnedFromChild()
        {
            if (!IsActive || dialog == null) return;

            // If the player was reading details (Tab) when they opened an info card, keep them there.
            if (currentTab == Tab.Detail)
            {
                if (detailIsEditor) IdeoSectionEditorState.Refresh();
                else viewer.AnnounceCurrentItem();
                return;
            }

            RebuildRows();
            // A freshly created/loaded ideoligion becomes the primary — land the cursor on it so the
            // return announcement reflects the change the sub-screen made.
            var primary = (Ideo)CurrentPrimaryIdeoProp.GetValue(dialog);
            int idx = rows.FindIndex(r => r.Kind == RowKind.Ideo && r.Ideo == primary);
            if (idx >= 0)
            {
                selectedIndex = idx;
                SyncSelected();
            }

            // When we land on the now-editable ideoligion (just created or loaded), point the player
            // at Tab so they discover they can open its full editor — otherwise the editor is hidden.
            string text = BuildCurrentText(includePosition: true);
            if (IsEditable(Current.Ideo))
                text += ". " + (string)"RimWorldAccess.Archonexus.Reform.PressTabToEdit".Translate();
            if (!string.IsNullOrEmpty(text))
                TolkHelper.SpeakData(text, SpeechPriority.High);
        }

        /// <summary>Rebuilds the row list (actions + every ideoligion), preserving the focused ideo.</summary>
        private static void RebuildRows()
        {
            Ideo keep = (selectedIndex >= 0 && selectedIndex < rows.Count && rows[selectedIndex].Kind == RowKind.Ideo)
                ? rows[selectedIndex].Ideo
                : null;

            rows.Clear();
            rows.Add(new Row { Kind = RowKind.CreateNew, Label = "CreateNew".Translate() });
            rows.Add(new Row { Kind = RowKind.CreateFluid, Label = "CreateFluid".Translate() });
            rows.Add(new Row { Kind = RowKind.LoadSaved, Label = "LoadExistingIdeoligion".Translate() });
            if (Find.IdeoManager != null)
            {
                foreach (Ideo ideo in Find.IdeoManager.IdeosInViewOrder)
                    rows.Add(new Row { Kind = RowKind.Ideo, Ideo = ideo, Label = ideo.name });
            }
            // "Assign colonists" only when some colonist isn't already on the (pending) primary —
            // mirrors the vanilla button's visibility condition.
            if (AnyColonistToConvert())
                rows.Add(new Row { Kind = RowKind.AssignColonists, Label = "AssignColonists".Translate() });
            rows.Add(new Row { Kind = RowKind.Confirm, Label = "Next".Translate() });

            if (keep != null)
            {
                int i = rows.FindIndex(r => r.Kind == RowKind.Ideo && r.Ideo == keep);
                if (i >= 0) selectedIndex = i;
            }
            if (selectedIndex >= rows.Count)
                selectedIndex = Math.Max(0, rows.Count - 1);
        }

        /// <summary>Pick up ideoligions created/loaded via the game's own sub-dialogs.</summary>
        private static void RefreshRowsIfChanged()
        {
            int managerCount = Find.IdeoManager == null ? 0 : Find.IdeoManager.IdeosInViewOrder.Count();
            int rowIdeoCount = rows.Count(r => r.Kind == RowKind.Ideo);
            if (managerCount != rowIdeoCount)
                RebuildRows();
        }

        private static bool AnyColonistToConvert()
        {
            // Same condition vanilla uses to show its "Assign colonists" button (source.Any()):
            // any colonist NOT already on the faction's current primary ideoligion.
            return PawnsField.GetValue(dialog) is List<Pawn> pawns
                && pawns.Any(p => p.IsColonist && p.Ideo != Faction.OfPlayer.ideos.PrimaryIdeo);
        }

        private static Row Current => (selectedIndex >= 0 && selectedIndex < rows.Count) ? rows[selectedIndex] : default;

        /// <summary>
        /// The one ideoligion vanilla renders editable in this screen: the custom ideo the player
        /// created or loaded (<c>Dialog_ConfigureIdeo.customOrLoadedIdeo</c>). Null until they do so.
        /// </summary>
        private static Ideo CustomOrLoadedIdeo => dialog == null ? null : (Ideo)CustomOrLoadedIdeoField.GetValue(dialog);

        private static bool IsEditable(Ideo i) => i != null && i == CustomOrLoadedIdeo;

        /// <summary>Keep the game's own selection in sync so its details/state track our cursor.</summary>
        private static void SyncSelected()
        {
            if (Current.Kind == RowKind.Ideo && Current.Ideo != null)
                IdeoUIUtility.SetSelected(Current.Ideo);
        }

        #endregion

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;
            bool shift = ev.shift;

            // Alt+S confirms ("Next") from either tab — matches the relocation chain's send convention.
            if (key == KeyCode.S && alt && !ctrl)
            {
                ConfirmAndProceed();
                return true;
            }

            if (currentTab == Tab.Detail)
                return HandleDetailInput(ev, key, alt, ctrl, shift);

            return HandleListInput(ev, key, alt, ctrl, shift);
        }

        private static bool HandleListInput(Event ev, KeyCode key, bool alt, bool ctrl, bool shift)
        {
            RefreshRowsIfChanged();

            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (typeahead.HasActiveSearch)
                {
                    typeahead.ClearSearchAndAnnounce();
                    AnnounceCurrent(includePosition: false);
                    return true;
                }
                // Dialog has openMenuOnCancel:true and no real Cancel — the player cannot
                // back out of this step of the quest. Make that audible.
                TolkHelper.Speak("RimWorldAccess.Archonexus.Reform.CannotCancel".Loc(), SpeechPriority.High);
                return true;
            }

            if (rows.Count == 0) return true;

            if (key == KeyCode.UpArrow) { Move(-1); return true; }
            if (key == KeyCode.DownArrow) { Move(1); return true; }
            if (key == KeyCode.Home) { typeahead.ClearSearch(); selectedIndex = 0; SyncSelected(); AnnounceCurrent(); return true; }
            if (key == KeyCode.End) { typeahead.ClearSearch(); selectedIndex = rows.Count - 1; SyncSelected(); AnnounceCurrent(); return true; }

            // Tab opens the detail viewer for the focused ideoligion.
            if (key == KeyCode.Tab && !alt && !ctrl)
            {
                OpenDetailForSelection();
                return true;
            }

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !alt && !ctrl)
            {
                ActivateCurrent();
                return true;
            }

            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch && typeahead.ProcessBackspace(Labels(), out int ni))
                {
                    if (ni >= 0) { selectedIndex = ni; SyncSelected(); }
                    AnnounceCurrent(includePosition: false);
                }
                return true;
            }

            char c = ev.character;
            if (!alt && !ctrl && c != '\0' && char.IsLetterOrDigit(c))
            {
                if (typeahead.ProcessCharacterInput(c, Labels(), out int ni))
                {
                    selectedIndex = ni;
                    SyncSelected();
                    AnnounceCurrent(includePosition: false);
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    typeahead.SpeakNoMatches();
                }
                return true;
            }

            return true; // swallow other keys while we own the dialog
        }

        private static bool HandleDetailInput(Event ev, KeyCode key, bool alt, bool ctrl, bool shift)
        {
            // Tab / Shift+Tab leaves the detail tab back to the list, in either detail mode.
            if (key == KeyCode.Tab && !alt && !ctrl)
            {
                SwitchToList();
                return true;
            }

            // Editable custom ideoligion: the full section editor owns the detail tab. It clears its
            // own typeahead on Escape and returns false only on Escape-with-no-search — our cue to
            // leave the editor back to the list.
            if (detailIsEditor)
            {
                if (IdeoSectionEditorState.HandleInput(ev))
                    return true;
                if (key == KeyCode.Escape && !alt && !ctrl)
                    SwitchToList();
                return true;
            }

            // Read-only viewer: typeahead within the viewer's tree.
            char c = ev.character;
            if (!alt && !ctrl && c != '\0' && char.IsLetterOrDigit(c))
            {
                viewer.HandleTypeaheadCharacter(c);
                return true;
            }

            // Escape: let the viewer clear its own search first; otherwise back to the list.
            if (key == KeyCode.Escape && !alt && !ctrl && !viewer.HasActiveSearch)
            {
                SwitchToList();
                return true;
            }

            // Everything else (arrows, expand/collapse, Alt+I info card, Escape-clears-search)
            // goes to the shared read-only viewer.
            if (viewer.HandleInput(ev))
                return true;

            return true; // keep ownership of the dialog's keys
        }

        #endregion

        #region Navigation

        private static void Move(int delta)
        {
            if (rows.Count == 0) return;
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
            {
                int mi = delta > 0 ? typeahead.GetNextMatch(selectedIndex) : typeahead.GetPreviousMatch(selectedIndex);
                if (mi >= 0) selectedIndex = mi;
            }
            else
            {
                selectedIndex = delta > 0
                    ? MenuHelper.SelectNext(selectedIndex, rows.Count)
                    : MenuHelper.SelectPrevious(selectedIndex, rows.Count);
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            SyncSelected();
            AnnounceCurrent(includePosition: false);
        }

        private static void OpenDetailForSelection()
        {
            if (Current.Kind != RowKind.Ideo || Current.Ideo == null)
            {
                TolkHelper.Speak("RimWorldAccess.Archonexus.Reform.NoIdeoDetailsHere".Loc());
                return;
            }
            typeahead.ClearSearch();
            currentTab = Tab.Detail;
            Ideo target = Current.Ideo;
            // Vanilla makes exactly one ideoligion editable in this screen — the one the player just
            // created or loaded. Present its full section editor; every other ideoligion stays
            // read-only (the shared viewer), matching what a sighted player can interact with here.
            if (IsEditable(target))
            {
                detailIsEditor = true;
                IdeoSectionEditorState.Open(target); // builds the section list and announces it
            }
            else
            {
                detailIsEditor = false;
                viewer.Initialize(target); // builds the read-only tree and announces its first item
            }
        }

        private static void SwitchToList()
        {
            if (detailIsEditor)
            {
                IdeoSectionEditorState.Close();
                detailIsEditor = false;
            }
            else
            {
                viewer.Reset();
            }
            currentTab = Tab.List;
            // Editing the custom ideo may have renamed it / changed its memes — rebuild so the row
            // label reflects the change, keeping the cursor on the same ideoligion.
            RebuildRows();
            AnnounceCurrent(includePosition: true);
        }

        private static void ActivateCurrent()
        {
            switch (Current.Kind)
            {
                case RowKind.Ideo: MakeSelectedPrimary(); break;
                case RowKind.CreateNew: CreateNew(fluid: false); break;
                case RowKind.CreateFluid: CreateNew(fluid: true); break;
                case RowKind.LoadSaved: LoadSaved(); break;
                case RowKind.AssignColonists: AssignColonists(); break;
                case RowKind.Confirm: ConfirmAndProceed(); break;
            }
        }

        #endregion

        #region Actions

        private static void MakeSelectedPrimary()
        {
            if (Current.Kind != RowKind.Ideo || Current.Ideo == null) return;
            Ideo ideo = Current.Ideo;
            var current = (Ideo)CurrentPrimaryIdeoProp.GetValue(dialog);
            if (ideo == current)
            {
                TolkHelper.SpeakData("RimWorldAccess.Archonexus.Reform.AlreadyPrimary".Translate(ideo.name));
                return;
            }
            // The dialog's own helper handles primary assignment + bookkeeping for the "new" ideo
            // it tracks; reuse it so we never re-implement game state changes.
            CheckRemoveAndMakePrimaryMethod.Invoke(dialog, new object[] { ideo });
            RefreshRowsIfChanged();
            // Clear any active search on select so the next keystrokes start fresh (parity with
            // the colony-selection screen).
            typeahead.ClearSearch();
            TolkHelper.SpeakData("RimWorldAccess.Archonexus.Reform.SetAsPrimary".Translate(ideo.name), SpeechPriority.High);
        }

        private static void CreateNew(bool fluid)
        {
            // Opens the game's Dialog_ChooseMemes (Structure) — already accessible via
            // IdeoMemeSelectionState — and, on accept, makes the new ideo primary.
            CreateNewIdeoMethod.Invoke(dialog, new object[] { fluid });
        }

        private static void LoadSaved()
        {
            // Mirror the dialog's own archonexus load callback (Dialog_ConfigureIdeo line 89-92):
            // make the loaded ideo primary and track it as the custom/loaded ideo. The load picker
            // (Dialog_IdeoList_Load) is keyboard-accessible via IdeoLoadState.
            Find.WindowStack.Add(new Dialog_IdeoList_Load(delegate (Ideo loaded)
            {
                CheckRemoveAndMakePrimaryMethod.Invoke(dialog, new object[] { loaded });
                CustomOrLoadedIdeoField.SetValue(dialog, loaded);
                RebuildRows();
            }));
        }

        private static void AssignColonists()
        {
            // Open the game's per-pawn conversion dialog with the same wiring the vanilla
            // "Assign colonists" button uses (Dialog_ConfigureIdeo line 116). The dialog is
            // keyboard-accessible via ArchonexusConvertColonistsState. The setter writes into the
            // dialog's pawnConvertToIdeo map, which ConfirmAndProceed ("Next") then applies.
            if (!(PawnsField.GetValue(dialog) is List<Pawn> pawns)) return;
            if (!(PawnConvertToIdeoField.GetValue(dialog) is Dictionary<Pawn, Ideo> convert)) return;
            var primary = (Ideo)CurrentPrimaryIdeoProp.GetValue(dialog);
            var initialPrimary = (Ideo)InitialPrimaryIdeoField.GetValue(dialog);

            // Match vanilla's source filter exactly (Dialog_ConfigureIdeo line 113): colonists NOT on
            // the faction's CURRENT primary. Colonists already on your existing ideoligion auto-convert
            // to the new one and are intentionally omitted — only those needing a manual choice appear.
            // (Filtering against the pending new primary instead would list a different set than a
            // sighted player sees.)
            IEnumerable<Pawn> source = pawns.Where(p => p.IsColonist && p.Ideo != Faction.OfPlayer.ideos.PrimaryIdeo);
            if (!source.Any())
            {
                TolkHelper.Speak("RimWorldAccess.Archonexus.Reform.NoColonistsNeedConverting".Loc());
                return;
            }

            Find.WindowStack.Add(new Dialog_ChooseColonistsForIdeo(
                primary,
                source,
                (Pawn p) => p.Ideo != initialPrimary && p.Ideo != primary,
                (Pawn p) => p.Ideo,
                (Pawn p) => convert.TryGetValue(p, out Ideo i) && i != null ? i : p.Ideo,
                delegate (Pawn p, Ideo i)
                {
                    convert[p] = (p.Ideo == i) ? null : i;
                }));
        }

        private static void ConfirmAndProceed()
        {
            // Replicates the dialog's "Next" button: commit the primary, apply any pawn conversions,
            // close, then run the questline's nextAction (InitMoveColony).
            try
            {
                var primary = (Ideo)CurrentPrimaryIdeoProp.GetValue(dialog);
                if (Faction.OfPlayer.ideos.PrimaryIdeo != primary)
                    Faction.OfPlayer.ideos.SetPrimary(primary);

                if (PawnsField.GetValue(dialog) is List<Pawn> pawns
                    && PawnConvertToIdeoField.GetValue(dialog) is Dictionary<Pawn, Ideo> conversions)
                {
                    foreach (Pawn pawn in pawns)
                    {
                        if (conversions.TryGetValue(pawn, out Ideo target) && target != null)
                            pawn.ideo.SetIdeo(target);
                    }
                }

                var next = NextActionField.GetValue(dialog) as Action;
                dialog.Close(doCloseSound: false);
                next?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error confirming reform ideo: {ex}");
                TolkHelper.Speak("RimWorldAccess.Archonexus.Reform.ErrorConfirming".Loc(), SpeechPriority.High);
            }
        }

        #endregion

        #region Announcements

        private static List<string> Labels() => rows.Select(r => r.Label).ToList();

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append("ConfigureIdeoligion".Translate());
            sb.Append(". ").Append("RimWorldAccess.Archonexus.Reform.OpenInstructions".Translate());
            if (rows.Count > 0)
                sb.Append(". ").Append(BuildCurrentText(includePosition: true));
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
        }

        private static void AnnounceCurrent(bool includePosition = true)
        {
            if (rows.Count == 0) return;
            string text = BuildCurrentText(includePosition);
            if (!string.IsNullOrEmpty(text))
                TolkHelper.SpeakData(text);
        }

        private static string BuildCurrentText(bool includePosition)
        {
            Row row = Current;
            var sb = new StringBuilder();

            if (row.Kind == RowKind.Ideo && row.Ideo != null)
            {
                Ideo ideo = row.Ideo;
                var current = (Ideo)CurrentPrimaryIdeoProp.GetValue(dialog);
                sb.Append(ideo.name);
                if (ideo == current) sb.Append(". current primary");
                if (ideo.StructureMeme != null)
                    sb.Append(". ").Append(ideo.StructureMeme.LabelCap);
                var nonStructure = ideo.memes.Where(m => m != ideo.StructureMeme).ToList();
                if (nonStructure.Count > 0)
                    sb.Append(". memes: ").Append(string.Join(", ", nonStructure.Select(m => m.label)));
            }
            else
            {
                sb.Append(row.Label);
            }

            if (includePosition)
            {
                string position = MenuHelper.FormatPosition(selectedIndex, rows.Count);
                if (!string.IsNullOrEmpty(position))
                    sb.Append(". ").Append(position);
            }
            return sb.ToString();
        }

        #endregion
    }

    /// <summary>
    /// Patches for Dialog_ConfigureIdeo — routes input to ArchonexusReformIdeoState,
    /// blocks vanilla Accept/Cancel keys (Enter would Close without confirming;
    /// Escape would toggle the in-game menu under us), pins focus on open.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ConfigureIdeo), "DoWindowContents")]
    public static class ArchonexusReformIdeoPatch
    {
        // Every real window this screen can open over itself (meme picker, load picker, the per-pawn
        // conversion dialog, an info card). Each one steals IMGUI keyboard focus while it's up; when
        // it closes, focus would otherwise stay lost and the reform screen would be dead to input —
        // the meme-selection lockup. HostFocusReturn reclaims focus the frame the last tracked child
        // closes and re-announces where the cursor landed, all from our own DoWindowContents pass
        // (the only GUI context where GUI.FocusWindow actually takes). This is the single, reusable
        // fix for the recurring "returned from a sub-screen and the keyboard is frozen" problem.
        private static readonly HostFocusReturn childFocus = new HostFocusReturn(
            ArchonexusReformIdeoState.OnReturnedFromChild,
            typeof(Dialog_InfoCard),
            typeof(Dialog_ChooseMemes),
            typeof(Dialog_IdeoList_Load),
            typeof(Dialog_ChooseColonistsForIdeo));

        // The section editor (editable custom ideoligion) opens the same windowless overlay editors
        // the builder uses — precepts, typed precepts, deities, appearance. They carry no real
        // window, so HostFocusReturn can't see them; we route their keys ourselves and refresh the
        // editor when they close. Mirrors IdeoReformPatch (the in-game reform precedent).
        private static bool overlayWasOpen;

        static bool Prefix(Dialog_ConfigureIdeo __instance)
        {
            try
            {
                ArchonexusReformIdeoState.EnsureOpen(__instance);

                // Keep any ritual-sound preview (started from the section editor's ] menu) alive,
                // exactly as the worldgen hub patch does for its editor.
                IdeoEditorCommands.MaintainRitualPreview();

                // Reclaim focus + re-announce the moment any tracked child WINDOW closes. Must run
                // before the yields below so it still fires on the frame the child disappears.
                childFocus.Track(__instance);

                // Modal text input (e.g. renaming the ideo) and confirmation/message boxes own all
                // keys. Confirmations are intercepted by DialogInterceptionPatch into a windowless
                // dialog, so there is no Dialog_MessageBox window to detect — check the state flags.
                if (TextInputManager.Active != null ||
                    WindowlessDialogState.IsActive || WindowlessConfirmationState.IsActive)
                    return true;

                // A real child window (meme picker / load / conversion / info card) owns the keyboard
                // while open; childFocus tracks the same set. Swallow Tab so the reform dialog drawn
                // beneath the child can't cycle IMGUI focus to its own controls and steal it.
                if (childFocus.AnyOpen)
                {
                    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
                        Event.current.Use();
                    return true;
                }

                bool floatMenuOpen = WindowlessFloatMenuState.IsActive;

                // Windowless overlay editors opened from the section editor.
                if (IdeoBuilderOverlays.AnyActive)
                {
                    overlayWasOpen = true;
                    if (floatMenuOpen)
                    {
                        // A sub-picker float menu owns the keyboard (routed by UnifiedKeyboardPatch).
                        IdeoBuilderOverlays.NoteFloatMenuOpen();
                        return true;
                    }
                    IdeoBuilderOverlays.RefreshIfReturnedFromFloatMenu();
                    if (Event.current.type == EventType.KeyDown && IdeoBuilderOverlays.RouteKeyDown(Event.current))
                        Event.current.Use();
                    return true;
                }

                // Just returned from an overlay editor — its edits may have changed the ideo, so
                // refresh the section editor's value summaries and re-announce.
                if (overlayWasOpen)
                {
                    overlayWasOpen = false;
                    ArchonexusReformIdeoState.OnReturnedFromChild();
                }

                // A bare float menu (e.g. a style / icon / color picker) owns the keyboard.
                if (floatMenuOpen)
                {
                    IdeoBuilderOverlays.NoteFloatMenuOpen();
                    return true;
                }
                IdeoBuilderOverlays.RefreshIfReturnedFromFloatMenu();

                if (Event.current.type == EventType.KeyDown)
                {
                    if (ArchonexusReformIdeoState.HandleInput(Event.current))
                        Event.current.Use();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ArchonexusReformIdeoPatch.Prefix: {ex}");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
    public static class ArchonexusReformIdeoPatch_OnAccept
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            // closeOnAccept defaults true; base would Close+Use silently. We handle
            // Enter ourselves (make primary / activate row); Alt+S is the explicit proceed.
            if (__instance is Dialog_ConfigureIdeo && ArchonexusReformIdeoState.IsActive)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
    public static class ArchonexusReformIdeoPatch_OnCancel
    {
        [HarmonyPrefix]
        static bool Prefix(Window __instance)
        {
            // openMenuOnCancel:true would toggle the in-game menu under our dialog
            // every time the player presses Escape. Block it and announce instead.
            if (__instance is Dialog_ConfigureIdeo && ArchonexusReformIdeoState.IsActive)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class ArchonexusReformIdeoPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ConfigureIdeo)
                Find.WindowStack.Notify_ManuallySetFocus(__instance);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class ArchonexusReformIdeoPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ConfigureIdeo)
                ArchonexusReformIdeoState.Close();
        }
    }
}
