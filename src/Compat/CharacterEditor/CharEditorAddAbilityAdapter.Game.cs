using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogAddAbility</c>. Two ComboBox filters (mod
    /// name, level); results are the dialog's own filtered candidate list; selection and Confirm
    /// ride the dialog's own <c>selectedAbility</c> field and <c>Window.OnAcceptKeyPressed</c>
    /// (which runs <c>DoAndClose</c> -- and unconditionally grants the pawn a
    /// psylink via <c>CheckAddPsylink()</c> before granting ANY selected ability, mod behavior,
    /// ridden as-is). One extra action: the dialog's own random pick from the filtered list.
    /// </summary>
    internal sealed class AddAbilityAdapter : CharEditorBrowserAdapterBase
    {
        private readonly List<ScreenAction> extraActions;

        public AddAbilityAdapter(Window dialog)
            : base(dialog)
        {
            extraActions = new List<ScreenAction>
            {
                new ScreenAction("RimWorldAccess.CharEd.Browser.Randomize".Translate(), Randomize),
            };
        }

        public override string Title => "RimWorldAccess.CharEd.Character.AddAbility".Translate().ToString();

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                ModNameCombo(CharEditorCompat.AbilityModNames,
                    () => CharEditorBrowserCompat.AddAbilityModName(dialog),
                    name => CharEditorBrowserCompat.AddAbilitySetModName(dialog, name)),
                Combo("RimWorldAccess.CharEd.Browser.LevelFilter".Translate(),
                    () => CharEditorBrowserCompat.AddAbilityLevelCandidates(dialog),
                    delegate
                    {
                        string current = CharEditorBrowserCompat.AddAbilityLevelFilter(dialog) ?? AllLabel();
                        return CharEditorBrowserCompat.AddAbilityLevelCandidates(dialog).FindIndex(c => c == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorBrowserCompat.AddAbilityLevelCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorBrowserCompat.AddAbilitySetLevelFilter(dialog, candidates[candidateIndex]);
                    }),
            };
        }

        public override int ResultCount => CharEditorBrowserCompat.AddAbilityResults(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorBrowserCompat.AddAbilityResults(dialog);
            return index >= 0 && index < results.Count ? results[index].LabelCap.ToString() : "";
        }

        /// <summary>The vanilla AbilityDef.GetTooltip(Pawn) the dialog's own list-view tooltip getter calls -- public vanilla, no reflection needed.</summary>
        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorBrowserCompat.AddAbilityResults(dialog);
            if (index < 0 || index >= results.Count)
                return null;
            return RedundantLabelPrefix.AfterTitle(results[index].GetTooltip(CharEditorCompat.CurrentPawn).StripTags(),
                results[index].LabelCap.ToString().StripTags());
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorBrowserCompat.AddAbilityResults(dialog);
                AbilityDef selected = CharEditorBrowserCompat.AddAbilitySelected(dialog);
                return results.IndexOf(selected);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorBrowserCompat.AddAbilityResults(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorBrowserCompat.AddAbilitySetSelected(dialog, results[index]);
        }

        public override IReadOnlyList<ScreenAction> ExtraActions => extraActions;

        private void Randomize()
        {
            CharEditorBrowserCompat.AddAbilityRandomize(dialog);
        }
    }
}
