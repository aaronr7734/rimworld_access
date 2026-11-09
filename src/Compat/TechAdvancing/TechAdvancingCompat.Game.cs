using System;
using System.Reflection;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>Reflection surface for Tech Advancing.</summary>
    internal static class TechAdvancingCompat
    {
        private static Type configWindowType;
        private static ConstructorInfo configWindowCtor;
        private static FieldInfo worldSaveHandlerField;
        private static FieldInfo menuButtonSelectedField;

        internal static readonly LazyReflectionGate Gate =
            new LazyReflectionGate("Tech Advancing", surface =>
            {
                Type window = surface.Type("TechAdvancing.TechAdvancing_Config_Tab");
                ConstructorInfo ctor = surface.Constructor(window, Type.EmptyTypes);
                FieldInfo handler = surface.Field(window, "worldCompSaveHandler");
                FieldInfo page = surface.Field(window, "menuButtonSelected");
                if (!surface.Ready)
                {
                    return false;
                }
                configWindowType = window;
                configWindowCtor = ctor;
                worldSaveHandlerField = handler;
                menuButtonSelectedField = page;
                return true;
            });

        /// <summary>The window's PreOpen loads from the world component, which exists only in a running game.</summary>
        internal static bool CanOpenConfig()
        {
            return Current.ProgramState == ProgramState.Playing && worldSaveHandlerField.GetValue(null) != null;
        }

        /// <summary>
        /// The config window's page buttons read as tabs. The field holds the current page's
        /// translation key, the same key the mod translates to caption that page's button.
        /// </summary>
        internal static void RegisterPageButtons()
        {
            WindowPageButtonRegistry.Register(configWindowType, window =>
            {
                string key = menuButtonSelectedField.GetValue(window) as string;
                return key == null ? null : key.Translate().ToString();
            });
        }

        /// <summary>The research tab's config button, verbatim (TA_Research_Menu_Patch.Prefix).</summary>
        internal static void OpenConfig()
        {
            SoundDef.Named("ResearchStart").PlayOneShotOnCamera(null);
            Find.WindowStack.Add((Window)configWindowCtor.Invoke(null));
        }
    }
}
