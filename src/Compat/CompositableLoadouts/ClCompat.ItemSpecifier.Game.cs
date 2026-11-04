using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    internal static partial class ClCompat
    {
        /// <summary>
        /// The private state of the item specifier (<c>Dialog_ItemSpecifier</c>): the member being
        /// edited, the item's shared quality and hit point filter, the stuff list's search text and
        /// allowed stuffs, the preview quality and stuff that pick the statistics columns, and the
        /// stat caches the table draws from.
        /// </summary>
        internal static class ItemSpecifier
        {
            private static readonly FieldInfo itemField;
            private static readonly FieldInfo memberField;
            private static readonly FieldInfo filterField;
            private static readonly FieldInfo stuffPreviewField;
            private static readonly FieldInfo qualityPreviewField;
            private static readonly FieldInfo stuffFilterField;
            private static readonly FieldInfo statScrollField;

            private static readonly FieldInfo armorStatsField;
            private static readonly FieldInfo baseWeaponStatsField;
            private static readonly FieldInfo meleeWeaponStatsField;
            private static readonly FieldInfo rangedWeaponStatsField;
            private static readonly FieldInfo generalItemStatsField;
            private static readonly FieldInfo statCacheField;
            private static readonly FieldInfo tipCacheField;

            private static readonly MethodInfo doWindowContents;
            private static readonly MethodInfo drawMemberSelector;
            private static readonly MethodInfo drawStuffList;
            private static readonly MethodInfo drawPreviewQuality;
            private static readonly MethodInfo drawStats;
            private static readonly MethodInfo drawSliders;

            private static readonly MethodInfo filterHpRangeGetter;
            private static readonly MethodInfo filterQualityRangeGetter;
            private static readonly MethodInfo filterSetHpRange;
            private static readonly MethodInfo filterSetQualityRange;

            private static readonly MethodInfo memberAllowedStuffsGetter;
            private static readonly MethodInfo memberRepresentativeStuffGetter;
            private static readonly ConstructorInfo safeDefCtor;
            private static readonly MethodInfo stuffsContains;
            private static readonly MethodInfo stuffsAdd;
            private static readonly MethodInfo stuffsRemove;

            private static readonly MethodInfo qualityNext;
            private static readonly MethodInfo qualityPrevious;

            private static readonly float rowHeight;
            private static readonly float scrollWidth;

            private static readonly bool ready;

            static ItemSpecifier()
            {
                var surface = new ReflectionSurface("ClCompat.ItemSpecifier");
                Type dialogType = surface.Supplied("Inventory.Dialog_ItemSpecifier", itemSpecifierType);
                Type filterType = surface.Type("Inventory.Filter");
                Type safeDefType = surface.Type("Inventory.SafeDef");

                itemField = surface.Field(dialogType, "item");
                memberField = surface.Field(dialogType, "member");
                filterField = surface.Field(dialogType, "filter");
                stuffPreviewField = surface.Field(dialogType, "stuffPreview");
                qualityPreviewField = surface.Field(dialogType, "qualityPreview");
                stuffFilterField = surface.Field(dialogType, "stuffScrollViewFilter");
                statScrollField = surface.Field(dialogType, "statScrollPosition");

                armorStatsField = surface.Field(dialogType, "armorStats");
                baseWeaponStatsField = surface.Field(dialogType, "baseWeaponStats");
                meleeWeaponStatsField = surface.Field(dialogType, "meleeWeaponStats");
                rangedWeaponStatsField = surface.Field(dialogType, "rangedWeaponStats");
                generalItemStatsField = surface.Field(dialogType, "generalItemStats");
                statCacheField = surface.Field(dialogType, "statCache");
                tipCacheField = surface.Field(dialogType, "tipCache");

                doWindowContents = surface.Method(dialogType, "DoWindowContents", new[] { typeof(Rect) });
                drawMemberSelector = surface.Method(dialogType, "DrawMemberSelector", new[] { typeof(Rect) });
                drawStuffList = surface.Method(dialogType, "DrawStuffSourceScrollableList",
                    new[] { typeof(Rect), typeof(List<ThingDef>), typeof(Vector2).MakeByRefType(), typeof(float).MakeByRefType() });
                drawPreviewQuality = surface.Method(dialogType, "DrawPreviewQuality", new[] { typeof(Rect) });
                drawStats = surface.Method(dialogType, "DrawStats", new[] { typeof(Rect) });
                drawSliders = surface.Method(dialogType, "DrawSliders", new[] { typeof(Rect) });

                filterHpRangeGetter = Getter(surface.Property(filterType, "HpRange"));
                filterQualityRangeGetter = Getter(surface.Property(filterType, "QualityRange"));
                filterSetHpRange = surface.Method(filterType, "SetHpRange", new[] { typeof(FloatRange) });
                filterSetQualityRange = surface.Method(filterType, "SetQualityRange", new[] { typeof(QualityRange) });

                PropertyInfo allowedStuffs = surface.Property(itemMemberType, "AllowedStuffs");
                memberAllowedStuffsGetter = Getter(allowedStuffs);
                memberRepresentativeStuffGetter = Getter(surface.Property(itemMemberType, "RepresentativeStuff"));
                safeDefCtor = surface.Constructor(safeDefType, new[] { typeof(ThingDef) });
                if (allowedStuffs != null && safeDefType != null)
                {
                    Type stuffsType = allowedStuffs.PropertyType;
                    stuffsContains = surface.Method(stuffsType, "Contains", new[] { safeDefType });
                    stuffsAdd = surface.Method(stuffsType, "Add", new[] { safeDefType });
                    stuffsRemove = surface.Method(stuffsType, "Remove", new[] { safeDefType });
                }

                qualityNext = surface.Method(utilityType, "Next", new[] { typeof(QualityCategory) });
                qualityPrevious = surface.Method(utilityType, "Previous", new[] { typeof(QualityCategory) });

                Type uicType = surface.Type("Inventory.UIC");
                rowHeight = Constant(surface.Field(uicType, "SPACED_HEIGHT"));
                scrollWidth = Constant(surface.Field(uicType, "SCROLL_WIDTH"));

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The dialog's row height (<c>UIC.SPACED_HEIGHT</c>): list rows, stat rows and slider spacing.</summary>
            public static float RowHeight { get { return rowHeight; } }

            /// <summary>The width every scroll view reserves for its bar (<c>UIC.SCROLL_WIDTH</c>).</summary>
            public static float ScrollWidth { get { return scrollWidth; } }

            public static MethodInfo DoWindowContents { get { return doWindowContents; } }
            public static MethodInfo DrawMemberSelectorMethod { get { return drawMemberSelector; } }
            public static MethodInfo DrawStuffListMethod { get { return drawStuffList; } }
            public static MethodInfo DrawPreviewQualityMethod { get { return drawPreviewQuality; } }
            public static MethodInfo DrawStatsMethod { get { return drawStats; } }
            public static MethodInfo DrawSlidersMethod { get { return drawSliders; } }

            public static object Item(Window dialog)
            {
                return Guarded.FieldOf<object>(itemField, dialog, "ClCompat.ItemSpecifier.Item");
            }

            public static object Member(Window dialog)
            {
                return Guarded.FieldOf<object>(memberField, dialog, "ClCompat.ItemSpecifier.Member");
            }

            public static ThingDef Def(Window dialog)
            {
                return MemberDef(Member(dialog));
            }

            public static int MemberCount(Window dialog)
            {
                return ItemMembers(Item(dialog)).Count;
            }

            // ---- The shared quality and hit point filter ----

            public static FloatRange HpRange(Window dialog)
            {
                return Guarded.Get(filterHpRangeGetter, Filter(dialog), "ClCompat.ItemSpecifier.HpRange", FloatRange.ZeroToOne);
            }

            public static QualityRange QualityRange(Window dialog)
            {
                return Guarded.Get(filterQualityRangeGetter, Filter(dialog), "ClCompat.ItemSpecifier.QualityRange", RimWorld.QualityRange.All);
            }

            public static void SetHpRange(Window dialog, FloatRange range)
            {
                Guarded.Call(filterSetHpRange, Filter(dialog), "ClCompat.ItemSpecifier.SetHpRange", range);
            }

            public static void SetQualityRange(Window dialog, QualityRange range)
            {
                Guarded.Call(filterSetQualityRange, Filter(dialog), "ClCompat.ItemSpecifier.SetQualityRange", range);
            }

            private static object Filter(Window dialog)
            {
                return Guarded.FieldOf<object>(filterField, dialog, "ClCompat.ItemSpecifier.Filter");
            }

            // ---- Preview quality and stuff ----

            public static QualityCategory PreviewQuality(Window dialog)
            {
                return Guarded.FieldOf(qualityPreviewField, dialog, "ClCompat.ItemSpecifier.PreviewQuality", QualityCategory.Normal);
            }

            /// <summary>The preview arrows' step, through the mod's own clamped Next/Previous.</summary>
            public static QualityCategory StepPreviewQuality(Window dialog, int direction)
            {
                QualityCategory current = PreviewQuality(dialog);
                object stepped = Guarded.Call(direction > 0 ? qualityNext : qualityPrevious, null,
                    "ClCompat.ItemSpecifier.StepPreviewQuality", current);
                if (ready && stepped is QualityCategory next)
                {
                    qualityPreviewField.SetValue(dialog, next); // MUTATION-C: mirrors Dialog_ItemSpecifier.DrawPreviewQuality (the arrow buttons' inline bodies)
                    return next;
                }
                return current;
            }

            public static ThingDef StuffPreview(Window dialog)
            {
                return Guarded.FieldOf<ThingDef>(stuffPreviewField, dialog, "ClCompat.ItemSpecifier.StuffPreview");
            }

            public static void SetStuffPreview(Window dialog, ThingDef stuff)
            {
                if (ready && dialog != null)
                {
                    stuffPreviewField.SetValue(dialog, stuff); // MUTATION-C: mirrors Dialog_ItemSpecifier.DrawStuffSourceScrollableList (a hovered row becomes the preview)
                }
            }

            public static ThingDef RepresentativeStuff(Window dialog)
            {
                return Guarded.Get<ThingDef>(memberRepresentativeStuffGetter, Member(dialog), "ClCompat.ItemSpecifier.RepresentativeStuff");
            }

            public static Vector2 StatScroll(Window dialog)
            {
                return Guarded.FieldOf(statScrollField, dialog, "ClCompat.ItemSpecifier.StatScroll", Vector2.zero);
            }

            // ---- The stuff list ----

            public static string StuffFilter(Window dialog)
            {
                return Guarded.FieldOf(stuffFilterField, dialog, "ClCompat.ItemSpecifier.StuffFilter", "");
            }

            public static void SetStuffFilter(Window dialog, string value)
            {
                if (ready && dialog != null)
                {
                    stuffFilterField.SetValue(dialog, value ?? ""); // MUTATION-C: mirrors Dialog_ItemSpecifier.DrawStuffSourceScrollableList (GUI.TextField writes the field each draw)
                }
            }

            /// <summary>The rows the list draws: the def's allowed stuffs, less those the search text misses, by defName.</summary>
            public static List<ThingDef> VisibleStuffs(Window dialog)
            {
                ThingDef def = Def(dialog);
                if (def == null || !def.MadeFromStuff)
                {
                    return new List<ThingDef>();
                }
                string search = StuffFilter(dialog);
                // The mod lowercases the label but not the search text, so an uppercase letter matches nothing.
                List<ThingDef> stuffs = GenStuff.AllowedStuffsFor(def)
                    .Where(td => td.LabelCap.Resolve().ToLower().Contains(search))
                    .ToList();
                stuffs.SortBy(t => t.defName);
                return stuffs;
            }

            public static bool StuffAllowed(Window dialog, ThingDef stuff)
            {
                object set = AllowedStuffs(dialog);
                object wrapped = Wrap(stuff);
                return set != null && wrapped != null
                    && Guarded.Call(stuffsContains, set, "ClCompat.ItemSpecifier.StuffAllowed", wrapped) is bool allowed && allowed;
            }

            /// <summary>One click on a stuff row's checkbox; returns the new state.</summary>
            public static bool ToggleStuff(Window dialog, ThingDef stuff)
            {
                object set = AllowedStuffs(dialog);
                object wrapped = Wrap(stuff);
                if (set == null || wrapped == null)
                {
                    return false;
                }
                bool allow = !StuffAllowed(dialog, stuff);
                // MUTATION-C: mirrors Dialog_ItemSpecifier.cs:296-302; DraggableCheckbox's result is handled inline in the row loop.
                Guarded.Call(allow ? stuffsAdd : stuffsRemove, set, "ClCompat.ItemSpecifier.ToggleStuff", wrapped);
                return StuffAllowed(dialog, stuff);
            }

            private static object AllowedStuffs(Window dialog)
            {
                return Guarded.Get<object>(memberAllowedStuffsGetter, Member(dialog), "ClCompat.ItemSpecifier.AllowedStuffs");
            }

            private static object Wrap(ThingDef stuff)
            {
                if (!ready || stuff == null)
                {
                    return null;
                }
                try
                {
                    return safeDefCtor.Invoke(new object[] { stuff });
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("ClCompat.ItemSpecifier.Wrap", ex);
                    return null;
                }
            }

            // ---- The statistics table ----

            /// <summary>DrawStats' rows for this def, from the dialog's own stat lists.</summary>
            public static List<StatDef> Stats(ThingDef def)
            {
                if (!ready || def == null)
                {
                    return new List<StatDef>();
                }
                if (def.IsApparel)
                {
                    return StatList(armorStatsField);
                }
                if (def.IsMeleeWeapon)
                {
                    return StatList(baseWeaponStatsField).Union(StatList(meleeWeaponStatsField)).ToList();
                }
                if (def.IsRangedWeapon)
                {
                    return StatList(baseWeaponStatsField).Union(StatList(rangedWeaponStatsField)).ToList();
                }
                return StatList(generalItemStatsField);
            }

            /// <summary>
            /// A cell's value as the table last drew it. False when the column has not been drawn yet;
            /// true with <paramref name="shown"/> false for a stat the table draws as "-".
            /// </summary>
            public static bool TryStatValue(ThingDef def, ThingDef stuff, QualityCategory quality, StatDef stat,
                out bool shown, out float value)
            {
                shown = false;
                value = -1f;
                Dictionary<StatDef, float> values;
                if (!TryCacheRow(statCacheField, def, stuff, quality, out values))
                {
                    return false;
                }
                shown = values.TryGetValue(stat, out value);
                return true;
            }

            public static string StatTip(ThingDef def, ThingDef stuff, QualityCategory quality, StatDef stat)
            {
                Dictionary<StatDef, string> tips;
                string tip;
                return TryCacheRow(tipCacheField, def, stuff, quality, out tips) && tips.TryGetValue(stat, out tip) ? tip : null;
            }

            private static bool TryCacheRow<T>(FieldInfo cacheField, ThingDef def, ThingDef stuff, QualityCategory quality,
                out Dictionary<StatDef, T> row)
            {
                row = null;
                var cache = ready
                    ? Guarded.FieldOf<Dictionary<Tuple<ThingDef, ThingDef, QualityCategory>, Dictionary<StatDef, T>>>(
                        cacheField, null, "ClCompat.ItemSpecifier.StatCache")
                    : null;
                return cache != null && cache.TryGetValue(Tuple.Create(def, stuff, quality), out row) && row != null;
            }

            private static float Constant(FieldInfo field)
            {
                return field != null && field.IsLiteral && field.GetRawConstantValue() is float value ? value : 0f;
            }

            private static List<StatDef> StatList(FieldInfo field)
            {
                return Guarded.FieldOf<List<StatDef>>(field, null, "ClCompat.ItemSpecifier.Stats") ?? new List<StatDef>();
            }
        }
    }
}
