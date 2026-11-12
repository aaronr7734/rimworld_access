using System;
using System.Collections;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Drives HugsLib's update-news dialog (<c>Dialog_UpdateFeatures</c> / its internal
    /// <c>Dialog_UpdateFeaturesFiltered</c> subclass, the one HugsLib actually constructs). The
    /// generic window reader refuses this dialog because it sets
    /// <c>absorbInputAroundWindow = false</c>, and it auto-opens whenever an installed HugsLib
    /// mod's version advances, so its content has to be spoken on arrival rather than on request.
    ///
    /// Content is read from the dialog's OWN parsed state every refresh (the <c>entries</c>
    /// field -- each a def plus its already-parsed <c>DescriptionSegment</c> list) rather than
    /// re-parsing <c>UpdateFeatureDef.content</c> ourselves, so this scope can never drift from
    /// whatever HugsLib's own <c>ParseEntryContent</c> decided. One content region ("Update
    /// news"), rows per entry in list order: title, then one row per segment (text verbatim,
    /// image as a placeholder since <c>imageNames</c> are texture paths with nothing
    /// player-meaningful to read, caption folded onto its own row), then the ignore-toggle
    /// checkbox, then a link row when the entry carries one.
    ///
    /// The ignore toggle mirrors <c>DoIgnoreNewsProviderToggle</c>: un-ignoring calls
    /// <c>IgnoredNewsIds.SetIgnored</c> directly, the method's own direct branch, but ignoring first
    /// opens the SAME <c>HugsLib.Utils.Dialog_Confirm</c> the else-branch builds and calls SetIgnored
    /// only from its confirmed action. The Shift-held mouse bypass is deliberately not mirrored, so
    /// Enter always confirms. <c>Dialog_Confirm</c> extends <c>Dialog_MessageBox</c>, so
    /// <see cref="MessageBoxScope"/> attaches to it automatically.
    /// TRAP: <c>Dialog_Confirm.DoWindowContents</c> runs its OWN raw KeyDown check for Return and
    /// Escape, bypassing both the key handlers and <see cref="ShellFrameStamps"/>, so neither
    /// suppression mechanism this codebase relies on touches it. A same-frame double-fire remains
    /// unverified against the live game.
    ///
    /// The Filter row (Filtered subclass only) is a declared <see cref="ScreenAction"/> rather than a
    /// captured button, so its label can embed the live "currently showing" readout; activating it
    /// invokes the dialog's own private <c>ShowFilterOptionsMenu</c>, whose real <c>FloatMenu</c> the
    /// mod's <c>DialogInterceptionPatch</c> makes keyboard-accessible. Selecting an option runs the
    /// mod's own <c>SetFilterAndUpdateShownDefs</c>, which rebuilds <c>entries</c>, and the next
    /// <c>RefreshContent</c> reads the rebuilt list. The dev-mode per-entry menu is deliberately not
    /// served.
    ///
    /// Close is likewise declared, calling <c>window.Close()</c> as <c>DrawCloseButton</c> does, so it
    /// can sit beside Filter in visual order. <see cref="CaptureWindowButtons"/> is off entirely, to
    /// avoid double-presenting the real Filter and Close buttons.
    ///
    /// Escape and Enter need no overrides: neither dialog class overrides <c>OnCancelKeyPressed</c>
    /// and both set <c>closeOnCancel = true</c>, so the base behavior is already right.
    ///
    /// F5 COLLISION: <c>Dialog_UpdateFeaturesFiltered.ExtraOnGUI</c> eats KeyUp F5 as its own
    /// reload-news-defs hotkey via a raw <c>Event.current.Use()</c>. This scope claims no action bound
    /// to F5 and must never gain one.
    /// </summary>
    internal sealed class HugsLibNewsScope : ScreenScope
    {
        private enum RowKind
        {
            Title,
            Text,
            Image,
            IgnoreToggle,
            Link,
        }

        private sealed class NewsRow
        {
            public RowKind Kind;
            public object Def;
            public string Text;
        }

        private readonly Window window;
        private readonly bool isFiltered;
        private readonly List<NewsRow> rows = new List<NewsRow>();
        private int entryCount;
        private bool announcedOpen;

        public HugsLibNewsScope(Window window)
        {
            this.window = window;
            isFiltered = HugsLibNewsCompat.FilteredType != null && HugsLibNewsCompat.FilteredType.IsInstanceOfType(window);
        }

        public override string Name
        {
            get { return "hugslib-news"; }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData((entryCount == 1
                ? "RimWorldAccess.Compat.HugsLib.NewsOpenedOne".Translate(entryCount)
                : "RimWorldAccess.Compat.HugsLib.NewsOpenedMany".Translate(entryCount)).ToString());
            AnnounceCurrentItem();
        }

        // ------------------------------------------------------------------
        // ScreenScope content contract.
        // ------------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Compat.HugsLib.NewsRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return rows.Count;
        }

        /// <summary>Rebuilds the flattened row list from the dialog's OWN parsed <c>entries</c> every refresh -- never re-parses content itself.</summary>
        protected override void RefreshContent()
        {
            rows.Clear();
            entryCount = 0;
            if (window == null)
            {
                return;
            }
            IEnumerable entries = HugsLibNewsCompat.EntriesField.GetValue(window) as IEnumerable;
            if (entries == null)
            {
                return;
            }
            foreach (object entry in entries)
            {
                entryCount++;
                object def = HugsLibNewsCompat.FeDefField.GetValue(entry);
                AppendTitleRow(def);

                IEnumerable segments = HugsLibNewsCompat.FeSegmentsField.GetValue(entry) as IEnumerable;
                if (segments != null)
                {
                    foreach (object seg in segments)
                    {
                        AppendSegmentRow(seg);
                    }
                }

                rows.Add(new NewsRow { Kind = RowKind.IgnoreToggle, Def = def });

                string linkUrl = HugsLibNewsCompat.DefLinkUrlField.GetValue(def) as string;
                if (!string.IsNullOrEmpty(linkUrl))
                {
                    rows.Add(new NewsRow { Kind = RowKind.Link, Def = def, Text = linkUrl });
                }
            }
        }

        /// <summary>Mirrors DrawEntryTitle: titleOverride when set, else the same composed title, under a local key carrying the identical "{0} version {1}" shape.</summary>
        private void AppendTitleRow(object def)
        {
            string titleOverride = HugsLibNewsCompat.DefTitleOverrideField.GetValue(def) as string;
            string title;
            if (!string.IsNullOrEmpty(titleOverride))
            {
                title = titleOverride;
            }
            else
            {
                string modName = HugsLibNewsCompat.DefModNameReadableField.GetValue(def) as string ?? "";
                string version = HugsLibNewsCompat.DefAssemblyVersionField.GetValue(def) as string ?? "";
                title = "RimWorldAccess.Compat.HugsLib.NewsEntryTitle".Translate(modName, version).ToString();
            }
            // Composed from raw fields, so the drawn title's own size tag never appears here — but a
            // mod-supplied titleOverride could still carry rich-text tags.
            title = title.StripTags();
            rows.Add(new NewsRow { Kind = RowKind.Title, Def = def, Text = title });
        }

        /// <summary>One row per DescriptionSegment: Text verbatim (the parser already unescaped it), Image as a placeholder since imageNames are texture paths, Caption on its own row.</summary>
        private void AppendSegmentRow(object seg)
        {
            object rawType = HugsLibNewsCompat.SegTypeField.GetValue(seg);
            string kind = rawType != null ? rawType.ToString() : null;

            if (kind == "Image")
            {
                rows.Add(new NewsRow { Kind = RowKind.Image });
                return;
            }

            string text = HugsLibNewsCompat.SegTextField.GetValue(seg) as string;
            if (string.IsNullOrEmpty(text))
            {
                return; // Mirrors the source's own draw guard (type == Text/Caption && text != null).
            }
            text = NormalizeNewsText(text);
            if (kind == "Caption")
            {
                text = "RimWorldAccess.Compat.HugsLib.NewsCaption".Translate(text).ToString();
            }
            rows.Add(new NewsRow { Kind = RowKind.Text, Text = text });
        }

        /// <summary>Paragraph breaks read as periods, never newlines.</summary>
        private static string NormalizeNewsText(string text)
        {
            return text.Replace("\r", "").Replace("\n\n", ". ").Replace("\n", " ").Trim();
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            NewsRow row = rows[index];
            switch (row.Kind)
            {
                case RowKind.Title:
                case RowKind.Text:
                    d.Label = row.Text;
                    break;
                case RowKind.Image:
                    d.Label = "RimWorldAccess.Compat.HugsLib.NewsImage".Translate().ToString();
                    break;
                case RowKind.IgnoreToggle:
                    d.Label = "RimWorldAccess.Compat.HugsLib.NewsIgnoreToggle".Translate(ResolveModName(row.Def)).ToString();
                    d.Role = ElementRole.Checkbox;
                    d.Check = IsIgnored(row.Def) ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case RowKind.Link:
                    // RimWorld capitalizes every substituted argument, which mangles a URL, so the
                    // address goes into the raw key text rather than through an arg.
                    d.Label = "RimWorldAccess.Compat.HugsLib.NewsLink".Translate().RawText.Replace("{0}", row.Text);
                    d.Role = ElementRole.Button;
                    break;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            NewsRow row = rows[index];
            switch (row.Kind)
            {
                case RowKind.IgnoreToggle:
                    ToggleIgnore(row.Def);
                    break;
                case RowKind.Link:
                    OpenLink(row.Def);
                    break;
                default:
                    AnnounceCurrentItem(); // Read-only row: no vanilla action exists here either.
                    break;
            }
        }

        private static string ResolveModName(object def)
        {
            return HugsLibNewsCompat.DefModNameReadableField.GetValue(def) as string ?? "";
        }

        /// <summary>
        /// The exact id <c>DoIgnoreNewsProviderToggle</c> keys off: <c>UpdateFeatureDef.OwningModId</c>,
        /// which falls back to the owning mod's PackageId. Reading the raw modIdentifier field would
        /// disagree with the mod's own ignore bookkeeping for any def that leaves it blank, so the
        /// property is the source of truth and the field only a fallback for a DLL lacking it.
        /// </summary>
        private static string ResolveOwnerId(object def)
        {
            if (HugsLibNewsCompat.DefOwningModIdProperty != null)
            {
                return HugsLibNewsCompat.DefOwningModIdProperty.GetValue(def) as string;
            }
            return HugsLibNewsCompat.DefModIdentifierField.GetValue(def) as string;
        }

        private bool IsIgnored(object def)
        {
            string ownerId = ResolveOwnerId(def);
            if (string.IsNullOrEmpty(ownerId))
            {
                return false;
            }
            object provider = HugsLibNewsCompat.IgnoredNewsProvidersField.GetValue(window);
            if (provider == null)
            {
                return false;
            }
            return (bool)HugsLibNewsCompat.ContainsMethod.Invoke(provider, new object[] { ownerId });
        }

        private void ToggleIgnore(object def)
        {
            try
            {
                string ownerId = ResolveOwnerId(def);
                if (string.IsNullOrEmpty(ownerId))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.HugsLib.ActionFailed".Loc());
                    return;
                }
                object provider = HugsLibNewsCompat.IgnoredNewsProvidersField.GetValue(window);
                if (provider == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.HugsLib.ActionFailed".Loc());
                    return;
                }
                string modName = ResolveModName(def);
                bool currentlyIgnored = (bool)HugsLibNewsCompat.ContainsMethod.Invoke(provider, new object[] { ownerId });

                if (!currentlyIgnored)
                {
                    // The confirm branch: opens the SAME Dialog_Confirm the mod builds, and SetIgnored
                    // runs only from its confirmed action, never here.
                    OpenIgnoreConfirm(provider, ownerId, modName);
                }
                else
                {
                    // The direct branch: the mod's own gated setter.
                    HugsLibNewsCompat.SetIgnoredMethod.Invoke(provider, new object[] { ownerId, false });
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    RefreshModel();
                    AnnounceCurrentItem();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("HugsLibNewsScope ignore toggle failed: " + ex.Message);
                TolkHelper.Speak("RimWorldAccess.Compat.HugsLib.ActionFailed".Loc());
            }
        }

        private void OpenIgnoreConfirm(object provider, string ownerId, string modName)
        {
            string text = "RimWorldAccess.Compat.HugsLib.NewsIgnoreConfirmText".Translate(modName).ToString();
            string title = "RimWorldAccess.Compat.HugsLib.NewsIgnoreConfirmTitle".Translate().ToString();
            Action confirmed = delegate
            {
                // The mod's own gated setter, from the same confirmed-action path it uses.
                HugsLibNewsCompat.SetIgnoredMethod.Invoke(provider, new object[] { ownerId, true });
            };
            object dialog = HugsLibNewsCompat.DialogConfirmCtor.Invoke(new object[] { text, confirmed, false, title });
            Find.WindowStack.Add((Window)dialog);
        }

        private static void OpenLink(object def)
        {
            string url = HugsLibNewsCompat.DefLinkUrlField.GetValue(def) as string;
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            // The exact call DrawEntryLinkWidget's click makes.
            Application.OpenURL(url);
            TolkHelper.Speak("RimWorldAccess.Compat.HugsLib.NewsLinkOpened".Loc());
        }

        // ------------------------------------------------------------------
        // Buttons region: Filter (Filtered subclass only) then Close, both declared, not captured.
        // ------------------------------------------------------------------

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                var actions = new List<ScreenAction>();
                if (isFiltered)
                {
                    actions.Add(new ScreenAction(BuildFilterLabel(), OpenFilterMenu));
                }
                actions.Add(new ScreenAction("CloseButton".Translate().ToString(), delegate { window.Close(); }));
                return actions;
            }
        }

        private string BuildFilterLabel()
        {
            string modName = null;
            if (HugsLibNewsCompat.DefFilterField != null)
            {
                object provider = HugsLibNewsCompat.DefFilterField.GetValue(window);
                if (provider != null && HugsLibNewsCompat.CurrentFilterModNameReadableProperty != null)
                {
                    modName = HugsLibNewsCompat.CurrentFilterModNameReadableProperty.GetValue(provider) as string;
                }
            }
            if (string.IsNullOrEmpty(modName) && HugsLibNewsCompat.AllModsFilterLabelField != null)
            {
                modName = HugsLibNewsCompat.AllModsFilterLabelField.GetValue(window) as string;
            }
            return "RimWorldAccess.Compat.HugsLib.NewsFilter".Translate(modName ?? "").ToString();
        }

        private void OpenFilterMenu()
        {
            try
            {
                // The exact private handler DrawFilterButton's click invokes. It pushes a real
                // FloatMenu straight onto the stack without returning it, and such a menu nowhere
                // near the mouse self-closes before a keyboard user can reach it, so its options are
                // rehosted in the windowless menu.
                int windowCountBefore = Find.WindowStack.Windows.Count;
                HugsLibNewsCompat.ShowFilterOptionsMenuMethod.Invoke(window, null);
                if (Find.WindowStack.Windows.Count > windowCountBefore
                    && Find.WindowStack.Windows[Find.WindowStack.Windows.Count - 1] is FloatMenu spawnedMenu)
                {
                    List<FloatMenuOption> options = PawnColumnMutationHelper.ExtractFloatMenuOptions(spawnedMenu);
                    Find.WindowStack.TryRemove(spawnedMenu, doCloseSound: false);
                    if (options != null && options.Count > 0)
                    {
                        // FloatMenu's constructor already played FloatMenu_Open.
                        WindowlessFloatMenuState.Open(options, spawnedMenu.givesColonistOrders, playOpenSound: false);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("HugsLibNewsScope filter menu failed: " + ex.Message);
                TolkHelper.Speak("RimWorldAccess.Compat.HugsLib.ActionFailed".Loc());
            }
        }
    }
}
