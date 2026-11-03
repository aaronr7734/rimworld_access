using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Data facade for the prisoner/slave management tab. The sections, the cursor,
    /// the typeahead and every announcement now live on
    /// <see cref="Shell.PrisonerTabScope"/>, which is a <see cref="Shell.ScreenScope"/>
    /// whose content REGIONS are this tab's sections (the EntityTabState/EntityTabScope
    /// split applied to this screen). This class keeps
    /// only the data the scope reads and the gated writes it drives, plus the
    /// <see cref="IsActive"/>/<see cref="CurrentPawn"/>/<see cref="Open"/>/
    /// <see cref="Close"/> surface the tab's Harmony patch, the inspect-tree adapter,
    /// the bare-'P' opener, ShellGuards and MapNavigationPatch already depend on.
    /// </summary>
    public static class PrisonerTabState
    {
        private static readonly List<string> infoLines = new List<string>();
        private static readonly List<PrisonerInteractionModeDef> exclusiveModes = new List<PrisonerInteractionModeDef>();
        private static readonly List<PrisonerInteractionModeDef> nonExclusiveModes = new List<PrisonerInteractionModeDef>();
        private static readonly List<SlaveInteractionModeDef> slaveModes = new List<SlaveInteractionModeDef>();

        public static bool IsActive { get; private set; }

        public static Pawn CurrentPawn { get; private set; }

        /// <summary>One navigable read-only stat per row (prisoner or slave variant).</summary>
        public static IReadOnlyList<string> InfoLines
        {
            get { return infoLines; }
        }

        /// <summary>The radio-style prisoner interaction modes.</summary>
        public static IReadOnlyList<PrisonerInteractionModeDef> ExclusiveModes
        {
            get { return exclusiveModes; }
        }

        /// <summary>The independently toggleable prisoner interaction modes.</summary>
        public static IReadOnlyList<PrisonerInteractionModeDef> NonExclusiveModes
        {
            get { return nonExclusiveModes; }
        }

        /// <summary>The radio-style slave interaction modes.</summary>
        public static IReadOnlyList<SlaveInteractionModeDef> SlaveModes
        {
            get { return slaveModes; }
        }

        /// <summary>
        /// The medical care levels in enum order, browsed as a list: moving the cursor
        /// only previews, Enter commits, so browsing never changes the setting
        /// underfoot.
        /// </summary>
        public static MedicalCareCategory[] MedicalCareLevels()
        {
            return (MedicalCareCategory[])System.Enum.GetValues(typeof(MedicalCareCategory));
        }

        /// <summary>Index of the level currently in force, or 0.</summary>
        public static int CurrentMedicalCareIndex()
        {
            if (CurrentPawn?.playerSettings == null)
                return 0;
            int index = System.Array.IndexOf(MedicalCareLevels(), CurrentPawn.playerSettings.medCare);
            return index >= 0 ? index : 0;
        }

        public static void Open(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (!pawn.IsPrisonerOfColony && !pawn.IsSlaveOfColony)
            {
                TolkHelper.Speak("RimWorldAccess.Prisoner.Guest.NotPrisonerOrSlave".Loc(pawn.LabelShort));
                return;
            }

            IsActive = true;
            CurrentPawn = pawn;
            Refresh();
        }

        public static void Close()
        {
            IsActive = false;
            CurrentPawn = null;
            ClearCachedData();

            TolkHelper.Speak("RimWorldAccess.Prisoner.Tab.Closed".Loc());
        }

        /// <summary>
        /// Rebuilds the cached row data for the current pawn. Called on open and by
        /// the scope before each count/describe cycle, so a mode toggle that changes
        /// what is available is reflected without a reopen.
        /// </summary>
        public static void Refresh()
        {
            ClearCachedData();
            if (CurrentPawn == null)
                return;

            if (CurrentPawn.IsPrisonerOfColony)
            {
                infoLines.AddRange(PrisonerTabHelper.GetPrisonerInfoRows(CurrentPawn));
                exclusiveModes.AddRange(PrisonerTabHelper.GetAvailableExclusiveInteractionModes(CurrentPawn));
                nonExclusiveModes.AddRange(PrisonerTabHelper.GetAvailableNonExclusiveInteractionModes(CurrentPawn));
            }
            else if (CurrentPawn.IsSlaveOfColony)
            {
                infoLines.AddRange(PrisonerTabHelper.GetSlaveInfoRows(CurrentPawn));
                slaveModes.AddRange(PrisonerTabHelper.GetAvailableSlaveInteractionModes());
            }
        }

        // ------------------------------------------------------------------
        // Gated writes. Every MUTATION-C marker below is carried over verbatim
        // from the pre-migration state — the vehicles are unchanged.
        // ------------------------------------------------------------------

        public static void SetMedicalCare(MedicalCareCategory category)
        {
            if (CurrentPawn?.playerSettings == null)
                return;
            // MUTATION-C: mirrors MedicalCareUtility.MedicalCareSetter, which writes
            // `medCare = mc;` directly from a drag/click on the IMGUI care-icon strip
            // (decompiled RimWorld/MedicalCareUtility.cs:49); vanilla has no gated setter.
            CurrentPawn.playerSettings.medCare = category;
        }

        /// <summary>
        /// Applies a prisoner interaction mode. Vehicle A for the mode itself
        /// (guest.SetExclusiveInteraction, vanilla's own gated method, called first
        /// exactly as ITab_Pawn_Visitor.DrawExclusiveInteractionRow does), plus
        /// vanilla's InteractionModeChanged follow-up that auto-assigns the primary
        /// ideo when switching to Convert with none chosen.
        ///
        /// Returns true when the caller should open the ideology picker: Convert with
        /// more than one player ideology, which vanilla exposes through the ideo icon.
        /// </summary>
        public static bool SetExclusiveMode(PrisonerInteractionModeDef mode)
        {
            return SetExclusiveMode(CurrentPawn, mode);
        }

        public static bool SetExclusiveMode(Pawn pawn, PrisonerInteractionModeDef mode)
        {
            if (pawn?.guest == null || mode == null)
                return false;

            pawn.guest.SetExclusiveInteraction(mode);

            if (mode == PrisonerInteractionModeDefOf.Convert
                && pawn.guest.ideoForConversion == null
                && Faction.OfPlayer.ideos != null)
            {
                // MUTATION-C: mirrors ITab_Pawn_Visitor.InteractionModeChanged's
                // `SelPawn.guest.ideoForConversion = Faction.OfPlayer.ideos.PrimaryIdeo;`
                // (decompiled RimWorld/ITab_Pawn_Visitor.cs:509); vanilla has no gated setter.
                pawn.guest.ideoForConversion = Faction.OfPlayer.ideos.PrimaryIdeo;
            }

            return mode == PrisonerInteractionModeDefOf.Convert
                && PrisonerTabHelper.GetPlayerIdeologies().Count > 1;
        }

        public static void SetSlaveMode(SlaveInteractionModeDef mode)
        {
            if (CurrentPawn?.guest == null || mode == null)
                return;
            // MUTATION-C: mirrors ITab_Pawn_Visitor.DoSlaveTab's
            // `SelPawn.guest.slaveInteractionMode = tmpSlaveInteractionMode;` radio-button
            // handler (decompiled RimWorld/ITab_Pawn_Visitor.cs:158); vanilla has no gated setter.
            CurrentPawn.guest.slaveInteractionMode = mode;
        }

        /// <summary>
        /// Whether setting this slave mode needs vanilla's own confirmation prompt:
        /// Execute on a slave whose faction is not hostile to the player
        /// (ITab_Pawn_Visitor.DoSlaveTab line 159-168).
        /// </summary>
        public static bool SlaveModeNeedsConfirmation(SlaveInteractionModeDef mode)
        {
            return mode == SlaveInteractionModeDefOf.Execute
                && CurrentPawn != null
                && CurrentPawn.SlaveFaction != null
                && !CurrentPawn.SlaveFaction.HostileTo(Faction.OfPlayer);
        }

        /// <summary>
        /// Toggles one non-exclusive prisoner mode. Vehicle A
        /// (guest.ToggleNonExclusiveInteraction), plus vanilla's hemogen-farm
        /// follow-up: queue or drop the extraction bill to match the new state.
        /// </summary>
        public static void ToggleNonExclusive(PrisonerInteractionModeDef mode, bool enabled)
        {
            if (CurrentPawn?.guest == null || mode == null)
                return;

            CurrentPawn.guest.ToggleNonExclusiveInteraction(mode, enabled);

            if (ModsConfig.BiotechActive && mode == PrisonerInteractionModeDefOf.HemogenFarm)
            {
                var bill = CurrentPawn.BillStack?.Bills?.FirstOrDefault(b => b.recipe == RecipeDefOf.ExtractHemogenPack);
                if (enabled && bill == null && SanguophageUtility.CanSafelyBeQueuedForHemogenExtraction(CurrentPawn))
                {
                    HealthCardUtility.CreateSurgeryBill(CurrentPawn, RecipeDefOf.ExtractHemogenPack, null);
                }
                else if (!enabled && bill != null)
                {
                    CurrentPawn.BillStack.Bills.Remove(bill);
                }
            }
        }

        public static void SetIdeoForConversion(Ideo ideo)
        {
            if (CurrentPawn?.guest == null)
                return;
            // MUTATION-C: mirrors ITab_Pawn_Visitor's ideo-icon FloatMenu action
            // `SelPawn.guest.ideoForConversion = newIdeo;` (decompiled
            // RimWorld/ITab_Pawn_Visitor.cs:324); vanilla has no gated setter.
            CurrentPawn.guest.ideoForConversion = ideo;
        }

        private static void ClearCachedData()
        {
            infoLines.Clear();
            exclusiveModes.Clear();
            nonExclusiveModes.Clear();
            slaveModes.Clear();
        }
    }
}
