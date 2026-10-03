using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helpers for the meme picker: reflection accessors into Dialog_ChooseMemes private state, the
    /// available-memes filter, the navigation tree (impact tier -> memes for Normal, a single flat
    /// meme list for Structure; hosted by <see cref="RimWorldAccess.Shell.IdeoMemeScreenScope"/>),
    /// and the validation/impact status line.
    ///
    /// The tree carries only what is STABLE for the dialog's life: bare meme names, bare impact-tier
    /// names, and vanilla's own tooltip lines as detail children. Selection markers, child counts and
    /// cannot-remove reasons are composed live in the scope's DescribeTreeNode, which is why a toggle
    /// never rebuilds anything — <c>Dialog_ChooseMemes.CanUseMeme</c> (decompiled :627-660) depends
    /// on defs, dev mode, scenario and factions, never on the current selection, so the row set is
    /// fixed once the dialog opens.
    ///
    /// Note on MemeGroupDef: the game's meme groups carry no label (only layout offsets such as
    /// drawOffset / maxRows used to arrange boxes on screen), so they convey nothing to a screen
    /// reader — the tree orders memes by the same key vanilla sorts on instead, so related memes
    /// stay adjacent.
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
        /// Structure memes: a flat list of meme nodes under the root (single-select, one shared
        /// impact level, and the meme groups have no labels — see the class note), ordered on
        /// <c>DoStructureMemeSelector</c>'s own sort key (decompiled :411) so memes that render
        /// adjacent in the grid stay adjacent here too.
        ///
        /// Normal memes: one node per non-empty impact tier (<c>DoNormalMemeSelector</c>'s outer loop,
        /// decompiled :467, whose 1..3 bound Alpha Memes transpiles to 4), each holding its tier's memes
        /// ordered on <c>NormalMemeSorter</c>'s key (group render order, then meme render order). A
        /// tier node's Label is the bare tier name; the child count rides the shared expansion
        /// suffix the scope appends, so it is spoken through exactly one channel.
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

            List<MemeDef> available = GetAvailableMemes(dialog);

            if (GetMemeCategory(dialog) == MemeCategory.Structure)
            {
                foreach (MemeDef meme in available
                    .OrderBy(m => m.groupDef != null)
                    .ThenBy(m => m.renderOrder))
                {
                    root.Children.Add(MakeMemeNode(dialog, meme, root, indent: 0));
                }
                return root;
            }

            foreach (int impact in available.Select(m => m.impact).Where(i => i >= 1).Distinct().OrderBy(i => i))
            {
                List<MemeDef> inTier = available.Where(m => m.impact == impact).ToList();

                var tierNode = new InspectionTreeItem
                {
                    Label = IdeoImpactUtility.MemeImpactLabel(impact).ToString().CapitalizeFirst()
                            + " " + ((string)"IdeoImpact".Translate()).ToLower(),
                    IndentLevel = 0,
                    IsExpandable = true,
                    IsExpanded = false,
                    Type = InspectionTreeItem.ItemType.Category,
                    // The boxed tier number is the node's identity: TreeStatePreserve matches Data by
                    // ReferenceEquals-or-Equals, so a boxed int survives a rebuild, and the scope's
                    // DescribeTreeNode branches on it to pick the parent grammar.
                    Data = impact,
                    Parent = root,
                };

                foreach (MemeDef meme in inTier
                    .OrderBy(m => m.groupDef?.renderOrder ?? int.MaxValue)
                    .ThenBy(m => m.renderOrder))
                {
                    tierNode.Children.Add(MakeMemeNode(dialog, meme, tierNode, indent: 1));
                }
                root.Children.Add(tierNode);
            }
            return root;
        }

        /// <summary>
        /// One meme node: a selectable row (the scope composes the RadioButton/Checkbox state live)
        /// that is also expandable, its details becoming one child line apiece so a screen-reader
        /// user can step through them instead of hearing one wall of text. The Label stays the bare
        /// name in both states — collapsed, the scope folds the same detail lines in itself.
        /// </summary>
        private static InspectionTreeItem MakeMemeNode(Dialog_ChooseMemes dialog, MemeDef meme, InspectionTreeItem parent, int indent)
        {
            List<string> detailLines = GetMemeTipDetailLines(dialog, meme);
            var node = new InspectionTreeItem
            {
                Label = meme.LabelCap.ToString(),
                IndentLevel = indent,
                IsExpandable = detailLines.Count > 0,
                IsExpanded = false,
                Type = InspectionTreeItem.ItemType.Item,
                Data = meme,
                // No LinkedDef: vanilla opens no Dialog_InfoCard for a MemeDef (see
                // IdeoMemeScreenScope.OnInfo's remarks for the citation).
                Parent = parent,
            };
            foreach (string line in detailLines)
            {
                node.Children.Add(new InspectionTreeItem
                {
                    Label = line,
                    IndentLevel = node.IndentLevel + 1,
                    IsExpandable = false,
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Parent = node,
                });
            }
            return node;
        }

        /// <summary>
        /// The meme's detail lines, drawn from vanilla's own tooltip (IdeoUIUtility.GetMemeTip) so
        /// every piece of information a sighted player sees on hover — impact, description, required
        /// precepts, unlocked roles/rituals, applied styles, prevented precepts, agreeable/
        /// disagreeable traits, starting research/buildings — is presented and stays localized.
        /// Rich-text tags are stripped; the leading line (the meme name) is dropped because it is
        /// already the node's own label. Each remaining line becomes one detail node.
        /// </summary>
        public static List<string> GetMemeTipDetailLines(Dialog_ChooseMemes dialog, MemeDef meme)
        {
            return GetMemeTipDetailLines(GetIdeo(dialog), meme);
        }

        /// <summary>
        /// The same lines for a surface that already holds the ideoligion rather than the
        /// picker dialog (the ideoligion details tree).
        /// </summary>
        public static List<string> GetMemeTipDetailLines(Ideo ideo, MemeDef meme)
        {
            string tip = GetMemeTipMethod != null
                ? GetMemeTipMethod.Invoke(null, new object[] { meme, ideo }) as string
                : null;

            if (!string.IsNullOrEmpty(tip))
            {
                var lines = tip.Split('\n')
                    .Select(IdeoBuilderHelper.CleanGameText)
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();
                // Skip the first line (the meme name — already the node's label).
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
