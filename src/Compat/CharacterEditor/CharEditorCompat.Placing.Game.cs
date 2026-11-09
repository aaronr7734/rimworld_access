using System;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <c>CharacterEditor.PlacingTool</c>'s teleport-arming statics -- the SAME two methods the
    /// editor's own teleport button (<c>BlockPerson.ABeginTeleportSelectPawn</c>) calls, minus that
    /// handler's own <c>API.EditorMoveRight()</c> call, which only repositions the editor window
    /// and is meaningless once the caller has closed it (<see cref="Shell.CharacterEditorScope"/>'s
    /// teleport rows close the editor themselves before arming).
    ///
    /// Both flows arm a vanilla <see cref="LudeonTK.DebugTool"/> (<c>DebugTools.curTool</c>) whose
    /// <c>clickAction</c> reads <c>UI.MouseCell()</c> on a future map click.
    /// <see cref="Shell.DevToolTargeting"/> already bridges every armed <c>DebugTool</c> to the
    /// shell's keyboard map cursor, so this facade implements NO targeting of its own -- it only
    /// arms the mod's own tool exactly as the mouse button would.
    ///
    /// Gated by its own <see cref="PlacingReady"/>: <c>PlacingTool</c> is free-standing (no
    /// <c>EditorUI</c> dependency), so a rename here must not take down the sibling slices.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static readonly LazyReflectionGate placingGate = new LazyReflectionGate("CharEditorCompat placing", ResolvePlacing);

        private static Type placingToolType;
        private static MethodInfo placingBeginTeleportPawnMethod;
        private static MethodInfo placingBeginTeleportCustomPawnMethod;
        private static MethodInfo editorMoveRightMethod;
        private static FieldInfo placingRotationField;

        /// <summary>True when every member the teleport rows need resolved.</summary>
        public static bool PlacingReady
        {
            get
            {
                gate.Ensure();
                return placingGate.Ensure();
            }
        }

        private static bool ResolvePlacing(ReflectionSurface surface)
        {
            if (editorUIType == null || tabTypeType == null)
                return false;

            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            placingToolType = surface.Type("CharacterEditor.PlacingTool");
            placingBeginTeleportPawnMethod = surface.Method(placingToolType, "BeginTeleportPawn", new[] { typeof(Pawn) });
            placingBeginTeleportCustomPawnMethod = surface.Method(placingToolType, "BeginTeleportCustomPawn", Type.EmptyTypes);
            placingRotationField = surface.Field(placingToolType, "rotation");
            editorMoveRightMethod = surface.Method(ceditorType, "EditorMoveRight", Type.EmptyTypes);

            return surface.Ready;
        }

        /// <summary>
        /// Vehicle A: <c>CEditor.EditorMoveRight()</c>, the window reposition the editor's own
        /// teleport button performs before arming its tool -- it shifts the editor to the right
        /// half of the screen so the map underneath is clickable. Keeps a sighted viewer's
        /// picture of an armed placement identical to the mouse flow, which is why our teleport
        /// rows call it instead of closing the window.
        /// </summary>
        public static void MoveEditorAside()
        {
            if (!PlacingReady)
                return;
            try
            {
                editorMoveRightMethod.Invoke(Api(), null);
            }
            catch (Exception ex)
            {
                Fail("MoveEditorAside", ex);
            }
        }

        /// <summary>
        /// Vehicle A: <c>PlacingTool.BeginTeleportPawn(Pawn)</c> -- the SAME static the teleport
        /// button calls when Ctrl is not held. Arms a DebugTool whose click teleports
        /// <paramref name="p"/> to the keyboard map cursor.
        /// </summary>
        public static void ArmTeleport(Pawn p)
        {
            if (!PlacingReady || p == null)
                return;
            try
            {
                placingBeginTeleportPawnMethod.Invoke(null, new object[] { p });
            }
            catch (Exception ex)
            {
                Fail("ArmTeleport", ex);
            }
        }

        /// <summary>
        /// Vehicle A: <c>PlacingTool.BeginTeleportCustomPawn()</c> -- the SAME static the teleport
        /// button calls when Ctrl is held. Arms a DebugTool that picks whatever pawn is at the
        /// keyboard cursor on the next Enter, teleports it, then re-arms itself for the next pick
        /// (the mod's own chain, CharacterEditor.PlacingTool.TeleportPawnAndReselect) until the
        /// player cancels with Escape.
        /// </summary>
        public static void ArmTeleportAnyPawn()
        {
            if (!PlacingReady)
                return;
            try
            {
                placingBeginTeleportCustomPawnMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Fail("ArmTeleportAnyPawn", ex);
            }
        }

        /// <summary>
        /// <c>PlacingTool.rotation</c>, the placing-mode facing the Objects dialog's Rotate row
        /// cycles (North/East/South/West, the mod's own fixed order) -- read live for the rotate
        /// announcement.
        /// </summary>
        public static Rot4 CurrentPlacingRotation
        {
            get
            {
                if (!PlacingReady)
                    return Rot4.North;
                try
                {
                    return (Rot4)placingRotationField.GetValue(null);
                }
                catch (Exception ex)
                {
                    Fail("CurrentPlacingRotation", ex);
                    return Rot4.North;
                }
            }
        }
    }
}
