using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection facade over <c>CharacterEditor.DialogColorPicker</c>, backing
    /// <see cref="Shell.CharEditorColorScope"/>. Its own <see cref="Ready"/> flag keeps a rename in
    /// this one dialog from taking down the Appearance tree's rows.
    ///
    /// Opening constructs the dialog through the exact constructor every <c>AChange*UI</c> handler
    /// calls and adds it to the WindowStack, matching <c>WindowTool.Open</c>; a dedicated
    /// <see cref="Open"/> overload per mode spares callers the mod's five-parameter shape.
    ///
    /// EVERYTHING APPLIES LIVE: <c>TextValuesFromSelectedColor</c> is the dialog's sole
    /// apply/commit function, called from its own draw pass whenever <c>selectedColor</c> drifts.
    /// <see cref="ApplyColor"/> invokes it directly rather than waiting for that pass, so an
    /// announcement always describes a color the target has already received.
    ///
    /// MAX-BRIGHTNESS GLOBAL SIDE EFFECT: <c>DrawColorSlider</c> also retints the shared 52-swatch
    /// palette inline in the DRAW call, not in any setter, so <see cref="SetMaxBrightness"/> must
    /// reproduce it — bypassing the draw loop means the mod's own inline code never runs.
    /// </summary>
    internal static class CharEditorColorPickerCompat
    {
        private static bool initialized;
        private static bool ready;

        private static Type dialogType;
        private static Type colorTypeEnumType;
        private static Type colorToolType;
        private static Type geneToolType;
        private static ConstructorInfo ctor;

        private static FieldInfo colorTypeField;
        private static FieldInfo isColor1ChoosenField;
        private static FieldInfo selectedColorField;
        private static FieldInfo fMinBrightField;
        private static FieldInfo fMaxBrightField;
        private static FieldInfo selectedGeneDefField;
        private static FieldInfo tempPawnField;
        private static MethodInfo textValuesFromSelectedColorMethod;
        private static MethodInfo aApparelSelectedMethod;
        private static MethodInfo aWeaponSelectedMethod;
        private static FieldInfo closestGeneDefField;
        private static FieldInfo closestGeneColrField;
        private static MethodInfo aColorSelectedByGeneMethod;
        private static MethodInfo aGeneSelectedByColorMethod;

        private static FieldInfo colorToolOffsetCxField;
        private static FieldInfo colorToolLcolorsField;
        private static FieldInfo colorToolFMaxField;
        private static FieldInfo colorToolIMaxField;
        private static MemberInfo colorToolListOfColorsMember; // a field in some mod versions, a property in others
        private static MethodInfo colorToolGetDerivedColorMethod;

        private static MethodInfo geneToolGetHairGenes;
        private static MethodInfo geneToolGetSkinGenes;

        public static bool ModPresent => CharEditorCompat.ModPresent;

        public static bool Ready
        {
            get
            {
                EnsureInit();
                return ready;
            }
        }

        public static Type DialogType
        {
            get { EnsureInit(); return dialogType; }
        }

        private static void EnsureInit()
        {
            if (initialized)
                return;
            initialized = true;

            if (!ModPresent)
                return;

            var surface = new ReflectionSurface("CharEditorColorPickerCompat");

            dialogType = surface.Type("CharacterEditor.DialogColorPicker");
            colorTypeEnumType = surface.Type("CharacterEditor.ColorType");
            colorToolType = surface.Type("CharacterEditor.ColorTool");
            geneToolType = surface.Type("CharacterEditor.GeneTool");

            // The signature names a mod-internal enum, so it resolves only after that type does.
            ctor = colorTypeEnumType == null ? null
                : surface.Constructor(dialogType, new[] { colorTypeEnumType, typeof(bool), typeof(Apparel), typeof(ThingWithComps), typeof(GeneDef) });
            colorTypeField = surface.Field(dialogType, "colorType");
            isColor1ChoosenField = surface.Field(dialogType, "isColor1Choosen");
            selectedColorField = surface.Field(dialogType, "selectedColor");
            fMinBrightField = surface.Field(dialogType, "fMinBright");
            fMaxBrightField = surface.Field(dialogType, "fMaxBright");
            selectedGeneDefField = surface.Field(dialogType, "selectedGeneDef");
            tempPawnField = surface.Field(dialogType, "tempPawn");
            textValuesFromSelectedColorMethod = surface.Method(dialogType, "TextValuesFromSelectedColor", Type.EmptyTypes);
            // Both handlers take the DEF, not the instance: they re-find the worn/equipped thing
            // from tempPawn themselves.
            aApparelSelectedMethod = surface.Method(dialogType, "AApparelSelected", new[] { typeof(ThingDef) });
            aWeaponSelectedMethod = surface.Method(dialogType, "AWeaponSelected", new[] { typeof(ThingDef) });
            closestGeneDefField = surface.Field(dialogType, "closestGeneDef");
            closestGeneColrField = surface.Field(dialogType, "closestGeneColr");
            aColorSelectedByGeneMethod = surface.Method(dialogType, "AColorSelectedByGene", new[] { typeof(Color), typeof(Gene) });
            aGeneSelectedByColorMethod = surface.Method(dialogType, "AGeneSelectedByColor", new[] { typeof(Color), typeof(GeneDef) });

            colorToolOffsetCxField = surface.Field(colorToolType, "offsetCX");
            colorToolLcolorsField = surface.Field(colorToolType, "lcolors");
            colorToolFMaxField = surface.Field(colorToolType, "FMAX");
            colorToolIMaxField = surface.Field(colorToolType, "IMAX");
            colorToolGetDerivedColorMethod = surface.Method(colorToolType, "GetDerivedColor", new[] { typeof(Color), typeof(float) });
            colorToolListOfColorsMember = surface.FieldOrProperty(colorToolType, "ListOfColors");

            geneToolGetHairGenes = surface.Method(geneToolType, "GetHairGenes", new[] { typeof(Pawn) });
            geneToolGetSkinGenes = surface.Method(geneToolType, "GetSkinGenes", new[] { typeof(Pawn) });

            ready = surface.Ready;
        }

        private static readonly System.Collections.Generic.HashSet<string> loggedFailures = new System.Collections.Generic.HashSet<string>();

        private static void Fail(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
                ModLogger.Error("CharEditorColorPickerCompat." + member + " failed: " + ex.Message);
        }

        /// <summary>The nine modes the dialog serves; the Appearance tree reaches five of them.</summary>
        public enum Mode { HairColor, ApparelColor, WeaponColor, SkinColor, FavColor, EyeColor, GeneColorHair, GeneColorSkinBase, GeneColorSkinOverride }

        private static object ModeValue(Mode mode)
        {
            return colorTypeEnumType != null && Enum.IsDefined(colorTypeEnumType, mode.ToString())
                ? Enum.Parse(colorTypeEnumType, mode.ToString())
                : null;
        }

        /// <summary>Opens the dialog in the given mode/channel, exactly as the row's own label click does.</summary>
        public static Window Open(Mode mode, bool primary, Apparel apparel = null, ThingWithComps weapon = null)
        {
            if (!Ready)
                return null;
            object modeValue = ModeValue(mode);
            if (modeValue == null)
                return null;
            try
            {
                var window = (Window)ctor.Invoke(new object[] { modeValue, primary, apparel, weapon, null });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
                return window;
            }
            catch (Exception ex)
            {
                Fail("Open", ex);
                return null;
            }
        }

        /// <summary>
        /// Opens the dialog in one of the three GeneColor* modes, targeting a GENE DEF's own color
        /// field rather than a pawn's live color — the def-editor pane's own <c>DrawColors</c>
        /// call, through the same constructor with the gene in the fifth slot.
        /// </summary>
        public static Window Open(Mode mode, GeneDef gene)
        {
            if (!Ready || gene == null)
                return null;
            object modeValue = ModeValue(mode);
            if (modeValue == null)
                return null;
            try
            {
                var window = (Window)ctor.Invoke(new object[] { modeValue, true, null, null, gene });
                window.layer = WindowLayer.Dialog;
                Find.WindowStack.Add(window);
                return window;
            }
            catch (Exception ex)
            {
                Fail("Open(gene)", ex);
                return null;
            }
        }

        /// <summary>The mode this open dialog serves; a window can be re-targeted to a new pawn but never changes mode.</summary>
        public static Mode GetMode(Window dlg)
        {
            if (!Ready || dlg == null)
                return Mode.HairColor;
            try
            {
                object value = colorTypeField.GetValue(dlg);
                return value != null && Enum.TryParse(value.ToString(), out Mode m) ? m : Mode.HairColor;
            }
            catch (Exception ex)
            {
                Fail("GetMode", ex);
                return Mode.HairColor;
            }
        }

        /// <summary>The channel flag DrawRadioButtons and TextValuesFromSelectedColor both read — distinct from the constructor's isPrimaryColor copy.</summary>
        public static bool GetChannelIsFirst(Window dlg)
        {
            return GetBool(isColor1ChoosenField, dlg, "GetChannelIsFirst");
        }

        /// <summary>
        /// MUTATION-C: mirrors DrawRadioButtons' own click branch -- a bare field
        /// write, since the mod's own radio buttons write it the same way with no gated setter.
        /// Callers must re-seed selectedColor from the newly active channel afterward
        /// (<see cref="Shell.CharEditorColorScope.ActivateChannelRow"/> does this immediately),
        /// matching DrawRadioButtons' own branch, which does both in one click.
        /// </summary>
        public static void SetChannelIsFirst(Window dlg, bool value)
        {
            SetBool(isColor1ChoosenField, dlg, value, "SetChannelIsFirst");
        }

        public static Color GetSelectedColor(Window dlg)
        {
            if (!Ready || dlg == null)
                return Color.white;
            try
            {
                return (Color)selectedColorField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("GetSelectedColor", ex);
                return Color.white;
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors the dialog's own slider/swatch handlers -- writes
        /// selectedColor, the sole write path, then invokes TextValuesFromSelectedColor (vehicle A)
        /// so the pawn/apparel/weapon/GeneDef target and the portrait update immediately instead of
        /// waiting for the window's next natural draw pass.
        /// </summary>
        public static void ApplyColor(Window dlg, Color color)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                selectedColorField.SetValue(dlg, color);
                textValuesFromSelectedColorMethod.Invoke(dlg, null);
            }
            catch (Exception ex)
            {
                Fail("ApplyColor", ex);
            }
        }

        public static float GetMinBrightness(Window dlg) => GetFloat(fMinBrightField, dlg, "GetMinBrightness");

        /// <summary>MUTATION-C: mirrors DrawColorSlider's own bare write to fMinBright -- unlike fMaxBright this bound carries no further side effect, so no palette resync is needed here.</summary>
        public static void SetMinBrightness(Window dlg, float value)
        {
            SetFloat(fMinBrightField, dlg, Mathf.Clamp01(value), "SetMinBrightness");
        }

        public static float GetMaxBrightness(Window dlg) => GetFloat(fMaxBrightField, dlg, "GetMaxBrightness");

        /// <summary>
        /// MUTATION-C: reproduces DrawColorSlider's own inline body -- writing
        /// fMaxBright alone does not retint the shared palette, since that only happens as a side
        /// effect of the draw call this facade bypasses. Clamped so max never dips below min first,
        /// matching the mod's own `if (fMaxBright &lt; fMinBright) fMaxBright = fMinBright;` guard.
        /// </summary>
        public static void SetMaxBrightness(Window dlg, float value)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                float min = GetMinBrightness(dlg);
                float clamped = Mathf.Clamp(value, min, 1f);
                fMaxBrightField.SetValue(dlg, clamped);
                float currentOffset = (float)colorToolOffsetCxField.GetValue(null);
                float desiredOffset = 1f - clamped;
                if (!Mathf.Approximately(currentOffset, desiredOffset))
                {
                    colorToolOffsetCxField.SetValue(null, desiredOffset);
                    colorToolLcolorsField.SetValue(null, null);
                }
            }
            catch (Exception ex)
            {
                Fail("SetMaxBrightness", ex);
            }
        }

        public static Pawn GetTempPawn(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return tempPawnField.GetValue(dlg) as Pawn;
            }
            catch (Exception ex)
            {
                Fail("GetTempPawn", ex);
                return null;
            }
        }

        /// <summary>The 52 preset swatches, already tinted by the shared brightness offset.</summary>
        public static List<Color> Palette()
        {
            var result = new List<Color>();
            if (!Ready)
                return result;
            try
            {
                object value = ReflectionSurface.ValueOf(colorToolListOfColorsMember, null);
                if (value is System.Collections.IEnumerable seq)
                {
                    foreach (object item in seq)
                    {
                        if (item is Color c)
                            result.Add(c);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("Palette", ex);
            }
            return result;
        }

        /// <summary>The 14-step brightness-derived strip of the current selection: offsets from +0.15 down to -0.24.</summary>
        public static Color DerivedColor(Color baseColor, int index)
        {
            if (!Ready)
                return baseColor;
            try
            {
                float offset = 0.15f - 0.03f * index;
                return (Color)colorToolGetDerivedColorMethod.Invoke(null, new object[] { baseColor, offset });
            }
            catch (Exception ex)
            {
                Fail("DerivedColor", ex);
                return baseColor;
            }
        }

        /// <summary>Existing hair-or-skin genes as color swatches: HairColor mode reads hair genes, every other mode skin genes.</summary>
        public static List<Gene> ModeGenes(Window dlg, Pawn pawn)
        {
            var result = new List<Gene>();
            if (!Ready || dlg == null || pawn == null)
                return result;
            try
            {
                Mode mode = GetMode(dlg);
                MethodInfo method = mode == Mode.HairColor ? geneToolGetHairGenes : geneToolGetSkinGenes;
                if (method.Invoke(null, new object[] { pawn }) is System.Collections.IEnumerable seq)
                {
                    foreach (object item in seq)
                    {
                        if (item is Gene g)
                            result.Add(g);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("ModeGenes", ex);
            }
            return result;
        }

        /// <summary>The nearest hair-or-skin GeneDef to the current selection, or null when one already exists among the pawn's genes. Read-only: the dialog recomputes it on every apply.</summary>
        public static GeneDef ClosestGeneDef(Window dlg)
        {
            if (!Ready || dlg == null)
                return null;
            try
            {
                return closestGeneDefField.GetValue(dlg) as GeneDef;
            }
            catch (Exception ex)
            {
                Fail("ClosestGeneDef", ex);
                return null;
            }
        }

        public static Color ClosestGeneColor(Window dlg)
        {
            if (!Ready || dlg == null)
                return Color.white;
            try
            {
                return (Color)closestGeneColrField.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail("ClosestGeneColor", ex);
                return Color.white;
            }
        }

        /// <summary>
        /// Clicking a gene swatch is a REAL mutation, not a preview: it makes the gene the pawn's
        /// dominant hair-or-skin gene and adopts its color, through the dialog's own click handler.
        /// </summary>
        public static void AdoptGeneColor(Window dlg, Gene gene)
        {
            if (!Ready || dlg == null || gene == null)
                return;
            try
            {
                aColorSelectedByGeneMethod.Invoke(dlg, new object[] { gene.def.IconColor, gene });
            }
            catch (Exception ex)
            {
                Fail("AdoptGeneColor", ex);
            }
        }

        /// <summary>
        /// Removes the gene and promotes the next hair-or-skin gene to first. The mod wires this to
        /// the SAME click handler with Ctrl held, so the modifier is spoofed around the call.
        /// </summary>
        public static void RemoveGeneViaSwatch(Window dlg, Gene gene)
        {
            if (!Ready || dlg == null || gene == null)
                return;
            EventModifiers original = Event.current != null ? Event.current.modifiers : EventModifiers.None;
            try
            {
                if (Event.current != null)
                    KeyboardHelper.SetEventModifiers(Event.current, EventModifiers.Control);
                aColorSelectedByGeneMethod.Invoke(dlg, new object[] { gene.def.IconColor, gene });
            }
            catch (Exception ex)
            {
                Fail("RemoveGeneViaSwatch", ex);
            }
            finally
            {
                if (Event.current != null)
                    KeyboardHelper.SetEventModifiers(Event.current, original);
            }
        }

        /// <summary>Adds ClosestGeneDef as a brand-new endogene and makes it first.</summary>
        public static void AddClosestColorAsGene(Window dlg)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                GeneDef def = ClosestGeneDef(dlg);
                if (def == null)
                    return;
                aGeneSelectedByColorMethod.Invoke(dlg, new object[] { ClosestGeneColor(dlg), def });
            }
            catch (Exception ex)
            {
                Fail("AddClosestColorAsGene", ex);
            }
        }

        /// <summary>Switches the apparel-mode target within the same open dialog, via the mod's worn-list selection handler.</summary>
        public static void SelectApparel(Window dlg, Apparel apparel)
        {
            if (!Ready || dlg == null || apparel == null)
                return;
            try
            {
                aApparelSelectedMethod.Invoke(dlg, new object[] { apparel.def });
            }
            catch (Exception ex)
            {
                Fail("SelectApparel", ex);
            }
        }

        /// <summary>Switches the weapon-mode target within the same open dialog, via the mod's equipped-list selection handler.</summary>
        public static void SelectWeapon(Window dlg, ThingWithComps weapon)
        {
            if (!Ready || dlg == null || weapon == null)
                return;
            try
            {
                aWeaponSelectedMethod.Invoke(dlg, new object[] { weapon.def });
            }
            catch (Exception ex)
            {
                Fail("SelectWeapon", ex);
            }
        }

        /// <summary>The dialog's own ceiling for the R/G/B/A sliders, read live rather than hardcoded.</summary>
        public static float ColorMax
        {
            get
            {
                if (!Ready)
                    return 1f;
                try
                {
                    return (float)colorToolIMaxField.GetValue(null);
                }
                catch (Exception ex)
                {
                    Fail("ColorMax", ex);
                    return 1f;
                }
            }
        }

        /// <summary>The dialog's own ceiling for the min/max random-brightness sliders.</summary>
        public static float BrightnessMax
        {
            get
            {
                if (!Ready)
                    return 1f;
                try
                {
                    return (float)colorToolFMaxField.GetValue(null);
                }
                catch (Exception ex)
                {
                    Fail("BrightnessMax", ex);
                    return 1f;
                }
            }
        }

        private static bool GetBool(FieldInfo field, Window dlg, string caller)
        {
            if (!Ready || dlg == null)
                return false;
            try
            {
                return (bool)field.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
                return false;
            }
        }

        /// <summary>
        /// MUTATION-C: shared write primitive for the dialog's private bool fields -- each public
        /// wrapper above (SetChannelIsFirst) carries the per-field justification mirroring the
        /// dialog's own bare inline write; this helper adds no path of its own.
        /// </summary>
        private static void SetBool(FieldInfo field, Window dlg, bool value, string caller)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                field.SetValue(dlg, value);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        private static float GetFloat(FieldInfo field, Window dlg, string caller)
        {
            if (!Ready || dlg == null)
                return 0f;
            try
            {
                return (float)field.GetValue(dlg);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
                return 0f;
            }
        }

        /// <summary>
        /// MUTATION-C: shared write primitive for the dialog's private float fields -- each public
        /// wrapper above (SetMinBrightness/SetMaxBrightness) carries the per-field justification
        /// mirroring the dialog's own bare slider write; this helper adds no path of its own.
        /// </summary>
        private static void SetFloat(FieldInfo field, Window dlg, float value, string caller)
        {
            if (!Ready || dlg == null)
                return;
            try
            {
                field.SetValue(dlg, value);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }
    }
}
