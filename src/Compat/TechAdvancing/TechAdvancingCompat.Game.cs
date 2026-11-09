using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>Reflection surface for Tech Advancing and the installs of its capture patches.</summary>
    internal static class TechAdvancingCompat
    {
        private static ConstructorInfo configWindowCtor;
        private static FieldInfo worldSaveHandlerField;
        private static MethodInfo drawTextMethod;
        private static MethodInfo settingsContentsMethod;

        internal static readonly LazyReflectionGate Gate =
            new LazyReflectionGate("Tech Advancing", surface =>
            {
                Type window = surface.Type("TechAdvancing.TechAdvancing_Config_Tab");
                Type settings = surface.Type("TechAdvancing.Settings");
                ConstructorInfo ctor = surface.Constructor(window, Type.EmptyTypes);
                FieldInfo handler = surface.Field(window, "worldCompSaveHandler");
                MethodInfo drawText = surface.Method(window, "DrawText",
                    new[] { typeof(Rect), typeof(string), typeof(float).MakeByRefType(), typeof(bool), typeof(Color?) });
                MethodInfo contents = surface.Method(settings, "DoSettingsWindowContents", new[] { typeof(Rect) });
                if (!surface.Ready)
                {
                    return false;
                }
                configWindowCtor = ctor;
                worldSaveHandlerField = handler;
                drawTextMethod = drawText;
                settingsContentsMethod = contents;
                return true;
            });

        internal static void InstallCapture(Harmony harmony)
        {
            harmony.Patch(drawTextMethod,
                prefix: new HarmonyMethod(typeof(TechAdvancingConfigTextCapture), nameof(TechAdvancingConfigTextCapture.Prefix)));
            harmony.Patch(settingsContentsMethod,
                transpiler: new HarmonyMethod(typeof(TechAdvancingSettingsCapture), nameof(TechAdvancingSettingsCapture.Transpiler)));
        }

        /// <summary>The window's PreOpen loads from the world component, which exists only in a running game.</summary>
        internal static bool CanOpenConfig()
        {
            return Current.ProgramState == ProgramState.Playing && worldSaveHandlerField.GetValue(null) != null;
        }

        /// <summary>The research tab's config button, verbatim (TA_Research_Menu_Patch.Prefix).</summary>
        internal static void OpenConfig()
        {
            SoundDef.Named("ResearchStart").PlayOneShotOnCamera(null);
            Find.WindowStack.Add((Window)configWindowCtor.Invoke(null));
        }
    }
}
