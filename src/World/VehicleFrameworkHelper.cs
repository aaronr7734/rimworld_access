using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// Optional reflection bridge for Vehicle Framework. Keeps RimWorld Access buildable
    /// and usable when Vehicle Framework is not installed.
    /// </summary>
    public static class VehicleFrameworkHelper
    {
        private const int VehicleFrameworkVehiclesTab = 10;

        private static Type vehiclePawnType;
        private static Type formCaravanPatchType;
        private static Type vehicleStatDefOfType;
        private static Type caravanHelperType;
        private static Type assignedSeatType;
        private static FieldInfo selectedTabField;
        private static FieldInfo assignedSeatsField;

        public static bool IsVehiclePawn(Thing thing)
        {
            if (thing == null)
                return false;

            Type type = GetVehiclePawnType();
            if (type != null)
                return type.IsInstanceOfType(thing);

            return thing.GetType().FullName == "Vehicles.VehiclePawn";
        }

        public static bool HasVehicleTransferables(List<TransferableOneWay> transferables)
        {
            if (transferables == null)
                return false;

            foreach (TransferableOneWay transferable in transferables)
            {
                if (IsVehiclePawn(transferable?.AnyThing))
                    return true;
            }
            return false;
        }

        public static void SyncFormCaravanTab(int vanillaTabValue, bool vehiclesTab)
        {
            FieldInfo field = GetSelectedTabField();
            if (field == null)
                return;

            field.SetValue(null, vehiclesTab ? VehicleFrameworkVehiclesTab : vanillaTabValue);
        }

        public static string BuildVehicleAnnouncement(TransferableOneWay transferable)
        {
            Thing vehicle = transferable?.AnyThing;
            if (!IsVehiclePawn(vehicle))
                return null;

            StringBuilder sb = new StringBuilder();
            sb.Append(vehicle.LabelCap.ToString().StripTags());
            if (transferable.CountToTransfer > 0)
                sb.Append(" - checked");

            string disabledReason = GetVehicleDisabledReason(vehicle);
            if (!disabledReason.NullOrEmpty())
                sb.Append($". Warning: {disabledReason}");

            string orientation = GetVehicleOrientationSummary(vehicle);
            if (!orientation.NullOrEmpty())
                sb.Append($". {orientation}");

            float speed = GetVehicleTilesPerDay(vehicle);
            if (speed > 0f)
                sb.Append($". Speed: {speed:F1} tiles per day");

            float cargoCapacity = GetVehicleStat(vehicle, "CargoCapacity");
            if (cargoCapacity > 0f)
                sb.Append($". Cargo capacity: {cargoCapacity:F1} kg");

            string operators = GetOperatorSummary(vehicle);
            if (!operators.NullOrEmpty())
                sb.Append($". {operators}");

            string fuel = GetFuelSummary(vehicle);
            if (!fuel.NullOrEmpty())
                sb.Append($". {fuel}");

            return sb.ToString();
        }

        public static IEnumerable<string> BuildVehicleSummaryItems(List<TransferableOneWay> transferables)
        {
            if (transferables == null)
                yield break;

            int selected = 0;
            int withFuel = 0;
            float lowestFuelDays = float.MaxValue;
            Thing limitingVehicle = null;
            List<string> warnings = new List<string>();

            foreach (TransferableOneWay transferable in transferables)
            {
                Thing vehicle = transferable?.AnyThing;
                if (!IsVehiclePawn(vehicle) || transferable.CountToTransfer <= 0)
                    continue;

                selected++;

                string disabledReason = GetVehicleDisabledReason(vehicle);
                if (!disabledReason.NullOrEmpty())
                    warnings.Add($"{vehicle.LabelShort}: {disabledReason}");

                string operatorWarning = GetOperatorWarning(vehicle);
                if (!operatorWarning.NullOrEmpty())
                    warnings.Add($"{vehicle.LabelShort}: {operatorWarning}");

                object fuelComp = GetCompFueledTravel(vehicle);
                if (fuelComp != null)
                {
                    withFuel++;
                    float days = GetFuelDays(fuelComp);
                    if (days >= 0f && days < lowestFuelDays)
                    {
                        lowestFuelDays = days;
                        limitingVehicle = vehicle;
                    }
                    if (GetFloatProperty(fuelComp, "Fuel") <= 0f)
                        warnings.Add($"{vehicle.LabelShort}: out of fuel");
                }
            }

            if (selected == 0)
                yield break;

            yield return selected == 1 ? "Vehicles: 1 selected" : $"Vehicles: {selected} selected";

            if (withFuel > 0)
            {
                if (lowestFuelDays == float.MaxValue)
                {
                    yield return "Vehicle fuel: available";
                }
                else if (limitingVehicle != null)
                {
                    yield return $"Vehicle fuel: limiting vehicle {limitingVehicle.LabelShort}, about {lowestFuelDays:F1} travel days";
                }
                else
                {
                    yield return $"Vehicle fuel: about {lowestFuelDays:F1} travel days";
                }
            }

            foreach (string warning in warnings)
                yield return $"Vehicle warning: {warning}";
        }

        public static bool TrySelectAndDraftVehicleAtCursor(IntVec3 cursorPosition, Map map, out string message)
        {
            message = null;
            Thing vehicle = FindVehicleAtCursor(cursorPosition, map);
            if (vehicle == null)
            {
                message = "No vehicle at cursor";
                return false;
            }

            Find.Selector.ClearSelection();
            if (vehicle is ISelectable selectable)
                Find.Selector.Select(selectable, playSound: true, forceDesignatorDeselect: false);

            string draftMessage;
            bool drafted = TrySetVehicleDrafted(vehicle, true, out draftMessage);
            string orientation = GetVehicleOrientationSummary(vehicle);
            string orientationPart = orientation.NullOrEmpty() ? "" : $". {orientation}";
            message = drafted
                ? $"{vehicle.LabelShort} selected and drafted{orientationPart}"
                : $"{vehicle.LabelShort} selected. {draftMessage}{orientationPart}";
            return drafted;
        }

        public static bool TryOrderSelectedVehicleToCursor(IntVec3 cursorPosition, Map map, out string message)
        {
            message = null;
            Thing vehicle = GetSelectedVehicle();
            if (vehicle == null)
            {
                message = "No vehicle selected. Move cursor to a vehicle and press Alt+V first.";
                return false;
            }

            if (map == null || !cursorPosition.IsValid || !cursorPosition.InBounds(map))
            {
                message = "Invalid destination";
                return false;
            }

            if (!TrySetVehicleDrafted(vehicle, true, out message))
                return false;

            IntVec3 destination;
            if (!TryFindNearestStandableVehicleCell(vehicle, cursorPosition, out destination))
            {
                message = $"{vehicle.LabelShort} cannot fit near cursor";
                return false;
            }

            if (!CanVehicleReach(vehicle, destination))
            {
                message = $"{vehicle.LabelShort} cannot reach cursor";
                return false;
            }

            if (!TryOrderVehicleMove(vehicle, cursorPosition, destination, out message))
                return false;

            message = $"{vehicle.LabelShort}: moving to {destination.x}, {destination.z}";
            return true;
        }

        public static bool TryBoardSelectedPawnsIntoVehicleAtCursor(IntVec3 cursorPosition, Map map, out string message)
        {
            message = null;
            Thing vehicle = FindVehicleAtCursor(cursorPosition, map);
            if (vehicle == null)
            {
                message = "No vehicle at cursor";
                return false;
            }

            if (Find.Selector == null || !Find.Selector.SelectedPawns.Any())
            {
                message = "No pawn selected";
                return false;
            }

            MethodInfo prompt = AccessTools.Method(vehicle.GetType(), "PromptToBoardVehicle");
            if (prompt == null)
            {
                message = "Vehicle boarding command is unavailable";
                return false;
            }

            int ordered = 0;
            List<string> failures = new List<string>();
            foreach (Pawn pawn in Find.Selector.SelectedPawns)
            {
                if (pawn == null || IsVehiclePawn(pawn))
                    continue;

                object handler = FindBoardingHandler(vehicle, pawn);
                if (handler == null)
                {
                    failures.Add($"{pawn.LabelShort}: no compatible seat");
                    continue;
                }

                try
                {
                    prompt.Invoke(vehicle, new[] { pawn, handler });
                    ordered++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{pawn.LabelShort}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }

            if (ordered > 0)
            {
                message = ordered == 1
                    ? $"Boarding order issued for {vehicle.LabelShort}"
                    : $"Boarding orders issued for {ordered} pawns";
                return true;
            }

            message = failures.Count > 0 ? string.Join(". ", failures) : "No compatible selected pawns";
            return false;
        }

        public static void ClearVehicleAssignments(Thing vehicle)
        {
            if (!IsVehiclePawn(vehicle))
                return;

            object assignments = GetAssignedSeats();
            if (assignments == null)
                return;

            MethodInfo remove = assignments.GetType().GetMethod("RemoveAssignments");
            try
            {
                remove?.Invoke(assignments, new object[] { vehicle });
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to clear vehicle assignments: {ex.Message}");
            }
        }

        public static bool AutoAssignVehicleSeats(
            TransferableOneWay vehicleTransferable,
            List<TransferableOneWay> transferables,
            out string resultMessage)
        {
            resultMessage = null;

            Thing vehicle = vehicleTransferable?.AnyThing;
            if (!IsVehiclePawn(vehicle))
            {
                resultMessage = "No vehicle selected";
                return false;
            }

            object assignments = GetAssignedSeats();
            Type assignedSeat = GetAssignedSeatType();
            if (assignments == null || assignedSeat == null)
            {
                resultMessage = "Vehicle Framework seat assignment is unavailable";
                return false;
            }

            List<TransferableOneWay> pawns = new List<TransferableOneWay>();
            foreach (TransferableOneWay transferable in transferables)
            {
                if (transferable?.AnyThing is Pawn && !IsVehiclePawn(transferable.AnyThing))
                    pawns.Add(transferable);
            }

            try
            {
                ResetAssignmentsForVehicle(vehicle, pawns);

                int assignedCount = AssignRequiredOperators(vehicle, pawns, assignments, assignedSeat);
                int required = GetIntProperty(vehicle, "PawnCountToOperate", 0);
                if (required > 0 && assignedCount < required)
                {
                    vehicleTransferable.AdjustTo(0);
                    resultMessage = $"Could not auto-assign required operators for {vehicle.LabelShort}. Operators assigned: {assignedCount} of {required}";
                    return false;
                }

                vehicleTransferable.AdjustTo(vehicleTransferable.GetMaximumToTransfer());
                resultMessage = $"Auto-assigned required operators for {vehicle.LabelShort}. {GetOperatorSummary(vehicle)}";
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to auto-assign required vehicle operators: {ex}");
                resultMessage = $"Could not auto-assign required operators for {vehicle.LabelShort}";
                return false;
            }
        }

        public static int GetVehicleRoleCount(Thing vehicle)
        {
            int count = 0;
            foreach (object handler in GetVehicleHandlers(vehicle))
                count++;
            return count;
        }

        public static string GetVehicleRoleSummary(Thing vehicle, int roleIndex)
        {
            object handler = GetVehicleHandler(vehicle, roleIndex);
            if (handler == null)
                return "No role";

            object role = AccessTools.Field(handler.GetType(), "role")?.GetValue(handler);
            string label = GetRoleLabel(role);
            int slots = GetIntProperty(role, "Slots", 0);
            int required = GetIntProperty(role, "SlotsToOperate", 0);
            int assigned = CountAssignedToHandler(vehicle, handler);
            string kind = IsMovementHandler(handler) ? "operator" : "passenger";

            StringBuilder sb = new StringBuilder();
            sb.Append($"{label}, {kind} role, assigned {assigned} of {slots}");
            if (required > 0)
                sb.Append($", required {required}");

            List<string> names = GetAssignedPawnNamesForHandler(vehicle, handler);
            if (names.Count > 0)
                sb.Append($". Assigned: {string.Join(", ", names)}");

            return sb.ToString();
        }

        public static string GetPawnRoleCompatibility(Thing vehicle, int roleIndex, Pawn pawn)
        {
            object handler = GetVehicleHandler(vehicle, roleIndex);
            if (handler == null || pawn == null)
                return "Unavailable";

            if (IsPawnAssignedElsewhere(pawn, vehicle))
                return "assigned to another vehicle";

            if (!CanOperateHandler(handler, pawn))
                return "cannot operate this role";

            return "available";
        }

        public static bool CanShowPawnForVehicleRole(Thing vehicle, int roleIndex, Pawn pawn)
        {
            object handler = GetVehicleHandler(vehicle, roleIndex);
            if (handler == null || pawn == null)
                return false;

            if (IsPawnAssignedElsewhere(pawn, vehicle))
                return false;

            return CanOperateHandler(handler, pawn);
        }

        public static bool AssignPawnToVehicleRole(
            TransferableOneWay vehicleTransferable,
            int roleIndex,
            TransferableOneWay pawnTransferable,
            List<TransferableOneWay> allTransferables,
            out string resultMessage)
        {
            resultMessage = null;

            Thing vehicle = vehicleTransferable?.AnyThing;
            Pawn pawn = pawnTransferable?.AnyThing as Pawn;
            object handler = GetVehicleHandler(vehicle, roleIndex);
            if (!IsVehiclePawn(vehicle) || pawn == null || handler == null)
            {
                resultMessage = "Cannot assign this pawn";
                return false;
            }

            if (IsPawnAssignedElsewhere(pawn, vehicle))
            {
                resultMessage = $"{pawn.LabelShortCap} is assigned to another vehicle";
                return false;
            }

            if (!CanOperateHandler(handler, pawn))
            {
                resultMessage = $"{pawn.LabelShortCap} cannot operate this role";
                return false;
            }

            if (CountAssignedToHandler(vehicle, handler) >= GetHandlerSlots(handler))
            {
                resultMessage = $"{GetRoleLabelForHandler(handler)} is full";
                return false;
            }

            object assignments = GetAssignedSeats();
            Type assignedSeat = GetAssignedSeatType();
            ConstructorInfo seatCtor = assignedSeat?.GetConstructor(new[] { typeof(Pawn), GetVehicleRoleHandlerType() });
            MethodInfo setAssignment = assignments?.GetType().GetMethod("SetAssignment");
            if (assignments == null || seatCtor == null || setAssignment == null)
            {
                resultMessage = "Vehicle Framework seat assignment is unavailable";
                return false;
            }

            object seat = seatCtor.Invoke(new object[] { pawn, handler });
            setAssignment.Invoke(assignments, new[] { seat });

            pawnTransferable.ForceTo(1);
            vehicleTransferable.AdjustTo(vehicleTransferable.GetMaximumToTransfer());
            ClearDuplicatePawnSelections(pawn, allTransferables, pawnTransferable);

            resultMessage = $"{pawn.LabelShortCap} assigned to {GetRoleLabelForHandler(handler)}. {GetOperatorSummary(vehicle)}";
            return true;
        }

        public static bool EnsureAssignedPawnsSelected(List<TransferableOneWay> transferables)
        {
            if (transferables == null)
                return false;

            object assignments = GetAssignedSeats();
            object allAssignments = AccessTools.Property(assignments?.GetType(), "AllAssignments")?.GetValue(assignments, null);
            System.Collections.IDictionary dictionary = allAssignments as System.Collections.IDictionary;
            if (dictionary == null)
                return false;

            bool changed = false;
            foreach (object key in dictionary.Keys)
            {
                Pawn pawn = key as Pawn;
                if (pawn == null)
                    continue;

                TransferableOneWay transferable = transferables.Find(t => t.AnyThing == pawn);
                if (transferable != null && transferable.CountToTransfer <= 0)
                {
                    transferable.ForceTo(1);
                    changed = true;
                }
            }

            return changed;
        }

        public static bool SyncVehicleRoute(Dialog_FormCaravan dialog, PlanetTile destinationTile)
        {
            if (dialog == null || !destinationTile.Valid || !HasVehicleTransferables(dialog.transferables))
                return false;

            try
            {
                object formation = GetVehicleCaravanFormation();
                if (formation == null)
                    return false;

                object vehicleDefs = BuildSelectedVehicleDefList(dialog.transferables);
                if (vehicleDefs == null)
                    return false;

                Map map = AccessTools.Field(typeof(Dialog_FormCaravan), "map")?.GetValue(dialog) as Map ?? Find.CurrentMap;
                if (map == null)
                    return false;

                Type caravanHelper = GetCaravanHelperType();
                MethodInfo bestExit = AccessTools.Method(caravanHelper, "BestExitTileToGoTo");
                if (bestExit == null)
                    return false;

                object startingTile = bestExit.Invoke(null, new object[] { vehicleDefs, destinationTile, map });
                SetPropertyOrField(formation, "DestinationTile", destinationTile);
                SetPropertyOrField(formation, "StartingTile", startingTile);
                SetPropertyOrField(formation, "TicksToArriveDirty", true);
                SetPropertyOrField(formation, "DaysWorthOfFoodDirty", true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to sync Vehicle Framework route: {ex.Message}");
                return false;
            }
        }

        public static void PrepareVehicleSend(Dialog_FormCaravan dialog)
        {
            if (dialog == null || !HasVehicleTransferables(dialog.transferables))
                return;

            try
            {
                bool changed = EnsureAssignedPawnsSelected(dialog.transferables);
                if (changed)
                    NotifyTransferablesChanged(dialog);

                SetDialogDirty(dialog);
                PlanetTile destination = GetDialogDestinationTile(dialog);
                SyncVehicleRoute(dialog, destination);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to prepare Vehicle Framework caravan send: {ex.Message}");
            }
        }

        public static void PostprocessVehicleFormationRecache(object formationInfo)
        {
            if (formationInfo == null)
                return;

            try
            {
                Dialog_FormCaravan dialog = GetProperty(formationInfo, "Dialog") as Dialog_FormCaravan;
                if (dialog == null || !HasVehicleTransferables(dialog.transferables))
                    return;

                EnsureAssignedPawnsInFormation(formationInfo, dialog);
                PlanetTile destination = GetDialogDestinationTile(dialog);
                SyncVehicleRoute(dialog, destination);
                EnsureReachableVehicleStartingTile(formationInfo, dialog);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to postprocess Vehicle Framework formation recache: {ex.Message}");
            }
        }

        public static bool RemoveLastAssignmentFromVehicleRole(
            Thing vehicle,
            int roleIndex,
            List<TransferableOneWay> allTransferables,
            out string resultMessage)
        {
            resultMessage = null;

            object handler = GetVehicleHandler(vehicle, roleIndex);
            if (handler == null)
            {
                resultMessage = "No role selected";
                return false;
            }

            Pawn pawn = GetLastAssignedPawnForHandler(vehicle, handler);
            if (pawn == null)
            {
                resultMessage = "No pawn assigned to this role";
                return false;
            }

            object assignments = GetAssignedSeats();
            MethodInfo removeAssignment = assignments?.GetType().GetMethod("RemoveAssignment");
            if (removeAssignment == null)
            {
                resultMessage = "Vehicle Framework seat assignment is unavailable";
                return false;
            }

            removeAssignment.Invoke(assignments, new object[] { pawn });

            TransferableOneWay pawnTransferable = allTransferables?.Find(t => t.AnyThing == pawn);
            if (pawnTransferable != null)
                pawnTransferable.ForceTo(0);

            resultMessage = $"{pawn.LabelShortCap} removed from {GetRoleLabelForHandler(handler)}";
            return true;
        }

        public static bool ClearVehicleSeatAssignments(
            Thing vehicle,
            List<TransferableOneWay> allTransferables,
            out string resultMessage)
        {
            resultMessage = null;
            if (!IsVehiclePawn(vehicle))
            {
                resultMessage = "No vehicle selected";
                return false;
            }

            foreach (object seat in GetAssignmentsForVehicle(vehicle))
            {
                Pawn pawn = AccessTools.Field(seat.GetType(), "pawn")?.GetValue(seat) as Pawn;
                TransferableOneWay pawnTransferable = allTransferables?.Find(t => t.AnyThing == pawn);
                if (pawnTransferable != null)
                    pawnTransferable.ForceTo(0);
            }

            ClearVehicleAssignments(vehicle);
            resultMessage = $"Cleared assignments for {vehicle.LabelShort}";
            return true;
        }

        private static string GetVehicleDisabledReason(Thing vehicle)
        {
            bool canMove = GetBoolProperty(vehicle, "CanMove", true);
            if (!canMove)
                return "cannot move";

            object fuelComp = GetCompFueledTravel(vehicle);
            if (fuelComp != null && GetBoolProperty(fuelComp, "EmptyTank", false))
                return "out of fuel";

            return null;
        }

        private static void ResetAssignmentsForVehicle(Thing vehicle, List<TransferableOneWay> pawnTransferables)
        {
            object assignments = GetAssignedSeats();
            if (assignments == null)
                return;

            foreach (TransferableOneWay transferable in pawnTransferables)
            {
                if (IsPawnAssignedToVehicle(transferable.AnyThing as Pawn, vehicle))
                    transferable.ForceTo(0);
            }

            MethodInfo remove = assignments.GetType().GetMethod("RemoveAssignments");
            remove?.Invoke(assignments, new object[] { vehicle });
        }

        private static int AssignRequiredOperators(
            Thing vehicle,
            List<TransferableOneWay> pawnTransferables,
            object assignments,
            Type assignedSeat)
        {
            MethodInfo setAssignment = assignments.GetType().GetMethod("SetAssignment");
            ConstructorInfo seatCtor = assignedSeat.GetConstructor(new[] { typeof(Pawn), GetVehicleRoleHandlerType() });
            if (setAssignment == null || seatCtor == null)
                return 0;

            foreach (object handler in GetVehicleHandlers(vehicle))
            {
                if (!IsMovementHandler(handler))
                    continue;

                int needed = GetHandlerSlotsToOperate(handler) - CountAssignedToHandler(vehicle, handler);
                while (needed > 0)
                {
                    TransferableOneWay pawnTransferable = FindBestOperator(handler, pawnTransferables, vehicle);
                    if (pawnTransferable == null)
                        return GetAssignedOperatorCount(vehicle);

                    object seat = seatCtor.Invoke(new object[] { pawnTransferable.AnyThing as Pawn, handler });
                    setAssignment.Invoke(assignments, new[] { seat });
                    pawnTransferable.ForceTo(1);
                    needed--;
                }
            }

            return GetAssignedOperatorCount(vehicle);
        }

        private static object GetVehicleHandler(Thing vehicle, int roleIndex)
        {
            if (!IsVehiclePawn(vehicle) || roleIndex < 0)
                return null;

            int index = 0;
            foreach (object handler in GetVehicleHandlers(vehicle))
            {
                if (index == roleIndex)
                    return handler;
                index++;
            }
            return null;
        }

        private static int GetHandlerSlots(object handler)
        {
            object role = AccessTools.Field(handler?.GetType(), "role")?.GetValue(handler);
            return role == null ? 0 : GetIntProperty(role, "Slots", 0);
        }

        private static string GetRoleLabelForHandler(object handler)
        {
            object role = AccessTools.Field(handler?.GetType(), "role")?.GetValue(handler);
            return GetRoleLabel(role);
        }

        private static string GetRoleLabel(object role)
        {
            if (role == null)
                return "Unknown role";

            FieldInfo labelField = AccessTools.Field(role.GetType(), "label");
            string label = labelField?.GetValue(role) as string;
            return label.NullOrEmpty() ? "Unnamed role" : label.StripTags();
        }

        private static List<string> GetAssignedPawnNamesForHandler(Thing vehicle, object handler)
        {
            List<string> names = new List<string>();
            foreach (object seat in GetAssignmentsForVehicle(vehicle))
            {
                object seatHandler = AccessTools.Field(seat.GetType(), "handler")?.GetValue(seat);
                if (!ReferenceEquals(seatHandler, handler))
                    continue;

                Pawn pawn = AccessTools.Field(seat.GetType(), "pawn")?.GetValue(seat) as Pawn;
                if (pawn != null)
                    names.Add(pawn.LabelShortCap.ToString().StripTags());
            }
            return names;
        }

        private static Pawn GetLastAssignedPawnForHandler(Thing vehicle, object handler)
        {
            Pawn found = null;
            foreach (object seat in GetAssignmentsForVehicle(vehicle))
            {
                object seatHandler = AccessTools.Field(seat.GetType(), "handler")?.GetValue(seat);
                if (!ReferenceEquals(seatHandler, handler))
                    continue;

                found = AccessTools.Field(seat.GetType(), "pawn")?.GetValue(seat) as Pawn;
            }
            return found;
        }

        private static void ClearDuplicatePawnSelections(
            Pawn pawn,
            List<TransferableOneWay> allTransferables,
            TransferableOneWay keep)
        {
            if (pawn == null || allTransferables == null)
                return;

            foreach (TransferableOneWay transferable in allTransferables)
            {
                if (transferable != keep && transferable.AnyThing == pawn)
                    transferable.ForceTo(0);
            }
        }

        private static object GetVehicleCaravanFormation()
        {
            Type caravanFormation = AccessTools.TypeByName("Vehicles.World.CaravanFormation");
            return AccessTools.Field(caravanFormation, "formation")?.GetValue(null);
        }

        private static object BuildSelectedVehicleDefList(List<TransferableOneWay> transferables)
        {
            Type vehicleDefType = AccessTools.TypeByName("Vehicles.VehicleDef");
            if (vehicleDefType == null)
                return null;

            Type listType = typeof(List<>).MakeGenericType(vehicleDefType);
            System.Collections.IList list = Activator.CreateInstance(listType) as System.Collections.IList;
            if (list == null)
                return null;

            foreach (TransferableOneWay transferable in transferables)
            {
                if (transferable?.AnyThing == null || transferable.CountToTransfer <= 0 || !IsVehiclePawn(transferable.AnyThing))
                    continue;

                object vehicleDef = GetProperty(transferable.AnyThing, "VehicleDef");
                if (vehicleDef != null && !list.Contains(vehicleDef))
                    list.Add(vehicleDef);
            }

            return list.Count > 0 ? list : null;
        }

        private static void EnsureAssignedPawnsInFormation(object formationInfo, Dialog_FormCaravan dialog)
        {
            IList pawns = AccessTools.Field(formationInfo.GetType(), "pawns")?.GetValue(formationInfo) as IList;
            if (pawns == null)
                return;

            List<Thing> selectedVehicles = GetSelectedVehicles(dialog.transferables);
            if (selectedVehicles.Count == 0)
                return;

            object assignments = GetAssignedSeats();
            object allAssignments = AccessTools.Property(assignments?.GetType(), "AllAssignments")?.GetValue(assignments, null);
            IDictionary dictionary = allAssignments as IDictionary;
            if (dictionary == null)
                return;

            foreach (DictionaryEntry entry in dictionary)
            {
                Pawn pawn = entry.Key as Pawn;
                object seat = entry.Value;
                Thing vehicle = GetProperty(seat, "Vehicle") as Thing;
                if (pawn == null || vehicle == null || !selectedVehicles.Contains(vehicle))
                    continue;

                if (!pawns.Contains(pawn))
                    pawns.Add(pawn);
            }
        }

        private static void EnsureReachableVehicleStartingTile(object formationInfo, Dialog_FormCaravan dialog)
        {
            if (formationInfo == null || dialog == null)
                return;

            PlanetTile original = GetFormationTile(formationInfo, "StartingTile");
            if (!original.Valid)
                return;

            if (VehicleExitSpotReachable(formationInfo))
                return;

            List<PlanetTile> neighbors = new List<PlanetTile>();
            Find.WorldGrid.GetTileNeighbors(dialog.CurrentTile, neighbors);
            foreach (PlanetTile candidate in neighbors)
            {
                if (!candidate.Valid || candidate == original)
                    continue;

                SetPropertyOrField(formationInfo, "StartingTile", candidate);
                SetPropertyOrField(formationInfo, "TicksToArriveDirty", true);
                SetPropertyOrField(formationInfo, "DaysWorthOfFoodDirty", true);
                if (VehicleExitSpotReachable(formationInfo))
                {
                    Log.Message($"RimWorld Access: Vehicle caravan starting tile changed from {original} to {candidate} because the original exit edge was unreachable.");
                    return;
                }
            }

            SetPropertyOrField(formationInfo, "StartingTile", original);
        }

        private static bool VehicleExitSpotReachable(object formationInfo)
        {
            try
            {
                Type caravanFormation = AccessTools.TypeByName("Vehicles.World.CaravanFormation");
                MethodInfo tryFindExitSpot = AccessTools.Method(
                    caravanFormation,
                    "TryFindExitSpot",
                    new[] { typeof(List<Pawn>), typeof(bool), typeof(IntVec3).MakeByRefType() });
                if (tryFindExitSpot == null)
                    return false;

                object pawns = AccessTools.Field(formationInfo.GetType(), "pawns")?.GetValue(formationInfo);
                if (pawns == null)
                    return false;

                object[] args = { pawns, true, IntVec3.Invalid };
                object result = tryFindExitSpot.Invoke(null, args);
                return result is bool reachable && reachable;
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to test Vehicle Framework exit spot: {ex.Message}");
                return false;
            }
        }

        private static PlanetTile GetFormationTile(object formationInfo, string name)
        {
            object value = GetProperty(formationInfo, name);
            if (value is PlanetTile tile)
                return tile;

            value = AccessTools.Field(formationInfo.GetType(), name)?.GetValue(formationInfo);
            return value is PlanetTile fieldTile ? fieldTile : PlanetTile.Invalid;
        }

        private static Thing FindVehicleAtCursor(IntVec3 cursorPosition, Map map)
        {
            if (map == null || !cursorPosition.IsValid || !cursorPosition.InBounds(map))
                return null;

            List<Thing> things = cursorPosition.GetThingList(map);
            Thing fallback = null;
            foreach (Thing thing in things)
            {
                if (!IsVehiclePawn(thing))
                    continue;

                if (fallback == null)
                    fallback = thing;

                if (thing.Faction == Faction.OfPlayer)
                    return thing;
            }

            return fallback;
        }

        private static Thing GetSelectedVehicle()
        {
            if (Find.Selector == null)
                return null;

            foreach (object selected in Find.Selector.SelectedObjects)
            {
                Thing thing = selected as Thing;
                if (IsVehiclePawn(thing))
                    return thing;
            }

            return null;
        }

        private static bool TrySetVehicleDrafted(Thing vehicle, bool drafted, out string message)
        {
            message = null;
            if (!IsVehiclePawn(vehicle))
            {
                message = "No vehicle selected";
                return false;
            }

            object ignition = AccessTools.Field(vehicle.GetType(), "ignition")?.GetValue(vehicle);
            if (ignition == null)
            {
                message = "Vehicle ignition is unavailable";
                return false;
            }

            PropertyInfo draftedProperty = AccessTools.Property(ignition.GetType(), "Drafted");
            if (draftedProperty == null)
            {
                message = "Vehicle draft control is unavailable";
                return false;
            }

            try
            {
                bool current = draftedProperty.GetValue(ignition, null) is bool value && value;
                if (current != drafted)
                    draftedProperty.SetValue(ignition, drafted, null);

                bool after = draftedProperty.GetValue(ignition, null) is bool afterValue && afterValue;
                if (after != drafted)
                {
                    message = $"{vehicle.LabelShort} cannot be drafted";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                message = $"Could not draft {vehicle.LabelShort}: {ex.Message}";
                return false;
            }
        }

        private static object FindBoardingHandler(Thing vehicle, Pawn pawn)
        {
            object fallback = null;
            foreach (object handler in GetVehicleHandlers(vehicle))
            {
                if (!GetBoolProperty(handler, "AreSlotsAvailableAndReservable", false))
                    continue;

                if (!CanOperateHandler(handler, pawn))
                    continue;

                if (IsMovementHandler(handler))
                    return handler;

                if (fallback == null)
                    fallback = handler;
            }

            return fallback;
        }

        private static bool TryFindNearestStandableVehicleCell(Thing vehicle, IntVec3 cursorPosition, out IntVec3 destination)
        {
            destination = cursorPosition;
            Type pathingHelper = AccessTools.TypeByName("Vehicles.PathingHelper");
            MethodInfo method = AccessTools.Method(
                pathingHelper,
                "TryFindNearestStandableCell",
                new[] { GetVehiclePawnType(), typeof(IntVec3), typeof(IntVec3).MakeByRefType(), typeof(float) });
            if (method == null)
                return cursorPosition.IsValid;

            object[] args = { vehicle, cursorPosition, IntVec3.Invalid, -1f };
            object result = method.Invoke(null, args);
            if (result is bool found && found && args[2] is IntVec3 cell)
            {
                destination = cell;
                return true;
            }

            return false;
        }

        private static bool CanVehicleReach(Thing vehicle, IntVec3 destination)
        {
            Type reachabilityUtility = AccessTools.TypeByName("Vehicles.VehicleReachabilityUtility");
            MethodInfo method = AccessTools.Method(
                reachabilityUtility,
                "CanReachVehicle",
                new[] { GetVehiclePawnType(), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(TraverseMode) });
            if (method == null)
                return true;

            object result = method.Invoke(null, new object[] { vehicle, new LocalTargetInfo(destination), PathEndMode.OnCell, Danger.Deadly, TraverseMode.ByPawn });
            return result is bool canReach && canReach;
        }

        private static bool TryOrderVehicleMove(Thing vehicle, IntVec3 clickCell, IntVec3 destination, out string message)
        {
            message = null;
            Type orderType = AccessTools.TypeByName("Vehicles.FloatMenuOptionProvider_OrderVehicle");
            MethodInfo method = AccessTools.Method(orderType, "PawnGotoAction");
            if (method == null)
            {
                message = "Vehicle move command is unavailable";
                return false;
            }

            object rotation = GetProperty(vehicle, "FullRotation");
            if (rotation == null)
                rotation = AccessTools.Field(vehicle.GetType(), "FullRotation")?.GetValue(vehicle);

            try
            {
                method.Invoke(null, new[] { clickCell, vehicle, destination, rotation });
                return true;
            }
            catch (Exception ex)
            {
                message = $"Could not order {vehicle.LabelShort}: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
        }

        private static List<Thing> GetSelectedVehicles(List<TransferableOneWay> transferables)
        {
            List<Thing> vehicles = new List<Thing>();
            if (transferables == null)
                return vehicles;

            foreach (TransferableOneWay transferable in transferables)
            {
                if (transferable?.AnyThing != null &&
                    transferable.CountToTransfer > 0 &&
                    IsVehiclePawn(transferable.AnyThing))
                {
                    vehicles.Add(transferable.AnyThing);
                }
            }

            return vehicles;
        }

        private static PlanetTile GetDialogDestinationTile(Dialog_FormCaravan dialog)
        {
            object value = AccessTools.Field(typeof(Dialog_FormCaravan), "destinationTile")?.GetValue(dialog);
            return value is PlanetTile tile ? tile : PlanetTile.Invalid;
        }

        private static void SetDialogDirty(Dialog_FormCaravan dialog)
        {
            AccessTools.Field(typeof(Dialog_FormCaravan), "daysWorthOfFoodDirty")?.SetValue(dialog, true);
            AccessTools.Field(typeof(Dialog_FormCaravan), "ticksToArriveDirty")?.SetValue(dialog, true);
        }

        private static void NotifyTransferablesChanged(Dialog_FormCaravan dialog)
        {
            try
            {
                MethodInfo notify = AccessTools.Method(typeof(Dialog_FormCaravan), "Notify_TransferablesChanged");
                notify?.Invoke(dialog, null);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to notify caravan transferables changed: {ex.Message}");
            }
        }

        private static void SetPropertyOrField(object instance, string name, object value)
        {
            if (instance == null)
                return;

            PropertyInfo property = AccessTools.Property(instance.GetType(), name);
            if (property != null)
            {
                property.SetValue(instance, value, null);
                return;
            }

            FieldInfo field = AccessTools.Field(instance.GetType(), name);
            field?.SetValue(instance, value);
        }

        private static TransferableOneWay FindBestOperator(
            object handler,
            List<TransferableOneWay> pawnTransferables,
            Thing vehicle)
        {
            TransferableOneWay fallback = null;

            foreach (TransferableOneWay transferable in pawnTransferables)
            {
                Pawn pawn = transferable.AnyThing as Pawn;
                if (pawn == null || IsPawnAssignedElsewhere(pawn, vehicle))
                    continue;

                if (!CanOperateHandler(handler, pawn))
                    continue;

                if (pawn.IsFreeNonSlaveColonist && !pawn.Downed && !pawn.InMentalState)
                    return transferable;

                if (fallback == null)
                    fallback = transferable;
            }

            return fallback;
        }

        private static bool IsPawnAssignedElsewhere(Pawn pawn, Thing vehicle)
        {
            object assignment = GetPawnAssignment(pawn);
            if (assignment == null)
                return false;

            Thing assignedVehicle = GetProperty(assignment, "Vehicle") as Thing;
            return assignedVehicle != null && assignedVehicle != vehicle;
        }

        private static bool IsPawnAssignedToVehicle(Pawn pawn, Thing vehicle)
        {
            object assignment = GetPawnAssignment(pawn);
            if (assignment == null)
                return false;

            return (GetProperty(assignment, "Vehicle") as Thing) == vehicle;
        }

        private static object GetPawnAssignment(Pawn pawn)
        {
            if (pawn == null)
                return null;

            object assignments = GetAssignedSeats();
            MethodInfo getAssignment = assignments?.GetType().GetMethod("GetAssignment");
            return getAssignment?.Invoke(assignments, new object[] { pawn });
        }

        private static string GetOperatorWarning(Thing vehicle)
        {
            int required = GetIntProperty(vehicle, "PawnCountToOperate", 0);
            if (required <= 0)
                return null;

            int assigned = GetAssignedOperatorCount(vehicle);
            if (assigned < required)
                return $"operators assigned {assigned} of {required}";

            return null;
        }

        private static string GetOperatorSummary(Thing vehicle)
        {
            int requiredOperators = GetIntProperty(vehicle, "PawnCountToOperate", 0);
            int totalSlots = 0;
            List<string> roleParts = new List<string>();
            foreach (object handler in GetVehicleHandlers(vehicle))
            {
                object role = AccessTools.Field(handler.GetType(), "role")?.GetValue(handler);
                string label = GetRoleLabel(role);
                int slots = GetHandlerSlots(handler);
                int required = GetHandlerSlotsToOperate(handler);
                totalSlots += slots;

                if (slots <= 0)
                    continue;

                roleParts.Add(required > 0
                    ? $"{label}: {required} required of {slots}"
                    : $"{label}: {slots} optional");
            }

            if (totalSlots <= 0)
                return "No operator or passenger seats";

            if (requiredOperators <= 0 && roleParts.Count == 0)
                return "No operator required";

            int assigned = GetAssignedOperatorCount(vehicle);
            string summary = $"Operators assigned: {assigned} of {requiredOperators} required. Seats: {totalSlots} total";
            if (roleParts.Count > 0)
                summary += $". {string.Join("; ", roleParts)}";

            return summary;
        }

        private static string GetFuelSummary(Thing vehicle)
        {
            object fuelComp = GetCompFueledTravel(vehicle);
            if (fuelComp == null)
                return "No fuel required";

            float fuel = GetFloatProperty(fuelComp, "Fuel");
            float capacity = GetFloatProperty(fuelComp, "FuelCapacity");
            string fuelLabel = GetFuelLabel(fuelComp);
            string result = $"{fuelLabel}: {fuel:F0} of {capacity:F0}";

            float days = GetFuelDays(fuelComp);
            if (days >= 0f)
                result += $", about {days:F1} travel days";

            if (fuel <= 0f)
                result += ", OUT OF FUEL";

            return result;
        }

        private static string GetFuelLabel(object fuelComp)
        {
            try
            {
                object props = GetProperty(fuelComp, "Props");
                if (props != null)
                {
                    FieldInfo fuelTypeField = AccessTools.Field(props.GetType(), "fuelType");
                    ThingDef fuelType = fuelTypeField?.GetValue(props) as ThingDef;
                    if (fuelType != null)
                        return fuelType.LabelCap.ToString();

                    FieldInfo electricField = AccessTools.Field(props.GetType(), "electricPowered");
                    if (electricField != null && electricField.GetValue(props) is bool electric && electric)
                        return "Electricity";
                }
            }
            catch { }

            return "Fuel";
        }

        private static string GetVehicleOrientationSummary(Thing vehicle)
        {
            object rotation = GetProperty(vehicle, "FullRotation");
            if (rotation == null)
                return null;

            string direction = FormatVehicleRotation(rotation.ToString());
            return direction.NullOrEmpty() ? null : $"Front facing {direction}";
        }

        private static string FormatVehicleRotation(string rotation)
        {
            if (rotation.NullOrEmpty())
                return null;

            switch (rotation)
            {
                case "North":
                    return "north";
                case "NorthEast":
                    return "north-east";
                case "East":
                    return "east";
                case "SouthEast":
                    return "south-east";
                case "South":
                    return "south";
                case "SouthWest":
                    return "south-west";
                case "West":
                    return "west";
                case "NorthWest":
                    return "north-west";
                default:
                    return rotation.StripTags();
            }
        }

        private static float GetFuelDays(object fuelComp)
        {
            float fuel = GetFloatProperty(fuelComp, "Fuel");
            float rate = GetFloatProperty(fuelComp, "ConsumptionRateWorldPerTick");
            if (fuel < 0f || rate <= 0f)
                return -1f;

            return fuel / (rate * 60000f);
        }

        private static object GetCompFueledTravel(Thing vehicle)
        {
            try
            {
                return GetProperty(vehicle, "CompFueledTravel");
            }
            catch
            {
                return null;
            }
        }

        private static float GetVehicleTilesPerDay(Thing vehicle)
        {
            float moveSpeed = GetVehicleStat(vehicle, "MoveSpeed");
            if (moveSpeed <= 0f)
                return 0f;

            float worldSpeedMultiplier = GetFloatProperty(vehicle, "WorldSpeedMultiplier", 1f);
            moveSpeed *= worldSpeedMultiplier;
            if (moveSpeed <= 0f)
                return 0f;

            float normalized = moveSpeed / 60f;
            if (normalized <= 0f)
                return 0f;

            // Mirrors Vehicle Framework's TicksFromMoveSpeed conversion.
            float ticksPerTile = Math.Max((float)Math.Round((1f / normalized) * 340f), 1f);
            if (ticksPerTile <= 0f)
                return 0f;

            return 60000f / ticksPerTile;
        }

        private static float GetVehicleStat(Thing vehicle, string statFieldName)
        {
            try
            {
                Type statDefOf = GetVehicleStatDefOfType();
                FieldInfo statField = statDefOf == null ? null : AccessTools.Field(statDefOf, statFieldName);
                object statDef = statField?.GetValue(null);
                if (statDef == null)
                    return 0f;

                MethodInfo method = vehicle.GetType().GetMethod("GetStatValue", new[] { statDef.GetType() });
                object result = method?.Invoke(vehicle, new[] { statDef });
                return ToFloat(result, 0f);
            }
            catch
            {
                return 0f;
            }
        }

        private static int GetAssignedOperatorCount(Thing vehicle)
        {
            int count = 0;
            foreach (object seat in GetAssignmentsForVehicle(vehicle))
            {
                object handler = AccessTools.Field(seat.GetType(), "handler")?.GetValue(seat);
                if (IsMovementHandler(handler))
                    count++;
            }
            return count;
        }

        private static int CountAssignedToHandler(Thing vehicle, object handler)
        {
            int count = 0;
            foreach (object seat in GetAssignmentsForVehicle(vehicle))
            {
                object seatHandler = AccessTools.Field(seat.GetType(), "handler")?.GetValue(seat);
                if (ReferenceEquals(seatHandler, handler))
                    count++;
            }
            return count;
        }

        private static IEnumerable<object> GetAssignmentsForVehicle(Thing vehicle)
        {
            object assignments = GetAssignedSeats();
            if (assignments == null)
                yield break;

            MethodInfo method = assignments.GetType().GetMethod("GetAssignments");
            object result = method?.Invoke(assignments, new object[] { vehicle });
            if (result is System.Collections.IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    yield return item;
            }
        }

        private static IEnumerable<object> GetVehicleHandlers(Thing vehicle)
        {
            FieldInfo field = AccessTools.Field(vehicle.GetType(), "handlers");
            object handlers = field?.GetValue(vehicle);
            if (handlers is System.Collections.IEnumerable enumerable)
            {
                foreach (object handler in enumerable)
                    yield return handler;
            }
        }

        private static bool IsMovementHandler(object handler)
        {
            object role = AccessTools.Field(handler?.GetType(), "role")?.GetValue(handler);
            if (role == null)
                return false;

            object handlingTypes = GetProperty(role, "HandlingTypes");
            if (handlingTypes == null)
                return false;

            try
            {
                return (Convert.ToInt32(handlingTypes) & 1) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static int GetHandlerSlotsToOperate(object handler)
        {
            object role = AccessTools.Field(handler?.GetType(), "role")?.GetValue(handler);
            return role == null ? 0 : GetIntProperty(role, "SlotsToOperate", 0);
        }

        private static bool CanOperateHandler(object handler, Pawn pawn)
        {
            MethodInfo method = AccessTools.Method(handler.GetType(), "CanOperateRole", new[] { typeof(Pawn) });
            object result = method?.Invoke(handler, new object[] { pawn });
            return result is bool canOperate && canOperate;
        }

        private static object GetAssignedSeats()
        {
            Type type = GetCaravanHelperType();
            if (type == null)
                return null;

            if (assignedSeatsField == null)
                assignedSeatsField = AccessTools.Field(type, "assignedSeats");

            return assignedSeatsField?.GetValue(null);
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null)
                return null;

            PropertyInfo property = AccessTools.Property(instance.GetType(), name);
            return property?.GetValue(instance, null);
        }

        private static bool GetBoolProperty(object instance, string name, bool defaultValue)
        {
            object value = GetProperty(instance, name);
            return value is bool boolValue ? boolValue : defaultValue;
        }

        private static int GetIntProperty(object instance, string name, int defaultValue)
        {
            object value = GetProperty(instance, name);
            if (value == null)
                return defaultValue;

            try { return Convert.ToInt32(value); }
            catch { return defaultValue; }
        }

        private static float GetFloatProperty(object instance, string name, float defaultValue = -1f)
        {
            object value = GetProperty(instance, name);
            return ToFloat(value, defaultValue);
        }

        private static float ToFloat(object value, float defaultValue)
        {
            if (value == null)
                return defaultValue;

            try { return Convert.ToSingle(value); }
            catch { return defaultValue; }
        }

        private static Type GetVehiclePawnType()
        {
            if (vehiclePawnType == null)
            {
                vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
            }
            return vehiclePawnType;
        }

        private static Type GetVehicleStatDefOfType()
        {
            if (vehicleStatDefOfType == null)
            {
                vehicleStatDefOfType = AccessTools.TypeByName("Vehicles.VehicleStatDefOf");
            }
            return vehicleStatDefOfType;
        }

        private static Type GetCaravanHelperType()
        {
            if (caravanHelperType == null)
            {
                caravanHelperType = AccessTools.TypeByName("Vehicles.CaravanHelper");
            }
            return caravanHelperType;
        }

        private static Type GetAssignedSeatType()
        {
            if (assignedSeatType == null)
            {
                assignedSeatType = AccessTools.TypeByName("Vehicles.AssignedSeat");
            }
            return assignedSeatType;
        }

        private static Type GetVehicleRoleHandlerType()
        {
            return AccessTools.TypeByName("Vehicles.VehicleRoleHandler");
        }

        private static FieldInfo GetSelectedTabField()
        {
            if (selectedTabField != null)
                return selectedTabField;

            if (formCaravanPatchType == null)
            {
                formCaravanPatchType = AccessTools.TypeByName("Vehicles.Patch_FormCaravanDialog");
            }

            if (formCaravanPatchType == null)
                return null;

            selectedTabField = AccessTools.Field(formCaravanPatchType, "selectedTab");
            return selectedTabField;
        }
    }
}
