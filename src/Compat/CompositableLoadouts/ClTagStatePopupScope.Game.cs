using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compositable Loadouts' per-tag state popup (<c>Dialog_SetTagLoadoutState</c>), which reads
    /// "Active when {state} is {active|inactive}". The state row serves the popup's own
    /// <c>GetElems</c> dropdown; the condition row, present once a state is chosen, flips the
    /// condition the way the popup's Active/Inactive button does.
    /// </summary>
    internal sealed class ClTagStatePopupScope : ScreenScope
    {
        private const int StateRow = 0;
        private const int ConditionRow = 1;

        private readonly Window dialog;
        private object element;

        public ClTagStatePopupScope(Window dialog)
        {
            this.dialog = dialog;
        }

        public override string Name => "clTagStatePopup";

        protected internal override Window OwnedWindow => dialog;

        protected override bool CaptureWindowButtons => false;

        protected override bool IncludeActionsRegion => false;

        protected override int ContentRegionCount => 1;

        protected override string ContentRegionName(int region)
        {
            return CompatText.ModText("CompositableLoadouts.ActiveWhen");
        }

        protected override string ComposeOpenAnnouncement()
        {
            return Sentence();
        }

        private string Sentence()
        {
            object state = ClCompat.ElementState(element);
            if (state == null)
            {
                return "RimWorldAccess.Compat.CompositableLoadouts.ActiveWhenAnyState".Translate(
                    CompatText.ModText("CompositableLoadouts.DefaultStateNameInUse"));
            }
            return (ClCompat.ElementActiveWhenStateActive(element)
                    ? "RimWorldAccess.Compat.CompositableLoadouts.ActiveWhenStateActive"
                    : "RimWorldAccess.Compat.CompositableLoadouts.ActiveWhenStateInactive")
                .Translate(ClCompat.StateName(state));
        }

        protected override void RefreshContent()
        {
            element = ClCompat.Dialogs.PopupElement(dialog);
        }

        protected override int ContentItemCount(int region)
        {
            if (element == null)
            {
                return 0;
            }
            return ClCompat.ElementState(element) == null ? 1 : 2;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index == StateRow)
            {
                d.Label = CompatText.ModText("CompositableLoadouts.Overview.State");
                d.Role = ElementRole.ComboBox;
                d.Value = ClCompat.StateName(ClCompat.ElementState(element))
                    ?? CompatText.ModText("CompositableLoadouts.DefaultStateNameInUse");
            }
            else if (index == ConditionRow)
            {
                d.Label = "RimWorldAccess.Compat.CompositableLoadouts.ConditionRow".Translate();
                d.Role = ElementRole.Button;
                d.Value = ConditionWord();
            }
            return d;
        }

        private string ConditionWord()
        {
            return CompatText.ModText(ClCompat.ElementActiveWhenStateActive(element)
                ? "CompositableLoadouts.Active"
                : "CompositableLoadouts.Inactive");
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (element == null)
            {
                return;
            }
            if (index == StateRow)
            {
                DropdownColumnHandler.OpenGeneratedMenu(ClCompat.Dialogs.PopupStateGenerator, null, element);
                return;
            }
            if (index == ConditionRow)
            {
                // MUTATION-C: mirrors Dialog_SetTagLoadoutState.cs:131-136; the Active/Inactive button body is inline in Draw.
                ClCompat.SetElementState(element, ClCompat.ElementState(element), !ClCompat.ElementActiveWhenStateActive(element));
                RefreshModel();
                TolkHelper.SpeakData(ConditionWord());
            }
        }
    }
}
