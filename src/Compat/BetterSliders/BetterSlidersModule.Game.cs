using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// BetterSliders draws a number box beside or over every slider and range. Its settings page
    /// is plain Listing rows the generic reader already covers; this module makes the box reachable
    /// from the keyboard in both display styles.
    /// </summary>
    internal sealed class BetterSlidersModule : CompatModule
    {
        public override string TargetPackageId => "sirrandoo.bettersliders";

        public override void Activate(Harmony harmony)
        {
            BetterSlidersTypeInCompat.Register(harmony);
        }
    }
}
