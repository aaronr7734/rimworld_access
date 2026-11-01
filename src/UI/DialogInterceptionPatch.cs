using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Redirects a small set of game-spawned FloatMenus into the accessible
    /// windowless float menu before they reach the window stack. Dialogs are
    /// no longer intercepted anywhere: message boxes, node trees, renames,
    /// naming dialogs and owner assignment are all real windows driven by
    /// their FocusScope shells via the ScopeForWindow mirror (waves A/B).
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class DialogInterceptionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Window window)
        {
            // Skip if no window
            if (window == null)
                return true;

            // A material-menu harvest is running: capture the designator's stuff menu
            // instead of letting it open (see MaterialMenuHarvest).
            if (window is FloatMenu harvestedMenu && MaterialMenuHarvest.InFlight)
            {
                MaterialMenuHarvest.NotifyIntercepted(harvestedMenu);
                return false;
            }

            // A gizmo handler is listing a mod's right-click menu as options (see FloatMenuHarvest).
            if (window is FloatMenu capturedMenu && FloatMenuHarvest.InFlight)
            {
                FloatMenuHarvest.NotifyIntercepted(capturedMenu);
                return false;
            }

            // Redirect FloatMenus spawned by the ideoligion builder's typed-precept "Add"
            // flow (and any nested grouping menus) into the accessible windowless menu.
            if (window is FloatMenu builderFloatMenu && IdeoTypedPreceptState.IsActive)
            {
                var optionsField2 = typeof(FloatMenu).GetField("options",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var opts2 = optionsField2?.GetValue(builderFloatMenu) as System.Collections.Generic.List<FloatMenuOption>;
                if (opts2 != null && opts2.Count > 0)
                {
                    WindowlessFloatMenuState.Open(opts2, builderFloatMenu.givesColonistOrders, playOpenSound: false);
                    return false;
                }
                return true;
            }

            // Special handling for FloatMenu when:
            // 1. Executing a gizmo (e.g., long-range scanner mineral selection)
            // 2. Confirming a transport pod destination (arrival options)
            // 3. Running a windowless menu option that opens its own vanilla FloatMenu
            //    (e.g. Vehicle Framework's turret reload ammo picker)
            // 4. Confirming an external world-targeting destination (e.g. Vehicle
            //    Framework aerial launch arrival options — see ExternalWorldTargeting)
            // 5. Activating a captured inspect-tab control: the armed pass runs a
            //    vanilla tab handler offscreen, and a FloatMenu it opens must ride
            //    the windowless path like every other inspection-origin menu.
            // 6. Activating a main-menu option whose captured vanilla delegate opens
            //    its own FloatMenu (e.g. BuySoundtrack's soundtrack picker).
            // 7. A Character Editor keyboard action invoking one of that mod's own
            //    delegates, which other mods patch to open FloatMenus (Vanilla
            //    Skills Expanded's passion picker) — a real FloatMenu far from the
            //    mouse self-closes before a keyboard user can reach it.
            // 8. A compat scope reflectively invoking a mod delegate that may open its
            //    own FloatMenu (e.g. RimTalk-family Discuss buttons) — see ScopeDelegateGuard.
            if (window is FloatMenu floatMenu &&
                (GizmoNavigationState.IsExecutingGizmo || TransportPodLaunchState.IsConfirmingDestination
                    || WindowlessFloatMenuState.IsExecutingOption || ExternalWorldTargeting.IsConfirming
                    || InspectTabCaptureService.ActivationInFlight || MenuNavigationState.IsExecutingOption
                    || CharEditorCompat.DelegateInFlight || RimWorldAccess.Shell.ScopeDelegateGuard.InFlight))
            {
                // Extract options from the FloatMenu and open windowless version
                var optionsField = typeof(FloatMenu).GetField("options",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (optionsField != null)
                {
                    var options = optionsField.GetValue(floatMenu) as System.Collections.Generic.List<FloatMenuOption>;
                    if (options != null && options.Count > 0)
                    {
                        TransportPodLaunchState.ClearConfirmingFlag();
                        ExternalWorldTargeting.IsConfirming = false;

                        // Grouped members may open one menu each: the last wins, announced once after.
                        bool deferAnnouncement = GizmoNavigationState.IsExecutingGizmo;
                        WindowlessFloatMenuState.Open(options, floatMenu.givesColonistOrders, playOpenSound: false,
                            announceFirst: !deferAnnouncement);
                        if (deferAnnouncement)
                            GizmoNavigationState.NoteMenuInterceptedDuringExecution();
                        return false; // Prevent FloatMenu from being added
                    }
                }

                // Clear flags even if we couldn't extract options
                TransportPodLaunchState.ClearConfirmingFlag();
                ExternalWorldTargeting.IsConfirming = false;

                // Fallback: let it through if we couldn't extract options
                return true;
            }

            return true;
        }
    }
}
