using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Descriptor for a single labeled widget drawn during one frame of
    /// Dialog_ModSettings.DoWindowContents. IMGUI widgets have no persistent identity
    /// across frames, so ModSettingsCaptureState correlates an activation back to a
    /// specific widget using (DrawIndex, Label) - this widget's ordinal position within
    /// the frame's capture list, plus its label as a sanity check.
    /// </summary>
    public class CapturedWidget
    {
        public enum Kind
        {
            Checkbox,
            Button,
            RadioButton,
            Slider,
            Label,
            TextField,
            IntEntry
        }

        public Kind WidgetKind;
        public string Label;
        public bool BoolValue;
        public float FloatValue;
        public float Min;
        public float Max;
        public int DrawIndex;

        // TextField only: the current text the field is showing.
        public string StringValue;

        // IntEntry only: current value, the step each Left/Right press applies, and the floor.
        public int IntValue;
        public int Multiplier;
        public int MinInt;
    }

    /// <summary>
    /// Gates the mod-settings widget patches (ModSettingsWidgetPatches.cs) and holds the
    /// per-frame shadow list of controls they capture, plus a single pending activation
    /// the patches apply back into the mod's own widgets on a later frame.
    ///
    /// <see cref="Capturing"/> is only ever true for the duration of
    /// Dialog_ModSettings.DoWindowContents (see ModSettingsDialogPatch's Prefix/Finalizer),
    /// so every other IMGUI call in the game - including every other mod dialog - pays
    /// zero overhead from these patches beyond the single boolean check.
    /// </summary>
    public static class ModSettingsCaptureState
    {
        // Number of frames a pending action survives before being dropped if the targeted
        // widget never reappears (e.g. the mod's own UI is mid-transition that frame).
        private const int PendingActionTimeoutFrames = 3;

        public static bool Capturing;

        private static List<CapturedWidget> currentFrameWidgets = new List<CapturedWidget>();
        private static int nextDrawIndex;

        // Reentrancy guard: Listing_Standard's widgets are thin wrappers around the raw
        // Widgets.* equivalents, which themselves often call Widgets.Label internally
        // (e.g. Listing_Standard.CheckboxLabeled -> Widgets.CheckboxLabeled -> Widgets.Label).
        // Since every one of those is independently patched, a single logical control would
        // otherwise be recorded 2-3 times. captureDepth tracks how many patched widget calls
        // are currently nested on the stack; only the OUTERMOST one (depth 0 -> 1) records.
        private static int captureDepth;

        private class PendingAction
        {
            public int DrawIndex;
            public string Label;
            public CapturedWidget.Kind WidgetKind;
            public bool? TargetBool;
            public float? TargetFloat;
            public string TargetString;
            public int FramesRemaining;
        }

        private static PendingAction pendingAction;

        /// <summary>The widgets recorded so far this frame. Safe to read mid-frame (e.g. right
        /// after Dialog_ModSettings.DoWindowContents' original body has run, before the
        /// Finalizer hands the list off to ModSettingsMenuState via EndFrame).</summary>
        public static List<CapturedWidget> CurrentFrameSnapshot => currentFrameWidgets;

        // The Dialog_ModSettings instance whose DoWindowContents is currently being captured.
        // Some heavily-modded setups (e.g. the "Mod Settings Framework" mod) put more than one
        // Dialog_ModSettings on screen - a real per-mod one that draws capturable widgets and a
        // wrapper one that draws nothing we recognise. We tag each captured frame with its
        // dialog so the menu only ever refreshes from the exact dialog it bound to, and never
        // gets clobbered by the other one's empty frame. Stored as object to avoid coupling
        // this file to RimWorld types; the menu compares it by reference.
        private static object currentFrameDialog;

        /// <summary>The dialog instance the current/most-recent captured frame belongs to.</summary>
        public static object CurrentFrameDialog => currentFrameDialog;

        /// <summary>Called by ModSettingsDialogPatch's capture-gating Prefix, before the
        /// original DoWindowContents (and therefore before any of the mod's own widgets)
        /// runs.</summary>
        public static void BeginFrame(object dialog)
        {
            Capturing = true;
            currentFrameDialog = dialog;
            currentFrameWidgets = new List<CapturedWidget>();
            nextDrawIndex = 0;
            // Defensive reset: if a previous frame threw between some widget's Prefix and its
            // own Postfix (which is where ExitWidget lives), captureDepth could be left nonzero.
            captureDepth = 0;
        }

        /// <summary>
        /// Marks entry into a patched widget call. Returns true if this is the OUTERMOST
        /// patched widget on the stack right now (depth was 0) - only the outermost call
        /// should record a CapturedWidget or apply a pending action; nested calls (a
        /// Listing_Standard wrapper calling its raw Widgets.* equivalent, which may itself
        /// call Widgets.Label) must still balance Enter/Exit but do nothing else.
        /// </summary>
        public static bool EnterWidget()
        {
            bool outer = captureDepth == 0;
            captureDepth++;
            return outer;
        }

        /// <summary>Marks exit from a patched widget call. Must be called exactly once for
        /// every <see cref="EnterWidget"/> call, including nested/non-outer ones.</summary>
        public static void ExitWidget()
        {
            if (captureDepth > 0)
                captureDepth--;
        }

        /// <summary>True when no patched widget call is currently on the stack. Used by the
        /// leaf Widgets.Label patch, which never calls another patched widget itself and so
        /// never needs to Enter/Exit - it only needs to know whether it's being called
        /// directly (top level) or nested inside some other widget that already claimed the
        /// "outer" slot.</summary>
        public static bool AtTopLevel => captureDepth == 0;

        /// <summary>Called by ModSettingsDialogPatch's Finalizer - always runs, even if the
        /// mod's DoSettingsWindowContents threw. Hands the frame's capture off to the menu
        /// and ages out any pending action that never found its target.</summary>
        public static void EndFrame()
        {
            Capturing = false;
            // Only the dialog our menu bound to may refresh it - the other (empty) dialog's
            // frame must not overwrite the menu with 0 widgets.
            ModSettingsMenuState.NotifyFrameCaptured(currentFrameDialog, currentFrameWidgets);

            if (pendingAction != null)
            {
                pendingAction.FramesRemaining--;
                if (pendingAction.FramesRemaining <= 0)
                    pendingAction = null;
            }
        }

        /// <summary>Records a widget drawn this frame and returns its DrawIndex.</summary>
        public static int RecordWidget(CapturedWidget.Kind kind, string label, bool boolValue = false, float floatValue = 0f, float min = 0f, float max = 0f)
        {
            int index = nextDrawIndex++;
            currentFrameWidgets.Add(new CapturedWidget
            {
                WidgetKind = kind,
                Label = label ?? "",
                BoolValue = boolValue,
                FloatValue = floatValue,
                Min = min,
                Max = max,
                DrawIndex = index
            });
            return index;
        }

        /// <summary>
        /// Records a slider. Many mods draw a bare slider (Widgets.HorizontalSlider /
        /// Listing_Standard.Slider) with the value baked into a SEPARATE label drawn just
        /// before it (e.g. a Label "Volume: 100%" immediately followed by the slider). When
        /// this slider has no label of its own, adopt that immediately-preceding standalone
        /// Label as the slider's label and drop the now-redundant Label item, so the control
        /// reads as one "Volume: 100%, slider" entry instead of a dead label + an unlabeled
        /// slider. Sliders that carry their own label (SliderLabeled) keep it.
        /// </summary>
        public static int RecordSlider(string label, float val, float min, float max)
        {
            if (string.IsNullOrEmpty(label) && currentFrameWidgets.Count > 0)
            {
                var last = currentFrameWidgets[currentFrameWidgets.Count - 1];
                if (last.WidgetKind == CapturedWidget.Kind.Label)
                {
                    label = last.Label;
                    currentFrameWidgets.RemoveAt(currentFrameWidgets.Count - 1);
                }
            }
            return RecordWidget(CapturedWidget.Kind.Slider, label, floatValue: val, min: min, max: max);
        }

        /// <summary>
        /// Records a text field. Like sliders, a bare text field (Widgets.TextField / TextArea,
        /// which is also the leaf every numeric field bottoms out in) carries no label of its own -
        /// the mod draws a separate Label just before it (e.g. "Max stack: " then the field, or
        /// TextFieldNumericLabeled's own left-half Label). Adopt that immediately-preceding
        /// standalone Label as the field's caption and drop it, so the control reads as one
        /// "Max stack, text field, 75" entry instead of a dead label plus an unlabeled field.
        /// </summary>
        public static int RecordTextField(string text)
        {
            string label = null;
            if (currentFrameWidgets.Count > 0)
            {
                var last = currentFrameWidgets[currentFrameWidgets.Count - 1];
                if (last.WidgetKind == CapturedWidget.Kind.Label)
                {
                    label = last.Label;
                    currentFrameWidgets.RemoveAt(currentFrameWidgets.Count - 1);
                }
            }
            int index = nextDrawIndex++;
            currentFrameWidgets.Add(new CapturedWidget
            {
                WidgetKind = CapturedWidget.Kind.TextField,
                Label = label ?? "",
                StringValue = text ?? "",
                DrawIndex = index
            });
            return index;
        }

        /// <summary>
        /// Records an integer +/- entry (Widgets.IntEntry / Listing_Standard.IntEntry). Adopts a
        /// preceding standalone Label as its caption exactly like a text field, since IntEntry
        /// draws no label of its own.
        /// </summary>
        public static int RecordIntEntry(int value, int multiplier, int min)
        {
            string label = null;
            if (currentFrameWidgets.Count > 0)
            {
                var last = currentFrameWidgets[currentFrameWidgets.Count - 1];
                if (last.WidgetKind == CapturedWidget.Kind.Label)
                {
                    label = last.Label;
                    currentFrameWidgets.RemoveAt(currentFrameWidgets.Count - 1);
                }
            }
            int index = nextDrawIndex++;
            currentFrameWidgets.Add(new CapturedWidget
            {
                WidgetKind = CapturedWidget.Kind.IntEntry,
                Label = label ?? "",
                IntValue = value,
                Multiplier = multiplier < 1 ? 1 : multiplier,
                MinInt = min,
                DrawIndex = index
            });
            return index;
        }

        /// <summary>Queues a ref-bool write (checkbox toggle) for the widget patches to apply
        /// the next time they see a matching (DrawIndex, Label, Kind).</summary>
        public static void SetPendingBool(int drawIndex, string label, CapturedWidget.Kind kind, bool targetValue)
        {
            pendingAction = new PendingAction
            {
                DrawIndex = drawIndex,
                Label = label,
                WidgetKind = kind,
                TargetBool = targetValue,
                FramesRemaining = PendingActionTimeoutFrames
            };
        }

        /// <summary>Queues a ref/return-value float write (slider drag) for the widget patches
        /// to apply the next time they see a matching (DrawIndex, Label, Kind).</summary>
        public static void SetPendingFloat(int drawIndex, string label, CapturedWidget.Kind kind, float targetValue)
        {
            pendingAction = new PendingAction
            {
                DrawIndex = drawIndex,
                Label = label,
                WidgetKind = kind,
                TargetFloat = targetValue,
                FramesRemaining = PendingActionTimeoutFrames
            };
        }

        /// <summary>Queues a text write for the widget patches to apply the next time they see a
        /// matching (DrawIndex, Label, Kind) - overriding the field's returned string so the mod's
        /// own "x = TextField(...)" / numeric-parse code reads the edited value.</summary>
        public static void SetPendingString(int drawIndex, string label, CapturedWidget.Kind kind, string targetValue)
        {
            pendingAction = new PendingAction
            {
                DrawIndex = drawIndex,
                Label = label,
                WidgetKind = kind,
                TargetString = targetValue,
                FramesRemaining = PendingActionTimeoutFrames
            };
        }

        /// <summary>Queues a "clicked" activation (button press / radio selection) for the
        /// widget patches to apply as a __result override the next time they see a matching
        /// (DrawIndex, Label, Kind).</summary>
        public static void SetPendingActivation(int drawIndex, string label, CapturedWidget.Kind kind)
        {
            pendingAction = new PendingAction
            {
                DrawIndex = drawIndex,
                Label = label,
                WidgetKind = kind,
                TargetBool = true,
                FramesRemaining = PendingActionTimeoutFrames
            };
        }

        /// <summary>
        /// If a pending bool-write action targets this exact (drawIndex, kind), consumes it
        /// and outputs the new value - but only if the label still matches what was targeted.
        /// A label mismatch means the widget set changed underneath us (the mod redrew a
        /// different UI at this position this frame); the action is dropped rather than
        /// mis-applied to the wrong control.
        /// </summary>
        public static bool TryConsumePendingBool(int drawIndex, string label, CapturedWidget.Kind kind, out bool newValue)
        {
            newValue = false;
            if (pendingAction == null || pendingAction.DrawIndex != drawIndex || pendingAction.WidgetKind != kind)
                return false;

            bool matched = pendingAction.TargetBool != null && pendingAction.Label == label;
            if (matched)
                newValue = pendingAction.TargetBool.Value;

            // Whether it matched or not, this pending action was meant for this exact slot -
            // clear it so a stale/mismatched action never lingers to be tried again.
            pendingAction = null;
            return matched;
        }

        /// <summary>
        /// Slider float writes. Unlike the bool/activation consumers this does NOT check the
        /// label: mods bake the live value into a slider's label ("Volume: 100%"), so the label
        /// changes every time the value does - matching on it would make the apply fail
        /// intermittently as the value moves. (DrawIndex, Kind) alone is a stable enough
        /// correlation for the one queued slider action.
        /// </summary>
        public static bool TryConsumePendingFloat(int drawIndex, CapturedWidget.Kind kind, out float newValue)
        {
            newValue = 0f;
            if (pendingAction == null || pendingAction.DrawIndex != drawIndex || pendingAction.WidgetKind != kind)
                return false;
            if (pendingAction.TargetFloat == null)
                return false;

            newValue = pendingAction.TargetFloat.Value;
            pendingAction = null;
            return true;
        }

        /// <summary>Returns the target of a queued slider action for this widget, if any, so the
        /// menu can compound rapid Left/Right presses from the value it last requested rather
        /// than from the (lagging) last-captured value.</summary>
        public static bool TryGetPendingFloat(int drawIndex, out float target)
        {
            target = 0f;
            if (pendingAction == null || pendingAction.DrawIndex != drawIndex || pendingAction.TargetFloat == null)
                return false;
            target = pendingAction.TargetFloat.Value;
            return true;
        }

        /// <summary>
        /// If a pending text write targets this exact (drawIndex, kind), consumes it and outputs
        /// the new string. Like the slider consumer, this matches on (DrawIndex, Kind) only, not
        /// the label: the leaf Widgets.TextField patch that applies the write has no access to the
        /// adopted caption, and a single queued action correlated by draw order is unambiguous.
        /// </summary>
        public static bool TryConsumePendingString(int drawIndex, CapturedWidget.Kind kind, out string newValue)
        {
            newValue = null;
            if (pendingAction == null || pendingAction.DrawIndex != drawIndex || pendingAction.WidgetKind != kind)
                return false;
            if (pendingAction.TargetString == null)
                return false;

            newValue = pendingAction.TargetString;
            pendingAction = null;
            return true;
        }

        /// <summary>Same contract as <see cref="TryConsumePendingBool"/>, for button/radio activations.</summary>
        public static bool TryConsumePendingActivation(int drawIndex, string label, CapturedWidget.Kind kind)
        {
            if (pendingAction == null || pendingAction.DrawIndex != drawIndex || pendingAction.WidgetKind != kind)
                return false;

            bool matched = pendingAction.TargetBool == true && pendingAction.Label == label;
            pendingAction = null;
            return matched;
        }

        /// <summary>Resets all capture/pending state. Called when the dialog closes.</summary>
        public static void Reset()
        {
            Capturing = false;
            currentFrameWidgets = new List<CapturedWidget>();
            nextDrawIndex = 0;
            pendingAction = null;
            captureDepth = 0;
        }
    }
}
