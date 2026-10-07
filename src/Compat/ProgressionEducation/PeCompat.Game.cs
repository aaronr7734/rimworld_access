using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only surface for Progression: Education — the education manager and its study
    /// groups/classrooms, the class dialogs' role-assignment plumbing, and the proficiency
    /// system. The class roles, candidate pool, and assignment manager implement VANILLA
    /// interfaces (<see cref="ILordJobRole"/>, <see cref="ILordJobCandidatePool"/>,
    /// <c>ILordJobAssignmentsManager&lt;ClassRole&gt;</c>): typed casts where the interface is
    /// non-generic, this facade's MethodInfos where it is not.
    /// </summary>
    internal static class PeCompat
    {
        private static readonly Type managerType;
        private static readonly Type studyGroupType;
        private static readonly Type classroomType;
        private static readonly Type subjectLogicType;
        private static readonly Type skillClassLogicType;
        private static readonly Type iClassDialogType;
        private static readonly Type assignmentsManagerType;
        private static readonly Type dialogCreateType;
        private static readonly Type dialogEditType;
        private static readonly Type mainTabType;
        private static readonly Type classroomSettingsDialogType;
        private static readonly Type renameClassroomDialogType;
        private static readonly Type colorPickerWindowType;
        private static readonly Type educationUtilityType;
        private static readonly Type roleWidgetType;

        private static readonly PropertyInfo managerInstanceProp;
        private static readonly PropertyInfo managerStudyGroupsProp;
        private static readonly PropertyInfo managerClassroomsProp;
        private static readonly MethodInfo removeStudyGroupMethod;

        private static readonly FieldInfo sgClassNameField;
        private static readonly FieldInfo sgClassroomField;
        private static readonly FieldInfo sgSubjectLogicField;
        private static readonly FieldInfo sgSuspendedField;
        private static readonly FieldInfo sgTeacherField;
        private static readonly FieldInfo sgStudentsField;
        private static readonly FieldInfo sgStartHourField;
        private static readonly FieldInfo sgEndHourField;
        private static readonly FieldInfo sgCurrentProgressField;
        private static readonly FieldInfo sgSemesterGoalField;
        private static readonly PropertyInfo sgProgressPercentageProp;
        private static readonly MethodInfo sgSuspendMethod;
        private static readonly MethodInfo sgGetStudentRoleMethod;

        private static readonly FieldInfo classroomNameField;
        private static readonly FieldInfo classroomColorField;
        private static readonly FieldInfo classroomBoardThingField;
        private static readonly PropertyInfo classroomClassSpeedProp;

        private static readonly PropertyInfo logicLabelCapProp;
        private static readonly PropertyInfo logicDescriptionProp;
        private static readonly PropertyInfo logicIsInfiniteProp;
        private static readonly PropertyInfo logicShowAttendanceProp;
        private static readonly PropertyInfo logicBenchCountProp;

        private static readonly PropertyInfo dlgAssignmentsManagerProp;
        private static readonly PropertyInfo dlgCandidatePoolProp;
        private static readonly PropertyInfo dlgStudentRoleProp;
        private static readonly PropertyInfo dlgTeacherRoleProp;
        private static readonly FieldInfo createParticipantsDrawerField;
        private static readonly FieldInfo editParticipantsDrawerField;
        private static readonly MethodInfo widgetExtraTipContentsMethod;

        private static readonly PropertyInfo amRolesProp;
        private static readonly MethodInfo amAssignedPawnsMethod;
        private static readonly MethodInfo amTryAssignMethod;
        private static readonly MethodInfo amTryUnassignAnyRoleMethod;
        private static readonly MethodInfo amPawnNotAssignableReasonMethod;
        private static readonly MethodInfo amRoleForPawnMethod;

        private static readonly ConstructorInfo dialogCreateCtor;
        private static readonly ConstructorInfo dialogEditCtor;
        private static readonly ConstructorInfo classroomSettingsCtor;
        private static readonly ConstructorInfo renameClassroomCtor;
        private static readonly ConstructorInfo colorPickerCtor;
        private static readonly MethodInfo hasBellOnMapMethod;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type DialogCreateType => dialogCreateType;
        public static Type DialogEditType => dialogEditType;
        public static Type MainTabType => mainTabType;

        static PeCompat()
        {
            var surface = new ReflectionSurface("PeCompat");

            managerType = surface.Type("ProgressionEducation.EducationManager");
            studyGroupType = surface.Type("ProgressionEducation.StudyGroup");
            classroomType = surface.Type("ProgressionEducation.Classroom");
            subjectLogicType = surface.Type("ProgressionEducation.ClassSubjectLogic");
            skillClassLogicType = surface.Type("ProgressionEducation.SkillClassLogic");
            iClassDialogType = surface.Type("ProgressionEducation.IClassDialog");
            assignmentsManagerType = surface.Type("ProgressionEducation.ClassAssignmentsManager");
            dialogCreateType = surface.Type("ProgressionEducation.Dialog_CreateClass");
            dialogEditType = surface.Type("ProgressionEducation.Dialog_EditClass");
            mainTabType = surface.Type("ProgressionEducation.MainTabWindow_Education");
            classroomSettingsDialogType = surface.Type("ProgressionEducation.Dialog_ClassroomSettings");
            renameClassroomDialogType = surface.Type("ProgressionEducation.Dialog_RenameClassroom");
            colorPickerWindowType = surface.Type("ProgressionEducation.Window_ColorPicker");
            educationUtilityType = surface.Type("ProgressionEducation.EducationUtility");
            roleWidgetType = surface.Type("ProgressionEducation.PawnClassRoleSelectionWidget");

            managerInstanceProp = surface.Property(managerType, "Instance");
            managerStudyGroupsProp = surface.Property(managerType, "StudyGroups");
            managerClassroomsProp = surface.Property(managerType, "Classrooms");
            removeStudyGroupMethod = surface.Method(managerType, "RemoveStudyGroup");

            sgClassNameField = surface.Field(studyGroupType, "className");
            sgClassroomField = surface.Field(studyGroupType, "classroom");
            sgSubjectLogicField = surface.Field(studyGroupType, "subjectLogic");
            sgSuspendedField = surface.Field(studyGroupType, "suspended");
            sgTeacherField = surface.Field(studyGroupType, "teacher");
            sgStudentsField = surface.Field(studyGroupType, "students");
            sgStartHourField = surface.Field(studyGroupType, "startHour");
            sgEndHourField = surface.Field(studyGroupType, "endHour");
            sgCurrentProgressField = surface.Field(studyGroupType, "currentProgress");
            sgSemesterGoalField = surface.Field(studyGroupType, "semesterGoal");
            sgProgressPercentageProp = surface.Property(studyGroupType, "ProgressPercentage");
            sgSuspendMethod = surface.Method(studyGroupType, "Suspend");
            sgGetStudentRoleMethod = surface.Method(studyGroupType, "GetStudentRole");

            classroomNameField = surface.Field(classroomType, "name");
            classroomColorField = surface.Field(classroomType, "color");
            classroomBoardThingField = surface.Field(classroomType, "learningBoardThing");
            classroomClassSpeedProp = surface.Property(classroomType, "ClassSpeed");

            logicLabelCapProp = surface.Property(subjectLogicType, "LabelCap");
            logicDescriptionProp = surface.Property(subjectLogicType, "Description");
            logicIsInfiniteProp = surface.Property(subjectLogicType, "IsInfinite");
            logicShowAttendanceProp = surface.Property(subjectLogicType, "ShowAttendance");
            logicBenchCountProp = surface.Property(subjectLogicType, "BenchCount");

            dlgAssignmentsManagerProp = surface.Property(iClassDialogType, "AssignmentsManager");
            dlgCandidatePoolProp = surface.Property(iClassDialogType, "CandidatePool");
            dlgStudentRoleProp = surface.Property(iClassDialogType, "StudentRole");
            dlgTeacherRoleProp = surface.Property(iClassDialogType, "TeacherRole");
            createParticipantsDrawerField = surface.Field(dialogCreateType, "participantsDrawer");
            editParticipantsDrawerField = surface.Field(dialogEditType, "participantsDrawer");
            widgetExtraTipContentsMethod = surface.Method(roleWidgetType, "ExtraTipContents");

            amRolesProp = surface.Property(assignmentsManagerType, "Roles");
            amAssignedPawnsMethod = surface.Method(assignmentsManagerType, "AssignedPawns");
            amTryAssignMethod = surface.Method(assignmentsManagerType, "TryAssign");
            amTryUnassignAnyRoleMethod = surface.Method(assignmentsManagerType, "TryUnassignAnyRole");
            amPawnNotAssignableReasonMethod = surface.Method(assignmentsManagerType, "PawnNotAssignableReason");
            amRoleForPawnMethod = surface.Method(assignmentsManagerType, "RoleForPawn");

            dialogCreateCtor = surface.Constructor(dialogCreateType, new[] { typeof(Map) });
            dialogEditCtor = surface.Constructor(dialogEditType, new[] { studyGroupType });
            classroomSettingsCtor = surface.Constructor(classroomSettingsDialogType, new[] { classroomType });
            renameClassroomCtor = surface.Constructor(renameClassroomDialogType, new[] { classroomType });
            colorPickerCtor = surface.Constructor(colorPickerWindowType, new[] { typeof(Color), typeof(Action<Color>) });
            hasBellOnMapMethod = surface.Method(educationUtilityType, "HasBellOnMap");

            ready = surface.Ready;
        }

        // ------------------------------------------------------------------
        // Manager reads.
        // ------------------------------------------------------------------

        private static object Manager()
        {
            return ready ? managerInstanceProp.GetValue(null) : null;
        }

        public static List<object> StudyGroups()
        {
            return BoxedList(Manager(), managerStudyGroupsProp);
        }

        public static List<object> Classrooms()
        {
            return BoxedList(Manager(), managerClassroomsProp);
        }

        private static List<object> BoxedList(object instance, PropertyInfo prop)
        {
            var result = new List<object>();
            if (instance == null)
            {
                return result;
            }
            if (prop.GetValue(instance) is IEnumerable list)
            {
                foreach (object item in list)
                {
                    result.Add(item);
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Study group reads.
        // ------------------------------------------------------------------

        public static string ClassName(object sg) => sgClassNameField.GetValue(sg) as string ?? "";
        public static object ClassroomOf(object sg) => sgClassroomField.GetValue(sg);
        public static object SubjectLogicOf(object sg) => sgSubjectLogicField.GetValue(sg);
        public static bool Suspended(object sg) => (bool)sgSuspendedField.GetValue(sg);
        public static Pawn TeacherOf(object sg) => sgTeacherField.GetValue(sg) as Pawn;
        public static int StudentCount(object sg) => (sgStudentsField.GetValue(sg) as IList)?.Count ?? 0;
        public static int StartHour(object sg) => (int)sgStartHourField.GetValue(sg);
        public static int EndHour(object sg) => (int)sgEndHourField.GetValue(sg);
        public static float CurrentProgress(object sg) => (float)sgCurrentProgressField.GetValue(sg);
        public static int SemesterGoal(object sg) => (int)sgSemesterGoalField.GetValue(sg);
        public static float ProgressPercentage(object sg) => (float)sgProgressPercentageProp.GetValue(sg);

        public static void SetSuspended(object sg, bool suspend)
        {
            sgSuspendMethod.Invoke(sg, new object[] { suspend });
        }

        public static void RemoveStudyGroup(object sg)
        {
            object manager = Manager();
            if (manager != null)
            {
                removeStudyGroupMethod.Invoke(manager, new[] { sg });
            }
        }

        public static ILordJobRole StudentRoleOfGroup(object sg)
        {
            return sgGetStudentRoleMethod.Invoke(sg, null) as ILordJobRole;
        }

        // ------------------------------------------------------------------
        // Subject logic reads.
        // ------------------------------------------------------------------

        public static string SubjectLabel(object logic) =>
            logic == null ? "" : logicLabelCapProp.GetValue(logic) as string ?? "";
        public static string SubjectDescription(object logic) =>
            logic == null ? "" : logicDescriptionProp.GetValue(logic) as string ?? "";
        public static bool SubjectIsInfinite(object logic) =>
            logic != null && (bool)logicIsInfiniteProp.GetValue(logic);
        public static bool SubjectShowsAttendance(object logic) =>
            logic != null && (bool)logicShowAttendanceProp.GetValue(logic);
        public static int SubjectBenchCount(object logic) =>
            logic == null ? 0 : (int)logicBenchCountProp.GetValue(logic);
        public static bool SubjectIsSkillClass(object logic) =>
            logic != null && skillClassLogicType.IsInstanceOfType(logic);

        // ------------------------------------------------------------------
        // Classroom reads.
        // ------------------------------------------------------------------

        public static string ClassroomName(object c) => classroomNameField.GetValue(c) as string ?? "";
        public static Color ClassroomColor(object c) => (Color)classroomColorField.GetValue(c);
        public static Thing ClassroomBoardThing(object c) => classroomBoardThingField.GetValue(c) as Thing;

        public static float ClassroomSpeed(object c)
        {
            try
            {
                return (float)classroomClassSpeedProp.GetValue(c);
            }
            catch
            {
                // The mod computes speed from the board thing's stat; a classroom whose board
                // was destroyed throws here, and a sighted player just sees no number.
                return 0f;
            }
        }

        // MUTATION-C: mirrors MainTabWindow_Education.DrawClassroomList's color-picker callback
        // (`classroom.color = newColor`); the field has no gated setter.
        public static void SetClassroomColor(object c, Color color)
        {
            classroomColorField.SetValue(c, color);
        }

        // ------------------------------------------------------------------
        // Class dialogs (IClassDialog) and role assignment.
        // ------------------------------------------------------------------

        public static object AssignmentsManagerOf(Window dialog) => dlgAssignmentsManagerProp.GetValue(dialog);
        public static ILordJobCandidatePool CandidatePoolOf(Window dialog) =>
            dlgCandidatePoolProp.GetValue(dialog) as ILordJobCandidatePool;
        public static ILordJobRole StudentRoleOf(Window dialog) =>
            dlgStudentRoleProp.GetValue(dialog) as ILordJobRole;
        public static ILordJobRole TeacherRoleOf(Window dialog) =>
            dlgTeacherRoleProp.GetValue(dialog) as ILordJobRole;

        public static List<Pawn> AssignedPawns(object manager, ILordJobRole role)
        {
            var result = new List<Pawn>();
            if (amAssignedPawnsMethod.Invoke(manager, new object[] { role }) is IEnumerable pawns)
            {
                foreach (object p in pawns)
                {
                    if (p is Pawn pawn)
                    {
                        result.Add(pawn);
                    }
                }
            }
            return result;
        }

        public static ILordJobRole RoleForPawn(object manager, Pawn pawn)
        {
            return amRoleForPawnMethod.Invoke(manager, new object[] { pawn, true }) as ILordJobRole;
        }

        public static string NotAssignableReason(object manager, Pawn pawn, ILordJobRole role)
        {
            return amPawnNotAssignableReasonMethod.Invoke(manager, new object[] { pawn, role }) as string;
        }

        /// <summary>The role widget's own portrait-tooltip body; null without a classroom, which its student breakdown dereferences.</summary>
        public static string CandidateTooltip(Window dialog, Pawn pawn)
        {
            FieldInfo drawerField = dialogCreateType.IsInstanceOfType(dialog) ? createParticipantsDrawerField
                : dialogEditType.IsInstanceOfType(dialog) ? editParticipantsDrawerField
                : null;
            object drawer = drawerField?.GetValue(dialog);
            if (drawer == null)
            {
                return null;
            }
            try
            {
                return widgetExtraTipContentsMethod.Invoke(drawer, new object[] { pawn }) as string;
            }
            catch (TargetInvocationException ex)
            {
                ModLogger.LimitedMessage("PE candidate tooltip", ex.InnerException ?? ex);
                return null;
            }
        }

        /// <summary>The manager's own gated assignment, same call the drag-and-drop widget makes.</summary>
        public static bool TryAssign(object manager, Pawn pawn, ILordJobRole role)
        {
            object[] args =
            {
                pawn, role, null,
                PsychicRitualRoleDef.Context.Dialog_BeginPsychicRitual, null,
            };
            return (bool)amTryAssignMethod.Invoke(manager, args);
        }

        public static bool TryUnassign(object manager, Pawn pawn)
        {
            return (bool)amTryUnassignAnyRoleMethod.Invoke(manager, new object[] { pawn });
        }

        // ------------------------------------------------------------------
        // Window openers — each the same window the mod's own buttons open.
        // ------------------------------------------------------------------

        public static void OpenCreateClassDialog(Map map)
        {
            Find.WindowStack.Add((Window)dialogCreateCtor.Invoke(new object[] { map }));
        }

        public static void OpenEditClassDialog(object sg)
        {
            Find.WindowStack.Add((Window)dialogEditCtor.Invoke(new[] { sg }));
        }

        public static void OpenClassroomSettingsDialog(object classroom)
        {
            Find.WindowStack.Add((Window)classroomSettingsCtor.Invoke(new[] { classroom }));
        }

        public static void OpenRenameClassroomDialog(object classroom)
        {
            Find.WindowStack.Add((Window)renameClassroomCtor.Invoke(new[] { classroom }));
        }

        public static void OpenClassroomColorPicker(object classroom)
        {
            Action<Color> apply = delegate (Color c) { SetClassroomColor(classroom, c); };
            Find.WindowStack.Add((Window)colorPickerCtor.Invoke(
                new object[] { ClassroomColor(classroom), apply }));
        }

        public static bool HasBellOnMap(Map map)
        {
            return (bool)hasBellOnMapMethod.Invoke(null, new object[] { map, false });
        }
    }
}
