using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <c>EditorUI+BlockNeeds</c>'s own mutation vehicles (the plus/minus need steppers, Fill all
    /// needs, Clear all memories) plus its two private label/icon resolvers, which this facade
    /// rides by reflection so the Memories subsection never re-derives the mod's own
    /// royal-title/role/relic/apparel/other-pawn token substitution. Gated by its own
    /// <see cref="NeedsReady"/>.
    ///
    /// MUTATION VEHICLES:
    /// <list type="bullet">
    /// <item><see cref="StepNeed"/> invokes BlockNeeds' own <c>AAddNeed(Need)</c>/<c>ASubNeed(Need)</c>
    /// (the plus/minus icon's own per-need handlers, vehicle A).</item>
    /// <item><see cref="SetNeedPercentageExact"/> writes vanilla <c>Need.CurLevelPercentage</c>
    /// directly -- a PUBLIC vanilla property whose setter self-clamps via <c>CurLevel</c>
    /// (<c>Mathf.Clamp(value, 0f, MaxLevel)</c>, Verse/Need.cs) -- vehicle B, no MUTATION-C: the
    /// mod itself has no exact-entry handler to ride (only its +/-5% buttons), and this is the
    /// SAME gated property the mod's own buttons write through.</item>
    /// <item><see cref="FillAllNeeds"/>/<see cref="ClearAllMemories"/> invoke BlockNeeds' own
    /// <c>AFullNeeds()</c>/<c>AClearAllMemory()</c> (the lower-button row's own handlers).</item>
    /// <item><see cref="OpenAddThought"/> invokes BlockNeeds' own <c>ADoAddThought()</c>, which
    /// opens <c>DialogAddThought()</c> with no type preset (Memories section's "Add thought..."
    /// row) -- distinct from Social's own <c>AAddThought()</c>, which presets the Social filter;
    /// both open the SAME dialog type, registered once against
    /// <see cref="CharEditorThoughtBrowserCompat"/>.</item>
    /// <item><see cref="RemoveThought"/> invokes <c>ThoughtTool.RemoveThought(Pawn, Thought)</c>
    /// (a mod-internal static extension method, not vanilla) -- the SAME call BlockNeeds' own
    /// per-row remove-mode click makes; Delete here always removes rather than reproducing the
    /// mod's own remove-MODE toggle.</item>
    /// </list>
    ///
    /// <see cref="DescribeThought"/> rides BlockNeeds' own PRIVATE <c>LabelHelper</c>/
    /// <c>IconAndOffsetHelper</c> instance methods by reflection (out-parameter methods; a
    /// reflection <c>Invoke</c> writes an out parameter back into the same argument array slot,
    /// so no by-ref plumbing beyond that array is needed) -- this is the mod's own resolved
    /// label (royal-title/role/relic/apparel/other-pawn token substitution, dev-mode stage
    /// detail) and its own icon/offset formula, never re-derived. <see cref="AggregatedThought"/>
    /// rides <c>ThoughtTool.CountOfDefs&lt;Thought_MemorySocial&gt;</c> (closed generic, mirroring
    /// <see cref="CharEditorCompat.CloseGeneric"/>'s own idiom) with the mod's own frozen
    /// "AteNon" prefix, so the aggregated row groups exactly the thoughts BlockNeeds' own listing
    /// aggregates. <see cref="SortedThoughts"/> rides <c>ThoughtTool.GetThoughtsSorted(Pawn)</c>,
    /// the same ordering (mood-only thoughts first reversed, then opinion-bearing thoughts) the
    /// mod's own Memories panel walks.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static readonly LazyReflectionGate needsGate = new LazyReflectionGate("CharEditorCompat needs", ResolveNeeds);

        private static Type blockNeedsType;
        private static Type thoughtToolType;

        private static MethodInfo getBlockNeeds;

        private static MethodInfo needsAddNeedMethod;
        private static MethodInfo needsSubNeedMethod;
        private static MethodInfo needsFullNeedsMethod;
        private static MethodInfo needsClearAllMemoryMethod;
        private static MethodInfo needsAddThoughtMethod;
        private static MethodInfo needsLabelHelperMethod;
        private static MethodInfo needsIconOffsetHelperMethod;

        private static MethodInfo thoughtCountOfDefsMethod;
        private static MethodInfo thoughtGetSortedMethod;
        private static MethodInfo thoughtRemoveMethod;

        /// <summary>True when every member this slice's Needs section needs resolved.</summary>
        public static bool NeedsReady
        {
            get
            {
                gate.Ensure();
                return needsGate.Ensure();
            }
        }

        private static bool ResolveNeeds(ReflectionSurface surface)
        {
            if (editorUIType == null || tabTypeType == null)
                return false;

            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            blockNeedsType = surface.Supplied("EditorUI.BlockNeeds", editorUIType.GetNestedType("BlockNeeds", NestedFlags));
            thoughtToolType = surface.Type("CharacterEditor.ThoughtTool");

            getBlockNeeds = surface.Required("EditorUI.Get<BlockNeeds>(TabType) closed",
                CloseGeneric(editorUIType, "Get", blockNeedsType));

            needsAddNeedMethod = surface.Method(blockNeedsType, "AAddNeed", new[] { typeof(Need) });
            needsSubNeedMethod = surface.Method(blockNeedsType, "ASubNeed", new[] { typeof(Need) });
            needsFullNeedsMethod = surface.Method(blockNeedsType, "AFullNeeds", Type.EmptyTypes);
            needsClearAllMemoryMethod = surface.Method(blockNeedsType, "AClearAllMemory", Type.EmptyTypes);
            needsAddThoughtMethod = surface.Method(blockNeedsType, "ADoAddThought", Type.EmptyTypes);
            needsLabelHelperMethod = surface.Method(blockNeedsType, "LabelHelper", new[]
            {
                typeof(Thought), typeof(string).MakeByRefType(), typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType(), typeof(string).MakeByRefType(), typeof(int),
            });
            needsIconOffsetHelperMethod = surface.Method(blockNeedsType, "IconAndOffsetHelper", new[]
            {
                typeof(Thought), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(),
                typeof(UnityEngine.Texture2D).MakeByRefType(), typeof(UnityEngine.Color).MakeByRefType(),
            });

            thoughtCountOfDefsMethod = surface.Required("ThoughtTool.CountOfDefs<Thought_MemorySocial> closed",
                CloseGeneric(thoughtToolType, "CountOfDefs", typeof(Thought_MemorySocial)));
            thoughtGetSortedMethod = surface.Method(thoughtToolType, "GetThoughtsSorted", new[] { typeof(Pawn) });
            thoughtRemoveMethod = surface.Method(thoughtToolType, "RemoveThought", new[] { typeof(Pawn), typeof(Thought) });

            return surface.Ready;
        }

        private static object NeedsBlock(Window editorUI)
        {
            return Block(editorUI, getBlockNeeds, "BlockNeeds");
        }

        private static void InvokeOnNeedsBlock(Window editorUI, MethodInfo method, object[] args, string caller)
        {
            if (!NeedsReady || method == null)
                return;
            try
            {
                object block = NeedsBlock(editorUI);
                if (block != null)
                    method.Invoke(block, args);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // ------------------------------------------------------------------
        // Reads.
        // ------------------------------------------------------------------

        /// <summary>The pawn's thoughts in the mod's own listing order -- ThoughtTool.GetThoughtsSorted(Pawn).</summary>
        public static List<Thought> SortedThoughts(Pawn pawn)
        {
            if (!NeedsReady || pawn == null)
                return new List<Thought>();
            try { return thoughtGetSortedMethod.Invoke(null, new object[] { pawn }) as List<Thought> ?? new List<Thought>(); }
            catch (Exception ex) { Fail("SortedThoughts", ex); return new List<Thought>(); }
        }

        /// <summary>
        /// The mod's own "AteNon"-prefixed <c>Thought_MemorySocial</c> aggregate: how many such
        /// thoughts exist and one example to resolve a shared label/icon/offset for, or a zero
        /// count with a null example when none exist -- ThoughtTool.CountOfDefs&lt;Thought_MemorySocial&gt;
        /// (list, out Thought, "AteNon"), the exact call BlockNeeds.DrawMemories makes.
        /// </summary>
        public static int AggregatedThought(List<Thought> sorted, out Thought example)
        {
            example = null;
            if (!NeedsReady || sorted == null)
                return 0;
            try
            {
                object[] args = { sorted, null, "AteNon" };
                var count = (int)thoughtCountOfDefsMethod.Invoke(null, args);
                example = args[1] as Thought;
                return count;
            }
            catch (Exception ex)
            {
                Fail("AggregatedThought", ex);
                return 0;
            }
        }

        /// <summary>
        /// The mod's own resolved label/description/other-pawn-name/tooltip for one thought --
        /// BlockNeeds.LabelHelper(Thought, out, out, out, out, int), the exact private resolver
        /// DrawMemories rides for royal-title/role/relic/apparel/other-pawn token substitution.
        /// <paramref name="count"/> is the aggregate size for the "AteNon" group's shared row (0
        /// for every individual row), matching the mod's own call shape.
        /// </summary>
        public static void DescribeThought(Window editorUI, Thought t, int count,
            out string label, out string description, out string otherPawnName, out string tooltip)
        {
            label = "";
            description = "";
            otherPawnName = "";
            tooltip = "";
            if (!NeedsReady || editorUI == null || t == null)
                return;
            try
            {
                object block = NeedsBlock(editorUI);
                if (block == null)
                    return;
                object[] args = { t, null, null, null, null, count };
                needsLabelHelperMethod.Invoke(block, args);
                label = args[1] as string ?? "";
                description = args[2] as string ?? "";
                otherPawnName = args[3] as string ?? "";
                tooltip = args[4] as string ?? "";
            }
            catch (Exception ex)
            {
                Fail("DescribeThought", ex);
            }
        }

        /// <summary>BlockNeeds.IconAndOffsetHelper(Thought, out, out, out, out) -- the mod's own opinion/mood offset formula (only the offsets are read; the icon/color are visual-only).</summary>
        public static void ThoughtOffsets(Window editorUI, Thought t, out float opinionOffset, out float moodOffset)
        {
            opinionOffset = 0f;
            moodOffset = 0f;
            if (!NeedsReady || editorUI == null || t == null)
                return;
            try
            {
                object block = NeedsBlock(editorUI);
                if (block == null)
                    return;
                object[] args = { t, null, null, null, null };
                needsIconOffsetHelperMethod.Invoke(block, args);
                opinionOffset = args[1] is float f1 ? f1 : 0f;
                moodOffset = args[2] is float f2 ? f2 : 0f;
            }
            catch (Exception ex)
            {
                Fail("ThoughtOffsets", ex);
            }
        }

        // ------------------------------------------------------------------
        // Mutators -- each rides the mod's own handler (vehicle A) except SetNeedPercentageExact
        // (vehicle B, see class remarks).
        // ------------------------------------------------------------------

        /// <summary>Steps one need by the mod's own five-percent increment -- BlockNeeds.AAddNeed(Need)/ASubNeed(Need).</summary>
        public static void StepNeed(Window editorUI, Need need, bool up)
        {
            if (need == null)
                return;
            InvokeOnNeedsBlock(editorUI, up ? needsAddNeedMethod : needsSubNeedMethod, new object[] { need }, "StepNeed");
        }

        /// <summary>Vehicle B: vanilla Need.CurLevelPercentage's own self-clamping setter -- see class remarks.</summary>
        public static void SetNeedPercentageExact(Need need, float percentage)
        {
            if (need == null)
                return;
            try
            {
                need.CurLevelPercentage = percentage;
            }
            catch (Exception ex)
            {
                Fail("SetNeedPercentageExact", ex);
            }
        }

        public static void FillAllNeeds(Window editorUI) => InvokeOnNeedsBlock(editorUI, needsFullNeedsMethod, null, "FillAllNeeds");

        public static void ClearAllMemories(Window editorUI) => InvokeOnNeedsBlock(editorUI, needsClearAllMemoryMethod, null, "ClearAllMemories");

        /// <summary>Opens DialogAddThought() with no type preset -- the Memories section's own "Add thought..." row (BlockNeeds.ADoAddThought()).</summary>
        public static void OpenAddThought(Window editorUI) => InvokeOnNeedsBlock(editorUI, needsAddThoughtMethod, null, "OpenAddThought");

        /// <summary>Removes one thought via ThoughtTool.RemoveThought(Pawn, Thought), the mod's own per-row remove-mode click; Delete here always removes.</summary>
        public static void RemoveThought(Pawn pawn, Thought t)
        {
            if (!NeedsReady || pawn == null || t == null)
                return;
            try { thoughtRemoveMethod.Invoke(null, new object[] { pawn, t }); }
            catch (Exception ex) { Fail("RemoveThought", ex); }
        }
    }
}
