using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.DialogAddThought</c>, with its OWN
    /// <see cref="Ready"/> flag so a rename here cannot take down the Needs/Social tree sections.
    /// Both BlockNeeds' unfiltered "Add thought..." row and BlockSocial's Social-preset "Add
    /// thought" icon open the SAME dialog type, so one <c>AddThoughtAdapter</c> registration covers
    /// both entry points.
    ///
    /// THE ACCEPT-KEY TRAP. <c>DialogAddThought</c> draws its OK button as a bare
    /// <c>WindowTool.SimpleAcceptButton(this, DoAndClose)</c> -- rendered ONLY while
    /// <c>bAllOk</c> is true (<c>DrawAccept</c>: <c>if (bAllOk) { ... }</c>) -- with NO
    /// <c>Window.OnAcceptKeyPressed</c> override. Calling the base method would run only
    /// <c>if (closeOnAccept) Close();</c> (closeOnAccept defaults true and is never overridden
    /// here) -- closing WITHOUT adding the thought. <see cref="Confirm"/> therefore invokes
    /// <c>DoAndClose()</c> directly, gated on <see cref="AllOk"/> exactly as the mod's own button
    /// visibility gates it -- never <c>OnAcceptKeyPressed</c>. Cancel is safe: no
    /// <c>OnCancelKeyPressed</c> override exists and <c>closeOnCancel</c>/<c>closeOnClickedOutside</c>
    /// are both set true in the constructor, so the base method's own <c>Close()</c> is correct.
    ///
    /// <see cref="SelectThought"/> invokes the dialog's own <c>AThoughtSelected(ThoughtDef)</c> --
    /// vehicle A, not a raw field write: this SAME method is the ListView's onSelect callback
    /// (DrawThoughtList's trailing argument), and it ALSO resets <c>SelectedPawn</c>/
    /// <c>selectedStage</c>/<c>selectedTitle</c>/<c>dicStages</c> for the newly picked thought,
    /// so the Parameters region reads fresh gating on the very next describe with no separate
    /// reconciliation call needed (unlike AddHediffAdapter's <c>selectedHediff</c>, which has no
    /// such onSelect hook and needs an explicit follow-up call).
    ///
    /// <see cref="ResolvedLabel"/>/<see cref="ResolvedTooltip"/> ride the dialog's own PRIVATE
    /// <c>GetLabelForThought</c>/<c>GetTooltipForThought</c>, which already carry every special case
    /// (DeadMansApparel's raw defName, the pawn's own current-gender label resolution, dev-mode
    /// stage detail, the "requires hediff/weapon/love relation/trait" hints).
    /// <see cref="NeedsOtherPawn"/>/<see cref="NeedsTitle"/> ride
    /// <c>ThoughtTool.HasOtherPawnMember</c>/<c>IsForTitle</c> for the same reason: both mix several
    /// <c>IsTypeOf&lt;T&gt;</c> special cases that would be brittle to reproduce by hand.
    /// <see cref="NeedsStage"/> needs no reflection (PUBLIC vanilla <c>ThoughtDef.stages</c>), and
    /// the mood/opinion interactivity gates hand-mirror <c>ThoughtTool.IsTypeOf&lt;T&gt;</c> -- see
    /// <see cref="IsTypeOfMirror"/> for why an exact mirror, not <c>IsAssignableFrom</c>, is needed.
    /// </summary>
    internal static class CharEditorThoughtBrowserCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorThoughtBrowserCompat", Resolve);

        private static Type dialogType;
        private static Type thoughtToolType;

        private static PropertyInfo selectedPawnProperty;

        private static FieldInfo selectedModNameField;
        private static FieldInfo selectedTypeField;
        private static FieldInfo selectedStageField;
        private static FieldInfo selectedTitleField;
        private static FieldInfo selectedThoughtField;
        private static FieldInfo modNamesField;
        private static FieldInfo listTypeField;
        private static FieldInfo listThoughtsField;
        private static FieldInfo royalTitlesField;
        private static FieldInfo dicStagesField;
        private static FieldInfo baseMoodOffsetField;
        private static FieldInfo selectedMoodOffsetField;
        private static FieldInfo selectedOpinionOffsetField;
        private static FieldInfo needOtherPawnField;
        private static FieldInfo needTitleField;
        private static FieldInfo needStageField;
        private static FieldInfo allOkField;

        private static MethodInfo modNameSelectedMethod;
        private static MethodInfo typeSelectedMethod;
        private static MethodInfo thoughtSelectedMethod;
        private static MethodInfo selectPawnMethod;
        private static MethodInfo stageSelectedMethod;
        private static MethodInfo royalTitleSelectedMethod;
        private static MethodInfo doAndCloseMethod;
        private static MethodInfo getLabelForThoughtMethod;
        private static MethodInfo getTooltipForThoughtMethod;

        private static MethodInfo hasOtherPawnMemberMethod;
        private static MethodInfo isForTitleMethod;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get
            {
                return gate.Ensure();
            }
        }

        public static Type DialogType { get { gate.Ensure(); return dialogType; } }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            dialogType = surface.Type("CharacterEditor.DialogAddThought");
            thoughtToolType = surface.Type("CharacterEditor.ThoughtTool");

            selectedPawnProperty = surface.Property(dialogType, "SelectedPawn");
            selectedModNameField = surface.Field(dialogType, "selectedModName");
            selectedTypeField = surface.Field(dialogType, "selectedType");
            selectedStageField = surface.Field(dialogType, "selectedStage");
            selectedTitleField = surface.Field(dialogType, "selectedTitle");
            selectedThoughtField = surface.Field(dialogType, "selectedThought");
            modNamesField = surface.Field(dialogType, "lModnames");
            listTypeField = surface.Field(dialogType, "lListType");
            listThoughtsField = surface.Field(dialogType, "lListThoughts");
            royalTitlesField = surface.Field(dialogType, "lRoyalTitles");
            dicStagesField = surface.Field(dialogType, "dicStages");
            baseMoodOffsetField = surface.Field(dialogType, "baseMoodOffset");
            selectedMoodOffsetField = surface.Field(dialogType, "selectedMoodOffset");
            selectedOpinionOffsetField = surface.Field(dialogType, "selectedOpinionOffset");
            needOtherPawnField = surface.Field(dialogType, "bNeedOtherPawn");
            needTitleField = surface.Field(dialogType, "bNeedTitle");
            needStageField = surface.Field(dialogType, "bNeedStage");
            allOkField = surface.Field(dialogType, "bAllOk");

            modNameSelectedMethod = surface.Method(dialogType, "AModnameSelected", new[] { typeof(string) });
            typeSelectedMethod = surface.Method(dialogType, "AThoughtTypeSelected", new[] { typeof(string) });
            thoughtSelectedMethod = surface.Method(dialogType, "AThoughtSelected", new[] { typeof(ThoughtDef) });
            selectPawnMethod = surface.Method(dialogType, "ASelectPawn", Type.EmptyTypes);
            stageSelectedMethod = surface.Method(dialogType, "AStageSelected", new[] { typeof(string) });
            royalTitleSelectedMethod = surface.Method(dialogType, "ARoyalTitleSelected", new[] { typeof(RoyalTitleDef) });
            doAndCloseMethod = surface.Method(dialogType, "DoAndClose", Type.EmptyTypes);
            getLabelForThoughtMethod = surface.Method(dialogType, "GetLabelForThought", new[] { typeof(ThoughtDef) });
            getTooltipForThoughtMethod = surface.Method(dialogType, "GetTooltipForThought", new[] { typeof(ThoughtDef) });

            hasOtherPawnMemberMethod = surface.Method(thoughtToolType, "HasOtherPawnMember", new[] { typeof(ThoughtDef) });
            isForTitleMethod = surface.Method(thoughtToolType, "IsForTitle", new[] { typeof(ThoughtDef) });

            return surface.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorThoughtBrowserCompat." + member + " failed: " + ex.Message);
        }

        // ------------------------------------------------------------------
        // Filters.
        // ------------------------------------------------------------------

        public static string ModName(Window dlg)
        {
            if (!Ready || dlg == null) return null;
            try { return selectedModNameField.GetValue(dlg) as string; }
            catch (Exception ex) { Fail("ModName", ex); return null; }
        }

        /// <summary>The mod-name filter's fixed candidate list -- DialogAddThought.lModnames (built once at construction from every mod contributing a thought def, plus a leading null for "All").</summary>
        public static List<string> ModNameCandidates(Window dlg)
        {
            if (!Ready || dlg == null) return new List<string>();
            try { return new List<string>(modNamesField.GetValue(dlg) as HashSet<string> ?? new HashSet<string>()); }
            catch (Exception ex) { Fail("ModNameCandidates", ex); return new List<string>(); }
        }

        public static void SetModName(Window dlg, string value) => InvokeOn(dlg, modNameSelectedMethod, new object[] { value }, "SetModName");

        public static string TypeFilter(Window dlg)
        {
            if (!Ready || dlg == null) return null;
            try { return selectedTypeField.GetValue(dlg) as string; }
            catch (Exception ex) { Fail("TypeFilter", ex); return null; }
        }

        /// <summary>The type filter's fixed candidate list -- DialogAddThought.lListType (All, Memory, Social, Situational, Situational+Social, Unsupported -- already built and localized at construction).</summary>
        public static List<string> TypeCandidates(Window dlg)
        {
            if (!Ready || dlg == null) return new List<string>();
            try { return new List<string>(listTypeField.GetValue(dlg) as HashSet<string> ?? new HashSet<string>()); }
            catch (Exception ex) { Fail("TypeCandidates", ex); return new List<string>(); }
        }

        public static void SetTypeFilter(Window dlg, string value) => InvokeOn(dlg, typeSelectedMethod, new object[] { value }, "SetTypeFilter");

        // ------------------------------------------------------------------
        // Results.
        // ------------------------------------------------------------------

        /// <summary>The dialog's own currently-filtered candidate set -- DialogAddThought.lListThoughts.</summary>
        public static List<ThoughtDef> Results(Window dlg)
        {
            if (!Ready || dlg == null) return new List<ThoughtDef>();
            try { return new List<ThoughtDef>(listThoughtsField.GetValue(dlg) as HashSet<ThoughtDef> ?? new HashSet<ThoughtDef>()); }
            catch (Exception ex) { Fail("Results", ex); return new List<ThoughtDef>(); }
        }

        public static ThoughtDef Selected(Window dlg)
        {
            if (!Ready || dlg == null) return null;
            try { return selectedThoughtField.GetValue(dlg) as ThoughtDef; }
            catch (Exception ex) { Fail("Selected", ex); return null; }
        }

        /// <summary>Vehicle A: the dialog's own ListView onSelect callback -- also resets SelectedPawn/selectedStage/selectedTitle/dicStages for the newly picked thought (see class remarks).</summary>
        public static void SelectThought(Window dlg, ThoughtDef def) => InvokeOn(dlg, thoughtSelectedMethod, new object[] { def }, "SelectThought");

        /// <summary>DialogAddThought.GetLabelForThought(ThoughtDef) -- the mod's own resolved label (DeadMansApparel's raw defName, else the pawn-aware GetThoughtLabel).</summary>
        public static string ResolvedLabel(Window dlg, ThoughtDef def)
        {
            if (!Ready || dlg == null || def == null) return "";
            try { return getLabelForThoughtMethod.Invoke(dlg, new object[] { def }) as string ?? ""; }
            catch (Exception ex) { Fail("ResolvedLabel", ex); return ""; }
        }

        /// <summary>DialogAddThought.GetTooltipForThought(ThoughtDef) -- description, per-stage mood/opinion breakdown, and the "requires ..." hints, dev-mode detail included.</summary>
        public static string ResolvedTooltip(Window dlg, ThoughtDef def)
        {
            if (!Ready || dlg == null || def == null) return null;
            try { return getTooltipForThoughtMethod.Invoke(dlg, new object[] { def }) as string; }
            catch (Exception ex) { Fail("ResolvedTooltip", ex); return null; }
        }

        // ------------------------------------------------------------------
        // Conditional parameter rows.
        // ------------------------------------------------------------------

        /// <summary>ThoughtTool.HasOtherPawnMember(ThoughtDef) -- see class remarks.</summary>
        public static bool NeedsOtherPawn(ThoughtDef def)
        {
            if (!Ready || def == null) return false;
            try { return (bool)hasOtherPawnMemberMethod.Invoke(null, new object[] { def }); }
            catch (Exception ex) { Fail("NeedsOtherPawn", ex); return false; }
        }

        /// <summary>Plain vanilla read: ThoughtDef.stages.Count > 1 -- the SAME condition DrawLowerDropdowns tests, no mod-internal logic involved.</summary>
        public static bool NeedsStage(ThoughtDef def) => def?.stages != null && def.stages.Count > 1;

        /// <summary>ThoughtTool.IsForTitle(ThoughtDef) -- see class remarks.</summary>
        public static bool NeedsTitle(ThoughtDef def)
        {
            if (!Ready || def == null) return false;
            try { return (bool)isForTitleMethod.Invoke(null, new object[] { def }); }
            catch (Exception ex) { Fail("NeedsTitle", ex); return false; }
        }

        /// <summary>
        /// Mirrors <c>CharacterEditor.ThoughtTool.IsTypeOf&lt;T&gt;</c> exactly:
        /// raw field, then the resolved <c>ThoughtDef.ThoughtClass</c>
        /// property (null <c>thoughtClass</c> with <c>durationDays &gt; 0</c> resolves to
        /// <c>Thought_Memory</c>), then up to three <c>BaseType</c> hops off the RAW field. NOT
        /// <c>IsAssignableFrom</c> -- the mod's gate is exact-or-shallow-base, and our row must be
        /// interactive exactly where the mod draws its slider.
        /// </summary>
        private static bool IsTypeOfMirror(ThoughtDef def, Type target)
        {
            if (def == null) return false;
            if (def.thoughtClass == target) return true;
            if (def.ThoughtClass == target) return true;
            if (def.thoughtClass == null) return false;
            if (def.thoughtClass.BaseType == target) return true;
            if (def.thoughtClass.BaseType == null) return false;
            if (def.thoughtClass.BaseType.BaseType == target) return true;
            if (def.thoughtClass.BaseType.BaseType == null) return false;
            return def.thoughtClass.BaseType.BaseType.BaseType == target;
        }

        /// <summary>ThoughtTool.IsTypeOf&lt;Thought_Memory&gt;(ThoughtDef) -- see <see cref="IsTypeOfMirror"/>. Gates the mood row's interactivity (slider vs. plain readout).</summary>
        public static bool IsMemoryKind(ThoughtDef def) => IsTypeOfMirror(def, typeof(Thought_Memory));

        /// <summary>ThoughtTool.IsTypeOf&lt;Thought_MemorySocial&gt;(ThoughtDef) -- see <see cref="IsTypeOfMirror"/>. Gates the opinion row's interactivity.</summary>
        public static bool IsSocialMemoryKind(ThoughtDef def) => IsTypeOfMirror(def, typeof(Thought_MemorySocial));

        public static Pawn TargetPawn(Window dlg)
        {
            if (!Ready || dlg == null) return null;
            try { return selectedPawnProperty.GetValue(dlg) as Pawn; }
            catch (Exception ex) { Fail("TargetPawn", ex); return null; }
        }

        /// <summary>Opens DialogChoosePawn (DialogAddThought.ASelectPawn()); the ChoosePawnAdapter registration covers the resulting window, writing back into THIS dialog's SelectedPawn property.</summary>
        public static void OpenTargetPawnPicker(Window dlg) => InvokeOn(dlg, selectPawnMethod, null, "OpenTargetPawnPicker");

        /// <summary>The stage combo's candidate labels, in dicStages' own key order (dicStages: stage index -> spoken label, built once per thought selection).</summary>
        public static List<string> StageCandidates(Window dlg)
        {
            if (!Ready || dlg == null) return new List<string>();
            try
            {
                var dict = dicStagesField.GetValue(dlg) as Dictionary<int, string>;
                var result = new List<string>();
                if (dict != null)
                    foreach (var kv in dict)
                        result.Add(kv.Value);
                return result;
            }
            catch (Exception ex) { Fail("StageCandidates", ex); return new List<string>(); }
        }

        public static string StageValue(Window dlg)
        {
            if (!Ready || dlg == null) return null;
            try { return selectedStageField.GetValue(dlg) as string; }
            catch (Exception ex) { Fail("StageValue", ex); return null; }
        }

        public static void SetStage(Window dlg, string value) => InvokeOn(dlg, stageSelectedMethod, new object[] { value }, "SetStage");

        public static List<RoyalTitleDef> RoyalTitleCandidates(Window dlg)
        {
            if (!Ready || dlg == null) return new List<RoyalTitleDef>();
            try { return new List<RoyalTitleDef>(royalTitlesField.GetValue(dlg) as HashSet<RoyalTitleDef> ?? new HashSet<RoyalTitleDef>()); }
            catch (Exception ex) { Fail("RoyalTitleCandidates", ex); return new List<RoyalTitleDef>(); }
        }

        public static RoyalTitleDef RoyalTitleValue(Window dlg)
        {
            if (!Ready || dlg == null) return null;
            try { return selectedTitleField.GetValue(dlg) as RoyalTitleDef; }
            catch (Exception ex) { Fail("RoyalTitleValue", ex); return null; }
        }

        public static void SetRoyalTitle(Window dlg, RoyalTitleDef value) => InvokeOn(dlg, royalTitleSelectedMethod, new object[] { value }, "SetRoyalTitle");

        public static float BaseMoodOffset(Window dlg)
        {
            if (!Ready || dlg == null) return 0f;
            try { return (float)baseMoodOffsetField.GetValue(dlg); }
            catch (Exception ex) { Fail("BaseMoodOffset", ex); return 0f; }
        }

        public static float MoodMultiplier(Window dlg)
        {
            if (!Ready || dlg == null) return 0f;
            try { return (float)selectedMoodOffsetField.GetValue(dlg); }
            catch (Exception ex) { Fail("MoodMultiplier", ex); return 0f; }
        }

        /// <summary>MUTATION-C: mirrors DrawSlider's own ref-bound SimpleMultiplierSlider write for `selectedMoodOffset` -- no setter exists.</summary>
        public static void SetMoodMultiplier(Window dlg, float value)
        {
            if (!Ready || dlg == null) return;
            try
            {
                // MUTATION-C: no setter exists (see the summary above).
                selectedMoodOffsetField.SetValue(dlg, value);
            }
            catch (Exception ex) { Fail("SetMoodMultiplier", ex); }
        }

        public static float OpinionMultiplier(Window dlg)
        {
            if (!Ready || dlg == null) return 0f;
            try { return (float)selectedOpinionOffsetField.GetValue(dlg); }
            catch (Exception ex) { Fail("OpinionMultiplier", ex); return 0f; }
        }

        /// <summary>MUTATION-C: mirrors DrawSlider's own ref-bound SimpleMultiplierSlider write for `selectedOpinionOffset` -- no setter exists.</summary>
        public static void SetOpinionMultiplier(Window dlg, float value)
        {
            if (!Ready || dlg == null) return;
            try
            {
                // MUTATION-C: no setter exists (see the summary above).
                selectedOpinionOffsetField.SetValue(dlg, value);
            }
            catch (Exception ex) { Fail("SetOpinionMultiplier", ex); }
        }

        // ------------------------------------------------------------------
        // Confirm/Cancel.
        // ------------------------------------------------------------------

        /// <summary>DialogAddThought.bAllOk -- true only when every requirement (thought chosen, other pawn if needed, title if needed, stage if needed) is satisfied; the mod's own OK button does not even draw otherwise (see class remarks).</summary>
        public static bool AllOk(Window dlg)
        {
            if (!Ready || dlg == null) return false;
            try { return (bool)allOkField.GetValue(dlg); }
            catch (Exception ex) { Fail("AllOk", ex); return false; }
        }

        /// <summary>Invokes the dialog's own gated OK delegate -- DoAndClose, NEVER OnAcceptKeyPressed (see class remarks) -- only when AllOk, matching the mod's own button visibility gate.</summary>
        public static void Confirm(Window dlg)
        {
            if (!AllOk(dlg))
                return;
            InvokeOn(dlg, doAndCloseMethod, null, "Confirm");
        }

        private static void InvokeOn(Window dlg, MethodInfo method, object[] args, string caller)
        {
            if (!Ready || dlg == null || method == null)
                return;
            try { method.Invoke(dlg, args); }
            catch (Exception ex) { Fail(caller, ex); }
        }
    }
}
