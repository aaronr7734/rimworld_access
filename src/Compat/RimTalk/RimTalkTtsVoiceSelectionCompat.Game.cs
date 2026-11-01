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
    /// Reflection facade for <c>RimTalk.TTS.UI.VoiceSelectionWindow</c> and its
    /// <c>RimTalk.TTS.Data.VoiceModel</c> element type: resolves the window's constructor and its
    /// private <c>_pawn</c>/<c>_selectedVoiceId</c>/<c>_voiceModels</c> fields plus VoiceModel's two
    /// public fields once behind <see cref="Ready"/> (missing type or member = the TTS addon not
    /// loaded, or its shipped DLL drifted -- silent decline), and installs the scope registration
    /// and draw-pass Harmony patch. Never touches RimTalk.TTS.Service.SiliconFlowClient or anything
    /// that references it (see <see cref="RimTalkTtsCompat"/>'s poisoned-assembly remarks).
    /// </summary>
    internal static class RimTalkTtsVoiceSelectionCompat
    {
        private static readonly Type dialogType;
        private static readonly ConstructorInfo dialogCtor;
        private static readonly FieldInfo pawnField;
        private static readonly FieldInfo selectedVoiceIdField;
        private static readonly FieldInfo voiceModelsField;
        private static readonly Type voiceModelType;
        private static readonly FieldInfo voiceModelIdField;
        private static readonly FieldInfo voiceModelNameField;
        private static readonly bool ready;

        public static bool Ready => ready;

        static RimTalkTtsVoiceSelectionCompat()
        {
            var surface = new ReflectionSurface("RimTalk TTS compat (voice selection)");

            dialogType = surface.Type("RimTalk.TTS.UI.VoiceSelectionWindow");
            voiceModelType = surface.Type("RimTalk.TTS.Data.VoiceModel");

            pawnField = surface.Field(dialogType, "_pawn");
            selectedVoiceIdField = surface.Field(dialogType, "_selectedVoiceId");
            voiceModelsField = surface.Field(dialogType, "_voiceModels");
            voiceModelIdField = surface.Field(voiceModelType, "ModelId");
            voiceModelNameField = surface.Field(voiceModelType, "ModelName");
            dialogCtor = dialogType != null
                ? AccessTools.Constructor(dialogType, new[] { typeof(Pawn) })
                : null;

            ready = surface.Ready && dialogCtor != null;
        }

        internal static Pawn GetPawn(Window w)
        {
            return pawnField != null ? pawnField.GetValue(w) as Pawn : null;
        }

        internal static string GetSelectedVoiceId(Window w)
        {
            return selectedVoiceIdField != null ? selectedVoiceIdField.GetValue(w) as string : null;
        }

        internal static void SetSelectedVoiceId(Window w, string value)
        {
            if (selectedVoiceIdField != null)
            {
                // MUTATION-C: mirrors DrawVoiceOption's own click-handler assignment verbatim; see
                // RimTalkTtsVoiceSelectionScope's class remarks for why no A/B vehicle exists.
                selectedVoiceIdField.SetValue(w, value ?? "DEFAULT");
            }
        }

        internal static IList GetVoiceModels(Window w)
        {
            return voiceModelsField != null ? voiceModelsField.GetValue(w) as IList : null;
        }

        internal static string VoiceModelId(object voiceModel)
        {
            return voiceModelIdField != null ? voiceModelIdField.GetValue(voiceModel) as string : null;
        }

        internal static string VoiceModelName(object voiceModel)
        {
            return voiceModelNameField != null ? voiceModelNameField.GetValue(voiceModel) as string : null;
        }

        /// <summary>Vehicle A: the exact constructor call BioTabVoicePatch.AddVoiceElement's own click makes (`Find.WindowStack.Add(new VoiceSelectionWindow(pawn))`), reproduced by reflection since the type is never referenced at compile time.</summary>
        internal static void OpenVoiceSelection(Pawn pawn)
        {
            if (!ready || pawn == null)
            {
                return;
            }
            try
            {
                object instance = dialogCtor.Invoke(new object[] { pawn });
                Find.WindowStack.Add((Window)instance);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS voice selection guard", ex);
            }
        }

        public static void Register()
        {
            if (!ready)
            {
                return;
            }

            ScopeForWindow.Register(dialogType, delegate (Window w)
            {
                return new RimTalkTtsVoiceSelectionScope(w);
            });
            PatchDrawPass();
        }

        private static void PatchDrawPass()
        {
            try
            {
                MethodInfo doWindowContents = AccessTools.Method(dialogType, "DoWindowContents");
                if (doWindowContents == null)
                {
                    ModLogger.Error("RimTalk TTS compat: could not resolve VoiceSelectionWindow.DoWindowContents; declining draw-pass patch.");
                    return;
                }
                RimWorldAccessMod.HarmonyInstance.Patch(doWindowContents,
                    prefix: new HarmonyMethod(typeof(RimTalkTtsVoiceSelectionCompat), nameof(DrawPrefix)),
                    finalizer: new HarmonyMethod(typeof(RimTalkTtsVoiceSelectionCompat), nameof(DrawFinalizer)));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk TTS compat: PatchDrawPass failed: {ex.Message}");
            }
        }

        public static void DrawPrefix(object __instance, out bool __state)
        {
            __state = false;
            try
            {
                Window window = __instance as Window;
                RimTalkTtsVoiceSelectionScope scope = RimTalkTextDialogScopeBase.OwningScope<RimTalkTtsVoiceSelectionScope>(window);
                if (scope != null)
                {
                    __state = true;
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS voice selection guard", ex);
            }
        }

        public static void DrawFinalizer(object __instance, Exception __exception, bool __state)
        {
            if (!__state)
            {
                return;
            }
            try
            {
                RimTalkTtsVoiceSelectionScope scope = __exception == null
                    ? RimTalkTextDialogScopeBase.OwningScope<RimTalkTtsVoiceSelectionScope>(__instance as Window)
                    : null;
                if (scope != null)
                {
                    scope.OnGuiPass();
                }
                else
                {
                    WidgetCapture.EndPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk TTS voice selection guard", ex);
            }
        }
    }
}
