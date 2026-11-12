using System.Globalization;
using System.Runtime.CompilerServices;

namespace RimWorldAccess.Tests
{
    /// <summary>
    /// Runs every test under en-US, the culture the game forces on its main thread
    /// (Verse.CultureInfoUtility.EnsureEnglish), so number formatting matches the
    /// running game on any host locale.
    /// </summary>
    internal static class GameCulture
    {
        [ModuleInitializer]
        internal static void EnsureEnglish()
        {
            var english = CultureInfo.CreateSpecificCulture("en-US");
            CultureInfo.DefaultThreadCurrentCulture = english;
            CultureInfo.CurrentCulture = english;
        }
    }
}
