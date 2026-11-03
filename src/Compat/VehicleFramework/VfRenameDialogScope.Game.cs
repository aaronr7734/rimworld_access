using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard support for Vehicle Framework's <c>Vehicles.Dialog_GiveVehicleName</c> (opened by
    /// <c>VehiclePawn.Rename()</c>), modeled on <see cref="RenameScope"/> with three deltas forced
    /// by the dialog's shape.
    ///
    /// <b>One content region holding vanilla's name field</b>, and a Buttons region with TWO rows
    /// (OK and Remove Name) instead of RenameScope's one — this dialog draws a second button
    /// ("VF_RemoveName") with no equivalent in the vanilla Dialog_Rename&lt;T&gt; family. The field
    /// row runs the mod-wide browse/edit grammar through a <see cref="TextFieldEditSession"/>
    /// (Recipe C); while editing, the session owns every key.
    ///
    /// Enter belongs to this scope on EVERY row (<see cref="OwnsAccept"/> is the chassis default
    /// true), as it is in RenameScope — but by a different mechanism. This dialog has no raw
    /// top-of-body poll to mask, only <c>public override void OnAcceptKeyPressed() =&gt;
    /// AcceptName();</c> — and that window-level accept path would otherwise submit "OK"
    /// regardless of which button the cursor sits on, including Remove. Claiming Enter everywhere
    /// and routing it through the correctly captured button
    /// (<see cref="ButtonTextCapture.RequestClick"/>) keeps activation on the row the user is
    /// actually looking at; OK's real click still runs <c>AcceptName()</c>, so this is vehicle A.
    /// With no raw poll to protect against, no <see cref="TextFieldRawPollGuard"/> mask is needed
    /// anywhere in this scope.
    ///
    /// Escape keeps its documented posture, now expressed through the chassis default: this scope
    /// owns cancel only while a typeahead search is live (clearing the search). Otherwise
    /// closeOnCancel is the Window default true, so vanilla's own Escape cancels the dialog in
    /// browse mode; while editing, the modal session consumes Escape first.
    ///
    /// The character cap is read per dialog INSTANCE type from the protected virtual
    /// <c>MaxNameLength</c> property, so a modded subclass that overrides it still gets an accurate
    /// cap. The base dialog's own gate (<c>if (label.Length &lt; MaxNameLength) curName = label;</c>,
    /// MaxNameLength = 28) accepts a strictly-less-than length, i.e. 27 printable characters.
    /// </summary>
    public sealed class VfRenameDialogScope : ScreenScope
    {
        private const float FocusRingExpand = 2f;

        private const string ActivateFocusedActionId = "vfRenameDialog.activateFocused";
        private const int FallbackMaxLength = 27;

        /// <summary>Dialog draw order is OK first, then Remove Name (Dialog_GiveVehicleName.DoWindowContents).</summary>
        private const int OkCaptureIndex = 0;
        private const int RemoveCaptureIndex = 1;

        // curName and MaxNameLength are declared on the concrete Dialog_GiveVehicleName, but
        // resolution is cached per the dialog's OWN runtime type so a modded subclass that shadows
        // either member is still read correctly.
        private static readonly Dictionary<Type, FieldInfo> curNameFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, MethodInfo> maxNameLengthGetterCache = new Dictionary<Type, MethodInfo>();

        private static FieldInfo ResolveCurNameField(Type concreteDialogType)
        {
            FieldInfo field;
            if (!curNameFieldCache.TryGetValue(concreteDialogType, out field))
            {
                field = AccessTools.Field(concreteDialogType, "curName");
                curNameFieldCache[concreteDialogType] = field;
            }
            return field;
        }

        private static MethodInfo ResolveMaxNameLengthGetter(Type concreteDialogType)
        {
            MethodInfo getter;
            if (!maxNameLengthGetterCache.TryGetValue(concreteDialogType, out getter))
            {
                getter = AccessTools.PropertyGetter(concreteDialogType, "MaxNameLength");
                maxNameLengthGetterCache[concreteDialogType] = getter;
            }
            return getter;
        }

        private readonly Window dialog;
        private readonly FieldInfo curNameField;
        private readonly MethodInfo maxNameLengthGetter;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public VfRenameDialogScope(Window dialog)
        {
            this.dialog = dialog;
            curNameField = ResolveCurNameField(dialog.GetType());
            maxNameLengthGetter = ResolveMaxNameLengthGetter(dialog.GetType());

            // Space stays a same-shape alternate activation onto the shared activation path
            // (see class remarks: Enter is this scope's on every row).
            Claim(ActivateFocusedActionId, e => ActivateCurrent(),
                when: delegate { return !TextDialogShared.ForeignWindowAbove(dialog); });

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "vf-rename-dialog"; }
        }

        public override bool IsModal
        {
            get { return !TextDialogShared.ForeignWindowAbove(dialog); }
        }

        /// <summary>Both bottom buttons are activated by capture index (see <see cref="DeclaredActions"/>), which the click injection addresses them by.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        /// <summary>
        /// The live scope owning <paramref name="window"/>, from anywhere on the focus stack —
        /// what this dialog's draw patch resolves with. See
        /// <see cref="TextDialogShared.ScopeOwning{T}"/> for why the top of the stack is wrong.
        /// </summary>
        internal static VfRenameDialogScope OwningScope(Window window)
        {
            return TextDialogShared.ScopeOwning<VfRenameDialogScope>(
                window, delegate(VfRenameDialogScope s, Window w) { return s.Owns(w); });
        }

        // ---------------------------------------------------------------
        // Row model: the name field, then the dialog's own two buttons.
        // ---------------------------------------------------------------

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
        /// OK then Remove Name, each activated through vanilla's own click so the dialog's own
        /// body runs unmodified. Declared rather than captured because the click addresses the
        /// button by the capture index this scope's own draw bracket recorded it at.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                int captured = ButtonTextCapture.Items.Count;
                if (captured > OkCaptureIndex)
                {
                    actions.Add(new ScreenAction(ResolveOkLabel(), () => ClickButton(OkCaptureIndex)));
                }
                if (captured > RemoveCaptureIndex)
                {
                    actions.Add(new ScreenAction(ResolveRemoveLabel(), () => ClickButton(RemoveCaptureIndex)));
                }
                return actions;
            }
        }

        private void ClickButton(int captureIndex)
        {
            if (ButtonTextCapture.Items.Count <= captureIndex)
            {
                return;
            }
            // THE RULE: this Enter/Space must never also reach vanilla's OnAcceptKeyPressed.
            ShellFrameStamps.MarkAcceptConsumed();
            ButtonTextCapture.RequestClick(captureIndex);
        }

        // ---------------------------------------------------------------
        // Per-GUI-pass work, driven by the dialog's own draw
        // ---------------------------------------------------------------

        internal void BeginDrawPass()
        {
            // The ring index is read from the pass just finished, before BeginPass clears it.
            TextDialogCapture.BeginPass(FocusedButtonCaptureIndex());
        }

        internal void OnGuiPass()
        {
            ShellTextFocus.ReleaseNativeFocus();
            session.MirrorLive();

            DrawFieldRing();
        }

        /// <summary>The focused button's capture index, else -1 (ButtonTextCapture draws that ring itself).</summary>
        private int FocusedButtonCaptureIndex()
        {
            ListModel region = Model.CurrentRegion;
            if (Model.RegionIndex != ContentRegionCount || region == null || region.IsEmpty)
            {
                return -1;
            }
            return region.Index == 0 ? OkCaptureIndex : RemoveCaptureIndex;
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

        // ---------------------------------------------------------------
        // Enter / type to edit (browse -> edit), via the shared session
        // ---------------------------------------------------------------

        private void BeginEdit(bool announcePrompt)
        {
            string current = (string)curNameField.GetValue(dialog) ?? "";

            int maxLength = FallbackMaxLength;
            if (maxNameLengthGetter != null)
            {
                try
                {
                    maxLength = (int)maxNameLengthGetter.Invoke(dialog, null) - 1;
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfRenameDialogScope: MaxNameLength read failed, using {FallbackMaxLength}-char default: {ex.Message}");
                    maxLength = FallbackMaxLength;
                }
            }

            // MUTATION-C: mirrors Dialog_GiveVehicleName.DoWindowContents' own accept gate
            // ("if (label.Length < MaxNameLength) curName = label;", base MaxNameLength = 28 ->
            // 27 printable characters); no invocable vehicle exists for this length cap, so it is
            // reproduced here from the per-instance-reflected MaxNameLength.
            var spec = new TextFieldSpec(
                labelKey: "RimWorldAccess.TextInput.LabelDefault",
                maxLength: maxLength,
                minLength: 1);

            session.EnterEdit(
                current,
                spec,
                ResolveFieldLabel(),
                ApplyValue,
                AnnounceCurrentItem,
                announcePrompt);
        }

        /// <summary>Writes the live buffer into the real curName so vanilla renders it and OK's own AcceptName() reads the true name.</summary>
        private void ApplyValue(string value)
        {
            // MUTATION-C: mirrors Widgets.TextField return-assign in
            // Dialog_GiveVehicleName.DoWindowContents; the length<28 gate is enforced by this
            // session's TextFieldSpec (27-char cap, read from MaxNameLength at BeginEdit).
            curNameField.SetValue(dialog, value ?? "");
        }

        /// <summary>
        /// Browse-type-to-edit: a printable key on the field row opens its edit session and
        /// inserts the character; anything else falls through to the shared typeahead. While
        /// editing, the dispatcher routes characters straight to the controller.
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

        // ---------------------------------------------------------------
        // Labels
        // ---------------------------------------------------------------

        private static string ResolveFieldLabel()
        {
            return "Rename".Translate().ToString();
        }

        private static string ResolveOkLabel()
        {
            return CapturedLabel(OkCaptureIndex, "RimWorldAccess.Compat.Vf.RenameOkFallback");
        }

        private static string ResolveRemoveLabel()
        {
            return CapturedLabel(RemoveCaptureIndex, "RimWorldAccess.Compat.Vf.RenameRemoveFallback");
        }

        private static string CapturedLabel(int captureIndex, string fallbackKey)
        {
            if (ButtonTextCapture.Items.Count > captureIndex)
            {
                string label = ButtonTextCapture.Items[captureIndex].Label;
                if (!string.IsNullOrEmpty(label))
                {
                    return label;
                }
            }
            return fallbackKey.Translate().ToString();
        }
    }
}
