using System;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A text-field row over a mod window's own <see cref="QuickSearchWidget"/>: describes the
    /// widget's live text and edits it through the owning scope's <see cref="TextFieldEditSession"/>,
    /// which the scope still mirrors and tears down. <c>onFilterChanged</c> runs what the window's
    /// own <c>onFilterChange</c> would after the widget's text write.
    /// </summary>
    internal sealed class ForeignQuickSearchRow
    {
        private readonly TextFieldEditSession session;
        private readonly Func<QuickSearchWidget> widget;
        private readonly Func<string> label;
        private readonly Action onFilterChanged;

        public ForeignQuickSearchRow(TextFieldEditSession session, Func<QuickSearchWidget> widget, Func<string> label, Action onFilterChanged)
        {
            this.session = session;
            this.widget = widget;
            this.label = label;
            this.onFilterChanged = onFilterChanged;
        }

        public void Describe(ElementDescription d)
        {
            d.Label = label();
            d.Role = ElementRole.TextField;
            d.EntersEditOnAccept = true;
            string text = widget()?.filter.Text;
            if (text.NullOrEmpty())
            {
                d.ValueBlank = true;
            }
            else
            {
                d.Value = text;
            }
        }

        public void BeginEdit(Action onClosed)
        {
            QuickSearchWidget search = widget();
            if (search == null)
            {
                return;
            }
            session.EnterEdit(search.filter.Text, new TextFieldSpec(labelKey: null, maxLength: search.maxSearchTextLength, minLength: 0),
                label(), SetText, onClosed);
        }

        private void SetText(string value)
        {
            QuickSearchWidget search = widget();
            if (search == null || search.filter.Text == (value ?? ""))
            {
                return;
            }
            search.filter.Text = value ?? ""; // MUTATION-C: mirrors QuickSearchWidget.OnGUI's text write, then the host's onFilterChange; the widget's only text writer is its IMGUI field
            onFilterChanged?.Invoke();
        }
    }
}
