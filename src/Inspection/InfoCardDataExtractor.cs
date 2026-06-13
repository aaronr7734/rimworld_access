using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Extracts data from RimWorld's Dialog_InfoCard and related utilities via reflection.
    /// Provides structured data for InfoCardTreeBuilder to consume.
    /// </summary>
    public static class InfoCardDataExtractor
    {
        // Cached reflection fields
        private static FieldInfo cachedDrawEntriesField;
        private static FieldInfo dialogThingField;
        private static FieldInfo dialogTabField;
        private static FieldInfo dialogDefField;
        private static FieldInfo dialogWorldObjectField;
        private static FieldInfo dialogHediffField;
        private static FieldInfo dialogTitleDefField;
        private static FieldInfo dialogFactionField;
        private static FieldInfo dialogStuffField;
        private static MethodInfo getWorkTypeDisableCausesMethod;

        static InfoCardDataExtractor()
        {
            // Cache reflection fields for performance
            cachedDrawEntriesField = typeof(StatsReportUtility).GetField(
                "cachedDrawEntries",
                BindingFlags.NonPublic | BindingFlags.Static
            );

            dialogThingField = typeof(Dialog_InfoCard).GetField(
                "thing",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogTabField = typeof(Dialog_InfoCard).GetField(
                "tab",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogDefField = typeof(Dialog_InfoCard).GetField(
                "def",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogWorldObjectField = typeof(Dialog_InfoCard).GetField(
                "worldObject",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogHediffField = typeof(Dialog_InfoCard).GetField(
                "hediff",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogTitleDefField = typeof(Dialog_InfoCard).GetField(
                "titleDef",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogFactionField = typeof(Dialog_InfoCard).GetField(
                "faction",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            dialogStuffField = typeof(Dialog_InfoCard).GetField(
                "stuff",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            getWorkTypeDisableCausesMethod = typeof(CharacterCardUtility).GetMethod(
                "GetWorkTypeDisableCauses",
                BindingFlags.NonPublic | BindingFlags.Static
            );
        }

        /// <summary>
        /// Gets the stat entries from StatsReportUtility's cached list.
        /// </summary>
        public static List<StatDrawEntry> GetStatEntries()
        {
            try
            {
                if (cachedDrawEntriesField == null)
                {
                    Log.Warning("[InfoCardDataExtractor] cachedDrawEntries field not found");
                    return new List<StatDrawEntry>();
                }

                var entries = cachedDrawEntriesField.GetValue(null) as List<StatDrawEntry>;
                return entries ?? new List<StatDrawEntry>();
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting stat entries: {ex.Message}");
                return new List<StatDrawEntry>();
            }
        }

        /// <summary>
        /// Gets the Thing being displayed in the dialog.
        /// </summary>
        public static Thing GetThing(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogThingField == null)
                    return null;

                return dialogThingField.GetValue(dialog) as Thing;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting thing: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the Pawn being displayed, if the thing is a pawn.
        /// </summary>
        public static Pawn GetPawn(Dialog_InfoCard dialog)
        {
            return GetThing(dialog) as Pawn;
        }

        /// <summary>
        /// Gets the Def being displayed (for def-only info cards).
        /// </summary>
        public static Def GetDef(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogDefField == null)
                    return null;

                return dialogDefField.GetValue(dialog) as Def;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting def: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the WorldObject being displayed in the dialog.
        /// </summary>
        public static WorldObject GetWorldObject(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogWorldObjectField == null)
                    return null;

                return dialogWorldObjectField.GetValue(dialog) as WorldObject;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting worldObject: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the Hediff being displayed in the dialog.
        /// </summary>
        public static Hediff GetHediff(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogHediffField == null)
                    return null;

                return dialogHediffField.GetValue(dialog) as Hediff;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting hediff: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the RoyalTitleDef being displayed in the dialog.
        /// </summary>
        public static RoyalTitleDef GetTitleDef(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogTitleDefField == null)
                    return null;

                return dialogTitleDefField.GetValue(dialog) as RoyalTitleDef;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting titleDef: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the Faction being displayed in the dialog.
        /// </summary>
        public static Faction GetFaction(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogFactionField == null)
                    return null;

                return dialogFactionField.GetValue(dialog) as Faction;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting faction: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the stuff (material) ThingDef being displayed in the dialog.
        /// </summary>
        public static ThingDef GetStuff(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogStuffField == null)
                    return null;

                return dialogStuffField.GetValue(dialog) as ThingDef;
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting stuff: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the current tab from the dialog.
        /// </summary>
        public static Dialog_InfoCard.InfoCardTab GetCurrentTab(Dialog_InfoCard dialog)
        {
            try
            {
                if (dialog == null || dialogTabField == null)
                    return Dialog_InfoCard.InfoCardTab.Stats;

                return (Dialog_InfoCard.InfoCardTab)dialogTabField.GetValue(dialog);
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting tab: {ex.Message}");
                return Dialog_InfoCard.InfoCardTab.Stats;
            }
        }

        /// <summary>
        /// Gets the list of available tabs for a thing.
        /// </summary>
        public static List<Dialog_InfoCard.InfoCardTab> GetAvailableTabs(Dialog_InfoCard dialog)
        {
            var tabs = new List<Dialog_InfoCard.InfoCardTab>();

            // Stats always available
            tabs.Add(Dialog_InfoCard.InfoCardTab.Stats);

            var pawn = GetPawn(dialog);
            if (pawn != null)
            {
                // Character only for humanlike
                if (pawn.RaceProps.Humanlike)
                {
                    tabs.Add(Dialog_InfoCard.InfoCardTab.Character);
                }

                // Health for all pawns
                tabs.Add(Dialog_InfoCard.InfoCardTab.Health);

                // Permits for Royalty DLC + humanlike + player faction
                // Must also check selectedFaction != null (RimWorld bug: crashes if null)
                // And exclude quest lodgers (per RimWorld's own logic)
                if (ModsConfig.RoyaltyActive &&
                    pawn.RaceProps.Humanlike &&
                    pawn.Faction == Faction.OfPlayer &&
                    !pawn.IsQuestLodger() &&
                    pawn.royalty != null &&
                    PermitsCardUtility.selectedFaction != null)
                {
                    tabs.Add(Dialog_InfoCard.InfoCardTab.Permits);
                }

                // Records for all pawns
                tabs.Add(Dialog_InfoCard.InfoCardTab.Records);
            }

            return tabs;
        }

        /// <summary>
        /// Gets backstory information for a pawn.
        /// </summary>
        public static List<(string title, string description)> GetBackstoryInfo(Pawn pawn)
        {
            var info = new List<(string, string)>();

            if (pawn?.story == null)
                return info;

            try
            {
                if (pawn.story.Childhood != null)
                {
                    string title = pawn.story.Childhood.TitleCapFor(pawn.gender);
                    string desc = pawn.story.Childhood.FullDescriptionFor(pawn).Resolve();
                    info.Add(($"{"Childhood".Translate()}: {title}", desc));
                }

                if (pawn.story.Adulthood != null)
                {
                    string title = pawn.story.Adulthood.TitleCapFor(pawn.gender);
                    string desc = pawn.story.Adulthood.FullDescriptionFor(pawn).Resolve();
                    info.Add(($"{"Adulthood".Translate()}: {title}", desc));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting backstory: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// Gets trait information for a pawn.
        /// </summary>
        public static List<(string label, string description, bool suppressed)> GetTraitsInfo(Pawn pawn)
        {
            var traits = new List<(string, string, bool)>();

            if (pawn?.story?.traits == null)
                return traits;

            try
            {
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    string label = trait.LabelCap;
                    string desc = trait.TipString(pawn);
                    bool suppressed = trait.Suppressed;
                    traits.Add((label, desc, suppressed));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting traits: {ex.Message}");
            }

            return traits;
        }

        /// <summary>
        /// Gets skill information for a pawn.
        /// </summary>
        public static List<(SkillDef def, int level, Passion passion, bool disabled, string levelDesc)> GetSkillsInfo(Pawn pawn)
        {
            var skills = new List<(SkillDef, int, Passion, bool, string)>();

            if (pawn?.skills == null)
                return skills;

            try
            {
                foreach (var skillDef in DefDatabase<SkillDef>.AllDefsListForReading)
                {
                    var skill = pawn.skills.GetSkill(skillDef);
                    if (skill != null)
                    {
                        skills.Add((
                            skillDef,
                            skill.Level,
                            skill.passion,
                            skill.TotallyDisabled,
                            skill.LevelDescriptor
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting skills: {ex.Message}");
            }

            return skills;
        }

        /// <summary>
        /// Gets age display lines for a pawn: a summary line (biological age, with the
        /// chronological age in parentheses when they differ) followed by the birth date and
        /// the chronological/biological breakdown. Mirrors the vanilla character card's age
        /// field and its hover tooltip. The debug tail that <c>AgeTooltipString</c> appends
        /// when <c>Prefs.DevMode</c> is enabled is stripped out.
        /// </summary>
        public static List<string> GetAgeInfo(Pawn pawn)
        {
            var lines = new List<string>();
            if (pawn?.ageTracker == null)
                return lines;

            try
            {
                lines.Add("RimWorldAccess.Inspection.Pawn.Age".Translate(pawn.ageTracker.AgeNumberString));

                string tooltip = pawn.ageTracker.AgeTooltipString;
                if (!string.IsNullOrEmpty(tooltip))
                {
                    int devIdx = tooltip.IndexOf("\n\nDev mode info:", StringComparison.Ordinal);
                    if (devIdx >= 0)
                        tooltip = tooltip.Substring(0, devIdx);

                    foreach (var line in tooltip.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = line.StripTags().Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            lines.Add(trimmed);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting age info: {ex.Message}");
            }

            return lines;
        }

        /// <summary>
        /// Gets incapable work tag info for a pawn, organized by WorkTag.
        /// Each entry includes the tag label (with inline causes), and affected work type defs.
        /// Mirrors the vanilla CharacterCardUtility tooltip structure.
        /// </summary>
        public static List<(string tagLabel, List<WorkTypeDef> affectedWorkTypes)> GetIncapableWorkTagsInfo(Pawn pawn)
        {
            var result = new List<(string, List<WorkTypeDef>)>();

            if (pawn?.story == null)
                return result;

            try
            {
                WorkTags disabled = pawn.CombinedDisabledWorkTags;
                if (disabled == WorkTags.None)
                    return result;

                foreach (WorkTags tag in disabled.GetAllSelectedItems<WorkTags>())
                {
                    if (tag == WorkTags.None)
                        continue;

                    string tagLabel = tag.LabelTranslated().CapitalizeFirst();

                    // Build inline cause string
                    string causeStr = GetCauseString(pawn, tag);
                    if (!string.IsNullOrEmpty(causeStr))
                        tagLabel += " (" + causeStr + ")";

                    var affectedWorkTypes = new List<WorkTypeDef>();
                    foreach (WorkTypeDef workTypeDef in DefDatabase<WorkTypeDef>.AllDefs)
                    {
                        if ((workTypeDef.workTags & tag) > WorkTags.None)
                        {
                            affectedWorkTypes.Add(workTypeDef);
                        }
                    }

                    result.Add((tagLabel, affectedWorkTypes));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting incapable work tags: {ex.Message}");
            }

            return result;
        }

        private static string GetCauseString(Pawn pawn, WorkTags tag)
        {
            if (getWorkTypeDisableCausesMethod == null)
                return null;

            try
            {
                var causeObjects = getWorkTypeDisableCausesMethod.Invoke(
                    null, new object[] { pawn, tag }) as List<object>;
                if (causeObjects == null || causeObjects.Count == 0)
                    return null;

                var parts = new List<string>();
                foreach (var cause in causeObjects)
                {
                    string formatted = FormatWorkTagDisableCause(pawn, cause);
                    if (!string.IsNullOrEmpty(formatted))
                        parts.Add(formatted);
                }
                return parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[InfoCardDataExtractor] Error getting disable causes for {tag}: {ex.Message}");
                return null;
            }
        }

        private static string FormatWorkTagDisableCause(Pawn pawn, object cause)
        {
            if (cause is BackstoryDef backstory)
                return "IncapableOfTooltipBackstory".Translate() + ": " + backstory.TitleFor(pawn.gender).CapitalizeFirst();
            if (cause is Trait trait)
                return "IncapableOfTooltipTrait".Translate() + ": " + trait.LabelCap;
            if (cause is Hediff hediff)
                return "IncapableOfTooltipHediff".Translate() + ": " + hediff.LabelCap;
            if (cause is RoyalTitle royalTitle)
                return "IncapableOfTooltipTitle".Translate() + ": " + royalTitle.def.GetLabelFor(pawn);
            if (cause is Quest quest)
                return "IncapableOfTooltipQuest".Translate() + ": " + quest.name;
            if (cause is Precept_Role role)
                return "IncapableOfTooltipRole".Translate() + ": " + role.LabelForPawn(pawn);
            if (cause is Gene gene)
                return "IncapableOfTooltipGene".Translate() + ": " + gene.LabelCap;
            if (cause is MutantDef mutantDef)
                return "IncapableOfTooltipMutant".Translate() + ": " + mutantDef.LabelCap;
            return cause?.ToString() ?? "";
        }

        /// <summary>
        /// Gets royal title information for a pawn.
        /// </summary>
        public static List<(string title, string faction, string description)> GetRoyalTitlesInfo(Pawn pawn)
        {
            var titles = new List<(string, string, string)>();

            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null)
                return titles;

            try
            {
                foreach (var title in pawn.royalty.AllTitlesForReading)
                {
                    string titleLabel = title.def.GetLabelCapFor(pawn);
                    string factionName = title.faction?.Name ?? "Unknown";
                    string desc = title.def.description ?? "";
                    titles.Add((titleLabel, factionName, desc));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting royal titles: {ex.Message}");
            }

            return titles;
        }

        /// <summary>
        /// Gets ideology role information for a pawn.
        /// </summary>
        public static (string roleName, string ideoName, string description)? GetIdeologyRoleInfo(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn?.Ideo == null)
                return null;

            try
            {
                var role = pawn.Ideo.GetRole(pawn);
                if (role != null)
                {
                    string roleName = role.LabelForPawn(pawn);
                    string ideoName = pawn.Ideo.name;
                    string desc = role.def.description ?? "";
                    return (roleName, ideoName, desc);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting ideology role: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets ability information for a pawn.
        /// </summary>
        public static List<(string label, string description)> GetAbilitiesInfo(Pawn pawn)
        {
            var abilities = new List<(string, string)>();

            if (pawn?.abilities == null)
                return abilities;

            try
            {
                foreach (var ability in pawn.abilities.AllAbilitiesForReading)
                {
                    if (ability.def.showOnCharacterCard)
                    {
                        string label = ability.def.LabelCap;
                        string desc = ability.def.description ?? "";
                        abilities.Add((label, desc));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting abilities: {ex.Message}");
            }

            return abilities;
        }

        /// <summary>
        /// Gets xenotype information for a pawn.
        /// </summary>
        public static (string xenotypeName, string description, List<(string name, GeneDef def)> genes)? GetXenotypeInfo(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.genes == null)
                return null;

            try
            {
                string xenotypeName = pawn.genes.XenotypeLabelCap;
                string desc = pawn.genes.XenotypeDescShort ?? "";

                var genes = new List<(string, GeneDef)>();
                foreach (var gene in pawn.genes.GenesListForReading)
                {
                    string geneName = gene.LabelCap;

                    // Melanin skin color genes all share generic "skin color" label.
                    // Synthesize a shade description from the color's luminance.
                    if (gene.def.skinColorBase.HasValue && gene.def.endogeneCategory == EndogeneCategory.Melanin)
                    {
                        Color color = gene.def.skinColorBase.Value;
                        float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
                        string shade = (luminance > 0.85f ? "RimWorldAccess.Inspection.InfoCard.SkinShade.VeryLight"
                                     : luminance > 0.7f  ? "RimWorldAccess.Inspection.InfoCard.SkinShade.Light"
                                     : luminance > 0.55f ? "RimWorldAccess.Inspection.InfoCard.SkinShade.Fair"
                                     : luminance > 0.45f ? "RimWorldAccess.Inspection.InfoCard.SkinShade.Medium"
                                     : luminance > 0.35f ? "RimWorldAccess.Inspection.InfoCard.SkinShade.Tan"
                                     : luminance > 0.2f  ? "RimWorldAccess.Inspection.InfoCard.SkinShade.Brown"
                                     : "RimWorldAccess.Inspection.InfoCard.SkinShade.DarkBrown").Translate();
                        geneName = "RimWorldAccess.Inspection.InfoCard.SkinColorShade".Translate(shade);
                    }

                    genes.Add((geneName, gene.def));
                }

                return (xenotypeName, desc, genes);
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting xenotype: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets health capacity information for a pawn.
        /// </summary>
        public static List<(string label, float efficiency, string tip)> GetCapacitiesInfo(Pawn pawn)
        {
            var capacities = new List<(string, float, string)>();

            if (pawn?.health?.capacities == null)
                return capacities;

            try
            {
                foreach (var capacityDef in DefDatabase<PawnCapacityDef>.AllDefsListForReading
                    .Where(c => c.showOnHumanlikes || !pawn.RaceProps.Humanlike)
                    .OrderBy(c => c.listOrder))
                {
                    if (!PawnCapacityUtility.BodyCanEverDoCapacity(pawn.RaceProps.body, capacityDef))
                        continue;

                    float efficiency = pawn.health.capacities.GetLevel(capacityDef);
                    string label = capacityDef.LabelCap;

                    // Use the game's actual tooltip (shows impactors: hediffs, body parts, genes, etc.)
                    // instead of capacityDef.description which is always empty in vanilla
                    string tip = "";
                    try
                    {
                        string fullTip = HealthCardUtility.GetPawnCapacityTip(pawn, capacityDef);
                        // Strip the first line (capacity name + qualitative assessment - already in our label)
                        int firstNewline = fullTip.IndexOf('\n');
                        if (firstNewline >= 0)
                            tip = fullTip.Substring(firstNewline + 1).TrimStart('\r', '\n');
                    }
                    catch { }

                    capacities.Add((label, efficiency, tip));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting capacities: {ex.Message}");
            }

            return capacities;
        }

        /// <summary>
        /// Gets hediff (health condition) information for a pawn.
        /// </summary>
        public static List<(string label, string partLabel, string severity, string tip)> GetHediffsInfo(Pawn pawn)
        {
            var hediffs = new List<(string, string, string, string)>();

            if (pawn?.health?.hediffSet == null)
                return hediffs;

            try
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs.Where(h => h.Visible))
                {
                    string label = hediff.LabelCap;
                    string partLabel = hediff.Part?.LabelCap ?? "WholeBody".Translate();
                    string severity = hediff.SeverityLabel ?? "";
                    string tip = hediff.GetTooltip(pawn, false);
                    hediffs.Add((label, partLabel, severity, tip));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting hediffs: {ex.Message}");
            }

            return hediffs;
        }

        /// <summary>
        /// Gets time record information for a pawn.
        /// </summary>
        public static List<(string label, string value)> GetTimeRecords(Pawn pawn)
        {
            var records = new List<(string, string)>();

            if (pawn?.records == null)
                return records;

            try
            {
                foreach (var recordDef in DefDatabase<RecordDef>.AllDefsListForReading
                    .Where(r => r.type == RecordType.Time)
                    .OrderBy(r => r.displayOrder))
                {
                    int ticks = pawn.records.GetAsInt(recordDef);
                    if (ticks > 0)
                    {
                        string label = recordDef.LabelCap;
                        string value = ticks.ToStringTicksToPeriod();
                        records.Add((label, value));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting time records: {ex.Message}");
            }

            return records;
        }

        /// <summary>
        /// Gets miscellaneous record information for a pawn.
        /// </summary>
        public static List<(string label, string value)> GetMiscRecords(Pawn pawn)
        {
            var records = new List<(string, string)>();

            if (pawn?.records == null)
                return records;

            try
            {
                foreach (var recordDef in DefDatabase<RecordDef>.AllDefsListForReading
                    .Where(r => r.type == RecordType.Int || r.type == RecordType.Float)
                    .OrderBy(r => r.displayOrder))
                {
                    float value = pawn.records.GetValue(recordDef);
                    if (value > 0.001f)
                    {
                        string label = recordDef.LabelCap;
                        string valueStr = value.ToString("0.##");
                        records.Add((label, valueStr));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting misc records: {ex.Message}");
            }

            return records;
        }

        /// <summary>
        /// Gets permit information for a pawn (Royalty DLC).
        /// </summary>
        public static List<(string permitName, Faction faction, string status, string description, string requiredTitle, RoyalTitlePermitDef def)> GetPermitsInfo(Pawn pawn)
        {
            var permits = new List<(string, Faction, string, string, string, RoyalTitlePermitDef)>();

            if (!ModsConfig.RoyaltyActive || pawn?.royalty == null)
                return permits;

            try
            {
                // Show ALL permits per faction (matching vanilla's PermitsCardUtility)
                foreach (var faction in Find.FactionManager.AllFactionsVisible)
                {
                    if (faction.IsPlayer || faction.def.permanentEnemy || faction.temporary)
                        continue;

                    var factionPermits = DefDatabase<RoyalTitlePermitDef>.AllDefs
                        .Where(d => d.faction == faction.def)
                        .OrderBy(d => d.uiPosition.y).ThenBy(d => d.uiPosition.x);

                    if (!factionPermits.Any())
                        continue;

                    foreach (var permitDef in factionPermits)
                    {
                        string status;
                        bool isUnlocked = IsPermitUnlocked(permitDef, pawn, faction);

                        if (isUnlocked)
                        {
                            if (pawn.royalty.HasPermit(permitDef, faction))
                            {
                                var factionPermit = pawn.royalty.AllFactionPermits
                                    .FirstOrDefault(fp => fp.Permit == permitDef && fp.Faction == faction);
                                status = (string)((factionPermit != null && factionPermit.OnCooldown)
                                    ? "RimWorldAccess.Inspection.Permit.GrantedOnCooldown".Translate()
                                    : "RimWorldAccess.Inspection.Permit.Granted".Translate());
                            }
                            else
                            {
                                // Unlocked via upgrade chain (prerequisite of a held permit)
                                status = "RimWorldAccess.Inspection.Permit.Granted".Translate();
                            }
                        }
                        else if (permitDef.AvailableForPawn(pawn, faction))
                        {
                            status = "RimWorldAccess.Inspection.Permit.AvailableWithPoints".Translate(permitDef.permitPointCost).ToString();
                        }
                        else
                        {
                            if (permitDef.prerequisite != null && !IsPermitUnlocked(permitDef.prerequisite, pawn, faction))
                                status = "RimWorldAccess.Inspection.Permit.LockedWithReason".Translate("UpgradeFrom".Translate(permitDef.prerequisite.LabelCap)).ToString();
                            else if (permitDef.minTitle != null)
                                status = "RimWorldAccess.Inspection.Permit.LockedWithReason".Translate("RequiresTitle".Translate(permitDef.minTitle.GetLabelForBothGenders())).ToString();
                            else
                                status = "RimWorldAccess.Inspection.Permit.Locked".Translate();
                        }

                        string requiredTitle = permitDef.minTitle?.GetLabelFor(pawn).CapitalizeFirst() ?? (string)"None".Translate();
                        permits.Add((permitDef.LabelCap, faction, status,
                            permitDef.description ?? "", requiredTitle, permitDef));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting permits: {ex.Message}");
            }

            return permits;
        }

        /// <summary>
        /// Checks if a permit is "unlocked" for display purposes.
        /// Matches vanilla PermitsCardUtility.PermitUnlocked logic.
        /// A permit is unlocked if directly held OR if another held permit has it as a prerequisite
        /// (meaning the pawn upgraded past it).
        /// </summary>
        public static bool IsPermitUnlocked(RoyalTitlePermitDef permit, Pawn pawn, Faction faction)
        {
            if (pawn.royalty.HasPermit(permit, faction))
                return true;

            var allFactionPermits = pawn.royalty.AllFactionPermits;
            for (int i = 0; i < allFactionPermits.Count; i++)
            {
                if (allFactionPermits[i].Permit.prerequisite == permit && allFactionPermits[i].Faction == faction)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Calculates total favor cost to return all permits.
        /// Matches vanilla PermitsCardUtility.TotalReturnPermitsCost: base cost of 8
        /// plus favor cost of any on-cooldown permits that have royalAid.
        /// </summary>
        public static int TotalReturnPermitsCost(Pawn pawn)
        {
            int cost = 8;
            var allFactionPermits = pawn.royalty.AllFactionPermits;
            for (int i = 0; i < allFactionPermits.Count; i++)
            {
                if (allFactionPermits[i].OnCooldown && allFactionPermits[i].Permit.royalAid != null)
                {
                    cost += allFactionPermits[i].Permit.royalAid.favorCost;
                }
            }
            return cost;
        }
    }
}
