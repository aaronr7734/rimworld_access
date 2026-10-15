using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Drives the real <see cref="Dialog_MessageBox"/> window, which
    /// DialogInterceptionPatch does not swallow: the window opens visibly and this scope drives
    /// its real buttons. It attaches through the ScopeForWindow mirror when the box enters the
    /// WindowStack and pops when it leaves by any path, so a mouse click stays in sync for free.
    ///
    /// One content region holds the box's body — the title and message as one read-only row per
    /// line, plus (for <see cref="Dialog_ConfirmModUpload"/>) its upload option checkboxes.
    /// The automatic Buttons region captures the box's
    /// own <c>Widgets.ButtonText</c> calls, so the interaction-delay countdown is spoken as
    /// rendered and Enter injects the click, running vanilla's inline handler and its
    /// <c>InteractionDelayExpired</c> gate unmodified. The body flows into the Buttons region
    /// (<see cref="ContentFlowsToActions"/>), so the box reads as one continuous surface.
    ///
    /// Escape follows vanilla's own OnCancelKeyPressed semantics, and for boxes vanilla keeps open
    /// until a button is chosen it re-reads the question rather than staying silent — so this
    /// scope owns cancel outright. Enter keeps the chassis default <c>OwnsAccept == true</c>,
    /// load-bearing here: vanilla re-tests the Accept binding in each window's deferred GUI pass,
    /// where the dispatcher's Event.Use() is invisible, and this box's
    /// closeOnAccept/forceCatchAcceptAndCancelEventEvenIfUnfocused would let that re-test resolve
    /// the dialog behind the scope's back.
    ///
    /// Subclasses attach this SAME scope through ScopeForWindow.RegisterHierarchy; the
    /// exact-type registration is unchanged and the hierarchy entry only catches what it missed.
    /// A subclass drawing extra widgets gets the base behavior for free.
    /// </summary>
    public sealed class MessageBoxScope : ScreenScope
    {
        private const float FocusRingExpand = 3f;

        /// <summary>
        /// Dialog_ConfirmModUpload's only subclass-specific field, private even to its own
        /// hierarchy: reflected once, read per pass so a live toggle shows on the next
        /// announcement.
        /// </summary>
        private static readonly AccessTools.FieldRef<Dialog_ConfirmModUpload, ModMetaData> confirmModUploadModField =
            AccessTools.FieldRefAccess<Dialog_ConfirmModUpload, ModMetaData>("mod");

        /// <summary>A Dialog_ConfirmModUpload checkbox: the ModMetaData bool it binds, drawn RowOffset rows above the translation one.</summary>
        private sealed class UploadOption
        {
            public string LabelKey;
            public string TipKey;
            public int RowOffset;
            public AccessTools.FieldRef<ModMetaData, bool> Value;
            public Func<ModMetaData, bool> Disabled;
        }

        private static readonly List<UploadOption> uploadOptions = new List<UploadOption>();

        // On-screen order, top first. The asset-pack pair exists only from 1.7, so absent fields drop out.
        static MessageBoxScope()
        {
            UploadOption buildAssetPackOption = AddUploadOption("uploadBuildAssetPack", "UploadBuildAssetPack", "UploadBuildAssetPackTip", 2, null);
            AddUploadOption("uploadSkipSourceTextures", "UploadSkipSourceTextures", "UploadSkipSourceTexturesTip", 1,
                mod => buildAssetPackOption == null || !buildAssetPackOption.Value(mod));
            AddUploadOption("translationMod", "TagAsTranslation", null, 0, null);
        }

        private static UploadOption AddUploadOption(string field, string labelKey, string tipKey, int rowOffset,
            Func<ModMetaData, bool> disabled)
        {
            if (AccessTools.Field(typeof(ModMetaData), field) == null)
            {
                return null;
            }
            UploadOption option = new UploadOption
            {
                LabelKey = labelKey,
                TipKey = tipKey,
                RowOffset = rowOffset,
                Value = AccessTools.FieldRefAccess<ModMetaData, bool>(field),
                Disabled = disabled,
            };
            uploadOptions.Add(option);
            return option;
        }

        private readonly Dialog_MessageBox box;
        private string bodyTextCache;
        private readonly List<string> bodyLines =
            new List<string>();

        public MessageBoxScope(Dialog_MessageBox box)
        {
            this.box = box;
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        /// <summary>The open announcement already reads the full message; the entry item would only repeat its first line.</summary>
        public override void OnFocus()
        {
            SuppressNextEntryAnnouncement();
            base.OnFocus();
        }

        public override string Name
        {
            get { return "message-box"; }
        }

        /// <summary>
        /// Escape is always this scope's: vanilla's OnCancelKeyPressed does nothing for a box
        /// whose buttons carry actions, and this scope re-reads the question instead of going
        /// silent. Unconditionally true folds in the base's typeahead case, whose Escape claim is
        /// registered ahead of this one.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, box);
        }

        // ---------------------------------------------------------------
        // Row model.
        // ---------------------------------------------------------------

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Unnamed: the body rows ARE the dialog, so a "Description" frame word is noise.</summary>
        protected override string ContentRegionName(int region)
        {
            return "";
        }

        /// <summary>Title, then the message one row per non-empty line; re-split only when the text changes.</summary>
        private List<string> BodyLines()
        {
            string key = Title + "\n" + Message;
            if (!string.Equals(key, bodyTextCache, StringComparison.Ordinal))
            {
                bodyTextCache = key;
                bodyLines.Clear();
                if (Title.Length > 0)
                {
                    bodyLines.Add(Title);
                }
                string[] raw = Message.Split('\n');
                for (int i = 0; i < raw.Length; i++)
                {
                    string line = raw[i].Trim();
                    if (line.Length > 0)
                    {
                        bodyLines.Add(line);
                    }
                }
            }
            return bodyLines;
        }

        protected override int ContentItemCount(int region)
        {
            return BodyLines().Count + (HasUploadOptions ? uploadOptions.Count : 0);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            UploadOption option = UploadOptionAt(index);
            if (option != null)
            {
                ModMetaData mod = UploadMod();
                return new ElementDescription
                {
                    Label = (string)option.LabelKey.Translate(),
                    Role = ElementRole.Checkbox,
                    Check = mod != null && option.Value(mod) ? CheckState.Checked : CheckState.Unchecked,
                    Disabled = mod != null && option.Disabled != null && option.Disabled(mod),
                    Extras = option.TipKey == null ? null : (string)option.TipKey.Translate(),
                };
            }
            List<string> lines = BodyLines();
            return new ElementDescription
            {
                Label = index >= 0 && index < lines.Count ? lines[index] : "",
                ReadOnly = true,
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            UploadOption option = UploadOptionAt(index);
            if (option != null && ToggleUploadOption(option))
            {
                return;
            }
            AnnounceCurrentItem();
        }

        /// <summary>Prose, not item names: the body lines would bury the buttons under whatever the message happens to mention.</summary>
        protected override bool ContentRowSearchable(int region, int row)
        {
            return UploadOptionAt(row) != null;
        }

        /// <summary>Down off the body continues into the buttons and Up returns — one message-reader flow.</summary>
        protected override bool ContentFlowsToActions(int region)
        {
            return true;
        }

        private bool HasUploadOptions
        {
            get { return box is Dialog_ConfirmModUpload; }
        }

        private UploadOption UploadOptionAt(int index)
        {
            int optionIndex = index - BodyLines().Count;
            return HasUploadOptions && optionIndex >= 0 && optionIndex < uploadOptions.Count ? uploadOptions[optionIndex] : null;
        }

        private string Title
        {
            get { return StripOrEmpty(box.title); }
        }

        private string Message
        {
            get { return StripOrEmpty(box.text.ToString()); }
        }

        // ---------------------------------------------------------------
        // Escape.
        // ---------------------------------------------------------------

        private void OnCancel(KeyEventSnapshot e)
        {
            // Stamp before acting: vanilla re-tests the Cancel binding in the deferred window
            // pass, where this frame's Event.Use() is invisible.
            ShellFrameStamps.MarkCancelConsumed();
            // Vanilla's own Escape semantics: a cancelAction wins; otherwise closeOnCancel —
            // true only when no button carries an action — closes outright.
            if (box.cancelAction != null)
            {
                TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
                box.cancelAction();
                box.Close();
                return;
            }
            if (box.closeOnCancel)
            {
                TolkHelper.Speak("RimWorldAccess.UI.Cancelled".Loc());
                box.Close();
                return;
            }
            // No cancel path: vanilla deliberately keeps this box open until a button is chosen.
            // Re-read the question so Escape is never silent.
            TolkHelper.SpeakData(BuildDescriptionAnnouncement(), SpeechPriority.High);
        }

        // ---------------------------------------------------------------
        // Dialog_ConfirmModUpload's checkboxes.
        // ---------------------------------------------------------------

        private ModMetaData UploadMod()
        {
            Dialog_ConfirmModUpload confirmModUpload = box as Dialog_ConfirmModUpload;
            return confirmModUpload == null ? null : confirmModUploadModField(confirmModUpload);
        }

        /// <summary>False when the option is greyed out, leaving the caller to re-read the row.</summary>
        private bool ToggleUploadOption(UploadOption option)
        {
            ModMetaData mod = UploadMod();
            if (mod == null || (option.Disabled != null && option.Disabled(mod)))
            {
                return false;
            }
            // The same ref-bool flip vanilla's bound Widgets.Checkbox(pos, ref field) performs; the
            // widget has no handler beyond the toggle, so this IS the vanilla path.
            option.Value(mod) = !option.Value(mod);
            ElementDescription d = new ElementDescription();
            d.Check = option.Value(mod) ? CheckState.Checked : CheckState.Unchecked;
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
            return true;
        }

        /// <summary>Drawn from <see cref="ConfirmModUploadCheckboxRingPatch"/>: the subclass paints its checkboxes after base DoWindowContents, the first point a ring survives.</summary>
        internal void DrawCheckboxRingIfFocused(Rect inRect)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != 0 || region == null || region.IsEmpty)
            {
                return;
            }
            UploadOption option = UploadOptionAt(region.Index);
            if (option == null)
            {
                return;
            }
            Rect rect = ComputeTranslationCheckboxRect(inRect);
            rect.y -= option.RowOffset * UploadOptionRowStep;
            FocusRing.Draw(rect.ExpandedBy(FocusRingExpand));
        }

        private const float UploadOptionRowStep = 34f;

        /// <summary>
        /// Dialog_ConfirmModUpload positions its checkboxes by direct arithmetic, not through any
        /// widget this mod captures, so the placement is replicated exactly rather than guessed.
        /// </summary>
        private static Rect ComputeTranslationCheckboxRect(Rect inRect)
        {
            const float size = 24f;
            float x = inRect.x + 10f;
            float y = inRect.height - 35f - size - 10f;
            return new Rect(x, y, size, size);
        }

        // ---------------------------------------------------------------
        // Announcements (legacy RimWorldAccess.UI.Dialog.* wording).
        // ---------------------------------------------------------------

        /// <summary>
        /// The whole message rides the open announcement; the entry announcement is suppressed
        /// (see <see cref="OnFocus"/>) so nothing repeats.
        /// </summary>
        protected override string ComposeOpenAnnouncement()
        {
            return DialogFrame.Opened(Title, Message, ButtonCount);
        }

        /// <summary>
        /// Read from the box's own button fields rather than the capture: the open announcement
        /// is composed on the first focus, a pass before vanilla has drawn anything.
        /// </summary>
        private int ButtonCount
        {
            get { return 1 + (box.buttonBText != null ? 1 : 0) + (box.buttonCText != null ? 1 : 0); }
        }

        private string BuildDescriptionAnnouncement()
        {
            return DialogFrame.Description(Title, Message);
        }

        private static string StripOrEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.StripTags();
        }
    }

    /// <summary>
    /// Dialog_ConfirmModUpload draws its checkbox in its own DoWindowContents override, after
    /// base.DoWindowContents. This postfix is the point in the frame where the checkbox has been
    /// painted, so it is the only correct place to ring it, and it carries the rect the checkbox
    /// was positioned against. Everything else on the box rides the shared
    /// <see cref="ScreenScopeDrawPatch"/> bracket.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ConfirmModUpload), "DoWindowContents")]
    public static class ConfirmModUploadCheckboxRingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dialog_ConfirmModUpload __instance, Rect inRect)
        {
            try
            {
                MessageBoxScope scope = FocusStack.Top as MessageBoxScope;
                if (scope != null && scope.Owns(__instance))
                {
                    scope.DrawCheckboxRingIfFocused(inRect);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Confirm mod upload checkbox ring error", ex);
            }
        }
    }
}
