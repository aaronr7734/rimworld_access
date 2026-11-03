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
    /// Keyboard support for the RimTalk TTS addon's per-pawn voice picker,
    /// <c>RimTalk.TTS.UI.VoiceSelectionWindow</c>, opened from the Bio-tab chip
    /// <see cref="RimTalkTtsCompat"/> registers. Wraps the window with the full
    /// <see cref="WidgetCapture"/> engine, but over a VARIABLE-length option list rather than a
    /// fixed set of rows: NONE / DEFAULT / RULE_BASED plus one row per entry in the mod's own
    /// <c>_voiceModels</c>, rebuilt every refresh from that live data, then the custom language
    /// field, then Save and Cancel.
    ///
    /// EACH OPTION ROW is drawn by <c>DrawVoiceOption</c> as FOUR overlapping primitives sharing
    /// one rect: a bare <c>Widgets.Checkbox</c> icon with no caption parameter, two plain Labels
    /// (name, then description), and a whole-row <c>Widgets.ButtonInvisible</c> over all of it.
    /// That is exactly the geometry GenericWindowScope's fusion heuristic mishandles -- a backward
    /// label search from the InvisibleButton fuses with whichever Label it reaches first, with no
    /// guarantee it is the option's name rather than its description -- which is why this window
    /// is bespoke. Rather than fight the fusion, this scope never asks WidgetCapture to identify
    /// an option row's CAPTION at all: the option list is built from <c>_voiceModels</c> plus the
    /// three synthetic entries, and each label/description comes from RimTalk's OWN resolved
    /// Translator strings. WidgetCapture still runs every pass purely for RECTS -- the Nth
    /// captured InvisibleButton in draw order is the Nth option's whole-row rect, used only to
    /// place the focus ring where a sighted player's hover highlight sits, never for identity or
    /// activation.
    ///
    /// ACTIVATING an option is <c>MUTATION-C</c>: <c>_selectedVoiceId</c> is a bare private field
    /// with no gated setter, assigned directly by <c>DrawVoiceOption</c>'s own click handler
    /// (<c>if (flag &amp;&amp; !flag2) { _selectedVoiceId = voiceId; }</c>). The Save/Cancel
    /// buttons and the custom language field carry no such ambiguity and ride real vehicles:
    /// Save/Cancel through <see cref="WidgetCapture.RequestActivate"/> (their resolved labels are
    /// unique in this window), and the language field through
    /// <see cref="WidgetCapture.RequestTextOverride"/>, which forces the real
    /// <c>Widgets.TextField</c> return value so the window's own
    /// <c>_customLanguage = Widgets.TextField(...)</c> assignment picks up what was typed with no
    /// reflected write.
    ///
    /// NO Harmony accept/cancel guard needed: VoiceSelectionWindow overrides NEITHER
    /// <c>OnAcceptKeyPressed</c> nor <c>OnCancelKeyPressed</c>. Its constructor sets
    /// <c>closeOnAccept = false</c> (making the inherited Accept an inert no-op) and leaves
    /// <c>closeOnCancel</c> at the Window default true (so the inherited Escape closes the window
    /// exactly as a sighted click on Cancel would), and DoWindowContents never reads Event.current
    /// for either key, so there is no raw poll to mask. Escape therefore keeps the chassis
    /// default: owned only while a typeahead search is live (which it clears), vanilla's own close
    /// every other time.
    /// </summary>
    public sealed class RimTalkTtsVoiceSelectionScope : RimTalkTextDialogScopeBase
    {
        private struct Option
        {
            public string Id;
            public string Label;
            public string Description;
        }

        private readonly Pawn pawn;
        private readonly List<Option> options = new List<Option>();

        // Resolved from the most recently completed draw pass.
        private readonly List<int> invisibleButtonIndices = new List<int>(); // one per option, draw order
        private int textFieldIndex = -1;
        private readonly int[] buttonIndices = { -1, -1 }; // Save, Cancel

        public RimTalkTtsVoiceSelectionScope(Window dialog)
            : base(dialog)
        {
            this.pawn = RimTalkTtsVoiceSelectionCompat.GetPawn(dialog);

            // Space stays a same-shape alternate activation onto the shared activation path.
            Claim("rimTalkTtsVoiceSelection.activateFocused", e => ActivateCurrent(), when: NotForeign);
        }

        public override string Name
        {
            get { return "rimtalk-tts-voice-selection"; }
        }

        // ---------------------------------------------------------------
        // Row model: the voice options, the custom language field, Save, Cancel.
        // ---------------------------------------------------------------

        protected override string ContentRegionName(int region)
        {
            return (string)"RimWorldAccess.Compat.RimTalk.TTS.VoiceSelection.RegionName".Translate();
        }

        /// <summary>Rebuilds the option list from the window's live <c>_voiceModels</c> plus the three synthetic entries, in DrawVoiceOption's own call order.</summary>
        protected override void RefreshContent()
        {
            options.Clear();
            options.Add(new Option
            {
                Id = "NONE",
                Label = Translator.Translate("RimTalk.TTS.VoiceNone").Resolve(),
                Description = Translator.Translate("RimTalk.TTS.VoiceNoneDesc").Resolve(),
            });
            options.Add(new Option
            {
                Id = "DEFAULT",
                Label = Translator.Translate("RimTalk.TTS.VoiceDefault").Resolve(),
                Description = Translator.Translate("RimTalk.TTS.VoiceDefaultDesc").Resolve(),
            });
            options.Add(new Option
            {
                Id = "RULE_BASED",
                Label = Translator.Translate("RimTalk.TTS.VoiceRuleBased").Resolve(),
                Description = Translator.Translate("RimTalk.TTS.VoiceRuleBasedDesc").Resolve(),
            });

            IList voiceModels = RimTalkTtsVoiceSelectionCompat.GetVoiceModels(dialog);
            if (voiceModels != null)
            {
                foreach (object voiceModel in voiceModels)
                {
                    if (voiceModel == null)
                    {
                        continue;
                    }
                    string modelId = RimTalkTtsVoiceSelectionCompat.VoiceModelId(voiceModel);
                    if (string.IsNullOrEmpty(modelId))
                    {
                        continue;
                    }
                    string modelName = RimTalkTtsVoiceSelectionCompat.VoiceModelName(voiceModel);
                    options.Add(new Option
                    {
                        Id = modelId,
                        Label = string.IsNullOrEmpty(modelName) ? modelId : modelName,
                        Description = "RimWorldAccess.Compat.RimTalk.TTS.ModelIdPrefix".Translate(modelId),
                    });
                }
            }
        }

        protected override int ContentItemCount(int region)
        {
            return options.Count + 3; // options + language field + Save + Cancel
        }

        protected override bool IsTextRow(int index)
        {
            return index == options.Count;
        }

        /// <summary>Save=0, Cancel=1; negative on any other row.</summary>
        private int ButtonOrdinal(int index)
        {
            return index - options.Count - 1;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < options.Count)
            {
                Option option = options[index];
                string currentId = RimTalkTtsVoiceSelectionCompat.GetSelectedVoiceId(dialog);
                d.Label = option.Label;
                d.Role = ElementRole.RadioButton;
                d.Selected = currentId == option.Id;
                d.Extras = option.Description;
            }
            else if (IsTextRow(index))
            {
                d.Label = ResolveFieldLabel();
                d.Role = ElementRole.TextField;
                string value = textFieldIndex >= 0 ? WidgetCapture.Items[textFieldIndex].Text : null;
                if (string.IsNullOrEmpty(value))
                {
                    d.ValueBlank = true;
                }
                else
                {
                    d.Value = value;
                }
            }
            else
            {
                d.Label = ResolveButtonLabel(ButtonOrdinal(index));
                d.Role = ElementRole.Button;
            }
            return d;
        }

        protected override void ActivateRow(int index)
        {
            if (index >= 0 && index < options.Count)
            {
                // MUTATION-C: mirrors DrawVoiceOption's own click-handler assignment
                // (`_selectedVoiceId = voiceId;`) -- a bare private field with no gated setter,
                // see class remarks.
                RimTalkTtsVoiceSelectionCompat.SetSelectedVoiceId(dialog, options[index].Id);
                AnnounceCurrentItem();
                return;
            }
            if (IsTextRow(index))
            {
                BeginEdit(announcePrompt: true);
                return;
            }
            ActivateWindowButton(ButtonOrdinal(index));
        }

        // ---------------------------------------------------------------
        // Per-GUI-pass work, driven by the dialog's own draw.
        // ---------------------------------------------------------------

        internal void BeginDrawPass()
        {
            WidgetCapture.BeginPass(-1);
        }

        internal void OnGuiPass()
        {
            ResolveIndices();
            RefreshModel();

            ShellTextFocus.ReleaseNativeFocus();
            session.MirrorLive();

            DrawFocusRing();

            FlushPendingAnnouncement();
        }

        /// <summary>Locates this pass's InvisibleButton/TextField/Button rows by kind, recomputed every pass so a future addon update never desyncs this scope's addressing.</summary>
        private void ResolveIndices()
        {
            invisibleButtonIndices.Clear();
            textFieldIndex = -1;
            buttonIndices[0] = -1;
            buttonIndices[1] = -1;

            int buttonsSeen = 0;
            IReadOnlyList<CapturedWidget> items = WidgetCapture.Items;
            for (int i = 0; i < items.Count; i++)
            {
                CapturedWidget w = items[i];
                if (w.Kind == WidgetKind.InvisibleButton)
                {
                    invisibleButtonIndices.Add(i);
                }
                else if (w.Kind == WidgetKind.TextField && textFieldIndex < 0)
                {
                    textFieldIndex = i;
                }
                else if (w.Kind == WidgetKind.Button && buttonsSeen < buttonIndices.Length)
                {
                    buttonIndices[buttonsSeen] = i;
                    buttonsSeen++;
                }
            }
        }

        private void DrawFocusRing()
        {
            Rect rect;
            int index = CurrentIndex;
            if (index < 0)
            {
                return;
            }
            if (index < options.Count)
            {
                if (index >= invisibleButtonIndices.Count)
                {
                    return;
                }
                rect = WidgetCapture.Items[invisibleButtonIndices[index]].Rect;
            }
            else if (IsTextRow(index))
            {
                if (textFieldIndex < 0)
                {
                    return;
                }
                rect = WidgetCapture.Items[textFieldIndex].Rect;
            }
            else
            {
                int bi = ButtonOrdinal(index);
                if (bi < 0 || bi >= buttonIndices.Length || buttonIndices[bi] < 0)
                {
                    return;
                }
                rect = WidgetCapture.Items[buttonIndices[bi]].Rect;
            }
            if (rect.width <= 0f)
            {
                return;
            }
            FocusRing.Draw(rect.ExpandedBy(FocusRingExpand));
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
            // Ordinal 0: Save and Cancel each carry a distinct resolved label,
            // so each is always the first occurrence of its own label in the capture stream.
            WidgetCapture.RequestActivate(WidgetKind.Button, button.Label ?? "", 0);
        }

        // ---------------------------------------------------------------
        // Enter / type to edit (browse -> edit), via the shared session
        // ---------------------------------------------------------------

        protected override void BeginEdit(bool announcePrompt)
        {
            string current = textFieldIndex >= 0 ? (WidgetCapture.Items[textFieldIndex].Text ?? "") : "";

            // No length cap: the mod's own Widgets.TextField call imposes none.
            var spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: null, minLength: 0);

            session.EnterEdit(
                current,
                spec,
                ResolveFieldLabel(),
                ApplyValue,
                ReAnnounceRow,
                announcePrompt);
        }

        /// <summary>Vehicle A/B: overrides the REAL Widgets.TextField call's return value, so the window's own return-assign (`_customLanguage = Widgets.TextField(...)`) picks up exactly what was typed -- no reflected field write needed.</summary>
        private void ApplyValue(string value)
        {
            if (textFieldIndex >= 0)
            {
                WidgetCapture.RequestTextOverride(textFieldIndex, value ?? "");
            }
        }

        // ---------------------------------------------------------------
        // Announcements
        // ---------------------------------------------------------------

        protected override string ComposeOpenedAnnouncement()
        {
            string pawnName = pawn != null ? pawn.LabelShortCap : "";
            return (string)"RimWorldAccess.Compat.RimTalk.TTS.VoiceSelection.Opened".Translate(pawnName);
        }

        private static string ResolveFieldLabel()
        {
            return (string)"RimWorldAccess.Compat.RimTalk.TTS.VoiceSelection.CustomLanguageField".Translate();
        }

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
            return bi == 0
                ? (string)"RimWorldAccess.Compat.RimTalk.TTS.VoiceSelection.SaveFallback".Translate()
                : (string)"RimWorldAccess.Compat.RimTalk.TTS.VoiceSelection.CancelFallback".Translate();
        }
    }
}
