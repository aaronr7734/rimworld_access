using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gives every thing weakened by nearby artificial buildings (Gauranlen trees, nature shrines,
    /// animus stones, modded equivalents) the anima tree's vanilla build radius warning toggle, so
    /// <see cref="MeditationProtectionHelper"/> can guard all of them, not only the anima tree.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BuildRadiusWarningToggle
    {
        private static readonly HashSet<ThingDef> injectedDefs = new HashSet<ThingDef>();

        static BuildRadiusWarningToggle()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (IsWeakenedByBuildings(def)
                    && !def.HasAssignableCompFrom(typeof(CompToggleDrawAffectedMeditationFoci)))
                {
                    def.comps.Add(new CompProperties_ToggleDrawAffectedMeditationFoci());
                    injectedDefs.Add(def);
                }
            }
        }

        public static bool IsInjected(ThingDef def) => injectedDefs.Contains(def);

        // Mirrors the two sources vanilla's placement overlays draw red lines to:
        // CompMeditationFocus.WillBeAffectedBy and CompTreeConnection.WillBeAffectedBy.
        private static bool IsWeakenedByBuildings(ThingDef def)
        {
            return def.comps != null && def.comps.Any(props => props is CompProperties_TreeConnection
                || (props is CompProperties_MeditationFocus focus
                    && focus.offsets.Any(offset => offset is FocusStrengthOffset_ArtificialBuildings)));
        }
    }

    /// <summary>
    /// Vanilla saves an off toggle as an absent node, which a save from before the toggle existed
    /// also has. The marker tells them apart, so pre-existing things start at the def default once
    /// and a toggle the player turned off stays off. Saves stay loadable without this mod: the
    /// loader ignores unread nodes.
    /// </summary>
    [HarmonyPatch(typeof(CompToggleDrawAffectedMeditationFoci),
        nameof(CompToggleDrawAffectedMeditationFoci.PostExposeData))]
    public static class BuildRadiusWarningToggleSavePatch
    {
        private static readonly AccessTools.FieldRef<CompToggleDrawAffectedMeditationFoci, bool> Enabled =
            AccessTools.FieldRefAccess<CompToggleDrawAffectedMeditationFoci, bool>("enabled");

        public static void Postfix(CompToggleDrawAffectedMeditationFoci __instance)
        {
            if (!BuildRadiusWarningToggle.IsInjected(__instance.parent.def))
                return;

            bool initialized = true;
            Scribe_Values.Look(ref initialized, "rwaBuildRadiusWarningInitialized");
            if (Scribe.mode == LoadSaveMode.LoadingVars && !initialized)
                Enabled(__instance) = __instance.Props.defaultEnabled;
        }
    }
}
