using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for a pawn's Needs tab: the sorted list of needs with their
    /// percentages, and per-need detail children (raw values, mood state,
    /// description, origin, and need-type-specific sub-nodes like joy
    /// tolerances and break thresholds).
    /// </summary>
    internal sealed class PawnNeedsAdapter : InspectNodeAdapter
    {
        private static readonly List<Action<InspectionTreeItem, Pawn, Need>> detailExtenders =
            new List<Action<InspectionTreeItem, Pawn, Need>>();

        /// <summary>
        /// Registers a mod-compat detail extender that runs at the end of
        /// <see cref="BuildNeedDetailChildren"/> for EVERY visible need, before the
        /// caller (<see cref="BuildNeedsChildren"/>) folds direct-child labels into
        /// the need's collapsed label — so anything an extender attaches directly to
        /// the need item becomes part of that fold; deeper grandchildren do not.
        /// Public for mod-compat shims (e.g. VaeNeedsCompat) that add need-type-
        /// specific sub-nodes without this adapter knowing about the mod that owns
        /// them.
        /// </summary>
        public static void RegisterDetailExtender(Action<InspectionTreeItem, Pawn, Need> extender)
        {
            detailExtenders.Add(extender);
        }

        public override string CategoryKey => "Needs";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildNeedsChildren(categoryItem, pawn);
        }

        private static void BuildNeedsChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            if (pawn.needs == null)
                return;

            int indent = parentItem.IndentLevel + 1;

            var needs = pawn.needs.AllNeeds;
            if (needs == null || needs.Count == 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoNeedsToDisplay".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            // Filter and order exactly as NeedsCardUtility.UpdateDisplayNeeds /
            // PawnNeedsUIUtility.SortInDisplayOrder do: the virtual ShowOnNeedList
            // property (not the static def flag — Need_Outdoors, Need_Chemical_Any,
            // Need_Indoors, and Need_Authority override it with runtime gates) and
            // def.listPriority descending (a fixed display order, not current level).
            var sortedNeeds = needs
                .Where(n => n.ShowOnNeedList)
                .OrderByDescending(n => n.def.listPriority)
                .ToList();

            foreach (var need in sortedNeeds)
            {
                string needName = need.LabelCap;

                var needItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = $"{needName}: {NeedBarText.Describe(need)}",
                    ExpandedLabel = needName,
                    Data = need,
                    IndentLevel = indent,
                    IsExpandable = true,
                    IsExpanded = false
                };

                BuildNeedDetailChildren(needItem, pawn, need);

                // Aggregate child labels into the collapsed summary (comma/period separated
                // so the screen reader pauses naturally). Newlines only exist in expanded
                // navigation where each line is its own child the user arrows through.
                var summaryParts = needItem.Children
                    .Select(c => c.Label)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                if (summaryParts.Count > 0)
                {
                    needItem.Label += $". {string.Join(". ", summaryParts)}";
                }
                else
                {
                    needItem.IsExpandable = false;
                }

                InspectNodeFactory.Attach(parentItem, needItem);
            }
        }

        /// <summary>
        /// Builds detail children for a need: raw values, mood state, description,
        /// origin (trait/gene/ideo/hediff), and need-type-specific sub-nodes.
        /// Mirrors Need.GetTipString / Need_Joy.GetTipString / Need_Mood.GetTipString.
        /// </summary>
        private static void BuildNeedDetailChildren(InspectionTreeItem needItem, Pawn pawn, Need need)
        {
            if (needItem.Children.Count > 0)
                return;

            int childIndent = needItem.IndentLevel + 1;

            // Food: show raw nutrition values only when they add info beyond the
            // percentage (MaxLevel > 1 for large races like Thrumbos; for humans
            // MaxLevel == 1 so the raw numbers would just duplicate the percentage).
            if (need is Need_Food food && food.MaxLevel > 1.01f)
            {
                InspectNodeFactory.Attach(needItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = $"{food.CurLevel.ToString("0.##")} / {food.MaxLevel.ToString("0.##")}",
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Mood: qualitative state string (Stressed / Content / About to break / ...)
            if (need is Need_Mood mood)
            {
                string moodState = mood.MoodString;
                if (!string.IsNullOrEmpty(moodState))
                {
                    InspectNodeFactory.Attach(needItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = moodState,
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
            }

            // Description (formatted for Learning to inject activity list, then flattened)
            string description = need.def.description;
            if (!string.IsNullOrEmpty(description))
            {
                if (need is Need_Learning)
                {
                    string activitiesLineList = DefDatabase<LearningDesireDef>.AllDefsListForReading
                        .Select(d => d.label)
                        .ToList()
                        .ToLineList("  - ", capitalizeItems: true);
                    description = description
                        .Formatted(activitiesLineList.Named("ACTIVITIES"), pawn.Named("PAWN"))
                        .Resolve();
                }

                string cleanDesc = description.StripTags().Trim();
                cleanDesc = System.Text.RegularExpressions.Regex.Replace(cleanDesc, @"\s+", " ");
                InspectNodeFactory.Attach(needItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = cleanDesc,
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Origin line (Comes from Trait/Gene/Ideo/Hediff) mirrors Need.GetTipString
            string originLine = GetNeedOriginLine(pawn, need);
            if (!string.IsNullOrEmpty(originLine))
            {
                InspectNodeFactory.Attach(needItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = originLine,
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
            }

            // Need-type-specific sub-nodes
            if (need is Need_Joy joy)
            {
                BuildJoyTolerancesSubNode(needItem, joy);
                BuildJoyExpectationChildren(needItem, pawn);
            }
            else if (need is Need_Mood && pawn.mindState?.mentalBreaker != null
                     && pawn.mindState.mentalBreaker.CanDoRandomMentalBreaks)
            {
                BuildMoodBreakThresholdsSubNode(needItem, pawn);
            }
            else if (need is Need_Learning
                     && pawn.learning?.ActiveLearningDesires != null
                     && pawn.learning.ActiveLearningDesires.Count > 0)
            {
                BuildActiveLearningDesiresSubNode(needItem, pawn);
            }

            // A modded need subclass can override GetTipString with body text this
            // structured mirror cannot know about (a def with an empty description
            // and all its prose in the override read as just "Name: 50%"). For any
            // need type from outside the vanilla assembly, read the need's own
            // tooltip — the same text a sighted player hovers — and attach whatever
            // it says beyond the parts already presented above.
            if (need.GetType().Assembly != typeof(Need).Assembly)
            {
                AttachModdedTipRemainder(needItem, need, childIndent);
            }

            foreach (var extender in detailExtenders)
            {
                try
                {
                    extender(needItem, pawn, need);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"PawnNeedsAdapter detail extender failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Attaches lines from a modded need's own GetTipString that none of the
        /// structured children above already carry. Lines are compared after tag
        /// stripping and whitespace collapsing; the "Label: percent" head line and
        /// anything contained in an existing child label are skipped as duplicates.
        /// </summary>
        private static void AttachModdedTipRemainder(InspectionTreeItem needItem, Need need, int childIndent)
        {
            string tip;
            try
            {
                tip = need.GetTipString();
            }
            catch (Exception)
            {
                return; // Tooltip code from another mod; never let it break the tree.
            }
            if (string.IsNullOrWhiteSpace(tip))
                return;

            string labelHead = need.LabelCap.ToString();
            var existing = needItem.Children
                .Select(c => Collapse(c.Label))
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            foreach (string rawLine in tip.StripTags().Split('\n'))
            {
                string line = Collapse(rawLine);
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.StartsWith(labelHead, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (existing.Any(l => l.Contains(line)))
                    continue;

                InspectNodeFactory.Attach(needItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line,
                    IndentLevel = childIndent,
                    IsExpandable = false
                });
                existing.Add(line);
            }
        }

        private static string Collapse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Returns a "Comes from Trait/Gene/Ideo/Hediff: X" line if the need is enabled
        /// by one of those sources, else null. Mirrors Need.GetTipString (Need.cs:137).
        /// </summary>
        private static string GetNeedOriginLine(Pawn pawn, Need need)
        {
            if (pawn.story?.traits != null
                && pawn.story.traits.TryGetNeedEnablingTrait(need.def, out var trait))
            {
                return $"{"ComesFromTrait".Translate()}: {trait.LabelCap}";
            }
            if (pawn.genes != null
                && pawn.genes.TryGetNeedEnablingGene(need.def, out var gene))
            {
                return $"{"ComesFromGene".Translate()}: {gene.LabelCap}";
            }
            if (pawn.Ideo != null && pawn.Ideo.EnablesNeed(need.def))
            {
                return $"{"ComesFromIdeo".Translate()}: {pawn.Ideo.name.CapitalizeFirst()}";
            }
            if (pawn.health != null
                && pawn.health.hediffSet.TryGetNeedEnablingHediff(need.def, out var hediff))
            {
                return $"{"ComesFromHediff".Translate()}: {hediff.LabelCap}";
            }
            return null;
        }

        /// <summary>
        /// Builds a Joy Tolerances sub-node listing each JoyKindDef with its current
        /// tolerance percentage, tagged "(bored)" when the pawn is bored of that kind.
        /// Mirrors JoyToleranceSet.TolerancesString (JoyToleranceSet.cs:65).
        /// </summary>
        private static void BuildJoyTolerancesSubNode(InspectionTreeItem needItem, Need_Joy joy)
        {
            var entries = new List<(string label, float pct, bool bored)>();
            foreach (var kind in DefDatabase<JoyKindDef>.AllDefsListForReading)
            {
                float tol = joy.tolerances[kind];
                if (tol > 0.01f)
                {
                    entries.Add((kind.LabelCap, tol, joy.tolerances.BoredOf(kind)));
                }
            }

            if (entries.Count == 0)
                return;

            int childIndent = needItem.IndentLevel + 1;
            string header = "JoyTolerances".Translate();
            var tolerancesItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = header,
                ExpandedLabel = header,
                IndentLevel = childIndent,
                IsExpandable = true,
                IsExpanded = false
            };

            int grandChildIndent = tolerancesItem.IndentLevel + 1;
            var entryLabels = new List<string>();
            foreach (var (label, pct, bored) in entries)
            {
                string line = $"{label}: {pct.ToStringPercent()}";
                if (bored)
                {
                    line += $" ({"bored".Translate()})";
                }
                entryLabels.Add(line);
                InspectNodeFactory.Attach(tolerancesItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line,
                    IndentLevel = grandChildIndent,
                    IsExpandable = false
                });
            }

            tolerancesItem.Label += $": {string.Join(". ", entryLabels)}";
            InspectNodeFactory.Attach(needItem, tolerancesItem);
        }

        /// <summary>
        /// Adds the current expectation line and (if on a map) the list of joy kinds
        /// available. Mirrors Need_Joy.GetTipString caravan/map branches.
        /// </summary>
        private static void BuildJoyExpectationChildren(InspectionTreeItem needItem, Pawn pawn)
        {
            int childIndent = needItem.IndentLevel + 1;

            if (pawn.MapHeld != null)
            {
                ExpectationDef expectation = ExpectationsUtility.CurrentExpectationFor(pawn);
                if (expectation != null)
                {
                    string expLine = "CurrentExpectationsAndRecreation".Translate(
                        expectation.label,
                        expectation.joyToleranceDropPerDay.ToStringPercent(),
                        expectation.joyKindsNeeded);
                    InspectNodeFactory.DetailLines(needItem, expLine);
                }

                BuildJoyKindsOnMapSubNode(needItem, pawn.MapHeld);
                return;
            }

            Caravan caravan = pawn.GetCaravan();
            if (caravan != null)
            {
                float perHour = caravan.needs.GetCurrentJoyGainPerTick(pawn) * 2500f;
                if (perHour > 0f)
                {
                    string line = "GainingJoyBecauseCaravanNotMoving".Translate()
                        + ": +" + perHour.ToStringPercent()
                        + "/" + "LetterHour".Translate();
                    InspectNodeFactory.Attach(needItem, new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.DetailText,
                        Label = line,
                        IndentLevel = childIndent,
                        IsExpandable = false
                    });
                }
            }
        }

        /// <summary>
        /// Adds a sub-node listing recreation kinds that are available on the pawn's
        /// map. Entries without parentheses are joy kinds that need no object
        /// (social, solitary relaxation, meditation). Entries with parentheses name
        /// the specific thing on the map that enables that kind (e.g. "Dexterity
        /// play (hoopstone ring)"). Joy kinds with no source available on the map
        /// are omitted entirely. Mirrors JoyUtility.JoyKindsOnMapString.
        /// </summary>
        private static void BuildJoyKindsOnMapSubNode(InspectionTreeItem needItem, Verse.Map map)
        {
            string raw = JoyUtility.JoyKindsOnMapString(map);
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var lines = raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new List<string>();
            foreach (var line in lines)
            {
                string trimmed = line.StripTags().Trim();
                // Vanilla prefixes each line with "  - "; strip so the bullet doesn't
                // get read aloud by the screen reader.
                if (trimmed.StartsWith("- "))
                    trimmed = trimmed.Substring(2).Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    cleaned.Add(trimmed);
            }
            if (cleaned.Count == 0)
                return;

            int childIndent = needItem.IndentLevel + 1;
            string header = "RimWorldAccess.Inspection.Tree.JoyKindsOnMap".Translate();
            var kindsItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = header,
                ExpandedLabel = header,
                IndentLevel = childIndent,
                IsExpandable = true,
                IsExpanded = false
            };

            int grandChildIndent = kindsItem.IndentLevel + 1;
            foreach (var line in cleaned)
            {
                InspectNodeFactory.Attach(kindsItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line,
                    IndentLevel = grandChildIndent,
                    IsExpandable = false
                });
            }

            kindsItem.Label += $": {string.Join(". ", cleaned)}";
            InspectNodeFactory.Attach(needItem, kindsItem);
        }

        /// <summary>
        /// Adds a "Break Thresholds" sub-node to the mood need mirroring the existing
        /// Mood category's break-threshold structure (Minor / Major / Extreme).
        /// </summary>
        private static void BuildMoodBreakThresholdsSubNode(InspectionTreeItem needItem, Pawn pawn)
        {
            int childIndent = needItem.IndentLevel + 1;
            string header = "RimWorldAccess.Inspection.Tree.BreakThresholds".Translate();
            var thresholdsItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = header,
                ExpandedLabel = header,
                Data = new InspectSectionDatum(pawn, InspectSectionKind.MoodBreakThresholds),
                IndentLevel = childIndent,
                IsExpandable = true,
                IsExpanded = false
            };

            PawnMoodAdapter.BuildBreakThresholdsChildren(thresholdsItem, pawn);

            var childLabels = thresholdsItem.Children.Select(c => c.Label).ToList();
            if (childLabels.Count > 0)
            {
                thresholdsItem.Label += $": {string.Join(". ", childLabels)}";
            }

            InspectNodeFactory.Attach(needItem, thresholdsItem);
        }

        /// <summary>
        /// Adds an "Active Learning Desires" sub-node listing each desire with its
        /// description (Biotech children).
        /// </summary>
        private static void BuildActiveLearningDesiresSubNode(InspectionTreeItem needItem, Pawn pawn)
        {
            int childIndent = needItem.IndentLevel + 1;
            string header = "RimWorldAccess.Inspection.Tree.ActiveLearningDesires".Translate();
            var desiresItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = header,
                ExpandedLabel = header,
                IndentLevel = childIndent,
                IsExpandable = true,
                IsExpanded = false
            };

            int grandChildIndent = desiresItem.IndentLevel + 1;
            var desireLabels = new List<string>();
            foreach (var desire in pawn.learning.ActiveLearningDesires)
            {
                string desireDesc = desire.description ?? "";
                string line = $"{desire.LabelCap}. {desireDesc}".TrimEnd();
                desireLabels.Add(line);
                InspectNodeFactory.Attach(desiresItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = line,
                    IndentLevel = grandChildIndent,
                    IsExpandable = false
                });
            }

            desiresItem.Label += $": {string.Join(". ", desireLabels)}";
            InspectNodeFactory.Attach(needItem, desiresItem);
        }
    }
}
