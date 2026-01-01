using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages a submenu for editing hit points or quality ranges.
    /// Allows separate adjustment of min and max values.
    /// </summary>
    public static class RangeEditMenuState
    {
        public enum RangeType
        {
            HitPoints,
            Quality
        }

        private static bool isActive = false;
        private static RangeType currentRangeType;
        private static int selectedOption = 0; // 0 = min, 1 = max
        private static FloatRange hitPointsRange;
        private static QualityRange qualityRange;

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens the range edit submenu for hit points.
        /// </summary>
        public static void OpenHitPointsRange(FloatRange currentRange)
        {
            isActive = true;
            currentRangeType = RangeType.HitPoints;
            hitPointsRange = currentRange;
            selectedOption = 0;

            AnnounceCurrentSelection();
            Log.Message("Opened hit points range editor");
        }

        /// <summary>
        /// Opens the range edit submenu for quality.
        /// </summary>
        public static void OpenQualityRange(QualityRange currentRange)
        {
            isActive = true;
            currentRangeType = RangeType.Quality;
            qualityRange = currentRange;
            selectedOption = 0;

            AnnounceCurrentSelection();
            Log.Message("Opened quality range editor");
        }

        /// <summary>
        /// Closes the range edit submenu without applying changes.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            selectedOption = 0;
        }

        /// <summary>
        /// Applies the changes and returns the updated range.
        /// Returns true if changes were applied.
        /// </summary>
        public static bool ApplyAndClose(out FloatRange hitPoints, out QualityRange quality)
        {
            hitPoints = hitPointsRange;
            quality = qualityRange;
            bool wasActive = isActive;
            Close();
            return wasActive;
        }

        /// <summary>
        /// Toggles between min and max selection.
        /// </summary>
        public static void ToggleSelection()
        {
            selectedOption = (selectedOption == 0) ? 1 : 0;
            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Moves selection up (same as toggle for 2-item menu).
        /// </summary>
        public static void SelectPrevious()
        {
            ToggleSelection();
        }

        /// <summary>
        /// Moves selection down (same as toggle for 2-item menu).
        /// </summary>
        public static void SelectNext()
        {
            ToggleSelection();
        }

        /// <summary>
        /// Increases the selected value (right arrow).
        /// </summary>
        public static void IncreaseValue()
        {
            if (currentRangeType == RangeType.HitPoints)
            {
                float step = 0.1f;
                if (selectedOption == 0) // Min
                {
                    hitPointsRange.min = Mathf.Min(hitPointsRange.max - step, hitPointsRange.min + step);
                }
                else // Max
                {
                    hitPointsRange.max = Mathf.Min(1f, hitPointsRange.max + step);
                }
            }
            else // Quality
            {
                QualityCategory[] qualities = (QualityCategory[])Enum.GetValues(typeof(QualityCategory));
                if (selectedOption == 0) // Min
                {
                    int minIndex = Array.IndexOf(qualities, qualityRange.min);
                    if (minIndex < Array.IndexOf(qualities, qualityRange.max))
                    {
                        qualityRange.min = qualities[minIndex + 1];
                    }
                }
                else // Max
                {
                    int maxIndex = Array.IndexOf(qualities, qualityRange.max);
                    if (maxIndex < qualities.Length - 1)
                    {
                        qualityRange.max = qualities[maxIndex + 1];
                    }
                }
            }

            AnnounceCurrentSelection();
        }

        /// <summary>
        /// Decreases the selected value (left arrow).
        /// </summary>
        public static void DecreaseValue()
        {
            if (currentRangeType == RangeType.HitPoints)
            {
                float step = 0.1f;
                if (selectedOption == 0) // Min
                {
                    hitPointsRange.min = Mathf.Max(0f, hitPointsRange.min - step);
                }
                else // Max
                {
                    hitPointsRange.max = Mathf.Max(hitPointsRange.min + step, hitPointsRange.max - step);
                }
            }
            else // Quality
            {
                QualityCategory[] qualities = (QualityCategory[])Enum.GetValues(typeof(QualityCategory));
                if (selectedOption == 0) // Min
                {
                    int minIndex = Array.IndexOf(qualities, qualityRange.min);
                    if (minIndex > 0)
                    {
                        qualityRange.min = qualities[minIndex - 1];
                    }
                }
                else // Max
                {
                    int maxIndex = Array.IndexOf(qualities, qualityRange.max);
                    if (maxIndex > Array.IndexOf(qualities, qualityRange.min))
                    {
                        qualityRange.max = qualities[maxIndex - 1];
                    }
                }
            }

            AnnounceCurrentSelection();
        }

        private static void AnnounceCurrentSelection()
        {
            string announcement;

            if (currentRangeType == RangeType.HitPoints)
            {
                string optionName = selectedOption == 0 ? "Minimum" : "Maximum";
                float value = selectedOption == 0 ? hitPointsRange.min : hitPointsRange.max;
                announcement = $"Hit Points {optionName}: {value:P0}. Range: {hitPointsRange.min:P0} to {hitPointsRange.max:P0}";
            }
            else
            {
                string optionName = selectedOption == 0 ? "Minimum" : "Maximum";
                QualityCategory value = selectedOption == 0 ? qualityRange.min : qualityRange.max;
                announcement = $"Quality {optionName}: {value}. Range: {qualityRange.min} to {qualityRange.max}";
            }

            TolkHelper.Speak(announcement);
        }
    }
}
