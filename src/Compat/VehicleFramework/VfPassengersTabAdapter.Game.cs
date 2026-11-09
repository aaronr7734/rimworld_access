using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Vehicle Framework's Vehicles.ITab_Vehicle_Passengers — the
    /// "Pawns" tab listing everyone aboard a vehicle by role, plus anyone stored in its cargo. One
    /// section per role handler, a final Cargo section, and per-pawn rows carrying the same
    /// needs/mood/thought detail the tab's own specific-needs popup draws. Structure is read
    /// straight from VehiclePawn.handlers and AllInventoryPawns. Only the four VF-specific types
    /// (VehiclePawn, VehicleRoleHandler, VehicleRole, VehicleTabHelper_Passenger) go through
    /// reflection; the vanilla types the data actually lives in are used typed.
    ///
    /// The tab's only mutation — dragging a pawn row onto another section — is driven by invoking
    /// VF's own VehicleTabHelper_Passenger.HandleDragEvent with a fabricated mouse-up event
    /// (vehicle A, see <see cref="InvokeDragDrop"/>). VF's CanOperateRole/HandlingType gating is
    /// deliberately NOT reproduced client-side: every move/swap row is offered whenever a slot
    /// COULD open it, and HandleDragEvent's own live re-check answers with its caution/reject
    /// message when the pawn can't actually operate the role.
    ///
    /// DEVIATION: the "Mood and thoughts" section adds a break-thresholds sub-node (via the shared
    /// PawnMoodAdapter.BuildBreakThresholdsChildren) that vanilla's own
    /// NeedsCardUtility.DoNeedsMoodAndThoughts panel never draws, gated on CanDoRandomMentalBreaks
    /// to match every other call site of that helper.
    ///
    /// Deliberately NOT covered: the dev-mode pawn-overlay-renderer editor (commented out in VF's
    /// own source — there is nothing live to mirror).
    /// </summary>
    internal sealed class VfPassengersTabAdapter : InspectNodeAdapter
    {
        private readonly Type vehiclePawnType;
        private readonly FieldInfo handlersField;
        private readonly PropertyInfo allInventoryPawnsProperty;

        private readonly Type roleHandlerType;
        private readonly FieldInfo thingOwnerField;
        private readonly FieldInfo roleField;
        private readonly PropertyInfo areSlotsAvailableProperty;

        private readonly Type roleType;
        private readonly FieldInfo roleLabelField;
        private readonly PropertyInfo roleSlotsProperty;

        private readonly Type tabHelperType;
        private readonly MethodInfo handleDragEventMethod;
        private readonly FieldInfo draggedPawnField;
        private readonly FieldInfo transferToHolderField;
        private readonly FieldInfo hoveringOverPawnField;

        private readonly bool ready;
        internal InspectTabBase sharedTab;

        public override bool Ready => ready;

        public VfPassengersTabAdapter()
        {
            var surface = new ReflectionSurface("VfPassengersTabAdapter");

            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            roleHandlerType = surface.Type("Vehicles.VehicleRoleHandler");
            roleType = surface.Type("Vehicles.VehicleRole");
            tabHelperType = surface.Type("Vehicles.VehicleTabHelper_Passenger");

            handlersField = surface.Field(vehiclePawnType, "handlers");
            allInventoryPawnsProperty = surface.Property(vehiclePawnType, "AllInventoryPawns");

            thingOwnerField = surface.Field(roleHandlerType, "thingOwner");
            roleField = surface.Field(roleHandlerType, "role");
            areSlotsAvailableProperty = surface.Property(roleHandlerType, "AreSlotsAvailable");

            roleLabelField = surface.Field(roleType, "label");
            roleSlotsProperty = surface.Property(roleType, "Slots");

            handleDragEventMethod = surface.Method(tabHelperType, "HandleDragEvent");
            draggedPawnField = surface.Field(tabHelperType, "draggedPawn");
            transferToHolderField = surface.Field(tabHelperType, "transferToHolder");
            hoveringOverPawnField = surface.Field(tabHelperType, "hoveringOverPawn");

            ready = surface.Ready;
        }

        // Stable English dispatch token (l10n-exempt: never displayed raw —
        // DisplayName/CategoryDisplayName below render VF's own tab label).
        public override string CategoryKey => "VF Passengers";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        // The tab's own labelKey, the word a sighted player reads on the tab strip.
        private static string TabLabel => CompatText.ResolveOrFallback("VF_TabPassengers", "RimWorldAccess.Compat.Vf.PassengersTab");

        public override string DisplayName(InspectTabBase tab) => TabLabel;

        public override string CategoryDisplayName(object obj) => TabLabel;

        public override bool CanExpand(object obj)
        {
            // Not gated on Vehicle.beached: the tab's own IsVisible, which the dynamic
            // tab-discovery layer already consults, is what hides the category while beached.
            return ready && obj is Pawn p && vehiclePawnType.IsInstanceOfType(p);
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;
            if (!(obj is Pawn vehicle) || !vehiclePawnType.IsInstanceOfType(vehicle))
                return;

            try
            {
                BuildAllSections(categoryItem, vehicle, mode);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPassengersTabAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears and rebuilds the whole category after a move or swap — seat counts and
        /// every other pawn's row-set can have shifted, not just the moved pawn's row.
        /// Routed through the framework so the extender and parity-capture passes survive
        /// the rebuild.
        /// </summary>
        private void RebuildCategory(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, obj, mode, this, sharedTab);
        }

        // ---- Tree body ----

        private void BuildAllSections(InspectionTreeItem categoryItem, Pawn vehicle, InspectionMode mode)
        {
            IList handlers = handlersField.GetValue(vehicle) as IList;
            if (handlers != null)
            {
                foreach (object handler in handlers)
                    BuildHandlerSection(categoryItem, vehicle, handlers, handler, mode);
            }
            BuildCargoSection(categoryItem, vehicle, handlers, mode);
        }

        private void BuildHandlerSection(InspectionTreeItem categoryItem, Pawn vehicle, IList handlers, object handler, InspectionMode mode)
        {
            object role = roleField.GetValue(handler);
            string roleLabel = roleLabelField.GetValue(role) as string ?? "";
            ThingOwner<Pawn> thingOwner = thingOwnerField.GetValue(handler) as ThingOwner<Pawn>;
            int count = thingOwner?.Count ?? 0;
            int slots = (int)roleSlotsProperty.GetValue(role);

            string label = "RimWorldAccess.Compat.Vf.PassengersRoleSection".Translate(roleLabel, count, slots);
            if (!(bool)areSlotsAvailableProperty.GetValue(handler))
                label += "RimWorldAccess.Compat.Vf.SeatsRoleFull".Translate();

            InspectionTreeItem sectionItem = InspectNodeFactory.Section(categoryItem, label, handler, secItem =>
            {
                ThingOwner<Pawn> liveOwner = thingOwnerField.GetValue(handler) as ThingOwner<Pawn>;
                if (liveOwner == null || liveOwner.Count == 0)
                {
                    InspectNodeFactory.DetailLine(secItem, "RimWorldAccess.Compat.Vf.PassengersNoneAboard".Translate());
                    return;
                }
                foreach (Pawn p in liveOwner)
                    BuildPawnRow(categoryItem, secItem, vehicle, handlers, handler, p, mode);
            });
            sectionItem.IsSectionHeader = true;
        }

        private void BuildCargoSection(InspectionTreeItem categoryItem, Pawn vehicle, IList handlers, InspectionMode mode)
        {
            List<Pawn> cargoPawns = allInventoryPawnsProperty.GetValue(vehicle) as List<Pawn> ?? new List<Pawn>();
            string label = "RimWorldAccess.Compat.Vf.PassengersCargoSection".Translate(cargoPawns.Count);

            InspectionTreeItem sectionItem = InspectNodeFactory.Section(categoryItem, label, vehicle.inventory, secItem =>
            {
                List<Pawn> liveCargo = allInventoryPawnsProperty.GetValue(vehicle) as List<Pawn> ?? new List<Pawn>();
                if (liveCargo.Count == 0)
                {
                    InspectNodeFactory.DetailLine(secItem, "RimWorldAccess.Compat.Vf.PassengersNoneAboard".Translate());
                    return;
                }
                foreach (Pawn p in liveCargo)
                    BuildPawnRow(categoryItem, secItem, vehicle, handlers, vehicle.inventory, p, mode);
            });
            sectionItem.IsSectionHeader = true;
        }

        private void BuildPawnRow(InspectionTreeItem categoryItem, InspectionTreeItem sectionParent, Pawn vehicle,
            IList handlers, object currentHolder, Pawn pawn, InspectionMode mode)
        {
            string label = pawn.LabelShortCap;
            if (pawn.Downed)
                label = "RimWorldAccess.Compat.Vf.PassengersDowned".Translate(label);

            InspectNodeFactory.Section(sectionParent, label, pawn, pawnItem =>
                BuildPawnChildren(categoryItem, pawnItem, vehicle, handlers, currentHolder, pawn, mode));
        }

        private void BuildPawnChildren(InspectionTreeItem categoryItem, InspectionTreeItem pawnItem, Pawn vehicle,
            IList handlers, object currentHolder, Pawn pawn, InspectionMode mode)
        {
            BuildNeedsLines(pawnItem, pawn);

            if (!pawn.Dead && pawn.needs?.mood != null)
                BuildMoodSection(pawnItem, pawn);

            InspectionTreeItem infoCardItem = InspectNodeFactory.ActionRow(
                pawnItem, ConceptDefOf.InfoCard.label.CapitalizeFirst(), pawn, null);
            infoCardItem.OnActivate = () => Find.WindowStack.Add(new Dialog_InfoCard(pawn));

            if (mode != InspectionMode.ReadOnly)
                BuildMoveActions(categoryItem, pawnItem, vehicle, handlers, currentHolder, pawn, mode);
        }

        /// <summary>
        /// The needs list the pawn row itself draws: filtered to showForCaravanMembers (NOT the
        /// Needs tab's own showOnNeedList — this mirrors VehicleTabHelper_Passenger.DoRow's
        /// filter). Deliberately the ONLY needs listing for this pawn; the Mood section adds
        /// mood/thought detail on top rather than repeating a differently-filtered second list.
        /// </summary>
        private static void BuildNeedsLines(InspectionTreeItem pawnItem, Pawn pawn)
        {
            if (pawn.needs?.AllNeeds == null)
                return;
            List<Need> needs = pawn.needs.AllNeeds.Where(n => n.def.showForCaravanMembers).ToList();
            PawnNeedsUIUtility.SortInDisplayOrder(needs);
            foreach (Need need in needs)
            {
                float pct = need.CurLevelPercentage * 100f;
                InspectNodeFactory.DetailLine(pawnItem, $"{need.LabelCap}: {pct:F0}%");
            }
        }

        /// <summary>
        /// Mirrors NeedsCardUtility.DoNeedsMoodAndThoughts' mood panel: the mood percentage/state
        /// line, the thought listing, then the break-thresholds sub-node (see class remarks for the
        /// CanDoRandomMentalBreaks gating deviation).
        /// </summary>
        private static void BuildMoodSection(InspectionTreeItem pawnItem, Pawn pawn)
        {
            InspectNodeFactory.Section(pawnItem, "RimWorldAccess.Compat.Vf.PassengersMoodSection".Translate(), pawn, moodItem =>
            {
                Need_Mood mood = pawn.needs.mood;
                float pct = mood.CurLevelPercentage * 100f;
                InspectNodeFactory.DetailLine(moodItem, $"{mood.LabelCap}: {pct:F0}%. {mood.MoodString}");

                PawnMoodAdapter.BuildThoughtRows(moodItem, mood);

                if (pawn.mindState?.mentalBreaker != null && pawn.mindState.mentalBreaker.CanDoRandomMentalBreaks)
                    PawnMoodAdapter.BuildBreakThresholdsChildren(moodItem, pawn);
            });
        }

        /// <summary>
        /// The keyboard-reachable equivalent of dragging the pawn's row onto another section.
        /// Every row is offered whenever it COULD succeed (an open slot, or simply "not already in
        /// cargo") — HandleDragEvent's own live gate accepts/cautions/rejects.
        /// </summary>
        private void BuildMoveActions(InspectionTreeItem categoryItem, InspectionTreeItem pawnItem, Pawn vehicle,
            IList handlers, object currentHolder, Pawn pawn, InspectionMode mode)
        {
            if (handlers != null)
            {
                foreach (object otherHandler in handlers)
                {
                    if (ReferenceEquals(otherHandler, currentHolder))
                        continue;

                    object otherRole = roleField.GetValue(otherHandler);
                    string otherRoleLabel = roleLabelField.GetValue(otherRole) as string ?? "";
                    bool hasOpenSlot = (bool)areSlotsAvailableProperty.GetValue(otherHandler);

                    if (hasOpenSlot)
                    {
                        InspectionTreeItem moveItem = InspectNodeFactory.ActionRow(pawnItem,
                            "RimWorldAccess.Compat.Vf.PassengersMoveTo".Translate(otherRoleLabel), otherHandler, null);
                        moveItem.OnActivate = () =>
                            TryMove(categoryItem, vehicle, mode, pawn, otherHandler, null, otherRoleLabel);
                    }
                    else if (roleHandlerType.IsInstanceOfType(currentHolder))
                    {
                        // Cargo-held pawns get no swap rows: VF's swap branch only fires when
                        // BOTH holders are role handlers, so a swap row here would always reject.
                        ThingOwner<Pawn> occupants = thingOwnerField.GetValue(otherHandler) as ThingOwner<Pawn>;
                        if (occupants == null)
                            continue;
                        foreach (Pawn occupant in occupants)
                        {
                            string swapLabel = "RimWorldAccess.Compat.Vf.PassengersSwapWith".Translate(occupant.LabelShortCap, otherRoleLabel);
                            InspectionTreeItem swapItem = InspectNodeFactory.ActionRow(pawnItem, swapLabel, otherHandler, null);
                            swapItem.OnActivate = () =>
                                TryMove(categoryItem, vehicle, mode, pawn, otherHandler, occupant, otherRoleLabel);
                        }
                    }
                }
            }

            if (!(currentHolder is Pawn_InventoryTracker))
            {
                InspectionTreeItem cargoItem = InspectNodeFactory.ActionRow(
                    pawnItem, "RimWorldAccess.Compat.Vf.PassengersMoveToCargo".Translate(), vehicle.inventory, null);
                string cargoDestinationLabel = "RimWorldAccess.Compat.Vf.PassengersCargo".Translate();
                cargoItem.OnActivate = () =>
                    TryMove(categoryItem, vehicle, mode, pawn, vehicle.inventory, null, cargoDestinationLabel);
            }
        }

        /// <summary>
        /// Runs one move/swap attempt through <see cref="InvokeDragDrop"/>. Success rebuilds the
        /// whole category (see <see cref="RebuildCategory"/>) and speaks one confirmation; failure
        /// speaks nothing further, since VF's own Messages.Message already spoke the
        /// rejection/caution.
        /// </summary>
        private void TryMove(InspectionTreeItem categoryItem, Pawn vehicle, InspectionMode mode, Pawn pawn,
            object targetHolder, Pawn swapWith, string destinationLabel)
        {
            try
            {
                if (vehicle == null || vehicle.Destroyed || pawn == null || pawn.Destroyed || targetHolder == null)
                    return;

                bool moved = InvokeDragDrop(pawn, targetHolder, swapWith);
                if (!moved)
                    return;

                RebuildCategory(categoryItem, vehicle, mode);

                if (swapWith != null)
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.PassengersSwapped".Loc(pawn.LabelShortCap, swapWith.LabelShortCap));
                else
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.PassengersMoved".Loc(pawn.LabelShortCap, destinationLabel));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfPassengersTabAdapter move action failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Drives VF's own drag-drop handler (vehicle A). HandleDragEvent only acts on a left
        /// mouse-up, so the event type/button are masked and restored around the call. All gates,
        /// messages, sounds, seat events, world-pawn bookkeeping, and caravan recaching run inside
        /// VF. For a swap, <paramref name="targetHolder"/> is the full handler and
        /// <paramref name="swapWith"/> is its occupant; for a plain move it is null.
        /// </summary>
        private bool InvokeDragDrop(Pawn pawn, object targetHolder, Pawn swapWith)
        {
            Event cur = Event.current;
            if (cur == null)
                return false; // outside OnGUI; nothing safe to do

            object before = pawn.ParentHolder;
            EventType savedType = cur.type;
            int savedButton = cur.button;
            try
            {
                // Vehicle A: stages input for VF's own VehicleTabHelper_Passenger.HandleDragEvent.
                // MUTATION-C: primes the drag-drop state the vanilla handler reads below; not a direct game-state write.
                draggedPawnField.SetValue(null, pawn);
                transferToHolderField.SetValue(null, targetHolder);
                hoveringOverPawnField.SetValue(null, swapWith);

                cur.type = EventType.MouseUp;
                cur.button = 0;
                handleDragEventMethod.Invoke(null, null);
            }
            finally
            {
                cur.type = savedType;
                cur.button = savedButton;
                // HandleDragEvent already clears draggedPawn on every path (its own try/finally).
                // MUTATION-C: these three cover our own early-return/exception paths with the same non-write staging as above.
                draggedPawnField.SetValue(null, null);
                transferToHolderField.SetValue(null, null);
                hoveringOverPawnField.SetValue(null, null);
            }

            // Swap success moves both pawns too, so comparing the dragged pawn's own
            // holder before/after covers both shapes.
            return !ReferenceEquals(pawn.ParentHolder, before);
        }
    }
}
