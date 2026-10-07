using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Drives the whole <see cref="Dialog_Rename{T}"/> family, modded closed generics included,
    /// through one generic-hierarchy registration on the open generic.
    ///
    /// One content region holding vanilla's name field, plus the automatic Buttons region with the
    /// dialog's OK button. The field row runs the mod-wide browse/edit grammar through a
    /// <see cref="TextFieldEditSession"/>: browse announces the value without entering it, so the
    /// cursor can arrow past to OK; Enter opens edit, and a printable character opens edit and
    /// inserts itself. While editing the session owns every key. Enter confirms AND presses OK —
    /// the field is this dialog's only content, so vanilla's type-and-Enter submit flow holds —
    /// while Escape returns to browse keeping the value, which was mirrored live into curName all
    /// along. OK is also the scope's <see cref="DefaultAcceptActionId"/>, so Shift+Enter presses
    /// it from anywhere. CJK composition flows through the IME funnel into the same session.
    ///
    /// The shell owns Unity keyboard focus: vanilla's one-shot UI.FocusControl is pre-empted in
    /// OnPush via the focusedRenameField flag, and <see cref="ShellTextFocus.ReleaseNativeFocus"/>
    /// reasserts it every pass, so no native TextField holds focus to swallow the arrow keys.
    ///
    /// Every row's Enter belongs to this scope, so <see cref="OwnsAccept"/> stays the chassis
    /// default: the field row opens its edit session, and Enter on OK injects vanilla's own click
    /// through <see cref="ButtonTextCapture.RequestClick"/>, running its submit body unmodified.
    /// Vanilla's focus-blind raw KeyDown poll at the top of DoWindowContents is therefore masked on
    /// EVERY frame — <see cref="ShouldMaskAcceptPoll"/> is always true — by masking
    /// Event.current.keyCode for exactly vanilla's body rather than calling Use(): the focused
    /// window's pass can run before the dispatcher's and shares Event state with it, so Use() here
    /// would starve this scope's own Enter claims. Leaving OK's Enter to that raw poll made the key
    /// depend on GUI pass order, since the dispatcher's modal swallow Uses the event first. The draw
    /// patch resolves this scope through <see cref="TextDialogShared.ScopeOwning{T}"/>, never off the
    /// top of the focus stack, because a live edit session's scope sits above it and a top-only
    /// lookup would skip the mask and MirrorLive while typing.
    ///
    /// Escape: this scope owns cancel only while a typeahead search is live, to clear it. Otherwise
    /// Escape is UNCLAIMED, so vanilla's own Escape cancels the dialog in browse mode. While editing,
    /// the modal session consumes it and the accept guard keeps it from reaching the window's cancel.
    /// </summary>
    public sealed class RenameScope : ScreenScope
    {
        private const float FocusRingExpand = 2f;

        private const string ActivateFocusedActionId = "renameDialog.activateFocused";
        private const string OkActionId = "renameDialog.ok";

        // curName is declared on the open generic as a plain string field, but every concrete rename
        // dialog is a DIFFERENT closed generic at the CLR level, so one FieldRefAccess cannot cover
        // the family. Resolved per concrete runtime type and cached, since a type's FieldInfo never
        // changes across its instances.
        private static readonly Dictionary<Type, FieldInfo> curNameFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> focusedFieldCache = new Dictionary<Type, FieldInfo>();

        private static FieldInfo ResolveField(Dictionary<Type, FieldInfo> cache, Type concreteDialogType, string fieldName)
        {
            FieldInfo field;
            if (!cache.TryGetValue(concreteDialogType, out field))
            {
                field = AccessTools.Field(concreteDialogType, fieldName);
                cache[concreteDialogType] = field;
            }
            return field;
        }

        private readonly Window dialog;
        private readonly FieldInfo curNameField;
        // Resolved per closed generic exactly like curName, and pre-set true in OnPush so vanilla's
        // one-shot UI.FocusControl never runs.
        private readonly FieldInfo focusedRenameFieldField;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public RenameScope(Window dialog)
        {
            this.dialog = dialog;
            curNameField = ResolveField(curNameFieldCache, dialog.GetType(), "curName");
            focusedRenameFieldField = ResolveField(focusedFieldCache, dialog.GetType(), "focusedRenameField");

            // An alternate onto the shared activation path, keeping the registered chord live.
            Claim(ActivateFocusedActionId, e => ActivateCurrent(),
                when: delegate { return !TextDialogShared.ForeignWindowAbove(dialog); });

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "rename-dialog"; }
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>The name row and OK are this scope's own rows; OK runs the dialog's submit body through the capture click.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Cross-row search: a small space here, but the grammar is the mod-wide one.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// The live scope owning <paramref name="window"/>, from anywhere on the focus stack; see
        /// <see cref="TextDialogShared.ScopeOwning{T}"/> for why the top of the stack is wrong.
        /// </summary>
        internal static RenameScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<RenameScope>(
                window, delegate(RenameScope s, Window w) { return s.Owns(w); });
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        public override void OnPush()
        {
            base.OnPush();
            // Pre-empt vanilla's one-shot native focus grab before the first draw: a vanilla control
            // holding Unity focus would eat the arrow keys this scope needs to reach OK.
            if (focusedRenameFieldField != null)
            {
                focusedRenameFieldField.SetValue(dialog, true);
            }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return ResolveFieldLabel();
        }

        protected override int ContentItemCount(int region)
        {
            return 1;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription { Label = ResolveFieldLabel(), Role = ElementRole.TextField };
            string value = (string)curNameField.GetValue(dialog);
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

        protected override void ActivateContentItem(int region, int index)
        {
            BeginEdit(announcePrompt: true);
        }

        /// <summary>
        /// OK, activated through vanilla's own click so its submit body runs unmodified. Declared
        /// rather than captured because the click addresses the button by the capture index this
        /// scope's draw bracket recorded it at.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (OkCaptureIndex() >= 0)
                {
                    actions.Add(new ScreenAction(ResolveOkLabel(), ClickOk, OkActionId));
                }
                return actions;
            }
        }

        protected override string DefaultAcceptActionId
        {
            get { return OkActionId; }
        }

        /// <summary>Vanilla draws OK last, whatever else the dialog put above it; -1 before the first capture pass.</summary>
        private static int OkCaptureIndex()
        {
            return ButtonTextCapture.Items.Count - 1;
        }

        private void ClickOk()
        {
            int index = OkCaptureIndex();
            if (index < 0)
            {
                return;
            }
            // Stamped defensively, though vanilla's raw poll is masked on every frame now.
            ShellFrameStamps.MarkAcceptConsumed();
            ButtonTextCapture.RequestClick(index);
        }

        internal void BeginDrawPass()
        {
            // The ring index is read from the pass just finished, before BeginPass clears it.
            ButtonTextCapture.BeginPass(FocusedButtonCaptureIndex());
            TextFieldCapture.BeginPass();
        }

        internal void OnGuiPass()
        {
            ButtonTextCapture.EndPass();
            TextFieldCapture.EndPass();

            // Keep the shell the sole owner of Unity keyboard focus so the arrow keys reach the
            // dispatcher, and keep vanilla's TextField rendering the live buffer while editing.
            // MirrorLive no-ops in browse mode, leaving curName as the committed value.
            ShellTextFocus.ReleaseNativeFocus();
            session.MirrorLive();

            DrawFieldRing();
        }

        /// <summary>The OK row's capture index while the cursor rests on it, else -1 (ButtonTextCapture draws the ring itself).</summary>
        private int FocusedButtonCaptureIndex()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != ContentRegionCount || region == null || region.IsEmpty)
            {
                return -1;
            }
            return OkCaptureIndex();
        }

        /// <summary>The field row's own ring; the Buttons region's is drawn by the capture tap.</summary>
        private void DrawFieldRing()
        {
            if (Model.RegionIndex != 0 || TextFieldCapture.Items.Count == 0)
            {
                return;
            }
            Rect rect = TextFieldCapture.Items[0].Rect;
            if (rect.width <= 0f)
            {
                return;
            }
            FocusRing.Draw(rect.ExpandedBy(FocusRingExpand));
        }

        /// <summary>
        /// Always true: every row's Enter belongs to this scope, so vanilla's focus-blind raw Return
        /// poll has no legitimate frame left to fire on.
        /// </summary>
        internal bool ShouldMaskAcceptPoll
        {
            get { return true; }
        }

        private void BeginEdit(bool announcePrompt)
        {
            string current = (string)curNameField.GetValue(dialog) ?? "";
            // Enter-confirm presses OK itself (the field is the dialog's only content); OK's
            // body still gates on NameIsValid, and Escape still exits without submitting.
            session.EnterEdit(
                current,
                TextFieldSpec.ForRimWorldDialog(dialog),
                ResolveFieldLabel(),
                ApplyValue,
                AnnounceCurrentItem,
                announcePrompt,
                onConfirm: ClickOk,
                silentConfirmExit: true);
        }

        /// <summary>Writes the live buffer into the real curName so vanilla renders it and its raw Enter poll submits the true name.</summary>
        private void ApplyValue(string value)
        {
            curNameField.SetValue(dialog, value ?? "");
        }

        /// <summary>
        /// A printable key on the field row opens its edit session and inserts the character;
        /// anything else falls through to typeahead. Browse-only — while editing, the dispatcher
        /// routes characters straight to the controller.
        /// </summary>
        public override bool HandleChar(char c)
        {
            if (TextDialogShared.ForeignWindowAbove(dialog))
            {
                return false;
            }
            if (!session.Editing && Model.RegionIndex == 0 && !char.IsControl(c) && !char.IsWhiteSpace(c))
            {
                BeginEdit(announcePrompt: false);
                session.FeedChar(c);
                return true;
            }
            return base.HandleChar(c);
        }

        private static string ResolveFieldLabel()
        {
            return "Rename".Translate().ToString();
        }

        private static string ResolveOkLabel()
        {
            int index = OkCaptureIndex();
            if (index >= 0)
            {
                string label = ButtonTextCapture.Items[index].Label;
                if (!string.IsNullOrEmpty(label))
                {
                    return label;
                }
            }
            return "OK".Translate().ToString();
        }
    }

    /// <summary>
    /// Brackets ButtonTextCapture/TextFieldCapture to the dialog's own draw and drives the scope's
    /// per-pass echo and focus ring, all inside the window's own GUI pass.
    ///
    /// Harmony CANNOT patch the open generic Dialog_Rename&lt;&gt;: Mono throws NotSupportedException
    /// at patch time, poisoning the mod's whole static constructor. T is constrained
    /// <c>class, IRenameable</c>, so Mono's generic sharing gives every instantiation one body and the
    /// Dialog_Rename&lt;IRenameable&gt; target covers them all. An override that skips base
    /// (Education's Dialog_RenameClassroom) escapes it, so every override is patched too; only the
    /// outermost bracket acts, since an override that calls base nests two.
    /// </summary>
    [HarmonyPatch]
    public static class RenameDrawPatch
    {
        private static object bracketedDialog;

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_Rename<IRenameable>), "DoWindowContents");
            foreach (Type type in SafeTypeSweep.SubclassesSafe(typeof(Window), (_, ex) => ModLogger.LimitedError("Rename dialog override sweep", ex)))
            {
                if (!SafeTypeSweep.TryEvaluate(type, t => IsRenameSubclass(t) ? AccessTools.DeclaredMethod(t, "DoWindowContents", new[] { typeof(Rect) }) : null,
                        out MethodInfo declared, (_, ex) => ModLogger.LimitedError("Rename dialog override sweep", ex)))
                {
                    continue;
                }
                if (declared != null && !declared.IsAbstract && !declared.ContainsGenericParameters)
                {
                    yield return declared;
                }
            }
        }

        private static bool IsRenameSubclass(Type type)
        {
            for (Type t = type.BaseType; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dialog_Rename<>))
                {
                    return true;
                }
            }
            return false;
        }

        [HarmonyPrefix]
        public static void Prefix(object __instance, out bool __state)
        {
            __state = false;
            if (bracketedDialog != null)
            {
                return;
            }
            try
            {
                RenameScope scope = RenameScope.OwningScope(__instance as Window);
                if (scope != null)
                {
                    bracketedDialog = __instance;
                    __state = true;
                    // Mask vanilla's raw Return poll: every row's Enter is this scope's, so the
                    // poll can never submit the rename from under one.
                    TextFieldRawPollGuard.MaskAcceptPoll(scope.ShouldMaskAcceptPoll);
                    scope.BeginDrawPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Rename dialog draw pass error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance, bool __state)
        {
            if (!__state)
            {
                return;
            }
            try
            {
                TextFieldRawPollGuard.RestoreAcceptPoll();
                RenameScope scope = RenameScope.OwningScope(__instance as Window);
                if (scope != null)
                {
                    scope.OnGuiPass();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Rename dialog draw pass error", ex);
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer(bool __state)
        {
            if (__state)
            {
                bracketedDialog = null;
            }
        }
    }
}
