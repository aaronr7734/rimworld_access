using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.DialogChangeHeadAddons</c> and the alien-race
    /// body-addon members it reads and writes on <c>CharacterEditor.AlienRaceTool</c>, backing
    /// <see cref="Shell.CharEditorHeadAddonsScope"/>. Alien-race-only: the dialog is only ever
    /// opened from the "bheadaddon" icon, itself gated on <c>isAlien</c>, so without a HAR race in
    /// the save this facade never gets a live pawn with addons and the scope's row list is empty.
    ///
    /// FOUR CONTROLS PER ADDON, three DIFFERENT apply mechanisms:
    /// <list type="bullet">
    /// <item>Draw size (<see cref="SetDrawSize"/>) and rotation (<see cref="SetRotation"/>) write
    /// straight through to the addon's own reflected field via <c>AlienRaceTool.BodyAddon_SetDrawSize</c>/
    /// <c>SetRotation</c> and call <c>CEditor.API.UpdateGraphics()</c> themselves, immediately, every
    /// time -- vehicle A, matching <c>AOnDrawSizeChanged</c>/<c>AOnRotationChanged</c> exactly.</item>
    /// <item>Male/female draw toggles (<see cref="ToggleDrawFor"/>) invoke
    /// <c>AlienPartGenerator_BodyAddon_Toggle_DrawFor</c> (also calling <c>UpdateGraphics()</c>
    /// itself) -- a pure gender-visibility FLIP, not a delete, despite the mod's own "Remove*"
    /// method names.</item>
    /// <item>The variant-index slider is the ONE control that is BATCHED: the dialog's own
    /// <c>DrawAddons</c> snapshots the whole list, lets each row's <c>Listing_X.AddIntSection</c>
    /// mutate the SAME list in place with a bare index write, then diffs old against new ONCE per
    /// frame and, only if anything changed anywhere, calls <c>AlienRaceComp_SetAddonVariants</c>
    /// (the whole list) plus ONE <c>UpdateGraphics()</c> -- there is no per-addon commit method to
    /// ride. <see cref="SetVariant"/> mirrors this exactly: write the local index into the SAME live
    /// list <c>AlienRaceComp_GetAddonVariants</c> returns, then immediately call
    /// <c>AlienRaceComp_SetAddonVariants</c> with that list and <c>UpdateGraphics()</c> -- committing
    /// on EVERY keyboard change rather than batching across a frame (this scope has no "many rows
    /// changing in one frame" concept the way a mouse-drag does), which is a strictly more
    /// responsive application of the SAME two-call commit, not a different one.</item>
    /// </list>
    /// </summary>
    internal static class CharEditorHeadAddonsCompat
    {
        private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorHeadAddonsCompat", Resolve);

        private static Type dialogType;
        private static Type alienRaceToolType;

        private static ConstructorInfo ctor;
        private static FieldInfo lAddonVariantsField;
        private static FieldInfo aAddonsField;

        private static MethodInfo bodyAddonGetPathMethod;
        private static MethodInfo bodyAddonGetVariantCountMaxMethod;
        private static MethodInfo bodyAddonGetDrawForFemaleMethod;
        private static MethodInfo bodyAddonGetDrawForMaleMethod;
        private static MethodInfo bodyAddonGetDrawSizeMethod;
        private static MethodInfo bodyAddonGetRotationMethod;
        private static MethodInfo bodyAddonSetDrawSizeMethod;
        private static MethodInfo bodyAddonSetRotationMethod;
        private static MethodInfo alienPartGeneratorGetBodyAddonsAsArrayMethod;
        private static MethodInfo alienRaceCompGetAddonVariantsMethod;
        private static MethodInfo alienRaceCompSetAddonVariantsMethod;
        private static MethodInfo alienPartGeneratorBodyAddonToggleDrawForMethod;

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

            dialogType = surface.Type("CharacterEditor.DialogChangeHeadAddons");
            alienRaceToolType = surface.Type("CharacterEditor.AlienRaceTool");

            ctor = surface.Constructor(dialogType, Type.EmptyTypes);
            lAddonVariantsField = surface.Field(dialogType, "lAddonVariants");
            aAddonsField = surface.Field(dialogType, "aAddons");

            bodyAddonGetPathMethod = surface.Method(alienRaceToolType, "BodyAddon_GetPath", new[] { typeof(object) });
            bodyAddonGetVariantCountMaxMethod = surface.Method(alienRaceToolType, "BodyAddon_GetVariantCountMax", new[] { typeof(object) });
            bodyAddonGetDrawForFemaleMethod = surface.Method(alienRaceToolType, "BodyAddon_GetDrawForFemale", new[] { typeof(object) });
            bodyAddonGetDrawForMaleMethod = surface.Method(alienRaceToolType, "BodyAddon_GetDrawForMale", new[] { typeof(object) });
            bodyAddonGetDrawSizeMethod = surface.Method(alienRaceToolType, "BodyAddon_GetDrawSize", new[] { typeof(object) });
            bodyAddonGetRotationMethod = surface.Method(alienRaceToolType, "BodyAddon_GetRotation", new[] { typeof(object) });
            bodyAddonSetDrawSizeMethod = surface.Method(alienRaceToolType, "BodyAddon_SetDrawSize", new[] { typeof(object), typeof(float) });
            bodyAddonSetRotationMethod = surface.Method(alienRaceToolType, "BodyAddon_SetRotation", new[] { typeof(object), typeof(float) });
            alienPartGeneratorGetBodyAddonsAsArrayMethod = surface.Method(alienRaceToolType, "AlienPartGenerator_GetBodyAddonsAsArray", new[] { typeof(Pawn) });
            alienRaceCompGetAddonVariantsMethod = surface.Method(alienRaceToolType, "AlienRaceComp_GetAddonVariants", new[] { typeof(Pawn) });
            alienRaceCompSetAddonVariantsMethod = surface.Method(alienRaceToolType, "AlienRaceComp_SetAddonVariants", new[] { typeof(Pawn), typeof(List<int>) });
            alienPartGeneratorBodyAddonToggleDrawForMethod = surface.Method(alienRaceToolType, "AlienPartGenerator_BodyAddon_Toggle_DrawFor", new[] { typeof(Pawn), typeof(int), typeof(bool) });

            return surface.Ready && CharEditorCompat.EditorCore.Ready;
        }

        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorHeadAddonsCompat." + member + " failed: " + ex.Message);
        }

        private static void UpdateGraphics()
        {
            CharEditorCompat.EditorCore.UpdateGraphics();
        }

        // ------------------------------------------------------------------
        // Opening. Vehicle A rides through CharEditorCompat.OpenHeadAddons (BlockPerson.AChangeHeadAddons);
        // this facade only reads/writes the resulting window.
        // ------------------------------------------------------------------

        public static int AddonCount(Window dlg)
        {
            if (!Ready || dlg == null)
                return 0;
            try
            {
                var list = lAddonVariantsField.GetValue(dlg) as List<int>;
                return list?.Count ?? 0;
            }
            catch (Exception ex)
            {
                Fail("AddonCount", ex);
                return 0;
            }
        }

        private static object[] Addons(Window dlg)
        {
            if (!Ready || dlg == null)
                return Array.Empty<object>();
            try
            {
                return aAddonsField.GetValue(dlg) as object[] ?? Array.Empty<object>();
            }
            catch (Exception ex)
            {
                Fail("Addons", ex);
                return Array.Empty<object>();
            }
        }

        private static object AddonAt(Window dlg, int index)
        {
            object[] addons = Addons(dlg);
            return index >= 0 && index < addons.Length ? addons[index] : null;
        }

        public static string AddonLabel(Window dlg, int index)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return "";
            try
            {
                string path = bodyAddonGetPathMethod.Invoke(null, new[] { addon }) as string ?? "";
                int slash = path.LastIndexOf('/');
                return slash >= 0 && slash + 1 < path.Length ? path.Substring(slash + 1) : path;
            }
            catch (Exception ex)
            {
                Fail("AddonLabel", ex);
                return "";
            }
        }

        /// <summary>The def-declared variantCountMax, or the dialog's own texture-probed fallback when that comes back 0.</summary>
        public static int VariantMax(Window dlg, int index)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return 0;
            try
            {
                int max = (int)bodyAddonGetVariantCountMaxMethod.Invoke(null, new[] { addon });
                return max;
            }
            catch (Exception ex)
            {
                Fail("VariantMax", ex);
                return 0;
            }
        }

        public static int Variant(Window dlg, int index)
        {
            if (!Ready || dlg == null)
                return 0;
            try
            {
                var list = lAddonVariantsField.GetValue(dlg) as List<int>;
                return list != null && index >= 0 && index < list.Count ? list[index] : 0;
            }
            catch (Exception ex)
            {
                Fail("Variant", ex);
                return 0;
            }
        }

        /// <summary>MUTATION-C: DrawAddons' own batched diff-then-commit-once (DialogChangeHeadAddons.cs:110-138), applied per keystroke rather than once per frame -- see class remarks.</summary>
        public static void SetVariant(Window dlg, Pawn pawn, int index, int value)
        {
            if (!Ready || dlg == null || pawn == null)
                return;
            try
            {
                var list = lAddonVariantsField.GetValue(dlg) as List<int>;
                if (list == null || index < 0 || index >= list.Count)
                    return;
                list[index] = value;
                alienRaceCompSetAddonVariantsMethod.Invoke(null, new object[] { pawn, list });
                UpdateGraphics();
            }
            catch (Exception ex)
            {
                Fail("SetVariant", ex);
            }
        }

        public static bool DrawForFemale(Window dlg, int index)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return false;
            try
            {
                return (bool)bodyAddonGetDrawForFemaleMethod.Invoke(null, new[] { addon });
            }
            catch (Exception ex)
            {
                Fail("DrawForFemale", ex);
                return false;
            }
        }

        public static bool DrawForMale(Window dlg, int index)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return false;
            try
            {
                return (bool)bodyAddonGetDrawForMaleMethod.Invoke(null, new[] { addon });
            }
            catch (Exception ex)
            {
                Fail("DrawForMale", ex);
                return false;
            }
        }

        /// <summary>Vehicle A: AlienPartGenerator_BodyAddon_Toggle_DrawFor -- a pure flip of the addon's own gender-visibility flag. Despite the mod's "Remove*" method names, the addon itself is never removed.</summary>
        public static void ToggleDrawFor(Pawn pawn, int index, bool female)
        {
            if (!Ready || pawn == null)
                return;
            try
            {
                alienPartGeneratorBodyAddonToggleDrawForMethod.Invoke(null, new object[] { pawn, index, female });
                UpdateGraphics();
            }
            catch (Exception ex)
            {
                Fail("ToggleDrawFor", ex);
            }
        }

        public static float DrawSize(Window dlg, int index)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return 1f;
            try
            {
                return ((Vector2)bodyAddonGetDrawSizeMethod.Invoke(null, new[] { addon })).x;
            }
            catch (Exception ex)
            {
                Fail("DrawSize", ex);
                return 1f;
            }
        }

        /// <summary>Vehicle A: BodyAddon_SetDrawSize + UpdateGraphics, immediate on every change, mirroring AOnDrawSizeChanged. Only X and Y together: the mod's own UI has no independent Y-scale control either.</summary>
        public static void SetDrawSize(Window dlg, int index, float value)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return;
            try
            {
                bodyAddonSetDrawSizeMethod.Invoke(null, new object[] { addon, value });
                UpdateGraphics();
            }
            catch (Exception ex)
            {
                Fail("SetDrawSize", ex);
            }
        }

        public static float Rotation(Window dlg, int index)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return 0f;
            try
            {
                return (float)bodyAddonGetRotationMethod.Invoke(null, new[] { addon });
            }
            catch (Exception ex)
            {
                Fail("Rotation", ex);
                return 0f;
            }
        }

        /// <summary>Vehicle A: BodyAddon_SetRotation + UpdateGraphics, immediate on every change, mirroring AOnRotationChanged.</summary>
        public static void SetRotation(Window dlg, int index, float value)
        {
            object addon = AddonAt(dlg, index);
            if (addon == null)
                return;
            try
            {
                bodyAddonSetRotationMethod.Invoke(null, new object[] { addon, value });
                UpdateGraphics();
            }
            catch (Exception ex)
            {
                Fail("SetRotation", ex);
            }
        }
    }
}
