using System;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.DialogChangeBirthday</c>, backing
    /// <see cref="Shell.CharEditorBirthdayScope"/>. Mirrors <see cref="CharEditorBrowserCompat"/>'s
    /// idiom (packageId presence decided by <see cref="CharEditorCompat.ModPresent"/>, every member
    /// resolved behind <see cref="Ready"/>, degrade-not-throw).
    ///
    /// OPENING constructs the dialog directly with the target pawn (vehicle A: the exact call
    /// <c>BlockBio.AAddBirthdayTick</c> makes, CEditor.cs:1929-1933) and adds it to the WindowStack
    /// at <c>WindowLayer.Dialog</c>, matching <c>CharacterEditor.WindowTool.Open</c> byte for byte
    /// (same reasoning as <see cref="CharEditorBrowserCompat.OpenAddTrait"/>).
    ///
    /// FIELD READS/WRITES are RAW field access on the open dialog instance, matching the mod's own
    /// binding: every numeric row is a bare <c>ref int</c> threaded through
    /// <c>Listing_X.AddIntSection</c>, which only ever re-renders the CURRENT field value when no
    /// mouse drag is live (confirmed against the decompile: its slider call
    /// <c>value = (int)Widgets.HorizontalSlider(rect, value, min, max)</c> is a pure read-render,
    /// never a self-mutation) -- so a direct field write between frames is safe and is picked up
    /// verbatim the next time the dialog's own (still-running) <c>DoWindowContents</c> draws it.
    /// There is no setter method to ride instead; every write here is MUTATION-C, mirroring the
    /// mod's own numeric-field commit exactly as S1b's age fields already do.
    ///
    /// <c>DoAndClose</c> has NO <c>OnAcceptKeyPressed</c> override (confirmed absent from the
    /// decompile, unlike every other dialog wrapped here) -- Enter never reaches it on the
    /// mod's own window, only the mouse OK button does. <see cref="Confirm"/> therefore invokes the
    /// private method directly via reflection rather than routing through
    /// <c>Window.OnAcceptKeyPressed</c>. <see cref="Cancel"/> calls the PUBLIC, inherited
    /// <c>Window.OnCancelKeyPressed()</c> directly (no reflection needed) -- the dialog sets
    /// <c>closeOnCancel = true</c> in its constructor with no override, so the base behavior closes
    /// it.
    /// </summary>
    internal static class CharEditorBirthdayCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorBirthdayCompat", Resolve);

        private static Type dialogType;
        private static ConstructorInfo ctor;

        private static FieldInfo yearField;
        private static FieldInfo quadrumField;
        private static FieldInfo dayField;
        private static FieldInfo hourField;
        private static FieldInfo bioYearField;
        private static FieldInfo bioQuadrumField;
        private static FieldInfo bioDayField;
        private static FieldInfo bioHourField;
        private static FieldInfo lifestageField;
        private static FieldInfo maxLifestageField;
        private static MethodInfo doAndCloseMethod;

        // The mod's own eight-language Label table -- read live rather than re-translated, per
        // doctrine ("the mod's own labels are read LIVE from ... its Label class").
        private static Type labelType;
        private static FieldInfo labelYearField;
        private static FieldInfo labelQuadrumField;
        private static FieldInfo labelDayField;
        private static FieldInfo labelHourField;
        private static FieldInfo labelYearsField;
        private static FieldInfo labelQuartalsField;
        private static FieldInfo labelDaysField;
        private static FieldInfo labelHoursField;
        private static FieldInfo labelLifestageField;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get
            {
                return gate.Ensure();
            }
        }

        public static Type DialogType
        {
            get { gate.Ensure(); return dialogType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            dialogType = surface.Type("CharacterEditor.DialogChangeBirthday");
            labelType = surface.Supplied("CharacterEditor.Label", CharEditorCompat.Labels.LabelType);

            ctor = surface.Constructor(dialogType, new[] { typeof(Pawn) });
            yearField = surface.Field(dialogType, "iSelectedYear");
            quadrumField = surface.Field(dialogType, "iSelctedQuadrum"); // mod's own typo, preserved
            dayField = surface.Field(dialogType, "iSelectedDay");
            hourField = surface.Field(dialogType, "iSelectedHour");
            bioYearField = surface.Field(dialogType, "iSelectedBioYear");
            bioQuadrumField = surface.Field(dialogType, "iSelectedBioQuadrum");
            bioDayField = surface.Field(dialogType, "iSelectedBioDay");
            bioHourField = surface.Field(dialogType, "iSelectedBioHour");
            lifestageField = surface.Field(dialogType, "iSelectedLifestage");
            maxLifestageField = surface.Field(dialogType, "iMaxLifestage");
            doAndCloseMethod = surface.Method(dialogType, "DoAndClose", Type.EmptyTypes);

            labelYearField = surface.Field(labelType, "YEAR");
            labelQuadrumField = surface.Field(labelType, "QUADRUM");
            labelDayField = surface.Field(labelType, "DAY");
            labelHourField = surface.Field(labelType, "HOUR");
            labelYearsField = surface.Field(labelType, "YEARS");
            labelQuartalsField = surface.Field(labelType, "QUARTALS");
            labelDaysField = surface.Field(labelType, "DAYS");
            labelHoursField = surface.Field(labelType, "HOURS");
            labelLifestageField = surface.Field(labelType, "LIFESTAGE");

            return surface.Ready && CharEditorCompat.Labels.Ready;
        }

        private static readonly System.Collections.Generic.HashSet<string> loggedFailures = new System.Collections.Generic.HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorBirthdayCompat." + member + " failed: " + ex.Message);
        }

        public static string YearLabel => LabelValue(labelYearField);
        public static string QuadrumLabel => LabelValue(labelQuadrumField);
        public static string DayLabel => LabelValue(labelDayField);
        public static string HourLabel => LabelValue(labelHourField);
        public static string YearsLabel => LabelValue(labelYearsField);
        public static string QuartalsLabel => LabelValue(labelQuartalsField);
        public static string DaysLabel => LabelValue(labelDaysField);
        public static string HoursLabel => LabelValue(labelHoursField);
        public static string LifestageLabel => LabelValue(labelLifestageField);

        private static string LabelValue(FieldInfo field)
        {
            if (!Ready || field == null)
                return "";
            try
            {
                return field.GetValue(null) as string ?? "";
            }
            catch (Exception ex)
            {
                Fail("Label:" + field.Name, ex);
                return "";
            }
        }

        /// <summary>Opens the dialog for <paramref name="pawn"/> -- the exact vehicle BlockBio.AAddBirthdayTick uses.</summary>
        public static Window Open(Pawn pawn)
        {
            if (!Ready || pawn == null)
                return null;
            try
            {
                var window = (Window)ctor.Invoke(new object[] { pawn });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
                return window;
            }
            catch (Exception ex)
            {
                Fail("Open", ex);
                return null;
            }
        }

        public static int GetYear(Window dlg) => GetInt(yearField, dlg, "GetYear");
        public static void SetYear(Window dlg, int v) => SetInt(yearField, dlg, v, "SetYear");
        public static int GetQuadrum(Window dlg) => GetInt(quadrumField, dlg, "GetQuadrum");
        public static void SetQuadrum(Window dlg, int v) => SetInt(quadrumField, dlg, v, "SetQuadrum");
        public static int GetDay(Window dlg) => GetInt(dayField, dlg, "GetDay");
        public static void SetDay(Window dlg, int v) => SetInt(dayField, dlg, v, "SetDay");
        public static int GetHour(Window dlg) => GetInt(hourField, dlg, "GetHour");
        public static void SetHour(Window dlg, int v) => SetInt(hourField, dlg, v, "SetHour");

        public static int GetBioYear(Window dlg) => GetInt(bioYearField, dlg, "GetBioYear");
        public static void SetBioYear(Window dlg, int v) => SetInt(bioYearField, dlg, v, "SetBioYear");
        public static int GetBioQuadrum(Window dlg) => GetInt(bioQuadrumField, dlg, "GetBioQuadrum");
        public static void SetBioQuadrum(Window dlg, int v) => SetInt(bioQuadrumField, dlg, v, "SetBioQuadrum");
        public static int GetBioDay(Window dlg) => GetInt(bioDayField, dlg, "GetBioDay");
        public static void SetBioDay(Window dlg, int v) => SetInt(bioDayField, dlg, v, "SetBioDay");
        public static int GetBioHour(Window dlg) => GetInt(bioHourField, dlg, "GetBioHour");
        public static void SetBioHour(Window dlg, int v) => SetInt(bioHourField, dlg, v, "SetBioHour");

        /// <summary>
        /// The number of life stages beyond the first (0 means the race has exactly one stage, in
        /// which case the mod draws no life-stage row at all).
        /// </summary>
        public static int MaxLifestage(Window dlg) => GetInt(maxLifestageField, dlg, "MaxLifestage");

        /// <summary>
        /// The mod re-syncs this field to the pawn's ACTUAL current life stage every frame right
        /// before reading it, so read fresh on every describe rather than
        /// trusting a cached value, matching that behavior instead of fighting it.
        /// </summary>
        public static int GetLifestage(Window dlg) => GetInt(lifestageField, dlg, "GetLifestage");
        public static void SetLifestage(Window dlg, int v) => SetInt(lifestageField, dlg, v, "SetLifestage");

        private static int GetInt(FieldInfo field, Window dlg, string caller)
        {
            if (!Ready || dlg == null)
                return 0;
            try
            {
                object v = field.GetValue(dlg);
                return v is int i ? i : 0;
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
                return 0;
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors DialogChangeBirthday's own numeric rows -- the mod threads each
        /// private int field by ref through Listing_X.AddIntSection, whose stepper/slider writes
        /// the field bare with no setter method or gate to ride; these are dialog-local pending
        /// values that only reach the pawn through the dialog's own DoAndClose.
        /// </summary>
        private static void SetInt(FieldInfo field, Window dlg, int value, string caller)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                field.SetValue(dlg, value);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        /// <summary>Vehicle A: the dialog's own apply-and-close (no OnAcceptKeyPressed override exists -- see class remarks).</summary>
        public static bool Confirm(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                doAndCloseMethod.Invoke(dlg, null);
                return !Find.WindowStack.IsOpen(dlg);
            }
            catch (Exception ex)
            {
                Fail("Confirm", ex);
                return false;
            }
        }
    }
}
