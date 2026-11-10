using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The Character tab's own mutation
    /// vehicles (<c>CharacterEditor.CEditor+EditorUI+BlockBio</c>'s Name/Age/Backstory/Traits/
    /// Skills handlers, plus the standalone <c>AgeTool</c>/<c>BackstoryTool</c>/<c>TraitTool</c>
    /// extension classes) that <see cref="Shell.CharacterEditorScope"/>'s Character section calls.
    /// Gated by its own <see cref="CharacterTabReady"/> rather than folding into
    /// <see cref="Ready"/>: a mod update that only renames a Character-tab member must not take
    /// down the Pawns/tab-switching surface S1a already shipped.
    ///
    /// Mutation vehicles:
    /// <list type="bullet">
    /// <item>Age rides the standalone <c>AgeTool.SetAge</c>/<c>SetChronoAge</c>, the same vehicle
    /// the arrow buttons and the timed text field commit through. The mod's arrow handlers clamp
    /// the STEP direction (floor 0 for both, ceiling 15498 for chronological only), which
    /// <see cref="StepBiologicalAge"/>/<see cref="StepChronologicalAge"/> reproduce. Exact text
    /// entry has no upper clamp beyond the widget's digit-count cap, and the setters deliberately
    /// add none either, preserving the mod's own quirk.</item>
    /// <item>Backstory: <see cref="StepBackstory"/> invokes BlockBio's <c>RememberOldBackstory</c>,
    /// the standalone <c>BackstoryTool.SetBackstory</c> stepper, then <c>RecalcSkills</c>, in that
    /// order. Callers pass <c>random: true</c> for the random-backstory rows; same method.</item>
    /// <item>Traits: <see cref="RemoveTrait"/> invokes <c>TraitTool.RemoveTrait</c> directly, the
    /// vehicle AOnTraitClick's remove branch calls, without its mode toggle since Delete always
    /// removes. Copy/paste/randomize ride BlockBio's own handlers so the clipboard stays the mod's
    /// <c>lCopyTraits</c>, shared with the sighted buttons.</item>
    /// <item>Skills: stepping and passion cycling ride BlockBio's own handlers, which are bare
    /// unbounded field writes today but carry any future mod-side change. Only exact-level ENTRY
    /// has no method to ride and stays a MUTATION-C mirror in the scope. Copy/paste/randomize ride
    /// BlockBio's handlers too: <c>SkillTool.PasteSkills</c> carries real disables-recompute logic,
    /// and the clipboard is shared with the sighted UI as traits' is.</item>
    /// <item><see cref="TraitOffsetForSkill"/> rides <c>TraitTool.GetTraitOffsetForSkill</c>. The
    /// backstory and aptitude components of the same bar are summed locally in
    /// <see cref="Shell.CharacterEditorScope"/> from vanilla's public
    /// <c>BackstoryDef.skillGains</c> and <c>SkillRecord.Aptitude</c>; the mod's own private helper
    /// is side-effect-free addition over the same public data.</item>
    /// </list>
    ///
    /// Name editing has no compat vehicle for the COMMIT: the mod's only path is a direct
    /// <c>API.Pawn.Name</c> assignment inside its timed inline field, so
    /// <see cref="Shell.CharacterEditorScope"/> performs that assignment itself, marked MUTATION-C.
    /// <see cref="RandomizeName"/>/<see cref="RandomizeNameFull"/> do have vehicles and ride them.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static readonly LazyReflectionGate characterTabGate = new LazyReflectionGate("CharEditorCompat character tab", ResolveCharacterTab);

        private static Type blockBioType;
        private static Type ageToolType;
        private static Type backstoryToolType;
        private static Type traitToolType;

        private static MethodInfo getBlockBio;

        private static MethodInfo agToolSetAge;
        private static MethodInfo agToolSetChronoAge;

        private static MethodInfo bioChangeNameTriple;
        private static MethodInfo bioChangeNameTripleAll;

        private static MethodInfo bioRememberOldBackstory;
        private static MethodInfo bioRecalcSkills;
        private static MethodInfo backstoryToolSetBackstoryStep;

        private static MethodInfo bioACapsuleUI;

        private static MethodInfo bioCopyTraits;
        private static MethodInfo bioPasteTraits;
        private static MethodInfo bioRandomTraits;
        private static FieldInfo bioTraitClipboardField;

        private static MethodInfo bioCopySkills;
        private static MethodInfo bioPasteSkills;
        private static MethodInfo bioRandomSkills;
        private static FieldInfo bioSkillClipboardField;
        private static MethodInfo bioAddSkillLevel;
        private static MethodInfo bioSubSkillLevel;
        private static MethodInfo bioTogglePassion;

        private static MethodInfo traitToolRemoveTrait;
        private static MethodInfo traitToolOffsetForSkill;

        private static MethodInfo ceditorGetHashSetString;
        private static object eTypeModsTraitDef;
        private static FieldInfo labelAllField;

        /// <summary>True when every member the Character tab's Name/Age/Backstory/Traits/Skills rows need resolved.</summary>
        public static bool CharacterTabReady
        {
            get
            {
                gate.Ensure();
                return characterTabGate.Ensure();
            }
        }

        /// <summary>Called from the main <c>EnsureInit</c> once EditorUI and TabType are known; idempotent.</summary>
        private static bool ResolveCharacterTab(ReflectionSurface surface)
        {
            if (editorUIType == null || tabTypeType == null)
                return false;

            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            blockBioType = surface.Supplied("EditorUI.BlockBio", editorUIType.GetNestedType("BlockBio", NestedFlags));
            ageToolType = surface.Type("CharacterEditor.AgeTool");
            backstoryToolType = surface.Type("CharacterEditor.BackstoryTool");
            traitToolType = surface.Type("CharacterEditor.TraitTool");

            getBlockBio = surface.Required("EditorUI.Get<BlockBio>(TabType) closed",
                CloseGeneric(editorUIType, "Get", blockBioType));

            bioChangeNameTriple = surface.Method(blockBioType, "AChangeNameTriple", Type.EmptyTypes);
            bioChangeNameTripleAll = surface.Method(blockBioType, "AChangeNameTripleAll", Type.EmptyTypes);
            bioRememberOldBackstory = surface.Method(blockBioType, "RememberOldBackstory", Type.EmptyTypes);
            bioRecalcSkills = surface.Method(blockBioType, "RecalcSkills", Type.EmptyTypes);
            // ACapsuleUI has no InStartingScreen gate of its own, only its drawn button does, so
            // this one vehicle covers both world-gen and in-game. The in-game capsule button has no
            // named handler to ride; it inlines the same WindowTool.Open call.
            bioACapsuleUI = surface.Method(blockBioType, "ACapsuleUI", Type.EmptyTypes);
            bioCopyTraits = surface.Method(blockBioType, "ACopyTraits", Type.EmptyTypes);
            bioPasteTraits = surface.Method(blockBioType, "APasteTraits", Type.EmptyTypes);
            bioRandomTraits = surface.Method(blockBioType, "ARandomTraits", Type.EmptyTypes);
            bioTraitClipboardField = surface.Field(blockBioType, "lCopyTraits");
            bioCopySkills = surface.Method(blockBioType, "ACopySkills", Type.EmptyTypes);
            bioPasteSkills = surface.Method(blockBioType, "APasteSkills", Type.EmptyTypes);
            bioRandomSkills = surface.Method(blockBioType, "ARandomSkills", Type.EmptyTypes);
            bioSkillClipboardField = surface.Field(blockBioType, "lOfCopySkills");
            bioAddSkillLevel = surface.Method(blockBioType, "AAddSkillLevel", new[] { typeof(SkillRecord) });
            bioSubSkillLevel = surface.Method(blockBioType, "ASubSkillLevel", new[] { typeof(SkillRecord) });
            bioTogglePassion = surface.Method(blockBioType, "ATogglePassion", new[] { typeof(SkillRecord) });

            agToolSetAge = surface.Method(ageToolType, "SetAge", new[] { typeof(Pawn), typeof(int) });
            agToolSetChronoAge = surface.Method(ageToolType, "SetChronoAge", new[] { typeof(Pawn), typeof(int) });

            backstoryToolSetBackstoryStep = surface.Method(backstoryToolType, "SetBackstory",
                new[] { typeof(Pawn), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });

            traitToolRemoveTrait = surface.Method(traitToolType, "RemoveTrait", new[] { typeof(Pawn), typeof(Trait) });
            traitToolOffsetForSkill = surface.Method(traitToolType, "GetTraitOffsetForSkill", new[] { typeof(Pawn), typeof(SkillDef) });

            // EType is the core file's required type, so a miss is reported there rather than here;
            // these members only become required once their type resolved.
            if (eTypeType != null)
            {
                ceditorGetHashSetString = surface.Required("CEditor.Get<HashSet<string>>(EType) closed",
                    CloseGeneric(ceditorType, "Get", typeof(HashSet<string>)));
                eTypeModsTraitDef = surface.EnumValue(eTypeType, "ModsTraitDef");
            }

            labelAllField = surface.Field(Labels.LabelType, "ALL");

            return surface.Ready && Labels.Ready;
        }

        /// <summary>The mod's own "All" word, the fallback its dropdowns show for a null filter value.</summary>
        public static string AllLabel
        {
            get
            {
                if (!CharacterTabReady || labelAllField == null)
                    return "";
                try
                {
                    return labelAllField.GetValue(null) as string ?? "";
                }
                catch (Exception ex)
                {
                    Fail("AllLabel", ex);
                    return "";
                }
            }
        }

        private static object BlockBio(Window editorUI)
        {
            return Block(editorUI, getBlockBio, "BlockBio");
        }

        // Name.

        /// <summary>Rerolls the pawn's full name via the mod's own dice handler.</summary>
        public static void RandomizeName(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioChangeNameTriple, "RandomizeName");
        }

        /// <summary>Assigns a fresh random name from any gender-appropriate name in the database.</summary>
        public static void RandomizeNameFull(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioChangeNameTripleAll, "RandomizeNameFull");
        }

        // Capsule.

        /// <summary>Opens DialogCapsuleUI via BlockBio.ACapsuleUI, which covers both entry points.</summary>
        public static void OpenCapsule(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioACapsuleUI, "OpenCapsule");
        }

        // Age.

        /// <summary>Exact biological-age write, mirroring the mod's timed text field; no upper clamp.</summary>
        public static void SetBiologicalAge(Pawn pawn, int years)
        {
            InvokeAgeTool(agToolSetAge, pawn, years, "SetBiologicalAge");
        }

        /// <summary>Exact chronological-age write, mirroring the mod's timed text field; no upper clamp.</summary>
        public static void SetChronologicalAge(Pawn pawn, int years)
        {
            InvokeAgeTool(agToolSetChronoAge, pawn, years, "SetChronologicalAge");
        }

        /// <summary>Steps biological age by one year, floor 0, as the mod's arrows do.</summary>
        public static void StepBiologicalAge(Pawn pawn, int direction)
        {
            if (pawn?.ageTracker == null)
                return;
            int next = pawn.ageTracker.AgeBiologicalYears + (direction > 0 ? 1 : -1);
            if (next < 0)
                next = 0;
            SetBiologicalAge(pawn, next);
        }

        /// <summary>Steps chronological age by one year, floor 0 and ceiling 15498, as the mod's arrows do.</summary>
        public static void StepChronologicalAge(Pawn pawn, int direction)
        {
            if (pawn?.ageTracker == null)
                return;
            int next = pawn.ageTracker.AgeChronologicalYears + (direction > 0 ? 1 : -1);
            if (next < 0)
                next = 0;
            if (next > 15498)
                next = 15498;
            SetChronologicalAge(pawn, next);
        }

        private static void InvokeAgeTool(MethodInfo method, Pawn pawn, int years, string caller)
        {
            if (!CharacterTabReady || method == null || pawn == null)
                return;
            try
            {
                method.Invoke(null, new object[] { pawn, years });
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // Backstory.

        /// <summary>Steps or randomizes one backstory slot as the mod's own handlers do: remember, step, recalc.</summary>
        public static void StepBackstory(Window editorUI, bool isChildhood, bool next, bool random)
        {
            if (!CharacterTabReady)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                Pawn pawn = CurrentPawn;
                if (bio == null || pawn == null)
                    return;
                bioRememberOldBackstory.Invoke(bio, null);
                backstoryToolSetBackstoryStep.Invoke(null, new object[] { pawn, next, random, isChildhood, false });
                bioRecalcSkills.Invoke(bio, null);
            }
            catch (Exception ex)
            {
                Fail("StepBackstory", ex);
            }
        }

        // Traits.

        /// <summary>
        /// Steps a skill's level via the delegates the mod's plus/minus buttons invoke. Those
        /// handlers are unbounded in both directions, and riding them preserves that.
        /// </summary>
        public static void StepSkillLevel(Window editorUI, SkillRecord record, bool up)
        {
            if (!CharacterTabReady || record == null)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return;
                (up ? bioAddSkillLevel : bioSubSkillLevel).Invoke(bio, new object[] { record });
            }
            catch (Exception ex)
            {
                Fail("StepSkillLevel", ex);
            }
        }

        /// <summary>Cycles a skill's passion None to Minor to Major to None, via the mod's own passion-click delegate.</summary>
        public static void CyclePassion(Window editorUI, SkillRecord record)
        {
            if (!CharacterTabReady || record == null)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return;
                bioTogglePassion.Invoke(bio, new object[] { record });
            }
            catch (Exception ex)
            {
                Fail("CyclePassion", ex);
            }
        }

        /// <summary>Removes a trait via TraitTool.RemoveTrait, the vehicle AOnTraitClick's remove branch calls.</summary>
        public static void RemoveTrait(Pawn pawn, Trait trait)
        {
            if (!CharacterTabReady || pawn == null || trait == null)
                return;
            try
            {
                traitToolRemoveTrait.Invoke(null, new object[] { pawn, trait });
            }
            catch (Exception ex)
            {
                Fail("RemoveTrait", ex);
            }
        }

        public static void CopyTraits(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioCopyTraits, "CopyTraits");
        }

        public static void PasteTraits(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioPasteTraits, "PasteTraits");
        }

        /// <summary>Creation-mode dice: clears all traits and adds 0-10 random ones via the mod's handler.</summary>
        public static void RandomizeTraits(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioRandomTraits, "RandomizeTraits");
        }

        /// <summary>Whether the mod's own trait clipboard holds anything to paste.</summary>
        public static bool HasTraitClipboard(Window editorUI)
        {
            if (!CharacterTabReady)
                return false;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return false;
                var list = bioTraitClipboardField.GetValue(bio) as System.Collections.ICollection;
                return list != null && list.Count > 0;
            }
            catch (Exception ex)
            {
                Fail("HasTraitClipboard", ex);
                return false;
            }
        }

        /// <summary>The trait contribution to a skill's background-offset bar.</summary>
        public static int TraitOffsetForSkill(Pawn pawn, SkillDef skill)
        {
            if (!CharacterTabReady || pawn == null || skill == null)
                return 0;
            try
            {
                return (int)traitToolOffsetForSkill.Invoke(null, new object[] { pawn, skill });
            }
            catch (Exception ex)
            {
                Fail("TraitOffsetForSkill", ex);
                return 0;
            }
        }

        // Skills.

        public static void CopySkills(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioCopySkills, "CopySkills");
        }

        /// <summary>Rides APasteSkills, whose SkillTool.PasteSkills does the disables recompute.</summary>
        public static void PasteSkills(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioPasteSkills, "PasteSkills");
        }

        /// <summary>Creation-mode dice: rerolls level and passion for every non-disabled skill.</summary>
        public static void RandomizeSkills(Window editorUI)
        {
            InvokeOnBlockBio(editorUI, bioRandomSkills, "RandomizeSkills");
        }

        /// <summary>Whether the mod's own skill clipboard holds anything to paste.</summary>
        public static bool HasSkillClipboard(Window editorUI)
        {
            if (!CharacterTabReady)
                return false;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return false;
                var list = bioSkillClipboardField.GetValue(bio) as System.Collections.ICollection;
                return list != null && list.Count > 0;
            }
            catch (Exception ex)
            {
                Fail("HasSkillClipboard", ex);
                return false;
            }
        }

        /// <summary>
        /// Every mod-name value the trait mod-name filter can select: the same live container
        /// DialogAddTrait's own dropdown reads, whatever "All"/null shape it carries.
        /// </summary>
        public static List<string> TraitModNames()
        {
            var result = new List<string>();
            if (!CharacterTabReady)
                return result;
            try
            {
                object api = Api();
                if (api == null)
                    return result;
                var set = ceditorGetHashSetString.Invoke(api, new[] { eTypeModsTraitDef }) as HashSet<string>;
                if (set != null)
                {
                    result.AddRange(set);
                }
            }
            catch (Exception ex)
            {
                Fail("TraitModNames", ex);
            }
            return result;
        }

        private static void InvokeOnBlockBio(Window editorUI, MethodInfo method, string caller)
        {
            if (!CharacterTabReady || method == null)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio != null)
                    method.Invoke(bio, null);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }
    }
}
