using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard navigation for CompRefuelable. Options are built dynamically from
    /// Props flags so buildings only expose what actually applies (e.g. a mortar's
    /// reinforced barrel shows only the view option; a fueled smelter shows all three).
    /// </summary>
    public static class RefuelableComponentState
    {
        private enum OptionKind
        {
            ViewStatus,
            ToggleAutoRefuel,
            AdjustTargetFuel,
        }

        private class Option
        {
            public OptionKind Kind;
            public string Label;
        }

        private static CompRefuelable refuelable = null;
        private static Building building = null;
        private static bool isActive = false;
        private static List<Option> options = new List<Option>();
        private static int selectedIndex = 0;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        public static void Open(Building targetBuilding)
        {
            if (!GuardHelper.RequireBuilding(targetBuilding)) return;

            CompRefuelable comp = targetBuilding.TryGetComp<CompRefuelable>();
            if (comp == null)
            {
                TolkHelper.Speak("Building does not have fuel system");
                return;
            }

            MapNavigationState.SuppressMapNavigation = true;
            building = targetBuilding;
            refuelable = comp;
            isActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();

            BuildOptions();
            TolkHelper.Speak(BuildVanillaFuelStatus());
        }

        public static void Close()
        {
            MapNavigationState.SuppressMapNavigation = false;
            refuelable = null;
            building = null;
            isActive = false;
            selectedIndex = 0;
            options.Clear();
            typeahead.ClearSearch();
        }

        private static void BuildOptions()
        {
            options.Clear();
            options.Add(new Option { Kind = OptionKind.ViewStatus, Label = "View detailed fuel status" });

            if (refuelable.Props.showAllowAutoRefuelToggle)
                options.Add(new Option { Kind = OptionKind.ToggleAutoRefuel, Label = AutoRefuelLabel() });

            if (refuelable.Props.targetFuelLevelConfigurable)
                options.Add(new Option { Kind = OptionKind.AdjustTargetFuel, Label = TargetFuelLabel() });
        }

        private static string AutoRefuelLabel()
            => $"Auto-refuel: {(refuelable.allowAutoRefuel ? "on" : "off")}";

        private static string TargetFuelLabel()
            => $"Target fuel level: {refuelable.TargetFuelLevel.ToStringDecimalIfSmall()} / {refuelable.Props.fuelCapacity.ToStringDecimalIfSmall()}";

        public static void SelectNext()
        {
            if (options.Count == 0) return;
            typeahead.ClearSearch();
            selectedIndex = MenuHelper.SelectNext(selectedIndex, options.Count);
            AnnounceCurrentOption();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        public static void SelectPrevious()
        {
            if (options.Count == 0) return;
            typeahead.ClearSearch();
            selectedIndex = MenuHelper.SelectPrevious(selectedIndex, options.Count);
            AnnounceCurrentOption();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        public static void JumpToFirst()
        {
            if (options.Count == 0) return;
            typeahead.ClearSearch();
            selectedIndex = 0;
            AnnounceCurrentOption();
        }

        public static void JumpToLast()
        {
            if (options.Count == 0) return;
            typeahead.ClearSearch();
            selectedIndex = options.Count - 1;
            AnnounceCurrentOption();
        }

        public static void ExecuteSelected()
        {
            if (refuelable == null || building == null || options.Count == 0) return;

            switch (options[selectedIndex].Kind)
            {
                case OptionKind.ViewStatus:
                    AnnounceDetailedStatus();
                    break;
                case OptionKind.ToggleAutoRefuel:
                    ToggleAutoRefuel();
                    break;
                case OptionKind.AdjustTargetFuel:
                    TolkHelper.Speak("Use Left and Right arrows to adjust. Current: "
                        + refuelable.TargetFuelLevel.ToStringDecimalIfSmall()
                        + " of " + refuelable.Props.fuelCapacity.ToStringDecimalIfSmall());
                    break;
            }
        }

        private static bool IsOnTargetFuelOption()
            => options.Count > 0 && options[selectedIndex].Kind == OptionKind.AdjustTargetFuel;

        public static void IncreaseTargetFuel()
        {
            if (refuelable == null || building == null) return;
            if (!IsOnTargetFuelOption())
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            float increment = refuelable.Props.fuelCapacity * 0.1f;
            refuelable.TargetFuelLevel += increment;
            RefreshSelectedLabel();
            TolkHelper.Speak(options[selectedIndex].Label);
        }

        public static void DecreaseTargetFuel()
        {
            if (refuelable == null || building == null) return;
            if (!IsOnTargetFuelOption())
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            float decrement = refuelable.Props.fuelCapacity * 0.1f;
            refuelable.TargetFuelLevel -= decrement;
            RefreshSelectedLabel();
            TolkHelper.Speak(options[selectedIndex].Label);
        }

        private static void ToggleAutoRefuel()
        {
            refuelable.allowAutoRefuel = !refuelable.allowAutoRefuel;
            RefreshSelectedLabel();
            TolkHelper.Speak(refuelable.allowAutoRefuel ? "on" : "off");
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }

        private static void RefreshSelectedLabel()
        {
            if (options.Count == 0) return;
            var opt = options[selectedIndex];
            switch (opt.Kind)
            {
                case OptionKind.ToggleAutoRefuel: opt.Label = AutoRefuelLabel(); break;
                case OptionKind.AdjustTargetFuel: opt.Label = TargetFuelLabel(); break;
            }
        }

        private static void AnnounceCurrentOption()
        {
            if (options.Count == 0) return;
            string label = options[selectedIndex].Label;
            string position = MenuHelper.FormatPosition(selectedIndex, options.Count);
            TolkHelper.Speak(string.IsNullOrEmpty(position) ? label : $"{label}. {position}");
        }

        /// <summary>
        /// Top-line fuel status mirroring CompRefuelable.CompInspectStringExtra — what a
        /// sighted player sees in the inspect panel. Uses ". " separators per project
        /// convention (no newlines in screen reader announcements).
        /// </summary>
        private static string BuildVanillaFuelStatus()
        {
            if (refuelable.Props.fuelIsMortarBarrel && Find.Storyteller.difficulty.classicMortars)
                return $"{building.LabelCap}. Classic mortars mode, no barrel wear.";

            var sb = new StringBuilder();
            sb.Append(refuelable.Props.FuelLabel.CapitalizeFirst()).Append(": ");
            sb.Append(refuelable.Fuel.ToStringDecimalIfSmall()).Append(" / ");
            sb.Append(refuelable.Props.fuelCapacity.ToStringDecimalIfSmall());

            if (!refuelable.Props.consumeFuelOnlyWhenUsed && refuelable.HasFuel)
            {
                int numTicks = (int)(refuelable.Fuel / refuelable.Props.fuelConsumptionRate * 60000f);
                sb.Append(" (").Append(numTicks.ToStringTicksToPeriod()).Append(")");
            }

            if (!refuelable.HasFuel && !refuelable.Props.outOfFuelMessage.NullOrEmpty())
            {
                sb.Append(". ").Append(refuelable.Props.outOfFuelMessage);
            }

            if (refuelable.Props.targetFuelLevelConfigurable)
            {
                sb.Append(". ").Append("ConfiguredTargetFuelLevel".Translate(refuelable.TargetFuelLevel.ToStringDecimalIfSmall()));
            }

            return sb.ToString();
        }

        private static void AnnounceDetailedStatus()
        {
            if (refuelable == null || building == null) return;

            var sb = new StringBuilder();
            sb.Append(BuildVanillaFuelStatus());

            if (refuelable.Props.fuelFilter != null && refuelable.Props.fuelFilter.AllowedDefCount == 1)
            {
                var fuelDef = refuelable.Props.fuelFilter.AllowedThingDefs.First();
                sb.Append(". Fuel type: ").Append(fuelDef.label);
            }

            if (refuelable.Props.showAllowAutoRefuelToggle)
            {
                sb.Append(". ").Append(AutoRefuelLabel());
            }

            TolkHelper.Speak(sb.ToString());
        }

        // Typeahead plumbing

        public static bool ProcessTypeaheadCharacter(char c)
        {
            var labels = options.Select(o => o.Label).ToList();
            if (typeahead.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0) { selectedIndex = newIndex; AnnounceWithSearch(); }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
            return true;
        }

        public static bool ProcessBackspace()
        {
            if (!typeahead.HasActiveSearch) return false;
            var labels = options.Select(o => o.Label).ToList();
            if (typeahead.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0) selectedIndex = newIndex;
                AnnounceWithSearch();
            }
            return true;
        }

        public static void ClearTypeaheadSearch()
        {
            typeahead.ClearSearchAndAnnounce();
            AnnounceCurrentOption();
        }

        public static bool SelectNextMatch()
        {
            if (!typeahead.HasActiveSearch) return false;
            int next = typeahead.GetNextMatch(selectedIndex);
            if (next >= 0) { selectedIndex = next; AnnounceWithSearch(); }
            return true;
        }

        public static bool SelectPreviousMatch()
        {
            if (!typeahead.HasActiveSearch) return false;
            int prev = typeahead.GetPreviousMatch(selectedIndex);
            if (prev >= 0) { selectedIndex = prev; AnnounceWithSearch(); }
            return true;
        }

        private static void AnnounceWithSearch()
        {
            if (options.Count == 0 || selectedIndex < 0 || selectedIndex >= options.Count) return;
            string label = options[selectedIndex].Label;
            if (typeahead.HasActiveSearch)
                TolkHelper.Speak(typeahead.BuildItemAnnouncement(label));
            else
                AnnounceCurrentOption();
        }
    }
}
