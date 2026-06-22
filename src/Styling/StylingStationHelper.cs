using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>The four style-item tabs plus the apparel-color tab of the styling station.</summary>
    public enum StylingTabKind
    {
        Hair,
        Beard,
        FaceTattoo,
        BodyTattoo,
        ApparelColor
    }

    /// <summary>
    /// Reflection bridge and data helpers for <c>Dialog_StylingStation</c>. The dialog keeps its
    /// working state (target pawn, desired hair color, per-apparel colors, the initial values used
    /// for Reset) in private fields and applies changes through private methods. We drive those
    /// directly so Accept/Reset behave exactly like vanilla's buttons.
    /// </summary>
    public static class StylingStationHelper
    {
        private static readonly Type DialogType = AccessTools.TypeByName("RimWorld.Dialog_StylingStation");
        private static readonly Type TabEnumType = DialogType?.GetNestedType("StylingTab", BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PawnField = AccessTools.Field(DialogType, "pawn");
        private static readonly FieldInfo StationField = AccessTools.Field(DialogType, "stylingStation");
        private static readonly FieldInfo DesiredHairColorField = AccessTools.Field(DialogType, "desiredHairColor");
        private static readonly FieldInfo ApparelColorsField = AccessTools.Field(DialogType, "apparelColors");
        private static readonly FieldInfo CurTabField = AccessTools.Field(DialogType, "curTab");

        private static readonly FieldInfo InitialHairField = AccessTools.Field(DialogType, "initialHairDef");
        private static readonly FieldInfo InitialBeardField = AccessTools.Field(DialogType, "initialBeardDef");
        private static readonly FieldInfo InitialFaceTattooField = AccessTools.Field(DialogType, "initialFaceTattoo");
        private static readonly FieldInfo InitialBodyTattooField = AccessTools.Field(DialogType, "initialBodyTattoo");

        private static readonly PropertyInfo AllHairColorsProp = AccessTools.Property(DialogType, "AllHairColors");
        private static readonly PropertyInfo AllColorsProp = AccessTools.Property(DialogType, "AllColors");

        private static readonly MethodInfo ResetMethod = AccessTools.Method(DialogType, "Reset");
        private static readonly MethodInfo ApplyApparelColorsMethod = AccessTools.Method(DialogType, "ApplyApparelColors");

        // ----- Field access -----

        public static Pawn GetPawn(Window dialog) => PawnField?.GetValue(dialog) as Pawn;
        public static Thing GetStation(Window dialog) => StationField?.GetValue(dialog) as Thing;

        public static Color GetDesiredHairColor(Window dialog) =>
            DesiredHairColorField != null ? (Color)DesiredHairColorField.GetValue(dialog) : Color.white;

        public static void SetDesiredHairColor(Window dialog, Color color) =>
            DesiredHairColorField?.SetValue(dialog, color);

        public static Dictionary<Apparel, Color> GetApparelColors(Window dialog) =>
            ApparelColorsField?.GetValue(dialog) as Dictionary<Apparel, Color>;

        /// <summary>Syncs the dialog's visible tab to ours so a sighted onlooker sees the same panel.</summary>
        public static void SetVisibleTab(Window dialog, StylingTabKind kind)
        {
            if (CurTabField == null || TabEnumType == null) return;
            // Our enum's first five values map 1:1 onto the dialog's StylingTab enum order
            // (Hair, Beard, TattooFace, TattooBody, ApparelColor).
            try { CurTabField.SetValue(dialog, Enum.ToObject(TabEnumType, (int)kind)); }
            catch { /* visual-only; ignore if the enum layout ever diverges */ }
        }

        // ----- Style item lists (mirrors Dialog_StylingStation.DrawStylingItemType filtering) -----

        public static List<StyleItemDef> BuildItems(Pawn pawn, StylingTabKind kind)
        {
            IEnumerable<StyleItemDef> source;
            StyleItemDef initial;

            switch (kind)
            {
                case StylingTabKind.Hair:
                    source = DefDatabase<HairDef>.AllDefs;
                    initial = pawn.story.hairDef;
                    break;
                case StylingTabKind.Beard:
                    source = DefDatabase<BeardDef>.AllDefs;
                    initial = pawn.style.beardDef;
                    break;
                case StylingTabKind.FaceTattoo:
                    source = DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Face);
                    initial = pawn.style.FaceTattoo;
                    break;
                case StylingTabKind.BodyTattoo:
                    source = DefDatabase<TattooDef>.AllDefs.Where(t => t.tattooType == TattooType.Body);
                    initial = pawn.style.BodyTattoo;
                    break;
                default:
                    return new List<StyleItemDef>();
            }

            return source
                .Where(x => PawnStyleItemChooser.WantsToUseStyle(pawn, x) || x == initial)
                .OrderByDescending(x => PawnStyleItemChooser.FrequencyFromGender(x, pawn))
                .ToList();
        }

        /// <summary>The style item currently applied to the pawn for the given tab.</summary>
        public static StyleItemDef GetCurrentItem(Pawn pawn, StylingTabKind kind)
        {
            switch (kind)
            {
                case StylingTabKind.Hair: return pawn.story.hairDef;
                case StylingTabKind.Beard: return pawn.style.beardDef;
                case StylingTabKind.FaceTattoo: return pawn.style.FaceTattoo;
                case StylingTabKind.BodyTattoo: return pawn.style.BodyTattoo;
                default: return null;
            }
        }

        /// <summary>Applies a style item live (mirrors the dialog's per-item selectAction).</summary>
        public static void SelectItem(Pawn pawn, StylingTabKind kind, StyleItemDef item)
        {
            switch (kind)
            {
                case StylingTabKind.Hair: pawn.story.hairDef = (HairDef)item; break;
                case StylingTabKind.Beard: pawn.style.beardDef = (BeardDef)item; break;
                case StylingTabKind.FaceTattoo: pawn.style.FaceTattoo = (TattooDef)item; break;
                case StylingTabKind.BodyTattoo: pawn.style.BodyTattoo = (TattooDef)item; break;
            }
            pawn.Drawer.renderer.SetAllGraphicsDirty();
        }

        // ----- Color sources -----
        //
        // Use the dialog's OWN color lists so we present exactly the swatch set a sighted player
        // sees. The dialog dedups near-identical colors (hair within a 0.15 threshold, apparel by
        // IndistinguishableFrom), collapsing the ~115 raw ColorDefs down to ~70 hair / ~36 apparel.

        /// <summary>The deduped hair color swatches (raw Colors), as the dialog builds them.</summary>
        public static List<Color> GetAllHairColors(Window dialog) =>
            AllHairColorsProp?.GetValue(dialog) as List<Color> ?? new List<Color>();

        /// <summary>The deduped apparel color swatches (raw Colors), as the dialog builds them.</summary>
        public static List<Color> GetAllColors(Window dialog) =>
            AllColorsProp?.GetValue(dialog) as List<Color> ?? new List<Color>();

        // ----- Apply / Reset (reuse the dialog's own private logic) -----

        /// <summary>
        /// Applies all pending changes exactly like the dialog's Accept button: schedules the
        /// styling job (so the pawn walks over and the look change takes effect) when any
        /// hair/beard/tattoo/hair-color changed, applies pending apparel colors, then closes.
        /// </summary>
        public static void Accept(Window dialog)
        {
            Pawn pawn = GetPawn(dialog);
            Thing station = GetStation(dialog);
            if (pawn == null) { dialog.Close(doCloseSound: false); return; }

            bool styleChanged =
                pawn.story.hairDef != (HairDef)InitialHairField?.GetValue(dialog)
                || pawn.style.beardDef != (BeardDef)InitialBeardField?.GetValue(dialog)
                || pawn.style.FaceTattoo != (TattooDef)InitialFaceTattooField?.GetValue(dialog)
                || pawn.style.BodyTattoo != (TattooDef)InitialBodyTattooField?.GetValue(dialog)
                || pawn.story.HairColor != GetDesiredHairColor(dialog);

            if (styleChanged && station != null)
            {
                pawn.style.SetupNextLookChangeData(pawn.story.hairDef, pawn.style.beardDef,
                    pawn.style.FaceTattoo, pawn.style.BodyTattoo, GetDesiredHairColor(dialog));
                // Revert the live preview defs (the scheduled job applies the desired look);
                // resetColors:false keeps the pending apparel colors and desired hair color.
                ResetMethod?.Invoke(dialog, new object[] { false });
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.UseStylingStation, station), JobTag.Misc);
            }

            ApplyApparelColorsMethod?.Invoke(dialog, null);
            dialog.Close(doCloseSound: false);
        }

        /// <summary>Reverts every pending change (mirrors the dialog's Reset button).</summary>
        public static void ResetAll(Window dialog)
        {
            ResetMethod?.Invoke(dialog, new object[] { true });
        }
    }
}
