using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RimWorldAccess
{
    internal static partial class CharEditorCompat
    {
        /// <summary>
        /// The mod's own singleton (<c>CEditor.API</c>) and the portrait refresh
        /// (<c>UpdateGraphics()</c>) every facade in this module reaches for. Its own block for the
        /// reason <see cref="CmrCompat.JobBase"/> is one: six facades used to bind these same two
        /// members against the same type, so a rename degraded several surfaces in several
        /// different failure modes, some of them silent. Bound once here, diagnosed once here,
        /// and each facade conjoins <see cref="Ready"/> into its own so its feature still gates
        /// exactly as before.
        ///
        /// Like <see cref="CmrCompat.JobBase"/> this block gates on nothing but its own surface: an
        /// absent mod means an unresolved type, which the surface declines quietly. Deliberately NOT
        /// gated on <see cref="ModPresent"/> -- that property runs the core file's bind, which itself
        /// reads these blocks, and a block resolving mid-init would hand out half-bound state.
        /// </summary>
        internal static class EditorCore
        {
            private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorCompat.EditorCore", Resolve);

            private static Type ceditorType;
            private static MethodInfo apiGetter;
            private static MethodInfo updateGraphicsMethod;

            /// <summary>True when both central members resolved against the loaded assembly.</summary>
            public static bool Ready
            {
                get { return gate.Ensure(); }
            }

            /// <summary>The <c>CEditor</c> type itself, for satellites resolving nested types or enums from it.</summary>
            public static Type CEditorType
            {
                get { gate.Ensure(); return ceditorType; }
            }

            private static bool Resolve(ReflectionSurface surface)
            {
                ceditorType = surface.Type("CharacterEditor.CEditor");
                apiGetter = surface.Property(ceditorType, "API")?.GetGetMethod(true);
                updateGraphicsMethod = surface.Method(ceditorType, "UpdateGraphics", Type.EmptyTypes);

                return surface.Ready;
            }

            /// <summary>The mod's singleton, or null when it has not bootstrapped yet.</summary>
            public static object Api()
            {
                gate.Ensure();
                if (!gate.Ensure() || apiGetter == null)
                    return null;
                try
                {
                    return apiGetter.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Fail("EditorCore.Api", ex);
                    return null;
                }
            }

            /// <summary>Refreshes the portrait the way every one of the mod's own appearance handlers does. False when the call could not be made.</summary>
            public static bool UpdateGraphics()
            {
                object api = Api();
                if (api == null || updateGraphicsMethod == null)
                    return false;
                try
                {
                    updateGraphicsMethod.Invoke(api, null);
                    return true;
                }
                catch (Exception ex)
                {
                    Fail("EditorCore.UpdateGraphics", ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// <c>CharacterEditor.SearchTool</c>, the filter-value holder every browse dialog keeps in
        /// its own <c>search</c> field. Four facades read the same shared slots off it; the
        /// dialog-specific slots (weapon type, apparel layer, thing category) stay with the one
        /// facade that uses them, bound against <see cref="SearchToolType"/>.
        ///
        /// Reads only. Every write to a SearchTool slot rides its dialog's own handler or carries
        /// its own MUTATION-C citation, and stays in the file that owns that dialog.
        /// </summary>
        internal static class Search
        {
            private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorCompat.Search", Resolve);

            private static Type searchToolType;
            private static FieldInfo modNameField;
            private static FieldInfo oFilter1Field;
            private static FieldInfo filter1Field;
            private static FieldInfo filter2Field;

            /// <summary>True when every shared filter slot resolved.</summary>
            public static bool Ready
            {
                get { return gate.Ensure(); }
            }

            /// <summary>The <c>SearchTool</c> type, for facades binding their own dialog-specific slots.</summary>
            public static Type SearchToolType
            {
                get { gate.Ensure(); return searchToolType; }
            }

            private static bool Resolve(ReflectionSurface surface)
            {
                searchToolType = surface.Type("CharacterEditor.SearchTool");
                modNameField = surface.Field(searchToolType, "modName");
                oFilter1Field = surface.Field(searchToolType, "ofilter1");
                filter1Field = surface.Field(searchToolType, "filter1");
                filter2Field = surface.Field(searchToolType, "filter2");

                return surface.Ready;
            }

            /// <summary>The mod-name filter every DialogTemplate row draws.</summary>
            public static string ModName(object search)
            {
                return Read(modNameField, search, "ModName") as string;
            }

            /// <summary>
            /// The typed first filter slot, boxed: AddTrait keeps a <c>StatModifier</c> here,
            /// ChangeRace a bool (its race-specific-dress checkbox), so callers cast.
            /// </summary>
            public static object OFilter1(object search)
            {
                return Read(oFilter1Field, search, "OFilter1");
            }

            /// <summary>The first string filter (AddTrait's category, AddHediff's category).</summary>
            public static string Filter1(object search)
            {
                return Read(filter1Field, search, "Filter1") as string;
            }

            /// <summary>The second string filter (AddHediff's body part).</summary>
            public static string Filter2(object search)
            {
                return Read(filter2Field, search, "Filter2") as string;
            }

            private static object Read(FieldInfo field, object search, string member)
            {
                gate.Ensure();
                if (!gate.Ensure() || field == null || search == null)
                    return null;
                try
                {
                    return field.GetValue(search);
                }
                catch (Exception ex)
                {
                    Fail("Search." + member, ex);
                    return null;
                }
            }
        }

        /// <summary>
        /// <c>CharacterEditor.Label</c>, the mod's own hand-rolled eight-language table for concepts
        /// vanilla has no key for. One type resolution for the whole module, plus the by-name reader
        /// the def-editor pane needs (about thirty labels, far too many for a named FieldInfo each).
        ///
        /// Facades that bind individual labels as readiness participants keep those binds and take
        /// the TYPE from <see cref="LabelType"/>: a renamed table then declines their feature the way
        /// it always did, instead of silently handing every row an empty string.
        /// </summary>
        internal static class Labels
        {
            private static readonly LazyReflectionGate gate = new LazyReflectionGate("CharEditorCompat.Labels", Resolve);

            private static Type labelType;
            private static readonly Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();

            /// <summary>True when the label table itself resolved.</summary>
            public static bool Ready
            {
                get { return gate.Ensure(); }
            }

            public static Type LabelType
            {
                get { gate.Ensure(); return labelType; }
            }

            private static bool Resolve(ReflectionSurface surface)
            {
                labelType = surface.Type("CharacterEditor.Label");
                return surface.Ready;
            }

            /// <summary>
            /// One label read live by field name and cached per name, empty when the table or the
            /// name is absent. Raw per-name lookups rather than surface binds: the name set is the
            /// caller's, not a fixed list, so each missing name warns once on its own.
            /// </summary>
            public static string Get(string fieldName)
            {
                gate.Ensure();
                if (labelType == null)
                    return "";
                if (!fieldCache.TryGetValue(fieldName, out FieldInfo field))
                {
                    field = AccessTools.Field(labelType, fieldName);
                    fieldCache[fieldName] = field;
                    if (field == null)
                        ModLogger.Warning("CharEditorCompat.Labels: Label." + fieldName + " could not be resolved.");
                }
                try
                {
                    return field?.GetValue(null) as string ?? "";
                }
                catch (Exception ex)
                {
                    Fail("Labels.Get:" + fieldName, ex);
                    return "";
                }
            }
        }
    }
}
