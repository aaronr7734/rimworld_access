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
                    info.Add(($"Childhood: {title}", desc));
                }

                if (pawn.story.Adulthood != null)
                {
                    string title = pawn.story.Adulthood.TitleCapFor(pawn.gender);
                    string desc = pawn.story.Adulthood.FullDescriptionFor(pawn).Resolve();
                    info.Add(($"Adulthood: {title}", desc));
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
        /// Gets incapable work types for a pawn.
        /// </summary>
        public static List<string> GetIncapableWorkTypes(Pawn pawn)
        {
            var incapable = new List<string>();

            if (pawn?.story == null)
                return incapable;

            try
            {
                WorkTags disabled = pawn.CombinedDisabledWorkTags;

                foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if ((workType.workTags & disabled) != 0)
                    {
                        incapable.Add(workType.labelShort.CapitalizeFirst());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfoCardDataExtractor] Error getting incapable work: {ex.Message}");
            }

            return incapable.Distinct().ToList();
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
                    if (gene.def.skinColorBase.HasValue && gene.def.label == "skin color")
                    {
                        Color color = gene.def.skinColorBase.Value;
                        float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
                        string shade = luminance > 0.85f ? "very light"
                                     : luminance > 0.7f  ? "light"
                                     : luminance > 0.55f ? "fair"
                                     : luminance > 0.45f ? "medium"
                                     : luminance > 0.35f ? "tan"
                                     : luminance > 0.2f  ? "brown"
                                     : "dark brown";
                        geneName = $"Skin color ({shade})";
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
                    string partLabel = hediff.Part?.LabelCap ?? "Whole body";
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
                        if (pawn.royalty.HasPermit(permitDef, faction))
                        {
                            var factionPermit = pawn.royalty.AllFactionPermits
                                .FirstOrDefault(fp => fp.Permit == permitDef && fp.Faction == faction);
                            status = (factionPermit != null && factionPermit.OnCooldown)
                                ? "Granted (on cooldown)" : "Granted";
                        }
                        else if (permitDef.AvailableForPawn(pawn, faction))
                        {
                            status = $"Available ({permitDef.permitPointCost} points)";
                        }
                        else
                        {
                            if (permitDef.prerequisite != null && !pawn.royalty.HasPermit(permitDef.prerequisite, faction))
                                status = $"Locked (requires {permitDef.prerequisite.LabelCap})";
                            else if (permitDef.minTitle != null)
                                status = $"Locked (requires {permitDef.minTitle.GetLabelFor(pawn).CapitalizeFirst()})";
                            else
                                status = "Locked";
                        }

                        string requiredTitle = permitDef.minTitle?.GetLabelFor(pawn).CapitalizeFirst() ?? "None";
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
    }
}
