using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Action inventory for the Compositable Loadouts compat screens (src/Compat/CompositableLoadouts/).
    /// Each feature slice implements one partial hook below in its own file, so no slice edits another's.
    /// </summary>
    internal static partial class ShellActionInventoryCompositableLoadouts
    {
        internal static void Register(ActionCatalog c)
        {
            // ---- ClTagSelectorScope (Dialog_TagSelector) ----
            // Space is the row's Shift+click multi-select; Alt+E its edit icon.
            c.Register(InputAction.ForScreen("clTagSelector", "clTagSelector.toggleSelection",
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            c.Register(InputAction.ForScreen("clTagSelector", "clTagSelector.editTag",
                new List<KeyChord> { KeyChord.Of(KeyCode.E, alt: true) }));

            RegisterLoadoutEditor(c);
            RegisterTagEditor(c);
            RegisterOverview(c);
            RegisterBillsAndSettings(c);
        }

        static partial void RegisterLoadoutEditor(ActionCatalog c);

        static partial void RegisterTagEditor(ActionCatalog c);

        static partial void RegisterOverview(ActionCatalog c);

        static partial void RegisterBillsAndSettings(ActionCatalog c);
    }
}
