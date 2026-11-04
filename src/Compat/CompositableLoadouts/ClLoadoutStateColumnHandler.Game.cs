using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Assign tab's "current loadout" column (<c>PawnColumnWorker_LoadoutState</c>). The cell is
    /// the state dropdown, served from the worker's own <c>GenerateMenuFor</c>. The rest of what the
    /// cell and header draw (copy and paste, Edit, the right-click mass change, the header's Edit
    /// Loadout States button and explanation) rides the table's context menu.
    /// </summary>
    internal sealed class ClLoadoutStateColumnHandler : DropdownColumnHandler, IPawnColumnValueCopier, IPawnColumnContextActions
    {
        public ClLoadoutStateColumnHandler()
            : base("GenerateMenuFor", CurrentLabel, cellExists: ClCompat.IsValidLoadoutHolder)
        {
        }

        private static string CurrentLabel(Pawn pawn)
        {
            return ClCompat.StateName(ClCompat.ActiveState(pawn)) ?? CompatText.ModText("CompositableLoadouts.SelectState");
        }

        /// <summary>The generator leaves out the pawn's current state, so an equal state is already copied.</summary>
        public bool TryCopyValue(PawnColumnDef def, Pawn sourcePawn, Pawn targetPawn)
        {
            if (!ClCompat.IsValidLoadoutHolder(sourcePawn) || !ClCompat.IsValidLoadoutHolder(targetPawn))
            {
                return false;
            }
            object state = ClCompat.ActiveState(sourcePawn);
            return Equals(state, ClCompat.ActiveState(targetPawn)) || TryApplyPayloadToTarget(def, targetPawn, state);
        }

        public string HeaderTip(PawnColumnDef def)
        {
            return CompatText.Flatten(CompatText.ModText("CompositableLoadouts.StatesExplanation"));
        }

        public List<FloatMenuOption> HeaderOptions(PawnColumnDef def)
        {
            return new List<FloatMenuOption>
            {
                new FloatMenuOption(CompatText.ModText("CompositableLoadouts.ModifyStates"),
                    () => ClCompat.OpenLoadoutStateEditor()),
            };
        }

        public List<FloatMenuOption> CellOptions(PawnColumnDef def, Pawn pawn)
        {
            if (!ClCompat.IsValidLoadoutHolder(pawn))
            {
                return null;
            }
            var options = new List<FloatMenuOption>();
            var copy = new FloatMenuOption("Copy".Translate(), () => ClCompat.Columns.CopyAll(pawn, replace: true));
            options.Add(copy);
            if (ClCompat.Columns.Primed)
            {
                options.Add(new FloatMenuOption("Paste".Translate(),
                    ClCompat.Columns.CanPasteTo(pawn) ? () => ClCompat.Columns.PasteTo(pawn) : (System.Action)null));
            }
            options.Add(CopyAllOption(pawn, "RimWorldAccess.Compat.CompositableLoadouts.CopyAllReplace", replace: true));
            options.Add(CopyAllOption(pawn, "RimWorldAccess.Compat.CompositableLoadouts.CopyAllAdd", replace: false));
            options.Add(SelectTagsOption(pawn, "RimWorldAccess.Compat.CompositableLoadouts.SelectTagsReplace", replace: true));
            options.Add(SelectTagsOption(pawn, "RimWorldAccess.Compat.CompositableLoadouts.SelectTagsAdd", replace: false));

            var edit = new FloatMenuOption("AssignTabEdit".Translate(), () => ClCompat.OpenLoadoutEditor(pawn));
            edit.tooltip = new TipSignal(CompatText.ModArgs("CompositableLoadouts.EditLoadoutDesc", pawn.LabelShortCap));
            options.Add(edit);

            FloatMenuOption massChange = MassChangeOption(pawn);
            if (massChange != null)
            {
                options.Add(massChange);
            }
            return options;
        }

        private static FloatMenuOption CopyAllOption(Pawn pawn, string key, bool replace)
        {
            return new FloatMenuOption(key.Translate(pawn.LabelShort),
                () => ClCompat.Columns.CopyAll(pawn, replace));
        }

        private static FloatMenuOption SelectTagsOption(Pawn pawn, string key, bool replace)
        {
            return new FloatMenuOption(key.Translate(pawn.LabelShort), () =>
            {
                ClCompat.Columns.ClearClipboard();
                List<object> elements = ClCompat.Elements(ClCompat.LoadoutOf(pawn));
                List<object> tags = elements.Select(ClCompat.ElementTag).ToList();
                ClCompat.OpenTagSelector(tags,
                    tag => ClCompat.Columns.AddToClipboard(pawn, elements.FirstOrDefault(e => ClCompat.ElementTag(e) == tag), replace),
                    multiSelect: true,
                    customTitleSuffix: CompatText.ModText("CompositableLoadouts.ToCopy"));
            });
        }

        /// <summary>The right click on the state dropdown: every loadout holder in the pawn's caravan or on its map.</summary>
        private static FloatMenuOption MassChangeOption(Pawn pawn)
        {
            bool inCaravan = pawn.IsCaravanMember();
            List<Pawn> source = inCaravan ? pawn.GetCaravan().PawnsListForReading : pawn.Map?.mapPawns.AllPawns;
            List<Pawn> pawns = source?.Where(ClCompat.IsValidLoadoutHolder).ToList();
            if (pawns.NullOrEmpty())
            {
                return null;
            }
            object state = ClCompat.ActiveState(pawn);
            string area = CompatText.ModText(inCaravan ? "CompositableLoadouts.Caravan" : "CompositableLoadouts.Map");
            string label = CompatText.ModArgs("CompositableLoadouts.MassChangeStates", pawn.LabelShortCap, area,
                ClCompat.StateName(state) ?? CompatText.ModText("CompositableLoadouts.DefaultStateName"));
            return new FloatMenuOption(label, () =>
            {
                // MUTATION-C: mirrors PawnColumnWorker_LoadoutState.cs:57; the right-click option body is inline.
                foreach (Pawn target in pawns)
                {
                    ClCompat.SetActiveState(target, ClCompat.ActiveState(pawn), immediatelyResolve: true);
                }
            });
        }
    }
}
