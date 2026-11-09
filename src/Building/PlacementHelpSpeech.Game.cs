using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks the commentary vanilla draws beside the cursor during placement — facility links,
    /// substructure footprints, terrain paint colour, same-def spacing — which is painted straight
    /// to the screen inside <c>DrawMouseAttachments</c> with no widget, tooltip or window to scrape.
    /// The strings are harvested, not re-derived: the collector opens across
    /// <see cref="PlacementAidCursorPatch"/>'s existing bracket, so they arrive translated and
    /// already filtered by vanilla's own conditions. Two cold taps carry everything —
    /// <c>PlaceWorker.DrawTextLine</c> (the shared static AND the four local functions of that name,
    /// which write the facility link lists) and <c>Widgets.DrawNumberOnMap</c>, where the number's
    /// COLOUR is vanilla's legality verdict (RimWorld/Designator_Place.cs:79).
    /// The bracket runs every frame, so a harvest is spoken only when its text changes, and spoken
    /// ahead of the cursor's new cell in one utterance: the cursor step hands its tile description
    /// over (<see cref="TryHoldTileAnnouncement"/>) instead of speaking it, and
    /// <see cref="HealHeldTile"/> covers a tile whose pass never arrived.
    /// </summary>
    internal static class PlacementHelpSpeech
    {
        private static readonly List<string> collected = new List<string>();
        private static readonly List<string> outlines = new List<string>();
        private static bool collecting;
        private static bool collectingOutlines;
        private static string lastSpoken;
        private static string heldTile;
        private static int heldTileFrame;
        private static IntVec3 outlineCell = IntVec3.Invalid;
        private static Rot4 outlineRot;
        private static GhostAuthor ghostAuthor;

        /// <summary>True while a placement draw pass is being harvested; the taps check it first.</summary>
        internal static bool Collecting
        {
            get { return collecting; }
        }

        /// <summary>True while the ghost pass is being harvested; see <see cref="CollectOutline"/>.</summary>
        internal static bool CollectingOutlines
        {
            get { return collectingOutlines; }
        }

        internal static void Begin(IntVec3 cell, Rot4 rot)
        {
            // Outlines (Update) seed the attachment pass (OnGUI) as ONE announcement; a step taken
            // since the ghost pass leaves them stale, so the held tile waits a frame for fresh ones.
            if (cell != outlineCell || rot != outlineRot)
            {
                return;
            }
            collecting = true;
            collected.Clear();
            collected.AddRange(outlines);
        }

        /// <summary>
        /// Opens the ghost harvest for the cell and rotation this pass is drawing. Outlines are
        /// kept across passes describing the same cell and rotation: vanilla BLINKS an outline it
        /// means as a warning (RimWorld/PlaceWorker_WatermillGenerator.cs:64), so a pass landing on
        /// a dark frame is missing that outline rather than reporting it gone, and clearing every
        /// pass would flip the harvest through the changed-since-last-time test twice a second.
        /// </summary>
        internal static void BeginOutlines(IntVec3 cell, Rot4 rot)
        {
            collectingOutlines = true;
            if (cell != outlineCell || rot != outlineRot)
            {
                outlines.Clear();
                outlineCell = cell;
                outlineRot = rot;
            }
        }

        internal static void EndOutlines()
        {
            collectingOutlines = false;
        }

        internal static void CollectOutline(string text)
        {
            if (!collectingOutlines || string.IsNullOrEmpty(text) || outlines.Contains(text))
            {
                return;
            }
            outlines.Add(text);
        }

        /// <summary>
        /// Closes the pass and speaks the harvest, ahead of any tile description the cursor step
        /// handed over. Safe to call without a matching <see cref="Begin"/> — the finalizer that
        /// calls it runs on the throwing path too.
        /// </summary>
        internal static void EndAndAnnounce()
        {
            if (!collecting)
            {
                return;
            }
            collecting = false;

            string text = collected.Count == 0 ? null : string.Join(". ", collected.ToArray());
            collected.Clear();

            PlacementHelpMerge.Decision decision = PlacementHelpMerge.Decide(text, TakeHeldTile(), lastSpoken);
            lastSpoken = decision.LastSpoken;
            if (decision.Utterance == null)
            {
                return;
            }
            // A tile description was asked for by an arrow press, so a merged utterance keeps the
            // priority a cursor step has on its own; help alone yields to whatever is speaking.
            TolkHelper.SpeakData(decision.Utterance,
                decision.CarriesTile ? SpeechPriority.Normal : SpeechPriority.Low);
        }

        /// <summary>
        /// Takes the tile description the cursor step composed, to be spoken by the draw pass
        /// after the placement help. Returns false when no pass is coming, and the caller speaks
        /// it itself.
        /// </summary>
        internal static bool TryHoldTileAnnouncement(string text)
        {
            if (string.IsNullOrEmpty(text) || !ExpectsPass())
            {
                return false;
            }
            // A second cursor step before the draw pass supersedes the first, whose cell never got
            // a harvest; speaking it now rather than dropping it keeps every step audible.
            FlushHeldTile();
            heldTile = text;
            heldTileFrame = Time.frameCount;
            return true;
        }

        /// <summary>
        /// Speaks a held tile description whose draw pass never arrived — the designator was
        /// dropped, or placement was cancelled, between the key and the draw. Called from the
        /// dispatcher's own per-frame reconcile, which runs whether or not a placement is live.
        /// </summary>
        internal static void HealHeldTile()
        {
            if (heldTile != null && PlacementHelpMerge.ShouldFlushHeld(heldTileFrame, Time.frameCount))
            {
                FlushHeldTile();
            }
        }

        /// <summary>
        /// Whether a placement draw pass will harvest help for the keyboard cursor's cell:
        /// <see cref="PlacementHelpCapturePatch"/>'s own gate, read ahead of the draw and asked of
        /// the designator the GAME has selected, since that is the one whose attachments it draws
        /// (Verse/DesignatorManager.cs:132).
        /// </summary>
        private static bool ExpectsPass()
        {
            if (Find.DesignatorManager == null)
            {
                return false;
            }
            Designator_Place place = Find.DesignatorManager.SelectedDesignator as Designator_Place;
            return place != null && PlacementAidCursorPatch.KeyboardPlacementCursor(place).IsValid;
        }

        private static string TakeHeldTile()
        {
            string text = heldTile;
            heldTile = null;
            return text;
        }

        private static void FlushHeldTile()
        {
            string text = TakeHeldTile();
            if (!string.IsNullOrEmpty(text))
            {
                TolkHelper.SpeakData(text);
            }
        }

        /// <summary>Forgets what was last spoken, so leaving and re-entering placement speaks again.</summary>
        internal static void Reset()
        {
            collecting = false;
            collectingOutlines = false;
            collected.Clear();
            outlines.Clear();
            outlineCell = IntVec3.Invalid;
            lastSpoken = null;
            ghostAuthor = default(GhostAuthor);
            // A held tile description is a cursor step the player already made and is owed, so
            // placement ending leaves it for HealHeldTile rather than dropping it.
        }

        /// <summary>
        /// The place worker whose ghost draw is running, with the cell and rotation it was handed.
        /// A footprint part is named from the worker's own type and cell sets, never from a label,
        /// so a modded worker simply stays unnamed.
        /// </summary>
        internal struct GhostAuthor
        {
            public Type Worker;
            public IntVec3 Cell;
            public Rot4 Rot;
        }

        internal static GhostAuthor CurrentGhostAuthor
        {
            get { return ghostAuthor; }
        }

        /// <summary>Opens a ghost-draw bracket, returning the enclosing one for the finalizer to restore.</summary>
        internal static GhostAuthor PushGhostAuthor(Type worker, IntVec3 cell, Rot4 rot)
        {
            GhostAuthor previous = ghostAuthor;
            ghostAuthor.Worker = worker;
            ghostAuthor.Cell = cell;
            ghostAuthor.Rot = rot;
            return previous;
        }

        internal static void PopGhostAuthor(GhostAuthor previous)
        {
            ghostAuthor = previous;
        }

        internal static void Collect(string text)
        {
            if (!collecting || string.IsNullOrEmpty(text))
            {
                return;
            }
            // Leading bullets and embedded newlines are layout; a newline would break the utterance
            // in two.
            string cleaned = text.Replace("\n", ". ").Replace("  - ", string.Empty).Trim();
            if (cleaned.Length == 0 || collected.Contains(cleaned))
            {
                return;
            }
            collected.Add(cleaned);
        }
    }

    /// <summary>
    /// Opens and closes the harvest across vanilla's placement-aid draw. Separate from
    /// <see cref="PlacementAidCursorPatch"/>'s prefix/finalizer pair so the two are gated
    /// independently: the cursor override serves the sighted viewer whatever the speech setting says.
    /// </summary>
    [HarmonyPatch]
    internal static class PlacementHelpCapturePatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            return PlacementAidCursorPatch.TargetMethods();
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(Designator_Place __instance)
        {
            try
            {
                // Only while the keyboard owns the placement: a mouse-driven placement already puts
                // this text where the player is looking, and harvesting it would speak over hover.
                IntVec3 cell = PlacementAidCursorPatch.KeyboardPlacementCursor(__instance);
                if (!cell.IsValid)
                {
                    return;
                }
                PlacementHelpSpeech.Begin(cell, BuildingReflection.GetPlacingRot(__instance));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Placement help capture error", ex);
            }
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        internal static Exception Finalizer(Exception __exception)
        {
            try
            {
                PlacementHelpSpeech.EndAndAnnounce();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Placement help capture error", ex);
            }
            return __exception;
        }
    }

    /// <summary>
    /// Opens the outline harvest across vanilla's ghost draw, with the placement cursor pointed at
    /// the keyboard's cell: everything inside <c>Designator_Place.SelectedUpdate</c> answers for
    /// <see cref="UI.MouseCell"/> (RimWorld/Designator_Place.cs:177), so without the override the
    /// outlines would describe wherever the pointer was parked.
    /// A finalizer, not a postfix — a modded place worker's throw must leave neither the cursor
    /// override latched nor the harvest open.
    /// </summary>
    [HarmonyPatch(typeof(Designator_Place), nameof(Designator_Place.SelectedUpdate))]
    internal static class PlacementGhostCapturePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(Designator_Place __instance, out bool __state)
        {
            __state = false;
            try
            {
                IntVec3 cell = PlacementAidCursorPatch.KeyboardPlacementCursor(__instance);
                if (!cell.IsValid)
                {
                    return;
                }
                __state = true;
                DevToolTargeting.PushCursorOverride(cell);
                PlacementHelpSpeech.BeginOutlines(cell, BuildingReflection.GetPlacingRot(__instance));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Placement ghost capture error", ex);
            }
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        internal static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state)
            {
                PlacementHelpSpeech.EndOutlines();
                DevToolTargeting.PopCursorOverride();
            }
            return __exception;
        }
    }

    /// <summary>
    /// Brackets every place worker's ghost draw so the outline capture knows which worker painted a
    /// given outline, and for which cell and rotation. Both of vanilla's ghost entry points funnel
    /// through this one virtual (Verse/GhostDrawer.cs:25 for a thing,
    /// RimWorld/Designator_Place.cs:188 for a terrain), as does a modded caller.
    /// Patched by declared-method sweep, not on <see cref="PlaceWorker"/>: the base body is empty
    /// and every real footprint lives in an override that never calls it, so a base-type patch
    /// would never run. A finalizer, not a postfix, so a throwing modded worker cannot leave the
    /// bracket open.
    /// </summary>
    [HarmonyPatch]
    internal static class PlaceWorkerGhostAuthorPatch
    {
        private static readonly Type[] DrawGhostParams =
        {
            typeof(ThingDef), typeof(IntVec3), typeof(Rot4), typeof(Color), typeof(Thing),
        };

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in SafeTypeSweep.AssignableFromSafe(typeof(PlaceWorker),
                (_, ex) => ModLogger.LimitedError("Place worker ghost sweep error", ex)))
            {
                if (!SafeTypeSweep.TryEvaluate(type,
                        t => AccessTools.DeclaredMethod(t, "DrawGhost", DrawGhostParams),
                        out MethodInfo declared,
                        (_, ex) => ModLogger.LimitedError("Place worker ghost sweep error", ex)))
                {
                    continue;
                }
                if (declared != null && !declared.IsAbstract && !declared.ContainsGenericParameters)
                {
                    yield return declared;
                }
            }
        }

        // Positional binding: an override is free to rename its parameters, and Harmony's by-name
        // binding throws at patch time when it does.
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(PlaceWorker __instance, [HarmonyArgument(1)] IntVec3 center,
            [HarmonyArgument(2)] Rot4 rot, out PlacementHelpSpeech.GhostAuthor __state)
        {
            __state = default(PlacementHelpSpeech.GhostAuthor);
            try
            {
                __state = PlacementHelpSpeech.PushGhostAuthor(
                    __instance != null ? __instance.GetType() : null, center, rot);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Place worker ghost bracket error", ex);
            }
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        internal static Exception Finalizer(PlacementHelpSpeech.GhostAuthor __state, Exception __exception)
        {
            PlacementHelpSpeech.PopGhostAuthor(__state);
            return __exception;
        }
    }

    /// <summary>
    /// Turns the regions a place worker outlines into speech — the only channel carrying a
    /// multi-part footprint, such as the water-mill wheel strip that must sit in moving water and
    /// is described by nothing but the outline colour around it. Green and red are
    /// <see cref="Designator_Place"/>'s own placement verdict colours, so speaking them reports
    /// vanilla's decision rather than re-deriving one; any other colour is decoration and stays
    /// silent.
    /// A region is named from the drawing worker's OWN cell sets for the cell and rotation it was
    /// handed (via <see cref="PlaceWorkerGhostAuthorPatch"/>'s bracket), so an unlisted or modded
    /// worker keeps the generic reading.
    /// </summary>
    [HarmonyPatch(typeof(GenDraw), nameof(GenDraw.DrawFieldEdges))]
    [HarmonyPatch(new[] { typeof(List<IntVec3>), typeof(Color), typeof(float?), typeof(HashSet<IntVec3>), typeof(int) })]
    internal static class PlacementOutlineCapturePatch
    {
        /// <summary>
        /// The blinking orange a water mill's flow area takes when another mill already draws from
        /// it (RimWorld/PlaceWorker_WatermillGenerator.cs:63) — vanilla's only cue that the
        /// turbulence penalty applies, so it is admitted alongside the two placement colours, and
        /// only for that one region.
        /// </summary>
        private static readonly Color SharedWaterColor = new Color(1f, 0.6f, 0f);

        [HarmonyPostfix]
        internal static void Postfix(List<IntVec3> cells, Color color)
        {
            try
            {
                if (!PlacementHelpSpeech.CollectingOutlines || cells == null || cells.Count == 0)
                {
                    return;
                }
                FootprintPart part = Identify(cells);
                OutlineVerdict verdict;
                if (!TryReadVerdict(color, part, out verdict))
                {
                    return;
                }
                if (part == FootprintPart.WatermillWaterFlow && verdict == OutlineVerdict.Suitable)
                {
                    // Unshared flow area is noise and falsely implies placement validity;
                    // only the shared-with-another-mill verdict is worth speaking.
                    return;
                }
                PlacementHelpSpeech.CollectOutline(DescribeOutline(cells, part, verdict));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Placement outline error", ex);
            }
        }

        /// <summary>
        /// Which named region of the drawing worker's footprint these cells are, asked of the
        /// worker's own cell sets rather than guessed from shape or draw order.
        /// </summary>
        private static FootprintPart Identify(List<IntVec3> cells)
        {
            PlacementHelpSpeech.GhostAuthor author = PlacementHelpSpeech.CurrentGhostAuthor;
            if (author.Worker == null || !author.Cell.IsValid)
            {
                return FootprintPart.Unnamed;
            }
            if (typeof(PlaceWorker_WatermillGenerator).IsAssignableFrom(author.Worker))
            {
                if (SameCells(cells, CompPowerPlantWater.WaterCells(author.Cell, author.Rot)))
                {
                    return FootprintPart.WatermillWheel;
                }
                if (SameCells(cells, CompPowerPlantWater.WaterUseCells(author.Cell, author.Rot)))
                {
                    return FootprintPart.WatermillWaterFlow;
                }
            }
            return FootprintPart.Unnamed;
        }

        private static bool SameCells(List<IntVec3> cells, IEnumerable<IntVec3> expected)
        {
            HashSet<IntVec3> drawn = new HashSet<IntVec3>(cells);
            int matched = 0;
            foreach (IntVec3 cell in expected)
            {
                if (!drawn.Contains(cell))
                {
                    return false;
                }
                matched++;
            }
            return matched == drawn.Count;
        }

        private static bool TryReadVerdict(Color color, FootprintPart part, out OutlineVerdict verdict)
        {
            if (color == Designator_Place.CanPlaceColor.ToOpaque())
            {
                verdict = OutlineVerdict.Suitable;
                return true;
            }
            if (color == Designator_Place.CannotPlaceColor.ToOpaque())
            {
                verdict = OutlineVerdict.Unsuitable;
                return true;
            }
            if (part == FootprintPart.WatermillWaterFlow && color == SharedWaterColor)
            {
                verdict = OutlineVerdict.SharedWithOtherWatermill;
                return true;
            }
            verdict = OutlineVerdict.Suitable;
            return false;
        }

        private static string DescribeOutline(List<IntVec3> cells, FootprintPart part, OutlineVerdict verdict)
        {
            CellRect bounds = CellRect.FromCellList(cells);
            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            IntVec3 center = bounds.CenterCell;
            int distance = cursor.IsValid ? Mathf.RoundToInt(cursor.DistanceTo(center)) : 0;
            bool atCursor = !cursor.IsValid || distance == 0;

            string key = PlacementFootprintNames.OutlineKey(part, verdict, atCursor)
                ?? PlacementFootprintNames.OutlineKey(FootprintPart.Unnamed, verdict, atCursor);
            if (key == null)
            {
                return null;
            }
            return atCursor
                ? key.Translate(bounds.Width, bounds.Height).ToString()
                : key.Translate(bounds.Width, bounds.Height, distance,
                    ScannerDirectionHelper.GetCompassDirection(cursor, center)).ToString();
        }
    }

    /// <summary>Harvests every place worker's own text lines. See <see cref="PlacementHelpSpeech"/>.</summary>
    [HarmonyPatch]
    internal static class PlaceWorkerTextLineCapturePatch
    {
        /// <summary>The types whose placement text goes through a local <c>DrawTextLine</c> of their own rather than <see cref="PlaceWorker"/>'s shared one.</summary>
        private static readonly Type[] LocalTextLineOwners =
        {
            typeof(CompFacility),
            typeof(CompAffectedByFacilities),
            typeof(PlaceWorker_DrawLinesToDeathrestBuildings),
            typeof(PlaceWorker_DrawLinesToDeathrestCaskets),
        };

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlaceWorker), "DrawTextLine");
            foreach (Type owner in LocalTextLineOwners)
            {
                foreach (MethodInfo local in AccessTools.GetDeclaredMethods(owner))
                {
                    // Roslyn mangles a local function's name around both the outer and inner names;
                    // matching the inner half by substring survives a recompile that renumbers the
                    // mangling, and the parameter-shape check keeps unrelated matches out.
                    if (local.Name.Contains("g__DrawTextLine") && HasTextParameter(local))
                    {
                        yield return local;
                    }
                }
            }
        }

        private static bool HasTextParameter(MethodInfo method)
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (parameter.Name == "text" && parameter.ParameterType == typeof(string))
                {
                    return true;
                }
            }
            return false;
        }

        [HarmonyPostfix]
        internal static void Postfix(string text)
        {
            try
            {
                PlacementHelpSpeech.Collect(text);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Placement help text error", ex);
            }
        }
    }

    /// <summary>Turns the same-def spacing numbers into speech, colour and all; the colour is the load-bearing half.</summary>
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.DrawNumberOnMap))]
    internal static class PlacementSpacingNumberCapturePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(int number, Color textColor)
        {
            try
            {
                if (!PlacementHelpSpeech.Collecting)
                {
                    return;
                }
                PlacementHelpSpeech.Collect(textColor == Color.red
                    ? (string)"RimWorldAccess.Building.Place.SpacingOutOfRange".Translate(number)
                    : (string)"RimWorldAccess.Building.Place.SpacingInRange".Translate(number));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Placement spacing error", ex);
            }
        }
    }
}
