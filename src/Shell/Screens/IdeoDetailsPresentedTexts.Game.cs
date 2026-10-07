using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Extras-haystack composites (see <see cref="ScreenScope.AdditionalPresentedTexts"/>) for what
    /// vanilla's shared <c>IdeoUIUtility.DoIdeoDetails</c> draws, whichever host is presenting it.
    ///
    /// Two entry points rather than one, because YIELD ORDER MATTERS:
    /// <see cref="ScreenScope.BuildPresentedHaystacks"/> joins the composites into one contiguous
    /// string and normalization strips every separator, so the diff is a containment check and
    /// reordering fragments is not inert. <see cref="DescriptionLockIconStates"/> is a page-global
    /// pair of texture-name literals, yielded before the host's own edit-mode chrome;
    /// <see cref="ForIdeo"/> is the ideo-keyed bulk, yielded inside the host's ideo block.
    ///
    /// The ritual-ambience-preview icon literals stay inline in <c>IdeoBuilderScreenScope</c> rather
    /// than moving here: that button is wrapped in <c>ProgramState == Entry</c>
    /// (IdeoUIUtility.cs:659-665) and so is worldgen-only, while the description-lock button is
    /// gated only on <c>editMode != IdeoEditMode.None</c> (:1084-1087) and every edit-affordance
    /// host draws it.
    /// </summary>
    public static class IdeoDetailsPresentedTexts
    {
        // MUTATION-C-adjacent read (no write): IdeoUIUtility.showAll has no public accessor, and
        // PreceptCategoryLabels must match vanilla's own `showAll || def.visible` test (:1436).
        private static readonly FieldInfo ShowAllField = AccessTools.Field(typeof(IdeoUIUtility), "showAll");

        private static bool ShowAll
        {
            get { return ShowAllField != null && (bool)ShowAllField.GetValue(null); }
        }

        /// <summary>
        /// The description-lock ButtonImage's two texture-name states (IdeoUIUtility.cs:1087), drawn
        /// by any non-<c>IdeoEditMode.None</c> host and keyed off no particular Ideo.
        /// </summary>
        public static IEnumerable<string> DescriptionLockIconStates(bool editAffordancesVisible)
        {
            if (!editAffordancesVisible)
            {
                yield break;
            }
            yield return "LockedMonochrome";
            yield return "UnlockedMonochrome";
        }

        /// <summary>
        /// The "ClickAnyElementToEditIt" caption (IdeoUIUtility.cs:711-715), drawn by any
        /// edit-affordance host — its block carries no <c>ProgramState</c> gate. Not ideo-keyed.
        /// </summary>
        public static IEnumerable<string> EditModeCaption(bool editAffordancesVisible)
        {
            if (!editAffordancesVisible)
            {
                yield break;
            }
            yield return (string)"ClickAnyElementToEditIt".Translate();
        }

        /// <summary>
        /// "AddDeity"/"RandomizeDeities" (IdeoFoundation_Deity.DoInfo:106-120), gated exactly like
        /// <see cref="EditModeCaption"/>. Vanilla's extra <c>deities.Count &lt; max</c> gate on
        /// "AddDeity" is not replicated: over-yielding into this haystack only ever narrows a false
        /// positive, never widens a false negative.
        /// </summary>
        public static IEnumerable<string> DeitySectionButtonLabels(bool editAffordancesVisible)
        {
            if (!editAffordancesVisible)
            {
                yield break;
            }
            yield return (string)"AddDeity".Translate();
            yield return (string)"RandomizeDeities".Translate();
        }

        /// <summary>
        /// The ideo's non-structure memes fused into ONE composite in vanilla's own draw order
        /// (IdeoUIUtility.DoMemes:1143-1149, <c>ideo.memes</c> order filtered to
        /// <c>category != MemeCategory.Structure</c>, with no tier grouping). Fused, not per-meme: a
        /// captured row carrying several meme boxes on one line matches by containment only against
        /// a composite covering them all. Separators need no care — normalization strips them.
        /// </summary>
        public static IEnumerable<string> NonStructureMemeNamesFused(Ideo ideo)
        {
            if (ideo == null || ideo.memes == null)
            {
                yield break;
            }
            var names = ideo.memes.Where(m => m.category != MemeCategory.Structure)
                .Select(m => m.LabelCap.ToString()).ToList();
            if (names.Count > 0)
            {
                yield return string.Join(", ", names);
            }
        }

        /// <summary>
        /// Every per-category "Add &lt;thing&gt;..." button label (IdeoUIUtility.DoPreceptsInt:1460-1464),
        /// drawn by any edit-affordance host. No typed row mirrors these strings —
        /// <see cref="IdeoEditorRegionCore"/>'s Section rows carry the FACET name instead — so every
        /// host needs the composite. Reconstructed exactly as vanilla builds it:
        /// <c>"AddPrecept".Translate(singular).CapitalizeFirst() + "..."</c>.
        /// </summary>
        public static IEnumerable<string> AddPreceptButtonLabels(bool editAffordancesVisible)
        {
            if (!editAffordancesVisible)
            {
                yield break;
            }
            yield return "AddPrecept".Translate((string)"Precept".Translate()).CapitalizeFirst() + "...";
            yield return "AddPrecept".Translate((string)"Role".Translate()).CapitalizeFirst() + "...";
            yield return "AddPrecept".Translate((string)"Ritual".Translate()).CapitalizeFirst() + "...";
            yield return "AddPrecept".Translate((string)"IdeoBuilding".Translate()).CapitalizeFirst() + "...";
            yield return "AddPrecept".Translate((string)"IdeoRelic".Translate()).CapitalizeFirst() + "...";
            yield return "AddPrecept".Translate((string)"IdeoWeapon".Translate()).CapitalizeFirst() + "...";
            yield return "AddPrecept".Translate((string)"Animal".Translate()).CapitalizeFirst() + "...";
            if (ModsConfig.BiotechActive)
            {
                yield return "AddPrecept".Translate("Xenotype".Translate().ToString().UncapitalizeFirst()).CapitalizeFirst() + "...";
            }
            yield return "AddPrecept".Translate((string)"IdeoApparelDesire".Translate()).CapitalizeFirst() + "...";
        }

        /// <summary>
        /// Every standard precept category's label, UNCONDITIONALLY, plus the greyed "(None)" when
        /// that category's own precepts are empty (IdeoUIUtility.DoPreceptsInt:1441 skip rule, :1493
        /// the "(None)" draw). Unconditional rather than empty-only: BuildPreceptCategorySection's
        /// single-precept flattening (IdeologyHelper.cs:795-806) drops the category label from the
        /// tree even for a non-empty category, so no per-ideo count predicts whether the tree
        /// mirrors it. Over-supplying the haystack is harmless. The "(None)" decision follows
        /// vanilla's own <c>showAll || def.visible</c> gate (:1436), not
        /// <see cref="IdeologyHelper"/>'s narrower <c>.visible</c> test, because it must match what
        /// vanilla's draw pass — and therefore the capture — actually produced. Mod-added categories
        /// go through <see cref="DiscoveredPreceptCategoryTexts"/>.
        /// </summary>
        private static IEnumerable<string> PreceptCategoryLabels(Ideo ideo)
        {
            if (ideo == null)
            {
                yield break;
            }
            bool showAll = ShowAll;
            string none = "(" + (string)"NoneLower".Translate() + ")";
            foreach (var category in PreceptCategoryFilters())
            {
                yield return category.Label;
                bool any = ideo.PreceptsListForReading.Any(p => (showAll || p.def.visible) && category.Filter(p.def));
                if (!any)
                {
                    yield return none;
                }
            }
            foreach (string text in DiscoveredPreceptCategoryTexts(ideo, showAll, none))
            {
                yield return text;
            }
        }

        /// <summary>
        /// The same chrome for a MOD-added precept category. A mod's <c>DoPrecepts</c> postfix passes
        /// its heading and singular noun to vanilla's private <c>DoPreceptsInt</c> already
        /// translated, so neither is readable here; both are reconstructed from
        /// <see cref="IdeoBuilderHelper.DiscoveredPreceptClass.Label"/>, which also titles the typed
        /// section row. A mod naming its heading otherwise leaves that line genuinely unmirrored,
        /// which is what the captured-extras region is for. The add button is yielded regardless of
        /// edit mode, since over-supplying only narrows a false positive.
        /// </summary>
        private static IEnumerable<string> DiscoveredPreceptCategoryTexts(Ideo ideo, bool showAll, string none)
        {
            foreach (IdeoBuilderHelper.DiscoveredPreceptClass discovered in IdeoBuilderHelper.DiscoverExtraPreceptClasses(ideo))
            {
                string label = discovered.Label;
                yield return label;
                yield return "AddPrecept".Translate(label).CapitalizeFirst() + "...";
                bool any = ideo.PreceptsListForReading.Any(p => (showAll || p.def.visible) && discovered.PreceptClass.IsInstanceOfType(p));
                if (!any)
                {
                    yield return none;
                }
            }
        }

        /// <summary>The exact category list and order <c>IdeoUIUtility.DoPrecepts</c> (:1414-1427) and <see cref="IdeologyHelper.BuildIdeologyTree"/> both use.</summary>
        private static IEnumerable<(string Label, Func<PreceptDef, bool> Filter)> PreceptCategoryFilters()
        {
            yield return ((string)"Precepts".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept));
            yield return ((string)"IdeoRoles".Translate(), (PreceptDef p) => typeof(Precept_Role).IsAssignableFrom(p.preceptClass));
            yield return ((string)"Rituals".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept_Ritual));
            yield return ((string)"IdeoBuildings".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept_Building) || p.preceptClass == typeof(Precept_RitualSeat));
            yield return ((string)"IdeoRelics".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept_Relic));
            yield return ((string)"IdeoWeapons".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept_Weapon));
            yield return ((string)"VeneratedAnimals".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept_Animal));
            if (ModsConfig.BiotechActive)
            {
                yield return ((string)"PreferredXenotypes".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept_Xenotype));
            }
            yield return ((string)"IdeoApparel".Translate(), (PreceptDef p) => p.preceptClass == typeof(Precept_Apparel));
        }

        /// <summary>
        /// The ideo-keyed composites <c>DoIdeoDetails</c> draws regardless of edit mode: the
        /// Appearance boxes' "{0} in use" counts (:2030), the adjective/memberName header fusion,
        /// every precept's UIInfoFirstLine/SecondLine in <c>PreceptsListForReading</c> order, the
        /// icon-only affordances near the name/culture/structure/style cluster (labelled by texture
        /// name), <see cref="PreceptCategoryLabels"/>, and
        /// <see cref="NonStructureMemeNamesFused"/>.
        /// </summary>
        public static IEnumerable<string> ForIdeo(Ideo ideo)
        {
            if (ideo == null)
            {
                yield break;
            }
            // Vanilla draws ideo.description as ONE Label block with its newlines preserved, while
            // BuildNarrativeSection fuses the same text with spaces instead (IdeologyHelper.cs:601).
            // Yielded verbatim, byte for byte, so no fusion-shape mismatch is possible.
            if (!string.IsNullOrEmpty(ideo.description))
            {
                yield return ideo.description;
            }
            if (ideo.style != null)
            {
                yield return (string)"NumAvailable".Translate(ideo.style.NumHairAndBeardStylesAvailable);
                yield return (string)"NumAvailable".Translate(ideo.style.NumTattooStylesAvailable);
            }
            if (!string.IsNullOrEmpty(ideo.adjective) || !string.IsNullOrEmpty(ideo.memberName))
            {
                yield return (ideo.adjective ?? "") + " " + (ideo.memberName ?? "");
            }
            foreach (Precept precept in ideo.PreceptsListForReading)
            {
                string first = precept.UIInfoFirstLine;
                if (!string.IsNullOrEmpty(first))
                {
                    yield return first;
                }
                string second = precept.UIInfoSecondLine;
                if (!string.IsNullOrEmpty(second))
                {
                    yield return second;
                }
            }
            if (ideo.Icon != null && !string.IsNullOrEmpty(ideo.Icon.name))
            {
                yield return ideo.Icon.name;
            }
            if (ideo.culture != null && ideo.culture.Icon != null && !string.IsNullOrEmpty(ideo.culture.Icon.name))
            {
                yield return ideo.culture.Icon.name;
            }
            MemeDef structure = ideo.memes?.FirstOrDefault(m => m.category == MemeCategory.Structure);
            if (structure != null && structure.Icon != null && !string.IsNullOrEmpty(structure.Icon.name))
            {
                yield return structure.Icon.name;
            }
            if (ideo.thingStyleCategories != null)
            {
                foreach (var style in ideo.thingStyleCategories)
                {
                    if (style?.category?.Icon != null && !string.IsNullOrEmpty(style.category.Icon.name))
                    {
                        yield return style.category.Icon.name;
                    }
                }
            }
            foreach (string text in PreceptCategoryLabels(ideo))
            {
                yield return text;
            }
            foreach (string text in NonStructureMemeNamesFused(ideo))
            {
                yield return text;
            }
            foreach (string text in DeitySectionTexts(ideo))
            {
                yield return text;
            }
        }

        /// <summary>
        /// The "Deities" section label plus every deity's drawn name, type and portrait icon
        /// (IdeoFoundation_Deity.DoInfo:104/:183/:186/:180) — always drawn whenever the foundation
        /// has a deity, unlike <see cref="DeitySectionButtonLabels"/>'s buttons. Vouched here
        /// explicitly and unconditionally, so it cannot depend on the Details tree having been
        /// rebuilt for this exact ideo at capture time.
        /// </summary>
        private static IEnumerable<string> DeitySectionTexts(Ideo ideo)
        {
            if (!(ideo.foundation is IdeoFoundation_Deity deityFoundation))
            {
                yield break;
            }
            List<IdeoFoundation_Deity.Deity> deities = deityFoundation.DeitiesListForReading;
            if (deities == null || deities.Count == 0)
            {
                yield break;
            }
            yield return (string)"Deities".Translate();
            foreach (IdeoFoundation_Deity.Deity deity in deities)
            {
                if (!string.IsNullOrEmpty(deity.name))
                {
                    yield return deity.name;
                }
                if (!string.IsNullOrEmpty(deity.type))
                {
                    yield return deity.type;
                }
                if (deity.Icon != null && !string.IsNullOrEmpty(deity.Icon.name))
                {
                    yield return deity.Icon.name;
                }
            }
        }
    }
}
