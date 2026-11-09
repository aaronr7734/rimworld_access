using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.DialogViewXenoGenes</c>, backing
    /// <see cref="Shell.CharEditorXenoGenesScope"/>. The dialog is opened through
    /// <see cref="CharEditorCompat.OpenViewGenes"/>; this facade only reads and writes the window.
    /// The dialog overrides no <c>OnAcceptKeyPressed</c>, so Enter is a genuine no-op on the raw
    /// window — no accept-key trap here, unlike the browser family — and <c>closeOnCancel = true</c>
    /// with no override means Escape already closes it through vanilla.
    /// Endo/xeno toggle: the mod flips <c>bIsXeno</c> in an inline anonymous delegate
    /// (DialogViewXenoGenes.cs:77-80), so <see cref="ToggleTarget"/> is MUTATION-C.
    /// Add gene rides <c>AOpenAddDialog()</c> (vehicle A), which opens <c>DialogGenery(bIsXeno)</c>;
    /// that dialog's captured <c>bIsXeno</c> decides which gene set its OnAccept writes to, so no
    /// plumbing beyond the constructor argument is needed.
    /// Remove gene rides <c>ARemoveEndoGene(Gene)</c>, which chains RemoveGeneKeepFirst,
    /// PrintIfXenotypeIsPrefered and UpdateGraphics; candidates come from the PUBLIC
    /// Xenogenes/Endogenes lists. Apply xenotype rides <c>AChangeXenotype</c>/
    /// <c>ALoadCustomXenotype</c> over the two lists the dialog snapshots at construction. Both are
    /// presented through <see cref="WindowlessFloatMenuState"/> rather than the mod's own FloatMenu,
    /// which would close itself before a keyboard user could reach it.
    /// Clear (<see cref="ClearGenes"/>) is MUTATION-C: it makes the two calls AResetGenes makes, with
    /// an EXPLICIT keepHairAndSkin argument in place of <c>Event.current.control</c>, which is unsafe
    /// to read outside the mouse click's own event — exposed as two discrete verbs rather than a
    /// hidden modifier. The mod's own button applies instantly, so the scope adds its own
    /// confirmation before wiping a gene set.
    /// </summary>
    internal static class CharEditorXenoGenesCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorXenoGenesCompat", Resolve);

        private static Type dialogType;
        private static Type geneToolType;

        private static FieldInfo targetField;
        private static FieldInfo bIsXenoField;
        private static FieldInfo lxenotypesField;
        private static FieldInfo lcustomxenotypesField;

        private static MethodInfo aOpenAddDialogMethod;
        private static MethodInfo aRemoveEndoGeneMethod;
        private static MethodInfo aChangeXenotypeMethod;
        private static MethodInfo aLoadCustomXenotypeMethod;

        private static MethodInfo clearGenesMethod;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get { gate.Ensure(); return gate.Ensure(); }
        }

        public static Type DialogType
        {
            get { gate.Ensure(); return dialogType; }
        }

        private static bool Resolve(ReflectionSurface surface)
        {
            if (!ModPresent)
                return false;

            dialogType = surface.Type("CharacterEditor.DialogViewXenoGenes");
            geneToolType = surface.Type("CharacterEditor.GeneTool");

            targetField = surface.Field(dialogType, "target");
            bIsXenoField = surface.Field(dialogType, "bIsXeno");
            lxenotypesField = surface.Field(dialogType, "lxenotypes");
            lcustomxenotypesField = surface.Field(dialogType, "lcustomxeontypes"); // mod's own typo, preserved
            aOpenAddDialogMethod = surface.Method(dialogType, "AOpenAddDialog", Type.EmptyTypes);
            aRemoveEndoGeneMethod = surface.Method(dialogType, "ARemoveEndoGene", new[] { typeof(Gene) });
            aChangeXenotypeMethod = surface.Method(dialogType, "AChangeXenotype", new[] { typeof(XenotypeDef) });
            aLoadCustomXenotypeMethod = surface.Method(dialogType, "ALoadCustomXenotype", new[] { typeof(CustomXenotype) });

            clearGenesMethod = surface.Method(geneToolType, "ClearGenes", new[] { typeof(Pawn), typeof(bool), typeof(bool) });

            return surface.Ready && CharEditorCompat.EditorCore.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorXenoGenesCompat." + member + " failed: " + ex.Message);
        }

        private static void UpdateGraphics()
        {
            CharEditorCompat.EditorCore.UpdateGraphics();
        }

        // Target pawn and endo/xeno toggle.

        /// <summary>The dialog's own captured pawn, fixed at construction: this dialog never re-targets a switched edited pawn.</summary>
        public static Pawn TargetPawn(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return targetField.GetValue(dlg) as Pawn;
            }
            catch (Exception ex)
            {
                Fail("TargetPawn", ex);
                return null;
            }
        }

        public static bool IsXeno(Window dlg)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                return (bool)bIsXenoField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("IsXeno", ex);
                return false;
            }
        }

        /// <summary>MUTATION-C: mirrors the mod's own inline toggle delegate (DialogViewXenoGenes.cs:77-80) -- no named method exists to ride.</summary>
        public static void ToggleTarget(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                bIsXenoField.SetValue(dlg, !(bool)bIsXenoField.GetValue(dlg));
            }
            catch (Exception ex)
            {
                Fail("ToggleTarget", ex);
            }
        }

        // Add gene.

        /// <summary>Vehicle A: DialogViewXenoGenes.AOpenAddDialog() -- opens DialogGenery(bIsXeno), picked up by CharEditorDialogCompat's own registration.</summary>
        public static void OpenAddGeneDialog(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                aOpenAddDialogMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("OpenAddGeneDialog", ex);
            }
        }

        // Remove gene.

        /// <summary>The mod's own remove-menu candidate list -- Pawn_GeneTracker.Xenogenes/Endogenes, both PUBLIC vanilla, read directly (no reflection needed).</summary>
        public static List<Gene> RemoveGeneCandidates(Window dlg)
        {
            Pawn pawn = TargetPawn(dlg);
            if (pawn?.genes == null)
                return new List<Gene>();
            return (IsXeno(dlg) ? pawn.genes.Xenogenes : pawn.genes.Endogenes)?.ToList() ?? new List<Gene>();
        }

        /// <summary>Vehicle A: DialogViewXenoGenes.ARemoveEndoGene(Gene) -- the exact callback the mod's own remove float menu invokes for the chosen gene.</summary>
        public static void RemoveGene(Window dlg, Gene gene)
        {
            if (!Ready || dlg == null || gene == null)
                return;
            try
            {
                aRemoveEndoGeneMethod.Invoke(dlg, new object[] { gene });
            }
            catch (Exception ex)
            {
                Fail("RemoveGene", ex);
            }
        }

        // Clear.

        /// <summary>MUTATION-C: reproduces AResetGenes' own two-line body (GeneDef.ClearGenes then UpdateGraphics) with an EXPLICIT keepHairAndSkin argument in place of Event.current.control -- see class remarks.</summary>
        public static void ClearGenes(Window dlg, bool keepHairAndSkin)
        {
            if (!Ready || dlg == null)
                return;
            Pawn pawn = TargetPawn(dlg);
            if (pawn == null)
                return;
            try
            {
                clearGenesMethod.Invoke(null, new object[] { pawn, IsXeno(dlg), keepHairAndSkin });
                UpdateGraphics();
            }
            catch (Exception ex)
            {
                Fail("ClearGenes", ex);
            }
        }

        // Apply xenotype (premade defs plus custom files on disk).

        /// <summary>Snapshotted at the dialog's own construction, displayPriority descending, matching the mod's own lxenotypes ordering.</summary>
        public static List<XenotypeDef> XenotypeCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<XenotypeDef>();
            try
            {
                return lxenotypesField.GetValue(dlg) as List<XenotypeDef> ?? new List<XenotypeDef>();
            }
            catch (Exception ex)
            {
                Fail("XenotypeCandidates", ex);
                return new List<XenotypeDef>();
            }
        }

        /// <summary>Snapshotted at the dialog's own construction (custom xenotype files on disk at that moment).</summary>
        public static List<CustomXenotype> CustomXenotypeCandidates(Window dlg)
        {
            if (!Ready || dlg == null)
                return new List<CustomXenotype>();
            try
            {
                return lcustomxenotypesField.GetValue(dlg) as List<CustomXenotype> ?? new List<CustomXenotype>();
            }
            catch (Exception ex)
            {
                Fail("CustomXenotypeCandidates", ex);
                return new List<CustomXenotype>();
            }
        }

        /// <summary>Vehicle A: DialogViewXenoGenes.AChangeXenotype(XenotypeDef).</summary>
        public static void ApplyXenotypeDef(Window dlg, XenotypeDef def)
        {
            if (!Ready || dlg == null || def == null)
                return;
            try
            {
                aChangeXenotypeMethod.Invoke(dlg, new object[] { def });
            }
            catch (Exception ex)
            {
                Fail("ApplyXenotypeDef", ex);
            }
        }

        /// <summary>Vehicle A: DialogViewXenoGenes.ALoadCustomXenotype(CustomXenotype).</summary>
        public static void ApplyCustomXenotype(Window dlg, CustomXenotype c)
        {
            if (!Ready || dlg == null || c == null)
                return;
            try
            {
                aLoadCustomXenotypeMethod.Invoke(dlg, new object[] { c });
            }
            catch (Exception ex)
            {
                Fail("ApplyCustomXenotype", ex);
            }
        }
    }
}
