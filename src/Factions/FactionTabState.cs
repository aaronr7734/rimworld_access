using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages keyboard navigation for the in-game Factions tab (windowless).
    /// Provides a treeview of factions with expandable sections and typeahead search.
    /// Opened via F12 > Factions or any other path that activates MainTabWindow_Factions.
    /// </summary>
    public static class FactionTabState
    {
        public static bool IsActive { get; private set; }

        private static FactionTreeNavigation navigation = new FactionTreeNavigation();

        /// <summary>
        /// Opens the faction tab state and builds the faction tree.
        /// Called from FactionTabPatch when MainTabWindow_Factions opens.
        /// </summary>
        public static void Open()
        {
            if (IsActive)
                return;

            IsActive = true;
            List<Faction> factions = FactionHelper.BuildFactionList();
            navigation.Initialize(factions);
        }

        /// <summary>
        /// Closes the faction tab state and resets all fields.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            navigation.Reset();
            TolkHelper.Speak("RimWorldAccess.Factions.Tab.Closed".Translate());
        }

        /// <summary>
        /// Handles keyboard input for the faction tab.
        /// Returns true if input was handled.
        /// Called from UnifiedKeyboardPatch which handles Event.current.Use().
        /// </summary>
        public static bool HandleInput(Event ev)
        {
            if (!IsActive || ev.type != EventType.KeyDown)
                return false;

            // Delegate to shared tree navigation
            if (navigation.HandleInput(ev))
                return true;

            // Escape with no active search — close
            if (ev.keyCode == KeyCode.Escape)
            {
                Close();
                return true;
            }

            return true;
        }
    }
}
