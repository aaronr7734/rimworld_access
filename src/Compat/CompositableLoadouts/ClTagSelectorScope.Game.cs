using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Compositable Loadouts' tag picker (<c>Dialog_TagSelector</c>). One list: the search box, then
    /// the tags its filter lets through, each read with the item icons' names and alternatives
    /// tooltips. Enter on a tag is the row click (<c>CloseAndSelect</c>); Space is the Shift+click
    /// multi-select; Alt+E is the row's edit icon. The search box feeds both the filter and the name
    /// "Create New Tag" gives a new tag, so it gets a real edit session. Create New Tag and Select
    /// Tag(s) are the dialog's own buttons, captured and clicked.
    /// </summary>
    internal sealed class ClTagSelectorScope : ScreenScope
    {
        private const string ToggleActionId = "clTagSelector.toggleSelection";
        private const string EditActionId = "clTagSelector.editTag";

        // The mod's InputField caps at 999 characters, which no tag name approaches.
        private static readonly TextFieldSpec SearchSpec = new TextFieldSpec(labelKey: null, minLength: 0);

        private readonly Window dialog;
        private readonly List<object> tags = new List<object>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession searchSession = new TextFieldEditSession();

        public ClTagSelectorScope(Window dialog)
        {
            this.dialog = dialog;
            Claim(ToggleActionId, e => ToggleCurrent(), when: () => CurrentTag() != null && ClCompat.Dialogs.SelectorMultiSelect(dialog));
            Claim(EditActionId, e => EditCurrent(), when: () => CurrentTag() != null);
            RegisterPopTeardown(searchSession.CancelIfActive);
        }

        public override string Name => "clTagSelector";

        protected override bool EnableTypeahead => true;

        protected internal override Window OwnedWindow => dialog;

        protected override int ContentRegionCount => 1;

        protected override string ContentRegionName(int region)
        {
            return CompatText.ModText(ClCompat.Dialogs.SelectorMultiSelect(dialog)
                ? "CompositableLoadouts.SelectTags"
                : "CompositableLoadouts.SelectTag");
        }

        protected override string ComposeOpenAnnouncement()
        {
            return CompatText.JoinSentences(new List<string>
            {
                ContentRegionName(0),
                ClCompat.Dialogs.SelectorTitleSuffix(dialog),
            });
        }

        protected override void RefreshContent()
        {
            tags.Clear();
            tags.AddRange(ClCompat.Dialogs.SelectorVisibleTags(dialog));
        }

        protected override int ContentItemCount(int region)
        {
            return 1 + tags.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index == 0)
            {
                d.Label = "RimWorldAccess.Compat.CompositableLoadouts.SearchTags".Translate();
                d.Role = ElementRole.TextField;
                d.EntersEditOnAccept = true;
                string search = ClCompat.Dialogs.SelectorSearch(dialog);
                if (search.NullOrEmpty())
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = search;
                }
                return d;
            }
            object tag = TagAt(index);
            if (tag == null)
            {
                return d;
            }
            d.Label = ClCompat.TagName(tag);
            d.Hint = "RimWorldAccess.Compat.CompositableLoadouts.EditTagHint".Translate(ChordDisplay(EditActionId));
            if (ClCompat.Dialogs.SelectorMultiSelect(dialog))
            {
                d.Selected = ClCompat.Dialogs.SelectorIsSelected(dialog, tag);
            }
            d.Extras = ItemsSummary(tag);
            return d;
        }

        /// <summary>What the row's icons say: the def under each, or the alternatives tooltip the icon carries.</summary>
        private static string ItemsSummary(object tag)
        {
            var parts = new List<string>();
            foreach (object item in ClCompat.TagItems(tag))
            {
                if (ClCompat.ItemHasAlternatives(item))
                {
                    parts.Add(CompatText.JoinSentences(new List<string>
                    {
                        CompatText.ModText("CompositableLoadouts.OneOfHeading"),
                        CompatText.Flatten(ClCompat.ItemRankedMembersDescription(item)),
                    }));
                }
                else
                {
                    ThingDef def = ClCompat.ItemRepresentativeDef(item);
                    if (def != null)
                    {
                        parts.Add(def.LabelCap);
                    }
                }
            }
            return CompatText.JoinSentences(parts);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index == 0)
            {
                BeginSearchEdit();
                return;
            }
            object tag = TagAt(index);
            if (tag != null)
            {
                ClCompat.Dialogs.SelectorCloseAndSelect(dialog, tag);
            }
        }

        private void BeginSearchEdit()
        {
            searchSession.EnterEdit(ClCompat.Dialogs.SelectorSearch(dialog), SearchSpec,
                "RimWorldAccess.Compat.CompositableLoadouts.SearchTags".Translate(),
                value => ClCompat.Dialogs.SetSelectorSearch(dialog, value),
                () =>
                {
                    RefreshModel();
                    AnnounceCurrentItem();
                });
        }

        /// <summary>Posts the live search buffer each draw, so the list filters as the player types.</summary>
        internal void MirrorLive()
        {
            searchSession.MirrorLive();
        }

        private void ToggleCurrent()
        {
            object tag = CurrentTag();
            if (tag == null)
            {
                return;
            }
            bool selected = ClCompat.Dialogs.ToggleSelectorSelected(dialog, tag);
            RefreshModel();
            TolkHelper.Speak((selected ? "RimWorldAccess.Shell.State.Selected" : "RimWorldAccess.Shell.State.NotSelected").Loc());
        }

        private void EditCurrent()
        {
            object tag = CurrentTag();
            if (tag != null)
            {
                ClCompat.OpenTagEditor(tag);
            }
        }

        private object CurrentTag()
        {
            return Model.RegionIndex == 0 && Model.CurrentRegion != null ? TagAt(Model.CurrentRegion.Index) : null;
        }

        private object TagAt(int index)
        {
            int tagIndex = index - 1;
            return tagIndex >= 0 && tagIndex < tags.Count ? tags[tagIndex] : null;
        }

        /// <summary>Create New Tag and Select Tag(s) are captured from the draw; the close X is an icon.</summary>
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
