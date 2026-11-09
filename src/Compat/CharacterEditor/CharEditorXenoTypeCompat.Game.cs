using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over the members <c>CharacterEditor.DialogXenoType</c> declares ITSELF
    /// (does NOT re-derive members already shared through <c>RimWorld.GeneCreationDialogBase</c>,
    /// which <see cref="RimWorldAccess.XenotypeReflection"/> already resolves once and which apply to
    /// ANY subclass instance including this one -- <c>xenotypeName</c>, <c>xenotypeNameLocked</c>,
    /// <c>ignoreRestrictions</c>, <c>OnGenesChanged</c>, <c>iconDef</c>, <c>gcx</c>/<c>met</c>/
    /// <c>arc</c>/<c>maxGCX</c>). Backs <see cref="RimWorldAccess.CharEditorXenoTypeState"/>, mirroring
    /// how <see cref="RimWorldAccess.XenotypeReflection"/>/<c>XenotypeEditorState</c> split the vanilla
    /// <c>Dialog_CreateXenotype</c> case.
    ///
    /// The rule this follows: FIRST check whether the shell already supports vanilla
    /// Dialog_CreateXenotype / GeneCreationDialogBase. It does
    /// (<see cref="Shell.XenotypeEditorScope"/>/<see cref="Shell.GeneDialogScopeBase"/>), but
    /// <c>DialogXenoType</c> is a SEPARATE concrete subclass of the shared base -- a SIBLING of
    /// <c>Dialog_CreateXenotype</c>, not a child of it -- confirmed directly against both decompiles:
    /// <c>DialogXenoType</c> declares its OWN <c>selectedGenes</c>/<c>inheritable</c>/
    /// <c>ignoreRestrictionsConfirmationSent</c> fields (same names, different declaring types, so
    /// <see cref="XenotypeReflection.InheritableField"/>/<c>IgnoreRestrictionsConfirmationSentField</c>
    /// -- both bound to <c>typeof(Dialog_CreateXenotype)</c> -- would throw
    /// <c>ArgumentException</c> on a <c>DialogXenoType</c> instance) and its OWN
    /// <c>CanAccept()</c>/<c>Accept()</c> overrides (neither resolved here -- see
    /// <see cref="CheckSaveAnd"/>'s own remarks for why). REUSED (see
    /// <see cref="RimWorldAccess.CharEditorXenoTypeState"/>): <see cref="Shell.GeneDialogScopeBase"/>
    /// (the whole Selected/Library/Controls region shape, typeahead, Alt+I info card, tree jumps),
    /// <see cref="RimWorldAccess.XenotypeTreeBuilder"/> (pure functions over <c>List&lt;GeneDef&gt;</c>,
    /// not tied to any concrete dialog type), and every <see cref="XenotypeReflection"/> field bound to
    /// <c>GeneCreationDialogBase</c> itself. ADDED (this file plus the state/scope files): only what
    /// <c>DialogXenoType</c> changes.
    ///
    /// THE ICON SELECTOR, XENOTYPE NAME FIELD, NAME LOCK, RANDOMIZE, AND "..." NAME-SUGGESTION BUTTON
    /// are NOT overridden by <c>DialogXenoType</c> at all -- confirmed against the decompile,
    /// <c>DialogXenoType</c> overrides only <c>DrawGenes</c>/<c>PostXenotypeOnGUI</c>/
    /// <c>DrawSearchRect</c>/<c>DoBottomButtons</c>/<c>CanAccept</c>/<c>Accept</c>/
    /// <c>OnGenesChanged</c>/<c>UpdateSearchResults</c>; the top-level layout including the icon
    /// selector button (<c>GeneCreationDialogBase.DrawIconSelector</c>, opens the vanilla
    /// <c>Dialog_SelectXenotypeIcon</c> unmodified) comes ENTIRELY from the base class's own
    /// <c>DoWindowContents</c>. <see cref="RimWorldAccess.CharEditorXenoTypeState"/> therefore reuses
    /// <see cref="Shell.XenotypeEditorScope"/>'s own icon/rename/lock/randomize logic verbatim rather
    /// than re-deriving it -- see that state class's own remarks.
    ///
    /// A THIRD BOTTOM BUTTON, vanilla <c>Dialog_CreateXenotype</c> does not have: DialogXenoType's own
    /// <c>DoBottomButtons</c> draws Save-and-Apply, Close, AND Save (no-apply) -- vanilla's own
    /// <c>Accept()</c> always both saves to file and applies (<c>callback?.Invoke()</c>); there is no
    /// no-apply path on the vanilla dialog at all. <see cref="CheckSaveAnd"/> below (private
    /// <c>ACheckSaveAnd(bool)</c>, resolved against <c>CharacterEditor.DialogXenoType</c> directly)
    /// is the vehicle for BOTH buttons: Save-and-Apply calls it with <c>apply: true</c>, Save calls
    /// it with <c>apply: false</c>, which the CharacterEditor mod itself has no other entry point
    /// for. Neither <c>CanAccept()</c> nor <c>Accept()</c> is resolved by this facade at all --
    /// <c>ACheckSaveAnd</c> already calls the dialog's own <c>CanAccept()</c> internally before
    /// acting, so it is a stronger (single-call) vehicle A than reproducing that pairing manually.
    /// </summary>
    internal static class CharEditorXenoTypeCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorXenoTypeCompat", Resolve);

        private static Type dialogType;

        private static ConstructorInfo ctor;
        private static FieldInfo pawnField;
        private static FieldInfo selectedGenesField;
        private static FieldInfo inheritableField;
        private static FieldInfo ignoreRestrictionsConfirmationSentField;
        private static MethodInfo aCheckSaveAndMethod;
        private static MethodInfo aLoadCustomXenotypeMethod;
        private static MethodInfo aLoadXenotypeDefMethod;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get { gate.Ensure(); return gate.Ensure(); }
        }

        public static Type DialogType
        {
            get { gate.Ensure(); return dialogType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            // Anchor: this facade resolves ONE type, so without a type known to be present a
            // missing DialogXenoType would read as "mod absent" and decline silently.
            surface.Supplied("CharacterEditor.CEditor", AccessTools.TypeByName("CharacterEditor.CEditor"));

            dialogType = surface.Type("CharacterEditor.DialogXenoType");

            ctor = surface.Constructor(dialogType, new[] { typeof(Pawn) });
            pawnField = surface.Field(dialogType, "pawn");
            selectedGenesField = surface.Field(dialogType, "selectedGenes");
            inheritableField = surface.Field(dialogType, "inheritable");
            ignoreRestrictionsConfirmationSentField = surface.Field(dialogType, "ignoreRestrictionsConfirmationSent");
            aCheckSaveAndMethod = surface.Method(dialogType, "ACheckSaveAnd", new[] { typeof(bool) });
            aLoadCustomXenotypeMethod = surface.Method(dialogType, "ALoadCustomXenotype", new[] { typeof(CustomXenotype) });
            aLoadXenotypeDefMethod = surface.Method(dialogType, "ALoadXenotypeDef", new[] { typeof(XenotypeDef) });

            return surface.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorXenoTypeCompat." + member + " failed: " + ex.Message);
        }

        public static Pawn TargetPawn(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return pawnField.GetValue(dlg) as Pawn;
            }
            catch (Exception ex)
            {
                Fail("TargetPawn", ex);
                return null;
            }
        }

        public static List<GeneDef> GetSelectedGenes(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return selectedGenesField.GetValue(dlg) as List<GeneDef>;
            }
            catch (Exception ex)
            {
                Fail("GetSelectedGenes", ex);
                return null;
            }
        }

        public static bool IsInheritable(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                return (bool)inheritableField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("IsInheritable", ex);
                return false;
            }
        }

        /// <summary>MUTATION-C: mirrors PostXenotypeOnGUI's own inline checkbox write (DialogXenoType.cs:433, `Widgets.CheckboxLabeled(rect, taggedString, ref inheritable);`) -- no setter method exists to ride.</summary>
        public static void SetInheritable(Window dlg, bool value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                inheritableField.SetValue(dlg, value);
            }
            catch (Exception ex)
            {
                Fail("SetInheritable", ex);
            }
        }

        public static bool IgnoreRestrictionsConfirmationSent()
        {
            if (!Ready)
                return false;
            try
            {
                return (bool)ignoreRestrictionsConfirmationSentField.GetValue(null);
            }
            catch (Exception ex)
            {
                Fail("IgnoreRestrictionsConfirmationSent", ex);
                return false;
            }
        }

        /// <summary>MUTATION-C: this dialog's OWN one-time-ever marker (DialogXenoType.cs:26/447-449), separate from vanilla Dialog_CreateXenotype's own copy of the same idiom.</summary>
        public static void SetIgnoreRestrictionsConfirmationSent(bool value)
        {
            if (!Ready)
                return;
            try
            {
                ignoreRestrictionsConfirmationSentField.SetValue(null, value);
            }
            catch (Exception ex)
            {
                Fail("SetIgnoreRestrictionsConfirmationSent", ex);
            }
        }

        /// <summary>
        /// Vehicle A: DialogXenoType.ACheckSaveAnd(bool) -- the EXACT delegate BOTH of the mod's own
        /// bottom buttons invoke (DoBottomButtons: Save-and-Apply calls this with apply:true, Save
        /// calls it with apply:false; confirmed against the decompile -- neither button routes
        /// through the base class's CanAccept()/Accept() override pair at all). The gate
        /// (CanAccept()) and the act (ASaveAnd -> AcceptInner) both live inside this ONE reflected
        /// call, so no separate Can*/Act pairing is needed at this facade's call sites -- matching
        /// CLAUDE.md's "invoke the vanilla widget/delegate itself" vehicle A shape directly, a
        /// stronger form than the vanilla XenotypeEditorState.SaveAndApply's own manually-paired
        /// CanAccept-then-Accept (there, no single button delegate exists to ride instead).
        /// </summary>
        public static void CheckSaveAnd(Window dlg, bool apply)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aCheckSaveAndMethod.Invoke(dlg, new object[] { apply });
            }
            catch (Exception ex)
            {
                Fail("CheckSaveAnd", ex);
            }
        }

        /// <summary>Vehicle A: DialogXenoType.ALoadCustomXenotype(CustomXenotype) -- the mod's own private handler, invoked directly rather than reimplemented (TRUE vehicle A, possible because this dialog exposes a real named method to ride; vanilla's Dialog_CreateXenotype load callbacks are inline delegates and need MUTATION-C mirrors instead).</summary>
        public static void LoadCustomXenotype(Window dlg, CustomXenotype xenotype)
        {
            if (!Ready || dlg == null || xenotype == null)
                return;
            try
            {
                aLoadCustomXenotypeMethod.Invoke(dlg, new object[] { xenotype });
            }
            catch (Exception ex)
            {
                Fail("LoadCustomXenotype", ex);
            }
        }

        /// <summary>Vehicle A: DialogXenoType.ALoadXenotypeDef(XenotypeDef) -- same reasoning as LoadCustomXenotype.</summary>
        public static void LoadXenotypeDef(Window dlg, XenotypeDef xenotype)
        {
            if (!Ready || dlg == null || xenotype == null)
                return;
            try
            {
                aLoadXenotypeDefMethod.Invoke(dlg, new object[] { xenotype });
            }
            catch (Exception ex)
            {
                Fail("LoadXenotypeDef", ex);
            }
        }
    }
}
