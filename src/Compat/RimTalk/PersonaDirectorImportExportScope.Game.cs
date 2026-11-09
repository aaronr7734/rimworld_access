using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Persona Director's <c>Window_ImportExport</c>. Its import buttons and text box are plain
    /// Widgets and ride captured extras. Its Export button is a raw <c>GUI.Button</c> drawn only on
    /// repaint, and the click is a raw mouse-up test where the left button copies to the
    /// clipboard and the right button saves to a file, so both are declared here and run the
    /// window's own two export methods (vehicle A).
    /// </summary>
    internal sealed class PersonaDirectorImportExportScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public PersonaDirectorImportExportScope(Window dialog)
        {
            this.dialog = dialog;
        }

        public override string Name
        {
            get { return "rimtalk-persona-director-import-export"; }
        }

        protected override int ContentRegionCount
        {
            get { return 0; }
        }

        protected override string ContentRegionName(int region)
        {
            return "";
        }

        protected override int ContentItemCount(int region)
        {
            return 0;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return new ElementDescription();
        }

        protected override void ActivateContentItem(int region, int index)
        {
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return widget.Kind == WidgetKind.Button && widget.Label == Translator.Translate("RPD_IO_Export").Resolve();
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.PersonaDirector.ExportToClipboard".Translate(),
                    () => PersonaDirectorCompat.ExportToClipboard(dialog)));
                actions.Add(new ScreenAction(
                    "RimWorldAccess.Compat.RimTalk.PersonaDirector.ExportToFile".Translate(),
                    () => PersonaDirectorCompat.ExportToFile(dialog)));
                return actions;
            }
        }
    }
}
