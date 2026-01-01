using Verse;
using RimWorld;

namespace RimWorldAccess
{
    public static class GiveNameDialogState
    {
        private static Dialog_GiveName currentDialog;
        private static int currentFocusIndex = 0; // 0 = first text field, 1 = first randomize button, 2 = second text field, 3 = second randomize button, 4 = OK button
        private static bool hasAnnounced = false;

        public static void Initialize(Dialog_GiveName dialog)
        {
            // Only reset if we're switching to a different dialog or a new one
            if (currentDialog != dialog)
            {
                currentDialog = dialog;
                currentFocusIndex = 0;
                hasAnnounced = false;
            }
        }

        public static void Reset()
        {
            currentDialog = null;
            currentFocusIndex = 0;
            hasAnnounced = false;
        }

        public static int GetFocusIndex()
        {
            return currentFocusIndex;
        }

        public static void SetFocusIndex(int index)
        {
            currentFocusIndex = index;
        }

        public static void MoveNext(bool useSecondName)
        {
            int maxIndex = useSecondName ? 4 : 2; // 0: field1, 1: random1, [2: field2, 3: random2], 4/2: OK
            currentFocusIndex = (currentFocusIndex + 1) % (maxIndex + 1);
        }

        public static void MovePrevious(bool useSecondName)
        {
            int maxIndex = useSecondName ? 4 : 2;
            currentFocusIndex--;
            if (currentFocusIndex < 0)
            {
                currentFocusIndex = maxIndex;
            }
        }

        public static bool HasAnnounced()
        {
            return hasAnnounced;
        }

        public static void MarkAsAnnounced()
        {
            hasAnnounced = true;
        }
    }
}
