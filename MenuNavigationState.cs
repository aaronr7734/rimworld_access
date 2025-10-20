using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    public static class MenuNavigationState
    {
        private static int currentColumn = 0;
        private static int selectedIndexColumn0 = 0;
        private static int selectedIndexColumn1 = 0;

        private static List<ListableOption> column0Options = new List<ListableOption>();
        private static List<ListableOption> column1Options = new List<ListableOption>();

        public static int CurrentColumn => currentColumn;
        public static int SelectedIndex => currentColumn == 0 ? selectedIndexColumn0 : selectedIndexColumn1;

        public static void Initialize(List<ListableOption> col0, List<ListableOption> col1)
        {
            column0Options = col0;
            column1Options = col1;

            // Ensure indices are valid
            if (selectedIndexColumn0 >= col0.Count)
                selectedIndexColumn0 = Math.Max(0, col0.Count - 1);
            if (selectedIndexColumn1 >= col1.Count)
                selectedIndexColumn1 = Math.Max(0, col1.Count - 1);
        }

        public static void MoveUp()
        {
            if (currentColumn == 0)
            {
                selectedIndexColumn0--;
                if (selectedIndexColumn0 < 0)
                    selectedIndexColumn0 = Math.Max(0, column0Options.Count - 1);
            }
            else
            {
                selectedIndexColumn1--;
                if (selectedIndexColumn1 < 0)
                    selectedIndexColumn1 = Math.Max(0, column1Options.Count - 1);
            }

            UpdateClipboard();
        }

        public static void MoveDown()
        {
            if (currentColumn == 0)
            {
                selectedIndexColumn0++;
                if (selectedIndexColumn0 >= column0Options.Count)
                    selectedIndexColumn0 = 0;
            }
            else
            {
                selectedIndexColumn1++;
                if (selectedIndexColumn1 >= column1Options.Count)
                    selectedIndexColumn1 = 0;
            }

            UpdateClipboard();
        }

        public static void SwitchColumn()
        {
            currentColumn = (currentColumn == 0) ? 1 : 0;
            UpdateClipboard();
        }

        public static void ActivateSelected()
        {
            ListableOption selected = GetCurrentSelection();
            if (selected?.action != null)
            {
                selected.action();
            }
        }

        public static ListableOption GetCurrentSelection()
        {
            if (currentColumn == 0 && selectedIndexColumn0 >= 0 && selectedIndexColumn0 < column0Options.Count)
            {
                return column0Options[selectedIndexColumn0];
            }
            else if (currentColumn == 1 && selectedIndexColumn1 >= 0 && selectedIndexColumn1 < column1Options.Count)
            {
                return column1Options[selectedIndexColumn1];
            }
            return null;
        }

        private static void UpdateClipboard()
        {
            ListableOption selected = GetCurrentSelection();
            if (selected != null)
            {
                ClipboardHelper.CopyToClipboard(selected.label);
            }
        }

        public static void Reset()
        {
            currentColumn = 0;
            selectedIndexColumn0 = 0;
            selectedIndexColumn1 = 0;
        }
    }
}
