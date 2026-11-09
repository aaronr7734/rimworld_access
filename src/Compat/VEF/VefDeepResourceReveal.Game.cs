using System;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The framework's <c>deepResourcesOnGUI</c> def extension, which shows the deep-resource
    /// readout while a building carrying it is selected, optionally without a scanner. Mirrors the
    /// gate of its <c>DeepResourceGrid.DeepResourcesOnGUI</c> postfix
    /// (VEF.Maps/VanillaExpandedFramework_DeepResourceGrid_DeepResourcesOnGUI.cs), so the resources
    /// key and the placement cursor read deep resources exactly when that readout is drawn.
    /// </summary>
    internal static class VefDeepResourceReveal
    {
        private static Type extensionType;
        private static FieldInfo showsField;
        private static FieldInfo requiresScannerField;

        public static void Register()
        {
            var surface = new ReflectionSurface("VefDeepResourceReveal");
            Type extension = surface.Type("VEF.Things.ThingDefExtension");
            FieldInfo shows = surface.Field(extension, "deepResourcesOnGUI");
            FieldInfo requiresScanner = surface.Field(extension, "deepResourcesOnGUIRequireScanner");
            if (!surface.Ready)
                return;

            extensionType = extension;
            showsField = shows;
            requiresScannerField = requiresScanner;
            DeepResourceReveal.Register(Reveals);
        }

        private static bool Reveals(Map map)
        {
            Thing selected = Find.Selector.SingleSelectedThing;
            if (map != Find.CurrentMap || selected == null || selected.Map != map)
                return false;
            DefModExtension extension = FirstExtension(selected.def);
            if (extension == null || !(bool)showsField.GetValue(extension))
                return false;
            return !(bool)requiresScannerField.GetValue(extension) || map.deepResourceGrid.AnyActiveDeepScannersOnMap();
        }

        /// <summary><c>Def.GetModExtension&lt;T&gt;</c> by runtime type: the first extension that is one.</summary>
        private static DefModExtension FirstExtension(Def def)
        {
            if (def.modExtensions == null)
                return null;
            foreach (DefModExtension extension in def.modExtensions)
            {
                if (extensionType.IsInstanceOfType(extension))
                    return extension;
            }
            return null;
        }
    }
}
