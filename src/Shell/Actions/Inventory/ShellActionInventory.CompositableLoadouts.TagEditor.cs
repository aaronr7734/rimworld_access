using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    internal static partial class ShellActionInventoryCompositableLoadouts
    {
        // ---- ClTagEditorScope (Dialog_TagEditor) ----
        // A required item's row icons: remove, copy and paste its filter, the Edit icon, and the
        // add-alternative plus. Alt+E matches the tag picker's edit chord.
        static partial void RegisterTagEditor(ActionCatalog c)
        {
            c.Register(InputAction.ForScreen("clTagEditor", "clTagEditor.delete",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
            c.Register(InputAction.ForScreen("clTagEditor", "clTagEditor.copy",
                new List<KeyChord> { KeyChord.Of(KeyCode.C, ctrl: true) }));
            c.Register(InputAction.ForScreen("clTagEditor", "clTagEditor.paste",
                new List<KeyChord> { KeyChord.Of(KeyCode.V, ctrl: true) }));
            c.Register(InputAction.ForScreen("clTagEditor", "clTagEditor.edit",
                new List<KeyChord> { KeyChord.Of(KeyCode.E, alt: true) }));
            c.Register(InputAction.ForScreen("clTagEditor", "clTagEditor.addAlternative",
                new List<KeyChord> { KeyChord.Of(KeyCode.A, alt: true) }));
        }
    }
}
