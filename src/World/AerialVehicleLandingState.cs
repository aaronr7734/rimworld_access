using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class AerialVehicleLandingState
    {
        private static bool isActive = false;
        private static object landingTargeter = null;

        public static bool IsActive => isActive;

        public static void Open(object targeter)
        {
            landingTargeter = targeter;
            isActive = targeter != null;
            if (!isActive)
                return;

            TolkHelper.Speak("Select aerial vehicle landing spot. Move map cursor, Enter confirms, Q and E rotate, R reports landing validity, Escape cancels.");
            AnnounceLandingStatus();
        }

        public static void Close()
        {
            isActive = false;
            landingTargeter = null;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive || landingTargeter == null)
                return false;

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                ConfirmLanding();
                return true;
            }

            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                CancelLanding();
                return true;
            }

            if (key == KeyCode.Q && !shift && !ctrl && !alt)
            {
                Rotate(RotationDirection.Counterclockwise);
                return true;
            }

            if (key == KeyCode.E && !shift && !ctrl && !alt)
            {
                Rotate(RotationDirection.Clockwise);
                return true;
            }

            if (key == KeyCode.R && !shift && !ctrl && !alt)
            {
                AnnounceLandingStatus();
                return true;
            }

            return false;
        }

        private static void ConfirmLanding()
        {
            IntVec3 cell = MapNavigationState.CurrentCursorPosition;
            Map map = Find.CurrentMap;
            if (map == null || !cell.IsValid || !cell.InBounds(map))
            {
                TolkHelper.Speak("Invalid landing position", SpeechPriority.High);
                return;
            }

            object localTarget = new LocalTargetInfo(cell);
            object state = GetPositionState(localTarget);
            string stateText = state?.ToString() ?? "Invalid";
            if (stateText == "Invalid")
            {
                TolkHelper.Speak("Invalid landing position", SpeechPriority.High);
                return;
            }

            try
            {
                Delegate action = AccessTools.Field(landingTargeter.GetType(), "action")?.GetValue(landingTargeter) as Delegate;
                if (action == null)
                {
                    TolkHelper.Speak("Landing action unavailable", SpeechPriority.High);
                    return;
                }

                Rot4 rotation = GetLandingRotation();
                action.DynamicInvoke(localTarget, rotation);
                AccessTools.Method(landingTargeter.GetType(), "StopTargeting")?.Invoke(landingTargeter, null);
                TolkHelper.Speak($"Landing selected at {cell.x}, {cell.z}, facing {FormatRotation(rotation)}");
                Close();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to confirm aerial vehicle landing: {ex}");
                TolkHelper.Speak("Could not confirm landing", SpeechPriority.High);
            }
        }

        private static void CancelLanding()
        {
            try
            {
                AccessTools.Method(landingTargeter.GetType(), "StopTargeting")?.Invoke(landingTargeter, null);
            }
            catch { }

            Close();
            TolkHelper.Speak("Landing targeting cancelled");
        }

        private static void Rotate(RotationDirection direction)
        {
            try
            {
                FieldInfo field = AccessTools.Field(landingTargeter.GetType(), "landingRotation");
                if (field == null)
                {
                    TolkHelper.Speak("Landing rotation unavailable");
                    return;
                }

                Rot4 rotation = (Rot4)field.GetValue(landingTargeter);
                rotation.Rotate(direction);
                field.SetValue(landingTargeter, rotation);
                TolkHelper.Speak($"Facing {FormatRotation(rotation)}");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to rotate aerial vehicle landing target: {ex.Message}");
                TolkHelper.Speak("Could not rotate landing target", SpeechPriority.High);
            }
        }

        private static void AnnounceLandingStatus()
        {
            IntVec3 cell = MapNavigationState.CurrentCursorPosition;
            if (!cell.IsValid)
            {
                TolkHelper.Speak("Invalid landing position");
                return;
            }

            object state = GetPositionState(new LocalTargetInfo(cell));
            Rot4 rotation = GetLandingRotation();
            string stateText = state?.ToString() ?? "Invalid";
            string reason = GetLandingProblemReason(cell, rotation, stateText);
            string reasonText = string.IsNullOrEmpty(reason) ? string.Empty : $". {reason}";
            TolkHelper.Speak($"Landing {stateText.ToLowerInvariant()} at {cell.x}, {cell.z}, facing {FormatRotation(rotation)}{reasonText}");
        }

        private static object GetPositionState(object localTarget)
        {
            try
            {
                MethodInfo method = AccessTools.Method(landingTargeter.GetType(), "GetPosState", new[] { typeof(LocalTargetInfo), typeof(bool) });
                return method?.Invoke(landingTargeter, new[] { localTarget, false });
            }
            catch
            {
                return null;
            }
        }

        private static string GetLandingProblemReason(IntVec3 cell, Rot4 rotation, string stateText)
        {
            if (string.Equals(stateText, "Valid", StringComparison.OrdinalIgnoreCase))
                return null;

            Map map = Find.CurrentMap;
            if (map == null)
                return "No active map";

            if (!cell.IsValid)
                return "Target cell is invalid";

            if (!cell.InBounds(map))
                return "Target cell is outside the map";

            object vehicle = GetVehicle();
            IntVec2 size = GetVehicleSize(vehicle);
            CellRect occupiedRect = GenAdj.OccupiedRect(cell, rotation, size);

            foreach (IntVec3 occupiedCell in occupiedRect)
            {
                if (!occupiedCell.InBounds(map))
                    return $"Aircraft footprint extends outside the map at {occupiedCell.x}, {occupiedCell.z}";
            }

            string validatorReason = GetTargetValidatorReason(cell, map, vehicle);
            if (!string.IsNullOrEmpty(validatorReason))
                return validatorReason;

            string roofReason = GetRoofReason(occupiedRect, map, vehicle);
            if (!string.IsNullOrEmpty(roofReason))
                return roofReason;

            string blockerReason = GetBlockerReason(occupiedRect, map);
            if (!string.IsNullOrEmpty(blockerReason))
                return blockerReason;

            if (string.Equals(stateText, "Obstructed", StringComparison.OrdinalIgnoreCase))
                return "Landing zone is obstructed";

            return "Vehicle Framework rejected this landing spot";
        }

        private static string GetTargetValidatorReason(IntVec3 cell, Map map, object vehicle)
        {
            try
            {
                object validator = AccessTools.Field(landingTargeter.GetType(), "targetValidator")?.GetValue(landingTargeter);
                Delegate targetValidator = validator as Delegate;
                if (targetValidator == null)
                    return null;

                bool accepted = Convert.ToBoolean(targetValidator.DynamicInvoke(new LocalTargetInfo(cell)));
                if (accepted)
                    return null;

                if (cell.Roofed(map))
                {
                    RoofDef roof = cell.GetRoof(map);
                    if (roof?.isThickRoof == true)
                        return $"Blocked by thick roof at {cell.x}, {cell.z}";
                    if (!VehicleCanRoofPunch(vehicle))
                        return $"Blocked by roof at {cell.x}, {cell.z}";
                }

                return "Target validator rejected this cell";
            }
            catch
            {
                return null;
            }
        }

        private static string GetRoofReason(CellRect occupiedRect, Map map, object vehicle)
        {
            bool canRoofPunch = VehicleCanRoofPunch(vehicle);
            foreach (IntVec3 occupiedCell in occupiedRect)
            {
                RoofDef roof = occupiedCell.GetRoof(map);
                if (roof == null)
                    continue;

                if (roof.isThickRoof)
                    return $"Blocked by thick roof at {occupiedCell.x}, {occupiedCell.z}";

                if (!canRoofPunch)
                    return $"Blocked by roof at {occupiedCell.x}, {occupiedCell.z}";
            }

            return null;
        }

        private static string GetBlockerReason(CellRect occupiedRect, Map map)
        {
            foreach (IntVec3 occupiedCell in occupiedRect)
            {
                if (!occupiedCell.Standable(map))
                {
                    Thing edifice = occupiedCell.GetEdifice(map);
                    if (edifice != null)
                        return $"Blocked by {edifice.LabelCap} at {occupiedCell.x}, {occupiedCell.z}";

                    TerrainDef terrain = occupiedCell.GetTerrain(map);
                    if (terrain != null && terrain.passability == Traversability.Impassable)
                        return $"Blocked by terrain {terrain.LabelCap} at {occupiedCell.x}, {occupiedCell.z}";

                    return $"Cell {occupiedCell.x}, {occupiedCell.z} is not standable";
                }

                List<Thing> things = occupiedCell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed)
                        continue;

                    if (thing is Pawn pawn)
                        return $"Blocked by pawn {pawn.LabelShortCap} at {occupiedCell.x}, {occupiedCell.z}";

                    if (thing.def?.passability == Traversability.Impassable)
                        return $"Blocked by {thing.LabelCap} at {occupiedCell.x}, {occupiedCell.z}";
                }
            }

            return null;
        }

        private static object GetVehicle()
        {
            try
            {
                Type type = landingTargeter.GetType();
                while (type != null)
                {
                    FieldInfo field = AccessTools.Field(type, "vehicle");
                    if (field != null)
                        return field.GetValue(landingTargeter);

                    type = type.BaseType;
                }
            }
            catch { }

            return null;
        }

        private static IntVec2 GetVehicleSize(object vehicle)
        {
            try
            {
                object vehicleDef = GetProperty(vehicle, "VehicleDef");
                object size = GetProperty(vehicleDef, "Size");
                if (size is IntVec2 intVec2)
                    return intVec2;
            }
            catch { }

            return new IntVec2(1, 1);
        }

        private static bool VehicleCanRoofPunch(object vehicle)
        {
            try
            {
                object launcher = GetProperty(vehicle, "CompVehicleLauncher");
                object props = GetProperty(launcher, "Props");
                object canRoofPunch = GetProperty(props, "canRoofPunch");
                return canRoofPunch is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null || string.IsNullOrEmpty(name))
                return null;

            PropertyInfo property = AccessTools.Property(instance.GetType(), name);
            if (property != null)
                return property.GetValue(instance, null);

            FieldInfo field = AccessTools.Field(instance.GetType(), name);
            return field?.GetValue(instance);
        }

        private static Rot4 GetLandingRotation()
        {
            try
            {
                object value = AccessTools.Field(landingTargeter.GetType(), "landingRotation")?.GetValue(landingTargeter);
                return value is Rot4 rot ? rot : Rot4.North;
            }
            catch
            {
                return Rot4.North;
            }
        }

        private static string FormatRotation(Rot4 rotation)
        {
            if (rotation == Rot4.North)
                return "north";
            if (rotation == Rot4.East)
                return "east";
            if (rotation == Rot4.South)
                return "south";
            if (rotation == Rot4.West)
                return "west";
            return rotation.ToString();
        }
    }
}
