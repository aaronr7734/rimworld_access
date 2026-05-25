using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Section model and builders for the IdeoBuilder hub.
    ///
    /// The hub is a flat menu where each row represents an editable facet of the ideoligion
    /// (name, structure meme, normal memes, deities, precepts, roles, rituals, etc.). Each
    /// row's label is built from the game's own translation keys and the current value of
    /// that facet on the live Ideo, so the hub stays in sync as edits happen and remains
    /// fully localized.
    /// </summary>
    public static class IdeoBuilderHelper
    {
        public enum SectionKind
        {
            Name,
            Adjective,
            MemberName,
            WorshipRoom,
            Description,
            Culture,
            Styles,
            Icon,
            Color,
            StructureMeme,
            NormalMemes,
            Deities,
            Precepts,
            Roles,
            Rituals,
            Buildings,
            Relics,
            Weapons,
            VeneratedAnimals,
            PreferredXenotypes,
            Apparel,
            Appearance,
        }

        public class HubSection
        {
            public SectionKind Kind;
            public string Label;
            public string ValueSummary;
            public bool Disabled;
            public string DisabledReason;
            public List<Def> InspectableDefs;
        }

        public static List<HubSection> BuildSections(Ideo ideo)
        {
            var sections = new List<HubSection>();
            if (ideo == null) return sections;

            sections.Add(BuildName(ideo));
            sections.Add(BuildAdjective(ideo));
            sections.Add(BuildMemberName(ideo));
            sections.Add(BuildWorshipRoom(ideo));
            sections.Add(BuildDescription(ideo));
            sections.Add(BuildCulture(ideo));
            sections.Add(BuildStyles(ideo));
            sections.Add(BuildIcon(ideo));
            sections.Add(BuildColor(ideo));
            sections.Add(BuildStructureMeme(ideo));
            sections.Add(BuildNormalMemes(ideo));

            if (ideo.foundation is IdeoFoundation_Deity)
                sections.Add(BuildDeities(ideo));

            sections.Add(BuildPrecepts(ideo));
            sections.Add(BuildPreceptType(ideo, SectionKind.Roles, "IdeoRoles", typeof(Precept_Role)));
            sections.Add(BuildPreceptType(ideo, SectionKind.Rituals, "Rituals", typeof(Precept_Ritual)));
            sections.Add(BuildBuildingsSection(ideo));
            sections.Add(BuildPreceptType(ideo, SectionKind.Relics, "IdeoRelics", typeof(Precept_Relic)));
            sections.Add(BuildPreceptType(ideo, SectionKind.Weapons, "IdeoWeapons", typeof(Precept_Weapon)));
            sections.Add(BuildPreceptType(ideo, SectionKind.VeneratedAnimals, "VeneratedAnimals", typeof(Precept_Animal)));

            if (ModsConfig.BiotechActive)
                sections.Add(BuildPreceptType(ideo, SectionKind.PreferredXenotypes, "PreferredXenotypes", typeof(Precept_Xenotype)));

            sections.Add(BuildPreceptType(ideo, SectionKind.Apparel, "IdeoApparel", typeof(Precept_Apparel)));
            sections.Add(BuildAppearance(ideo));

            return sections;
        }

        /// <summary>Allowed hair/beard and tattoo styles (vanilla's DoAppearanceItems).</summary>
        private static HubSection BuildAppearance(Ideo ideo)
        {
            return new HubSection
            {
                Kind = SectionKind.Appearance,
                Label = GetLocalizedSectionLabel(SectionKind.Appearance),
                ValueSummary = AppearanceSummary(ideo),
            };
        }

        /// <summary>"{n} hair and beards, {m} tattoos" — the count of available appearance styles.</summary>
        public static string AppearanceSummary(Ideo ideo)
        {
            if (ideo?.style == null) return "";
            return ideo.style.NumHairAndBeardStylesAvailable + " " + "HairAndBeards".Translate().ToString().ToLower()
                 + ", " + ideo.style.NumTattooStylesAvailable + " " + "Tattoos".Translate().ToString().ToLower();
        }

        public static string GetLocalizedSectionLabel(SectionKind kind)
        {
            switch (kind)
            {
                case SectionKind.Name: return "Name".Translate().CapitalizeFirst();
                case SectionKind.Adjective: return "Adjective".Translate().CapitalizeFirst();
                case SectionKind.MemberName: return "IdeoMembers".Translate().CapitalizeFirst();
                case SectionKind.WorshipRoom: return "WorshipRoom".Translate().CapitalizeFirst();
                case SectionKind.Description: return "Description".Translate().CapitalizeFirst();
                case SectionKind.Culture: return "Culture".Translate().CapitalizeFirst();
                case SectionKind.Styles: return "Styles".Translate().CapitalizeFirst();
                case SectionKind.Icon: return "Icon".Translate().CapitalizeFirst();
                case SectionKind.Color: return "Color".Translate().CapitalizeFirst();
                case SectionKind.StructureMeme: return "StructureMeme".Translate().CapitalizeFirst();
                case SectionKind.NormalMemes: return "Memes".Translate().CapitalizeFirst();
                case SectionKind.Deities: return "Deities".Translate().CapitalizeFirst();
                case SectionKind.Precepts: return "Precepts".Translate().CapitalizeFirst();
                case SectionKind.Roles: return "IdeoRoles".Translate().CapitalizeFirst();
                case SectionKind.Rituals: return "Rituals".Translate().CapitalizeFirst();
                case SectionKind.Buildings: return "IdeoBuildings".Translate().CapitalizeFirst();
                case SectionKind.Relics: return "IdeoRelics".Translate().CapitalizeFirst();
                case SectionKind.Weapons: return "IdeoWeapons".Translate().CapitalizeFirst();
                case SectionKind.VeneratedAnimals: return "VeneratedAnimals".Translate().CapitalizeFirst();
                case SectionKind.PreferredXenotypes: return "PreferredXenotypes".Translate().CapitalizeFirst();
                case SectionKind.Apparel: return "IdeoApparel".Translate().CapitalizeFirst();
                case SectionKind.Appearance: return "Appearance".Translate().CapitalizeFirst();
                default: return kind.ToString();
            }
        }

        #region Section builders

        private static HubSection BuildName(Ideo ideo)
        {
            string value = ideo.name.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.name;
            return new HubSection
            {
                Kind = SectionKind.Name,
                Label = GetLocalizedSectionLabel(SectionKind.Name),
                ValueSummary = value,
            };
        }

        private static HubSection BuildAdjective(Ideo ideo)
        {
            string value = ideo.adjective.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.adjective;
            return new HubSection
            {
                Kind = SectionKind.Adjective,
                Label = GetLocalizedSectionLabel(SectionKind.Adjective),
                ValueSummary = value,
            };
        }

        private static HubSection BuildMemberName(Ideo ideo)
        {
            string value = ideo.memberName.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.memberName;
            return new HubSection
            {
                Kind = SectionKind.MemberName,
                Label = GetLocalizedSectionLabel(SectionKind.MemberName),
                ValueSummary = value,
            };
        }

        private static HubSection BuildWorshipRoom(Ideo ideo)
        {
            string value = ideo.WorshipRoomLabel.NullOrEmpty()
                ? ("None".Translate().ToString())
                : ideo.WorshipRoomLabel;
            return new HubSection
            {
                Kind = SectionKind.WorshipRoom,
                Label = GetLocalizedSectionLabel(SectionKind.WorshipRoom),
                ValueSummary = value,
            };
        }

        private static HubSection BuildDescription(Ideo ideo)
        {
            string value;
            if (ideo.description.NullOrEmpty())
                value = "None".Translate().ToString();
            else
            {
                // Flatten whitespace only. Never truncate — the full description is presented.
                value = ideo.description.Replace("\r", " ").Replace("\n", " ").Trim();
            }
            return new HubSection
            {
                Kind = SectionKind.Description,
                Label = GetLocalizedSectionLabel(SectionKind.Description),
                ValueSummary = value,
            };
        }

        private static HubSection BuildCulture(Ideo ideo)
        {
            string value;
            if (ideo.culture == null)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                value = ideo.culture.LabelCap.ToString();
                if (!string.IsNullOrEmpty(ideo.culture.description))
                    value += ". " + ideo.culture.description;
            }
            return new HubSection
            {
                Kind = SectionKind.Culture,
                Label = GetLocalizedSectionLabel(SectionKind.Culture),
                ValueSummary = value,
            };
        }

        private static HubSection BuildStyles(Ideo ideo)
        {
            string value;
            if (ideo.thingStyleCategories == null || ideo.thingStyleCategories.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                value = string.Join(", ", ideo.thingStyleCategories
                    .Where(s => s?.category != null)
                    .Select(s => s.category.LabelCap.ToString()));
                if (string.IsNullOrEmpty(value))
                    value = "None".Translate().ToString();
            }
            return new HubSection
            {
                Kind = SectionKind.Styles,
                Label = GetLocalizedSectionLabel(SectionKind.Styles),
                ValueSummary = value,
            };
        }

        private static HubSection BuildIcon(Ideo ideo)
        {
            string value = ideo.iconDef != null
                ? (ideo.iconDef.label.NullOrEmpty() ? ideo.iconDef.defName : ideo.iconDef.LabelCap.ToString())
                : "None".Translate().ToString();
            return new HubSection
            {
                Kind = SectionKind.Icon,
                Label = GetLocalizedSectionLabel(SectionKind.Icon),
                ValueSummary = value,
            };
        }

        private static HubSection BuildColor(Ideo ideo)
        {
            string value = ideo.colorDef != null
                ? (ideo.colorDef.label.NullOrEmpty() ? ideo.colorDef.defName : ideo.colorDef.LabelCap.ToString())
                : "None".Translate().ToString();
            return new HubSection
            {
                Kind = SectionKind.Color,
                Label = GetLocalizedSectionLabel(SectionKind.Color),
                ValueSummary = value,
            };
        }

        private static HubSection BuildStructureMeme(Ideo ideo)
        {
            var structure = ideo.memes?.FirstOrDefault(m => m.category == MemeCategory.Structure);
            string value;
            if (structure == null)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                value = structure.LabelCap.ToString();
                if (!string.IsNullOrEmpty(structure.description))
                    value += ". " + structure.description;
            }
            var inspectable = structure != null ? new List<Def> { structure } : null;
            return new HubSection
            {
                Kind = SectionKind.StructureMeme,
                Label = GetLocalizedSectionLabel(SectionKind.StructureMeme),
                ValueSummary = value,
                InspectableDefs = inspectable,
            };
        }

        private static HubSection BuildNormalMemes(Ideo ideo)
        {
            var normals = ideo.memes?.Where(m => m.category == MemeCategory.Normal).ToList() ?? new List<MemeDef>();
            string value;
            if (normals.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                var names = string.Join(", ", normals.Select(m => m.LabelCap.ToString()));
                int impact = ImpactOf(normals);
                string impactLabel = IdeoImpactUtility.OverallImpactLabel(impact);
                value = $"{normals.Count}. {names}. {"IdeoImpact".Translate()}: {impactLabel}";
            }
            var inspectable = normals.Count > 0 ? normals.Cast<Def>().ToList() : null;
            return new HubSection
            {
                Kind = SectionKind.NormalMemes,
                Label = GetLocalizedSectionLabel(SectionKind.NormalMemes),
                ValueSummary = value,
                InspectableDefs = inspectable,
            };
        }

        private static HubSection BuildDeities(Ideo ideo)
        {
            var foundation = ideo.foundation as IdeoFoundation_Deity;
            string value;
            if (foundation == null || foundation.DeitiesListForReading == null || foundation.DeitiesListForReading.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                var names = foundation.DeitiesListForReading
                    .Select(d => string.IsNullOrEmpty(d.name) ? d.type ?? "" : d.name)
                    .Where(n => !string.IsNullOrEmpty(n));
                value = string.Join(", ", names);
                if (string.IsNullOrEmpty(value))
                    value = foundation.DeitiesListForReading.Count.ToString();
            }
            return new HubSection
            {
                Kind = SectionKind.Deities,
                Label = GetLocalizedSectionLabel(SectionKind.Deities),
                ValueSummary = value,
            };
        }

        private static HubSection BuildPrecepts(Ideo ideo)
        {
            // Base precepts (issue-based) excluding the typed precept lists which get their own sections.
            var basePrecepts = ideo.PreceptsListForReading
                .Where(p => !(p is Precept_Role)
                         && !(p is Precept_Ritual)
                         && !(p is Precept_Building)
                         && !(p is Precept_RitualSeat)
                         && !(p is Precept_Relic)
                         && !(p is Precept_Weapon)
                         && !(p is Precept_Animal)
                         && !(p is Precept_Xenotype)
                         && !(p is Precept_Apparel))
                .ToList();

            string value = basePrecepts.Count == 0
                ? "None".Translate().ToString()
                : basePrecepts.Count.ToString();
            return new HubSection
            {
                Kind = SectionKind.Precepts,
                Label = GetLocalizedSectionLabel(SectionKind.Precepts),
                ValueSummary = value,
            };
        }

        private static HubSection BuildPreceptType(Ideo ideo, SectionKind kind, string labelKey, System.Type preceptType)
        {
            var matching = ideo.PreceptsListForReading
                .Where(p => preceptType.IsInstanceOfType(p))
                .ToList();

            string value;
            if (matching.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                var names = string.Join(", ", matching.Select(PreceptLabel));
                value = $"{matching.Count}. {names}";
            }

            return new HubSection
            {
                Kind = kind,
                Label = GetLocalizedSectionLabel(kind),
                ValueSummary = value,
            };
        }

        private static HubSection BuildBuildingsSection(Ideo ideo)
        {
            // Buildings section covers both Precept_Building and Precept_RitualSeat (matches viewer).
            var matching = ideo.PreceptsListForReading
                .Where(p => p is Precept_Building || p is Precept_RitualSeat)
                .ToList();

            string value;
            if (matching.Count == 0)
            {
                value = "None".Translate().ToString();
            }
            else
            {
                var names = string.Join(", ", matching.Select(PreceptLabel));
                value = $"{matching.Count}. {names}";
            }

            return new HubSection
            {
                Kind = SectionKind.Buildings,
                Label = GetLocalizedSectionLabel(SectionKind.Buildings),
                ValueSummary = value,
            };
        }

        /// <summary>
        /// The display label for a precept, matching what vanilla draws in the precept box
        /// (UIInfoFirstLine, plus UIInfoSecondLine when it adds information) rather than the
        /// generic generated name. This is what surfaces the venerated animal, the desired
        /// apparel item, the noble/despised weapon classes, the role's title, etc.
        /// </summary>
        public static string PreceptLabel(Precept precept)
        {
            if (precept == null) return "";
            string first = precept.UIInfoFirstLine?.Trim();
            string second = precept.UIInfoSecondLine?.Trim();
            if (string.IsNullOrEmpty(first))
                first = (string)precept.LabelCap;
            if (!string.IsNullOrEmpty(second) && second != first)
                return first + ". " + second;
            return first;
        }

        /// <summary>
        /// Strips rich-text tags and unresolved grammar tokens (e.g. {ORGANIZER_labelShort}) from
        /// game text and collapses runs of whitespace, so screen-reader output never reads markup
        /// or template placeholders aloud.
        /// </summary>
        public static string CleanGameText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.StripTags();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\{[^{}]*\}", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[ \t]+", " ");
            return s.Trim();
        }

        #endregion

        /// <summary>
        /// Sum of all meme impact values, clamped to the game's combined cap.
        /// Mirrors the private IdeoUIUtility.ImpactOf, which is used by the same
        /// IdeoImpactUtility.OverallImpactLabel that vanilla shows next to the
        /// continue button. Kept local so we stay independent of vanilla's private API.
        /// </summary>
        public static int ImpactOf(IEnumerable<MemeDef> memes)
        {
            if (memes == null) return 0;
            int total = 0;
            foreach (var m in memes)
                total += m.impact;
            if (total < 0) total = 0;
            if (total > IdeoImpactUtility.MaxCombinedImpact) total = IdeoImpactUtility.MaxCombinedImpact;
            return total;
        }

        #region Validation summary

        /// <summary>
        /// Returns a player-readable validation message for the current ideoligion, or empty if it's valid.
        /// Mirrors the checks in Page_ConfigureIdeo.CanDoNext so the user knows in advance what's blocking them.
        /// </summary>
        public static string BuildValidationSummary(Ideo ideo)
        {
            if (ideo == null)
                return "MessageMustChooseIdeo".Translate();

            if (ideo.name.NullOrEmpty())
                return "MessageIdeoNameCantBeEmpty".Translate();

            var pair = ideo.FirstIncompatiblePreceptPair();
            if (pair != default(Pair<Precept, Precept>))
            {
                return "MessageIdeoIncompatiblePrecepts".Translate(
                    pair.First.Label.Named("PRECEPT1"),
                    pair.Second.Label.Named("PRECEPT2")
                ).CapitalizeFirst();
            }

            var missingRitualTarget = ideo.FirstRitualMissingTarget();
            if (missingRitualTarget != null)
            {
                return "MessageRitualMissingTarget".Translate(missingRitualTarget.Item1.LabelCap.Named("PRECEPT"))
                    + ": " + missingRitualTarget.Item2.ToCommaList().CapitalizeFirst() + ".";
            }

            var missingBuildingRitual = ideo.FirstConsumableBuildingMissingRitual();
            if (missingBuildingRitual != null)
                return "MessageBuildingMissingRitual".Translate(missingBuildingRitual.LabelCap.Named("PRECEPT"));

            return "";
        }

        /// <summary>
        /// Returns a non-blocking player warning for the current ideoligion (e.g. a precept
        /// that won't function as intended), or empty. Mirrors the yellow warning vanilla shows
        /// near the continue button via Ideo.FirstPreceptWithWarning / Precept.GetPlayerWarning.
        /// This does NOT block continuing — it's surfaced alongside the impact readout.
        /// </summary>
        public static string BuildPlayerWarning(Ideo ideo)
        {
            var precept = ideo?.FirstPreceptWithWarning();
            if (precept == null) return "";
            if (!precept.GetPlayerWarning(out var shortText, out var description))
                return "";
            string text = "Warning".Translate() + ": " + (shortText ?? "").CapitalizeFirst();
            if (!string.IsNullOrEmpty(description))
                text += ". " + description;
            return text;
        }

        #endregion

        #region Opening announcement

        /// <summary>
        /// Builds the first-time announcement when the builder hub opens.
        /// </summary>
        public static string BuildOpeningAnnouncement(Ideo ideo)
        {
            var sb = new StringBuilder();
            sb.Append("CustomizeIdeoligion".Translate().ToString());
            if (ideo != null && !ideo.name.NullOrEmpty())
            {
                sb.Append(". ");
                sb.Append(ideo.name);
            }

            // Add the impact line (sighted players see this near the continue button).
            if (ideo?.memes != null)
            {
                var normals = ideo.memes.Where(m => m.category == MemeCategory.Normal).ToList();
                if (normals.Count > 0)
                {
                    int impact = ImpactOf(normals);
                    string impactLabel = IdeoImpactUtility.OverallImpactLabel(impact);
                    sb.Append(". ").Append("IdeoImpact".Translate()).Append(": ").Append(impactLabel);
                }
            }

            return sb.ToString();
        }

        #endregion
    }
}
