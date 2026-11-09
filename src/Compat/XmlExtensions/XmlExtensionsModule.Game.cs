using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class XmlExtensionsModule : CompatModule
    {
        public override string TargetPackageId => "imranfish.xmlextensions";

        public override bool ShouldActivate =>
            AccessTools.TypeByName("XmlExtensions.Setting.SettingContainer") != null;

        public override void Activate(Harmony harmony)
        {
            XmlExtensionsSettingsCompat.Register(harmony);
        }
    }
}
