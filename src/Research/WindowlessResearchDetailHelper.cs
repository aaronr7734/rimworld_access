using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Stateless research-detail content builders for <see cref="WindowlessResearchDetailState"/>,
    /// which keeps lifecycle, navigation wiring and the research-start path. Every method here is
    /// a pure function of its arguments -- no static field, no mutation, no announcement side
    /// effect -- so it can be called from tests or other menus.
    /// </summary>
    internal static class WindowlessResearchDetailHelper
    {
        /// <summary>The masked label vanilla shows for a hidden Anomaly project (ListProjects, MainTabWindow_Research.cs:915-918).</summary>
        internal static string HiddenResearchLabel() => string.Format("({0})", "UnknownResearch".Translate());

        internal static InspectionTreeItem BuildDetailTree(ResearchProjectDef project)
        {
            var root = new InspectionTreeItem
            {
                Label = project.LabelCap,
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };

            string descContent = BuildDescriptionContent(project);
            var descNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = "RimWorldAccess.Research.Detail.DescriptionLabel".Translate(),
                Description = descContent,
                Data = DetailNodeType.Info,
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root
            };
            root.Children.Add(descNode);

            var prereqNode = BuildPrerequisitesNode(project, root);
            if (prereqNode != null)
                root.Children.Add(prereqNode);

            var unlocksNode = BuildUnlocksNode(project, root);
            if (unlocksNode != null)
                root.Children.Add(unlocksNode);

            var dependentsNode = BuildDependentsNode(project, root);
            if (dependentsNode != null)
                root.Children.Add(dependentsNode);

            // Content source draws last in vanilla's scroll view, right before the Start/Stop
            // button (DrawContentSource, MainTabWindow_Research.cs:500).
            var contentSourceNode = BuildContentSourceNode(project, root);
            if (contentSourceNode != null)
                root.Children.Add(contentSourceNode);

            // Label/hint mirror DrawStartButton's four button states
            // (MainTabWindow_Research.cs:506-590) so a Locked project is announced as locked, with
            // every unmet requirement, instead of a Start button that would fail silently later.
            bool isCurrent = Find.ResearchManager.IsCurrentProject(project);
            string actionLabel;
            string actionHint;
            if (isCurrent)
            {
                actionLabel = "RimWorldAccess.Research.Detail.StopResearch".Translate().ToString();
                actionHint = "RimWorldAccess.Research.Detail.StopResearchHint".Translate().ToString();
            }
            else if (project.IsFinished)
            {
                actionLabel = "RimWorldAccess.Research.Status.CompletedWord".Translate().ToString();
                actionHint = null;
            }
            else if (project.CanStartNow)
            {
                actionLabel = "RimWorldAccess.Research.Detail.StartResearch".Translate().ToString();
                actionHint = "RimWorldAccess.Research.Detail.StartResearchHint".Translate().ToString();
            }
            else
            {
                actionLabel = "RimWorldAccess.Research.Status.LockedWord".Translate().ToString();
                actionHint = BuildLockedReasons(project);
            }
            var actionNode = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = actionLabel,
                Description = actionHint,
                Data = DetailNodeType.Action,
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root
            };
            root.Children.Add(actionNode);

            var debugNodes = BuildDebugActionNodes(project, root);
            foreach (var debugNode in debugNodes)
                root.Children.Add(debugNode);

            return root;
        }

        /// <summary>
        /// Vanilla's two dev-mode buttons (DrawProjectInfo, MainTabWindow_Research.cs:421-440).
        /// Labels are vanilla's own untranslated dev-tool text, deliberately raw literals.
        /// </summary>
        private static List<InspectionTreeItem> BuildDebugActionNodes(
            ResearchProjectDef project, InspectionTreeItem root)
        {
            var nodes = new List<InspectionTreeItem>();
            if (!Prefs.DevMode)
                return nodes;

            if (!Find.ResearchManager.IsCurrentProject(project) && !project.IsFinished)
            {
                nodes.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "Debug: Finish now", // l10n-exempt: verbatim vanilla dev-mode literal (MainTabWindow_Research.cs:424), untranslated there too
                    Data = DetailNodeType.DebugFinish,
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root
                });
            }

            if (!project.TechprintRequirementMet)
            {
                nodes.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "Debug: Apply techprint", // l10n-exempt: verbatim vanilla dev-mode literal (MainTabWindow_Research.cs:434), untranslated there too
                    Data = DetailNodeType.DebugApplyTechprint,
                    IndentLevel = 0,
                    IsExpandable = false,
                    Parent = root
                });
            }
            return nodes;
        }

        /// <summary>
        /// Read-only row mirroring vanilla's DrawContentSource (MainTabWindow_Research.cs:1315-1329):
        /// omitted for a Core-authored project, otherwise the mod's name plus, when the mod is a DLC
        /// expansion (vanilla shows an icon we cannot render), the expansion's own name as text.
        /// </summary>
        private static InspectionTreeItem BuildContentSourceNode(ResearchProjectDef project, InspectionTreeItem root)
        {
            if (project.modContentPack == null || project.modContentPack.IsCoreMod)
                return null;

            string modName = project.modContentPack.Name;
            var expansionDef = ModLister.AllExpansions.Find(e => e.linkedMod == project.modContentPack.PackageId);

            string description = expansionDef != null
                ? (string)"RimWorldAccess.Research.Detail.ContentSourceWithExpansion".Translate(modName, expansionDef.LabelCap)
                : modName;

            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.DetailText,
                Label = "Stat_Source_Label".Translate(),
                Description = description,
                Data = DetailNodeType.Info,
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root
            };
        }

        private static InspectionTreeItem BuildPrerequisitesNode(ResearchProjectDef project, InspectionTreeItem root)
        {
            var children = new List<InspectionTreeItem>();

            if (project.prerequisites != null && project.prerequisites.Count > 0)
            {
                foreach (var prereq in project.prerequisites.OrderBy(p => p.LabelCap.ToString()))
                {
                    // A not-yet-discovered Anomaly project can appear as a plain (non-hidden-list)
                    // prerequisite — mask it as the menu tree and ListProjects do, rather than
                    // leaking its real name/cost/status.
                    string rowLabel = prereq.IsHidden
                        ? HiddenResearchLabel()
                        : "RimWorldAccess.Research.Detail.ResearchRow".Translate(
                            prereq.LabelCap, prereq.CostApparent.ToString("F0"),
                            prereq.IsFinished
                                ? "RimWorldAccess.Research.Status.CompletedWord".Translate().ToString()
                                : "RimWorldAccess.Research.Status.LockedWord".Translate().ToString()).ToString();
                    children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = rowLabel,
                        Data = DetailNodeType.ResearchItem,
                        LinkedDef = prereq,
                        IndentLevel = 1,
                        IsExpandable = false
                    });
                }
            }

            // Hidden prerequisites - just show count without revealing what they are
            if (project.hiddenPrerequisites != null && project.hiddenPrerequisites.Count > 0)
            {
                int missingHiddenCount = project.hiddenPrerequisites.Count(p => !p.IsFinished);
                int totalHiddenCount = project.hiddenPrerequisites.Count;

                string hiddenStatus = missingHiddenCount == 0
                    ? "RimWorldAccess.Research.Detail.HiddenAllCompleted".Translate().ToString()
                    : "RimWorldAccess.Research.Detail.HiddenIncompleteCount".Translate(missingHiddenCount).ToString();
                string hiddenLabel = totalHiddenCount == 1
                    ? "RimWorldAccess.Research.Detail.HiddenOne".Translate(hiddenStatus).ToString()
                    : "RimWorldAccess.Research.Detail.HiddenMany".Translate(totalHiddenCount, hiddenStatus).ToString();

                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = hiddenLabel,
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Availability here means the physical bench is BUILT on some colony map, and must NOT
            // be conflated with the facility requirement below: PlayerHasAnyAppropriateResearchBench
            // returns false when a required facility is missing, so a present bench would report
            // "Not available".
            if (project.requiredResearchBuilding != null)
            {
                bool benchPresent = Find.Maps.Any(map =>
                    map.listerBuildings.allBuildingsColonist.Find(
                        b => b.def == project.requiredResearchBuilding) != null);
                string benchStatus = benchPresent
                    ? "RimWorldAccess.Research.Detail.BenchAvailable".Translate().ToString()
                    : "RimWorldAccess.Research.Detail.BenchNotAvailable".Translate().ToString();
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Research.Detail.RequiresBench".Translate(
                        project.requiredResearchBuilding.LabelCap, benchStatus),
                    Data = DetailNodeType.UnlockedItem,
                    LinkedDef = project.requiredResearchBuilding,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // Mirror vanilla: pick the best-matching built bench and report each facility relative
            // to it - active, present-but-inactive, or absent.
            if (project.requiredResearchFacilities != null && project.requiredResearchFacilities.Count > 0)
            {
                CompAffectedByFacilities bestBenchComp = FindBenchFulfillingMostRequirements(
                    project.requiredResearchBuilding, project.requiredResearchFacilities)
                    ?.TryGetComp<CompAffectedByFacilities>();

                foreach (var facility in project.requiredResearchFacilities)
                {
                    Thing present = null;
                    Thing active = null;
                    if (bestBenchComp != null)
                    {
                        var linked = bestBenchComp.LinkedFacilitiesListForReading;
                        present = linked.Find(x => x.def == facility);
                        active = linked.Find(x => x.def == facility && bestBenchComp.IsFacilityActive(x));
                    }

                    string facilityStatus;
                    if (active != null)
                        facilityStatus = "RimWorldAccess.Research.Detail.BenchAvailable".Translate().ToString();
                    else if (present != null)
                        facilityStatus = "InactiveFacility".Translate().ToString();
                    else
                        facilityStatus = "RimWorldAccess.Research.Detail.BenchNotAvailable".Translate().ToString();

                    children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.Research.Detail.RequiresFacility".Translate(
                            facility.LabelCap, facilityStatus),
                        Data = DetailNodeType.UnlockedItem,
                        LinkedDef = facility,
                        IndentLevel = 1,
                        IsExpandable = false
                    });
                }
            }

            if (project.TechprintCount > 0)
            {
                int applied = project.TechprintsApplied;
                int required = project.TechprintCount;
                string techprintStatus = applied >= required
                    ? "RimWorldAccess.Research.Detail.TechprintsComplete".Translate().ToString()
                    : "RimWorldAccess.Research.Detail.TechprintsProgress".Translate(applied, required).ToString();
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Research.Detail.RequiresTechprints".Translate(techprintStatus),
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });

                // The techprint item itself (GetTip, Verse/ResearchProjectDef.cs:471); the row above
                // states only the applied/required count.
                ThingDef techprint = project.Techprint;
                if (techprint != null)
                {
                    children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RequiredTechprintTip".Translate() + ": " + techprint.LabelCap,
                        Data = DetailNodeType.UnlockedItem,
                        LinkedDef = techprint,
                        IndentLevel = 1,
                        IsExpandable = false
                    });
                }

                // Factions that sell or quest-reward this techprint (DrawTechprintInfo,
                // MainTabWindow_Research.cs:1410-1449), under vanilla's own header key.
                if (project.heldByFactionCategoryTags != null)
                {
                    var holderNames = new List<string>();
                    foreach (var categoryTag in project.heldByFactionCategoryTags)
                    {
                        foreach (var faction in Find.FactionManager.AllFactionsInViewOrder)
                        {
                            if (faction.def.categoryTag == categoryTag)
                                holderNames.Add(faction.Name);
                        }
                    }
                    if (holderNames.Count > 0)
                    {
                        children.Add(new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.DetailText,
                            Label = "ResearchTechprintsFromFactions".Translate() + " " + string.Join(", ", holderNames),
                            Data = DetailNodeType.Info,
                            IndentLevel = 1,
                            IsExpandable = false
                        });
                    }
                }
            }

            if (project.requiresMechanitor)
            {
                string mechStatus = project.PlayerMechanitorRequirementMet
                    ? "RimWorldAccess.Research.Detail.MechanitorMet".Translate().ToString()
                    : "RimWorldAccess.Research.Detail.MechanitorNotMet".Translate().ToString();
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Research.Detail.RequiresMechanitor".Translate(mechStatus),
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            if (project.requiredAnalyzed != null && project.requiredAnalyzed.Count > 0)
            {
                int completed = project.AnalyzedThingsCompleted;
                int required = project.RequiredAnalyzedThingCount;
                string analyzeStatus = completed >= required
                    ? "RimWorldAccess.Research.Detail.AnalyzeComplete".Translate().ToString()
                    : "RimWorldAccess.Research.Detail.AnalyzeProgress".Translate(completed, required).ToString();
                string thingNames = string.Join(", ", project.requiredAnalyzed.Select(t => t.LabelCap.ToString()));
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Research.Detail.RequiresAnalyzing".Translate(thingNames, analyzeStatus),
                    Data = DetailNodeType.Info,
                    IndentLevel = 1,
                    IsExpandable = false
                });

                // One navigable row per studied thing beside the aggregate row above, mirroring
                // AnalyzedThingsCompleted's per-thing check (Verse/ResearchProjectDef.cs:256-275).
                foreach (ThingDef analyzed in project.requiredAnalyzed)
                {
                    int analysisId = analyzed.GetCompProperties<CompProperties_CompAnalyzableUnlockResearch>()?.analysisID ?? -1;
                    Find.AnalysisManager.TryGetAnalysisProgress(analysisId, out var details);
                    bool studied = details != null && details.Satisfied;
                    children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = "RimWorldAccess.Research.Detail.AnalyzedThingRow".Translate(
                            analyzed.LabelCap,
                            (studied
                                ? "RimWorldAccess.Research.Detail.StudiedYes"
                                : "RimWorldAccess.Research.Detail.StudiedNo").Translate()).ToString(),
                        Data = DetailNodeType.UnlockedItem,
                        LinkedDef = analyzed,
                        IndentLevel = 1,
                        IsExpandable = false
                    });
                }
            }

            if (ModsConfig.OdysseyActive && project.requireGravEngineInspected)
            {
                string inspectStatus = project.InspectionRequirementsMet
                    ? "RimWorldAccess.Research.Detail.GravEngineInspected".Translate().ToString()
                    : "RimWorldAccess.Research.Detail.GravEngineNotInspected".Translate().ToString();
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "RimWorldAccess.Research.Detail.RequiresGravEngine".Translate(inspectStatus),
                    Data = DetailNodeType.UnlockedItem,
                    LinkedDef = ThingDefOf.GravEngine,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            // The node appears even with no prerequisites, carrying a message instead of children.
            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = children.Count > 0
                    ? "RimWorldAccess.Research.Detail.PrerequisitesWithCount".Translate(children.Count).ToString()
                    : "RimWorldAccess.Research.Detail.PrerequisitesNone".Translate().ToString(),
                Data = DetailNodeType.Category,
                IndentLevel = 0,
                IsExpandable = children.Count > 0,
                Parent = root
            };

            foreach (var child in children)
            {
                child.Parent = node;
                node.Children.Add(child);
            }

            return node;
        }

        /// <summary>
        /// The unlocks node. Sourced from ResearchProjectDef.UnlockedDefs
        /// (Verse/ResearchProjectDef.cs:301-329) and grouped as DrawUnlockableHyperlinks does
        /// (MainTabWindow_Research.cs:1245-1292) via
        /// ResearchPrerequisitesUtility.UnlockedDefsGroupedByPrerequisites. Do not swap in a
        /// DefDatabase scan: it misses terrain, sown plants, surgeries and psychic rituals, and
        /// mis-sorts recipe unlocks vanilla resolves to their product thing.
        /// </summary>
        private static InspectionTreeItem BuildUnlocksNode(ResearchProjectDef project, InspectionTreeItem root)
        {
            var children = new List<InspectionTreeItem>();

            var grouped = ResearchPrerequisitesUtility.UnlockedDefsGroupedByPrerequisites(project);
            foreach (var group in grouped)
            {
                var header = group.First;
                var defs = group.Second;

                if (!header.unlockedBy.Any())
                {
                    // Vanilla's plain "Unlocks:" group - no other prerequisite required.
                    foreach (var def in defs)
                        children.Add(CreateUnlockedItemNode(def));
                    continue;
                }

                // Vanilla's "Unlocked with X:" sub-group - these defs need every project in
                // header.unlockedBy researched too (HeaderLabel, MainTabWindow_Research.cs:1392-1408).
                // Nested and expandable rather than a plain label, so the items stay navigable.
                string headerNames = string.Join(", ", header.unlockedBy.Select(p =>
                    p.IsHidden
                        ? HiddenResearchLabel()
                        : "RimWorldAccess.Research.Detail.UnlockedWithProjectStatus".Translate(
                            p.LabelCap,
                            p.IsFinished
                                ? "RimWorldAccess.Research.Status.CompletedWord".Translate().ToString()
                                : "RimWorldAccess.Research.Status.LockedWord".Translate().ToString()).ToString()));

                var headerChildren = defs.Select(CreateUnlockedItemNode).ToList();
                var headerNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = "RimWorldAccess.Research.Detail.UnlockedWithHeader".Translate(headerNames, headerChildren.Count).ToString(),
                    Data = DetailNodeType.Category,
                    IndentLevel = 1,
                    IsExpandable = headerChildren.Count > 0
                };
                foreach (var child in headerChildren)
                {
                    child.Parent = headerNode;
                    child.IndentLevel = 2;
                    headerNode.Children.Add(child);
                }
                children.Add(headerNode);
            }

            // Vanilla's free-text unlock entries under the same "Unlocks:" heading
            // (DrawCustomUnlockables, MainTabWindow_Research.cs:1294-1313). Already-resolved
            // [MustTranslate] strings from the def itself - spoken verbatim.
            if (project.customUnlockTexts != null)
            {
                foreach (var text in project.customUnlockTexts)
                {
                    children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = text,
                        Data = DetailNodeType.Info,
                        IndentLevel = 1,
                        IsExpandable = false
                    });
                }
            }

            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = children.Count > 0
                    ? "RimWorldAccess.Research.Detail.UnlocksWithCount".Translate(children.Count).ToString()
                    : "RimWorldAccess.Research.Detail.UnlocksNone".Translate().ToString(),
                Data = DetailNodeType.Category,
                IndentLevel = 0,
                IsExpandable = children.Count > 0,
                Parent = root
            };

            foreach (var child in children)
            {
                child.Parent = node;
                node.Children.Add(child);
            }

            return node;
        }

        /// <summary>
        /// An unlocked-item node with its description inline in the label; category and description
        /// come from the def's own type/fields, mirroring vanilla's hyperlink row.
        /// </summary>
        private static InspectionTreeItem CreateUnlockedItemNode(Def def)
        {
            string category = GetUnlockCategoryLabel(def);
            string cleanDesc = CleanDescriptionText(def.description);
            string memeSuffix = BuildUnlockedDefMemeSuffix(def);

            string fullLabel = $"{def.LabelCap} ({category})";
            if (!string.IsNullOrEmpty(memeSuffix))
            {
                fullLabel += memeSuffix;
            }
            if (!string.IsNullOrEmpty(cleanDesc))
            {
                fullLabel += $" - {cleanDesc}";
            }

            return new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                Label = fullLabel,
                Data = DetailNodeType.UnlockedItem,
                LinkedDef = def,
                IndentLevel = 1,
                IsExpandable = false
            };
        }

        /// <summary>
        /// Strips markup and collapses whitespace in a def description for inline announcement.
        /// </summary>
        private static string CleanDescriptionText(string description)
        {
            if (string.IsNullOrEmpty(description))
                return "";

            string cleanDesc = description;
            if (cleanDesc.Contains("<"))
            {
                cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, "<[^>]+>", "");
            }
            cleanDesc = cleanDesc.Trim();
            cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ");
            return cleanDesc;
        }

        /// <summary>
        /// The category noun shown after an unlocked def's label. ResearchProjectDef.UnlockedDefs
        /// already resolves crafting recipes to their product ThingDef, so only surgery recipes
        /// ever surface as RecipeDef here (Verse/ResearchProjectDef.cs:301-329).
        /// </summary>
        private static string GetUnlockCategoryLabel(Def def)
        {
            switch (def)
            {
                case ThingDef thingDef:
                    if (thingDef.building != null)
                        return "RimWorldAccess.Research.Detail.UnlockCategoryBuilding".Translate().ToString();
                    if (thingDef.plant != null)
                        return "RimWorldAccess.Research.Detail.UnlockCategoryPlant".Translate().ToString();
                    return "RimWorldAccess.Research.Detail.UnlockCategoryItem".Translate().ToString();
                case TerrainDef _:
                    return "RimWorldAccess.Research.Detail.UnlockCategoryTerrain".Translate().ToString();
                case RecipeDef _:
                    return "RimWorldAccess.Research.Detail.UnlockCategorySurgery".Translate().ToString();
                case PsychicRitualDef _:
                    return "RimWorldAccess.Research.Detail.UnlockCategoryPsychicRitual".Translate().ToString();
                case WorkTypeDef _:
                    // Mods (VFE Tribals) add work types to a project's unlocked defs.
                    return "RimWorldAccess.Research.Detail.UnlockCategoryWorkType".Translate().ToString();
                default:
                    return "RimWorldAccess.Research.Detail.UnlockCategoryItem".Translate().ToString();
            }
        }

        /// <summary>
        /// Mirrors LabelSuffixForUnlocked (MainTabWindow_Research.cs:1332-1376): the parenthetical
        /// meme/culture-style-category note vanilla appends after an unlocked def's hyperlink,
        /// e.g. "(Transhumanist)". Returns null when Ideology isn't active or no meme/culture
        /// gates the def's designator.
        /// </summary>
        private static string BuildUnlockedDefMemeSuffix(Def unlocked)
        {
            if (!ModLister.IdeologyInstalled)
                return null;

            // AllDesignatorBuildables is a List<BuildableDef>; only ThingDef/TerrainDef unlocks
            // (both BuildableDef subclasses) can ever match - a surgery RecipeDef or
            // PsychicRitualDef here just means buildableDef is null and every Contains is false,
            // same as vanilla's own designator-buildable check.
            BuildableDef buildableDef = unlocked as BuildableDef;

            var suffixes = new List<string>();
            foreach (var meme in DefDatabase<MemeDef>.AllDefs)
            {
                if (meme.AllDesignatorBuildables.Contains(buildableDef))
                {
                    suffixes.AddUnique(meme.LabelCap.ToString());
                }
                if (meme.thingStyleCategories.NullOrEmpty())
                    continue;
                foreach (var styleCategory in meme.thingStyleCategories)
                {
                    if (styleCategory.category.AllDesignatorBuildables.Contains(buildableDef))
                    {
                        suffixes.AddUnique(meme.LabelCap.ToString());
                    }
                }
            }
            foreach (var culture in DefDatabase<CultureDef>.AllDefs)
            {
                if (culture.thingStyleCategories.NullOrEmpty())
                    continue;
                foreach (var styleCategory in culture.thingStyleCategories)
                {
                    if (styleCategory.category.AllDesignatorBuildables.Contains(buildableDef))
                    {
                        suffixes.AddUnique(culture.LabelCap.ToString());
                    }
                }
            }

            if (suffixes.Count == 0)
                return null;
            return " (" + string.Join(", ", suffixes) + ")";
        }

        /// <summary>
        /// Builds the dependents node with children.
        /// </summary>
        private static InspectionTreeItem BuildDependentsNode(ResearchProjectDef project, InspectionTreeItem root)
        {
            var children = new List<InspectionTreeItem>();

            var dependents = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.prerequisites != null && p.prerequisites.Contains(project))
                .OrderBy(p => p.LabelCap.ToString())
                .ToList();

            foreach (var dep in dependents)
            {
                // This is a whole-database reverse lookup (every project that lists us as a
                // prerequisite), so it readily surfaces not-yet-discovered Anomaly projects that
                // build on this one. Mask exactly like the menu tree does instead of leaking the
                // dependent's real name/cost/status (ResearchProjectDef.IsHidden).
                string rowLabel = dep.IsHidden
                    ? HiddenResearchLabel()
                    : "RimWorldAccess.Research.Detail.ResearchRow".Translate(
                        dep.LabelCap, dep.CostApparent.ToString("F0"),
                        dep.IsFinished
                            ? "RimWorldAccess.Research.Status.CompletedWord".Translate().ToString()
                            : dep.CanStartNow
                                ? "RimWorldAccess.Research.Status.AvailableWord".Translate().ToString()
                                : "RimWorldAccess.Research.Status.LockedWord".Translate().ToString()).ToString();
                children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = rowLabel,
                    Data = DetailNodeType.ResearchItem,
                    LinkedDef = dep,
                    IndentLevel = 1,
                    IsExpandable = false
                });
            }

            var node = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Category,
                Label = children.Count > 0
                    ? "RimWorldAccess.Research.Detail.DependentsWithCount".Translate(children.Count).ToString()
                    : "RimWorldAccess.Research.Detail.DependentsNone".Translate().ToString(),
                Data = DetailNodeType.Category,
                IndentLevel = 0,
                IsExpandable = children.Count > 0,
                Parent = root
            };

            foreach (var child in children)
            {
                child.Parent = node;
                node.Children.Add(child);
            }

            return node;
        }

        /// <summary>
        /// Builds the description content.
        /// </summary>
        private static string BuildDescriptionContent(ResearchProjectDef project)
        {
            var sb = new StringBuilder();

            sb.AppendLine("RimWorldAccess.Research.Desc.ProjectLine".Translate(project.LabelCap).ToString());
            sb.AppendLine();

            if (!string.IsNullOrEmpty(project.description))
            {
                sb.AppendLine(project.description);
                sb.AppendLine();
            }

            // Vanilla appends this Anomaly-only help text after the description
            // (DrawProjectPrimaryInfo, MainTabWindow_Research.cs:456-459).
            if (ModsConfig.AnomalyActive && project.knowledgeCategory != null)
            {
                sb.AppendLine("AnomalyResearchDescriptionHelpText".Translate().ToString());
                sb.AppendLine();
            }

            // Tech level gates CostFactor (DrawProjectPrimaryInfo doesn't state it directly,
            // but it appears on the def's own info card) - stated as a measured fact here.
            if (project.techLevel != TechLevel.Undefined)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.TechLevel".Translate(
                    project.techLevel.ToStringHuman()).ToString());
            }

            if (project.CostApparent > 0)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.ResearchCost".Translate(project.CostApparent.ToString("F0")).ToString());
            }
            else if (project.knowledgeCost > 0)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.KnowledgeCost".Translate(project.knowledgeCost.ToString("F0")).ToString());
            }

            // Tech-level cost multiplier, surfaced with vanilla's own keys/wording (identically
            // to DrawProjectScrollView's top-of-scroll warning, MainTabWindow_Research.cs:477-488)
            // when the colony's faction tech level is below the project's.
            if ((int)project.techLevel > (int)Faction.OfPlayer.def.techLevel)
            {
                float costFactor = project.CostFactor(Faction.OfPlayer.def.techLevel);
                string techLevelLine = "TechLevelTooLow".Translate(
                    Faction.OfPlayer.def.techLevel.ToStringHuman(),
                    project.techLevel.ToStringHuman(),
                    (1f / costFactor).ToStringPercent()).ToString();
                if (costFactor != 1f)
                {
                    techLevelLine += " " + "ResearchCostComparison".Translate(
                        project.Cost.ToString("F0"), project.CostApparent.ToString("F0")).ToString();
                }
                sb.AppendLine(techLevelLine);
            }

            if (ModsConfig.AnomalyActive && project.knowledgeCategory != null)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.KnowledgeCategory".Translate(project.knowledgeCategory.LabelCap).ToString());
            }

            // Unconditional progress line - unlike the status cascade below, this also
            // covers a project that was started and then stopped, matching the tile's
            // own partial-progress fill (ListProjects, MainTabWindow_Research.cs:997).
            if (project.ProgressReal > 0f && !project.IsFinished)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.ProgressDetail".Translate(
                    project.ProgressApparentString,
                    project.CostApparent.ToString("F0"),
                    project.ProgressPercent.ToStringPercent()).ToString());
            }

            if (Find.ResearchManager.IsCurrentProject(project))
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.StatusInProgress".Translate().ToString());
            }
            else if (project.IsFinished)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.StatusCompleted".Translate().ToString());
            }
            else if (project.CanStartNow)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.StatusAvailable".Translate().ToString());
            }
            else
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.StatusLocked".Translate().ToString());
            }

            if (project.requiredResearchBuilding != null)
            {
                sb.AppendLine("RimWorldAccess.Research.Desc.RequiredBench".Translate(project.requiredResearchBuilding.LabelCap).ToString());
            }

            if (project.requiredResearchFacilities != null && project.requiredResearchFacilities.Count > 0)
            {
                sb.Append("RimWorldAccess.Research.Desc.RequiredFacilitiesPrefix".Translate().ToString());
                sb.AppendLine(string.Join(", ", project.requiredResearchFacilities.Select(f => f.LabelCap.ToString())));
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds a combined, spoken list of every unmet requirement for starting research,
        /// mirroring vanilla's Locked-button tooltip (DrawStartButton, MainTabWindow_Research.cs:
        /// 524-570) via the game's own translation keys.
        /// </summary>
        internal static string BuildLockedReasons(ResearchProjectDef project)
        {
            if (project == null)
                return "RimWorldAccess.Research.MissingPrereqs.Unknown".Translate();

            var reasons = new List<string>();

            if (!project.PrerequisitesCompleted)
                reasons.Add(GetMissingPrerequisites(project));

            if (!project.TechprintRequirementMet)
            {
                reasons.Add("InsufficientTechprintsApplied".Translate(
                    project.TechprintsApplied, project.TechprintCount).ToString());
            }

            // Vanilla hides this reason while the Anomaly tab is active (DrawStartButton,
            // MainTabWindow_Research.cs:546); the project's own tab stands in for curTab here.
            if ((!ModsConfig.AnomalyActive || project.tab != ResearchTabDefOf.Anomaly)
                && !project.PlayerHasAnyAppropriateResearchBench)
            {
                reasons.Add("MissingRequiredResearchFacilities".Translate().ToString());
            }

            if (!project.PlayerMechanitorRequirementMet)
                reasons.Add("MissingRequiredMechanitor".Translate().ToString());

            if (!project.InspectionRequirementsMet)
                reasons.Add("MissingGravEngineInspection".Translate().ToString());

            if (!project.AnalyzedThingsRequirementsMet && project.requiredAnalyzed != null)
            {
                foreach (var analyzed in project.requiredAnalyzed)
                    reasons.Add("NotStudied".Translate(analyzed.LabelCap).ToString());
            }

            ResearchLockedReasonProviders.AppendTo(project, reasons);

            return reasons.Count > 0
                ? string.Join(". ", reasons)
                : "RimWorldAccess.Research.MissingPrereqs.Unknown".Translate().ToString();
        }

        /// <summary>
        /// Gets a formatted list of missing prerequisites.
        /// </summary>
        private static string GetMissingPrerequisites(ResearchProjectDef project)
        {
            if (project == null)
                return "RimWorldAccess.Research.MissingPrereqs.Unknown".Translate();

            var parts = new List<string>();

            // Visible prerequisites
            if (project.prerequisites != null)
            {
                var missing = project.prerequisites
                    .Where(p => !p.IsFinished)
                    .Select(p => p.LabelCap.ToString());
                parts.AddRange(missing);
            }

            // Hidden prerequisites - just mention they exist
            if (project.hiddenPrerequisites != null)
            {
                int missingHiddenCount = project.hiddenPrerequisites.Count(p => !p.IsFinished);
                if (missingHiddenCount > 0)
                {
                    string hiddenText = missingHiddenCount == 1
                        ? "RimWorldAccess.Research.MissingPrereqs.HiddenOne".Translate().ToString()
                        : "RimWorldAccess.Research.MissingPrereqs.HiddenMany".Translate(missingHiddenCount).ToString();
                    parts.Add(hiddenText);
                }
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "RimWorldAccess.Research.MissingPrereqs.Unknown".Translate().ToString();
        }

        /// <summary>
        /// Finds the built colony research bench that best fulfils the project's facility
        /// requirements, mirroring vanilla MainTabWindow_Research.FindBenchFulfillingMostRequirements.
        /// Facility availability is then reported relative to this single bench.
        /// </summary>
        private static Building_ResearchBench FindBenchFulfillingMostRequirements(
            ThingDef requiredBench, List<ThingDef> requiredFacilities)
        {
            Building_ResearchBench best = null;
            float bestScore = 0f;
            foreach (Map map in Find.Maps)
            {
                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_ResearchBench bench &&
                        (requiredBench == null || bench.def == requiredBench))
                    {
                        float score = GetResearchBenchRequirementsScore(bench, requiredFacilities);
                        if (best == null || score > bestScore)
                        {
                            bestScore = score;
                            best = bench;
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Scores a bench by how many required facilities it has linked - 1 point for an active
        /// facility, 0.6 for one present but inactive. Mirrors vanilla's scoring exactly.
        /// </summary>
        private static float GetResearchBenchRequirementsScore(
            Building_ResearchBench bench, List<ThingDef> requiredFacilities)
        {
            float num = 0f;
            CompAffectedByFacilities comp = bench.GetComp<CompAffectedByFacilities>();
            if (comp == null)
                return 0f;

            var linked = comp.LinkedFacilitiesListForReading;
            for (int i = 0; i < requiredFacilities.Count; i++)
            {
                ThingDef facility = requiredFacilities[i];
                if (linked.Find(x => x.def == facility && comp.IsFacilityActive(x)) != null)
                    num += 1f;
                else if (linked.Find(x => x.def == facility) != null)
                    num += 0.6f;
            }
            return num;
        }
    }
}
