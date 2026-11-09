using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    internal sealed partial class CharacterEditorScope
    {
        // ---- Shared Character-row dispatch (Describe/Activate/Adjust). ----

        private void DescribeCharRow(RegionRow<CharRowKind> row, ElementDescription d)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (row.Kind)
            {
                case CharRowKind.NameFirst:
                    d.Label = "FirstName".Translate().CapitalizeFirst();
                    d.Role = ElementRole.TextField;
                    d.Value = (pawn?.Name as NameTriple)?.First ?? "";
                    d.ValueBlank = d.Value.NullOrEmpty();
                    break;
                case CharRowKind.NameNick:
                    d.Label = "NickName".Translate().CapitalizeFirst();
                    d.Role = ElementRole.TextField;
                    d.Value = (pawn?.Name as NameTriple)?.Nick ?? "";
                    d.ValueBlank = d.Value.NullOrEmpty();
                    break;
                case CharRowKind.NameLast:
                    d.Label = "LastName".Translate().CapitalizeFirst();
                    d.Role = ElementRole.TextField;
                    d.Value = (pawn?.Name as NameTriple)?.Last ?? "";
                    d.ValueBlank = d.Value.NullOrEmpty();
                    break;
                case CharRowKind.NameSingle:
                    d.Label = "RimWorldAccess.CharEd.Character.NameSection".Translate();
                    d.Role = ElementRole.TextField;
                    d.Value = (pawn?.Name as NameSingle)?.Name ?? "";
                    d.ValueBlank = d.Value.NullOrEmpty();
                    break;
                case CharRowKind.NameRandomize:
                    d.Label = "RimWorldAccess.CharEd.Character.RandomizeName".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.NameRandomizeFull:
                    d.Label = "RimWorldAccess.CharEd.Character.RandomizeNameFull".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.NameRoyalTitle:
                {
                    RoyalTitle title = CurrentRoyalTitle(pawn);
                    d.Label = title != null
                        ? title.def.GetLabelCapFor(pawn)
                        : "RimWorldAccess.CharEd.Character.NoRoyalTitle".Translate().ToString();
                    d.ReadOnly = true;
                    break;
                }
                case CharRowKind.AgeBiological:
                {
                    int years = pawn?.ageTracker?.AgeBiologicalYears ?? 0;
                    d.Label = "RimWorldAccess.CharEd.Character.AgeBiological".Translate();
                    d.Role = ElementRole.Stepper;
                    d.Value = years.ToString();
                    d.AtMinimum = years <= 0;
                    break;
                }
                case CharRowKind.AgeChronological:
                {
                    int years = pawn?.ageTracker?.AgeChronologicalYears ?? 0;
                    d.Label = "RimWorldAccess.CharEd.Character.AgeChronological".Translate();
                    d.Role = ElementRole.Stepper;
                    d.Value = years.ToString();
                    d.AtMinimum = years <= 0;
                    d.AtMaximum = years >= 15498;
                    break;
                }
                case CharRowKind.BackstoryChildhood:
                    d.Label = "Childhood".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = BackstoryValueLabel(pawn, true);
                    break;
                case CharRowKind.BackstoryAdulthood:
                    d.Label = "Adulthood".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = BackstoryValueLabel(pawn, false);
                    break;
                case CharRowKind.BackstoryRandomChildhood:
                    d.Label = "RimWorldAccess.CharEd.Character.RandomizeChildhood".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.BackstoryRandomAdulthood:
                    d.Label = "RimWorldAccess.CharEd.Character.RandomizeAdulthood".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.IncapableEntry:
                {
                    var tag = (WorkTags)row.Payload;
                    d.Label = tag.LabelTranslated().CapitalizeFirst();
                    d.ReadOnly = true;
                    d.Extras = IncapableCause(pawn, tag);
                    break;
                }
                case CharRowKind.TraitAdd:
                    d.Label = "RimWorldAccess.CharEd.Character.AddTrait".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.TraitCopy:
                    d.Label = "Copy".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.TraitPaste:
                {
                    d.Label = "Paste".Translate();
                    d.Role = ElementRole.Button;
                    bool has = CharEditorCompat.HasTraitClipboard(window);
                    d.Disabled = !has;
                    // ElementDescription has no reason field; the refusal reason rides Extras.
                    d.Extras = has ? null : "RimWorldAccess.CharEd.Character.NothingToPaste".Translate().ToString();
                    break;
                }
                case CharRowKind.TraitRandomize:
                    d.Label = "Randomize".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.TraitEntry:
                {
                    var trait = row.Payload as Trait;
                    d.Label = trait?.LabelCap ?? "";
                    d.Role = ElementRole.ComboBox;
                    d.Extras = trait != null && pawn != null ? trait.TipString(pawn) : null;
                    break;
                }
                case CharRowKind.SkillCopy:
                    d.Label = "Copy".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.SkillPaste:
                {
                    d.Label = "Paste".Translate();
                    d.Role = ElementRole.Button;
                    bool has = CharEditorCompat.HasSkillClipboard(window);
                    d.Disabled = !has;
                    // ElementDescription has no reason field; the refusal reason rides Extras.
                    d.Extras = has ? null : "RimWorldAccess.CharEd.Character.NothingToPaste".Translate().ToString();
                    break;
                }
                case CharRowKind.SkillRandomize:
                    d.Label = "Randomize".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.SkillEntry:
                    DescribeSkillEntry(row.Payload as SkillRecord, pawn, d);
                    break;
                case CharRowKind.NameChangeRace:
                    d.Label = "RimWorldAccess.CharEd.Character.ChangeRace".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = pawn?.def?.LabelCap.ToString() ?? "None".Translate().ToString();
                    d.Extras = pawn?.def?.description;
                    break;
                case CharRowKind.AgeBirthday:
                    d.Label = "RimWorldAccess.CharEd.Character.Birthday".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.AbilityAdd:
                    d.Label = "RimWorldAccess.CharEd.Character.AddAbility".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.AbilityCopy:
                    d.Label = "Copy".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.AbilityPaste:
                {
                    d.Label = "Paste".Translate();
                    d.Role = ElementRole.Button;
                    bool has = CharEditorCompat.HasAbilityClipboard(window);
                    d.Disabled = !has;
                    d.Extras = has ? null : "RimWorldAccess.CharEd.Character.NothingToPaste".Translate().ToString();
                    break;
                }
                case CharRowKind.AbilityRandomize:
                    d.Label = "Randomize".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.AbilityEntry:
                {
                    var ability = row.Payload as Ability;
                    d.Label = ability?.def?.LabelCap.ToString() ?? "";
                    d.Role = ElementRole.Button;
                    // Command_Ability.Desc is empty without hover -- read the def's own description directly.
                    d.Extras = ability?.def == null ? null
                        : RedundantLabelPrefix.AfterTitle(ability.def.GetTooltip(pawn).StripTags(), d.Label.StripTags());
                    break;
                }
                case CharRowKind.PsycastEntropy:
                {
                    Pawn_PsychicEntropyTracker tracker = pawn?.psychicEntropy;
                    float value = tracker?.EntropyValue ?? 0f;
                    float max = tracker?.MaxEntropy ?? 0f;
                    d.Label = "RimWorldAccess.CharEd.Character.NeuralHeat".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = "RimWorldAccess.CharEd.Character.ValueOfMax".Translate(value.ToString("0.#"), max.ToString("0.#")).ToString();
                    d.AtMinimum = value <= 0f;
                    d.AtMaximum = value >= max;
                    break;
                }
                case CharRowKind.PsycastPsyfocus:
                {
                    float value = pawn?.psychicEntropy?.CurrentPsyfocus ?? 0f;
                    d.Label = "RimWorldAccess.CharEd.Character.Psyfocus".Translate();
                    d.Role = ElementRole.Slider;
                    d.Value = value.ToStringPercent();
                    d.AtMinimum = value <= 0f;
                    d.AtMaximum = value >= 1f;
                    break;
                }
                case CharRowKind.IdentityFaction:
                    d.Label = "Faction".Translate().CapitalizeFirst();
                    d.Role = ElementRole.ComboBox;
                    d.Value = pawn?.Faction != null ? pawn.Faction.Name : "None".Translate().ToString();
                    break;
                case CharRowKind.IdentityIdeo:
                    d.Label = "StatsReport_Ideoligion".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = DescribeIdeoValue(pawn);
                    d.Extras = DescribeIdeoExtras(pawn);
                    break;
                case CharRowKind.IdentityXenoView:
                    d.Label = "ViewGenes".Translate();
                    d.Role = ElementRole.Button;
                    break;
                case CharRowKind.IdentityXenoEdit:
                    d.Label = "XenotypeEditor".Translate();
                    d.Role = ElementRole.Button;
                    d.Value = pawn?.genes != null ? pawn.genes.XenotypeLabelCap : "";
                    d.Extras = pawn?.genes != null ? pawn.genes.XenotypeDescShort : null;
                    break;
                case CharRowKind.IdentityFavoriteColor:
                    d.Label = "RimWorldAccess.CharEd.Character.FavoriteColor".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = DescribeFavoriteColorValue(pawn);
                    break;
                case CharRowKind.IdentityMutant:
                    d.Label = "RimWorldAccess.CharEd.Character.Mutant".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = DescribeMutantValue(pawn);
                    break;
                case CharRowKind.IdentityRoyalTitle:
                {
                    RoyalTitle title = CurrentRoyalTitle(pawn);
                    d.Label = "RimWorldAccess.CharEd.Character.RoyalTitleRow".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = title != null ? title.def.GetLabelCapFor(pawn) : "None".Translate().ToString();
                    break;
                }
                case CharRowKind.IdentityRecruit:
                    d.Label = "RimWorldAccess.CharEd.Character.Recruit".Translate();
                    d.Role = ElementRole.Button;
                    d.Extras = "RimWorldAccess.CharEd.Character.RecruitDesc".Translate();
                    break;
                case CharRowKind.IdentityEnslave:
                    d.Label = "RimWorldAccess.CharEd.Character.Enslave".Translate();
                    d.Role = ElementRole.Button;
                    d.Extras = "RimWorldAccess.CharEd.Character.EnslaveDesc".Translate();
                    break;
                case CharRowKind.TrainingMaster:
                    d.Label = "Master".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = DescribeMasterValue(pawn);
                    break;
                case CharRowKind.TrainingTrainability:
                    d.Label = "Trainability".Translate();
                    d.ReadOnly = true;
                    d.Value = DescribeTrainabilityValue(pawn);
                    break;
                case CharRowKind.TrainingEntry:
                    if (row.Payload is TrainableDef trainableDef && pawn?.training != null)
                    {
                        DescribeTrainingEntry(trainableDef, pawn, d);
                    }
                    break;
            }
        }

        private void ActivateCharRow(RegionRow<CharRowKind> row, InspectionTreeItem item)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (row.Kind)
            {
                case CharRowKind.NameFirst:
                    BeginNameFieldEdit(pawn, true, 0);
                    break;
                case CharRowKind.NameNick:
                    BeginNameFieldEdit(pawn, true, 1);
                    break;
                case CharRowKind.NameLast:
                    BeginNameFieldEdit(pawn, true, 2);
                    break;
                case CharRowKind.NameSingle:
                    BeginNameFieldEdit(pawn, false, 0);
                    break;
                case CharRowKind.NameRandomize:
                    CharEditorCompat.RandomizeName(window);
                    AnnounceNameChanged(pawn);
                    break;
                case CharRowKind.NameRandomizeFull:
                    CharEditorCompat.RandomizeNameFull(window);
                    AnnounceNameChanged(pawn);
                    break;
                case CharRowKind.NameRoyalTitle:
                {
                    RoyalTitle title = CurrentRoyalTitle(pawn);
                    if (title != null)
                    {
                        // Vehicle A: BlockBio.AShowTitle's own info-card open (CEditor.cs:1850-1853).
                        Find.WindowStack.Add(new Dialog_InfoCard(title.def, title.faction));
                    }
                    else
                    {
                        AnnounceCurrentItem();
                    }
                    break;
                }
                case CharRowKind.AgeBiological:
                    OpenExactAgeEntry(pawn, false);
                    break;
                case CharRowKind.AgeChronological:
                    OpenExactAgeEntry(pawn, true);
                    break;
                case CharRowKind.BackstoryChildhood:
                    CharEditorBrowserCompat.OpenChangeBackstory(true);
                    break;
                case CharRowKind.BackstoryAdulthood:
                    CharEditorBrowserCompat.OpenChangeBackstory(false);
                    break;
                case CharRowKind.BackstoryRandomChildhood:
                    CharEditorCompat.StepBackstory(window, true, true, true);
                    AnnounceBackstoryChanged(item, true);
                    break;
                case CharRowKind.BackstoryRandomAdulthood:
                    CharEditorCompat.StepBackstory(window, false, true, true);
                    AnnounceBackstoryChanged(item, false);
                    break;
                case CharRowKind.IncapableEntry:
                    AnnounceCurrentItem();
                    break;
                case CharRowKind.TraitAdd:
                    RememberTraitsBeforeAddDialog();
                    CharEditorBrowserCompat.OpenAddTrait(null);
                    break;
                case CharRowKind.TraitCopy:
                    CharEditorCompat.CopyTraits(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Character.TraitsCopied".Translate().ToString());
                    break;
                case CharRowKind.TraitPaste:
                    if (!CharEditorCompat.HasTraitClipboard(window))
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        AnnounceCurrentItem();
                        break;
                    }
                    CharEditorCompat.PasteTraits(window);
                    RebuildTraitsAfterMutation(item);
                    break;
                case CharRowKind.TraitRandomize:
                    CharEditorCompat.RandomizeTraits(window);
                    RebuildTraitsAfterMutation(item);
                    break;
                case CharRowKind.TraitEntry:
                    RememberTraitsBeforeAddDialog();
                    CharEditorBrowserCompat.OpenAddTrait(row.Payload as Trait);
                    break;
                case CharRowKind.SkillCopy:
                    CharEditorCompat.CopySkills(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Character.SkillsCopied".Translate().ToString());
                    break;
                case CharRowKind.SkillPaste:
                    if (!CharEditorCompat.HasSkillClipboard(window))
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        AnnounceCurrentItem();
                        break;
                    }
                    CharEditorCompat.PasteSkills(window);
                    RefreshModel();
                    AnnounceCurrentItem();
                    break;
                case CharRowKind.SkillRandomize:
                    CharEditorCompat.RandomizeSkills(window);
                    RefreshModel();
                    AnnounceCurrentItem();
                    break;
                case CharRowKind.SkillEntry:
                    BeginSkillLevelEdit(row.Payload as SkillRecord, item);
                    break;
                case CharRowKind.NameChangeRace:
                    CharEditorBrowserCompat.OpenChangeRace(pawn);
                    break;
                case CharRowKind.AgeBirthday:
                    CharEditorBirthdayCompat.Open(pawn);
                    break;
                case CharRowKind.AbilityAdd:
                    RememberAbilitiesBeforeAddDialog();
                    CharEditorBrowserCompat.OpenAddAbility();
                    break;
                case CharRowKind.AbilityCopy:
                    CharEditorCompat.CopyAbilities(window);
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Character.AbilitiesCopied".Translate().ToString());
                    break;
                case CharRowKind.AbilityPaste:
                    if (!CharEditorCompat.HasAbilityClipboard(window))
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        AnnounceCurrentItem();
                        break;
                    }
                    CharEditorCompat.PasteAbilities(window);
                    RebuildAbilitiesAfterMutation(item);
                    break;
                case CharRowKind.AbilityRandomize:
                    CharEditorCompat.RandomizeAbilities(window);
                    RebuildAbilitiesAfterMutation(item);
                    break;
                case CharRowKind.AbilityEntry:
                    // No edit-mode entry exists for abilities (DialogAddAbility has one constructor
                    // overload); Enter just re-reads the row.
                    AnnounceCurrentItem();
                    break;
                case CharRowKind.PsycastEntropy:
                case CharRowKind.PsycastPsyfocus:
                    AnnounceCurrentItem();
                    break;
                case CharRowKind.IdentityFaction:
                    CharEditorBrowserCompat.OpenChangeFaction();
                    break;
                case CharRowKind.IdentityIdeo:
                    // Vehicle A: BlockBio's own AChangeIdeo, including its Prefs.DevMode dance
                    // (a known mod bug, ridden as-is).
                    CharEditorCompat.ChangeIdeo(window);
                    break;
                case CharRowKind.IdentityXenoView:
                    CharEditorCompat.OpenViewGenes(pawn);
                    break;
                case CharRowKind.IdentityXenoEdit:
                    CharEditorCompat.OpenXenotypeEditor(pawn);
                    break;
                case CharRowKind.IdentityFavoriteColor:
                    OpenFavoriteColorPicker(pawn, item);
                    break;
                case CharRowKind.IdentityMutant:
                    OpenMutantPicker(pawn, item);
                    break;
                case CharRowKind.IdentityRoyalTitle:
                    OpenRoyalTitlePicker(pawn, item);
                    break;
                case CharRowKind.IdentityRecruit:
                    CharEditorCompat.Recruit(window);
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Character.Recruited".Translate(DescribePawnValue()).ToString());
                    break;
                case CharRowKind.IdentityEnslave:
                    CharEditorCompat.Enslave(window);
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.Character.Enslaved".Translate(DescribePawnValue()).ToString());
                    break;
                case CharRowKind.TrainingMaster:
                    OpenMasterPicker(pawn, item);
                    break;
                case CharRowKind.TrainingTrainability:
                    AnnounceCurrentItem();
                    break;
                case CharRowKind.TrainingEntry:
                    if (row.Payload is TrainableDef def)
                    {
                        ToggleTrainable(def, item);
                    }
                    break;
            }
        }

        // ---- Identity pickers: windowless float menus over the mod's own candidate lists. ----

        private void OpenFavoriteColorPicker(Pawn pawn, InspectionTreeItem item)
        {
            List<ColorDef> candidates = CharEditorCompat.FavoriteColorCandidates(window);
            OpenColorDefPicker(candidates, "RimWorldAccess.CharEd.Character.FavoriteColor".Translate(),
                selected =>
                {
                    CharEditorCompat.SetFavoriteColor(window, selected);
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                });
        }

        private void OpenColorDefPicker(List<ColorDef> candidates, string title, Action<ColorDef> apply)
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("None".Translate(), delegate { apply(null); }));
            foreach (ColorDef candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }
                ColorDef captured = candidate;
                options.Add(new FloatMenuOption(ColorDefSpokenLabel(captured), delegate { apply(captured); }));
            }
            WindowlessFloatMenuState.OpenTitled(title, options);
        }

        /// <summary>
        /// A ColorDef's spoken identity. Most vanilla ColorDefs the mod lists have NO label — its
        /// own float menu shows a tinted swatch with empty text — so the def's color rendered as
        /// hex is the only datum left.
        /// </summary>
        private static string ColorDefSpokenLabel(ColorDef def)
        {
            string label = def.LabelCap.ToString();
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }
            return "RimWorldAccess.UI.GenericWindow.ColorSwatch"
                .Translate(UnityEngine.ColorUtility.ToHtmlStringRGB(def.color)).ToString();
        }

        private void OpenMutantPicker(Pawn pawn, InspectionTreeItem item)
        {
            List<MutantDef> candidates = CharEditorCompat.MutantCandidates();
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("None".Translate(), delegate
            {
                CharEditorCompat.SetMutant(window, null);
                AnnounceMutantApplied(item);
            }));
            foreach (MutantDef candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }
                MutantDef captured = candidate;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    CharEditorCompat.SetMutant(window, captured);
                    AnnounceMutantApplied(item);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Character.Mutant".Translate(), options);
        }

        /// <summary>OnChangeMutant applies immediately, so the announcement says so explicitly.</summary>
        private void AnnounceMutantApplied(InspectionTreeItem item)
        {
            RefreshModel();
            if (item?.Data is RegionRow<CharRowKind> row)
            {
                var d = new ElementDescription();
                DescribeCharRow(row, d);
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance)
                    + " " + "RimWorldAccess.CharEd.Character.AppliesImmediately".Translate());
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        private void OpenRoyalTitlePicker(Pawn pawn, InspectionTreeItem item)
        {
            List<RoyalTitleDef> candidates = CharEditorCompat.RoyalTitleCandidates(window);
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("None".Translate(), delegate
            {
                CharEditorCompat.RemoveRoyalTitle(window);
                AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
            }));
            foreach (RoyalTitleDef candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }
                RoyalTitleDef captured = candidate;
                options.Add(new FloatMenuOption(captured.GetLabelCapFor(pawn), delegate
                {
                    CharEditorCompat.SetRoyalTitle(window, captured);
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Character.RoyalTitleRow".Translate(), options);
        }

        private void BeginSkillLevelEdit(SkillRecord skill, InspectionTreeItem item)
        {
            if (skill == null || skill.TotallyDisabled)
            {
                AnnounceCurrentItem();
                return;
            }
            // MUTATION-C: mirrors BlockBio's own timed numeric field (CEditor.cs:2448-2452,
            // `skill2.levelInt = SZWidgets.NumericTextField(..., skill2.levelInt, 0, 9999);`) -- a
            // bare, ungated levelInt write, clamped only by the WIDGET's own min/max arguments
            // (0-9999, which also give the field its four-digit cap), not by any game rule. No
            // wrapper method exists to ride instead.
            CharEdNumericEntry.OpenInt(editSession, skill.def.LabelCap.ToString(), skill.levelInt,
                0, 9999, level => { skill.levelInt = level; },
                onExit: () => AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow));
        }

        /// <summary>Right/Left step an adjustable Character-section row (the steppers and sliders). Returns false for combo-box, action and read-only rows, falling through to the base's tree expand/collapse.</summary>
        private bool AdjustCharRow(RegionRow<CharRowKind> row, InspectionTreeItem item, int direction)
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            switch (row.Kind)
            {
                case CharRowKind.AgeBiological:
                    CharEditorCompat.StepBiologicalAge(pawn, direction);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                    return true;
                case CharRowKind.AgeChronological:
                    CharEditorCompat.StepChronologicalAge(pawn, direction);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                    return true;
                case CharRowKind.SkillEntry:
                {
                    var skill = row.Payload as SkillRecord;
                    if (skill == null || skill.TotallyDisabled)
                    {
                        return true;
                    }
                    CharEditorCompat.RunWithMenuRedirect(() =>
                        CharEditorCompat.StepSkillLevel(window, skill, direction > 0));
                    if (WindowlessFloatMenuState.IsActive)
                    {
                        return true;
                    }
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                    return true;
                }
                case CharRowKind.PsycastEntropy:
                {
                    Pawn_PsychicEntropyTracker tracker = pawn?.psychicEntropy;
                    if (tracker == null)
                    {
                        return true;
                    }
                    float max = tracker.MaxEntropy;
                    float step = max / PsycastStepDivisor;
                    float next = Mathf.Clamp(tracker.EntropyValue + step * direction, 0f, max);
                    CharEditorCompat.SetEntropy(pawn, next);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                    return true;
                }
                case CharRowKind.PsycastPsyfocus:
                {
                    if (pawn?.psychicEntropy == null)
                    {
                        return true;
                    }
                    float step = 1f / PsycastStepDivisor;
                    float next = Mathf.Clamp(pawn.psychicEntropy.CurrentPsyfocus + step * direction, 0f, 1f);
                    CharEditorCompat.SetPsyfocus(pawn, next);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    AnnounceAdjustedRow<CharRowKind>(item, DescribeCharRow);
                    return true;
                }
                default:
                    // Every ComboBox-role Character row lands here: a combo box is a dropdown, so
                    // Enter/Space open its picker and Left/Right fall through to the base's tree
                    // expand/collapse instead of stepping a value.
                    return false;
            }
        }

        // ---- Space (cycle passion) / Delete (remove trait) when-guards and handlers. ----

        /// <summary>Space claims charEditor.cyclePassion only while the cursor sits on an adjustable (not TotallyDisabled) skill row -- mirrors DevDebugScope's devDebug.togglePin when-guard shape.</summary>
        private bool OnSkillEntryRow()
        {
            RefreshModel();
            return CurrentTreeItem()?.Data is RegionRow<CharRowKind> row
                && row.Kind == CharRowKind.SkillEntry
                && !((row.Payload as SkillRecord)?.TotallyDisabled ?? true);
        }

        private bool OnTraitOrAbilityEntryRow()
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            if (item?.Data is RegionRow<CharRowKind> row)
            {
                return row.Kind == CharRowKind.TraitEntry || row.Kind == CharRowKind.AbilityEntry;
            }
            // The charEditor.removeTrait claim is reused for every removable row kind rather than
            // re-registered under new ids. Health condition groups are one of them.
            if (item?.Data is RegionRow<HealthRowKind> healthRow && healthRow.Kind == HealthRowKind.ConditionEntry)
            {
                return true;
            }
            // Memory rows (Needs) and direct relation rows (Social) too.
            if (item?.Data is RegionRow<NeedsRowKind> needsRow && needsRow.Kind == NeedsRowKind.MemoryEntry)
            {
                return true;
            }
            if (item?.Data is RegionRow<SocialRowKind> socialRow && socialRow.Kind == SocialRowKind.DirectRelationEntry)
            {
                return true;
            }
            // Inventory thing rows too, but ONLY in-game. Delete means the mod's plain drop path,
            // never destroy, and that distinction has no meaning during world generation, where
            // BlockInventory's non-Alt branch IS the destroy path. There, Destroy stays reachable
            // only through the row's own Alt+I drill-in.
            return item?.Data is RegionRow<InventoryRowKind> inventoryRow && inventoryRow.Kind == InventoryRowKind.ThingEntry
                && !CharEditorCompat.InStartingScreen;
        }

        private void PerformCyclePassion()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (!(item?.Data is RegionRow<CharRowKind> row) || !(row.Payload is SkillRecord skill))
            {
                return;
            }
            CharEditorCompat.RunWithMenuRedirect(() => CharEditorCompat.CyclePassion(window, skill));
            if (WindowlessFloatMenuState.IsActive)
            {
                // A patching mod (Vanilla Skills Expanded) replaced the cycle with its own passion
                // picker; the redirected menu announces itself.
                return;
            }
            // The delta of a passion cycle IS the passion; ComposeStateChange would speak only the
            // level value, so speak the passion directly.
            string passion = PawnSkillsTableHelper.PassionLabel(skill.passion);
            TolkHelper.SpeakData(string.IsNullOrEmpty(passion)
                ? "PassionNone".Translate().ToString()
                : passion);
        }

        /// <summary>Delete on a trait row removes via TraitTool.RemoveTrait; on an ability row via vanilla's own public Pawn_AbilityTracker.RemoveAbility, the same vehicle BlockBio's element-stack remove callback uses. Health condition rows route through the RegionRow<HealthRowKind> branch below.</summary>
        private void PerformRemoveTraitOrAbility()
        {
            InspectionTreeItem item = CurrentTreeItem();
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn == null)
            {
                return;
            }
            if (item?.Data is RegionRow<CharRowKind> row)
            {
                if (row.Kind == CharRowKind.TraitEntry && row.Payload is Trait trait)
                {
                    CharEditorCompat.RemoveTrait(pawn, trait);
                    RebuildTraitsAfterMutation(item);
                }
                else if (row.Kind == CharRowKind.AbilityEntry && row.Payload is Ability ability && pawn.abilities != null)
                {
                    pawn.abilities.RemoveAbility(ability.def);
                    RebuildAbilitiesAfterMutation(item);
                }
                return;
            }
            // Vehicle A: BlockHealth's own per-row Delete icon (BRemoveHediff) — removes just the
            // group's first hediff, matching the mod's own row.
            if (item?.Data is RegionRow<HealthRowKind> healthRow && healthRow.Kind == HealthRowKind.ConditionEntry
                && healthRow.Payload is List<Hediff> group && group.Count > 0)
            {
                CharEditorCompat.RemoveHediff(window, group[0]);
                RefreshHealthSectionInPlace(silent: false);
                return;
            }
            // ThoughtTool.RemoveThought(Pawn, Thought): the aggregated "AteNon" row removes just its
            // one Example, matching BlockNeeds' own DrawMemories click.
            if (item?.Data is RegionRow<NeedsRowKind> needsRow && needsRow.Kind == NeedsRowKind.MemoryEntry
                && needsRow.Payload is NeedsMemoryEntry memoryEntry && memoryEntry.Example != null)
            {
                CharEditorCompat.RemoveThought(pawn, memoryEntry.Example);
                RefreshNeedsSectionInPlace(silent: false);
                return;
            }
            // Vehicle B: 100% vanilla Pawn_RelationsTracker.RemoveDirectRelation, no reflection.
            if (item?.Data is RegionRow<SocialRowKind> socialRow && socialRow.Kind == SocialRowKind.DirectRelationEntry
                && socialRow.Payload is DirectPawnRelation relation)
            {
                pawn.relations.RemoveDirectRelation(relation);
                RefreshSocialSectionInPlace(silent: false);
                return;
            }
            // Vehicle A: BlockInventory's own InterfaceDrop(thing, false), the same plain-drop path
            // its Drop icon runs in-game; the when-guard above already excludes world generation,
            // where this branch would be the destroy path instead.
            if (item?.Data is RegionRow<InventoryRowKind> inventoryRow && inventoryRow.Kind == InventoryRowKind.ThingEntry
                && inventoryRow.Payload is InventoryThingEntry entry && entry.Thing != null)
            {
                CharEditorCompat.DropThing(window, entry.Thing);
                RefreshInventorySectionInPlace(silent: false);
            }
        }

        /// <summary>
        /// Rebuilds the whole Character section in place after returning focus from a child browser
        /// dialog (Add Trait / Change Backstory), whose OK path mutates traits or backstory out from
        /// under this scope's cached rows. Cheap and always safe; a no-op when the section is
        /// collapsed. The rebuild preserves what the user had open: each expanded subsection is
        /// re-expanded by its stable identity label, and the cursor re-finds its row by
        /// kind-plus-payload identity, falling back to kind within the same subsection.
        /// </summary>
        private void RefreshCharacterSectionAfterChildDialog()
        {
            InspectionTreeItem section = FindTopLevelSection(SectionKind.Character);
            if (section == null || !section.IsExpanded)
            {
                staleSections.Add(SectionKind.Character);
                return;
            }
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn == null)
            {
                return;
            }

            var expandedSubs = new HashSet<string>();
            foreach (InspectionTreeItem child in section.Children)
            {
                if (child.IsExpanded && !string.IsNullOrEmpty(child.ExpandedLabel ?? child.Label))
                {
                    expandedSubs.Add(child.ExpandedLabel ?? child.Label);
                }
            }
            InspectionTreeItem current = CurrentTreeItem();
            var cursorRow = current?.Data as RegionRow<CharRowKind>;
            string cursorSubLabel = SubsectionIdentityLabel(current);
            int cursorIndex = current != null ? IndexOfVisible(Tree.Visible, current) : -1;

            section.Children.Clear();
            BuildCharacterSection(section, pawn);
            foreach (InspectionTreeItem child in section.Children)
            {
                if (expandedSubs.Contains(child.ExpandedLabel ?? child.Label))
                {
                    child.IsExpanded = true;
                }
            }
            Tree.Reflatten();
            ResetBoundaryTracking();
            RefreshModel();
            RestoreCursorAfterRebuild(cursorRow, cursorSubLabel, cursorIndex);
        }

        /// <summary>The identity label of the subsection containing <paramref name="item"/> (ExpandedLabel when folded), or null.</summary>
        private static string SubsectionIdentityLabel(InspectionTreeItem item)
        {
            for (InspectionTreeItem p = item; p != null; p = p.Parent)
            {
                if (p.Type == InspectionTreeItem.ItemType.SubCategory)
                {
                    return p.ExpandedLabel ?? p.Label;
                }
            }
            return null;
        }

        /// <summary>
        /// Re-seats the cursor after a Character-section rebuild: exact kind-plus-payload match
        /// first, then the first row of the same kind in the same subsection, then the old
        /// flattened index clamped.
        /// </summary>
        private void RestoreCursorAfterRebuild(RegionRow<CharRowKind> cursorRow, string cursorSubLabel, int cursorIndex)
        {
            if (Tree.Visible.Count == 0)
            {
                return;
            }
            int target = -1;
            if (cursorRow != null)
            {
                for (int i = 0; i < Tree.Visible.Count; i++)
                {
                    if (Tree.Visible[i].Data is RegionRow<CharRowKind> r && r.Kind == cursorRow.Kind
                        && ReferenceEquals(r.Payload, cursorRow.Payload))
                    {
                        target = i;
                        break;
                    }
                }
                if (target < 0)
                {
                    for (int i = 0; i < Tree.Visible.Count; i++)
                    {
                        if (Tree.Visible[i].Data is RegionRow<CharRowKind> r && r.Kind == cursorRow.Kind
                            && SubsectionIdentityLabel(Tree.Visible[i]) == cursorSubLabel)
                        {
                            target = i;
                            break;
                        }
                    }
                }
            }
            if (target < 0 && cursorIndex >= 0)
            {
                target = Math.Min(cursorIndex, Tree.Visible.Count - 1);
            }
            if (target >= 0)
            {
                Tree.SetSelectedIndex(target);
                SyncRegionFromCurrentTree();
            }
        }

        /// <summary>
        /// Moves the cursor onto the row for the object a child dialog just added, so the entry
        /// announcement reads it instead of the button that opened the dialog. Silent by design:
        /// <see cref="ScreenScope.AfterFocusDispatch"/> speaks whatever the cursor lands on.
        /// </summary>
        private void FocusRowForAddedObject<T>(List<T> before, CharRowKind kind, List<T> after)
            where T : class
        {
            if (before == null || after == null)
            {
                return;
            }
            T added = null;
            for (int i = 0; i < after.Count; i++)
            {
                if (!before.Contains(after[i]))
                {
                    added = after[i];
                    break;
                }
            }
            if (added == null)
            {
                return;
            }
            for (int i = 0; i < Tree.Visible.Count; i++)
            {
                if (Tree.Visible[i].Data is RegionRow<CharRowKind> row
                    && row.Kind == kind
                    && ReferenceEquals(row.Payload, added))
                {
                    Tree.SetSelectedIndex(i);
                    SyncRegionFromCurrentTree();
                    return;
                }
            }
        }

        /// <summary>
        /// Consumes whichever snapshot the just-closed child dialog left behind (Add Trait or Add
        /// Ability) and lands the cursor on the object it added. A cancelled dialog, a pawn switched
        /// underneath, or a collapsed Character section all leave the cursor where the rebuild put it.
        /// </summary>
        private void FocusObjectAddedByChildDialog()
        {
            List<Trait> traitsBefore = traitsBeforeAddDialog;
            List<Ability> abilitiesBefore = abilitiesBeforeAddDialog;
            Pawn pawn = objectsBeforeAddDialogPawn;
            traitsBeforeAddDialog = null;
            abilitiesBeforeAddDialog = null;
            objectsBeforeAddDialogPawn = null;
            if (pawn == null || !ReferenceEquals(pawn, CharEditorCompat.CurrentPawn))
            {
                return;
            }
            if (traitsBefore != null)
            {
                FocusRowForAddedObject(traitsBefore, CharRowKind.TraitEntry, pawn.story?.traits?.allTraits);
                return;
            }
            if (abilitiesBefore != null)
            {
                FocusRowForAddedObject(abilitiesBefore, CharRowKind.AbilityEntry, pawn.abilities?.abilities);
            }
        }

        /// <summary><see cref="TreeModel{T}.Visible"/> is an <see cref="IReadOnlyList{T}"/>, which has no IndexOf -- reference search by hand.</summary>
        private static int IndexOfVisible(IReadOnlyList<InspectionTreeItem> visible, InspectionTreeItem item)
        {
            if (item == null)
            {
                return -1;
            }
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i], item))
                {
                    return i;
                }
            }
            return -1;
        }

        private InspectionTreeItem FindTopLevelSection(SectionKind kind)
        {
            InspectionTreeItem root = Tree.Root;
            if (root == null)
            {
                return null;
            }
            foreach (InspectionTreeItem child in root.Children)
            {
                if (child.Data is SectionKind k && k == kind)
                {
                    return child;
                }
            }
            return null;
        }
    }
}
