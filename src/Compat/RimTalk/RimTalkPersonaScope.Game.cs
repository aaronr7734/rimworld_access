using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for RimTalk's persona editor, <c>RimTalk.UI.PersonaEditorWindow</c>: the
    /// per-pawn personality text, a chattiness slider, and four buttons (Save / Smart Gen / Roll
    /// Gen / Clear). Needs the full <see cref="WidgetCapture"/> engine rather than the lightweight
    /// captures <see cref="RimTalkChatScope"/> uses, because the persona field is a multi-line
    /// <c>Widgets.TextArea</c> and the chattiness control a real
    /// <c>Widgets.HorizontalSlider</c>; WidgetCapture's adjust and text-override channels drive
    /// both real widgets, so no reflected field mutation is needed.
    ///
    /// Left/Right on the slider stay this window's own chords rather than the chassis's
    /// horizontal pair (<see cref="ScreenScope.CanAdjustContentItem"/> stays false everywhere
    /// here) — the slider carve-out in SharedMenuGrammar.
    ///
    /// No length cap despite the mod's own <c>MaxLength = 500</c>: the TextArea call clamps
    /// nothing and the character-count label is a purely visual warning.
    ///
    /// No Harmony accept/cancel guard needed: the window overrides neither key handler and its
    /// constructor sets closeOnAccept=false / closeOnCancel=true, so the routing patched on
    /// <c>Window</c> already gates it. DoWindowContents never polls Event.current for
    /// Return/Escape either.
    ///
    /// Async Smart Gen exposes no signal but the window's private <c>_isGenerating</c> bool,
    /// polled once per GUI pass with a cue on the true-&gt;false edge. A faulted generation looks
    /// identical (RimTalk's continuation treats IsCompleted as success), which this scope
    /// observes faithfully rather than working around.
    /// </summary>
    public sealed class RimTalkPersonaScope : RimTalkTextDialogScopeBase
    {
        private enum ElementKind
        {
            PersonaField,
            ChattinessSlider,
            SaveButton,
            SmartGenButton,
            RollGenButton,
            ClearButton,
            // PersonaDirector's extra rows, present only while that mod is loaded and its
            // settings gate them on. Declared rather than resolved through WidgetCapture (see
            // ResolveIndices' known-risk remarks); their ring comes from ExtraButtonRect.
            EditNotesButton,
            EvolveButton,
            SetTimeButton,
        }

        private readonly Pawn pawn;
        private readonly List<ElementKind> activeKinds = new List<ElementKind>(9);
        private bool wasGenerating;
        private bool wasEvolveBusy;
        private Rect lastInRect;

        // WidgetCapture's text-override channel is one global slot, not per-window — tracked
        // locally so this scope clears it only when it was the one that set it.
        private bool textOverrideActive;

        // Resolved from the most recently completed draw pass; -1 means not found this pass,
        // which should only happen before the window has drawn once.
        private int textAreaIndex = -1;
        private int sliderIndex = -1;
        private readonly int[] buttonIndices = { -1, -1, -1, -1 }; // Save, SmartGen, RollGen, Clear

        public RimTalkPersonaScope(Window dialog)
            : base(dialog)
        {
            this.pawn = RimTalkPersonaDialogCompat.GetPawn(dialog);
            wasGenerating = RimTalkPersonaDialogCompat.IsGenerating(dialog);

            Func<bool> sliderFocused = delegate { return KindAt(CurrentIndex) == ElementKind.ChattinessSlider && NotForeign(); };

            Claim("rimTalkPersonaEditor.activateFocused", e => ActivateCurrent(), when: NotForeign);
            Claim("rimTalkPersonaEditor.decreaseChattiness", e => AdjustChattiness(-1), when: sliderFocused);
            Claim("rimTalkPersonaEditor.increaseChattiness", e => AdjustChattiness(1), when: sliderFocused);
        }

        public override string Name
        {
            get { return "rimtalk-persona-editor"; }
        }

        public override void OnPop()
        {
            // CancelIfActive fires no onExit, so the override posted through ApplyValue must be
            // cleared explicitly — and only when this scope is the one holding it.
            if (textOverrideActive)
            {
                WidgetCapture.ClearTextOverride();
                textOverrideActive = false;
            }
            base.OnPop();
        }

        // ---------------------------------------------------------------
        // Row model
        // ---------------------------------------------------------------

        protected override string ContentRegionName(int region)
        {
            return (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.RegionName".Translate();
        }

        /// <summary>
        /// Rebuilds the active-row list: the base six plus PersonaDirector's up-to-three extras.
        /// Recomputed every refresh, since PersonaDirector's settings can change live through its
        /// settings page while this window stays open.
        /// </summary>
        protected override void RefreshContent()
        {
            activeKinds.Clear();
            activeKinds.Add(ElementKind.PersonaField);
            activeKinds.Add(ElementKind.ChattinessSlider);
            activeKinds.Add(ElementKind.SaveButton);
            activeKinds.Add(ElementKind.SmartGenButton);
            activeKinds.Add(ElementKind.RollGenButton);
            activeKinds.Add(ElementKind.ClearButton);
            if (PersonaDirectorCompat.IncludeEditNotesButton())
            {
                activeKinds.Add(ElementKind.EditNotesButton);
            }
            if (PersonaDirectorCompat.IncludeEvolveButtons())
            {
                activeKinds.Add(ElementKind.EvolveButton);
                activeKinds.Add(ElementKind.SetTimeButton);
            }
        }

        protected override int ContentItemCount(int region)
        {
            return activeKinds.Count;
        }

        private ElementKind KindAt(int index)
        {
            return index >= 0 && index < activeKinds.Count ? activeKinds[index] : ElementKind.PersonaField;
        }

        protected override bool IsTextRow(int index)
        {
            return index >= 0 && KindAt(index) == ElementKind.PersonaField;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (KindAt(index))
            {
                case ElementKind.PersonaField:
                {
                    d.Label = ResolveFieldLabel();
                    d.Role = ElementRole.TextField;
                    string value = textAreaIndex >= 0 ? WidgetCapture.Items[textAreaIndex].Text : null;
                    if (string.IsNullOrEmpty(value))
                    {
                        d.ValueBlank = true;
                    }
                    else
                    {
                        d.Value = value;
                    }
                    // Mirrors RimTalk's own on-screen instruction.
                    d.Extras = (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.Instruct".Translate();
                    break;
                }
                case ElementKind.ChattinessSlider:
                {
                    d.Label = (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.Chattiness".Translate();
                    d.Role = ElementRole.Slider;
                    if (sliderIndex >= 0)
                    {
                        CapturedWidget slider = WidgetCapture.Items[sliderIndex];
                        d.Value = slider.SliderValue.ToString("0.00");
                        d.AtMinimum = slider.SliderValue <= slider.SliderMin + 0.0001f;
                        d.AtMaximum = slider.SliderValue >= slider.SliderMax - 0.0001f;
                    }
                    // Mirrors RimTalk's own info-icon tooltip.
                    d.Extras = (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.TalkFrequencyDesc".Translate();
                    break;
                }
                case ElementKind.EditNotesButton:
                    d.Label = PersonaDirectorCompat.EditNotesLabel();
                    d.Role = ElementRole.Button;
                    d.Extras = PersonaDirectorCompat.EditNotesTip();
                    break;
                case ElementKind.EvolveButton:
                    d.Label = PersonaDirectorCompat.EvolveLabel();
                    d.Role = ElementRole.Button;
                    if (!PersonaDirectorCompat.IsEvolveBusy())
                    {
                        d.Extras = PersonaDirectorCompat.EvolveTip();
                    }
                    break;
                case ElementKind.SetTimeButton:
                    d.Label = PersonaDirectorCompat.SetTimeLabel(pawn);
                    d.Role = ElementRole.Button;
                    d.Extras = PersonaDirectorCompat.SetTimeTip(pawn);
                    break;
                default:
                {
                    d.Label = ResolveButtonLabel(ButtonOrdinal(index));
                    d.Role = ElementRole.Button;
                    break;
                }
            }
            return d;
        }

        protected override void ActivateRow(int index)
        {
            switch (KindAt(index))
            {
                case ElementKind.PersonaField:
                    BeginEdit(announcePrompt: true);
                    return;
                case ElementKind.ChattinessSlider:
                    // A slider owns Left/Right and nothing else; Enter just re-reads it.
                    AnnounceCurrentItem();
                    return;
                case ElementKind.EditNotesButton:
                    PersonaDirectorCompat.OpenNotesEditorUnconditional();
                    return;
                case ElementKind.EvolveButton:
                    PersonaDirectorCompat.TryStartEvolve(pawn, dialog);
                    pendingAnnounce = true;
                    return;
                case ElementKind.SetTimeButton:
                    PersonaDirectorCompat.SetTime(pawn);
                    pendingAnnounce = true;
                    return;
                default:
                    ActivateWindowButton(ButtonOrdinal(index));
                    return;
            }
        }

        // ---------------------------------------------------------------
        // Per-GUI-pass work, driven by the dialog's own draw
        // ---------------------------------------------------------------

        internal void BeginDrawPass(Rect inRect)
        {
            lastInRect = inRect;
            // No ring index: this scope draws its own ring after the pass completes.
            WidgetCapture.BeginPass(-1);
        }

        internal void OnGuiPass()
        {
            ResolveIndices();
            RefreshModel();

            ShellTextFocus.ReleaseNativeFocus();
            session.MirrorLive();

            PollGenerationState();

            DrawFocusRing();

            FlushPendingAnnouncement();
        }

        /// <summary>
        /// Locates this pass's TextArea/Slider/Button rows by kind, since WidgetCapture mixes
        /// every widget kind into one stream. Recomputed every pass rather than hardcoding stream
        /// positions, so adding or removing a decorative label cannot desync the addressing.
        ///
        /// KNOWN RISK: with PersonaDirector installed, another Harmony postfix on this same
        /// <c>DoWindowContents</c> draws three more <c>Widgets.ButtonText</c> calls. Harmony
        /// guarantees no ordering between postfixes from different patches, so those three may
        /// land inside this scope's capture window; if they do, the first-four-buttons scan below
        /// can mis-sweep one into <see cref="buttonIndices"/> for that pass. Hence
        /// PersonaDirector's own buttons being declared rows rather than scanned.
        /// </summary>
        private void ResolveIndices()
        {
            textAreaIndex = -1;
            sliderIndex = -1;
            buttonIndices[0] = -1;
            buttonIndices[1] = -1;
            buttonIndices[2] = -1;
            buttonIndices[3] = -1;

            var items = WidgetCapture.Items;
            int buttonsSeen = 0;
            for (int i = 0; i < items.Count; i++)
            {
                CapturedWidget w = items[i];
                if (w.Kind == WidgetKind.TextField && w.MultiLine && textAreaIndex < 0)
                {
                    textAreaIndex = i;
                }
                else if (w.Kind == WidgetKind.Slider && sliderIndex < 0)
                {
                    sliderIndex = i;
                }
                else if (w.Kind == WidgetKind.Button && buttonsSeen < buttonIndices.Length)
                {
                    buttonIndices[buttonsSeen] = i;
                    buttonsSeen++;
                }
            }
        }

        /// <summary>Save=0, SmartGen=1, RollGen=2, Clear=3. Meaningless for any other row.</summary>
        private int ButtonOrdinal(int index)
        {
            return (int)KindAt(index) - (int)ElementKind.SaveButton;
        }

        /// <summary>
        /// PersonaDirector's extra rows have no WidgetCapture-backed rect, so their ring mirrors
        /// the fixed geometry Patch_PersonaEditorWindow_DirectorFeatures draws at, over
        /// <see cref="lastInRect"/>. Slots are Edit Notes, Evolve, Set Time regardless of which
        /// are shown: the mod computes all three rects before gating each draw.
        /// </summary>
        private static Rect ExtraButtonRect(Rect inRect, int slot)
        {
            const float width = 80f;
            const float height = 24f;
            const float gap = 5f;
            float x = inRect.x + slot * (width + gap);
            float y = inRect.y + 267f;
            return new Rect(x, y, width, height);
        }

        private void DrawFocusRing()
        {
            Rect rect;
            int index = CurrentIndex;
            if (index < 0)
            {
                return;
            }
            switch (KindAt(index))
            {
                case ElementKind.PersonaField:
                    if (textAreaIndex < 0)
                    {
                        return;
                    }
                    rect = WidgetCapture.Items[textAreaIndex].Rect;
                    break;
                case ElementKind.ChattinessSlider:
                    if (sliderIndex < 0)
                    {
                        return;
                    }
                    rect = WidgetCapture.Items[sliderIndex].Rect;
                    break;
                case ElementKind.EditNotesButton:
                    rect = ExtraButtonRect(lastInRect, 0);
                    break;
                case ElementKind.EvolveButton:
                    rect = ExtraButtonRect(lastInRect, 1);
                    break;
                case ElementKind.SetTimeButton:
                    rect = ExtraButtonRect(lastInRect, 2);
                    break;
                default:
                    int bi = ButtonOrdinal(index);
                    if (bi < 0 || bi >= buttonIndices.Length || buttonIndices[bi] < 0)
                    {
                        return;
                    }
                    rect = WidgetCapture.Items[buttonIndices[bi]].Rect;
                    break;
            }
            if (rect.width <= 0f)
            {
                return;
            }
            FocusRing.Draw(rect.ExpandedBy(FocusRingExpand));
        }

        // ---------------------------------------------------------------
        // Async Smart Gen / Evolve completion
        // ---------------------------------------------------------------

        private void PollGenerationState()
        {
            bool isGenerating = RimTalkPersonaDialogCompat.IsGenerating(dialog);
            if (wasGenerating && !isGenerating)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.RimTalk.PersonaEditor.GenerationComplete".Loc());
            }
            wasGenerating = isGenerating;

            bool evolveBusy = PersonaDirectorCompat.IsEvolveBusy();
            if (wasEvolveBusy && !evolveBusy)
            {
                TolkHelper.Speak("RimWorldAccess.Compat.RimTalk.PersonaEditor.EvolveComplete".Loc());
            }
            wasEvolveBusy = evolveBusy;
        }

        // ---------------------------------------------------------------
        // Activation
        // ---------------------------------------------------------------

        private void ActivateWindowButton(int ordinal)
        {
            if (ordinal < 0 || ordinal >= buttonIndices.Length || buttonIndices[ordinal] < 0)
            {
                return;
            }
            CapturedWidget button = WidgetCapture.Items[buttonIndices[ordinal]];
            // Ordinal 0: each of the four buttons carries a distinct label, so each is the only
            // occurrence of its own label in the stream.
            WidgetCapture.RequestActivate(WidgetKind.Button, button.Label ?? "", 0);
        }

        /// <summary>
        /// Steps the real Widgets.HorizontalSlider's return value through WidgetCapture's adjust
        /// channel, which the window assigns straight into its own field — no reflected mutation.
        /// </summary>
        private void AdjustChattiness(int direction)
        {
            if (sliderIndex < 0)
            {
                return;
            }
            // Blank label, ordinal 0: the slider draws no label of its own and is the only slider
            // in this window.
            WidgetCapture.RequestAdjust("", 0, direction);
            pendingAnnounce = true;
        }

        // ---------------------------------------------------------------
        // Enter / type to edit (browse -> edit), via the shared session
        // ---------------------------------------------------------------

        protected override void BeginEdit(bool announcePrompt)
        {
            string current = textAreaIndex >= 0 ? (WidgetCapture.Items[textAreaIndex].Text ?? "") : "";

            // No length cap: the mod's own TextArea call imposes none (see class remarks).
            TextFieldSpec spec = TextFieldSpec.MultiLineUnrestricted("RimWorldAccess.TextInput.LabelDefault");

            session.EnterEdit(
                current,
                spec,
                ResolveFieldLabel(),
                ApplyValue,
                ReAnnounceRow,
                announcePrompt);
            // No onConfirm: the Save button, not the field, is the mod's real commit.
        }

        /// <summary>
        /// Overrides the real Widgets.TextArea call's return value from this pass until cleared,
        /// so the window's own return-assign picks up what was typed — no reflected mutation.
        /// </summary>
        private void ApplyValue(string value)
        {
            if (textAreaIndex >= 0)
            {
                WidgetCapture.RequestTextOverride(textAreaIndex, value ?? "");
                textOverrideActive = true;
            }
        }

        protected override void ReAnnounceRow()
        {
            if (textOverrideActive)
            {
                WidgetCapture.ClearTextOverride();
                textOverrideActive = false;
            }
            base.ReAnnounceRow();
        }

        // ---------------------------------------------------------------
        // Announcements
        // ---------------------------------------------------------------

        /// <summary>
        /// Mirrors RimTalk's own window title. Baked into our own key because check_l10n_keys.py
        /// only scans our Keyed XML, never a Workshop mod's.
        /// </summary>
        protected override string ComposeOpenedAnnouncement()
        {
            string pawnName = pawn != null ? pawn.LabelShortCap : "";
            return (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.Title".Translate(pawnName);
        }

        private static string ResolveFieldLabel()
        {
            return (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.PersonaField".Translate();
        }

        /// <summary>Falls back to our own mirrored wording on a pass where the row hasn't drawn.</summary>
        private string ResolveButtonLabel(int bi)
        {
            if (bi >= 0 && bi < buttonIndices.Length && buttonIndices[bi] >= 0)
            {
                string label = WidgetCapture.Items[buttonIndices[bi]].Label;
                if (!string.IsNullOrEmpty(label))
                {
                    return label;
                }
            }
            switch (bi)
            {
                case 0: return (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.SaveFallback".Translate();
                case 1: return (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.SmartGenFallback".Translate();
                case 2: return (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.RollGenFallback".Translate();
                case 3: return (string)"RimWorldAccess.Compat.RimTalk.PersonaEditor.ClearFallback".Translate();
                default: return "";
            }
        }
    }
}
