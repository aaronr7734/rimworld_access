using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>Context in which world navigation is active. Controls which features are available.</summary>
    public enum WorldNavContext
    {
        None,     // Not active
        InGame,   // F8 world map during gameplay
        WorldGen  // Starting site selection during game setup
    }

    /// <summary>
    /// Tracks the selected tile as the player arrows around the world map, for both the in-game
    /// world view and the world-generation starting-site screen.
    /// </summary>
    public static class WorldNavigationState
    {
        private static PlanetTile currentSelectedTile = PlanetTile.Invalid;
        private static bool isActive = false;
        private static bool isInitialized = false;
        private static Caravan selectedCaravan = null;
        private static HashSet<Caravan> multiSelectedCaravans = new HashSet<Caravan>();
        private static bool isInPoleTerritory = false;
        private static bool lastWasInPoleTerritory = false;
        private static WorldNavContext context = WorldNavContext.None;

        private static PlanetTile pendingStartTile = PlanetTile.Invalid;

        /// <summary>Latitude beyond which compass directions become unreliable, in degrees.</summary>
        private const float PoleLatitudeThreshold = 75f;

        private static readonly List<PlanetTile> neighborBuffer = new List<PlanetTile>();

        /// <summary>Whether world navigation is active; other systems suppress their input while it is.</summary>
        public static bool IsActive => isActive;

        public static WorldNavContext Context => context;

        public static bool IsInitialized => isInitialized;

        /// <summary>Gets or sets the current selected tile on the world map.</summary>
        public static PlanetTile CurrentSelectedTile
        {
            get => currentSelectedTile;
            set
            {
                currentSelectedTile = value;
                // External origin writes end the world scanner's navigation session so the next
                // Page Up/Down re-sorts from the new origin. Scanner-driven jumps (JumpToCurrent)
                // guard with a flag so they do not self-invalidate.
                WorldScannerState.NotifyOriginWritten();
            }
        }

        /// <summary>Whether the cursor sits where converging meridians make compass directions unreliable.</summary>
        public static bool IsInPoleTerritory => isInPoleTerritory;

        /// <summary>
        /// Sets a start tile <see cref="Open"/> will consume instead of resolving one, for when the
        /// current map is removed before the world view opens.
        /// </summary>
        public static PlanetTile PendingStartTile
        {
            get => pendingStartTile;
            set => pendingStartTile = value;
        }

        /// <summary>Opens world navigation in the in-game context.</summary>
        public static void Open()
        {
            Open(WorldNavContext.InGame);
        }

        /// <summary>
        /// Opens world navigation. WorldGen takes the WorldInterface selection or a supplied start
        /// tile; InGame walks pending tile, current map, game selection, caravan, then home.
        /// </summary>
        public static void Open(WorldNavContext navContext, PlanetTile? startTile = null)
        {
            if (Find.World == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NotAvailable".Loc(), SpeechPriority.High);
                return;
            }

            context = navContext;
            isActive = true;

            if (navContext == WorldNavContext.WorldGen)
            {
                if (startTile.HasValue && startTile.Value.Valid)
                {
                    currentSelectedTile = startTile.Value;
                }
                else if (Find.GameInitData?.startingTile.Valid == true)
                {
                    // Game's PostOpen() already called ChooseRandomStartingTile() which picks a valid land tile
                    currentSelectedTile = Find.GameInitData.startingTile;
                }
                else if (Find.WorldInterface?.SelectedTile.Valid == true)
                {
                    currentSelectedTile = Find.WorldInterface.SelectedTile;
                }
                else
                {
                    currentSelectedTile = TileFinder.RandomStartingTile();
                }
            }
            else
            {
                bool foundCaravan = false;
                bool foundStartingTile = false;

                if (pendingStartTile.Valid)
                {
                    currentSelectedTile = pendingStartTile;
                    foundStartingTile = true;
                    pendingStartTile = PlanetTile.Invalid; // Consume the pending tile

                    var caravanAtTile = Find.WorldObjects?.ObjectsAt(currentSelectedTile)
                        .OfType<RimWorld.Planet.Caravan>()
                        .FirstOrDefault(c => c.Faction == Faction.OfPlayer);
                    if (caravanAtTile != null)
                    {
                        selectedCaravan = caravanAtTile;
                        foundCaravan = true;
                    }
                }

                if (!foundStartingTile)
                {
                    Map currentMap = Find.CurrentMap;
                    if (currentMap != null && currentMap.Tile.Valid)
                    {
                        currentSelectedTile = currentMap.Tile;
                        foundStartingTile = true;

                        var caravanAtTile = Find.WorldObjects?.ObjectsAt(currentSelectedTile)
                            .OfType<RimWorld.Planet.Caravan>()
                            .FirstOrDefault(c => c.Faction == Faction.OfPlayer);
                        if (caravanAtTile != null)
                        {
                            selectedCaravan = caravanAtTile;
                            foundCaravan = true;
                        }
                    }
                }

                if (!foundStartingTile && Find.WorldSelector != null && Find.WorldSelector.SelectedTile.Valid)
                {
                    currentSelectedTile = Find.WorldSelector.SelectedTile;
                    foundStartingTile = true;
                }

                if (!foundStartingTile)
                {
                    var playerCaravans = Find.WorldObjects?.Caravans?
                        .Where(c => c.Faction == Faction.OfPlayer)
                        .ToList();

                    if (playerCaravans != null && playerCaravans.Count >= 1)
                    {
                        currentSelectedTile = playerCaravans[0].Tile;
                        selectedCaravan = playerCaravans[0];
                        foundCaravan = true;
                        foundStartingTile = true;
                    }
                }

                if (!foundStartingTile)
                {
                    Settlement homeSettlement = Find.WorldObjects?.Settlements?.FirstOrDefault(s => s.Faction == Faction.OfPlayer);
                    if (homeSettlement != null)
                    {
                        currentSelectedTile = homeSettlement.Tile;
                        foundStartingTile = true;
                    }
                    else
                    {
                        currentSelectedTile = new PlanetTile(0, -1);
                    }
                }

                if (!foundCaravan && currentSelectedTile.Valid)
                {
                    var caravanAtTile = Find.WorldObjects?.ObjectsAt(currentSelectedTile)
                        .OfType<RimWorld.Planet.Caravan>()
                        .FirstOrDefault(c => c.Faction == Faction.OfPlayer);
                    if (caravanAtTile != null)
                    {
                        selectedCaravan = caravanAtTile;
                    }
                }
            }

            isInitialized = true;

            SyncSelectionWithGame();

            string initialInfo = WorldInfoHelper.GetTileSummary(currentSelectedTile, includeRouteInfo: navContext == WorldNavContext.InGame);

            string biomeDesc = BiomeDescriptionTracker.GetBiomeDescriptionIfNew(currentSelectedTile);
            if (!string.IsNullOrEmpty(biomeDesc))
            {
                initialInfo = AppendSentence(initialInfo, biomeDesc);
            }

            if (navContext == WorldNavContext.InGame && RoutePlannerState.IsActive)
            {
                int waypointCount = RoutePlannerState.WaypointCount;
                if (waypointCount > 0)
                {
                    TolkHelper.Speak("RimWorldAccess.World.OpenWithRoute".Loc(waypointCount, initialInfo));
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.World.OpenRoutePlannerActive".Loc(initialInfo));
                }
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.World.OpenInstructions".Loc(initialInfo));
            }

            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            OrientCameraNorthUp();

            UpdatePoleStatus();
        }

        /// <summary>Appends a sentence with exactly one period of separation.</summary>
        private static string AppendSentence(string existing, string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return existing;
            if (string.IsNullOrEmpty(existing)) return sentence;

            string trimmedExisting = existing.TrimEnd();
            bool existingEndsPeriod = trimmedExisting.EndsWith(".");

            if (existingEndsPeriod)
                return trimmedExisting + " " + sentence;
            else
                return trimmedExisting + ". " + sentence;
        }

        /// <summary>Syncs the current tile into the game's selection, in-game and world-gen alike.</summary>
        // MUTATION-C: mirrors WorldSelector.SelectUnderMouse's ClearSelection()+bare-selectedTile-write
        // sequence (RimWorld.Planet/WorldSelector.cs:206,391) for the WorldSelector.SelectedTile write
        // below, and Page_SelectStartingSite.DoWindowContents' per-frame sync (Page_SelectStartingSite.cs:149-152)
        // for the WorldInterface.SelectedTile/GameInitData.startingTile writes — the latter genuinely runs
        // live here too (StartingSitePatch's Harmony patch on DoWindowContents doesn't suppress vanilla's
        // own body).
        public static void SyncSelectionWithGame()
        {
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            if (context == WorldNavContext.WorldGen)
            {
                if (Find.WorldInterface != null)
                    Find.WorldInterface.SelectedTile = currentSelectedTile;
                if (Find.GameInitData != null)
                    Find.GameInitData.startingTile = currentSelectedTile;
            }
        }

        /// <summary>Closes world navigation mode. Called when returning to map view.</summary>
        public static void Close()
        {
            isActive = false;
            isInitialized = false;
            context = WorldNavContext.None;
            currentSelectedTile = PlanetTile.Invalid;
            selectedCaravan = null;
            multiSelectedCaravans.Clear();
            isInPoleTerritory = false;
            lastWasInPoleTerritory = false;
            BiomeDescriptionTracker.Reset();
        }

        /// <summary>Orients the camera north-up so arrow keys match compass directions.</summary>
        private static void OrientCameraNorthUp()
        {
            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.RotateSoNorthIsUp();
            }
        }

        /// <summary>Updates pole-territory status from the current latitude, announcing each crossing.</summary>
        private static void UpdatePoleStatus()
        {
            if (!currentSelectedTile.Valid || Find.WorldGrid == null)
            {
                isInPoleTerritory = false;
                return;
            }

            UnityEngine.Vector2 longlat = Find.WorldGrid.LongLatOf(currentSelectedTile);
            float latitude = longlat.y;

            isInPoleTerritory = UnityEngine.Mathf.Abs(latitude) > PoleLatitudeThreshold;

            if (isInPoleTerritory && !lastWasInPoleTerritory)
            {
                string pole = latitude > 0
                    ? "RimWorldAccess.Map.Direction.Lower.North".Translate()
                    : "RimWorldAccess.Map.Direction.Lower.South".Translate();
                TolkHelper.Speak("RimWorldAccess.World.NearPole".Loc(pole), SpeechPriority.Normal);
            }
            else if (!isInPoleTerritory && lastWasInPoleTerritory)
            {
                TolkHelper.Speak("RimWorldAccess.World.LeavingPole".Loc(), SpeechPriority.Normal);
            }

            lastWasInPoleTerritory = isInPoleTerritory;
        }

        /// <summary>Moves to a neighbouring tile, resolving the direction against the camera's orientation.</summary>
        public static bool MoveInDirection(UnityEngine.Vector3 desiredDirection)
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return false;

            if (Find.WorldGrid == null)
                return false;

            PlanetTile bestNeighbor = BestNeighborToward(currentSelectedTile, desiredDirection);
            if (!bestNeighbor.Valid)
                return false;

            FinishCursorMove(bestNeighbor, endScannerSession: false);
            return true;
        }

        /// <summary>Ctrl+arrow: jumps to the first tile of another biome in a compass direction.</summary>
        public static void JumpToNextBiomeInDirection(UnityEngine.KeyCode arrowKey)
        {
            if (!isInitialized || Find.WorldGrid == null)
                return;

            if (!currentSelectedTile.Valid)
            {
                if (context == WorldNavContext.WorldGen)
                    StartingSiteContext.SelectRandomTile();
                return;
            }

            BiomeDef currentBiome = currentSelectedTile.Tile?.PrimaryBiome;
            if (currentBiome == null)
            {
                TolkHelper.Speak("RimWorldAccess.StartingSite.CannotDetermineBiome".Loc());
                return;
            }

            UnityEngine.Vector3 desiredDirection = CompassDirection(currentSelectedTile, arrowKey);
            if (desiredDirection == UnityEngine.Vector3.zero)
                return;

            PlanetTile walker = currentSelectedTile;
            int iterations = 0;
            const int maxIterations = 1000;

            while (iterations < maxIterations)
            {
                PlanetTile bestNeighbor = BestNeighborToward(walker, desiredDirection);
                if (!bestNeighbor.Valid)
                    break;

                if (bestNeighbor.Tile?.PrimaryBiome != currentBiome)
                {
                    FinishCursorMove(bestNeighbor, endScannerSession: true);
                    return;
                }

                walker = bestNeighbor;
                iterations++;
            }

            TolkHelper.Speak("RimWorldAccess.StartingSite.NoDifferentBiomeFound".Loc(iterations));
        }

        /// <summary>Alt+Shift+J on the planet: moves the cursor onto the hovered tile, leaving the camera still so the tile stays under the pointer.</summary>
        public static void PullCursorToTile(PlanetTile tile)
        {
            if (!isInitialized || !tile.Valid)
                return;

            FinishCursorMove(tile, endScannerSession: true, moveCamera: false);
        }

        private static void FinishCursorMove(PlanetTile tile, bool endScannerSession, bool moveCamera = true)
        {
            currentSelectedTile = tile;
            if (endScannerSession)
            {
                WorldScannerState.NotifyOriginWritten();
            }
            // MUTATION-C: mirrors WorldSelector.SelectUnderMouse's ClearSelection()
            // + bare-selectedTile-write sequence (RimWorld.Planet/WorldSelector.cs:
            // 206,391) for the WorldSelector.SelectedTile write, and
            // Page_SelectStartingSite.DoWindowContents' per-frame sync
            // (Page_SelectStartingSite.cs:149-152) for the WorldInterface.
            // SelectedTile/GameInitData.startingTile writes -- routed through the
            // shared helper (WorldNavigationState.SyncSelectionWithGame) so a
            // previously-selected WorldObject (landmark/faction base) is cleared
            // before this tile-only jump, instead of leaking stale selection
            // alongside the new tile.
            SyncSelectionWithGame();

            if (moveCamera && Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }

            UpdatePoleStatus();

            if (context == WorldNavContext.InGame)
            {
                RoutePlannerState.CheckOffRoute(currentSelectedTile);
            }

            AnnounceTile();
        }

        /// <summary>The scanner's own north/east calculation, so arrow keys match what it reports.</summary>
        private static UnityEngine.Vector3 CompassDirection(PlanetTile tile, UnityEngine.KeyCode key)
        {
            ScannerDirectionHelper.TangentBasis(Find.WorldGrid.GetTileCenter(tile),
                out UnityEngine.Vector3 north, out UnityEngine.Vector3 east);

            switch (key)
            {
                case UnityEngine.KeyCode.UpArrow:
                    return north;
                case UnityEngine.KeyCode.DownArrow:
                    return -north;
                case UnityEngine.KeyCode.RightArrow:
                    return east;
                case UnityEngine.KeyCode.LeftArrow:
                    return -east;
                default:
                    return UnityEngine.Vector3.zero;
            }
        }

        /// <summary>The neighbor an arrow step toward <paramref name="direction"/> lands on.</summary>
        internal static PlanetTile BestNeighborToward(PlanetTile tile, UnityEngine.Vector3 direction)
        {
            neighborBuffer.Clear();
            Find.WorldGrid.GetTileNeighbors(tile, neighborBuffer);

            UnityEngine.Vector3 origin = Find.WorldGrid.GetTileCenter(tile);
            PlanetTile best = PlanetTile.Invalid;
            float bestDot = -2f;
            for (int i = 0; i < neighborBuffer.Count; i++)
            {
                UnityEngine.Vector3 toNeighbor = (Find.WorldGrid.GetTileCenter(neighborBuffer[i]) - origin).normalized;
                float dot = UnityEngine.Vector3.Dot(toNeighbor, direction);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = neighborBuffer[i];
                }
            }
            return best;
        }

        /// <summary>Cycles to the next available planet layer.</summary>
        public static void CyclePlanetLayer()
        {
            var worldGrid = Find.WorldGrid;
            if (worldGrid == null) return;

            var currentLayer = PlanetLayer.Selected;
            if (currentLayer == null) return;

            PlanetLayer nextLayer = null;
            foreach (var kvp in worldGrid.PlanetLayers)
            {
                var layer = kvp.Value;
                if (layer == currentLayer) continue;
                if (!currentLayer.HasConnectionFromTo(layer)) continue;

                AcceptanceReport report = layer.CanSelectLayer();
                if (!report.Accepted)
                {
                    TolkHelper.Speak("RimWorldAccess.World.LayerSwitchFailed".Loc(layer.Def.LabelCap, report.Reason));
                    return;
                }
                nextLayer = layer;
                break;
            }

            if (nextLayer == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoOtherLayers".Loc());
                return;
            }

            PlanetLayer.Selected = nextLayer;
            OnSelectedLayerChanged();
        }

        /// <summary>
        /// Moves the cursor onto the newly selected planet layer, closest to where it was, and
        /// announces the switch. Call after any change to <see cref="PlanetLayer.Selected"/>: a
        /// "View layer" gizmo only sets the layer and would strand the cursor silently.
        /// </summary>
        public static void OnSelectedLayerChanged()
        {
            var newLayer = PlanetLayer.Selected;
            if (newLayer == null) return;

            var closestTile = newLayer.GetClosestTile_NewTemp(currentSelectedTile);
            if (closestTile.Valid)
            {
                currentSelectedTile = closestTile;
                SyncSelectionWithGame();
            }
            else
            {
                currentSelectedTile = PlanetTile.Invalid;
            }

            string layerName = newLayer.Def.LabelCap;
            bool isSpace = newLayer.Def.isSpace;
            TolkHelper.Speak(isSpace
                ? "RimWorldAccess.World.SwitchedToLayerSpace".Loc(layerName)
                : "RimWorldAccess.World.SwitchedToLayer".Loc(layerName));
            AnnounceTile();
        }

        /// <summary>
        /// Follows an external world jump and announces distance, direction and full tile info as if
        /// the player had arrowed there. <c>CameraJumperPatch</c> already moved the cursor, so only
        /// the pre-jump <paramref name="origin"/> is needed for the delta. Returns true when it
        /// announced, so the caller can suppress its own selection announcement; false when
        /// navigation is inactive, a dialog opened, or the cursor did not move.
        /// </summary>
        public static bool FollowExternalJumpAndAnnounce(PlanetTile origin)
        {
            if (!isInitialized || !IsActive)
                return false;

            // A window that blocks camera motion (e.g. the caravan form dialog) means this was an
            // action, not a jump — don't hijack it with a "jumped" announcement.
            if (Find.WindowStack != null && Find.WindowStack.WindowsPreventCameraMotion)
                return false;

            // The destination is wherever the cursor now sits (CameraJumperPatch synced it during the
            // jump). If it did not move from the captured origin, this was not a jump.
            PlanetTile dest = currentSelectedTile;
            if (!dest.Valid || !origin.Valid || dest == origin)
                return false;

            // The jump may cross planet layers (e.g. surface to orbit); keep the selected layer in
            // sync, and reset camera rotation so arrow-key compass directions stay correct.
            if (dest.Layer != PlanetLayer.Selected)
                PlanetLayer.Selected = dest.Layer;
            if (Find.WorldCameraDriver != null)
                Find.WorldCameraDriver.RotateSoNorthIsUp();

            // Lead with the move delta, then the full destination tile in the same utterance — the
            // world has no terrain sound, so the spoken tile is what confirms arrival. Distance and
            // direction are only meaningful within a single layer.
            string prefix = null;
            if (Find.WorldGrid != null && origin.Layer == dest.Layer)
            {
                float dist = Find.WorldGrid.ApproxDistanceInTiles(origin, dest);
                if (dist >= 0.5f)
                {
                    string dir = WorldScannerItem.GetDirectionFromTile(origin, dest);
                    prefix = !string.IsNullOrEmpty(dir)
                        ? "RimWorldAccess.World.JumpedTilesDirection".Translate(dir, dist.ToString("F0")).ToString()
                        : "RimWorldAccess.World.JumpedTiles".Translate(dist.ToString("F0")).ToString();
                }
            }
            AnnounceTile(prefix);
            return true;
        }

        /// <summary>Announces the current tile, with biome text and, in world-gen, faction/settle warnings.</summary>
        public static void AnnounceTile(string prefix = null)
        {
            if (!currentSelectedTile.Valid)
                return;

            TolkHelper.SpeakData(ComposeTileAnnouncement(currentSelectedTile,
                BiomeDescriptionTracker.GetBiomeDescriptionIfNew(currentSelectedTile), prefix));
        }

        /// <summary>The cursor's utterance for any tile, state-free; the caller owns the change-only biome paragraph.</summary>
        internal static string ComposeTileAnnouncement(PlanetTile tile, string biomeDesc, string prefix = null)
        {
            string fuelCostInfo = null;
            if (context == WorldNavContext.InGame && TransportPodLaunchState.IsActive)
            {
                int originTile = TransportPodLaunchState.GetOriginTile();
                if (originTile >= 0 && Find.WorldGrid != null)
                {
                    float distance = Find.WorldGrid.ApproxDistanceInTiles(originTile, tile);
                    if (distance > 0.1f)
                    {
                        fuelCostInfo = TransportPodLaunchState.GetFuelCostAnnouncement(distance);
                    }
                }
            }
            else if (context == WorldNavContext.InGame && GravshipDestinationState.ShouldAnnounceFuelCosts())
            {
                fuelCostInfo = GravshipDestinationState.GetFuelCostAnnouncement(tile);
            }

            string abilityDestInfo = null;
            if (context == WorldNavContext.InGame && WorldAbilityTargetingState.IsActive)
            {
                abilityDestInfo = WorldAbilityTargetingState.GetDestinationInfo(tile);
            }

            string externalTargetingInfo = null;
            if (context == WorldNavContext.InGame && ExternalWorldTargeting.ActiveProvider != null)
            {
                externalTargetingInfo = ExternalWorldTargeting.ActiveProvider.DestinationInfo(tile);
            }

            string tileInfo = WorldInfoHelper.GetTileSummary(
                tile,
                includeRouteInfo: context == WorldNavContext.InGame,
                minimal: false,
                fuelCostInfo: fuelCostInfo);

            if (!string.IsNullOrEmpty(abilityDestInfo))
            {
                tileInfo += $". {abilityDestInfo}";
            }

            if (!string.IsNullOrEmpty(externalTargetingInfo))
            {
                tileInfo += $". {externalTargetingInfo}";
            }

            // WorldGen: append faction proximity warning (change-only) before biome description
            if (context == WorldNavContext.WorldGen)
            {
                string factionWarning = StartingSiteContext.GetFactionProximityWarning(tile);
                if (!string.IsNullOrEmpty(factionWarning))
                {
                    tileInfo = AppendSentence(tileInfo, factionWarning);
                }

                BiomeDef biome = tile.Tile?.PrimaryBiome;
                if (biome != null && !string.IsNullOrEmpty(biome.settleWarning))
                {
                    tileInfo = AppendSentence(tileInfo, "Warning".Translate() + ": " + biome.settleWarning);
                }
            }

            if (!string.IsNullOrEmpty(biomeDesc))
            {
                tileInfo = AppendSentence(tileInfo, biomeDesc);
            }

            // Optional lead-in (e.g. a scanner "Jumped N tiles dir to center" cue), so the move
            // delta and the full tile description are spoken as one utterance.
            if (!string.IsNullOrEmpty(prefix))
            {
                tileInfo = string.IsNullOrEmpty(tileInfo) ? prefix : $"{prefix}. {tileInfo}";
            }

            return tileInfo;
        }

        /// <summary>Maps arrow keys to compass directions, using the scanner's own calculation.</summary>
        public static void HandleArrowKey(UnityEngine.KeyCode key)
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            if (Find.WorldGrid == null)
                return;

            UnityEngine.Vector3 desiredDirection = CompassDirection(currentSelectedTile, key);
            if (desiredDirection != UnityEngine.Vector3.zero)
            {
                MoveInDirection(desiredDirection);
            }
        }

        /// <summary>Jumps to the player's home settlement. In-game only.</summary>
        public static void JumpToHome()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized)
                return;

            Settlement homeSettlement = Find.WorldObjects?.Settlements?.FirstOrDefault(s => s.Faction == Faction.OfPlayer);

            if (homeSettlement == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoHomeSettlement".Loc(), SpeechPriority.Normal);
                return;
            }

            currentSelectedTile = homeSettlement.Tile;

            // MUTATION-C: ClearSelection()+Select(obj) mirror WorldSelector's own object-click primitives
            // (WorldSelector.cs:206,221); the trailing SelectedTile write is additional — vanilla's click
            // model leaves selectedTile Invalid when an object is selected, but our keyboard cursor
            // (WorldNavigationState.currentSelectedTile) needs SelectedTile to reflect the same tile for
            // tile-keyed lookups (AnnounceTile, gizmo tile lookups) to keep working.
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(homeSettlement);
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }
            OrientCameraNorthUp();

            UpdatePoleStatus();

            AnnounceTile();
        }

        /// <summary>Jumps to the nearest player caravan. In-game only.</summary>
        public static void JumpToNearestCaravan()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            List<Caravan> playerCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .ToList();

            if (playerCaravans == null || playerCaravans.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoPlayerCaravans".Loc(), SpeechPriority.Normal);
                return;
            }

            Caravan nearestCaravan = null;
            float nearestDistance = float.MaxValue;

            foreach (Caravan caravan in playerCaravans)
            {
                if (!caravan.Tile.Valid)
                    continue;

                float distance = Find.WorldGrid.ApproxDistanceInTiles(currentSelectedTile, caravan.Tile);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestCaravan = caravan;
                }
            }

            if (nearestCaravan == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoCaravansAtAll".Loc(), SpeechPriority.Normal);
                return;
            }

            currentSelectedTile = nearestCaravan.Tile;

            // MUTATION-C: ClearSelection()+Select(obj) mirror WorldSelector's own object-click primitives
            // (WorldSelector.cs:206,221); the trailing SelectedTile write is additional — vanilla's click
            // model leaves selectedTile Invalid when an object is selected, but our keyboard cursor
            // (WorldNavigationState.currentSelectedTile) needs SelectedTile to reflect the same tile for
            // tile-keyed lookups (AnnounceTile, gizmo tile lookups) to keep working.
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(nearestCaravan);
                Find.WorldSelector.SelectedTile = currentSelectedTile;
            }

            if (Find.WorldCameraDriver != null)
            {
                Find.WorldCameraDriver.JumpTo(currentSelectedTile);
            }
            OrientCameraNorthUp();

            UpdatePoleStatus();

            AnnounceTile();
        }

        /// <summary>Reads detailed information about the current tile (I key).</summary>
        public static void ReadDetailedTileInfo()
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            string detailedInfo = WorldInfoHelper.GetDetailedTileInfo(currentSelectedTile);
            TolkHelper.SpeakData(detailedInfo);
        }

        /// <summary>Announces one category of tile information, chosen by the number key pressed.</summary>
        public static void AnnounceTileInfoCategory(int category)
        {
            if (!isInitialized || !currentSelectedTile.Valid)
                return;

            string info;
            switch (category)
            {
                case 1:
                    info = WorldInfoHelper.GetTileGrowingInfo(currentSelectedTile);
                    break;
                case 2:
                    info = WorldInfoHelper.GetTileMovementInfo(currentSelectedTile);
                    break;
                case 3:
                    info = WorldInfoHelper.GetTileHealthInfo(currentSelectedTile);
                    break;
                case 4:
                    info = WorldInfoHelper.GetTileLocationInfo(currentSelectedTile);
                    break;
                case 5:
                    info = WorldInfoHelper.GetTileFeaturesInfo(currentSelectedTile);
                    break;
                default:
                    return;
            }

            TolkHelper.SpeakData(info);
        }

        /// <summary>
        /// Forms a caravan at the selected settlement, in-game only: the route planner opens first to
        /// set a destination, then the caravan formation dialog.
        /// </summary>
        public static void FormCaravanAtSelectedSettlement()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized || !currentSelectedTile.Valid)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoTileSelected".Loc(), SpeechPriority.Normal);
                return;
            }

            Settlement settlement = Find.WorldObjects?.SettlementAt(currentSelectedTile);

            if (settlement == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoSettlementAtTile".Loc(), SpeechPriority.Normal);
                return;
            }

            if (settlement.Faction != Faction.OfPlayer)
            {
                TolkHelper.Speak("RimWorldAccess.World.OnlyPlayerSettlements".Loc(), SpeechPriority.Normal);
                return;
            }

            if (!settlement.HasMap)
            {
                TolkHelper.Speak("RimWorldAccess.World.SettlementHasNoMap".Loc(), SpeechPriority.Normal);
                return;
            }

            // The pending flag must be set before Add, or PostOpen activates CaravanFormationState.
            Dialog_FormCaravan dialog = new Dialog_FormCaravan(settlement.Map);
            CaravanFormationState.PendingRoutePlannerOpen = true;
            Find.WindowStack.Add(dialog);

            // Caravan-formation mode drops waypoint 1 on the settlement and hides the dialog until
            // ConfirmRoute reopens it with the destination set.
            RoutePlannerState.OpenForCaravan(dialog);
        }

        /// <summary>Shows the caravan inspect screen (I key when caravan selected). In-game only.</summary>
        public static void ShowCaravanInspect()
        {
            if (context != WorldNavContext.InGame) return;
            Caravan caravan = GetSelectedCaravan();
            if (caravan == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoCaravanSelected".Loc(), SpeechPriority.Normal);
                return;
            }

            CaravanInspectState.Open(caravan);
        }

        /// <summary>
        /// Opens the order menu for the currently selected caravan (] key). In-game only.
        /// Uses the cursor tile as the target location for orders.
        /// </summary>
        public static void GiveCaravanOrders()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized || !currentSelectedTile.Valid)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoTileSelected".Loc(), SpeechPriority.Normal);
                return;
            }

            Caravan caravan = GetSelectedCaravan();
            if (caravan == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoCaravanSelected".Loc(), SpeechPriority.Normal);
                return;
            }

            List<FloatMenuOption> orders = new List<FloatMenuOption>();

            if (currentSelectedTile != caravan.Tile)
            {
                FloatMenuOption travelOption = new FloatMenuOption(
                    "RimWorldAccess.World.TravelToTile".Translate(),
                    delegate
                    {
                        AutoOrderToTile(caravan, currentSelectedTile);
                    },
                    MenuOptionPriority.Default,
                    null,
                    null,
                    0f,
                    null,
                    null
                );
                orders.Add(travelOption);
            }

            List<FloatMenuOption> worldObjectOrders = FloatMenuMakerWorld.ChoicesAtFor(currentSelectedTile, caravan);
            if (worldObjectOrders != null && worldObjectOrders.Count > 0)
            {
                orders.AddRange(worldObjectOrders);
            }

            if (orders.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoOrdersAlreadyHere".Loc(), SpeechPriority.Normal);
                return;
            }

            WindowlessFloatMenuState.Open(orders, colonistOrders: false);
            TolkHelper.Speak("RimWorldAccess.World.CaravanOrders".Loc(caravan.Label, orders.Count));
        }

        // MUTATION-C: mirrors RimWorld.Planet.WorldSelector.AutoOrderToTile; private instance method tied to mouse selection, no callable vehicle.
        private static void AutoOrderToTile(Caravan c, PlanetTile tile)
        {
            if (!tile.Valid)
            {
                return;
            }
            if (c.autoJoinable && CaravanExitMapUtility.AnyoneTryingToJoinCaravan(c))
            {
                CaravanExitMapUtility.OpenSomeoneTryingToJoinCaravanDialog(c, delegate
                {
                    AutoOrderToTileNow(c, tile);
                });
            }
            else
            {
                AutoOrderToTileNow(c, tile);
            }
        }

        // MUTATION-C: mirrors RimWorld.Planet.WorldSelector.AutoOrderToTileNow; private instance method tied to mouse selection, no callable vehicle.
        private static void AutoOrderToTileNow(Caravan c, PlanetTile tile)
        {
            if (tile.Valid && (!(tile == c.Tile) || c.pather.Moving))
            {
                PlanetTile planetTile = CaravanUtility.BestGotoDestNear(tile, c);
                if (planetTile.Valid)
                {
                    c.pather.StartPath(planetTile, null, repathImmediately: true);
                    c.gotoMote.OrderedToTile(planetTile);
                    SoundDefOf.ColonistOrdered.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.World.CaravanTraveling".Loc(c.Label));
                }
            }
        }

        /// <summary>Cycles to the next player caravan for order-giving, without moving the cursor. In-game only.</summary>
        public static void CycleToNextCaravan()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized)
                return;

            List<Caravan> playerCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .OrderBy(c => c.Label)
                .ToList();

            if (playerCaravans == null || playerCaravans.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoPlayerCaravans".Loc(), SpeechPriority.Normal);
                selectedCaravan = null;
                return;
            }

            int currentIndex = -1;
            if (selectedCaravan != null)
            {
                currentIndex = playerCaravans.IndexOf(selectedCaravan);
            }

            int nextIndex = (currentIndex + 1) % playerCaravans.Count;
            if (nextIndex == 0 && currentIndex == playerCaravans.Count - 1 && playerCaravans.Count > 1)
                MenuHelper.PlayWrapTone();
            selectedCaravan = playerCaravans[nextIndex];

            ValidateAndCleanupSelection();

            if (Find.WorldSelector != null && multiSelectedCaravans.Count == 0)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(selectedCaravan);
            }

            string caravanStatus = WorldInfoHelper.GetCaravanStatus(selectedCaravan);
            bool isMultiSelected = multiSelectedCaravans.Contains(selectedCaravan);
            string announcement = isMultiSelected
                ? "RimWorldAccess.World.CycleCaravanSelected".Translate(selectedCaravan.Label, caravanStatus, nextIndex + 1, playerCaravans.Count)
                : "RimWorldAccess.World.CycleCaravanNotSelected".Translate(selectedCaravan.Label, caravanStatus, nextIndex + 1, playerCaravans.Count);
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>Cycles to the previous player caravan for order-giving, without moving the cursor. In-game only.</summary>
        public static void CycleToPreviousCaravan()
        {
            if (context != WorldNavContext.InGame) return;
            if (!isInitialized)
                return;

            List<Caravan> playerCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer)
                .OrderBy(c => c.Label)
                .ToList();

            if (playerCaravans == null || playerCaravans.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoPlayerCaravans".Loc(), SpeechPriority.Normal);
                selectedCaravan = null;
                return;
            }

            int currentIndex = -1;
            if (selectedCaravan != null)
            {
                currentIndex = playerCaravans.IndexOf(selectedCaravan);
            }

            int prevIndex = currentIndex - 1;
            if (prevIndex < 0)
            {
                prevIndex = playerCaravans.Count - 1;
                if (currentIndex == 0 && playerCaravans.Count > 1)
                    MenuHelper.PlayWrapTone();
            }

            selectedCaravan = playerCaravans[prevIndex];

            ValidateAndCleanupSelection();

            if (Find.WorldSelector != null && multiSelectedCaravans.Count == 0)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(selectedCaravan);
            }

            string caravanStatus = WorldInfoHelper.GetCaravanStatus(selectedCaravan);
            bool isMultiSelected = multiSelectedCaravans.Contains(selectedCaravan);
            string announcement = isMultiSelected
                ? "RimWorldAccess.World.CycleCaravanSelected".Translate(selectedCaravan.Label, caravanStatus, prevIndex + 1, playerCaravans.Count)
                : "RimWorldAccess.World.CycleCaravanNotSelected".Translate(selectedCaravan.Label, caravanStatus, prevIndex + 1, playerCaravans.Count);
            TolkHelper.SpeakData(announcement);
        }

        /// <summary>The selected caravan, dropped once destroyed (merges), else a caravan on the cursor tile.</summary>
        public static Caravan GetSelectedCaravan()
        {
            if (!isInitialized)
                return null;

            if (selectedCaravan != null && (selectedCaravan.Destroyed || !Find.WorldObjects.Caravans.Contains(selectedCaravan)))
            {
                selectedCaravan = null;
            }

            if (selectedCaravan != null)
                return selectedCaravan;

            if (!currentSelectedTile.Valid)
                return null;

            var worldObjects = Find.WorldObjects?.ObjectsAt(currentSelectedTile);
            if (worldObjects == null)
                return null;

            foreach (WorldObject obj in worldObjects)
            {
                if (obj is Caravan caravan && caravan.Faction == Faction.OfPlayer)
                {
                    return caravan;
                }
            }

            return null;
        }

        /// <summary>
        /// Toggles multi-selection of the currently focused caravan (via Ctrl+Space). In-game only.
        /// </summary>
        public static void ToggleCaravanSelection()
        {
            if (context != WorldNavContext.InGame) return;
            if (selectedCaravan == null)
            {
                TolkHelper.Speak("RimWorldAccess.World.NoCaravanFocused".Loc());
                return;
            }

            ValidateAndCleanupSelection();

            if (multiSelectedCaravans.Contains(selectedCaravan))
            {
                multiSelectedCaravans.Remove(selectedCaravan);
                TolkHelper.Speak("RimWorldAccess.World.CaravanDeselected".Loc(selectedCaravan.Label, multiSelectedCaravans.Count));
            }
            else
            {
                multiSelectedCaravans.Add(selectedCaravan);
                TolkHelper.Speak("RimWorldAccess.World.CaravanSelected".Loc(selectedCaravan.Label, multiSelectedCaravans.Count));
            }

            SyncMultiSelectionWithGame();
        }

        /// <summary>Syncs our multi-selection with RimWorld's WorldSelector.</summary>
        private static void SyncMultiSelectionWithGame()
        {
            if (Find.WorldSelector == null)
                return;

            Find.WorldSelector.ClearSelection();
            foreach (var caravan in multiSelectedCaravans)
            {
                if (caravan != null && !caravan.Destroyed)
                {
                    Find.WorldSelector.Select(caravan, playSound: false);
                }
            }
        }

        /// <summary>Gets whether the specified caravan is multi-selected.</summary>
        public static bool IsCaravanMultiSelected(Caravan caravan)
        {
            return multiSelectedCaravans.Contains(caravan);
        }

        /// <summary>
        /// Gets all multi-selected caravans.
        /// Does NOT validate automatically - call ValidateAndCleanupSelection() explicitly when needed.
        /// </summary>
        public static IReadOnlyCollection<Caravan> GetMultiSelectedCaravans()
        {
            return multiSelectedCaravans;
        }

        /// <summary>
        /// Validates the multi-selection and removes any destroyed or invalid caravans.
        /// Call this after actions that might destroy caravans (like merge).
        /// </summary>
        public static void ValidateAndCleanupSelection()
        {
            if (multiSelectedCaravans.Count == 0)
                return;

            var validCaravans = Find.WorldObjects?.Caravans?
                .Where(c => c.Faction == Faction.OfPlayer && !c.Destroyed)
                .ToHashSet() ?? new HashSet<Caravan>();

            multiSelectedCaravans.RemoveWhere(c => c == null || c.Destroyed || !validCaravans.Contains(c));
        }

        /// <summary>
        /// Jumps the cursor to the selected caravan(s) location (Alt+C) and leaves the
        /// caravan selected. In-game only.
        /// If multiple caravans are selected, they must all be on the same tile.
        /// </summary>
        public static void JumpToSelectedCaravans()
        {
            if (context != WorldNavContext.InGame) return;
            if (multiSelectedCaravans.Count > 0)
            {
                var tiles = multiSelectedCaravans.Select(c => c.Tile).Distinct().ToList();
                if (tiles.Count > 1)
                {
                    TolkHelper.Speak("RimWorldAccess.World.MultiSelectDifferentTiles".Loc());
                    return;
                }

                PlanetTile targetTile = tiles[0];
                currentSelectedTile = targetTile;
                SyncSelectionWithGame();

                Find.WorldCameraDriver?.JumpTo(Find.WorldGrid.GetTileCenter(targetTile));

                string tileInfo = WorldInfoHelper.GetTileSummary(targetTile);
                TolkHelper.Speak("RimWorldAccess.World.JumpedToMultipleCaravans".Loc(multiSelectedCaravans.Count, tileInfo));
                return;
            }

            // Fall back to the caravan the cursor is on, resolved exactly as the I inspect
            // and ']' order keys resolve theirs: the cycle-focused caravan, else a player
            // caravan on the current tile (where the scanner's own jump leaves the cursor).
            Caravan caravan = GetSelectedCaravan();
            if (caravan != null)
            {
                currentSelectedTile = caravan.Tile;

                // Camera first: JumpTo(PlanetTile) is what switches the selected planet layer for
                // a caravan on another layer, and a layer change during the SelectedTile write
                // below would clear the selection with it (WorldSelector.SelectedLayer's setter).
                Find.WorldCameraDriver?.JumpTo(currentSelectedTile);

                // Leave the caravan selected so the follow-up keys (I, ']', vanilla gizmos)
                // act on it; TrySelect carries vanilla's own SelectableNow gate.
                // MUTATION-C: the trailing SelectedTile write mirrors WorldSelector.SelectUnderMouse
                // (RimWorld.Planet/WorldSelector.cs:391) and is additional to TrySelect — vanilla
                // leaves selectedTile Invalid once an object is selected, but our keyboard cursor
                // needs it to reflect the same tile for tile-keyed lookups (AnnounceTile, gizmo
                // tile lookups). No vanilla vehicle both selects an object and keeps a tile selected.
                CameraJumper.TrySelect(caravan);
                if (Find.WorldSelector != null)
                    Find.WorldSelector.SelectedTile = currentSelectedTile;

                string tileInfo = WorldInfoHelper.GetTileSummary(caravan.Tile);
                TolkHelper.Speak("RimWorldAccess.World.JumpedToCaravan".Loc(caravan.Label, tileInfo));
                return;
            }

            TolkHelper.Speak("RimWorldAccess.World.NoCaravanSelectedHint".Loc());
        }

        /// <summary>Clears all multi-selected caravans.</summary>
        public static void ClearMultiSelection()
        {
            multiSelectedCaravans.Clear();
            if (Find.WorldSelector != null)
            {
                Find.WorldSelector.ClearSelection();
            }
        }
    }
}
