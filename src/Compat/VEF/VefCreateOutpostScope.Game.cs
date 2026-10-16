using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard scope for VEF's outpost picker (<c>Outposts.Dialog_CreateCamp</c>), registered
    /// through <see cref="VefDialogCompat"/>. The dialog draws each outpost as an icon, name,
    /// description, a Create button and a blocked reason, with the requirements checklist as a
    /// tooltip over the whole block; this scope folds each block into one row. Enter clicks that
    /// block's own Create button, so the dialog's own validity check, rejection message and
    /// creation all run unchanged.
    /// </summary>
    internal sealed class VefCreateOutpostScope : ScreenScope
    {
        // The mod's own Keyed strings: the gizmo's label, and the caption on every block's button.
        private const string TitleKey = "Outposts.Commands.Create.Label";
        private const string CreateButtonKey = "Outposts.Dialog.Create";

        private readonly Window dialog;
        private List<VefCreateOutpostCompat.OutpostEntry> entries = new List<VefCreateOutpostCompat.OutpostEntry>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public VefCreateOutpostScope(Window w)
        {
            dialog = w;
        }

        public override string Name => "vef-create-outpost";

        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        protected override int ContentRegionCount => 1;

        protected override void RefreshContent()
        {
            entries = VefCreateOutpostCompat.Entries(dialog);
        }

        protected override string ContentRegionName(int region)
        {
            return TitleKey.Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return entries.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= entries.Count)
                return d;
            VefCreateOutpostCompat.OutpostEntry entry = entries[index];
            d.Label = entry.Def.LabelCap;
            d.Role = ElementRole.Button;
            d.Disabled = !entry.BlockedReason.NullOrEmpty();
            d.PositionIndex = index + 1;
            d.PositionCount = entries.Count;
            var parts = new List<string>();
            if (d.Disabled)
                parts.Add(entry.BlockedReason);
            if (!entry.Requirements.NullOrEmpty())
                parts.Add(GizmoTextUtility.FlattenNewlines(entry.Requirements.StripTags()));
            if (!entry.Def.description.NullOrEmpty())
                parts.Add(GizmoTextUtility.FlattenNewlines(entry.Def.description));
            d.Extras = string.Join(". ", parts);
            return d;
        }

        // Each block draws one Create button in list order, so the row index is its ordinal.
        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= entries.Count)
                return;
            WidgetCapture.RequestActivate(WidgetKind.Button, CreateButtonKey.Translate(), index);
        }

        // The widget pass carries Enter's click; the blocks themselves are this scope's rows.
        protected override bool IncludeCapturedExtrasRegion => true;

        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return true;
        }

        protected override bool CaptureWindowButtons => false;

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("CloseButton".Translate(), delegate { dialog.Close(); }, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return TitleKey.Translate();
        }
    }
}
