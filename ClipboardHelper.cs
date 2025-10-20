using UnityEngine;

namespace RimWorldAccess
{
    public static class ClipboardHelper
    {
        public static void CopyToClipboard(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                GUIUtility.systemCopyBuffer = text;
            }
        }
    }
}
