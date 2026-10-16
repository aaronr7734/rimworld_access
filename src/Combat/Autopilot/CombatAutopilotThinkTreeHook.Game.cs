using System.Collections.Generic;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// Puts the autopilot ahead of every JobGiver_Orders, including ones inserted by mods
    /// that load after this one (Patches/CombatAutopilot.xml only sees the trees as they stood
    /// at our load position). Runs at ThinkTreeDef resolution, after all XML patching and
    /// before save keys are assigned, so the inserted nodes key like XML-declared ones.
    /// </summary>
    public static class CombatAutopilotThinkTreeHook
    {
        // Mod constructors run before XML patching and def resolution; PatchAll runs after.
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ThinkTreeDef), nameof(ThinkTreeDef.ResolveReferences)),
                prefix: new HarmonyMethod(typeof(CombatAutopilotThinkTreeHook), nameof(Prefix)));
        }

        private static readonly HashSet<ThinkTreeDef> processed = new HashSet<ThinkTreeDef>();

        // Every tree before the first resolves: ThinkNode_Subtree deep-copies its target tree
        // at resolution, so a tree fixed after a parent resolved would miss that copy.
        private static void Prefix()
        {
            foreach (ThinkTreeDef def in DefDatabase<ThinkTreeDef>.AllDefsListForReading)
            {
                if (processed.Add(def))
                {
                    InsertBeforeOrders(def.thinkRoot);
                }
            }
        }

        private static void InsertBeforeOrders(ThinkNode node)
        {
            List<ThinkNode> subNodes = node?.subNodes;
            if (subNodes == null)
            {
                return;
            }
            for (int i = 0; i < subNodes.Count; i++)
            {
                if (subNodes[i] is JobGiver_Orders
                    && (i == 0 || !(subNodes[i - 1] is ThinkNode_ConditionalCombatAutopilot)))
                {
                    subNodes.Insert(i, NewAutopilotNode());
                    i++;
                }
                else
                {
                    InsertBeforeOrders(subNodes[i]);
                }
            }
        }

        /// <summary>Same configuration as the generic insert in Patches/CombatAutopilot.xml.</summary>
        private static ThinkNode NewAutopilotNode()
        {
            ThinkNode_ConditionalCombatAutopilot conditional = new ThinkNode_ConditionalCombatAutopilot();
            conditional.subNodes.Add(JobGiver_CombatAutopilot.Create(200f, 72f));
            return conditional;
        }
    }
}
