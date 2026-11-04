using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compositable Loadouts' state editor (<c>Dialog_LoadoutStateEditor</c>). The first region holds
    /// the panic state dropdown and the new-state name box; the second is the state list, one row per
    /// state with its Rename and Delete icons as cells. Create is the dialog's own captured button, so
    /// its empty-name refusal and the list re-sort stay the mod's.
    /// </summary>
    internal sealed class ClStateEditorScope : ScreenScope
    {
        private const int SettingsRegion = 0;
        private const int StatesRegion = 1;
        private const int PanicRow = 0;
        private const int NewNameRow = 1;
        private const int RenameColumn = 0;
        private const int DeleteColumn = 1;

        // The mod's InputField caps at 999 characters, and a rename may be blank.
        private static readonly TextFieldSpec NameSpec = new TextFieldSpec(labelKey: null, minLength: 0);

        private readonly Window dialog;
        private readonly List<object> states = new List<object>();
        private List<object> knownStates;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession nameSession = new TextFieldEditSession();

        public ClStateEditorScope(Window dialog)
        {
            this.dialog = dialog;
            RegisterPopTeardown(nameSession.CancelIfActive);
        }

        public override string Name => "clStateEditor";

        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        protected override int ContentRegionCount => 2;

        protected override string ContentRegionName(int region)
        {
            return region == SettingsRegion
                ? CompatText.ModText("CompositableLoadouts.ModifyStates")
                : "RimWorldAccess.Compat.CompositableLoadouts.StatesRegion".Translate().ToString();
        }

        protected override void RefreshContent()
        {
            states.Clear();
            states.AddRange(ClCompat.Dialogs.EditorStates(dialog));
        }

        protected override int ContentItemCount(int region)
        {
            return region == SettingsRegion ? 2 : states.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == StatesRegion ? 2 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return column == RenameColumn
                ? new TableColumnInfo("Rename".Translate().ToString(), cellRole: ElementRole.Button)
                : new TableColumnInfo("Delete".Translate().ToString(), cellRole: ElementRole.Button);
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            return "";
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == StatesRegion)
            {
                d.Label = index >= 0 && index < states.Count ? ClCompat.StateName(states[index]) : "";
                return d;
            }
            if (index == PanicRow)
            {
                d.Label = CompatText.ModText("CompositableLoadouts.PanicState").Trim();
                d.Role = ElementRole.ComboBox;
                d.Value = ClCompat.StateName(ClCompat.PanicState()) ?? CompatText.ModText("CompositableLoadouts.DefaultStateName");
                d.Disabled = !HasOtherPanicCandidate();
                d.Extras = CompatText.Flatten(CompatText.ModText("CompositableLoadouts.PanicStateDesc"));
            }
            else if (index == NewNameRow)
            {
                d.Label = "RimWorldAccess.Compat.CompositableLoadouts.NewStateName".Translate();
                d.Role = ElementRole.TextField;
                d.EntersEditOnAccept = true;
                string name = ClCompat.Dialogs.EditorNewName(dialog);
                if (name.NullOrEmpty())
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = name;
                }
            }
            return d;
        }

        /// <summary>DrawSetPanicState's gate: the dropdown only draws while some state other than the panic one exists.</summary>
        private static bool HasOtherPanicCandidate()
        {
            object panic = ClCompat.PanicState();
            foreach (object state in ClCompat.AllStates())
            {
                if (!Equals(state, panic))
                {
                    return true;
                }
            }
            return false;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == StatesRegion)
            {
                if (index >= 0 && index < states.Count)
                {
                    BeginRename(states[index]);
                }
                return;
            }
            if (index == PanicRow)
            {
                if (HasOtherPanicCandidate())
                {
                    DropdownColumnHandler.OpenGeneratedMenu(ClCompat.Dialogs.EditorPanicGenerator, dialog, ClCompat.PanicState());
                }
                else
                {
                    Messages.Message(CompatText.ModText("CompositableLoadouts.NoValidPanicStates"), MessageTypeDefOf.CautionInput, false);
                }
                return;
            }
            if (index == NewNameRow)
            {
                nameSession.EnterEdit(ClCompat.Dialogs.EditorNewName(dialog), NameSpec,
                    "RimWorldAccess.Compat.CompositableLoadouts.NewStateName".Translate(),
                    value => ClCompat.Dialogs.SetEditorNewName(dialog, value),
                    AnnounceCurrentItem);
            }
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != StatesRegion || row < 0 || row >= states.Count)
            {
                return false;
            }
            object state = states[row];
            if (column == RenameColumn)
            {
                BeginRename(state);
                return true;
            }
            string name = ClCompat.StateName(state);
            // MUTATION-C: mirrors Dialog_LoadoutStateEditor.cs:60-62; the delete icon's body is inline.
            if (ClCompat.ManagerRemoveState(state))
            {
                RefreshModel();
                TolkHelper.Speak("RimWorldAccess.Compat.CompositableLoadouts.StateDeleted".Loc(name));
            }
            return true;
        }

        private void BeginRename(object state)
        {
            ClCompat.Dialogs.SetEditorRenaming(dialog, state, true);
            nameSession.EnterEdit(ClCompat.StateName(state), NameSpec, "Rename".Translate(),
                value => ClCompat.Dialogs.SetStateName(state, value),
                () =>
                {
                    ClCompat.Dialogs.SetEditorRenaming(dialog, state, false);
                    RefreshModel();
                    AnnounceCurrentItem();
                });
        }

        /// <summary>Posts the live buffer each draw, so the mod's own box shows what is being typed.</summary>
        /// <summary>Runs before every draw; the captured Create click lands inside the mod's draw and speaks nothing itself.</summary>
        internal void MirrorLive()
        {
            nameSession.MirrorLive();
            List<object> now = ClCompat.Dialogs.EditorStates(dialog);
            if (knownStates != null)
            {
                foreach (object state in now)
                {
                    // LoadoutState equality is by name, so a live rename must not read as a new state.
                    if (!knownStates.Exists(k => ReferenceEquals(k, state)))
                    {
                        RefreshModel();
                        TolkHelper.SpeakData("RimWorldAccess.Compat.CompositableLoadouts.StateCreated"
                            .Translate(ClCompat.StateName(state)).ToString());
                    }
                }
            }
            knownStates = now;
        }

        /// <summary>Keeps only Create: the panic dropdown draws a text button of its own.</summary>
        protected override bool KeepCapturedButton(string rawLabel)
        {
            return rawLabel == CompatText.ModText("CompositableLoadouts.Create");
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("CloseButton".Translate().ToString(), () => dialog.Close(),
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }
    }
}
