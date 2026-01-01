using System.Collections.Generic;
using RimWorld;

namespace RimWorldAccess
{
    public static class ScenarioNavigationState
    {
        private static bool initialized = false;
        private static int selectedIndex = 0;
        private static List<Scenario> flatScenarioList = new List<Scenario>();

        public static void Initialize(List<Scenario> scenarios)
        {
            if (!initialized || flatScenarioList.Count != scenarios.Count)
            {
                flatScenarioList = new List<Scenario>(scenarios);
                selectedIndex = 0;
                initialized = true;
            }
        }

        public static void Reset()
        {
            initialized = false;
            selectedIndex = 0;
            flatScenarioList.Clear();
        }

        public static int SelectedIndex
        {
            get { return selectedIndex; }
        }

        public static Scenario SelectedScenario
        {
            get
            {
                if (flatScenarioList.Count == 0 || selectedIndex < 0 || selectedIndex >= flatScenarioList.Count)
                    return null;
                return flatScenarioList[selectedIndex];
            }
        }

        public static int ScenarioCount
        {
            get { return flatScenarioList.Count; }
        }

        public static void NavigateUp()
        {
            if (flatScenarioList.Count == 0) return;

            selectedIndex--;
            if (selectedIndex < 0)
                selectedIndex = flatScenarioList.Count - 1;

            CopySelectedToClipboard();
        }

        public static void NavigateDown()
        {
            if (flatScenarioList.Count == 0) return;

            selectedIndex++;
            if (selectedIndex >= flatScenarioList.Count)
                selectedIndex = 0;

            CopySelectedToClipboard();
        }

        private static void CopySelectedToClipboard()
        {
            Scenario selected = SelectedScenario;
            if (selected == null) return;

            string categoryPrefix = GetCategoryPrefix(selected);
            string text = $"{categoryPrefix}{selected.name} - {selected.summary}";

            TolkHelper.Speak(text);
        }

        private static string GetCategoryPrefix(Scenario scenario)
        {
            switch (scenario.Category)
            {
                case ScenarioCategory.FromDef:
                    return "[Built-in] ";
                case ScenarioCategory.CustomLocal:
                    return "[Custom] ";
                case ScenarioCategory.SteamWorkshop:
                    return "[Workshop] ";
                default:
                    return "";
            }
        }

        public static Scenario GetScenarioAtIndex(int index)
        {
            if (index < 0 || index >= flatScenarioList.Count)
                return null;
            return flatScenarioList[index];
        }
    }
}
