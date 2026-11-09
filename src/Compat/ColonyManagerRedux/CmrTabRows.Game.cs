using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The row factories every threshold-tab provider (<see cref="CmrHuntingDetails"/>,
    /// <see cref="CmrForestryDetails"/>, <see cref="CmrForagingDetails"/>, <see cref="CmrMiningDetails"/>)
    /// and <see cref="CmrLivestockDetails"/>/<see cref="CmrProductionDetails"/> share: the toggle row,
    /// the shortcut rows above a def list, the area-strip combo box, the threshold section's own run
    /// of rows, and the refresh-plus-padlock icon pair, called with each tab's own keys and facade
    /// delegates.
    /// </summary>
    internal static class CmrTabRows
    {
        // ------------------------------------------------------------------
        // Toggle: a checkbox row over a plain bool.
        // ------------------------------------------------------------------

        public static CmrDetailRow Toggle(string section, string labelKey, Func<bool> get, Action<bool> set,
            string tipKey = null)
        {
            return new CmrDetailRow
            {
                Label = ModText(labelKey),
                Tooltip = ModTip(tipKey ?? labelKey + ".Tip"),
                Role = ElementRole.Checkbox,
                SectionTitle = section,
                Check = () => get() ? CheckState.Checked : CheckState.Unchecked,
                Activate = () => set(!get()),
            };
        }

        // ------------------------------------------------------------------
        // Shortcut rows: the mod's own group toggles above a def list.
        // ------------------------------------------------------------------

        public static void AddShortcutRows<T>(CmrDetailRegion region, List<T> all,
            List<CmrShortcutCategory<T>> categories, Func<T, bool> isAllowed, Action<T, bool> setAllowed,
            bool includeAllRow = true)
        {
            if (includeAllRow)
            {
                AddShortcutRow(region, all, all, ModText("ColonyManagerRedux.Shortcuts.All"),
                    ModText("ColonyManagerRedux.Shortcuts.All.Tip"), true, isAllowed, setAllowed);
            }
            for (int c = 0; c < categories.Count; c++)
            {
                var matching = new List<T>();
                for (int i = 0; i < all.Count; i++)
                {
                    if (categories[c].Predicate(all[i]))
                    {
                        matching.Add(all[i]);
                    }
                }
                AddShortcutRow(region, matching, all, categories[c].Label, categories[c].Tip, false,
                    isAllowed, setAllowed);
            }
        }

        /// <summary>The mod's mouse-modifier tooltip line is replaced by its keyboard twin.</summary>
        public static void AddShortcutRow<T>(CmrDetailRegion region, List<T> matching, List<T> all,
            string label, string tip, bool isAllRow, Func<T, bool> isAllowed, Action<T, bool> setAllowed)
        {
            if (!CmrCompat.Shortcuts.RowVisible(matching.Count, all.Count, isAllRow))
            {
                return;
            }
            Action<CmrShortcutClick> click = mode => CmrCompat.Shortcuts.Apply(matching, all, setAllowed, mode,
                CmrSubsetToggle.ValueForNextClick(matching.Count, AllowedCount(matching, isAllowed)));
            region.Rows.Add(new CmrDetailRow
            {
                Label = label,
                Tooltip = () => CompatText.JoinSentences(new List<string>
                {
                    Flatten(tip),
                    "RimWorldAccess.Cmr.Shortcuts.ModifiersTip".Translate(
                        KeyboardHelper.CtrlLabelFor(KeyCode.Return)).ToString(),
                }),
                Role = ElementRole.Checkbox,
                Check = () => CmrSubsetToggle.StateOf(matching.Count, AllowedCount(matching, isAllowed)),
                Activate = () => click(CmrShortcutClick.Toggle),
                ShortcutClick = click,
            });
        }

        private static int AllowedCount<T>(List<T> subset, Func<T, bool> isAllowed)
        {
            int allowed = 0;
            for (int i = 0; i < subset.Count; i++)
            {
                if (isAllowed(subset[i]))
                {
                    allowed++;
                }
            }
            return allowed;
        }

        // ------------------------------------------------------------------
        // Area row: the single-select area strip as a combo box (AreaAllowedGUI.cs:87-118).
        // ------------------------------------------------------------------

        public static CmrDetailRow AreaRow(string label, Func<Area> read, Action<Area> write, Func<Map> map,
            Func<string> tooltip = null, string sectionTitle = null)
        {
            return new CmrDetailRow
            {
                Label = label,
                Tooltip = tooltip,
                Role = ElementRole.ComboBox,
                SectionTitle = sectionTitle,
                Value = () => AreaLabel(read()),
                CanAdjust = direction => CanCycleArea(map(), read(), direction),
                Adjust = direction => CycleArea(map(), read(), write, direction),
                Choices = () => AreaChoices(map(), write),
            };
        }

        /// <summary>The strip's own cells: the unrestricted one first, then every assignable area on the map.</summary>
        private static List<Area> AreaOptions(Map map)
        {
            var areas = new List<Area> { null };
            if (map != null)
            {
                List<Area> all = map.areaManager.AllAreas;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].AssignableAsAllowed())
                    {
                        areas.Add(all[i]);
                    }
                }
            }
            return areas;
        }

        private static List<CmrPickerChoice> AreaChoices(Map map, Action<Area> write)
        {
            List<Area> areas = AreaOptions(map);
            var choices = new List<CmrPickerChoice>(areas.Count);
            for (int i = 0; i < areas.Count; i++)
            {
                Area area = areas[i];
                choices.Add(new CmrPickerChoice(AreaLabel(area), () => write(area)));
            }
            return choices;
        }

        private static bool CanCycleArea(Map map, Area current, int direction)
        {
            List<Area> areas = AreaOptions(map);
            int index = areas.IndexOf(current);
            if (index < 0)
            {
                return areas.Count > 0;
            }
            return direction < 0 ? index > 0 : index < areas.Count - 1;
        }

        private static void CycleArea(Map map, Area current, Action<Area> write, int direction)
        {
            List<Area> areas = AreaOptions(map);
            if (areas.Count == 0)
            {
                return;
            }
            int index = areas.IndexOf(current);
            // A job pointing at a deleted area snaps to unrestricted from either direction.
            int next = index < 0 ? 0 : Mathf.Clamp(index + direction, 0, areas.Count - 1);
            write(areas[next]);
        }

        /// <summary>The strip's own cell label, from the game's own area labelling (AreaAllowedGUI.cs:255).</summary>
        public static string AreaLabel(Area area)
        {
            return AreaUtility.AreaAllowedLabel_Area(area).StripTags();
        }

        // ------------------------------------------------------------------
        // Threshold section: the run every threshold-trigger tab draws around its own trigger.
        // ------------------------------------------------------------------

        /// <summary>
        /// The threshold section's own row run, shared by every trigger-driven tab
        /// (<see cref="CmrHuntingDetails"/>, <see cref="CmrForestryDetails"/>, <see cref="CmrForagingDetails"/>,
        /// <see cref="CmrMiningDetails"/>): the opener label/tooltip and the stepper's own value are the
        /// tab's own composed wording (<c>ThresholdText</c> stays with each provider, since its argument
        /// list differs tab to tab), passed in already composed; everything else -- the count's own
        /// 0..MaxUpperThreshold range, AllowAnyThreshold, CountAllOnMap, and the reach/path pair -- is the
        /// shared trigger base every threshold job carries. Returns the section title so a caller with
        /// trailing rows of its own (Hunting's meat shortcuts) can keep grouping under it.
        /// </summary>
        public static string AddThresholdRows(CmrDetailRegion region, object job, string tabKeyPrefix,
            string targetCountRowLabelKey, Func<string> openerLabel, Func<string> openerTooltip,
            Action openDetails, Func<string> targetLabel, Func<bool> syncGet, Action<bool> syncSet,
            bool reachBeforePath)
        {
            string section = ModText("ColonyManagerRedux.Threshold");

            region.Rows.Add(new CmrDetailRow
            {
                Label = openerLabel(),
                Tooltip = openerTooltip,
                Role = ElementRole.Button,
                SectionTitle = section,
                Activate = openDetails,
                OpensWindow = true,
            });

            region.Rows.Add(new CmrDetailRow
            {
                Label = targetCountRowLabelKey.Translate(),
                Role = ElementRole.Stepper,
                SectionTitle = section,
                Value = targetLabel,
                CanAdjust = direction => CanStepThresholdCount(job, direction),
                Adjust = direction => StepThresholdCount(job, direction),
                Numeric = new CmrNumericSpec
                {
                    Min = 0,
                    Max = CmrCompat.Threshold.JobMaxUpperThreshold(job),
                    Current = () => CmrCompat.Threshold.JobTargetCount(job),
                    Apply = value => CmrCompat.Threshold.SetJobTargetCount(job, value),
                },
            });

            region.Rows.Add(Toggle(section, "ColonyManagerRedux.Threshold.AllowAnyThreshold",
                () => CmrCompat.Threshold.JobAllowAnyThreshold(job),
                value => CmrCompat.Threshold.SetJobAllowAnyThreshold(job, value)));
            region.Rows.Add(Toggle(section, "ColonyManagerRedux.Threshold.CountAllOnMap",
                () => CmrCompat.Threshold.JobCountAllOnMap(job),
                value => CmrCompat.Threshold.SetJobCountAllOnMap(job, value)));

            // The mod pairs the shared "synchronize threshold" label with a tab-specific tip.
            region.Rows.Add(Toggle(section, "ColonyManagerRedux.SyncFilterAndAllowed", syncGet, syncSet,
                tipKey: "ColonyManagerRedux." + tabKeyPrefix + ".SyncFilterAndAllowed.Tip"));

            if (reachBeforePath)
            {
                AddReachabilityRow(region, section, job);
                AddPathBasedDistanceRow(region, section, job);
            }
            else
            {
                AddPathBasedDistanceRow(region, section, job);
                AddReachabilityRow(region, section, job);
            }

            return section;
        }

        private static void AddReachabilityRow(CmrDetailRegion region, string section, object job)
        {
            region.Rows.Add(Toggle(section, "ColonyManagerRedux.Threshold.CheckReachability",
                () => CmrCompat.JobBase.ShouldCheckReachable(job),
                value => CmrCompat.JobBase.SetShouldCheckReachable(job, value)));
        }

        private static void AddPathBasedDistanceRow(CmrDetailRegion region, string section, object job)
        {
            region.Rows.Add(Toggle(section, "ColonyManagerRedux.Threshold.PathBasedDistance",
                () => CmrCompat.JobBase.UsePathBasedDistance(job),
                value => CmrCompat.JobBase.SetUsePathBasedDistance(job, value)));
        }

        private static bool CanStepThresholdCount(object job, int direction)
        {
            int count = CmrCompat.Threshold.JobTargetCount(job);
            return direction < 0 ? count > 0 : count < CmrCompat.Threshold.JobMaxUpperThreshold(job);
        }

        /// <summary>One keyboard step along the same 0..MaxUpperThreshold range the mod's slider spans.</summary>
        private static void StepThresholdCount(object job, int direction)
        {
            int max = CmrCompat.Threshold.JobMaxUpperThreshold(job);
            float stepped = SliderStep.Stepped(CmrCompat.Threshold.JobTargetCount(job), direction, 0f, max, -1f);
            CmrCompat.Threshold.SetJobTargetCount(job, Mathf.RoundToInt(stepped));
        }

        // ------------------------------------------------------------------
        // Refresh and padlock: the icon pair a def-list column carries.
        // ------------------------------------------------------------------

        public static void AddRefreshAndLock(CmrDetailRegion region, string refreshLabelKey,
            string refreshedKey, Action refresh, string lockLabelKey, Func<bool> lockGet,
            Action<bool> lockSet)
        {
            region.Rows.Add(new CmrDetailRow
            {
                Label = refreshLabelKey.Translate(),
                Role = ElementRole.Button,
                Confirmation = refreshedKey.Translate(),
                Activate = refresh,
            });
            region.Rows.Add(LockRow(lockLabelKey, lockGet, lockSet));
        }

        /// <summary>The padlock: the mod ships no hover text for either icon, so both carry our own name.</summary>
        public static CmrDetailRow LockRow(string lockLabelKey, Func<bool> lockGet, Action<bool> lockSet)
        {
            return new CmrDetailRow
            {
                Label = lockLabelKey.Translate(),
                Role = ElementRole.Checkbox,
                Check = () => lockGet() ? CheckState.Checked : CheckState.Unchecked,
                Activate = () => lockSet(!lockGet()),
            };
        }
    }
}
