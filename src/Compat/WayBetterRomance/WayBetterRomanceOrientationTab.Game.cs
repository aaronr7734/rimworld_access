using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The mod's Orientation tab is Hidden: no tab button, opened only by clicking an asexual
    /// trait on the character card (CharacterCardUtility_DoLeftSection). It is listed under
    /// the same condition that click target exists, so the inspection tree offers it exactly
    /// when the bio card does.
    /// </summary>
    internal static class WayBetterRomanceOrientationTab
    {
        private static FieldInfo asexualTraitsField;

        public static void Register()
        {
            try
            {
                var surface = new ReflectionSurface("Way Better Romance orientation tab");
                Type tabType = surface.Type("BetterRomance.ITab_Orientation");
                FieldInfo traitsField = surface.Field(surface.Type("BetterRomance.SexualityUtility"), "asexualTraits");
                if (!surface.Ready)
                {
                    return;
                }
                asexualTraitsField = traitsField;
                TabRegistry.RegisterReachableHiddenTab(tabType, Reachable,
                    () => "RimWorldAccess.Compat.WayBetterRomance.OrientationTab".Translate());
            }
            catch (Exception ex)
            {
                ModLogger.Error("Way Better Romance compat registration failed: " + ex.Message);
            }
        }

        /// <summary>The click target's own gate: a game in progress and an asexual trait on the card.</summary>
        private static bool Reachable()
        {
            if (Current.ProgramState != ProgramState.Playing)
            {
                return false;
            }
            var asexualTraits = asexualTraitsField.GetValue(null) as List<TraitDef>;
            Pawn pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (asexualTraits == null || pawn == null || pawn.story == null || pawn.story.traits == null)
            {
                return false;
            }
            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                if (asexualTraits.Contains(trait.def))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
