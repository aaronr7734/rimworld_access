using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for the RimTalk TTS addon's reference browser,
    /// <c>RimTalk.TTS.UI.VoiceLibraryWindow</c> (opened from the settings page's Voice Library
    /// button for whichever supplier is active). The ~90-entry static voice list is entirely
    /// reflected data with five columns per row, so it rides a <see cref="ScreenScope"/> table
    /// region rather than a WidgetCapture pass over literal widget rects.
    ///
    /// WHY BESPOKE, given that this window sets <c>absorbInputAroundWindow = true</c> and so is
    /// already auto-eligible for GenericWindowScope with no registration at all: DoWindowContents
    /// draws each row as FIVE separate Labels, each in its own narrower sub-rect, all wrapped in
    /// one whole-row <c>Widgets.ButtonInvisible</c>. The generic reader's InvisibleButton-to-Label
    /// fusion rule finds exactly one Label per InvisibleButton (the nearest its backward search
    /// reaches), so a generic read would present one column and silently drop the other four,
    /// including the voice Name itself.
    ///
    /// FILTERING mirrors DoWindowContents' own LINQ: search matches
    /// Name/LanguageDisplay/Personality case-insensitively; the language filter (when not "All")
    /// matches LanguageDisplay; surviving rows group by LanguageDisplay alphabetically, each group
    /// ordered by Name, with a read-only "{language} ({count} voices)" divider row ahead of its
    /// members.
    ///
    /// SEARCH FIELD is the one hand-built row (Filters region, a genuine
    /// <see cref="TextFieldEditSession"/>): <c>searchText</c> is a plain private field with no
    /// gated setter (MUTATION-C, mirrors <c>searchText = Widgets.TextField(...)</c>'s own
    /// return-assign). The LANGUAGE filter is left to the captured-extras region instead: it is a
    /// real <c>Widgets.ButtonText</c> opening a real <c>FloatMenu</c> built from the mod's own
    /// <c>FloatMenuOption</c> delegates, so <c>selectedLanguage</c> needs only a read, never a
    /// reflected write.
    ///
    /// ACTIVATING a data row reproduces DoWindowContents' own click handler
    /// (<c>GUIUtility.systemCopyBuffer = item3.Name; Messages.Message(...)</c>); both are real
    /// compile-time APIs, and the resulting toast already flows through the message-to-speech
    /// bridge.
    ///
    /// ACCEPT/CANCEL: VoiceLibraryWindow overrides NEITHER <c>OnAcceptKeyPressed</c> nor
    /// <c>OnCancelKeyPressed</c>, so <see cref="ScreenScope"/>'s own defaults are correct with no
    /// twin patch.
    /// </summary>
    public sealed class RimTalkTtsVoiceLibraryScope : ScreenScope
    {
        private const int FiltersRegion = 0;
        private const int TableRegion = 1;

        private readonly Window dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();

        private struct VoiceRow
        {
            public bool IsHeader;
            public string HeaderText;
            public string Name;
            public string Gender;
            public string Language;
            public string Personality;
            public string Category;
        }

        private readonly List<VoiceRow> rows = new List<VoiceRow>();

        public RimTalkTtsVoiceLibraryScope(Window dialog)
        {
            this.dialog = dialog;

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "rimtalk-tts-voice-library"; }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == FiltersRegion
                ? "RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.FiltersRegionName".Translate()
                : "RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.TableRegionName".Translate();
        }

        protected override void RefreshContent()
        {
            rows.Clear();

            IList allVoices = RimTalkTtsVoiceLibraryCompat.GetAllVoices();
            if (allVoices == null)
            {
                return;
            }

            string searchText = RimTalkTtsVoiceLibraryCompat.GetSearchText(dialog) ?? "";
            string selectedLanguage = RimTalkTtsVoiceLibraryCompat.GetSelectedLanguage(dialog) ?? "All";

            var filtered = new List<RimTalkTtsVoiceLibraryCompat.VoiceEntry>();
            foreach (object entry in allVoices)
            {
                if (entry == null)
                {
                    continue;
                }
                RimTalkTtsVoiceLibraryCompat.VoiceEntry v = RimTalkTtsVoiceLibraryCompat.ReadEntry(entry);
                if (!string.IsNullOrEmpty(searchText))
                {
                    string needle = searchText.ToLowerInvariant();
                    bool matches = (v.Name ?? "").ToLowerInvariant().Contains(needle)
                        || (v.LanguageDisplay ?? "").ToLowerInvariant().Contains(needle)
                        || (v.Personality ?? "").ToLowerInvariant().Contains(needle);
                    if (!matches)
                    {
                        continue;
                    }
                }
                if (selectedLanguage != "All" && v.LanguageDisplay != selectedLanguage)
                {
                    continue;
                }
                filtered.Add(v);
            }

            filtered.Sort((a, b) =>
            {
                int langCompare = string.CompareOrdinal(a.LanguageDisplay, b.LanguageDisplay);
                return langCompare != 0 ? langCompare : string.CompareOrdinal(a.Name, b.Name);
            });

            string lastLanguage = null;
            int groupCount = 0;
            int groupStart = -1;
            for (int i = 0; i <= filtered.Count; i++)
            {
                bool boundary = i == filtered.Count || filtered[i].LanguageDisplay != lastLanguage;
                if (boundary && groupStart >= 0)
                {
                    rows.Insert(groupStart, new VoiceRow
                    {
                        IsHeader = true,
                        HeaderText = groupCount == 1
                            ? "RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.GroupHeaderOne".Translate(lastLanguage, groupCount)
                            : "RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.GroupHeaderMany".Translate(lastLanguage, groupCount),
                    });
                    groupStart = -1;
                    groupCount = 0;
                }
                if (i == filtered.Count)
                {
                    break;
                }
                if (groupStart < 0)
                {
                    groupStart = rows.Count;
                    lastLanguage = filtered[i].LanguageDisplay;
                }
                groupCount++;
                RimTalkTtsVoiceLibraryCompat.VoiceEntry v = filtered[i];
                rows.Add(new VoiceRow
                {
                    Name = v.Name,
                    Gender = v.Gender,
                    Language = v.LanguageDisplay,
                    Personality = v.Personality,
                    Category = v.Category,
                });
            }
        }

        protected override int ContentItemCount(int region)
        {
            return region == FiltersRegion ? 1 : rows.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == TableRegion ? 5 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != TableRegion)
            {
                return null;
            }
            switch (column)
            {
                case 0: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.ColumnName".Translate());
                case 1: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.ColumnGender".Translate());
                case 2: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.ColumnLanguage".Translate());
                case 3: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.ColumnPersonality".Translate());
                default: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.ColumnCategory".Translate());
            }
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != TableRegion || row < 0 || row >= rows.Count)
            {
                return "";
            }
            VoiceRow r = rows[row];
            if (r.IsHeader)
            {
                return column == 0 ? r.HeaderText : "";
            }
            switch (column)
            {
                case 0: return r.Name;
                case 1: return r.Gender;
                case 2: return r.Language;
                case 3: return r.Personality;
                default: return r.Category;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (region == FiltersRegion)
            {
                d.Role = ElementRole.TextField;
                d.Label = "RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.SearchLabel".Translate();
                string value = RimTalkTtsVoiceLibraryCompat.GetSearchText(dialog);
                if (string.IsNullOrEmpty(value))
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = value;
                }
                return d;
            }

            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            VoiceRow r = rows[index];
            if (r.IsHeader)
            {
                d.Label = r.HeaderText;
                d.Role = ElementRole.None;
                d.ReadOnly = true;
                return d;
            }
            d.Label = r.Name;
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == FiltersRegion)
            {
                BeginEditSearch();
                return;
            }
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            VoiceRow r = rows[index];
            if (r.IsHeader)
            {
                AnnounceCurrentItem();
                return;
            }
            // Vehicle A: the exact real APIs DoWindowContents' own row click calls. The English
            // literal is the addon's own compiled string character for character -- mod-text
            // parity, not an authored string, so it deliberately bypasses Translate.
            GUIUtility.systemCopyBuffer = r.Name;
            Messages.Message("Copied to clipboard: " + r.Name, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// Each voice row is DoWindowContents' own whole-row <c>ButtonInvisible</c>, drawn in
        /// the same grouped order <see cref="RefreshContent"/> mirrors; group headers draw
        /// none, so the ordinal counts data rows only.
        /// </summary>
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != TableRegion || region == null
                || region.Index < 0 || region.Index >= rows.Count || rows[region.Index].IsHeader)
            {
                return default(Rect);
            }
            int ordinal = 0;
            for (int i = 0; i < region.Index; i++)
            {
                if (!rows[i].IsHeader)
                {
                    ordinal++;
                }
            }
            int capture = WidgetCapture.IndexOfKind(WidgetKind.InvisibleButton, ordinal);
            return capture < 0 ? default(Rect) : WidgetCapture.Items[capture].VisibleScreenRect;
        }

        private void BeginEditSearch()
        {
            string current = RimTalkTtsVoiceLibraryCompat.GetSearchText(dialog) ?? "";
            var spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: null, minLength: 0);
            session.EnterEdit(
                current,
                spec,
                "RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.SearchLabel".Translate(),
                ApplySearchValue,
                ReAnnounceFiltersRow,
                announcePrompt: true);
        }

        /// <summary>MUTATION-C: mirrors DoWindowContents' own return-assign (`searchText = Widgets.TextField(...)`) -- a bare private field with no gated setter.</summary>
        private void ApplySearchValue(string value)
        {
            RimTalkTtsVoiceLibraryCompat.SetSearchText(dialog, value ?? "");
            RefreshModel();
        }

        private void ReAnnounceFiltersRow()
        {
            AnnounceCurrentItem();
        }

        // The Language filter button (a real ButtonText opening a real FloatMenu) rides the
        // captured-extras region automatically.

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>Drops the TextField capture -- the hand-built Filters row owns searchText instead.</summary>
        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return widget.Kind == WidgetKind.TextField;
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.Compat.RimTalk.TTS.VoiceLibrary.Opened".Translate();
        }
    }
}
