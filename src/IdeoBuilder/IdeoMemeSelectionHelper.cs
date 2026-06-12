using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helpers for the meme picker: reflection accessors into Dialog_ChooseMemes private state,
    /// tree building (impact tier -> memes for Normal; a single flat list for Structure), and
    /// label formatting that includes selection state, impact, description, and unlocked
    /// roles/rituals.
    ///
    /// Note on MemeGroupDef: the game's meme groups carry no label (only layout offsets such as
    /// drawOffset / maxRows used to arrange boxes on screen), so they convey nothing to a screen
    /// reader. We therefore ignore them for navigation and keep the list flat, ordering by the
    /// same key vanilla sorts on so related memes stay adjacent.
    /// </summary>
    public static class IdeoMemeSelectionHelper
    {
        #region Reflection accessors

        private static readonly System.Reflection.FieldInfo NewMemesField =
            AccessTools.Field(typeof(Dialog_ChooseMemes), "newMemes");
        private static readonly System.Reflection.FieldInfo IdeoField =
            AccessTools.Field(typeof(Dialog_ChooseMemes), "ideo");
        private static readonly System.Reflection.FieldInfo MemeCategoryField =
            AccessTools.Field(typeof(Dialog_ChooseMemes), "memeCategory");
        private static readonly System.Reflection.FieldInfo InitialSelectionField =
            AccessTools.Field(typeof(Dialog_ChooseMemes), "initialSelection");
        private static readonly System.Reflection.FieldInfo ReformingIdeoField =
            AccessTools.Field(typeof(Dialog_ChooseMemes), "reformingIdeo");

        private static readonly System.Reflection.PropertyInfo MemeCountRangeAbsoluteProp =
            AccessTools.Property(typeof(Dialog_ChooseMemes), "MemeCountRangeAbsolute");
        private static readonly System.Reflection.PropertyInfo ConfiguringNewFluidIdeoProp =
            AccessTools.Property(typeof(Dialog_ChooseMemes), "ConfiguringNewFluidIdeo");
        private static readonly System.Reflection.PropertyInfo ReformingFluidIdeoProp =
            AccessTools.Property(typeof(Dialog_ChooseMemes), "ReformingFluidIdeo");
        private static readonly System.Reflection.PropertyInfo NormalMemesRemoveCountProp =
            AccessTools.Property(typeof(Dialog_ChooseMemes), "NormalMemesRemoveCount");

        private static readonly System.Reflection.MethodInfo CanUseMemeMethod =
            AccessTools.Method(typeof(Dialog_ChooseMemes), "CanUseMeme");
        private static readonly System.Reflection.MethodInfo CanRemoveMemeMethod =
            AccessTools.Method(typeof(Dialog_ChooseMemes), "CanRemoveMeme");
        private static readonly System.Reflection.MethodInfo TryAcceptMethod =
            AccessTools.Method(typeof(Dialog_ChooseMemes), "TryAccept");
        private static readonly System.Reflection.MethodInfo GetMemeCountMethod =
            AccessTools.Method(typeof(Dialog_ChooseMemes), "GetMemeCount");
        private static readonly System.Reflection.MethodInfo GetFirstIncompatibleMemePairMethod =
            AccessTools.Method(typeof(Dialog_ChooseMemes), "GetFirstIncompatibleMemePair");

        // Vanilla's full meme tooltip (name, impact, description, required precepts, unlocked
        // roles/rituals, applied styles, prevented precepts, traits, etc.). Private static.
        private static readonly System.Reflection.MethodInfo GetMemeTipMethod =
            AccessTools.Method(typeof(IdeoUIUtility), "GetMemeTip");

        public static List<MemeDef> GetNewMemes(Dialog_ChooseMemes dialog) =>
            (List<MemeDef>)NewMemesField.GetValue(dialog);

        public static Ideo GetIdeo(Dialog_ChooseMemes dialog) =>
            (Ideo)IdeoField.GetValue(dialog);

        public static MemeCategory GetMemeCategory(Dialog_ChooseMemes dialog) =>
            (MemeCategory)MemeCategoryField.GetValue(dialog);

        public static bool GetInitialSelection(Dialog_ChooseMemes dialog) =>
            (bool)InitialSelectionField.GetValue(dialog);

        public static bool GetReformingIdeo(Dialog_ChooseMemes dialog) =>
            (bool)ReformingIdeoField.GetValue(dialog);

        public static IntRange GetMemeCountRangeAbsolute(Dialog_ChooseMemes dialog) =>
            (IntRange)MemeCountRangeAbsoluteProp.GetValue(dialog);

        public static bool GetConfiguringNewFluidIdeo(Dialog_ChooseMemes dialog) =>
            (bool)ConfiguringNewFluidIdeoProp.GetValue(dialog);

        public static bool GetReformingFluidIdeo(Dialog_ChooseMemes dialog) =>
            (bool)ReformingFluidIdeoProp.GetValue(dialog);

        public static int GetNormalMemesRemoveCount(Dialog_ChooseMemes dialog) =>
            (int)NormalMemesRemoveCountProp.GetValue(dialog);

        public static bool CanUseMeme(Dialog_ChooseMemes dialog, MemeDef meme) =>
            (bool)CanUseMemeMethod.Invoke(dialog, new object[] { meme });

        public static AcceptanceReport CanRemoveMeme(Dialog_ChooseMemes dialog, MemeDef meme) =>
            (AcceptanceReport)CanRemoveMemeMethod.Invoke(dialog, new object[] { meme });

        public static int GetMemeCount(Dialog_ChooseMemes dialog, MemeCategory category) =>
            (int)GetMemeCountMethod.Invoke(dialog, new object[] { category });

        public static Pair<MemeDef, MemeDef> GetFirstIncompatibleMemePair(Dialog_ChooseMemes dialog) =>
            (Pair<MemeDef, MemeDef>)GetFirstIncompatibleMemePairMethod.Invoke(dialog, null);

        public static void InvokeTryAccept(Dialog_ChooseMemes dialog) =>
            TryAcceptMethod.Invoke(dialog, null);

        #endregion

        #region Available memes

        public static List<MemeDef> GetAvailableMemes(Dialog_ChooseMemes dialog)
        {
            var category = GetMemeCategory(dialog);
            return DefDatabase<MemeDef>.AllDefsListForReading
                .Where(m => m.category == category && CanUseMeme(dialog, m))
                .ToList();
        }

        #endregion

        #region Tree building

        /// <summary>
        /// Builds the navigation tree for the meme picker.
        ///
        /// For Structure memes: a single flat list (single-select, one shared impact level, and
        /// the meme groups have no labels — see class note).
        ///
        /// For Normal memes: top level is impact tier (Low/Medium/High); each tier holds a flat
        /// list of its memes. We keep the meaningful impact grouping but drop the nameless
        /// MemeGroupDef sub-grouping that vanilla only uses for box layout.
        /// </summary>
        public static InspectionTreeItem BuildTree(Dialog_ChooseMemes dialog)
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category,
            };

            var available = GetAvailableMemes(dialog);
            var category = GetMemeCategory(dialog);

            if (category == MemeCategory.Structure)
            {
                BuildStructureTree(dialog, root, available);
            }
            else
            {
                BuildNormalTree(dialog, root, available);
            }
            return root;
        }

        private static void BuildStructureTree(Dialog_ChooseMemes dialog, InspectionTreeItem root, List<MemeDef> available)
        {
            AddMemesFlat(dialog, root, available, indent: 0);
        }

        private static void BuildNormalTree(Dialog_ChooseMemes dialog, InspectionTreeItem root, List<MemeDef> available)
        {
            // Impact tiers 1..3 (Low / Medium / High); only emit tiers with memes.
            for (int impact = 1; impact <= 3; impact++)
            {
                var inTier = available.Where(m => m.impact == impact).ToList();
                if (inTier.Count == 0) continue;

                var impactNode = new InspectionTreeItem
                {
                    Label = IdeoImpactUtility.MemeImpactLabel(impact).ToString().CapitalizeFirst()
                            + " " + "IdeoImpact".Translate().ToString().ToLower()
                            + ". " + inTier.Count + " " + "Memes".Translate().ToString().ToLower(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.Category,
                    Parent = root,
                };

                AddMemesFlat(dialog, impactNode, inTier, indent: 1);
                root.Children.Add(impactNode);
            }
        }

        /// <summary>
        /// Adds <paramref name="memes"/> as a flat list of leaf nodes under <paramref name="parent"/>.
        /// MemeGroupDef is layout-only (no label), so we don't create group nodes; instead we order
        /// by the same key vanilla sorts on (group render order, then per-meme render order) so memes
        /// that vanilla draws together stay adjacent in the list.
        /// </summary>
        private static void AddMemesFlat(Dialog_ChooseMemes dialog, InspectionTreeItem parent, List<MemeDef> memes, int indent)
        {
            var ordered = memes
                .OrderBy(m => m.groupDef?.renderOrder ?? int.MaxValue)
                .ThenBy(m => m.renderOrder)
                .ToList();
            foreach (var meme in ordered)
                parent.Children.Add(MakeMemeNode(dialog, meme, parent, indent));
        }

        /// <summary>
        /// Each meme is both a checkbox (Space/Enter toggles selection) and an expandable tree
        /// node. Collapsed, it reads its full details inline (name + impact + description + …);
        /// expanded, it reads just the short label (name [+ "Selected"]) and its details become
        /// child nodes, one line apiece, so a screen-reader user can step through them instead of
        /// hearing one wall of text. Same pattern as the info card / read-only ideology tree.
        /// </summary>
        private static InspectionTreeItem MakeMemeNode(Dialog_ChooseMemes dialog, MemeDef meme, InspectionTreeItem parent, int indent)
        {
            var node = new InspectionTreeItem
            {
                IndentLevel = indent,
                IsExpandable = true,
                IsExpanded = false,
                Type = InspectionTreeItem.ItemType.Item,
                Data = meme,
                LinkedDef = meme,
                Parent = parent,
            };
            PopulateMemeNode(dialog, node);
            return node;
        }

        /// <summary>
        /// Builds (first call) or refreshes (later calls) a meme node's labels from current state.
        /// The detail-line children are static (built once); only the short/full labels — which
        /// carry the live "Selected" marker and any cannot-remove reason — are recomputed on a
        /// refresh, so toggling selection never disturbs the expanded child list or the cursor.
        /// </summary>
        public static void PopulateMemeNode(Dialog_ChooseMemes dialog, InspectionTreeItem node)
        {
            if (!(node.Data is MemeDef meme)) return;

            var newMemes = GetNewMemes(dialog);
            bool selected = newMemes != null && newMemes.Contains(meme);

            string shortLabel = BuildMemeShortLabel(dialog, meme);
            node.ExpandedLabel = shortLabel;

            var detailLines = GetMemeTipDetailLines(dialog, meme);

            var sb = new StringBuilder(shortLabel);
            foreach (var line in detailLines)
                sb.Append(". ").Append(line);

            // Cannot-remove reason (e.g. a faction-required meme) only ever applies to a selected
            // meme; surface it on the collapsed label, not as a child line.
            if (selected)
            {
                var report = CanRemoveMeme(dialog, meme);
                if (!report.Accepted && !string.IsNullOrEmpty(report.Reason))
                    sb.Append(". ").Append(report.Reason);
            }
            node.Label = sb.ToString();

            if (node.Children.Count == 0)
            {
                int childIndent = node.IndentLevel + 1;
                foreach (var line in detailLines)
                {
                    node.Children.Add(new InspectionTreeItem
                    {
                        Label = line,
                        IndentLevel = childIndent,
                        IsExpandable = false,
                        Type = InspectionTreeItem.ItemType.Item,
                        Parent = node,
                    });
                }
            }
        }

        #endregion

        #region Meme label

        /// <summary>
        /// The short, speakable label for a meme: its name, plus ", Selected" only when it is
        /// currently selected. We never announce "Not selected" — silence means unselected, which
        /// keeps multi-select lists fast to scan and matches how the user asked to hear it.
        /// </summary>
        public static string BuildMemeShortLabel(Dialog_ChooseMemes dialog, MemeDef meme)
        {
            var newMemes = GetNewMemes(dialog);
            bool selected = newMemes != null && newMemes.Contains(meme);
            string name = meme.LabelCap.ToString();
            return selected ? name + ", " + (string)"RimWorldAccess.Ideology.Builder.Status.Selected".Translate() : name;
        }

        /// <summary>
        /// The meme's detail lines, drawn from vanilla's own tooltip (IdeoUIUtility.GetMemeTip) so
        /// every piece of information a sighted player sees on hover — impact, description, required
        /// precepts, unlocked roles/rituals, applied styles, prevented precepts, agreeable/
        /// disagreeable traits, starting research/buildings — is presented and stays localized.
        /// Rich-text tags are stripped; the leading line (the meme name) is dropped because it is
        /// already the node's short label. Each remaining line becomes one detail node.
        /// </summary>
        public static List<string> GetMemeTipDetailLines(Dialog_ChooseMemes dialog, MemeDef meme)
        {
            var ideo = GetIdeo(dialog);
            string tip = GetMemeTipMethod != null
                ? GetMemeTipMethod.Invoke(null, new object[] { meme, ideo }) as string
                : null;

            if (!string.IsNullOrEmpty(tip))
            {
                var lines = tip.Split('\n')
                    .Select(IdeoBuilderHelper.CleanGameText)
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();
                // Skip the first line (the meme name — already the short label).
                return lines.Skip(1).ToList();
            }

            // Defensive fallback if the reflected tooltip is unavailable: impact + description.
            var fallback = new List<string>
            {
                "IdeoImpact".Translate() + ": " +
                    IdeoImpactUtility.MemeImpactLabel(meme.impact).ToString().CapitalizeFirst()
            };
            if (!string.IsNullOrEmpty(meme.description))
                fallback.Add(meme.description);
            return fallback;
        }

        #endregion

        #region Status / impact

        /// <summary>
        /// Builds the validation / impact status string (mirrors the bottom-right text in
        /// Dialog_ChooseMemes). Returns "" if the current selection is valid and there's no
        /// impact line to show (Structure dialog).
        /// </summary>
        public static string BuildStatusLine(Dialog_ChooseMemes dialog)
        {
            var category = GetMemeCategory(dialog);
            var newMemes = GetNewMemes(dialog);
            var range = GetMemeCountRangeAbsolute(dialog);
            bool configuringNewFluid = GetConfiguringNewFluidIdeo(dialog);

            var incompat = GetFirstIncompatibleMemePair(dialog);
            if (incompat != default(Pair<MemeDef, MemeDef>))
                // Pass the MemeDefs (not their LabelCaps) so the {0_label}/{1_label} placeholders
                // resolve — matching vanilla's Dialog_ChooseMemes call.
                return "IncompatibleMemes".Translate(incompat.First, incompat.Second).CapitalizeFirst();

            int structCount = GetMemeCount(dialog, MemeCategory.Structure);
            if (structCount < 1 && category == MemeCategory.Structure)
                return "ChooseStructureMeme".Translate();

            if (category == MemeCategory.Normal)
            {
                int normalCount = GetMemeCount(dialog, MemeCategory.Normal);
                if (normalCount < range.min)
                {
                    return (string)(configuringNewFluid
                        ? "NotEnoughMemesFluidIdeo".Translate(range.min)
                        : "NotEnoughMemes".Translate(range.min));
                }
                if (normalCount > range.max)
                    return "TooManyMemes".Translate(range.max);

                // No errors: speak the overall impact. Vanilla's IdeoUIUtility.DrawImpactInfo shows
                // BOTH the numeric score and the word, so we present both.
                int impact = IdeoBuilderHelper.ImpactOf(newMemes.Where(m => m.category == MemeCategory.Normal));
                string impactLabel = IdeoImpactUtility.OverallImpactLabel(impact);
                return $"{"IdeoImpact".Translate()}: {impact}, {impactLabel}";
            }

            return "";
        }

        #endregion
    }
}
