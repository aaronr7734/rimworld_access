using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The family's non-absorbing windows that draw only plain Widgets, opted into the generic
    /// reader by name (<see cref="ScopeForWindow.TryRegisterGenericReaderForWindow"/>); absorbing
    /// windows are already eligible and need no entry. Raw <c>GUI.TextArea</c> boxes (the custom
    /// request JSON, the API log's request editor) are captured like any text area. The API log
    /// draws its read-only request and response with a raw <c>GUI.Label</c>, which no capture
    /// sees; the same text is in the debug window's Details region.
    /// </summary>
    internal static class RimTalkGenericWindowsCompat
    {
        public static void Register()
        {
            if (ModsConfig.IsActive("cj.rimtalk"))
            {
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalk.UI.Dialog_ApiLog");
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalk.UI.Dialog_CustomizeRequest");
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalk.UI.Dialog_CustomizeRequestHelp");
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalk.UI.Dialog_ImageViewer");
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalk.Dialog_BubbleSettings");
            }
            if (ModsConfig.IsActive("sanguo.rimtalk.expandactions"))
            {
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimTalkExpandActions.UI.Window_ActionDebug");
            }
        }
    }
}
