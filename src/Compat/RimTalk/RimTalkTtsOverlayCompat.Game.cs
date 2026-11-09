using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard parity for the RimTalk TTS addon's overlay-adjacent controls, the ones a sighted
    /// player reaches with a mouse click on the map view or the PlaySettings row, outside any of
    /// the addon's own windows. Two surfaces, each gated exactly as the addon gates its own
    /// drawing so a keyboard action never appears when the matching mouse control would not:
    ///
    /// SURFACE 1 -- <c>RimTalk.TTS.Patches.ToggleButtonPatch</c> (postfix on
    /// <c>PlaySettings.DoPlaySettingsGlobalControls</c>): a toggleable icon shown while TTS is
    /// enabled and <c>TTSSettings.ButtonDisplay</c> is on. Its click flips the session-only
    /// <c>TTSSettings.isOnButton</c> and queues <c>RimTalkPatches.RequestToggle</c>, whose next
    /// tick posts the addon's on/off message and, when turning off, resets the pipeline.
    /// <see cref="TrySetOnState"/> does the same pair; the icon itself needs a live
    /// <c>WidgetRow</c> from an in-progress PlaySettings draw, which cannot be built outside one.
    ///
    /// SURFACE 2 -- the four map-view buttons <c>OverlayButtonPatch</c> draws while
    /// <c>TtsRuntime.ShowControlButtons</c> holds (Reset, Generate, Ignore, Display next). Each
    /// button's handler is a private static method; the keyboard invokes those exact methods by
    /// reflection (vehicle A, including the addon's own Messages.Message calls).
    ///
    /// Independent reflection cache from <see cref="RimTalkNarrativeCompat"/>'s TTS-backoff probe:
    /// the TtsRuntime/TTSSettings types come from <see cref="RimTalkSettingsFacade"/>, but every
    /// member stays bound on this class's own surface so a drift in one concern never takes
    /// another down. Every type is resolved by exact name, never swept (CLAUDE.md gotcha).
    /// </summary>
    internal static class RimTalkTtsOverlayCompat
    {
        private const string TtsPackageId = "nitoritech.rimtalk.tts";

        private static readonly LazyReflectionGate gate =
            new LazyReflectionGate("RimTalk TTS compat (overlay-adjacent controls)", ResolveReflection);

        private static PropertyInfo runtimeCurrentProperty;
        private static PropertyInfo runtimeIsEnabledProperty;
        private static PropertyInfo runtimeShowControlButtonsProperty;
        private static PropertyInfo runtimeSettingsProperty;
        private static PropertyInfo buttonDisplayProperty;
        private static PropertyInfo isOnButtonProperty;
        private static MethodInfo requestToggleMethod;
        private static MethodInfo resetButtonFuncMethod;
        private static MethodInfo generateButtonFuncMethod;
        private static MethodInfo ignoreButtonFuncMethod;
        private static MethodInfo displayNextMethod;

        private static bool ResolveReflection(ReflectionSurface surface)
        {
            Type runtimeType = surface.Supplied("RimTalk.TTS.TtsRuntime", RimTalkSettingsFacade.TtsRuntimeType);
            Type settingsType = surface.Supplied("RimTalk.TTS.Data.TTSSettings", RimTalkSettingsFacade.TtsSettingsType);
            Type patchesType = surface.Type("RimTalk.TTS.Patches.RimTalkPatches");
            Type overlayButtonPatchType = surface.Type("RimTalk.TTS.Patches.OverlayButtonPatch");
            Type overlayPacingPatchType = surface.Type("RimTalk.TTS.Patches.OverlayPacingPatch");

            PropertyInfo current = surface.Property(runtimeType, "Current");
            PropertyInfo isEnabled = surface.Property(runtimeType, "IsEnabled");
            PropertyInfo showControlButtons = surface.Property(runtimeType, "ShowControlButtons");
            PropertyInfo settings = surface.Property(runtimeType, "Settings");
            PropertyInfo buttonDisplay = surface.Property(settingsType, "ButtonDisplay");
            PropertyInfo isOnButton = surface.Property(settingsType, "isOnButton");
            MethodInfo requestToggle = surface.Method(patchesType, "RequestToggle", new[] { typeof(bool) });
            MethodInfo resetMethod = surface.Method(overlayButtonPatchType, "ResetButtonFunc", Type.EmptyTypes);
            MethodInfo generateMethod = surface.Method(overlayButtonPatchType, "GenerateButtonFunc", Type.EmptyTypes);
            MethodInfo ignoreMethod = surface.Method(overlayButtonPatchType, "IgnoreButtonFunc", Type.EmptyTypes);
            MethodInfo displayNext = surface.Method(overlayPacingPatchType, "DisplayNext", Type.EmptyTypes);

            if (!surface.Ready)
            {
                return false;
            }

            runtimeCurrentProperty = current;
            runtimeIsEnabledProperty = isEnabled;
            runtimeShowControlButtonsProperty = showControlButtons;
            runtimeSettingsProperty = settings;
            buttonDisplayProperty = buttonDisplay;
            isOnButtonProperty = isOnButton;
            requestToggleMethod = requestToggle;
            resetButtonFuncMethod = resetMethod;
            generateButtonFuncMethod = generateMethod;
            ignoreButtonFuncMethod = ignoreMethod;
            displayNextMethod = displayNext;
            return true;
        }

        /// <summary>The live TtsRuntime, or null if the addon isn't installed, reflectable, or initialized.</summary>
        private static object Runtime()
        {
            if (!ModsConfig.IsActive(TtsPackageId) || !gate.Ensure())
            {
                return null;
            }
            try
            {
                return runtimeCurrentProperty.GetValue(null);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS compat: runtime probe", ex);
                return null;
            }
        }

        /// <summary>The live TTSSettings, gated as ToggleButtonPatch gates its icon: the runtime exists and TTS is enabled.</summary>
        private static object EnabledSettings()
        {
            object runtime = Runtime();
            if (runtime == null || !(runtimeIsEnabledProperty.GetValue(runtime) is bool enabled) || !enabled)
            {
                return null;
            }
            return runtimeSettingsProperty.GetValue(runtime);
        }

        /// <summary>Mirrors ToggleButtonPatch.Postfix's own gate (ButtonDisplay must be on). Out param is the current isOnButton state.</summary>
        internal static bool TryGetOnState(out bool isOn)
        {
            isOn = false;
            try
            {
                object settings = EnabledSettings();
                if (settings == null || !(buttonDisplayProperty.GetValue(settings) is bool shown) || !shown)
                {
                    return false;
                }
                isOn = isOnButtonProperty.GetValue(settings) is bool on && on;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS compat: on-state read", ex);
                return false;
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors ToggleButtonPatch.Postfix's click body (see class header): the
        /// session-only isOnButton write, then the addon's own RequestToggle, which posts its
        /// message and resets the pipeline on the next tick exactly as for a click.
        /// </summary>
        internal static bool TrySetOnState(bool value)
        {
            bool unused;
            if (!TryGetOnState(out unused))
            {
                return false;
            }
            try
            {
                isOnButtonProperty.SetValue(EnabledSettings(), value);  // MUTATION-C: see summary.
                requestToggleMethod.Invoke(null, new object[] { value });
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS compat: on-state write", ex);
                return false;
            }
        }

        /// <summary>Mirrors OverlayButtonPatch's own draw gate, <c>TtsRuntime.ShowControlButtons</c>.</summary>
        internal static bool ControlButtonsAvailable()
        {
            try
            {
                object runtime = Runtime();
                return runtime != null && runtimeShowControlButtonsProperty.GetValue(runtime) is bool shown && shown;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS compat: control-buttons gate", ex);
                return false;
            }
        }

        /// <summary>Vehicle A: the exact handler the map view's Reset button runs.</summary>
        internal static bool TryResetAudio()
        {
            return InvokeButtonFunc(resetButtonFuncMethod, "Reset");
        }

        /// <summary>Vehicle A: the exact handler the map view's Generate button runs.</summary>
        internal static bool TryGenerateDialogue()
        {
            return InvokeButtonFunc(generateButtonFuncMethod, "Generate");
        }

        /// <summary>Vehicle A: the exact handler the map view's Ignore button runs.</summary>
        internal static bool TryIgnoreAllDialogue()
        {
            return InvokeButtonFunc(ignoreButtonFuncMethod, "Ignore");
        }

        /// <summary>Vehicle A: the exact handler the map view's Display next button runs.</summary>
        internal static bool TryDisplayNextDialogue()
        {
            return InvokeButtonFunc(displayNextMethod, "Display");
        }

        private static bool InvokeButtonFunc(MethodInfo method, string name)
        {
            if (!ControlButtonsAvailable())
            {
                return false;
            }
            try
            {
                method.Invoke(null, null);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError($"RimTalk TTS compat: {name} control", ex);
                return false;
            }
        }
    }
}
