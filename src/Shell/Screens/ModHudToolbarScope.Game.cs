using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Windowless state for the shared mod-HUD-toolbar menu: which registered
    /// <see cref="ModHudToolbarRegistry.Toolbar"/> is open, and the entry rows whose own
    /// availability gates passed at open time (labels stay live-resolved; the row SET is a
    /// snapshot so the list cannot reshuffle under the cursor mid-browse). Opened from the
    /// pause menu (one injected item per visible toolbar, <c>PauseMenuScope</c>), the same
    /// reachability shape as <see cref="PlaySettingsMenuState"/> — this project's precedent
    /// for a global control strip drawn outside any window.
    /// </summary>
    public static class ModHudToolbarState
    {
        private static ModHudToolbarRegistry.Toolbar current;
        private static readonly List<ModHudToolbarRegistry.Entry> rows = new List<ModHudToolbarRegistry.Entry>();

        public static bool IsActive
        {
            get { return current != null; }
        }

        internal static IReadOnlyList<ModHudToolbarRegistry.Entry> Rows
        {
            get { return rows; }
        }

        internal static string Title()
        {
            ModHudToolbarRegistry.Toolbar toolbar = current;
            if (toolbar == null)
            {
                return "";
            }
            try
            {
                return toolbar.Title() ?? "";
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Mod HUD toolbar title", ex);
                return "";
            }
        }

        public static void Open(string toolbarId)
        {
            ModHudToolbarRegistry.Toolbar toolbar = ModHudToolbarRegistry.Find(toolbarId);
            if (toolbar == null)
            {
                return;
            }
            rows.Clear();
            for (int i = 0; i < toolbar.Entries.Count; i++)
            {
                ModHudToolbarRegistry.Entry entry = toolbar.Entries[i];
                try
                {
                    if (entry.Available == null || entry.Available())
                    {
                        rows.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Mod HUD toolbar availability", ex);
                }
            }
            if (rows.Count == 0)
            {
                return;
            }
            current = toolbar;
        }

        public static void Close()
        {
            current = null;
            rows.Clear();
        }

        internal static ModHudToolbarRegistry.Entry SoleActionEntry(ModHudToolbarRegistry.Toolbar toolbar)
        {
            ModHudToolbarRegistry.Entry sole = null;
            for (int i = 0; i < toolbar.Entries.Count; i++)
            {
                ModHudToolbarRegistry.Entry entry = toolbar.Entries[i];
                if (entry.Available != null && !entry.Available())
                {
                    continue;
                }
                if (sole != null || entry.Activate == null)
                {
                    return null;
                }
                sole = entry;
            }
            return sole;
        }
    }

    /// <summary>
    /// The shared menu over <see cref="ModHudToolbarState"/>: one list region titled with the
    /// toolbar's own name, one row per HUD button (read-only rows for readouts like a ping
    /// label). Activating a row closes this menu FIRST, then runs the entry's mirrored click
    /// body — the <c>PlaySettingsMenuState.OpenEntityCodex</c> posture, so a windowless overlay
    /// scope is never left live underneath the real window the click opens. Escape returns to
    /// the pause menu while Playing, matching how the play-settings menu's own Escape behaves
    /// (<see cref="RimWorldAccess.PlaySettingsMenuState"/>).
    /// </summary>
    public sealed class ModHudToolbarScope : ScreenScope
    {
        private bool announcedOpen;

        public ModHudToolbarScope()
        {
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "mod-hud-toolbar"; }
        }

        /// <summary>Windowless overlay: this scope owns Escape itself unconditionally — there is no typeahead here to gate on.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return ModHudToolbarState.Title();
        }

        protected override int ContentItemCount(int region)
        {
            return ModHudToolbarState.Rows.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            IReadOnlyList<ModHudToolbarRegistry.Entry> rows = ModHudToolbarState.Rows;
            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            ModHudToolbarRegistry.Entry entry = rows[index];
            try
            {
                d.Label = entry.Label() ?? "";
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Mod HUD toolbar label", ex);
                d.Label = "";
            }
            if (entry.Activate == null)
            {
                d.ReadOnly = true;
            }
            else
            {
                d.Role = ElementRole.Button;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            IReadOnlyList<ModHudToolbarRegistry.Entry> rows = ModHudToolbarState.Rows;
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            ModHudToolbarRegistry.Entry entry = rows[index];
            if (entry.Activate == null)
            {
                return;
            }
            ModHudToolbarState.Close();
            try
            {
                entry.Activate();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Mod HUD toolbar activation", ex);
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            RefreshModel();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            AnnounceCurrentItem();
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            ModHudToolbarState.Close();
            // Return to the pause menu, the same posture the play-settings menu's own Escape
            // takes (PlaySettingsMenuState.HandleClose) — this menu is a pause-menu drill-in,
            // so Escape backs out one level instead of dropping the user onto the map.
            if (Current.ProgramState == ProgramState.Playing)
            {
                PauseMenuScope.OpenRealMenu();
            }
        }
    }

    /// <summary>
    /// Chain-independent mirror (law-J-α bare-IsActive gate): the only opener is the pause
    /// menu's own injected item, which closes the real menu window before Open() runs, and no
    /// other mirrored state can be simultaneously active with a pause-menu drill-in (the same
    /// mutual-exclusion argument the play-settings menu's own opener shares, since both are
    /// injected items on the identical pause-menu list). Rows never open an info card, so no
    /// InfoCard stand-down term is needed.
    /// </summary>
    internal static class ModHudToolbarScopeMirror
    {
        private static readonly ModHudToolbarScope scope = new ModHudToolbarScope();

        public static void Reconcile()
        {
            if (ModHudToolbarState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
