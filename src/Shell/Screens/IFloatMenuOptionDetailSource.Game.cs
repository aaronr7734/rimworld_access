using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Supplies what a mod-built <see cref="FloatMenuOption"/> carries only in its icon or its
    /// extraPartOnGUI info button: no tooltip, no shownItem, no click target. Registered through
    /// <see cref="FloatMenuOptionDetails"/>; both queries return null for options that are not the
    /// source's own.
    /// </summary>
    internal interface IFloatMenuOptionDetailSource
    {
        /// <summary>Spoken after the option's label, in place of the tooltip it lacks.</summary>
        string ExtrasFor(FloatMenuOption option);

        /// <summary>The subject of the option's own info-card button, for Alt+I.</summary>
        Def InfoDefFor(FloatMenuOption option);
    }
}
