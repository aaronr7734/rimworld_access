using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Page_SelectStartingSite (the world-gen planet screen) as a
    /// <see cref="ScreenScope"/>: a content region holding the shared
    /// <see cref="WorldMapElement"/>, a read-only Tile info region, and the
    /// Buttons region (Back, Select random site, Factions, Next).
    /// Window-attached via ShellBootstrap.
    /// NON-modal deliberately, so the <see cref="IsModal"/> override is
    /// load-bearing: the page is non-absorbing over a live planet, and unclaimed
    /// keys must keep falling through to vanilla (world camera zoom reads
    /// KeyDownEvent at the end of the frame).
    /// It carries no ForeignWindowAbove fold: every real window that stacks here
    /// brings a modal scope that masks these claims on the walk, while the
    /// WorldInspectPane — open whenever a tile is selected, i.e. almost always —
    /// takes no keyboard input, so a foreign-window guard would kill every claim
    /// on the screen. <see cref="LearningHelperScope"/> masks this scope the same
    /// structural way, so no claim carries a LearningHelperState term.
    /// Enter never reaches the Window/Page accept routers:
    /// Page_SelectStartingSite.OnAcceptKeyPressed overrides Page's declaration
    /// without calling base and its prefix returns false unconditionally, so
    /// accept handling lives entirely on the mod side (<see cref="ActivateContentItem"/>
    /// and the Next row) and <see cref="OwnsAccept"/> is left at ScreenScope's
    /// <c>true</c> because nothing consults it here.
    /// Escape=Back is a main-pass reader (DoCustomBottomButtons via ExtraOnGUI),
    /// so the dispatcher's own consume suppresses it — KeyBindingDef.KeyDownEvent
    /// returns false for a Use()d event within the same pass.
    /// </summary>
    public sealed class StartingSiteScreenScope : ScreenScope
    {
        private readonly Page_SelectStartingSite page;

        /// <summary>
        /// The shared world-map element, hosted as this screen's one content
        /// item; each of its claims carries this screen's own gate.
        /// </summary>
        private readonly WorldMapElement map;

        private readonly ScreenAction[] declaredActions;

        // PopulateMenuItems runs Find.World.CoastDirectionAt per candidate tile,
        // so RefreshContent rebuilds only on a genuine tile change.
        private PlanetTile tileInfoBuiltFor = PlanetTile.Invalid;

        // Vehicle B for Back: CanDoBack must be honored before DoBack runs,
        // matching the vanilla button (Page.cs:58-61) — e.g. tutorial mode's
        // GotoPrevPage veto.
        private static readonly System.Reflection.MethodInfo CanDoBackMethod =
            HarmonyLib.AccessTools.Method(typeof(Page), "CanDoBack");
        private static readonly System.Reflection.MethodInfo DoBackMethod =
            HarmonyLib.AccessTools.Method(typeof(Page), "DoBack");

        // Page.BottomButSize is protected static — reflection is the only way in
        // (Page.cs:21).
        private static readonly System.Reflection.FieldInfo BottomButSizeField =
            HarmonyLib.AccessTools.Field(typeof(Page), "BottomButSize");

        public StartingSiteScreenScope(Page_SelectStartingSite page)
        {
            this.page = page;

            // Vanilla's own order (Page_SelectStartingSite.DoCustomBottomButtons);
            // each row shares its vehicle with the standalone-key twin.
            declaredActions = new ScreenAction[]
            {
                new ScreenAction("Back".Translate(), DoBackIfAllowed, SharedMenuGrammar.Cancel),
                new ScreenAction("SelectRandomSite".Translate(), StartingSiteContext.SelectRandomTile, "startingSite.randomTile"),
                new ScreenAction("WorldFactionsTab".Translate(), OpenWorldFactions, "startingSite.factionRelations"),
                new ScreenAction("Next".Translate(), ConfirmSiteSelectionAction, "startingSite.next"),
            };

            map = new WorldMapElement(new WorldMapElementProfile
            {
                CursorLive = MapCursorClaimable,
                BiomeJumpLive = SiteKeysLive,
                TileInfoLive = ScannerKeysLive,
                ScannerLive = ScannerKeysLive,
                ReadTileLive = SiteKeysLive,
                // No caravans exist during world generation, but Alt+End is still
                // consumed rather than falling through.
                JumpToNearestCaravanIsSilentNoOp = true,
            });
            map.RegisterClaims(this);

            Claim("startingSite.randomTile", delegate { StartingSiteContext.SelectRandomTile(); }, when: SiteKeysNoSearchLive);
            Claim("startingSite.factionRelations", delegate { OpenWorldFactions(); }, when: SiteKeysNoSearchLive);
            Claim("startingSite.next", delegate { ConfirmSiteSelectionAction(); }, when: SiteKeysNoSearchLive);

            // Z / Ctrl+Z scanner-search openers share the Playing openers' action
            // ids (MapScope.Scanner.Game.cs) so a rebind applies everywhere.
            Claim("map.scanner.search", delegate
            {
                ScannerSearchState.Activate(true);
            }, when: WorldGenSearchOpenLive);
            Claim("map.scanner.clearFilter", delegate
            {
                ScannerSearchState.ClearActiveFilter();
            }, when: WorldGenSearchClearLive);

            Claim(SharedMenuGrammar.Cancel, OnCancel, when: CancelLive);

            // The Learning Helper opener rides the ScreenScope base ctor
            // (LearningHelperOpenerClaims.RegisterForScreen); none needed here.
        }

        public override string Name
        {
            get { return "starting-site"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        /// <summary>
        /// Blocks the page's inherited Page.OnCancelKeyPressed (a
        /// closeOnCancel-gated no-op) so the Escape claim is the only Back path.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>
        /// Vanilla draws this page's bottom buttons in ExtraOnGUI, outside
        /// ButtonTextCapture's InnerWindowOnGUI bracket, so the Buttons region is
        /// declared rather than captured.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get { return declaredActions; }
        }

        /// <summary>
        /// Exception to the "Buttons regions are not reported here" contract
        /// (ScreenScope.Game.cs:815-819): the page's InitialSize is Vector2.zero, so
        /// the generic ring consumer draws into a zero-area clip group and
        /// <see cref="StartingSiteButtonRingPatch"/> draws the visual ring instead.
        /// This override exists solely to keep <c>UiPointerFollow</c> tracking these
        /// buttons; it does not feed a second ring.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            int ordinal = FocusedBottomButtonOrdinal();
            return ordinal >= 0 ? BottomButtonRect(ordinal) : default(Rect);
        }

        /// <summary>
        /// The cursor's ordinal among vanilla's four bottom buttons (0-3), or -1 when
        /// the cursor is elsewhere. A declared-count mismatch degrades to no ring
        /// rather than a wrong one.
        /// </summary>
        internal int FocusedBottomButtonOrdinal()
        {
            if (Model.RegionIndex != ContentRegionCount || declaredActions.Length != 4)
            {
                return -1;
            }
            ListModel buttons = Model.CurrentRegion;
            if (buttons == null || buttons.IsEmpty || buttons.Index < 0 || buttons.Index >= 4)
            {
                return -1;
            }
            return buttons.Index;
        }

        /// <summary>
        /// The focused bottom button's rect, closed-form mirror of
        /// Page_SelectStartingSite.DoCustomBottomButtons (decompiled :223-263).
        /// Returns absolute UI points, the clip context both consumers draw in, so
        /// neither converts coordinates.
        /// </summary>
        internal static Rect BottomButtonRect(int ordinal)
        {
            Vector2 bottomButSize = (Vector2)BottomButSizeField.GetValue(null);
            int num = 4;
            int num2 = (num < 3 || !((float)UI.screenWidth < 540f + (float)num * (bottomButSize.x + 10f))) ? 1 : 2;
            int num3 = Mathf.CeilToInt((float)num / (float)num2);
            float num4 = bottomButSize.x * num3 + 10f * (num3 + 1);
            float num5 = num2 * bottomButSize.y + 10f * (num2 + 1);
            Rect rect = new Rect(((float)UI.screenWidth - num4) / 2f, (float)UI.screenHeight - num5 - 4f, num4, num5);

            WorldInspectPane pane = Find.WindowStack.WindowOfType<WorldInspectPane>();
            if (pane != null && rect.x < InspectPaneUtility.PaneWidthFor(pane) + 4f)
            {
                rect.x = InspectPaneUtility.PaneWidthFor(pane) + 4f;
            }

            float x = rect.xMin + 10f;
            float y = rect.yMin + 10f;
            Rect result = default(Rect);
            for (int i = 0; i < num; i++)
            {
                if (i == ordinal)
                {
                    result = new Rect(x, y, bottomButSize.x, bottomButSize.y);
                }
                x += bottomButSize.x + 10f;
                if (i == 1 && num2 == 2)
                {
                    x = rect.xMin + 10f;
                    y += bottomButSize.y + 10f;
                }
            }
            return result;
        }

        protected override string DefaultAcceptActionId
        {
            get { return "startingSite.next"; }
        }

        /// <summary>
        /// Tab stands down while a scanner search is typing (letters feed the
        /// search sink) and outside the live world-gen session.
        /// </summary>
        protected override bool EnableRegionCycling
        {
            get { return SiteKeysLive() && !ScannerSearchState.IsActive; }
        }

        /// <summary>
        /// Rebuilds the Tile info rows only when the selected tile changed:
        /// <c>PopulateMenuItems</c> calls <c>Find.World.CoastDirectionAt</c>, too
        /// costly to repeat every refresh pass.
        /// </summary>
        protected override void RefreshContent()
        {
            PlanetTile current = WorldNavigationState.CurrentSelectedTile;
            if (!current.Equals(tileInfoBuiltFor))
            {
                tileInfoBuiltFor = current;
                StartingSiteContext.PopulateMenuItems();
            }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == 0
                ? "RimWorldAccess.StartingSite.WorldMapRegion".Translate().ToString()
                : "RimWorldAccess.StartingSite.TileInfoRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return region == 0 ? 1 : StartingSiteContext.TileInfoRowCount;
        }

        /// <summary>
        /// Region 0 is the map; region 1's rows are the read-only Tile info
        /// categories, their detail resolved lazily for the row being described —
        /// <c>GetDetailedInfoForCategory</c> reaches
        /// <c>Find.World.NaturalRockTypesIn</c> for the stone row.
        /// The detail rides <see cref="ElementDescription.Extras"/> and is therefore
        /// suppressible by the AnnounceExtrasPart setting, which is correct for a
        /// verbose tail; the Label fragment keeps the row itself audible.
        /// </summary>
        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == 0)
            {
                return map.Describe();
            }

            ElementDescription d = new ElementDescription();
            d.Label = StartingSiteContext.TileInfoRowName(index);
            d.ReadOnly = true;
            d.Extras = StartingSiteContext.TileInfoRowDetail(index);
            return d;
        }

        /// <summary>
        /// Region 0: Enter validates and confirms the site. Region 1: Enter
        /// re-reads the focused row, which carries no action of its own.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (region == 0)
            {
                ConfirmSiteSelectionAction();
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// The map is a whole surface, not a row: while the cursor is on it the
        /// arrows are a compass and Home/End belong to the scanner, so the base's
        /// row-navigation claims stand down. Region 1 is an ordinary row region and
        /// is deliberately not exempted.
        /// </summary>
        protected override bool ContentItemOwnsNavigationKeys(int region, int index)
        {
            return region == 0 && index == 0;
        }

        /// <summary>
        /// The page's opening announcement, spoken once per scope lifetime after
        /// the first model refresh and before the entry-item announcement.
        /// </summary>
        protected override string ComposeOpenAnnouncement()
        {
            string pageTitle = "SelectStartingSite".Translate();
            return (string)"RimWorldAccess.StartingSite.OpenInstructions".Translate(pageTitle);
        }

        /// <summary>The shared core gate: the world-gen navigation session is live.</summary>
        private static bool WorldGenSessionLive()
        {
            return WorldNavigationState.IsActive
                && WorldNavigationState.Context == WorldNavContext.WorldGen;
        }

        /// <summary>Scanner and tile-info claims: no StartingPawn term.</summary>
        private static bool ScannerKeysLive()
        {
            return WorldGenSessionLive();
        }

        /// <summary>Site keys, standing down for the one-frame site-to-chargen page overlap.</summary>
        private static bool SiteKeysLive()
        {
            return WorldGenSessionLive() && !StartingPawnState.IsActive;
        }

        internal static bool WorldSurfaceLive()
        {
            return SiteKeysLive();
        }

        internal static bool PointerOverBottomButtons()
        {
            Vector2 pos = UI.MousePositionOnUIInverted;
            for (int i = 0; i < 4; i++)
            {
                if (BottomButtonRect(i).Contains(pos))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SiteKeysNoSearchLive()
        {
            return SiteKeysLive() && !ScannerSearchState.IsActive;
        }

        /// <summary>
        /// The scanner-opener gate. WindowsPreventCameraMotion is false on this page
        /// and true under any ordinary dialog, which is what stands the openers down.
        /// </summary>
        private static bool WorldGenSearchBaseLive()
        {
            return WorldGenSessionLive()
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion);
        }

        private static bool WorldGenSearchOpenLive()
        {
            return WorldGenSearchBaseLive()
                && !ScannerSearchState.IsActive
                && !GoToState.IsActive;
        }

        private static bool WorldGenSearchClearLive()
        {
            return WorldGenSearchBaseLive()
                && !ScannerSearchState.IsActive
                && ScannerSearchState.HasActiveFilter;
        }

        /// <summary>
        /// The plain arrows, and only the plain arrows, stand down when focus
        /// leaves the map: elsewhere they are the region's own navigation.
        /// </summary>
        private bool MapCursorClaimable()
        {
            return SiteKeysLive() && MapRegionFocused();
        }

        /// <summary>
        /// Whether the cursor is on the map region. Reads the model's current
        /// region rather than refreshing: a claim predicate must stay side-effect
        /// free (ScreenScope.ItemNavigationClaimable).
        /// </summary>
        private bool MapRegionFocused()
        {
            return Model.RegionIndex == 0 && !Model.CurrentRegionIsEmpty;
        }

        private bool CancelLive()
        {
            return SiteKeysLive()
                && !ScannerSearchState.IsActive;
        }

        private void OnCancel(KeyEventSnapshot e)
        {
            DoBackIfAllowed();
        }

        /// <summary>The World factions vehicle (Page_SelectStartingSite.cs:264-267).</summary>
        private static void OpenWorldFactions()
        {
            // Opening announcement handled by FactionLandingState via PostOpen patch.
            Find.WindowStack.Add(new Dialog_FactionDuringLanding());
        }

        /// <summary>
        /// Shared vehicle for the Next row, Alt+N and Enter on the map. It does not
        /// stamp the accept frame itself: the base's menus.activate claim stamps
        /// unconditionally beforehand, which keeps the settle-confirm
        /// Dialog_MessageBox that DoNext may open from consuming the same Enter.
        /// </summary>
        private void ConfirmSiteSelectionAction()
        {
            StartingSitePatch.ConfirmSiteSelection(page);
        }

        /// <summary>
        /// Shared vehicle for the Back row and Escape: vehicle B, honoring CanDoBack
        /// (Page.cs:81-88, e.g. the tutorial-mode GotoPrevPage veto) before DoBack.
        /// </summary>
        private void DoBackIfAllowed()
        {
            ShellFrameStamps.MarkCancelConsumed();
            if ((bool)CanDoBackMethod.Invoke(page, null))
            {
                DoBackMethod.Invoke(page, null);
            }
        }
    }

    /// <summary>
    /// Draws the focused bottom button's ring directly, because
    /// Page_SelectStartingSite's window is Vector2.zero-sized (decompiled :23-25) and
    /// the generic ring consumer draws inside InnerWindowOnGUI's clip group, which is
    /// clipped to nothing here. Skips the Layout event: it is a measure-only pass on
    /// the same Event.current as Repaint, and drawing the ring twice per frame risks a
    /// mismatched-group exception.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStartingSite), "DoCustomBottomButtons")]
    internal static class StartingSiteButtonRingPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                if (Event.current != null && Event.current.type == EventType.Layout)
                {
                    return;
                }
                StartingSiteScreenScope scope = FocusStack.Top as StartingSiteScreenScope;
                if (scope == null)
                {
                    return;
                }
                int ordinal = scope.FocusedBottomButtonOrdinal();
                if (ordinal < 0)
                {
                    return;
                }
                FocusRing.Draw(StartingSiteScreenScope.BottomButtonRect(ordinal));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Starting site button ring draw error", ex);
            }
        }
    }
}
