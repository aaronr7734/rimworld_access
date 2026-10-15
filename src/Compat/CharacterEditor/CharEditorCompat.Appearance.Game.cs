using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The appearance surface's mutation vehicles: <c>BlockPerson</c>'s NavSelector handlers (hair,
    /// beard, tattoos, head, the six Facial Stuff controllers, body, apparel, weapons) plus the
    /// standalone <c>HairTool</c>/<c>StyleTool</c>/<c>HeadTool</c>/<c>SkinTool</c>/<c>FacialTool</c>
    /// extension classes those handlers call through to. Gated by its own
    /// <see cref="AppearanceReady"/> so an appearance-only rename cannot take down the sibling
    /// slices.
    ///
    /// Every write invokes the exact delegate the mod's own NavSelectorImageBox row passes. "Custom"
    /// pickers invoke the label-click handler, which opens a real vanilla <c>FloatMenu</c> over the
    /// mod's candidate list; those menus already ride
    /// <see cref="Shell.WindowlessFloatMenuState"/>, so <see cref="RunWithMenuRedirect"/> is the only
    /// extra step. Prev/next steppers invoke <c>ASetNext*</c>/<c>ASetPrev*</c> directly.
    ///
    /// Randomize handlers branch on <c>Event.current.alt/shift/control</c> to pick which channel to
    /// randomize, a modifier a click can carry but a keyboard Activate cannot, so
    /// <see cref="InvokeWithSimulatedModifiers"/> sets <c>Event.current.modifiers</c> to the
    /// combination the drill-in option represents, invokes, then restores.
    /// <c>AChangeApparelUI</c>/<c>AChangeWeaponUI</c> already contain the mod's make-colorable
    /// confirm flow, so riding them needs no colorable-only gate here.
    /// </summary>
    internal static partial class CharEditorCompat
    {
        private static bool appearanceInitialized;
        private static bool appearanceReady;

        private static Type hairToolType;
        private static Type styleToolType;
        private static Type headToolType;
        private static Type skinToolType;
        private static Type facialToolType;

        // BlockPerson: prev/next/random/custom, no-arg instance methods.
        private static MethodInfo mAChooseFaceTattooCustom, mARandomFaceTattoo;
        private static MethodInfo mAChooseBodyTattooCustom, mARandomBodyTattoo;
        private static MethodInfo mAChooseHeadCustom, mARandomHead;
        private static MethodInfo mAChooseBodyCustom, mARandomBody;
        private static MethodInfo mAChooseGradientCustom, mARandomGradient;
        private static MethodInfo mARandomHairColor;
        private static MethodInfo mARandomSkinColor;
        private static MethodInfo mARandomEyeColor, mARandomEyeColor2;
        private static FieldInfo fIsAlien;

        // BlockPerson: Apparel/Weapon, one-Thing-arg instance methods.
        private static MethodInfo mAChangeApparelUI, mARandomApparel, mASetNextApparel, mASetPrevApparel, mGetDrawOrder;
        private static MethodInfo mAChangeWeaponUI, mARandomWeapon;
        private static MethodInfo mAOnTextureApparel, mAOnTextureWeapon;

        // BlockPerson: head addons (alien-race only).
        private static MethodInfo mAChangeHeadAddons;

        // Static/extension tool methods.
        private static MethodInfo hairToolAChooseHairCustom;      // HairTool.AChooseHairCustom() [static]
        private static MethodInfo hairToolARandomHair;            // HairTool.ARandomHair() [static]
        private static MethodInfo hairToolGetHairColor;           // HairTool.GetHairColor(Pawn, bool)
        private static MethodInfo hairToolGetGradientMask;        // HairTool.GetGradientMask(Pawn)
        private static MethodInfo styleToolAChooseBeardCustom;    // StyleTool.AChooseBeardCustom() [static]
        private static MethodInfo styleToolARandomBeard;          // StyleTool.ARandomBeard() [static]
        private static MethodInfo headToolGetEyeColor;            // HeadTool.GetEyeColor(Pawn)
        private static MethodInfo headToolGetEyeColor2;           // HeadTool.GetEyeColor2(Pawn)
        private static MethodInfo skinToolGetSkinColor;           // SkinTool.GetSkinColor(Pawn, bool)
        private static MethodInfo facialToolGetCurrentDef;        // FacialTool.FA_GetCurrentDef(Pawn, string)
        private static MethodInfo facialToolGetCurrentDefName;    // FacialTool.FA_GetCurrentDefName(Pawn, string)

        private static MemberInfo isGradientHairActiveMember;      // static; some mod versions declare it a field, others a property

        /// <summary>The six Facial Stuff controllers, keyed by BlockPerson method-name suffix and FacialTool controller-comp name.</summary>
        private static readonly string[] FacialKeys = { "Head", "Eye", "Lid", "Brow", "Mouth", "Skin" };
        private static readonly Dictionary<string, string> FacialControllerCompNames = new Dictionary<string, string>
        {
            { "Head", "HeadControllerComp" },
            { "Eye", "EyeballControllerComp" },
            { "Lid", "LidControllerComp" },
            { "Brow", "BrowControllerComp" },
            { "Mouth", "MouthControllerComp" },
            { "Skin", "SkinControllerComp" },
        };
        private static readonly Dictionary<string, MethodInfo> facialCustom = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, MethodInfo> facialRandom = new Dictionary<string, MethodInfo>();

        /// <summary>True when every member the Appearance section's Hair/Head/Torso/Apparel/Weapons rows need resolved.</summary>
        public static bool AppearanceReady
        {
            get
            {
                EnsureInit();
                return appearanceReady;
            }
        }

        /// <summary>Called from the main <c>EnsureInit</c> once EditorUI and BlockPerson are known.</summary>
        private static void BindAppearance()
        {
            if (appearanceInitialized)
                return;
            appearanceInitialized = true;

            var surface = new ReflectionSurface("CharEditorCompat appearance");
            surface.Supplied("EditorUI.BlockPerson", blockPersonType);
            surface.Supplied("CharacterEditor.CEditor", ceditorType);

            hairToolType = surface.Type("CharacterEditor.HairTool");
            styleToolType = surface.Type("CharacterEditor.StyleTool");
            headToolType = surface.Type("CharacterEditor.HeadTool");
            skinToolType = surface.Type("CharacterEditor.SkinTool");
            facialToolType = surface.Type("CharacterEditor.FacialTool");

            mAChooseFaceTattooCustom = surface.Method(blockPersonType, "AChooseFaceTattooCustom", Type.EmptyTypes);
            mARandomFaceTattoo = surface.Method(blockPersonType, "ARandomFaceTattoo", Type.EmptyTypes);
            mAChooseBodyTattooCustom = surface.Method(blockPersonType, "AChooseBodyTattooCustom", Type.EmptyTypes);
            mARandomBodyTattoo = surface.Method(blockPersonType, "ARandomBodyTattoo", Type.EmptyTypes);
            mAChooseHeadCustom = surface.Method(blockPersonType, "AChooseHeadCustom", Type.EmptyTypes);
            mARandomHead = surface.Method(blockPersonType, "ARandomHead", Type.EmptyTypes);
            mAChooseBodyCustom = surface.Method(blockPersonType, "AChooseBodyCustom", Type.EmptyTypes);
            mARandomBody = surface.Method(blockPersonType, "ARandomBody", Type.EmptyTypes);
            mAChooseGradientCustom = surface.Method(blockPersonType, "AChooseGradientCustom", Type.EmptyTypes);
            mARandomGradient = surface.Method(blockPersonType, "ARandomGradient", Type.EmptyTypes);
            mARandomHairColor = surface.Method(blockPersonType, "ARandomHairColor", Type.EmptyTypes);
            mARandomSkinColor = surface.Method(blockPersonType, "ARandomSkinColor", Type.EmptyTypes);
            mARandomEyeColor = surface.Method(blockPersonType, "ARandomEyeColor", Type.EmptyTypes);
            mARandomEyeColor2 = surface.Method(blockPersonType, "ARandomEyeColor2", Type.EmptyTypes);
            fIsAlien = surface.Field(blockPersonType, "isAlien");

            mAChangeApparelUI = surface.Method(blockPersonType, "AChangeApparelUI", new[] { typeof(Apparel) });
            mARandomApparel = surface.Method(blockPersonType, "ARandomApparel", new[] { typeof(Apparel) });
            mASetNextApparel = surface.Method(blockPersonType, "ASetNextApparel", new[] { typeof(Apparel) });
            mASetPrevApparel = surface.Method(blockPersonType, "ASetPrevApparel", new[] { typeof(Apparel) });
            mGetDrawOrder = surface.Method(blockPersonType, "GetDrawOrder", new[] { typeof(Apparel) });

            mAChangeWeaponUI = surface.Method(blockPersonType, "AChangeWeaponUI", new[] { typeof(ThingWithComps) });
            mARandomWeapon = surface.Method(blockPersonType, "ARandomWeapon", new[] { typeof(ThingWithComps) });
            mAOnTextureApparel = surface.Method(blockPersonType, "AOnTextureApparel", new[] { typeof(Apparel) });
            mAOnTextureWeapon = surface.Method(blockPersonType, "AOnTextureWeapon", new[] { typeof(ThingWithComps) });
            mAChangeHeadAddons = surface.Method(blockPersonType, "AChangeHeadAddons", Type.EmptyTypes);

            hairToolAChooseHairCustom = surface.Method(hairToolType, "AChooseHairCustom", Type.EmptyTypes);
            hairToolARandomHair = surface.Method(hairToolType, "ARandomHair", Type.EmptyTypes);
            hairToolGetHairColor = surface.Method(hairToolType, "GetHairColor", new[] { typeof(Pawn), typeof(bool) });
            hairToolGetGradientMask = surface.Method(hairToolType, "GetGradientMask", new[] { typeof(Pawn) });

            styleToolAChooseBeardCustom = surface.Method(styleToolType, "AChooseBeardCustom", Type.EmptyTypes);
            styleToolARandomBeard = surface.Method(styleToolType, "ARandomBeard", Type.EmptyTypes);

            headToolGetEyeColor = surface.Method(headToolType, "GetEyeColor", new[] { typeof(Pawn) });
            headToolGetEyeColor2 = surface.Method(headToolType, "GetEyeColor2", new[] { typeof(Pawn) });

            skinToolGetSkinColor = surface.Method(skinToolType, "GetSkinColor", new[] { typeof(Pawn), typeof(bool) });

            facialToolGetCurrentDef = surface.Method(facialToolType, "FA_GetCurrentDef", new[] { typeof(Pawn), typeof(string) });
            facialToolGetCurrentDefName = surface.Method(facialToolType, "FA_GetCurrentDefName", new[] { typeof(Pawn), typeof(string) });

            foreach (string key in FacialKeys)
            {
                facialCustom[key] = surface.Method(blockPersonType, "AFACustom" + key, Type.EmptyTypes);
                facialRandom[key] = surface.Method(blockPersonType, "AFARandom" + key, Type.EmptyTypes);
            }

            isGradientHairActiveMember = surface.FieldOrProperty(ceditorType, "IsGradientHairActive");

            appearanceReady = surface.Ready;
        }

        // Gates.

        /// <summary>Gradient Hair active: gates the Gradient row and the hair color B row, never alien-race gated.</summary>
        public static bool GradientHairActive
        {
            get
            {
                if (!AppearanceReady)
                    return false;
                try
                {
                    return (bool)ReflectionSurface.ValueOf(isGradientHairActiveMember, null);
                }
                catch (Exception ex)
                {
                    Fail("GradientHairActive", ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// Whether the pawn's Facial Stuff HeadControllerComp is present: the mod's single gate for
        /// the six facial controller rows and for eye color 1/2, drawn inside the same branch.
        /// </summary>
        public static bool HasFacialHeadController(Pawn pawn)
        {
            if (!AppearanceReady || pawn == null)
                return false;
            try
            {
                object result = facialToolGetCurrentDef.Invoke(null, new object[] { pawn, "HeadControllerComp" });
                return result != null;
            }
            catch (Exception ex)
            {
                Fail("HasFacialHeadController", ex);
                return false;
            }
        }

        /// <summary>
        /// BlockPerson's own cached alien-race flag for the edited pawn, read rather than
        /// recomputed. Gates the HAR dual-channel skin color rows.
        /// </summary>
        public static bool IsAlienRace(Window editorUI)
        {
            if (!AppearanceReady)
                return false;
            try
            {
                object block = Block(editorUI, getBlockPerson, "BlockPerson");
                if (block == null)
                    return false;
                return (bool)fIsAlien.GetValue(block);
            }
            catch (Exception ex)
            {
                Fail("IsAlienRace", ex);
                return false;
            }
        }

        // Simulated-modifier invocation (see class remarks).

        private static void InvokeWithSimulatedModifiers(MethodInfo method, object instance, object[] args, EventModifiers modifiers, string caller)
        {
            if (!AppearanceReady || method == null)
                return;
            Event evt = Event.current;
            EventModifiers original = evt != null ? evt.modifiers : EventModifiers.None;
            try
            {
                if (evt != null)
                    KeyboardHelper.SetEventModifiers(evt, modifiers);
                method.Invoke(instance, args);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
            finally
            {
                if (evt != null)
                    KeyboardHelper.SetEventModifiers(evt, original);
            }
        }

        private static object BlockPersonInstance(Window editorUI)
        {
            return Block(editorUI, getBlockPerson, "BlockPerson");
        }

        private static void InvokeOnBlockPersonNoArg(Window editorUI, MethodInfo method, string caller)
        {
            if (!AppearanceReady || method == null)
                return;
            try
            {
                object block = BlockPersonInstance(editorUI);
                if (block != null)
                    method.Invoke(block, null);
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        private static void InvokeOnBlockPersonWithArg(Window editorUI, MethodInfo method, object arg, string caller)
        {
            if (!AppearanceReady || method == null || arg == null)
                return;
            try
            {
                object block = BlockPersonInstance(editorUI);
                if (block != null)
                    method.Invoke(block, new[] { arg });
            }
            catch (Exception ex)
            {
                Fail(caller, ex);
            }
        }

        // Hair.

        /// <summary>Opens the mod's own hair-style FloatMenu; static, so no BlockPerson instance is needed.</summary>
        public static void OpenHairPicker(Window editorUI)
        {
            if (!AppearanceReady)
                return;
            try
            {
                hairToolAChooseHairCustom.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Fail("OpenHairPicker", ex);
            }
        }

        /// <summary>
        /// HairTool.ARandomHair's Alt/Ctrl branch: plain randomizes the hair def, Alt also
        /// randomizes both color channels opaque, Ctrl with Alt does the same with random alpha.
        /// <paramref name="alsoColor"/> and <paramref name="alpha"/> promote those to drill-in
        /// options.
        /// </summary>
        public static void RandomizeHair(Window editorUI, bool alsoColor, bool alpha)
        {
            if (!AppearanceReady)
                return;
            EventModifiers mods = alsoColor ? (alpha ? (EventModifiers.Alt | EventModifiers.Control) : EventModifiers.Alt) : EventModifiers.None;
            InvokeWithSimulatedModifiers(hairToolARandomHair, null, null, mods, "RandomizeHair");
        }

        // Beard.

        public static void OpenBeardPicker(Window editorUI)
        {
            if (!AppearanceReady)
                return;
            try
            {
                styleToolAChooseBeardCustom.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Fail("OpenBeardPicker", ex);
            }
        }

        /// <summary>StyleTool.ARandomBeard has no modifier branch: beard has no separate color channel.</summary>
        public static void RandomizeBeard(Window editorUI)
        {
            if (!AppearanceReady)
                return;
            try
            {
                styleToolARandomBeard.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Fail("RandomizeBeard", ex);
            }
        }

        // Hair color (the flattened A/B rows).

        /// <summary>Channel A is vanilla's story.HairColor; channel B is the Gradient Hair color when active, else white.</summary>
        public static Color GetHairColor(Pawn pawn, bool primary)
        {
            if (!AppearanceReady || pawn == null)
                return Color.white;
            try
            {
                return (Color)hairToolGetHairColor.Invoke(null, new object[] { pawn, primary });
            }
            catch (Exception ex)
            {
                Fail("GetHairColor", ex);
                return Color.white;
            }
        }

        /// <summary>
        /// BlockPerson.ARandomHairColor's convention: Alt is channel A, Shift channel B, and Ctrl
        /// combines to mean random-with-alpha. <paramref name="primary"/> names the asking row, so
        /// only that channel's half of the mod's branch fires.
        /// </summary>
        public static void RandomizeHairColorChannel(Window editorUI, bool primary, bool alpha)
        {
            if (!AppearanceReady)
                return;
            EventModifiers mods = primary ? EventModifiers.Alt : EventModifiers.Shift;
            if (alpha)
                mods |= EventModifiers.Control;
            object block = BlockPersonInstance(editorUI);
            InvokeWithSimulatedModifiers(mARandomHairColor, block, null, mods, "RandomizeHairColorChannel");
        }

        // Gradient mask (Gradient Hair only).

        /// <summary>The row's raw texture path, tailed to its file name: the bare path is unspeakable.</summary>
        public static string GradientMaskLabel(Pawn pawn)
        {
            if (!AppearanceReady || pawn == null)
                return "";
            try
            {
                string path = hairToolGetGradientMask.Invoke(null, new object[] { pawn }) as string ?? "";
                int slash = path.LastIndexOf('/');
                return slash >= 0 && slash + 1 < path.Length ? path.Substring(slash + 1) : path;
            }
            catch (Exception ex)
            {
                Fail("GradientMaskLabel", ex);
                return "";
            }
        }

        public static void OpenGradientPicker(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mAChooseGradientCustom, "OpenGradientPicker");
        }

        /// <summary>SetGradientMask and RandomizeGradientMask call UpdateGraphics() internally.</summary>
        public static void RandomizeGradient(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mARandomGradient, "RandomizeGradient");
        }

        // Face tattoo / body tattoo.

        public static void OpenFaceTattooPicker(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mAChooseFaceTattooCustom, "OpenFaceTattooPicker");
        }

        public static void RandomizeFaceTattoo(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mARandomFaceTattoo, "RandomizeFaceTattoo");
        }

        public static void OpenBodyTattooPicker(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mAChooseBodyTattooCustom, "OpenBodyTattooPicker");
        }

        public static void RandomizeBodyTattoo(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mARandomBodyTattoo, "RandomizeBodyTattoo");
        }

        // Head (the vanilla HeadTypeDef path, used only when Facial Stuff's comp is absent).

        public static void OpenHeadPicker(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mAChooseHeadCustom, "OpenHeadPicker");
        }

        public static void RandomizeHead(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mARandomHead, "RandomizeHead");
        }

        // Facial Stuff's six controllers (head, eye, lid, brow, mouth, skin morph).

        /// <summary>The controller's current def name, as the row's own label reads it.</summary>
        public static string FacialCurrentDefName(Pawn pawn, string key)
        {
            if (!AppearanceReady || pawn == null || !FacialControllerCompNames.TryGetValue(key, out string compName))
                return "";
            try
            {
                return facialToolGetCurrentDefName.Invoke(null, new object[] { pawn, compName }) as string ?? "";
            }
            catch (Exception ex)
            {
                Fail("FacialCurrentDefName:" + key, ex);
                return "";
            }
        }

        public static void OpenFacialPicker(Window editorUI, string key)
        {
            if (facialCustom.TryGetValue(key, out MethodInfo m))
                InvokeOnBlockPersonNoArg(editorUI, m, "OpenFacialPicker:" + key);
        }

        public static void RandomizeFacial(Window editorUI, string key)
        {
            if (facialRandom.TryGetValue(key, out MethodInfo m))
                InvokeOnBlockPersonNoArg(editorUI, m, "RandomizeFacial:" + key);
        }

        // Eye color 1/2, reachable only when HasFacialHeadController is true.

        public static Color GetEyeColor(Pawn pawn, bool primary)
        {
            if (!AppearanceReady || pawn == null)
                return Color.white;
            try
            {
                MethodInfo m = primary ? headToolGetEyeColor : headToolGetEyeColor2;
                return (Color)m.Invoke(null, new object[] { pawn });
            }
            catch (Exception ex)
            {
                Fail("GetEyeColor", ex);
                return Color.white;
            }
        }

        /// <summary>Plain full-random; these handlers have no modifier branch.</summary>
        public static void RandomizeEyeColor(Window editorUI, bool primary)
        {
            InvokeOnBlockPersonNoArg(editorUI, primary ? mARandomEyeColor : mARandomEyeColor2, "RandomizeEyeColor");
        }

        // Body.

        public static void OpenBodyPicker(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mAChooseBodyCustom, "OpenBodyPicker");
        }

        public static void RandomizeBody(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mARandomBody, "RandomizeBody");
        }

        // Skin color: one row for non-alien pawns, two for HAR aliens.

        public static Color GetSkinColor(Pawn pawn, bool primary)
        {
            if (!AppearanceReady || pawn == null)
                return Color.white;
            try
            {
                return (Color)skinToolGetSkinColor.Invoke(null, new object[] { pawn, primary });
            }
            catch (Exception ex)
            {
                Fail("GetSkinColor", ex);
                return Color.white;
            }
        }

        /// <summary>
        /// BlockPerson.ARandomSkinColor's convention: Alt is channel A (story.SkinColorBase, plus the
        /// HAR channel when alien), Shift is channel B (alien only), and Ctrl combines to mean
        /// random-with-alpha for the HAR channel. The non-alien single row passes no modifiers, the
        /// mod's default branch and the only one that does anything for a non-alien pawn.
        /// </summary>
        public static void RandomizeSkinColorChannel(Window editorUI, bool? primary, bool alpha)
        {
            if (!AppearanceReady)
                return;
            EventModifiers mods = primary == null
                ? EventModifiers.None
                : (primary.Value ? EventModifiers.Alt : EventModifiers.Shift);
            if (alpha)
                mods |= EventModifiers.Control;
            object block = BlockPersonInstance(editorUI);
            InvokeWithSimulatedModifiers(mARandomSkinColor, block, null, mods, "RandomizeSkinColorChannel");
        }

        // Apparel.

        /// <summary>The pawn's worn apparel in the mod's own draw order.</summary>
        public static List<Apparel> OrderedWornApparel(Window editorUI, Pawn pawn)
        {
            var result = new List<Apparel>();
            if (!AppearanceReady || pawn?.apparel == null)
                return result;
            try
            {
                object block = BlockPersonInstance(editorUI);
                if (block == null)
                    return result;
                result.AddRange(pawn.apparel.WornApparel);
                result = result.OrderByDescending(a =>
                {
                    try
                    {
                        return (int)mGetDrawOrder.Invoke(block, new object[] { a });
                    }
                    catch
                    {
                        return 0;
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                Fail("OrderedWornApparel", ex);
            }
            return result;
        }

        /// <summary>
        /// AChangeApparelUI opens DialogColorPicker directly for a colorable item and is itself the
        /// make-colorable-then-open flow for an uncolorable one, so no gate is needed here.
        /// </summary>
        public static void OpenApparelColorDialog(Window editorUI, Apparel apparel)
        {
            InvokeOnBlockPersonWithArg(editorUI, mAChangeApparelUI, apparel, "OpenApparelColorDialog");
        }

        /// <summary>
        /// The three modifier variants <c>WearSelectedApparel</c> reads, as drill-in options: Alt
        /// keeps the outgoing item's color, Shift a random opaque color, Control random alpha too.
        /// </summary>
        public enum ApparelColorVariant { KeepColor, RandomColor, RandomAlpha }

        public static void StepApparelWithColorVariant(Window editorUI, Apparel apparel, bool next, ApparelColorVariant variant)
        {
            if (!AppearanceReady || apparel == null)
                return;
            EventModifiers mods = variant == ApparelColorVariant.KeepColor ? EventModifiers.Alt
                : variant == ApparelColorVariant.RandomColor ? EventModifiers.Shift
                : EventModifiers.Control;
            object block = BlockPersonInstance(editorUI);
            InvokeWithSimulatedModifiers(next ? mASetNextApparel : mASetPrevApparel, block,
                new object[] { apparel }, mods, "StepApparelWithColorVariant");
        }

        /// <summary>Creation-mode dice: a plain random item swap with default coloring.</summary>
        public static void RandomizeApparel(Window editorUI, Apparel apparel)
        {
            if (!AppearanceReady || apparel == null)
                return;
            object block = BlockPersonInstance(editorUI);
            InvokeWithSimulatedModifiers(mARandomApparel, block, new object[] { apparel }, EventModifiers.None, "RandomizeApparel");
        }

        // Weapons: no channel, no modifier variants.

        /// <summary>
        /// AChangeWeaponUI is the make-colorable prompt path for an uncolorable weapon and opens
        /// DialogColorPicker directly for a colorable one; riding it keeps the eager every-frame
        /// config-panel path out of reach.
        /// </summary>
        public static void OpenWeaponColorDialog(Window editorUI, ThingWithComps weapon)
        {
            InvokeOnBlockPersonWithArg(editorUI, mAChangeWeaponUI, weapon, "OpenWeaponColorDialog");
        }

        public static void RandomizeWeapon(Window editorUI, ThingWithComps weapon)
        {
            InvokeOnBlockPersonWithArg(editorUI, mARandomWeapon, weapon, "RandomizeWeapon");
        }

        // Replace flow: the row's own texture-button opener, which pre-loads DialogObjects in the
        // matching mode for the clicked item. The resulting window attaches CharEditorBrowserScope
        // through the exact-type registration in CharEditorDialogCompat.

        public static void OpenReplaceApparelDialog(Window editorUI, Apparel apparel)
        {
            InvokeOnBlockPersonWithArg(editorUI, mAOnTextureApparel, apparel, "OpenReplaceApparelDialog");
        }

        public static void OpenReplaceWeaponDialog(Window editorUI, ThingWithComps weapon)
        {
            InvokeOnBlockPersonWithArg(editorUI, mAOnTextureWeapon, weapon, "OpenReplaceWeaponDialog");
        }

        // Head addons, alien-race only: gated on IsAlienRace, the same flag DrawMainIcons gates the
        // "bheadaddon" icon on, which is the only way in.

        public static void OpenHeadAddons(Window editorUI)
        {
            InvokeOnBlockPersonNoArg(editorUI, mAChangeHeadAddons, "OpenHeadAddons");
        }

        /// <summary>
        /// Finds the open editor window through the WindowStack, the idiom the mod's own
        /// <c>WindowTool.GetWindowOf&lt;T&gt;</c> uses. Lets
        /// <see cref="Shell.CharEditorColorScope"/>, hosted on <c>DialogColorPicker</c> rather than
        /// <c>EditorUI</c>, reach <see cref="IsAlienRace"/> without a second copy of the read.
        /// </summary>
        public static Window FindEditorWindow()
        {
            if (!Ready || editorUIType == null)
                return null;
            foreach (Window w in Find.WindowStack.Windows)
            {
                if (editorUIType.IsInstanceOfType(w))
                    return w;
            }
            return null;
        }
    }
}
