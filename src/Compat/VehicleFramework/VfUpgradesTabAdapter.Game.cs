using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Vehicle Framework's Vehicles.ITab_Vehicle_Upgrades — the
    /// upgrade-tree tab that lets a player spend resources to unlock nodes on a vehicle's upgrade
    /// grid. The grid's prerequisite lines become the tree: each visible node nests under its
    /// first visible prerequisite (the tab draws no line to a hidden one), nodes with none are
    /// roots, siblings keep the def's author order, and every further prerequisite is spoken as
    /// "also requires". Each node carries its cost, effect and incompatibility detail plus the
    /// one action the drawn node's button would perform. Structure is read straight from
    /// CompUpgradeTree.Props.def.nodes.
    ///
    /// The in-progress operation's "Removal" flag is the one OPTIONAL member: if it fails to
    /// resolve, every in-progress state is presented as a plain upgrade rather than failing
    /// <see cref="Ready"/> outright.
    ///
    /// The tab's mutations are driven by invoking CompUpgradeTree's own
    /// StartUnlock/ClearUpgrade/RemoveUnlock/FinishUnlock/ResetUnlock (vehicle A). Each action
    /// row's OnActivate re-evaluates the comp's own gates
    /// (NodeUnlocked/LastNodeUnlocked/Disabled/PrerequisitesMet) at activation time rather than
    /// trusting the state captured when the row was built, then mirrors
    /// ITab_Vehicle_Upgrades.DrawButtons' branch structure including its reject-message paths.
    ///
    /// DEVIATION: the effects listing (each Upgrade's UpgradeDescription text entries) is
    /// presented even though the installed VF build comments out its own DrawUpgradeList — it is
    /// VF's own data model for these effects, and the accessible equivalent of the before/after
    /// vehicle graphic comparison a blind player cannot see.
    ///
    /// DEVIATION: the incompatibility line is always present, not only while the node is disabled
    /// — it renders the same red-hover disabler cue a sighted player gets by hovering.
    ///
    /// DEVIATION: a node unlocked but not the last-unlocked node in its chain draws a live Upgrade
    /// button in the vanilla tab whose click is a no-op (guarded by "not already unlocked"). This
    /// adapter replaces that dead button with a read-only explanatory line.
    /// </summary>
    internal sealed class VfUpgradesTabAdapter : InspectNodeAdapter
    {
        private readonly Type vehiclePawnType;
        private readonly PropertyInfo compUpgradeTreeProperty;

        private readonly Type compUpgradeTreeType;
        private readonly PropertyInfo propsProperty;
        private readonly PropertyInfo nodeUnlockingProperty;
        private readonly FieldInfo upgradeField;
        private readonly MethodInfo nodeUnlockedMethod;
        private readonly MethodInfo prerequisitesMetMethod;
        private readonly MethodInfo disabledMethod;
        private readonly MethodInfo lastNodeUnlockedMethod;
        private readonly MethodInfo startUnlockMethod;
        private readonly MethodInfo clearUpgradeMethod;
        private readonly MethodInfo removeUnlockMethod;
        private readonly MethodInfo finishUnlockMethod;
        private readonly MethodInfo resetUnlockMethod;

        private readonly FieldInfo removalField;
        private readonly PropertyInfo removalProperty;

        private readonly Type compPropertiesUpgradeTreeType;
        private readonly FieldInfo defField;

        private readonly Type upgradeTreeDefType;
        private readonly FieldInfo nodesField;
        private readonly MethodInfo getNodeMethod;

        private readonly Type upgradeNodeType;
        private readonly FieldInfo keyField;
        private readonly FieldInfo labelField;
        private readonly FieldInfo descriptionField;
        private readonly FieldInfo upgradeExplanationField;
        private readonly FieldInfo hiddenField;
        private readonly FieldInfo ingredientsField;
        private readonly FieldInfo prerequisiteNodesField;
        private readonly FieldInfo disableIfUpgradeNodeEnabledField;
        private readonly FieldInfo disableIfUpgradeNodesEnabledField;
        private readonly FieldInfo upgradesField;

        private readonly Type upgradeType;
        private readonly MethodInfo upgradeDescriptionMethod;

        private readonly Type upgradeTextEntryType;
        private readonly FieldInfo textEntryLabelField;
        private readonly FieldInfo textEntryDescriptionField;

        private readonly bool ready;
        internal InspectTabBase sharedTab;

        public override bool Ready => ready;

        public VfUpgradesTabAdapter()
        {
            var surface = new ReflectionSurface("VfUpgradesTabAdapter");

            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            compUpgradeTreeType = surface.Type("Vehicles.CompUpgradeTree");
            upgradeTreeDefType = surface.Type("Vehicles.UpgradeTreeDef");
            upgradeNodeType = surface.Type("Vehicles.UpgradeNode");
            upgradeType = surface.Type("Vehicles.Upgrade");
            compPropertiesUpgradeTreeType = surface.Type("Vehicles.CompProperties_UpgradeTree");
            upgradeTextEntryType = surface.Type("Vehicles.UpgradeTextEntry");

            compUpgradeTreeProperty = surface.Property(vehiclePawnType, "CompUpgradeTree");

            propsProperty = surface.Property(compUpgradeTreeType, "Props");
            nodeUnlockingProperty = surface.Property(compUpgradeTreeType, "NodeUnlocking");
            upgradeField = surface.Field(compUpgradeTreeType, "upgrade");
            if (upgradeNodeType != null)
            {
                Type[] nodeArg = { upgradeNodeType };
                nodeUnlockedMethod = surface.Method(compUpgradeTreeType, "NodeUnlocked", nodeArg);
                prerequisitesMetMethod = surface.Method(compUpgradeTreeType, "PrerequisitesMet", nodeArg);
                disabledMethod = surface.Method(compUpgradeTreeType, "Disabled", nodeArg);
                lastNodeUnlockedMethod = surface.Method(compUpgradeTreeType, "LastNodeUnlocked", nodeArg);
                startUnlockMethod = surface.Method(compUpgradeTreeType, "StartUnlock", nodeArg);
                removeUnlockMethod = surface.Method(compUpgradeTreeType, "RemoveUnlock", nodeArg);
                finishUnlockMethod = surface.Method(compUpgradeTreeType, "FinishUnlock", nodeArg);
                resetUnlockMethod = surface.Method(compUpgradeTreeType, "ResetUnlock", nodeArg);
            }
            clearUpgradeMethod = surface.Method(compUpgradeTreeType, "ClearUpgrade", Type.EmptyTypes);

            if (upgradeField != null)
            {
                // OPTIONAL: missing it presents every in-progress state as a plain upgrade.
                removalField = AccessTools.Field(upgradeField.FieldType, "Removal");
                if (removalField == null)
                    removalProperty = AccessTools.Property(upgradeField.FieldType, "Removal");
            }

            defField = surface.Field(compPropertiesUpgradeTreeType, "def");

            nodesField = surface.Field(upgradeTreeDefType, "nodes");
            getNodeMethod = surface.Method(upgradeTreeDefType, "GetNode", new[] { typeof(string) });

            keyField = surface.Field(upgradeNodeType, "key");
            labelField = surface.Field(upgradeNodeType, "label");
            descriptionField = surface.Field(upgradeNodeType, "description");
            upgradeExplanationField = surface.Field(upgradeNodeType, "upgradeExplanation");
            hiddenField = surface.Field(upgradeNodeType, "hidden");
            ingredientsField = surface.Field(upgradeNodeType, "ingredients");
            prerequisiteNodesField = surface.Field(upgradeNodeType, "prerequisiteNodes");
            disableIfUpgradeNodeEnabledField = surface.Field(upgradeNodeType, "disableIfUpgradeNodeEnabled");
            disableIfUpgradeNodesEnabledField = surface.Field(upgradeNodeType, "disableIfUpgradeNodesEnabled");
            upgradesField = surface.Field(upgradeNodeType, "upgrades");

            upgradeDescriptionMethod = vehiclePawnType != null
                ? surface.Method(upgradeType, "UpgradeDescription", new[] { vehiclePawnType })
                : null;

            textEntryLabelField = surface.Field(upgradeTextEntryType, "label");
            textEntryDescriptionField = surface.Field(upgradeTextEntryType, "description");

            ready = surface.Ready;
        }

        // Stable English dispatch token (l10n-exempt: never displayed raw —
        // DisplayName/CategoryDisplayName below render VF's own tab label).
        public override string CategoryKey => "VF Upgrades";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        // The tab's own labelKey, the word a sighted player reads on the tab strip.
        private static string TabLabel => CompatText.ResolveOrFallback("VF_TabUpgrades", "RimWorldAccess.Compat.Vf.UpgradesTab");

        public override string DisplayName(InspectTabBase tab) => TabLabel;

        public override string CategoryDisplayName(object obj) => TabLabel;

        public override bool CanExpand(object obj)
        {
            return ready && obj is Pawn p && vehiclePawnType.IsInstanceOfType(p)
                && compUpgradeTreeProperty.GetValue(p) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;
            if (!(obj is Pawn vehicle) || !vehiclePawnType.IsInstanceOfType(vehicle))
                return;

            try
            {
                BuildAllSections(categoryItem, vehicle, mode);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfUpgradesTabAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears and rebuilds the whole category after a mutation — starting, finishing
        /// or cancelling one node's unlock moves the in-progress summary line and can
        /// change every other node's state clause and action row, so a full rebuild is
        /// simplest and correct. Routed through the framework so the extender and
        /// parity-capture passes survive the rebuild.
        /// </summary>
        private void RebuildCategory(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, obj, mode, this, sharedTab);
        }

        // ---- Tree body ----

        private void BuildAllSections(InspectionTreeItem categoryItem, Pawn vehicle, InspectionMode mode)
        {
            object comp = compUpgradeTreeProperty.GetValue(vehicle);
            if (comp == null)
                return;

            object nodeUnlocking = nodeUnlockingProperty.GetValue(comp);
            if (nodeUnlocking != null)
            {
                string unlockingLabel = labelField.GetValue(nodeUnlocking) as string ?? "";
                InspectNodeFactory.DetailLine(categoryItem,
                    "RimWorldAccess.Compat.Vf.UpgradeInProgressSummary".Translate(unlockingLabel));
            }

            object props = propsProperty.GetValue(comp);
            object def = props != null ? defField.GetValue(props) : null;
            IList nodes = def != null ? nodesField.GetValue(def) as IList : null;
            if (nodes == null)
                return;

            var graph = new UpgradeGraph(this, def, nodes);
            foreach (object root in graph.Roots)
                BuildNodeSection(categoryItem, categoryItem, vehicle, comp, def, root, graph, mode);
        }

        private void BuildNodeSection(InspectionTreeItem categoryItem, InspectionTreeItem parentItem, Pawn vehicle, object comp, object def, object node, UpgradeGraph graph, InspectionMode mode)
        {
            string nodeLabel = labelField.GetValue(node) as string ?? "";
            string stateClause = ComputeStateClause(comp, def, node);
            string label = "RimWorldAccess.Compat.Vf.UpgradeNodeSection".Translate(nodeLabel, stateClause);

            InspectNodeFactory.Section(parentItem, label, node, secItem =>
                BuildNodeChildren(categoryItem, secItem, vehicle, comp, def, node, graph, mode));
        }

        /// <summary>
        /// The tree the tab's prerequisite lines draw. A node whose first visible prerequisite
        /// sits in a cycle is never reached from a root, so unreached nodes are promoted to roots
        /// in author order; every node therefore appears exactly once.
        /// </summary>
        private sealed class UpgradeGraph
        {
            internal readonly List<object> Roots = new List<object>();
            private readonly Dictionary<string, List<object>> children = new Dictionary<string, List<object>>();
            private readonly Dictionary<string, string> parentKey = new Dictionary<string, string>();
            private readonly VfUpgradesTabAdapter owner;

            internal UpgradeGraph(VfUpgradesTabAdapter owner, object def, IList nodes)
            {
                this.owner = owner;
                var visible = new List<object>();
                foreach (object node in nodes)
                {
                    if (!(bool)owner.hiddenField.GetValue(node))
                        visible.Add(node);
                }

                var candidates = new List<object>();
                foreach (object node in visible)
                {
                    string parent = FirstVisiblePrerequisite(def, node);
                    if (parent == null)
                    {
                        candidates.Add(node);
                        continue;
                    }
                    parentKey[Key(node)] = parent;
                    if (!children.TryGetValue(parent, out List<object> list))
                        children[parent] = list = new List<object>();
                    list.Add(node);
                }

                var reached = new HashSet<string>();
                foreach (object node in candidates)
                    AddRoot(node, reached);
                foreach (object node in visible)
                {
                    if (!reached.Contains(Key(node)))
                    {
                        parentKey.Remove(Key(node));
                        AddRoot(node, reached);
                    }
                }
            }

            internal IEnumerable<object> ChildrenOf(object node)
            {
                return children.TryGetValue(Key(node), out List<object> list) ? list : Enumerable.Empty<object>();
            }

            internal string ParentKeyOf(object node)
            {
                return parentKey.TryGetValue(Key(node), out string parent) ? parent : null;
            }

            private void AddRoot(object root, HashSet<string> reached)
            {
                Roots.Add(root);
                var queue = new Queue<object>();
                reached.Add(Key(root));
                queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    object node = queue.Dequeue();
                    if (!children.TryGetValue(Key(node), out List<object> list))
                        continue;
                    list.RemoveAll(child => reached.Contains(Key(child)));
                    foreach (object child in list)
                    {
                        reached.Add(Key(child));
                        queue.Enqueue(child);
                    }
                }
            }

            private string FirstVisiblePrerequisite(object def, object node)
            {
                if (!(owner.prerequisiteNodesField.GetValue(node) is List<string> keys))
                    return null;
                foreach (string key in keys)
                {
                    object prereq = owner.getNodeMethod.Invoke(def, new object[] { key });
                    if (prereq != null && !(bool)owner.hiddenField.GetValue(prereq) && key != Key(node))
                        return key;
                }
                return null;
            }

            private string Key(object node) => owner.keyField.GetValue(node) as string ?? "";
        }

        /// <summary>Mirrors DrawNodeCondition's border priority: in-progress beats unlocked beats disabled beats missing-prerequisites beats available.</summary>
        private string ComputeStateClause(object comp, object def, object node)
        {
            object nodeUnlocking = nodeUnlockingProperty.GetValue(comp);
            if (nodeUnlocking != null && Equals(nodeUnlocking, node))
            {
                bool removal = false;
                object upgradeInProgress = upgradeField.GetValue(comp);
                if (upgradeInProgress != null)
                {
                    if (removalField != null)
                        removal = (bool)removalField.GetValue(upgradeInProgress);
                    else if (removalProperty != null)
                        removal = (bool)removalProperty.GetValue(upgradeInProgress);
                }
                return removal
                    ? "RimWorldAccess.Compat.Vf.UpgradeStateRemovalInProgress".Translate()
                    : "RimWorldAccess.Compat.Vf.UpgradeStateUpgradeInProgress".Translate();
            }

            if ((bool)nodeUnlockedMethod.Invoke(comp, new[] { node }))
                return "RimWorldAccess.Compat.Vf.UpgradeStateUnlocked".Translate();

            if ((bool)disabledMethod.Invoke(comp, new[] { node }))
            {
                string disablers = string.Join(", ", GetUnlockedDisablerLabels(comp, def, node));
                return "RimWorldAccess.Compat.Vf.UpgradeStateDisabledBy".Translate(disablers);
            }

            if (!(bool)prerequisitesMetMethod.Invoke(comp, new[] { node }))
            {
                string missing = string.Join(", ", GetUnmetPrerequisiteLabels(comp, def, node));
                return "RimWorldAccess.Compat.Vf.UpgradeStateRequires".Translate(missing);
            }

            return "RimWorldAccess.Compat.Vf.UpgradeStateAvailable".Translate();
        }

        private List<string> GetUnlockedDisablerLabels(object comp, object def, object node)
        {
            var labels = new List<string>();
            foreach (object disablerNode in GetDisablerNodes(def, node))
            {
                if (!(bool)nodeUnlockedMethod.Invoke(comp, new[] { disablerNode }))
                    continue;
                string l = labelField.GetValue(disablerNode) as string;
                if (!string.IsNullOrEmpty(l))
                    labels.Add(l);
            }
            return labels;
        }

        private List<string> GetUnmetPrerequisiteLabels(object comp, object def, object node)
        {
            var labels = new List<string>();
            List<string> prereqKeys = prerequisiteNodesField.GetValue(node) as List<string>;
            if (prereqKeys == null)
                return labels;

            foreach (string key in prereqKeys)
            {
                object prereqNode = getNodeMethod.Invoke(def, new object[] { key });
                if (prereqNode == null)
                    continue;
                if ((bool)nodeUnlockedMethod.Invoke(comp, new[] { prereqNode }))
                    continue;
                string l = labelField.GetValue(prereqNode) as string;
                if (!string.IsNullOrEmpty(l))
                    labels.Add(l);
            }
            return labels;
        }

        private IEnumerable<object> GetDisablerNodes(object def, object node)
        {
            string single = disableIfUpgradeNodeEnabledField.GetValue(node) as string;
            if (!string.IsNullOrEmpty(single))
            {
                object disablerNode = getNodeMethod.Invoke(def, new object[] { single });
                if (disablerNode != null)
                    yield return disablerNode;
            }

            List<string> multi = disableIfUpgradeNodesEnabledField.GetValue(node) as List<string>;
            if (multi != null)
            {
                foreach (string key in multi)
                {
                    object disablerNode = getNodeMethod.Invoke(def, new object[] { key });
                    if (disablerNode != null)
                        yield return disablerNode;
                }
            }
        }

        private void BuildNodeChildren(InspectionTreeItem categoryItem, InspectionTreeItem secItem, Pawn vehicle, object comp, object def, object node, UpgradeGraph graph, InspectionMode mode)
        {
            string description = descriptionField.GetValue(node) as string;
            if (!string.IsNullOrEmpty(description))
                InspectNodeFactory.DetailLine(secItem, description);

            BuildCostLines(secItem, vehicle, node);
            BuildEffectLines(secItem, vehicle, node);
            BuildAlsoRequiresLine(secItem, def, node, graph.ParentKeyOf(node));
            BuildIncompatibilityLine(secItem, def, node);

            if (mode != InspectionMode.ReadOnly)
                BuildActionRow(categoryItem, secItem, vehicle, comp, node, mode);

            foreach (object child in graph.ChildrenOf(node))
                BuildNodeSection(categoryItem, secItem, vehicle, comp, def, child, graph, mode);
        }

        /// <summary>Every prerequisite except the one the node nests under, met or not: the drawn lines show them all.</summary>
        private void BuildAlsoRequiresLine(InspectionTreeItem secItem, object def, object node, string parentKey)
        {
            if (!(prerequisiteNodesField.GetValue(node) is List<string> keys))
                return;

            var labels = new List<string>();
            foreach (string key in keys)
            {
                if (key == parentKey)
                    continue;
                object prereq = getNodeMethod.Invoke(def, new object[] { key });
                string l = prereq != null ? labelField.GetValue(prereq) as string : null;
                if (!string.IsNullOrEmpty(l))
                    labels.Add(l);
            }

            if (labels.Count > 0)
                InspectNodeFactory.DetailLine(secItem,
                    "RimWorldAccess.Compat.Vf.UpgradeAlsoRequires".Translate(string.Join(", ", labels)));
        }

        private void BuildCostLines(InspectionTreeItem secItem, Pawn vehicle, object node)
        {
            List<ThingDefCountClass> ingredients = ingredientsField.GetValue(node) as List<ThingDefCountClass>;
            if (ingredients == null)
                return;

            bool hasMap = vehicle.Map != null;
            foreach (ThingDefCountClass tdc in ingredients)
            {
                string thingLabel = tdc.thingDef.LabelCap;
                if (hasMap)
                {
                    int have = vehicle.Map.resourceCounter.GetCount(tdc.thingDef);
                    InspectNodeFactory.DetailLine(secItem,
                        "RimWorldAccess.Compat.Vf.UpgradeCostLine".Translate(thingLabel, tdc.count, have));
                }
                else
                {
                    InspectNodeFactory.DetailLine(secItem,
                        "RimWorldAccess.Compat.Vf.UpgradeCostLineNoMap".Translate(thingLabel, tdc.count));
                }
            }
        }

        private void BuildEffectLines(InspectionTreeItem secItem, Pawn vehicle, object node)
        {
            string explanation = upgradeExplanationField.GetValue(node) as string;
            if (!string.IsNullOrEmpty(explanation))
            {
                // Embedded newlines would read as one run-on sentence otherwise.
                // Null when the explanation was whitespace only.
                InspectNodeFactory.DetailLine(secItem, SpeechFlatten.ToSentences(explanation) ?? string.Empty);
                return;
            }

            IList upgrades = upgradesField.GetValue(node) as IList;
            if (upgrades == null || upgrades.Count == 0)
                return;

            try
            {
                foreach (object upgrade in upgrades)
                {
                    IEnumerable entries = upgradeDescriptionMethod.Invoke(upgrade, new object[] { vehicle }) as IEnumerable;
                    if (entries == null)
                        continue;

                    foreach (object entry in entries)
                    {
                        string entryLabel = textEntryLabelField.GetValue(entry) as string ?? "";
                        string entryDescription = textEntryDescriptionField.GetValue(entry) as string;
                        if (string.IsNullOrEmpty(entryDescription))
                            continue;
                        InspectNodeFactory.DetailLine(secItem, $"{entryLabel}: {entryDescription}");
                    }
                }
            }
            catch (Exception ex)
            {
                // A modded Upgrade subclass's UpgradeDescription is third-party code; isolate its
                // failure to this section rather than the whole tab.
                ModLogger.Error($"VfUpgradesTabAdapter effect listing failed: {ex.Message}");
            }
        }

        private void BuildIncompatibilityLine(InspectionTreeItem secItem, object def, object node)
        {
            var labels = new List<string>();
            foreach (object disablerNode in GetDisablerNodes(def, node))
            {
                string l = labelField.GetValue(disablerNode) as string;
                if (!string.IsNullOrEmpty(l))
                    labels.Add(l);
            }

            if (labels.Count > 0)
                InspectNodeFactory.DetailLine(secItem,
                    "RimWorldAccess.Compat.Vf.UpgradeIncompatibleWith".Translate(string.Join(", ", labels)));
        }

        /// <summary>Exactly one action row (or an explanatory read-only line) per node, matching ITab_Vehicle_Upgrades.DrawButtons' branch structure and priority.</summary>
        private void BuildActionRow(InspectionTreeItem categoryItem, InspectionTreeItem secItem, Pawn vehicle, object comp, object node, InspectionMode mode)
        {
            object nodeUnlocking = nodeUnlockingProperty.GetValue(comp);
            bool isUnlocking = nodeUnlocking != null && Equals(nodeUnlocking, node);
            bool unlocked = (bool)nodeUnlockedMethod.Invoke(comp, new[] { node });
            bool lastUnlocked = unlocked && (bool)lastNodeUnlockedMethod.Invoke(comp, new[] { node });

            if (isUnlocking)
            {
                InspectionTreeItem item = InspectNodeFactory.ActionRow(secItem, "CancelButton".Translate(), node, null);
                item.OnActivate = () => OnCancelUpgrade(categoryItem, vehicle, comp, mode);
                return;
            }

            if (unlocked && lastUnlocked)
            {
                InspectionTreeItem item = InspectNodeFactory.ActionRow(secItem, CompatText.ResolveOrFallback("VF_RemoveUpgrade", "RimWorldAccess.Compat.Vf.RemoveUpgradeActionLabel"), node, null);
                item.OnActivate = () => OnRemoveUpgrade(categoryItem, vehicle, comp, node, mode);
                return;
            }

            if (!unlocked)
            {
                InspectionTreeItem item = InspectNodeFactory.ActionRow(secItem, CompatText.ResolveOrFallback("VF_Upgrade", "RimWorldAccess.Compat.Vf.UpgradeActionLabel"), node, null);
                item.OnActivate = () => OnStartUpgrade(categoryItem, vehicle, comp, node, mode);
                return;
            }

            // Unlocked but not the last-unlocked node in its chain: the drawn Upgrade button is a
            // live no-op here (guarded by !NodeUnlocked), so this read-only line substitutes.
            InspectNodeFactory.DetailLine(secItem, "RimWorldAccess.Compat.Vf.UpgradeLockedByDependents".Translate());
        }

        // Vehicle A: the comp predicates (Disabled/PrerequisitesMet/NodeUnlocked/LastNodeUnlocked)
        // ARE the vanilla gates, and the reject paths fire VF's own Messages, which the message
        // pipeline speaks on its own — never mirrored here with TolkHelper.

        private void OnCancelUpgrade(InspectionTreeItem categoryItem, Pawn vehicle, object comp, InspectionMode mode)
        {
            try
            {
                if (vehicle == null || vehicle.Destroyed)
                    return;

                clearUpgradeMethod.Invoke(comp, null);

                RebuildCategory(categoryItem, vehicle, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.UpgradeCanceled".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfUpgradesTabAdapter cancel action failed: {ex.Message}");
            }
        }

        private void OnRemoveUpgrade(InspectionTreeItem categoryItem, Pawn vehicle, object comp, object node, InspectionMode mode)
        {
            try
            {
                if (vehicle == null || vehicle.Destroyed)
                    return;
                if (!(bool)nodeUnlockedMethod.Invoke(comp, new[] { node })
                    || !(bool)lastNodeUnlockedMethod.Invoke(comp, new[] { node }))
                    return; // state moved on since this row was built

                string nodeLabel = labelField.GetValue(node) as string ?? "";
                SoundDefOf.Click.PlayOneShotOnCamera(vehicle.Map);

                if (DebugSettings.godMode)
                {
                    resetUnlockMethod.Invoke(comp, new[] { node });
                    RebuildCategory(categoryItem, vehicle, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.UpgradeRemovedInstant".Loc(nodeLabel));
                }
                else
                {
                    removeUnlockMethod.Invoke(comp, new[] { node });
                    RebuildCategory(categoryItem, vehicle, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.UpgradeRemovalQueued".Loc(nodeLabel));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfUpgradesTabAdapter remove action failed: {ex.Message}");
            }
        }

        private void OnStartUpgrade(InspectionTreeItem categoryItem, Pawn vehicle, object comp, object node, InspectionMode mode)
        {
            try
            {
                if (vehicle == null || vehicle.Destroyed)
                    return;
                if ((bool)nodeUnlockedMethod.Invoke(comp, new[] { node }))
                    return; // already unlocked since this row was built

                if ((bool)disabledMethod.Invoke(comp, new[] { node }))
                {
                    Messages.Message(CompatText.ResolveOrFallback("VF_DisabledFromOtherNode", "RimWorldAccess.Compat.Vf.UpgradeDisabledMessage"), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (!(bool)prerequisitesMetMethod.Invoke(comp, new[] { node }))
                {
                    Messages.Message(CompatText.ResolveOrFallback("VF_MissingPrerequisiteUpgrade", "RimWorldAccess.Compat.Vf.UpgradeMissingPrerequisiteMessage"), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                string nodeLabel = labelField.GetValue(node) as string ?? "";
                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera(vehicle.Map);

                if (DebugSettings.godMode)
                {
                    finishUnlockMethod.Invoke(comp, new[] { node });
                    SoundDefOf.Building_Complete.PlayOneShot(vehicle);
                    RebuildCategory(categoryItem, vehicle, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.UpgradeCompletedInstant".Loc(nodeLabel));
                }
                else
                {
                    startUnlockMethod.Invoke(comp, new[] { node });
                    RebuildCategory(categoryItem, vehicle, mode);
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.UpgradeStarted".Loc(nodeLabel));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfUpgradesTabAdapter start action failed: {ex.Message}");
            }
        }
    }
}
