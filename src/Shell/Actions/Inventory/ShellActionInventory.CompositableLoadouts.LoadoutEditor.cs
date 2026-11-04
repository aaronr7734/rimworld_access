using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    internal static partial class ShellActionInventoryCompositableLoadouts
    {
        static partial void RegisterLoadoutEditor(ActionCatalog c)
        {
            // ---- ClLoadoutEditorScope (Dialog_LoadoutEditor) ----
            // Comma and Period are the window's own (and vanilla's) colonist cycling keys; Alt+P and
            // Alt+N are the character editor's previous and next pawn chords.
            c.Register(InputAction.ForScreen("clLoadoutEditor", "clLoadoutEditor.previousPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Comma), KeyChord.Of(KeyCode.P, alt: true) }));
            c.Register(InputAction.ForScreen("clLoadoutEditor", "clLoadoutEditor.nextPawn",
                new List<KeyChord> { KeyChord.Of(KeyCode.Period), KeyChord.Of(KeyCode.N, alt: true) }));
            c.Register(InputAction.ForScreen("clLoadoutEditor", "clLoadoutEditor.editTag",
                new List<KeyChord> { KeyChord.Of(KeyCode.E, alt: true) }));
            c.Register(InputAction.ForScreen("clLoadoutEditor", "clLoadoutEditor.removeTag",
                new List<KeyChord> { KeyChord.Of(KeyCode.Delete) }));
        }
    }
}
