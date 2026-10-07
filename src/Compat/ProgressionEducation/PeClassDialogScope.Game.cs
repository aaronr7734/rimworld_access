using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Progression: Education's <c>Dialog_CreateClass</c> and
    /// <c>Dialog_EditClass</c>, registered by <see cref="PeModule"/>; all reflection lives in
    /// <see cref="PeCompat"/>. The dialogs' form controls and read-only lines are ordinary
    /// captured widgets, so the extras region presents and operates them with no bespoke work,
    /// including controls other Progression mods contribute.
    ///
    /// What capture cannot see is the mod's role-assignment grid: a copy of vanilla's
    /// <c>PawnRoleSelectionWidgetBase</c> driven by drag-and-drop and raw mouse events. Its data
    /// model implements vanilla interfaces, so this scope presents one content region per role
    /// with a checkbox row per candidate, toggling through the manager's own gated
    /// <c>TryAssign</c>/<c>TryUnassignAnyRole</c> — the same calls its click handlers make.
    /// </summary>
    internal sealed class PeClassDialogScope : ScreenScope
    {
        private readonly Window dialog;
        private readonly List<ILordJobRole> roles = new List<ILordJobRole>();
        private readonly List<Pawn> candidates = new List<Pawn>();

        public PeClassDialogScope(Window w)
        {
            dialog = w;
        }

        public override string Name => "pe-class-dialog";

        protected internal override Window OwnedWindow => dialog;

        protected override bool EnableTypeahead => true;

        /// <summary>All the window's ButtonTexts are form controls or the Cancel/commit pair the
        /// extras region already presents in place; blanket Buttons-region capture would collect
        /// the subject/classroom/hour dropdowns as if they were window chrome.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override bool IncludeCapturedExtrasRegion => true;

        protected override bool ExtrasIncludeReadOnlyRows => true;

        /// <summary>The role widget's own mouse instruction. Assignment here is the checkbox rows
        /// above, so the line reads as advice the keyboard cannot follow; vanilla draws it from its
        /// own key, which is what this compares against.</summary>
        protected override bool ExcludeFromCapturedExtras(CapturedWidget widget)
        {
            return widget.Kind == WidgetKind.Label
                && string.Equals(widget.Label,
                    "DragPawnsToRolesInfo".Translate().ToString(), StringComparison.Ordinal);
        }

        protected override void RefreshContent()
        {
            roles.Clear();
            candidates.Clear();
            ILordJobRole teacher = PeCompat.TeacherRoleOf(dialog);
            ILordJobRole student = PeCompat.StudentRoleOf(dialog);
            if (teacher != null)
            {
                roles.Add(teacher);
            }
            if (student != null)
            {
                roles.Add(student);
            }
            ILordJobCandidatePool pool = PeCompat.CandidatePoolOf(dialog);
            if (pool != null)
            {
                candidates.AddRange(pool.AllCandidatePawns);
            }
        }

        protected override int ContentRegionCount => roles.Count;

        protected override string ContentRegionName(int region)
        {
            ILordJobRole role = roles[region];
            object manager = PeCompat.AssignmentsManagerOf(dialog);
            int assigned = manager == null ? 0 : PeCompat.AssignedPawns(manager, role).Count;
            return "RimWorldAccess.Compat.Pe.RoleRegion".Translate(
                role.LabelCap, assigned, role.MaxCount);
        }

        protected override int ContentItemCount(int region)
        {
            return candidates.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= candidates.Count)
            {
                return d;
            }
            Pawn pawn = candidates[index];
            ILordJobRole role = roles[region];
            object manager = PeCompat.AssignmentsManagerOf(dialog);
            if (manager == null)
            {
                return d;
            }

            d.Label = pawn.LabelShortCap;
            d.Role = ElementRole.Checkbox;
            ILordJobRole current = PeCompat.RoleForPawn(manager, pawn);
            bool inThisRole = current != null && ReferenceEquals(current, role);
            d.Check = inThisRole ? CheckState.Checked : CheckState.Unchecked;
            if (!inThisRole)
            {
                if (current != null)
                {
                    d.Extras = "RimWorldAccess.Compat.Pe.AssignedElsewhere".Translate(current.LabelCap);
                }
                else
                {
                    string reason = PeCompat.NotAssignableReason(manager, pawn, role);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        d.Disabled = true;
                        d.Extras = CompatText.Flatten(reason);
                    }
                }
            }
            d.Extras = new AnnouncementBuilder()
                .Add(d.Extras?.TrimEnd('.'))
                .Add(CompatText.Flatten(PeCompat.CandidateTooltip(dialog, pawn)))
                .Build();
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index < 0 || index >= candidates.Count)
            {
                return;
            }
            Pawn pawn = candidates[index];
            ILordJobRole role = roles[region];
            object manager = PeCompat.AssignmentsManagerOf(dialog);
            if (manager == null)
            {
                return;
            }

            ILordJobRole current = PeCompat.RoleForPawn(manager, pawn);
            if (current != null && ReferenceEquals(current, role))
            {
                if (PeCompat.TryUnassign(manager, pawn))
                {
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    RefreshModel();
                    AnnounceCurrentItem();
                }
                return;
            }

            if (PeCompat.TryAssign(manager, pawn, role))
            {
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            string reason = PeCompat.NotAssignableReason(manager, pawn, role);
            if (string.IsNullOrEmpty(reason))
            {
                TolkHelper.Speak("RimWorldAccess.Compat.Pe.CannotAssign".Loc(
                    pawn.LabelShortCap, role.Label));
            }
            else
            {
                TolkHelper.SpeakData(CompatText.Flatten(reason));
            }
        }
    }

    /// <summary>Both class dialogs submit on ANY Return KeyDown, polled raw in their own body and
    /// so beyond every router and scope claim. Enter belongs to the focused candidate row and the
    /// Create/Save button rides the extras region, so the poll is masked around the vanilla body
    /// while this scope owns the window — the QA R6 mask/restore, never an <c>Use()</c>.</summary>
    internal static class PeClassDialogAcceptGuardPatch
    {
        public static void Install(Harmony harmony, Type windowType)
        {
            if (harmony == null || windowType == null)
            {
                return;
            }
            try
            {
                MethodInfo target = AccessTools.DeclaredMethod(windowType, "DoWindowContents");
                if (target == null)
                {
                    ModLogger.Error("Progression Education compat: could not resolve "
                        + windowType.FullName
                        + ".DoWindowContents; declining the class dialog's Return guard.");
                    return;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(PeClassDialogAcceptGuardPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(PeClassDialogAcceptGuardPatch), nameof(Postfix)));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PE class dialog Return guard", ex);
            }
        }

        public static void Prefix(object __instance)
        {
            try
            {
                var window = __instance as Window;
                TextFieldRawPollGuard.MaskAcceptPoll(
                    window != null && ScopeForWindow.HasAttachedScope(window));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PE class dialog Return guard", ex);
            }
        }

        public static void Postfix()
        {
            try
            {
                TextFieldRawPollGuard.RestoreAcceptPoll();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PE class dialog Return guard", ex);
            }
        }
    }
}
