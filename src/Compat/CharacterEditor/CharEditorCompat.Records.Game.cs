using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <c>CharacterEditor.RecordTool.SetRecordValue(this Pawn, RecordDef, float)</c> -- the SAME
    /// write <c>RecordTool.DrawRecord</c>'s three numeric boxes make once their typed value differs
    /// from what was read that frame. Vehicle A: the mod's own extension method is invoked by
    /// reflection because <c>RecordTool</c> is a mod-internal static class with no vanilla-facing
    /// signature this assembly can reference directly.
    ///
    /// Gated by its own <see cref="RecordsReady"/>: <c>RecordTool</c> is free-standing (no
    /// <c>EditorUI</c> dependency), so a rename here must not take down the sibling slices.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static readonly LazyReflectionGate recordsGate = new LazyReflectionGate("CharEditorCompat records", ResolveRecords);

        private static Type recordToolType;
        private static MethodInfo recordSetValueMethod;

        /// <summary>True when RecordTool.SetRecordValue(Pawn, RecordDef, float) resolved.</summary>
        public static bool RecordsReady
        {
            get
            {
                gate.Ensure();
                return recordsGate.Ensure();
            }
        }

        private static bool ResolveRecords(ReflectionSurface surface)
        {
            if (editorUIType == null || tabTypeType == null)
                return false;

            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            recordToolType = surface.Type("CharacterEditor.RecordTool");
            recordSetValueMethod = surface.Method(recordToolType, "SetRecordValue",
                new[] { typeof(Pawn), typeof(RecordDef), typeof(float) });

            return surface.Ready;
        }

        /// <summary>
        /// Callers pass an int/long widened to float exactly as <c>DrawRecord</c> itself does: its
        /// Int and Long boxes hand their typed result straight to this same float overload.
        /// </summary>
        public static void SetRecordValue(Pawn pawn, RecordDef def, float value)
        {
            if (!RecordsReady || pawn == null || def == null)
                return;
            try
            {
                recordSetValueMethod.Invoke(null, new object[] { pawn, def, value });
            }
            catch (Exception ex)
            {
                Fail("SetRecordValue", ex);
            }
        }
    }
}
