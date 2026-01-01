using System;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for temperature control device settings (coolers, heaters, etc.).
    /// Allows adjusting target temperature via keyboard shortcuts.
    /// </summary>
    public static class TempControlMenuState
    {
        private static CompTempControl tempControl = null;
        private static Building building = null;
        private static bool isActive = false;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the temperature control menu for the given building.
        /// </summary>
        public static void Open(Building targetBuilding)
        {
            if (targetBuilding == null)
            {
                TolkHelper.Speak("No building to configure");
                return;
            }

            CompTempControl comp = targetBuilding.TryGetComp<CompTempControl>();
            if (comp == null)
            {
                TolkHelper.Speak("Building does not have temperature control");
                return;
            }

            building = targetBuilding;
            tempControl = comp;
            isActive = true;

            AnnounceCurrentSettings();
        }

        /// <summary>
        /// Closes the temperature control menu.
        /// </summary>
        public static void Close()
        {
            tempControl = null;
            building = null;
            isActive = false;
        }

        /// <summary>
        /// Increases target temperature by 1 degree.
        /// </summary>
        public static void IncreaseTemperatureSmall()
        {
            if (tempControl == null) return;

            float offset = RoundedToCurrentTempModeOffset(1f);
            AdjustTemperature(offset);
        }

        /// <summary>
        /// Increases target temperature by 10 degrees.
        /// </summary>
        public static void IncreaseTemperatureLarge()
        {
            if (tempControl == null) return;

            float offset = RoundedToCurrentTempModeOffset(10f);
            AdjustTemperature(offset);
        }

        /// <summary>
        /// Decreases target temperature by 1 degree.
        /// </summary>
        public static void DecreaseTemperatureSmall()
        {
            if (tempControl == null) return;

            float offset = RoundedToCurrentTempModeOffset(-1f);
            AdjustTemperature(offset);
        }

        /// <summary>
        /// Decreases target temperature by 10 degrees.
        /// </summary>
        public static void DecreaseTemperatureLarge()
        {
            if (tempControl == null) return;

            float offset = RoundedToCurrentTempModeOffset(-10f);
            AdjustTemperature(offset);
        }

        /// <summary>
        /// Resets target temperature to default (21°C).
        /// </summary>
        public static void ResetTemperature()
        {
            if (tempControl == null) return;

            tempControl.TargetTemperature = 21f;
            AnnounceCurrentSettings();
        }

        private static void AdjustTemperature(float offset)
        {
            if (tempControl == null) return;

            tempControl.TargetTemperature += offset;
            tempControl.TargetTemperature = Mathf.Clamp(tempControl.TargetTemperature, -273.15f, 1000f);
            AnnounceCurrentSettings();
        }

        private static void AnnounceCurrentSettings()
        {
            if (tempControl == null || building == null)
                return;

            string targetTemp = tempControl.TargetTemperature.ToStringTemperature("F0");

            // Get power mode if available
            string powerMode = "";
            if (tempControl.PowerTrader != null)
            {
                if (tempControl.PowerTrader.Off)
                {
                    powerMode = " - Off";
                }
                else if (tempControl.operatingAtHighPower)
                {
                    powerMode = " - High power";
                }
                else
                {
                    powerMode = " - Low power";
                }
            }

            string announcement = $"{building.LabelCap} - Target: {targetTemp}{powerMode}";
            TolkHelper.Speak(announcement);
        }

        /// <summary>
        /// Helper method to round temperature offset based on current temperature mode (Celsius/Fahrenheit/Kelvin).
        /// </summary>
        private static float RoundedToCurrentTempModeOffset(float celsiusTemp)
        {
            return GenTemperature.ConvertTemperatureOffset(
                Mathf.RoundToInt(GenTemperature.CelsiusToOffset(celsiusTemp, Prefs.TemperatureMode)),
                Prefs.TemperatureMode,
                TemperatureDisplayMode.Celsius);
        }
    }
}
