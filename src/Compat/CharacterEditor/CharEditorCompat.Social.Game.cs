using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <c>EditorUI+BlockSocial</c>'s mental state and inspiration pickers plus its guided
    /// relation-builder flow: the Relation combo first, then the pawn slot rows the mod reveals for
    /// that relation. Gated by its own <see cref="SocialReady"/>.
    ///
    /// The relation builder runs per-frame in the mod: <c>BlockSocial.DrawRelations</c> calls one of
    /// five private no-arg <c>Handle*</c> methods every draw pass whenever <c>selRelation1</c> is one
    /// of the five supported implied defs, recomputing which slots show, which gender toggles are
    /// offered, auto-filling inferable slots, clearing collisions and setting <c>canAddRelation</c>.
    /// Those are pure state methods (no <c>Widgets</c>/<c>GUI</c> calls) and safe off a draw frame,
    /// unlike <c>Draw</c>/<c>DrawRelations</c> themselves. <see cref="ReconcileRelationBuilder"/> is
    /// this facade's per-frame equivalent: call it once before every relation-builder rebuild.
    /// For a NON-implied relation it reproduces <c>DrawRelations</c>' inline "if the chosen pawn is
    /// the edited pawn, clear it" guard against <c>SelectedPawn</c>'s public setter.
    ///
    /// MUTATION VEHICLES (vehicle A unless noted):
    /// <list type="bullet">
    /// <item><see cref="SetRelationChoice"/> invokes <c>AOnRelationSelected(PawnRelationDef)</c>, the
    /// combo's own picker callback.</item>
    /// <item><see cref="ToggleSlotGender"/> invokes the matching <c>AOnGenderChange[N]()</c>, which
    /// also clears that slot's pawn selection as the mod's own toggle does.</item>
    /// <item><see cref="OpenSlotPicker"/> invokes <c>AOnImgAction()</c> (slot 1) or
    /// <c>AOnParent[N]Selected()</c> (slots 2-4), each opening <c>DialogChoosePawn</c> with that
    /// slot's id, already registered against <see cref="Shell.CharEditorBrowserScope"/>'s
    /// <c>ChoosePawnAdapter</c>.</item>
    /// <item><see cref="AddRelation"/>/<see cref="AddImpliedRelation"/> invoke <c>AAddRelation()</c>/
    /// <c>AAddIndirectRelation()</c>, the confirm button's own chain of vanilla
    /// <c>AddDirectRelation</c> calls followed by a slot clear.</item>
    /// <item><see cref="OpenAddSocialThought"/> invokes <c>AAddThought()</c>, which opens
    /// <c>DialogAddThought(Label.TH_SOCIAL)</c> — the same dialog type registered once against
    /// <see cref="CharEditorThoughtBrowserCompat"/>.</item>
    /// <item><see cref="SetMentalState"/>/<see cref="SetInspiration"/> invoke
    /// <c>AChangeMentalState</c>/<c>AChangeInspiration</c>.</item>
    /// <item><see cref="SwitchToPawn"/> rides <see cref="CharEditorCompat.SelectPawn"/> rather than
    /// BlockSocial's <c>ASelectOtherPawn()</c>: both write the identical <c>CEditor.Pawn</c>
    /// property, and SelectPawn is the vehicle every other pawn-switch path uses.</item>
    /// <item>Removing a direct relation and adding a non-implied one ride public vanilla API with no
    /// reflection — see <see cref="Shell.CharacterEditorScope"/>'s Social-section reads.</item>
    /// </list>
    ///
    /// <see cref="SlotCaption"/> rides BlockSocial's own private <c>GetLabelSelectedPawn2/3/4</c>
    /// rather than re-deriving the gender/relation-branch formula.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static readonly LazyReflectionGate socialGate = new LazyReflectionGate("CharEditorCompat social", ResolveSocial);

        private static Type blockSocialType;

        private static MethodInfo getBlockSocial;

        private static PropertyInfo socialPawn1Property;
        private static PropertyInfo socialPawn2Property;
        private static PropertyInfo socialPawn3Property;
        private static PropertyInfo socialPawn4Property;

        private static FieldInfo socialSelRelation1Field;
        private static FieldInfo socialSelRelation2Field;
        private static FieldInfo socialSelRelation3Field;
        private static FieldInfo socialSelRelation4Field;
        private static FieldInfo socialGender1Field;
        private static FieldInfo socialGender2Field;
        private static FieldInfo socialGender3Field;
        private static FieldInfo socialGender4Field;
        private static FieldInfo socialAllowGender2Field;
        private static FieldInfo socialAllowGender3Field;
        private static FieldInfo socialAllowGender4Field;
        private static FieldInfo socialShowPawn3Field;
        private static FieldInfo socialShowPawn4Field;
        private static FieldInfo socialCanAddRelationField;
        private static FieldInfo socialRelationCandidatesField;

        private static MethodInfo socialRelationSelectedMethod;
        private static MethodInfo socialGenderChange1Method;
        private static MethodInfo socialGenderChange2Method;
        private static MethodInfo socialGenderChange3Method;
        private static MethodInfo socialGenderChange4Method;
        private static MethodInfo socialImgActionMethod;
        private static MethodInfo socialParent2SelectedMethod;
        private static MethodInfo socialParent3SelectedMethod;
        private static MethodInfo socialParent4SelectedMethod;
        private static MethodInfo socialAddRelationMethod;
        private static MethodInfo socialAddIndirectRelationMethod;
        private static MethodInfo socialAddThoughtMethod;
        private static MethodInfo socialChangeMentalStateMethod;
        private static MethodInfo socialChangeInspirationMethod;
        private static MethodInfo socialHandleChildMethod;
        private static MethodInfo socialHandleSiblingMethod;
        private static MethodInfo socialHandleHalfSiblingMethod;
        private static MethodInfo socialHandleGrandparentMethod;
        private static MethodInfo socialHandleUncleMethod;
        private static MethodInfo socialLabelSlot2Method;
        private static MethodInfo socialLabelSlot3Method;
        private static MethodInfo socialLabelSlot4Method;

        private static MethodInfo listOfMentalStatesMethod;
        private static MethodInfo listOfInspirationsMethod;

        /// <summary>True when every member this slice's Social section needs resolved.</summary>
        public static bool SocialReady
        {
            get
            {
                gate.Ensure();
                return socialGate.Ensure();
            }
        }

        private static bool ResolveSocial(ReflectionSurface surface)
        {
            if (editorUIType == null || tabTypeType == null)
                return false;

            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            blockSocialType = surface.Supplied("EditorUI.BlockSocial", editorUIType.GetNestedType("BlockSocial", NestedFlags));

            getBlockSocial = surface.Required("EditorUI.Get<BlockSocial>(TabType) closed",
                CloseGeneric(editorUIType, "Get", blockSocialType));

            socialPawn1Property = surface.Property(blockSocialType, "SelectedPawn");
            socialPawn2Property = surface.Property(blockSocialType, "SelectedPawn2");
            socialPawn3Property = surface.Property(blockSocialType, "SelectedPawn3");
            socialPawn4Property = surface.Property(blockSocialType, "SelectedPawn4");

            socialSelRelation1Field = surface.Field(blockSocialType, "selRelation1");
            socialSelRelation2Field = surface.Field(blockSocialType, "selRelation2");
            socialSelRelation3Field = surface.Field(blockSocialType, "selRelation3");
            socialSelRelation4Field = surface.Field(blockSocialType, "selRelation4");
            socialGender1Field = surface.Field(blockSocialType, "selectedGender");
            socialGender2Field = surface.Field(blockSocialType, "selectedGender2");
            socialGender3Field = surface.Field(blockSocialType, "selectedGender3");
            socialGender4Field = surface.Field(blockSocialType, "selectedGender4");
            socialAllowGender2Field = surface.Field(blockSocialType, "allowGender2");
            socialAllowGender3Field = surface.Field(blockSocialType, "allowGender3");
            socialAllowGender4Field = surface.Field(blockSocialType, "allowGender4");
            socialShowPawn3Field = surface.Field(blockSocialType, "showPawn3");
            socialShowPawn4Field = surface.Field(blockSocialType, "showPawn4");
            socialCanAddRelationField = surface.Field(blockSocialType, "canAddRelation");
            socialRelationCandidatesField = surface.Field(blockSocialType, "lOfRelations");

            socialRelationSelectedMethod = surface.Method(blockSocialType, "AOnRelationSelected", new[] { typeof(PawnRelationDef) });
            socialGenderChange1Method = surface.Method(blockSocialType, "AOnGenderChange", Type.EmptyTypes);
            socialGenderChange2Method = surface.Method(blockSocialType, "AOnGenderChange2", Type.EmptyTypes);
            socialGenderChange3Method = surface.Method(blockSocialType, "AOnGenderChange3", Type.EmptyTypes);
            socialGenderChange4Method = surface.Method(blockSocialType, "AOnGenderChange4", Type.EmptyTypes);
            socialImgActionMethod = surface.Method(blockSocialType, "AOnImgAction", Type.EmptyTypes);
            socialParent2SelectedMethod = surface.Method(blockSocialType, "AOnParent2Selected", Type.EmptyTypes);
            socialParent3SelectedMethod = surface.Method(blockSocialType, "AOnParent3Selected", Type.EmptyTypes);
            socialParent4SelectedMethod = surface.Method(blockSocialType, "AOnParent4Selected", Type.EmptyTypes);
            socialAddRelationMethod = surface.Method(blockSocialType, "AAddRelation", Type.EmptyTypes);
            socialAddIndirectRelationMethod = surface.Method(blockSocialType, "AAddIndirectRelation", Type.EmptyTypes);
            socialAddThoughtMethod = surface.Method(blockSocialType, "AAddThought", Type.EmptyTypes);
            socialChangeMentalStateMethod = surface.Method(blockSocialType, "AChangeMentalState", new[] { typeof(MentalStateDef) });
            socialChangeInspirationMethod = surface.Method(blockSocialType, "AChangeInspiration", new[] { typeof(InspirationDef) });
            socialHandleChildMethod = surface.Method(blockSocialType, "HandleChild", Type.EmptyTypes);
            socialHandleSiblingMethod = surface.Method(blockSocialType, "HandleSibling", Type.EmptyTypes);
            socialHandleHalfSiblingMethod = surface.Method(blockSocialType, "HandleHalfSibling", Type.EmptyTypes);
            socialHandleGrandparentMethod = surface.Method(blockSocialType, "HandleGrandparent", Type.EmptyTypes);
            socialHandleUncleMethod = surface.Method(blockSocialType, "HandleUncle", Type.EmptyTypes);
            socialLabelSlot2Method = surface.Method(blockSocialType, "GetLabelSelectedPawn2", new[] { typeof(PawnRelationDef) });
            socialLabelSlot3Method = surface.Method(blockSocialType, "GetLabelSelectedPawn3", new[] { typeof(PawnRelationDef) });
            socialLabelSlot4Method = surface.Method(blockSocialType, "GetLabelSelectedPawn4", new[] { typeof(PawnRelationDef) });

            listOfMentalStatesMethod = surface.Required("CEditor.ListOf<MentalStateDef>(EType) closed",
                CloseGeneric(ceditorType, "ListOf", typeof(MentalStateDef)));
            listOfInspirationsMethod = surface.Required("CEditor.ListOf<InspirationDef>(EType) closed",
                CloseGeneric(ceditorType, "ListOf", typeof(InspirationDef)));

            return surface.Ready;
        }

        private static object SocialBlock(Window editorUI)
        {
            return Block(editorUI, getBlockSocial, "BlockSocial");
        }

        private static void InvokeOnSocialBlock(Window editorUI, MethodInfo method, object[] args, string caller)
        {
            if (!SocialReady || method == null)
                return;
            try
            {
                object block = SocialBlock(editorUI);
                if (block != null)
                    method.Invoke(block, args);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // State (mental state / inspiration).

        public static List<MentalStateDef> MentalStateCandidates()
        {
            if (!SocialReady)
                return new List<MentalStateDef>();
            try
            {
                object api = Api();
                return api != null ? listOfMentalStatesMethod.Invoke(api, new object[] { EnumValue(eTypeType, "MentalStates") }) as List<MentalStateDef> ?? new List<MentalStateDef>() : new List<MentalStateDef>();
            }
            catch (Exception ex) { Fail("MentalStateCandidates", ex); return new List<MentalStateDef>(); }
        }

        public static List<InspirationDef> InspirationCandidates()
        {
            if (!SocialReady)
                return new List<InspirationDef>();
            try
            {
                object api = Api();
                return api != null ? listOfInspirationsMethod.Invoke(api, new object[] { EnumValue(eTypeType, "Inspirations") }) as List<InspirationDef> ?? new List<InspirationDef>() : new List<InspirationDef>();
            }
            catch (Exception ex) { Fail("InspirationCandidates", ex); return new List<InspirationDef>(); }
        }

        /// <summary>BlockSocial.AChangeMentalState(MentalStateDef) -- ends any current state first, SocialFighting picks a random free colonist to fight, matching the mod's own branch.</summary>
        public static void SetMentalState(Window editorUI, MentalStateDef def) =>
            InvokeOnSocialBlock(editorUI, socialChangeMentalStateMethod, new object[] { def }, "SetMentalState");

        /// <summary>BlockSocial.AChangeInspiration(InspirationDef) -- None ends the current inspiration.</summary>
        public static void SetInspiration(Window editorUI, InspirationDef def) =>
            InvokeOnSocialBlock(editorUI, socialChangeInspirationMethod, new object[] { def }, "SetInspiration");

        // Relation builder.

        /// <summary>The relation combo's fixed candidate list -- every non-implied PawnRelationDef plus the five explicitly-supported implied ones (BlockSocial.lOfRelations, frozen at construction).</summary>
        public static List<PawnRelationDef> RelationCandidates(Window editorUI)
        {
            if (!SocialReady || editorUI == null)
                return new List<PawnRelationDef>();
            try
            {
                object block = SocialBlock(editorUI);
                return block != null ? socialRelationCandidatesField.GetValue(block) as List<PawnRelationDef> ?? new List<PawnRelationDef>() : new List<PawnRelationDef>();
            }
            catch (Exception ex) { Fail("RelationCandidates", ex); return new List<PawnRelationDef>(); }
        }

        public static PawnRelationDef RelationChoice(Window editorUI) => SocialField<PawnRelationDef>(editorUI, socialSelRelation1Field);

        public static Gender SlotGender(Window editorUI, int slot)
        {
            FieldInfo field;
            if (slot == 1) field = socialGender1Field;
            else if (slot == 2) field = socialGender2Field;
            else if (slot == 3) field = socialGender3Field;
            else field = socialGender4Field;
            return SocialField<Gender>(editorUI, field);
        }

        public static bool SlotGenderToggleAllowed(int slotFromTwoToFour, Window editorUI)
        {
            FieldInfo field;
            if (slotFromTwoToFour == 2) field = socialAllowGender2Field;
            else if (slotFromTwoToFour == 3) field = socialAllowGender3Field;
            else field = socialAllowGender4Field;
            return SocialField<bool>(editorUI, field);
        }

        public static bool SlotVisible(int slotThreeOrFour, Window editorUI)
        {
            FieldInfo field = slotThreeOrFour == 3 ? socialShowPawn3Field : socialShowPawn4Field;
            return SocialField<bool>(editorUI, field);
        }

        /// <summary>True when the implied-relation confirm gate passes -- BlockSocial.canAddRelation, freshened by ReconcileRelationBuilder just before this is read.</summary>
        public static bool ImpliedRelationReady(Window editorUI) => SocialField<bool>(editorUI, socialCanAddRelationField);

        public static Pawn SlotPawn(Window editorUI, int slot)
        {
            PropertyInfo prop;
            if (slot == 1) prop = socialPawn1Property;
            else if (slot == 2) prop = socialPawn2Property;
            else if (slot == 3) prop = socialPawn3Property;
            else prop = socialPawn4Property;
            if (!SocialReady || editorUI == null)
                return null;
            try
            {
                object block = SocialBlock(editorUI);
                return block != null ? prop.GetValue(block) as Pawn : null;
            }
            catch (Exception ex) { Fail("SlotPawn", ex); return null; }
        }

        /// <summary>BlockSocial.GetLabelSelectedPawn2/3/4(PawnRelationDef) -- the "Parent(name)" style caption the mod draws above an auto-filled implied slot; slot 1 has no such caption (its identity IS the chosen top-level relation).</summary>
        public static string SlotCaption(Window editorUI, int slotFromTwoToFour)
        {
            if (!SocialReady || editorUI == null)
                return "";
            MethodInfo method;
            FieldInfo relField;
            if (slotFromTwoToFour == 2) { method = socialLabelSlot2Method; relField = socialSelRelation2Field; }
            else if (slotFromTwoToFour == 3) { method = socialLabelSlot3Method; relField = socialSelRelation3Field; }
            else { method = socialLabelSlot4Method; relField = socialSelRelation4Field; }
            try
            {
                object block = SocialBlock(editorUI);
                if (block == null)
                    return "";
                object relation = relField.GetValue(block);
                return method.Invoke(block, new[] { relation }) as string ?? "";
            }
            catch (Exception ex) { Fail("SlotCaption", ex); return ""; }
        }

        private static T SocialField<T>(Window editorUI, FieldInfo field)
        {
            if (!SocialReady || editorUI == null || field == null)
                return default;
            try
            {
                object block = SocialBlock(editorUI);
                return block != null && field.GetValue(block) is T value ? value : default;
            }
            catch (Exception ex) { Fail("SocialField", ex); return default; }
        }

        /// <summary>
        /// Freshens the relation builder's derived state before every rebuild: invokes the matching
        /// <c>Handle*</c> method for an implied relation, or reproduces the mod's inline self-pick
        /// guard for a non-implied one.
        /// </summary>
        public static void ReconcileRelationBuilder(Window editorUI)
        {
            if (!SocialReady || editorUI == null)
                return;
            try
            {
                object block = SocialBlock(editorUI);
                if (block == null)
                    return;
                var relation1 = socialSelRelation1Field.GetValue(block) as PawnRelationDef;
                if (relation1 != null && relation1.implied)
                {
                    MethodInfo handler = HandlerForImpliedRelation(relation1);
                    handler?.Invoke(block, null);
                    return;
                }
                // Mirrors DrawRelations' own inline guard through BlockSocial's PUBLIC
                // IPawnable.SelectedPawn setter, the same one DialogChoosePawn.DoAndClose writes.
                Pawn current = CurrentPawn;
                var selected = socialPawn1Property.GetValue(block) as Pawn;
                if (current != null && ReferenceEquals(current, selected))
                {
                    // MUTATION-C: reflected PUBLIC property setter, not a private field --
                    // marked only because the ratchet's mechanical reflection-write count cannot
                    // tell a reflected property accessor from a reflected raw field write.
                    socialPawn1Property.SetValue(block, null);
                }
            }
            catch (Exception ex)
            {
                Fail("ReconcileRelationBuilder", ex);
            }
        }

        private static MethodInfo HandlerForImpliedRelation(PawnRelationDef relation)
        {
            if (relation == PawnRelationDefOf.Child) return socialHandleChildMethod;
            if (relation == PawnRelationDefOf.Sibling) return socialHandleSiblingMethod;
            if (relation == PawnRelationDefOf.HalfSibling) return socialHandleHalfSiblingMethod;
            if (relation == PawnRelationDefOf.Grandparent) return socialHandleGrandparentMethod;
            if (relation == PawnRelationDefOf.UncleOrAunt) return socialHandleUncleMethod;
            return null;
        }

        /// <summary>BlockSocial.AOnRelationSelected(PawnRelationDef) -- the relation combo's own picker callback; resets every slot exactly as the mod's own float menu selection does.</summary>
        public static void SetRelationChoice(Window editorUI, PawnRelationDef relation) =>
            InvokeOnSocialBlock(editorUI, socialRelationSelectedMethod, new object[] { relation }, "SetRelationChoice");

        /// <summary>Flips the gender toggle for slot 1 (BlockSocial.AOnGenderChange()) -- also clears that slot's chosen pawn, matching the mod's own toggle.</summary>
        public static void ToggleSlot1Gender(Window editorUI) => InvokeOnSocialBlock(editorUI, socialGenderChange1Method, null, "ToggleSlot1Gender");

        public static void ToggleSlotGender(Window editorUI, int slotFromTwoToFour)
        {
            MethodInfo method;
            if (slotFromTwoToFour == 2) method = socialGenderChange2Method;
            else if (slotFromTwoToFour == 3) method = socialGenderChange3Method;
            else method = socialGenderChange4Method;
            InvokeOnSocialBlock(editorUI, method, null, "ToggleSlotGender");
        }

        /// <summary>Opens DialogChoosePawn for slot 1 (BlockSocial.AOnImgAction()); the ChoosePawnAdapter registration covers this dialog generically.</summary>
        public static void OpenSlot1Picker(Window editorUI) => InvokeOnSocialBlock(editorUI, socialImgActionMethod, null, "OpenSlot1Picker");

        public static void OpenSlotPicker(Window editorUI, int slotFromTwoToFour)
        {
            MethodInfo method;
            if (slotFromTwoToFour == 2) method = socialParent2SelectedMethod;
            else if (slotFromTwoToFour == 3) method = socialParent3SelectedMethod;
            else method = socialParent4SelectedMethod;
            InvokeOnSocialBlock(editorUI, method, null, "OpenSlotPicker");
        }

        /// <summary>Confirms a NON-implied relation -- BlockSocial.AAddRelation() (vanilla AddDirectRelation, then clears every slot).</summary>
        public static void AddRelation(Window editorUI) => InvokeOnSocialBlock(editorUI, socialAddRelationMethod, null, "AddRelation");

        /// <summary>Confirms an IMPLIED relation -- BlockSocial.AAddIndirectRelation() (the correct chain of vanilla AddDirectRelation calls for the chosen relation type, then clears every slot).</summary>
        public static void AddImpliedRelation(Window editorUI) => InvokeOnSocialBlock(editorUI, socialAddIndirectRelationMethod, null, "AddImpliedRelation");

        /// <summary>Opens DialogAddThought(Label.TH_SOCIAL) -- BlockSocial's own "Add thought" icon, preset to the Social type filter.</summary>
        public static void OpenAddSocialThought(Window editorUI) => InvokeOnSocialBlock(editorUI, socialAddThoughtMethod, null, "OpenAddSocialThought");
    }
}
