using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The Character tab's remaining BlockBio surfaces beyond
    /// Name/Age/Backstory/Traits/Skills -- Abilities, Psycasts,
    /// Identity (faction, ideoligion, xenotype openers, favorite color, mutant, royal title,
    /// recruit/enslave), and the animal Training block -- plus the standalone
    /// <c>AbilityTool</c>/<c>PawnxTool</c> extension methods those rows write through. Gated by
    /// its own <see cref="CharacterExtendedReady"/>, so a mod update renaming one of these members
    /// cannot take down the sibling surfaces.
    ///
    /// Mutation vehicles:
    /// <list type="bullet">
    /// <item>Abilities: copy, paste and randomize ride BlockBio's own handlers so the clipboard stays
    /// the mod's <c>lCopyAbilities</c>. Per-ability Delete rides vanilla's public
    /// <c>Pawn_AbilityTracker.RemoveAbility</c>, the vehicle the mod's element-stack remove callback
    /// uses, without its remove-mode toggle since Delete always removes. Add opens
    /// <c>DialogAddAbility</c> directly.</item>
    /// <item>Psycasts: <see cref="SetEntropy"/>/<see cref="SetPsyfocus"/> ride the standalone
    /// <c>AbilityTool</c> extensions, the same vehicle <c>BlockBio.DrawPsycasts</c> calls the moment
    /// its slider value differs. The reads are all public vanilla members.</item>
    /// <item>Identity: <see cref="ChangeIdeo"/> invokes <c>AChangeIdeo</c> verbatim, including its
    /// <c>Prefs.DevMode</c> reflection dance and its KNOWN MOD BUG, not fixed here:
    /// <c>rememberDevMode</c> is never written from the local snapshot, so closing the ideoligion
    /// editor always restores DevMode to false. Favorite colour, mutant and royal title ride the
    /// exact float-menu callbacks the mod's inline pickers invoke. <see cref="Recruit"/>/
    /// <see cref="Enslave"/> ride <c>ARecruit</c>/<c>AEnslave</c>, so the dust puff and the
    /// <c>guest.Recruitable</c> force-set come along; these bypass vanilla's resistance/timer flow
    /// entirely, as the mod's own button does.</item>
    /// <item>Training: <see cref="SelectMaster"/> rides <c>ASelectMaster</c>, and
    /// <see cref="TrainOneStep"/> rides <c>ATrain</c>, which calls vanilla's gated
    /// <c>Pawn_TrainingTracker.Train</c>. The wanted checkbox and <c>CanAssignToTrain</c> are public
    /// vanilla members. <see cref="TrainingSteps"/> goes through BlockBio's own
    /// <c>GetTrainingSteps</c> wrapper rather than duplicating its reflection.</item>
    /// </list>
    ///
    /// <see cref="OpenViewGenes"/>/<see cref="OpenXenotypeEditor"/> construct the same dialogs
    /// <c>AShowXenoType</c>/<c>AConfigXenoType</c> construct. Both set
    /// <c>absorbInputAroundWindow</c>, so they are ordinary absorbing modal dialogs the generic
    /// reader covers.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static readonly LazyReflectionGate characterExtendedGate = new LazyReflectionGate("CharEditorCompat character extended", ResolveCharacterExtended);

        private static Type abilityToolType;
        private static Type pawnxToolType;
        private static Type dialogViewXenoGenesType;
        private static Type dialogXenoTypeType;

        private static MethodInfo bioCopyAbilitiesMethod;
        private static MethodInfo bioPasteAbilitiesMethod;
        private static MethodInfo bioRandomAbilitiesMethod;
        private static FieldInfo bioAbilityClipboardField;

        private static MethodInfo abilityToolSetEntropy;
        private static MethodInfo abilityToolSetPsyfocus;

        private static MethodInfo bioChangeIdeoMethod;
        private static MethodInfo bioSetFavColorMethod;
        private static FieldInfo bioColorDefsField;
        private static MethodInfo bioOnChangeMutantMethod;
        private static MethodInfo pawnxToolAllMutantDefsGetter;
        private static MethodInfo bioSetTitleMethod;
        private static MethodInfo bioRemoveTitleMethod;
        private static FieldInfo bioRoyalTitlesField;
        private static MethodInfo bioRecruitMethod;
        private static MethodInfo bioEnslaveMethod;

        private static ConstructorInfo viewXenoGenesCtor;
        private static ConstructorInfo xenoTypeCtor;

        private static FieldInfo bioColonistsField;
        private static MethodInfo bioSelectMasterMethod;
        private static MethodInfo bioGetTrainingStepsMethod;
        private static MethodInfo bioTrainMethod;

        private static object eTypeModsAbilityDef;

        /// <summary>True when every member the Abilities/Psycasts/Identity/Training rows need resolved.</summary>
        public static bool CharacterExtendedReady
        {
            get
            {
                gate.Ensure();
                return characterExtendedGate.Ensure();
            }
        }

        /// <summary>Runs after <see cref="BindCharacterTab"/>, reusing the members it bound.</summary>
        private static bool ResolveCharacterExtended(ReflectionSurface surface)
        {
            if (editorUIType == null || tabTypeType == null)
                return false;

            surface.Supplied("CharacterEditor.CEditor", ceditorType);
            surface.Supplied("EditorUI.BlockBio", blockBioType);
            surface.Supplied("CharacterEditor.EType", eTypeType);

            bioCopyAbilitiesMethod = surface.Method(blockBioType, "ACopyAbilities", Type.EmptyTypes);
            bioPasteAbilitiesMethod = surface.Method(blockBioType, "APasteAbilities", Type.EmptyTypes);
            bioRandomAbilitiesMethod = surface.Method(blockBioType, "ARandomAbilities", Type.EmptyTypes);
            bioAbilityClipboardField = surface.Field(blockBioType, "lCopyAbilities");

            bioChangeIdeoMethod = surface.Method(blockBioType, "AChangeIdeo", Type.EmptyTypes);
            bioSetFavColorMethod = surface.Method(blockBioType, "ASetFavColor", new[] { typeof(ColorDef) });
            bioColorDefsField = surface.Field(blockBioType, "lOfColorDefs");
            bioOnChangeMutantMethod = surface.Method(blockBioType, "OnChangeMutant", new[] { typeof(MutantDef) });
            bioSetTitleMethod = surface.Method(blockBioType, "ASetTitle", new[] { typeof(RoyalTitleDef) });
            bioRemoveTitleMethod = surface.Method(blockBioType, "ARemoveTitle", Type.EmptyTypes);
            bioRoyalTitlesField = surface.Field(blockBioType, "lOfRoyalTitles");
            bioRecruitMethod = surface.Method(blockBioType, "ARecruit", Type.EmptyTypes);
            bioEnslaveMethod = surface.Method(blockBioType, "AEnslave", Type.EmptyTypes);

            bioColonistsField = surface.Field(blockBioType, "lOfColonists");
            bioSelectMasterMethod = surface.Method(blockBioType, "ASelectMaster", new[] { typeof(Pawn) });
            bioGetTrainingStepsMethod = surface.Method(blockBioType, "GetTrainingSteps", new[] { typeof(TrainableDef) });
            bioTrainMethod = surface.Method(blockBioType, "ATrain", new[] { typeof(TrainableDef) });

            abilityToolType = surface.Type("CharacterEditor.AbilityTool");
            abilityToolSetEntropy = surface.Method(abilityToolType, "SetEntropy", new[] { typeof(Pawn), typeof(float) });
            abilityToolSetPsyfocus = surface.Method(abilityToolType, "SetPsyfocus", new[] { typeof(Pawn), typeof(float) });

            pawnxToolType = surface.Type("CharacterEditor.PawnxTool");
            pawnxToolAllMutantDefsGetter = surface.Property(pawnxToolType, "AllMutantDefs")?.GetGetMethod(true);

            dialogViewXenoGenesType = surface.Type("CharacterEditor.DialogViewXenoGenes");
            viewXenoGenesCtor = surface.Constructor(dialogViewXenoGenesType, new[] { typeof(Pawn) });

            dialogXenoTypeType = surface.Type("CharacterEditor.DialogXenoType");
            xenoTypeCtor = surface.Constructor(dialogXenoTypeType, new[] { typeof(Pawn) });

            // Closed by BindCharacterTab, which the core runs first.
            surface.Required("CEditor.Get<HashSet<string>>(EType) closed", ceditorGetHashSetString);
            eTypeModsAbilityDef = surface.Required("EType.ModsAbilityDef boxed", EnumValue(eTypeType, "ModsAbilityDef"));

            return surface.Ready;
        }

        // Abilities.

        public static void CopyAbilities(Window editorUI)
        {
            InvokeOnBlockBioExtended(editorUI, bioCopyAbilitiesMethod, "CopyAbilities");
        }

        public static void PasteAbilities(Window editorUI)
        {
            InvokeOnBlockBioExtended(editorUI, bioPasteAbilitiesMethod, "PasteAbilities");
        }

        /// <summary>Creation-mode dice: clears all abilities and gains 0-10 random ones.</summary>
        public static void RandomizeAbilities(Window editorUI)
        {
            InvokeOnBlockBioExtended(editorUI, bioRandomAbilitiesMethod, "RandomizeAbilities");
        }

        /// <summary>Whether the mod's own ability clipboard holds anything to paste.</summary>
        public static bool HasAbilityClipboard(Window editorUI)
        {
            if (!CharacterExtendedReady)
                return false;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return false;
                var list = bioAbilityClipboardField.GetValue(bio) as System.Collections.ICollection;
                return list != null && list.Count > 0;
            }
            catch (Exception ex)
            {
                Fail("HasAbilityClipboard", ex);
                return false;
            }
        }

        // Psycasts.

        /// <summary>Exact entropy write, the vehicle DrawPsycasts calls the instant its slider changes.</summary>
        public static void SetEntropy(Pawn pawn, float value)
        {
            InvokeStatic(abilityToolSetEntropy, pawn, value, "SetEntropy");
        }

        /// <summary>Exact psyfocus write as a 0-1 fraction; same vehicle as SetEntropy.</summary>
        public static void SetPsyfocus(Pawn pawn, float value)
        {
            InvokeStatic(abilityToolSetPsyfocus, pawn, value, "SetPsyfocus");
        }

        private static void InvokeStatic(MethodInfo method, Pawn pawn, float value, string caller)
        {
            if (!CharacterExtendedReady || method == null || pawn == null)
                return;
            try
            {
                method.Invoke(null, new object[] { pawn, value });
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        /// <summary>
        /// Every mod-name value the ability mod-name filter can select: the same live container
        /// DialogAddAbility's own dropdown reads, whatever "All"/null shape it carries.
        /// </summary>
        public static List<string> AbilityModNames()
        {
            var result = new List<string>();
            if (!CharacterExtendedReady)
                return result;
            try
            {
                object api = Api();
                if (api == null)
                    return result;
                var set = ceditorGetHashSetString.Invoke(api, new[] { eTypeModsAbilityDef }) as HashSet<string>;
                if (set != null)
                {
                    result.AddRange(set);
                }
            }
            catch (Exception ex)
            {
                Fail("AbilityModNames", ex);
            }
            return result;
        }

        // Identity.

        /// <summary>Opens Dialog_ConfigureIdeo via AChangeIdeo; see the class remarks for the DevMode-restore bug this rides unfixed.</summary>
        public static void ChangeIdeo(Window editorUI)
        {
            InvokeOnBlockBioExtended(editorUI, bioChangeIdeoMethod, "ChangeIdeo");
        }

        public static void SetFavoriteColor(Window editorUI, ColorDef color)
        {
            if (!CharacterExtendedReady)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio != null)
                    bioSetFavColorMethod.Invoke(bio, new object[] { color });
            }
            catch (Exception ex)
            {
                Fail("SetFavoriteColor", ex);
            }
        }

        /// <summary>The mod's own null-inclusive favorite-colour candidate list, snapshotted at BlockBio construction.</summary>
        public static List<ColorDef> FavoriteColorCandidates(Window editorUI)
        {
            return BlockBioHashSet<ColorDef>(editorUI, bioColorDefsField, "FavoriteColorCandidates");
        }

        public static void SetMutant(Window editorUI, MutantDef mutant)
        {
            if (!CharacterExtendedReady)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio != null)
                    bioOnChangeMutantMethod.Invoke(bio, new object[] { mutant });
            }
            catch (Exception ex)
            {
                Fail("SetMutant", ex);
            }
        }

        /// <summary>The mod's own all-mutants candidate list; static, so no BlockBio instance is needed.</summary>
        public static List<MutantDef> MutantCandidates()
        {
            var result = new List<MutantDef>();
            if (!CharacterExtendedReady)
                return result;
            try
            {
                var set = pawnxToolAllMutantDefsGetter.Invoke(null, null) as HashSet<MutantDef>;
                if (set != null)
                    result.AddRange(set);
            }
            catch (Exception ex)
            {
                Fail("MutantCandidates", ex);
            }
            return result;
        }

        public static void SetRoyalTitle(Window editorUI, RoyalTitleDef title)
        {
            if (!CharacterExtendedReady)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio != null)
                    bioSetTitleMethod.Invoke(bio, new object[] { title });
            }
            catch (Exception ex)
            {
                Fail("SetRoyalTitle", ex);
            }
        }

        public static void RemoveRoyalTitle(Window editorUI)
        {
            InvokeOnBlockBioExtended(editorUI, bioRemoveTitleMethod, "RemoveRoyalTitle");
        }

        /// <summary>The mod's own null-inclusive royal-title candidate list.</summary>
        public static List<RoyalTitleDef> RoyalTitleCandidates(Window editorUI)
        {
            return BlockBioHashSet<RoyalTitleDef>(editorUI, bioRoyalTitlesField, "RoyalTitleCandidates");
        }

        /// <summary>Instant, no-confirm recruit via ARecruit, which bypasses vanilla's resistance/timer flow entirely.</summary>
        public static void Recruit(Window editorUI)
        {
            InvokeOnBlockBioExtended(editorUI, bioRecruitMethod, "Recruit");
        }

        /// <summary>Instant, no-confirm enslave via AEnslave; the same bypass as Recruit.</summary>
        public static void Enslave(Window editorUI)
        {
            InvokeOnBlockBioExtended(editorUI, bioEnslaveMethod, "Enslave");
        }

        /// <summary>Opens DialogViewXenoGenes, the icon button's own vehicle.</summary>
        public static void OpenViewGenes(Pawn pawn)
        {
            OpenDialog(viewXenoGenesCtor, pawn, "OpenViewGenes");
        }

        /// <summary>Opens DialogXenoType, the label button's own vehicle.</summary>
        public static void OpenXenotypeEditor(Pawn pawn)
        {
            OpenDialog(xenoTypeCtor, pawn, "OpenXenotypeEditor");
        }

        private static void OpenDialog(ConstructorInfo ctor, Pawn pawn, string caller)
        {
            if (!CharacterExtendedReady || ctor == null || pawn == null)
                return;
            try
            {
                var window = (Window)ctor.Invoke(new object[] { pawn });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // Training.

        /// <summary>
        /// The mod's own colonist candidate list for the Master picker: a snapshot taken once at
        /// BlockBio construction, so it can go stale if colonists join or leave after the editor
        /// opens. Preserved as-is, matching the mod's own dropdown.
        /// </summary>
        public static List<Pawn> MasterCandidates(Window editorUI)
        {
            if (!CharacterExtendedReady)
                return new List<Pawn>();
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return new List<Pawn>();
                return bioColonistsField.GetValue(bio) as List<Pawn> ?? new List<Pawn>();
            }
            catch (Exception ex)
            {
                Fail("MasterCandidates", ex);
                return new List<Pawn>();
            }
        }

        public static void SelectMaster(Window editorUI, Pawn master)
        {
            if (!CharacterExtendedReady)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio != null)
                    bioSelectMasterMethod.Invoke(bio, new object[] { master });
            }
            catch (Exception ex)
            {
                Fail("SelectMaster", ex);
            }
        }

        /// <summary>Current step count for one trainable, via the mod's own wrapper over vanilla's private GetSteps.</summary>
        public static int TrainingSteps(Window editorUI, TrainableDef def)
        {
            if (!CharacterExtendedReady || def == null)
                return 0;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return 0;
                object result = bioGetTrainingStepsMethod.Invoke(bio, new object[] { def });
                return result is int steps ? steps : 0;
            }
            catch (Exception ex)
            {
                Fail("TrainingSteps", ex);
                return 0;
            }
        }

        /// <summary>Trains one step via ATrain, which calls vanilla's gated Pawn_TrainingTracker.Train.</summary>
        public static void TrainOneStep(Window editorUI, TrainableDef def)
        {
            if (!CharacterExtendedReady || def == null)
                return;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio != null)
                    bioTrainMethod.Invoke(bio, new object[] { def });
            }
            catch (Exception ex)
            {
                Fail("TrainOneStep", ex);
            }
        }

        // Shared helpers.

        private static List<T> BlockBioHashSet<T>(Window editorUI, FieldInfo field, string caller)
        {
            var result = new List<T>();
            if (!CharacterExtendedReady || field == null)
                return result;
            try
            {
                object bio = BlockBio(editorUI);
                if (bio == null)
                    return result;
                var set = field.GetValue(bio) as HashSet<T>;
                if (set != null)
                    result.AddRange(set);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
            return result;
        }

        private static void InvokeOnBlockBioExtended(Window editorUI, MethodInfo method, string caller)
        {
            if (!CharacterExtendedReady || method == null)
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
