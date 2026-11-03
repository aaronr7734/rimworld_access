using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard facing choice for Vehicle Framework's Vehicles.VehicleOrientationController, the
    /// "Go here" session that confirms the moment the right mouse button is not held and rotates
    /// only while it is. A session started from a keyboard order (PendingTargetingContext is set
    /// by every keyboard order path and by no mouse path) is held open and offered as a windowless
    /// menu of the eight facings VF's own Q/E stepping reaches; choosing one sets VF's Rotation and
    /// runs VF's ConfirmOrientation, Escape runs VF's StopTargeting.
    /// </summary>
    internal static class VfOrientationCompat
    {
        private static PropertyInfo instanceProperty;    // private static VehicleOrientationController Instance
        private static PropertyInfo isTargetingProperty; // public override bool IsTargeting
        private static PropertyInfo rotationProperty;    // private Rot8 Rotation { get; set; }
        private static FieldInfo vehiclesField;          // private readonly List<VehiclePawn> vehicles
        private static MethodInfo confirmMethod;         // private void ConfirmOrientation()
        private static MethodInfo stopTargetingMethod;   // public override void StopTargeting()
        private static MethodInfo rotatedMethod;         // Rot8.Rotated(RotationDirection, bool)
        private static PropertyInfo asIntProperty;       // Rot8.AsInt, 0-3 cardinal, 4-7 diagonal
        private static PropertyInfo asAngleProperty;     // Rot8.AsAngle, degrees clockwise from north

        private static bool ready;

        private static object armedInstance;
        private static object armedRotation;
        private static List<FloatMenuOption> menuOptions;

        public static void Register(Harmony harmony)
        {
            try
            {
                var surface = new ReflectionSurface("VfOrientationCompat");

                Type controllerType = surface.Type("Vehicles.VehicleOrientationController");
                Type rot8Type = surface.Type("SmashTools.Rot8");

                instanceProperty = surface.Property(controllerType, "Instance");
                isTargetingProperty = surface.Property(controllerType, "IsTargeting");
                rotationProperty = surface.Property(controllerType, "Rotation");
                vehiclesField = surface.Field(controllerType, "vehicles");
                confirmMethod = surface.Method(controllerType, "ConfirmOrientation", Type.EmptyTypes);
                stopTargetingMethod = surface.Method(controllerType, "StopTargeting", Type.EmptyTypes);
                MethodInfo initMethod = surface.Method(controllerType, "Init");
                MethodInfo processInputEventsMethod = surface.Method(controllerType, "ProcessInputEvents", Type.EmptyTypes);
                rotatedMethod = surface.Method(rot8Type, "Rotated", new[] { typeof(RotationDirection), typeof(bool) });
                asIntProperty = surface.Property(rot8Type, "AsInt");
                asAngleProperty = surface.Property(rot8Type, "AsAngle");

                ready = surface.Ready;
                if (!ready)
                    return;

                ExternalMapTargeting.Register(IsSessionHeld);

                harmony.Patch(initMethod,
                    postfix: new HarmonyMethod(typeof(VfOrientationCompat), nameof(InitPostfix)));
                harmony.Patch(processInputEventsMethod,
                    prefix: new HarmonyMethod(typeof(VfOrientationCompat), nameof(ProcessInputEventsPrefix)));
                harmony.Patch(stopTargetingMethod,
                    postfix: new HarmonyMethod(typeof(VfOrientationCompat), nameof(StopTargetingPostfix)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VF orientation compat registration failed: {ex.Message}");
                ready = false;
            }
        }

        private static bool IsSessionHeld()
        {
            return ready && armedInstance != null && (bool)isTargetingProperty.GetValue(armedInstance);
        }

        public static void InitPostfix(object __instance)
        {
            if (!ready)
                return;

            try
            {
                armedInstance = PendingTargetingContext.ConsumeLabel() != null ? __instance : null;
                // Init just seeded Rotation with the lead vehicle's facing; TargeterUpdate overwrites it every frame.
                armedRotation = armedInstance != null ? rotationProperty.GetValue(__instance) : null;
                menuOptions = null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfOrientationCompat.InitPostfix failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Skips VF's body for a held session: that body auto-confirms without the right mouse button
        /// and, for several vehicles, re-aims the line at the physical mouse. The menu opens here rather
        /// than in Init so the order path that started the session has finished its own work first.
        /// </summary>
        public static bool ProcessInputEventsPrefix(object __instance)
        {
            if (!ready || armedInstance == null || !ReferenceEquals(__instance, armedInstance))
                return true;

            try
            {
                if (!(bool)isTargetingProperty.GetValue(__instance))
                {
                    Disarm();
                    return true;
                }

                if (menuOptions == null)
                {
                    OpenMenu(__instance);
                    return false;
                }

                // Replaced or closed by something other than a choice or Escape: VF's own session
                // must not then auto-confirm a facing nobody chose.
                if (!MenuStillOurs())
                {
                    Disarm();
                    stopTargetingMethod.Invoke(__instance, null);
                }
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfOrientationCompat.ProcessInputEventsPrefix failed: {ex.Message}");
                Disarm();
                return true;
            }
        }

        /// <summary>Closes a still-open facing menu when VF ends the session on its own (another targeter pushed, game disposed).</summary>
        public static void StopTargetingPostfix(object __instance)
        {
            if (!ready || !ReferenceEquals(__instance, armedInstance))
                return;

            bool menuStillOurs = MenuStillOurs();
            Disarm();
            if (menuStillOurs)
                WindowlessFloatMenuState.Close();
        }

        private static bool MenuStillOurs()
        {
            return menuOptions != null
                && WindowlessFloatMenuState.IsActive
                && ReferenceEquals(WindowlessFloatMenuState.CurrentOptions, menuOptions);
        }

        private static void Disarm()
        {
            armedInstance = null;
            armedRotation = null;
            menuOptions = null;
        }

        private static void OpenMenu(object controller)
        {
            var options = new List<FloatMenuOption>(8);
            object facing = armedRotation;
            for (int i = 0; i < 8; i++)
            {
                object chosen = facing;
                string label = FacingLabel(chosen);
                string rowLabel = i == 0
                    ? "RimWorldAccess.Compat.Vf.OrientCurrentFacing".Translate(label).ToString()
                    : label;
                options.Add(new FloatMenuOption(rowLabel, () => Confirm(controller, chosen, label)));
                facing = rotatedMethod.Invoke(facing, new object[] { RotationDirection.Clockwise, true });
            }

            menuOptions = options;
            WindowlessFloatMenuState.Open(
                options,
                colonistOrders: false,
                announceSelection: false,
                onClose: cancelled =>
                {
                    if (cancelled)
                        Cancel(controller);
                },
                titleText: "RimWorldAccess.Compat.Vf.OrientTitle".Translate(VehicleNames(controller)).ToString());
        }

        private static void Confirm(object controller, object rotation, string label)
        {
            if (!ReferenceEquals(controller, armedInstance) || !(bool)isTargetingProperty.GetValue(controller))
                return;

            Disarm();
            // MUTATION-C: mirrors VehicleOrientationController.ProcessInputEvents' Q/E branch; that branch only runs while the right mouse button is held.
            rotationProperty.SetValue(controller, rotation);
            confirmMethod.Invoke(controller, null);
            TolkHelper.Speak("RimWorldAccess.Compat.Vf.OrientConfirmed".Loc(label));
        }

        private static void Cancel(object controller)
        {
            if (!ReferenceEquals(controller, armedInstance))
                return;

            Disarm();
            // VF's own Cancel-key branch is exactly this call.
            stopTargetingMethod.Invoke(controller, null);
            TolkHelper.Speak("RimWorldAccess.Compat.Vf.OrientCancelled".Loc());
        }

        /// <summary>Vanilla's own words for the four cardinal facings; vanilla has only abbreviations for the diagonals.</summary>
        private static string FacingLabel(object rot8)
        {
            int asInt = (int)asIntProperty.GetValue(rot8);
            if (asInt < 4)
                return new Rot4(asInt).ToStringHuman();
            return ScannerDirectionHelper.GetCompassDirection((double)(float)asAngleProperty.GetValue(rot8));
        }

        private static string VehicleNames(object controller)
        {
            var names = new List<string>();
            if (vehiclesField.GetValue(controller) is IList vehicles)
            {
                foreach (object vehicle in vehicles)
                {
                    if (vehicle is Pawn pawn)
                        names.Add(pawn.LabelShort);
                }
            }
            return MenuHelper.FormatNameList(names);
        }
    }
}
