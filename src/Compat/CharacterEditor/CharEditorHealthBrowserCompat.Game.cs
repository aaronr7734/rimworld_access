using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over the four Health-tab dialogs (<c>DialogAddHediff</c>,
    /// <c>DialogFullheal</c>, <c>DialogChoosePart</c>, <c>DialogChoosePawn</c>), with its OWN
    /// <see cref="Ready"/> flag so a rename in one cannot take down the other Character Editor
    /// browsers. Opening rides BlockHealth's own handlers; this class never constructs a dialog,
    /// and ChoosePart/ChoosePawn are opened by the mod's own <c>CheckIsReady</c> chain.
    ///
    /// THE ACCEPT-KEY TRAP. Three of the four draw OK as a bare
    /// <c>WindowTool.SimpleAcceptButton</c> with no <c>Window.OnAcceptKeyPressed</c> override, so
    /// calling that method runs only the base body and closes the dialog WITHOUT applying the
    /// selection. <c>DialogAddHediff</c> is worse: its override calls <c>DoAndClose()</c> directly,
    /// skipping its own <c>CheckAndDo</c>/<c>CheckIsReady</c> gating and so able to add a hediff
    /// with a null body part that was required. Every Confirm below therefore invokes the dialog's
    /// OWN button delegate — <c>CheckAndDo</c> for AddHediff, <c>DoAndClose</c> for the rest — and
    /// NEVER <c>OnAcceptKeyPressed</c>. Cancel is safe: none of the four override
    /// <c>OnCancelKeyPressed</c> and <c>closeOnCancel</c> defaults true.
    ///
    /// Two next-frame reconciliations are reproduced synchronously here.
    /// <see cref="AddHediffReconcileSelection"/> invokes the dialog's own private
    /// <c>CheckSelectionChanged</c> (vehicle A, the same call <c>DoWindowContents</c> makes every
    /// frame) so every derived field is fresh before the Parameters region reads it.
    /// <c>DialogFullheal</c>'s Flip and All buttons only set one-shot flags its own next draw pass
    /// applies and clears, with no callable method to ride, so
    /// <see cref="FullhealFlipAll"/>/<see cref="FullhealCheckAll"/> reproduce the end state.
    /// The three selection fields are raw and ref-bound with no setter, hence the MUTATION-C
    /// markers on their writes; only AddHediff recomputes other state off its selection.
    /// </summary>
    internal static class CharEditorHealthBrowserCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorHealthBrowserCompat", Resolve);

        private static Type dialogAddHediffType;
        private static Type dialogFullhealType;
        private static Type dialogChoosePartType;
        private static Type dialogChoosePawnType;

        // DialogAddHediff members.
        private static FieldInfo hediffSelectedField;
        private static FieldInfo hediffSearchField;
        private static FieldInfo hediffFilter1CandidatesField;
        private static FieldInfo hediffFilter2CandidatesField;
        private static FieldInfo hediffResultsField;
        private static FieldInfo hediffSeverityField;
        private static FieldInfo hediffLevelField;
        private static FieldInfo hediffPainField;
        private static FieldInfo hediffDurationField;
        private static FieldInfo hediffIsPermanentField;
        private static FieldInfo hediffIsAdjustableField;
        private static FieldInfo hediffInEditModeField;
        private static FieldInfo hediffExampleField;
        private static FieldInfo hediffHDisappearsField;
        private static FieldInfo hediffHPermanentField;
        private static FieldInfo hediffExtraTipStringField;
        private static MethodInfo hediffCheckSelectionChangedMethod;
        private static MethodInfo hediffCheckAndDoMethod;
        private static MethodInfo hediffToggleOverrideMethod;
        private static MethodInfo hediffSelectedModNameMethod; // ASelectedModName(string)
        private static MethodInfo hediffSelectFilter1Method; // ASelectFilter1(string)
        private static MethodInfo hediffSelectBodyPartMethod; // ASelectBodyPartRecord(string)
        private static MethodInfo hediffChangePermanentMethod; // AChangePermanent(bool)

        // CharacterEditor.HealthTool: override flag, mod-name candidates, level check.
        private static Type healthToolTypeLocal;
        private static FieldInfo healthToolOverriddenField;
        private static MethodInfo healthToolConvertSliderToPainCategoryMethod;
        private static MethodInfo healthToolGetMaxSeverityMethod;

        private static Type ceditorTypeLocal;
        private static Type eTypeTypeLocal;
        private static MethodInfo ceditorGetHashSetMethod;
        private static object eTypeModsHediffDef;

        // DialogFullheal members.
        private static FieldInfo fullhealDicToRemoveField;
        private static FieldInfo fullhealListField;
        private static MethodInfo fullhealDoAndCloseMethod;

        // DialogChoosePart members.
        private static FieldInfo choosePartListField;
        private static FieldInfo choosePartSelectedField;
        private static MethodInfo choosePartDoAndCloseMethod;

        // DialogChoosePawn members.
        private static FieldInfo choosePawnListField;
        private static FieldInfo choosePawnSelectedField;
        private static FieldInfo choosePawnSelectedListNameField;
        private static FieldInfo choosePawnCustomTextField;
        private static MethodInfo choosePawnFactionSelectedMethod; // AFactionSelected(string)
        private static MethodInfo choosePawnDoAndCloseMethod;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get
            {
                return gate.Ensure();
            }
        }

        public static Type AddHediffDialogType { get { gate.Ensure(); return dialogAddHediffType; } }
        public static Type FullhealDialogType { get { gate.Ensure(); return dialogFullhealType; } }
        public static Type ChoosePartDialogType { get { gate.Ensure(); return dialogChoosePartType; } }
        public static Type ChoosePawnDialogType { get { gate.Ensure(); return dialogChoosePawnType; } }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            ceditorTypeLocal = surface.Supplied("CharacterEditor.CEditor", CharEditorCompat.EditorCore.CEditorType);
            eTypeTypeLocal = surface.Type("CharacterEditor.EType");
            healthToolTypeLocal = surface.Type("CharacterEditor.HealthTool");

            dialogAddHediffType = surface.Type("CharacterEditor.DialogAddHediff");
            hediffSelectedField = surface.Field(dialogAddHediffType, "selectedHediff");
            hediffSearchField = surface.Field(dialogAddHediffType, "search");
            hediffFilter1CandidatesField = surface.Field(dialogAddHediffType, "lOfFilter1");
            hediffFilter2CandidatesField = surface.Field(dialogAddHediffType, "DicOfFilter2");
            hediffResultsField = surface.Field(dialogAddHediffType, "lOfHediffs");
            hediffSeverityField = surface.Field(dialogAddHediffType, "selectedSeverity");
            hediffLevelField = surface.Field(dialogAddHediffType, "selectedLevel");
            hediffPainField = surface.Field(dialogAddHediffType, "selectedPain");
            hediffDurationField = surface.Field(dialogAddHediffType, "selectedDuration");
            hediffIsPermanentField = surface.Field(dialogAddHediffType, "isPermanent");
            hediffIsAdjustableField = surface.Field(dialogAddHediffType, "isAdjustable");
            hediffInEditModeField = surface.Field(dialogAddHediffType, "inEditMode");
            hediffExampleField = surface.Field(dialogAddHediffType, "example");
            hediffHDisappearsField = surface.Field(dialogAddHediffType, "hDisappears");
            hediffHPermanentField = surface.Field(dialogAddHediffType, "hPermanent");
            hediffExtraTipStringField = surface.Field(dialogAddHediffType, "extraTipString");
            hediffCheckSelectionChangedMethod = surface.Method(dialogAddHediffType, "CheckSelectionChanged", new[] { typeof(Hediff) });
            hediffCheckAndDoMethod = surface.Method(dialogAddHediffType, "CheckAndDo", Type.EmptyTypes);
            hediffToggleOverrideMethod = surface.Method(dialogAddHediffType, "AToggleOverride", Type.EmptyTypes);
            hediffSelectedModNameMethod = surface.Method(dialogAddHediffType, "ASelectedModName", new[] { typeof(string) });
            hediffSelectFilter1Method = surface.Method(dialogAddHediffType, "ASelectFilter1", new[] { typeof(string) });
            hediffSelectBodyPartMethod = surface.Method(dialogAddHediffType, "ASelectBodyPartRecord", new[] { typeof(string) });
            hediffChangePermanentMethod = surface.Method(dialogAddHediffType, "AChangePermanent", new[] { typeof(bool) });

            healthToolOverriddenField = surface.Field(healthToolTypeLocal, "bIsOverridden");
            healthToolConvertSliderToPainCategoryMethod = surface.Method(healthToolTypeLocal, "ConvertSliderToPainCategory", new[] { typeof(int) });
            healthToolGetMaxSeverityMethod = surface.Method(healthToolTypeLocal, "GetMaxSeverity", new[] { typeof(HediffDef) });

            ceditorGetHashSetMethod = surface.Required("CEditor.Get<HashSet<string>>(EType) closed",
                CharEditorCompat.CloseGeneric(ceditorTypeLocal, "Get", typeof(HashSet<string>)));
            eTypeModsHediffDef = surface.EnumValue(eTypeTypeLocal, "ModsHediffDef");

            dialogFullhealType = surface.Type("CharacterEditor.DialogFullheal");
            fullhealDicToRemoveField = surface.Field(dialogFullhealType, "dicToRemove");
            fullhealListField = surface.Field(dialogFullhealType, "lOfHediff");
            fullhealDoAndCloseMethod = surface.Method(dialogFullhealType, "DoAndClose", Type.EmptyTypes);

            dialogChoosePartType = surface.Type("CharacterEditor.DialogChoosePart");
            choosePartListField = surface.Field(dialogChoosePartType, "lOfParts");
            choosePartSelectedField = surface.Field(dialogChoosePartType, "selectedPart");
            choosePartDoAndCloseMethod = surface.Method(dialogChoosePartType, "DoAndClose", Type.EmptyTypes);

            dialogChoosePawnType = surface.Type("CharacterEditor.DialogChoosePawn");
            choosePawnListField = surface.Field(dialogChoosePawnType, "lOfPawns");
            choosePawnSelectedField = surface.Field(dialogChoosePawnType, "selectedPawn");
            choosePawnSelectedListNameField = surface.Field(dialogChoosePawnType, "selectedListname");
            choosePawnCustomTextField = surface.Field(dialogChoosePawnType, "customText");
            choosePawnFactionSelectedMethod = surface.Method(dialogChoosePawnType, "AFactionSelected", new[] { typeof(string) });
            choosePawnDoAndCloseMethod = surface.Method(dialogChoosePawnType, "DoAndClose", Type.EmptyTypes);

            return surface.Ready && CharEditorCompat.EditorCore.Ready && CharEditorCompat.Search.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorHealthBrowserCompat." + member + " failed: " + ex.Message);
        }

        // ---- DialogAddHediff ----

        public static bool HediffInEditMode(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try { return (bool)hediffInEditModeField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffInEditMode", ex); return false; }
        }

        public static HediffDef HediffSelected(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try { return hediffSelectedField.GetValue(dlg) as HediffDef; }
            catch (Exception ex) { Fail("HediffSelected", ex); return null; }
        }

        /// <summary>MUTATION-C: mirrors SZWidgets.ListView's own ref-bound selection write (DialogAddHediff.cs, ListView call site) -- no setter exists.</summary>
        public static void HediffSetSelected(Window dlg, HediffDef def)
        {
            if (!Ready || dlg == null)
                return;
            // MUTATION-C: no setter exists (see the summary above).
            try { hediffSelectedField.SetValue(dlg, def); }
            catch (Exception ex) { Fail("HediffSetSelected", ex); }
        }

        /// <summary>Vehicle A: the SAME reconciliation DoWindowContents calls every frame -- see class remarks.</summary>
        public static void HediffReconcileSelection(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try { hediffCheckSelectionChangedMethod.Invoke(dlg, new object[] { null }); }
            catch (Exception ex) { Fail("HediffReconcileSelection", ex); }
        }

        private static object HediffSearch(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try { return hediffSearchField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffSearch", ex); return null; }
        }

        public static string HediffModName(Window dlg)
        {
            return CharEditorCompat.Search.ModName(HediffSearch(dlg));
        }

        public static string HediffCategory(Window dlg)
        {
            return CharEditorCompat.Search.Filter1(HediffSearch(dlg));
        }

        public static string HediffBodyPartFilter(Window dlg)
        {
            return CharEditorCompat.Search.Filter2(HediffSearch(dlg));
        }

        /// <summary>Every mod-name value the hediff mod-name filter can select -- CEditor.API.Get&lt;HashSet&lt;string&gt;&gt;(EType.ModsHediffDef), the same live container DialogAddHediff's own dropdown reads.</summary>
        public static List<string> HediffModNames()
        {
            var result = new List<string>();
            if (!Ready)
                return result;
            try
            {
                object api = CharEditorCompat.EditorCore.Api();
                if (api == null)
                    return result;
                var set = ceditorGetHashSetMethod.Invoke(api, new[] { eTypeModsHediffDef }) as HashSet<string>;
                if (set != null)
                    result.AddRange(set);
            }
            catch (Exception ex)
            {
                Fail("HediffModNames", ex);
            }
            return result;
        }

        /// <summary>The category filter's fixed candidate list (All, implants, addictions, diseases, injuries, time-based, with-level) -- already-localized display strings baked into the dialog at construction.</summary>
        public static List<string> HediffCategoryCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<string>();
            try { return (hediffFilter1CandidatesField.GetValue(dlg) as List<string>) ?? new List<string>(); }
            catch (Exception ex) { Fail("HediffCategoryCandidates", ex); return new List<string>(); }
        }

        /// <summary>The body-part filter's fixed candidate keys (All, Whole body, then every body part label) -- DicOfFilter2's own keys, already-localized.</summary>
        public static List<string> HediffBodyPartCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<string>();
            try
            {
                var dict = hediffFilter2CandidatesField.GetValue(dlg) as System.Collections.IDictionary;
                var result = new List<string>();
                if (dict != null)
                    foreach (object key in dict.Keys)
                        if (key is string s) result.Add(s);
                return result;
            }
            catch (Exception ex)
            {
                Fail("HediffBodyPartCandidates", ex);
                return new List<string>();
            }
        }

        public static void HediffSetModName(Window dlg, string value)
        {
            InvokeOn(dlg, hediffSelectedModNameMethod, new object[] { value }, "HediffSetModName");
        }

        public static void HediffSetCategory(Window dlg, string value)
        {
            InvokeOn(dlg, hediffSelectFilter1Method, new object[] { value }, "HediffSetCategory");
        }

        public static void HediffSetBodyPartFilter(Window dlg, string value)
        {
            InvokeOn(dlg, hediffSelectBodyPartMethod, new object[] { value }, "HediffSetBodyPartFilter");
        }

        private static void InvokeOn(Window dlg, MethodInfo method, object[] args, string caller)
        {
            if (!Ready || dlg == null || method == null)
                return;
            try { method.Invoke(dlg, args); }
            catch (Exception ex) { Fail(caller, ex); }
        }

        public static List<HediffDef> HediffResults(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<HediffDef>();
            try { return (hediffResultsField.GetValue(dlg) as List<HediffDef>) ?? new List<HediffDef>(); }
            catch (Exception ex) { Fail("HediffResults", ex); return new List<HediffDef>(); }
        }

        /// <summary>Invokes the dialog's own gated OK delegate -- CheckAndDo, NEVER OnAcceptKeyPressed (see class remarks). May chain-open DialogChoosePart/DialogChoosePawn instead of closing.</summary>
        public static void HediffConfirm(Window dlg)
        {
            InvokeOn(dlg, hediffCheckAndDoMethod, null, "HediffConfirm");
        }

        public static void HediffToggleOverride(Window dlg)
        {
            InvokeOn(dlg, hediffToggleOverrideMethod, null, "HediffToggleOverride");
        }

        public static bool HediffOverrideActive()
        {
            if (!Ready)
                return false;
            try { return (bool)healthToolOverriddenField.GetValue(null); }
            catch (Exception ex) { Fail("HediffOverrideActive", ex); return false; }
        }

        public static bool HediffIsAdjustable(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try { return (bool)hediffIsAdjustableField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffIsAdjustable", ex); return false; }
        }

        public static float HediffSeverity(Window dlg)
        {
            if (!Ready || dlg == null)
                return 0f;
            try { return (float)hediffSeverityField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffSeverity", ex); return 0f; }
        }

        /// <summary>MUTATION-C: mirrors DialogAddHediff.DrawAdjustableSeverity's own raw `selectedSeverity` field write (redrawn every frame from the slider) plus its `example.Severity = selectedSeverity;` sync -- no setter exists for either.</summary>
        public static void HediffSetSeverity(Window dlg, float value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                // MUTATION-C: no setter exists (see the summary above).
                hediffSeverityField.SetValue(dlg, value);
                var example = hediffExampleField.GetValue(dlg) as Hediff;
                if (example != null)
                    example.Severity = value;
            }
            catch (Exception ex) { Fail("HediffSetSeverity", ex); }
        }

        public static int HediffLevel(Window dlg)
        {
            if (!Ready || dlg == null)
                return -1;
            try { return (int)hediffLevelField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffLevel", ex); return -1; }
        }

        /// <summary>
        /// MUTATION-C: mirrors DialogAddHediff.DrawAdjustableLevel's own raw `selectedLevel` field
        /// write (redrawn every frame from the stepper) plus HealthTool.SetLevel(Hediff, int) -- a
        /// MOD-INTERNAL extension method (not callable directly; this project has no compile-time
        /// reference to the mod's assembly) reproduced by its two-line body directly against public
        /// vanilla surface instead: <c>Hediff_Level</c> and its public <c>level</c> field are both
        /// vanilla (Verse/Hediff_Level.cs), so this needs no reflection at all.
        /// </summary>
        public static void HediffSetLevel(Window dlg, int value)
        {
            if (!Ready || dlg == null || value < 0)
                return;
            try
            {
                // MUTATION-C: no setter exists (see the summary above).
                hediffLevelField.SetValue(dlg, value);
                var example = hediffExampleField.GetValue(dlg) as Hediff_Level;
                if (example != null)
                {
                    example.level = value;
                    example.Severity = value;
                }
            }
            catch (Exception ex) { Fail("HediffSetLevel", ex); }
        }

        public static int HediffPainSliderValue(Window dlg)
        {
            if (!Ready || dlg == null)
                return -1;
            try { return (int)hediffPainField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffPainSliderValue", ex); return -1; }
        }

        /// <summary>MUTATION-C: mirrors DialogAddHediff.DrawAdjustablePain's own raw `selectedPain` field write (redrawn every frame from the slider) -- no setter exists.</summary>
        public static void HediffSetPainSliderValue(Window dlg, int value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                // MUTATION-C: no setter exists (see the summary above).
                hediffPainField.SetValue(dlg, value);
                var hPermanent = hediffHPermanentField.GetValue(dlg) as HediffComp_GetsPermanent;
                hPermanent?.SetPainCategory(HealthTool_ConvertSliderToPainCategory(value));
            }
            catch (Exception ex) { Fail("HediffSetPainSliderValue", ex); }
        }

        /// <summary>HealthTool.ConvertSliderToPainCategory(int) -- mod-internal, invoked reflectively since it is not vanilla.</summary>
        private static PainCategory HealthTool_ConvertSliderToPainCategory(int val)
        {
            try
            {
                return (PainCategory)healthToolConvertSliderToPainCategoryMethod.Invoke(null, new object[] { val });
            }
            catch (Exception ex)
            {
                Fail("ConvertSliderToPainCategory", ex);
                return PainCategory.Painless;
            }
        }

        public static int HediffDuration(Window dlg)
        {
            if (!Ready || dlg == null)
                return -1;
            try { return (int)hediffDurationField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffDuration", ex); return -1; }
        }

        /// <summary>MUTATION-C for the dialog's own raw `selectedDuration` field write (redrawn every frame from the stepper, no setter exists), THEN vehicle A/B for the actual duration change: HediffComp_Disappears.SetDuration(int), the public vanilla setter. Props.showRemainingTime is already set once by CheckSelectionChanged when the hediff was selected, not per-edit, matching the mod's own DoWindowContents (which likewise only writes the raw field, never the Props flag, on every draw pass).</summary>
        public static void HediffSetDuration(Window dlg, int value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                // MUTATION-C: no setter exists (see the summary above).
                hediffDurationField.SetValue(dlg, value);
                var hDisappears = hediffHDisappearsField.GetValue(dlg) as HediffComp_Disappears;
                hDisappears?.SetDuration(value);
            }
            catch (Exception ex) { Fail("HediffSetDuration", ex); }
        }

        public static bool HediffIsPermanent(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try { return (bool)hediffIsPermanentField.GetValue(dlg); }
            catch (Exception ex) { Fail("HediffIsPermanent", ex); return false; }
        }

        /// <summary>Vehicle A: DialogAddHediff's own AChangePermanent(bool) handler.</summary>
        public static void HediffSetPermanent(Window dlg, bool value)
        {
            InvokeOn(dlg, hediffChangePermanentMethod, new object[] { value }, "HediffSetPermanent");
        }

        public static bool HediffHasPainComp(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try { return hediffHPermanentField.GetValue(dlg) != null; }
            catch (Exception ex) { Fail("HediffHasPainComp", ex); return false; }
        }

        public static bool HediffHasDurationComp(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try { return hediffHDisappearsField.GetValue(dlg) != null; }
            catch (Exception ex) { Fail("HediffHasDurationComp", ex); return false; }
        }

        public static bool HediffInjuryProps(Window dlg)
        {
            HediffDef def = HediffSelected(dlg);
            return def?.injuryProps != null;
        }

        public static float HediffLethalSeverity(Window dlg)
        {
            HediffDef def = HediffSelected(dlg);
            return def != null ? def.lethalSeverity : -1f;
        }

        public static float HediffMinSeverity(Window dlg)
        {
            HediffDef def = HediffSelected(dlg);
            return def != null ? def.minSeverity : 0f;
        }

        /// <summary>HealthTool.GetMaxSeverity(HediffDef) -- mod-internal, invoked reflectively.</summary>
        public static float HediffMaxSeverity(Window dlg)
        {
            HediffDef def = HediffSelected(dlg);
            if (def == null)
                return 0f;
            try
            {
                return (float)healthToolGetMaxSeverityMethod.Invoke(null, new object[] { def });
            }
            catch (Exception ex)
            {
                Fail("HediffMaxSeverity", ex);
                return def.maxSeverity;
            }
        }

        public static string HediffExtraTipString(Window dlg)
        {
            if (!Ready || dlg == null)
                return "";
            try { return hediffExtraTipStringField.GetValue(dlg) as string ?? ""; }
            catch (Exception ex) { Fail("HediffExtraTipString", ex); return ""; }
        }

        // ---- DialogFullheal ----

        public static List<Hediff> FullhealList(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<Hediff>();
            try { return (fullhealListField.GetValue(dlg) as List<Hediff>) ?? new List<Hediff>(); }
            catch (Exception ex) { Fail("FullhealList", ex); return new List<Hediff>(); }
        }

        public static bool FullhealIsChecked(Window dlg, Hediff hediff)
        {
            if (!Ready || dlg == null || hediff == null)
                return false;
            try
            {
                var dict = fullhealDicToRemoveField.GetValue(dlg) as Dictionary<Hediff, bool>;
                return dict != null && dict.TryGetValue(hediff, out bool value) && value;
            }
            catch (Exception ex) { Fail("FullhealIsChecked", ex); return false; }
        }

        /// <summary>MUTATION-C: mirrors DialogFullheal's own per-row CheckboxLabeled ref-write (`dicToRemove[key] = isChecked;`) -- no setter method exists.</summary>
        public static void FullhealToggle(Window dlg, Hediff hediff)
        {
            if (!Ready || dlg == null || hediff == null)
                return;
            try
            {
                var dict = fullhealDicToRemoveField.GetValue(dlg) as Dictionary<Hediff, bool>;
                if (dict != null && dict.ContainsKey(hediff))
                    dict[hediff] = !dict[hediff];
            }
            catch (Exception ex) { Fail("FullhealToggle", ex); }
        }

        /// <summary>MUTATION-C: reproduces the end state of the mod's own one-shot bAll flag (applied and cleared on the dialog's NEXT DoWindowContents pass) -- see class remarks.</summary>
        public static void FullhealCheckAll(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                var dict = fullhealDicToRemoveField.GetValue(dlg) as Dictionary<Hediff, bool>;
                if (dict == null)
                    return;
                foreach (Hediff key in dict.Keys.ToList())
                    dict[key] = true;
            }
            catch (Exception ex) { Fail("FullhealCheckAll", ex); }
        }

        /// <summary>MUTATION-C: reproduces the end state of the mod's own one-shot bToogle flag -- see class remarks.</summary>
        public static void FullhealFlipAll(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                var dict = fullhealDicToRemoveField.GetValue(dlg) as Dictionary<Hediff, bool>;
                if (dict == null)
                    return;
                foreach (Hediff key in dict.Keys.ToList())
                    dict[key] = !dict[key];
            }
            catch (Exception ex) { Fail("FullhealFlipAll", ex); }
        }

        /// <summary>Invokes the dialog's own gated OK delegate -- DoAndClose, NEVER OnAcceptKeyPressed (see class remarks).</summary>
        public static void FullhealConfirm(Window dlg)
        {
            InvokeOn(dlg, fullhealDoAndCloseMethod, null, "FullhealConfirm");
        }

        // ---- DialogChoosePart ----

        public static List<BodyPartRecord> ChoosePartCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<BodyPartRecord>();
            try { return (choosePartListField.GetValue(dlg) as List<BodyPartRecord>) ?? new List<BodyPartRecord>(); }
            catch (Exception ex) { Fail("ChoosePartCandidates", ex); return new List<BodyPartRecord>(); }
        }

        public static BodyPartRecord ChoosePartSelected(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try { return choosePartSelectedField.GetValue(dlg) as BodyPartRecord; }
            catch (Exception ex) { Fail("ChoosePartSelected", ex); return null; }
        }

        /// <summary>MUTATION-C: mirrors the dialog's own Listing_Standard.RadioButton click assignment -- no setter exists.</summary>
        public static void ChoosePartSetSelected(Window dlg, BodyPartRecord part)
        {
            if (!Ready || dlg == null)
                return;
            // MUTATION-C: no setter exists (see the summary above).
            try { choosePartSelectedField.SetValue(dlg, part); }
            catch (Exception ex) { Fail("ChoosePartSetSelected", ex); }
        }

        /// <summary>Invokes the dialog's own gated OK delegate -- DoAndClose, NEVER OnAcceptKeyPressed (see class remarks). Writes back into the caller's (DialogAddHediff's) SelectedPart property, continuing its CheckIsReady chain.</summary>
        public static void ChoosePartConfirm(Window dlg)
        {
            InvokeOn(dlg, choosePartDoAndCloseMethod, null, "ChoosePartConfirm");
        }

        // ---- DialogChoosePawn ----

        /// <summary>The faction/list-source filter's candidates -- the same live container CharEditorCompat.ListSources() already reads (CEditor.API.DicFactions), which DialogChoosePawn's own DicFactions property is a passthrough to.</summary>
        public static List<string> ChoosePawnFactionCandidates() => CharEditorCompat.ListSources();

        public static string ChoosePawnSelectedFaction(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try { return choosePawnSelectedListNameField.GetValue(dlg) as string; }
            catch (Exception ex) { Fail("ChoosePawnSelectedFaction", ex); return null; }
        }

        /// <summary>Vehicle A: the dialog's own faction dropdown handler, which re-queries lOfPawns.</summary>
        public static void ChoosePawnSetFaction(Window dlg, string value)
        {
            InvokeOn(dlg, choosePawnFactionSelectedMethod, new object[] { value }, "ChoosePawnSetFaction");
        }

        public static List<Pawn> ChoosePawnCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<Pawn>();
            try { return (choosePawnListField.GetValue(dlg) as List<Pawn>) ?? new List<Pawn>(); }
            catch (Exception ex) { Fail("ChoosePawnCandidates", ex); return new List<Pawn>(); }
        }

        public static Pawn ChoosePawnSelected(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try { return choosePawnSelectedField.GetValue(dlg) as Pawn; }
            catch (Exception ex) { Fail("ChoosePawnSelected", ex); return null; }
        }

        /// <summary>MUTATION-C: mirrors the dialog's own SZWidgets.ListView ref-bound selection write -- no setter exists.</summary>
        public static void ChoosePawnSetSelected(Window dlg, Pawn pawn)
        {
            if (!Ready || dlg == null)
                return;
            // MUTATION-C: no setter exists (see the summary above).
            try { choosePawnSelectedField.SetValue(dlg, pawn); }
            catch (Exception ex) { Fail("ChoosePawnSetSelected", ex); }
        }

        /// <summary>The dialog's own supplementary text baked into its title (e.g. "(Father)"/"(Genitor)" for pregnancy-related pawn selection) -- read live rather than transcribed.</summary>
        public static string ChoosePawnCustomText(Window dlg)
        {
            if (!Ready || dlg == null)
                return "";
            try { return choosePawnCustomTextField.GetValue(dlg) as string ?? ""; }
            catch (Exception ex) { Fail("ChoosePawnCustomText", ex); return ""; }
        }

        /// <summary>Invokes the dialog's own gated OK delegate -- DoAndClose, NEVER OnAcceptKeyPressed (see class remarks). Writes back into the caller's (DialogAddHediff's) SelectedPawn property, continuing its CheckIsReady chain.</summary>
        public static void ChoosePawnConfirm(Window dlg)
        {
            InvokeOn(dlg, choosePawnDoAndCloseMethod, null, "ChoosePawnConfirm");
        }
    }
}
