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

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompTempControl comp = targetBuilding.TryGetComp<CompTempControl>();
            if (comp == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Temp.NoTempComponent".Loc());
                return;
            }

            building = targetBuilding;
            tempControl = comp;
            isActive = true;
            MapNavigationState.SuppressMapNavigation = true;

            AnnounceCurrentSettings();
        }

        public static void Close()
        {
            tempControl = null;
            building = null;
            isActive = false;
            MapNavigationState.SuppressMapNavigation = false;
        }

        public static void IncreaseTemperatureSmall()
        {
            if (tempControl == null) return;
            AdjustTemperature(RoundedToCurrentTempModeOffset(1f));
        }

        public static void IncreaseTemperatureLarge()
        {
            if (tempControl == null) return;
            AdjustTemperature(RoundedToCurrentTempModeOffset(10f));
        }

        public static void DecreaseTemperatureSmall()
        {
            if (tempControl == null) return;
            AdjustTemperature(RoundedToCurrentTempModeOffset(-1f));
        }

        public static void DecreaseTemperatureLarge()
        {
            if (tempControl == null) return;
            AdjustTemperature(RoundedToCurrentTempModeOffset(-10f));
        }

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

            string targetTemp = MenuHelper.FormatTemperature(tempControl.TargetTemperature, "F0");

            string powerSuffix = "";
            if (tempControl.PowerTrader != null)
            {
                if (tempControl.PowerTrader.Off)
                {
                    powerSuffix = "RimWorldAccess.Building.Temp.PowerModeOffSuffix".Translate();
                }
                else if (tempControl.operatingAtHighPower)
                {
                    powerSuffix = "RimWorldAccess.Building.Temp.PowerModeHighSuffix".Translate();
                }
                else
                {
                    powerSuffix = "RimWorldAccess.Building.Temp.PowerModeLowSuffix".Translate();
                }
            }

            TolkHelper.Speak("RimWorldAccess.Building.Temp.LabelTarget".Loc(building.LabelCap, targetTemp, powerSuffix));
        }

        private static float RoundedToCurrentTempModeOffset(float celsiusTemp)
        {
            return GenTemperature.ConvertTemperatureOffset(
                Mathf.RoundToInt(GenTemperature.CelsiusToOffset(celsiusTemp, Prefs.TemperatureMode)),
                Prefs.TemperatureMode,
                TemperatureDisplayMode.Celsius);
        }
    }
}
