using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    internal static partial class ClCompat
    {
        /// <summary>
        /// The mod's settings page pieces the generic reader cannot see on its own (the inter-game
        /// tag panel's raw text box and its two side-by-side tag lists), and the panic toggle the mod
        /// adds to the play-settings strip.
        /// </summary>
        internal static class Settings
        {
            private static readonly MethodInfo inputFieldMethod;
            private static readonly MethodInfo interGameDrawMethod;
            private static readonly MethodInfo tagSummaryViewMethod;
            private static readonly MethodInfo togglePanicModeMethod;
            private static readonly bool ready;

            static Settings()
            {
                var surface = new ReflectionSurface("ClCompat.Settings");
                Type guiUtilityType = surface.Type("Inventory.GUIUtility");
                Type panelType = surface.Type("Inventory.Panel_InterGameSettingsPanel");

                inputFieldMethod = surface.Method(guiUtilityType, "InputField",
                    new[] { typeof(Rect), typeof(string), typeof(string).MakeByRefType(), typeof(bool) });
                togglePanicModeMethod = surface.Method(managerType, "TogglePanicMode", new[] { typeof(bool?) });
                if (tagType != null)
                {
                    Type tagList = typeof(System.Collections.Generic.List<>).MakeGenericType(tagType);
                    interGameDrawMethod = surface.Method(panelType, "Draw",
                        new[] { typeof(Rect).MakeByRefType(), tagList, tagList });
                    tagSummaryViewMethod = surface.Method(panelType, "DrawTagSummaryView",
                        new[] { typeof(Rect).MakeByRefType(), tagList, typeof(Vector2).MakeByRefType(), typeof(bool) });
                }

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The mod's raw GUI.TextField wrapper, shared by every one of its windows.</summary>
            public static MethodInfo InputField
            {
                get { return ready ? inputFieldMethod : null; }
            }

            /// <summary>The inter-game tag panel's draw, the only caller whose text box the generic reader handles.</summary>
            public static MethodInfo InterGameDraw
            {
                get { return ready ? interGameDrawMethod : null; }
            }

            /// <summary>Draws one of the panel's two tag lists; its last argument is true for the loaded list.</summary>
            public static MethodInfo TagSummaryView
            {
                get { return ready ? tagSummaryViewMethod : null; }
            }

            /// <summary>Flips the panic state on the current map, as the play-settings button does.</summary>
            public static void TogglePanicMode()
            {
                if (ManagerLoaded())
                {
                    Guarded.Call(togglePanicModeMethod, null, "ClCompat.Settings.TogglePanicMode", new object[] { null });
                }
            }
        }
    }
}
