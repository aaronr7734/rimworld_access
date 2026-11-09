using System;
using System.Reflection;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>Reflection surface for Tech Advancing.</summary>
    internal static class TechAdvancingCompat
    {
        private static ConstructorInfo configWindowCtor;
        private static FieldInfo worldSaveHandlerField;

        internal static readonly LazyReflectionGate Gate =
            new LazyReflectionGate("Tech Advancing", surface =>
            {
                Type window = surface.Type("TechAdvancing.TechAdvancing_Config_Tab");
                ConstructorInfo ctor = surface.Constructor(window, Type.EmptyTypes);
                FieldInfo handler = surface.Field(window, "worldCompSaveHandler");
                if (!surface.Ready)
                {
                    return false;
                }
                configWindowCtor = ctor;
                worldSaveHandlerField = handler;
                return true;
            });

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
