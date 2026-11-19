#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Real keyboard input for the dev bridge on 1.7, through Waylith.Windowing's own
    /// <c>IInputInjector</c>. Unlike <see cref="Inject"/>, an injected key travels the whole game
    /// path (platform key event, text tap, ImguiDispatch, every window's GUI pass), so vanilla
    /// window key handling and typed characters are exercised too. Works with the game unfocused
    /// and hidden: keys route to the main window, and <see cref="KeepDrawingInBackground"/> forces
    /// the GUI passes 1.7 otherwise skips while occluded. Unavailable on 1.6.
    /// </summary>
    public static partial class ShellDev
    {
        private static bool realInputResolved;
        private static object injector, mainWindow;
        private static Type physicalKeyType;
        private static MethodInfo injectKeyDown, injectKeyDownThroughKeymap, injectKeyUp, injectSetFocused, requestRedraw;

        internal static bool RealInputAvailable
        {
            get
            {
                ResolveRealInput();
                return injector != null;
            }
        }

        private static void ResolveRealInput()
        {
            if (realInputResolved)
                return;
            realInputResolved = true;

            Assembly engineAssembly = null, windowingAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (name == "Waylith")
                    engineAssembly = assembly;
                else if (name == "Waylith.Windowing")
                    windowingAssembly = assembly;
            }
            if (engineAssembly == null || windowingAssembly == null)
                return;

            Type engineType = engineAssembly.GetType("Waylith.EngineApplication");
            physicalKeyType = windowingAssembly.GetType("Waylith.Windowing.PhysicalKey");
            Type injectorType = windowingAssembly.GetType("Waylith.Windowing.IInputInjector");
            Type windowType = windowingAssembly.GetType("Waylith.Windowing.IWindow");
            object engineInstance = engineType?.GetProperty("Instance")?.GetValue(null);
            object platform = engineInstance == null ? null : engineType.GetProperty("WindowSystem")?.GetValue(engineInstance);
            if (platform == null || physicalKeyType == null || injectorType == null)
                return;

            var windows = platform.GetType().GetProperty("Windows")?.GetValue(platform) as System.Collections.IList;
            mainWindow = windows != null && windows.Count > 0 ? windows[0] : null;
            Type timeSpanArg = typeof(TimeSpan?);
            injectKeyDown = injectorType.GetMethod("KeyDown", new[] { physicalKeyType, timeSpanArg });
            injectKeyDownThroughKeymap = injectorType.GetMethod("KeyDownThroughKeymap", new[] { physicalKeyType, timeSpanArg });
            injectKeyUp = injectorType.GetMethod("KeyUp", new[] { physicalKeyType, timeSpanArg });
            injectSetFocused = injectorType.GetMethod("SetFocused", new[] { windowType, typeof(bool) });
            requestRedraw = windowType.GetMethod("RequestRedraw", Type.EmptyTypes);
            if (mainWindow != null && injectKeyDown != null && injectKeyDownThroughKeymap != null && injectKeyUp != null && injectSetFocused != null)
                injector = platform.GetType().GetProperty("InputInjector")?.GetValue(platform);
        }

        /// <summary>Asks the engine to render next frame even while occluded, so GUI passes keep running.</summary>
        internal static void KeepDrawingInBackground()
        {
            ResolveRealInput();
            if (mainWindow != null)
                requestRedraw?.Invoke(mainWindow, null);
        }

        private static readonly Dictionary<KeyCode, string> PhysicalKeyRenames = new Dictionary<KeyCode, string>
        {
            { KeyCode.Return, "Enter" }, { KeyCode.KeypadEnter, "NumpadEnter" },
            { KeyCode.UpArrow, "ArrowUp" }, { KeyCode.DownArrow, "ArrowDown" },
            { KeyCode.LeftArrow, "ArrowLeft" }, { KeyCode.RightArrow, "ArrowRight" },
            { KeyCode.LeftBracket, "BracketLeft" }, { KeyCode.RightBracket, "BracketRight" },
            { KeyCode.Equals, "Equal" }, { KeyCode.BackQuote, "Backquote" },
            { KeyCode.KeypadPlus, "NumpadAdd" }, { KeyCode.KeypadMinus, "NumpadSubtract" },
            { KeyCode.KeypadMultiply, "NumpadMultiply" }, { KeyCode.KeypadDivide, "NumpadDivide" },
            { KeyCode.KeypadPeriod, "NumpadDecimal" },
            { KeyCode.LeftControl, "ControlLeft" }, { KeyCode.RightControl, "ControlRight" },
            { KeyCode.LeftAlt, "AltLeft" }, { KeyCode.RightAlt, "AltRight" },
            { KeyCode.LeftShift, "ShiftLeft" }, { KeyCode.RightShift, "ShiftRight" },
        };

        /// <summary>The Waylith PhysicalKey for a Unity KeyCode, boxed, or null when there is none.</summary>
        private static object ToPhysicalKey(KeyCode key)
        {
            string name;
            if (!PhysicalKeyRenames.TryGetValue(key, out name))
            {
                string unity = key.ToString();
                if (key >= KeyCode.A && key <= KeyCode.Z)
                    name = "Key" + unity;
                else if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                    name = "Digit" + unity.Substring(5);
                else if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                    name = "Numpad" + unity.Substring(6);
                else
                    name = unity;
            }
            return Enum.IsDefined(physicalKeyType, name) ? Enum.Parse(physicalKeyType, name) : null;
        }

        private static void InvokeInjector(MethodInfo method, object physicalKey)
        {
            injectSetFocused.Invoke(injector, new[] { mainWindow, (object)true });
            method.Invoke(injector, new[] { physicalKey, null });
        }

        /// <summary>Presses (down) or releases (up) a modifier key; held across frames by the caller.</summary>
        private static string RealModifier(KeyCode modifier, bool down)
        {
            object physical = ToPhysicalKey(modifier);
            InvokeInjector(down ? injectKeyDown : injectKeyUp, physical);
            return null;
        }

        /// <summary>One full press of a non-modifier key, translated through the active keyboard layout.</summary>
        private static string RealTap(KeyCode key, string label)
        {
            object physical = ToPhysicalKey(key);
            if (physical == null)
                return label + " -> no physical key for " + key;
            InvokeInjector(injectKeyDownThroughKeymap, physical);
            InvokeInjector(injectKeyUp, physical);
            return label + " -> real";
        }

        /// <summary>
        /// The physical key (and whether Shift is needed) that types <paramref name="c"/> on a US
        /// layout, for real-mode "text:" steps. False for anything else; those fall back to
        /// <see cref="InjectText"/>.
        /// </summary>
        private static bool TryKeyForCharacter(char c, out KeyCode key, out bool shift)
        {
            shift = false;
            key = KeyCode.None;
            if (c >= 'a' && c <= 'z')
                key = KeyCode.A + (c - 'a');
            else if (c >= 'A' && c <= 'Z')
            {
                key = KeyCode.A + (c - 'A');
                shift = true;
            }
            else if (c >= '0' && c <= '9')
                key = KeyCode.Alpha0 + (c - '0');
            else if (c == ' ')
                key = KeyCode.Space;
            return key != KeyCode.None;
        }
    }
}
#endif
