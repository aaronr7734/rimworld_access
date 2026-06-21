using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Surfaces RimWorld Access documentation chapters (and enriched vanilla concepts) at the
    /// moment they become relevant — the contextual "teach at the right time" layer on top of
    /// the browsable Learning Helper.
    ///
    /// Teaching routes through the game's own <see cref="LessonAutoActivator.TeachOpportunity"/>,
    /// which: respects the player's AdaptiveTraining setting, never re-teaches an already-learned
    /// concept, and de-duplicates against currently active lessons. Crucially, the explicit-teach
    /// path (TeachOpportunity -> TryInitiateLesson -> LearningReadout.TryActivateConcept) has NO
    /// <c>gameMode</c>/ProgramState gate — that gate lives only in the background desire engine —
    /// so a chapter can be taught during world/site selection (Entry) as well as in-game (Playing).
    /// Activation is announced by <see cref="LearningHelperPatch"/>.
    ///
    /// Trigger points call <see cref="Teach"/> from natural "first use" moments (a page opening,
    /// the first inspection, the first colonist selected). A once-per-session guard means a
    /// per-open hook can call it every time without re-announcing an unlearned lesson on each open;
    /// the guard resets on game start/load (see <see cref="ResetSession"/>) so each game gets a
    /// fresh teaching pass, while the knowledge database still suppresses already-learned lessons.
    /// </summary>
    public static class DocsTeacher
    {
        private static readonly HashSet<string> offeredThisSession = new HashSet<string>();

        // Tile-by-tile cursor moves before we suggest the faster jump modes.
        private const int JumpModesMoveThreshold = 12;
        private static int cursorMoveCount;

        // When a new game starts, the starting colonists are still descending in drop pods, so the
        // map has no spawned colonists yet (FreeColonistsCount is non-zero but they are not on the
        // map). Teaching "how to select a colonist" then is premature. Instead we arm this flag and
        // teach the moment a colonist is actually present (see PollDeferred), polled each frame.
        private static bool selectingColonistsPending;

        // The scanner and forbid/allow lessons both wait until ALL starting colonists have landed.
        // The scanner surveys the whole map, so it is most useful once everything is on the ground;
        // and the drop pods carry cargo that arrives forbidden, so teaching "press Alt+F to free
        // everything" before the last pod lands would have the player free the map, then watch fresh
        // forbidden items rain down. We wait for every pod down, then teach both in the same poll
        // frame (so the announcement coalesces). Forbidding stays gated on there being enough
        // forbidden items, so a clean late-game save load does not trigger it.
        private static bool scannerPending;
        private static bool forbiddingPending;

        /// <summary>
        /// Offer a lesson for the concept with the given defName. No-op when adaptive training is
        /// off, the scripted tutorial is running, the tutor is unavailable, the concept does not
        /// exist (e.g. a DLC concept whose DLC is absent), or it has already been offered this
        /// session. Otherwise safe to call repeatedly from an event hook.
        /// </summary>
        public static void Teach(string conceptDefName, OpportunityType opportunity = OpportunityType.Important)
        {
            if (!TutorSystem.AdaptiveTrainingEnabled || TutorSystem.TutorialMode)
                return;
            if (Find.Tutor?.learningReadout == null)
                return;
            if (!offeredThisSession.Add(conceptDefName))
                return;

            ConceptDef conc = DefDatabase<ConceptDef>.GetNamedSilentFail(conceptDefName);
            if (conc == null)
                return;

            ReteachOverriddenConceptIfAlreadyLearned(conc);

            LessonAutoActivator.TeachOpportunity(conc, opportunity);
        }

        /// <summary>
        /// A vanilla (or DLC) concept the player completed long ago keeps its "learned" flag, which
        /// makes <see cref="LessonAutoActivator.TeachOpportunity"/> skip it — so our re-authored,
        /// accessible version of that concept (e.g. the world-map chapter overriding
        /// WorldCameraMovement) would never surface for a veteran player. The first time we
        /// contextually teach a concept whose help text we override, if the player has already
        /// completed it, clear that one concept's knowledge so our version is taught. Recorded in
        /// settings so it happens exactly once per player per concept — no nagging across games, and
        /// concepts without an override (our own RWA_* chapters) are never touched.
        /// </summary>
        private static void ReteachOverriddenConceptIfAlreadyLearned(ConceptDef conc)
        {
            if (ConceptHelpOverrides.GetHelpText(conc) == null)
                return;

            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || settings.RetaughtOverriddenConcepts.Contains(conc.defName))
                return;

            settings.RetaughtOverriddenConcepts.Add(conc.defName);
            settings.Write();

            if (PlayerKnowledgeDatabase.IsComplete(conc))
                PlayerKnowledgeDatabase.SetKnowledge(conc, 0f);
        }

        /// <summary>
        /// Fires the cursor-landing contextual lessons for wherever the map cursor just arrived,
        /// regardless of HOW it got there — arrowing tile-by-tile, a scanner Home jump, etc. (The
        /// teach checks used to live only in arrow movement, so jumping straight to a thing via the
        /// scanner never triggered them.) Safe to call repeatedly; the once-per-session guard and the
        /// knowledge database keep it from re-announcing.
        /// </summary>
        public static void NotifyCursorLanded(IntVec3 position, Map map)
        {
            if (map == null)
                return;

            // Cursor landing on a pawn is the moment to learn it can be inspected — before the
            // player would otherwise know to press Enter.
            if (position.GetFirstPawn(map) != null)
            {
                Teach("RWA_InspectingThings");
            }
            // The context menu (]) gives a selected colonist orders about what's under the cursor:
            // equip a weapon, wear apparel, build a blueprint, work on a building. Teach it the
            // moment a selected colonist's cursor reaches one of those. (Frame is a Building too.)
            else if (Find.Selector?.SingleSelectedThing is Pawn selectedColonist && selectedColonist.IsColonist
                     && position.GetThingList(map).Any(t =>
                            t.def.IsWeapon || t.def.IsApparel || t is Blueprint || t is Building))
            {
                Teach("RWA_ContextMenu");
            }
        }

        /// <summary>
        /// Counts tile-by-tile cursor movement; once the player has nudged the cursor enough times
        /// to feel the slowness, suggests the jump-modes chapter. Called from cursor navigation.
        /// </summary>
        public static void NotifyCursorMoved()
        {
            if (offeredThisSession.Contains("RWA_JumpModes"))
                return;
            if (++cursorMoveCount >= JumpModesMoveThreshold)
                Teach("RWA_JumpModes", OpportunityType.GoodToKnow);
        }

        /// <summary>
        /// Arms the "selecting colonists" lesson to be taught once a colonist is actually spawned on
        /// the map, rather than at game start when the starting pawns are still in transit. Called
        /// from GameStartPatch; resolved by <see cref="PollDeferred"/>.
        /// </summary>
        public static void RequestSelectingColonistsWhenPresent()
        {
            selectingColonistsPending = true;
        }

        /// <summary>
        /// Arms the map-basics lessons (scanner, then forbid/allow) to be taught once every starting
        /// colonist has spawned — i.e. all drop pods are down and the map is complete. Both fire in
        /// the same poll frame so their announcements coalesce.
        /// </summary>
        public static void RequestMapBasicsWhenAllColonistsPresent()
        {
            scannerPending = true;
            forbiddingPending = true;
        }

        /// <summary>
        /// Per-frame check for deferred teaches that wait on world state. Cheap (a bool test) until
        /// something is pending. Called every OnGUI pass from UnifiedKeyboardPatch, so it runs even
        /// while the game is paused (the starting drop pods land on the first unpaused ticks).
        /// </summary>
        public static void PollDeferred()
        {
            if (!selectingColonistsPending && !scannerPending && !forbiddingPending)
                return;

            Map map = Find.CurrentMap;
            if (map == null)
                return;

            MapPawns mp = map.mapPawns;
            int spawned = mp.FreeColonistsSpawnedCount;

            // Teach selecting as soon as there is at least one colonist on the map to select.
            if (selectingColonistsPending && spawned > 0)
            {
                selectingColonistsPending = false;
                Teach("RWA_SelectingColonists", OpportunityType.Critical);
            }

            // Teach the map-basics batch (scanner, then forbid/allow) only once every colonist has
            // landed (spawned has caught up to the total free-colonist count, which includes those
            // still in transit), so all pod cargo is on the map. Both fire in this one frame so their
            // announcements coalesce. Forbidding stays gated on a meaningful number of forbidden items.
            if ((scannerPending || forbiddingPending) && spawned > 0 && spawned >= mp.FreeColonistsCount)
            {
                if (scannerPending)
                {
                    scannerPending = false;
                    Teach("RWA_Scanner");
                }
                if (forbiddingPending)
                {
                    forbiddingPending = false;
                    if (CountForbiddenItems(map) > 20)
                        Teach("Forbidding");
                }
            }
        }

        /// <summary>
        /// Counts forbidden items on the map, stopping once it passes 21 (we only need the
        /// threshold, so there is no point scanning a whole late-game map).
        /// </summary>
        private static int CountForbiddenItems(Map map)
        {
            int count = 0;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                CompForbiddable forbiddable = thing.TryGetComp<CompForbiddable>();
                if (forbiddable != null && forbiddable.Forbidden)
                {
                    count++;
                    if (count > 20)
                        break;
                }
            }
            return count;
        }

        /// <summary>Clears per-session teaching state. Called on game start/load so each game
        /// re-offers contextual lessons the player has not yet learned.</summary>
        public static void ResetSession()
        {
            offeredThisSession.Clear();
            cursorMoveCount = 0;
            selectingColonistsPending = false;
            scannerPending = false;
            forbiddingPending = false;
        }
    }
}
