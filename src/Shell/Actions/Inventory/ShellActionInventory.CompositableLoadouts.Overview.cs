using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    internal static partial class ShellActionInventoryCompositableLoadouts
    {
        // ---- ClOverviewScope (MainTabWindow_LoadoutOverview) ----
        // Space, Ctrl+Space and Shift+Space are the name's click, Ctrl+click and Shift+click.
        // Cut, copy and paste stand in for dragging a tag between rows (Ctrl held copies).
        static partial void RegisterOverview(ActionCatalog c)
        {
            c.Register(InputAction.ForScreen("clOverview", "clOverview.select",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("clOverview", "clOverview.toggleSelection",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space, ctrl: true) }));
            c.Register(InputAction.ForScreen("clOverview", "clOverview.selectRange",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space, shift: true) }));
            c.Register(InputAction.ForScreen("clOverview", "clOverview.removeTag",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("clOverview", "clOverview.contextMenu",
                new List<KeyChord> { KeyChord.Of(KeyCode.RightBracket) }));
            c.Register(InputAction.ForScreen("clOverview", "clOverview.cut",
                new List<KeyChord> { KeyChord.Of(KeyCode.X, ctrl: true) }));
            c.Register(InputAction.ForScreen("clOverview", "clOverview.copy",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));
            c.Register(InputAction.ForScreen("clOverview", "clOverview.paste",
                new List<KeyChord> { KeyChord.Of(KeyCode.V, ctrl: true) }));
        }
    }
}
