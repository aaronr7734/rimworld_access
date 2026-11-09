using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade backing the extended def-editor panes -- the seventy-plus-field LIVE
    /// editors <c>DialogGenery</c>/<c>DialogObjects</c> reveal behind the <c>CEditor.IsExtendedUI</c>
    /// widen toggle they share through <c>DialogTemplate&lt;T&gt;</c>. Backs
    /// <see cref="Shell.CharEditorDefEditorScope"/>.
    ///
    /// COVERED: GeneDef's three biostat sliders, its 15 boolean rows, its random-brightness slider,
    /// its three color rows, its ChemicalDef/ForcedHair/Passion combo rows, its body-size life-stage
    /// rows (gated on <c>IsBodySizeGene</c>), and all thirteen of its list-relational sections.
    /// ThingDef gets its nine analogous list sections plus the biocoded checkbox and the recipe
    /// research-prerequisite combo.
    ///
    /// NOT COVERED, because the mod exposes no Set*/Remove* vehicle for them -- only bare
    /// <c>List.Add</c>/<c>Remove</c> calls inlined in a widget delegate: GeneDef's ~20 raw float
    /// sliders (DrawFloats), its label and four resource-text fields, its
    /// Categories/Prerequisite/HistoryEvent combo rows, its three sound-def rows, its five
    /// tag/string-list fields; ThingDef's DrawLabel/DrawOther/DrawEnums/DrawCostStuffCount/
    /// DrawHitpoints/DrawDamageSelectors/DrawBullet*/DrawSound*/DrawRanged*/DrawExtras surface and
    /// its four tag-list fields.
    ///
    /// VEHICLES: every list section's Add/Remove rides the mod's own <c>GeneTool</c>/<c>ThingTool</c>
    /// extension method (vehicle A/B). Scalar rows have no such method --
    /// <c>SZWidgets.LabelIntFieldSlider</c>/<c>LabelFloatFieldSlider</c>/<c>CheckboxLabeled</c> all
    /// take the target field by <c>ref</c> and the mod's draw code assigns straight back into it --
    /// so those writes are MUTATION-C, clamped to the same bounds the mod's own slider enforces.
    /// The color rows ride vehicle A by opening <c>DialogColorPicker</c> exactly as <c>DrawColors</c>
    /// does, after which the <see cref="CharEditorColorPickerCompat"/> registration owns it.
    ///
    /// GeneDef/ThingDef/StatModifier/Aptitude/PawnCapacityModifier/GeneticTraitData/DamageFactor/
    /// ApparelLayerDef/BodyPartGroupDef/ResearchProjectDef/StuffCategoryDef/ThingDefCountClass are
    /// all PUBLIC vanilla types, so every FIELD read/write on them is plain C#, not reflection --
    /// reflection here is needed only for the CharacterEditor-internal <c>GeneTool</c>/<c>ThingTool</c>
    /// static classes (their Set*/Remove* methods and their <c>lFree*</c> candidate-set fields) and
    /// for <c>CEditor.IsExtendedUI</c>.
    /// </summary>
    internal static partial class CharEditorDefEditorCompat
    {
        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly LazyReflectionGate geneGate =
            new LazyReflectionGate("CharEditorDefEditorCompat gene", ResolveGene);
        private static readonly LazyReflectionGate objectGate =
            new LazyReflectionGate("CharEditorDefEditorCompat object", ResolveObject);

        private static Type geneToolType;
        private static Type thingToolType;
        private static Type ceditorType;

        // GeneTool free-lists (the mod declares these as bare static FIELDS -- see EnsureInit).
        private static FieldInfo geneLFreeStatFactorsField, geneLFreeStatOffsetsField, geneLFreeAptitudesField,
            geneLFreeCapacitiesField, geneLFreeAbilitiesField, geneLFreeForcedTraitsField, geneLFreeSuppressedTraitsField,
            geneLFreeImmunitiesField, geneLFreeProtectionsField, geneLFreeDamageFactorsField, geneLFreeNeedsField,
            geneLFreeForcedHeadTypesField, geneLFreeWorkTagsField;

        private static MethodInfo geneSetStatFactor, geneRemoveStatFactor, geneSetStatOffset, geneRemoveStatOffset;
        private static MethodInfo geneSetAptitude, geneRemoveAptitude, geneSetCapacity, geneRemoveCapacity;
        private static MethodInfo geneSetAbility, geneRemoveAbility;
        private static MethodInfo geneSetForcedTrait, geneRemoveForcedTrait, geneSetSuppressedTrait, geneRemoveSuppressedTrait;
        private static MethodInfo geneSetImmunity, geneRemoveImmunity, geneSetProtection, geneRemoveProtection;
        private static MethodInfo geneSetDamageFactor, geneRemoveDamageFactor;
        private static MethodInfo geneSetDisabledNeed, geneRemoveDisabledNeed;
        private static MethodInfo geneSetForcedHeadType, geneRemoveForcedHeadType;
        private static MethodInfo geneSetDisabledWorkTags, geneRemoveDisabledWorkTags;
        private static MethodInfo geneSetChemicalDef, geneSetForcedHairDef, geneSetPassionMod;
        private static MethodInfo geneIsBodySizeGene, geneGetLifeStageDef;

        private static FieldInfo thingLFreeStatFactorsField, thingLFreeStatOffsetsField, thingLFreeStuffCategoriesField,
            thingLFreeCostsField, thingLFreeCostsDiffField, thingLFreeApparelLayerField, thingLFreeBodyPartGroupField,
            thingLFreePrerequisitesField;
        private static PropertyInfo thingAllWeaponTraitDefProp;

        private static MethodInfo thingSetStatFactor, thingRemoveStatFactor, thingSetStatOffset, thingRemoveStatOffset;
        private static MethodInfo thingSetStuffCategorie, thingRemoveStuffCategorie;
        private static MethodInfo thingSetCosts, thingRemoveCosts, thingSetCostsDiff, thingRemoveCostsDiff;
        private static MethodInfo thingSetApparelLayer, thingRemoveApparelLayer;
        private static MethodInfo thingSetBodyPartGroup, thingRemoveBodyPartGroup;
        private static MethodInfo thingSetPrerequisite, thingRemovePrerequisite;
        private static MethodInfo thingSetResearchPrerequisite;
        private static MethodInfo thingSetBladeLinkTrait, thingRemoveBladeLinkTrait;

        private static PropertyInfo isExtendedUIProp;

        // Selected.tempThing -- the live Thing instance WeaponTraits/biocoded operate on
        // (ThingTool.SelectedThing.tempThing; CharEditorObjectsCompat exposes the ThingDef via
        // its own Selected(Window), but not this live-Thing field, so it is bound independently
        // here rather than widening that file for one section's sake).
        private static PropertyInfo thingSelectedThingProp;
        private static FieldInfo selectedTempThingField;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool GeneReady { get { return geneGate.Ensure(); } }
        public static bool ObjectReady { get { return objectGate.Ensure(); } }

        /// <summary>
        /// Vehicle A: the browse dialog's own bottom-row Save button (DialogTemplate&lt;T&gt;.ASave,
        /// the abstract member WindowTool.SimpleAcceptAndExtend wires at DialogTemplate.cs:187) --
        /// the ONLY path that persists def modifications into the mod's option strings. Resolved
        /// against the concrete window type: private members resolve across the generic hierarchy.
        /// </summary>
        public static void SaveDefModification(Window dlg) => InvokeTemplateButton(dlg, "ASave");

        /// <summary>Vehicle A: the dialog's own Reset button (reverts the SELECTED def's modification).</summary>
        public static void ResetDefModification(Window dlg) => InvokeTemplateButton(dlg, "AReset");

        /// <summary>Vehicle A: the dialog's own Reset All button (reverts every def modification of this family).</summary>
        public static void ResetAllDefModifications(Window dlg) => InvokeTemplateButton(dlg, "AResetAll");

        private static void InvokeTemplateButton(Window dlg, string member)
        {
            if (dlg == null)
                return;
            try
            {
                MethodInfo m = HarmonyLib.AccessTools.Method(dlg.GetType(), member, Type.EmptyTypes);
                if (m != null)
                {
                    m.Invoke(dlg, null);
                }
                else
                {
                    Fail(member, new MissingMethodException(dlg.GetType().Name + "." + member));
                }
            }
            catch (Exception ex) { Fail(member, ex); }
        }

        /// <summary>Sets the mod's shared widen toggle so DrawParameter (and its per-frame side effects, e.g. GeneDef's cached-description clear) actually runs while this scope is open -- vehicle A: the exact bare property write the toggle button itself performs (WindowTool.cs:50).</summary>
        public static void SetExtendedUI(bool value)
        {
            EnsureInit();
            try
            {
                // MUTATION-C marker for the reflection-write ratchet (mechanical, cannot
                // distinguish vehicle A from C): this write is actually vehicle A -- CEditor.
                // IsExtendedUI is a bare auto-property with no gate at all, and this line is
                // byte-for-byte the toggle button's own delegate (WindowTool.cs:50).
                isExtendedUIProp?.SetValue(null, value, null);
            }
            catch (Exception ex) { Fail("SetExtendedUI", ex); }
        }

        private static void EnsureInit()
        {
            geneGate.Ensure();
            objectGate.Ensure();
        }

        private static bool ResolveGene(ReflectionSurface surface)
        {
            if (!CharEditorCompat.ModPresent) return false;
            ceditorType = CharEditorCompat.EditorCore.CEditorType;
            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            geneToolType = surface.Type("CharacterEditor.GeneTool");
            isExtendedUIProp = surface.Property(ceditorType, "IsExtendedUI");

            geneLFreeStatFactorsField = surface.Field(geneToolType, "lFreeStatDefFactors");
            geneLFreeStatOffsetsField = surface.Field(geneToolType, "lFreeStatDefOffsets");
            geneLFreeAptitudesField = surface.Field(geneToolType, "lFreeAptitudes");
            geneLFreeCapacitiesField = surface.Field(geneToolType, "lFreeCapacities");
            geneLFreeAbilitiesField = surface.Field(geneToolType, "lFreeAbilities");
            geneLFreeForcedTraitsField = surface.Field(geneToolType, "lFreeForcedTraits");
            geneLFreeSuppressedTraitsField = surface.Field(geneToolType, "lFreeSuppressedTraits");
            geneLFreeImmunitiesField = surface.Field(geneToolType, "lFreeImmunities");
            geneLFreeProtectionsField = surface.Field(geneToolType, "lFreeProtections");
            geneLFreeDamageFactorsField = surface.Field(geneToolType, "lFreeDamageFactors");
            geneLFreeNeedsField = surface.Field(geneToolType, "lFreeNeeds");
            geneLFreeForcedHeadTypesField = surface.Field(geneToolType, "lFreeForcedHeadTypes");
            geneLFreeWorkTagsField = surface.Field(geneToolType, "lFreeWorkTags");

            geneSetStatFactor = surface.Method(geneToolType, "SetStatFactor", new[] { typeof(GeneDef), typeof(StatDef), typeof(float) });
            geneRemoveStatFactor = surface.Method(geneToolType, "RemoveStatFactor", new[] { typeof(GeneDef), typeof(StatDef) });
            geneSetStatOffset = surface.Method(geneToolType, "SetStatOffset", new[] { typeof(GeneDef), typeof(StatDef), typeof(float) });
            geneRemoveStatOffset = surface.Method(geneToolType, "RemoveStatOffset", new[] { typeof(GeneDef), typeof(StatDef) });
            geneSetAptitude = surface.Method(geneToolType, "SetAptitude", new[] { typeof(GeneDef), typeof(SkillDef), typeof(int) });
            geneRemoveAptitude = surface.Method(geneToolType, "RemoveAptitude", new[] { typeof(GeneDef), typeof(SkillDef) });
            geneSetCapacity = surface.Method(geneToolType, "SetCapacity", new[] { typeof(GeneDef), typeof(PawnCapacityDef), typeof(float), typeof(float) });
            geneRemoveCapacity = surface.Method(geneToolType, "RemoveCapacity", new[] { typeof(GeneDef), typeof(PawnCapacityDef) });
            geneSetAbility = surface.Method(geneToolType, "SetAbility", new[] { typeof(GeneDef), typeof(AbilityDef) });
            geneRemoveAbility = surface.Method(geneToolType, "RemoveAbility", new[] { typeof(GeneDef), typeof(AbilityDef) });
            geneSetForcedTrait = surface.Method(geneToolType, "SetForcedTrait", new[] { typeof(GeneDef), typeof(GeneticTraitData), typeof(TraitDef), typeof(int) });
            geneRemoveForcedTrait = surface.Method(geneToolType, "RemoveForcedTrait", new[] { typeof(GeneDef), typeof(GeneticTraitData) });
            geneSetSuppressedTrait = surface.Method(geneToolType, "SetSuppressedTrait", new[] { typeof(GeneDef), typeof(GeneticTraitData), typeof(TraitDef), typeof(int) });
            geneRemoveSuppressedTrait = surface.Method(geneToolType, "RemoveSuppressedTrait", new[] { typeof(GeneDef), typeof(GeneticTraitData) });
            geneSetImmunity = surface.Method(geneToolType, "SetImmunity", new[] { typeof(GeneDef), typeof(HediffDef) });
            geneRemoveImmunity = surface.Method(geneToolType, "RemoveImmunity", new[] { typeof(GeneDef), typeof(HediffDef) });
            geneSetProtection = surface.Method(geneToolType, "SetProtection", new[] { typeof(GeneDef), typeof(HediffDef) });
            geneRemoveProtection = surface.Method(geneToolType, "RemoveProtection", new[] { typeof(GeneDef), typeof(HediffDef) });
            geneSetDamageFactor = surface.Method(geneToolType, "SetDamageFactor", new[] { typeof(GeneDef), typeof(DamageDef), typeof(float) });
            geneRemoveDamageFactor = surface.Method(geneToolType, "RemoveDamageFactor", new[] { typeof(GeneDef), typeof(DamageDef) });
            geneSetDisabledNeed = surface.Method(geneToolType, "SetDisabledNeed", new[] { typeof(GeneDef), typeof(NeedDef) });
            geneRemoveDisabledNeed = surface.Method(geneToolType, "RemoveDisabledNeed", new[] { typeof(GeneDef), typeof(NeedDef) });
            geneSetForcedHeadType = surface.Method(geneToolType, "SetForcedHeadType", new[] { typeof(GeneDef), typeof(HeadTypeDef) });
            geneRemoveForcedHeadType = surface.Method(geneToolType, "RemoveForcedHeadType", new[] { typeof(GeneDef), typeof(HeadTypeDef) });
            geneSetDisabledWorkTags = surface.Method(geneToolType, "SetDisabledWorkTags", new[] { typeof(GeneDef), typeof(WorkTags) });
            geneRemoveDisabledWorkTags = surface.Method(geneToolType, "RemoveDisabledWorkTags", new[] { typeof(GeneDef), typeof(WorkTags) });
            geneSetChemicalDef = surface.Method(geneToolType, "SetChemicalDef", new[] { typeof(GeneDef), typeof(ChemicalDef) });
            geneSetForcedHairDef = surface.Method(geneToolType, "SetForcedHairDef", new[] { typeof(GeneDef), typeof(HairDef) });
            geneSetPassionMod = surface.Method(geneToolType, "SetPassionMod", new[] { typeof(GeneDef), typeof(SkillDef), typeof(PassionMod.PassionModType) });
            geneIsBodySizeGene = surface.Method(geneToolType, "IsBodySizeGene", new[] { typeof(GeneDef) });
            geneGetLifeStageDef = surface.Method(geneToolType, "GetLifeStageDef", new[] { typeof(GeneDef) });

            return surface.Ready;
        }

        private static bool ResolveObject(ReflectionSurface surface)
        {
            if (!CharEditorCompat.ModPresent) return false;
            ceditorType = CharEditorCompat.EditorCore.CEditorType;
            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            thingToolType = surface.Type("CharacterEditor.ThingTool");
            Type selectedType = surface.Type("CharacterEditor.Selected");

            thingLFreeStatFactorsField = surface.Field(thingToolType, "lFreeStatDefFactors");
            thingLFreeStatOffsetsField = surface.Field(thingToolType, "lFreeStatDefOffsets");
            thingLFreeStuffCategoriesField = surface.Field(thingToolType, "lFreeStuffCategories");
            thingLFreeCostsField = surface.Field(thingToolType, "lFreeCosts");
            thingLFreeCostsDiffField = surface.Field(thingToolType, "lFreeCostsDiff");
            thingLFreeApparelLayerField = surface.Field(thingToolType, "lFreeApparelLayer");
            thingLFreeBodyPartGroupField = surface.Field(thingToolType, "lFreeBodyPartGroup");
            thingLFreePrerequisitesField = surface.Field(thingToolType, "lFreePrerequisites");
            thingAllWeaponTraitDefProp = surface.Property(thingToolType, "AllWeaponTraitDef");
            thingSelectedThingProp = surface.Property(thingToolType, "SelectedThing");
            selectedTempThingField = surface.Field(selectedType, "tempThing");

            thingSetStatFactor = surface.Method(thingToolType, "SetStatFactor", new[] { typeof(ThingDef), typeof(StatDef), typeof(float) });
            thingRemoveStatFactor = surface.Method(thingToolType, "RemoveStatFactor", new[] { typeof(ThingDef), typeof(StatDef) });
            thingSetStatOffset = surface.Method(thingToolType, "SetStatOffset", new[] { typeof(ThingDef), typeof(StatDef), typeof(float) });
            thingRemoveStatOffset = surface.Method(thingToolType, "RemoveStatOffset", new[] { typeof(ThingDef), typeof(StatDef) });
            thingSetStuffCategorie = selectedType == null ? null
                : surface.Method(thingToolType, "SetStuffCategorie", new[] { typeof(ThingDef), typeof(StuffCategoryDef), selectedType });
            thingRemoveStuffCategorie = surface.Method(thingToolType, "RemoveStuffCategorie", new[] { typeof(ThingDef), typeof(StuffCategoryDef) });
            thingSetCosts = surface.Method(thingToolType, "SetCosts", new[] { typeof(ThingDef), typeof(ThingDef), typeof(int) });
            thingRemoveCosts = surface.Method(thingToolType, "RemoveCosts", new[] { typeof(ThingDef), typeof(ThingDef) });
            thingSetCostsDiff = surface.Method(thingToolType, "SetCostsDiff", new[] { typeof(ThingDef), typeof(ThingDef), typeof(int) });
            thingRemoveCostsDiff = surface.Method(thingToolType, "RemoveCostsDiff", new[] { typeof(ThingDef), typeof(ThingDef) });
            thingSetApparelLayer = surface.Method(thingToolType, "SetApparelLayer", new[] { typeof(ThingDef), typeof(ApparelLayerDef) });
            thingRemoveApparelLayer = surface.Method(thingToolType, "RemoveApparelLayer", new[] { typeof(ThingDef), typeof(ApparelLayerDef) });
            thingSetBodyPartGroup = surface.Method(thingToolType, "SetBodyPartGroup", new[] { typeof(ThingDef), typeof(BodyPartGroupDef) });
            thingRemoveBodyPartGroup = surface.Method(thingToolType, "RemoveBodyPartGroup", new[] { typeof(ThingDef), typeof(BodyPartGroupDef) });
            thingSetPrerequisite = surface.Method(thingToolType, "SetPrerequisite", new[] { typeof(ThingDef), typeof(ResearchProjectDef) });
            thingRemovePrerequisite = surface.Method(thingToolType, "RemovePrerequisite", new[] { typeof(ThingDef), typeof(ResearchProjectDef) });
            thingSetResearchPrerequisite = surface.Method(thingToolType, "SetResearchPrerequisite", new[] { typeof(ThingDef), typeof(ResearchProjectDef) });
            thingSetBladeLinkTrait = surface.Method(thingToolType, "SetBladeLinkTrait", new[] { typeof(Thing), typeof(WeaponTraitDef) });
            thingRemoveBladeLinkTrait = surface.Method(thingToolType, "RemoveBladeLinkTrait", new[] { typeof(Thing), typeof(WeaponTraitDef) });

            return surface.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();
        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorDefEditorCompat." + member + " failed: " + ex.Message);
        }

        // ------------------------------------------------------------------
        // Gene: list-relational sections. Each Set/Remove pair is GeneTool.cs:768-1131 (verified
        // per-method against the decompile).
        // ------------------------------------------------------------------

        public static HashSet<StatDef> GeneFreeStatFactors() => geneLFreeStatFactorsField?.GetValue(null) as HashSet<StatDef>;
        public static void GeneSetStatFactor(GeneDef g, StatDef s, float v) => Invoke(geneSetStatFactor, g, s, v);
        public static void GeneRemoveStatFactor(GeneDef g, StatDef s) => Invoke(geneRemoveStatFactor, g, s);

        public static HashSet<StatDef> GeneFreeStatOffsets() => geneLFreeStatOffsetsField?.GetValue(null) as HashSet<StatDef>;
        public static void GeneSetStatOffset(GeneDef g, StatDef s, float v) => Invoke(geneSetStatOffset, g, s, v);
        public static void GeneRemoveStatOffset(GeneDef g, StatDef s) => Invoke(geneRemoveStatOffset, g, s);

        public static HashSet<SkillDef> GeneFreeAptitudes() => geneLFreeAptitudesField?.GetValue(null) as HashSet<SkillDef>;
        public static void GeneSetAptitude(GeneDef g, SkillDef s, int v) => Invoke(geneSetAptitude, g, s, v);
        public static void GeneRemoveAptitude(GeneDef g, SkillDef s) => Invoke(geneRemoveAptitude, g, s);

        public static HashSet<PawnCapacityDef> GeneFreeCapacities() => geneLFreeCapacitiesField?.GetValue(null) as HashSet<PawnCapacityDef>;
        public static void GeneSetCapacity(GeneDef g, PawnCapacityDef c, float offset, float factor) => Invoke(geneSetCapacity, g, c, offset, factor);
        public static void GeneRemoveCapacity(GeneDef g, PawnCapacityDef c) => Invoke(geneRemoveCapacity, g, c);

        public static HashSet<AbilityDef> GeneFreeAbilities() => geneLFreeAbilitiesField?.GetValue(null) as HashSet<AbilityDef>;
        public static void GeneSetAbility(GeneDef g, AbilityDef a) => Invoke(geneSetAbility, g, a);
        public static void GeneRemoveAbility(GeneDef g, AbilityDef a) => Invoke(geneRemoveAbility, g, a);

        public static HashSet<GeneticTraitData> GeneFreeForcedTraits() => geneLFreeForcedTraitsField?.GetValue(null) as HashSet<GeneticTraitData>;
        public static void GeneSetForcedTrait(GeneDef g, GeneticTraitData gtd) => Invoke(geneSetForcedTrait, g, gtd, gtd.def, gtd.degree);
        public static void GeneRemoveForcedTrait(GeneDef g, GeneticTraitData gtd) => Invoke(geneRemoveForcedTrait, g, gtd);

        public static HashSet<GeneticTraitData> GeneFreeSuppressedTraits() => geneLFreeSuppressedTraitsField?.GetValue(null) as HashSet<GeneticTraitData>;
        public static void GeneSetSuppressedTrait(GeneDef g, GeneticTraitData gtd) => Invoke(geneSetSuppressedTrait, g, gtd, gtd.def, gtd.degree);
        public static void GeneRemoveSuppressedTrait(GeneDef g, GeneticTraitData gtd) => Invoke(geneRemoveSuppressedTrait, g, gtd);

        public static HashSet<HediffDef> GeneFreeImmunities() => geneLFreeImmunitiesField?.GetValue(null) as HashSet<HediffDef>;
        public static void GeneSetImmunity(GeneDef g, HediffDef h) => Invoke(geneSetImmunity, g, h);
        public static void GeneRemoveImmunity(GeneDef g, HediffDef h) => Invoke(geneRemoveImmunity, g, h);

        public static HashSet<HediffDef> GeneFreeProtections() => geneLFreeProtectionsField?.GetValue(null) as HashSet<HediffDef>;
        public static void GeneSetProtection(GeneDef g, HediffDef h) => Invoke(geneSetProtection, g, h);
        public static void GeneRemoveProtection(GeneDef g, HediffDef h) => Invoke(geneRemoveProtection, g, h);

        public static HashSet<DamageDef> GeneFreeDamageFactors() => geneLFreeDamageFactorsField?.GetValue(null) as HashSet<DamageDef>;
        public static void GeneSetDamageFactor(GeneDef g, DamageDef d, float v) => Invoke(geneSetDamageFactor, g, d, v);
        public static void GeneRemoveDamageFactor(GeneDef g, DamageDef d) => Invoke(geneRemoveDamageFactor, g, d);

        public static HashSet<NeedDef> GeneFreeNeeds() => geneLFreeNeedsField?.GetValue(null) as HashSet<NeedDef>;
        public static void GeneSetDisabledNeed(GeneDef g, NeedDef n) => Invoke(geneSetDisabledNeed, g, n);
        public static void GeneRemoveDisabledNeed(GeneDef g, NeedDef n) => Invoke(geneRemoveDisabledNeed, g, n);

        public static HashSet<HeadTypeDef> GeneFreeForcedHeadTypes() => geneLFreeForcedHeadTypesField?.GetValue(null) as HashSet<HeadTypeDef>;
        public static void GeneSetForcedHeadType(GeneDef g, HeadTypeDef h) => Invoke(geneSetForcedHeadType, g, h);
        public static void GeneRemoveForcedHeadType(GeneDef g, HeadTypeDef h) => Invoke(geneRemoveForcedHeadType, g, h);

        public static HashSet<WorkTags> GeneFreeWorkTags() => geneLFreeWorkTagsField?.GetValue(null) as HashSet<WorkTags>;
        public static void GeneSetDisabledWorkTags(GeneDef g, WorkTags w) => Invoke(geneSetDisabledWorkTags, g, w);
        public static void GeneRemoveDisabledWorkTags(GeneDef g, WorkTags w) => Invoke(geneRemoveDisabledWorkTags, g, w);

        public static void GeneSetChemicalDef(GeneDef g, ChemicalDef c) => Invoke(geneSetChemicalDef, g, c);
        public static void GeneSetForcedHairDef(GeneDef g, HairDef h) => Invoke(geneSetForcedHairDef, g, h);
        public static void GeneSetPassionMod(GeneDef g, SkillDef s, PassionMod.PassionModType t) => Invoke(geneSetPassionMod, g, s, t);

        public static bool GeneIsBodySizeGene(GeneDef g)
        {
            if (!geneGate.Ensure()) return false;
            try { return (bool)geneIsBodySizeGene.Invoke(null, new object[] { g }); }
            catch (Exception ex) { Fail("GeneIsBodySizeGene", ex); return false; }
        }

        public static LifeStageDef GeneGetLifeStageDef(GeneDef g)
        {
            if (!geneGate.Ensure()) return null;
            try { return geneGetLifeStageDef.Invoke(null, new object[] { g }) as LifeStageDef; }
            catch (Exception ex) { Fail("GeneGetLifeStageDef", ex); return null; }
        }

        // ------------------------------------------------------------------
        // Object (ThingDef): list-relational sections. Each Set/Remove pair is
        // ThingTool.cs:2175-2382 (verified per-method against the decompile).
        // ------------------------------------------------------------------

        public static HashSet<StatDef> ThingFreeStatFactors() => thingLFreeStatFactorsField?.GetValue(null) as HashSet<StatDef>;
        public static void ThingSetStatFactor(ThingDef t, StatDef s, float v) => Invoke(thingSetStatFactor, t, s, v);
        public static void ThingRemoveStatFactor(ThingDef t, StatDef s) => Invoke(thingRemoveStatFactor, t, s);

        public static HashSet<StatDef> ThingFreeStatOffsets() => thingLFreeStatOffsetsField?.GetValue(null) as HashSet<StatDef>;
        public static void ThingSetStatOffset(ThingDef t, StatDef s, float v) => Invoke(thingSetStatOffset, t, s, v);
        public static void ThingRemoveStatOffset(ThingDef t, StatDef s) => Invoke(thingRemoveStatOffset, t, s);

        public static HashSet<StuffCategoryDef> ThingFreeStuffCategories() => thingLFreeStuffCategoriesField?.GetValue(null) as HashSet<StuffCategoryDef>;
        public static void ThingSetStuffCategorie(ThingDef t, StuffCategoryDef c) => Invoke(thingSetStuffCategorie, t, c, null);
        public static void ThingRemoveStuffCategorie(ThingDef t, StuffCategoryDef c) => Invoke(thingRemoveStuffCategorie, t, c);

        public static HashSet<ThingDef> ThingFreeCosts() => thingLFreeCostsField?.GetValue(null) as HashSet<ThingDef>;
        public static void ThingSetCosts(ThingDef t, ThingDef cost, int v) => Invoke(thingSetCosts, t, cost, v);
        public static void ThingRemoveCosts(ThingDef t, ThingDef cost) => Invoke(thingRemoveCosts, t, cost);

        public static HashSet<ThingDef> ThingFreeCostsDiff() => thingLFreeCostsDiffField?.GetValue(null) as HashSet<ThingDef>;
        public static void ThingSetCostsDiff(ThingDef t, ThingDef cost, int v) => Invoke(thingSetCostsDiff, t, cost, v);
        public static void ThingRemoveCostsDiff(ThingDef t, ThingDef cost) => Invoke(thingRemoveCostsDiff, t, cost);

        public static HashSet<ApparelLayerDef> ThingFreeApparelLayer() => thingLFreeApparelLayerField?.GetValue(null) as HashSet<ApparelLayerDef>;
        public static void ThingSetApparelLayer(ThingDef t, ApparelLayerDef l) => Invoke(thingSetApparelLayer, t, l);
        public static void ThingRemoveApparelLayer(ThingDef t, ApparelLayerDef l) => Invoke(thingRemoveApparelLayer, t, l);

        public static HashSet<BodyPartGroupDef> ThingFreeBodyPartGroup() => thingLFreeBodyPartGroupField?.GetValue(null) as HashSet<BodyPartGroupDef>;
        public static void ThingSetBodyPartGroup(ThingDef t, BodyPartGroupDef b) => Invoke(thingSetBodyPartGroup, t, b);
        public static void ThingRemoveBodyPartGroup(ThingDef t, BodyPartGroupDef b) => Invoke(thingRemoveBodyPartGroup, t, b);

        public static HashSet<ResearchProjectDef> ThingFreePrerequisites() => thingLFreePrerequisitesField?.GetValue(null) as HashSet<ResearchProjectDef>;
        public static void ThingSetPrerequisite(ThingDef t, ResearchProjectDef r) => Invoke(thingSetPrerequisite, t, r);
        public static void ThingRemovePrerequisite(ThingDef t, ResearchProjectDef r) => Invoke(thingRemovePrerequisite, t, r);

        public static void ThingSetResearchPrerequisite(ThingDef t, ResearchProjectDef r) => Invoke(thingSetResearchPrerequisite, t, r);

        public static HashSet<WeaponTraitDef> ThingAllWeaponTraitDef()
        {
            if (!objectGate.Ensure()) return null;
            try { return thingAllWeaponTraitDefProp.GetValue(null, null) as HashSet<WeaponTraitDef>; }
            catch (Exception ex) { Fail("ThingAllWeaponTraitDef", ex); return null; }
        }
        public static void ThingSetBladeLinkTrait(Thing t, WeaponTraitDef w) => Invoke(thingSetBladeLinkTrait, t, w);
        public static void ThingRemoveBladeLinkTrait(Thing t, WeaponTraitDef w) => Invoke(thingRemoveBladeLinkTrait, t, w);

        /// <summary>The live Thing instance the Objects browser is editing (ThingTool.SelectedThing.tempThing) -- needed for the WeaponTraits/biocoded section, which DrawWeaponTraits keys off a real Thing's CompBladelinkWeapon rather than the ThingDef.</summary>
        public static Thing ThingSelectedTempThing()
        {
            if (!objectGate.Ensure()) return null;
            try
            {
                object selected = thingSelectedThingProp.GetValue(null, null);
                return selected != null ? selectedTempThingField.GetValue(selected) as Thing : null;
            }
            catch (Exception ex) { Fail("ThingSelectedTempThing", ex); return null; }
        }

        private static void Invoke(MethodInfo method, params object[] args)
        {
            if (method == null) return;
            try { method.Invoke(null, args); }
            catch (Exception ex) { Fail(method.Name, ex); }
        }

        /// <summary>
        /// One of the mod's own eight-language labels, read live by field name through the shared
        /// <see cref="CharEditorCompat.Labels"/> table -- this pane alone needs about thirty of them,
        /// far too many for a named FieldInfo each.
        /// </summary>
        public static string Label(string fieldName)
        {
            if (!GeneReady && !ObjectReady) return "";
            return CharEditorCompat.Labels.Get(fieldName);
        }
    }
}
