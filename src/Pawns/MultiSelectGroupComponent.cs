using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// GameComponent that persists multi-select pawn groups across save/load.
    /// Provides 4 group slots (F1-F4) that can be saved and recalled.
    /// Automatically discovered by RimWorld via reflection.
    /// </summary>
    public class MultiSelectGroupComponent : GameComponent
    {
        private const int GroupCount = 4;

        private List<Pawn> group0 = new List<Pawn>();
        private List<Pawn> group1 = new List<Pawn>();
        private List<Pawn> group2 = new List<Pawn>();
        private List<Pawn> group3 = new List<Pawn>();

        public MultiSelectGroupComponent(Game game) : base()
        {
        }

        /// <summary>
        /// Gets the list for a specific slot (0-4).
        /// </summary>
        private List<Pawn> GetGroupList(int slot)
        {
            switch (slot)
            {
                case 0: return group0;
                case 1: return group1;
                case 2: return group2;
                case 3: return group3;
                default: return null;
            }
        }

        /// <summary>
        /// Saves the current selection to a group slot (Ctrl+Shift+F1-F5).
        /// </summary>
        public void SaveGroup(int slot, IReadOnlyCollection<Pawn> pawns)
        {
            if (slot < 0 || slot >= GroupCount)
                return;

            var groupList = GetGroupList(slot);
            groupList.Clear();
            groupList.AddRange(pawns.Where(p => p != null && !p.Destroyed));

            string names = MenuHelper.FormatNameList(
                groupList.Select(p => p.LabelShort).ToList());
            TolkHelper.Speak("RimWorldAccess.Pawns.Group.Saved".Translate(slot + 1, names));
        }

        /// <summary>
        /// Recalls a group from a slot and activates multi-select (Ctrl+F1-F5).
        /// Filters to pawns on the current map that are alive and spawned.
        /// </summary>
        public void RecallGroup(int slot)
        {
            if (slot < 0 || slot >= GroupCount)
                return;

            var groupList = GetGroupList(slot);

            if (groupList.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Group.SlotEmpty".Translate(slot + 1));
                return;
            }

            // Filter to valid, available pawns on current map
            var validPawns = groupList
                .Where(p => p != null && !p.Destroyed && !p.Dead && p.Spawned &&
                            p.Map == Find.CurrentMap)
                .ToList();

            int totalSaved = groupList.Count(p => p != null && !p.Destroyed);
            int unavailableCount = totalSaved - validPawns.Count;

            if (validPawns.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Group.NoPawnsAvailable".Translate(slot + 1));
                return;
            }

            // Activate multi-select with these pawns
            MultiSelectState.SetSelection(validPawns);

            string names = MenuHelper.FormatNameList(
                validPawns.Select(p => p.LabelShort).ToList());

            if (unavailableCount > 0)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Group.RecalledPartial".Translate(
                    slot + 1, names, unavailableCount, validPawns.Count));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Group.Recalled".Translate(
                    slot + 1, names, validPawns.Count));
            }
        }

        /// <summary>
        /// Save/load group data with the game save file.
        /// Uses Scribe_Collections with LookMode.Reference for pawn references.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            // Clean up dead pawns before saving
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                for (int i = 0; i < GroupCount; i++)
                {
                    var list = GetGroupList(i);
                    list.RemoveAll(p => p == null || p.Destroyed || p.Dead);
                }
            }

            Scribe_Collections.Look(ref group0, "multiSelectGroup0", LookMode.Reference);
            Scribe_Collections.Look(ref group1, "multiSelectGroup1", LookMode.Reference);
            Scribe_Collections.Look(ref group2, "multiSelectGroup2", LookMode.Reference);
            Scribe_Collections.Look(ref group3, "multiSelectGroup3", LookMode.Reference);

            // Initialize null lists after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (group0 == null) group0 = new List<Pawn>();
                if (group1 == null) group1 = new List<Pawn>();
                if (group2 == null) group2 = new List<Pawn>();
                if (group3 == null) group3 = new List<Pawn>();
            }
        }
    }
}
