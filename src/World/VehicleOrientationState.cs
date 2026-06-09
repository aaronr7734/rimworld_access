using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    public static class VehicleOrientationState
    {
        private static bool isActive = false;
        private static Thing vehicle = null;
        private static object originalRotation = null;

        public static bool IsActive => isActive;

        public static void Open(Thing selectedVehicle)
        {
            vehicle = selectedVehicle;
            originalRotation = GetRotation(selectedVehicle);
            isActive = vehicle != null && originalRotation != null;
            if (!isActive)
                return;

            TolkHelper.Speak($"{GetStatusAnnouncement()}. Q and E rotate. Enter confirms. Escape cancels. R repeats current facing.");
        }

        public static void Close()
        {
            isActive = false;
            vehicle = null;
            originalRotation = null;
        }

        public static bool HandleInput(KeyCode key, bool shift, bool ctrl, bool alt)
        {
            if (!isActive || vehicle == null)
                return false;

            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && !shift && !ctrl && !alt)
            {
                Confirm();
                return true;
            }

            if (key == KeyCode.Escape && !shift && !ctrl && !alt)
            {
                Cancel();
                return true;
            }

            if ((key == KeyCode.Q || key == KeyCode.LeftArrow) && !shift && !ctrl && !alt)
            {
                Rotate(false);
                return true;
            }

            if ((key == KeyCode.E || key == KeyCode.RightArrow) && !shift && !ctrl && !alt)
            {
                Rotate(true);
                return true;
            }

            if (key == KeyCode.R && !shift && !ctrl && !alt)
            {
                TolkHelper.Speak(GetStatusAnnouncement());
                return true;
            }

            return false;
        }

        private static void Confirm()
        {
            string status = GetStatusAnnouncement();
            Close();
            TolkHelper.Speak($"{status}. Orientation confirmed.");
        }

        private static void Cancel()
        {
            if (vehicle != null && originalRotation != null)
                SetRotation(vehicle, originalRotation);

            Close();
            TolkHelper.Speak("Vehicle orientation cancelled");
        }

        private static void Rotate(bool clockwise)
        {
            object rotation = GetRotation(vehicle);
            if (rotation == null)
            {
                TolkHelper.Speak("Vehicle rotation is unavailable", SpeechPriority.High);
                return;
            }

            MethodInfo rotatedMethod = AccessTools.Method(rotation.GetType(), "Rotated", new[] { typeof(RotationDirection), typeof(bool) });
            object nextRotation = null;
            if (rotatedMethod != null)
            {
                nextRotation = rotatedMethod.Invoke(rotation, new object[] { clockwise ? RotationDirection.Clockwise : RotationDirection.Counterclockwise, false });
            }
            else
            {
                rotatedMethod = AccessTools.Method(rotation.GetType(), "Rotated", new[] { typeof(RotationDirection) });
                if (rotatedMethod != null)
                    nextRotation = rotatedMethod.Invoke(rotation, new object[] { clockwise ? RotationDirection.Clockwise : RotationDirection.Counterclockwise });
            }

            if (nextRotation == null || !SetRotation(vehicle, nextRotation))
            {
                TolkHelper.Speak("Vehicle rotation is unavailable", SpeechPriority.High);
                return;
            }

            TolkHelper.Speak(GetStatusAnnouncement());
        }

        private static object GetRotation(Thing thing)
        {
            if (thing == null)
                return null;

            PropertyInfo property = AccessTools.Property(thing.GetType(), "FullRotation");
            if (property != null)
                return property.GetValue(thing, null);

            FieldInfo field = AccessTools.Field(thing.GetType(), "FullRotation");
            return field?.GetValue(thing);
        }

        private static bool SetRotation(Thing thing, object rotation)
        {
            if (thing == null || rotation == null)
                return false;

            try
            {
                PropertyInfo property = AccessTools.Property(thing.GetType(), "FullRotation");
                if (property != null)
                {
                    property.SetValue(thing, rotation, null);
                    return true;
                }

                FieldInfo field = AccessTools.Field(thing.GetType(), "FullRotation");
                if (field != null)
                {
                    field.SetValue(thing, rotation);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string GetStatusAnnouncement()
        {
            string facing = VehicleFrameworkHelper.FormatVehicleRotation(GetRotation(vehicle));
            string label = vehicle?.LabelShortCap.ToString().StripTags() ?? "Vehicle";
            return facing.NullOrEmpty()
                ? $"{label} orientation mode"
                : $"{label}. Facing {facing}";
        }
    }
}
