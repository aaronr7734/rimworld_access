using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Editors for the ideoligion's name, adjective, member name, worship room label,
    /// description, styles, icon, and color. Text fields use the unified modal
    /// TextInputController; icon / color / style use the windowless float menu.
    ///
    /// Opened from the builder hub. Each edit, on confirm, mutates the live Ideo, regenerates
    /// any derived data (precept names, description), and asks the hub to refresh + re-announce.
    /// </summary>
    public static class IdeoSymbolEditState
    {
        // Vanilla's symbol validation: letters, digits, spaces, apostrophes, hyphens; max 40 chars.
        private static readonly Regex ValidSymbolRegex = new Regex("^[\\p{L}0-9 '\\-]*$");
        private const int MaxSymbolLength = 40;

        private static readonly TextInputController controller = new TextInputController();

        private static TextFieldSpec SymbolSpec(string labelKey) =>
            new TextFieldSpec(labelKey, maxLength: MaxSymbolLength, minLength: 1, allowedChars: ValidSymbolRegex);

        #region Text fields

        public static void EditName(Ideo ideo)
        {
            controller.Begin(ideo.name, SymbolSpec("Name"),
                text =>
                {
                    ideo.name = text.Trim();
                    ideo.MakeMemeberNamePluralDirty();
                    ideo.RegenerateAllPreceptNames();
                    AfterEdit();
                });
        }

        public static void EditAdjective(Ideo ideo)
        {
            controller.Begin(ideo.adjective, SymbolSpec("Adjective"),
                text =>
                {
                    ideo.adjective = text.Trim();
                    ideo.MakeMemeberNamePluralDirty();
                    ideo.RegenerateAllPreceptNames();
                    AfterEdit();
                });
        }

        public static void EditMemberName(Ideo ideo)
        {
            controller.Begin(ideo.memberName, SymbolSpec("IdeoMembers"),
                text =>
                {
                    ideo.memberName = text.Trim();
                    ideo.MakeMemeberNamePluralDirty();
                    ideo.RegenerateAllPreceptNames();
                    AfterEdit();
                });
        }

        /// <summary>
        /// Worship room has a Reset action (revert to the auto-generated default) in addition
        /// to manual entry, mirroring Dialog_ChooseIdeoSymbols, so it opens a small menu.
        /// </summary>
        public static void OpenWorshipRoomMenu(Ideo ideo)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit".Translate(), () =>
                    controller.Begin(ideo.WorshipRoomLabel, SymbolSpec("WorshipRoom"),
                        text => { ideo.WorshipRoomLabel = text.Trim(); AfterEdit(); })),
                new FloatMenuOption("Reset".Translate(), () =>
                {
                    ideo.WorshipRoomLabel = null; // reverts to the generated default
                    AfterEdit();
                }),
            };
            TolkHelper.Speak("WorshipRoom".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        /// <summary>
        /// The narrative supports manual entry, randomization, and a lock that controls whether
        /// it auto-regenerates when memes/precepts change — all of which vanilla exposes
        /// (Dialog_EditIdeoDescription + the lock button in IdeoUIUtility.DoDescription).
        /// </summary>
        public static void OpenDescriptionMenu(Ideo ideo)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("EditNarrative".Translate(), () =>
                    controller.Begin(ideo.description, TextFieldSpec.MultiLineUnrestricted("Description"),
                        text =>
                        {
                            ideo.description = text;
                            ideo.descriptionTemplate = null;
                            ideo.descriptionLocked = true;
                            AfterEdit();
                        })),
                new FloatMenuOption("Randomize".Translate(), () =>
                {
                    var result = ideo.GetNewDescription(force: true);
                    ideo.description = result.text;
                    ideo.descriptionTemplate = result.template;
                    ideo.descriptionLocked = true;
                    AfterEdit();
                }),
                // Lock toggle. The label states the CURRENT lock state; selecting it flips it.
                new FloatMenuOption(LockStateText(ideo), () =>
                {
                    ideo.descriptionLocked = !ideo.descriptionLocked;
                    (ideo.descriptionLocked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff)
                        .PlayOneShotOnCamera();
                    TolkHelper.SpeakData(LockStateText(ideo), SpeechPriority.High);
                }),
            };
            TolkHelper.Speak("CoreNarrative".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static string LockStateText(Ideo ideo)
        {
            return (ideo.descriptionLocked ? "LockInOn" : "LockInOff")
                .Translate("Narrative".Translate(), "NarrativeLower".Translate());
        }

        #endregion

        #region Icon / Color / Styles pickers

        public static void OpenIconPicker(Ideo ideo)
        {
            var options = new List<FloatMenuOption>();
            foreach (var iconDef in DefDatabase<IdeoIconDef>.AllDefs)
            {
                var captured = iconDef;
                string label = iconDef.label.NullOrEmpty() ? iconDef.defName : iconDef.LabelCap.ToString();
                if (iconDef == ideo.iconDef) label += ". " + "RimWorldAccess.Ideology.Builder.PreceptCurrent".Translate();
                options.Add(new FloatMenuOption(label, () =>
                {
                    ideo.SetIcon(captured, ideo.colorDef);
                    AfterEdit();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            TolkHelper.Speak("Icon".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        public static void OpenColorPicker(Ideo ideo)
        {
            var options = new List<FloatMenuOption>();
            var colors = DefDatabase<ColorDef>.AllDefsListForReading
                .Where(c => c.colorType == ColorType.Ideo)
                .ToList();
            foreach (var colorDef in colors)
            {
                var captured = colorDef;
                string label = colorDef.label.NullOrEmpty() ? colorDef.defName : colorDef.LabelCap.ToString();
                if (colorDef == ideo.colorDef) label += ". " + "RimWorldAccess.Ideology.Builder.PreceptCurrent".Translate();
                options.Add(new FloatMenuOption(label, () =>
                {
                    ideo.SetIcon(ideo.iconDef, captured, clearPrimaryFactionColor: true);
                    AfterEdit();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            TolkHelper.Speak("Color".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        public static void OpenCulturePicker(Ideo ideo)
        {
            var options = new List<FloatMenuOption>();
            foreach (var culture in DefDatabase<CultureDef>.AllDefs.OrderBy(c => c.label))
            {
                var captured = culture;
                string label = culture.LabelCap.ToString();
                if (!string.IsNullOrEmpty(culture.description))
                    label += ". " + culture.description;
                if (culture == ideo.culture) label += ". " + "RimWorldAccess.Ideology.Builder.PreceptCurrent".Translate();
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (ideo.culture != captured)
                    {
                        ideo.culture = captured;
                        ideo.foundation.RandomizeStyles();
                        ideo.style.RecalculateAvailableStyleItems();
                        if (ideo.foundation is IdeoFoundation_Deity deityFoundation)
                            deityFoundation.GenerateDeities();
                        ideo.RegenerateDescription(force: true);
                    }
                    AfterEdit();
                }));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            TolkHelper.Speak("ChooseCulture".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        public static void OpenStylePicker(Ideo ideo)
        {
            // Top-level: list current style slots + an add option, mirroring vanilla's 3-slot model.
            var options = new List<FloatMenuOption>();
            var slots = ideo.thingStyleCategories;

            for (int i = 0; i < slots.Count; i++)
            {
                int slotIndex = i;
                string slotName = slots[i]?.category != null ? slots[i].category.LabelCap.ToString() : "Random".Translate().ToString();
                options.Add(new FloatMenuOption("Styles".Translate() + " " + (i + 1) + ": " + slotName,
                    () => OpenStyleSlotPicker(ideo, slotIndex)));
            }

            if (slots.Count < 3)
                options.Add(new FloatMenuOption("AddStyleCategory".Translate().ToString(), () => OpenStyleSlotPicker(ideo, -1)));

            TolkHelper.Speak("Styles".Loc());
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void OpenStyleSlotPicker(Ideo ideo, int slotIndex)
        {
            var slots = ideo.thingStyleCategories;
            var options = new List<FloatMenuOption>();

            var available = DefDatabase<StyleCategoryDef>.AllDefs
                .Where(s => !s.fixedIdeoOnly && !slots.Any(p => p?.category == s))
                .ToList();

            foreach (var style in available)
            {
                var captured = style;
                string styleLabel = style.LabelCap.ToString();
                if (!string.IsNullOrEmpty(style.description))
                    styleLabel += ". " + style.description;
                options.Add(new FloatMenuOption(styleLabel, () =>
                {
                    if (slotIndex == -1)
                        slots.Add(new ThingStyleCategoryWithPriority(captured, slots.Count == 0 ? 2 : 1));
                    else
                        slots[slotIndex].category = captured;
                    ideo.SortStyleCategories();
                    ideo.style.RecalculateAvailableStyleItems();
                    AfterEdit();
                }));
            }

            // Remove option for existing slots (keep at least one).
            if (slotIndex >= 0 && slots.Count > 1)
            {
                options.Add(new FloatMenuOption("Remove".Translate().ToString(), () =>
                {
                    slots.RemoveAt(slotIndex);
                    ideo.SortStyleCategories();
                    ideo.style.RecalculateAvailableStyleItems();
                    AfterEdit();
                }));
            }

            if (options.Count == 0)
                options.Add(new FloatMenuOption("NoneLower".Translate(), null));
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        #endregion

        private static void AfterEdit()
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            // Refresh whichever builder context launched this edit.
            if (IdeoSectionEditorState.IsActive)
            {
                IdeoSectionEditorState.Refresh();
            }
            else if (IdeoReformState.IsActive)
            {
                IdeoReformState.RefreshSections();
            }
            else
            {
                IdeoBuilderHubState.RebuildSections();
                IdeoBuilderHubState.AnnounceCurrentSection();
            }
        }
    }
}
