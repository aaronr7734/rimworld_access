using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Vanilla Factions Expanded - Tribals. Most of the mod rides existing vehicles (era
    /// rituals, choice letters, research defs, work-type gating); what needs bespoke work is the
    /// cornerstones window (<see cref="VfetCornerstonesScope"/>) and its entry point, a button
    /// the mod Harmony-patches onto the Factions tab that the tab's data-model scope cannot see
    /// — mirrored here as a <see cref="CompatScreenActions"/> row opening the same window.
    /// </summary>
    internal sealed class VfetModule : CompatModule
    {
        public override string TargetPackageId => "OskarPotocki.VFE.Tribals";

        public override void Activate(Harmony harmony)
        {
            ResearchTechLevelGrouping.Offer();

            if (!VfetCompat.Ready)
            {
                return;
            }

            ScopeForWindow.Register(VfetCompat.CornerstonesWindowType, delegate (Window w)
            {
                return new VfetCornerstonesScope(w);
            });

            // The mod's postfix draws this button unconditionally, but the window it opens
            // dereferences the tribal component, so the row is withheld when that is absent.
            CompatScreenActions.Register("faction-tab", delegate
            {
                if (!VfetCompat.HasLiveGame())
                {
                    return null;
                }
                return new ScreenAction(
                    CompatText.ModText("VFET.Cornerstones"),
                    VfetCompat.OpenCornerstonesWindow);
            });

            // The note the mod paints onto the ideology-configuration pages
            // (Page_ConfigureIdeo_DoWindowContents_Patch); Enter repeats it.
            CompatScreenActions.Register("ideo-builder-hub", delegate
            {
                string note = CompatText.ModText("VFET.CustomizeNPCIdeologionDesc");
                return new ScreenAction(note, delegate { TolkHelper.SpeakData(note); });
            });

            // The mod hides the social card's role dropdown until Culture is researched
            // (SocialCardUtility_DrawPawnRoleSelection_Patch); keep our roles section in parity.
            SocialTabHelper.RegisterRoleSelectionSuppressor(delegate (Pawn pawn)
            {
                if (pawn.Faction != Faction.OfPlayer)
                {
                    return false;
                }
                ResearchProjectDef culture =
                    DefDatabase<ResearchProjectDef>.GetNamedSilentFail("VFET_Culture");
                return culture != null && !culture.IsFinished;
            });

        }
    }
}
