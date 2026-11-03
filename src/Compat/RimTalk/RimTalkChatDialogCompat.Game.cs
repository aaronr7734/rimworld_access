using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for <c>RimTalk.UI.CustomDialogueWindow</c>: resolves the type and its
    /// private <c>_initiator</c>/<c>_recipient</c>/<c>_text</c> fields once behind <see cref="Ready"/>
    /// (missing type or member = RimTalk not loaded, or its shipped DLL drifted -- silent decline),
    /// and installs the two Harmony guards <see cref="RimTalkChatScope"/>'s class remarks require.
    /// </summary>
    internal static class RimTalkChatDialogCompat
    {
        private const string RimTalkPackageId = "cj.rimtalk";

        private static readonly Type dialogType;
        private static readonly FieldInfo initiatorField;
        private static readonly FieldInfo recipientField;
        private static readonly FieldInfo textField;
        private static readonly bool ready;

        public static Type DialogType => dialogType;
        public static bool Ready => ready;

        static RimTalkChatDialogCompat()
        {
            var surface = new ReflectionSurface("RimTalk chat dialog compat");

            dialogType = surface.Type("RimTalk.UI.CustomDialogueWindow");
            initiatorField = surface.Field(dialogType, "_initiator");
            recipientField = surface.Field(dialogType, "_recipient");
            textField = surface.Field(dialogType, "_text");

            ready = surface.Ready;
        }

        internal static Pawn GetInitiator(Window w)
        {
            return initiatorField != null ? initiatorField.GetValue(w) as Pawn : null;
        }

        internal static Pawn GetRecipient(Window w)
        {
            return recipientField != null ? recipientField.GetValue(w) as Pawn : null;
        }

        internal static string GetText(Window w)
        {
            return textField != null ? (textField.GetValue(w) as string ?? "") : "";
        }

        internal static void SetText(Window w, string value)
        {
            if (textField != null)
            {
                // MUTATION-C: mirrors CustomDialogueWindow.DoWindowContents' own return-assign
                // (`_text = Widgets.TextField(...)`); no invocable vehicle exists for a private
                // field with no setter method, so the same plain assignment is reproduced here.
                textField.SetValue(w, value ?? "");
            }
        }

        /// <summary>Registers the scope and installs its draw-pass/accept-key guards. No-op unless both RimTalk is active and this dialog's reflection surface resolved.</summary>
        public static void Register()
        {
            if (!ready || !ModsConfig.IsActive(RimTalkPackageId))
            {
                return;
            }

            ScopeForWindow.Register(dialogType, delegate (Window w)
            {
                return new RimTalkChatScope(w);
            });
            PatchGuards();
        }

        /// <summary>
        /// Two independent patches, both required -- see <see cref="RimTalkChatScope"/>'s class
        /// remarks for why each exists:
        ///
        /// 1) DoWindowContents: brackets the scope's capture passes AND masks the window's own
        /// raw in-draw Return poll (<see cref="TextFieldRawPollGuard"/>) unconditionally, since this
        /// scope drives Enter entirely itself.
        ///
        /// 2) OnAcceptKeyPressed: CustomDialogueWindow overrides it WITHOUT calling base, and
        /// closeOnAccept defaults true on the base Window class, so vanilla's own
        /// WindowStack.Notify_PressedAccept invokes it unconditionally on every Enter press --
        /// completely bypassing the existing WindowAcceptKeyRouterPatch (patched on the declaring
        /// type Window, which an override-without-base escapes, per CLAUDE.md's Harmony gotcha).
        /// The twin's Prefix reuses WindowAcceptKeyRouterPatch.Prefix UNMODIFIED: since this scope
        /// never calls OnAcceptKeyPressed itself (its own vehicle is always
        /// ButtonTextCapture.RequestClick), there is no self-blocking risk in doing so.
        /// </summary>
        private static void PatchGuards()
        {
            try
            {
                MethodInfo doWindowContents = AccessTools.Method(dialogType, "DoWindowContents");
                if (doWindowContents == null)
                {
                    ModLogger.Error("RimTalk chat dialog compat: could not resolve CustomDialogueWindow.DoWindowContents; declining draw-pass patch.");
                }
                else
                {
                    RimWorldAccessMod.HarmonyInstance.Patch(doWindowContents,
                        prefix: new HarmonyMethod(typeof(RimTalkChatDialogCompat), nameof(DrawPrefix)),
                        finalizer: new HarmonyMethod(typeof(RimTalkChatDialogCompat), nameof(DrawFinalizer)));
                }

                MethodInfo onAcceptKeyPressed = AccessTools.Method(dialogType, "OnAcceptKeyPressed");
                if (onAcceptKeyPressed == null)
                {
                    ModLogger.Error("RimTalk chat dialog compat: could not resolve CustomDialogueWindow.OnAcceptKeyPressed; declining accept-key guard.");
                }
                else
                {
                    RimWorldAccessMod.HarmonyInstance.Patch(onAcceptKeyPressed,
                        prefix: new HarmonyMethod(typeof(RimTalkChatDialogCompat), nameof(AcceptKeyPrefix)));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimTalk chat dialog compat: PatchGuards failed: {ex.Message}");
            }
        }

        public static void DrawPrefix(object __instance, out RimTalkChatScope __state)
        {
            __state = null;
            try
            {
                Window window = __instance as Window;
                RimTalkChatScope scope = RimTalkTextDialogScopeBase.OwningScope<RimTalkChatScope>(window);
                if (scope != null)
                {
                    // Unconditional: this scope drives Enter entirely itself (see class remarks),
                    // so the window's own raw in-draw Return poll -- gated on real Unity native
                    // focus sitting on "CustomTalkTextField", which ShellTextFocus.ReleaseNativeFocus
                    // fights every pass -- must never independently fire either.
                    TextFieldRawPollGuard.MaskAcceptPoll(true);
                    __state = scope;
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk chat dialog guard", ex);
            }
        }

        public static void DrawFinalizer(Exception __exception, RimTalkChatScope __state)
        {
            if (__state != null)
            {
                CapturePasses.Finish(__state, FocusStack.Contains(__state), __exception,
                    () => TextDialogCapture.EndMaskedPass(), s => s.OnGuiPass(), "RimTalk chat dialog guard");
            }
        }

        /// <summary>
        /// The declaring-type twin (see PatchGuards' remarks): __instance is typed <c>object</c>
        /// because the patched method's declaring type (CustomDialogueWindow) is never referenced
        /// at compile time; casting to the compile-time-known base Window is enough for
        /// WindowAcceptKeyRouterPatch.Prefix's own logic, which only ever needs the Window API.
        /// </summary>
        public static bool AcceptKeyPrefix(object __instance)
        {
            try
            {
                return WindowAcceptKeyRouterPatch.Prefix(__instance as Window);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalk chat dialog guard", ex);
                return true;
            }
        }

    }
}
