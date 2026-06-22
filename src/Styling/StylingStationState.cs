using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard navigation for vanilla's <c>Dialog_StylingStation</c> (Ideology). Presents the
    /// dialog as a set of tabs — Hair, Beard, Face tattoo, Body tattoo, Apparel color — each a flat
    /// list with typeahead. Style items are applied live; colors are chosen through the unified
    /// Alt+I picker. Alt+S accepts (schedules the styling job), Escape cancels, ] resets.
    ///
    /// Mirrors the State + reflection-bridge pattern: navigation lives here, all dialog field/method
    /// access goes through <see cref="StylingStationHelper"/>.
    /// </summary>
    public static class StylingStationState
    {
        private static bool isActive;
        private static Window currentDialog;
        private static Pawn pawn;

        private static readonly List<StylingTabKind> tabs = new List<StylingTabKind>();
        private static int tabIndex;

        // Active-tab working lists (only one is populated at a time).
        private static List<StyleItemDef> styleItems = new List<StyleItemDef>();
        private static List<Apparel> apparelItems = new List<Apparel>();
        private static int selectedIndex;

        private static readonly TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        public static bool IsActive => isActive;
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        private static StylingTabKind CurrentKind => tabs[tabIndex];
        private static bool IsApparelTab => CurrentKind == StylingTabKind.ApparelColor;
        private static int ListCount => IsApparelTab ? apparelItems.Count : styleItems.Count;

        // ===== Lifecycle =====

        public static void Open(Window dialog)
        {
            if (dialog == null) return;

            try
            {
                currentDialog = dialog;
                pawn = StylingStationHelper.GetPawn(dialog);
                if (pawn == null) { Close(); return; }

                tabs.Clear();
                tabs.Add(StylingTabKind.Hair);
                if (pawn.style != null && pawn.style.CanWantBeard)
                    tabs.Add(StylingTabKind.Beard);
                tabs.Add(StylingTabKind.FaceTattoo);
                tabs.Add(StylingTabKind.BodyTattoo);
                tabs.Add(StylingTabKind.ApparelColor);

                tabIndex = 0;
                typeahead.ClearSearch();
                isActive = true;

                LoadTab(announce: false);

                string pawnName = pawn.Name?.ToStringShort ?? pawn.LabelShortCap;
                TolkHelper.SpeakData((string)"RimWorldAccess.Styling.Opened".Translate(pawnName));
                AnnounceTab();
            }
            catch (Exception ex)
            {
                Log.Error($"[StylingStationState] Error opening: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            currentDialog = null;
            pawn = null;
            tabs.Clear();
            styleItems.Clear();
            apparelItems.Clear();
            selectedIndex = 0;
            typeahead.ClearSearch();
        }

        // ===== Input =====

        public static bool HandleInput(Event evt)
        {
            if (!isActive || currentDialog == null) return false;

            // Defer to the color picker float menu while it's open (it lives at a lower priority).
            if (WindowlessFloatMenuState.IsActive) return false;

            if (evt.type != EventType.KeyDown) return false;

            KeyCode key = evt.keyCode;
            bool ctrl = evt.control;
            bool alt = KeyboardHelper.IsAltHeld;
            bool shift = evt.shift;

            // Alt+S — accept and apply.
            if (alt && key == KeyCode.S)
            {
                StylingStationHelper.Accept(currentDialog);
                return true;
            }

            // Tab / Shift+Tab — switch styling tabs.
            if (key == KeyCode.Tab && !ctrl && !alt)
            {
                CycleTab(shift ? -1 : 1);
                return true;
            }

            // Consume any remaining modifier combos so they don't leak to the game.
            if (ctrl || alt) return true;

            switch (key)
            {
                case KeyCode.RightBracket:
                    StylingStationHelper.ResetAll(currentDialog);
                    LoadTab(announce: false);
                    TolkHelper.Speak("RimWorldAccess.Styling.ResetDone".Loc());
                    AnnounceCurrentItem();
                    return true;

                case KeyCode.Escape:
                    if (typeahead.HasActiveSearch)
                    {
                        typeahead.ClearSearchAndAnnounce();
                        AnnounceCurrentItem();
                        return true;
                    }
                    // Revert all pending changes, then close. PostClose announces the dismissal.
                    StylingStationHelper.ResetAll(currentDialog);
                    currentDialog.Close(doCloseSound: false);
                    return true;

                case KeyCode.Home:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int firstMatch = typeahead.GetFirstMatch();
                        if (firstMatch >= 0) selectedIndex = firstMatch;
                        AnnounceWithSearch();
                        return true;
                    }
                    typeahead.ClearSearch();
                    selectedIndex = MenuHelper.JumpToFirst();
                    AnnounceCurrentItem();
                    return true;

                case KeyCode.End:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int lastMatch = typeahead.GetLastMatch();
                        if (lastMatch >= 0) selectedIndex = lastMatch;
                        AnnounceWithSearch();
                        return true;
                    }
                    typeahead.ClearSearch();
                    selectedIndex = MenuHelper.JumpToLast(ListCount);
                    AnnounceCurrentItem();
                    return true;

                case KeyCode.UpArrow:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int prev = typeahead.GetPreviousMatch(selectedIndex);
                        if (prev >= 0) selectedIndex = prev;
                        AnnounceWithSearch();
                    }
                    else if (ListCount > 0)
                    {
                        selectedIndex = MenuHelper.SelectPrevious(selectedIndex, ListCount);
                        AnnounceCurrentItem();
                    }
                    return true;

                case KeyCode.DownArrow:
                    if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                    {
                        int next = typeahead.GetNextMatch(selectedIndex);
                        if (next >= 0) selectedIndex = next;
                        AnnounceWithSearch();
                    }
                    else if (ListCount > 0)
                    {
                        selectedIndex = MenuHelper.SelectNext(selectedIndex, ListCount);
                        AnnounceCurrentItem();
                    }
                    return true;

                case KeyCode.Backspace:
                    if (typeahead.HasActiveSearch)
                    {
                        if (typeahead.ProcessBackspace(GetLabels(), out int backIdx))
                        {
                            selectedIndex = backIdx;
                            AnnounceWithSearch();
                        }
                        else
                        {
                            AnnounceCurrentItem();
                        }
                    }
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    ActivateCurrent();
                    return true;

                default:
                    return true; // Modal window: consume unhandled keys; typeahead routed via TypeaheadDispatcher.
            }
        }

        public static void HandleTypeahead(char c)
        {
            if (!isActive) return;

            if (typeahead.ProcessCharacterInput(c, GetLabels(), out int newIdx))
            {
                if (newIdx >= 0)
                {
                    selectedIndex = newIdx;
                    AnnounceWithSearch();
                }
            }
            else
            {
                typeahead.SpeakNoMatches();
            }
        }

        // ===== Tab management =====

        private static void CycleTab(int direction)
        {
            typeahead.ClearSearch();
            tabIndex = (tabIndex + direction + tabs.Count) % tabs.Count;
            LoadTab(announce: false);
            AnnounceTab();
        }

        private static void LoadTab(bool announce)
        {
            typeahead.ClearSearch();
            StylingStationHelper.SetVisibleTab(currentDialog, CurrentKind);

            if (IsApparelTab)
            {
                styleItems.Clear();
                apparelItems = BuildApparelList();
                selectedIndex = 0;
            }
            else
            {
                apparelItems.Clear();
                styleItems = StylingStationHelper.BuildItems(pawn, CurrentKind);
                // Land on the currently applied style.
                StyleItemDef current = StylingStationHelper.GetCurrentItem(pawn, CurrentKind);
                int idx = current != null ? styleItems.IndexOf(current) : -1;
                selectedIndex = idx >= 0 ? idx : 0;
            }

            if (announce) AnnounceTab();
        }

        private static List<Apparel> BuildApparelList()
        {
            var colors = StylingStationHelper.GetApparelColors(currentDialog);
            if (colors == null || pawn.apparel == null) return new List<Apparel>();
            // Preserve worn order, include only colorable pieces (the dict's keys).
            return pawn.apparel.WornApparel.Where(colors.ContainsKey).ToList();
        }

        // ===== Activation =====

        private static void ActivateCurrent()
        {
            if (ListCount == 0) return;

            // Choosing an item drills into the color dropdown next, so clear any typeahead search
            // now — otherwise the buffer would still be live when we return to the list.
            typeahead.ClearSearch();

            if (IsApparelTab)
            {
                // An apparel row's only action is recoloring it.
                OpenApparelColorPicker();
                return;
            }

            // Apply the selected style item live, then open its color dropdown. Hair and beard
            // share the hair color; tattoos have no color, so just confirm the selection.
            StyleItemDef item = styleItems[selectedIndex];
            StylingStationHelper.SelectItem(pawn, CurrentKind, item);

            if (CurrentKind == StylingTabKind.Hair || CurrentKind == StylingTabKind.Beard)
                OpenHairColorPicker();
            else
                TolkHelper.SpeakData((string)"RimWorldAccess.Styling.ItemSelected".Translate(item.LabelCap));
        }

        // ===== Color pickers =====

        private static void OpenHairColorPicker()
        {
            Color current = StylingStationHelper.GetDesiredHairColor(currentDialog);
            List<Color> colors = StylingStationHelper.GetAllHairColors(currentDialog);
            List<string> names = ColorNameHelper.NamesForColors(colors);

            var options = new List<FloatMenuOption>();
            int startIndex = 0;
            for (int i = 0; i < colors.Count; i++)
            {
                Color captured = colors[i];
                string name = names[i];
                string label = name;
                if (current.IndistinguishableFrom(captured))
                {
                    label += ", " + "RimWorldAccess.Styling.CurrentColor".Translate();
                    startIndex = i;
                }
                options.Add(new FloatMenuOption(label, () =>
                {
                    StylingStationHelper.SetDesiredHairColor(currentDialog, captured);
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                    TolkHelper.SpeakData((string)"RimWorldAccess.Styling.ColorSet".Translate(name));
                }));
            }

            if (options.Count == 0) { TolkHelper.Speak("NoneLower".Loc()); return; }
            TolkHelper.Speak("RimWorldAccess.Styling.HairColorTitle".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex);
        }

        private static void OpenApparelColorPicker()
        {
            if (apparelItems.Count == 0) { TolkHelper.Speak("RimWorldAccess.Styling.NoApparel".Loc()); return; }

            Apparel apparel = apparelItems[selectedIndex];

            // Locked apparel cannot be recolored (matches vanilla's locked-row message).
            if (pawn.apparel != null && pawn.apparel.IsLocked(apparel))
            {
                TolkHelper.SpeakData(
                    (string)"ApparelLockedCannotRecolor".Translate(pawn.Named("PAWN"), apparel.Named("APPAREL")));
                return;
            }

            var colorsDict = StylingStationHelper.GetApparelColors(currentDialog);
            if (colorsDict == null) return;
            Color current = colorsDict.TryGetValue(apparel, out Color cc) ? cc : Color.white;

            List<Color> colors = StylingStationHelper.GetAllColors(currentDialog);
            List<string> names = ColorNameHelper.NamesForColors(colors);

            var options = new List<FloatMenuOption>();
            int startIndex = 0;
            for (int i = 0; i < colors.Count; i++)
            {
                Color captured = colors[i];
                string name = names[i];
                string label = name;
                if (current.IndistinguishableFrom(captured))
                {
                    label += ", " + "RimWorldAccess.Styling.CurrentColor".Translate();
                    startIndex = i;
                }
                options.Add(new FloatMenuOption(label, () => SetApparelColor(apparel, captured, name)));
            }

            // The dialog's "Set ideoligion color" / "Set favorite color" buttons, as menu entries.
            if (pawn.Ideo != null && !Find.IdeoManager.classicMode)
            {
                Color ideoColor = pawn.Ideo.ApparelColor;
                options.Add(new FloatMenuOption((string)"SetIdeoColor".Translate(),
                    () => SetApparelColor(apparel, ideoColor, ColorNameHelper.NameForColor(ideoColor))));
            }
            if (pawn.story?.favoriteColor != null)
            {
                Color favColor = pawn.story.favoriteColor.color;
                options.Add(new FloatMenuOption((string)"SetFavoriteColor".Translate(),
                    () => SetApparelColor(apparel, favColor, ColorNameHelper.NameForColor(favColor))));
            }

            if (options.Count == 0) { TolkHelper.Speak("NoneLower".Loc()); return; }
            TolkHelper.SpeakData((string)"RimWorldAccess.Styling.ApparelColorTitle".Translate(apparel.LabelCap));
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex);
        }

        private static void SetApparelColor(Apparel apparel, Color color, string colorName)
        {
            var colors = StylingStationHelper.GetApparelColors(currentDialog);
            if (colors == null) return;
            colors[apparel] = color;
            pawn.Drawer.renderer.SetAllGraphicsDirty();
            TolkHelper.SpeakData((string)"RimWorldAccess.Styling.ColorSet".Translate(colorName));
        }

        // ===== Announcements =====

        private static void AnnounceTab()
        {
            string tabName = TabLabel(CurrentKind);

            // Tab position respects the AnnouncePosition setting (empty when off).
            bool showPos = !string.IsNullOrEmpty(MenuHelper.FormatPosition(tabIndex, tabs.Count));
            string posClause = showPos
                ? (string)"RimWorldAccess.Styling.TabPosition".Translate(tabIndex + 1, tabs.Count) + ". "
                : "";

            if (ListCount == 0)
            {
                TolkHelper.SpeakData(posClause + (string)"RimWorldAccess.Styling.TabEmpty".Translate(tabName));
                return;
            }

            TolkHelper.SpeakData(posClause + (string)"RimWorldAccess.Styling.TabHeader".Translate(tabName, ListCount));
            AnnounceCurrentItem();
        }

        private static void AnnounceCurrentItem()
        {
            if (ListCount == 0 || selectedIndex < 0 || selectedIndex >= ListCount) return;
            string label = ItemLabel(selectedIndex);
            string position = MenuHelper.FormatPosition(selectedIndex, ListCount);
            TolkHelper.SpeakData(string.IsNullOrEmpty(position) ? label : $"{label}, {position}");
        }

        private static void AnnounceWithSearch()
        {
            if (ListCount == 0 || selectedIndex < 0 || selectedIndex >= ListCount) return;
            if (!typeahead.HasActiveSearch) { AnnounceCurrentItem(); return; }
            TolkHelper.SpeakData(
                $"{ItemLabel(selectedIndex)}{(string)"RimWorldAccess.Search.ContextSuffix".Translate(typeahead.CurrentMatchPosition, typeahead.MatchCount, typeahead.SearchBuffer)}");
        }

        private static string ItemLabel(int index)
        {
            if (IsApparelTab)
            {
                Apparel apparel = apparelItems[index];
                var colors = StylingStationHelper.GetApparelColors(currentDialog);
                string colorName = (colors != null && colors.TryGetValue(apparel, out Color c))
                    ? ColorNameHelper.NameForColor(c) : "";
                bool locked = pawn.apparel != null && pawn.apparel.IsLocked(apparel);
                string baseLabel = (string)"RimWorldAccess.Styling.ApparelRow".Translate(apparel.LabelCap, colorName);
                if (locked)
                    baseLabel += ", " + (string)"RimWorldAccess.Styling.Locked".Translate();
                return baseLabel;
            }

            StyleItemDef item = styleItems[index];
            string label = item.LabelCap.ToString();

            // Only the style currently applied to the pawn carries a color and the selected marker;
            // every other row is just its name (no color, since none is applied to it).
            if (item == StylingStationHelper.GetCurrentItem(pawn, CurrentKind))
            {
                // Hair and beard render in the hair color — surface it on the selected row.
                if (CurrentKind == StylingTabKind.Hair || CurrentKind == StylingTabKind.Beard)
                {
                    string colorName = ColorNameHelper.NameForColor(
                        StylingStationHelper.GetDesiredHairColor(currentDialog));
                    label += ", " + colorName;
                }
                label += ", " + (string)"RimWorldAccess.Styling.Selected".Translate();
            }

            // Trailing visual description so the player knows what the style looks like; placed last
            // so the actionable name (and selected marker) is heard first and can be skipped past.
            string description = StyleDescriptionHelper.Describe(item);
            if (!string.IsNullOrEmpty(description))
                label += ". " + description;

            return label;
        }

        private static List<string> GetLabels()
        {
            if (IsApparelTab)
                return apparelItems.Select(a => a.LabelCap.ToString()).ToList();
            return styleItems.Select(s => s.LabelCap.ToString()).ToList();
        }

        private static string TabLabel(StylingTabKind kind)
        {
            switch (kind)
            {
                case StylingTabKind.Hair: return "Hair".Translate().CapitalizeFirst();
                case StylingTabKind.Beard: return "Beard".Translate().CapitalizeFirst();
                case StylingTabKind.FaceTattoo: return "TattooFace".Translate().CapitalizeFirst();
                case StylingTabKind.BodyTattoo: return "TattooBody".Translate().CapitalizeFirst();
                case StylingTabKind.ApparelColor: return "ApparelColor".Translate().CapitalizeFirst();
                default: return "";
            }
        }
    }
}
