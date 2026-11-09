using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Verse;
using RimWorld;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Builds the tab content for the growth moment dialog, which
    /// <see cref="RimWorldAccess.Shell.GrowthMomentScope"/> presents as one content region per
    /// tab: Info, Passions (tiers 4-8 only), Traits, Aspirations (Vanilla Aspirations Expanded
    /// compat, see VaeCompat), then the letter's read-only Character and Health side panels.
    /// Cursors, typeahead and announcement plumbing belong to the scope; the data builders,
    /// choice mutations, radio-group settle rule and per-tab Describe live here.
    /// </summary>
    public static class GrowthMomentState
    {
        private enum Tab { Info, Passions, Traits, Aspirations, Character, Health }

        private class PassionItem
        {
            public SkillDef Skill;
            public Passion CurrentPassion;
            public Passion NewPassion;
            public int SkillLevel;
            public string Label;
        }

        private class TraitItem
        {
            public Trait TraitOption;
            public string Label;
            public string Description;
            public bool IsNoTrait;
        }

        private static bool isActive = false;
        private static ChoiceLetter_GrowthMoment letter;
        private static Window dialog;
        private static bool isArchiveView;

        // The tabs this letter offers, in draw order; the index IS the scope's content-region index.
        private static List<Tab> availableTabs = new List<Tab>();

        private static string[] infoLines;

        // The letter.ShowInfoTabs side panels, flattened to read-only lines like the Info tab.
        private static string[] characterLines;
        private static string[] healthLines;

        private static List<PassionItem> passionItems = new List<PassionItem>();

        private static List<TraitItem> traitItems = new List<TraitItem>();

        private static List<VaeCompat.AspirationChoice> aspirationItems = new List<VaeCompat.AspirationChoice>();
        private static int aspirationGainsCount = 0;

        // The open dialog owns the selection. Bound to the declaring type so the fields still
        // resolve when the live instance is VAE's subclass.
        private static readonly FieldInfo ChosenPassionsField =
            AccessTools.Field(typeof(Dialog_GrowthMomentChoices), "chosenPassions");
        private static readonly FieldInfo ChosenTraitField =
            AccessTools.Field(typeof(Dialog_GrowthMomentChoices), "chosenTrait");

        /// <summary>
        /// The dialog's own <c>chosenPassions</c>, mutated in place so the drawn checkboxes and radio
        /// dots follow the keyboard selection. A throwaway empty list when no dialog is open, never
        /// an editable session, since the archive view draws no choices.
        /// </summary>
        // MUTATION-C: mirrors the chosenPassions writes Dialog_GrowthMomentChoices' own passion
        // checkbox and radio branches perform (DrawPassionChoices :266-285); the writes are
        // inline in those widget branches, so no A or B vehicle exists.
        private static List<SkillDef> SelectedPassions =>
            (dialog is Dialog_GrowthMomentChoices ? ChosenPassionsField?.GetValue(dialog) as List<SkillDef> : null)
            ?? new List<SkillDef>();

        /// <summary>The <see cref="SelectedPassions"/> counterpart over <c>chosenTrait</c>.</summary>
        private static Trait SelectedTrait
        {
            get => dialog is Dialog_GrowthMomentChoices ? ChosenTraitField?.GetValue(dialog) as Trait : null;
            // MUTATION-C: mirrors the chosenTrait write Dialog_GrowthMomentChoices' own trait
            // radios perform (DrawTraitChoices :223-234); same inline-branch shape as above.
            set { if (dialog is Dialog_GrowthMomentChoices) ChosenTraitField?.SetValue(dialog, value); }
        }

        /// <summary>
        /// VAE's aspirations dialog owns its in-progress selection as vanilla owns the two above.
        /// Empty when the open dialog is not VAE's, which is also when the tab is absent.
        /// </summary>
        private static IList SelectedAspirations =>
            VaeCompat.TryGetChosenAspirations(dialog, out IList chosen) ? chosen : new List<object>();

        public static bool IsActive => isActive;

        /// <summary>True for a letter being re-read from the archive: every row is read-only and vanilla draws no Later button.</summary>
        internal static bool IsArchiveView => isArchiveView;

        private static bool HasChoicesToMake()
        {
            return !isArchiveView && (passionItems.Count > 0 || traitItems.Count > 0 || aspirationItems.Count > 0);
        }

        /// <summary>Extracts the letter's data and builds the tabs.</summary>
        public static void Open(ChoiceLetter_GrowthMoment growthLetter, Window growthDialog)
        {
            if (growthLetter == null || growthDialog == null)
            {
                Log.Error("[RimWorld Access] Cannot open growth moment state: null letter or dialog");
                return;
            }

            letter = growthLetter;
            dialog = growthDialog;
            isArchiveView = letter.ArchiveView;
            isActive = true;

            // Aspirations must build before Info: BuildInfoLines reads the aspiration fields to
            // decide whether to append the explainer line.
            BuildPassionItems();
            BuildTraitItems();
            BuildAspirationItems();
            BuildInfoLines();
            BuildCharacterAndHealthLines();
            BuildAvailableTabs();
        }

        /// <summary>Closes the growth moment state.</summary>
        public static void Close()
        {
            isActive = false;
            letter = null;
            dialog = null;
            passionItems.Clear();
            traitItems.Clear();
            aspirationItems.Clear();
            aspirationGainsCount = 0;
            infoLines = null;
            characterLines = null;
            healthLines = null;
            availableTabs.Clear();
        }

        /// <summary>The tabs this letter offers: the scope's content regions, one per tab.</summary>
        internal static int TabCount => availableTabs.Count;

        /// <summary>
        /// One tab's region name: vanilla's own tab label plus, on a choice tab of a live letter,
        /// the "choose n of m" line. The region frame spends its Extras slot on the row under the
        /// cursor, so the choice count rides the name instead.
        /// </summary>
        internal static string TabName(int region)
        {
            if (region < 0 || region >= availableTabs.Count) return "";
            Tab tab = availableTabs[region];
            string name = GetTabName(tab);
            if (isArchiveView) return name;
            switch (tab)
            {
                case Tab.Passions:
                    return name + ". " + (letter.passionGainsCount == 1
                        ? "RimWorldAccess.Biotech.GrowthMoment.ChooseOne".Translate()
                        : "RimWorldAccess.Biotech.GrowthMoment.ChooseNOfM".Translate(letter.passionGainsCount, passionItems.Count));
                case Tab.Traits:
                    return name + ". " + "RimWorldAccess.Biotech.GrowthMoment.ChooseOne".Translate();
                case Tab.Aspirations:
                    return name + ". " + (aspirationGainsCount == 1
                        ? "RimWorldAccess.Biotech.GrowthMoment.ChooseOne".Translate()
                        : "RimWorldAccess.Biotech.GrowthMoment.ChooseNOfM".Translate(aspirationGainsCount, aspirationItems.Count));
                default:
                    return name;
            }
        }

        /// <summary>Whether a tab joins the typeahead search space; false for the plain-text, no-selection tabs.</summary>
        internal static bool TabSearchable(int region)
        {
            if (region < 0 || region >= availableTabs.Count) return false;
            Tab tab = availableTabs[region];
            return tab == Tab.Passions || tab == Tab.Traits || tab == Tab.Aspirations;
        }

        internal static int RowCount(int region)
        {
            if (region < 0 || region >= availableTabs.Count) return 0;
            switch (availableTabs[region])
            {
                case Tab.Info: return infoLines?.Length ?? 0;
                case Tab.Passions: return passionItems.Count;
                case Tab.Traits: return traitItems.Count;
                case Tab.Aspirations: return aspirationItems.Count;
                case Tab.Character: return characterLines?.Length ?? 0;
                case Tab.Health: return healthLines?.Length ?? 0;
                default: return 0;
            }
        }

        /// <summary>
        /// The tab to land on when the screen opens: the first selection tab, so the player arrives
        /// where the choice is. The archive view, and a letter with nothing but Info, stays on Info.
        /// </summary>
        internal static int InitialRegion => !isArchiveView && availableTabs.Count > 1 ? 1 : 0;

        /// <summary>
        /// The vanilla object one row was built from, for the focus ring: its <see cref="SkillDef"/>
        /// on Passions, its <see cref="Trait"/> on Traits. Null everywhere vanilla draws no listing
        /// rows, the archive view included.
        /// </summary>
        internal static object RowObject(int region, int index)
        {
            if (!isActive || isArchiveView || region < 0 || region >= availableTabs.Count)
                return null;
            switch (availableTabs[region])
            {
                case Tab.Passions:
                    return index >= 0 && index < passionItems.Count ? passionItems[index].Skill : null;
                case Tab.Traits:
                    if (index < 0 || index >= traitItems.Count)
                        return null;
                    TraitItem trait = traitItems[index];
                    return trait.IsNoTrait ? ChoiceLetter_GrowthMoment.NoTrait : trait.TraitOption;
                default:
                    return null;
            }
        }

        /// <summary>Escape closes the archive view, or postpones a live choice.</summary>
        public static void HandleCancel()
        {
            if (isArchiveView)
            {
                CloseDialog();
                return;
            }

            PostponeChoices();
        }

        /// <summary>Alt+S confirm; a no-op in the archive view.</summary>
        public static void HandleConfirmKey()
        {
            if (!isArchiveView)
                ConfirmChoices();
        }

        /// <summary>
        /// Vanilla's own OK button as a Buttons-region row: it commits a live letter's choices and
        /// merely closes an archived one, whose choices were applied when it was first answered.
        /// </summary>
        internal static void HandleOkButton()
        {
            if (isArchiveView)
            {
                CloseDialog();
                return;
            }
            ConfirmChoices();
        }

        /// <summary>Vanilla's own "Later" button, which the archive view does not draw; the path Escape takes.</summary>
        internal static void HandleLaterButton()
        {
            PostponeChoices();
        }

        /// <summary>
        /// The radio-group contract: landing on a single-choice row selects it, no Enter needed.
        /// Runs from OnCursorSettled BEFORE the row announcement composes, so the row is spoken
        /// already selected, and stays silent because the announcement carries the state.
        ///
        /// Only rows the announcements present as RadioButtons qualify — the Passions and
        /// Aspirations tabs when the letter offers exactly ONE pick, and the always-single Traits
        /// tab — because a checkbox must never flip just because the cursor arrived. The archive
        /// view selects nothing, and every selection stays staged until
        /// <see cref="ConfirmChoices"/>.
        /// </summary>
        internal static void SettleRow(int region, int index)
        {
            if (isArchiveView || letter == null || region < 0 || region >= availableTabs.Count)
                return;

            switch (availableTabs[region])
            {
                case Tab.Passions:
                {
                    if (letter.passionGainsCount != 1 || index < 0 || index >= passionItems.Count)
                        return;
                    SkillDef skill = passionItems[index].Skill;
                    List<SkillDef> chosen = SelectedPassions;
                    if (chosen.Count == 1 && chosen[0] == skill)
                        return;
                    chosen.Clear();
                    chosen.Add(skill);
                    return;
                }
                case Tab.Traits:
                {
                    if (index < 0 || index >= traitItems.Count)
                        return;
                    TraitItem item = traitItems[index];
                    SelectedTrait = item.IsNoTrait ? ChoiceLetter_GrowthMoment.NoTrait : item.TraitOption;
                    return;
                }
                case Tab.Aspirations:
                {
                    if (aspirationGainsCount != 1 || index < 0 || index >= aspirationItems.Count)
                        return;
                    object def = aspirationItems[index].Def;
                    IList chosen = SelectedAspirations;
                    if (chosen.Count == 1 && ReferenceEquals(chosen[0], def))
                        return;
                    chosen.Clear();
                    chosen.Add(def);
                    return;
                }
            }
        }

        /// <summary>
        /// Enter/Space toggles the focused passion/trait/aspiration, or on Info confirms when there
        /// is nothing left to choose. The Character/Health side panels have nothing to activate.
        /// </summary>
        internal static void ActivateRow(int region, int index)
        {
            if (region < 0 || region >= availableTabs.Count)
                return;
            Tab tab = availableTabs[region];

            if (tab == Tab.Info)
            {
                if (!isArchiveView && !HasChoicesToMake())
                    ConfirmChoices();
                return;
            }

            if (isArchiveView)
            {
                TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.ArchiveReadOnly".Translate());
                return;
            }

            if (tab == Tab.Passions)
                TogglePassion(index);
            else if (tab == Tab.Traits)
                SelectTrait(index);
            else if (tab == Tab.Aspirations)
                ToggleAspiration(index);
        }

        private static void TogglePassion(int passionIndex)
        {
            if (passionIndex < 0 || passionIndex >= passionItems.Count)
                return;

            PassionItem item = passionItems[passionIndex];
            List<SkillDef> chosen = SelectedPassions;

            if (letter.passionGainsCount == 1)
            {
                chosen.Clear();
                chosen.Add(item.Skill);
                TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.PassionSelectedFeedback".Translate(
                    item.Label, chosen.Count, letter.passionGainsCount));
            }
            else
            {
                if (chosen.Contains(item.Skill))
                {
                    chosen.Remove(item.Skill);
                    TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.PassionDeselectedFeedback".Translate(
                        item.Label, chosen.Count, letter.passionGainsCount));
                }
                else
                {
                    if (chosen.Count >= letter.passionGainsCount)
                    {
                        TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.PassionLimitReached".Translate(letter.passionGainsCount));
                        return;
                    }
                    chosen.Add(item.Skill);
                    TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.PassionSelectedFeedback".Translate(
                        item.Label, chosen.Count, letter.passionGainsCount));
                }
            }
        }

        private static void ToggleAspiration(int aspirationIndex)
        {
            if (aspirationIndex < 0 || aspirationIndex >= aspirationItems.Count)
                return;

            VaeCompat.AspirationChoice item = aspirationItems[aspirationIndex];
            IList chosen = SelectedAspirations;

            if (aspirationGainsCount == 1)
            {
                chosen.Clear();
                chosen.Add(item.Def);
                TolkHelper.SpeakData("RimWorldAccess.Compat.Vae.AspirationSelectedFeedback".Translate(
                    item.Label, chosen.Count, aspirationGainsCount));
            }
            else
            {
                if (chosen.Contains(item.Def))
                {
                    chosen.Remove(item.Def);
                    TolkHelper.SpeakData("RimWorldAccess.Compat.Vae.AspirationDeselectedFeedback".Translate(
                        item.Label, chosen.Count, aspirationGainsCount));
                }
                else
                {
                    if (chosen.Count >= aspirationGainsCount)
                    {
                        TolkHelper.SpeakData("RimWorldAccess.Compat.Vae.AspirationLimitReached".Translate(aspirationGainsCount));
                        return;
                    }
                    chosen.Add(item.Def);
                    TolkHelper.SpeakData("RimWorldAccess.Compat.Vae.AspirationSelectedFeedback".Translate(
                        item.Label, chosen.Count, aspirationGainsCount));
                }
            }
        }

        private static void SelectTrait(int traitIndex)
        {
            if (traitIndex < 0 || traitIndex >= traitItems.Count)
                return;

            TraitItem item = traitItems[traitIndex];

            if (item.IsNoTrait)
            {
                SelectedTrait = ChoiceLetter_GrowthMoment.NoTrait;
                TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.NoTraitSelected".Translate());
            }
            else
            {
                SelectedTrait = item.TraitOption;
                TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.TraitSelected".Translate(item.Label));
            }
        }

        private static void ConfirmChoices()
        {
            if (isArchiveView)
                return;

            List<SkillDef> chosenPassions = SelectedPassions;
            if (!letter.passionChoices.NullOrEmpty() && chosenPassions.Count != letter.passionGainsCount)
            {
                if (letter.passionGainsCount == 1)
                    TolkHelper.Speak("SelectPassionSingular".Loc(), SpeechPriority.High);
                else
                    TolkHelper.Speak("SelectPassionsPlural".Loc(letter.passionGainsCount), SpeechPriority.High);
                return;
            }

            Trait chosenTrait = SelectedTrait;
            if (!letter.traitChoices.NullOrEmpty() && chosenTrait == null)
            {
                TolkHelper.Speak("SelectATrait".Loc(), SpeechPriority.High);
                return;
            }

            // Mirrors Dialog_GrowthMomentChoices_Aspirations.CanCloseOverride's aspiration clause.
            IList chosenAspirations = SelectedAspirations;
            if (aspirationItems.Count > 0 && chosenAspirations.Count != aspirationGainsCount)
            {
                if (aspirationGainsCount == 1)
                    TolkHelper.Speak("RimWorldAccess.Compat.Vae.SelectAspirationSingular".Loc(), SpeechPriority.High);
                else
                    TolkHelper.Speak("RimWorldAccess.Compat.Vae.SelectAspirationsPlural".Loc(aspirationGainsCount), SpeechPriority.High);
                return;
            }

            // Save references first: TryRemove trips the PostClose patch, which nulls letter/dialog.
            string pawnName = letter.pawn?.LabelShort ?? "RimWorldAccess.Biotech.GrowthMoment.PawnFallback".Translate().ToString();
            var letterRef = letter;
            var dialogRef = dialog;

            // VAE's own OK button calls MakeAspirationChoices before MakeChoices unconditionally
            // whenever the letter is its aspirations letter; mirrored in the same order.
            if (VaeCompat.IsAspirationLetter(letterRef))
                VaeCompat.ApplyAspirationChoices(letterRef, chosenAspirations);
            letterRef.MakeChoices(chosenPassions, chosenTrait);

            Close();

            if (dialogRef != null)
                Find.WindowStack.TryRemove(dialogRef);
            Find.LetterStack.RemoveLetter(letterRef);

            TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.Confirmed".Translate(pawnName));
        }

        // MUTATION-C: mirrors Dialog_GrowthMomentChoices's "Later" button handler
        // (:104-114) exactly — it never calls MakeChoices, regardless of whether
        // there are outstanding choices; it either rejects (ShouldAutomaticallyOpenLetter)
        // or Close()s, always leaving the letter revisitable. The prior version here
        // force-committed via ConfirmChoices() when nothing was left to choose, which
        // has no vanilla counterpart and permanently resolved a letter "Later" would
        // have left open (ChoiceLetter_GrowthMoment.cs's growthPoints/canGainGrowthPoints
        // reset only happens inside MakeChoices, :196-197).
        private static void PostponeChoices()
        {
            if (letter.ShouldAutomaticallyOpenLetter)
            {
                TolkHelper.Speak("MessageCannotPostponeGrowthMoment".Loc(letter.pawn.Named("PAWN")), SpeechPriority.High);
                return;
            }

            CloseDialog();
        }

        private static void CloseDialog()
        {
            var dialogRef = dialog;

            Close();

            if (dialogRef != null)
                Find.WindowStack.TryRemove(dialogRef);

            TolkHelper.SpeakData("RimWorldAccess.Biotech.GrowthMoment.Postponed".Translate());
        }

        /// <summary>
        /// The screen's opening text: every Info line, then the how-to lines. The landing tab and
        /// the focused row are not folded in; the chassis speaks both right after this.
        /// </summary>
        internal static string BuildOpeningText()
        {
            if (letter == null)
                return null;

            var parts = new List<string>();

            if (infoLines != null)
            {
                foreach (string line in infoLines)
                {
                    parts.Add(line);
                }
            }

            if (!isArchiveView)
            {
                if (!HasChoicesToMake())
                {
                    parts.Add("RimWorldAccess.Biotech.GrowthMoment.PressEnterToConfirm".Translate());
                }
                else
                {
                    string tabCount = "RimWorldAccess.Biotech.GrowthMoment.TabCount".Translate(availableTabs.Count);

                    if (!letter.passionChoices.NullOrEmpty() && letter.passionGainsCount > 0)
                    {
                        parts.Add("RimWorldAccess.Biotech.GrowthMoment.ChoosePassionsAndTrait".Translate(
                            letter.passionGainsCount, passionItems.Count, tabCount));
                    }
                    else
                    {
                        parts.Add("RimWorldAccess.Biotech.GrowthMoment.ChooseTraitOnly".Translate(tabCount));
                    }

                    if (aspirationItems.Count > 0)
                    {
                        parts.Add("RimWorldAccess.Compat.Vae.ChooseAspirations".Translate(
                            aspirationGainsCount, aspirationItems.Count));
                    }
                }
            }

            return string.Join(". ", parts);
        }

        /// <summary>
        /// One row of one tab. Position is the chassis's to fill; each per-tab builder sets only its
        /// own Label/Role/Selected/Extras.
        /// </summary>
        internal static ElementDescription DescribeRow(int region, int index)
        {
            if (region < 0 || region >= availableTabs.Count)
                return new ElementDescription();
            switch (availableTabs[region])
            {
                case Tab.Passions: return DescribePassionItem(index);
                case Tab.Traits: return DescribeTraitItem(index);
                case Tab.Aspirations: return DescribeAspirationItem(index);
                case Tab.Character: return DescribeLine(characterLines, index);
                case Tab.Health: return DescribeLine(healthLines, index);
                default: return DescribeLine(infoLines, index);
            }
        }

        /// <summary>A read-only prose line, shared by the Info tab and the two side panels.</summary>
        private static ElementDescription DescribeLine(string[] lines, int index)
        {
            var d = new ElementDescription();
            if (lines == null || index < 0 || index >= lines.Length)
                return d;
            d.Label = lines[index];
            d.ReadOnly = true;
            return d;
        }

        /// <summary>
        /// RadioButton when the letter offers exactly one passion slot, Checkbox when it offers
        /// several, matching the mutation the row performs. Selected, not Check, carries the state
        /// so the spoken word is the same whichever role applies; Extras is the current-to-new
        /// passion transition alone.
        /// </summary>
        private static ElementDescription DescribePassionItem(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= passionItems.Count)
                return d;

            PassionItem item = passionItems[index];
            string currentName = GetPassionName(item.CurrentPassion);
            string newName = GetPassionName(item.NewPassion);

            d.Label = item.Label;
            d.Role = letter.passionGainsCount == 1 ? ElementRole.RadioButton : ElementRole.Checkbox;
            d.Selected = SelectedPassions.Contains(item.Skill);
            d.Extras = "RimWorldAccess.Biotech.GrowthMoment.PassionTransitionDetail".Translate(currentName, newName);
            return d;
        }

        /// <summary>Always exactly one trait choice, so always a RadioButton row.</summary>
        private static ElementDescription DescribeTraitItem(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= traitItems.Count)
                return d;

            TraitItem item = traitItems[index];
            Trait chosen = SelectedTrait;
            d.Label = item.Label;
            d.Role = ElementRole.RadioButton;
            d.Selected = item.IsNoTrait
                ? chosen == ChoiceLetter_GrowthMoment.NoTrait
                : chosen == item.TraitOption;
            d.Extras = item.Description;
            return d;
        }

        /// <summary>The same radio-vs-checkbox split as <see cref="DescribePassionItem"/>, keyed off the gains count.</summary>
        private static ElementDescription DescribeAspirationItem(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= aspirationItems.Count)
                return d;

            VaeCompat.AspirationChoice item = aspirationItems[index];
            d.Label = item.Label;
            d.Role = aspirationGainsCount == 1 ? ElementRole.RadioButton : ElementRole.Checkbox;
            d.Selected = SelectedAspirations.Contains(item.Def);
            d.Extras = item.Details;
            return d;
        }

        private static void BuildInfoLines()
        {
            var lines = new List<string>();

            if (letter.text != null)
            {
                string cleanText = StripTags(letter.text.Resolve());
                string[] splitLines = cleanText.Split('\n');
                foreach (string line in splitLines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        lines.Add(trimmed);
                }
            }

            if (letter.growthTier >= 0)
            {
                lines.Add("RimWorldAccess.Biotech.GrowthMoment.GrowthTier".Translate(letter.growthTier));
            }

            if (letter.pawn?.Name != null && letter.oldName != null && letter.pawn.Name != letter.oldName)
            {
                string oldFull = letter.oldName.ToStringFull;
                string newShort = letter.pawn.LabelShort;
                lines.Add("RimWorldAccess.Biotech.GrowthMoment.NicknameChanged".Translate(
                    StripTags(oldFull), StripTags(newShort)));
            }

            // The explainer VAE's dialog draws under the aspiration choice list.
            if (!isArchiveView && aspirationItems.Count > 0)
            {
                lines.Add(StripTags("RimWorldAccess.Compat.Vae.AspirationDesc".Translate(letter.pawn.NameShortColored)));
            }

            if (isArchiveView)
            {
                if (!letter.chosenPassions.NullOrEmpty())
                {
                    string passionList = string.Join(", ", letter.chosenPassions.Select(s => s.label));
                    lines.Add("RimWorldAccess.Biotech.GrowthMoment.ChosenPassions".Translate(passionList));
                }

                if (letter.chosenTrait != null)
                {
                    string traitLabel = letter.chosenTrait == ChoiceLetter_GrowthMoment.NoTrait
                        ? "RimWorldAccess.Biotech.GrowthMoment.NoTraitChosen".Translate().ToString()
                        : letter.chosenTrait.LabelCap;
                    lines.Add("RimWorldAccess.Biotech.GrowthMoment.ChosenTrait".Translate(traitLabel));
                }

                // Mirrors Dialog_GrowthMomentChoices_Aspirations' own archive-view line.
                List<string> chosenAspirationLabels = VaeCompat.GetChosenAspirationLabels(letter);
                if (chosenAspirationLabels != null)
                {
                    lines.Add(chosenAspirationLabels.Count == 1
                        ? "RimWorldAccess.Compat.Vae.ArchiveSingular".Translate(chosenAspirationLabels[0])
                        : "RimWorldAccess.Compat.Vae.ArchivePlural".Translate(string.Join(", ", chosenAspirationLabels)));
                }
            }

            infoLines = lines.ToArray();
        }

        private static void BuildPassionItems()
        {
            passionItems.Clear();

            if (isArchiveView || letter.passionChoices.NullOrEmpty() || letter.passionGainsCount <= 0)
                return;

            foreach (SkillDef skillDef in letter.passionChoices)
            {
                SkillRecord skill = letter.pawn?.skills?.GetSkill(skillDef);
                if (skill == null) continue;

                passionItems.Add(new PassionItem
                {
                    Skill = skillDef,
                    CurrentPassion = skill.passion,
                    NewPassion = skill.passion.IncrementPassion(),
                    SkillLevel = skill.Level,
                    Label = skillDef.LabelCap
                });
            }
        }

        private static void BuildTraitItems()
        {
            traitItems.Clear();

            if (isArchiveView || letter.traitChoices.NullOrEmpty())
                return;

            foreach (Trait trait in letter.traitChoices)
            {
                string description = "";
                try
                {
                    // Trait.TipString builds a multi-line tooltip and StripTags only removes HTML
                    // tags, so flatten the embedded newlines into sentence breaks before speech.
                    description = StripTags(trait.TipString(letter.pawn))
                        .Replace("\r", "").Replace("\n\n", ". ").Replace("\n", ". ").Trim();
                }
                catch
                {
                }

                traitItems.Add(new TraitItem
                {
                    TraitOption = trait,
                    Label = trait.LabelCap,
                    Description = description,
                    IsNoTrait = false
                });
            }

            if (letter.noTraitOptionShown)
            {
                string noTraitLabel = "BirthdayNoTraitChoice".Translate();
                string noTraitDesc = "";
                try
                {
                    noTraitDesc = StripTags("BirthdayNoTraitChoiceTooltip".Translate(letter.pawn));
                }
                catch { }

                traitItems.Add(new TraitItem
                {
                    TraitOption = ChoiceLetter_GrowthMoment.NoTrait,
                    Label = noTraitLabel,
                    Description = noTraitDesc,
                    IsNoTrait = true
                });
            }
        }

        /// <summary>Builds the Aspirations tab content from a VAE aspirations letter, if any.</summary>
        private static void BuildAspirationItems()
        {
            aspirationItems.Clear();
            aspirationGainsCount = 0;

            if (isArchiveView)
                return;

            if (VaeCompat.TryGetAspirationChoices(letter, out List<VaeCompat.AspirationChoice> choices, out int gains))
            {
                aspirationItems.AddRange(choices);
                aspirationGainsCount = gains;
            }
        }

        /// <summary>
        /// Builds the Character/Health tab content from the same tree adapters the pawn's real
        /// inspect tabs use, read-only to match the side panel's non-interactive display. Each
        /// adapter's top-level child Labels already self-fold their full subtree, so reading them
        /// flat one per line loses nothing against vanilla's own card drawing.
        /// </summary>
        private static void BuildCharacterAndHealthLines()
        {
            characterLines = null;
            healthLines = null;

            Pawn pawn = letter.pawn;
            if (isArchiveView || pawn == null || !letter.ShowInfoTabs)
                return;

            characterLines = BuildCategoryLines(pawn, new PawnCharacterAdapter());
            healthLines = BuildCategoryLines(pawn, new PawnHealthAdapter());
        }

        private static string[] BuildCategoryLines(Pawn pawn, InspectNodeAdapter adapter)
        {
            var categoryItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = adapter.CategoryKey,
                IndentLevel = 0,
                IsExpandable = true,
                IsExpanded = true
            };
            adapter.BuildChildren(categoryItem, pawn, InspectionMode.ReadOnly);
            return categoryItem.Children.Select(c => c.Label).ToArray();
        }

        private static void BuildAvailableTabs()
        {
            availableTabs.Clear();
            availableTabs.Add(Tab.Info);

            if (!isArchiveView)
            {
                if (passionItems.Count > 0)
                    availableTabs.Add(Tab.Passions);

                if (traitItems.Count > 0)
                    availableTabs.Add(Tab.Traits);

                if (aspirationItems.Count > 0)
                    availableTabs.Add(Tab.Aspirations);

                // Mirrors letter.ShowInfoTabs: the side panels only appear alongside an actual
                // passion/trait choice, never on their own.
                if (letter.ShowInfoTabs)
                {
                    availableTabs.Add(Tab.Character);
                    availableTabs.Add(Tab.Health);
                }
            }
        }

        private static string GetTabName(Tab tab)
        {
            switch (tab)
            {
                case Tab.Info: return "RimWorldAccess.Biotech.GrowthMoment.TabInfo".Translate();
                case Tab.Passions: return "RimWorldAccess.Biotech.GrowthMoment.TabPassions".Translate();
                case Tab.Traits: return "RimWorldAccess.Biotech.GrowthMoment.TabTraits".Translate();
                case Tab.Aspirations: return "RimWorldAccess.Compat.Vae.TabAspirations".Translate();
                // Vanilla's own TabRecord labels for these two panels.
                case Tab.Character: return "TabCharacter".Translate();
                case Tab.Health: return "TabHealth".Translate();
                default: return "RimWorldAccess.Biotech.GrowthMoment.TabUnknown".Translate();
            }
        }

        private static string GetPassionName(Passion passion)
        {
            switch (passion)
            {
                case Passion.None: return "RimWorldAccess.Biotech.GrowthMoment.PassionNone".Translate();
                case Passion.Minor: return "RimWorldAccess.Biotech.GrowthMoment.PassionMinor".Translate();
                case Passion.Major: return "RimWorldAccess.Biotech.GrowthMoment.PassionMajor".Translate();
                default: return passion.GetLabel();
            }
        }

        private static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return Regex.Replace(text, @"</?[a-zA-Z][^>]*>", "");
        }
    }
}
